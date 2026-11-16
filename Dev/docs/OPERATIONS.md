# Housekeeping — Operations, Deployment & Troubleshooting Runbook

- **Document:** `OPERATIONS.md` (Technical Manual 3 of 4)
- **Active Build Version:** `v0.1.12-hf.1` (In-Dev Target: `v0.1.13`)
- **Canonical Production Root:** `D:\Programs\Housekeeping\`
- **Development Root:** `D:\Projects\Personal\Housekeeping\Dev\`
- **Default Ports:** HTTP / SignalR `:5178` · Tailscale Serve HTTPS Proxy `:443` / `:8478` $\rightarrow$ `http://127.0.0.1:5178`

---

## 1. Install Topology & Canonical Directories

Per Dev Bible [Standard 01](file:///D:/Projects/Personal/_DevBible/01_PROJECT_LAYOUTS.md) and [Standard 12 §3.1](file:///D:/Projects/Personal/_DevBible/12_WORKSPACE_AND_DEPLOYMENT_CLEANLINESS.md), Housekeeping separates its outer 5-folder workspace (`D:\Projects\Personal\Housekeeping\`) from its clean deployed runtime layout (`D:\Programs\Housekeeping\` or `Builds\v0.1.12-hf.1\`):

```text
D:\Programs\Housekeeping\                     # Canonical Production Root (<InstallRoot>)
├── Housekeeping.exe                          # Root Launcher Shim (Housekeeping.Launcher)
├── AGENTS.md                                 # Compact zero-PII portable context anchor (<= 40 lines / < 2 KB)
├── mother.manifest.json                      # Mother v1.0 Fleet supervision manifest (SchemaVersion 2)
├── app\                                      # Compiled application binaries, wwwroot SPA & installer receipts
│   ├── Housekeeping.exe                      # Primary WinForms Tray + WebView2 Host (Housekeeping.DesktopHost)
│   ├── Housekeeping.Server.exe               # Standalone Kestrel server executable
│   ├── Housekeeping-Uninstall.exe            # Contained uninstaller binary
│   ├── Housekeeping.Core.dll                 # Core domain, services & 8 pipeline modules
│   ├── Housekeeping.Server.dll               # ASP.NET Core Minimal APIs & SignalR hub
│   ├── suite.manifest.json                   # Standard 19 §3 companion suite manifest (v2)
│   ├── .install_manifest.json                # Installed file inventory receipt
│   ├── version.txt                           # Active deployed SemVer stamp (0.1.12-hf.1)
│   ├── wwwroot\                              # Compiled React 19 + Vite 6 SPA assets
│   └── tools\                                # Optional bundled portable CLI tools
├── data\                                     # Canonical runtime state (<DataDir>, resolved by ConfigService)
│   ├── settings.json                         # Active Schema v4 configuration (+ settings.json.bak)
│   ├── history.json                          # Pipeline execution history log
│   ├── compliance_reports.json               # Archive compliance & .hash findings store
│   ├── pending_swaps.json                    # Deferred .partial transcode swaps
│   ├── interrupted_run_checkpoint.json       # Mid-shutdown recovery checkpoint (preserved on graceful shutdown)
│   ├── .hk_cookie_secret.key                 # 32-byte hex HMAC-SHA256 session cookie signing key
│   ├── dats\                                 # No-Intro & Redump .dat / .xml files
│   ├── manifests\                            # Default output directory for collection_manifest.csv/.tsv/.json/.html
│   ├── tools\                                # User/auto-provisioned CLI binaries (HandBrakeCLI, 7z, rclone, hbd)
│   └── webview2_cache\                       # DisposableWebViewForm Chromium user data folder
├── backups\                                  # Sibling backup root (never inside data\ — prevents self-inclusion)
│   ├── Housekeeping_Backup_*.zip / *.7z      # Clean JSON state-only backups (/api/v1/system/backup/now & backup-now.bat)
│   └── config_snapshots\                     # 20 most recent SHA-256 deduplicated settings.json snapshots
├── logs\                                     # Canonical log root (<LogsDir> / ActiveLogsDir)
│   ├── Housekeeping_Live.log                 # Rolling live log (10 MB x 5 generations: .1.log .. .5.log)
│   ├── telemetry_heartbeat.json              # 10s (active) / 30s (idle) JSON health heartbeat
│   ├── Housekeeping_Doctor_*.json            # Sanitized System Doctor diagnostic bundles (contained in ActiveLogsDir)
│   ├── runs\                                 # Per-run transcripts (Run_<Queue>_<yyyyMMdd_HHmmss>.log, 30-day retention)
│   └── crashes\                              # Filtered zero-PII JSON crash bundles (crash_<yyyyMMdd_HHmmss>_<id>.json, max 30)
└── scripts\                                  # Non-interactive & interactive operational batch utilities
    ├── start-daemon.bat
    ├── stop-daemon.bat
    ├── status-check.bat
    ├── backup-now.bat
    └── interactive\
        ├── inspect-status.bat
        ├── repair-config.bat
        └── tail-logs.bat
```

---

## 2. Executables, CLI Flags & Environment Variables

### 2.1 Runtime Executables (`Housekeeping.exe` / `app\Housekeeping.exe` / `app\Housekeeping.Server.exe`)
Parsed by [`HousekeepingCliParser.Parse`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Hosting/HousekeepingCliParser.cs#L1-L284), [`HousekeepingCliRunner`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Hosting/HousekeepingCliRunner.cs#L1-L137), and [`DesktopHost\Program.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.DesktopHost/Program.cs#L1-L232):

| CLI Flag | Env Var Equivalent | Default | Behavior |
| :--- | :--- | :--- | :--- |
| `--data-dir <path>` (or `--data-dir=<path>`) | `HOUSEKEEPING_DATA_DIR`, `DATA_DIR`, `APP_HOME` | `<InstallRoot>\data` | Overrides `<DataDir>` for `settings.json`, `history.json`, `compliance_reports.json`, `pending_swaps.json`, `dats\`, and `manifests\`. Marks invocation as isolated. |
| `--config <filePath>` (or `--config=<filePath>`) | `HOUSEKEEPING_CONFIG_FILE`, `CONFIG_PATH` | `<DataDir>\settings.json` | Overrides the active JSON configuration file path. |
| `--log-dir <path>` (or `--log-dir=<path>`) | `HOUSEKEEPING_LOG_DIR`, `LOG_DIR` | `<InstallRoot>\logs` (or `<DataDir>\logs` when custom `--data-dir` is passed) | Overrides `<LogsDir>` (`ActiveLogsDir`) for `Housekeeping_Live.log`, `runs\`, `crashes\`, System Doctor exports, and `telemetry_heartbeat.json`. |
| `--port <1..65535>` (or `--port=<n>`) | `HOUSEKEEPING_PORT`, `PORT` | `5178` | Overrides `WebServer.Port` for Kestrel HTTP & SignalR binding. |
| `--localhost-only` | — | `false` | Forces loopback-only `127.0.0.1` Kestrel binding (disables LAN/Tailnet listener). |
| `--tray-only` | — | `false` (`TrayDaemonApplicationContext`) | Starts background daemon in System Tray without popping open the desktop window (`UiPresentationMode.TrayOrBackground`). |
| `--status` | — | `false` | **One-shot CLI probe:** Outputs structured JSON (`version`, `dataDir`, `configPath`, `logsDir`, `configValid`, `daemonReachable`, `port`) and exits synchronously (`0`). |
| `--repair-config` | — | `false` | **One-shot CLI healer:** Validates `settings.json`; if corrupt, quarantines it to `settings.json.corrupt_<timestamp>.bak`, restores from `.bak`/snapshot/defaults, prints JSON summary, and exits (`0`). |
| `APP_ISOLATED_TEST=1` | `APP_ISOLATED_TEST=1` | `false` | Automated test isolation mode (`ConfigService.IsIsolatedMode = true`), disabling post-run OS sleep/shutdown and scoping single-instance mutex to `Global\Housekeeping_3_Scoped_<hash>`. |
| `--dry-run` | `HOUSEKEEPING_DRY_RUN=1`, `DRY_RUN=1` | `false` | Sets `PipelineOrchestrator.ForceDryRun = true` so every manual or scheduled run logs `[DRY]` actions without modifying disk. |
| `--minimized` / `--tray` / `--silent` | — | `false` | Starts the tray daemon silently without popping open the standalone WebView2 window or browser on startup. |
| `--managed-by-mother` | — | `false` | Suppresses the standalone system tray icon when supervised by Mother v1.0. |
| `--version` / `-v` | — | — | Prints `Housekeeping v<Version>` and exits with code `0`. |
| `--help` / `-h` / `/?` | — | — | Prints CLI usage reference and exits with code `0`. |

> [!TIP]
> **One-Shot Operational Batch Scripts (`v0.1.12+`):** [`scripts\status-check.bat`](file:///D:/Projects/Personal/Housekeeping/Dev/scripts/status-check.bat), [`scripts\interactive\inspect-status.bat`](file:///D:/Projects/Personal/Housekeeping/Dev/scripts/interactive/inspect-status.bat), and [`scripts\interactive\repair-config.bat`](file:///D:/Projects/Personal/Housekeeping/Dev/scripts/interactive/repair-config.bat) invoke `Housekeeping.exe` with `--status` or `--repair-config`. [`Housekeeping.Launcher\Program.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Launcher/Program.cs) (`ShouldWaitForChild`) and [`HousekeepingCliRunner`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Hosting/HousekeepingCliRunner.cs) execute these flags synchronously and exit immediately without starting a background daemon.

### 2.2 Operational Batch Scripts (`<InstallRoot>\scripts\`)

| Script | Path | Behavior |
| :--- | :--- | :--- |
| `start-daemon.bat` | `scripts\start-daemon.bat` | Starts `Housekeeping.exe --minimized --data-dir "<Root>\data" --log-dir "<Root>\logs"` in the background. |
| `stop-daemon.bat` | `scripts\stop-daemon.bat` | Sends `POST http://127.0.0.1:5178/api/v1/system/shutdown` (`InitiateGracefulShutdownAbort`, preserving active checkpoints and `PendingRuns`) and exits `0`. |
| `status-check.bat` | `scripts\status-check.bat` | Executes `Housekeeping.exe --status --data-dir "<Root>\data" --log-dir "<Root>\logs"` and returns exit code. |
| `backup-now.bat` | `scripts\backup-now.bat` | Compresses `data\settings.json` and `data\history.json` into `<Root>\backups\manual_backup_<timestamp>.7z` (`-mx=9`, or `.zip` fallback). |
| `inspect-status.bat` | `scripts\interactive\inspect-status.bat` | Interactive wrapper calling `status-check.bat` followed by `pause`. |
| `repair-config.bat` | `scripts\interactive\repair-config.bat` | Interactive wrapper calling `Housekeeping.exe --repair-config --data-dir "<Root>\data"` followed by `pause`. |
| `tail-logs.bat` | `scripts\interactive\tail-logs.bat` | Streams `<Root>\logs\Housekeeping_Live.log` live (`Get-Content -Wait -Tail 40`). |

### 2.3 Installer CLI Flags (`Housekeeping-Setup-v0.1.12-hf.1-win-x64.exe`)
Parsed by [`Housekeeping.Installer\Program.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Installer/Program.cs) and [`Program.DiagnosticsAndCleanup.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Installer/Program.DiagnosticsAndCleanup.cs):
- `--install-dir <dir>` / `--target-dir <dir>`: Target installation root (default `%LOCALAPPDATA%\Programs\Housekeeping`, or `HOUSEKEEPING_INSTALL_DIR` / `INSTALL_DIR`).
- `--silent` / `--quiet`: Non-interactive console execution (skips WinForms wizard).
- `--verify`: Verifies embedded `payload.zip` SHA-256 hash against `metadata.json` and exits (`0` = valid).
- `--dry-run`: Verifies payload and logs planned install actions without mutating disk.
- `--data-dir <dir>`, `--config <file>`, `--log-dir <dir>`: Isolated state/log overrides (suppresses host Registry & Start Menu `.lnk` mutations when explicit `--data-dir` is supplied).
- `--launch` / `-l` and `--no-launch`: Explicitly launch or suppress launching `Housekeeping.exe` after setup.
- `--no-shortcuts`: Skips Start Menu and root shortcut creation.
- `--uninstall` (and optional `--purge-data`): Removes `app\` binaries and uninstall registry entry (preserving `data\`, `logs\`, and `backups\` unless `--purge-data` is passed).

---

## 3. Configuration & Settings File Reference (`data\settings.json`)

[`ConfigService`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/ConfigService.cs) and [`ConfigService.AtomicIoAndMigrations.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/ConfigService.AtomicIoAndMigrations.cs) manage `<DataDir>\settings.json` at **`ConfigVersion = 4` (`CurrentSchemaVersion = 4`)**:

