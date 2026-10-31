@echo off
setlocal
cd /d "%~dp0"
call run.bat %* db-test
exit /b %ERRORLEVEL%
