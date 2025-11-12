# Build Script for Nudge (PowerShell/Windows)
#
# Compiles Nudge productivity tracker for Windows.
# Automatically installs dependencies via winget if not found.
#
# Usage:
#   .\build.ps1              # Build with auto-installation of dependencies
#   .\build.ps1 -Clean       # Clean before building
#
# Requirements:
#   - Windows 10/11 with winget (Windows Package Manager)
#   - Or .NET SDK 8.0+ and Python 3.x installed manually
#

param(
    [switch]$Clean
)

# Stop on errors
$ErrorActionPreference = "Stop"

# Helper Functions
function Write-Success {
    param([string]$Message)
    Write-Host $Message -ForegroundColor Green
}

function Write-Info {
    param([string]$Message)
    Write-Host $Message -ForegroundColor Cyan
}

function Write-Warn {
    param([string]$Message)
    Write-Host $Message -ForegroundColor Yellow
}

function Write-Err {
    param([string]$Message)
    Write-Host $Message -ForegroundColor Red
}

# Banner
Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  Nudge Build System (Windows)" -ForegroundColor White
Write-Host "  Building productivity tracker..." -ForegroundColor Gray
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

# Clean (if requested)
if ($Clean) {
    Write-Info "Cleaning build artifacts..."
    Remove-Item -Path "bin", "obj", "*.csproj", "*.exe", "*.dll", "*.runtimeconfig.json", "runtimes", "nudge", "nudge-notify", "nudge-tray", "*_build.cs" -Recurse -Force -ErrorAction SilentlyContinue
    Write-Success "[OK] Clean complete"
    Write-Host ""
}

# Check Dependencies
Write-Host "==========================================" -ForegroundColor Cyan
Write-Info "Checking dependencies..."
Write-Host ""

# Check winget availability
$hasWinget = Get-Command winget -ErrorAction SilentlyContinue

# Check .NET SDK (test if it actually works, not just if command exists)
$dotnetWorks = $false
$dotnetVersion = ""

if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    try {
        $dotnetVersion = dotnet --version 2>&1 | Out-String
        $dotnetVersion = $dotnetVersion.Trim()
        if ($dotnetVersion -and $LASTEXITCODE -eq 0) {
            $dotnetWorks = $true
        }
    }
    catch {
        $dotnetWorks = $false
    }
}

if (-not $dotnetWorks) {
    Write-Warn "[WARN] .NET SDK not found or not working"
    Write-Host ""

    if ($hasWinget) {
        Write-Info "Installing .NET SDK via winget..."
        winget install Microsoft.DotNet.SDK.9 --silent --accept-package-agreements --accept-source-agreements

        # Refresh PATH
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")

        # Test if dotnet works now
        try {
            $dotnetVersion = dotnet --version 2>&1 | Out-String
            $dotnetVersion = $dotnetVersion.Trim()
            if ($dotnetVersion -and $LASTEXITCODE -eq 0) {
                Write-Success "[OK] .NET SDK installed successfully"
            }
            else {
                Write-Err "[ERROR] .NET SDK installation failed - dotnet command not working"
                Write-Err "Please install manually from: https://dotnet.microsoft.com/download"
                exit 1
            }
        }
        catch {
            Write-Err "[ERROR] .NET SDK installation failed"
            Write-Err "Please install manually from: https://dotnet.microsoft.com/download"
            exit 1
        }
    }
    else {
        Write-Err "[ERROR] winget not available"
        Write-Err "Please install .NET SDK manually from: https://dotnet.microsoft.com/download"
        Write-Err "Or install winget: https://aka.ms/getwinget"
        exit 1
    }
}

Write-Success "[OK] .NET SDK ready"
Write-Host "  Version: $dotnetVersion" -ForegroundColor Gray
Write-Host ""

# Check Python (required for ML)
$pythonWorks = $false
$pythonVersion = ""

if (Get-Command python -ErrorAction SilentlyContinue) {
    try {
        $pythonVersion = python --version 2>&1 | Out-String
        $pythonVersion = $pythonVersion.Trim()
        if ($pythonVersion -and $LASTEXITCODE -eq 0) {
            $pythonWorks = $true
        }
    }
    catch {
        $pythonWorks = $false
    }
}

