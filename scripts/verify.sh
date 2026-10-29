#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

if ! command -v dotnet >/dev/null 2>&1 && [[ -x "$HOME/.dotnet/dotnet" ]]; then
  export PATH="$HOME/.dotnet:$PATH"
fi
if ! command -v dotnet >/dev/null 2>&1 || [[ "$(dotnet --version)" != 10.* ]]; then
  printf 'Install the .NET 10 SDK and add dotnet to PATH.\n' >&2
  exit 1
fi

dotnet test NAVDataApi.slnx --configuration Release
node --test tests/frontend/*.test.mjs
