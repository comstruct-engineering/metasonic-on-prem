# Comstruct → MetaSonic DMS Upload Worker

Automatisierter Hintergrund-Worker, der geprüfte Rechnungen über die Comstruct-API abruft und deren PDF-Dateien inklusive Prüfvermerken an die zugehörigen Dokumente im MetaSonic DMS anhängt.

## Funktionsweise

1. Ruft Rechnungen im Status `PRICE_CHECKING` über die Comstruct-API ab (bereits als `SYNCED` markierte Belege werden übersprungen).
2. Lädt für jede Rechnung das geprüfte PDF (inkl. Annotationen und Match-Status-Overlay) von Comstruct herunter.
3. Hängt das PDF per `AddItem` an das zugehörige MetaSonic-Dokument an (zugeordnet über `external_id`).
4. Setzt optional Qualifizierer am MetaSonic-Dokument (z. B. *„Geprüft von Comstruct: Ja"* sowie den Direktlink zum Beleg in Comstruct).
5. Meldet das Ergebnis zurück an Comstruct (`SYNCED` bzw. `FAILED`).

## Voraussetzungen

- Windows Server oder Workstation mit **.NET Framework 4.7.2** oder höher
- **Visual Studio Build Tools** (oder Visual Studio 2019/2022) für die Kompilierung beim ersten Start
- Netzwerkzugriff auf `https://api.comstruct.com` sowie auf den lokalen MetaSonic-Server (`ScanviewWebservice.svc`)

## Einrichtung

1. Kopieren Sie `config.env.example` nach `config.env` und tragen Sie Ihre Zugangsdaten ein (siehe Tabelle unten). `config.env` wird von Git ignoriert und beim Start von `run.bat` automatisch geladen:

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

Starten Sie `run.bat` über die Eingabeaufforderung oder per Doppelklick:

- Lädt `config.env`, sofern vorhanden (oder verwendet gesetzte Umgebungsvariablen)
- Kompiliert das Projekt beim ersten Start automatisch, falls `bin\Debug\MetasonicDocumentUploadPoC.exe` noch nicht vorhanden ist (mit `run.bat --rebuild` erzwingen Sie einen Neu-Build)
- Verbindet sich mit Comstruct und MetaSonic
- Verarbeitet alle ausstehenden Rechnungen
- Beendet sich mit dem Exit-Code des Workers (geeignet für die unbeaufsichtigte Ausführung in der Windows Aufgabenplanung)

## Zeitgesteuerte Ausführung (Windows Aufgabenplanung)

Um den Worker automatisch alle 5 Minuten auszuführen, richten Sie eine Aufgabe in der **Windows Aufgabenplanung** ein:

1. Aufgabenplanung öffnen → **Aufgabe erstellen...**
2. Name: `Comstruct DMS Upload`
3. Trigger: **Täglich** → **Wiederholen jede: 5 Minuten**
4. Aktion: **Programm starten** → Pfad zu `run.bat` auswählen und unter **Starten in** den Ordnerpfad von `DocumentUploadPoC` eintragen
5. Option **Unabhängig von der Benutzeranmeldung ausführen** aktivieren

## Umgebungsvariablen

| Variable | Pflicht | Beschreibung |
|---|---|---|
| `COMSTRUCT_NEST_URL` | Ja | Basis-URL der Comstruct-API (Standard: `https://api.comstruct.com`) |
| `COMSTRUCT_API_KEY` | Ja | Comstruct API-Key (wird als Header `x-api-key` gesendet) |
| `COMSTRUCT_API_PREFIX` | Ja | API-Pfad-Präfix (Standard: `v1`) |
| `METASONIC_ENDPOINT` | Ja | MetaSonic Scanview SOAP-URL (`http://...:8089/ScanviewWebservice.svc`) |
| `METASONIC_USER` | Ja | MetaSonic-Benutzername |
| `METASONIC_PASSWORD` | Ja | MetaSonic-Passwort |
| `METASONIC_DB` | Ja | MetaSonic-Datenbankname (wie im Login-Dialog) |
| `METASONIC_DOC_CLASS_ID` | Nein | Dokumentklassen-ID (Standard: `1`) |
| `METASONIC_COMSTRUCT_INVOICE_QUALIFIER_ID` | Nein | MetaSonic-Qualifizierer-ID für die Comstruct-Rechnungs-ID |
| `METASONIC_SEARCH_USER_FOLDER_ID` | Nein | Optionale Benutzermappen-ID für Suchanfragen |
| `METASONIC_DMS_CORRELATION_KEY` | Nein | `external_id` (Standard) oder `id` für die Zuordnung Rechnung ↔ DMS-Dokument |
| `COMSTRUCT_DMS_QUEUE_STATUS` | Nein | Rechnungsstatus-Filter (Standard: `PRICE_CHECKING`) |
| `COMSTRUCT_DMS_LIMIT` | Nein | Seitengröße 1–500 (Standard: `50`) |
| `COMSTRUCT_DMS_UPDATED_AFTER_PARAM` | Nein | Query-Parametername für Zeitstempel-Filter (Standard: `updated_after`) |
| `COMSTRUCT_DMS_PATCH_PATH` | Nein | Pfad-Vorlage für das Status-Update bei Comstruct |
| `COMSTRUCT_PDF_ANNOTATIONS` | Nein | `false` / `0` deaktiviert das Einbetten von PDF-Annotationen |
| `COMSTRUCT_PDF_OVERLAY` | Nein | `false` / `0` deaktiviert die Match-Status-Boxen im PDF (`overlay=true`) |
| `DMS_WORKER_STATE_FILE` | Nein | Pfad zur Statusdatei des Workers (Standard: `.dms-worker-state.json`) |
| `DMS_WORKER_INITIAL_UPDATED_AFTER` | Nein | Initialer ISO-Zeitstempel für den ersten Abruf |
| `DMS_WORKER_DRY_RUN` | Nein | `true` / `1` aktiviert den Simulationsmodus ohne Schreibzugriffe |
| `DMS_WORKER_ALLOW_CREATE_DMS_DOCUMENT` | Nein | `true` / `1` erlaubt das Neuanlegen von Dokumenten, falls keines gefunden wird |
| `METASONIC_COMSTRUCT_SYNCED_QUALIFIER_ID` | Nein | Qualifizierer-ID; Worker schreibt `Ja` nach erfolgreichem Upload |
| `METASONIC_COMSTRUCT_URL_QUALIFIER_ID` | Nein | Qualifizierer-ID für den Direktlink zur Rechnung in Comstruct |

Siehe `config.env.example` für eine vollständig kommentierte Vorlage.

## Dateiname des angehängten PDFs

Das hochgeladene PDF übernimmt den Dateinamen der ersten bestehenden Datei am MetaSonic-Dokument und ergänzt das Suffix `_geprueft_von_comstruct`:

```
eingangsrechnung.pdf  →  eingangsrechnung_geprueft_von_comstruct.pdf
```

Falls der bestehende Dateiname nicht ermittelt werden kann, wird `Comstruct_Rechnung_{Rechnungsnummer}.pdf` verwendet.

## Projektstruktur

```
DocumentUploadPoC/
├── run.bat                   # Standard-Startskript (lädt config.env, kompiliert bei Bedarf und startet Worker)
├── build.bat                 # MSBuild-Kompilierungsskript
├── config.env.example        # Vorlage für Umgebungsvariablen
├── Program.cs                # Einstiegspunkt (Worker-Modus & interaktiver Upload-Test)
├── DmsUploadWorker.cs        # Hauptlogik des Synchronisations-Workers
├── ComstructApiClient.cs     # HTTP-Client für die Comstruct REST-API
├── MetasonicUploadHelper.cs  # Hilfsmethoden für MetaSonic SOAP-Uploads & Qualifizierer-Updates
├── MetasonicSearchHelper.cs  # Hilfsmethoden für die Dokumentensuche in MetaSonic
├── DmsEnvironment.cs         # Auslesen der Umgebungsvariablen
├── DocumentUploadPoC.csproj  # C#-Projektdatei (.NET Framework 4.7.2)
└── ScanviewWebservice/
    └── Reference.cs          # Generierter MetaSonic WCF-Client
```
