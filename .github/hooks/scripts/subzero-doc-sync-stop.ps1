$null = [Console]::In.ReadToEnd()

function Write-HookOutput {
    param(
        [bool]$Continue = $true,
        [string]$SystemMessage = ""
    )

    $payload = [ordered]@{
        continue = $Continue
    }

    if (-not [string]::IsNullOrWhiteSpace($SystemMessage)) {
        $payload.systemMessage = $SystemMessage
    }

    $payload | ConvertTo-Json -Compress
}

try {
    $repoRoot = git rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repoRoot)) {
        Write-HookOutput
        exit 0
    }

    Push-Location $repoRoot
    try {
        $statusLines = @(git status --porcelain=v1 2>$null)
        if ($LASTEXITCODE -ne 0 -or $statusLines.Count -eq 0) {
            Write-HookOutput
            exit 0
        }

        $changedPaths = foreach ($line in $statusLines) {
            if ([string]::IsNullOrWhiteSpace($line) -or $line.Length -lt 4) {
                continue
            }

            $pathText = $line.Substring(3).Trim()
            if ($pathText.Contains(' -> ')) {
                $pathText = ($pathText -split ' -> ')[-1].Trim()
            }

            if (-not [string]::IsNullOrWhiteSpace($pathText)) {
                $pathText
            }
        }

        if ($changedPaths.Count -eq 0) {
            Write-HookOutput
            exit 0
        }

        $substantivePaths = @($changedPaths | Where-Object {
            $extension = [System.IO.Path]::GetExtension($_)
            $extension -notin @('.md', '.txt')
        })

        if ($substantivePaths.Count -eq 0) {
            Write-HookOutput
            exit 0
        }

        $requiredDocs = @(
            'docs/ReleasePlan.md',
            '.github/copilot-instructions.md'
        )

        $missingDocs = @($requiredDocs | Where-Object { $_ -notin $changedPaths })
        if ($missingDocs.Count -eq 0) {
            Write-HookOutput
            exit 0
        }

        $preview = ($substantivePaths | Select-Object -First 6) -join ', '
        $systemMessage = "Documentation sync reminder: substantive repo changes are present ($preview). Before closing this SubZero Maintainer session, run SubZero Documentation Sync and update the missing canonical docs: $($missingDocs -join ', '). Update any other affected markdown files too."
        Write-HookOutput -SystemMessage $systemMessage
        exit 0
    }
    finally {
        Pop-Location
    }
}
catch {
    Write-HookOutput -SystemMessage "Documentation sync hook warning: $($_.Exception.Message)"
    exit 0
}
