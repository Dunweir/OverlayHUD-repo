@echo off
setlocal
cd /d "%~dp0"
set ELECTRON_RUN_AS_NODE=

if not exist ".electron-cache\electron-v42.4.1-win32-x64.zip" (
    if not exist ".electron-cache" mkdir ".electron-cache"
    echo Downloading Electron runtime...
    curl.exe -L --retry 3 -o ".electron-cache\electron-v42.4.1-win32-x64.zip" "https://github.com/electron/electron/releases/download/v42.4.1/electron-v42.4.1-win32-x64.zip"
    if errorlevel 1 exit /b 1
)

call npm.cmd install --no-audit --no-fund
if errorlevel 1 exit /b 1

call npm.cmd run package
if errorlevel 1 exit /b 1

copy /y "overlay-config.json" "dist\REPOGameOverlay-win32-x64\overlay-config.json" >nul
copy /y "icon.png" "dist\REPOGameOverlay-win32-x64\icon.png" >nul

echo Portable overlay is ready in dist\REPOGameOverlay-win32-x64
endlocal
