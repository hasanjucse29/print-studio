@echo off
title Build Print Studio Installer (Setup.exe)
cd /d "%~dp0"

echo ===================================================
echo     Building Print Studio Windows Installer (.exe)
echo ===================================================
echo.

set "ISCC_PATH=C:\Users\Md Hasan Al Mamun\AppData\Local\Programs\Inno Setup 6\ISCC.exe"
if not exist "%ISCC_PATH%" (
    set "ISCC_PATH=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
)
if not exist "%ISCC_PATH%" (
    set "ISCC_PATH=C:\Program Files\Inno Setup 6\ISCC.exe"
)

if not exist "%ISCC_PATH%" (
    echo [ERROR] Inno Setup compiler (ISCC.exe) not found!
    echo Please install Inno Setup 6 from https://jrsoftware.org/isdl.php
    pause
    exit /b 1
)

if not exist "dist\PrintStudio.exe" (
    echo Portable executable not found in dist\. Compiling it first...
    call build_portable.bat
)

echo Compiling Inno Setup script...
"%ISCC_PATH%" installer.iss

if not exist "dist\PrintStudio_Setup_v1.0.exe" (
    echo.
    echo [ERROR] Setup compilation failed.
    pause
    exit /b 1
)

echo.
echo ===================================================
echo   Digitally Signing Installer to Prevent SmartScreen Warnings...
echo ===================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$cert = Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -like '*PrintStudio*' } | Select-Object -First 1;" ^
    "if ($cert) {" ^
    "    Set-AuthenticodeSignature -FilePath 'dist\PrintStudio_Setup_v1.0.exe' -Certificate $cert -TimestampServer 'http://timestamp.digicert.com' | Out-Null;" ^
    "    Unblock-File -Path 'dist\PrintStudio_Setup_v1.0.exe' -ErrorAction SilentlyContinue;" ^
    "    Write-Host 'Installer digitally signed and unblocked successfully!';" ^
    "}"

echo.
echo ===================================================
echo   INSTALLER CREATED SUCCESSFULLY!
echo   Setup installer available at:
echo   dist\PrintStudio_Setup_v1.0.exe
echo ===================================================

pause
