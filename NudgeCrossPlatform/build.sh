#!/bin/bash
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# Nudge Build Script
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
#
# Compiles Nudge productivity tracker with obsessive attention to detail.
# Jon Blow style: No projects, no ceremony, just compile the code.
#
# Usage:
#   ./build.sh           # Build with auto-detected compiler
#   ./build.sh --clean   # Clean before building
#
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

set -e

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# ANSI COLORS
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

RESET='\033[0m'
BOLD='\033[1m'
DIM='\033[2m'

RED='\033[31m'
GREEN='\033[32m'
YELLOW='\033[33m'
CYAN='\033[36m'

BRED='\033[1;31m'
BGREEN='\033[1;32m'
BYELLOW='\033[1;33m'
BCYAN='\033[1;36m'

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# HELPER FUNCTIONS
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

success() {
    echo -e "${BGREEN}$1${RESET}"
}

info() {
    echo -e "${CYAN}$1${RESET}"
}

warning() {
    echo -e "${BYELLOW}$1${RESET}"
}

error() {
    echo -e "${BRED}$1${RESET}"
}

dim() {
    echo -e "${DIM}$1${RESET}"
}

separator() {
    echo -e "${BCYAN}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${RESET}"
}

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# BANNER
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

echo
separator
echo -e "  ${BOLD}Nudge Build System${RESET}"
echo -e "  ${DIM}Building productivity tracker...${RESET}"
separator
echo

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# PARSE ARGUMENTS
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

CLEAN=false
if [ "$1" == "--clean" ] || [ "$1" == "-c" ]; then
    CLEAN=true
fi

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# CLEAN (if requested)
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

if [ "$CLEAN" = true ]; then
    info "Cleaning build artifacts..."
    rm -rf bin obj *.csproj *.exe *.dll nudge nudge-notify 2>/dev/null || true
    success "✓ Clean complete"
    echo
fi

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# DETECT COMPILER
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

info "Detecting C# compiler..."
echo

if command -v dotnet &> /dev/null; then
    COMPILER="dotnet"
    VERSION=$(dotnet --version)
    success "✓ Found .NET SDK"
    dim "  Version: ${VERSION}"
elif command -v csc &> /dev/null; then
    COMPILER="mono"
    success "✓ Found Mono C# Compiler"
else
    error "✗ No C# compiler found"
    error ""
    error "Please install either:"
    error "  - .NET SDK 8.0+:  https://dot.net"
    error "  - Mono:           https://www.mono-project.com"
    exit 1
fi

echo

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# BUILD
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

if [ "$COMPILER" = "dotnet" ]; then
    info "Building with .NET..."
    echo

    # Create minimal project files on the fly (Jon Blow style: generate when needed)
    cat > nudge.csproj << 'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>Nudge</RootNamespace>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="nudge.cs" />
  </ItemGroup>
</Project>
EOF

    cat > nudge-notify.csproj << 'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>NudgeNotify</RootNamespace>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="nudge-notify.cs" />
  </ItemGroup>
</Project>
EOF

    dim "  Building nudge..."
    dotnet build nudge.csproj -c Release -v quiet --nologo
    success "  ✓ nudge"

    dim "  Building nudge-notify..."
    dotnet build nudge-notify.csproj -c Release -v quiet --nologo
    success "  ✓ nudge-notify"

    # Copy binaries to root for easy access
    cp bin/Release/net8.0/nudge ./ 2>/dev/null || true
    cp bin/Release/net8.0/nudge-notify ./ 2>/dev/null || true

elif [ "$COMPILER" = "mono" ]; then
    info "Building with Mono..."
    echo

    dim "  Compiling nudge.cs..."
    csc -out:nudge nudge.cs -r:System.Net.Sockets.dll > /dev/null
    success "  ✓ nudge"

    dim "  Compiling nudge-notify.cs..."
    csc -out:nudge-notify nudge-notify.cs > /dev/null
    success "  ✓ nudge-notify"
fi

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# VERIFY BINARIES
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

echo
info "Verifying binaries..."

if [ -f "nudge" ] && [ -x "nudge" ] || [ -f "nudge.exe" ]; then
    NUDGE_SIZE=$(du -h nudge 2>/dev/null | cut -f1 || du -h nudge.exe | cut -f1)
    success "✓ nudge (${NUDGE_SIZE})"
else
    error "✗ nudge binary not found"
    exit 1
fi

if [ -f "nudge-notify" ] && [ -x "nudge-notify" ] || [ -f "nudge-notify.exe" ]; then
    NOTIFY_SIZE=$(du -h nudge-notify 2>/dev/null | cut -f1 || du -h nudge-notify.exe | cut -f1)
    success "✓ nudge-notify (${NOTIFY_SIZE})"
else
    error "✗ nudge-notify binary not found"
    exit 1
fi

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# SUCCESS BANNER
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

echo
separator
success "  ✓ Build successful"
separator
echo
info "Run Nudge:"
echo -e "  ${BCYAN}./nudge${RESET}                    # Start tracker"
echo -e "  ${BCYAN}./nudge --help${RESET}             # Show help"
echo -e "  ${BCYAN}./nudge --interval 2${RESET}       # Snapshot every 2 min"
echo
info "Respond to snapshots:"
echo -e "  ${BGREEN}./nudge-notify YES${RESET}         # I was productive"
echo -e "  ${BYELLOW}./nudge-notify NO${RESET}          # I was not productive"
echo
