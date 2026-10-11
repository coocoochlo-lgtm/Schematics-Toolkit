# Downloads the assistant's models into Agent~/models and builds the server into Agent~/bin.
#   ./setup.ps1                 CPU build
#   ./setup.ps1 -Backend Cuda12 NVIDIA GPU build (or Vulkan)
param([string]$Backend = "Cpu")
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
New-Item -ItemType Directory -Force models | Out-Null
$ProgressPreference = "SilentlyContinue"   # Invoke-WebRequest is very slow with the progress bar

function Fetch($url, $file) {
    if (Test-Path "models/$file") { Write-Host "  have $file"; return }
    Write-Host "  downloading $file"
    Invoke-WebRequest -Uri $url -OutFile "models/$file.part"
    Move-Item "models/$file.part" "models/$file"
}

Write-Host "Models:"
Fetch "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf" "qwen2.5-1.5b-instruct-q4_k_m.gguf"
Fetch "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin" "ggml-base.en.bin"
if (-not (Test-Path "models/kokoro-en-v0_19")) {
    Fetch "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/kokoro-en-v0_19.tar.bz2" "kokoro-en-v0_19.tar.bz2"
    tar -xjf models/kokoro-en-v0_19.tar.bz2 -C models   # tar ships with Windows 10+
    Remove-Item models/kokoro-en-v0_19.tar.bz2
}

Write-Host "Server ($Backend):"
dotnet build SchematicsAgent/SchematicsAgent.csproj -c Release -p:LlmBackend=$Backend -o bin
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Write-Host "Done. Unity starts it from Tools > Remedy > Schematics > Assistant."
