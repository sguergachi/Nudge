#!/bin/bash
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# Nudge Build Script
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
#
# Usage:
#   ./build.sh               Build with dependency checks
#   ./build.sh --clean       Remove generated build artifacts first
#   ./build.sh --skip-deps   Skip dependency installation/check helpers
#   ./build.sh --skip-tests  Skip dotnet test
#   ./build.sh --no-run      Do not auto-launch nudge-tray after build
#
# Checked-in project files are the source of truth. The only generated source
# files are nudge_build.cs and nudge-notify_build.cs, produced by stripping the
# shebang line before build.
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

DOTNET_MAJOR_REQUIRED=10
MAIN_TFM="net10.0"
WINDOWS_TFM="net10.0-windows10.0.17763.0"

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

success() { echo -e "${BGREEN}$1${RESET}"; }
info() { echo -e "${CYAN}$1${RESET}"; }
warning() { echo -e "${BYELLOW}$1${RESET}"; }
error() { echo -e "${BRED}$1${RESET}"; }
dim() { echo -e "${DIM}$1${RESET}"; }
separator() { echo -e "${BCYAN}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${RESET}"; }

CLEAN=false
SKIP_DEPS=false
SKIP_TESTS=false
NO_RUN=false

for arg in "$@"; do
    case "$arg" in
        --clean|-c) CLEAN=true ;;
        --skip-deps) SKIP_DEPS=true ;;
        --skip-tests) SKIP_TESTS=true ;;
        --no-run) NO_RUN=true ;;
        *)
            error "Unknown argument: $arg"
            exit 1
            ;;
    esac
done

detect_os() {
    if [[ "$OSTYPE" == "darwin"* ]]; then
        echo "macos"
    elif [ -f /etc/arch-release ]; then
        echo "arch"
    elif [ -f /etc/debian_version ]; then
        echo "debian"
    elif [ -f /etc/fedora-release ]; then
        echo "fedora"
    else
        echo "unknown"
    fi
}

is_tracked() {
    git ls-files --error-unmatch "$1" >/dev/null 2>&1
}

remove_if_untracked() {
    local path="$1"
    [ -e "$path" ] || [ -L "$path" ] || return 0
    if is_tracked "$path"; then
        dim "  keeping tracked file: $path"
    else
        rm -rf "$path"
    fi
}

clean_artifacts() {
    info "Cleaning generated build artifacts..."
    rm -rf \
        bin obj dist \
        NudgeCrossPlatform.Tests/bin NudgeCrossPlatform.Tests/obj \
        ../TrayIconTest/bin ../TrayIconTest/obj

    remove_if_untracked nudge_build.cs
    remove_if_untracked nudge-notify_build.cs

    for path in \
        nudge nudge.dll nudge.runtimeconfig.json nudge.deps.json \
        nudge-notify nudge-notify.dll nudge-notify.runtimeconfig.json nudge-notify.deps.json \
        nudge-tray nudge-tray.dll nudge-tray.runtimeconfig.json nudge-tray.deps.json; do
        remove_if_untracked "$path"
    done

    if [ -d runtimes ] && ! is_tracked runtimes/osx/native/libAvaloniaNative.dylib; then
        rm -rf runtimes
    fi

    success "✓ Clean complete"
    echo
}

install_dotnet() {
    local os_type
    os_type=$(detect_os)

    info "Installing .NET SDK ${DOTNET_MAJOR_REQUIRED}.0..."
    echo

    case "$os_type" in
        arch)
            sudo -S pacman -S --noconfirm dotnet-sdk
            ;;
        debian)
            sudo -S apt update
            sudo -S apt install -y dotnet-sdk-10.0
            ;;
        fedora)
            sudo -S dnf install -y dotnet-sdk-10.0
            ;;
        macos)
            if command -v brew >/dev/null 2>&1; then
                brew install dotnet@10
            else
                error "Homebrew not found. Install .NET 10 manually from dotnet.microsoft.com."
                exit 1
            fi
            ;;
        *)
            error "Unsupported OS. Install .NET 10 manually from dotnet.microsoft.com."
            exit 1
            ;;
    esac

    success "✓ .NET SDK install step completed"
}

