# Project Context: Housekeeping

- **Role:** Automated Storage Hygiene, Media Transcoding, Bit-Rot Auditing & Cloud Sync Engine (`v0.2.0` Public Beta, `Foundation v0.5.3`, `MPL-2.0`).
- **Layout & Tests:** 4-Folder structure (`Archive/`, `Builds/`, `Dev/`, `Releases/`); isolated unit/integration tests via `APP_ISOLATED_TEST=1` (`dotnet test Dev/Housekeeping.slnx -c Release -p:TreatWarningsAsErrors=true`) and 4-Drive Sandbox E2E (`Dev/scripts/run_stage2_e2e.py`).
- **Install Paths (Read-Only in Dev):** `%LOCALAPPDATA%\Programs\StreamItAll\Mother\Housekeeping\` (default suite install) or custom `--install-dir` / `--suite-dir`; human review gate (`requireHumanReviewBeforePush: true`) before any push or live deployment.

## Root Docs & State
- [README.md](README.md) | [FEATURES.md](FEATURES.md) | [ROADMAP.md](ROADMAP.md) | [CHANGELOG.md](CHANGELOG.md)
- [SECURITY.md](SECURITY.md) | [CONTRIBUTING.md](CONTRIBUTING.md) | [LICENSE](LICENSE) | [active.state](active.state)

## Technical Manuals & Engineering (`Dev/docs/`)
- **Manual Shelf:** [ARCHITECTURE.md](Dev/docs/ARCHITECTURE.md) | [INTERFACES.md](Dev/docs/INTERFACES.md) | [USER_GUIDE.md](Dev/docs/USER_GUIDE.md) | [OPERATIONS.md](Dev/docs/OPERATIONS.md)
- **Local Plans & Reports (Gitignored):** `Dev/docs/plans/` | `Dev/docs/reports/`
- **Local Hub Metadata (Gitignored, Manual-Only Sync):** [Dev/data/notion_hub.local.json](Dev/data/notion_hub.local.json)
