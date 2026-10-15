# Comstruct ↔ MetaSonic DMS On-Premises Sync (.NET / C#)

On-premises Windows synchronization worker and C# examples for integrating **Comstruct** with **MetaSonic Scanview DMS** (for **BRZ 7**, **BRZ 8**, and **BRZ 365** environments).

Because BRZ does not expose a third-party REST API for document workflows, Comstruct integrates directly with the **MetaSonic Scanview SOAP/WCF webservice** (`ScanviewWebservice.svc`). This repository provides the standard on-premises C# synchronization worker (`DocumentUploadPoC`) along with a CLI tool (`MetasonicCLI`) and C# code examples showing how to synchronize documents, attach verified invoice PDFs, and update document qualifiers.

For the complete functional guide (workflows, BRZ master data CSV setup, and qualifier configuration), see the Comstruct Knowledge Base article:
**[BRZ und Metasonic DMS an comstruct anbinden](https://knowledge.comstruct.com/articles/1464016790-brz-metasonic-x-comstruct-anbindung)**

---

## Standard Way to Run the Sync Worker

### Prerequisites

- **Windows Server** or workstation with **.NET Framework 4.7.2+**
- **Visual Studio Build Tools** (or Visual Studio 2019/2022) installed for compiling the worker on first run
- Outbound HTTPS access to `https://api.comstruct.com`
- Local network access to your MetaSonic Scanview endpoint (e.g. `http://YOUR_METASONIC_HOST:8089/ScanviewWebservice.svc`)

### Step 1: Configure `config.env`

Copy the template file and fill in your MetaSonic and Comstruct credentials (`config.env` is gitignored so your credentials are never committed):

```cmd
cd DocumentUploadPoC
copy config.env.example config.env
notepad config.env
```

Minimal required settings in `DocumentUploadPoC\config.env`:

```ini
# Comstruct REST API
COMSTRUCT_NEST_URL=https://api.comstruct.com
COMSTRUCT_API_PREFIX=v1
COMSTRUCT_API_KEY=your_comstruct_api_key

# MetaSonic Scanview SOAP/WCF Webservice
METASONIC_ENDPOINT=http://YOUR_METASONIC_HOST:8089/ScanviewWebservice.svc
METASONIC_USER=your_metasonic_user
METASONIC_PASSWORD=your_metasonic_password
METASONIC_DB=your_metasonic_database

# Optional: MetaSonic qualifier IDs updated after each synced invoice
METASONIC_COMSTRUCT_SYNCED_QUALIFIER_ID=
METASONIC_COMSTRUCT_URL_QUALIFIER_ID=
```

### Step 2: Run Manually (Build + Sync)

Run **`DocumentUploadPoC\run.bat`**:

```cmd
cd DocumentUploadPoC
run.bat
```

What `run.bat` does:
1. Automatically loads `config.env` from the `DocumentUploadPoC` directory (or uses system environment variables if already set).
2. Builds `bin\Debug\MetasonicDocumentUploadPoC.exe` via MSBuild if not yet compiled (pass `run.bat --rebuild` to force a clean rebuild).
3. Runs `MetasonicDocumentUploadPoC.exe worker` to synchronize all pending invoices between Comstruct and MetaSonic DMS.
4. Returns exit code `0` on success or `1` on failure (without blocking on interactive prompts).

> **Dry-Run Mode:** To test connectivity and see which invoices would be processed without modifying MetaSonic or Comstruct, set `DMS_WORKER_DRY_RUN=true` in `config.env` before running `run.bat`.

### Step 3: Schedule Automated Sync (Windows Task Scheduler)

In production, schedule `run.bat` to run every 5 minutes:

1. Open **Windows Task Scheduler** → **Create Task...**
2. **General** tab:
   - Name: `Comstruct MetaSonic DMS Sync`
   - Select **Run whether user is logged on or not**
3. **Triggers** tab:
   - **New...** → Daily → **Repeat task every: 5 minutes** for a duration of **Indefinitely**
4. **Actions** tab:
   - **New...** → Action: **Start a program**
   - **Program/script:** `C:\path\to\metasonic-on-prem\DocumentUploadPoC\run.bat`
   - **Start in:** `C:\path\to\metasonic-on-prem\DocumentUploadPoC`

---

## How Synchronization Works & C# Examples

### 1. Automated Comstruct → MetaSonic Invoice Sync (`DmsUploadWorker.cs`)

On each run, the worker (`MetasonicDocumentUploadPoC.exe worker`) performs the following steps:

1. **Fetch pending invoices from Comstruct** (`GET /v1/invoices?status=PRICE_CHECKING&limit=50&offset=0`), skipping any invoice where `external_status` is already `SYNCED`.
2. **Download the verified PDF** (`GET /v1/invoices/{id}/pdf?annotations=true&overlay=true`), which includes Comstruct's verification stamps/annotations and match-status overlay boxes.
3. **Append the verified PDF to the existing MetaSonic document** (`AddItem`), naming the attachment `<original_filename>_geprueft_von_comstruct.pdf` so the original invoice file remains untouched alongside the verified copy.
4. **Update MetaSonic document qualifiers** (`SaveDocument`), such as setting *"Geprüft von Comstruct"* (`METASONIC_COMSTRUCT_SYNCED_QUALIFIER_ID`) to `Ja` and writing the Comstruct invoice deep link (`https://app.comstruct.com/invoices/{id}`) into `METASONIC_COMSTRUCT_URL_QUALIFIER_ID`.
5. **Acknowledge sync in Comstruct** (`PATCH /v1/invoices/{id}/external-status` with `{"external_status": "SYNCED"}` and `POST /v1/invoices/callback`).

#### Example: Appending a Verified PDF & Updating Qualifiers in C#

```csharp
using System.Collections.Generic;
using System.IO;
using System.ServiceModel;
using DmsFormular1.ScanviewWebservice;
using MetasonicDocumentUploadPoC;

// 1. Connect and authenticate to MetaSonic Scanview WCF service
var binding = new BasicHttpBinding
{
    MaxReceivedMessageSize = int.MaxValue,
    MaxBufferSize = int.MaxValue
};
var endpoint = new EndpointAddress("http://YOUR_METASONIC_HOST:8089/ScanviewWebservice.svc");
var client = new ScanviewWebserviceClient(binding, endpoint);

var loginData = new LoginData
{
    AutoLogin = true,
    NoSession = true,
    UseSSO = false,
    Username = "your_metasonic_user",
    Password = "your_metasonic_password",
    LoginDatabaseName = "your_metasonic_db"
};
client.Login(ref loginData);

string metasonicDocId = "1527018";
byte[] pdfBytes = File.ReadAllBytes(@"C:\Temp\invoice_geprueft_von_comstruct.pdf");

// 2. Append the verified PDF attachment to the existing MetaSonic document
MetasonicUploadHelper.AppendDocumentPdfItem(
    client,
    ref loginData,
    metasonicDocId,
    pdfBytes,
    "invoice_geprueft_von_comstruct.pdf"
);

// 3. Update qualifiers on the MetaSonic document (e.g. Sachlich, Preislich, Comstruct URL)
var qualifierUpdates = new Dictionary<string, string>
{
    ["223"] = "richtig",                                         // Sachlich geprüft
    ["222"] = "richtig",                                         // Preislich geprüft
    ["YOUR_SYNCED_QUALIFIER_ID"] = "Ja",                         // Geprüft von Comstruct
    ["YOUR_URL_QUALIFIER_ID"] = "https://app.comstruct.com/invoices/inv_123"
};

MetasonicUploadHelper.SetDocumentQualifiers(
    client,
    ref loginData,
    metasonicDocId,
    qualifierUpdates
);
```

---

### 2. Creating a New Document with File & Metadata (`Program.cs`)

To test creating a brand-new document in MetaSonic DMS with qualifiers (Kreditor, Belegnummer, Betrag, Sachlich, Preislich) and an attached PDF:

```cmd
cd DocumentUploadPoC
build.bat
bin\Debug\MetasonicDocumentUploadPoC.exe "C:\path\to\invoice.pdf"
```

#### Example: Creating a New Document in C#

```csharp
var metadata = new[]
{
    new RequestField { ID = "89900207", Value = "Muster Lieferant GmbH" }, // Kreditor
    new RequestField { ID = "89900202", Value = "RE-2026-0042" },          // Belegnummer
    new RequestField { ID = "89900131", Value = "1250.50" },               // Betrag (Netto)
    new RequestField { ID = "223",      Value = "ungeprüft" },             // Sachlich
    new RequestField { ID = "222",      Value = "ungeprüft" }              // Preislich
};

ResponseDocument created = MetasonicUploadHelper.SaveNewDocument(
    client,
    ref loginData,
    metadata,
    File.ReadAllBytes(@"C:\path\to\invoice.pdf"),
    "invoice.pdf",
    docClassId: "1"
);
```

---

### 3. Inspecting and Updating Qualifiers via CLI (`MetasonicCLI`)

`MetasonicCLI` provides a command-line interface for inspecting documents, searching by correlation qualifier, updating individual qualifiers, or triggering the sync worker:

```cmd
cd MetasonicCLI
build.bat

REM View all qualifiers on a MetaSonic document
bin\Debug\MetasonicCLI.exe get 1527018

REM Update "Sachlich geprüft" (qualifier 223) or "Preislich geprüft" (qualifier 222)
bin\Debug\MetasonicCLI.exe update 1527018 sachlich richtig
bin\Debug\MetasonicCLI.exe update 1527018 preislich richtig

REM Search for a MetaSonic document by its correlation value (e.g. external_id)
bin\Debug\MetasonicCLI.exe search EXT-2026-001

REM Run the Comstruct → MetaSonic sync worker
bin\Debug\MetasonicCLI.exe worker
```

---

## Repository Structure

All projects are included in **`MetasonicFormular.sln`** and target **.NET Framework 4.7.2**:

| Path | Description |
|------|-------------|
| **`DocumentUploadPoC/`** | **Primary on-premises synchronization worker (`run.bat`)** and C# client library (`DmsUploadWorker.cs`, `ComstructApiClient.cs`, `MetasonicUploadHelper.cs`, `MetasonicSearchHelper.cs`). |
| **`MetasonicCLI/`** | C# command-line tool (`MetasonicCLI.exe`) for reading, searching, and updating document qualifiers (`get`, `update`, `search`, `worker`). |
| **`DmsFormular1.csproj`** (`Form1.cs`) | Windows Forms utility for viewing and updating document verification status (`Sachlich` / `Preislich`) interactively. |
| **`METASONIC-WEBSERVICE_DEMO/`** | Minimal Windows Forms demo showing raw WCF `GetDocument` and `UpdateDocumentFields` calls. |

## License

Released under the [MIT License](LICENSE).
