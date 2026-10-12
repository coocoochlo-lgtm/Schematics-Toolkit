# Installs everything the assistant needs, unattended: the .NET SDK (into .dotnet\ if no .NET 10 SDK is installed),
# the models (into models\), and the server (built into bin\). Unity runs this automatically the first time the
# assistant appears; you can also run it yourself.
#   ./setup.ps1                              CPU build, installed here in Agent~
#   ./setup.ps1 -Backend Cuda12              NVIDIA GPU build (or Vulkan)
#   ./setup.ps1 -InstallDir D:\Sprocket       install on another drive: .NET, models, server, the NuGet packages the
#                                            build downloads, and temporary files all go there
# Progress goes to install-status.txt as "step|message" lines for the editor to show.
param([string]$Backend = "Cpu", [string]$InstallDir = "")
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # Invoke-WebRequest is very slow with the progress bar
Set-Location $PSScriptRoot
$Agent = $PSScriptRoot
function Status($step, $message) { Set-Content -Path (Join-Path $Agent "install-status.txt") -Value "$step|$message"; Write-Host "== $message" }

# Where the big things go: here, or the folder chosen in Unity (Preferences > Schematics Assistant > Install Folder).
if ($InstallDir -eq "") { $InstallDir = $Agent }
New-Item -ItemType Directory -Force $InstallDir | Out-Null
$InstallDir = (Resolve-Path $InstallDir).Path
if ($InstallDir -ne $Agent) {
    # The build's packages (LLamaSharp's native libraries are large) and downloads' temporary files, off the system drive.
    $env:NUGET_PACKAGES = Join-Path $InstallDir "nuget"
    $env:TEMP = Join-Path $InstallDir "tmp"; $env:TMP = $env:TEMP
    New-Item -ItemType Directory -Force $env:TEMP | Out-Null
}
$DotnetDir = Join-Path $InstallDir ".dotnet"
$Models = Join-Path $InstallDir "models"
$Bin = Join-Path $InstallDir "bin"

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
    elseif ((Test-Path "$DotnetDir\dotnet.exe") -and (HasSdk10 "$DotnetDir\dotnet.exe")) { $dotnet = "$DotnetDir\dotnet.exe" }
    else {
        Status dotnet "Installing .NET 10 (private copy)"
        $installer = Join-Path $InstallDir "dotnet-install.ps1"
        Download "https://dot.net/v1/dotnet-install.ps1" $installer
        & $installer -Channel 10.0 -InstallDir $DotnetDir -NoPath
        Remove-Item $installer
        $dotnet = "$DotnetDir\dotnet.exe"
    }
    Set-Content -Path (Join-Path $Agent "dotnet-path.txt") -Value $dotnet
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"; $env:DOTNET_NOLOGO = "1"

    # 2. Models.
    New-Item -ItemType Directory -Force $Models | Out-Null
    function Fetch($url, $file) {
        if (Test-Path "$Models/$file") { return }
        Status download "Downloading $file"
        Download $url "$Models/$file.part"
        Move-Item "$Models/$file.part" "$Models/$file"
    }
    Fetch "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf" "qwen2.5-1.5b-instruct-q4_k_m.gguf"
    Fetch "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin" "ggml-base.en.bin"
    if (-not (Test-Path "$Models/kokoro-en-v0_19/model.onnx")) {
        Fetch "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/kokoro-en-v0_19.tar.bz2" "kokoro-en-v0_19.tar.bz2"
        Status extract "Unpacking the voice model"
        tar -xjf "$Models/kokoro-en-v0_19.tar.bz2" -C "$Models"   # tar ships with Windows 10+
        if ($LASTEXITCODE -ne 0) { throw "Couldn't unpack the voice model" }
        Remove-Item "$Models/kokoro-en-v0_19.tar.bz2"
    }

    # 3. The server.
    Status build "Building the assistant ($Backend)"
    & $dotnet build SchematicsAgent/SchematicsAgent.csproj -c Release -p:LlmBackend=$Backend -o $Bin
    if ($LASTEXITCODE -ne 0) { throw "The build failed" }
    Status done "Ready"
}
catch {
    Status error "Setup failed: $($_.Exception.Message) (see setup.log)"
    exit 1
}
