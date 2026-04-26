#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_FILE="${SCRIPT_DIR}/nudge-tray.csproj"
BUILD_DIR="${SCRIPT_DIR}/bin/Debug/net10.0"
APP_HOST="${BUILD_DIR}/nudge-tray"

needs_build=0
if [[ ! -x "${APP_HOST}" ]]; then
    needs_build=1
elif [[ "${SCRIPT_DIR}/nudge-tray.cs" -nt "${APP_HOST}" ]] || [[ "${SCRIPT_DIR}/AnalyticsWindow.cs" -nt "${APP_HOST}" ]] || [[ "${PROJECT_FILE}" -nt "${APP_HOST}" ]]; then
    needs_build=1
fi

if [[ ${needs_build} -eq 1 ]]; then
    dotnet build "${PROJECT_FILE}" --nologo >/dev/null
fi

exec "${APP_HOST}" "$@"
