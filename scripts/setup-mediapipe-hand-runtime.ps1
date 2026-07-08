[CmdletBinding()]
param(
    [switch]$ForceRecreate,
    [switch]$SkipModelDownload,
    [switch]$SkipBuild,
    [string]$PipIndexUrl = 'https://pypi.tuna.tsinghua.edu.cn/simple',
    [string]$PipTrustedHost = 'pypi.tuna.tsinghua.edu.cn'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $repoRoot '.runtime'
$mambaRoot = Join-Path $runtimeRoot 'micromamba'
$bootstrapDir = Join-Path $mambaRoot 'bootstrap'
$micromambaExe = Join-Path $bootstrapDir 'micromamba.exe'
$envPrefix = Join-Path $mambaRoot 'envs\\mediapipe-hand-py311'
$pythonExe = Join-Path $envPrefix 'python.exe'
$environmentSpec = Join-Path $repoRoot 'workers\\mediapipe_hand\\environment.yml'
$requirementsFile = Join-Path $repoRoot 'workers\\mediapipe_hand\\requirements.txt'
$taskFilePath = Join-Path $repoRoot 'DL\\MediaPipeHand\\hand_landmarker.task'
$taskFileUrl = 'https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task'
$desktopProject = Join-Path $repoRoot 'src\\VideoInference.Desktop\\VideoInference.Desktop.csproj'
$micromambaDownloadUrl = 'https://micro.mamba.pm/api/micromamba/win-64/latest'
$micromambaArchivePath = Join-Path $bootstrapDir 'micromamba.tar.bz2'
$micromambaExtractDir = Join-Path $bootstrapDir '_extract'

function Write-Step([string]$message) {
    Write-Host "==> $message" -ForegroundColor Cyan
}

function Ensure-Directory([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) {
        New-Item -ItemType Directory -Path $path | Out-Null
    }
}

function Ensure-Micromamba {
    Ensure-Directory $bootstrapDir
    if (Test-Path -LiteralPath $micromambaExe) {
        return
    }

    Write-Step "Downloading micromamba bootstrap"
    Invoke-WebRequest -UseBasicParsing -Uri $micromambaDownloadUrl -OutFile $micromambaArchivePath

    if (Test-Path -LiteralPath $micromambaExtractDir) {
        Remove-Item -LiteralPath $micromambaExtractDir -Recurse -Force
    }

    Ensure-Directory $micromambaExtractDir
    tar -xjf $micromambaArchivePath -C $micromambaExtractDir
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to extract micromamba bootstrap archive."
    }

    $downloadedExe = Get-ChildItem -Path $micromambaExtractDir -Filter 'micromamba.exe' -Recurse -File | Select-Object -First 1
    if ($null -eq $downloadedExe) {
        throw "micromamba bootstrap archive did not contain micromamba.exe"
    }

    Copy-Item -LiteralPath $downloadedExe.FullName -Destination $micromambaExe -Force
    Remove-Item -LiteralPath $micromambaArchivePath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $micromambaExtractDir -Recurse -Force -ErrorAction SilentlyContinue
}

function Invoke-Micromamba([string[]]$arguments) {
    & $micromambaExe @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "micromamba failed with exit code $LASTEXITCODE"
    }
}

function Test-PythonRuntime([string]$pythonPath) {
    if (-not (Test-Path -LiteralPath $pythonPath)) {
        return $false
    }

    & $pythonPath -c "import select, ssl, pip; print('python_runtime_ok')"
    return $LASTEXITCODE -eq 0
}

function Ensure-Environment {
    $shouldCreate = $ForceRecreate -or -not (Test-Path -LiteralPath $pythonExe)

    if (-not $shouldCreate -and -not (Test-PythonRuntime $pythonExe)) {
        Write-Step "Detected incomplete MediaPipe environment; recreating"
        $shouldCreate = $true
    }

    if ($shouldCreate -and (Test-Path -LiteralPath $envPrefix)) {
        Write-Step "Removing existing MediaPipe environment"
        Remove-Item -LiteralPath $envPrefix -Recurse -Force
    }

    if ($shouldCreate) {
        Write-Step "Creating micromamba environment"
        Invoke-Micromamba @(
            'create',
            '--yes',
            '--root-prefix', $mambaRoot,
            '--prefix', $envPrefix,
            '--file', $environmentSpec
        )
    }
    else {
        Write-Step "Reusing existing micromamba environment"
    }

    Write-Step "Installing Python dependencies"
    Invoke-PipWithRetry @('-m', 'pip', 'install', '--upgrade', 'pip', '-i', $PipIndexUrl, '--trusted-host', $PipTrustedHost)
    Invoke-PipWithRetry @('-m', 'pip', 'install', '--prefer-binary', '--retries', '10', '--timeout', '120', '-i', $PipIndexUrl, '--trusted-host', $PipTrustedHost, '-r', $requirementsFile)
}

function Ensure-TaskModel {
    if ($SkipModelDownload) {
        return
    }

    Write-Step "Downloading hand_landmarker.task"
    Invoke-WebRequest -UseBasicParsing -Uri $taskFileUrl -OutFile $taskFilePath
}

function Test-Environment {
    Write-Step "Validating MediaPipe worker environment"
    & $pythonExe -c "import mediapipe, numpy; import cv2; print('mediapipe_ok')"
    if ($LASTEXITCODE -ne 0) {
        throw "MediaPipe environment validation failed with exit code $LASTEXITCODE"
    }
}

function Invoke-PipWithRetry([string[]]$arguments) {
    $maxAttempts = 3
    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        & $pythonExe @arguments
        if ($LASTEXITCODE -eq 0) {
            return
        }

        if ($attempt -eq $maxAttempts) {
            throw "pip command failed after $attempt attempts with exit code $LASTEXITCODE"
        }

        Write-Host "pip attempt $attempt failed, retrying..." -ForegroundColor Yellow
        Start-Sleep -Seconds (5 * $attempt)
    }
}

function Build-DesktopApp {
    if ($SkipBuild) {
        return
    }

    Write-Step "Building desktop application"
    dotnet build $desktopProject -v minimal
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE"
    }
}

Write-Step "Preparing local MediaPipe runtime under $runtimeRoot"
Ensure-Micromamba
Ensure-Environment
Ensure-TaskModel
Test-Environment
Build-DesktopApp
Write-Host ''
Write-Host "MediaPipe runtime is ready." -ForegroundColor Green
Write-Host "VIDEOINFERENCE_MEDIAPIPE_PYTHON=$pythonExe"
Write-Host "VIDEOINFERENCE_MEDIAPIPE_TASK_FILE=$taskFilePath"
