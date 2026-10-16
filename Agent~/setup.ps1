# Installs everything the assistant needs, unattended: the .NET SDK (into .dotnet\ if no .NET 10 SDK is installed),
# the models (into models\), and the server (built into bin\). Unity runs this automatically the first time the
# assistant appears; you can also run it yourself.
#   ./setup.ps1                              CPU build, installed here in Agent~
#   ./setup.ps1 -Backend Cuda12              NVIDIA GPU build (or Vulkan)
#   ./setup.ps1 -InstallDir D:\Sprocket       install on another drive: .NET, models, server, the NuGet packages the
#                                            build downloads, and temporary files all go there
# Progress goes to install-status.txt as "step|message" lines for the editor to show.
param([string]$Backend = "Cpu", [string]$InstallDir = "", [string]$Llm = "qwen2.5-1.5b-instruct-q4_k_m.gguf")
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # Invoke-WebRequest is very slow with the progress bar
Set-Location $PSScriptRoot
$Agent = $PSScriptRoot
function Status($step, $message) { Set-Content -Path (Join-Path $Agent "install-status.txt") -Value "$step|$message"; Write-Host "== $message" }

# Where the big things go: here, or the folder chosen in Unity (Preferences > Schematics Assistant > Install Folder).
if ($InstallDir -eq "") { $InstallDir = $Agent }
New-Item -ItemType Directory -Force $InstallDir | Out-Null
$InstallDir = (Resolve-Path $InstallDir).Path
# Everything the build downloads goes here too, never to the user profile on C: (it may be full): NuGet's packages,
# its download cache (v3-cache) and plugin cache, .NET's first-run files, and temporary files.
$env:NUGET_PACKAGES = Join-Path $InstallDir "nuget\packages"
$env:NUGET_HTTP_CACHE_PATH = Join-Path $InstallDir "nuget\http-cache"
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $InstallDir "nuget\plugins-cache"
$env:DOTNET_CLI_HOME = Join-Path $InstallDir "nuget\cli-home"
$env:TEMP = Join-Path $InstallDir "tmp"; $env:TMP = $env:TEMP
New-Item -ItemType Directory -Force $env:TEMP, $env:DOTNET_CLI_HOME | Out-Null
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
    # The language model: the standard 1.5B, or the 0.5B that slow CPUs can keep up with.
    if ($Llm -like "qwen2.5-0.5b*") { $repo = "Qwen2.5-0.5B-Instruct-GGUF" } else { $repo = "Qwen2.5-1.5B-Instruct-GGUF" }
    Fetch "https://huggingface.co/Qwen/$repo/resolve/main/$Llm" $Llm
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
    # A server still running from this folder locks its files, and the build can't replace them.
    try {
        Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
            Where-Object { $_.CommandLine -and $_.CommandLine -like "*$Bin*SchematicsAgent.dll*" } |
            ForEach-Object { Write-Host "Stopping the running server (process $($_.ProcessId))"; Stop-Process -Id $_.ProcessId -Force }
        Start-Sleep -Milliseconds 500
    } catch { Write-Host "Couldn't check for a running server: $($_.Exception.Message)" }
    $BuildLog = Join-Path $Agent "build.log"
    & $dotnet build SchematicsAgent/SchematicsAgent.csproj -c Release -p:LlmBackend=$Backend -o $Bin "-flp:LogFile=$BuildLog;Verbosity=minimal"
    if ($LASTEXITCODE -ne 0) {
        # The first error, so Unity can say what went wrong.
        $first = Get-Content $BuildLog -ErrorAction SilentlyContinue | Where-Object { $_ -match ': error ' } | Select-Object -First 1
        if ($first) { throw "The build failed: $($first.Trim())" }
        throw "The build failed"
    }
    Status done "Ready"
}
catch {
    Status error "Setup failed: $($_.Exception.Message) (see setup.log)"
    exit 1
}
