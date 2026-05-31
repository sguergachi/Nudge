#!/bin/bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

TFM="net10.0"
BUILD_DIR="bin/Release/$TFM"

PYTHON_RUNTIME_VERSION="3.11.15"
PYTHON_RUNTIME_DATE="20260510"
PYTHON_RUNTIME_URL="https://github.com/astral-sh/python-build-standalone/releases/download/${PYTHON_RUNTIME_DATE}/cpython-${PYTHON_RUNTIME_VERSION}+${PYTHON_RUNTIME_DATE}-x86_64-unknown-linux-gnu-install_only.tar.gz"
PYTHON_RUNTIME_DIR="python-runtime"

info() { echo -e "\033[1;36m$1\033[0m"; }

clean() { rm -rf obj; }

build_one() {
    local proj=$1
    clean
    dotnet restore "$proj" --nologo
    dotnet build "$proj" -c Release -f "$TFM" --no-restore --nologo
}

bundle_python() {
    if [ -f "$PYTHON_RUNTIME_DIR/bin/python3" ]; then
        info "Bundled Python already present, skipping download."
        return 0
    fi
    info "Downloading bundled Python runtime..."
    local tmp_tar="$(mktemp)"
    curl -fSL --progress-bar -o "$tmp_tar" "$PYTHON_RUNTIME_URL"
    mkdir -p "$PYTHON_RUNTIME_DIR"
    tar -xzf "$tmp_tar" --strip-components=1 -C "$PYTHON_RUNTIME_DIR"
    rm -f "$tmp_tar"
    chmod +x "$PYTHON_RUNTIME_DIR/bin/python3" 2>/dev/null || true
    info "Bundled Python ready: $PYTHON_RUNTIME_DIR/bin/python3"
}

info "Cleaning..."
rm -rf bin dist ./nudge ./nudge-notify ./nudge-tray

if [ "${1:-}" = "--bundle-python" ] || [ "${1:-}" = "-p" ]; then
    bundle_python
fi

info "Building..."
build_one nudge.csproj
build_one nudge-notify.csproj
build_one nudge-tray.csproj

info "Copying binaries..."
cp "$BUILD_DIR/nudge" ./nudge 2>/dev/null && chmod +x ./nudge
cp "$BUILD_DIR/nudge-notify" ./nudge-notify && chmod +x ./nudge-notify
cp "$BUILD_DIR/nudge-tray" ./nudge-tray && chmod +x ./nudge-tray
cp "$BUILD_DIR"/*.dll ./

info "Build complete."
