$ErrorActionPreference = "Stop"

$scriptDir = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptDir)) {
    $scriptDir = (Get-Location).Path
}

$venvPython = Join-Path $scriptDir ".venv\Scripts\python.exe"
$streamlitExe = Join-Path $scriptDir ".venv\Scripts\streamlit.exe"
$appPath = Join-Path $scriptDir "app.py"

if (!(Test-Path $venvPython) -or !(Test-Path $streamlitExe)) {
    throw "Virtual environment not found. Run .\\setup_env.ps1 first."
}

& $streamlitExe run $appPath --server.fileWatcherType poll --server.port 8501
if ($LASTEXITCODE -ne 0) {
    throw "Streamlit exited with code $LASTEXITCODE"
}
