# Build Script for Nudge (PowerShell/Windows)
#
# Usage:
#   .\build.ps1               Build with dependency checks
#   .\build.ps1 -Clean        Remove generated build artifacts first
#   .\build.ps1 -SkipDeps     Skip dependency installation helpers
#   .\build.ps1 -SkipTests    Skip dotnet test
#   .\build.ps1 -Platform     Also publish self-contained platform binaries (win-x64, linux-x64)

param(
    [switch]$Clean,
    [switch]$SkipDeps,
    [switch]$SkipTests,
    [switch]$Platform
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$DotnetMajorRequired = 10
$MainTfm = "net10.0"
$WindowsTfm = "net10.0-windows10.0.17763.0"

# Bundled Python runtime (python-build-standalone)
$PythonRuntimeVersion = "3.11.15"
$PythonRuntimeDate = "20260510"
$PythonRuntimeBaseUrl = "https://github.com/astral-sh/python-build-standalone/releases/download/${PythonRuntimeDate}"
$PythonRuntimeDir = "python-runtime"

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
    Remove-Item -Path "bin", "obj", "dist", "NudgeCrossPlatform.Tests/bin", "NudgeCrossPlatform.Tests/obj" -Recurse -Force -ErrorAction SilentlyContinue

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

function Assert-Winget {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        Write-Err "[ERROR] winget (Windows Package Manager) is not available."
        Write-Warn "  winget ships with Windows 10 1809+ via the App Installer."
        Write-Warn "  Install it from the Microsoft Store: 'App Installer'"
        Write-Warn "  Or install .NET 10 manually from: https://dotnet.microsoft.com/download/dotnet/10.0"
        exit 1
    }
}

