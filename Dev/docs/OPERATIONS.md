# Housekeeping — Operations, Deployment & Troubleshooting Runbook

- **Document:** `OPERATIONS.md` (Technical Manual 3 of 4)
- **Active Build Version:** `v1.0.0-rc.11-hf.2` (In-Dev Target: `v1.0.0-rc.12`)
- **Canonical Production Root:** `D:\Programs\Housekeeping\`
- **Development & Sandbox Roots:** `D:\Projects\Personal\Housekeeping\Dev\` & `D:\Projects\Personal\Housekeeping\Sandbox\`
- **Default Ports:** HTTP / SignalR `:5178` (`:15178` in Sandbox) · Tailscale Serve HTTPS Proxy `:443` / `:8578` $\rightarrow$ `http://127.0.0.1:5178`

---

## 1. Install Topology & Canonical Directories

Per Dev Bible [Standard 01](file:///D:/Projects/Personal/_DevBible/01_PROJECT_LAYOUTS.md) and [Standard 12 §3.1](file:///D:/Projects/Personal/_DevBible/12_WORKSPACE_AND_DEPLOYMENT_CLEANLINESS.md), Housekeeping separates its outer 6-folder workspace (`D:\Projects\Personal\Housekeeping\`) from its clean deployed runtime layout (`D:\Programs\Housekeeping\` or `Builds\current\`):

```text
D:\Programs\Housekeeping\                     # Canonical Production Root (<InstallRoot>)
├── Housekeeping.exe                          # Root Launcher Shim (Housekeeping.Launcher, 152 KB)
├── mother.manifest.json                      # Mother v1.0 Fleet supervision manifest
├── app\                                      # Compiled application binaries & wwwroot SPA
│   ├── Housekeeping.exe                      # Primary WinForms Tray + WebView2 Host (Housekeeping.DesktopHost)
│   ├── Housekeeping.Server.exe               # Headless Kestrel server executable
│   ├── Housekeeping.Core.dll                 # Core domain, services & 8 pipeline modules
│   ├── Housekeeping.Server.dll               # ASP.NET Core Minimal APIs & SignalR hub
│   ├── wwwroot\                              # Compiled React 19 + Vite 6 SPA assets
│   └── tools\                                # Optional bundled portable CLI tools
├── data\                                     # Canonical runtime state (<DataDir>, resolved by ConfigService)
│   ├── settings.json                         # Active Schema v4 configuration (+ settings.json.bak)
│   ├── history.json                          # Pipeline execution history log
│   ├── compliance_reports.json               # Archive compliance & .hash findings store
│   ├── pending_swaps.json                    # Deferred .partial transcode swaps
│   ├── interrupted_run_checkpoint.json       # Mid-shutdown recovery checkpoint (present only when interrupted)
│   ├── .hk_cookie_secret.key                 # 32-byte hex HMAC-SHA256 session cookie signing key
│   ├── dats\                                 # No-Intro & Redump .dat / .xml files
│   ├── manifests\                            # Default output directory for collection_manifest.csv/.json/.html
│   ├── tools\                                # User/auto-provisioned CLI binaries (HandBrakeCLI, 7z, rclone, hbd)
│   └── webview2_cache\                       # DisposableWebViewForm Chromium user data folder
├── backups\
│   └── config_snapshots\                     # 20 most recent SHA-256 deduplicated settings.json snapshots + .7z backups
├── logs\                                     # Canonical log root (<LogsDir>)
│   ├── Housekeeping_Live.log                 # Rolling live log (10 MB x 5 generations: .1.log .. .5.log)
│   ├── telemetry_heartbeat.json              # 10s (active) / 30s (idle) JSON health heartbeat
│   ├── runs\                                 # Per-run transcripts (Run_<Queue>_<yyyyMMdd_HHmmss>.log, 30-day retention)
│   └── crashes\                              # Zero-PII JSON crash bundles (crash_<yyyyMMdd_HHmmss>_<id>.json, max 30)
└── scripts\                                  # Non-interactive & interactive operational batch utilities
    ├── start.bat
    ├── stop.bat
    ├── restart.bat
    ├── status-check.bat
    ├── backup-data.bat
    └── interactive\
        ├── launch-ui.bat
        └── repair-config.bat
```

---

## 2. Executables, CLI Flags & Environment Variables

### 2.1 Runtime Executables (`Housekeeping.exe` / `app\Housekeeping.exe` / `app\Housekeeping.Server.exe`)
Parsed by [`HousekeepingCliParser.Parse`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Hosting/HousekeepingCliParser.cs#L43-L237) and [`DesktopHost\Program.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.DesktopHost/Program.cs#L19-L66):

| CLI Flag | Env Var Equivalent | Default | Behavior |
| :--- | :--- | :--- | :--- |
| `--data-dir <path>` (or `--data-dir=<path>`) | `HOUSEKEEPING_DATA_DIR`, `DATA_DIR`, `APP_HOME` | `<InstallRoot>\data` | Overrides `<DataDir>` for `settings.json`, `history.json`, `compliance_reports.json`, `pending_swaps.json`, `dats\`, and `manifests\`. Marks invocation as sandboxed. |
| `--config <filePath>` (or `--config=<filePath>`) | `HOUSEKEEPING_CONFIG_FILE`, `CONFIG_PATH` | `<DataDir>\settings.json` | Overrides the active JSON configuration file path. |
| `--log-dir <path>` (or `--log-dir=<path>`) | `HOUSEKEEPING_LOG_DIR`, `LOG_DIR` | `<InstallRoot>\logs` (or `<DataDir>\logs` when custom `--data-dir` is passed) | Overrides `<LogsDir>` for `Housekeeping_Live.log`, `runs\`, `crashes\`, and `telemetry_heartbeat.json`. |
| `--port <1..65535>` (or `--port=<n>`) | `HOUSEKEEPING_PORT`, `PORT` | `5178` (`15178` in Sandbox) | Overrides `WebServer.Port` for Kestrel HTTP & SignalR binding. |
| `--headless` | `HOUSEKEEPING_HEADLESS=1` | `false` (Tray Host) / `true` (`Server.exe`) | Runs Kestrel web/API server and background scheduler without WinForms system tray UI or WebView2 window. |
| `--test-mode` | `HOUSEKEEPING_TEST_MODE=1`, `TEST_MODE=1` | `false` | Enables sandbox containment (`ConfigService.IsTestMode = true`), disables post-run OS sleep/shutdown, and scopes single-instance mutex to `Global\Housekeeping_3_Scoped_<hash>`. |
| `--dry-run` | `HOUSEKEEPING_DRY_RUN=1`, `DRY_RUN=1` | `false` | Sets `PipelineOrchestrator.ForceDryRun = true` so every manual or scheduled run logs `[DRY]` actions without modifying disk. |
| `--minimized` / `--tray` | — | `false` | Starts the tray daemon silently without popping open the standalone WebView2 window or browser on startup. |
| `--version` / `-v` | — | — | Prints `Housekeeping v<Version>` and exits with code `0`. |
| `--help` / `-h` / `/?` | — | — | Prints CLI usage reference and exits with code `0`. |

> [!WARNING]
> **Operator Note on `scripts\status-check.bat` and `scripts\interactive\repair-config.bat`:** In `v1.0.0-rc.11-hf.2`, [`status-check.bat`](file:///D:/Projects/Personal/Housekeeping/Dev/scripts/status-check.bat#L1-L7) invokes `Housekeeping.exe --headless --status` and [`repair-config.bat`](file:///D:/Projects/Personal/Housekeeping/Dev/scripts/interactive/repair-config.bat#L1-L8) invokes `Housekeeping.exe --repair-config`. Because [`HousekeepingCliParser.Parse`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Hosting/HousekeepingCliParser.cs#L196-L200) places unrecognized flags into `RemainingArgs` rather than exiting immediately, those two batch files currently start a server session instead of performing a one-shot exit. To check live status non-interactively, query `Invoke-RestMethod http://127.0.0.1:5178/api/v1/health` (or `/api/scheduler/status`); to repair or restore `settings.json`, use `POST /api/v1/config/snapshots/{id}/restore` or copy `data\settings.json.bak` over `data\settings.json` while stopped.

### 2.2 Installer CLI Flags (`Housekeeping-Setup-v*.exe`)
Parsed by [`Housekeeping.Installer\Program.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Installer/Program.cs#L1797-L1914):
- `--install-dir <dir>` / `--target-dir <dir>`: Target installation root (default `%LOCALAPPDATA%\Programs\Housekeeping`, or `HOUSEKEEPING_INSTALL_DIR` / `INSTALL_DIR`).
- `--silent` / `--quiet` / `--headless`: Non-interactive console execution (skips WinForms wizard).
- `--verify`: Verifies embedded `payload.zip` SHA-256 hash against `metadata.json` and exits (`0` = valid).
- `--dry-run`: Verifies payload and logs planned install actions without mutating disk.
- `--test-mode`: Enforces fail-closed sandbox path validation (rejects any path outside `Sandbox\` / `Temp\`), skips Windows Registry uninstall keys and Start Menu `.lnk` creation, and requires `--data-dir`, `--config`, and `--log-dir` containment.
- `--launch` / `-l` and `--no-launch`: Explicitly launch or suppress launching `Housekeeping.exe` after setup.
- `--no-shortcuts`: Skips Start Menu and root shortcut creation.
- `--uninstall` (and optional `--purge-data`): Removes `app\` binaries and uninstall registry entry (preserving `data\`, `logs\`, and `backups\` unless `--purge-data` is passed).

---

## 3. Configuration & Settings File Reference (`data\settings.json`)

[`ConfigService`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/ConfigService.cs#L209-L762) manages `<DataDir>\settings.json` at **`ConfigVersion = 4` (`CurrentSchemaVersion = 4`)**:

- **Automatic Schema Migration (`MigrateConfig` [`L573-L602`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/ConfigService.cs#L573-L602)):**
  - `v1 -> v2`: Seeds `DefinedJobs` and `NamedQueues` from legacy single-module configs.
  - `v2 -> v3`: Initializes built-in `ArchiveComplianceTemplate` definitions (`auto-community`, `redump-optical`, `nointro-cartridge`, `community-modern-disc`, `official-folder-dump`).
  - `v3 -> v4`: Migrates `WebServer.DefaultDesktopLaunchMode` from `"Browser"` to `"StandaloneWindow"` so tray double-click opens `DisposableWebViewForm`.
- **Legacy Root-to-`data\` Auto-Migration (`MigrateLegacyRootStateIfPresent` [`L218-L332`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/ConfigService.cs#L218-L332)):**
  - On startup (and during installer upgrade), if `settings.json`, `history.json`, `compliance_reports.json`, `pending_swaps.json`, `dats\`, `manifests\`, or `tools\` exist loose in `<InstallRoot>\`, they are atomically moved/merged into `<InstallRoot>\data\`.
- **20-Snapshot Rollback History (`CreateSnapshotIfChanged` [`L857-L949`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/ConfigService.cs#L857-L949)):**
  - Every `TryUpdateAndSave` call hashes the serialized JSON with SHA-256; if content changed, a timestamped snapshot (`settings_yyyyMMdd_HHmmss_fff.json`) is written to `<InstallRoot>\backups\config_snapshots\` and older files beyond **20** are pruned.

---

## 4. Ports, Single-Instance Mutex, Locks & Sister-App Coexistence

| Resource / Mechanism | Identifier / Value | Scope & Coexistence Rules |
| :--- | :--- | :--- |
| **Production HTTP & SignalR Port** | `5178` (`http://127.0.0.1:5178` or `0.0.0.0:5178`) | Configured in `WebServer.Port` and `mother.manifest.json`. If port `5178` is occupied during a non-production/test launch, [`HousekeepingServerHost.StartAsync`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Hosting/HousekeepingServerHost.cs#L135-L178) automatically falls back to an ephemeral port (`127.0.0.1:0`) so tests never crash. |
| **Sandbox Verification Port** | `15178` | Passed via `--port 15178` during `Sandbox\` verification so sandbox runs never collide with live production on `:5178`. |
| **Tailscale Serve HTTPS Port** | `443` (or `8578` in `mother.manifest.json`) | Managed via [`TailscalePwaService`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Services/TailscalePwaService.cs#L140-L265) (`tailscale serve --bg --https=443 http://127.0.0.1:5178`). |
| **Single-Instance Mutex** | `Global\Housekeeping_2_SingleInstance_Mutex` | Enforced in [`DesktopHost\Program.cs#L16,L199-L218`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.DesktopHost/Program.cs#L199-L218). When any sandbox flag (`--test-mode`, `--dry-run`, `--headless`, `--port`, `--data-dir`, `--config`, `--log-dir`) is present, switches to `Global\Housekeeping_3_Scoped_<12hex>` so sandbox instances never collide with production or trigger `HOUSEKEEPING_RESTORE_WINDOW` on the live tray daemon. |
| **Distributed Transcode Lockfile** | `<finalOutputPath>.hklock` | JSON lockfile written by [`NetworkSafetyService.TryAcquireDistributedLock`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/NetworkSafetyService.cs#L383-L466) with a 60s heartbeat timer and 90-minute stale reclamation timeout. |
| **Mother v1.0 Fleet Coexistence** | [`mother.manifest.json`](file:///D:/Projects/Personal/Housekeeping/Dev/mother.manifest.json) | Declares `health_endpoint: "http://127.0.0.1:5178/api/v1/health"`, `scheduler_status_endpoint: "/api/scheduler/status"`, `scheduler_defer_endpoint: "/api/scheduler/defer"`, and `graceful_shutdown_endpoint: "/api/system/shutdown"`. Mother calls `/api/scheduler/defer` during heavy cross-app disk I/O to pause Housekeeping schedules cleanly. |
| **Greenroom (`:8420`) Coexistence** | `http://127.0.0.1:8420/api/sync` | [`GreenroomSyncIntegrationService`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/GreenroomSyncIntegrationService.cs#L170-L321) posts `collect_library` + `verify_hashes` (Module 4) or `collect_library` + `collect_plex` (Module 8) only when SHA-256 manifest fingerprints actually change, falling back to `run_greenroom.bat` if `:8420` is offline. |

---

## 5. Logs, Heartbeats, Crash Bundles & System Doctor

1. **Rolling Live Log (`<LogsDir>\Housekeeping_Live.log`):**
   - Managed by [`TerminalLogBufferService`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/TerminalLogBufferService.cs#L24-L268).
   - Rotates at **`10 MB` (`MaxLiveLogBytes = 10 * 1024 * 1024`)** across **5 generations** (`Housekeeping_Live.1.log` through `Housekeeping_Live.5.log`).
2. **Per-Run Transcripts (`<LogsDir>\runs\Run_<Queue>_<yyyyMMdd_HHmmss>.log`):**
   - Attached at the start of every queue/job execution and pruned automatically after **30 days** (`MaxRunLogRetentionDays = 30`) by [`TerminalLogBufferService.PruneStaleRunLogs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/TerminalLogBufferService.cs#L173-L204).
3. **10s / 30s Telemetry Heartbeat (`<LogsDir>\telemetry_heartbeat.json`):**
   - Written atomically via `.tmp` swap by [`SystemPerformanceTelemetryService.PublishHeartbeatFile`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/SystemPerformanceTelemetryService.cs#L197-L234) every **10 seconds** during active pipeline execution and every **30 seconds** while idle.
4. **Zero-PII Crash Bundles (`<LogsDir>\crashes\crash_<yyyyMMdd_HHmmss>_<8hex>.json`):**
   - Written by [`CrashBundleService`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/CrashBundleService.cs#L115-L425) on any unhandled exception with `SchemaVersion: "1.0"`, sanitized command line, failing module/file/line, stack trace, and the 50 preceding terminal log lines (`PrecedingLogRingBuffer`), retaining the 30 newest bundles.
5. **🩺 System Doctor (`Settings` $\rightarrow$ `System Doctor` & `/api/v1/doctor/*`):**
   - **`POST /api/v1/doctor/backup`:** Creates a timestamped `.7z` (or `.zip` fallback) archive of `settings.json`, `history.json`, `compliance_reports.json`, and `pending_swaps.json` in `<InstallRoot>\backups\config_snapshots\`.
   - **`GET /api/v1/doctor/diagnostic-bundle`:** Downloads a sanitized JSON diagnostic bundle containing app version, OS/machine info, dependency status, mounted drive metrics, sanitized `AppConfig`, recent history, and the last 250 log lines.

---

## 6. Backup, Restore & Migration Procedures

### 6.1 Backing Up Canonical Production State
Always back up `D:\Programs\Housekeeping\data\` before manual migrations or major upgrades:
```powershell
# Option A: Use the built-in batch script (creates a timestamped .7z in backups\config_snapshots\)
D:\Programs\Housekeeping\scripts\backup-data.bat

# Option B: Trigger via REST API while Housekeeping is running
Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:5178/api/v1/doctor/backup"
```

### 6.2 Restoring a Configuration Snapshot
- **From Web / Desktop UI:** Click **↺ History (`N`)** in the Top Command Bar, choose a snapshot from the table, and click **Restore**.
- **From Disk While Stopped:**
  1. Run `D:\Programs\Housekeeping\scripts\stop.bat` (or exit via the system tray icon).
  2. Copy the desired `D:\Programs\Housekeeping\backups\config_snapshots\settings_*.json` (or `data\settings.json.bak`) over `D:\Programs\Housekeeping\data\settings.json`.
  3. Run `D:\Programs\Housekeeping\scripts\start.bat`.

---

## 7. Build, Deployment, Upgrade & Rollback Runbook

### 7.1 End-to-End Build, Sign & Package Pipeline (`build_installer.ps1`)
Run [`Dev\scripts\build_installer.ps1`](file:///D:/Projects/Personal/Housekeeping/Dev/scripts/build_installer.ps1#L1-L343) from `D:\Projects\Personal\Housekeeping\Dev\`:
```powershell
powershell -ExecutionPolicy Bypass -File "D:\Projects\Personal\Housekeeping\Dev\scripts\build_installer.ps1"
```
What `build_installer.ps1` executes automatically:
1. Reads the target version from [`Dev\version.txt`](file:///D:/Projects/Personal/Housekeeping/Dev/version.txt).
2. Builds the React 19 SPA (`npm run build` in `Dev\Housekeeping.Web\`) into `Dev\Housekeeping.Server\wwwroot\`.
3. Publishes `Housekeeping.DesktopHost` and `Housekeeping.Server` into `Builds\current\app\` and publishes `Housekeeping.Launcher` (`Housekeeping.exe`) to `Builds\current\`.
4. Copies `mother.manifest.json`, `scripts\`, and `scripts\interactive\` into `Builds\current\`.
5. Stages `payload.zip` and `metadata.json` (with SHA-256 digest and zero-PII audit) and compiles the single-file installer `Installers\Housekeeping-Setup-v<Version>.exe`.
6. Signs the root launcher, app binaries, and setup `.exe` with the `Housekeeping Local Authenticode` code-signing certificate (`Set-AuthenticodeSignature`).
7. Mirrors the newest installer to `D:\Projects\Installers\` (removing superseded `Housekeeping-Setup-*.exe` copies there) and packages the 6-file release bundle in `Releases\v<Version>\` + `Releases\Latest\` + `.7z` archives.

### 7.2 Safe Production Deployment (`D:\Programs\Housekeeping\`)
> [!CAUTION]
> **Production Deployment Gate ([Dev Bible Standard 03 §2–§3](file:///D:/Projects/Personal/_DevBible/03_VERSIONING_AND_RELEASES.md)):** Never deploy Nightly/Test builds to `D:\Programs\Housekeeping\` and never stop or overwrite production without explicit user confirmation.
```powershell
# 1. Back up canonical production data\ first
D:\Programs\Housekeeping\scripts\backup-data.bat

# 2. Run the signed installer in silent upgrade mode targeting D:\Programs\Housekeeping
D:\Projects\Personal\Housekeeping\Installers\Housekeeping-Setup-v1.0.0-rc.11-hf.2.exe --silent --install-dir "D:\Programs\Housekeeping" --launch

# 3. Verify health endpoint
Invoke-RestMethod -Uri "http://127.0.0.1:5178/api/v1/health"
```

### 7.3 Rollback Procedure
If a newly deployed RC exhibits regressions:
1. Stop Housekeeping (`D:\Programs\Housekeeping\scripts\stop.bat`).
2. Run the previous known-good versioned installer from `D:\Projects\Personal\Housekeeping\Installers\Housekeeping-Setup-v<PreviousVersion>.exe --silent --install-dir "D:\Programs\Housekeeping" --launch`.
3. If `settings.json` needs to be reverted, restore the pre-upgrade snapshot from `D:\Programs\Housekeeping\backups\config_snapshots\`.

---

## 8. Sandbox Verification Recipe (`Sandbox\`)

Before promoting any build, verify the installer and runtime inside `D:\Projects\Personal\Housekeeping\Sandbox\` with 100% fail-closed isolation (zero `D:\Programs\Housekeeping` or host Registry/Start Menu mutations):

```powershell
$setup = "D:\Projects\Personal\Housekeeping\Installers\Housekeeping-Setup-v1.0.0-rc.11-hf.2.exe"
$sbRoot = "D:\Projects\Personal\Housekeeping\Sandbox"

# 1. Verify embedded payload SHA-256
& $setup --verify

# 2. Run --test-mode install into Sandbox\app
& $setup --test-mode --silent `
  --install-dir "$sbRoot\app" `
  --data-dir "$sbRoot\data" `
  --config "$sbRoot\data\settings.json" `
  --log-dir "$sbRoot\logs" `
  --no-shortcuts --no-launch

# 3. Boot headless sandbox instance on port 15178 with --test-mode and --dry-run
$proc = Start-Process -FilePath "$sbRoot\app\Housekeeping.exe" -ArgumentList @(
  "--headless", "--test-mode", "--dry-run",
  "--port", "15178",
  "--data-dir", "$sbRoot\data",
  "--config", "$sbRoot\data\settings.json",
  "--log-dir", "$sbRoot\logs"
) -PassThru

# 4. Probe sandbox health & scheduler endpoints, then shut down cleanly
Start-Sleep -Seconds 3
Invoke-RestMethod -Uri "http://127.0.0.1:15178/api/v1/health"
Invoke-RestMethod -Uri "http://127.0.0.1:15178/api/scheduler/status"
Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:15178/api/system/shutdown"
```

---

## 9. Incident Playbooks (Empirical Production & Audit Case Studies)

### Playbook 1 — Locked Media Destination File During Transcode (`PendingSwapService`)
- **Historical Context:** [Report `v1.0.0-nightly.20261101.-.pending-swap-recovery.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/reports/archive/v1.0.0-nightly.20261101.2026-11-01_0000.-.pending-swap-recovery.md). When `HandBrakeCLI` finished a multi-hour 4K encode while Plex or an SMB client held a read lock on the destination `.mkv`, `File.Move` threw an `IOException`.
- **Diagnosis:** Check the Dashboard for the **⏳ Deferred Pending Swap(s) Ready** banner or inspect `data\pending_swaps.json` (`GET /api/v1/pipeline/state` $\rightarrow$ `PendingSwapCount`).
- **Remediation:** Once playback ends, click **🔄 Commit Swaps Now** (`POST /api/v1/pipeline/pending-swaps/retry`), which calls [`PendingSwapService.SweepAndCommitAll`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/PendingSwapService.cs#L171-L196) to atomically move `<stem>.partial<ext>` over the destination file without re-encoding.

### Playbook 2 — Orphaned `rclone bisync` `.lck` File Blocking Cloud Sync
- **Historical Context:** `CHANGELOG.md` `v1.0.0-rc.7` / [`CodeAuditRc7RegressionTests.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Tests/CodeAuditRc7RegressionTests.cs). When `rclone bisync` was killed mid-transfer, its `.lck` file remained in `%LOCALAPPDATA%\rclone\bisync\`, and ANSI color sequences in `rclone`'s error output previously prevented regex matching.
- **Diagnosis:** Look for `[WARN] Detected orphaned rclone bisync lockfile` in `Housekeeping_Live.log`.
- **Remediation:** [`CloudSyncModule.TryRecoverOrphanedBisyncLock`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/Modules/CloudAndHumbleSyncModules.cs#L150-L185) automatically strips ANSI codes, verifies no active `rclone.exe` PID is running, deletes the `.lck` file, and retries `bisync`. If `rclone` reports bisync state divergence (`Must run --resync`), open the Cloud Sync job in **Job Builder** and click **⚡ Run Once with --resync**.

### Playbook 3 — Per-Module Scheduler DST Fall-Back Stall
- **Historical Context:** [Report `v1.0.0-nightly.20261101.-.per-module-scheduler-dst.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/reports/archive/v1.0.0-nightly.20261101.2026-11-01_0000.-.per-module-scheduler-dst.md). Comparing `LastScheduledRun` in local `DateTime.Now` caused a 1-hour stall during Daylight Saving Time fall-back transitions and risked clobbering `LastScheduledRun` when saving unrelated UI settings.
- **Remediation:** [`SchedulerAndNotificationService.CheckModule`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/SchedulerAndNotificationService.cs#L732-L765) normalizes all cadence elapsed-time math to UTC (`ToUniversalTime()`), and [`ConfigService.PreserveModuleScheduleUtc`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/ConfigService.cs#L764-L788) preserves server-side `LastScheduledRun` timestamps across UI config saves.

### Playbook 4 — ShadowLibrary `.hash` Sidecar Ping-Pong & Repacked Container False Positives
- **Historical Context:** [Report `v1.0.0-rc.11-hf.2.-.shadowlibrary-hashing-and-sidecar-compatibility.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/reports/archive/v1.0.0-rc.11-hf.2.2026-11-03_0606.-.shadowlibrary-hashing-and-sidecar-compatibility.md) & Plan [`v1.0.0-rc.12.2026-11-04.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/plans/v1.0.0-rc.12.2026-11-04.md).
- **Diagnosis:** When Housekeeping and ShadowLibrary both audit the same ROM directory, outer `.7z` repacks or missing `MD5:` fields in legacy sidecars can cause repeated sidecar rewrites.
- **Remediation:** Enable **Two-Step Archive Inspection (`CheckInnerArchiveAndContainer`)** and **Auto-Heal Repacked Containers (`EnableBitRotAutoHeal`)** in Module 4 / Module 8, or run **🔐 Verify & Heal .hash Now** (`FastHeaderAndHeal`), which validates inner 7-Zip CRC32 headers and writes full `SIZE + CRC32 + MD5 + SHA1` + `# Container:` sidecar headers compatible with ShadowLibrary.
