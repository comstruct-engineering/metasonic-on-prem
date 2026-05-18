@echo off
REM Build script for Metasonic CLI

echo ========================================
echo Metasonic CLI - Build
echo ========================================
echo.

REM Try to find MSBuild
set MSBUILD_PATH=

REM Check common MSBuild locations
if exist "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" (
    set MSBUILD_PATH="C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
    goto :build
)

if exist "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe" (
    set MSBUILD_PATH="C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"
    goto :build
)

if exist "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe" (
    set MSBUILD_PATH="C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    goto :build
)

if exist "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe" (
    set MSBUILD_PATH="C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
    goto :build
)

if exist "C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe" (
    set MSBUILD_PATH="C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe"
    goto :build
)

REM Try to use MSBuild from PATH
where msbuild >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    set MSBUILD_PATH=msbuild
    goto :build
)

echo ERROR: MSBuild not found!
echo.
echo Please install one of the following:
echo - Visual Studio 2019 or 2022 (any edition)
echo - Visual Studio Build Tools
echo - .NET Framework 4.7.2 Developer Pack
echo.
pause
exit /b 1

:build
echo Found MSBuild: %MSBUILD_PATH%
echo.

REM Clean previous build
echo Cleaning previous build...
if exist "bin" rmdir /s /q "bin"
if exist "obj" rmdir /s /q "obj"
echo.

REM Build Debug configuration
echo Building Debug configuration...
%MSBUILD_PATH% MetasonicCLI.csproj /p:Configuration=Debug /p:Platform=AnyCPU /t:Build /v:minimal

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ERROR: Build failed!
    pause
    exit /b 1
)

echo.
echo ========================================
echo BUILD SUCCESSFUL
echo ========================================
echo.
echo Output: bin\Debug\MetasonicCLI.exe
echo.
echo Quick Start:
echo   bin\Debug\MetasonicCLI --help
echo   bin\Debug\MetasonicCLI get 1527018
echo   bin\Debug\MetasonicCLI update 1527018 sachlich richtig
echo.
pause
