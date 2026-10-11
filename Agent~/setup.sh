#!/usr/bin/env bash
# Installs everything the assistant needs, unattended: the .NET SDK (into .dotnet/ if no .NET 10 SDK is installed),
# the models (into models/), and the server (built into bin/). Unity runs this automatically the first time the
# assistant appears; you can also run it yourself.
#   ./setup.sh            CPU build
#   ./setup.sh Cuda12     NVIDIA GPU build (or Vulkan)
# Progress goes to install-status.txt as "step|message" lines for the editor to show.
set -euo pipefail
cd "$(dirname "$0")"
BACKEND="${1:-Cpu}"
STATUS="install-status.txt"
status() { echo "$1|$2" > "$STATUS"; echo "== $2"; }
trap 'status error "Setup failed at line $LINENO (see setup.log)"' ERR

# 1. .NET 10 SDK: the installed one, else a private copy.
status dotnet "Checking for .NET"
DOTNET=""
if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  DOTNET="$(command -v dotnet)"
elif [ -x .dotnet/dotnet ] && .dotnet/dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  DOTNET="$PWD/.dotnet/dotnet"
else
  status dotnet "Installing .NET 10 (private copy)"
  curl -sSL --fail -o dotnet-install.sh https://dot.net/v1/dotnet-install.sh
  bash dotnet-install.sh --channel 10.0 --install-dir .dotnet --no-path
  rm -f dotnet-install.sh
  DOTNET="$PWD/.dotnet/dotnet"
fi
echo "$DOTNET" > dotnet-path.txt
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# 2. Models.
mkdir -p models
fetch() { # url file
  if [ -f "models/$2" ]; then return; fi
  status download "Downloading $2"
  curl -L --fail -sS -C - -o "models/$2.part" "$1"
  mv "models/$2.part" "models/$2"
}
fetch "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf" "qwen2.5-1.5b-instruct-q4_k_m.gguf"
fetch "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin" "ggml-base.en.bin"
if [ ! -f models/kokoro-en-v0_19/model.onnx ]; then
  fetch "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/kokoro-en-v0_19.tar.bz2" "kokoro-en-v0_19.tar.bz2"
  status extract "Unpacking the voice model"
  tar -xjf models/kokoro-en-v0_19.tar.bz2 -C models
  rm models/kokoro-en-v0_19.tar.bz2
fi

# 3. The server.
status build "Building the assistant ($BACKEND)"
"$DOTNET" build SchematicsAgent/SchematicsAgent.csproj -c Release -p:LlmBackend="$BACKEND" -o bin
status done "Ready"
