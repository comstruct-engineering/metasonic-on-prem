# Quick Start - Metasonic CLI (TypeScript)

## Install & Build

```bash
# Install dependencies
npm install

# Build
npm run build
```

Or use the build script:
```bash
# Linux/Mac
./build.sh

# Windows
build.bat
```

## Usage

```bash
# Get document
npm start -- get 1527018

# Update qualifier
npm start -- update 1527018 sachlich richtig

# Use live environment
npm start -- update 1527018 sachlich richtig --env live

# Help
npm start -- --help
```

## Install Globally (Optional)

```bash
npm install -g .

# Then use directly
metasonic-cli get 1527018
metasonic-cli update 1527018 sachlich richtig
```

## Qualifiers

| Name | ID | Values |
|------|----|----|
| sachlich | 223 | richtig/falsch/ungeprüft |
| preislich | 222 | richtig/falsch/ungeprüft |
| kreditor | 89900207 | Text |
| belegnr | 89900202 | Text |

## Examples

```bash
# Basic get
npm start -- get 1527018

# Update Sachlich
npm start -- update 1527018 sachlich richtig

# Update Preislich
npm start -- update 1527018 preislich falsch

# Update on live
npm start -- update 1527018 sachlich richtig --env live
```

## Batch Processing

```bash
# Bash/Linux/Mac
for docid in 1527018 1527019 1527020; do
  npm start -- update "$docid" sachlich richtig
done

# PowerShell
1527018,1527019,1527020 | ForEach-Object {
  npm start -- update $_ sachlich richtig
}
```

## Environment Variables

Create `.env` file:
```bash
METASONIC_PASSWORD=your-password
METASONIC_ENV=test
```

Then use without --pass:
```bash
npm start -- get 1527018
```

## Requirements

- Node.js 16+
- Network access to Metasonic DMS
