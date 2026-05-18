# Metasonic CLI - Quick Start Guide

## What is This?

A **command-line tool** for updating Metasonic DMS document qualifiers. It does the same thing as the GUI application (Form1), but from the command line for automation and scripting.

## Installation

```cmd
cd MetasonicCLI
build.bat
```

The executable: `bin\Debug\MetasonicCLI.exe`

## Basic Usage

### View Document Qualifiers

```cmd
MetasonicCLI get 1527018
```

**Output:**
```
Document ID: 1527018
Qualifiers:
--------------------------------------------------------------------------------
ID              Name                 Value
--------------------------------------------------------------------------------
89900207        Kreditor             Test Supplier GmbH
89900202        Belegnummer          INV-2026-001
223             Sachlich             ungeprüft
222             Preislich            ungeprüft
```

### Update a Qualifier

```cmd
# Using friendly name
MetasonicCLI update 1527018 sachlich richtig

# Using qualifier ID
MetasonicCLI update 1527018 223 richtig
```

**Output:**
```
Updating document 1527018...
  Qualifier: 223 (Sachlich)
  New value: richtig
  Current value: ungeprüft
✓ Update successful
  Verified value: richtig
```

## Common Commands

```cmd
# Get document info
MetasonicCLI get <docid>

# Update Sachlich status
MetasonicCLI update <docid> sachlich <richtig|falsch|ungeprüft>

# Update Preislich status
MetasonicCLI update <docid> preislich <richtig|falsch|ungeprüft>

# Update on live environment
MetasonicCLI update <docid> sachlich richtig --env live

# Show help
MetasonicCLI --help
```

## Qualifier Names

| Name | ID | Description | Values |
|------|----|----|------------|
| `kreditor` | 89900207 | Vendor name | Any text |
| `belegnr` | 89900202 | Invoice number | Any text |
| `betrag` | 89900131 | Amount | Numeric |
| `sachlich` | 223 | Material verification | richtig/falsch/ungeprüft |
| `preislich` | 222 | Price verification | richtig/falsch/ungeprüft |
| `issplit` | 234 | Split indicator | JA/NEIN |

## Status Values

- **richtig** - Correct/Approved
- **falsch** - Incorrect/Rejected
- **ungeprüft** - Unreviewed/Pending

## Batch Operations

### PowerShell Example

```powershell
# Update multiple documents
$documents = @("1527018", "1527019", "1527020")
foreach ($doc in $documents) {
    MetasonicCLI update $doc sachlich richtig
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Failed to update $doc"
        break
    }
}
```

### Batch Script Example

```cmd
@echo off
for %%D in (1527018 1527019 1527020) do (
    echo Updating %%D...
    MetasonicCLI update %%D sachlich richtig
    if errorlevel 1 exit /b 1
)
echo All documents updated!
```

### CSV Import Example (PowerShell)

```powershell
# CSV format: DocID,Sachlich,Preislich
# 1527018,richtig,richtig
# 1527019,richtig,falsch

Import-Csv updates.csv | ForEach-Object {
    MetasonicCLI update $_.DocID sachlich $_.Sachlich
    MetasonicCLI update $_.DocID preislich $_.Preislich
}
```

## Environment Options

### Default (explicit credentials required)

All calls need credentials via flags or env vars (`METASONIC_USER`, `METASONIC_PASSWORD`, `METASONIC_DB`). Endpoint: `METASONIC_ENDPOINT`, or `http://127.0.0.1:8089/ScanviewWebservice.svc` when unset.

```cmd
set METASONIC_ENDPOINT=http://YOUR_HOST:8089/ScanviewWebservice.svc
MetasonicCLI get 1527018 --user YOUR_USER --pass YOUR_PASS --db YOUR_DB
```

### `--env` flag

Reserved for forwards compatibility; the endpoint is controlled by `METASONIC_ENDPOINT` only.

### Custom Credentials

```cmd
MetasonicCLI get 1527018 --user myuser --pass mypass --db MYDB
```

## Exit Codes

- **0** - Success
- **1** - Error

Use in scripts:

