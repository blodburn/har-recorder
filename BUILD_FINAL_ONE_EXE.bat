@echo off
setlocal EnableExtensions
cd /d "%~dp0"

echo.
echo ============================================================
echo HarRecorder standalone final builder
echo ============================================================
echo.

set "DOTNET=dotnet"

where dotnet >nul 2>nul
if errorlevel 1 goto NO_DOTNET

goto BUILD

:NO_DOTNET
if exist "C:\Program Files\dotnet\dotnet.exe" goto DOTNET_FOUND
echo dotnet SDK was not found.
echo.
echo Install .NET SDK 8 first:
echo winget install -e --id Microsoft.DotNet.SDK.8
echo.
pause
exit /b 1

:DOTNET_FOUND
set "DOTNET=C:\Program Files\dotnet\dotnet.exe"
goto BUILD

:BUILD
echo [1/3] Cleaning old output...
if exist "%~dp0_FINAL" rmdir /s /q "%~dp0_FINAL"
if exist "%~dp0bin" rmdir /s /q "%~dp0bin"
if exist "%~dp0obj" rmdir /s /q "%~dp0obj"
mkdir "%~dp0_FINAL"

echo.
echo [2/3] Publishing one-file self-contained EXE...
"%DOTNET%" publish "%~dp0HarRecorder.csproj" -c Release -r win-x64 --self-contained true --nologo /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:EnableCompressionInSingleFile=true /p:DebugType=none /p:DebugSymbols=false

if errorlevel 1 goto BUILD_FAILED

echo.
echo [3/3] Copying final EXE only...
if not exist "%~dp0bin\Release\net8.0-windows\win-x64\publish\HarRecorder.exe" goto EXE_NOT_FOUND
copy /y "%~dp0bin\Release\net8.0-windows\win-x64\publish\HarRecorder.exe" "%~dp0_FINAL\HarRecorder.exe" >nul

echo.
echo ============================================================
echo DONE
echo ============================================================
echo.
echo Final standalone GUI EXE:
echo "%~dp0_FINAL\HarRecorder.exe"
echo.
echo Use ONLY this EXE.
echo Do NOT use HarRecorder.dll.
echo.
pause
exit /b 0

:EXE_NOT_FOUND
echo Final EXE was not found.
pause
exit /b 1

:BUILD_FAILED
echo Build failed.
pause
exit /b 1
