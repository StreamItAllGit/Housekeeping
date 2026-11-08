# Housekeeping

- **Active Version:** `v0.1.11` (`2026-11-07`)
- **Lifecycle State:** `active` ([`active.state`](file:///D:/Projects/Personal/Housekeeping/active.state))
- **Tech Stack:** .NET 8 (`net8.0-windows`) / C# 12 / ASP.NET Core Kestrel + SignalR / WinForms System Tray + Disposable WebView2 / WinForms Single-Surface Dark Installer / React 19 + TypeScript 5 + Vite 6 PWA
- **Canonical Production Path:** `D:\Programs\Housekeeping\` (Clean Release Root per Standard 12 §3.1)
- **Sister Applications:** Greenroom Companion (`:8420`), Redroom, ShadowLibrary, StreamPad, Mother Companion Daemon (`mother.manifest.json`)

---

## 1. What It Is & Core Goal

**Housekeeping** is Stream's always-on media workstation, archive compliance, cloud synchronization, cryptographic manifest, and game library automation engine. It executes unattended local and multi-machine distributed pipelines across workstation and NAS storage pools while exposing a unified responsive control plane accessible via desktop browser (`http://localhost:5178`), a disposable zero-idle-RAM native WebView2 window, or a Tailscale Serve HTTPS mobile Progressive Web App (PWA).

### Core Goals
- **Unattended Media & Archive Automation:** Orchestrate 8 specialized execution modules (`ProxyGen`, `ArchiveCompressor`, `MediaTranscoder`, `RomLauncherGenerator`, `CloudSync`, `HumbleSync`, `CustomScripts`, and `CollectionManifest`) through independent, reusable jobs and scheduled named queues (`PendingRuns` distinct FIFO queue).
- **Universal Media Manifest & Bit Rot Auditor:** Audit, inventory, and verify media collections (Videos, Movies, TV Shows, Music, ISOs, Archives, Documents, Games) with Two-Step Archive Bit Rot verification (ShadowLibrary parity: outer container hash check with inner payload fallback), missing baseline `.hash` capture, container auto-healing, and color-coded reporting.
- **Decoupled Game Launchers & Emulation Pipelines:** Generate `.m3u` playlists, `.7z` solid archives, `.bat` scratch launchers, and `rclone bisync` save syncing—strictly decoupled from sidecar manipulation.
- **Network-Resilient & Multi-Node Safe Execution:** Guard shared NAS targets with distributed `.hklock` lockfiles, recover interrupted encodes via `PendingSwapService` (`pending_swaps.json`), resume mid-shutdown runs via `interrupted_run_checkpoint.json`, and coordinate multi-machine fleet deployments over LAN and Tailscale.
- **Clean Release Root & Ecosystem Integration:** Maintain a strictly separated deployed root (`Housekeeping.exe` root launcher stub delegating to `app\`, accompanied by `scripts\` and `scripts\interactive\` suites) and coordinate disk arbitration and companion daemon state with Mother via `mother.manifest.json`.

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
powershell -NoProfile -ExecutionPolicy Bypass -File "D:\Projects\Personal\Housekeeping\Dev\scripts\build_installer.ps1" -Version "1.0.0-rc.11-hf.2"
& "D:\Projects\Personal\Housekeeping\Installers\Housekeeping-Setup-v1.0.0-rc.11-hf.2-win-x64.exe" --verify
```

| CLI Flag / Env Var | Default | Purpose |
| :--- | :--- | :--- |
| `--data-dir` / `HOUSEKEEPING_DATA_DIR`, `DATA_DIR`, `APP_HOME` | `<BaseDir>` | Redirects runtime data, history, snapshots, and caches to `<dir>`. |
| `--config` / `HOUSEKEEPING_CONFIG`, `CONFIG_PATH` | `<data-dir>\settings.json` | Loads and persists configuration at `<file>` with 20-snapshot rollback history. |
| `--log-dir` / `HOUSEKEEPING_LOG_DIR`, `LOG_DIR` | `<data-dir>\logs` | Writes `Housekeeping_Live.log` (`10 MB × 5`), `logs\runs\`, `crashes\`, and `telemetry_heartbeat.json`. |
| `--port` / `HOUSEKEEPING_PORT`, `PORT` | `5178` (`15178` in Sandbox) | Overrides Kestrel HTTP listening port. |
| `--localhost-only` | `false` | Binds strictly to `127.0.0.1` loopback (disables LAN/Tailnet listener). |
| `--headless` | `false` | Runs Kestrel + SignalR server directly without System Tray or WebView2 GUI. |
| `--test-mode` / `HOUSEKEEPING_TEST_MODE=1` | `false` | Enables fail-closed sandbox mode (suppresses Registry, Task Scheduler, Tailscale Serve, and OS sleep/shutdown). |
| `--dry-run` / `HOUSEKEEPING_DRY_RUN=1` | `false` | Forces all pipeline and job dispatches into non-destructive simulation mode. |
| `--minimized` / `--silent` / `--watchdog` | `false` | Starts in System Tray without opening a browser or WebView2 window on launch. |
| `--managed-by-mother` | `false` | Suppresses standalone system tray icon when managed under Mother v1.0 fleet orchestration. |
| `--version`, `-v` / `--help`, `-h` | N/A | Prints application version or CLI usage reference and exits cleanly. |

---

## 3. Architecture At a Glance

- **Application Archetype:** Hybrid System Tray Daemon + Kestrel Web/API Server + Disposable WebView2 Desktop Host + Mobile PWA + Mother Companion Daemon ([Standard 08](file:///D:/Projects/Personal/_DevBible/08_APPLICATION_ARCHETYPES_AND_LIFECYCLE.md))
- **Core Entry Points:** [`Housekeeping.Launcher\Program.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Launcher/Program.cs), [`Housekeeping.DesktopHost\Program.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.DesktopHost/Program.cs), [`Housekeeping.Server\Program.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Program.cs), [`PipelineOrchestratorService.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/PipelineOrchestratorService.cs)
- **Persistence & State:** Atomic copy-on-write JSON (`settings.json`, `history.json`, `compliance_reports.json`, `pending_swaps.json`, `interrupted_run_checkpoint.json`) strictly contained in `data\`, 20-snapshot config rollback history in `backups\config_snapshots\`, and 9-column `CRC32`+`SHA-1` `collection_manifest.csv` ledgers.
- **Port & IPC Allocation:** Production HTTP/SignalR on `:5178` (`15178` in `Sandbox\`); Mother disk arbitration on `/api/scheduler/status` and `/api/scheduler/defer`.

### Solution Components (`Dev\`)

| Project | Role |
| :--- | :--- |
| [`Housekeeping.Launcher`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Launcher/) | Root thin single-file launcher stub (`Housekeeping.exe`) delegating arguments cleanly to `app\Housekeeping.DesktopHost.exe` with working directory set to `<InstallRoot>` per Clean Release Root standard (Standard 12 §3.1). |
| [`Housekeeping.Core`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/) | Core pipeline orchestrator, 8 execution modules, atomic copy-on-write JSON persistence with `data\` containment and legacy root migration (`MigrateLegacyRootStateIfPresent`), Schema v4 auto-upgrade to `StandaloneWindow`, universal media manifest and two-step archive bit rot engine with ShadowLibrary parity, decoupled game launcher generator, distributed `.hklock` locking, and Standard 15 zero-PII crash bundle dumper. |
| [`Housekeeping.Server`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/) | Kestrel REST API (`/api/v1/*`), graceful HTTP shutdown endpoint (`POST /api/v1/system/shutdown`), node-scoped SignalR real-time hub (`/hubs/housekeeping`), tiered authentication (loopback auto-trust, Tailscale Serve identity headers, PBKDF2-SHA256 remote admin sessions, `X-Housekeeping-Token` fleet auth), Mother companion hooks, and remote node proxy/deployment endpoints. |
| [`Housekeeping.DesktopHost`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.DesktopHost/) | Single-instance Windows System Tray daemon (`app\Housekeeping.DesktopHost.exe`) hosting Kestrel in-process, native STA folder/file picker dialogs, disposable zero-idle-RAM Standalone WebView2 Window mode by default (`data\webview2_cache`), `--managed-by-mother` tray suppression, and `--headless` CLI server mode. |
| [`Housekeeping.Installer`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Installer/) | 4-pillar single-surface dark-themed WinForms GUI installer wizard (`650×434` / `650×544`) & uninstaller (`app\Housekeeping-Uninstall.exe`) with `data\` user state migration, `app\` installer metadata containment (`.install_manifest.json`, `version.txt`), fail-closed 3-stage process lifecycle termination, post-backup root clutter cleanup (`CleanupObsoleteInstallationFiles`), automatic daemon relaunch (`--launch`/`--no-launch`), Authenticode signing, `.7z` pre-upgrade backups, zero PII, and full CLI parity. |
| [`Housekeeping.Web`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Web/) | Responsive desktop & mobile PWA interface with Amber (`#f59e0b`) left sidebar navigation rail (`220px` / `56px`), full-width dashboard layout with docked bottom terminal drawer/footer, global `Ctrl+K` Omni-Search modal, `?` keyboard shortcuts cheat-sheet modal, `🩺 System Doctor` diagnostics and backup sub-tab in Settings, table multi-column sorting (`▲/▼`), row selection checkboxes, CSV/JSON data export, and tabular numerals. |
| [`Housekeeping.Tests`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Tests/) | Automated xUnit unit and live Kestrel/SignalR integration test suite (`160` passing tests across 6 test suites, `100% clean sweep`). |

---

## 4. Project Navigation & Living Docs

- **Feature Inventory & Sister-App Parity Matrix:** [`FEATURES.md`](file:///D:/Projects/Personal/Housekeeping/FEATURES.md)
- **Strategic Roadmap & Milestones:** [`ROADMAP.md`](file:///D:/Projects/Personal/Housekeeping/ROADMAP.md)
- **Version History Ledger:** [`CHANGELOG.md`](file:///D:/Projects/Personal/Housekeeping/CHANGELOG.md)
- **Active Lifecycle State:** [`active.state`](file:///D:/Projects/Personal/Housekeeping/active.state)
- **Deep Technical Module Specifications:** [`Dev\MODULES_REFERENCE.md`](file:///D:/Projects/Personal/Housekeeping/Dev/MODULES_REFERENCE.md)
- **Implementation Plans (`@pland`):** [`Dev\docs\plans\`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/plans/)
- **Diagnostic Reports (`@inspecter`):** [`Dev\docs\reports\`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/reports/)

### 📘 Manuals (`Dev\docs\`)

- **📐 Architecture & Engineering Manual:** [`Dev/docs/ARCHITECTURE.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/ARCHITECTURE.md) — System boundaries, Mermaid data-flow diagrams, 5 end-to-end execution pipelines, 68-file LOC & 400-LOC audit registry, atomic JSON persistence ownership, 5-step security model, 166-test strategy map, ADRs, and zero-touch extension playbooks.
- **📘 User Guide:** [`Dev/docs/USER_GUIDE.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/USER_GUIDE.md) — Complete tour of the 8 execution modules, Single-Surface WinForms installer wizard, 5 starter jobs, 7 main views, 8 module editors, 8 Settings sub-tabs (including `🩺 System Doctor`), `DualPaneTerminal`, everyday workflows, troubleshooting FAQ, and domain glossary.
- **🛠️ Operations & Runbook Manual:** [`Dev/docs/OPERATIONS.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/OPERATIONS.md) — Canonical `D:\Programs\Housekeeping\` Clean Release Root topology, CLI flags & environment variables, `Schema v4` configuration & 20-snapshot rollback history, port/mutex/`.hklock`/Mother/Greenroom coexistence, rolling logs (`10 MB × 5`), `telemetry_heartbeat.json` (`10s`/`30s`), zero-PII crash bundles, backup/restore/rollback runbooks, `Sandbox\` `--test-mode` verification recipe (`:15178`), and empirical incident playbooks.
- **🔌 Interfaces & API Reference:** [`Dev/docs/INTERFACES.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/INTERFACES.md) — Complete 5-step `TieredAuthService` authentication reference, all 70+ REST endpoints (`/api/v1/*`, `/api/*`, `/api/scheduler/*`), SignalR `/hubs/housekeeping` events and hub methods, Sister-App integration seams (Mother, ShadowLibrary, Greenroom), CLI & batch scripts, `.hash` sidecars (single/multi-file, composite folder `COMPOSITE_SHA1`, `# Container:` two-step header), `.hklock` distributed lockfiles, CSV/JSON/HTML manifests, and JSON state schemas.
