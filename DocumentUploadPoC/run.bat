@echo off
REM ============================================================
REM  Fill in values below (see config.env.example). Do not commit real secrets.
REM ============================================================

REM MetaSonic DMS — required
set METASONIC_ENDPOINT=http://YOUR_HOST:8089/ScanviewWebservice.svc
set METASONIC_USER=
set METASONIC_PASSWORD=
set METASONIC_DB=

REM Optional MetaSonic qualifiers after successful sync:
REM set METASONIC_COMSTRUCT_SYNCED_QUALIFIER_ID=
REM set METASONIC_COMSTRUCT_URL_QUALIFIER_ID=

REM Comstruct API — set your API key
set COMSTRUCT_NEST_URL=https://api.comstruct.com
set COMSTRUCT_API_PREFIX=v1
set COMSTRUCT_API_KEY=

REM Debugging only — uncomment to re-fetch from a watermark or wipe local state:
REM set DMS_WORKER_INITIAL_UPDATED_AFTER=2020-01-01T00:00:00Z
REM if exist ".dms-worker-state.json" del ".dms-worker-state.json"

REM ============================================================
REM Clean old build artifacts and rebuild
if exist "bin" rmdir /s /q "bin"
if exist "obj" rmdir /s /q "obj"
call build.bat
if %ERRORLEVEL% NEQ 0 (
    echo Build failed. Please install Visual Studio Build Tools and try again.
    pause
    exit /b 1
)

bin\Debug\MetasonicDocumentUploadPoC.exe worker
pause