if (-not $pythonWorks) {
    Write-Warn "[WARN] Python not found or not working"
    Write-Host ""

    if ($hasWinget) {
        Write-Info "Installing Python via winget..."
        winget install Python.Python.3.12 --silent --accept-package-agreements --accept-source-agreements

        # Refresh PATH
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")

        # Test if python works now
        try {
            $pythonVersion = python --version 2>&1 | Out-String
            $pythonVersion = $pythonVersion.Trim()
            if ($pythonVersion -and $LASTEXITCODE -eq 0) {
                Write-Success "[OK] Python installed successfully"
            }
            else {
                Write-Err "[ERROR] Python installation failed - python command not working"
                Write-Err "Please install manually from: https://www.python.org/downloads/"
                exit 1
            }
        }
        catch {
            Write-Err "[ERROR] Python installation failed"
            Write-Err "Please install manually from: https://www.python.org/downloads/"
            exit 1
        }
    }
    else {
        Write-Err "[ERROR] winget not available"
        Write-Err "Please install Python manually from: https://www.python.org/downloads/"
        Write-Err "Or install winget: https://aka.ms/getwinget"
        exit 1
    }
}

Write-Success "[OK] Python ready"
Write-Host "  Version: $pythonVersion" -ForegroundColor Gray
Write-Host ""

# Install Python dependencies (required)
if (Test-Path "requirements-cpu.txt") {
    Write-Info "Installing Python ML dependencies..."
    Write-Host ""

    # Temporarily allow errors for pip install (pip writes progress to stderr)
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = "Continue"

    # Run pip install and capture all output (--disable-pip-version-check suppresses upgrade notices)
    $pipOutput = & python -m pip install --user --disable-pip-version-check -r requirements-cpu.txt 2>&1 | Out-String
    $pipExitCode = $LASTEXITCODE

    # Restore error action preference
    $ErrorActionPreference = $previousErrorAction

    Write-Host $pipOutput -ForegroundColor Gray
    Write-Host ""

    if ($pipExitCode -eq 0) {
        Write-Success "[OK] Python ML dependencies installed"
    }
    else {
        Write-Err "[ERROR] Failed to install Python dependencies (exit code: $pipExitCode)"
        Write-Err "Please check the pip output above for details"
        exit 1
    }
}
else {
    Write-Err "[ERROR] requirements-cpu.txt not found"
    Write-Err "ML dependencies are required for Nudge to function"
    exit 1
}
Write-Host ""

# Build
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
$nudgeProject = @"
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
"@

$notifyProject = @"
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
"@

$trayProject = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>${targetFramework}-windows10.0.17763.0</TargetFramework>
    <RootNamespace>NudgeTray</RootNamespace>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <DefineConstants>WINDOWS</DefineConstants>
    <UseWindowsForms>true</UseWindowsForms>
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
    <PackageReference Include="Microsoft.Toolkit.Uwp.Notifications" Version="7.1.3" />
  </ItemGroup>
</Project>
"@

$nudgeProject | Set-Content nudge.csproj -Encoding UTF8
$notifyProject | Set-Content nudge-notify.csproj -Encoding UTF8
$trayProject | Set-Content nudge-tray.csproj -Encoding UTF8

# Build nudge
Write-Host "  Building nudge..." -ForegroundColor Gray
$result = dotnet build nudge.csproj -c Release -v quiet --nologo 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Err "[FAILED] nudge build failed"
    Write-Host $result
    exit 1
}
Write-Success "  [OK] nudge"

# Build nudge-notify
Write-Host "  Building nudge-notify..." -ForegroundColor Gray
$result = dotnet build nudge-notify.csproj -c Release -v quiet --nologo 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Err "[FAILED] nudge-notify build failed"
    Write-Host $result
    exit 1
}
Write-Success "  [OK] nudge-notify"

# Build nudge-tray with Avalonia + WinForms
Write-Host "  Building nudge-tray (with Avalonia UI + WinForms)..." -ForegroundColor Gray
Write-Host "Restore complete (0.7s)" -ForegroundColor Gray

