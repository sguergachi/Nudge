#Requires -Version 5.0
<#
.SYNOPSIS
    Installs Nudge by trusting its signing certificate and installing the MSIX package.
.DESCRIPTION
    MSIX packages require a trusted signing certificate. This script installs the
    Nudge certificate into your Trusted People store (current user only), then
    installs the app. Run from the folder containing Nudge.msix and Nudge.cer.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Install-Nudge.ps1
#>
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$msixPath  = Join-Path $scriptDir "Nudge.msix"
$cerPath   = Join-Path $scriptDir "Nudge.cer"

if (-not (Test-Path $msixPath)) {
    Write-Host "ERROR: Nudge.msix not found in $scriptDir"
    Write-Host "Download Nudge.msix from the release page and place it next to this script."
    exit 1
}
if (-not (Test-Path $cerPath)) {
    Write-Host "ERROR: Nudge.cer not found in $scriptDir"
    Write-Host "Download Nudge.cer from the release page and place it next to this script."
    exit 1
}

Write-Host "Installing Nudge signing certificate to Trusted People store (current user)..."
$store = New-Object System.Security.Cryptography.X509Certificates.X509Store(
    [System.Security.Cryptography.X509Certificates.StoreName]::TrustedPeople,
    [System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser
)
$store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cerPath)
$store.Add($cert)
$store.Close()
Write-Host "Certificate installed."

Write-Host "Installing Nudge..."
Add-AppxPackage -Path $msixPath
Write-Host ""
Write-Host "Nudge installed successfully! Launch it from the Start menu."
