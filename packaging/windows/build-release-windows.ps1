<#
.SYNOPSIS
    Builds a complete Windows release locally — no GitHub Actions required.

.DESCRIPTION
    Produces the same artifact layout the CI pipeline does, so a locally-built installer is
    interchangeable with a CI-built one:

        <OutputRoot>\<rid>\
            SubZeroFramework.exe, *.dll, Assets\ ...          <- the WinUI app  (INSTALLFOLDER in the MSI)
            service-package\windows\SubZeroFramework.Service.exe  <- the packaged service helper
        <OutputRoot>\SubZeroFramework-Setup-<version>-<arch>.msi   <- WiX v7 MSI installer

    That service-package\windows path is load-bearing: it is exactly where the app's helper discovery
    looks, so PackagedHelperAvailable only becomes true when the layout is right. Getting it wrong
    produces an installer that appears to work while Settings -> Service stays greyed out.

    Windows ships from net10.0-windows10.0.26100 (WinUI 3) ONLY. net10.0-desktop is the Linux head and
    is deliberately not built here.

.PARAMETER Arch
    x64 or arm64. Defaults to the current machine's architecture.

.PARAMETER Version
    Version to stamp on the app, the service, and the installer filename. Defaults to <Version> from
    Directory.Build.props so a bare run still produces a coherently-stamped build.

.PARAMETER SkipInstaller
    Publish the payload but do not build the MSI (useful when iterating, or if the WiX CLI is not
    installed — see the warning in the installer stage for the one-time WiX setup commands).

.EXAMPLE
    .\packaging\windows\build-release-windows.ps1
    .\packaging\windows\build-release-windows.ps1 -Arch arm64 -Version 0.1.0
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')]
    [string]$Arch = $(if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }),
    [string]$Version = '',
    [string]$OutputRoot = '',
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$rid = "win-$Arch"
$tfm = 'net10.0-windows10.0.26100'

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\windows"
}

# Fall back to the single shared <Version> so local builds are never stamped inconsistently.
if ([string]::IsNullOrWhiteSpace($Version)) {
    $props = Get-Content (Join-Path $repoRoot 'Directory.Build.props') -Raw
    if ($props -match '<Version>([^<]+)</Version>') {
        $Version = $Matches[1].Trim()
    }
    else {
        throw "Could not read <Version> from Directory.Build.props. Pass -Version explicitly."
    }
}

$payloadDir = Join-Path $OutputRoot $rid

Write-Host "SubZero Framework - local Windows release" -ForegroundColor Cyan
Write-Host "  version : $Version"
Write-Host "  arch    : $Arch ($rid)"
Write-Host "  output  : $payloadDir"
Write-Host ""

if (Test-Path $payloadDir) {
    Write-Host "Clearing previous payload..." -ForegroundColor DarkGray
    Remove-Item $payloadDir -Recurse -Force
}
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null

# ── 1. The WinUI app ──────────────────────────────────────────────────────────────────────────────
# Uses the checked-in publish profile so a local build cannot drift from CI (notably PublishTrimmed,
# which is OFF because a trimmed build was measured crashing at startup).
Write-Host "[1/3] Publishing the app ($tfm, $rid)..." -ForegroundColor Cyan
$appProject = Join-Path $repoRoot 'SubZeroFramework\SubZeroFramework.csproj'
# Build each -p:… as a single pre-interpolated string. A parenthesised expression next to `-p:Name=`
# becomes a SEPARATE argument in PowerShell, which MSBuild then reads as a second project path
# (error MSB1008) while the property itself arrives empty.
$appProfile = Join-Path $repoRoot "SubZeroFramework\Properties\PublishProfiles\$rid.pubxml"
& dotnet publish $appProject `
    -c Release `
    -f $tfm `
    "-p:PublishProfile=$appProfile" `
    "-p:PublishDir=$payloadDir\" `
    "-p:Version=$Version" `
    "-p:InformationalVersion=$Version" `
    /v:minimal `
    /consoleloggerparameters:NoSummary
if ($LASTEXITCODE -ne 0) { throw "App publish failed ($LASTEXITCODE)." }

# ── 2. The service, into the layout the app's discovery expects ───────────────────────────────────
Write-Host "[2/3] Publishing the service..." -ForegroundColor Cyan
& (Join-Path $repoRoot 'SubZeroFramework.Service\Scripts\package-windows-service.ps1') `
    -RuntimeIdentifier $rid `
    -OutputRoot $payloadDir `
    -Configuration Release `
    -Version $Version
if ($LASTEXITCODE -ne 0) { throw "Service publish failed ($LASTEXITCODE)." }

$serviceExe = Join-Path $payloadDir 'service-package\windows\SubZeroFramework.Service.exe'
if (-not (Test-Path $serviceExe)) {
    throw "Service executable missing at '$serviceExe'. The installer would produce a build whose in-app service management stays disabled."
}

# ── 3. Installer ──────────────────────────────────────────────────────────────────────────────────
if ($SkipInstaller) {
    Write-Host "[3/3] Skipped (-SkipInstaller)." -ForegroundColor Yellow
    Write-Host "`nPayload ready: $payloadDir" -ForegroundColor Green
    return
}

