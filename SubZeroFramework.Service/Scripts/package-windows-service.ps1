param(
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputRoot = (Join-Path (Get-Location) "artifacts"),
    [string]$Configuration = "Release",
    # Product version to stamp. Leave empty to fall back to <Version> in Directory.Build.props.
    # CI must pass the SAME value it stamps on the UI and the installer, otherwise one installer ships
    # a service and a UI reporting different versions (and two copies of SubZeroFramework.Core.dll at
    # different versions), because this script is the ONLY place the service is ever built.
    [string]$Version = ""
)

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$serviceRoot = Resolve-Path (Join-Path $scriptDirectory "..")
$outputDirectory = Join-Path $OutputRoot "service-package/windows"
$projectPath = Join-Path $serviceRoot "SubZeroFramework.Service.csproj"

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

# Append the version properties only when a value was supplied. A bare `-p:Version=` is an MSBuild
# GLOBAL property: it overrides Directory.Build.props and stamps an empty/invalid version, which is
# strictly worse than the honest fallback.
$versionArgs = @()
if (-not [string]::IsNullOrWhiteSpace($Version))
{
    $versionArgs = @("-p:Version=$Version", "-p:InformationalVersion=$Version")
}

& dotnet publish $projectPath `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    -o $outputDirectory `
    @versionArgs `
    /property:GenerateFullPaths=true `
    /v:minimal `
    /consoleloggerparameters:NoSummary

if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}
