# Comstruct ↔ MetaSonic DMS & BRZ On-Premise-Synchronisation (.NET / C#)

On-Premise-Synchronisations-Worker für Windows sowie C#-Beispiele zur Anbindung von **Comstruct** an das **MetaSonic Scanview DMS** und die **BRZ-Datenbank** (für **BRZ 7**, **BRZ 8** und **BRZ 365**).

Da BRZ keine offene Drittanbieter-REST-API für Dokumenten-Workflows bereitstellt, erfolgt die Integration über zwei komplementäre Skripte im Ordner `DocumentUploadPoC`:
1. **BRZ-Datenbank → Comstruct (`run-masterdata.bat` / `test-db.bat`):** Liest Stammdaten (Kreditoren, Baustellen/Kostenstellen, Sachkonten, Steuerkennzeichen, Zahlungsbedingungen) über einen lesenden BRZ-Datenbankbenutzer aus und synchronisiert sie für die automatische Kopfdaten-Erfassung und Vorkontierung nach Comstruct.
2. **Comstruct → MetaSonic DMS (`run.bat`):** Überträgt geprüfte Rechnungen inklusive vergebener Druckjob-ID (`external_id`), PDF mit Prüfstempel (`overlay=true`) und Annotationen (`annotations=true`) sowie Prüfstatus an den **MetaSonic Scanview SOAP/WCF-Webservice** (`ScanviewWebservice.svc`) zur finalen Prüfung.

