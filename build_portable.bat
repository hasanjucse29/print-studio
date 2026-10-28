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

echo Compiling PrintStudio.exe using PyInstaller...
pyinstaller PrintStudio.spec --clean --noconfirm

if exist "dist\PrintStudio.exe" (
    echo.
    echo ===================================================
    echo   BUILD SUCCESSFUL!
    echo   Portable Executable created at:
    echo   dist\PrintStudio.exe
    echo ===================================================
) else (
    echo.
    echo [ERROR] Build failed. Please check output messages above.
)

pause
