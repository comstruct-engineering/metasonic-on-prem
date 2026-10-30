@echo off
setlocal
cd /d "%~dp0"

REM ============================================================
REM  Note: MetasonicDocumentUploadPoC.exe loads config.env
REM  directly in C# so special characters (%, !, =, &) in
REM  passwords and connection strings are preserved verbatim.
REM ============================================================

REM Build only if executable does not exist yet (or pass --rebuild)
if /I "%~1"=="--rebuild" (
    if exist "bin" rmdir /s /q "bin"
    if exist "obj" rmdir /s /q "obj"
    shift
)

if not exist "bin\Debug\MetasonicDocumentUploadPoC.exe" (
    call build.bat --no-pause
    if %ERRORLEVEL% NEQ 0 (
        echo Build failed.
        exit /b 1
    )
)

set "WORKER_MODE=%~1"
if "%WORKER_MODE%"=="" (
    bin\Debug\MetasonicDocumentUploadPoC.exe worker
) else (
    bin\Debug\MetasonicDocumentUploadPoC.exe %*
)
exit /b %ERRORLEVEL%
