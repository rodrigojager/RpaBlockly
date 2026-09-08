@echo off
setlocal
set "ROOT=%~dp0"
set "PROJECT=%~1"
if "%PROJECT%"=="" set "PROJECT=examples\RpaExemplo"
call "%ROOT%rpablockly.cmd" editor -Project "%PROJECT%"
exit /b %ERRORLEVEL%
