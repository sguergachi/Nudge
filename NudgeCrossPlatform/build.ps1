# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# Nudge Build Script (PowerShell/Windows)
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
#
# Compiles Nudge productivity tracker for Windows.
#
# Usage:
#   .\build.ps1              # Build with auto-detected .NET SDK
#   .\build.ps1 -Clean       # Clean before building
#
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

param(
    [switch]$Clean
)

# Stop on errors
$ErrorActionPreference = "Stop"

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# HELPER FUNCTIONS
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

function Write-Success {
    param([string]$Message)
    Write-Host $Message -ForegroundColor Green
}

function Write-Info {
    param([string]$Message)
    Write-Host $Message -ForegroundColor Cyan
}

function Write-Warning {
    param([string]$Message)
    Write-Host $Message -ForegroundColor Yellow
}

function Write-Error {
    param([string]$Message)
    Write-Host $Message -ForegroundColor Red
}

function Write-Separator {
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
}

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# BANNER
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Write-Host ""
Write-Separator
Write-Host "  Nudge Build System (Windows)" -ForegroundColor White
Write-Host "  Building productivity tracker..." -ForegroundColor Gray
Write-Separator
Write-Host ""

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# CLEAN (if requested)
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

if ($Clean) {
    Write-Info "Cleaning build artifacts..."
    Remove-Item -Path "bin", "obj", "*.csproj", "*.exe", "*.dll", "*.runtimeconfig.json", "runtimes", "nudge", "nudge-notify", "nudge-tray", "*_build.cs" -Recurse -Force -ErrorAction SilentlyContinue
    Write-Success "✓ Clean complete"
    Write-Host ""
}

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# CHECK DEPENDENCIES
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Write-Separator
Write-Info "Checking dependencies..."
Write-Host ""

# Check .NET SDK
if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "✗ .NET SDK not found"
    Write-Host ""
    Write-Error "Please install .NET SDK from: https://dotnet.microsoft.com/download"
    exit 1
}

$dotnetVersion = dotnet --version
Write-Success "✓ .NET SDK already installed"
Write-Host "  Version: $dotnetVersion" -ForegroundColor Gray
Write-Host ""

# Check Python (optional)
if (Get-Command python -ErrorAction SilentlyContinue) {
    Write-Info "Installing Python dependencies..."
    if (Test-Path "requirements-cpu.txt") {
        python -m pip install --user -q -r requirements-cpu.txt
        Write-Success "✓ Python dependencies installed"
    }
    else {
        Write-Warning "⚠ requirements-cpu.txt not found, skipping Python packages"
    }
    Write-Host ""
}
else {
    Write-Warning "⚠ Python not found. Skipping Python dependencies."
    Write-Warning "  Install Python to train ML models."
    Write-Host ""
}

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# BUILD
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Write-Info "Building with .NET..."
Write-Host ""

# Strip shebang lines for compilation
Write-Host "  Preparing source files..." -ForegroundColor Gray
Get-Content nudge.cs | Where-Object { $_ -notmatch '^#!/' } | Set-Content nudge_build.cs -Encoding UTF8
Get-Content nudge-notify.cs | Where-Object { $_ -notmatch '^#!/' } | Set-Content nudge-notify_build.cs -Encoding UTF8

# Detect installed .NET version
$dotnetMajorVersion = $dotnetVersion.Split('.')[0]
$targetFramework = "net$dotnetMajorVersion.0"

Write-Host "  Target framework: $targetFramework" -ForegroundColor Gray

# Create minimal project files on the fly
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>$targetFramework</TargetFramework>
    <RootNamespace>Nudge</RootNamespace>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="nudge_build.cs" />
  </ItemGroup>
</Project>
"@ | Set-Content nudge.csproj -Encoding UTF8

@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>$targetFramework</TargetFramework>
    <RootNamespace>NudgeNotify</RootNamespace>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="nudge-notify_build.cs" />
  </ItemGroup>
</Project>
"@ | Set-Content nudge-notify.csproj -Encoding UTF8

Write-Host "  Building nudge..." -ForegroundColor Gray
dotnet build nudge.csproj -c Release -v quiet --nologo
Write-Success "  ✓ nudge"

Write-Host "  Building nudge-notify..." -ForegroundColor Gray
dotnet build nudge-notify.csproj -c Release -v quiet --nologo
Write-Success "  ✓ nudge-notify"

