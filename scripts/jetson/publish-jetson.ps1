param(
    [string]$Configuration = "Release",
    [string]$Runtime = "linux-arm64",
    [string]$Output = "artifacts/jetson/publish",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$project = Join-Path $repoRoot "src\VideoInference.Jetson\VideoInference.Jetson.csproj"
$outputPath = Join-Path $repoRoot $Output
$selfContainedValue = if ($SelfContained.IsPresent) { "true" } else { "false" }

Write-Host "Publishing VideoInference.Jetson"
Write-Host "  Project: $project"
Write-Host "  Runtime: $Runtime"
Write-Host "  Configuration: $Configuration"
Write-Host "  Output: $outputPath"

if (Test-Path $outputPath)
{
    Remove-Item -LiteralPath $outputPath -Recurse -Force
}

$publishArgs = @(
    "publish",
    $project,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", $selfContainedValue,
    "-o", $outputPath
)

& dotnet @publishArgs

$deployDir = Join-Path $repoRoot "deploy\jetson"
if (Test-Path $deployDir)
{
    Copy-Item -Path (Join-Path $deployDir "*") -Destination $outputPath -Recurse -Force
}

Write-Host
Write-Host "Publish completed."
Write-Host "Next steps:"
Write-Host "  1. Copy $outputPath to Jetson."
Write-Host "  2. Review deploy/jetson/videoinference-jetson.env.example."
Write-Host "  3. Install the systemd template if you want auto-start."
