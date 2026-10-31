# Comstruct ↔ MetaSonic DMS Bidirectional Sync (.NET / C#)

Leichtgewichtiger, bidirektionaler Hintergrund-Worker (`DocumentUploadPoC\run.bat`) für Windows Server zur vollautomatischen Synchronisation zwischen **MetaSonic Scanview DMS** und **Comstruct**:

1. **Phase 1 — MetaSonic → Comstruct (Rechnungs-Import & Auto-Anlage von Kreditor/Projekt):**
   - Prüft in MetaSonic alle Rechnungen in `METASONIC_DOC_CLASS_ID`, die seit dem letzten gespeicherten Zeitstempel (`lastMetasonicCheckAt` in `.dms-worker-state.json`) erstellt oder aktualisiert wurden.
   - Prüft für jedes MetaSonic-Dokument, ob die Rechnung bereits in Comstruct existiert (anhand `external_id = <MetaSonic-DocumentID>`).
   - Prüft, ob **Lieferant (Kreditor)** und **Projekt (Kostenstelle)** bereits in Comstruct existieren; falls nicht, werden sie automatisch in Comstruct angelegt (mit der Kreditoren-/Projektnummer bzw. der `documentId` als `external_id`, inkl. automatischer Namensauflösung über die MetaSonic-Kostenstellenklasse `89035003`).
   - Lädt das Rechnungs-PDF/-TIFF aus MetaSonic herunter und legt die Rechnung in Comstruct mit der MetaSonic-`documentId` als `external_id` an.

2. **Phase 2 — Comstruct → MetaSonic (Rückübertragung geprüfter Rechnungen mit Stempel):**
   - Prüft in Comstruct alle Rechnungen im Status `CHECKED` (`COMSTRUCT_DMS_QUEUE_STATUS`), die seit dem letzten gespeicherten Zeitstempel (`lastComstructCheckAt` in `.dms-worker-state.json`) aktualisiert wurden und noch nicht als verarbeitet (`external_status = SYNCED`) markiert sind.
   - Lädt das geprüfte PDF **inklusive Comstruct-Prüfstempel (`overlay=true`) und Annotationen (`annotations=true`)** herunter.
   - Hängt das gestempelte PDF per `AddItem` als zusätzliches Dokument (`<Originalname>_geprueft_von_comstruct.pdf`) an den MetaSonic-Beleg an (das Originaldokument bleibt unverändert).
   - Markiert die Rechnung in Comstruct über `PATCH /v1/invoices/{id}/external-status` mit `{"external_status": "SYNCED"}` als verarbeitet und speichert den neuen Zeitstempel persistent in `.dms-worker-state.json`.

---

## Schnellstart in 3 Schritten

### Schritt 1: `config.env` anlegen

```cmd
cd DocumentUploadPoC
copy config.env.example config.env
notepad config.env
```

Es müssen lediglich **5 Pflichtfelder** ausgefüllt werden (alle weiteren Einstellungen haben sinnvolle Standardwerte und erkennen Feldnamen in MetaSonic automatisch):

```ini
# 1. Comstruct API
COMSTRUCT_API_KEY=ihr_comstruct_api_key
COMSTRUCT_NEST_URL=https://api.comstruct.com

# 2. MetaSonic DMS
METASONIC_ENDPOINT=http://IHR_METASONIC_HOST:8089/ScanviewWebservice.svc
METASONIC_USER=SVPADM
METASONIC_PASSWORD=ihr_passwort
METASONIC_DB=BRZARCHIVMP

# 3. MetaSonic Dokumentenklassen
METASONIC_DOC_CLASS_ID=1
METASONIC_PROJECTS_DOC_CLASS_ID=89035003
```

### Schritt 2: Manuell testen (`run.bat`)

```cmd
cd DocumentUploadPoC

REM Optional: Dry-Run (Simulationsmodus ohne Schreibzugriffe) in config.env aktivieren:
REM DMS_WORKER_DRY_RUN=true

run.bat
```

`run.bat` kompiliert das Projekt beim ersten Start automatisch (nutzt den auf jedem Windows Server vorinstallierten `.NET Framework 4`-Compiler – keine Visual Studio Installation erforderlich) und führt nacheinander **Phase 1 (MetaSonic → Comstruct)** und **Phase 2 (Comstruct → MetaSonic)** aus.

Bei Bedarf können beide Richtungen auch einzeln gestartet werden:
```cmd
run.bat inbound     REM Nur Phase 1: MetaSonic -> Comstruct
run.bat outbound    REM Nur Phase 2: Comstruct -> MetaSonic
```

### Schritt 3: Automatische Ausführung alle 5 Minuten (Windows Aufgabenplanung)

1. **Aufgabenplanung (`taskschd.msc`)** öffnen → **Aufgabe erstellen...**
2. Reiter **Allgemein**:
   - Name: `Comstruct MetaSonic Sync`
   - Option **Unabhängig von der Benutzeranmeldung ausführen** aktivieren
3. Reiter **Trigger**:
   - **Neu...** → Täglich → **Wiederholen jede: 5 Minuten** für die Dauer **Unbegrenzt**
4. Reiter **Aktionen**:
   - **Neu...** → Aktion: **Programm starten**
   - **Programm/Skript:** `C:\Pfad\zu\metasonic-on-prem\DocumentUploadPoC\run.bat`
   - **Starten in:** `C:\Pfad\zu\metasonic-on-prem\DocumentUploadPoC`

---

## Persistenz des letzten erfolgreichen Laufs (`.dms-worker-state.json`)

Der Worker speichert seinen Zustand nach jeder erfolgreich übertragenen Rechnung automatisch in der lokalen Datei `DocumentUploadPoC\.dms-worker-state.json`:

```json
{
  "lastMetasonicCheckAt": "2026-10-31T14:00:00.0000000Z",
  "lastComstructCheckAt": "2026-10-31T14:05:00.0000000Z",
  "lastCheckedAt": "2026-10-31T14:05:00.0000000Z",
  "importedMetasonicDocIds": ["1527018", "1527019"]
}
```

- **`lastMetasonicCheckAt`**: Zeitstempel der zuletzt aus MetaSonic geprüften/importierten Rechnung.
- **`lastComstructCheckAt`**: Zeitstempel (`updated_at`) der zuletzt aus Comstruct nach MetaSonic geschriebenen geprüften Rechnung.
- **`importedMetasonicDocIds`**: Lokaler Cache bereits importierter MetaSonic-Dokument-IDs (zusätzlich zur Live-Prüfung gegen die Comstruct-API).

---

## Hilfreiche Diagnose-Befehle (`brz-read.bat`)

Falls Sie Dokumentenklassen, Qualifizierer oder Kostenstellen in MetaSonic inspizieren möchten:

```cmd
cd DocumentUploadPoC

REM MetaSonic-Login testen:
brz-read.bat dms-login

REM Alle Dokumentenklassen & Qualifizierer-IDs in MetaSonic anzeigen:
brz-read.bat dms-classes

REM Alle Elemente einer Dokumentenklasse (z. B. 89035003 Kostenstellen) auflisten & als CSV speichern:
brz-read.bat dms-list 89035003 kostenstellen.csv

REM Alle Kostenstellen aus 89035003 einmalig komplett nach Comstruct synchronisieren:
brz-read.bat dms-sync-projects 89035003

REM Einzelnes MetaSonic-Dokument inkl. aller Felder & Dateien inspizieren:
brz-read.bat dms-inspect <DocID>
```

## Lizenz

Veröffentlicht unter der [MIT-Lizenz](LICENSE).
