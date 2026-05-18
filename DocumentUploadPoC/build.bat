@echo off
REM Build script for Metasonic Document Upload PoC
REM Requires: .NET Framework 4.7.2 SDK or Visual Studio Build Tools

echo ========================================
echo Metasonic Document Upload PoC - Build
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

REM Try vswhere to find any VS installation
set VSWHERE="%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist %VSWHERE% (
    for /f "usebackq tokens=*" %%i in (`%VSWHERE% -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do (
        set MSBUILD_PATH="%%i"
        goto :build
    )
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
echo Or ensure MSBuild is in your PATH
pause
exit /b 1

:build
echo Found MSBuild: %MSBUILD_PATH%
echo.

REM Clean previous build
echo Cleaning previous build...
if exist "bin\Debug" rmdir /s /q "bin\Debug"
if exist "bin\Release" rmdir /s /q "bin\Release"
if exist "obj" rmdir /s /q "obj"
echo.

REM Build Debug configuration
echo Building Debug configuration...
%MSBUILD_PATH% DocumentUploadPoC.csproj /p:Configuration=Debug /p:Platform=AnyCPU /t:Build /v:minimal

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
echo Output: bin\Debug\MetasonicDocumentUploadPoC.exe
echo.
echo To run the PoC:
echo   bin\Debug\MetasonicDocumentUploadPoC.exe
echo.
echo To run with a specific file:
echo   bin\Debug\MetasonicDocumentUploadPoC.exe "path\to\your\file.pdf"
echo.
pause