```cmd
MetasonicCLI update 1527018 sachlich richtig
if %errorlevel% neq 0 (
    echo Update failed
    exit /b 1
)
```

## Integration Examples

### CI/CD Pipeline (Azure DevOps)

```yaml
- task: CmdLine@2
  displayName: 'Update DMS Documents'
  inputs:
    script: |
      MetasonicCLI update $(DocumentID) sachlich richtig --env live
```

### PowerShell Function

```powershell
function Update-MetasonicDocument {
    param(
        [string]$DocID,
        [ValidateSet("richtig","falsch","ungeprüft")]
        [string]$Sachlich,
        [ValidateSet("richtig","falsch","ungeprüft")]
        [string]$Preislich,
        [ValidateSet("test","live")]
        [string]$Environment = "test"
    )

    $envArg = if ($Environment -eq "live") { "--env live" } else { "" }

    if ($Sachlich) {
        & MetasonicCLI update $DocID sachlich $Sachlich $envArg
        if ($LASTEXITCODE -ne 0) { return $false }
    }

    if ($Preislich) {
        & MetasonicCLI update $DocID preislich $Preislich $envArg
        if ($LASTEXITCODE -ne 0) { return $false }
    }

    return $true
}

# Usage
Update-MetasonicDocument -DocID "1527018" -Sachlich "richtig" -Preislich "richtig"
```

## Comparison: GUI vs CLI

| Feature | GUI Application | CLI Tool |
|---------|-----------------|----------|
| **Interface** | Click buttons | Type commands |
| **Speed** | Slower | Faster |
| **Automation** | ❌ No | ✅ Yes |
| **Batch Updates** | Manual one-by-one | Script multiple |
| **CI/CD** | ❌ Can't use | ✅ Can use |
| **Remote** | RDP required | SSH/WinRM |
| **Logging** | Manual | Automatic (stdout) |

## Use Cases

### ✅ Good For

- **Automation** - Update documents from scripts
- **Batch Operations** - Process multiple documents
- **CI/CD** - Integrate with build pipelines
- **Remote Operations** - Run over SSH/WinRM
- **Scheduled Tasks** - Windows Task Scheduler
- **API Integration** - Call from other applications

### ❌ Not Good For

- **Interactive Review** - Use GUI for visual inspection
- **Complex Workflows** - GUI better for decision-making
- **Document Upload** - CLI focuses on updates only

## Troubleshooting

### Document Not Found

```
Error: Document not found
```

**Solution**: Verify document ID and environment

### Authentication Failed

```
Error: Authentication failed
```

**Solution**: Check credentials with `--user` and `--pass`

### Qualifier Not Found

```
Error: Invalid qualifier ID
```

**Solution**: Use `get` command to see available qualifiers

## Advanced Features

### Silent Mode (for scripts)

```powershell
# Suppress console output, check exit code only
$null = MetasonicCLI update 1527018 sachlich richtig 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "Success"
}
```

### Error Handling

```powershell
try {
    $result = & MetasonicCLI update 1527018 sachlich richtig 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Update failed: $result"
    }
} catch {
    Write-Error "Error updating document: $_"
    exit 1
}
```

### Parallel Processing

```powershell
# Update documents in parallel
$documents = @("1527018", "1527019", "1527020")
$documents | ForEach-Object -Parallel {
    & MetasonicCLI update $_ sachlich richtig --env live
} -ThrottleLimit 5
```

## Security

⚠️ **Important**:
- Don't hardcode passwords in scripts
- Use environment variables: `$env:DMS_PASSWORD`
- Restrict access to scripts with credentials
- Enable audit logging
- Use service accounts with minimal permissions

**Example**:
```powershell
$password = $env:DMS_PASSWORD
MetasonicCLI update 1527018 sachlich richtig --pass $password
```

## Support

- **Documentation**: See `MetasonicCLI/README.md`
- **Help**: `MetasonicCLI --help`
- **Issues**: Report to BRZ IT department

---

**Created**: 2026-02-19
**Version**: 1.0
**Status**: ✅ Ready to Use