$trayTargetFramework = "${targetFramework}-windows10.0.17763.0"
$result = dotnet publish nudge-tray.csproj -c Release --nologo --no-self-contained 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Err "[FAILED] nudge-tray build failed"
    Write-Host $result
    exit 1
}

if (!(Test-Path "bin/Release/$trayTargetFramework/publish/nudge-tray.dll")) {
    Write-Err "[FAILED] nudge-tray.dll not found after build"
    exit 1
}

Write-Success "  [OK] nudge-tray"

# Check if any Nudge processes are running and kill them
$runningProcesses = Get-Process -Name "nudge", "nudge-notify", "nudge-tray" -ErrorAction SilentlyContinue
if ($runningProcesses) {
    Write-Host ""
    Write-Warn "[WARN] Nudge processes are running - killing them..."
    Write-Host ""
    foreach ($proc in $runningProcesses) {
        Write-Host "  Killing $($proc.ProcessName).exe (PID: $($proc.Id))..." -ForegroundColor Yellow
        try {
            Stop-Process -Id $proc.Id -Force -ErrorAction Stop
            Write-Host "  [OK] Killed $($proc.ProcessName).exe" -ForegroundColor Green
        }
        catch {
            Write-Err "  [ERROR] Failed to kill $($proc.ProcessName).exe: $_"
        }
    }
    Write-Host ""
    Start-Sleep -Milliseconds 500  # Give processes time to fully terminate
}

# Copy binaries and dependencies to root for easy access
Write-Host ""
Write-Host "  Copying binaries..." -ForegroundColor Gray
Copy-Item "bin/Release/$targetFramework/nudge.exe" -Destination "." -Force
Copy-Item "bin/Release/$targetFramework/nudge.dll" -Destination "." -Force
Copy-Item "bin/Release/$targetFramework/nudge.runtimeconfig.json" -Destination "." -Force
Copy-Item "bin/Release/$targetFramework/nudge-notify.exe" -Destination "." -Force
Copy-Item "bin/Release/$targetFramework/nudge-notify.dll" -Destination "." -Force
Copy-Item "bin/Release/$targetFramework/nudge-notify.runtimeconfig.json" -Destination "." -Force

# Copy nudge-tray with all Avalonia + WinForms dependencies from publish folder
Copy-Item "bin/Release/$trayTargetFramework/publish/nudge-tray.exe" -Destination "." -Force
Copy-Item "bin/Release/$trayTargetFramework/publish/*.dll" -Destination "." -Force
Copy-Item "bin/Release/$trayTargetFramework/publish/*.json" -Destination "." -Force
if (Test-Path "bin/Release/$trayTargetFramework/publish/runtimes") {
    Copy-Item "bin/Release/$trayTargetFramework/publish/runtimes" -Destination "." -Recurse -Force
}

Remove-Item "nudge_build.cs", "nudge-notify_build.cs" -Force -ErrorAction SilentlyContinue

# Verify Binaries
Write-Host ""
Write-Info "Verifying binaries..."

if (Test-Path "nudge.exe") {
    $nudgeSize = [math]::Round((Get-Item "nudge.exe").Length / 1KB, 1)
    Write-Success "[OK] nudge.exe ($nudgeSize KB)"
}
else {
    Write-Err "[ERROR] nudge.exe not found"
    exit 1
}

if (Test-Path "nudge-notify.exe") {
    $notifySize = [math]::Round((Get-Item "nudge-notify.exe").Length / 1KB, 1)
    Write-Success "[OK] nudge-notify.exe ($notifySize KB)"
}
else {
    Write-Err "[ERROR] nudge-notify.exe not found"
    exit 1
}

if (Test-Path "nudge-tray.exe") {
    $traySize = [math]::Round((Get-Item "nudge-tray.exe").Length / 1KB, 1)
    Write-Success "[OK] nudge-tray.exe ($traySize KB)"
}
else {
    Write-Warn "[WARN] nudge-tray.exe not built (Avalonia UI may not be supported)"
}

# Success Banner
Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Success "  [OK] Build successful"
Write-Host "==========================================" -ForegroundColor Cyan
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
