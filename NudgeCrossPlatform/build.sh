#!/bin/bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# Paths
TFM="net10.0"
BUILD_DIR="bin/Release/$TFM"

# 1. Clean
info() { echo -e "\033[1;36m$1\033[0m"; }
info "Cleaning..."
rm -rf bin obj dist ./nudge ./nudge-notify ./nudge-tray

# 2. Build (Parallelized)
info "Building..."
# Restore once
dotnet restore nudge.csproj --nologo
dotnet restore nudge-notify.csproj --nologo
dotnet restore nudge-tray.csproj --nologo

# Build in parallel
dotnet build nudge.csproj -c Release -f "$TFM" --no-restore --nologo -m &
dotnet build nudge-notify.csproj -c Release -f "$TFM" --no-restore --nologo -m &
dotnet build nudge-tray.csproj -c Release -f "$TFM" --no-restore --nologo -m &
wait

# 3. Explicit Copy to root (avoiding collision)
info "Copying binaries..."
cp "$BUILD_DIR/nudge" ./nudge
chmod +x ./nudge
cp "$BUILD_DIR/nudge-notify" ./nudge-notify
chmod +x ./nudge-notify
cp "$BUILD_DIR/nudge-tray" ./nudge-tray
chmod +x ./nudge-tray

# Copy necessary dlls
cp "$BUILD_DIR"/*.dll ./

info "Build complete."
