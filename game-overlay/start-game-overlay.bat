@echo off
setlocal
cd /d "%~dp0"
set ELECTRON_RUN_AS_NODE=

if not exist "node_modules\electron\dist\electron.exe" (
    echo Installing Electron for the first launch...
    call npm.cmd install --no-audit --no-fund
    if errorlevel 1 (
        echo Electron installation failed.
        pause
        exit /b 1
    )
)

call npm.cmd start
endlocal
