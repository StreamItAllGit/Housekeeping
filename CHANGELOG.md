# Changelog

All notable changes to the **Housekeeping** project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

---

## [0.1.12-hf.1] - 2026-11-15

### Changed
- **Isolated Runtime & Tray Alignment**:
  - Migrated test/isolation/presentation paths to standard `--data-dir`, `--config`, `--log-dir`, `--port`, `--tray-only` (`UiPresentationMode.TrayOrBackground`), and `APP_ISOLATED_TEST=1`.
  - Standardized Tier A 5-folder container layout (`Archive\`, `Builds\`, `Dev\`, `Installers\`, `Releases\`) across `active.state`, `.gitignore`, `AGENTS.md`, Root Quartet, Manual Shelf, and archived plans/reports, and aligned `verify_ui_hints.mjs` & `build_installer.ps1` cleanly.

### Removed
- **Legacy Harness & Mock Scaffolding**:
  - Removed legacy harness directories, standalone walkthrough reports, alternate test port bifurcation, synthetic uninstall/shortcut mocks, and viewport probe scaffolding while passing all `183/183` Release xUnit tests under `APP_ISOLATED_TEST=1`.

---

## [0.1.12] - 2026-11-11

### Added
- **Dual-Mode Stop & Graceful Shutdown Control (`PipelineOrchestratorService.CancellationAndShutdown.cs`, `HousekeepingApiEndpoints.CoexistenceAndControl.cs`, `TrayMenuFactory.cs`, `ActivePipelineControlBar.tsx`)**:
  - Separated `StopCurrentJob()` (`POST /api/v1/pipeline/stop-job`, linked per-job `_activeJobCts` advancing the active queue while preserving `PendingRuns`), `StopActiveRun()` (`POST /api/v1/pipeline/stop-queue`, explicit user stop clearing `PendingRuns` and checkpoint), and `InitiateGracefulShutdownAbort()` (invoked by `POST /api/v1/system/shutdown`, `TrayDaemonApplicationContext.ShutdownAndExit()`, and `Dispose()`, preserving `interrupted_run_checkpoint.json` and `PendingRuns` across upgrades/reboots).
  - Split Web UI (`TopCommandBar.tsx`, `ActivePipelineControlBar.tsx`) and System Tray (`TrayMenuFactory.cs`) controls into distinct **Stop Job** and **Stop Queue** actions.
- **4-Pillar Sister-App Coexistence Bridge & Standard 19 `.hklock` Coordination (`SisterAppCoexistenceService.cs`, `NetworkSafetyService.DirectoryLocks.cs`, `CoexistenceWarningBanner.tsx`, `SettingsIntegrationsAndDoctorCard.tsx`)**:
  - Added `SisterAppCoexistenceService` (`GET /api/v1/coexistence/status`, `POST /api/v1/coexistence/apply-recommendations`) with snapshot-backed remediation presets (`delegate_to_shadowlibrary`, `align_placement`, `enable_auto_align`), `AutoAlignWithSisterApps`, and `<VaultRoot>\.coexistence.json` (`v2`) discovery across `ShadowLibrary (:5179)`, `Redroom (:5180)`, `uStation (:8460)`, `StreamEngine (:8430)`, and `Greenroom (:8420)`.
  - Upgraded directory and archive `.hklock` coordination to Dev Bible Standard 19 §4 (`pid` + `processStartTimeUtc` verification with automatic stale/reused-PID eviction) plus Module 3 coexistence guards (`MediaTranscoderModule.CoexistenceGuards.cs`) ignoring `.redroom-part` files, preserving `{edition-*}` tags & sibling `.srt` subtitles, and trusting `promoted_integrity.json` CRC32 records.
- **Universal 4-Mode `.hash` Sidecars & Post-Injection `.archive_hashes.json` (`HashSidecarService.ArchiveRegistry.cs`, `HashSidecarService.ValidationCompat.cs`, `RomManifestCsvService.CoexistenceCompat.cs`)**:
  - Upgraded `HashSidecarMode` to the canonical 4-mode enum (`DatabaseOnly = 0`, `SidecarBeside = 1`, `PackageInsideArchive = 2`, `SidecarAndArchive = 3`, with backward-compatible `"None"` $\rightarrow$ `DatabaseOnly` alias).
  - Added `<Directory>\.archive_hashes.json` post-injection outer archive hash registry, `# Container:` & `CRC32+MD5+SHA1` non-degrading sidecar preservation, DAT `MD5`/`SHA1` backfill, `.cue/.bin` wrapper path normalization, `# TotalFiles:` / `COMPOSITE_SHA1` compatibility, and 9-column `collection_manifest.csv` `Match Status` synonym normalization.
- **One-Shot `--status` & `--repair-config` CLI + Delimiter-Aware `.csv`/`.tsv` Exports (`HousekeepingCliRunner.cs`, `CollectionManifestModule.Exporters.cs`, `CollectionManifestExportCard.tsx`)**:
  - Added synchronous `--status` and `--repair-config` CLI flags across `HousekeepingCliParser`, `HousekeepingCliRunner`, `Launcher`, `DesktopHost`, and `Server` (exiting cleanly with structured JSON and automatic `.corrupt_<timestamp>.bak` recovery).
  - Wired Module 8 `ExportManifestsAsync` and `CollectionManifestExportCard.tsx` to honor `CsvDelimiter` (`Comma`/`.csv`, `Tab`/`.tsv`, `Pipe`/`.csv`, `Semicolon`/`.csv`).
- **Resizable Docked Terminal, Consolidated Sidebar Storage, Mobile Bottom Nav, `ProfileSelect` & 2-Line Hover Hints (`DualPaneTerminal.tsx`, `TerminalResizeHandle.tsx`, `DrivesTableModal.tsx`, `MobileBottomNavBar.tsx`, `ProfileSelect.tsx`, `src/hints/*.ts`)**:
  - Added drag-resizable docked terminal height (`S`/`M`/`L` presets persisted in `AppConfig.UiLayout`), `Ctrl+B` sidebar collapse hotkey, consolidated sidebar drive footer opening shared `DrivesTableModal.tsx`, docked `<MobileBottomNavBar />` (`<= 820px`), single-line Job Builder module pills, `<select>` `ProfileSelect.tsx` dropdowns with `⚠ Missing` stale-value guards, and 100% 2-line hover hints verified by `Dev\scripts\verify_ui_hints.mjs`.
  - Added 3 modular `<= 400 LOC` xUnit regression suites (`AuditV0112DataAndShutdownTests.cs`, `AuditV0112FleetDoctorCliTests.cs`, `CoexistenceAndSidecarV0112Tests.cs` — `183` total passing tests).

### Changed
- **Standard 08 §8.2 Extract-on-Touch Modularization & Module 4 Decoupling (`Dev\Housekeeping.*`)**:
  - Extracted cohesive `<= 400 LOC` partial classes, services, hooks, and sub-components across all 28 touched legacy files (achieving strict net LOC reductions across every touched file and bringing `NetworkSafetyService.cs`, `CrashBundleService.cs`, `TrayDaemonApplicationContext.cs`, `DualPaneTerminal.tsx`, `apiClient.ts`, `MediaTranscoderEditor.tsx`, `DashboardPage.tsx`, `CloudSyncModule.cs`, `HumbleSyncModule.cs`, and `CustomScriptsModule.cs` under `400 LOC`).
  - Decoupled Module 4 (`RomLauncherGeneratorModule.cs` & `RomLauncherGeneratorEditor.tsx`) from legacy Step 2.6 DAT/manifest/sidecar auditing (delegating checksum/manifest auditing cleanly to Module 8 and `ShadowLibrary`).
  - Updated `Dev\scripts\build_installer.ps1` to auto-build `Housekeeping.Web`, copy all `Housekeeping.Installer\*.cs` partials, emit portable zero-PII `Builds\v0.1.12\AGENTS.md` and `app\suite.manifest.json`, generate sanitized `Releases\Latest\README.md` from `USER_GUIDE.md` + dynamic `RELEASE_NOTES.md`, and retain unpacked `Releases\v0.1.12\` mirrors per Standard 03 §1.8.
  - Authored and synchronized the 4-Doc Manual Shelf (`Dev\docs\{ARCHITECTURE,USER_GUIDE,OPERATIONS,INTERFACES}.md`).

### Fixed
- **Safe Exclusive SMB Pre-Probe in `CommitPartialEncode` (`BUG-01`, `P0` — `NetworkSafetyService.DirectoryLocks.cs`, `PendingSwapService.cs`)**:
  - Eliminated destructive pre-move `File.Delete(finalOutputPath)` calls before `File.Move(..., overwrite: true)` and added `TryProbeExclusiveAccess` (`FileShare.None`) to accurately distinguish `.partial` source locks (Defender/SMB opportunistic locks) from destination media locks (Plex).
- **Crash Telemetry Hardening & Fleet Socket Idle Timeout (`BUG-03`, `P1` — `CrashBundleService.cs`, `CrashBundleExceptionFilter.cs`, `HousekeepingRuntimeContainer.cs`)**:
  - Added `e.SetObserved()` in `TaskScheduler.UnobservedTaskException`, suppressed false-positive crash dumps on benign `OperationCanceledException` and idle `HttpConnection` `SocketException (10054)` resets, configured `PooledConnectionIdleTimeout = 25s` on `FleetHttpClient`, and updated all version fallbacks to `"0.1.12"`.
- **Unique Atomic `.tmp` Writes & Clean-Release-Root Fleet SMB Sync (`BUG-04`, `P1` — `ConfigService.AtomicIoAndMigrations.cs`, `RemoteNodeDeploymentService.PathLayout.cs`)**:
  - Upgraded `ConfigService.AtomicWriteAllText` to per-caller `{target}.{pid}.{guid:N}.tmp` with UTF-8 No-BOM and bounded SMB retries, and updated Fleet Node SMB deployment/sync for Clean-Release-Root `<RemoteRoot>\app\` & `<RemoteRoot>\data\` layouts with full runtime exclusions.
- **System Doctor `--log-dir` Containment & Clean State-Only Backup (`BUG-05`, `P1` — `HousekeepingApiEndpoints.SystemDoctorAndBackup.cs`)**:
  - Fixed `/api/v1/system/doctor/export-bundle` to write strictly inside `ActiveLogsDir` and fixed `/api/v1/system/backup/now` to write into sibling `backups\` while archiving only clean JSON state files via `FileShare.ReadWrite` (eliminating self-inclusion and `webview2_cache` `IOException` crashes).
- **Terminal Ring-Buffer Saturation Auto-Scroll, Suspended Child Resume & Zero Empty Catches (`OPT-01`, `OPT-02`, `SIMP-01`)**:
  - Fixed `DualPaneTerminal` auto-scroll stall at the 2,500-entry ring cap via `lastEntryKey` + `ResizeObserver` + `45px` proximity latch, resumed `NtSuspendProcess`-frozen child trees before kill in `ProcessRunnerService.cs`, deduplicated repeated `[WARN] [WARN]` / `[HH:mm:ss]` prefixes in `TerminalLogBufferService.cs`, removed dead `SafeRead`, and eliminated 100% of bare `catch {}` blocks (`0` `ERR-001`).

---

## [0.1.11] (Universal SemVer Transition from 1.0.0-rc.11-hf.2) - 2026-11-02

### Changed
- **Sister-App Clean Release Root Containment (`data\` & `app\` Parity) (`ConfigService.cs`, `Housekeeping.Installer\Program.cs`, `Housekeeping.Launcher\Program.cs`, `Dev\scripts\build_installer.ps1`)**:
  - Updated `ConfigService` (`InstallRootDirectory`, `ResolveDefaultDataDirectory`, `MigrateLegacyRootStateIfPresent`) so running from `<InstallRoot>\app\` resolves `DataDirectory` to `<InstallRoot>\data`, `ActiveLogsDir` to sibling `<InstallRoot>\logs`, and `SnapshotsDir` to sibling `<InstallRoot>\backups\config_snapshots`.
  - Updated `Housekeeping.Installer` and `ConfigService` to automatically migrate all legacy outer-root user state files (`settings.json`, `settings.json.bak*`, `compliance_reports.json`, `history.json`, `pending_swaps.json`, `interrupted_run_checkpoint.json`, `.hk_cookie_secret.key`) and runtime folders (`dats\`, `tools\`, `manifests\`, `cache\`, `crashes\`, and `WebView2Cache\` $\rightarrow$ `data\webview2_cache`) into `<InstallRoot>\data\`, deleting stale outer-root and `app\settings.json` copies.
  - Moved `Housekeeping-Uninstall.exe`, `.install_manifest.json`, and `version.txt` inside `<InstallRoot>\app\` (removing outer-root copies) so the deployed `<InstallRoot>\` directory contains strictly the 5 folders (`app\`, `backups\`, `data\`, `logs\`, `scripts\`) and 2 root files (`Housekeeping.exe`, `mother.manifest.json`) in 100% parity with `ShadowLibrary`.
  - Removed the legacy `--data-dir <rootDir>` override from `Housekeeping.Launcher\Program.cs` so the thin root launcher forwards arguments cleanly to `app\Housekeeping.exe`.
- **Native Standalone WebView2 Window Default & Schema v4 Migration (`AppConfig.cs`, `ConfigService.cs`, `TrayDaemonApplicationContext.cs`, `DisposableWebViewForm.cs`)**:
  - Changed `WebServerConfig.DefaultDesktopLaunchMode` default from `"Browser"` to `"StandaloneWindow"` across `AppConfig.cs` and `TrayDaemonApplicationContext.cs`.
  - Bumped `AppConfig.CurrentSchemaVersion` from `3` to `4` so `ConfigService.MigrateConfig` automatically upgrades existing `v3` `settings.json` configurations from `"Browser"` to `"StandaloneWindow"`.
  - Updated `TrayDaemonApplicationContext` and `DisposableWebViewForm` to always store the WebView2 profile inside `Path.Combine(DataDirectory, "webview2_cache")` (`data\webview2_cache`).

---

## [1.0.0-rc.11-hf.1] - 2026-11-02

### Added
- **Graceful HTTP Shutdown Endpoint (`HousekeepingApiEndpoints.cs`, `TrayDaemonApplicationContext.cs`)**:
  - Added `POST /api/v1/system/shutdown` and `POST /api/system/shutdown` backed by `IHostApplicationLifetime.StopApplication()` and `TrayDaemonApplicationContext` STA shutdown hooks, enabling clean non-interactive daemon termination for both the installer and `scripts\stop-daemon.bat`.
- **Installer Process Lifecycle & `--launch` / `--no-launch` Flags (`Housekeeping.Installer\Program.cs`, `InstallerWizardForm.cs`)**:
  - Added fail-closed `IsWithinDirectory` path verification and 3-stage `StopRunningProcessesInDirectory` (graceful `CloseMainWindow` -> HTTP `/api/v1/system/shutdown` -> process tree kill fallback) before upgrade extraction or uninstallation.
  - Added automatic post-upgrade daemon relaunch when a running instance was stopped (as well as explicit `--launch` / `-l` and `--no-launch` CLI flags and `OnFormClosing` launch handling in the GUI wizard).

### Fixed
- **Canonical Parent Configuration Adoption on Clean Release Root (`ConfigService.cs`, `Housekeeping.Launcher\Program.cs`, `Housekeeping.Installer\Program.cs`)**:
  - Fixed `ConfigService.ResolveDefaultDataDirectory` and `TryAdoptParentCanonicalSettings` so binaries executing inside `<Root>\app\` automatically detect, sync, and adopt the user's canonical `<Root>\settings.json` (preserving all schedules, queues, jobs, `history.json`, and `compliance_reports.json`) instead of generating a blank default `app\settings.json` stub.
  - Updated `Housekeeping.Launcher` to forward `--data-dir <Root>` when `<Root>\settings.json` is present and no custom data directory override is supplied.
  - Added `MigrateLegacyUserState` to `Housekeeping.Installer` during upgrade and uninstall transitions.
- **UX Layout & Sister-App Parity (`Housekeeping.Web\src\App.tsx`)**:
  - Fixed the horizontal flex layout bug where `<GlobalTerminalDrawer />` was rendered as a direct child of `.hk-shell` (`flex-direction: row`), creating a giant empty 3rd column on the right side of the window. Nesting `<GlobalTerminalDrawer />` inside `.hk-body-area` directly beneath `<main className="hk-main">` restores full-width dashboard layout and bottom-docked status footer parity.
- **Post-Backup Legacy Root Clutter Cleanup (`Housekeeping.Installer\Program.cs`)**:
  - Added `CleanupObsoleteInstallationFiles` immediately after pre-upgrade state backup to sweep recursive `*.old_upgrade` files, legacy flat-layout root DLLs/PDBs/manifests (`Housekeeping.Core.dll`, `MediaInfo.dll`, `Microsoft.Web.WebView2.*`, `WebView2Loader.dll`, `Housekeeping.Server.exe`, `CHANGELOG.md`, `README.md`, `MODULES_REFERENCE.md`), and obsolete root asset directories (`Assets\`, `runtimes\`, `wwwroot\`) while strictly protecting canonical user databases and directories (`settings.json`, `compliance_reports.json`, `history.json`, `backups\`, `data\`, `logs\`, `dats\`, `tools\`) per Dev Bible Standard 12 §3.1.

---

## [1.0.0-rc.11] - 2026-11-02

### Added
- **Universal Media Manifest & Bit Rot Auditor (`CollectionManifestModule.cs`, `HashSidecarService.cs`, `AppConfig.cs`, `CollectionManifestEditor.tsx`)**:
  - Transformed Module 8 from specialized ROM DAT matching into a universal media cataloger and bit rot detection engine across all media types (Videos, Movies, TV Shows, Music, ISOs, Archives, Documents, Games).
  - Added Two-Step Archive Bit Rot verification with ShadowLibrary parity: ultra-fast outer container hash check (`# Container: <name> | SIZE: <sz> | CRC32: <crc> | MD5: <md5> | SHA1: <sha1>`) with inner payload fallback before flagging bit rot.
  - Added automatic baseline `.hash` creation (`CreateMissingHashFiles`) and container auto-healing (`EnableBitRotAutoHeal`) on repacked archives.
  - Emits prominent `[BIT ROT WARNING]` terminal/disk logs and color-coded status badges (`Verified`, `Baseline Captured`, `Bit Rot Warning`, `Missing`, `N/A`) in CSV, JSON, and HTML visual reports on detected corruption.
  - Added automatic sister-app coexistence probing for ShadowLibrary (`.hklock` locks and `# Container:` header parity).
  - Updated `CollectionManifestEditor.tsx` with dedicated "🔐 Integrity & Bit Rot Engine" settings and toggles.
- **WinForms Single-Surface GUI Installer & Clean Release Root (`Housekeeping.Installer`, `Housekeeping.Launcher`, `Dev\scripts\build_installer.ps1`)**:
  - Authored dark-themed WinForms installer wizard (`650×434` collapsed, `650×544` expanded) with Housekeeping Amber accent (`#f59e0b`), upgrade/repair/uninstall modes, collapsible details log, Authenticode code signing, and single-surface execution.
  - Implemented Clean Release Root layout per Standard 12 §3.1: deployed root contains strictly `Housekeeping.exe` (single-file thin launcher stub forwarding args to `app\Housekeeping.exe`), `version.txt`, and subdirectories `app\`, `scripts\`, `scripts\interactive\`, `data\`, and `logs\`.
  - Added operational scripts (`start-daemon.bat`, `stop-daemon.bat`, `status-check.bat`, `backup-now.bat`) and interactive troubleshooting scripts (`inspect-status.bat`, `tail-logs.bat`, `repair-config.bat`).
- **Amber Left Sidebar Navigation Rail, Omni-Search & System Doctor (`SidebarNavRail.tsx`, `OmniSearchModal.tsx`, `KeyboardShortcutsModal.tsx`, `SettingsPage.tsx`, `HousekeepingApiEndpoints.cs`)**:
  - Replaced horizontal top tabs with responsive left sidebar rail (`220px` expanded / `56px` collapsed icon rail) with Housekeeping Amber active indicator, `1..7` keyboard shortcuts, and pinned drive space.
  - Added global `Ctrl+K` Omni-Search modal and `?` keyboard shortcuts cheat-sheet dialog.
  - Added `🩺 System Doctor` sub-tab in Settings providing real-time dependency status checks for `7z.exe`, `rclone.exe`, `HandBrakeCLI`, `hbd`, `MediaInfo.dll`, Tailscale, and drive health, with one-click `.7z` backup and diagnostic bundle export.
- **Mother v1.0 Ecosystem Integration & Disk Arbitration Hooks (`Dev\mother.manifest.json`, `SchedulerAndNotificationService.cs`, `HousekeepingApiEndpoints.cs`, `HousekeepingCliParser.cs`, `TrayDaemonApplicationContext.cs`)**:
  - Shipped `mother.manifest.json` (`id: "housekeeping"`, `port: 5178`, `brand_accent: "#f59e0b"`, `archetype: "companion_daemon"`).
  - Added `DeferredUntilUtc`, `DeferSchedules(TimeSpan)`, and `ClearDeferral()` to `SchedulerAndNotificationService` with schedule evaluation guards in `ShouldTriggerQueueNow`, `ShouldTriggerMasterScheduleNow`, and `EvaluateQueueSchedules`.
  - Exposed `GET /api/scheduler/status` (and `/api/v1/scheduler/status`) and `POST /api/scheduler/defer` (and `/api/v1/scheduler/defer`) for Mother v1.0 fleet disk arbitration.
  - Added `--managed-by-mother` CLI flag to `HousekeepingCliParser` and `TrayDaemonApplicationContext` to suppress the standalone system tray icon when supervised by Mother v1.0.
- **Table Multi-Column Sorting, Filter Pills & Data Export (`HistoryPage.tsx`, `ReportsCenterPage.tsx`, `JobQueuesPage.tsx`)**:
  - Upgraded `HistoryPage.tsx` with clickable `▲/▼` sort headers across all columns (`Started`, `Duration`, `Trigger`, `Summary`, `Processed`, `Errors`, `Status`), status filter pills (`All`, `Completed`, `Error`, `Cancelled`), row selection checkboxes (`Select All / Invert`), and client-side `📥 Export CSV` and `📥 Export JSON` downloads.
  - Added `📥 Export JSON` to `ReportsCenterPage.tsx` backed by `GET /api/v1/reports/export-json`.
  - Added `📥 Export Queue JSON` and master enabled toggle in `JobQueuesPage.tsx`.

### Changed
- **Game Launcher Decoupling (`RomLauncherGeneratorModule.cs`, `AppConfig.cs`, `RomLauncherGeneratorEditor.tsx`)**:
  - Completely removed redundant `.hash` manipulation (generation, verification, healing, and deletion) from the game launcher packaging pipeline (`PackageUncompressedRomsInto7zAsync` and `FinalizeCompressedArchiveSidecarAsync`).
  - Module 4 strictly focuses on ROM packaging into `.7z`, multi-disc `.m3u` playlist generation, `.bat` launcher scripts, `.url` Steam shortcuts, and `rclone bisync` save syncing.
  - Cleaned up obsolete `.hash` placement UI controls from `RomLauncherGeneratorEditor.tsx`.
- Decoupled Sister-App launch buttons and centralized power/watchdog governance: removed standalone post-run power dropdowns from top bar, removed legacy Greenroom companion launch items from UI and Windows System Tray menu, and removed standalone Task Scheduler watchdog registration in favor of `Mother v1.0` centralized supervision.

### Fixed
- Hardened `PendingSwapService.RetrySwap` atomic file moves with exclusive `FileShare.None` pre-probes, eliminating premature target deletion on locked working files.
- Hardened `CloudSyncModule.TryExtractStaleBisyncLockPath` by stripping ANSI escape sequences prior to PID, year, and lock path regex matching.
- Enforced tabular numerals (`font-variant-numeric: tabular-nums`) across `.hk-kpi-value`, `.hk-table td`, `.hk-pill`, and `.hk-badge-count`.
- Synchronized all project manifests, `CrashBundleService` fallback version strings, and `Directory.Build.props` in lockstep to `1.0.0-rc.11`.

---

*(Older pre-transition 1.0.0-rc and 2.x history trimmed per Standard 04 3-minor retention; snapshots preserved in Archive\\.)*
