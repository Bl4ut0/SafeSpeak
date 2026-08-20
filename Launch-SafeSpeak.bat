@echo off
setlocal
title SafeSpeak Launcher
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\Start-LatestBuild.ps1"
exit /b %ERRORLEVEL%
