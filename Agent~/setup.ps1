# Installs everything the assistant needs, unattended: the .NET SDK (into .dotnet\ if no .NET 10 SDK is installed),
# the models (into models\), and the server (built into bin\). Unity runs this automatically the first time the
# assistant appears; you can also run it yourself.
#   ./setup.ps1                 CPU build
#   ./setup.ps1 -Backend Cuda12 NVIDIA GPU build (or Vulkan)
# Progress goes to install-status.txt as "step|message" lines for the editor to show.
param([string]$Backend = "Cpu")
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # Invoke-WebRequest is very slow with the progress bar
Set-Location $PSScriptRoot
function Status($step, $message) { Set-Content -Path "install-status.txt" -Value "$step|$message"; Write-Host "== $message" }

function HasSdk10($exe) {
    try { return [bool]((& $exe --list-sdks 2>$null) | Where-Object { $_ -match '^10\.' }) } catch { return $false }
}

function Download($url, $file) {
    # curl.exe ships with Windows 10+ and resumes; Invoke-WebRequest is the fallback.
    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($curl) {
        & $curl.Source -L --fail -sS -C - -o $file $url
        if ($LASTEXITCODE -ne 0) { throw "Download failed: $url" }
    } else {
        Invoke-WebRequest -Uri $url -OutFile $file
    }
}

try {
    # 1. .NET 10 SDK: the installed one, else a private copy.
    Status dotnet "Checking for .NET"
    $dotnet = $null
    $installed = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($installed -and (HasSdk10 $installed.Source)) { $dotnet = $installed.Source }
    elseif ((Test-Path ".dotnet\dotnet.exe") -and (HasSdk10 ".dotnet\dotnet.exe")) { $dotnet = (Resolve-Path ".dotnet\dotnet.exe").Path }
    else {
        Status dotnet "Installing .NET 10 (private copy)"
        Download "https://dot.net/v1/dotnet-install.ps1" "dotnet-install.ps1"
        & .\dotnet-install.ps1 -Channel 10.0 -InstallDir .dotnet -NoPath
        Remove-Item dotnet-install.ps1
        $dotnet = (Resolve-Path ".dotnet\dotnet.exe").Path
    }
    Set-Content -Path "dotnet-path.txt" -Value $dotnet
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"; $env:DOTNET_NOLOGO = "1"

    # 2. Models.
    New-Item -ItemType Directory -Force models | Out-Null
    function Fetch($url, $file) {
        if (Test-Path "models/$file") { return }
        Status download "Downloading $file"
        Download $url "models/$file.part"
        Move-Item "models/$file.part" "models/$file"
    }
    Fetch "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf" "qwen2.5-1.5b-instruct-q4_k_m.gguf"
    Fetch "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin" "ggml-base.en.bin"
    if (-not (Test-Path "models/kokoro-en-v0_19/model.onnx")) {
        Fetch "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/kokoro-en-v0_19.tar.bz2" "kokoro-en-v0_19.tar.bz2"
        Status extract "Unpacking the voice model"
        tar -xjf models/kokoro-en-v0_19.tar.bz2 -C models   # tar ships with Windows 10+
        if ($LASTEXITCODE -ne 0) { throw "Couldn't unpack the voice model" }
        Remove-Item models/kokoro-en-v0_19.tar.bz2
    }

    # 3. The server.
    Status build "Building the assistant ($Backend)"
    & $dotnet build SchematicsAgent/SchematicsAgent.csproj -c Release -p:LlmBackend=$Backend -o bin
    if ($LASTEXITCODE -ne 0) { throw "The build failed" }
    Status done "Ready"
}
catch {
    Status error "Setup failed: $($_.Exception.Message) (see setup.log)"
    exit 1
}
