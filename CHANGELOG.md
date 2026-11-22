# Changelog

All notable changes to the **Housekeeping** project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]


### Changed
- **Workspace Hygiene (`/clean` -- 2026-11-20)**: purged 17.00 MB of disposable caches.
---

## [0.2.0] - 2026-11-20

### Added
- **Foundation v0.5.3 Suite Installer, Root Launcher & 7-File Transition Packaging (`Dev/Housekeeping.Installer/`, `Dev/Housekeeping.Launcher/`, `Dev/scripts/build_installer.ps1`):**
  - Integrated pinned `Foundation v0.5.3` assemblies (`Foundation.Core`, `Foundation.Installer`, `Foundation.Launcher`, `Foundation.Automation` Debug/TestAutomation-only) across `Dev/Housekeeping.slnx`.
  - Replaced custom setup wizards with `StandardInstallerHost` + declarative `installer.manifest.json` (`schemaVersion: 2`, default `%LOCALAPPDATA%\Programs\StreamItAll\Mother\Housekeeping\`, `--suite-dir`, `--profile lightweight|full|custom`, opt-in `--migrate-legacy` cleaning up legacy `Housekeeping_Watchdog` Scheduled Tasks, and compressed `.dll.gz` embedded assemblies).
  - Migrated `Housekeeping.Launcher` (`Housekeeping.exe`) to `Foundation.Launcher.RootLauncherHost` (`HousekeepingLauncherArgsHelper.cs`) forwarding `--status`, `--repair-config`, `--doctor`, `--bind`, `--suite-id`, and `--automation-*`.
  - Packaged strictly 7 files into NTFS-hardlinked `Releases/v0.2.0/` and `Releases/Latest/` (`Housekeeping-Setup-v0.2.0-win-x64.exe`, `Housekeeping-v0.2.0-win-x64-Portable.zip`, `README.md`, `FEATURES.md`, `CHANGELOG.md`, `RELEASE_NOTES.md`, `SHA256SUMS.txt`) alongside `.7z` archives.
- **`Mother` Subpath Proxy (`/app/housekeeping/`), Loopback Guard & Offline SVG QR (`Housekeeping.Server/`, `Housekeeping.DesktopHost/`, `Housekeeping.Web/`):**
  - Added `X-Forwarded-Prefix` / `UsePathBase` handling in `HousekeepingServerHost.cs` emitting matching `Service-Worker-Allowed` headers on `/sw.js` and scoped dynamic `/manifest.webmanifest` responses.
  - Added `HousekeepingApiEndpoints.MotherAndQr.cs` and `Foundation.Core.QrCodeEncoder.RenderSvg` in `TailscalePwaService.cs` to expose `/api/v1/system/tailscale-qr` offline SVG QR pairing.
  - Configured Vite `base: './'`, created `src/utils/subpathUrl.ts` prefix-aware REST/SignalR/asset routing, and extracted `TopBarPwaControls.tsx` + `SettingsTailscalePwaCard.tsx` with inline SVG QR and 2-line hover hints.
  - Added `--bind <host>`, `--suite-id <id>`, and `--managed-by-suite` CLI flags enforcing `127.0.0.1` loopback binding, suppressing duplicate tray icons, and unifying the single-instance mutex to `Global\Mother_Housekeeping_Mutex`.
- **`--doctor` CLI, Sandbox Containment (`ExitCode = 3`), Release Fail-Closed Guard (`ExitCode = 2`) & Stage 2 4-Drive Sandbox E2E (`Dev/scripts/run_stage2_e2e.py`, `Dev/Housekeeping.Tests/V020*.cs`):**
  - Added `HousekeepingCliRunner.RunDoctor(cli)` returning structured JSON health, containment, sandbox, and external tool diagnostics (`0` healthy, `2` on issues).
  - Wired `Foundation.Core.SandboxEnvironmentSeam` (`FOUNDATION_SANDBOX_ROOT`, `SANDBOX_*_DRIVE`) into `ConfigService.SandboxAndPaths.cs` with fail-closed `ExitCode = 3` host-escape containment distinct from Stage 1 `APP_ISOLATED_TEST=1`.
  - Wired `DesktopAutomationBridge.cs` (`Foundation.Automation.AutomationLoopbackServer`, `GET /v1/snapshot.png`) strictly behind `Debug || HousekeepingTestAutomation`, failing closed with `ExitCode = 2` on both `Housekeeping.exe` and `Housekeeping-Setup-v0.2.0-win-x64.exe` in Release builds.
  - Added modular `V020SuiteAndInstallerTests.cs`, `V020SecurityAndContainmentTests.cs`, `V020SandboxStage2E2ETests.cs`, and `Dev/scripts/run_stage2_e2e.py` verifying Two-Build 4-Drive Sandbox E2E across all 8 modules, `/app/housekeeping/` HTTP proxy + SVG QR, and `--payload` Zip-Slip + tampered-SHA256 / corrupt-ZIP negative installer tests.
- **Public Repository Polish, Configuration Templates & Community Health Files:**
  - Added `Dev/config/settings.example.json` (zero-PII configuration template automatically copied to `<dataDir>\settings.example.json` if missing) and `Dev/Housekeeping.code-workspace`.
  - Added `SECURITY.md`, `CONTRIBUTING.md`, `.github/CODE_OF_CONDUCT.md`, `.github/PULL_REQUEST_TEMPLATE.md`, `.github/ISSUE_TEMPLATE/{bug_report.yml, feature_request.yml, config.yml}`, `.github/workflows/ci.yml`, `.github/workflows/release.yml` (conditional SignPath signing), and visibility-gated `.github/workflows/pages.yml` (emitting `.nojekyll` and `404.html`).
  - Added Shields.io status badges, HTTPS clone instructions, `## License & Legal` (`MPL-2.0`, 7-Zip/MediaInfo/HandBrake/rclone attributions, BYO-media/non-affiliation), and SignPath `## Code Signing Policy` to `README.md`.

### Changed
- **Strict 4-Folder Cutover & Build Reproducibility (`Dev/Directory.Build.props`, `Dev/suite.manifest.json`, `Dev/scripts/build_installer.ps1`):**
  - Retired the legacy root installer directory and central installer mirror copies, excluded `AGENTS.md` from `Builds\` and `Releases\` bundles, and staged `app\LICENSE`, `app\suite.manifest.json`, and `app\config\settings.example.json` inside `app\`.
  - Normalized `Dev/suite.manifest.json` to strict `camelCase` (`schemaVersion: 2`, `sdkVersion: "0.5.3"`, `minSuiteVersion: "0.3.0"`, `warm`/`cold`/`disabled` residency, string `commands`, relative `/api/v1/health` endpoints, `schedules[]`, `targetDrives[]`).
  - Enabled deterministic `PathMap` and Release `DebugType=none` in `Dev/Directory.Build.props`, and achieved net SLOC reductions across `HousekeepingApiEndpoints.cs`, `TopCommandBar.tsx`, `SettingsPage.tsx`, `AppConfig.cs`, `ConfigService.cs`, and `RemoteNodeDeploymentService.cs`.
  - Tokenized `AGENTS.md` (relative links, local gitignored `Dev/data/notion_hub.local.json`), synchronized `FEATURES.md`, `ROADMAP.md`, and the 4-Doc Manual Shelf (`Dev/docs/`), and scrubbed workstation paths, test webhooks, and internal shorthand across source, tests, and documentation.

### Fixed
- **Cookie Secret Relocation (`Dev/Housekeeping.Server/Security/TieredAuthService.cs`):**
  - Atomically migrated `.hk_cookie_secret.key` from legacy `logs\` directories to `<dataDir>\.hk_cookie_secret.key` with strict ACLs, verified persistence, and deleted any `logs\` copy so log directories retain zero cryptographic secrets.
- **Socket `10054` Unobserved Crash Suppression (`Dev/Housekeeping.Core/Services/{CrashBundleService.cs, CrashBundleExceptionFilter.cs}`):**
  - Observed and downgraded aborted-client `SocketException (10054)` and transient disconnect `HttpRequestException`/`IOException` unobserved task exceptions into non-fatal structured `WARN` telemetry with zero crash JSON artifacts.
- **Single-Flight SMB Push Lock & Zero `AGENTS.md` Remote Copies (`Dev/Housekeeping.Core/Services/RemoteNodeDeploymentService.PushLock.cs`):**
  - Serialized concurrent remote `settings.json` pushes with a per-node `SemaphoreSlim` gate and unique `{pid}.{guid}.tmp` staging filenames, and removed `AGENTS.md` from remote node deployment syncs.
- **3-Caller `rclone` Containment & Async Output (`CloudSyncModule.cs`, `RomLauncherGeneratorModule.CommandBuilder.cs`, `DependencyCheckService.cs`, `ProcessRunnerService.cs`):**
  - Injected `RCLONE_CONFIG`, `--workdir`, `--cache-dir`, and `RCLONE_CACHE_DIR` under `<dataDir>\rclone\` across all 3 `rclone` callers via `ProcessRunnerService` environment injection so isolated/sandboxed runs never touch host `%LOCALAPPDATA%\rclone\`; replaced blocking `StandardOutput.ReadToEnd()` before `WaitForExit` in `DependencyCheckService` with bounded async consumption.
- **Dynamic `MediaInfo` Resolution & Probe Failure Truthfulness (`MediaInfoNativeResolver.cs`, `MediaInfoDLL.cs`, `MediaTranscoderModule.CoexistenceGuards.cs`):**
  - Replaced bare `[DllImport("MediaInfo.dll")]` with `NativeLibrary.SetDllImportResolver` + `Foundation.Core.ToolResolver` (`app\`, `app\tools\`, `data\tools\`, `%ProgramFiles%\MediaInfo\`, `PATH`) and fixed `CheckFileIsHevc` so failed opens or missing native libraries emit explicit probe failure diagnostics instead of silently returning `true`.

### Removed
- Deleted legacy `InstallerWizardForm.cs` and `Program.DiagnosticsAndCleanup.cs`, untracked compiled `Dev/Housekeeping.Server/wwwroot/` assets and binary `Dev/Housekeeping.Core/MediaInfo.dll`, and retired legacy standalone installer staging paths.

---

## [0.1.12-hf.1] - 2026-11-15

### Changed
- **Isolated Runtime & Tray Alignment:**
  - Migrated test/isolation/presentation paths to `--data-dir`, `--config`, `--log-dir`, `--port`, `--tray-only` (`UiPresentationMode.TrayOrBackground`), and `APP_ISOLATED_TEST=1`.
  - Aligned `active.state`, `.gitignore`, `AGENTS.md`, Root Quartet, Manual Shelf, `verify_ui_hints.mjs`, and `build_installer.ps1`.

### Removed
- **Legacy Harness & Mock Scaffolding:**
  - Removed legacy harness directories, standalone walkthrough reports, alternate test port bifurcation, synthetic uninstall/shortcut mocks, and viewport probe scaffolding while passing all `183/183` Release xUnit tests under `APP_ISOLATED_TEST=1`.

---

## [0.1.12] - 2026-11-11

### Added
- **Dual-Mode Stop & Graceful Shutdown Control (`PipelineOrchestratorService.CancellationAndShutdown.cs`, `HousekeepingApiEndpoints.CoexistenceAndControl.cs`, `TrayMenuFactory.cs`, `ActivePipelineControlBar.tsx`):**
  - Separated `StopCurrentJob()` (`POST /api/v1/pipeline/stop-job`, linked per-job `_activeJobCts` advancing the active queue while preserving `PendingRuns`), `StopActiveRun()` (`POST /api/v1/pipeline/stop-queue`, explicit user stop clearing `PendingRuns` and checkpoint), and `InitiateGracefulShutdownAbort()` (`POST /api/v1/system/shutdown` & tray exit preserving `interrupted_run_checkpoint.json` and `PendingRuns` across upgrades/reboots).
  - Split Web UI (`TopCommandBar.tsx`, `ActivePipelineControlBar.tsx`) and System Tray (`TrayMenuFactory.cs`) controls into distinct **Stop Job** and **Stop Queue** actions.
- **4-Pillar Sister-App Coexistence Bridge & `.hklock` Coordination (`SisterAppCoexistenceService.cs`, `NetworkSafetyService.DirectoryLocks.cs`, `CoexistenceWarningBanner.tsx`, `SettingsIntegrationsAndDoctorCard.tsx`):**
  - Added `SisterAppCoexistenceService` (`GET /api/v1/coexistence/status`, `POST /api/v1/coexistence/apply-recommendations`) with snapshot-backed presets (`delegate_to_shadowlibrary`, `align_placement`, `enable_auto_align`), `AutoAlignWithSisterApps`, and `<VaultRoot>\.coexistence.json` (`v2`) discovery across `ShadowLibrary (:5179)`, `Redroom (:5180)`, `uStation (:8460)`, `StreamEngine (:8430)`, and `Greenroom (:8420)`.
  - Upgraded directory and archive `.hklock` coordination (`pid` + `processStartTimeUtc` verification with automatic stale/reused-PID eviction) plus Module 3 coexistence guards (`MediaTranscoderModule.CoexistenceGuards.cs`) ignoring `.redroom-part` files, preserving `{edition-*}` tags & sibling `.srt` subtitles, and trusting `promoted_integrity.json` CRC32 records.
- **Universal 4-Mode `.hash` Sidecars & Post-Injection `.archive_hashes.json` (`HashSidecarService.ArchiveRegistry.cs`, `HashSidecarService.ValidationCompat.cs`, `RomManifestCsvService.CoexistenceCompat.cs`):**
  - Upgraded `HashSidecarMode` to the canonical 4-mode enum (`DatabaseOnly = 0`, `SidecarBeside = 1`, `PackageInsideArchive = 2`, `SidecarAndArchive = 3`, with backward-compatible `"None"` -> `DatabaseOnly` alias).
  - Added `<Directory>\.archive_hashes.json` post-injection outer archive hash registry, `# Container:` & `CRC32+MD5+SHA1` non-degrading sidecar preservation, DAT `MD5`/`SHA1` backfill, `.cue/.bin` wrapper path normalization, `# TotalFiles:` / `COMPOSITE_SHA1` compatibility, and 9-column `collection_manifest.csv` `Match Status` synonym normalization.
- **One-Shot `--status` & `--repair-config` CLI + Delimiter-Aware `.csv`/`.tsv` Exports (`HousekeepingCliRunner.cs`, `CollectionManifestModule.Exporters.cs`, `CollectionManifestExportCard.tsx`):**
  - Added synchronous `--status` and `--repair-config` CLI flags across `HousekeepingCliParser`, `HousekeepingCliRunner`, `Launcher`, `DesktopHost`, and `Server` (exiting cleanly with structured JSON and automatic `.corrupt_<timestamp>.bak` recovery).
  - Wired Module 8 `ExportManifestsAsync` and `CollectionManifestExportCard.tsx` to honor `CsvDelimiter` (`Comma`/`.csv`, `Tab`/`.tsv`, `Pipe`/`.csv`, `Semicolon`/`.csv`).
- **Resizable Docked Terminal, Consolidated Sidebar Storage, Mobile Bottom Nav, `ProfileSelect` & 2-Line Hover Hints (`DualPaneTerminal.tsx`, `TerminalResizeHandle.tsx`, `DrivesTableModal.tsx`, `MobileBottomNavBar.tsx`, `ProfileSelect.tsx`, `src/hints/*.ts`):**
  - Added drag-resizable docked terminal height (`S`/`M`/`L` presets persisted in `AppConfig.UiLayout`), `Ctrl+B` sidebar collapse hotkey, consolidated sidebar drive footer opening shared `DrivesTableModal.tsx`, docked `<MobileBottomNavBar />` (`<= 820px`), single-line Job Builder pills, `<select>` `ProfileSelect.tsx` dropdowns with missing-value guards, and 100% 2-line hover hints verified by `Dev/scripts/verify_ui_hints.mjs`.
  - Added 3 modular regression suites (`AuditV0112DataAndShutdownTests.cs`, `AuditV0112FleetDoctorCliTests.cs`, `CoexistenceAndSidecarV0112Tests.cs` — `183` total passing tests).

### Changed
- **Extract-on-Touch Modularization & Module 4 Decoupling (`Dev/Housekeeping.*`):**
  - Extracted cohesive `<= 400 LOC` partial classes, services, hooks, and sub-components across all 28 touched legacy files (bringing `NetworkSafetyService.cs`, `CrashBundleService.cs`, `TrayDaemonApplicationContext.cs`, `DualPaneTerminal.tsx`, `apiClient.ts`, `MediaTranscoderEditor.tsx`, `DashboardPage.tsx`, `CloudSyncModule.cs`, `HumbleSyncModule.cs`, and `CustomScriptsModule.cs` under `400 LOC`).
  - Decoupled Module 4 (`RomLauncherGeneratorModule.cs` & `RomLauncherGeneratorEditor.tsx`) from legacy DAT/manifest/sidecar auditing (delegating checksum/manifest auditing to Module 8 and `ShadowLibrary`).
  - Authored and synchronized the 4-Doc Manual Shelf (`Dev/docs/{ARCHITECTURE,USER_GUIDE,OPERATIONS,INTERFACES}.md`).

### Fixed
- **Safe Exclusive SMB Pre-Probe in `CommitPartialEncode` (`NetworkSafetyService.DirectoryLocks.cs`, `PendingSwapService.cs`):**
  - Eliminated destructive pre-move `File.Delete(finalOutputPath)` calls before `File.Move(..., overwrite: true)` and added `TryProbeExclusiveAccess` (`FileShare.None`) to accurately distinguish `.partial` source locks from destination media locks.
- **Crash Telemetry Hardening & Fleet Socket Idle Timeout (`CrashBundleService.cs`, `CrashBundleExceptionFilter.cs`, `HousekeepingRuntimeContainer.cs`):**
  - Added `e.SetObserved()` in `TaskScheduler.UnobservedTaskException`, suppressed false-positive crash dumps on benign `OperationCanceledException` and idle `HttpConnection` `SocketException (10054)` resets, configured `PooledConnectionIdleTimeout = 25s` on `FleetHttpClient`, and updated version fallbacks.
- **Unique Atomic `.tmp` Writes & Clean-Release-Root Fleet SMB Sync (`ConfigService.AtomicIoAndMigrations.cs`, `RemoteNodeDeploymentService.PathLayout.cs`):**
  - Upgraded `ConfigService.AtomicWriteAllText` to per-caller `{target}.{pid}.{guid:N}.tmp` with UTF-8 No-BOM and bounded SMB retries, and updated Fleet Node SMB sync for Clean-Release-Root `<RemoteRoot>\app\` & `<RemoteRoot>\data\` layouts.
- **System Doctor `--log-dir` Containment & Clean State-Only Backup (`HousekeepingApiEndpoints.SystemDoctorAndBackup.cs`):**
  - Fixed `/api/v1/system/doctor/export-bundle` to write strictly inside `ActiveLogsDir` and fixed `/api/v1/system/backup/now` to write into sibling `backups\` while archiving only clean JSON state files via `FileShare.ReadWrite` (excluding `webview2_cache`).
- **Terminal Ring-Buffer Saturation Auto-Scroll, Suspended Child Resume & Zero Empty Catches:**
  - Fixed `DualPaneTerminal` auto-scroll stall at the 2,500-entry ring cap via `lastEntryKey` + `ResizeObserver` + `45px` proximity latch, resumed `NtSuspendProcess`-frozen child trees before kill in `ProcessRunnerService.cs`, deduplicated repeated log prefixes in `TerminalLogBufferService.cs`, and eliminated bare `catch {}` blocks.

---

## [0.1.11] (Universal SemVer Transition from 1.0.0-rc.11-hf.2) - 2026-11-02

### Changed
- **Clean Release Root Containment (`data\` & `app\` Parity) (`ConfigService.cs`, `Housekeeping.Installer`, `Housekeeping.Launcher`):**
  - Updated `ConfigService` (`InstallRootDirectory`, `ResolveDefaultDataDirectory`, `MigrateLegacyRootStateIfPresent`) so running from `<InstallRoot>\app\` resolves `DataDirectory` to `<InstallRoot>\data`, `ActiveLogsDir` to sibling `<InstallRoot>\logs`, and `SnapshotsDir` to sibling `<InstallRoot>\backups\config_snapshots`.
  - Automatically migrated legacy outer-root user state files and runtime folders into `<InstallRoot>\data\` and placed `Housekeeping-Uninstall.exe`, `.install_manifest.json`, and `version.txt` inside `<InstallRoot>\app\`.
- **Native Standalone WebView2 Window Default & Schema v4 Migration (`AppConfig.cs`, `ConfigService.cs`, `TrayDaemonApplicationContext.cs`, `DisposableWebViewForm.cs`):**
  - Changed `WebServerConfig.DefaultDesktopLaunchMode` default from `"Browser"` to `"StandaloneWindow"`, bumped `AppConfig.CurrentSchemaVersion` from `3` to `4`, and contained the WebView2 cache inside `data\webview2_cache`.

---

*(Older pre-transition `1.0.0-rc` history trimmed for 3-minor retention; cold snapshots preserved in `Archive/`.)*
