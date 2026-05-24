# Build Script for Nudge (PowerShell/Windows)
#
# Usage:
#   .\build.ps1               Build with dependency checks
#   .\build.ps1 -Clean        Remove generated build artifacts first
#   .\build.ps1 -SkipDeps     Skip dependency installation helpers
#   .\build.ps1 -SkipTests    Skip dotnet test
#   .\build.ps1 -NoRun        Do not auto-launch nudge-tray after build

param(
    [switch]$Clean,
    [switch]$SkipDeps,
    [switch]$SkipTests,
    [switch]$NoRun
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$DotnetMajorRequired = 10
$MainTfm = "net10.0"
$WindowsTfm = "net10.0-windows10.0.17763.0"

function Write-Success { param([string]$Message) Write-Host $Message -ForegroundColor Green }
function Write-Info { param([string]$Message) Write-Host $Message -ForegroundColor Cyan }
function Write-Warn { param([string]$Message) Write-Host $Message -ForegroundColor Yellow }
function Write-Err { param([string]$Message) Write-Host $Message -ForegroundColor Red }

function Test-IsTracked {
    param([string]$Path)

    git ls-files --error-unmatch -- $Path *> $null
    return $LASTEXITCODE -eq 0
}

function Remove-IfUntracked {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return
    }

    if (Test-IsTracked $Path) {
        Write-Host "  keeping tracked file: $Path" -ForegroundColor DarkGray
    }
    else {
        Remove-Item -Path $Path -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Clean-Artifacts {
    Write-Info "Cleaning generated build artifacts..."
    Remove-Item -Path "bin", "obj", "dist", "NudgeCrossPlatform.Tests/bin", "NudgeCrossPlatform.Tests/obj", "../TrayIconTest/bin", "../TrayIconTest/obj" -Recurse -Force -ErrorAction SilentlyContinue

    Remove-IfUntracked "nudge_build.cs"
    Remove-IfUntracked "nudge-notify_build.cs"

    foreach ($path in @(
        "nudge.exe", "nudge.dll", "nudge.runtimeconfig.json", "nudge.deps.json",
        "nudge-notify.exe", "nudge-notify.dll", "nudge-notify.runtimeconfig.json", "nudge-notify.deps.json",
        "nudge-tray.exe", "nudge-tray.dll", "nudge-tray.runtimeconfig.json", "nudge-tray.deps.json")) {
        Remove-IfUntracked $path
    }

    if ((Test-Path "runtimes") -and -not (Test-IsTracked "runtimes/osx/native/libAvaloniaNative.dylib")) {
        Remove-Item -Path "runtimes" -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Success "[OK] Clean complete"
    Write-Host ""
}

function Refresh-Path {
    $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")
}

function Install-Dotnet10 {
    Write-Info "Installing .NET SDK 10..."
    winget install Microsoft.DotNet.SDK.10 --silent --accept-package-agreements --accept-source-agreements
    Refresh-Path
}

function Ensure-Dotnet10 {
    $dotnetVersion = ""
    $dotnetAvailable = $false

    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        $dotnetVersion = (dotnet --version 2>&1 | Out-String).Trim()
        $dotnetAvailable = $dotnetVersion -match '^\d+\.\d+\.\d+'
    }

    if (-not $dotnetAvailable) {
        # dotnet --version may fail due to global.json requiring a newer SDK.
        # Fall back to --list-sdks to find the highest installed version.
        try {
            $sdkOutput = dotnet --list-sdks 2>&1 | Out-String
            $versions = @()
            $sdkOutput -split "`n" | ForEach-Object {
                if ($_ -match '^(\d+\.\d+\.\d+)') {
                    $versions += [version]$matches[1]
                }
            }
            if ($versions.Count -gt 0) {
                $highest = ($versions | Sort-Object -Descending)[0]
                $dotnetVersion = $highest.ToString()
                $dotnetAvailable = $true
            }
        }
        catch { }
    }

    if ($dotnetAvailable) {
        $major = [int]($dotnetVersion.Split('.')[0])
        if ($major -ge $DotnetMajorRequired) {
            Write-Success "[OK] .NET SDK ready"
            Write-Host "  Version: $dotnetVersion" -ForegroundColor DarkGray
            return
        }
    }

    if ($SkipDeps) {
        Write-Err ".NET SDK $DotnetMajorRequired.0+ is required but not found."
        if (Get-Command dotnet -ErrorAction SilentlyContinue) {
            dotnet --list-sdks 2>&1 | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
        }
        exit 1
    }

    Write-Warn "[WARN] .NET SDK 10 not found"
    Install-Dotnet10

    $dotnetVersion = (dotnet --version 2>&1 | Out-String).Trim()
    if ($dotnetVersion -notmatch '^\d+\.\d+\.\d+') {
        Write-Err "Failed to install .NET SDK $DotnetMajorRequired.0+."
        if (Get-Command dotnet -ErrorAction SilentlyContinue) {
            dotnet --list-sdks 2>&1 | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
        }
        exit 1
    }
    $major = [int]($dotnetVersion.Split('.')[0])
    if ($major -lt $DotnetMajorRequired) {
        Write-Err "Installed .NET SDK $dotnetVersion is too old. $DotnetMajorRequired.0+ required."
        exit 1
    }

    Write-Success "[OK] .NET SDK ready"
    Write-Host "  Version: $dotnetVersion" -ForegroundColor DarkGray
}

function Ensure-Python {
    $pythonCommand = $null

    foreach ($candidate in @("python", "py")) {
        if (Get-Command $candidate -ErrorAction SilentlyContinue) {
            try {
                $version = (& $candidate --version 2>&1 | Out-String).Trim()
                if ($version) {
                    $script:PythonCommand = $candidate
                    Write-Success "[OK] Python ready"
                    Write-Host "  Version: $version" -ForegroundColor DarkGray
                    return
                }
            }
            catch { }
        }
    }

    if ($SkipDeps) {
        Write-Warn "[WARN] Python not found. Skipping Python dependency install because -SkipDeps was used."
        return
    }

    Write-Warn "[WARN] Python not found"
    winget install Python.Python.3.12 --silent --accept-package-agreements --accept-source-agreements
    Refresh-Path

    foreach ($candidate in @("python", "py")) {
        if (Get-Command $candidate -ErrorAction SilentlyContinue) {
            $script:PythonCommand = $candidate
            Write-Success "[OK] Python ready"
            return
        }
    }

    Write-Err "[ERROR] Python installation failed"
    exit 1
}

function Install-PythonDeps {
    if (-not $script:PythonCommand) {
        return
    }

    if (-not (Test-Path "requirements-cpu.txt")) {
        Write-Warn "[WARN] requirements-cpu.txt not found, skipping Python package install"
        return
    }

    Write-Info "Installing Python dependencies..."
    & $script:PythonCommand -m pip install --user --disable-pip-version-check -r requirements-cpu.txt
    if ($LASTEXITCODE -ne 0) {
        Write-Err "[ERROR] Failed to install Python dependencies"
        exit 1
    }

    Write-Success "[OK] Python dependencies checked"
}

function Prepare-BuildSources {
    Write-Host "  Preparing shebang-stripped sources..." -ForegroundColor DarkGray
    Get-Content -LiteralPath "nudge.cs" | Select-Object -Skip 1 | Set-Content -LiteralPath "nudge_build.cs" -Encoding utf8
    Get-Content -LiteralPath "nudge-notify.cs" | Select-Object -Skip 1 | Set-Content -LiteralPath "nudge-notify_build.cs" -Encoding utf8
}

function Invoke-Dotnet {
    param([string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed"
    }
}

function Publish-DistBinary {
    param(
        [string]$Framework,
        [string]$Rid,
        [string]$OutputDir,
        [string]$ExpectedFile
    )

    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

    try {
        Invoke-Dotnet @(
            "publish", "nudge-tray.csproj",
            "-c", "Release",
            "-f", $Framework,
            "-r", $Rid,
            "-o", $OutputDir,
            "--self-contained",
            "-p:PublishSingleFile=true",
            "-p:IncludeNativeLibrariesForSelfExtract=true",
            "--nologo",
            "-v", "quiet"
        )

        if (Test-Path $ExpectedFile) {
            Write-Success "  [OK] $ExpectedFile"
        }
        else {
            Write-Warn "  [WARN] Publish completed but $ExpectedFile was not produced"
        }
    }
    catch {
        Write-Warn "  [WARN] Failed to publish $Rid"
    }
}

function Verify-Output {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        Write-Err "[ERROR] Missing expected output: $Path"
        exit 1
    }

    $sizeKb = [math]::Round((Get-Item $Path).Length / 1KB, 1)
    Write-Success "[OK] $Path ($sizeKb KB)"
}

Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  Nudge Build System (Windows /.NET 10)" -ForegroundColor White
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

if ($Clean) {
    Clean-Artifacts
}

if (-not $SkipDeps) {
    Write-Host "==========================================" -ForegroundColor Cyan
    Write-Info "Checking dependencies..."
    Write-Host ""
    Ensure-Dotnet10
    Write-Host ""
    Ensure-Python
    Write-Host ""
    Install-PythonDeps
    Write-Host ""
}
else {
    Write-Info "Skipping dependency installation (-SkipDeps)"
    Write-Host ""
    Ensure-Dotnet10
    Write-Host ""
}

Write-Host "==========================================" -ForegroundColor Cyan
Write-Info "Building projects..."
Write-Host ""

Prepare-BuildSources

Write-Host "  Restoring projects..." -ForegroundColor DarkGray
Invoke-Dotnet @("restore", "nudge.csproj", "--nologo")
Invoke-Dotnet @("restore", "nudge-notify.csproj", "--nologo")
Invoke-Dotnet @("restore", "nudge-tray.csproj", "--nologo")
Invoke-Dotnet @("restore", "NudgeCrossPlatform.Tests/NudgeCrossPlatform.Tests.csproj", "--nologo")
Invoke-Dotnet @("restore", "../TrayIconTest/TrayIconTest.csproj", "--nologo")

Write-Host "  Building projects in parallel..." -ForegroundColor DarkGray
$buildJobs = @()
$buildJobs += Start-Job -WorkingDirectory $PSScriptRoot { dotnet build nudge.csproj -c Release -f "net10.0" --no-restore --nologo -m -v quiet }
$buildJobs += Start-Job -WorkingDirectory $PSScriptRoot { dotnet build nudge-notify.csproj -c Release -f "net10.0" --no-restore --nologo -m -v quiet }
$buildJobs += Start-Job -WorkingDirectory $PSScriptRoot { dotnet build nudge-tray.csproj -c Release -f "net10.0-windows10.0.17763.0" --no-restore --nologo -m -v quiet }
$buildJobs += Start-Job -WorkingDirectory $PSScriptRoot { dotnet build ../TrayIconTest/TrayIconTest.csproj -c Release --no-restore --nologo -m -v quiet }

Wait-Job $buildJobs | Out-Null
$failedJobs = $buildJobs | Where-Object { $_.State -ne 'Completed' -or $_.ChildJobs[0].JobStateInfo.State -eq 'Failed' }
$jobOutput = Receive-Job $buildJobs 2>&1
Remove-Job $buildJobs

if ($failedJobs) {
    Write-Err "[ERROR] One or more build jobs failed:"
    $jobOutput | ForEach-Object { Write-Host $_ }
    exit 1
}

Write-Success "  [OK] Build completed"

if (-not $SkipTests) {
    Write-Host "  Running tests..." -ForegroundColor DarkGray
    Invoke-Dotnet @("test", "NudgeCrossPlatform.Tests/NudgeCrossPlatform.Tests.csproj", "-c", "Release", "--no-restore", "--nologo", "-v", "quiet")
    Write-Success "  [OK] tests"
}
else {
    Write-Warn "  [WARN] Skipping tests (-SkipTests)"
}

Write-Host "  Publishing nudge-tray runtime output..." -ForegroundColor DarkGray
Invoke-Dotnet @("publish", "nudge-tray.csproj", "-c", "Release", "-f", $WindowsTfm, "--no-self-contained", "--no-restore", "--nologo", "-v", "quiet")
Write-Success "  [OK] nudge-tray publish"

$runningProcesses = Get-Process -Name "nudge", "nudge-notify", "nudge-tray" -ErrorAction SilentlyContinue
if ($runningProcesses) {
    Write-Info "Stopping running processes..."
    foreach ($proc in $runningProcesses) {
        try {
            Stop-Process -Id $proc.Id -Force -ErrorAction Stop
        }
        catch { }
    }
    Start-Sleep -Milliseconds 500
    Write-Success "[OK] Processes stopped"
    Write-Host ""
}

Copy-Item "bin/Release/$MainTfm/nudge.exe" -Destination "." -Force
Copy-Item "bin/Release/$MainTfm/nudge.dll" -Destination "." -Force
Copy-Item "bin/Release/$MainTfm/nudge.runtimeconfig.json" -Destination "." -Force
Copy-Item "bin/Release/$MainTfm/nudge-notify.exe" -Destination "." -Force
Copy-Item "bin/Release/$MainTfm/nudge-notify.dll" -Destination "." -Force
Copy-Item "bin/Release/$MainTfm/nudge-notify.runtimeconfig.json" -Destination "." -Force
Copy-Item "bin/Release/$WindowsTfm/publish/nudge-tray.exe" -Destination "." -Force
Copy-Item "bin/Release/$WindowsTfm/publish/*.dll" -Destination "." -Force
Copy-Item "bin/Release/$WindowsTfm/publish/*.json" -Destination "." -Force
if (Test-Path "bin/Release/$WindowsTfm/publish/runtimes") {
    Copy-Item "bin/Release/$WindowsTfm/publish/runtimes" -Destination "." -Recurse -Force
}

Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Info "Building platform binaries..."
Write-Host ""
Publish-DistBinary -Framework $WindowsTfm -Rid "win-x64" -OutputDir "dist/win-x64" -ExpectedFile "dist/win-x64/nudge-tray.exe"
Publish-DistBinary -Framework $MainTfm -Rid "linux-x64" -OutputDir "dist/linux-x64" -ExpectedFile "dist/linux-x64/nudge-tray"

Write-Host ""
Write-Info "Verifying binaries..."
Verify-Output "nudge.exe"
Verify-Output "nudge-notify.exe"
Verify-Output "nudge-tray.exe"

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

if (-not $NoRun -and (Test-Path "./nudge-tray.exe")) {
    Write-Info "Launching Nudge..."
    Start-Process -FilePath "./nudge-tray.exe"
    Write-Success "[OK] nudge-tray launched"
}
