#!/bin/bash

echo "========================================"
echo "Metasonic CLI (TypeScript) - Build"
echo "========================================"
echo ""

# Check if Node.js is installed
if ! command -v node &> /dev/null; then
    echo "Error: Node.js is not installed"
    echo "Please install Node.js 16+ from https://nodejs.org/"
    exit 1
fi

echo "Node.js version: $(node --version)"
echo "npm version: $(npm --version)"
echo ""

# Install dependencies
echo "Installing dependencies..."
npm install

if [ $? -ne 0 ]; then
    echo ""
    echo "Error: Failed to install dependencies"
    exit 1
fi

echo ""
echo "Building TypeScript..."
npm run build

if [ $? -ne 0 ]; then
    echo ""
    echo "Error: Build failed"
    exit 1
fi

echo ""
echo "========================================"
echo "BUILD SUCCESSFUL"
echo "========================================"
echo ""
echo "Output: dist/index.js"
echo ""
echo "Quick Start:"
echo "  npm start -- get 1527018"
echo "  npm start -- update 1527018 sachlich richtig"
echo "  npm start -- --help"
echo ""
echo "Or install globally:"
echo "  npm install -g ."
echo "  metasonic-cli --help"
echo ""
