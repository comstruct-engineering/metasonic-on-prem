@echo off
setlocal
cd /d "%~dp0"
call run.bat %* masterdata
exit /b %ERRORLEVEL%
