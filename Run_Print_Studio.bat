@echo off
title Print Studio
cd /d "%~dp0"

echo ===================================================
echo               Print Studio Launching...
echo ===================================================
echo.

where python >nul 2>nul
if %errorlevel% neq 0 (
    echo [ERROR] Python is not installed or not in PATH!
    echo Please install Python 3.10+ from python.org
    pause
    exit /b 1
)

:start_server
echo Starting Print Studio local web interface...
python app.py
echo.
echo ===================================================
echo Server stopped. Press any key to restart, or close window.
echo ===================================================
pause
goto start_server
