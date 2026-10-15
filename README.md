# metasonic-on-prem

Integration tooling between **Comstruct** and **MetaSonic Scanview DMS** (for **BRZ 7**, **BRZ 8**, and **BRZ 365** environments).

Because BRZ does not expose a native third-party REST API, Comstruct integrates via the **MetaSonic Scanview SOAP/WCF webservice** (`ScanviewWebservice.svc`). This repository provides an on-premises synchronization worker and CLI tools that fetch verified invoices from Comstruct, attach PDFs and verification results (such as *Sachlich geprüft*, *Preislich geprüft*, *Geprüft von Comstruct*, and deep links) to MetaSonic DMS documents, and report synchronization status back to Comstruct.

For a complete overview of the integration architecture, supported workflows, and prerequisites, see the Comstruct Knowledge Base article: [BRZ und Metasonic DMS an comstruct anbinden](https://knowledge.comstruct.com/articles/1464016790-brz-metasonic-x-comstruct-anbindung).

## Repository layout

| Path | Role |
|------|------|
| **`DocumentUploadPoC/`** | **C# DMS upload worker (.NET Framework 4.7.2)** — main on-premises Windows worker (`run.bat`) scheduled via Windows Task Scheduler (by default every 5 minutes). |
| `metasonic-cli-ts/` | Cross-platform Node.js / TypeScript CLI, DMS upload worker, and in-process Scanview SOAP mock server with unit/integration tests. |
| `MetasonicCLI/` | C# command-line tool for reading, searching, and updating document qualifiers; shares worker code with `DocumentUploadPoC`. |
| `DmsFormular1` (`Form1.*`) | Legacy Windows Forms client for viewing and updating document qualifiers via the Scanview WCF service. |
| `METASONIC-WEBSERVICE_DEMO/` | Minimal WCF WinForms demo project. |
| `POC_SUMMARY.md` | Technical architecture notes and PoC findings. |

## Quick start (On-Premises Windows Worker)

1. Read **[DocumentUploadPoC/README.md](DocumentUploadPoC/README.md)**.
2. Copy **`DocumentUploadPoC/config.env.example`** to **`config.env`** (local only; gitignored) as a reference for your settings.
3. Configure **`METASONIC_*`** and **`COMSTRUCT_*`** environment variables in **`DocumentUploadPoC/run.bat`** (or system environment variables) — **never commit real credentials or API keys**.
4. Run `run.bat` manually to verify connectivity, or schedule it every 5 minutes in **Windows Task Scheduler**.

## Related documentation

- **Knowledge Base (BRZ & MetaSonic):** [BRZ und Metasonic DMS an comstruct anbinden](https://knowledge.comstruct.com/articles/1464016790-brz-metasonic-x-comstruct-anbindung)
- **Windows worker setup:** [DocumentUploadPoC/README.md](DocumentUploadPoC/README.md)
- **TypeScript CLI & mock server:** [metasonic-cli-ts/README.md](metasonic-cli-ts/README.md)
- **Windows CLI:** [MetasonicCLI/README.md](MetasonicCLI/README.md) and [CLI_QUICKSTART.md](CLI_QUICKSTART.md)
- **PoC summary & architecture:** [POC_SUMMARY.md](POC_SUMMARY.md)

## Requirements (Windows Worker)

- Windows Server or workstation with **.NET Framework 4.7.2+**
- Outbound HTTPS access to `https://api.comstruct.com` and network access to your MetaSonic Scanview endpoint (`ScanviewWebservice.svc`)
- Visual Studio Build Tools (or Visual Studio 2019/2022) for building from source

## License

Released under the [MIT License](LICENSE).
