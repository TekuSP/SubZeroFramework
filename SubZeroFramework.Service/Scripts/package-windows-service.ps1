param(
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputRoot = (Join-Path (Get-Location) "artifacts"),
    [string]$Configuration = "Release"
)

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$serviceRoot = Resolve-Path (Join-Path $scriptDirectory "..")
$outputDirectory = Join-Path $OutputRoot "service-package/windows"
$projectPath = Join-Path $serviceRoot "SubZeroFramework.Service.csproj"

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

& dotnet publish $projectPath `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    -o $outputDirectory `
    /property:GenerateFullPaths=true `
    /v:minimal `
    /consoleloggerparameters:NoSummary

if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}
