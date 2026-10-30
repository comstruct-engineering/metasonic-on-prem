# Comstruct ↔ MetaSonic DMS & BRZ Stammdaten-Worker

Automatisierter Hintergrund-Worker für zwei Aufgaben:
1. **Rechnungs-Synchronisation (`run.bat` / `DmsUploadWorker.cs`):** Ruft geprüfte Rechnungen über die Comstruct-API ab und übergibt deren PDF-Dateien inklusive Prüfstempel (`overlay=true`), Prüfvermerken (`annotations=true`) und Druckjob-ID (`external_id`) an das MetaSonic DMS (entweder als neues Dokument per `SaveDocument` oder als Anhang per `AddItem`).
2. **BRZ-Stammdaten-Synchronisation (`run-masterdata.bat` / `test-db.bat` / `BrzMasterDataWorker.cs`):** Verbindet sich über einen lesenden Datenbankbenutzer mit der lokalen BRZ-Datenbank und synchronisiert Kreditoren, Baustellen/Kostenstellen, Sachkonten, Steuerkennzeichen und Zahlungsbedingungen an die Comstruct-API.

## Funktionsweise: Rechnungs-Worker (`run.bat`)

1. Ruft Rechnungen im Status `PRICE_CHECKING` über die Comstruct-API ab (bereits als `SYNCED` markierte Belege werden übersprungen).
2. Lädt für jede Rechnung das geprüfte PDF (inkl. Annotationen und Prüfstempel/Match-Status-Overlay) von Comstruct herunter.
3. Sucht optional über `METASONIC_COMSTRUCT_INVOICE_QUALIFIER_ID` nach einem bestehenden MetaSonic-Dokument (anhand der Druckjob-ID / `external_id`):
   - Existiert ein Dokument, wird das PDF per `AddItem` angehängt.
   - Existiert noch kein Dokument und ist `DMS_WORKER_ALLOW_CREATE_DMS_DOCUMENT=true` gesetzt (z. B. wenn Rechnungen per E-Mail-Weiterleitung direkt in Comstruct eingehen), legt der Worker per `SaveDocument` ein neues MetaSonic-Dokument mit Kreditor, Belegnummer, Betrag, Druckjob-ID und gestempeltem PDF an.
