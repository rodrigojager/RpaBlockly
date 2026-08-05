@echo off
setlocal
set "ROOT=%~dp0"
set "PROJECT=%~1"
if "%PROJECT%"=="" set "PROJECT=examples\RpaExemplo"
dotnet run --project "%ROOT%src\RpaFlow.Editor\RpaFlow.Editor.csproj" -- --project-root "%ROOT%%PROJECT%"
endlocal
