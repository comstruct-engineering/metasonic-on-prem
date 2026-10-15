# Comstruct ↔ MetaSonic DMS On-Premise-Synchronisation (.NET / C#)

On-Premise-Synchronisations-Worker für Windows sowie C#-Beispiele zur Anbindung von **Comstruct** an das **MetaSonic Scanview DMS** (für **BRZ 7**, **BRZ 8** und **BRZ 365**).

Da BRZ keine offene Drittanbieter-REST-API für Dokumenten-Workflows bereitstellt, erfolgt die Integration direkt über den **MetaSonic Scanview SOAP/WCF-Webservice** (`ScanviewWebservice.svc`). Dieses Repository enthält den standardmäßigen C#-Synchronisations-Worker (`DocumentUploadPoC`), ein Kommandozeilen-Tool (`MetasonicCLI`) sowie C#-Codebeispiele zum Synchronisieren von Belegen, Anhängen geprüfter Rechnungs-PDFs und Aktualisieren von Qualifizierern (Metadatenfeldern).

Eine vollständige fachliche Übersicht (Integrationsvarianten, BRZ-Stammdaten-CSV-Austausch und Qualifizierer-Konfiguration) finden Sie im Comstruct Knowledge-Base-Artikel:
**[BRZ und Metasonic DMS an comstruct anbinden: Rechnungen, Lieferscheine und Stammdaten synchronisieren](https://knowledge.comstruct.com/articles/1464016790-brz-metasonic-x-comstruct-anbindung)**

---

## Standard-Ausführung (On-Premise Windows Worker)

### Voraussetzungen

- **Windows Server** (oder Windows-Workstation) mit **.NET Framework 4.7.2** oder höher
- **Visual Studio Build Tools** (oder Visual Studio 2019/2022) zum einmaligen Kompilieren des Workers beim ersten Start
- Ausgehender HTTPS-Zugriff auf `https://api.comstruct.com` (Port 443)
- Netzwerkzugriff auf den lokalen MetaSonic Scanview-Endpunkt (z. B. `http://IHR_METASONIC_HOST:8089/ScanviewWebservice.svc`)

### Schritt 1: `config.env` konfigurieren

Kopieren Sie die Beispiel-Konfigurationsdatei und tragen Sie Ihre Zugangsdaten für MetaSonic und Comstruct ein (`config.env` ist in `.gitignore` ausgeschlossen, sodass keine Zugangsdaten versehentlich committet werden):

```cmd
cd DocumentUploadPoC
copy config.env.example config.env
notepad config.env
```

Erforderliche Mindesteinstellungen in `DocumentUploadPoC\config.env`:

```ini
# Comstruct REST API
COMSTRUCT_NEST_URL=https://api.comstruct.com
COMSTRUCT_API_PREFIX=v1
COMSTRUCT_API_KEY=ihr_comstruct_api_key

# MetaSonic Scanview SOAP/WCF-Webservice
METASONIC_ENDPOINT=http://IHR_METASONIC_HOST:8089/ScanviewWebservice.svc
METASONIC_USER=ihr_metasonic_benutzer
METASONIC_PASSWORD=ihr_metasonic_passwort
METASONIC_DB=ihre_metasonic_datenbank

# Optional: MetaSonic Qualifizierer-IDs, die nach erfolgreichem Sync gesetzt werden
METASONIC_COMSTRUCT_SYNCED_QUALIFIER_ID=
METASONIC_COMSTRUCT_URL_QUALIFIER_ID=
```

### Schritt 2: Manuell ausführen (Kompilieren + Synchronisieren)

Führen Sie **`DocumentUploadPoC\run.bat`** aus:

```cmd
cd DocumentUploadPoC
run.bat
```

Was `run.bat` automatisch ausführt:
1. Lädt die lokale `config.env` aus dem Verzeichnis `DocumentUploadPoC` (oder nutzt bestehende Windows-Systemumgebungsvariablen).
2. Kompiliert `bin\Debug\MetasonicDocumentUploadPoC.exe` über MSBuild, sofern die `.exe` noch nicht existiert (mit `run.bat --rebuild` erzwingen Sie jederzeit einen sauberen Neu-Build).
3. Startet `MetasonicDocumentUploadPoC.exe worker` und synchronisiert alle offenen Rechnungen zwischen Comstruct und MetaSonic DMS.
4. Beendet sich direkt mit Exit-Code `0` (Erfolg) bzw. `1` (Fehler) ohne blockierende `pause`-Abfrage.

> **Dry-Run-Modus (Testlauf ohne Schreibzugriffe):** Um die Verbindung zu testen und zu prüfen, welche Rechnungen verarbeitet würden, ohne Daten in MetaSonic oder Comstruct zu verändern, setzen Sie `DMS_WORKER_DRY_RUN=true` in der `config.env` und starten Sie `run.bat`.

### Schritt 3: Automatische Ausführung einrichten (Windows Aufgabenplanung)

Für den produktiven Betrieb wird `run.bat` über die **Windows Aufgabenplanung (Task Scheduler)** alle 5 Minuten ausgeführt:

1. **Aufgabenplanung** öffnen → **Aufgabe erstellen...**
2. Reiter **Allgemein**:
   - Name: `Comstruct MetaSonic DMS Sync`
   - Option **Unabhängig von der Benutzeranmeldung ausführen** aktivieren
3. Reiter **Trigger**:
   - **Neu...** → Täglich → **Wiederholen jede: 5 Minuten** für die Dauer **Unbegrenzt**
4. Reiter **Aktionen**:
   - **Neu...** → Aktion: **Programm starten**
   - **Programm/Skript:** `C:\Pfad\zu\metasonic-on-prem\DocumentUploadPoC\run.bat`
   - **Starten in:** `C:\Pfad\zu\metasonic-on-prem\DocumentUploadPoC`

---

## Ablauf der Synchronisation & C#-Codebeispiele

### 1. Automatische Rechnungs-Synchronisation Comstruct → MetaSonic (`DmsUploadWorker.cs`)

Bei jedem Durchlauf führt der Worker (`MetasonicDocumentUploadPoC.exe worker`) folgende Schritte aus:

1. **Abruf zu synchronisierender Rechnungen aus Comstruct** (`GET /v1/invoices?status=PRICE_CHECKING&limit=50&offset=0`), wobei bereits synchronisierte Belege (`external_status = SYNCED`) übersprungen werden.
2. **Download des geprüften Rechnungs-PDFs** (`GET /v1/invoices/{id}/pdf?annotations=true&overlay=true`) inklusive Prüfvermerken/Annotationen und Abgleich-Markierungen aus Comstruct.
3. **Anhängen des geprüften PDFs an das bestehende MetaSonic-Dokument** (`AddItem`) unter dem Dateinamen `<Originaldateiname>_geprueft_von_comstruct.pdf`. Das ursprüngliche Rechnungsdokument bleibt unverändert erhalten.
4. **Aktualisieren der Qualifizierer in MetaSonic** (`SaveDocument`), z. B. Setzen von *„Geprüft von Comstruct"* (`METASONIC_COMSTRUCT_SYNCED_QUALIFIER_ID`) auf `Ja` sowie Speichern des direkten Comstruct-Beleglinks (`https://app.comstruct.com/invoices/{id}`) in `METASONIC_COMSTRUCT_URL_QUALIFIER_ID`.
5. **Rückmeldung des Synchronisationsstatus an Comstruct** (`PATCH /v1/invoices/{id}/external-status` mit `{"external_status": "SYNCED"}` sowie `POST /v1/invoices/callback`).

#### C#-Beispiel: Geprüftes PDF anhängen & Qualifizierer aktualisieren

```csharp
using System.Collections.Generic;
using System.IO;
using System.ServiceModel;
using DmsFormular1.ScanviewWebservice;
using MetasonicDocumentUploadPoC;

// 1. Verbindung zum MetaSonic Scanview WCF-Webservice aufbauen und anmelden
var binding = new BasicHttpBinding
{
    MaxReceivedMessageSize = int.MaxValue,
    MaxBufferSize = int.MaxValue
};
var endpoint = new EndpointAddress("http://IHR_METASONIC_HOST:8089/ScanviewWebservice.svc");
var client = new ScanviewWebserviceClient(binding, endpoint);

var loginData = new LoginData
{
    AutoLogin = true,
    NoSession = true,
    UseSSO = false,
    Username = "ihr_metasonic_benutzer",
    Password = "ihr_metasonic_passwort",
    LoginDatabaseName = "ihre_metasonic_datenbank"
};
client.Login(ref loginData);

string metasonicDocId = "1527018";
byte[] pdfBytes = File.ReadAllBytes(@"C:\Temp\rechnung_geprueft_von_comstruct.pdf");

// 2. Geprüftes PDF als zusätzliches Item an das bestehende MetaSonic-Dokument anhängen
MetasonicUploadHelper.AppendDocumentPdfItem(
    client,
    ref loginData,
    metasonicDocId,
    pdfBytes,
    "rechnung_geprueft_von_comstruct.pdf"
);

// 3. Qualifizierer am MetaSonic-Dokument aktualisieren (z. B. Sachlich, Preislich, Comstruct-Link)
var qualifierUpdates = new Dictionary<string, string>
{
    ["223"] = "richtig",                                         // Sachlich geprüft
    ["222"] = "richtig",                                         // Preislich geprüft
    ["IHRE_SYNCED_QUALIFIER_ID"] = "Ja",                         // Geprüft von Comstruct
    ["IHRE_URL_QUALIFIER_ID"] = "https://app.comstruct.com/invoices/inv_123"
};

MetasonicUploadHelper.SetDocumentQualifiers(
    client,
    ref loginData,
    metasonicDocId,
    qualifierUpdates
);
```

---

### 2. Neues Dokument inkl. Datei & Metadaten in MetaSonic anlegen (`Program.cs`)

Um ein komplett neues Dokument mit Qualifizierern (Kreditor, Belegnummer, Betrag, Sachlich, Preislich) und einer PDF-Datei in MetaSonic DMS anzulegen:

```cmd
cd DocumentUploadPoC
build.bat
bin\Debug\MetasonicDocumentUploadPoC.exe "C:\Pfad\zur\rechnung.pdf"
```

#### C#-Beispiel: Neues Dokument in MetaSonic erstellen

```csharp
var metadata = new[]
{
    new RequestField { ID = "89900207", Value = "Muster Lieferant GmbH" }, // Kreditor
    new RequestField { ID = "89900202", Value = "RE-2026-0042" },          // Belegnummer
    new RequestField { ID = "89900131", Value = "1250.50" },               // Betrag (Netto)
    new RequestField { ID = "223",      Value = "ungeprüft" },             // Sachlich geprüft
    new RequestField { ID = "222",      Value = "ungeprüft" }              // Preislich geprüft
};

ResponseDocument created = MetasonicUploadHelper.SaveNewDocument(
    client,
    ref loginData,
    metadata,
    File.ReadAllBytes(@"C:\Pfad\zur\rechnung.pdf"),
    "rechnung.pdf",
    docClassId: "1"
);
```

---

### 3. Qualifizierer per Kommandozeile abfragen und aktualisieren (`MetasonicCLI`)

Mit `MetasonicCLI` lassen sich Dokumente und deren Qualifizierer direkt über die Kommandozeile oder aus eigenen Skripten heraus abfragen, suchen und aktualisieren:

```cmd
cd MetasonicCLI
build.bat

REM Alle Qualifizierer eines MetaSonic-Dokuments anzeigen
bin\Debug\MetasonicCLI.exe get 1527018

REM "Sachlich geprüft" (Qualifizierer 223) oder "Preislich geprüft" (Qualifizierer 222) setzen
bin\Debug\MetasonicCLI.exe update 1527018 sachlich richtig
bin\Debug\MetasonicCLI.exe update 1527018 preislich richtig

REM MetaSonic-Dokument anhand des Korrelationswerts (z. B. external_id) suchen
bin\Debug\MetasonicCLI.exe search EXT-2026-001

REM Comstruct → MetaSonic Synchronisations-Worker über die CLI starten
bin\Debug\MetasonicCLI.exe worker
```

---

## Projektstruktur

Alle Projekte sind in der Visual-Studio-Solution **`MetasonicFormular.sln`** enthalten und basieren auf **.NET Framework 4.7.2**:

| Pfad | Beschreibung |
|------|--------------|
| **`DocumentUploadPoC/`** | **Primärer On-Premise-Synchronisations-Worker (`run.bat`)** und C#-Client-Bibliothek (`DmsUploadWorker.cs`, `ComstructApiClient.cs`, `MetasonicUploadHelper.cs`, `MetasonicSearchHelper.cs`). Details siehe [DocumentUploadPoC/README.md](DocumentUploadPoC/README.md). |
| **`MetasonicCLI/`** | C#-Kommandozeilen-Tool (`MetasonicCLI.exe`) zum Auslesen, Suchen und Aktualisieren von Dokument-Qualifizierern (`get`, `update`, `search`, `worker`). Details siehe [MetasonicCLI/README.md](MetasonicCLI/README.md). |
| **`DmsFormular1.csproj`** (`Form1.cs`) | Windows-Forms-Anwendung zur interaktiven Anzeige und Bearbeitung des Prüfstatus (`Sachlich` / `Preislich`). |
| **`METASONIC-WEBSERVICE_DEMO/`** | Minimales Windows-Forms-Beispielprojekt für direkte WCF-Aufrufe (`GetDocument` und `UpdateDocumentFields`). |

## Lizenz

Veröffentlicht unter der [MIT-Lizenz](LICENSE).
