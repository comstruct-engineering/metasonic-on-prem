@echo off
setlocal
cd /d "%~dp0"

REM ============================================================
REM  BRZ & MetaSonic Inspection / Read Helper Script
REM  Automatically uses the compiled C# exe if available, or falls
REM  back to the zero-compile PowerShell script (brz-sync.ps1).
REM ============================================================

set "SUBCMD=%~1"
if /I "%SUBCMD%"=="" set "SUBCMD=test"

REM If C# executable is already built or MSBuild is installed, try run.bat for DMS commands
if /I "%SUBCMD%"=="dms-login" (
    call run.bat dms-login
    exit /b %ERRORLEVEL%
)

if /I "%SUBCMD%"=="dms-classes" (
    call run.bat dms-classes "%~2"
    exit /b %ERRORLEVEL%
)

if /I "%SUBCMD%"=="dms-inspect" (
    call run.bat dms-inspect "%~2"
    exit /b %ERRORLEVEL%
)

if exist "bin\Debug\MetasonicDocumentUploadPoC.exe" (
    if /I "%SUBCMD%"=="test" (
        call run.bat db-test
        exit /b %ERRORLEVEL%
    )
    if /I "%SUBCMD%"=="tables" (
        call run.bat db-tables "%~2"
        exit /b %ERRORLEVEL%
    )
    if /I "%SUBCMD%"=="query" (
        call run.bat db-query "%~2" "%~3"
        exit /b %ERRORLEVEL%
    )
    if /I "%SUBCMD%"=="sync" (
        call run.bat masterdata
        exit /b %ERRORLEVEL%
    )
    if /I "%SUBCMD%"=="odata" (
        call run.bat odata "%~2" "%~3"
        exit /b %ERRORLEVEL%
    )
    if /I "%SUBCMD%"=="odata-sync" (
        call run.bat odata-sync "%~2"
        exit /b %ERRORLEVEL%
    )
)

REM Zero-compile fallback: run standalone PowerShell script directly
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0brz-sync.ps1" %*
exit /b %ERRORLEVEL%
