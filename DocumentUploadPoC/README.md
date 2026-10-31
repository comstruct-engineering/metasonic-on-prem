# Comstruct ↔ MetaSonic DMS Bidirectional Sync Worker

Automatisierter Hintergrund-Worker (`run.bat` / `DmsUploadWorker.cs`), der in einem einzigen Durchlauf beide Richtungen zwischen **MetaSonic DMS** und **Comstruct** synchronisiert:

## Ablauf (`run.bat`)

### Phase 1: MetaSonic → Comstruct (`RunPhase1MetasonicToComstruct`)
1. Sucht in MetaSonic (`METASONIC_DOC_CLASS_ID`) nach Dokumenten, deren Änderungs-/Erstellungsdatum neuer als `lastMetasonicCheckAt` (aus `.dms-worker-state.json`) ist.
2. Prüft über die Comstruct-API (`GET /v1/invoices` & `GET /v1/workflows/status/{external_id}`), ob die Rechnung mit `external_id = <MetaSonic-DocID>` bereits in Comstruct existiert.
3. Liest Kreditor und Projekt/Kostenstelle aus den MetaSonic-Qualifizierern aus (entweder über konfigurierte Qualifier-IDs oder vollautomatisch anhand der Feldnamen in der Dokumentenklasse):
   - Stellt sicher, dass der **Lieferant** in Comstruct existiert (`POST /v1/suppliers`, Fallback `external_id = <DocID>`).
   - Prüft per `GET /v1/projects/byExternalId` bzw. `byNumber`, ob das **Projekt** in Comstruct existiert; falls nicht, wird es per `POST /v1/projects` angelegt (mit Namensauflösung aus der MetaSonic-Kostenstellen-Dokumentenklasse `METASONIC_PROJECTS_DOC_CLASS_ID=89035003` bzw. Fallback `external_id = <DocID>`).
4. Lädt die Original-Rechnungsdatei (`GetItemData`) aus MetaSonic und legt die Rechnung in Comstruct mit `external_id = <DocID>` an (`POST /v1/invoices/custom` bzw. Fallback `POST /v1/workflows/trigger`).
5. Speichert `lastMetasonicCheckAt` und die importierte `DocID` sofort in `.dms-worker-state.json`.

### Phase 2: Comstruct → MetaSonic (`RunPhase2ComstructToMetasonic`)
1. Ruft alle Rechnungen in Comstruct ab, die den Status `CHECKED` (`COMSTRUCT_DMS_QUEUE_STATUS`) haben, seit `lastComstructCheckAt` aktualisiert wurden und deren `external_status` noch nicht `SYNCED` (`COMSTRUCT_DMS_PROCESSED_STATUS`) ist.
2. Lädt das geprüfte PDF inklusive **Comstruct-Prüfstempel (`overlay=true`) und Annotationen (`annotations=true`)** herunter (`GET /v1/invoices/{id}/pdf?annotations=true&overlay=true`).
3. Hängt das gestempelte PDF per `AddItem` als zusätzliches Dokument (`<Originaldateiname>_geprueft_von_comstruct.pdf`) an das MetaSonic-Dokument (`external_id`) an.
4. Aktualisiert optional konfigurierte MetaSonic-Qualifizierer (`METASONIC_COMSTRUCT_SYNCED_QUALIFIER_ID`, `METASONIC_COMSTRUCT_URL_QUALIFIER_ID`).
5. Setzt `external_status = SYNCED` an der Rechnung in Comstruct (`PATCH /v1/invoices/{id}/external-status`) und aktualisiert `lastComstructCheckAt` in `.dms-worker-state.json`.

## Konfiguration (`config.env`)

Siehe `config.env.example` für die minimale Konfiguration:

| Variable | Standard | Beschreibung |
|---|---|---|
| `COMSTRUCT_API_KEY` | *(Pflicht)* | Comstruct API-Schlüssel |
| `COMSTRUCT_NEST_URL` | `https://api.comstruct.com` | Basis-URL der Comstruct-API |
| `METASONIC_ENDPOINT` | *(Pflicht)* | MetaSonic Scanview SOAP-URL (`http://...:8089/ScanviewWebservice.svc`) |
| `METASONIC_USER` | *(Pflicht)* | MetaSonic-Benutzername (z. B. `SVPADM`) |
| `METASONIC_PASSWORD` | *(Pflicht)* | MetaSonic-Passwort |
| `METASONIC_DB` | *(Pflicht)* | MetaSonic-Datenbankname (z. B. `BRZARCHIVMP`) |
| `METASONIC_DOC_CLASS_ID` | `1` | Dokumentenklassen-ID der Rechnungen in MetaSonic |
| `METASONIC_PROJECTS_DOC_CLASS_ID` | `89035003` | Dokumentenklassen-ID der Kostenstellen/Projekte in MetaSonic |
| `COMSTRUCT_DMS_QUEUE_STATUS` | `CHECKED` | Status geprüfter Rechnungen in Comstruct für den Rücktransfer |
| `COMSTRUCT_DMS_PROCESSED_STATUS` | `SYNCED` | `external_status`-Wert nach erfolgreichem Rücktransfer |
| `COMSTRUCT_PDF_OVERLAY` | `true` | Prüfstempel im PDF einbetten (`overlay=true`) |
| `COMSTRUCT_PDF_ANNOTATIONS` | `true` | Annotationen im PDF einbetten (`annotations=true`) |
| `DMS_WORKER_STATE_FILE` | `.dms-worker-state.json` | Pfad zur Statusdatei für `lastMetasonicCheckAt` & `lastComstructCheckAt` |
| `DMS_WORKER_DRY_RUN` | `false` | `true` aktiviert den Simulationsmodus ohne Schreibzugriffe |
