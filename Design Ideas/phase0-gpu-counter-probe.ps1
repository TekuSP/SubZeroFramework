# Phase 0 probe for the GPU/NPU utilization feature (Design Ideas/gpu-npu-utilization-plan.md).
#
# THE QUESTION: the reader is meant to live in SubZeroFramework.Service, which runs as LocalSystem in
# session 0. Windows GPU counter instances are per-process. If session 0 cannot see the instances belonging
# to interactive processes, the service can only ever report a fraction of real GPU/NPU load, and the whole
# Windows design has to move to the (unprivileged) client instead.
#
# WHAT IT DOES: measures the counters twice — once as you, once as SYSTEM via a temporary scheduled task —
# and compares. Read-only: it only reads performance counters. The scheduled task is deleted in a finally
# block, including on Ctrl+C.
#
# RUN FROM AN ELEVATED POWERSHELL (creating a SYSTEM task requires admin):
#     powershell -ExecutionPolicy Bypass -File "Design Ideas\phase0-gpu-counter-probe.ps1"

$ErrorActionPreference = 'Stop'
$taskName = 'SubZeroPhase0GpuProbe'
$workDir  = Join-Path $env:TEMP 'subzero-phase0'
$inner    = Join-Path $workDir 'probe-inner.ps1'
$result   = Join-Path $workDir 'result.txt'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
        ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'This must run from an ELEVATED PowerShell (it registers a task that runs as SYSTEM).' -ForegroundColor Red
    exit 1
}

New-Item -ItemType Directory -Force -Path $workDir | Out-Null
Remove-Item $result -ErrorAction SilentlyContinue

# The body that runs twice: as the current user, and as SYSTEM. Counts instances, groups them by adapter,
# and times a native PDH collect with a PERSISTENT query (the shape the service would actually use).
$body = @'
$out = @()
$out += "identity : " + [Security.Principal.WindowsIdentity]::GetCurrent().Name
$out += "session  : " + [Diagnostics.Process]::GetCurrentProcess().SessionId
try {
    $paths = (Get-Counter -ListSet 'GPU Engine').PathsWithInstances
    $out += "instances: " + $paths.Count

    $byAdapter = $paths | ForEach-Object {
        if ($_ -match '(luid_0x[0-9A-Fa-f]+_0x[0-9A-Fa-f]+_phys_\d+)') { $matches[1] }
    } | Group-Object | Sort-Object Name
    foreach ($a in $byAdapter) { $out += ("  {0}  x{1}" -f $a.Name, $a.Count) }

    $src = @"
using System;
using System.Runtime.InteropServices;
public static class PdhProbe {
    [DllImport("pdh.dll", CharSet=CharSet.Unicode)] static extern uint PdhOpenQueryW(string s, IntPtr u, out IntPtr q);
    [DllImport("pdh.dll", CharSet=CharSet.Unicode)] static extern uint PdhAddEnglishCounterW(IntPtr q, string p, IntPtr u, out IntPtr c);
    [DllImport("pdh.dll")] static extern uint PdhCollectQueryData(IntPtr q);
    [DllImport("pdh.dll", CharSet=CharSet.Unicode)] static extern uint PdhGetFormattedCounterArrayW(IntPtr c, uint f, ref uint sz, out uint n, IntPtr b);
    public static string Run(string path, int iterations) {
        IntPtr q, c;
        if (PdhOpenQueryW(null, IntPtr.Zero, out q) != 0) return "PdhOpenQuery failed";
        if (PdhAddEnglishCounterW(q, path, IntPtr.Zero, out c) != 0) return "PdhAddEnglishCounter failed";
        PdhCollectQueryData(q); System.Threading.Thread.Sleep(1000);
        var sw = new System.Diagnostics.Stopwatch(); uint items = 0;
        for (int i = 0; i < iterations; i++) {
            sw.Start(); PdhCollectQueryData(q);
            uint size = 0, n = 0;
            PdhGetFormattedCounterArrayW(c, 0x200, ref size, out n, IntPtr.Zero);
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try { PdhGetFormattedCounterArrayW(c, 0x200, ref size, out n, buf); items = n; }
            finally { Marshal.FreeHGlobal(buf); }
            sw.Stop(); System.Threading.Thread.Sleep(150);
        }
        return string.Format("pdh      : {0} items, {1:N2} ms/collect", items, sw.Elapsed.TotalMilliseconds / iterations);
    }
}
"@
    Add-Type -TypeDefinition $src -Language CSharp | Out-Null
    $out += [PdhProbe]::Run('\GPU Engine(*)\Utilization Percentage', 10)
}
catch { $out += "FAILED   : " + $_.Exception.Message }
$out
'@

try {
    Write-Host "`n=== AS YOU (interactive session) ===" -ForegroundColor Cyan
    $mine = & ([scriptblock]::Create($body))
    $mine | ForEach-Object { "  $_" }

    # Same body, written to disk so the SYSTEM task can run it; it appends its output to $result.
    Set-Content -Path $inner -Encoding UTF8 -Value ("& { $body } | Set-Content -Path '$result' -Encoding UTF8")

    $ps = (Get-Command powershell.exe).Source
    schtasks /create /tn $taskName /f /sc once /st 00:00 /ru SYSTEM /rl HIGHEST `
        /tr "`"$ps`" -NoProfile -ExecutionPolicy Bypass -File `"$inner`"" | Out-Null
    schtasks /run /tn $taskName | Out-Null

    Write-Host "`n=== AS SYSTEM (session 0) ===" -ForegroundColor Cyan
    $deadline = (Get-Date).AddSeconds(45)
    while (-not (Test-Path $result) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
    if (-not (Test-Path $result)) { throw 'The SYSTEM task produced no output within 45s.' }
    Start-Sleep -Seconds 1
    $theirs = Get-Content $result
    $theirs | ForEach-Object { "  $_" }

    # ---- verdict -------------------------------------------------------------------------------------
    $mineCount   = [int](($mine   | Where-Object { $_ -match '^instances: (\d+)' } | ForEach-Object { $matches[1] }) | Select-Object -First 1)
    $systemCount = [int](($theirs | Where-Object { $_ -match '^instances: (\d+)' } | ForEach-Object { $matches[1] }) | Select-Object -First 1)

    Write-Host "`n=== VERDICT ===" -ForegroundColor Cyan
    Write-Host ("  interactive : {0} instances" -f $mineCount)
    Write-Host ("  SYSTEM      : {0} instances" -f $systemCount)

    if ($systemCount -eq 0) {
        Write-Host '  BLOCKED: session 0 sees nothing. The reader cannot live in the service.' -ForegroundColor Red
    }
    elseif ($systemCount -ge [Math]::Floor($mineCount * 0.8)) {
        Write-Host '  PASS: session 0 sees the counters. Phase 1 proceeds as planned.' -ForegroundColor Green
    }
    else {
        Write-Host '  PARTIAL: session 0 sees noticeably fewer instances. Check whether the MISSING ones are' -ForegroundColor Yellow
        Write-Host '  the interactive apps doing the actual work - if so the service under-reports load.' -ForegroundColor Yellow
    }
    Write-Host "`n  Also compare the two 'pdh' lines: cost should stay ~1-2 ms/collect as SYSTEM.`n"
}
finally {
    schtasks /delete /tn $taskName /f 2>$null | Out-Null
    Remove-Item $workDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host 'Cleaned up (scheduled task + temp files removed).' -ForegroundColor DarkGray
}
