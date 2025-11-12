#!/bin/bash
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# Nudge Build Script
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
#
# Compiles Nudge productivity tracker with obsessive attention to detail.
# Jon Blow style: No projects, no ceremony, just compile the code.
#
# Usage:
#   ./build.sh              # Build with auto-detected compiler (installs dependencies)
#   ./build.sh --clean      # Clean before building
#   ./build.sh --skip-deps  # Skip automatic dependency installation
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
# OS DETECTION
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

detect_os() {
    if [ -f /etc/arch-release ]; then
        echo "arch"
    elif [ -f /etc/debian_version ]; then
        echo "debian"
    elif [ -f /etc/fedora-release ]; then
        echo "fedora"
    elif [[ "$OSTYPE" == "darwin"* ]]; then
        echo "macos"
    else
        echo "unknown"
    fi
}

install_dotnet() {
    local os_type=$(detect_os)

    info "Installing .NET SDK..."
    echo

    case "$os_type" in
        arch)
            info "Detected Arch Linux"
            echo
            # Use pacman directly
            sudo -S pacman -S --noconfirm dotnet-sdk
            ;;
        debian)
            info "Detected Debian/Ubuntu"
            echo
            sudo -S apt update
            sudo -S apt install -y dotnet-sdk-8.0
            ;;
        fedora)
            info "Detected Fedora"
            echo
            sudo -S dnf install -y dotnet-sdk-8.0
            ;;
        macos)
            info "Detected macOS"
            echo
            if command -v brew &> /dev/null; then
                brew install dotnet
            else
                error "Homebrew not found. Please install from https://brew.sh"
                exit 1
            fi
            ;;
        *)
            error "Unsupported OS. Please install .NET SDK manually:"
            error "  https://dotnet.microsoft.com/download"
            exit 1
            ;;
    esac

    echo
    success "✓ .NET SDK installed"
}

install_python_deps() {
    local os_type=$(detect_os)

    info "Installing Python dependencies..."
    echo

    # Install pip if not available
    if ! python3 -m pip --version &> /dev/null; then
        warning "⚠ pip not installed, installing..."
        case "$os_type" in
            arch)
                if ! pacman -Q python-pip &> /dev/null; then
                    sudo -S pacman -S --noconfirm python-pip
                fi
                ;;
            debian)
                sudo -S apt install -y python3-pip
                ;;
            fedora)
                sudo -S dnf install -y python3-pip
                ;;
            macos)
                python3 -m ensurepip --upgrade
                ;;
        esac
    fi

    # Install Python packages
    if python3 -m pip --version &> /dev/null; then
        if [ -f "requirements-cpu.txt" ]; then
            dim "  Installing TensorFlow, pandas, numpy, scikit-learn..."
            # Use --break-system-packages for externally-managed Python environments
            # This is safe when combined with --user (installs to ~/.local)
            python3 -m pip install --user --break-system-packages -q -r requirements-cpu.txt 2>&1 | grep -v "externally-managed" || true
            if python3 -m pip list --user 2>/dev/null | grep -q tensorflow; then
                success "✓ Python dependencies installed"
            else
                warning "⚠ Python package installation may have failed"
            fi
        else
            warning "⚠ requirements-cpu.txt not found, skipping Python packages"
        fi
    else
        warning "⚠ Failed to install pip"
    fi
}

install_runtime_deps() {
    local os_type=$(detect_os)

    info "Installing runtime dependencies..."
    echo

    case "$os_type" in
        arch)
            dim "  Checking qt6-tools for KDE/Wayland support..."
            if ! pacman -Q qt6-tools &> /dev/null; then
                sudo -S pacman -S --noconfirm qt6-tools
            fi
            # Create qdbus symlink if needed
            if [ ! -e /usr/local/bin/qdbus ] && [ -e /usr/bin/qdbus6 ]; then
                sudo -S ln -sf /usr/bin/qdbus6 /usr/local/bin/qdbus
            fi
            ;;
        debian)
            dim "  Installing qdbus and Qt tools..."
            sudo -S apt install -y qdbus-qt5 qt6-tools-dev-tools
            ;;
        fedora)
            dim "  Installing Qt D-Bus tools..."
            sudo -S dnf install -y qt6-qttools
            ;;
        macos)
            dim "  Installing Qt via Homebrew..."
            if command -v brew &> /dev/null; then
                brew install qt6
            fi
            ;;
    esac

    success "✓ Runtime dependencies installed"
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
SKIP_DEPS=false

for arg in "$@"; do
    case "$arg" in
        --clean|-c)
            CLEAN=true
            ;;
        --skip-deps)
            SKIP_DEPS=true
            ;;
    esac
done

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# CLEAN (if requested)
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

if [ "$CLEAN" = true ]; then
    info "Cleaning build artifacts..."
    rm -rf bin obj *.csproj *.exe *.dll *.runtimeconfig.json *.so* runtimes nudge nudge-notify nudge-tray *_build.cs 2>/dev/null || true
    success "✓ Clean complete"
    echo
fi

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# INSTALL DEPENDENCIES
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

if [ "$SKIP_DEPS" = false ]; then
    separator
    info "Checking dependencies..."
    echo

    # Check and install .NET SDK
    if ! command -v dotnet &> /dev/null; then
        warning "✗ .NET SDK not found"
        echo
        install_dotnet
        echo
    else
        VERSION=$(dotnet --version)
        success "✓ .NET SDK already installed"
        dim "  Version: ${VERSION}"
        echo
    fi

    # Install runtime dependencies (Qt, qdbus for Wayland/KDE)
    install_runtime_deps
    echo

    # Install Python dependencies
    if command -v python3 &> /dev/null; then
        install_python_deps
        echo
    else
        warning "⚠ Python 3 not found. Skipping Python dependencies."
        warning "  Install Python 3 to train ML models."
        echo
    fi
