# Housekeeping

- **Active Version:** `v0.1.12` (`2026-11-11`)
- **Lifecycle State:** `active` ([`active.state`](file:///D:/Projects/Personal/Housekeeping/active.state))
- **Tech Stack:** .NET 8 (`net8.0-windows`) / C# 12 / ASP.NET Core Kestrel + SignalR / WinForms System Tray + Disposable WebView2 / WinForms Single-Surface Dark Installer / React 19 + TypeScript 5 + Vite 6 PWA
- **Canonical Production Path:** `D:\Programs\Housekeeping\` (Clean Release Root per Standard 12 §3.1)
- **Sister Applications:** Greenroom Companion (`:8420`), Redroom (`:5180`), ShadowLibrary (`:5179`), uStation (`:8460`), StreamEngine (`:8430`), StreamPad, Mother Companion Daemon (`mother.manifest.json` / `app\suite.manifest.json`)

---

## 1. What It Is & Core Goal

**Housekeeping** is Stream's always-on media workstation, archive compliance, cloud synchronization, cryptographic manifest, and game library automation engine. It executes unattended local and multi-machine distributed pipelines across workstation and NAS storage pools while exposing a unified responsive control plane accessible via desktop browser (`http://localhost:5178`), a disposable zero-idle-RAM native WebView2 window, or a Tailscale Serve HTTPS mobile Progressive Web App (PWA).

### Core Goals
- **Unattended Media & Archive Automation:** Orchestrate 8 specialized execution modules (`ProxyGen`, `ArchiveCompressor`, `MediaTranscoder`, `RomLauncherGenerator`, `CloudSync`, `HumbleSync`, `CustomScripts`, and `CollectionManifest`) through independent, reusable jobs, scheduled named queues (`PendingRuns` distinct FIFO queue), and three-tier stop/shutdown controls (`Stop Current Job`, `Stop Entire Queue`, and graceful shutdown checkpoint preservation).
- **Universal Media Manifest & Bit Rot Auditor:** Audit, inventory, and verify media collections (Videos, Movies, TV Shows, Music, ISOs, Archives, Documents, Games) with Two-Step Archive Bit Rot verification (`# Container:` + inner payload fallback), 4-mode `.hash` placement (`0..3`), post-injection `.archive_hashes.json` tracking, delimiter-aware `.csv`/`.tsv`/`.json`/`.html` exports, and ShadowLibrary zero-rewrite parity.
- **Decoupled Game Launchers & Emulation Pipelines:** Generate `.m3u` playlists, `.7z` solid archives, `.bat`/`.lnk` scratch launchers across 16 emulator runners, and `rclone bisync` save syncing—100% decoupled from DAT/manifest/sidecar manipulation.
- **Network-Resilient & Sister-App Safe Execution:** Guard shared NAS targets with Standard 19 `pid` + `processStartTimeUtc` `.hklock` sentinels and `<VaultRoot>\.coexistence.json` (`v2`) discovery across `ShadowLibrary`, `Redroom`, `uStation`, `StreamEngine`, and `Greenroom`; commit `.partial` encodes safely via exclusive `FileShare.None` pre-probes (`PendingSwapService`); resume mid-shutdown runs via `interrupted_run_checkpoint.json`; and sync Clean-Release-Root multi-machine fleet nodes over LAN, SMB, and Tailscale.
- **Clean Release Root & Ecosystem Integration:** Maintain a strictly separated deployed root (`Housekeeping.exe` root launcher stub delegating to `app\`, accompanied by `AGENTS.md`, `mother.manifest.json`, `scripts\`, and `scripts\interactive\` suites) and coordinate disk arbitration and companion daemon state with Mother.

---

## 2. Quickstart & Sandbox CLI Flags

### Production / Normal Launch (Clean Release Root)
```powershell
# Launch via Clean Release Root launcher stub
D:\Programs\Housekeeping\Housekeeping.exe

# Or execute non-pausing operational scripts
D:\Programs\Housekeeping\scripts\start-daemon.bat
D:\Programs\Housekeeping\scripts\status-check.bat
D:\Programs\Housekeeping\scripts\backup-now.bat
```

### Isolated Sandbox Verification (`Sandbox\`)
```powershell
& "D:\Projects\Personal\Housekeeping\Sandbox\app\Housekeeping.exe" `
  --headless --localhost-only --port 15178 `
  --data-dir "D:\Projects\Personal\Housekeeping\Sandbox\data" `
  --config "D:\Projects\Personal\Housekeeping\Sandbox\data\settings.json" `
  --log-dir "D:\Projects\Personal\Housekeeping\Sandbox\logs" `
  --test-mode --dry-run
```

### Build, Test & 4-Pillar Installer Verification
```powershell
npm --prefix "D:\Projects\Personal\Housekeeping\Dev\Housekeeping.Web" run build
dotnet test "D:\Projects\Personal\Housekeeping\Dev\Housekeeping.slnx" -c Release /warnaserror
powershell -NoProfile -ExecutionPolicy Bypass -File "D:\Projects\Personal\Housekeeping\Dev\scripts\build_installer.ps1" -Version "0.1.12"
& "D:\Projects\Personal\Housekeeping\Installers\Housekeeping-Setup-v0.1.12-win-x64.exe" --verify
```

| CLI Flag / Env Var | Default | Purpose |
| :--- | :--- | :--- |
| `--data-dir` / `HOUSEKEEPING_DATA_DIR`, `DATA_DIR`, `APP_HOME` | `<InstallRoot>\data` | Redirects runtime data, history, snapshots, and caches to `<dir>`. |
| `--config` / `HOUSEKEEPING_CONFIG_FILE`, `CONFIG_PATH` | `<data-dir>\settings.json` | Loads and persists configuration at `<file>` with 20-snapshot rollback history. |
| `--log-dir` / `HOUSEKEEPING_LOG_DIR`, `LOG_DIR` | `<InstallRoot>\logs` | Writes `Housekeeping_Live.log` (`10 MB × 5`), `logs\runs\`, `crashes\`, doctor bundles, and `telemetry_heartbeat.json`. |
| `--port` / `HOUSEKEEPING_PORT`, `PORT` | `5178` (`15178` in Sandbox) | Overrides Kestrel HTTP listening port. |
| `--localhost-only` | `false` | Binds strictly to `127.0.0.1` loopback (disables LAN/Tailnet listener). |
| `--headless` / `HOUSEKEEPING_HEADLESS=1` | `false` | Runs Kestrel + SignalR server directly without System Tray or WebView2 GUI. |
| `--status` | `false` | Synchronous one-shot CLI status check (`version`, `dataDir`, `logsDir`, `daemonReachable`, `configValid` JSON) and exits `0`. |
| `--repair-config` | `false` | Synchronous one-shot `settings.json` validator/healer (quarantines corrupt file to `.corrupt_<ts>.bak` and restores) and exits `0`. |
| `--test-mode` / `HOUSEKEEPING_TEST_MODE=1` | `false` | Enables fail-closed sandbox mode (suppresses Registry, Task Scheduler, Tailscale Serve, and OS sleep/shutdown). |
| `--dry-run` / `HOUSEKEEPING_DRY_RUN=1` | `false` | Forces all pipeline and job dispatches into non-destructive simulation mode. |
| `--minimized` / `--tray` / `--silent` | `false` | Starts in System Tray without opening a browser or WebView2 window on launch. |
| `--managed-by-mother` | `false` | Suppresses standalone system tray icon when managed under Mother v1.0 fleet orchestration. |
| `--version`, `-v` / `--help`, `-h` | N/A | Prints application version or CLI usage reference and exits cleanly. |

---

## 3. Architecture At a Glance

- **Application Archetype:** Hybrid System Tray Daemon + Kestrel Web/API Server + Disposable WebView2 Desktop Host + Mobile PWA + Mother Companion Daemon ([Standard 08](file:///D:/Projects/Personal/_DevBible/08_APPLICATION_ARCHETYPES_AND_LIFECYCLE.md))
- **Core Entry Points:** [`Housekeeping.Launcher\Program.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Launcher/Program.cs), [`Housekeeping.DesktopHost\Program.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.DesktopHost/Program.cs), [`Housekeeping.Server\Program.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Program.cs), [`HousekeepingApiEndpoints.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs), [`PipelineOrchestratorService.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/PipelineOrchestratorService.cs)
- **Persistence & State:** Per-caller `{pid}.{guid}.tmp` atomic copy-on-write JSON (`settings.json`, `history.json`, `compliance_reports.json`, `pending_swaps.json`, `interrupted_run_checkpoint.json`) strictly contained in `data\`, 20-snapshot config rollback history in `backups\config_snapshots\`, post-injection `.archive_hashes.json`, `<SharedRoot>\.coexistence.json` (`v2`), and 9-column `CRC32`+`SHA-1` `collection_manifest.csv` ledgers.
- **Port & IPC Allocation:** Production HTTP/SignalR on `:5178` (`15178` in `Sandbox\`); Mother disk arbitration on `/api/scheduler/status` and `/api/scheduler/defer`; Sister-App coexistence on `/api/v1/coexistence/*`.

### Solution Components (`Dev\`)

| Project | Role |
| :--- | :--- |
| [`Housekeeping.Launcher`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Launcher/) | Root thin single-file launcher stub (`Housekeeping.exe`) delegating arguments (including synchronous one-shot `--status` & `--repair-config`) cleanly to `app\Housekeeping.exe` with working directory set to `<InstallRoot>` per Clean Release Root standard (Standard 12 §3.1). |
| [`Housekeeping.Core`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/) | Core pipeline orchestrator with 3-tier stop/graceful-shutdown checkpoint preservation, 8 execution modules, per-caller atomic JSON persistence (`Schema v4` + `[JsonExtensionData]`), universal media manifest & 4-mode `.hash` bit-rot engine (`0..3` + `.archive_hashes.json`), decoupled game launcher generator, 4-pillar `SisterAppCoexistenceService` (`.coexistence.json` `v2`), Standard 19 `pid`+`processStartTimeUtc` `.hklock` coordination, exclusive `FileShare.None` SMB pre-probes, Clean-Release-Root Fleet SMB sync, and filtered zero-PII crash bundles. |
| [`Housekeeping.Server`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/) | Kestrel REST API (`/api/v1/*`), split `/api/v1/pipeline/stop-job` & `stop-queue`, `/api/v1/coexistence/*`, graceful HTTP shutdown (`POST /api/v1/system/shutdown`), `ActiveLogsDir` System Doctor bundles, sibling `backups\` state-only backups, `HousekeepingCliRunner` (`--status`, `--repair-config`), node-scoped SignalR hub (`/hubs/housekeeping`), and 5-step tiered authentication. |
| [`Housekeeping.DesktopHost`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.DesktopHost/) | Single-instance Windows System Tray daemon (`app\Housekeeping.exe`) hosting Kestrel in-process, split `Stop Current Job` / `Stop Entire Queue` tray menu (`TrayMenuFactory`), graceful exit checkpoint preservation, native STA folder/file dialogs, disposable zero-idle-RAM Standalone WebView2 Window (`data\webview2_cache`), and `--managed-by-mother` tray suppression. |
| [`Housekeeping.Installer`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Installer/) | 4-pillar single-surface dark-themed WinForms GUI installer wizard (`650×434` / `650×544`) & uninstaller (`app\Housekeeping-Uninstall.exe`) with `data\` user state migration, `app\` metadata containment (`.install_manifest.json`, `version.txt`, `suite.manifest.json`), fail-closed 3-stage process termination, Authenticode signing, `.7z` pre-upgrade backups, zero PII, and full CLI parity. |
| [`Housekeeping.Web`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Web/) | Responsive desktop & mobile PWA interface with Amber (`#f59e0b`) left sidebar rail (`Ctrl+B` toggle) & consolidated mounted drive strip (`DrivesTableModal`), docked `<MobileBottomNavBar />` (`<= 820px`), drag-resizable `DualPaneTerminal` (`S`/`M`/`L` presets + ring-buffer-safe auto-scroll), `CoexistenceWarningBanner`, single-line Job Builder pills, `<select>` `ProfileSelect` guards, 2-line hover hints, `Ctrl+K` Omni-Search, and `🩺 System Doctor`. |
| [`Housekeeping.Tests`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Tests/) | Automated xUnit unit and live Kestrel/SignalR integration test suite (`183` passing tests across `11` test suites, `100% clean sweep`). |

---

## 4. Project Navigation & Living Docs

- **Feature Inventory & Sister-App Parity Matrix:** [`FEATURES.md`](file:///D:/Projects/Personal/Housekeeping/FEATURES.md)
- **Strategic Roadmap & Milestones:** [`ROADMAP.md`](file:///D:/Projects/Personal/Housekeeping/ROADMAP.md)
- **Version History Ledger:** [`CHANGELOG.md`](file:///D:/Projects/Personal/Housekeeping/CHANGELOG.md)
- **Active Lifecycle State:** [`active.state`](file:///D:/Projects/Personal/Housekeeping/active.state)
- **Project Context Anchor:** [`AGENTS.md`](file:///D:/Projects/Personal/Housekeeping/AGENTS.md)
- **Deep Technical Module Specifications:** [`Dev\MODULES_REFERENCE.md`](file:///D:/Projects/Personal/Housekeeping/Dev/MODULES_REFERENCE.md)
- **Implementation Plans (`@pland`):** [`Dev\docs\plans\`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/plans/)
- **Diagnostic Reports (`@inspecter`):** [`Dev\docs\reports\`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/reports/)

### 📘 Manuals (`Dev\docs\`)

- **📐 Architecture & Engineering Manual:** [`Dev/docs/ARCHITECTURE.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/ARCHITECTURE.md) — System boundaries, Mermaid data-flow diagrams, 5 end-to-end execution pipelines, Standard 08 §8.2 Extract-on-Touch LOC & 400-LOC audit registry, atomic JSON & coexistence artifact ownership, 5-step security model, 183-test strategy map, ADRs, and zero-touch extension playbooks.
- **📘 User Guide:** [`Dev/docs/USER_GUIDE.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/USER_GUIDE.md) — Complete tour of the 8 execution modules, Single-Surface WinForms installer wizard, 7 main views, docked mobile bottom nav (`<= 820px`), split `Stop Job` / `Stop Queue` controls, resizable `DualPaneTerminal`, Sister-App Coexistence presets, everyday workflows, troubleshooting FAQ, and domain glossary.
- **🛠️ Operations & Runbook Manual:** [`Dev/docs/OPERATIONS.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/OPERATIONS.md) — Canonical `D:\Programs\Housekeeping\` Clean Release Root topology, one-shot `--status` & `--repair-config` CLI flags, operational batch scripts, `Schema v4` configuration & 20-snapshot rollback history, `.coexistence.json` (`v2`) / `.archive_hashes.json` / Standard 19 `.hklock` coordination, `ActiveLogsDir` System Doctor bundles, sibling `backups\` archives, `Sandbox\` `--test-mode` verification recipe (`:15178`), and empirical incident playbooks.
- **🔌 Interfaces & API Reference:** [`Dev/docs/INTERFACES.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/INTERFACES.md) — Complete 5-step `TieredAuthService` authentication reference, all REST endpoints (`/api/v1/*`, `/api/v1/coexistence/*`, `/api/scheduler/*`), SignalR `/hubs/housekeeping` events, Sister-App integration seams, CLI & batch scripts, 4-mode `.hash` sidecars, `.archive_hashes.json`, `.coexistence.json` (`v2`), Standard 19 `.hklock` sentinels, CSV/TSV/JSON/HTML manifests, and JSON state schemas.
