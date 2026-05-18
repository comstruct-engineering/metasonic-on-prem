# Metasonic CLI (TypeScript)

Command-line tool for updating Metasonic DMS document qualifiers, written in TypeScript/Node.js.

## Installation

```bash
# Install dependencies
npm install

# Build
npm run build

# Optional: Install globally
npm install -g .
```

## Usage

### Basic Commands

```bash
# Get document qualifiers
npm start -- get 1527018

# Update qualifier (friendly name)
npm start -- update 1527018 sachlich richtig

# Update qualifier (ID)
npm start -- update 1527018 223 richtig

# Use live environment
npm start -- update 1527018 sachlich richtig --env live
```

### After Global Install

```bash
metasonic-cli get 1527018
metasonic-cli update 1527018 sachlich richtig
metasonic-cli --help
```

## DMS upload worker (Comstruct → MetaSonic)

Scheduled job entrypoint: polls Comstruct for invoices ready for DMS, downloads the PDF (HTTP **404** or empty body → **skip**: no Comstruct PATCH, no MetaSonic; watermark for that row is **not** advanced), then **`PATCH` `dms_upload_in_progress`**. When **`Search`** finds a match on the correlation key (`external_id` by default), it **appends** a new PDF via **`AddItem`** (existing attachments stay). On success it **PATCH**es Comstruct with `dms_uploaded` and `external_status: SYNCED`. If no document is found and `DMS_WORKER_ALLOW_CREATE_DMS_DOCUMENT` is not enabled, the run **fails** that row (expects pre-existing DMS entries).

```bash
npm run build
npm run dms:worker
```

**Dry run (no MetaSonic/Comstruct mutations beyond optional list GET):** `DMS_WORKER_DRY_RUN=1 npm run dms:worker`

### Comstruct API contract (assumed; implement in comstruct-nest)

| Action | Method | Path | Notes |
|--------|--------|------|--------|
| Queue | `GET` | `/{apiPrefix}/invoices` | Query: `status` = `COMSTRUCT_DMS_QUEUE_STATUS` (default **`CHECKED`**), optional `updated_after` (param name: `COMSTRUCT_DMS_UPDATED_AFTER_PARAM`, default `updated_after`) |
| PDF | `GET` | `/{apiPrefix}/invoices/{id}/pdf` | By default **`?annotations=true`**. **404** or empty body: worker skips the row (no lifecycle PATCH). Set **`COMSTRUCT_PDF_ANNOTATIONS=0`** or **`false`** to omit the query param |
| Lifecycle | `PATCH` | `/{apiPrefix}/invoices/{id}/dms` | Body JSON: `{ "dmsStatus", "metasonicDocumentId"?, "dmsLastError"?, "external_status"? }` — **must be added** server-side to match this worker |

### Using `CHECKED` + `updated_at` (no new invoice statuses)

You can **avoid** extra workflow values like `ready_for_dms_upload` and instead:

1. **Query** `status=CHECKED` and `updated_after=<cursor>` (Comstruct must add the `updated_after` query and filter on `invoices.updated_at` — it is not in the public DTO as of early wire-up; treat as a small Nest + DB change).
2. **Deduplicate** after upload: have Comstruct return **`external_status`** (e.g. `SYNCED`) on each list row so the worker can skip finished work — **MetaSonic Dokument id is not on the list**; it comes only from **`Search`** using **`external_id`**.

**Caveat:** Relying on `updated_at` + watermark **without** a “done” flag can repeat or miss work. Prefer **`external_status`** — **or** MetaSonic-only dedupe via **`Search`** (same `external_id`).

### Idempotency via MetaSonic `Search` (no Comstruct column)

The WSDL exposes **`Search`** with `RequestField[] searchFields` (see `ScanviewWebservice1.wsdl` / generated `Reference.cs`). If you configure a **dedicated qualifier** on the DMS document that stores a **stable key** aligned with Comstruct (typically **`external_id`**, or Comstruct’s internal `id` if the DMS was keyed that way):

1. Set **`METASONIC_COMSTRUCT_INVOICE_QUALIFIER_ID`** to that field’s ID (admin / `GetFieldProperties` on the test system).
The typical contract: **Comstruct** exposes **`external_id`** on list rows; **MetaSonic** stores the same value in a searchable qualifier and has its own internal **Dokument id** — that id is obtained only via **`Search`**, then used for **`SaveDocument`**. It is **not** expected on the Comstruct invoice list.
3. **Before** DMS write, the worker **`Search`es**. If a document exists, it calls **`AddItem`** with the PDF bytes (new attachment; does not remove prior items). If none exists and **`DMS_WORKER_ALLOW_CREATE_DMS_DOCUMENT`** is set, it may create a document with **correlation qualifier + PDF only** (no invoice-derived qualifiers).
4. On success it **PATCH**es Comstruct with `dms_uploaded`, **`metasonicDocumentId`**, and **`external_status: "SYNCED"`**.

Optional: **`METASONIC_SEARCH_USER_FOLDER_ID`** — user-folder scope for `Search` (often empty string).

