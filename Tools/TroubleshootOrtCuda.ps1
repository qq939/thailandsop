param(
    [string]$Config = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$logDir = Join-Path $root "Logs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$logPath = Join-Path $logDir "TroubleshootOrtCuda.log"

function Log($msg) {
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff') $msg"
    $line | Tee-Object -FilePath $logPath -Append
}

Log "=== ORT CUDA Troubleshoot ==="
Log "Root: $root"
Log "Config: $Config"
Log "OS: $([System.Runtime.InteropServices.RuntimeInformation]::OSDescription)"
Log "Arch: $([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture) | 64-bit: $([Environment]::Is64BitProcess)"
Log ".NET: $([System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription)"

$cudaPath = $env:CUDA_PATH
Log "CUDA_PATH: $cudaPath"

$cudaRoot = "C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA"
$cudaBin = ""
if (-not [string]::IsNullOrWhiteSpace($cudaPath)) {
    $candidate = Join-Path $cudaPath "bin"
    if (Test-Path $candidate) { $cudaBin = $candidate }
}
if ([string]::IsNullOrWhiteSpace($cudaBin) -and (Test-Path $cudaRoot)) {
    $versions = Get-ChildItem $cudaRoot -Directory -Filter "v*" | Sort-Object Name -Descending
    if ($versions.Count -gt 0) {
        $candidate = Join-Path $versions[0].FullName "bin"
        if (Test-Path $candidate) { $cudaBin = $candidate }
    }
}
Log "CUDA bin: $cudaBin (exists=$((Test-Path $cudaBin)))"

# cuDNN bin discovery (NVIDIA distribution layout)
$cudnnBin = ""
$cudnnRoot = "C:\Program Files\NVIDIA\CUDNN"
if (Test-Path $cudnnRoot) {
    $cudaMajor = ""
    if (-not [string]::IsNullOrWhiteSpace($cudaBin)) {
        $cudaDir = Split-Path -Parent $cudaBin
        $name = Split-Path -Leaf $cudaDir
        if ($name -match '^v(\d+)') { $cudaMajor = $matches[1] }
    }

    $bins = Get-ChildItem $cudnnRoot -Directory -Filter "v*" | ForEach-Object {
        $binRoot = Join-Path $_.FullName "bin"
        if (Test-Path $binRoot) { Get-ChildItem $binRoot -Directory -ErrorAction SilentlyContinue }
    } | Where-Object { $_ }

    if ($bins.Count -gt 0) {
        if (-not [string]::IsNullOrWhiteSpace($cudaMajor)) {
            $match = $bins | Where-Object { $_.Name -like "$cudaMajor*" } | Sort-Object Name -Descending | Select-Object -First 1
            if ($match) { $cudnnBin = $match.FullName }
        }
        if ([string]::IsNullOrWhiteSpace($cudnnBin)) {
            $cudnnBin = ($bins | Sort-Object Name -Descending | Select-Object -First 1).FullName
        }
    }
}
Log "cuDNN bin: $cudnnBin (exists=$((Test-Path $cudnnBin)))"

$nativeDir = Join-Path $root "bin\$Config\net8.0-windows\runtimes\win-x64\native"
if (-not (Test-Path $nativeDir)) {
    $nativeDir = Join-Path $root "bin\Debug\net8.0-windows\runtimes\win-x64\native"
}
Log "ORT native dir: $nativeDir (exists=$((Test-Path $nativeDir)))"

$providerDlls = @(
    "onnxruntime.dll",
    "onnxruntime_providers_shared.dll",
    "onnxruntime_providers_cuda.dll"
)
foreach ($dll in $providerDlls) {
    $path = Join-Path $nativeDir $dll
    Log "ORT: $dll => $([System.IO.File]::Exists($path))"
}

$cudaDlls = @(
    "cudart64_12.dll",
    "cudart64_110.dll",
    "cublas64_12.dll",
    "cublas64_11.dll",
    "cublasLt64_12.dll",
    "cublasLt64_11.dll",
    "cufft64_11.dll",
    "cufft64_10.dll"
)
foreach ($dll in $cudaDlls) {
    $path = if ($cudaBin) { Join-Path $cudaBin $dll } else { "" }
    Log "CUDA: $dll => $([System.IO.File]::Exists($path))"
}

$cudnnDlls = @(
    "cudnn64_9.dll",
    "cudnn_ops64_9.dll",
    "cudnn_cnn64_9.dll",
    "cudnn_adv64_9.dll",
    "cudnn64_8.dll",
    "cudnn_ops_infer64_8.dll",
    "cudnn_cnn_infer64_8.dll"
)
foreach ($dll in $cudnnDlls) {
    $path = if ($cudnnBin) { Join-Path $cudnnBin $dll } else { "" }
    Log "cuDNN: $dll => $([System.IO.File]::Exists($path))"
}

# Show PATH hints
$pathHints = ($env:PATH -split ';') | Where-Object { $_ -match 'cuda|cudnn|onnxruntime' }
Log "PATH (filtered):"
foreach ($p in $pathHints) { Log "  $p" }

Log "Done. Log saved: $logPath"
