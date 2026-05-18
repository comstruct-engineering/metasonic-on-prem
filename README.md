# metasonic-on-prem

On-premises integration work between **Comstruct** and **MetaSonic Scanview DMS** (WCF): fetch invoice PDFs from Comstruct, match them to DMS documents, attach files, and report status back.

## On-site delivery (Reif, C#)

The **production-style piece that was implemented and run on site at Reif** is the **C# .NET Framework solution** under **`DocumentUploadPoC/`**. That project contains the **Comstruct → MetaSonic DMS upload worker** (batch process, WCF client, Comstruct REST client) and is what operators use on Windows with `run.bat` after setting environment variables (see `DocumentUploadPoC/config.env.example` and `DocumentUploadPoC/README.md`).

The rest of this repository is supporting material: WinForms demos, CLI variants, TypeScript tooling, and technical notes from the PoC.

## Repository layout

| Path | Role |
|------|------|
| **`DocumentUploadPoC/`** | **C# worker** — main on-site integration for Reif; build and run via `run.bat`. |
| `metasonic-cli-ts/` | Node/TypeScript CLI and tests (mock-friendly, cross-platform). |
| `MetasonicCLI/` | C# CLI for qualifier updates; shares DMS worker code with `DocumentUploadPoC`. |
| `DmsFormular1` / `Form1.*` | Legacy WinForms app using the Scanview WCF client. |
| `METASONIC-WEBSERVICE_DEMO/` | Small WCF demo project. |
| `POC_SUMMARY.md` | Stakeholder-oriented PoC summary and technical findings. |

## Quick start (Reif worker)

1. Read **`DocumentUploadPoC/README.md`**.
2. Copy **`DocumentUploadPoC/config.env.example`** to **`config.env`** (local only; gitignored).
3. Set **`METASONIC_*`** and **`COMSTRUCT_*`** in **`DocumentUploadPoC/run.bat`** (or system environment variables) — **never commit secrets**.

## Related docs

- **Worker setup:** [DocumentUploadPoC/README.md](DocumentUploadPoC/README.md)
- **PoC narrative:** [POC_SUMMARY.md](POC_SUMMARY.md)
- **TS CLI:** [metasonic-cli-ts/README.md](metasonic-cli-ts/README.md)
- **Windows CLI:** [MetasonicCLI/README.md](MetasonicCLI/README.md)

## Requirements (worker)

- Windows, **.NET Framework 4.7.2+**
- Reachability of your MetaSonic Scanview endpoint and Comstruct API
- Visual Studio Build Tools (or VS) for the first build unless you rely on existing `bin` output
