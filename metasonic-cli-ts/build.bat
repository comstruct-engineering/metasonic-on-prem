@echo off
REM Build script for Metasonic CLI (TypeScript)

echo ========================================
echo Metasonic CLI (TypeScript) - Build
echo ========================================
echo.

REM Check if Node.js is installed
where node >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo Error: Node.js is not installed
    echo Please install Node.js 16+ from https://nodejs.org/
    pause
    exit /b 1
)

echo Node.js version:
node --version
echo npm version:
npm --version
echo.

REM Install dependencies
echo Installing dependencies...
call npm install

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Error: Failed to install dependencies
    pause
    exit /b 1
)

echo.
echo Building TypeScript...
call npm run build

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Error: Build failed
    pause
    exit /b 1
)

echo.
echo ========================================
echo BUILD SUCCESSFUL
echo ========================================
echo.
echo Output: dist\index.js
echo.
echo Quick Start:
echo   npm start -- get 1527018
echo   npm start -- update 1527018 sachlich richtig
echo   npm start -- --help
echo.
echo Or install globally:
echo   npm install -g .
echo   metasonic-cli --help
echo.
pause