`dmsStatus` values: `dms_upload_in_progress` | `dms_uploaded` | `dms_upload_failed`.

`external_status` (Comstruct field on the invoice): `SYNCED` after MetaSonic PDF upload/link succeeded, `FAILED` on failure. Omitted while `dms_upload_in_progress`.

The list response must follow `{ data: [...], total, ... }` (same shape as current `GetInvoicesResponseDto`).

### Environment

| Variable | Purpose |
|----------|---------|
| `COMSTRUCT_NEST_URL` | e.g. `http://localhost:8082` |
| `COMSTRUCT_API_KEY` | `x-api-key` for Comstruct |
| `COMSTRUCT_API_PREFIX` | Default `v1` |
| `COMSTRUCT_PDF_ANNOTATIONS` | Default **on** — PDF URL includes `?annotations=true`. Set **`0`** or **`false`** to omit it |
| `COMSTRUCT_DMS_QUEUE_STATUS` | Query `status` (default `CHECKED` — existing enum) |
| `DMS_WORKER_SKIP_IF_EXTERNAL_STATUS_SYNCED` | Default on — skip rows where list `external_status` is **`SYNCED`** |
| `COMSTRUCT_DMS_LIMIT` | Page size (default `50`, max `500`) |
| `COMSTRUCT_DMS_PATCH_PATH` | Default `/{prefix}/invoices/{id}/dms` |
| `METASONIC_ENDPOINT` | Scanview WCF base URL (VPN if needed) |
| `METASONIC_PASSWORD` / `METASONIC_USER` / `METASONIC_DB` / `METASONIC_ENV` | Same as smokes |
| `METASONIC_DOC_CLASS_ID` | Document class for new DMS files (default `1`) |
| `METASONIC_DMS_CORRELATION_KEY` | `external_id` (default) or `id` — value used for `Search` + correlation qualifier; must be present on list rows when the qualifier is configured |
| `METASONIC_COMSTRUCT_INVOICE_QUALIFIER_ID` | MetaSonic qualifier id for that correlation value; enables `Search` dedupe |
| `METASONIC_SEARCH_USER_FOLDER_ID` | Folder id for `Search` scope (default empty) |
| `DMS_WORKER_STATE_FILE` | JSON file for `lastCheckedAt` watermark (default `./.dms-worker-state.json`) |
| `DMS_WORKER_INITIAL_UPDATED_AFTER` | First-run ISO cursor if no state file |
| `DMS_WORKER_DRY_RUN` | `1` to skip upload and PATCH |
| `DMS_WORKER_ALLOW_CREATE_DMS_DOCUMENT` | `true` / `1` — if MetaSonic `Search` finds no document, create one with **correlation qualifier + PDF only** (default: off — expect existing DMS row) |

**Cron (example):** `*/10 * * * * cd /path/to/metasonic-cli-ts && /usr/bin/env … npm run dms:worker`

## Development

```bash
# Run without building
npm run dev -- get 1527018

# Watch mode
npm run watch

# Clean build
npm run clean && npm run build
```

## Testing

Unit tests do **not** call MetaSonic over the network: the SOAP client is **mocked** in `src/metasonic-client.test.ts`. `src/index-upload-cli.test.ts` builds the CLI and checks the `upload` command exits with an error when the PDF path is missing.

There is also an **integration** test (`src/mock/scanview-mock-server.test.ts`) that starts an in-process **Scanview mock** (real WSDL from `METASONIC-WEBSERVICE_DEMO/.../ScanviewWebservice1.wsdl`, stubbed operations) and runs `MetasonicClient.connect` + `getDocument`. It requires that WSDL file to exist next to `metasonic-cli-ts` in the monorepo (or set `SCANVIEW_MOCK_WSDL`).

```bash
npm test
```

Use `npm run test:watch` while changing tests or implementation.

### Real Comstruct API (local stack)

To hit **comstruct-nest** with seeded demo data (Option B), see [`docs/COMSTRUCT_OPTION_B.md`](./docs/COMSTRUCT_OPTION_B.md). After the monorepo stack is up: `npm run comstruct:smoke` with `COMSTRUCT_NEST_URL` and `COMSTRUCT_API_KEY` set.

### Metasonic DMS (live SOAP)

This calls the real **Scanview** service (`MetasonicClient.connect` + `getDocument`). It does **not** run in `npm test` (tests mock SOAP). You need a reachable WSDL/endpoint (VPN, DNS, or `METASONIC_ENDPOINT` override).

```bash
npm run build
export METASONIC_DOC_ID=1527018
# optional: METASONIC_ENV=live  METASONIC_USER=…  METASONIC_PASSWORD=…  METASONIC_DB=…  METASONIC_ENDPOINT=http://…
npm run metasonic:smoke
```

Exit code is **0** on success, **1** on failure or if `METASONIC_DOC_ID` is unset.

### Metasonic mock server (no VPN)

