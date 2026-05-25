#!/bin/bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

TFM="net10.0"
BUILD_DIR="bin/Release/$TFM"

info() { echo -e "\033[1;36m$1\033[0m"; }

clean() { rm -rf obj; }

build_one() {
    local proj=$1
    clean
    dotnet restore "$proj" --nologo
    dotnet build "$proj" -c Release -f "$TFM" --no-restore --nologo
}

info "Cleaning..."
rm -rf bin dist ./nudge ./nudge-notify ./nudge-tray

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
