#!/usr/bin/env bash
# Downloads the assistant's models into Agent~/models and builds the server into Agent~/bin.
#   ./setup.sh            CPU build
#   ./setup.sh Cuda12     NVIDIA GPU build (or Vulkan)
set -euo pipefail
cd "$(dirname "$0")"
BACKEND="${1:-Cpu}"
mkdir -p models

fetch() { # url file
  if [ -f "models/$2" ]; then echo "  have $2"; return; fi
  echo "  downloading $2"
  curl -L --fail --progress-bar -o "models/$2.part" "$1"
  mv "models/$2.part" "models/$2"
}

echo "Models:"
fetch "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf" "qwen2.5-1.5b-instruct-q4_k_m.gguf"
fetch "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin" "ggml-base.en.bin"
if [ ! -d models/kokoro-en-v0_19 ]; then
  fetch "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/kokoro-en-v0_19.tar.bz2" "kokoro-en-v0_19.tar.bz2"
  tar -xjf models/kokoro-en-v0_19.tar.bz2 -C models
  rm models/kokoro-en-v0_19.tar.bz2
fi

echo "Server ($BACKEND):"
dotnet build SchematicsAgent/SchematicsAgent.csproj -c Release -p:LlmBackend="$BACKEND" -o bin
echo "Done. Unity starts it from Tools > Remedy > Schematics > Assistant."