Use this when your real MetaSonic host is not reachable. The mock serves the same WSDL path shape as production and returns canned `Login` / `GetDocument` (and minimal `SaveDocument` / `UpdateDocumentFields`) responses.

**Prerequisite:** the file `METASONIC-WEBSERVICE_DEMO/Connected Services/ScanviewWebservice/ScanviewWebservice1.wsdl` must exist (clone layout: `MetasonicFormular/metasonic-cli-ts` next to `MetasonicFormular/METASONIC-WEBSERVICE_DEMO`). Override with `SCANVIEW_MOCK_WSDL` if the file lives elsewhere.

```bash
npm run build
# terminal 1 — default port 19089, override with SCANVIEW_MOCK_PORT
npm run metasonic:mock
# terminal 2
export METASONIC_DOC_ID=1
# path must match BasicHTTP in WSDL: /ScanviewWCF/ScanviewWebservice.svc (not /ScanviewWebservice.svc)
export METASONIC_ENDPOINT=http://127.0.0.1:19089/ScanviewWCF/ScanviewWebservice.svc
npm run metasonic:smoke
```

**Full CLI against the mock** (same env as above, then use `get` / `list` / `update` / `upload`):

```bash
export METASONIC_ENDPOINT=http://127.0.0.1:19089/ScanviewWCF/ScanviewWebservice.svc
npm start -- get 1
# or after global install: metasonic-cli get 1
```

`npm test` also runs the mock integration tests on a random port (no second terminal required): **GetDocument**, **UpdateDocumentFields** (qualifier change visible on next get), **SaveDocument** (metadata write), and **`updateDocumentFile`** (minimal PDF on disk → base64 `ItemData` in SOAP → mock **SaveDocument** round-trip). `ItemData` must be sent as base64 text (not a raw `Buffer`), or node-soap can emit invalid XML. For large production PDFs, still validate end-to-end on **VPN + real DMS** if you rely on MTOM or server-specific limits.

**Manual CLI (mock running):**

```bash
export METASONIC_ENDPOINT=http://127.0.0.1:19089/ScanviewWCF/ScanviewWebservice.svc
npm start -- update 1 223 falsch
npm start -- get 1
```

## Commands

| Command | Description |
|---------|-------------|
| `get <docid>` | Get all qualifiers |
| `update <docid> <qualifier> <value>` | Update qualifier |
| `list <docid>` | Alias for get |

## Options

| Option | Description | Default |
|--------|-------------|---------|
| `--env <test\|live>` | Environment | `test` |
| `--user <username>` | Username | from `METASONIC_USER` |
| `--pass <password>` | Password | (from env) |
| `--db <database>` | Database | from `METASONIC_DB` |

## Qualifiers

| Name | ID | Values |
|------|----|----|
| `kreditor` | 89900207 | Text |
| `belegnr` | 89900202 | Text |
| `betrag` | 89900131 | Number |
| `sachlich` | 223 | richtig/falsch/ungeprüft |
| `preislich` | 222 | richtig/falsch/ungeprüft |
| `issplit` | 234 | JA/NEIN |

## Examples

```bash
# Get document
metasonic-cli get 1527018

# Approve material verification
metasonic-cli update 1527018 sachlich richtig

# Update on live
metasonic-cli update 1527018 sachlich richtig --env live

# Custom credentials
metasonic-cli get 1527018 --user myuser --pass mypass
```

## Environment Variables

```bash
# Set password via environment variable
export METASONIC_PASSWORD="your-password"
metasonic-cli get 1527018

# Override Scanview base URL (e.g. local mock or tunnel) — same as metasonic:smoke
export METASONIC_ENDPOINT="http://127.0.0.1:19089/ScanviewWCF/ScanviewWebservice.svc"
metasonic-cli get 1
```

## Batch Operations

```bash
# Bash
for docid in 1527018 1527019 1527020; do
  metasonic-cli update "$docid" sachlich richtig
done

# Or use Node.js script
cat > batch.js << 'EOF'
const { exec } = require('child_process');
const docs = ['1527018', '1527019', '1527020'];

for (const doc of docs) {
  exec(`metasonic-cli update ${doc} sachlich richtig`, (err, stdout) => {
    console.log(stdout);
  });
}
EOF
node batch.js
```

## TypeScript API

You can also use the client programmatically:

```typescript
import { MetasonicClient } from './metasonic-client';

const client = new MetasonicClient({ environment: 'test' });
await client.connect();

// Get document
const doc = await client.getDocument('1527018');
console.log(doc.FieldList);

// Update qualifier
await client.updateQualifier('1527018', '223', 'richtig');

client.disconnect();
```

## Requirements

- Node.js 16+
- npm or yarn
- Network access to Metasonic DMS server

## Comparison with C# Version

| Feature | C# | TypeScript |
|---------|----|----|
| **Runtime** | .NET Framework | Node.js |
| **Platform** | Windows | Cross-platform |
| **Package Manager** | NuGet | npm |
| **Type Safety** | ✅ | ✅ |
| **SOAP Client** | WCF | soap library |

## License

Internal BRZ tool - not for external distribution.
