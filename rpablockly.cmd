@echo off
setlocal
pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\RpaBlockly.ps1" %*
exit /b %ERRORLEVEL%
