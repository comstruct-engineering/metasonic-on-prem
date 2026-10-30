# Metasonic CLI – Kommandozeilen-Tool für Dokument-Qualifizierer

Kommandozeilen-Werkzeug (CLI) zum Abfragen, Suchen und Aktualisieren von MetaSonic-DMS-Qualifizierern sowie zum Starten des Synchronisations-Workers über den Scanview WCF-Webservice.

## Funktionen

- **Qualifizierer auslesen (`get` / `list`)** – Alle Metadatenfelder eines Dokuments anzeigen
- **Qualifizierer aktualisieren (`update`)** – Werte einzelner Qualifizierer per Befehl ändern (mit anschließender Verifizierung)
- **Sprechende Feldnamen** – z. B. `sachlich` oder `preislich` statt numerischer IDs (`223`, `222`)
- **Dokumentensuche (`search`)** – Dokumente anhand eines Korrelationsfelds (z. B. `external_id`) suchen
- **Synchronisations-Worker (`worker`)** – Den Comstruct → MetaSonic DMS-Abgleich direkt ausführen
- **BRZ-Stammdaten-Worker (`masterdata` / `db-test`)** – Lesende BRZ-Datenbankverbindung testen und Stammdaten (Kreditoren, Kostenstellen, Sachkonten, Steuercodes, Zahlungsbedingungen) nach Comstruct synchronisieren

## Kompilierung

```cmd
cd MetasonicCLI
build.bat
```

Die ausführbare Datei wird unter `bin\Debug\MetasonicCLI.exe` erstellt.

## Verwendung

### Qualifizierer eines Dokuments abrufen

```cmd
MetasonicCLI get 1527018
```

Beispielausgabe:

```
Document ID: 1527018
Qualifiers:
--------------------------------------------------------------------------------
ID              Name                 Value
--------------------------------------------------------------------------------
89900207        Kreditor             Muster Lieferant GmbH
89900202        Belegnummer          RE-2026-001
89900131        Betrag               1250.50
223             Sachlich             ungeprüft
222             Preislich            ungeprüft
```

### Qualifizierer aktualisieren

```cmd
REM Aktualisierung über die Qualifizierer-ID
MetasonicCLI update 1527018 223 richtig

REM Aktualisierung über den sprechenden Namen
MetasonicCLI update 1527018 sachlich richtig
```

Beispielausgabe:

```
Updating document 1527018...
  Qualifier: 223 (Sachlich)
  New value: richtig
  Current value: ungeprüft
✓ Update successful
  Verified value: richtig
```

### Dokument über Korrelationswert suchen

```cmd
set METASONIC_COMSTRUCT_INVOICE_QUALIFIER_ID=<qualifier-id>
MetasonicCLI search EXT-2026-001
```

### Synchronisations-Worker starten

```cmd
MetasonicCLI worker
```

## Befehlsübersicht

| Befehl | Beschreibung | Beispiel |
|--------|--------------|----------|
| `get <docid>` | Alle Qualifizierer eines Dokuments anzeigen | `MetasonicCLI get 1527018` |
| `update <docid> <qualifier> <value>` | Einen Qualifizierer aktualisieren | `MetasonicCLI update 1527018 sachlich richtig` |
| `list <docid>` | Alias für `get` | `MetasonicCLI list 1527018` |
| `search <correlationValue>` | Dokument anhand des Korrelationsfelds suchen | `MetasonicCLI search EXT-2026-001` |
| `worker` | Comstruct → MetaSonic DMS-Synchronisation ausführen | `MetasonicCLI worker` |
| `masterdata` | BRZ-Datenbank → Comstruct Stammdaten-Synchronisation ausführen | `MetasonicCLI masterdata` |
| `db-test` | Lesende BRZ-Datenbankverbindung testen (`SELECT 1`) | `MetasonicCLI db-test` |
| `db-tables [filter]` | Tabellen und Views der BRZ-Datenbank auflisten | `MetasonicCLI db-tables Kred` |
| `db-query "<sql>" [csv]` | Lese-Abfrage auf der BRZ-Datenbank ausführen (optional als CSV speichern) | `MetasonicCLI db-query "SELECT TOP 10 * FROM Kreditoren" out.csv` |
| `dms-classes [docClassId]` | MetaSonic-Dokumentenklassen, Qualifizierer-IDs/Namen, Pflichtfelder, SQL-Lookups & Kontierungs-Klassen anzeigen | `MetasonicCLI dms-classes` |
| `dms-inspect [docId]` | MetaSonic-Dokument inkl. Feldnamen, Dateien und Kontierungstabelle (`GetAccountingTable`) untersuchen | `MetasonicCLI dms-inspect 1527018` |
| `--help` | Hilfe anzeigen | `MetasonicCLI --help` |

## Unterstützte Qualifizierer-Kurzbezeichnungen

| Kurzname | Qualifizierer-ID | Beschreibung | Gültige Werte |
|----------|------------------|--------------|---------------|
| `kreditor` | `89900207` | Name des Kreditors / Lieferanten | Freitext |
| `belegnr` | `89900202` | Rechnungs- bzw. Belegnummer | Freitext |
| `betrag` | `89900131` | Nettobetrag | Numerisch |
| `sachlich` | `223` | Sachliche Prüfung | `richtig`, `falsch`, `ungeprüft` |
| `preislich` | `222` | Preisliche Prüfung | `richtig`, `falsch`, `ungeprüft` |
| `issplit` | `234` | Kennzeichnung für Split-Rechnungen | `JA`, `NEIN` |

## Optionen & Umgebungsvariablen

Die Zugangsdaten können entweder über Umgebungsvariablen oder per Kommandozeilen-Parameter übergeben werden:

| Option | Umgebungsvariable | Beschreibung |
|--------|-------------------|--------------|
| *(keine)* | `METASONIC_ENDPOINT` | Scanview SOAP-URL (Standard: `http://127.0.0.1:8089/ScanviewWebservice.svc`) |
| `--user <benutzer>` | `METASONIC_USER` | MetaSonic-Benutzername |
| `--pass <passwort>` | `METASONIC_PASSWORD` | MetaSonic-Passwort |
| `--db <datenbank>` | `METASONIC_DB` | MetaSonic-Datenbankname |

## Beispiel für Stapelverarbeitung (PowerShell)

```powershell
$docIds = @("1527018", "1527019", "1527020")
foreach ($docId in $docIds) {
    .\bin\Debug\MetasonicCLI.exe update $docId sachlich richtig
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Fehler beim Aktualisieren von Dokument $docId"
        exit 1
    }
}
```

## Lizenz

Veröffentlicht unter der [MIT-Lizenz](../LICENSE).
