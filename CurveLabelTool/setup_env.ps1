param(
    [string]$PythonExe = "python",
    [string]$VenvDir = ".venv",
    [switch]$UseLock,
    [switch]$UpgradePip
)

$ErrorActionPreference = "Stop"

$scriptDir = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptDir)) {
    $scriptDir = (Get-Location).Path
}

$venvPath = if ([System.IO.Path]::IsPathRooted($VenvDir)) {
    $VenvDir
} else {
    Join-Path $scriptDir $VenvDir
}

$venvPython = Join-Path $venvPath "Scripts\python.exe"
$requirements = Join-Path $scriptDir "requirements.txt"
$requirementsLock = Join-Path $scriptDir "requirements-lock.txt"

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Action
    )

    Write-Host $Message
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE"
    }
}

if (!(Test-Path $venvPython)) {
    Invoke-Checked "Creating virtual environment: $venvPath" {
        & $PythonExe -m venv $venvPath
    }
}

if (!(Test-Path $venvPython)) {
    throw "Virtual environment was not created successfully: $venvPython"
}

if ($UpgradePip) {
    Invoke-Checked "Upgrading pip" {
        & $venvPython -m pip install --upgrade pip
    }
}

if ($UseLock -and (Test-Path $requirementsLock)) {
    Invoke-Checked "Installing locked dependencies from requirements-lock.txt" {
        & $venvPython -m pip install -r $requirementsLock
    }
} else {
    Invoke-Checked "Installing dependencies from requirements.txt" {
        & $venvPython -m pip install -r $requirements
    }
}

Write-Host ""
Write-Host "Environment is ready."
Write-Host "Run command: .\\run.ps1"
