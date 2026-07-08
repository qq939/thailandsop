@echo off
setlocal
powershell -ExecutionPolicy Bypass -File "%~dp0setup-mediapipe-hand-runtime.ps1" %*
exit /b %ERRORLEVEL%
