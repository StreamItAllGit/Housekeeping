# Housekeeping — Operations, Deployment & Troubleshooting Runbook

- **Document:** `OPERATIONS.md` (Technical Manual 3 of 4)
- **Active Build Version:** `v0.2.0` (`Foundation v0.5.3`)
- **Default Production Install Root:** `%LOCALAPPDATA%\Programs\StreamItAll\Mother\Housekeeping\`
- **Default Ports:** HTTP / SignalR `127.0.0.1:5178` · `Mother` Gateway Subpath `/app/housekeeping/` · Tailscale Serve HTTPS Proxy `:443` / `:8478` -> `http://127.0.0.1:5178`

---

## 1. Install Topology & Canonical Directories

Housekeeping separates its 4-folder development workspace (`Archive\`, `Builds\`, `Dev\`, `Releases\`) from its clean deployed Clean Release Root (`<InstallRoot>`):

```text
<InstallRoot>\                                # e.g. %LOCALAPPDATA%\Programs\StreamItAll\Mother\Housekeeping\
├── Housekeeping.exe                          # Root Launcher Shim (Foundation.Launcher.RootLauncherHost)
├── app\                                      # Compiled application binaries, wwwroot SPA, config templates & receipts
│   ├── Housekeeping.exe                      # Primary WinForms Tray + WebView2 Host (Housekeeping.DesktopHost)
│   ├── Housekeeping.Server.exe               # Standalone Kestrel server executable
│   ├── Housekeeping-Uninstall.exe            # Contained uninstaller binary
│   ├── Housekeeping.Core.dll                 # Core domain, services & 8 pipeline modules
│   ├── Housekeeping.Server.dll               # ASP.NET Core Minimal APIs & SignalR hub
│   ├── LICENSE                               # Verbatim MPL-2.0 license text
│   ├── suite.manifest.json                   # Mother v2 camelCase companion suite manifest (sdkVersion: 0.5.3)
│   ├── .install_manifest.json                # Installed file inventory receipt
│   ├── version.txt                           # Active deployed SemVer stamp (0.2.0)
│   ├── config\
│   │   └── settings.example.json             # Zero-PII default configuration reference template
│   ├── wwwroot\                              # Compiled React 19 + Vite 6 SPA assets (base: './')
│   └── tools\                                # Optional bundled portable CLI tools (plus optional MediaInfo.dll)
├── data\                                     # Canonical runtime state (<DataDir>, resolved by ConfigService)
│   ├── settings.json                         # Active Schema v4 configuration (+ settings.json.bak)
│   ├── settings.example.json                 # Auto-seeded zero-PII reference template (when missing)
│   ├── history.json                          # Pipeline execution history log
│   ├── compliance_reports.json               # Archive compliance & .hash findings store
│   ├── pending_swaps.json                    # Deferred .partial transcode swaps
│   ├── interrupted_run_checkpoint.json       # Mid-shutdown recovery checkpoint (preserved on graceful shutdown)
│   ├── .hk_cookie_secret.key                 # 32-byte hex HMAC-SHA256 session cookie signing key (ACL-protected)
│   ├── rclone\                               # Contained rclone state (rclone.conf, bisync\ locks, cache\)
│   ├── dats\                                 # No-Intro & Redump .dat / .xml files
│   ├── manifests\                            # Default output directory for collection_manifest.csv/.tsv/.json/.html
│   ├── tools\                                # User/auto-provisioned CLI binaries (HandBrakeCLI, 7z, rclone, hbd, MediaInfo.dll)
│   └── webview2_cache\                       # DisposableWebViewForm Chromium user data folder
├── backups\                                  # Sibling backup root (never inside data\ — prevents self-inclusion)
│   ├── Housekeeping_Backup_*.zip / *.7z      # Clean JSON state-only backups (/api/v1/system/backup/now & backup-now.bat)
│   └── config_snapshots\                     # 20 most recent SHA-256 deduplicated settings.json snapshots
├── logs\                                     # Canonical log root (<LogsDir> / ActiveLogsDir — zero secrets)
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

> **Clean Deployment Rule:** `AGENTS.md` and `mother.manifest.json` are excluded from `Builds\`, `Releases\`, installed roots, and remote SMB sync targets.

---

## 2. Executables, CLI Flags & Environment Variables

### 2.1 Runtime Executables (`Housekeeping.exe` / `app\Housekeeping.exe` / `app\Housekeeping.Server.exe`)
Parsed by [HousekeepingCliParser.cs](../Housekeeping.Server/Hosting/HousekeepingCliParser.cs), [HousekeepingCliRunner.cs](../Housekeeping.Server/Hosting/HousekeepingCliRunner.cs), [Housekeeping.Launcher/Program.cs](../Housekeeping.Launcher/Program.cs), and [Housekeeping.DesktopHost/Program.cs](../Housekeeping.DesktopHost/Program.cs):

| CLI Flag | Env Var Equivalent | Default | Behavior |
| :--- | :--- | :--- | :--- |
| `--data-dir <path>` | `HOUSEKEEPING_DATA_DIR`, `DATA_DIR`, `APP_HOME` | `<InstallRoot>\data` | Redirects `settings.json`, `.hk_cookie_secret.key`, `rclone\`, `history.json`, `compliance_reports.json`, `pending_swaps.json`, `dats\`, and `manifests\`. |
| `--config <filePath>` | `HOUSEKEEPING_CONFIG`, `CONFIG_PATH` | `<DataDir>\settings.json` | Overrides the active JSON configuration file path. |
| `--log-dir <path>` | `HOUSEKEEPING_LOG_DIR`, `LOG_DIR` | `<InstallRoot>\logs` (or `<DataDir>\logs` with `--data-dir`) | Overrides `<LogsDir>` (`ActiveLogsDir`) for `Housekeeping_Live.log`, `runs\`, `crashes\`, System Doctor exports, and heartbeats. |
| `--port <1..65535>` | `HOUSEKEEPING_PORT`, `PORT` | `5178` | Overrides Kestrel HTTP & SignalR listening port. |
| `--bind <host>` | — | `127.0.0.1` | Specifies Kestrel bind address (`127.0.0.1`, `localhost`, `0.0.0.0`, etc.). Overridden to `127.0.0.1` when `--managed-by-mother` / `--managed-by-suite` is active. |
| `--localhost-only` | — | `false` | Forces loopback-only `127.0.0.1` Kestrel binding (disables LAN/Tailnet listener). |
| `--managed-by-mother`, `--managed-by-suite` | `HOUSEKEEPING_MANAGED_BY_MOTHER` | `false` | Enforces `127.0.0.1` loopback binding, suppresses duplicate WinForms tray icon creation, and sets suite coordination mode. |
| `--suite-id <id>` | — | `mother` | Identifies the supervising suite ID passed by `Mother` (`suite.manifest.json`). |
| `--tray-only`, `--minimized`, `--silent` | — | `false` | Starts background daemon silently without opening the standalone WebView2 window or browser on startup. |
| `--doctor` | — | `false` | **One-shot diagnostic check:** Validates config, `data\`/`logs\` containment, sandbox bounds, and `HandBrakeCLI`/`7z`/`rclone`/`MediaInfo` availability; prints JSON and exits `0` (healthy) or `2` (unhealthy). |
| `--status` | — | `false` | **One-shot CLI probe:** Outputs structured JSON (`version`, `dataDir`, `configPath`, `logsDir`, `daemonOnline`, `port`) and exits synchronously (`0`). |
| `--repair-config` | — | `false` | **One-shot CLI healer:** Validates `settings.json`; if corrupt, quarantines to `data\quarantine\settings.corrupt_<timestamp>.json`, restores from `.bak`/snapshot/defaults, prints JSON, and exits (`0`). |
| `--dry-run` | `HOUSEKEEPING_DRY_RUN=1`, `DRY_RUN=1` | `false` | Sets `ForceDryRun = true` so every manual or scheduled run simulates `[DRY]` actions without mutating media targets. |
| `APP_ISOLATED_TEST=1` | `APP_ISOLATED_TEST=1` | `false` | Automated test isolation suppressing host Registry, Task Scheduler, Tailscale Serve, and OS sleep/shutdown mutations. |
| `FOUNDATION_SANDBOX_ROOT` | `FOUNDATION_SANDBOX_ROOT`, `SANDBOX_*_DRIVE` | Unset | Engages 4-drive `SandboxEnvironmentSeam` containment; host-escape attempts fail closed with **`ExitCode = 3`**. |
| `--automation-port`, `--automation-token` | — | Disabled | Test automation bridge (`Debug` or `HousekeepingTestAutomation=true` only); **fails closed with `ExitCode = 2` in Release builds**. |
| `--version` / `-v` & `--help` / `-h` | — | — | Prints application version or CLI reference and exits `0`. |

### 2.2 Operational Batch Scripts (`<InstallRoot>\scripts\`)

| Script | Path | Behavior |
| :--- | :--- | :--- |
| `start-daemon.bat` | `scripts\start-daemon.bat` | Starts `Housekeeping.exe --minimized --data-dir "<Root>\data" --log-dir "<Root>\logs"` in the background. |
| `stop-daemon.bat` | `scripts\stop-daemon.bat` | Sends `POST http://127.0.0.1:5178/api/v1/system/shutdown` (`InitiateGracefulShutdownAbort`, preserving checkpoints & `PendingRuns`). |
| `status-check.bat` | `scripts\status-check.bat` | Executes `Housekeeping.exe --status --data-dir "<Root>\data" --log-dir "<Root>\logs"`. |
| `backup-now.bat` | `scripts\backup-now.bat` | Compresses `data\settings.json` and `data\history.json` into `<Root>\backups\manual_backup_<timestamp>.7z` (`.zip` fallback). |
| `inspect-status.bat` | `scripts\interactive\inspect-status.bat` | Interactive wrapper calling `status-check.bat` followed by `pause`. |
| `repair-config.bat` | `scripts\interactive\repair-config.bat` | Interactive wrapper calling `Housekeeping.exe --repair-config --data-dir "<Root>\data"` followed by `pause`. |
| `tail-logs.bat` | `scripts\interactive\tail-logs.bat` | Streams `<Root>\logs\Housekeeping_Live.log` live (`Get-Content -Wait -Tail 40`). |

### 2.3 Installer CLI Flags (`Housekeeping-Setup-v0.2.0-win-x64.exe`)
Powered by `Foundation.Installer.StandardInstallerHost` and `installer.manifest.json`:
- `--install-dir <dir>` / `--target-dir <dir>`: Explicit installation root.
- `--suite-dir <dir>`: Nests Housekeeping under `<dir>\Housekeeping\` (default umbrella target: `%LOCALAPPDATA%\Programs\StreamItAll\Mother\Housekeeping\`).
- `--profile <lightweight|full|custom>`: Selects installation component profile (`full` default).
- `--migrate-legacy`: Opt-in migration copying legacy flat install state into the suite layout and unregistering legacy `Housekeeping_Watchdog` Scheduled Tasks safely via `IPlatformRegistry` / `SandboxEnvironmentSeam`.
- `--payload <zipPath>`: Uses an explicit local release archive (validates SHA-256 and rejects Zip-Slip traversal or corrupt ZIPs cleanly).
- `--silent` / `--quiet`, `--verify`, `--dry-run`, `--data-dir <dir>`, `--config <file>`, `--log-dir <dir>`, `--launch` / `--no-launch`, `--no-shortcuts`, `--uninstall` (`--purge-data`).
- `--automation-port` / `--automation-token`: Debug / `HousekeepingTestAutomation=true` only; fails closed with **`ExitCode = 2`** in Release `Setup.exe`.

---

## 3. Configuration, Cookie Secret & `rclone` Containment

- **Atomic Writes & Template Seeding:** [ConfigService.AtomicIoAndMigrations.cs](../Housekeeping.Core/Services/ConfigService.AtomicIoAndMigrations.cs) writes UTF-8 No-BOM via `{target}.{pid}.{guid:N}.tmp` -> `.bak` -> `File.Move(..., overwrite: true)`. [ConfigService.SandboxAndPaths.cs](../Housekeeping.Core/Services/ConfigService.SandboxAndPaths.cs) enforces `SandboxEnvironmentSeam` (`ExitCode = 3`) and copies `app\config\settings.example.json` into `<DataDir>\settings.example.json` when missing.
- **20-Snapshot History & Migrations:** `MigrateConfig` upgrades `v1..v3 -> v4` (`StandaloneWindow` default), and `CreateSnapshotIfChanged` retains up to 20 SHA-256 deduplicated snapshots in `backups\config_snapshots\`.
- **Cookie Secret Relocation (`<DataDir>\.hk_cookie_secret.key`):** `TieredAuthService` automatically migrates any legacy `logs\.hk_cookie_secret.key` into `<DataDir>\.hk_cookie_secret.key` with restrictive ACLs and deletes the `logs\` copy.
- **3-Caller `rclone` Containment (`<DataDir>\rclone\`):** `CloudSyncModule`, `RomLauncherGeneratorModule.CommandBuilder`, and `DependencyCheckService` inject `RCLONE_CONFIG=<DataDir>\rclone\rclone.conf`, `--workdir <DataDir>\rclone\bisync`, `--cache-dir <DataDir>\rclone\cache`, and `RCLONE_CACHE_DIR=<DataDir>\rclone\cache` so host `%LOCALAPPDATA%\rclone\` is never touched during isolated or sandboxed runs.
- **`MediaInfo` Resolution (`MediaInfoNativeResolver`):** Searches `app\`, `app\tools\`, `data\tools\`, `%ProgramFiles%\MediaInfo\`, and `PATH` via `NativeLibrary.SetDllImportResolver` + `ToolResolver` (returning explicit probe failures instead of assuming HEVC if missing).

---

## 4. Ports, Mutex, Locks & Sister-App Coexistence

| Resource / Mechanism | Identifier / Value | Scope & Coexistence Rules |
| :--- | :--- | :--- |
| **HTTP & SignalR Port** | `5178` (`127.0.0.1:5178`) | Enforced `127.0.0.1` loopback when `--managed-by-mother` / `--managed-by-suite` or `--localhost-only` is set; served behind `Mother` at `/app/housekeeping/`. |
| **Tailscale Serve HTTPS** | `:443` (`:8478` manifest) | Managed via `TailscalePwaService` (`tailscale serve --bg --yes http://127.0.0.1:5178`) with offline SVG QR via `GET /api/v1/system/tailscale-qr`. |
| **Single-Instance Mutex** | `Global\Mother_Housekeeping_Mutex` | Unified suite mutex; scopes to `Global\Housekeeping_3_Scoped_<12hex>` under isolated/test overrides (`APP_ISOLATED_TEST=1`, `--data-dir`, `--port`, etc.). |
| **File & Directory `.hklock`** | `<Target>.hklock` & `<Dir>\.<appId>_<pid>.hklock` | Distributed lockfile + PID/StartTime directory sentinel evicting dead/recycled PIDs and deferring to active peer locks. |
| **Coexistence & Archive Registries** | `.coexistence.json` (`v2`), `.archive_hashes.json` (`v1`) | Coordinates `Housekeeping`, `ShadowLibrary`, `Redroom`, `uStation`, `StreamEngine`, and `Greenroom`. |
| **Single-Flight Fleet SMB Sync** | `<RemoteRoot>\app\` & `<RemoteRoot>\data\` | Per-node `SemaphoreSlim` gate (`RemoteNodeDeploymentService.PushLock.cs`) with `{pid}.{guid}.tmp` atomic staging and zero `AGENTS.md` remote sync. |

---

## 5. Logs, Heartbeats, Crash Bundles & System Doctor

1. **Rolling Live Log (`<LogsDir>\Housekeeping_Live.log`):** `10 MB x 5` generations (`Housekeeping_Live.1.log`..`.5.log`), prefix-deduplicated via `TerminalLogBufferService`.
2. **Per-Run Transcripts (`<LogsDir>\runs\Run_<Queue>_<yyyyMMdd_HHmmss>.log`):** 30-day automatic retention.
3. **Heartbeat (`<LogsDir>\telemetry_heartbeat.json`):** Atomic `.tmp` swap every **10s** active / **30s** idle.
4. **Filtered Zero-PII Crash Bundles (`<LogsDir>\crashes\crash_<ts>_<hex>.json`):** Calls `e.SetObserved()`, suppresses benign `OperationCanceledException` and aborted-client `SocketException (10054)` / transient disconnect exceptions (`WARN` telemetry only), and rotates beyond 30 bundles.
5. **System Doctor & Backup:** `POST /api/v1/system/backup/now` writes clean JSON state-only backups into sibling `backups\` (`FileShare.ReadWrite`, excluding `webview2_cache`); `export-bundle` writes sanitized diagnostics inside `ActiveLogsDir`.

---

## 6. Build, Packaging & Stage 2 4-Drive Sandbox Verification

### 6.1 Build & 7-File Transition Packaging (`build_installer.ps1`)
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Dev\scripts\build_installer.ps1 -Version 0.2.0
# Add -SkipSigning if no local Authenticode certificate is present
```
1. Restores/builds `Housekeeping.Web` (`npm ci`, `npm run build` with `base: './'`) into `Dev\Housekeeping.Server\wwwroot\`.
2. Publishes `DesktopHost`, `Server`, and `RootLauncherHost` `Launcher` into `Builds\v0.2.0\` (`app\LICENSE`, `app\suite.manifest.json`, `app\config\settings.example.json`, `app\version.txt`, `scripts\` — zero `AGENTS.md`, zero PDBs, zero `Foundation.Automation.*` in Release).
3. Scans staged payloads for PII/tokens/secrets, compiles `Housekeeping.Installer` (`StandardInstallerHost` + compressed Foundation `.dll.gz` streams), and signs executables when signing is enabled.
4. Packages strictly 7 files into NTFS-hardlinked `Releases\v0.2.0\` and `Releases\Latest\` (`Housekeeping-Setup-v0.2.0-win-x64.exe`, `Housekeeping-v0.2.0-win-x64-Portable.zip`, `README.md`, `FEATURES.md`, `CHANGELOG.md`, `RELEASE_NOTES.md`, `SHA256SUMS.txt`) plus `.7z` archives (4-folder layout only).

### 6.2 Production Deployment, Rollback & Stage 2 Sandbox E2E
> **Production Deployment Safety:** Never deploy `-nightly` or `-test` builds to live production directories, and never stop or overwrite a live production instance without explicit operator approval.

```powershell
# 1. Back up live data\ first
& "<InstallRoot>\scripts\backup-now.bat"

# 2. Silent install/upgrade (add --migrate-legacy only when migrating a legacy flat install)
.\Housekeeping-Setup-v0.2.0-win-x64.exe --silent --suite-dir "%LOCALAPPDATA%\Programs\StreamItAll\Mother" --profile full --launch

# 3. Verify health & CLI doctor
.\Housekeeping.exe --doctor --data-dir "<InstallRoot>\data" --log-dir "<InstallRoot>\logs"
Invoke-RestMethod -Uri "http://127.0.0.1:5178/api/v1/health"
```
To roll back: run `<InstallRoot>\scripts\stop-daemon.bat`, install the prior `Housekeeping-Setup-v<Previous>-win-x64.exe`, and restore a snapshot from `<InstallRoot>\backups\config_snapshots\` if needed.

For isolated Two-Build Stage 2 4-Drive Sandbox verification (`TestBuild` with `-p:HousekeepingTestAutomation=true` + `ReleaseBuild` `ExitCode = 2` check):
```powershell
$env:APP_ISOLATED_TEST = "1"
dotnet test Dev\Housekeeping.slnx -c Release -p:TreatWarningsAsErrors=true -nodeReuse:false
python Dev\scripts\run_stage2_e2e.py
```

---

## 7. Incident Playbooks

1. **Locked `.partial` or Destination File (`PendingSwapService`):** `NetworkSafetyService` pre-probes both `.partial` and destination paths with `FileShare.None` without pre-deleting existing media files. Click **Commit Swaps Now** on the Dashboard banner once playback or AV scanning completes.
2. **Orphaned `rclone bisync` `.lck` File:** Under `v0.2.0`, `rclone` runs strictly inside `<DataDir>\rclone\`. `CloudSyncModule` strips ANSI codes, verifies no active `rclone.exe` PID holds the lock, removes stale `.lck` files, and retries automatically; if state diverged, run once with `--resync`.
3. **Aborted-Client `SocketException (10054)` or Concurrent SMB Sync Collision:** `CrashBundleExceptionFilter` downgrades client-reset unobserved tasks to `WARN` telemetry without crashing or writing crash JSONs, and `RemoteNodeDeploymentService.PushLock.cs` serializes remote `settings.json` pushes per node using `SemaphoreSlim` + `{pid}.{guid}.tmp`.
4. **Missing `MediaInfo.dll` During Quality Tier Scan:** Run `Housekeeping.exe --doctor` or check `Settings -> System Doctor`. `MediaInfoNativeResolver` reports explicit unavailability rather than falsely classifying files as HEVC; place `MediaInfo.dll` (x64) inside `app\`, `app\tools\`, `data\tools\`, or system `MediaInfo` installation directory.
5. **Preserving Active Queue Progress Across Upgrades:** `POST /api/v1/system/shutdown` and tray exit invoke `InitiateGracefulShutdownAbort()`, resuming suspended child processes and preserving `data\interrupted_run_checkpoint.json` + `PendingRuns` for seamless resume on relaunch.
