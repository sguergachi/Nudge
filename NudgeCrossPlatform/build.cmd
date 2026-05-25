@echo off
:: Wrapper that runs build.ps1 with execution policy bypass.
:: On a fresh Windows 10 the default policy blocks unsigned scripts;
:: this lets users build without changing system-wide policy.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
