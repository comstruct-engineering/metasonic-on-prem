# Comstruct → MetaSonic DMS Upload Worker

Automated worker that fetches invoices from the Comstruct platform and attaches their PDFs to the corresponding documents in MetaSonic DMS.

## How It Works

1. Fetches invoices in `PRICE_CHECKING` status from the Comstruct API (skips already `SYNCED` rows)
2. For each invoice, downloads the PDF from Comstruct
3. Appends the PDF to the matching MetaSonic document (identified by `external_id`)
4. Optionally sets a boolean qualifier on the document to signal the attachment
5. Reports the result back to Comstruct (`SYNCED` or `FAILED`)

## Prerequisites

- Windows machine with **.NET Framework 4.7.2** or higher
- **Visual Studio Build Tools** (for the first build)
- Network access to both `https://api.comstruct.com` and the MetaSonic server

## Setup

1. Copy `config.env.example` to `config.env` and fill in values (see table below). `config.env` is gitignored and automatically loaded by `run.bat` when present.

2. Alternatively, open `run.bat` (or Windows system environment variables) and set the values there:

```bat
set METASONIC_ENDPOINT=http://YOUR_HOST:8089/ScanviewWebservice.svc
set METASONIC_USER=<username>
set METASONIC_PASSWORD=<password>
set METASONIC_DB=<database-name>

set COMSTRUCT_NEST_URL=https://api.comstruct.com
set COMSTRUCT_API_PREFIX=v1
set COMSTRUCT_API_KEY=<your-api-key>
```

3. Optionally, set a qualifier ID to flag synced documents in MetaSonic:

```bat
set METASONIC_COMSTRUCT_SYNCED_QUALIFIER_ID=<qualifier-id>
```

When set, the worker writes `Ja` into this qualifier after each successful upload, so users can see _"Geprüft von Comstruct: Ja"_ in the document metadata panel.

## Running

Run `run.bat` (or double-click it). It will:
- Load `config.env` if present (or fall back to environment variables in `run.bat` / Windows)
- Build the project automatically on first run if `bin\Debug\MetasonicDocumentUploadPoC.exe` is missing (pass `run.bat --rebuild` to force a clean rebuild)
- Connect to Comstruct and MetaSonic
- Process all pending invoices
- Exit with the worker exit code (safe for unattended Windows Task Scheduler runs)

## Scheduling (Optional)

To run automatically every 5–10 minutes, use **Windows Task Scheduler**:

1. Open Task Scheduler → **Create Basic Task**
2. Name: `Comstruct DMS Upload`
3. Trigger: Daily → repeat every 5 minutes for 1 day
4. Action: Start a program → point to `run.bat`, set _Start in_ to the folder path
5. Enable "Run whether user is logged on or not"

## Environment Variables

| Variable | Required | Description |
|---|---|---|
| `COMSTRUCT_NEST_URL` | Yes | Comstruct API base URL (default in code: `https://api.comstruct.com`) |
| `COMSTRUCT_API_KEY` | Yes | Comstruct API key (sent as `x-api-key`) |
| `COMSTRUCT_API_PREFIX` | Yes | API path prefix (default in code: `v1`) |
| `METASONIC_ENDPOINT` | Yes | MetaSonic Scanview SOAP URL |
| `METASONIC_USER` | Yes | MetaSonic username |
| `METASONIC_PASSWORD` | Yes | MetaSonic password |
| `METASONIC_DB` | Yes | MetaSonic database name (login screen) |
| `METASONIC_DOC_CLASS_ID` | No | Document class ID (default: `1`) |
| `METASONIC_COMSTRUCT_INVOICE_QUALIFIER_ID` | No | MetaSonic qualifier for Comstruct invoice id |
| `METASONIC_SEARCH_USER_FOLDER_ID` | No | User folder hint for searches |
| `METASONIC_DMS_CORRELATION_KEY` | No | `external_id` (default) or `id` for invoice ↔ document matching |
| `COMSTRUCT_DMS_QUEUE_STATUS` | No | Invoice status filter (default: `PRICE_CHECKING`) |
| `COMSTRUCT_DMS_LIMIT` | No | Page size 1–500 (default: `50`) |
| `COMSTRUCT_DMS_UPDATED_AFTER_PARAM` | No | Query param name for watermark (default: `updated_after`) |
| `COMSTRUCT_DMS_PATCH_PATH` | No | Path template for Comstruct status PATCH |
| `COMSTRUCT_PDF_ANNOTATIONS` | No | `false` / `0` to disable PDF annotations query param |
| `COMSTRUCT_PDF_OVERLAY` | No | `false` / `0` to disable match-status overlay boxes in the PDF (`overlay=true`) |
| `DMS_WORKER_STATE_FILE` | No | Path to worker state JSON (default: `.dms-worker-state.json` in cwd) |
| `DMS_WORKER_INITIAL_UPDATED_AFTER` | No | ISO timestamp seed for first fetch watermark |
| `DMS_WORKER_DRY_RUN` | No | `true` / `1` to avoid writes |
| `DMS_WORKER_ALLOW_CREATE_DMS_DOCUMENT` | No | `true` / `1` to create MetaSonic docs when missing |
| `METASONIC_COMSTRUCT_SYNCED_QUALIFIER_ID` | No | Qualifier ID; worker writes `Ja` after success |
| `METASONIC_COMSTRUCT_URL_QUALIFIER_ID` | No | Qualifier ID for Comstruct invoice URL |

See `config.env.example` for a full commented template.

## PDF Filename

The uploaded PDF takes the name of the first existing file in the MetaSonic document and appends `_geprueft_von_comstruct`:

```
abc.pdf  →  abc_geprueft_von_comstruct.pdf
```

If the existing item name cannot be read, it falls back to `Comstruct_Rechnung_{invoice_number}.pdf`.

## Project Structure

```
DocumentUploadPoC/
├── run.bat                   # Entry point — build + run
├── build.bat                 # Build script (MSBuild)
├── config.env.example        # Environment variable template
├── Program.cs                # CLI entry point
├── DmsUploadWorker.cs        # Main worker loop
├── ComstructApiClient.cs     # Comstruct REST API client
├── MetasonicUploadHelper.cs  # MetaSonic SOAP helpers
├── DmsEnvironment.cs         # Environment variable config
├── DocumentUploadPoC.csproj  # Project file
└── ScanviewWebservice/
    └── Reference.cs          # Generated MetaSonic WCF client
```
