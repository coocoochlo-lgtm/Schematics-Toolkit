#!/usr/bin/env bash
set -euo pipefail

echo "CleanupCode"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOLUTION="$SCRIPT_DIR/../REDox.slnx"
JB_CMD="jb"
JB_GLOBAL="$HOME/.dotnet/tools/jb"

if ! command -v jb >/dev/null 2>&1; then
  echo "jb command was not found. Installing JetBrains ReSharper GlobalTools..."

  if ! command -v dotnet >/dev/null 2>&1; then
    echo "ERROR: dotnet command was not found. Please install .NET SDK first."
    exit 1
  fi

  if ! dotnet tool install -g JetBrains.ReSharper.GlobalTools; then
    echo "Install failed or the tool is already installed. Trying update..."
    dotnet tool update -g JetBrains.ReSharper.GlobalTools
  fi

  if [[ -x "$JB_GLOBAL" ]]; then
    JB_CMD="$JB_GLOBAL"
  else
    echo "WARNING: jb was installed, but $JB_GLOBAL was not found or not executable."
    echo "Please make sure ~/.dotnet/tools is added to PATH."
  fi
fi

"$JB_CMD" cleanupcode "$SOLUTION"
