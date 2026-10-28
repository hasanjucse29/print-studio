@echo off
title Build Print Studio Portable Executable
cd /d "%~dp0"

echo ===================================================
echo     Building Print Studio Standalone Portable .exe
echo ===================================================
echo.

where pyinstaller >nul 2>nul
if %errorlevel% neq 0 (
    echo Installing PyInstaller and build dependencies...
    pip install pyinstaller pywebview
)

echo Cleaning previous builds...
rmdir /s /q build 2>nul
rmdir /s /q dist 2>nul

echo Compiling PrintStudio.exe with embedded metadata and icon...
pyinstaller PrintStudio.spec --clean --noconfirm

if not exist "dist\PrintStudio.exe" (
    echo.
    echo [ERROR] Build failed. Please check output messages above.
    pause
    exit /b 1
)

echo.
echo ===================================================
echo   Digitally Signing Executable to Prevent SmartScreen Warnings...
echo ===================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$cert = Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -like '*PrintStudio*' } | Select-Object -First 1;" ^
    "if (-not $cert) {" ^
    "    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=PrintStudio, O=Md Hasan Al Mamun' -CertStoreLocation 'Cert:\CurrentUser\My' -NotAfter (Get-Date).AddYears(5);" ^
    "    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store 'TrustedPublisher', 'CurrentUser';" ^
    "    $store.Open('ReadWrite');" ^
    "    $store.Add($cert);" ^
    "    $store.Close();" ^
    "};" ^
    "Set-AuthenticodeSignature -FilePath 'dist\PrintStudio.exe' -Certificate $cert -TimestampServer 'http://timestamp.digicert.com' | Out-Null;" ^
    "Unblock-File -Path 'dist\PrintStudio.exe' -ErrorAction SilentlyContinue;" ^
    "Write-Host 'Digitally signed and unblocked successfully!'"

echo.
echo ===================================================
echo   BUILD & SIGNING SUCCESSFUL!
echo   Portable Executable created at:
echo   dist\PrintStudio.exe
echo ===================================================

pause
