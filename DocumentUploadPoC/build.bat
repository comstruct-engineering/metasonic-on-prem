@echo off
setlocal
cd /d "%~dp0"

REM Build script for Metasonic Document Upload Worker
REM Supports Visual Studio MSBuild OR the built-in Windows .NET Framework 4.0 MSBuild/CSC (zero install required).

set NO_PAUSE=0
if /I "%~1"=="--no-pause" set NO_PAUSE=1

echo ========================================
echo Metasonic Document Upload Worker - Build
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

REM Try vswhere to find any VS / Build Tools installation
set VSWHERE="%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist %VSWHERE% (
    for /f "usebackq tokens=*" %%i in (`%VSWHERE% -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do (
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

REM Fallback to built-in Windows .NET Framework 4.0 csc.exe (pre-installed on every Windows Server)
if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" (
    set CSC_PATH="%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    goto :build_csc
)
if exist "%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe" (
    set CSC_PATH="%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    goto :build_csc
)

echo ERROR: Neither MSBuild nor built-in .NET Framework v4.0 csc.exe was found!
if "%NO_PAUSE%"=="0" pause
exit /b 1

:build_csc
echo Using built-in Windows .NET compiler: %CSC_PATH%
if not exist "bin\Debug" mkdir "bin\Debug"
%CSC_PATH% /nologo /target:exe /out:"bin\Debug\MetasonicDocumentUploadPoC.exe" /r:System.dll /r:System.Core.dll /r:System.Data.dll /r:System.Net.Http.dll /r:System.Runtime.Serialization.dll /r:System.ServiceModel.dll /r:System.Web.Extensions.dll /r:System.Xml.dll /r:System.Xml.Linq.dll /r:Microsoft.CSharp.dll *.cs Properties\*.cs ScanviewWebservice\*.cs
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ERROR: Compilation with csc.exe failed!
    if "%NO_PAUSE%"=="0" pause
    exit /b 1
)
if exist "App.config" copy /Y "App.config" "bin\Debug\MetasonicDocumentUploadPoC.exe.config" >nul
goto :success

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
    if "%NO_PAUSE%"=="0" pause
    exit /b 1
)

:success
echo.
echo ========================================
echo BUILD SUCCESSFUL
echo ========================================
echo.
echo Output: bin\Debug\MetasonicDocumentUploadPoC.exe
echo.
if "%NO_PAUSE%"=="0" pause
exit /b 0