ensure_dotnet_10() {
    if ! command -v dotnet >/dev/null 2>&1; then
        if [ "$SKIP_DEPS" = true ]; then
            error ".NET SDK ${DOTNET_MAJOR_REQUIRED}.0+ is required but dotnet is not installed."
            exit 1
        fi

        warning "✗ .NET SDK not found"
        install_dotnet
    fi

    local version major
    version=$(dotnet --version)
    major=${version%%.*}

    if [ "$major" -lt "$DOTNET_MAJOR_REQUIRED" ]; then
        error "Found .NET SDK $version, but Nudge now requires .NET SDK ${DOTNET_MAJOR_REQUIRED}.0+."
        error "Install .NET ${DOTNET_MAJOR_REQUIRED} and try again."
        exit 1
    fi

    success "✓ .NET SDK ready"
    dim "  Version: $version"
}

install_python_deps() {
    local os_type
    os_type=$(detect_os)

    info "Installing Python dependencies..."
    echo

    if ! python3 -m pip --version >/dev/null 2>&1; then
        warning "⚠ pip not installed, installing..."
        case "$os_type" in
            arch) sudo -S pacman -S --noconfirm python-pip ;;
            debian) sudo -S apt install -y python3-pip ;;
            fedora) sudo -S dnf install -y python3-pip ;;
            macos) python3 -m ensurepip --upgrade ;;
        esac
    fi

    if [ -f requirements-cpu.txt ]; then
        python3 -m pip install --user --break-system-packages -q -r requirements-cpu.txt 2>&1 | grep -v "externally-managed" || true
        success "✓ Python dependencies checked"
    else
        warning "⚠ requirements-cpu.txt not found, skipping Python package install"
    fi
}

install_runtime_deps() {
    local os_type
    os_type=$(detect_os)

    info "Installing runtime dependencies..."
    echo

    case "$os_type" in
        arch)
            sudo -S pacman -S --noconfirm qt6-tools
            if [ ! -e /usr/local/bin/qdbus ] && [ -e /usr/bin/qdbus6 ]; then
                sudo -S ln -sf /usr/bin/qdbus6 /usr/local/bin/qdbus
            fi
            ;;
        debian)
            sudo -S apt install -y qdbus-qt5 qt6-tools-dev-tools
            ;;
        fedora)
            sudo -S dnf install -y qt6-qttools
            ;;
        macos)
            if command -v brew >/dev/null 2>&1; then
                brew install qt6
            fi
            ;;
    esac

    success "✓ Runtime dependencies checked"
}

prepare_build_sources() {
    dim "  Preparing shebang-stripped sources..."
    tail -n +2 nudge.cs > nudge_build.cs
    tail -n +2 nudge-notify.cs > nudge-notify_build.cs
}

verify_file() {
    local path="$1"
    if [ -f "$path" ]; then
        local size
        size=$(du -h "$path" | cut -f1)
        success "✓ $path ($size)"
    else
        error "✗ Missing expected output: $path"
        exit 1
    fi
}

publish_dist_binary() {
    local framework="$1"
    local rid="$2"
    local output_dir="$3"
    local expected_file="$4"

    mkdir -p "$output_dir"
    if dotnet publish nudge-tray.csproj -c Release -f "$framework" -r "$rid" -o "$output_dir" --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --nologo -v quiet; then
        if [ -f "$expected_file" ]; then
            success "  ✓ $expected_file"
        else
            warning "  ⚠ Publish completed but $expected_file was not produced"
        fi
    else
        warning "  ⚠ Failed to publish $rid"
    fi
}

echo
separator
echo -e "  ${BOLD}Nudge Build System${RESET}"
echo -e "  ${DIM}Pinned to .NET 10${RESET}"
separator
echo

if [ "$CLEAN" = true ]; then
    clean_artifacts
fi

if [ "$SKIP_DEPS" = false ]; then
    separator
    info "Checking dependencies..."
    echo
    ensure_dotnet_10
    echo
    install_runtime_deps
    echo
    if command -v python3 >/dev/null 2>&1; then
        install_python_deps
        echo
    else
        warning "⚠ Python 3 not found. Skipping Python dependencies."
        echo
    fi
else
    info "Skipping dependency installation (--skip-deps)"
    echo
    ensure_dotnet_10
    echo
fi

separator
info "Building projects..."
echo

prepare_build_sources

dim "  Restoring projects..."
dotnet restore nudge.csproj --nologo >/dev/null
dotnet restore nudge-notify.csproj --nologo >/dev/null
dotnet restore nudge-tray.csproj --nologo >/dev/null
dotnet restore NudgeCrossPlatform.Tests/NudgeCrossPlatform.Tests.csproj --nologo >/dev/null
dotnet restore ../TrayIconTest/TrayIconTest.csproj --nologo >/dev/null

dim "  Building nudge..."
dotnet build nudge.csproj -c Release -f "$MAIN_TFM" --no-restore --nologo -v quiet
success "  ✓ nudge"