function Install-Dotnet10 {
    Assert-Winget
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
    $script:PythonCommand = $null

    foreach ($candidate in @("py", "python")) {
        if (Get-Command $candidate -ErrorAction SilentlyContinue) {
            try {
                $version = (& $candidate --version 2>&1 | Out-String).Trim()
                if ($version -match '^Python \d+\.\d+\.\d+') {
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
    Assert-Winget
    winget install Python.Python.3.12 --silent --accept-package-agreements --accept-source-agreements
    Refresh-Path

    foreach ($candidate in @("py", "python")) {
        if (Get-Command $candidate -ErrorAction SilentlyContinue) {
            try {
                $version = (& $candidate --version 2>&1 | Out-String).Trim()
                if ($version -match '^Python \d+\.\d+\.\d+') {
                    $script:PythonCommand = $candidate
                    Write-Success "[OK] Python ready"
                    return
                }
            }
            catch { }
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

function Bundle-PythonRuntime {
    param([string]$Rid)
    if (Test-Path "$PythonRuntimeDir/bin/python3") {
        Write-Host "  Bundled Python runtime already present, skipping download." -ForegroundColor DarkGray
        return
    }
    if (Test-Path "$PythonRuntimeDir/python.exe") {
        Write-Host "  Bundled Python runtime already present, skipping download." -ForegroundColor DarkGray
        return
    }
    $url = if ($Rid -eq "win-x64") {
        "${PythonRuntimeBaseUrl}/cpython-${PythonRuntimeVersion}+${PythonRuntimeDate}-x86_64-pc-windows-msvc-install_only.tar.gz"
    } else {
        "${PythonRuntimeBaseUrl}/cpython-${PythonRuntimeVersion}+${PythonRuntimeDate}-x86_64-unknown-linux-gnu-install_only.tar.gz"
    }
    Write-Info "Downloading bundled Python runtime for $Rid..."
    Write-Host "  $url" -ForegroundColor DarkGray
    $tmpTar = Join-Path $env:TEMP "nudge-python-runtime.tar.gz"
    Invoke-WebRequest -Uri $url -OutFile $tmpTar -UseBasicParsing
    New-Item -ItemType Directory -Force -Path $PythonRuntimeDir | Out-Null
    tar -xzf $tmpTar --strip-components=1 -C $PythonRuntimeDir
    Remove-Item $tmpTar -Force
    Write-Success "[OK] Bundled Python runtime ready"
}

function Bundle-PythonRuntimeForDist {
    param([string]$Rid, [string]$DestDir)
    $runtimeDest = Join-Path $DestDir "python-runtime"
    # Skip if already present in dest
    if ((Test-Path "$runtimeDest/bin/python3") -or (Test-Path "$runtimeDest/python.exe")) {
        Write-Host "  Bundled Python already in $DestDir, skipping." -ForegroundColor DarkGray
        return
    }
    $url = if ($Rid -eq "win-x64") {
        "${PythonRuntimeBaseUrl}/cpython-${PythonRuntimeVersion}+${PythonRuntimeDate}-x86_64-pc-windows-msvc-install_only.tar.gz"
    } else {
        "${PythonRuntimeBaseUrl}/cpython-${PythonRuntimeVersion}+${PythonRuntimeDate}-x86_64-unknown-linux-gnu-install_only.tar.gz"
    }
    Write-Info "  Downloading bundled Python runtime for $Rid..."
    Write-Host "    $url" -ForegroundColor DarkGray
    $tmpTar = Join-Path $env:TEMP "nudge-python-runtime-$Rid.tar.gz"
    Invoke-WebRequest -Uri $url -OutFile $tmpTar -UseBasicParsing
    New-Item -ItemType Directory -Force -Path $runtimeDest | Out-Null
    tar -xzf $tmpTar --strip-components=2 -C $runtimeDest
    Remove-Item $tmpTar -Force
    Write-Success "  [OK] Bundled Python runtime → $runtimeDest"
}

function Prepare-BuildSources {
    Write-Host "  Preparing shebang-stripped sources..." -ForegroundColor DarkGray
    $body = [System.IO.File]::ReadAllText((Resolve-Path "nudge.cs"), [System.Text.Encoding]::UTF8) -replace '^#!.*\r?\n', ''
    [System.IO.File]::WriteAllText((Join-Path $PSScriptRoot "nudge_build.cs"), $body, [System.Text.Encoding]::UTF8)
    $body = [System.IO.File]::ReadAllText((Resolve-Path "nudge-notify.cs"), [System.Text.Encoding]::UTF8) -replace '^#!.*\r?\n', ''
    [System.IO.File]::WriteAllText((Join-Path $PSScriptRoot "nudge-notify_build.cs"), $body, [System.Text.Encoding]::UTF8)
}

function Invoke-Dotnet {
    param([string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed"
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

Write-Host "  Building projects in parallel..." -ForegroundColor DarkGray
$buildProcs = @(
    (Start-Process -FilePath "dotnet" -ArgumentList @("build", "nudge.csproj", "-c", "Release", "-f", "net10.0", "--no-restore", "--nologo", "-m", "-v", "quiet") -NoNewWindow -PassThru),
    (Start-Process -FilePath "dotnet" -ArgumentList @("build", "nudge-notify.csproj", "-c", "Release", "-f", "net10.0", "--no-restore", "--nologo", "-m", "-v", "quiet") -NoNewWindow -PassThru),
    (Start-Process -FilePath "dotnet" -ArgumentList @("build", "nudge-tray.csproj", "-c", "Release", "-f", "net10.0-windows10.0.17763.0", "--no-restore", "--nologo", "-m", "-v", "quiet") -NoNewWindow -PassThru)
)
$buildProcs | Wait-Process
$failedProcs = $buildProcs | Where-Object { $_.ExitCode -ne 0 }
if ($failedProcs) {
    Write-Err "[ERROR] Build failed:"
    $failedProcs | ForEach-Object { Write-Host "  $($_.StartInfo.FileName) $($_.StartInfo.Arguments) exited with code $($_.ExitCode)" -ForegroundColor Red }
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
Invoke-Dotnet @("publish", "nudge-tray.csproj", "-c", "Release", "-f", $WindowsTfm, "--no-self-contained", "--no-restore", "--no-build", "--nologo", "-v", "quiet")
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

if ($Platform) {
    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Cyan
    Write-Info "Building platform binaries..."
    Write-Host ""

    function Publish-Dist {
        param([string]$Rid, [string]$TfmTray, [string]$Dir, [string]$ExeExt)
        Remove-Item -Path $Dir -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Path $Dir -Force | Out-Null

        # Publish each project to temp subdirectories (avoids cross-contamination)
        Invoke-Dotnet @("publish", "nudge.csproj",        "-c", "Release", "-f", "net10.0", "-r", $Rid, "-o", "$Dir/_.nudge",     "--self-contained", "-p:PublishSingleFile=true", "--nologo", "-v", "quiet")
        Invoke-Dotnet @("publish", "nudge-notify.csproj", "-c", "Release", "-f", "net10.0", "-r", $Rid, "-o", "$Dir/_.notify",   "--self-contained", "-p:PublishSingleFile=true", "--nologo", "-v", "quiet")
        Invoke-Dotnet @("publish", "nudge-tray.csproj",   "-c", "Release", "-f", $TfmTray, "-r", $Rid, "-o", "$Dir/_.tray",   "--self-contained", "-p:PublishSingleFile=true", "-p:SkipBuildNudgeDaemon=true", "--nologo", "-v", "quiet")

        # Copy single-file exes + all native assets from tray.d
        Copy-Item "$Dir/_.nudge/nudge$ExeExt"    $Dir
        Copy-Item "$Dir/_.notify/nudge-notify$ExeExt" $Dir
        Copy-Item "$Dir/_.tray/nudge-tray$ExeExt" $Dir
        Get-ChildItem "$Dir/_.tray" | Where-Object { $_.Name -notlike "nudge-tray*" } | Copy-Item -Destination $Dir -Force

        # Copy Python support files
        Copy-Item "model_inference.py", "train_model.py", "background_trainer.py", "generate_sample_data.py", "requirements-cpu.txt", "requirements.txt" -Destination $Dir -Force

        # Bundle the pretrained V1 model so AI predictions work from first launch.
        # The trainer refines/replaces it once real labels accumulate.
        if (Test-Path "model") {
            $modelDest = Join-Path $Dir "model"
            New-Item -ItemType Directory -Force -Path $modelDest | Out-Null
            Copy-Item "model\productivity_model.joblib", "model\scaler.json", "model\trainer_meta.json" -Destination $modelDest -Force -ErrorAction SilentlyContinue
        }

        # Download and bundle the self-contained Python runtime
        Bundle-PythonRuntimeForDist -Rid $Rid -DestDir $Dir

        Remove-Item "$Dir/_.nudge", "$Dir/_.notify", "$Dir/_.tray" -Recurse -Force
        Write-Success "  [OK] $Rid → $Dir"
    }

    Publish-Dist -Rid "win-x64"  -TfmTray $WindowsTfm -Dir "dist/win-x64"   -ExeExt ".exe"
    Publish-Dist -Rid "linux-x64" -TfmTray $MainTfm    -Dir "dist/linux-x64" -ExeExt ""

    Write-Success "[OK] Platform binaries complete"
}

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