else
    info "Skipping dependency installation (--skip-deps)"
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
    success "✓ Using .NET SDK"
    dim "  Version: ${VERSION}"
elif command -v csc &> /dev/null; then
    COMPILER="mono"
    success "✓ Using Mono C# Compiler"
else
    error "✗ No C# compiler found"
    error ""
    error "Automatic installation may have failed. Please install manually:"
    error "  - .NET SDK 8.0+:  https://dot.net"
    error "  - Mono:           https://www.mono-project.com"
    error ""
    error "Or try running: ./build.sh (without --skip-deps)"
    exit 1
fi

echo

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# BUILD
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

if [ "$COMPILER" = "dotnet" ]; then
    info "Building with .NET..."
    echo

    # Strip shebang lines for compilation (Jon Blow style: make it work)
    dim "  Preparing source files..."
    sed '1{/^#!/d;}' nudge.cs > nudge_build.cs
    sed '1{/^#!/d;}' nudge-notify.cs > nudge-notify_build.cs

    # Detect installed .NET version
    DOTNET_MAJOR_VERSION=$(dotnet --version | cut -d'.' -f1)
    TARGET_FRAMEWORK="net${DOTNET_MAJOR_VERSION}.0"

    dim "  Target framework: ${TARGET_FRAMEWORK}"

    # Create minimal project files on the fly
    cat > nudge.csproj << EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>${TARGET_FRAMEWORK}</TargetFramework>
    <RootNamespace>Nudge</RootNamespace>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="nudge_build.cs" />
  </ItemGroup>
</Project>
EOF

    cat > nudge-notify.csproj << EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>${TARGET_FRAMEWORK}</TargetFramework>
    <RootNamespace>NudgeNotify</RootNamespace>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="nudge-notify_build.cs" />
  </ItemGroup>
</Project>
EOF

    dim "  Building nudge..."
    dotnet build nudge.csproj -c Release -v quiet --nologo
    success "  ✓ nudge"

    dim "  Building nudge-notify..."
    dotnet build nudge-notify.csproj -c Release -v quiet --nologo
    success "  ✓ nudge-notify"

    # Create nudge-tray project with Avalonia
    cat > nudge-tray.csproj << EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>${TARGET_FRAMEWORK}</TargetFramework>
    <RootNamespace>NudgeTray</RootNamespace>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>

  <!-- Windows-specific settings -->
  <PropertyGroup Condition="'\$(OS)' == 'Windows_NT'">
    <UseWindowsForms>true</UseWindowsForms>
    <DefineConstants>\$(DefineConstants);WINDOWS</DefineConstants>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="nudge-tray.cs" />
    <Compile Include="CustomNotification.cs" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.2.2" />
    <PackageReference Include="Avalonia.Desktop" Version="11.2.2" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.2.2" />
    <PackageReference Include="Tmds.DBus.Protocol" Version="0.21.0" />
  </ItemGroup>

  <!-- Windows-specific packages -->
  <ItemGroup Condition="'\$(OS)' == 'Windows_NT'">
    <PackageReference Include="Microsoft.Toolkit.Uwp.Notifications" Version="7.1.3" />
  </ItemGroup>
</Project>
EOF

    dim "  Building nudge-tray (with Avalonia UI)..."
    dotnet publish nudge-tray.csproj -c Release --nologo --no-self-contained

    if [ ! -f "bin/Release/${TARGET_FRAMEWORK}/publish/nudge-tray.dll" ]; then
        error "✗ nudge-tray.dll not found after build"
        exit 1
    fi

    success "  ✓ nudge-tray"

    # Copy binaries and dependencies to root for easy access
    cp bin/Release/${TARGET_FRAMEWORK}/nudge ./
    cp bin/Release/${TARGET_FRAMEWORK}/nudge.dll ./
    cp bin/Release/${TARGET_FRAMEWORK}/nudge.runtimeconfig.json ./
    cp bin/Release/${TARGET_FRAMEWORK}/nudge-notify ./
    cp bin/Release/${TARGET_FRAMEWORK}/nudge-notify.dll ./
    cp bin/Release/${TARGET_FRAMEWORK}/nudge-notify.runtimeconfig.json ./

    # Copy nudge-tray with all Avalonia dependencies from publish folder
    cp bin/Release/${TARGET_FRAMEWORK}/publish/nudge-tray ./
    cp bin/Release/${TARGET_FRAMEWORK}/publish/*.dll ./
    cp bin/Release/${TARGET_FRAMEWORK}/publish/*.json ./
    cp bin/Release/${TARGET_FRAMEWORK}/publish/*.so* ./ 2>/dev/null || true
    # Copy native libraries with proper directory structure
    if [ -d "bin/Release/${TARGET_FRAMEWORK}/publish/runtimes" ]; then
        cp -r bin/Release/${TARGET_FRAMEWORK}/publish/runtimes ./
    fi

    rm -f nudge_build.cs nudge-notify_build.cs

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

if [ -f "nudge-tray" ] && [ -x "nudge-tray" ] || [ -f "nudge-tray.exe" ]; then
    TRAY_SIZE=$(du -h nudge-tray 2>/dev/null | cut -f1 || du -h nudge-tray.exe | cut -f1)
    success "✓ nudge-tray (${TRAY_SIZE})"
else
    warning "⚠ nudge-tray not built (Avalonia UI may not be supported on this system)"
fi

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# SUCCESS BANNER
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

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