Eine vollständige fachliche Übersicht finden Sie im Comstruct Knowledge-Base-Artikel:
**[BRZ und Metasonic DMS an comstruct anbinden: Rechnungen, Lieferscheine und Stammdaten synchronisieren](https://knowledge.comstruct.com/articles/1464016790-brz-metasonic-x-comstruct-anbindung)**

---

## Überblick: Prozessschritte & zugehörige Skripte

Passend zum Zielprozess **„Rechnungseingang & Prüfung“** verteilen sich die Aufgaben wie folgt auf Comstruct und die lokalen On-Premise-Skripte:

| Prozessschritt | System | Technische Umsetzung / Skript in diesem Repository |
|---|---|---|
| **1–3. Rechnungseingang via E-Mail-Weiterleitung** | Lieferant → Mail → Comstruct | E-Mail-Weiterleitungsregel im Postfach an die Comstruct-Eingangsadresse (kein lokales Skript nötig). |
| **4. Stammdaten & Kopfdaten-Erfassung** | BRZ DB → Comstruct | **`DocumentUploadPoC\run-masterdata.bat`** (`BrzMasterDataWorker.cs`): Liest Kreditoren und Kostenstellen/Projekte über den lesenden BRZ-Datenbankbenutzer (`BRZ_DB_CONNECTION_STRING`) und synchronisiert sie an `POST /v1/suppliers` und `POST /v1/projects`. |
| **5. Automatische Vorkontierung** | BRZ DB → Comstruct | **`DocumentUploadPoC\run-masterdata.bat`**: Synchronisiert Steuerkennzeichen (`POST /v1/tax-codes`), Sachkonten (`POST /v1/accounts`) und Zahlungsbedingungen (`POST /v1/payment-terms`) aus der BRZ-Datenbank. Die Vorkontierungs-Logik (>80 %-Kombination aus Lieferant + Projekt + Steuerkennzeichen) läuft automatisch in Comstruct. |
| **6–7. Vorprüfung (24 h) & Preisprüfung (Direkt)** | Comstruct | Automatische Sach-/Mengenprüfung (mit 24h-Puffer für Lieferscheine) und Preisprüfung direkt in Comstruct; Vergabe der Druckjob-ID (`external_id`) durch Comstruct. |
| **8. Übergabe zur Finalen Prüfung in MetaSonic** | Comstruct → MetaSonic DMS | **`DocumentUploadPoC\run.bat`** (`DmsUploadWorker.cs`): Lädt geprüfte Rechnungen inkl. PDF mit Prüfstempel (`overlay=true`) und Annotationen (`annotations=true`) herunter und legt sie per `SaveDocument` neu in MetaSonic an (`DMS_WORKER_ALLOW_CREATE_DMS_DOCUMENT=true`) bzw. hängt sie per `AddItem` an bestehende Dokumente an. |

---

## Standard-Ausführung (On-Premise Windows Worker)

### Voraussetzungen

- **Windows Server** (oder Windows-Workstation) mit **.NET Framework 4.7.2** oder höher
- **Visual Studio Build Tools** (oder Visual Studio 2019/2022) zum einmaligen Kompilieren des Workers beim ersten Start
- Ausgehender HTTPS-Zugriff auf `https://api.comstruct.com` (Port 443)
- Netzwerkzugriff auf den lokalen MetaSonic Scanview-Endpunkt (z. B. `http://IHR_METASONIC_HOST:8089/ScanviewWebservice.svc`)
- Lesender Datenbankbenutzer auf der BRZ-Datenbank (SQL Server, ODBC oder OLE DB) für die Stammdaten-Synchronisation

### Schritt 1: `config.env` konfigurieren

Kopieren Sie die Beispiel-Konfigurationsdatei und tragen Sie Ihre Zugangsdaten für MetaSonic, die BRZ-Datenbank und Comstruct ein (`config.env` ist in `.gitignore` ausgeschlossen, sodass keine Zugangsdaten versehentlich committet werden):

```cmd
cd DocumentUploadPoC
copy config.env.example config.env
notepad config.env
```

Wichtige Einstellungen in `DocumentUploadPoC\config.env`:

```ini
# Comstruct REST API (Scopes: invoices:read, invoices:write, suppliers:write, projects:write, accounts:write, tax_codes:write, payment_terms:write)
COMSTRUCT_NEST_URL=https://api.comstruct.com
COMSTRUCT_API_PREFIX=v1
COMSTRUCT_API_KEY=ihr_comstruct_api_key

# PDF-Export mit Prüfstempel (overlay) und Annotationen
COMSTRUCT_PDF_ANNOTATIONS=true
COMSTRUCT_PDF_OVERLAY=true

# MetaSonic Scanview SOAP/WCF-Webservice
METASONIC_ENDPOINT=http://IHR_METASONIC_HOST:8089/ScanviewWebservice.svc
METASONIC_USER=ihr_metasonic_benutzer
METASONIC_PASSWORD=ihr_metasonic_passwort
METASONIC_DB=ihre_metasonic_datenbank

# Dokumentneuanlage aktivieren, wenn Rechnungen direkt über Comstruct eingehen:
DMS_WORKER_ALLOW_CREATE_DMS_DOCUMENT=true
METASONIC_DOC_CLASS_ID=1
METASONIC_COMSTRUCT_INVOICE_QUALIFIER_ID=

# Optional: MetaSonic Qualifizierer-IDs nach erfolgreichem Sync
METASONIC_COMSTRUCT_SYNCED_QUALIFIER_ID=
METASONIC_COMSTRUCT_URL_QUALIFIER_ID=

# BRZ Datenbankverbindung (lesender Benutzer für Stammdaten-Sync)
BRZ_DB_PROVIDER=sqlserver
BRZ_DB_CONNECTION_STRING=Server=BRZ-SERVER;Database=BRZ;User Id=comstruct_readonly;Password=GEHEIM;
BRZ_SQL_SUPPLIERS_QUERY=SELECT KreditorNr AS external_id, Name AS title, 'DE' AS country_code, UStId AS legal_uid, Strasse AS address, Ort AS city, PLZ AS zip, IBAN AS iban FROM Kreditoren WHERE Aktiv = 1
BRZ_SQL_PROJECTS_QUERY=SELECT Kostenstelle AS project_number, Bezeichnung AS title, Kostenstelle AS external_id, Betriebsnummer AS legal_entity_number FROM Kostenstellen WHERE Aktiv = 1
BRZ_SQL_ACCOUNTS_QUERY=SELECT KontoNr AS number, Bezeichnung AS title FROM Sachkonten WHERE Aktiv = 1
BRZ_SQL_TAX_CODES_QUERY=SELECT Steuercode AS number, Bezeichnung AS title, Steuersatz AS rate FROM Steuerkennzeichen
```

### Schritt 2: Manuell testen & ausführen

Im Ordner `DocumentUploadPoC` stehen die Startskripte für BRZ-Read und die Synchronisation bereit:

```cmd
cd DocumentUploadPoC

REM 1. BRZ-Datenbankverbindung testen (führt SELECT 1 über BRZ_DB_CONNECTION_STRING aus)
brz-read.bat test
REM (oder: test-db.bat)

REM 2. Verfügbare BRZ-Tabellen/Views des lesenden DB-Benutzers auflisten (optional mit Suchfilter)
brz-read.bat tables
brz-read.bat tables Kred

REM 3. Beliebige Lese-Abfrage auf der BRZ-Datenbank ausführen (optional mit direktem CSV-Export)
brz-read.bat query "SELECT TOP 20 * FROM Kreditoren"
brz-read.bat query "SELECT * FROM Kreditoren" C:\Temp\kreditoren.csv

REM 4. BRZ-Stammdaten (Kreditoren, Kostenstellen, Sachkonten, Steuercodes) nach Comstruct synchronisieren
brz-read.bat sync
REM (oder: run-masterdata.bat)

REM 5. MetaSonic Feldzuordnungen (Qualifizierer), Pflichtfelder, SQL-Lookups & Kontierungs-Klassen abfragen
brz-read.bat dms-classes

REM 6. Bestehendes MetaSonic-Dokument inkl. aller Feldwerte und Kontierungstabelle (GetAccountingTable) prüfen
brz-read.bat dms-inspect
brz-read.bat dms-inspect 1527018

REM 7. Geprüfte Rechnungen (inkl. PDF mit Stempel & Annotationen) von Comstruct nach MetaSonic übertragen
run.bat
```

> **Dry-Run-Modus (Testlauf ohne Schreibzugriffe):** Um die Datenbankabfragen und den Rechnungsabruf zu testen, ohne Daten in MetaSonic oder Comstruct zu verändern, setzen Sie `DMS_WORKER_DRY_RUN=true` in der `config.env` und starten Sie `run-masterdata.bat` bzw. `run.bat`.

### Schritt 3: Automatische Ausführung einrichten (Windows Aufgabenplanung)

Für den produktiven Betrieb werden zwei Aufgaben in der **Windows Aufgabenplanung (Task Scheduler)** eingerichtet:

1. **Rechnungs-Synchronisation (alle 5 Minuten):**
   - **Name:** `Comstruct MetaSonic DMS Sync`
   - **Trigger:** Täglich → **Wiederholen jede: 5 Minuten** für die Dauer **Unbegrenzt**
   - **Programm/Skript:** `C:\Pfad\zu\metasonic-on-prem\DocumentUploadPoC\run.bat`
   - **Starten in:** `C:\Pfad\zu\metasonic-on-prem\DocumentUploadPoC`
2. **BRZ Stammdaten-Synchronisation (1× täglich nachts):**
   - **Name:** `Comstruct BRZ Stammdaten Sync`
   - **Trigger:** Täglich (z. B. `03:00` Uhr)
   - **Programm/Skript:** `C:\Pfad\zu\metasonic-on-prem\DocumentUploadPoC\run-masterdata.bat`
   - **Starten in:** `C:\Pfad\zu\metasonic-on-prem\DocumentUploadPoC`

---

## Ablauf der Synchronisation & C#-Codebeispiele

### 1. Automatische Rechnungs-Synchronisation Comstruct → MetaSonic (`DmsUploadWorker.cs`)

Bei jedem Durchlauf führt der Worker (`run.bat` bzw. `MetasonicDocumentUploadPoC.exe worker`) folgende Schritte aus:

1. **Abruf zu synchronisierender Rechnungen aus Comstruct** (`GET /v1/invoices?status=PRICE_CHECKING&limit=50&offset=0`), wobei bereits synchronisierte Belege (`external_status = SYNCED`) übersprungen werden.
2. **Download des geprüften Rechnungs-PDFs** (`GET /v1/invoices/{id}/pdf?annotations=true&overlay=true`) inklusive Prüfvermerken/Annotationen und Prüfstempel aus Comstruct.
3. **Ablage in MetaSonic DMS:**
   - **Bestehendes Dokument (`AddItem`):** Wenn das Dokument bereits in MetaSonic existiert (gefunden über `METASONIC_COMSTRUCT_INVOICE_QUALIFIER_ID` oder `external_id`), wird das geprüfte PDF unter `<Originaldateiname>_geprueft_von_comstruct.pdf` angehängt.
   - **Neues Dokument (`SaveDocument` bei `DMS_WORKER_ALLOW_CREATE_DMS_DOCUMENT=true`):** Wenn Rechnungen direkt per E-Mail-Weiterleitung in Comstruct eingehen, legt der Worker automatisch ein neues MetaSonic-Dokument mit Kreditor, Belegnummer, Betrag, Prüfstatus (`ungeprüft`), der Druckjob-ID (`external_id`) und dem gestempelten PDF an.
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

### 2. BRZ-Datenbank-Stammdaten nach Comstruct synchronisieren (`BrzMasterDataWorker.cs`)

Über `run-masterdata.bat` (`MetasonicDocumentUploadPoC.exe masterdata`) liest der Worker die konfigurierten SQL-Abfragen über ADO.NET (`SqlConnection`, `OdbcConnection` oder `OleDbConnection`) aus der BRZ-Datenbank und überträgt sie per Upsert an die Comstruct REST-API:

- **Steuerkennzeichen (`BRZ_SQL_TAX_CODES_QUERY`)** → `POST /v1/tax-codes`
- **Zahlungsbedingungen (`BRZ_SQL_PAYMENT_TERMS_QUERY`)** → `POST /v1/payment-terms`
- **Sachkonten (`BRZ_SQL_ACCOUNTS_QUERY`)** → `POST /v1/accounts`
- **Kreditoren / Lieferanten (`BRZ_SQL_SUPPLIERS_QUERY`)** → `POST /v1/suppliers` (inkl. automatischer Gruppierung mehrerer Bankverbindungen/IBANs pro Kreditor)
- **Kostenstellen / Baustellen (`BRZ_SQL_PROJECTS_QUERY`)** → `POST /v1/projects`

---

### 3. Qualifizierer & Worker per Kommandozeile steuern (`MetasonicCLI`)

Mit `MetasonicCLI` lassen sich Dokumente und deren Qualifizierer direkt über die Kommandozeile abfragen, suchen und aktualisieren sowie die Worker starten:

```cmd
cd MetasonicCLI
build.bat

REM Alle Qualifizierer eines MetaSonic-Dokuments anzeigen
bin\Debug\MetasonicCLI.exe get 1527018

REM "Sachlich geprüft" (Qualifizierer 223) oder "Preislich geprüft" (Qualifizierer 222) setzen
bin\Debug\MetasonicCLI.exe update 1527018 sachlich richtig
bin\Debug\MetasonicCLI.exe update 1527018 preislich richtig

REM MetaSonic-Dokument anhand des Korrelationswerts (z. B. Druckjob-ID / external_id) suchen
bin\Debug\MetasonicCLI.exe search EXT-2026-001

REM Comstruct → MetaSonic Rechnungs-Worker starten
bin\Debug\MetasonicCLI.exe worker

REM BRZ-Datenbankverbindung testen & Stammdaten-Sync starten
bin\Debug\MetasonicCLI.exe db-test
bin\Debug\MetasonicCLI.exe masterdata
```

---

## Projektstruktur

Alle Projekte sind in der Visual-Studio-Solution **`MetasonicFormular.sln`** enthalten und basieren auf **.NET Framework 4.7.2**:

| Pfad | Beschreibung |
|------|--------------|
| **`DocumentUploadPoC/`** | **Primärer On-Premise-Worker (`run.bat`, `run-masterdata.bat`, `test-db.bat`)** und C#-Client-Bibliothek (`DmsUploadWorker.cs`, `BrzMasterDataWorker.cs`, `ComstructApiClient.cs`, `MetasonicUploadHelper.cs`, `MetasonicSearchHelper.cs`). Details siehe [DocumentUploadPoC/README.md](DocumentUploadPoC/README.md). |
| **`MetasonicCLI/`** | C#-Kommandozeilen-Tool (`MetasonicCLI.exe`) zum Auslesen, Suchen und Aktualisieren von Dokument-Qualifizierern (`get`, `update`, `search`, `worker`, `masterdata`, `db-test`). Details siehe [MetasonicCLI/README.md](MetasonicCLI/README.md). |
| **`DmsFormular1.csproj`** (`Form1.cs`) | Windows-Forms-Anwendung zur interaktiven Anzeige und Bearbeitung des Prüfstatus (`Sachlich` / `Preislich`). |
| **`METASONIC-WEBSERVICE_DEMO/`** | Minimales Windows-Forms-Beispielprojekt für direkte WCF-Aufrufe (`GetDocument` und `UpdateDocumentFields`). |

## Lizenz

Veröffentlicht unter der [MIT-Lizenz](LICENSE).
