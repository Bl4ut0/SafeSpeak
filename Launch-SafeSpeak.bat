@echo off
setlocal
title SafeSpeak – Dev Launcher
color 0A

echo ============================================
echo   SafeSpeak Dev Launcher
echo ============================================
echo.

REM ── Stop any running instance ──────────────────────────────────────
echo [1/3] Stopping any running SafeSpeak process...
taskkill /IM SafeSpeak.App.exe /F >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo       Stopped previous instance.
    timeout /t 1 /nobreak >nul
) else (
    echo       No running instance found.
)

REM ── Build the solution (Debug) ─────────────────────────────────────
echo.
echo [2/3] Building SafeSpeak (Debug)...
echo.
dotnet build "%~dp0SafeSpeak.sln" --configuration Debug --verbosity minimal
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo  *** BUILD FAILED – fix the errors above and try again ***
    echo.
    pause
    exit /b 1
)

REM ── Launch the freshly-built binary ────────────────────────────────
echo.
echo [3/3] Launching SafeSpeak...
set "EXE=%~dp0src\SafeSpeak.App\bin\Debug\net8.0-windows10.0.19041.0\SafeSpeak.App.exe"
if not exist "%EXE%" (
    set "EXE=%~dp0src\SafeSpeak.App\bin\Debug\net8.0-windows\SafeSpeak.App.exe"
)

if not exist "%EXE%" (
    echo.
    echo  *** ERROR: Built executable not found at:
    echo       %EXE%
    echo  *** Verify the build output above.
    echo.
    pause
    exit /b 1
)

start "" "%EXE%"
echo       SafeSpeak is running.
echo.
echo ============================================
echo   Done – you can close this window.
echo ============================================
exit /b 0
