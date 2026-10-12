#!/usr/bin/env bash
# Installs everything the assistant needs, unattended: the .NET SDK (into .dotnet/ if no .NET 10 SDK is installed),
# the models (into models/), and the server (built into bin/). Unity runs this automatically the first time the
# assistant appears; you can also run it yourself.
#   ./setup.sh                        CPU build, installed here in Agent~
#   ./setup.sh Cuda12                 NVIDIA GPU build (or Vulkan)
#   ./setup.sh Cpu /mnt/data/sprocket install on another drive: .NET, models, server, the NuGet packages the build
#                                     downloads, and temporary files all go there
# Progress goes to install-status.txt as "step|message" lines for the editor to show.
set -euo pipefail
cd "$(dirname "$0")"
AGENT="$PWD"
BACKEND="${1:-Cpu}"
INSTALL="${2:-$AGENT}"
mkdir -p "$INSTALL"
INSTALL="$(cd "$INSTALL" && pwd)"
if [ "$INSTALL" != "$AGENT" ]; then
  # The build's packages and downloads' temporary files, off the system drive too.
  export NUGET_PACKAGES="$INSTALL/nuget" TMPDIR="$INSTALL/tmp"
  mkdir -p "$TMPDIR"
fi
DOTNET_DIR="$INSTALL/.dotnet"
MODELS="$INSTALL/models"
STATUS="$AGENT/install-status.txt"
status() { echo "$1|$2" > "$STATUS"; echo "== $2"; }
trap 'status error "Setup failed at line $LINENO (see setup.log)"' ERR

# 1. .NET 10 SDK: the installed one, else a private copy.
status dotnet "Checking for .NET"
DOTNET=""
if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  DOTNET="$(command -v dotnet)"
elif [ -x "$DOTNET_DIR/dotnet" ] && "$DOTNET_DIR/dotnet" --list-sdks 2>/dev/null | grep -q '^10\.'; then
  DOTNET="$DOTNET_DIR/dotnet"
else
  status dotnet "Installing .NET 10 (private copy)"
  curl -sSL --fail -o "$INSTALL/dotnet-install.sh" https://dot.net/v1/dotnet-install.sh
  bash "$INSTALL/dotnet-install.sh" --channel 10.0 --install-dir "$DOTNET_DIR" --no-path
  rm -f "$INSTALL/dotnet-install.sh"
  DOTNET="$DOTNET_DIR/dotnet"
fi
echo "$DOTNET" > "$AGENT/dotnet-path.txt"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# 2. Models.
mkdir -p "$MODELS"
fetch() { # url file
  if [ -f "$MODELS/$2" ]; then return; fi
  status download "Downloading $2"
  curl -L --fail -sS -C - -o "$MODELS/$2.part" "$1"
  mv "$MODELS/$2.part" "$MODELS/$2"
}
fetch "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf" "qwen2.5-1.5b-instruct-q4_k_m.gguf"
fetch "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin" "ggml-base.en.bin"
if [ ! -f "$MODELS/kokoro-en-v0_19/model.onnx" ]; then
  fetch "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/kokoro-en-v0_19.tar.bz2" "kokoro-en-v0_19.tar.bz2"
  status extract "Unpacking the voice model"
  tar -xjf "$MODELS/kokoro-en-v0_19.tar.bz2" -C "$MODELS"
  rm "$MODELS/kokoro-en-v0_19.tar.bz2"
fi

# 3. The server.
status build "Building the assistant ($BACKEND)"
# Stop a server still running from this folder before replacing its files.
pkill -f "$INSTALL/bin/SchematicsAgent.dll" 2>/dev/null && sleep 0.5 || true
if ! "$DOTNET" build SchematicsAgent/SchematicsAgent.csproj -c Release -p:LlmBackend="$BACKEND" -o "$INSTALL/bin" "-flp:LogFile=$AGENT/build.log;Verbosity=minimal"; then
  status error "The build failed: $(grep -m1 ': error ' "$AGENT/build.log" | sed 's/^ *//') (see setup.log)"
  exit 1
fi
status done "Ready"