- **Unique Per-Caller Atomic `.tmp` Writes (`AtomicWriteAllText`):**
  - Writes UTF-8 No-BOM to `{target}.{pid}.{guid:N}.tmp`, flushes, backs up `{target}.bak`, and commits via `File.Move(..., overwrite: true)` with bounded SMB retry and `finally` temp cleanup—eliminating sharing violations under 16-way concurrent local or SMB saves (`BUG-04`).
- **Forward-Compatible Additive Properties (`[JsonExtensionData]`):**
  - `AppConfig` preserves unknown future JSON keys across round-trips and ships additive `v0.1.12` defaults (`UiLayout`, `AutoAlignWithSisterApps`, `SkipShadowLibraryManagedRoots`, `SyncPostInjectionContainerHashJson`).
- **Automatic Schema Migration (`MigrateConfig`):**
  - `v1 -> v2`: Seeds `DefinedJobs` and `NamedQueues` from legacy single-module configs.
  - `v2 -> v3`: Initializes built-in `ArchiveComplianceTemplate` definitions (`auto-community`, `redump-optical`, `nointro-cartridge`, `community-modern-disc`, `official-folder-dump`).
  - `v3 -> v4`: Migrates `WebServer.DefaultDesktopLaunchMode` from `"Browser"` to `"StandaloneWindow"` so tray double-click opens `DisposableWebViewForm`.
