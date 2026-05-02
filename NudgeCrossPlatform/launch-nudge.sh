#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DLL_PATH="${SCRIPT_DIR}/bin/Release/net10.0/nudge-tray.dll"

# Build if missing or out of date
if [[ ! -f "${DLL_PATH}" ]]; then
    dotnet build "${SCRIPT_DIR}/nudge-tray.csproj" -c Release --nologo >/dev/null
fi

exec dotnet "${DLL_PATH}" "$@"
