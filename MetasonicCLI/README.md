# Metasonic CLI - Document Qualifier Update Tool

Command-line interface for updating Metasonic DMS document qualifiers via WCF web service.

## Features

✅ **Get document qualifiers** - View all qualifiers for a document
✅ **Update qualifiers** - Change qualifier values via command line
✅ **Friendly names** - Use "sachlich" instead of "223"
✅ **Multiple environments** - Switch between test and live
✅ **Verification** - Automatic verification after updates

## Installation

### Build from Source

```cmd
cd MetasonicCLI
build.bat
```

The executable will be in `bin\Debug\MetasonicCLI.exe`

### Add to PATH (Optional)

Add the `bin\Debug` directory to your PATH to use `MetasonicCLI` from anywhere.

## Usage

### Get Document Qualifiers

```cmd
# View all qualifiers for a document
MetasonicCLI get 1527018

# Output:
Document ID: 1527018
Qualifiers:
--------------------------------------------------------------------------------
ID              Name                 Value
--------------------------------------------------------------------------------
89900207        Kreditor             Test Supplier GmbH
89900202        Belegnummer          INV-2026-001
89900131        Betrag               1250.50
223             Sachlich             ungeprüft
222             Preislich            ungeprüft
```

### Update Document Qualifiers

```cmd
# Update using qualifier ID
MetasonicCLI update 1527018 223 richtig

# Update using friendly name
MetasonicCLI update 1527018 sachlich richtig

# Output:
Updating document 1527018...
  Qualifier: 223 (Sachlich)
  New value: richtig
  Current value: ungeprüft
✓ Update successful
  Verified value: richtig
```

### Environment Options

```cmd
# Use live environment instead of test
MetasonicCLI update 1527018 sachlich richtig --env live

# Use custom credentials
MetasonicCLI get 1527018 --user myuser --pass mypassword --db MYDB
```

## Commands

| Command | Description | Example |
|---------|-------------|---------|
| `get <docid>` | Get all qualifiers | `MetasonicCLI get 1527018` |
| `update <docid> <qualifier> <value>` | Update a qualifier | `MetasonicCLI update 1527018 sachlich richtig` |
| `list <docid>` | List all qualifiers (alias for get) | `MetasonicCLI list 1527018` |
| `--help` | Show help | `MetasonicCLI --help` |

## Qualifier Reference

### Friendly Names

You can use friendly names instead of IDs:

| Name | ID | Description | Valid Values |
|------|----|----|--------------|
| `kreditor` | 89900207 | Creditor/Vendor name | Any text |
| `belegnr` | 89900202 | Invoice/Document number | Any text |
| `betrag` | 89900131 | Amount (net) | Numeric |
| `sachlich` | 223 | Material verification | richtig, falsch, ungeprüft |
| `preislich` | 222 | Price verification | richtig, falsch, ungeprüft |
| `issplit` | 234 | Split document indicator | JA, NEIN |

### Status Values

| German | English | Description |
|--------|---------|-------------|
| `richtig` | correct | Verification passed |
| `falsch` | incorrect | Verification failed |
| `ungeprüft` | unreviewed | Not yet reviewed |

## Options

| Option | Description | Default |
|--------|-------------|---------|
| `--env <test\|live>` | Reserved for future use | `test` |
| `--user <username>` | Username | `METASONIC_USER` |
| `--pass <password>` | Password | `METASONIC_PASSWORD` |
| `--db <database>` | Database name | `METASONIC_DB` |

## Examples

### Basic Operations

```cmd
# Get document info
MetasonicCLI get 1527018

# Approve material verification
MetasonicCLI update 1527018 sachlich richtig

# Approve price verification
MetasonicCLI update 1527018 preislich richtig

# Mark as incorrect
MetasonicCLI update 1527018 sachlich falsch
```

### Batch Operations (PowerShell)

```powershell
# Update multiple documents
$docIds = @("1527018", "1527019", "1527020")
foreach ($docId in $docIds) {
    MetasonicCLI update $docId sachlich richtig
}
```

### Batch Operations (Bash/Git Bash)

```bash
# Update documents from a list
while read docid; do
    MetasonicCLI update "$docid" sachlich richtig
done < document_ids.txt
```

### Integration with Scripts

```cmd
@echo off
REM Batch update script

set ENV=--env test
set STATUS=richtig

for %%D in (1527018 1527019 1527020) do (
    echo Updating document %%D...
    MetasonicCLI update %%D sachlich %STATUS% %ENV%
    if errorlevel 1 (
        echo ERROR updating %%D
        exit /b 1
    )
)

echo All documents updated successfully
```

## Error Handling

The CLI returns exit codes for script integration:

| Exit Code | Description |
|-----------|-------------|
| 0 | Success |
| 1 | Error (see console output) |

Example usage in scripts:

```cmd
MetasonicCLI update 1527018 sachlich richtig
if %errorlevel% neq 0 (
    echo Update failed!
    exit /b 1
)
```

## Environment Configuration

Configure with environment variables (`METASONIC_ENDPOINT`, `METASONIC_USER`, `METASONIC_PASSWORD`, `METASONIC_DB`) or pass `--user`, `--pass`, and `--db` on each invocation. Optional default when `METASONIC_ENDPOINT` is unset: `http://127.0.0.1:8089/ScanviewWebservice.svc`.
## Security Notes

⚠️ **For automation/scripts**:
- Store credentials in environment variables, not in scripts
- Use service accounts with minimal permissions
- Enable audit logging for all updates
- Restrict network access to DMS servers

## Troubleshooting

### "Document not found"

- Verify the document ID is correct
- Check you have permission to access the document
- Ensure you're connecting to the correct environment

### "Authentication failed"

- Check username and password
- Verify database name
- Ensure network connectivity to DMS server

### "Qualifier not found"

- Verify the qualifier ID or name is correct
- Use `get` command to see available qualifiers
- Check document class supports the qualifier

## API Integration

The CLI can be used as part of automated workflows:

```python
# Python example
import subprocess
import json

def update_document(doc_id, qualifier, value, env="test"):
    cmd = ["MetasonicCLI", "update", doc_id, qualifier, value, "--env", env]
    result = subprocess.run(cmd, capture_output=True, text=True)
    return result.returncode == 0

# Update document
if update_document("1527018", "sachlich", "richtig"):
    print("Update successful")
else:
    print("Update failed")
```

## Comparison with GUI Application

| Feature | GUI App (Form1) | CLI Tool |
|---------|-----------------|----------|
| **Interface** | Windows Forms | Command Line |
| **Automation** | Manual clicks | Scriptable |
| **Batch Updates** | No | Yes |
| **CI/CD Integration** | No | Yes |
| **Remote Use** | RDP required | SSH/WinRM capable |
| **Speed** | Slower (UI overhead) | Faster (direct API) |

## Technical Details

- **Framework**: .NET Framework 4.7.2
- **Protocol**: WCF SOAP over HTTP
- **Method**: `UpdateDocumentFields`
- **Authentication**: Username/password with session
- **Transport**: BasicHttpBinding

## Building from Source

### Prerequisites

- .NET Framework 4.7.2 SDK or Visual Studio 2019/2022
- Access to Metasonic DMS test/production environment

### Build Steps

```cmd
# Using build script
cd MetasonicCLI
build.bat

# Or using MSBuild directly
msbuild MetasonicCLI.csproj /p:Configuration=Release
```

### Output

- Debug: `bin\Debug\MetasonicCLI.exe`
- Release: `bin\Release\MetasonicCLI.exe`

## License

Internal BRZ tool - not for external distribution.

---

**Version**: 1.0
**Last Updated**: 2026-02-19