- **Legacy Root-to-`data\` Auto-Migration (`ConfigService.LegacyRootMigration.cs`):**
  - On startup (and during installer upgrade), any loose legacy `settings.json`, `history.json`, `compliance_reports.json`, `pending_swaps.json`, `dats\`, `manifests\`, or `tools\` in `<InstallRoot>\` are atomically moved/merged into `<InstallRoot>\data\`.
- **20-Snapshot Rollback History (`CreateSnapshotIfChanged`):**
  - Every `TryUpdateAndSave` hashes the serialized JSON with SHA-256; if content changed, a timestamped snapshot (`settings_yyyyMMdd_HHmmss_fff.json`) is written to `<InstallRoot>\backups\config_snapshots\` and older files beyond **20** are pruned.

---

## 4. Ports, Single-Instance Mutex, Locks & Sister-App Coexistence

| Resource / Mechanism | Identifier / Value | Scope & Coexistence Rules |
| :--- | :--- | :--- |
| **Production HTTP & SignalR Port** | `5178` (`http://127.0.0.1:5178` or `0.0.0.0:5178`) | Configured in `WebServer.Port`, `mother.manifest.json`, and `app\suite.manifest.json`. Falls back to ephemeral `127.0.0.1:0` during non-production/test port collisions. |
| **Tailscale Serve HTTPS Port** | `443` (or `8478` in `suite.manifest.json`) | Managed via [`TailscalePwaService`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Services/TailscalePwaService.cs) (`tailscale serve --bg --https=443 http://127.0.0.1:5178`). |
| **Single-Instance Mutex** | `Global\Housekeeping_2_SingleInstance_Mutex` | Switches to `Global\Housekeeping_3_Scoped_<12hex>` whenever any isolation override (`APP_ISOLATED_TEST=1`, `--dry-run`, `--port`, `--data-dir`, `--config`, `--log-dir`) is present. |
| **Per-File Distributed Lockfile** | `<finalOutputPath>.hklock` | JSON lockfile written by [`NetworkSafetyService.DistributedLocks.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/NetworkSafetyService.DistributedLocks.cs) with a 60s heartbeat timer and 90-minute stale reclamation timeout. |
| **Standard 19 §4 Directory Sentinel** | `<TargetDir>\.<appId>_<pid>.hklock` | JSON `schemaVersion: 1` sentinel written/inspected by [`NetworkSafetyService.DirectoryLocks.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/NetworkSafetyService.DirectoryLocks.cs) carrying `pid` + `processStartTimeUtc`. Automatically evicts dead or recycled-PID locks while deferring when `ShadowLibrary`, `Redroom`, `uStation`, or `StreamEngine` holds a live lock. |
| **Shared-Root Coexistence Manifest** | `<SharedRoot>\.coexistence.json` | Standard 19 §3 `schemaVersion: 2` manifest managed by [`SisterAppCoexistenceService`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/SisterAppCoexistenceService.cs) (`GET /api/v1/coexistence/status`, `POST /api/v1/coexistence/apply-recommendations`) coordinating ownership and `.hash` placement across all 5 peer apps. |
| **Post-Injection Archive Registry** | `<TargetDir>\.archive_hashes.json` | `schemaVersion: 1` JSON registry written by [`HashSidecarService.ArchiveRegistry.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/HashSidecarService.ArchiveRegistry.cs) storing outer archive size, `lastWriteUtc`, `CRC32`, `MD5`, and `SHA1` after sidecar injection (`PackageInsideArchive` / `SidecarAndArchive`) or `7z d` cleanup. |
| **Clean-Release-Root Fleet SMB Sync** | `<RemoteRoot>\app\` & `<RemoteRoot>\data\` | [`RemoteNodeDeploymentService.PathLayout.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/RemoteNodeDeploymentService.PathLayout.cs) resolves Clean-Release-Root remote node paths (with legacy flat fallback) while excluding `data\`, `backups\`, `webview2_cache`, `pending_swaps.json`, `compliance_reports.json`, and `.hk_cookie_secret.key`. |
| **Mother v1.0 & Greenroom (`:8420`)** | `mother.manifest.json` & `:8420/api/sync` | Mother polls `/api/v1/health` + `/api/scheduler/status` and defers I/O via `/api/scheduler/defer`. `GreenroomSyncIntegrationService` posts changed manifest fingerprints to `http://127.0.0.1:8420/api/sync` (or `run_greenroom.bat`). |

