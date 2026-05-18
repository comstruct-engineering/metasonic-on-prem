# Document Upload Feature

## New Command: `upload`

Update an existing document with a new file (replaces the file, keeps metadata).

## Usage

```bash
# Upload a new file to existing document
metasonic-cli upload <docid> <filepath>

# Examples
metasonic-cli upload 1527018 ./invoice.pdf
metasonic-cli upload 1527018 /path/to/updated-invoice.pdf
metasonic-cli upload 1527018 ./scan.tiff --env live
```

## What It Does

1. **Fetches current document** - Gets existing metadata and document class
2. **Reads new file** - Loads the file from your local filesystem
3. **Preserves metadata** - Keeps all existing qualifiers (Kreditor, Belegnummer, etc.)
4. **Replaces file** - Uploads new file, replacing the old one
5. **Returns result** - Shows updated document ID and confirmation

## Supported File Types

| Extension | Type Number | Description |
|-----------|-------------|-------------|
| `.pdf` | 1 | PDF documents |
| `.tif`, `.tiff` | 2 | TIFF images |
| `.jpg`, `.jpeg` | 3 | JPEG images |
| `.png` | 4 | PNG images |

## Example Output

```bash
$ metasonic-cli upload 1527018 ./new-invoice.pdf

Updating document 1527018 with new file...
  File: new-invoice.pdf
  Size: 245.67 KB
  Fetching current document...
  Current DocClass: 1
  Uploading new file...
✓ Upload successful
  Document ID: 1527018
  File uploaded: new-invoice.pdf
```

## How It Works

The `upload` command uses the `SaveDocument` WCF method:

```typescript
// 1. Get current document
const currentDoc = await client.getDocument(docId);

// 2. Read new file
const fileBuffer = fs.readFileSync(filePath);

// 3. Create update request
const document = {
  ID: docId,                           // Update existing document
  DocClassID: currentDoc.DocClassID,   // Keep document class
  FieldList: currentDoc.FieldList,     // Keep all metadata
  ItemList: [newFileItem]              // Replace file
};

// 4. Save to DMS
await client.saveDocument(document);
```

## Use Cases

### 1. Replace Scanned Document

```bash
# Original scan was poor quality, upload better scan
metasonic-cli upload 1527018 ./better-quality-scan.pdf
```

### 2. Update Invoice

```bash
# Vendor sent corrected invoice
metasonic-cli upload 1527019 ./corrected-invoice.pdf
```

### 3. Convert Format

```bash
# Replace TIFF with PDF
metasonic-cli upload 1527020 ./converted.pdf
```

### 4. Batch Upload

```bash
# PowerShell: Update multiple documents
$updates = @(
  @{docid="1527018"; file="./invoices/inv001.pdf"},
  @{docid="1527019"; file="./invoices/inv002.pdf"}
)

foreach ($u in $updates) {
  metasonic-cli upload $u.docid $u.file
}
```

## API Usage

You can also use the client programmatically:

```typescript
import { MetasonicClient } from './metasonic-client';

const client = new MetasonicClient({ environment: 'test' });
await client.connect();

// Update document with new file
const result = await client.updateDocumentFile(
  '1527018',
  './new-invoice.pdf'
);

console.log(`Updated document: ${result.ID}`);

client.disconnect();
```

## Advanced: Update File + Metadata

```typescript
import { MetasonicClient } from './metasonic-client';

const client = new MetasonicClient({ environment: 'test' });
await client.connect();

// Update file AND metadata at the same time
const result = await client.updateDocumentFile(
  '1527018',
  './new-invoice.pdf',
  [
    { ID: '89900207', Value: 'New Supplier Name' },
    { ID: '89900131', Value: '2500.00' }
  ]
);

client.disconnect();
```

## Notes

⚠️ **Important**:
- The upload replaces the existing file
- All metadata (qualifiers) are preserved by default
- Document class remains unchanged
- Original document ID stays the same

✅ **Best Practices**:
- Verify file path before uploading
- Check file size (large files may timeout)
- Test in test environment first
- Keep backup of original files

## Comparison: Update vs Upload

| Command | Changes | Use Case |
|---------|---------|----------|
| **update** | Metadata only | Change Sachlich/Preislich status |
| **upload** | File only (keeps metadata) | Replace with better scan |

## Error Handling

```bash
# File not found
$ metasonic-cli upload 1527018 ./missing.pdf
Error: File not found: ./missing.pdf

# Document not found
$ metasonic-cli upload 9999999 ./invoice.pdf
Error: Failed to get document 9999999: Document not found

# Network error
$ metasonic-cli upload 1527018 ./invoice.pdf
Error: Failed to connect to Metasonic DMS: ECONNREFUSED
```

## Testing

```bash
# 1. Build the CLI
npm run build

# 2. Create a test file
echo "test" > test.pdf

# 3. Test upload (test environment)
npm start -- upload 1527018 test.pdf

# 4. Verify in Metasonic UI
# Check that the document has the new file
```

---

**Added**: 2026-02-19
**Feature**: Document file replacement via `upload` command