dim "  Building nudge-notify..."
dotnet build nudge-notify.csproj -c Release -f "$MAIN_TFM" --no-restore --nologo -v quiet
success "  ✓ nudge-notify"

LOCAL_TRAY_TFM="$MAIN_TFM"
dim "  Building nudge-tray ($LOCAL_TRAY_TFM)..."
dotnet build nudge-tray.csproj -c Release -f "$LOCAL_TRAY_TFM" --no-restore --nologo -v quiet
success "  ✓ nudge-tray"

dim "  Building TrayIconTest..."
dotnet build ../TrayIconTest/TrayIconTest.csproj -c Release --no-restore --nologo -v quiet
success "  ✓ TrayIconTest"

if [ "$SKIP_TESTS" = false ]; then
    dim "  Running tests..."
    dotnet test NudgeCrossPlatform.Tests/NudgeCrossPlatform.Tests.csproj -c Release --no-restore --nologo -v quiet
    success "  ✓ tests"
else
    warning "  ⚠ Skipping tests (--skip-tests)"
fi

dim "  Publishing nudge-tray runtime output..."
dotnet publish nudge-tray.csproj -c Release -f "$LOCAL_TRAY_TFM" --no-self-contained --no-restore --nologo -v quiet
success "  ✓ nudge-tray publish"

info "Stopping running processes..."
for _ in 1 2 3; do
    pkill -9 -f "nudge-tray" 2>/dev/null || true
    pkill -9 -f "./nudge" 2>/dev/null || true
    pkill -9 -f "/nudge " 2>/dev/null || true
    pkill -9 -f "model_inference" 2>/dev/null || true
    pkill -9 -f "background_trainer" 2>/dev/null || true
    sleep 0.5
done
success "✓ Processes stopped"
echo

cp "bin/Release/$MAIN_TFM/nudge" ./
cp "bin/Release/$MAIN_TFM/nudge.dll" ./
cp "bin/Release/$MAIN_TFM/nudge.runtimeconfig.json" ./
cp "bin/Release/$MAIN_TFM/nudge-notify" ./
cp "bin/Release/$MAIN_TFM/nudge-notify.dll" ./
cp "bin/Release/$MAIN_TFM/nudge-notify.runtimeconfig.json" ./
cp "bin/Release/$LOCAL_TRAY_TFM/publish/nudge-tray" ./
cp "bin/Release/$LOCAL_TRAY_TFM/publish"/*.dll ./
cp "bin/Release/$LOCAL_TRAY_TFM/publish"/*.json ./
cp "bin/Release/$LOCAL_TRAY_TFM/publish"/*.so* ./ 2>/dev/null || true
if [ -d "bin/Release/$LOCAL_TRAY_TFM/publish/runtimes" ]; then
    cp -r "bin/Release/$LOCAL_TRAY_TFM/publish/runtimes" ./
fi

echo
separator
info "Building platform binaries..."
echo
publish_dist_binary "$MAIN_TFM" linux-x64 dist/linux-x64 dist/linux-x64/nudge-tray
publish_dist_binary "$WINDOWS_TFM" win-x64 dist/win-x64 dist/win-x64/nudge-tray.exe

echo
info "Verifying binaries..."
verify_file nudge
verify_file nudge-notify
verify_file nudge-tray

echo
separator
success "  ✓ Build successful"
separator
echo
info "Run Nudge (CLI mode):"
echo -e "  ${BCYAN}./nudge${RESET}                    # Start tracker"
echo -e "  ${BCYAN}./nudge --help${RESET}             # Show help"
echo -e "  ${BCYAN}./nudge --interval 2${RESET}       # Snapshot every 2 min"
echo
info "Run Nudge (System Tray mode):"
echo -e "  ${BCYAN}./nudge-tray${RESET}               # Start with system tray icon"
echo -e "  ${BCYAN}./nudge-tray --interval 2${RESET}  # Tray mode, 2 min intervals"
echo
info "Respond to snapshots (CLI mode):"
echo -e "  ${BGREEN}./nudge-notify YES${RESET}         # I was productive"
echo -e "  ${BYELLOW}./nudge-notify NO${RESET}          # I was not productive"
echo

if [ "$NO_RUN" = false ] && [ -f ./nudge-tray ] && [ -x ./nudge-tray ]; then
    separator
    info "Launching Nudge..."
    ./nudge-tray &
    success "  ✓ nudge-tray launched (PID $!)"
    separator
    echo
fi