---

## 5. Logs, Heartbeats, Crash Bundles & System Doctor

1. **Rolling Live Log (`<LogsDir>\Housekeeping_Live.log`):**
   - Managed by [`TerminalLogBufferService`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/TerminalLogBufferService.cs) (`10 MB × 5` generations: `Housekeeping_Live.1.log` .. `.5.log`), automatically deduplicating repeated `[WARN] [WARN]` / HandBrake `[HH:mm:ss]` prefixes while preserving `[Severity] [Component]` grammar.
2. **Per-Run Transcripts (`<LogsDir>\runs\Run_<Queue>_<yyyyMMdd_HHmmss>.log`):**
   - Attached at the start of every queue/job execution and pruned automatically after **30 days** (`MaxRunLogRetentionDays = 30`).
3. **10s / 30s Telemetry Heartbeat (`<LogsDir>\telemetry_heartbeat.json`):**
   - Written atomically via `.tmp` swap by [`SystemPerformanceTelemetryService`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/SystemPerformanceTelemetryService.cs) every **10 seconds** during active pipeline execution and every **30 seconds** while idle.
4. **Filtered Zero-PII Crash Bundles (`<LogsDir>\crashes\crash_<yyyyMMdd_HHmmss>_<8hex>.json`):**
   - Managed by [`CrashBundleService`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/CrashBundleService.cs) and [`CrashBundleExceptionFilter`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/CrashBundleExceptionFilter.cs). Calls `e.SetObserved()` on `TaskScheduler.UnobservedTaskException`, suppresses false-positive dumps from benign `OperationCanceledException` and idle pooled `SocketException (10054)` resets (`FleetHttpClient` `PooledConnectionIdleTimeout = 25s`), and retains the 30 newest sanitized bundles.
