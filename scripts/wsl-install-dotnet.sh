#!/usr/bin/env bash
set -euo pipefail
if command -v dotnet >/dev/null 2>&1 && [[ "$(dotnet --version 2>/dev/null | cut -d. -f1)" == "9" ]]; then
  dotnet --version
  exit 0
fi
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 9.0 --install-dir "$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"
grep -q '.dotnet' "$HOME/.bashrc" 2>/dev/null || echo 'export PATH=$HOME/.dotnet:$PATH' >> "$HOME/.bashrc"
"$HOME/.dotnet/dotnet" --version
