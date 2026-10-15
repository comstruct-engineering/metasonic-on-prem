@echo off
setlocal
cd /d "%~dp0"

REM ============================================================
REM  Load local config.env if present (gitignored), otherwise use
REM  values set below or in Windows system environment variables.
REM  Do not commit real secrets.
REM ============================================================
if exist "config.env" (
    for /f "usebackq eol=# tokens=1,* delims==" %%A in ("config.env") do (
        if not "%%A"=="" set "%%A=%%B"
    )
)

REM MetaSonic DMS — required (used when not already set in config.env / environment)
if not defined METASONIC_ENDPOINT set METASONIC_ENDPOINT=http://YOUR_HOST:8089/ScanviewWebservice.svc
if not defined METASONIC_USER set METASONIC_USER=
if not defined METASONIC_PASSWORD set METASONIC_PASSWORD=
if not defined METASONIC_DB set METASONIC_DB=

REM Optional MetaSonic qualifiers after successful sync:
REM set METASONIC_COMSTRUCT_SYNCED_QUALIFIER_ID=
REM set METASONIC_COMSTRUCT_URL_QUALIFIER_ID=

REM Comstruct API — set your API key
if not defined COMSTRUCT_NEST_URL set COMSTRUCT_NEST_URL=https://api.comstruct.com
if not defined COMSTRUCT_API_PREFIX set COMSTRUCT_API_PREFIX=v1
if not defined COMSTRUCT_API_KEY set COMSTRUCT_API_KEY=

REM Debugging only — uncomment to re-fetch from a watermark or wipe local state:
REM set DMS_WORKER_INITIAL_UPDATED_AFTER=2020-01-01T00:00:00Z
REM if exist ".dms-worker-state.json" del ".dms-worker-state.json"

REM ============================================================
REM Build only if executable does not exist yet (or pass --rebuild)
if /I "%~1"=="--rebuild" (
    if exist "bin" rmdir /s /q "bin"
    if exist "obj" rmdir /s /q "obj"
)

if not exist "bin\Debug\MetasonicDocumentUploadPoC.exe" (
    call build.bat --no-pause
    if %ERRORLEVEL% NEQ 0 (
        echo Build failed. Please install Visual Studio Build Tools and try again.
        exit /b 1
    )
)

bin\Debug\MetasonicDocumentUploadPoC.exe worker
exit /b %ERRORLEVEL%