5. **🩺 Contained System Doctor (`Settings` $\rightarrow$ `System Doctor` & [`HousekeepingApiEndpoints.SystemDoctorAndBackup.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.SystemDoctorAndBackup.cs)):**
   - **`POST /api/v1/system/backup/now` (`/api/v1/doctor/backup`):** Writes a clean JSON state-only backup archive (`settings.json`, `history.json`, `compliance_reports.json`, `pending_swaps.json`, `interrupted_run_checkpoint.json` opened with `FileShare.ReadWrite`) strictly into sibling `<InstallRoot>\backups\` (`Path.GetDirectoryName(SnapshotsDir)`), preventing `ZipFile` self-inclusion and locked `webview2_cache` `IOException` crashes (`BUG-05`).
   - **`POST` / `GET /api/v1/system/doctor/export-bundle` (`/api/v1/doctor/diagnostic-bundle`):** Writes sanitized diagnostic JSON bundles strictly inside `ActiveLogsDir` (`--log-dir`), never leaking into `AppContext.BaseDirectory\logs`.

---

## 6. Backup, Restore & Migration Procedures

### 6.1 Backing Up Canonical Production State
Always back up `D:\Programs\Housekeeping\data\` before manual migrations or major upgrades:
```powershell
# Option A: Use the built-in batch script (creates manual_backup_<timestamp>.7z in D:\Programs\Housekeeping\backups\)
D:\Programs\Housekeeping\scripts\backup-now.bat

# Option B: Trigger clean state-only archive via REST API while Housekeeping is running
Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:5178/api/v1/system/backup/now"
```

### 6.2 Restoring or Repairing Configuration
- **From Web / Desktop UI:** Click **↺ History (`N`)** in the Top Command Bar, choose a snapshot from the table, and click **Restore**.
- **One-Shot Automatic CLI Repair:** Run `D:\Programs\Housekeeping\scripts\interactive\repair-config.bat` (or `D:\Programs\Housekeeping\Housekeeping.exe --repair-config --data-dir "D:\Programs\Housekeeping\data"`).
- **Manual Restore From Disk While Stopped:**
  1. Run `D:\Programs\Housekeeping\scripts\stop-daemon.bat` (or exit via the system tray icon).
  2. Copy the desired `D:\Programs\Housekeeping\backups\config_snapshots\settings_*.json` (or `data\settings.json.bak`) over `D:\Programs\Housekeeping\data\settings.json`.
  3. Run `D:\Programs\Housekeeping\scripts\start-daemon.bat`.

---

## 7. Build, Deployment, Upgrade & Rollback Runbook

### 7.1 End-to-End Build, Sign & Package Pipeline (`build_installer.ps1`)
Run [`Dev\scripts\build_installer.ps1`](file:///D:/Projects/Personal/Housekeeping/Dev/scripts/build_installer.ps1) from `D:\Projects\Personal\Housekeeping\`:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "D:\Projects\Personal\Housekeeping\Dev\scripts\build_installer.ps1" -Version "0.1.12-hf.1"
```
What `build_installer.ps1` executes automatically:
1. Auto-restores `node_modules` if absent and builds the React 19 SPA (`npm --prefix Housekeeping.Web run build`) into `Dev\Housekeeping.Server\wwwroot\`.
2. Publishes `Housekeeping.DesktopHost`, `Housekeeping.Server`, and `Housekeeping.Launcher` into `Builds\v0.1.12-hf.1\`, stamping portable zero-PII `Builds\v0.1.12-hf.1\AGENTS.md` (`<= 40 lines / < 2 KB`), `mother.manifest.json`, `app\suite.manifest.json`, `scripts\`, and `scripts\interactive\`.
3. Copies all `Housekeeping.Installer\*.cs` partial files into `%TEMP%` staging, builds `payload.zip` + `metadata.json` (with SHA-256 digest and zero-PII audit), and compiles `Installers\Housekeeping-Setup-v0.1.12-hf.1-win-x64.exe`.
4. Signs the root launcher, app binaries, and setup `.exe` with Authenticode (`Set-AuthenticodeSignature`).
5. Mirrors the newest installer to `D:\Projects\Installers\` and packages the 6-file release bundle in `Releases\v0.1.12-hf.1\`, `Releases\Latest\`, `Releases\v0.1.12-hf.1.7z`, and `Releases\Latest.7z` (generating sanitized `README.md` from `USER_GUIDE.md` and dynamic `RELEASE_NOTES.md` from `CHANGELOG.md`).

### 7.2 Safe Production Deployment (`D:\Programs\Housekeeping\`)
> [!CAUTION]
> **Production Deployment Gate ([Dev Bible Standard 03 §2–§3](file:///D:/Projects/Personal/_DevBible/03_VERSIONING_AND_RELEASES.md)):** Never deploy Nightly/Test builds to `D:\Programs\Housekeeping\` and never stop or overwrite production without explicit user confirmation.
```powershell
# 1. Back up canonical production data\ first
D:\Programs\Housekeeping\scripts\backup-now.bat

# 2. Run the signed installer in silent upgrade mode targeting D:\Programs\Housekeeping
D:\Projects\Personal\Housekeeping\Installers\Housekeeping-Setup-v0.1.12-hf.1-win-x64.exe --silent --install-dir "D:\Programs\Housekeeping" --launch

# 3. Verify health & coexistence endpoints
Invoke-RestMethod -Uri "http://127.0.0.1:5178/api/v1/health"
Invoke-RestMethod -Uri "http://127.0.0.1:5178/api/v1/coexistence/status"
```

### 7.3 Rollback Procedure
If a newly deployed build exhibits regressions:
1. Stop Housekeeping (`D:\Programs\Housekeeping\scripts\stop-daemon.bat`).
2. Run the previous known-good versioned installer (`D:\Projects\Personal\Housekeeping\Installers\Housekeeping-Setup-v<PreviousVersion>-win-x64.exe --silent --install-dir "D:\Programs\Housekeeping" --launch`).
3. If `settings.json` needs to be reverted, restore the pre-upgrade snapshot from `D:\Programs\Housekeeping\backups\config_snapshots\`.

---

## 8. Isolated Verification & Unit Test Recipe (`APP_ISOLATED_TEST=1`)

Before promoting any build, verify the xUnit suite (`183/183` passing under `APP_ISOLATED_TEST=1`), installer integrity (`--verify`), and one-shot CLI flags cleanly in `%TEMP%` without touching production:

```powershell
$setup = "D:\Projects\Personal\Housekeeping\Installers\Housekeeping-Setup-v0.1.12-hf.1-win-x64.exe"
$isoRoot = Join-Path $env:TEMP "hk_iso_check"

# 1. Verify embedded payload SHA-256
& $setup --verify

# 2. Run dry-run install verification in isolated TEMP root
& $setup --silent --dry-run `
  --install-dir "$isoRoot\app" `
  --data-dir "$isoRoot\data" `
  --config "$isoRoot\data\settings.json" `
  --log-dir "$isoRoot\logs" `
  --no-shortcuts --no-launch

# 3. Verify synchronous one-shot CLI flags (--status & --repair-config)
& "D:\Projects\Personal\Housekeeping\Builds\v0.1.12-hf.1\Housekeeping.exe" --status --data-dir "$isoRoot\data" --log-dir "$isoRoot\logs"
& "D:\Projects\Personal\Housekeeping\Builds\v0.1.12-hf.1\Housekeeping.exe" --repair-config --data-dir "$isoRoot\data"
```

---

## 9. Incident Playbooks (Empirical Production & Audit Case Studies)

### Playbook 1 — Locked `.partial` or Media Destination File During Transcode (`BUG-01` & `PendingSwapService`)
- **Historical Context:** [Report `v0.1.11.2026-11-11_1952`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/reports/archive/v0.1.11.2026-11-11_1952.-.full-audit-optimizations-and-undetected-errors.md) (`BUG-01`). Live logs caught SMB/Defender opportunistic locks holding `.partial.mkv` immediately after `HandBrakeCLI` exited, while pre-move `File.Delete(finalOutputPath)` risked deleting existing media targets.
- **Diagnosis:** Check the Dashboard for **⏳ Deferred Pending Swap(s) Ready** or inspect `Housekeeping_Live.log` for exact lock attribution (`workingOutputPath` `.partial` locked vs. `finalOutputPath` destination locked).
- **Remediation:** [`NetworkSafetyService.CommitPartialEncode`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/NetworkSafetyService.cs) and [`PendingSwapService.RetrySwap`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/PendingSwapService.cs) now probe both paths with `TryProbeExclusiveAccess` (`FileShare.None`) **without** pre-deleting `finalOutputPath`, then execute atomic `File.Move(working, final, overwrite: true)`. Click **🔄 Commit Swaps Now** (`POST /api/v1/pipeline/pending-swaps/retry`) once playback or scanning finishes.

### Playbook 2 — Orphaned `rclone bisync` `.lck` File Blocking Cloud Sync
- **Historical Context:** `CHANGELOG.md` `v1.0.0-rc.7` / [`CodeAuditRc7RegressionTests.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Tests/CodeAuditRc7RegressionTests.cs).
- **Diagnosis:** Look for `[WARN] Detected orphaned rclone bisync lockfile` in `Housekeeping_Live.log`.
- **Remediation:** [`CloudSyncModule`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/Modules/CloudSyncModule.cs) strips ANSI codes, verifies no active `rclone.exe` PID is running, deletes the `.lck` file, and retries `bisync`. If `rclone` reports state divergence (`Must run --resync`), open the Cloud Sync job in **Job Builder** and click **⚡ Run Once with --resync**.

### Playbook 3 — Per-Module Scheduler DST Fall-Back Stall
- **Historical Context:** [Report `v1.0.0-nightly.20261101.-.per-module-scheduler-dst.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/reports/archive/v1.0.0-nightly.20261101.2026-11-01_0000.-.per-module-scheduler-dst.md).
- **Remediation:** [`SchedulerAndNotificationService`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/SchedulerAndNotificationService.cs) normalizes all cadence elapsed-time math to UTC (`ToUniversalTime()`), and [`ConfigService`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/ConfigService.cs) preserves server-side `LastScheduledRun` timestamps across UI config saves.

### Playbook 4 — ShadowLibrary `.hash` Sidecar Ping-Pong & Post-Injection Container Drift
- **Historical Context:** [Report `v1.0.0-rc.11-hf.2.2026-11-03_0606`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/reports/archive/v1.0.0-rc.11-hf.2.2026-11-03_0606.-.shadowlibrary-hashing-and-sidecar-compatibility.md) & Plan [`v0.1.12.2026-11-11.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/plans/v0.1.12.2026-11-11.md).
- **Remediation:** Module 4 is now 100% decoupled from `.hash` manipulation (`T13`), while Module 8 (`HashSidecarService.ArchiveRegistry.cs`) supports 4-mode `HashSidecarMode` (`0..3`), preserves `# Container:` + `CRC32+MD5+SHA1` digests without unnecessary rewrites, tracks post-injection outer archive digests in `<Directory>\.archive_hashes.json`, and offers 1-click ShadowLibrary delegation or placement alignment via `SisterAppCoexistenceService`.

### Playbook 5 — Preserving Active Queue Progress Across Upgrades & Daemon Restarts (`BUG-02`)
- **Historical Context:** [Report `v0.1.11.2026-11-11_1952`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/reports/archive/v0.1.11.2026-11-11_1952.-.full-audit-optimizations-and-undetected-errors.md) (`BUG-02`). Previously, `POST /api/v1/system/shutdown` invoked `StopActiveRun()`, deleting `interrupted_run_checkpoint.json` and clearing `PendingRuns`.
- **Remediation:** `POST /api/v1/system/shutdown` (`stop-daemon.bat` & installer upgrade) and `TrayDaemonApplicationContext.ShutdownAndExit()` now call [`PipelineOrchestratorService.InitiateGracefulShutdownAbort()`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/PipelineOrchestratorService.CancellationAndShutdown.cs), which resumes any suspended child tree, aborts active subprocesses cleanly, and **preserves** `data\interrupted_run_checkpoint.json` and `PendingRuns` so work resumes seamlessly after restart.