# Create nudge-tray project with Avalonia
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>$targetFramework</TargetFramework>
    <RootNamespace>NudgeTray</RootNamespace>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <DefineConstants>WINDOWS</DefineConstants>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="nudge-tray.cs" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.2.2" />
    <PackageReference Include="Avalonia.Desktop" Version="11.2.2" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.2.2" />
    <PackageReference Include="System.Windows.Forms" Version="4.0.0.0" />
  </ItemGroup>
</Project>
"@ | Set-Content nudge-tray.csproj -Encoding UTF8

Write-Host "  Building nudge-tray (with Avalonia UI)..." -ForegroundColor Gray
dotnet publish nudge-tray.csproj -c Release --nologo --no-self-contained

if (!(Test-Path "bin/Release/$targetFramework/publish/nudge-tray.dll")) {
    Write-Error "✗ nudge-tray.dll not found after build"
    exit 1
}

Write-Success "  ✓ nudge-tray"

# Copy binaries and dependencies to root for easy access
Copy-Item "bin/Release/$targetFramework/nudge.exe" -Destination "." -Force
Copy-Item "bin/Release/$targetFramework/nudge.dll" -Destination "." -Force
Copy-Item "bin/Release/$targetFramework/nudge.runtimeconfig.json" -Destination "." -Force
Copy-Item "bin/Release/$targetFramework/nudge-notify.exe" -Destination "." -Force
Copy-Item "bin/Release/$targetFramework/nudge-notify.dll" -Destination "." -Force
Copy-Item "bin/Release/$targetFramework/nudge-notify.runtimeconfig.json" -Destination "." -Force

# Copy nudge-tray with all Avalonia dependencies from publish folder
Copy-Item "bin/Release/$targetFramework/publish/nudge-tray.exe" -Destination "." -Force
Copy-Item "bin/Release/$targetFramework/publish/*.dll" -Destination "." -Force
Copy-Item "bin/Release/$targetFramework/publish/*.json" -Destination "." -Force
if (Test-Path "bin/Release/$targetFramework/publish/runtimes") {
    Copy-Item "bin/Release/$targetFramework/publish/runtimes" -Destination "." -Recurse -Force
}

Remove-Item "nudge_build.cs", "nudge-notify_build.cs" -Force -ErrorAction SilentlyContinue

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# VERIFY BINARIES
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Write-Host ""
Write-Info "Verifying binaries..."

if (Test-Path "nudge.exe") {
    $nudgeSize = (Get-Item "nudge.exe").Length / 1KB
    Write-Success "✓ nudge.exe ($([math]::Round($nudgeSize, 1)) KB)"
}
else {
    Write-Error "✗ nudge.exe not found"
    exit 1
}

if (Test-Path "nudge-notify.exe") {
    $notifySize = (Get-Item "nudge-notify.exe").Length / 1KB
    Write-Success "✓ nudge-notify.exe ($([math]::Round($notifySize, 1)) KB)"
}
else {
    Write-Error "✗ nudge-notify.exe not found"
    exit 1
}

if (Test-Path "nudge-tray.exe") {
    $traySize = (Get-Item "nudge-tray.exe").Length / 1KB
    Write-Success "✓ nudge-tray.exe ($([math]::Round($traySize, 1)) KB)"
}
else {
    Write-Warning "⚠ nudge-tray.exe not built (Avalonia UI may not be supported)"
}

# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# SUCCESS BANNER
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Write-Host ""
Write-Separator
Write-Success "  ✓ Build successful"
Write-Separator
Write-Host ""
Write-Info "Run Nudge (CLI mode):"
Write-Host "  .\nudge.exe                    # Start tracker" -ForegroundColor Cyan
Write-Host "  .\nudge.exe --help             # Show help" -ForegroundColor Cyan
Write-Host "  .\nudge.exe --interval 2       # Snapshot every 2 min" -ForegroundColor Cyan
Write-Host ""
Write-Info "Run Nudge (System Tray mode):"
Write-Host "  .\nudge-tray.exe               # Start with system tray icon" -ForegroundColor Cyan
Write-Host "  .\nudge-tray.exe --interval 2  # Tray mode, 2 min intervals" -ForegroundColor Cyan
Write-Host ""
Write-Info "Respond to snapshots (CLI mode):"
Write-Host "  .\nudge-notify.exe YES         # I was productive" -ForegroundColor Green
Write-Host "  .\nudge-notify.exe NO          # I was not productive" -ForegroundColor Yellow
Write-Host ""