# WiX v7 (MSI). Migrated from Inno Setup 2026-07-22: Inno 6.5+ is non-commercial-only without a paid
# license; WiX v7's Open Source Maintenance Fee applies only above USD $10k annual revenue, so this
# project uses it free after a one-time `wix eula accept wix7`.
$wix = Get-Command 'wix' -ErrorAction SilentlyContinue
if (-not $wix) {
    Write-Warning "WiX CLI not found - skipping the installer. Install with 'dotnet tool install --global wix', run 'wix eula accept wix7', then 'wix extension add -g WixToolset.Util.wixext' and 'wix extension add -g WixToolset.UI.wixext'. Or re-run with -SkipInstaller to silence this."
    Write-Host "`nPayload ready: $payloadDir" -ForegroundColor Green
    return
}

Write-Host "[3/3] Building MSI with WiX..." -ForegroundColor Cyan

# The .wxs harvests the payload with a <Files> glob, which cannot EXCLUDE. The two executables are
# authored explicitly there (the service exe carries ServiceInstall/ServiceControl; the app exe carries
# the shortcut and launch checkbox), so they must not also arrive via the glob — that would be a
# duplicate-file validation error. Stage a copy of the payload with exactly those two removed.
$stagingDir = Join-Path $OutputRoot "msi-staging-$rid"
if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
# /E (not /MIR): the staging dir is deleted above, so mirroring semantics add nothing — and /E has no
# delete behavior. robocopy exit codes 0-7 are success variants; only >= 8 is failure.
& robocopy $payloadDir $stagingDir /E /NFL /NDL /NJH /NJS /XF 'SubZeroFramework.exe' 'SubZeroFramework.Service.exe' | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Staging the MSI payload failed (robocopy exit $LASTEXITCODE)." }
$global:LASTEXITCODE = 0

# WixUI's license page needs RTF; generate it from LICENSE.txt so the license has one source of truth.
$licenseRtf = Join-Path (Split-Path $stagingDir -Parent) 'License.rtf'
$licenseText = Get-Content (Join-Path $repoRoot 'LICENSE.txt') -Raw
$escaped = $licenseText.Replace('\', '\\').Replace('{', '\{').Replace('}', '\}')
$rtfBody = ($escaped -split "\r?\n") -join '\par '
[IO.File]::WriteAllText($licenseRtf, '{\rtf1\ansi\deff0{\fonttbl{\f0 Consolas;}}\fs18 ' + $rtfBody + '}')

$installer = Join-Path $OutputRoot "SubZeroFramework-Setup-$Version-$Arch.msi"
& wix build (Join-Path $PSScriptRoot 'subzeroframework.wxs') `
    -arch $Arch `
    -d "Version=$Version" `
    -d "PayloadDir=$stagingDir" `
    -d "FullPayloadDir=$payloadDir" `
    -d "LicenseRtf=$licenseRtf" `
    -ext WixToolset.Util.wixext `
    -ext WixToolset.UI.wixext `
    -o $installer
if ($LASTEXITCODE -ne 0) { throw "WiX build failed ($LASTEXITCODE)." }

Remove-Item $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $licenseRtf -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  payload   : $payloadDir"
if (Test-Path $installer) {
    Write-Host ("  installer : {0} ({1:N1} MB)" -f $installer, ((Get-Item $installer).Length / 1MB))
}
Write-Host ""
Write-Host "NOTE: this installer is unsigned - SmartScreen will warn on another machine." -ForegroundColor Yellow