4. Setzt optional Qualifizierer am MetaSonic-Dokument (z. B. *„Geprüft von Comstruct: Ja"* sowie den Direktlink zum Beleg in Comstruct).
5. Meldet das Ergebnis zurück an Comstruct (`SYNCED` bzw. `FAILED`).

## Funktionsweise: BRZ-Stammdaten-Worker (`run-masterdata.bat` & `test-db.bat`)

1. Baut eine lesende Datenbankverbindung zur BRZ-Datenbank auf (`BRZ_DB_PROVIDER=sqlserver`, `odbc` oder `oledb` mit `BRZ_DB_CONNECTION_STRING`).
2. Führt die in `config.env` definierten SQL-Abfragen aus und überträgt die Datensätze per Upsert an Comstruct:
   - `BRZ_SQL_TAX_CODES_QUERY` → `POST /v1/tax-codes` (Steuerkennzeichen für die automatische Vorkontierung)
   - `BRZ_SQL_PAYMENT_TERMS_QUERY` → `POST /v1/payment-terms` (Zahlungsbedingungen)
   - `BRZ_SQL_ACCOUNTS_QUERY` → `POST /v1/accounts` (Sachkonten für die automatische Vorkontierung)
   - `BRZ_SQL_SUPPLIERS_QUERY` → `POST /v1/suppliers` (Kreditoren inkl. Bankverbindungen/IBANs)
   - `BRZ_SQL_PROJECTS_QUERY` → `POST /v1/projects` (Baustellen / Kostenstellen)

## Voraussetzungen

- Windows Server oder Workstation mit **.NET Framework 4.7.2** oder höher
- **Visual Studio Build Tools** (oder Visual Studio 2019/2022) für die Kompilierung beim ersten Start
- Netzwerkzugriff auf `https://api.comstruct.com` sowie auf den lokalen MetaSonic-Server (`ScanviewWebservice.svc`) und ggf. den BRZ-Datenbankserver

## Einrichtung

1. Kopieren Sie `config.env.example` nach `config.env` und tragen Sie Ihre Zugangsdaten ein (siehe Tabelle unten). `config.env` wird von Git ignoriert und beim Start von `run.bat`, `run-masterdata.bat` und `test-db.bat` automatisch geladen:

```cmd
copy config.env.example config.env
notepad config.env
```

2. Alternativ können Sie die Variablen direkt in `run.bat` oder als Windows-Systemumgebungsvariablen hinterlegen:

```bat
set METASONIC_ENDPOINT=http://IHR_METASONIC_HOST:8089/ScanviewWebservice.svc
set METASONIC_USER=<benutzername>
set METASONIC_PASSWORD=<passwort>
set METASONIC_DB=<datenbankname>

set COMSTRUCT_NEST_URL=https://api.comstruct.com
set COMSTRUCT_API_PREFIX=v1
set COMSTRUCT_API_KEY=<ihr-api-key>
```

3. Optional können Sie Qualifizierer-IDs konfigurieren, die nach erfolgreichem PDF-Upload am Dokument in MetaSonic aktualisiert werden:

```ini
METASONIC_COMSTRUCT_SYNCED_QUALIFIER_ID=<qualifier-id>
METASONIC_COMSTRUCT_URL_QUALIFIER_ID=<qualifier-id>
```

Wenn `METASONIC_COMSTRUCT_SYNCED_QUALIFIER_ID` gesetzt ist, schreibt der Worker nach jedem erfolgreichen Upload `Ja` in dieses Feld (z. B. *„Geprüft von Comstruct: Ja"*). Wenn `METASONIC_COMSTRUCT_URL_QUALIFIER_ID` gesetzt ist, wird dort die Beleg-URL `https://app.comstruct.com/invoices/{id}` hinterlegt.

## Ausführen

- **`brz-read.bat`**: Zentrales Skript für den lesenden BRZ-Datenbankzugriff sowie zur Analyse der MetaSonic-Feldzuordnungen & Kontierung:
  - `brz-read.bat test` (oder `test-db.bat`): Testet die Verbindung zur BRZ-Datenbank (`SELECT 1`).
  - `brz-read.bat tables [Filter]`: Listet alle für den lesenden DB-Benutzer sichtbaren BRZ-Tabellen und Views auf.
  - `brz-read.bat query "<SQL>" [pfad.csv]`: Führt eine SQL-Abfrage auf der BRZ-Datenbank aus und exportiert das Ergebnis optional als CSV-Datei.
  - `brz-read.bat sync` (oder `run-masterdata.bat`): Führt den BRZ → Comstruct Stammdaten-Abgleich aus.
  - `brz-read.bat dms-classes [DocClassID]`: Liest aus MetaSonic alle Dokumentenklassen, Qualifizierer-IDs, Feldnamen, Pflichtfeld-Flags, hinterlegte SQL-/ODBC-Abfragen (`WordlistSQLInfo`) sowie Kontierungs-Dokumentenklassen (`GetAccountingDocClasses`) aus.
  - `brz-read.bat dms-inspect [DocID]`: Inspiziert ein bestehendes MetaSonic-Dokument inklusive aller Qualifizierer (mit klaren Feldnamen), angehängten Dateien und der MetaSonic-Kontierungstabelle (`GetAccountingTable`).
- **`run.bat`**: Führt den Comstruct → MetaSonic Rechnungs-Upload aus.

Alle drei Skripte laden automatisch `config.env`, kompilieren das Projekt beim ersten Start (falls `bin\Debug\MetasonicDocumentUploadPoC.exe` noch nicht existiert; mit `--rebuild` erzwingen Sie einen Neu-Build) und beenden sich mit dem jeweiligen Exit-Code (geeignet für die unbeaufsichtigte Ausführung in der Windows Aufgabenplanung).

## Zeitgesteuerte Ausführung (Windows Aufgabenplanung)

1. **Rechnungs-Upload (alle 5 Minuten):**
   - Name: `Comstruct DMS Upload`
   - Trigger: **Täglich** → **Wiederholen jede: 5 Minuten**
   - Aktion: **Programm starten** → `run.bat` (Starten in: Ordnerpfad von `DocumentUploadPoC`)
2. **BRZ-Stammdaten-Sync (täglich):**
   - Name: `Comstruct BRZ Stammdaten Sync`
   - Trigger: **Täglich** (z. B. `03:00` Uhr)
   - Aktion: **Programm starten** → `run-masterdata.bat` (Starten in: Ordnerpfad von `DocumentUploadPoC`)

## Umgebungsvariablen

| Variable | Pflicht | Beschreibung |
|---|---|---|
| `COMSTRUCT_NEST_URL` | Ja | Basis-URL der Comstruct-API (Standard: `https://api.comstruct.com`) |
| `COMSTRUCT_API_KEY` | Ja | Comstruct API-Key (wird als Header `x-api-key` gesendet) |
| `COMSTRUCT_API_PREFIX` | Ja | API-Pfad-Präfix (Standard: `v1`) |
| `METASONIC_ENDPOINT` | Ja (DMS) | MetaSonic Scanview SOAP-URL (`http://...:8089/ScanviewWebservice.svc`) |
| `METASONIC_USER` | Ja (DMS) | MetaSonic-Benutzername |
| `METASONIC_PASSWORD` | Ja (DMS) | MetaSonic-Passwort |
| `METASONIC_DB` | Ja (DMS) | MetaSonic-Datenbankname (wie im Login-Dialog) |
| `METASONIC_DOC_CLASS_ID` | Nein | Dokumentklassen-ID (Standard: `1`) |
| `METASONIC_COMSTRUCT_INVOICE_QUALIFIER_ID` | Nein | MetaSonic-Qualifizierer-ID für die Korrelations-ID (z. B. Druckjob-ID / `external_id`) |
| `METASONIC_SEARCH_USER_FOLDER_ID` | Nein | Optionale Benutzermappen-ID für Suchanfragen |
| `METASONIC_DMS_CORRELATION_KEY` | Nein | `external_id` (Standard) oder `id` für die Zuordnung Rechnung ↔ DMS-Dokument |
| `COMSTRUCT_DMS_QUEUE_STATUS` | Nein | Rechnungsstatus-Filter (Standard: `PRICE_CHECKING`) |
| `COMSTRUCT_DMS_LIMIT` | Nein | Seitengröße 1–500 (Standard: `50`) |
| `COMSTRUCT_DMS_UPDATED_AFTER_PARAM` | Nein | Query-Parametername für Zeitstempel-Filter (Standard: `updated_after`) |
| `COMSTRUCT_DMS_PATCH_PATH` | Nein | Pfad-Vorlage für das Status-Update bei Comstruct |
| `COMSTRUCT_PDF_ANNOTATIONS` | Nein | `false` / `0` deaktiviert das Einbetten von PDF-Annotationen |
| `COMSTRUCT_PDF_OVERLAY` | Nein | `false` / `0` deaktiviert den Prüfstempel / die Match-Status-Boxen im PDF (`overlay=true`) |
| `DMS_WORKER_STATE_FILE` | Nein | Pfad zur Statusdatei des Workers (Standard: `.dms-worker-state.json`) |
| `DMS_WORKER_INITIAL_UPDATED_AFTER` | Nein | Initialer ISO-Zeitstempel für den ersten Abruf |
| `DMS_WORKER_DRY_RUN` | Nein | `true` / `1` aktiviert den Simulationsmodus ohne Schreibzugriffe |
| `DMS_WORKER_ALLOW_CREATE_DMS_DOCUMENT` | Nein | `true` / `1` erlaubt das Neuanlegen von Dokumenten in MetaSonic, falls keines gefunden wird |
| `METASONIC_COMSTRUCT_SYNCED_QUALIFIER_ID` | Nein | Qualifizierer-ID; Worker schreibt `Ja` nach erfolgreichem Upload |
| `METASONIC_COMSTRUCT_URL_QUALIFIER_ID` | Nein | Qualifizierer-ID für den Direktlink zur Rechnung in Comstruct |
| `BRZ_DB_PROVIDER` | Nein | Datenbank-Treiber für BRZ: `sqlserver` (Standard), `odbc` oder `oledb` |
| `BRZ_DB_CONNECTION_STRING` | Ja (DB-Sync) | Connection-String für den lesenden BRZ-Datenbankbenutzer |
| `BRZ_DB_TIMEOUT_SECONDS` | Nein | SQL-Command-Timeout in Sekunden (Standard: `60`) |
| `BRZ_SQL_SUPPLIERS_QUERY` | Nein | SQL-Abfrage für Kreditoren (`POST /v1/suppliers`) |
| `BRZ_SQL_PROJECTS_QUERY` | Nein | SQL-Abfrage für Baustellen / Kostenstellen (`POST /v1/projects`) |
| `BRZ_SQL_ACCOUNTS_QUERY` | Nein | SQL-Abfrage für Sachkonten (`POST /v1/accounts`) |
| `BRZ_SQL_TAX_CODES_QUERY` | Nein | SQL-Abfrage für Steuerkennzeichen (`POST /v1/tax-codes`) |
| `BRZ_SQL_PAYMENT_TERMS_QUERY` | Nein | SQL-Abfrage für Zahlungsbedingungen (`POST /v1/payment-terms`) |

Siehe `config.env.example` für eine vollständig kommentierte Vorlage.

## Dateiname des angehängten PDFs

Das hochgeladene PDF übernimmt bei bestehenden Dokumenten den Dateinamen der ersten bestehenden Datei am MetaSonic-Dokument und ergänzt das Suffix `_geprueft_von_comstruct`:

```
eingangsrechnung.pdf  →  eingangsrechnung_geprueft_von_comstruct.pdf
```

Bei neu angelegten Dokumenten oder falls der bestehende Dateiname nicht ermittelt werden kann, wird `Comstruct_Rechnung_{Rechnungsnummer}.pdf` verwendet.

## Projektstruktur

```
DocumentUploadPoC/
├── run.bat                   # Startskript für den Rechnungs-Worker (Comstruct → MetaSonic DMS)
├── brz-read.bat              # Zentrales BRZ-Read-Skript (test, tables, query, sync)
├── run-masterdata.bat        # Startskript für den Stammdaten-Worker (BRZ DB → Comstruct)
├── test-db.bat               # Testskript für die lesende BRZ-Datenbankverbindung
├── build.bat                 # MSBuild-Kompilierungsskript
├── config.env.example        # Vorlage für Umgebungsvariablen (MetaSonic, BRZ DB, Comstruct)
├── Program.cs                # Einstiegspunkt (worker, masterdata, db-test & interaktiver Upload-Test)
├── DmsUploadWorker.cs        # Hauptlogik des Rechnungs-Synchronisations-Workers
├── BrzMasterDataWorker.cs    # Hauptlogik des BRZ-Datenbank-Stammdaten-Workers
├── MetasonicInspectHelper.cs # Analyse-Helfer für MetaSonic-Dokumentenklassen, Qualifizierer, SQL-Lookups & Kontierungstabelle
├── ComstructApiClient.cs     # HTTP-Client für die Comstruct REST-API
├── MetasonicUploadHelper.cs  # Hilfsmethoden für MetaSonic SOAP-Uploads & Qualifizierer-Updates
├── MetasonicSearchHelper.cs  # Hilfsmethoden für die Dokumentensuche in MetaSonic
├── DmsEnvironment.cs         # Auslesen der Umgebungsvariablen
├── DocumentUploadPoC.csproj  # C#-Projektdatei (.NET Framework 4.7.2)
└── ScanviewWebservice/
    └── Reference.cs          # Generierter MetaSonic WCF-Client
```
