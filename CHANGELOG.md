# Changelog

All notable changes to the **Housekeeping** project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

---

## [0.1.12] - 2026-11-11

### Added
- **Three-Tier Execution & Graceful Shutdown Control (`PipelineOrchestratorService.CancellationAndShutdown.cs`, `HousekeepingApiEndpoints.CoexistenceAndControl.cs`, `TrayMenuFactory.cs`, `ActivePipelineControlBar.tsx`)**:
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

## [1.0.0-rc.10-nightly.1] - 2026-11-02

### Added
- **Root Documentation Quartet & 6-Folder `Releases\` Distribution Architecture (`README.md`, `FEATURES.md`, `ROADMAP.md`, `CHANGELOG.md`, `Releases\`, `Dev\scripts\build_installer.ps1`)**:
  - Migrated `README.md` and `CHANGELOG.md` from `Dev\` to the outer project root (`D:\Projects\Personal\Housekeeping\`) and authored root `FEATURES.md` and `ROADMAP.md` per Dev Bible Standard 16.
  - Added `Releases\` 6-file distribution bundle packaging (`Housekeeping-Setup-v1.0.0-rc.10-nightly.1-win-x64.exe`, stripped end-user `README.md` & `FEATURES.md`, `RELEASE_NOTES.md`, `CHANGELOG.md`, `SHA256SUMS.txt` in `Releases\Latest\`, `Releases\Latest.7z`, and `Releases\v1.0.0-rc.10-nightly.1.7z`) and automatic central installer mirroring to `D:\Projects\Installers\`.
- **Standard 15 Structured Disk Log Grammar, `10 MB × 5` Log Rotation & 30-Day Run Log Retention (`TerminalLogBufferService.cs`, `CrashBundleService.cs`)**:
  - Upgraded `Housekeeping_Live.log` disk lines to Dev Bible Standard 15 §5.1 grammar (`YYYY-MM-DDTHH:mm:ss.fffZ [v<Version>] [PID:<pid>/T:<tid>] [<LEVEL>] [<TAG>] <Message>`) while preserving `[HH:mm:ss] [TAG] Message` in `entry.FormattedLine` for UI terminal drawer compatibility.
  - Increased live log rotation threshold from `5 MB` to `10 MB` with `5` numbered historical generations (`Housekeeping_Live.log.1`…`5`), added automatic startup pruning for `logs\runs\*.log` older than 30 days (`PruneStaleRunLogs`), and aligned `CrashBundleService` fallback log path casing (`Housekeeping_Live.log`).

### Fixed
- **ANSI Escape Code Stripping in `rclone bisync` Stale Lock Recovery (`CloudAndHumbleSyncModules.cs`)**:
  - Added `StripAnsiCodes` and `TryExtractStaleBisyncLockInfo` to strip ANSI color escape sequences (`\x1b[31m`, `\x1b[93m`, `\x1b[0m`) and extract locker PID (`PID of locker:` / `by PID`) and lock expiry year (`2226`) before matching `.lck` paths, fixing production `rclone bisync` lock recovery when `rclone` emits colorized stderr notices.
- **Exclusive-Lock Pre-Probe Before `PendingSwapService.RetrySwap` (`PendingSwapService.cs`)**:
  - Removed the premature `File.Delete(item.TargetPath)` call before `File.Move` in `RetrySwap` and added an exclusive `FileShare.None` readiness check on `item.WorkingPath` (and existing `item.TargetPath`) before executing `File.Move(..., overwrite: true)`, guaranteeing a locked `.partial.mkv` never deletes an existing target media file.
- **Numbered RC Nightly SemVer Ordering, Zero Bare `catch` Blocks & Tabular Numerals (`RemoteNodeDeploymentService.cs`, `Housekeeping.Installer\Program.cs`, `TopCommandBar.tsx`, `theme.css`)**:
  - Updated `RemoteNodeDeploymentService.CompareSemVer` pre-release tokenization to split on both `.` and `-` so numbered RC nightlies (`1.0.0-rc.10 < 1.0.0-rc.10-nightly.1 < 1.0.0-rc.11`) sort in strict SemVer order.
  - Replaced all 13 bare `catch` blocks in `Housekeeping.Installer\Program.cs` with `Debug.WriteLine` diagnostics (`0` first-party `ERR-001` findings), added `"scripts"` cleanup to `RunUninstall`, updated mobile prompt version fallback in `TopCommandBar.tsx`, and added `font-variant-numeric: tabular-nums` to `.hk-badge-count`, `.hk-pill`, and `.hk-table td`.

---

## [1.0.0-rc.10] - 2026-11-02

### Added
- **Dev Bible Standard 15 Zero-PII JSON Crash Bundle Dumper (`CrashBundleService.cs`, `TerminalLogBufferService.cs`, `HousekeepingRuntimeContainer.cs`, `Housekeeping.DesktopHost\Program.cs`, `Housekeeping.Server\Program.cs`)**:
  - Added `CrashBundleService` (`BUG-03`) generating structured JSON crash bundles under `<LogDir>\crashes\crash_YYYYMMDD_HHMMSS_v<BuildVersion>.json` on unhandled `AppDomain.CurrentDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`, WinForms `Application.ThreadException`, and fatal startup exceptions.
  - Added `TerminalLogBufferService.GetPrecedingLogLines(int count = 50)` so every crash bundle captures the preceding 50 formatted ring-buffer log lines automatically.
  - Enforced strict zero-PII/secret sanitization (`%USERPROFILE%` path redaction and `--token`/credential redaction) and a 20-file retention cap in `<LogDir>\crashes\`.
- **Upgrade-Safe Installer Hardening for `1.0.0` Sprint (`Housekeeping.Installer\Program.cs`)**:
  - Added existing installation auto-detection (`DetectExistingInstallLocation`) via `HKCU\...\Uninstall\Housekeeping` (`InstallLocation`) and fixed-drive `Programs\Housekeeping` discovery so upgrades automatically target the active installation directory.
  - Added atomic pre-upgrade user-state snapshots (`<InstallDir>\backups\upgrade_<timestamp>_<priorVersion>\`, retaining the 10 newest snapshots), obsolete asset cleanup using the prior `.install_manifest.json` while protecting all user files/directories (`settings.json`, `history.json`, `compliance_reports.json`, `pending_swaps.json`, `interrupted_run_checkpoint.json`, `backups\`, `dats\`, `logs\`, `manifests\`, `tools\`, `data\`, `cache\`, `crashes\`), and `.old_upgrade` in-place rename fallback (`OpenUpgradeSafeOutputStream` / `CopyFileUpgradeSafe`) when upgrading over locked/in-use binaries or DLLs.

### Fixed
- **Unified Pending Swap Recovery, Overwrite Shield & Intra-Job Sweep (`NetworkSafetyService.cs`, `MediaTranscoderModule.cs`, `ProxyGeneratorModule.cs`, `ArchiveCompressorModule.cs`)**:
  - Added `NetworkSafetyService.TryResolveOrRecoverPendingEncode` (`BUG-01`) across `MediaTranscoderModule` (both resolution tiers and language edition rules), `ProxyGeneratorModule`, and `ArchiveCompressorModule` to check `PendingSwapService.FindByTarget`, `.new` fallback files, and `.partial.*` working files before launching HandBrake.
  - Added a pre-encode `PendingSwaps.IsPending(workingOutput)` shield so HandBrake never overwrites a completed `.partial.*` encode that is waiting in `pending_swaps.json` due to a locked target file.
  - Updated post-encode commit logging to distinguish immediate commits (`[Done] Created ...`) from deferred background swaps (`[Queued Swap] Completed ... encode preserved and queued for background swap`), and added a throttled 2-minute opportunistic `PendingSwaps.SweepAndCommitAll` inside long-running module file loops.
- **Monotonic UTC Per-Module Custom Scheduling & Persistence Sync (`AppConfig.cs`, `SchedulerAndNotificationService.cs`, `ConfigService.cs`)**:
  - Added persisted `ModuleBaseConfig.LastScheduledRunUtc` (`BUG-02`) and updated `SchedulerAndNotificationService.GetPerModuleSchedulesDueNow` / `CheckModule` to evaluate interval and daily schedules using monotonic `DateTime.UtcNow` (preventing 1-hour stalls across Daylight Saving Time fall-back transitions and immediate re-triggers on process restart).
  - Updated `ConfigService.TryUpdateAndSave` (`PreserveModuleScheduleUtc`) to preserve server-owned `LastScheduledRunUtc` timestamps across all 8 modules against stale browser/PWA `PUT /api/v1/config` snapshots.

---

## [1.0.0-nightly.20261101] - 2026-11-01

### Fixed
- **Orphaned `rclone bisync` Lock File Protection & Auto-Recovery (`CloudAndHumbleSyncModules.cs`, `AppConfig.cs`)**:
  - Appended bounded `--max-lock 2h` by default to `rclone bisync` executions in `CloudSyncModule` (whenever custom flags do not already specify `--max-lock`) and generated emulator save-sync `bisync` commands in `RomLauncherConfig`, preventing killed/interrupted `bisync` runs from creating non-expiring `.lck` files.
  - Added orphaned `bisync` `.lck` detection and safe single-retry auto-recovery in `CloudSyncModule`: when `rclone bisync` exits non-zero reporting `prior lock file found: ...bisync...lck` and zero `rclone` processes are currently active on the host, `CloudSyncModule` removes the stale `.lck` file, logs a `[WARN]` notice, and retries the `bisync` job once.
- **Sandbox Container Preservation on `--uninstall --purge-data --test-mode` (`Housekeeping.Installer\Program.cs`)**:
  - Guarded empty directory deletion at the end of `RunUninstall` with `!testMode` so `--uninstall --purge-data --test-mode` empties all installed files inside `Sandbox\app` without deleting the required `Sandbox\app` directory container itself.
- **8-Digit `YYYYMMDD` Nightly Windows PE `FileVersion` Mapping (`Dev\scripts\build_installer.ps1`, `RemoteNodeDeploymentService.cs`)**:
  - Updated `build_installer.ps1` to map 8-digit `-nightly.YYYYMMDD` pre-release stamps to 4-digit `MMDD` (`<= 65534`) for Windows PE `/p:FileVersion` while preserving full SemVer `<X.Y.Z>-nightly.<YYYYMMDD>` in `Version`, `InformationalVersion`, and embedded installer metadata, and upgraded `CompareSemVer` numeric pre-release parsing to 64-bit `long`.

---

## [1.0.0-rc.9] - 2026-11-01

### Added
- **Distinct FIFO `"Up Next"` Pending Queue Engine & Manual `"Queue Next"` Dispatch (`PipelineOrchestratorService.cs`, `HousekeepingApiEndpoints.cs`, `TrayDaemonApplicationContext.cs`, `DashboardPage.tsx`, `TopCommandBar.tsx`, `JobQueuesPage.tsx`, `JobBuilderPage.tsx`)**:
  - Replaced unbounded pending queue lists with a bounded, distinct FIFO wait-list (`PendingRuns`, maximum 1 pending entry per distinct named queue or single job) exposed via `GET /api/v1/pipeline/pending-runs`, `DELETE /api/v1/pipeline/pending-runs/{itemKey}`, and `POST /api/v1/pipeline/pending-runs/clear`.
  - Enabled manual **`⏭ Queue Next`** queueing while a pipeline run, compliance scan, or `.hash` verifier task is active across the Top Command Bar, Dashboard, Named Job Queues, Job Builder, and System Tray—while rejecting duplicate or currently running targets (`HTTP 409 Conflict` / toast notification) so no queue or job is ever queued infinitely or consecutively.
  - Added live `"Up Next"` queue visibility on the Dashboard with per-item cancel (`✕`) and **`🗑 Clear Queued`** controls.
- **4-Pillar Self-Contained Single-EXE Windows Installer (`Housekeeping.Installer`, `Dev\scripts\build_installer.ps1`)**:
  - Added native `.NET 8` (`win-x64`) single-file self-contained installer (`Housekeeping-Setup-v<Version>-win-x64.exe`) embedding the compiled payload archive, cryptographically Authenticode-signed (`CN=StreamItAll Code Signing`), 100% free of PII/secrets/developer build paths, preserving canonical user state (`settings.json`, `history.json`, `compliance_reports.json`, `pending_swaps.json`, `dats\`, `backups\`, `logs\`, `tools\`) across upgrades/uninstalls, and supporting `--test-mode`, `--verify`, `--dry-run`, `--install-dir`, `--data-dir`, `--log-dir`, `--uninstall`, and `--purge-data`.

### Fixed
- **DST Fall-Back Ambiguous Local-Time Infinite 15-Second Re-Trigger Bug (`SchedulerAndNotificationService.cs`, `PipelineOrchestratorService.cs`)**:
  - Fixed `ShouldTriggerQueueNow` stripping `.NET`'s `s_kindLocalAmbiguous` bit via `DateTime.SpecifyKind(..., DateTimeKind.Local)` during the 1:00–2:00 AM Daylight Saving Time fall-back hour (which shifted `nowUtc` `+60 minutes` ahead of `DateTime.UtcNow` and re-triggered 60-minute interval queues every 15 seconds), and switched `EvaluateQueueSchedules()` to pass `DateTime.UtcNow` directly.
- **`LastRunUtc` Start + Completion Stamping, Stale Browser Config Save Protection & Explicit Stop Queue Clearing (`PipelineOrchestratorService.cs`, `ConfigService.cs`)**:
  - Stamped `NamedQueues[*].LastRunUtc` on **both actual execution start and completion** (preventing immediate back-to-back re-runs after waiting behind a long job) and guarded `ConfigService.TryUpdateAndSave` so stale browser/PWA `PUT /api/v1/config` payloads cannot overwrite newer server-owned `LastRunUtc` or `LastMasterScheduleRunUtc` timestamps.
  - Updated `StopActiveRun()` to clear all waiting `PendingRuns` and suppress `finally`-block auto-chaining when the user clicks **`⏹ Stop`**, plus added a `_isTransitioningPendingRun` handoff guard across `ExecutePipelineAsync`, `RunComplianceScanAsync`, `RunHashSidecarVerifierAsync`, and `EvaluateQueueSchedules`.

---

## [1.0.0-rc.8] - 2026-11-01

### Changed
- **Canonical 5-Folder Project Hierarchy Migration (`Archive\`, `Builds\`, `Dev\`, `Installers\`, `Sandbox\`)**:
  - Migrated the workspace from its legacy flat layout into the clean 5-folder container hierarchy (`zero` loose outer files), archiving pre-migration and legacy snapshots in `Archive\`, relocating active source/tests/docs into `Dev\`, isolating `Sandbox\{app,data,logs}` as a sibling to `Dev\`, and staging versioned release binaries in `Builds\v1.0.0-rc.8\`.
- **Full Manifest Synchronization & Production RC8 Cutover (`Directory.Build.props`, `version.txt`, `package.json`, `package-lock.json`)**:
  - Synchronized all backend and frontend manifests in lockstep to `1.0.0-rc.8`, verified `141 / 141` xUnit tests under `/warnaserror` (`0` warnings) and `0` binary PII/path leaks, re-verified contained live execution in `Sandbox\`, and deployed `1.0.0-rc.8` binaries + PWA assets to canonical production with atomic pre-deploy backup.

---

## [1.0.0-rc.7] - 2026-10-31

### Added
- **Unified Sandbox-by-Design CLI Parser, `--help`/`--version`, & Deterministic Anonymized Builds (`HousekeepingCliParser.cs`, `Directory.Build.props`, `README.md`, `MODULES_REFERENCE.md`)**:
  - Added `HousekeepingCliParser` across `Housekeeping.exe` (`AttachConsole` stdout support) and `Housekeeping.Server`, exposing `--data-dir`, `--config`, `--log-dir`, `--port`, `--localhost-only`, `--headless`, `--test-mode`, `--dry-run`, `--minimized`/`--silent`, `--watchdog`, `--version`/`-v`, and `--help`/`-h`/`/?`, plus environment variable fallbacks (`HOUSEKEEPING_DATA_DIR`, `DATA_DIR`, `HOUSEKEEPING_HOME`, `APP_HOME`, `HOUSEKEEPING_CONFIG`, `CONFIG_PATH`, `HOUSEKEEPING_LOG_DIR`, `LOG_DIR`, `HOUSEKEEPING_PORT`, `PORT`, `HOUSEKEEPING_TEST_MODE`, `TEST_MODE`, `HOUSEKEEPING_DRY_RUN`, `DRY_RUN`).
  - Added solution-wide `Directory.Build.props` unifying `<Version>1.0.0-rc.7</Version>`, `<Deterministic>true</Deterministic>`, `<ContinuousIntegrationBuild>` in Release, and `<PathMap>` source-path anonymization to eliminate loose `.pdb` sidecars and strip local developer build paths from Release binaries.
  - Authored comprehensive [`README.md`](README.md) matching `--help` 1:1 and updated [`MODULES_REFERENCE.md`](MODULES_REFERENCE.md) for the `.NET 8 Kestrel + React 19 PWA` architecture and all 8 modules (including Module 8 `Collection Manifest Auditor` and 9-column `CRC32`+`SHA-1` compliance workflows).

### Security
- **`RestrictRawShellToLocalhost` Remote Execution Surface Hardening (`HousekeepingApiEndpoints.cs`, `ConfigService.cs`)**:
  - Blocked non-loopback `POST /api/v1/profiles/import` and `POST /api/v1/config/snapshots/{id}/restore` requests while `RestrictRawShellToLocalhost` is active (`HTTP 403 Forbidden`), and expanded `HasRestrictedExecutionSurfaceChanged` on `PUT /api/v1/config` to inspect `NamedQueues[*].Jobs` (`CustomScripts`), `RestrictRawShellToLocalhost`, and all 5 CLI binary override paths (`HandBrakeCliPath`, `SevenZipPath`, `RclonePath`, `HbdPath`, `GreenroomRootPath`).
  - Hardened `ConfigService.RestoreSnapshot`, `ConfigService.ImportProfileFromJson`, and `ConfigService.ResetToCleanDefaults` to validate restored payloads atomically (`PersistCandidateLocked`) and preserve the active host's `AdminPasswordHash`, `AdminPasswordSalt`, and `NodeApiToken`.
- **Login Brute-Force Rate Limiting, Tailscale Serve `Secure` Session Cookies & Pure Log-Folder Isolation (`TieredAuthService.cs`, `HousekeepingApiEndpoints.cs`)**:
  - Added per-IP sliding-window failed-login throttling (`5` failed attempts within `60s` -> `30s` `HTTP 429 Too Many Requests` cooldown with `Retry-After` header).
  - Added `TieredAuthService.IsEffectiveHttps` so `hk_session` cookies issued behind loopback `tailscale serve` HTTPS proxies (`X-Forwarded-Proto: https`) carry the `Secure` attribute, and relocated newly generated `.hk_cookie_secret.key` files to `DataDirectory` (with read-fallback from `ActiveLogsDir`) so `--log-dir` remains strictly pure log files.

### Fixed
- **Node-Scoped SignalR Real-Time Groups (`HousekeepingHub.cs`, `RealtimeBroadcastHostedService.cs`)**:
  - Enrolled SignalR hub connections into node-scoped groups (`node:local` vs `node:<targetNodeId>`, plus dynamic `SubscribeToNode(nodeId)`) and scoped local telemetry/pipeline/log broadcasts exclusively to `node:local`, preventing local host `2s` ticks from overwriting remote Target Node workspace state while relaying live `2s` remote node status to active remote subscribers.
- **Strict `--data-dir`, `--config`, `--log-dir`, `--test-mode`, `--dry-run` Containment & Zero Hardcoded Host Paths (`ConfigService.cs`, `AppConfig.cs`, `GreenroomSyncIntegrationService.cs`, `PipelineOrchestratorService.cs`, `HousekeepingApiEndpoints.cs`, `SystemPerformanceTelemetryService.cs`, `CollectionManifestModule.cs`, `RomLauncherGeneratorModule.cs`, `Program.cs`, `DisposableWebViewForm.cs`, `TrayDaemonApplicationContext.cs`)**:
  - Removed hardcoded local host `Greenroom` root defaults in favor of dynamic `GreenroomRootPath` / `GREENROOM_ROOT` / relative sibling resolution (scoped inside `DataDirectory` in `--test-mode`).
  - Added `--test-mode` host-isolation guards suppressing Windows Registry startup mutations, Task Scheduler `schtasks.exe` writes, Tailscale Serve mutations, and post-run OS sleep/shutdown actions (`simulated: true`), plus `PipelineOrchestratorService.ForceDryRun` (`--dry-run`) forcing all pipeline dispatches to `IsDryRun = true`.
  - Routed `telemetry_heartbeat.json` exclusively to `ConfigService.ActiveLogsDir`, scoped default `tools/README_TOOLS.txt`, `dats`, `compliance_reports.json`, WebView2 user-data caches, and named single-instance mutexes under sandbox directories when sandbox flags are active (guaranteeing `0` writes into `AppDomain.CurrentDomain.BaseDirectory`).
- **Module 8 Per-Module Custom Schedule Evaluation & `IsDryRun` Non-Destructive Parity (`SchedulerAndNotificationService.cs`, `CollectionManifestModule.cs`)**:
  - Added `CheckModule(ModuleId.CollectionManifest, cfg.CollectionManifest, now, due)` to `GetPerModuleSchedulesDueNow()` so custom per-module schedules on Module 8 (`Collection Manifest Auditor`) evaluate alongside Modules 1–7.
  - Added strict `ctx.IsDryRun` guards in `CollectionManifestModule.ExecuteAsync` so dry-run executions perform full file discovery and cryptographic audit simulation while suppressing online DAT downloads, `.hash` sidecar writes/7z injections, CSV/JSON/HTML manifest overwrites, and secondary mirror copies.
- **Thread-Safe Copy-On-Write Config Mutations, Disk-Write Error Propagation & Zero Silent `catch` Blocks (`ConfigService.cs`, `HousekeepingApiEndpoints.cs`, `PipelineOrchestratorService.cs`, `ModuleCatalogService.cs`, `ReportsCenterPage.tsx`, `SettingsPage.tsx`)**:
  - Added `ConfigService.MutateAndSave` and `ConfigService.TrySave(out string? ioError)`, eliminating concurrent `List<T>` mutation races on `DefinedJobs` / `NamedQueues` and propagating disk-write failures (`TryUpdateAndSave` rolls back in-memory state and surfaces I/O errors instead of falsely returning `ok: true`).
  - Eliminated all `219` silent empty `catch` blocks across C# and HTML with structured diagnostic trace/warning logging (`0` `ERR-001` findings) and added explicit `.catch(...)` toast/error handlers to all Promise calls in `ReportsCenterPage.tsx` and `SettingsPage.tsx`.
- **SemVer Pre-Release Fleet Eligibility & Remote `WebServer` Identity Preservation (`RemoteNodeDeploymentService.cs`)**:
  - Added SemVer 2.0.0 pre-release comparison (`CompareSemVer`) so RC-to-RC (`1.0.0-rc.5` -> `1.0.0-rc.7`) and RC-to-GA fleet updates evaluate `CanUpdateApp = true`, and preserved remote nodes' existing `WebServer` (`Port`, `NodeApiToken`, `AdminPasswordHash`) and `RemoteNodes` blocks during `PushSettingsToRemoteAsync`.
- **Mobile `< 768px` Pre-Hydration `Touch` (`44px`) Density & Unified `"Housekeeping"` Branding (`index.html`, `AppStateContext.tsx`, `manifest.webmanifest`, `DisposableWebViewForm.cs`)**:
  - Updated `index.html` pre-hydration bootstrap and `AppStateContext.tsx` `readInitialDensity()` to default first-time mobile viewports (`max-width: 767px`) to `'touch'` density without layout flash, and unified HTML `<title>`, PWA `manifest.webmanifest` `"name"`, and native `DisposableWebViewForm` window title to `"Housekeeping"`.

### Changed
- **I/O & Socket Resource Efficiency (`HousekeepingRuntimeContainer.cs`, `TerminalLogBufferService.cs`, `SystemPerformanceTelemetryService.cs`, `TailscalePwaService.cs`)**:
  - Replaced per-request `new HttpClient()` allocations across fleet endpoints with a shared pooled `FleetHttpClient`, switched `TerminalLogBufferService` to a persistent `FileShare.ReadWrite` `StreamWriter` (eliminating per-line NTFS file open/stat/close syscalls inside `_lock`), throttled idle `telemetry_heartbeat.json` writes to `15s` (`92%` fewer idle disk writes), hardened `TailscalePwaService` child `Process` disposal/timeout cleanup, and purged `~164 MB` of legacy workspace clutter across Dev and Prod.

---

## [1.0.0-rc.6] - 2026-10-31

### Fixed
- **Desktop Modules Catalog & Multi-Column Grid Card Alignment (`theme.css`, `ModulesCatalogPage.tsx`)**:
  - **Pill Card-Border Containment**: Constrained `.hk-card .hk-pill` (`max-inline-size: 100%; min-inline-size: 0; white-space: normal; overflow-wrap: break-word`) and `.hk-row` (`min-inline-size: 0`) so long tool readiness badges (such as `✓ Windows Command Prompt or PowerShell (built into Windows) (Built-in / Ready)` on the `Custom Tasks` card) wrap cleanly inside the `420px` module card instead of overflowing `49px` past the right card border.
  - **Side-by-Side Grid Card Vertical Alignment**: Reset `margin-top: 0` on direct `.hk-card` children of `.hk-grid-2`, `.hk-grid-3`, `.hk-grid-4`, and `.hk-builder-layout` so `.hk-card + .hk-card` no longer offsets the second card down by `10–12px` inside CSS Grids (aligning `✨ Core Capabilities` and `🧭 Quick Setup Guide` in **Modules**, `🌐 Alternate Language Edition Rules` in **Job Builder → Media Transcoder**, and the right inspector panes in **Reports** and **Archive Templates**).
  - **Sticky Desktop Module Detail Pane**: Added `@media (min-width: 1025px)` `position: sticky; inset-block-start: 0;` on `.hk-builder-layout.modules-split > .hk-card` so scrolling down the 8-module catalog to select bottom modules (`Custom Tasks`, `Collection Manifest`) keeps the selected module's header, action buttons, and documentation pinned in view.

---

## [1.0.0-rc.5] - 2026-10-31

### Changed
- **Mobile Browser & Standalone Mobile PWA Layout Overhaul (`theme.css`, `index.html`, `TopCommandBar.tsx`, `GlobalTerminalDrawer.tsx`, `DualPaneTerminal.tsx`, `DashboardPage.tsx`, `JobBuilderPage.tsx`, `JobQueuesPage.tsx`, `RomLauncherGeneratorEditor.tsx`, `ReportsCenterPage.tsx`, `ModulesCatalogPage.tsx`, `ArchiveTemplateEditorDialog.tsx`, `HistoryPage.tsx`, `App.tsx`)**:
  - **100% Desktop UI Preservation**: Scoped all mobile layout adaptations to `<= 768px` (and responsive grid track containment at `<= 1024px`) so desktop workspaces (`> 1024px`) remain visually untouched.
  - **Compact 2-Row Mobile Topbar & Expandable Controls Drawer**: Replaced the 7-row wrapped mobile header (~42% of phone screen height) with a compact 2-row sticky header (Brand + Version + 1-line schedule + 1-tap Save + contextual `▶ Run` / `⏸` / `⏹` + `☰` Controls drawer toggle + single-row touch-scrollable nav strip) with `viewport-fit=cover` safe-area inset padding. Added a **`💾 Drives (N)`** button inside the mobile controls drawer and `stack-mobile` card layout on the Drives modal so local and mapped NAS drives (`V:`–`Z:`) are inspectable on mobile.
  - **2-Line Stacked Mobile Status Footer**: Eliminated `54vw` status truncation on mobile by stacking full-width active pipeline status + percentage badge + slim progress bar above a compact second row (`Verbosity` selector + real-time `Done • Skip • Err` session totals + touch `▲ Terminal` toggle without desktop `(Ctrl+\`)` hints).
  - **Mobile Terminal & Interactive CLI Ergonomics**: Shortened the mobile CLI working-directory prompt to its leaf folder (`…\Housekeeping>`) so the interactive `<input>` receives ~75% of horizontal width on phones, enabled log word-wrap by default on viewports `<= 768px` (toggleable via `Wrap`), tightened mobile log tag/timestamp spacing, and collapsed side-by-side split mode cleanly on narrow viewports.
  - **2-Column Bento Dashboard KPIs & Mobile Job/Queue Quick Switchers**: Converted mobile Dashboard KPIs into a compact 2-column Bento grid with 2-line clamped pipeline status text (`-webkit-line-clamp: 2`), and added a 1-row quick switcher dropdown + collapsible list toggle (`📂 Manage (N)`) at the top of **Job Builder** and **Job Queues** on mobile so editors appear immediately without scrolling past full repositories.

### Fixed
- **CSS Grid `1fr` (`minmax(auto, 1fr)`) Horizontal Blowout & Container-Query Table Stacking (`theme.css`, `RomLauncherGeneratorEditor.tsx`)**:
  - Replaced bare `1fr` responsive grid tracks and inline `gridTemplateColumns` overrides with `minmax(0, 1fr)` + `min-inline-size: 0` across `.hk-builder-layout` (`.templates-split`, `.reports-split`, `.modules-split`), `.hk-grid-*`, `.hk-card`, `.hk-field`, and `.hk-path-picker .hk-input`. Long Windows paths, `<textarea>` previews, and wide tables no longer blow out card borders or clip `📁 Browse...` / `+ Add Sync Row` off-screen, and `.table-wrapper` (`container-type: inline-size`) now accurately measures `< 640px` to trigger `.hk-table.stack-mobile` card layouts (including Console Collection rules and Save/State sync rows).

---

## [1.0.0-rc.4] - 2026-10-31

### Added
- **Tailscale Serve HTTPS Provisioner & In-App Mobile PWA Install UX (`TailscalePwaService.cs`, `HousekeepingApiEndpoints.cs`, `TopCommandBar.tsx`, `SettingsPage.tsx`)**:
  - Added `/api/v1/system/tailscale-pwa` (`GET`, `POST /enable`, `POST /disable`) backed by `TailscalePwaService`, inspecting local Tailscale MagicDNS / HTTPS certificate readiness and provisioning `tailscale serve --bg` (`https://<machine>.<tailnet>.ts.net` -> `http://127.0.0.1:5178`) without hijacking raw LAN/fleet HTTP `:5178`. Captures and monitors one-time Tailscale Admin Console Serve/HTTPS authorization URLs automatically in the background when Serve is not yet enabled on the tailnet.
  - Added a **`📱 Mobile PWA & Tailscale Serve HTTPS`** management card under **Settings → Web & Auth**, plus automatic **`📲 Install App`** (`beforeinstallprompt`) and **`🔒 Switch to HTTPS (PWA)`** / **`📱 Setup HTTPS PWA`** helpers in `TopCommandBar.tsx`.

### Fixed
- **Android WebAPK & iOS Standalone PWA Installability (`manifest.webmanifest`, `sw.js`, `index.html`, `main.tsx`, `HousekeepingServerHost.cs`)**:
  - Fixed a manifest icon dimension mismatch in `manifest.webmanifest` where `/app_icon.png` (`512x512`) was declared as `"sizes": "256x256"` with no `192x192` or `512x512` entries, causing Android Chrome's WebAPK validator to reject PWA installation.
  - Generated dedicated crisp `192x192` and `512x512` PNG icons for `"purpose": "any"` (`icon-192.png`, `icon-512.png`), full-bleed adaptive-safe-zone icons for `"purpose": "maskable"` (`icon-maskable-192.png`, `icon-maskable-512.png`), and `180x180` `apple-touch-icon.png`, and added `"id": "/"` + `"scope": "/"` to `manifest.webmanifest`.
  - Added a network-first Service Worker (`/sw.js`, registered in secure contexts via `main.tsx`) that strictly bypasses `/api/*` and `/hubs/*` (zero stale API/SignalR caching) while providing offline shell resilience and satisfying Service Worker checks across Chrome, Samsung Internet, Edge, and Brave on Android. Configured `Cache-Control: no-cache, must-revalidate` and `Service-Worker-Allowed: /` in `HousekeepingServerHost.cs`.

---

## [1.0.0-rc.3] - 2026-10-31

### Added
- **Mid-Shutdown Interrupted Run Persistence, Recovery Banner & Auto-Restart on Startup (`PipelineOrchestratorService.cs`, `HousekeepingApiEndpoints.cs`, `AppConfig.cs`, `App.tsx`, `SettingsPage.tsx`)**:
  - Persists an atomic `interrupted_run_checkpoint.json` checkpoint whenever a Queue, Single Job, `.hash Integrity Verifier`, or `Archive Compliance Scan` is running (tracking completed job IDs, active job/progress/status, dry-run/resync flags, and FIFO pending scheduled queues).
  - Preserves the checkpoint across graceful shutdown (`Dispose()`), update deployments, reboots, and abrupt process termination, clearing it only on normal completion, explicit user **`⏹ Stop`**, or **`✕ Dismiss`**.
  - Added a top-of-workspace **Interrupted Mid-Shutdown Run Recovery Banner** across all tabs with **`▶ Resume Remaining Jobs`** / **`▶ Restart Interrupted Run`**, **`↺ Restart Entire Queue`** (for multi-job queues with completed prior steps), **`Auto-restart on startup`** toggle (`AppConfig.AutoRestartInterruptedRunsOnStartup`, also configurable in **Settings → Watchdog & Process Guard**), and **`✕ Dismiss`**.

### Fixed
- **Reactive Fixed-Size Dashboard Terminal & Always-Visible Interactive CLI Input (`theme.css`, `DualPaneTerminal.tsx`, `GlobalTerminalDrawer.tsx`)**:
  - Assigned definite viewport-reactive block sizes to `.hk-terminal-surface` (`clamp(320px, calc(100dvh - 430px), 580px)` in Dashboard single/side-split mode, `clamp(230px, calc((100dvh - 440px) / 2), 340px)` in stacked split mode, and `flex: 1 1 0; block-size: 100%; min-block-size: 0` inside `.hk-global-drawer`) with `flex: 1 1 0; min-block-size: 0; overflow: auto; scrollbar-gutter: stable; overscroll-behavior: contain;` on `.hk-terminal-viewport`, preventing virtualized log buffers from growing vertically off the bottom of the webpage or clipping `.hk-terminal-cli` inside the `Ctrl+\`` drawer.
  - Guaranteed the interactive `.hk-terminal-cli` bar (`flex: 0 0 auto`) is always visible: automatically attaches to whichever split pane is viewing `local`, and renders an inline **`🖥 Switch to Local CLI`** action bar when viewing remote-only log tails so the CLI input never disappears.
- **Restored Mapped Network / NAS Drives Alongside Local Drives in Top Command Bar (`SystemPerformanceTelemetryService.cs`, `AppConfig.cs`, `HousekeepingApiEndpoints.cs`, `TopCommandBar.tsx`)**:
  - Added `SystemPerformanceTelemetryService.ScanLocalAndNetworkDrivesBoundedAsync`, combining `DriveInfo.GetDrives()` with persistent `HKCU\Network` mapped NAS drive letters (`V:`–`Z:`, etc.) probed asynchronously every `20s` behind `IsUncHostReachableFastAsync` (`900 ms` TCP `:445`) and `RunBoundedUncOperationAsync` (`1.2s` single-flight wall-clock cap + `20s` offline cooldown). Online NAS shares render live free space alongside local drives while unreachable mapped drives render `🔴 X: !` without ever stalling telemetry.
- **Real-Time Bottom-Bar `Session Totals` (`Processed | Skipped | Errors`) Across All Jobs & Verifiers (`PipelineOrchestratorService.cs`, `HashSidecarService.cs`, `ArchiveComplianceService.cs`, `RealtimeBroadcastHostedService.cs`, `AppStateContext.tsx`)**:
  - Wired `ctx.Stats.OnStatsDelta` to trigger `ThrottledStateBroadcast()` (`<= 120 ms` coalesced `PipelineState` push) during active job execution, added per-target live `onStatsDelta` updates to `.hash Integrity Verifier` and `Archive Compliance Scan`, and included `SessionProcessed`, `SessionSkipped`, `SessionErrors`, and `FooterStatusText` in the 2-second `TelemetrySnapshot` SignalR tick.

---

## [1.0.0-rc.2] - 2026-10-31

### Added
- **Global `Ctrl+S` / `Cmd+S` Force Save & Explicit `💾 Save` Buttons (`AppStateContext.tsx`, `TopCommandBar.tsx`, `JobBuilderPage.tsx`)**:
  - Added global `Ctrl+S` / `Cmd+S` keyboard shortcut and explicit **`💾 Save`** / **`💾 Save (Ctrl+S)`** buttons in both the Top Command Bar and the Job Builder header to force an immediate `settings.json` write with toast confirmation at any time.

### Fixed
- **Offline Remote Node SMB Kernel Hang & 6-Socket HTTP/1.1 Connection Pool Starvation (`RemoteNodeDeploymentService.cs`, `HousekeepingApiEndpoints.cs`, `DualPaneTerminal.tsx`)**:
  - Fixed a severe hang where `DualPaneTerminal` Pane 2 polling a configured remote node (`\\SERVER\...`) with `setInterval(..., 2000)` spawned overlapping `Directory.Exists` calls on an offline SMB share (`45–60s` Win32 `mrxsmb.sys` kernel block), exhausting all 6 browser HTTP/1.1 sockets to `127.0.0.1:5178` and starving `PUT /api/v1/config`, `POST /api/v1/terminal/exec` (`echo`), and `POST /api/v1/hash-verifier/run`.
  - Added `RemoteNodeDeploymentService.IsUncHostReachableFastAsync` (`1.2s` non-blocking TCP `:445` pre-flight) and `RemoteNodeDeploymentService.RunBoundedUncOperationAsync` (`1.8s` single-flight wall-clock `.WaitAsync` cap + `20s` cooldown cache for stalled Tailscale/SMB sessions where TCP `:445` connects yet `mrxsmb.sys` blocks), and switched `DualPaneTerminal` to a non-overlapping sequential `setTimeout` loop with `AbortController` timeout and `10s` offline backoff.
- **Revision-Safe Single-Flight Configuration Save Queue (`AppStateContext.tsx`, `RomLauncherGeneratorEditor.tsx`)**:
  - Replaced boolean `dirtyConfigRef` with monotonic `editRevisionRef` / `savedRevisionRef` and a serialized single-flight `PUT /api/v1/config` queue (`450ms` debounce + `beforeunload` guard) so rapid multi-row edits (such as selecting Archive Compliance Templates across 18 ROM Console rules) never race with an in-flight save or drop trailing changes. Aligned starter console preset template IDs (`official-folder-dump`, `community-modern-disc`).
- **Instant Interactive CLI Output & Non-Blocking Background `.hash` Verifier / Compliance Scanner (`InteractiveCliSessionService.cs`, `PipelineOrchestratorService.cs`, `HousekeepingApiEndpoints.cs`, `RomLauncherGeneratorEditor.tsx`, `CollectionManifestEditor.tsx`, `ReportsCenterPage.tsx`)**:
  - Interactive CLI commands (`echo`, `dir`, etc.) now close redirected `StandardInput` immediately, emit `"CLI"`-tagged entries that bypass pipeline verbosity hiding, and return `entries` directly in the `POST /api/v1/terminal/exec` JSON response (deduplicated by `entry.Id` with SignalR batches) so output renders immediately and `cliRunning` never sticks.
  - **`🔍 Verify & Heal .hash Files`** and **`🔍 Scan Compliance Now`** now launch asynchronously via `StartHashSidecarVerifier` / `StartComplianceScan` (`< 15 ms` HTTP response), automatically open the Global Terminal Drawer (`Ctrl+\``), stream live `[X/Y]` percentage progress, and support 1-click **`⏹ Stop Verifier`** cancellation. Fixed `SidecarVerifyMode` enum value alignment (`DeepCryptographicAndHeal` / `DryRunReportOnly` + backward-compatible alias parsing) between the frontend dropdowns and `POST /api/v1/hash-verifier/run`.

---

## [1.0.0-rc.1] - 2026-10-31

### Changed
- **Unified Brand Rename to `Housekeeping` & Graduation Out of `2.x` Beta into `1.0.0-rc.1`**:
  - Renamed the application display brand across the web/desktop UI, System Tray daemon, PWA manifest, native dialogs, terminal banners, HTML audit reports, and Discord notifications from `Housekeeping 2.0` / `Housekeeping 3.0` to **`Housekeeping`**.
  - Graduated from the `2.x` beta series to **`1.0.0-rc.1`** across all project manifests (`Housekeeping.Core`, `Housekeeping.Server`, `Housekeeping.DesktopHost`, `Housekeeping.Web`, and `version.txt`), with remote fleet version eligibility supporting seamless 1-click upgrades from legacy `2.8.x` beta nodes.

### Added
- **Four-Project Always-On Engine + Dual-Mode Web/Desktop Architecture (`Housekeeping.Core`, `Housekeeping.Server`, `Housekeeping.Web`, `Housekeeping.DesktopHost`)**:
  - Decoupled all 8 automation modules, 3-topology hashing (`SingleFileRom`, `MultiFileRomSet`, `CompositeFolderDump`), Fast 7z Header `CRC32` + `SHA-1` verification, Redump/No-Intro DAT correlation, Archive Structure Compliance Templates, guarded `7z d` extra-file cleanup, atomic pending swaps, and network-drop safety into pure, UI-free **`Housekeeping.Core`** (`net8.0-windows`).
  - Added **`Housekeeping.Server`** (`ASP.NET Core Kestrel` + `SignalR` `/hubs/housekeeping`, default `0.0.0.0:5178`) serving 12 `/api/v1/*` REST endpoint groups and real-time coalesced SignalR streams (`50 ms` terminal log batches, `100 ms` subprocess progress, `2 s` telemetry/drives/global-next-run, pipeline state transitions, and compliance KPI updates).
  - Added **`Housekeeping.DesktopHost`** (`Housekeeping.exe`), a single-executable Windows System Tray daemon hosting Kestrel in-process in the user logon session (preserving mapped NAS drives `V:\`–`Z:\` and `WindowsWatchdogService` Task Scheduler compatibility) with **Dual UI Launch Modes**: open in any **Browser (`http://localhost:5178`)** or launch a native dark-titlebar **`DisposableWebViewForm` (`WebView2`)** whose close button (`X`) disposes `WebView2` completely (freeing 100% of `msedgewebview2.exe` Chromium memory to `0 MB`) while the background engine keeps running until explicitly shut down from the System Tray (`⏻ Shut Down Housekeeping Engine`). Supports `--minimized`, `--watchdog`, `--headless`, `--port`, `--data-dir`, and `--localhost-only`.
- **Tiered Loopback / LAN / Tailnet Authentication & Raw Shell Governance (`TieredAuthService.cs`, `TieredAuthMiddleware.cs`)**:
  - Zero-friction auto-trust on direct `127.0.0.1` / `::1` loopback connections, optional `Tailscale-User-Login` identity header trust for `tailscale serve` loopback proxies, PBKDF2-SHA256 admin password setup/login with 30-day HMAC-SHA256 signed `HttpOnly; SameSite=Lax` `hk_session` cookies for LAN/Tailnet/Remote browsers, `X-Housekeeping-Token` / Bearer fleet API authentication, and an opt-in `RestrictRawShellToLocalhost` security boundary blocking remote `/api/v1/terminal/exec` and `CustomScripts` modifications (`403 Forbidden`).
- **Unified Top-Bar Multi-Node Fleet Proxy (`RemoteNodeProxyMiddleware.cs`, `TopCommandBar.tsx`)**:
  - Switch the active Target Node (`Local Machine` vs. any configured LAN/Tailnet Remote Node) directly in the Top Bar; `RemoteNodeProxyMiddleware` transparently forwards `/api/v1/*` requests carrying `X-Target-Node-Id` authenticated with the target node's `ApiToken`, while preserving SMB fallback compatibility for legacy `2.8.x` nodes.
- **Debounced Auto-Save, Rolling 20-Snapshot `settings.json` History & Immutable Run-Start Snapshots (`ConfigService.cs`, `PipelineOrchestratorService.cs`, `SnapshotHistoryDialog.tsx`)**:
  - Configuration edits validate regexes and job-chaining cycles pre-commit and auto-save ~900 ms after settling with a live `"✓ Saved"` / `"⟳ Auto-Saving..."` / `"⚠ Validation Error"` Top-Bar indicator, backed by 20 deduplicated timestamped `backups/config_snapshots/` rollback points and 1-click restore.
  - `PipelineOrchestratorService` clones `AppConfig` into an immutable run-start snapshot so mid-run UI auto-saves never mutate or corrupt active background pipelines.
- **General Post-Job Chaining (`OnSuccessTriggerJobId`) & Greenroom Companion (`:8420`) Post-Manifest Auto-Sync (`PipelineOrchestratorService.cs`, `GreenroomSyncIntegrationService.cs`)**:
  - Added `HousekeepingJob.OnSuccessTriggerJobId` with static + runtime visited-set cycle guards so any job can automatically trigger a follow-up job upon completion.
  - Added automatic non-blocking Greenroom Companion (`POST http://127.0.0.1:8420/api/tasks/run` for `collect_library` + `verify_hashes` / `collect_plex`, with `run_greenroom.bat` CLI fallback) triggered **only when ROM or Collection manifest files actually changed** during a non-dry-run job, plus a 1-click **`🟢 Greenroom`** Top-Bar shortcut resolving `http://<current-browser-hostname>:8420`.
- **Sister-App React 19 + TypeScript + Vite SPA & Adaptive Desktop/Mobile PWA (`Housekeeping.Web`)**:
  - Unified Sister-App design system matching Greenroom Companion (`light-dark()` Dark `#090e17`/`#131c2e` & Light `#f3f5f8`/`#ffffff` palettes, Housekeeping Sky-Blue `#38bdf8` / `#0284c7` accent, `Compact` / `Comfortable` / `Touch` UI density switcher, native `<dialog>` top-layer modals, `@container` responsive mobile tables, `content-visibility: auto` deferred sections, and `manifest.webmanifest` PWA support).
  - Full feature parity and UX upgrades across all **7 Operational Tabs** (`Dashboard`, `Job Queues`, `Job Builder` for all 8 Modules including live C#-backed `.NET Regex` Playground & multi-row Pre/Post-Launch sync preview, `Modules`, `🛡 Reports`, `Settings`, `History`), **Global ``Ctrl+` `` Collapsible Terminal Drawer**, and **Hybrid Dual-Mode Path Picker** (`StaNativeDialogBridge` 1-click Windows OS dialog on localhost + `ServerFolderPickerDialog` for `C:`–`Z:` & `\\NAS\...` over Tailnet/mobile).

### Fixed
- **Permanent Elimination of WPF UI Thread Freezes, DataGrid Vertical Glyph Clipping & Multi-Click Input Lag**:
  - Replaced the retired single-threaded WPF `DataGrid` / `Dispatcher` rendering layer with hardware-accelerated CSS Flexbox/Grid controls (`line-height: 1.35`, density-aware min-heights, 1-click `ComboSelect` & inputs, and 60fps coalesced terminal rendering), permanently resolving clipped input/dropdown text, unresponsive windows during NAS probes or heavy log bursts, and 2–3 click cell focus delays.

---

## [2.8.5-rc.5] - 2026-10-31

### Added
- **9-Column `collection_manifest.csv` with `Verified Local CRC32` Beside `SHA-1` & `Archive Compliance` (`RomManifestCsvService.cs`, `HashSidecarService.cs`, `NoIntroRedumpDatParser.cs`, `CollectionManifestModule.cs`)**:
  - Added `Verified Local CRC32` (8-hex uppercase) immediately beside `Verified Local Hash` (`SHA-1`) plus `Archive Compliance` in a 9-column `collection_manifest.csv` (`Game Title,Proper Redump/No-Intro Name,Archive File Name,ROM / Folder Name,Verified Local CRC32,Verified Local Hash,Match Status,Archive Compliance,Notes`), with full header-aware backward compatibility for existing 7-column and 8-column manifests.
  - Added zero-decompression `CRC32` auto-backfill on cached `SHA-1` CSV rows by reading `.hash` sidecars, `< 1 ms` in-process `.zip` Central Directory headers (`ZipFile.OpenRead`), or `7z l -slt` headers without re-extracting archives.
  - Added **Timestamp-Proof Archive Header `CRC32` Cache Validation**: when an archive's `LastWriteTimeUtc` changes (e.g., touched, moved across NAS shares, or repacked) without modifying inner ROM bytes, Housekeeping verifies the `7z`/`.zip` header `CRC32` + `Size` against the cached `VerifiedLocalCrc32` and reuses the verified `SHA-1` without decompressing gigabytes.
  - Added **`⚡ Fast 7z Header CRC32 Audit`** mode (`UseFastArchiveHeaderCrc32Audit`) in Module 4 and fast archive header `CRC32` path in Module 8, plus `CRC32` + `Size` fallback matching (`MATCH (CRC32)`) in `NoIntroRedumpDatParser` for rapid multi-terabyte collection audits and `CRC32`-only DAT sets.
- **Community & Official (Redump / No-Intro) Archive Structure Templates Engine (`ArchiveComplianceModels.cs`, `ArchiveComplianceService.cs`, `ArchiveTemplateEditorWindow.xaml`)**:
  - Shipped 7 built-in **Community** and **Official (Redump / No-Intro)** Archive Structure Templates (`auto-community`, `community-cartridge-7z`, `community-modern-disc` for `.chd`/`.rvz`, `community-folder-dump` for PS3/Wii U/3DS trees, `official-nointro-cartridge`, `official-redump-cd` for `.cue`+`.bin`, and `official-redump-dvd` for `.iso`) plus custom/cloned template editing via **`ArchiveTemplateEditorWindow`** and per-console `Archive Template` assignment in Module 4.
  - Zero-decompression archive compliance inspection (`EvaluateTargetComplianceAsync`, `ScanConsoleCollectionAsync`, `ReadSmallArchiveTextEntryAsync`) detects extra non-rule scene clutter (`.nfo`, `.txt`, `.url`, `.jpg`, `.sfv`, `PS3_UPDATE/PS3UPDAT.PUP`), multi-ROM packs, wrong inner/outer formats, nested archives, disallowed wrapper folders, missing `.cue`/`.gdi` descriptors, broken `.cue`/`.gdi`/`.m3u` track references, orphan `.bin` tracks, missing PS3/Wii U boot anchors (`PARAM.SFO`, `EBOOT.BIN`, `meta.xml`, `code/*.rpx`), and cross-archive multi-disc sequence gaps (`Disc 1..N`).
- **Top-Level `🛡 Reports` Center & Guarded `7z d` Extra-File Cleanup (`MainWindow.xaml`, `MainWindow.xaml.cs`, `ComplianceReportService.cs`, `ArchiveComplianceService.cs`)**:
  - Added top-level **`🛡 Reports`** tab (`TabReports`) with live open-issue badge count, KPI summary cards, Console/Category/Status/Search filtering, persistent atomic `compliance_reports.json` storage, CSV export, `.hash` verifier findings integration, and an interactive inner-archive file tree inspector.
  - Added **Dry-Run Preview** (`BuildCleanupDryRunPreview`) and **Guarded `7z d` Extra-File Cleanup** (`CleanExtraFilesFromArchiveAsync` / `DeleteArchiveInternalPathsAsync`) that removes checked extra non-rule bloat files in-place from `.7z`/`.zip` archives while strictly protecting primary ROM payloads, referenced disc tracks/descriptors, and `.hash` sidecars.

### Fixed
- **WPF UI Thread Hangs, High-Rate Terminal Log Floods, DataGrid Text Clipping & Multi-Click Cell Activation (`SystemPerformanceTelemetryService.cs`, `SchedulerAndNotificationService.cs`, `DependencyCheckService.cs`, `App.xaml`, `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Eliminated UI freezes caused by `DriveInfo.IsReady` probing offline SMB network drives (`H:`, `I:`), synchronous `rclone listremotes` process spawns on the UI thread, 2-second UI-thread `Process.GetProcessesByName` polling (`GetCachedGuardedProcessRunning`), synchronous NAS `Directory.Exists` checks on button clicks, and unbatched per-line `AppendLog` `Dispatcher.BeginInvoke` + `ScrollIntoView` floods (replaced with `ConcurrentQueue` background batch flushing).
  - Fixed vertically clipped `ComboBox` and `TextBox` text inside `DataGrid` rows (`MinRowHeight="38"` / `"40"`, `PART_EditableTextBox` zero-clipping padding) and added global 1-click `DataGridCell` edit/button/checkbox/dropdown activation (`OnDataGridCellPreviewMouseLeftButtonDown`).
- **Pre-Launch & Post-Launch Multi-Row Save Sync Self-Poisoning (`AppConfig.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Fixed a bug where displaying the auto-generated `rclone` preview in `TxtRomPreLaunchCmd` / `TxtRomPostLaunchCmd` wrote the preview string back into `PreLaunchCommand` / `PostLaunchCommand`, short-circuiting `ResolveAllSyncCommands` to a single line and visually truncating row 2..N commands. Added explicit `UseCustomPrePostLaunchCommands` (`Override with Custom CLI Script`), `SanitizeAutoGeneratedCommandOverrides()`, and multi-line read-only previews displaying all `1..N` enabled sync commands (`1` per line).

---

## [2.8.5-rc.4] - 2026-10-31

### Added
- **Three-Topology Release Classification for Multi-File ROM Sets & Huge Console Folder Dumps (`ReleaseStructureDetector.cs`, `HashSidecarService.cs`)**:
  - Classified both disk directories and `.7z` / `.zip` / `.rar` archives into three topologies: `SingleFileRom`, `MultiFileRomSet` (PS1, PS2 CD, Sega CD, Saturn, Dreamcast multi-`.bin`/`.cue`/`.gdi`, and multi-disc `Disc 1..N` + `.m3u` sets), and `CompositeFolderDump` (PS3 `PS3_GAME`/`PS3_DISC.SFB`, PS4 `sce_sys/param.sfo`, PS5 `sce_sys/param.json`, Xbox 360/Xbox `default.xex`/`default.xbe`, Wii U `code`/`content`/`meta`, Switch/3DS `romfs`/`exefs`, `BDMV`, `VIDEO_TS`, and `>100`-file installed game trees).
  - Added `IsCohesiveMultiFileGameFolder` and `DiscoverDirectoryGameTargets` so unpacked multi-bin and multi-disc game folders are audited as a single cohesive game unit with one `<GameFolder>.hash` placed beside the folder, while organizational folders (`A`–`Z`, `USA`, `[BIOS]`, or folders containing separate games/archives) continue recursing per game.
- **Full Multi-File Redump Hashing & Incomplete Sidecar Self-Healing (`HashSidecarService.cs`, `MainWindow.xaml.cs`)**:
  - Added `ParsedSidecarDocument`, `TryParseSidecarDocument`, `HashArchivePayloadFilesAsync`, and `HashMultiFileGameFolderAsync` to compute and verify full Redump/No-Intro checksums (`SIZE`, `CRC32`, `MD5`, `SHA-1`) across every `.bin`, `.cue`, `.gdi`, `.iso`, and `.m3u` payload file inside multi-track and multi-disc sets, while preserving 100% of already-valid single-file RC3 `.hash` sidecars with zero wasted re-hashing.
  - Added `SidecarVerificationStatus.IncompleteMultiFileSet` (`IncompleteMultiFileCount` in verification reports) to detect partial 1-file `.hash` sidecars on multi-track/multi-disc archives and upgrade them in a single extraction pass (reusing already header-CRC-verified tracks via `@listfile` delta extraction on `.7z` archives and streaming `.zip` entries in memory).
- **Scene `.sfv` / 7-Zip `CRC32` Composite Folder Manifests for Loose & Archived Console Dumps (`HashSidecarService.cs`)**:
  - Added `BuildCompositeHashFromArchiveEntries` and upgraded `ComputeFolderCompositeHashAsync` / `GenerateSidecarContentForFolder` to record per-file `ENTRY: <RelativePath> | SIZE: <Bytes> | CRC32: <8-hex>` lines and deterministic `COMPOSITE_SHA1` checksums, allowing multi-gigabyte archived PS3/PS4/Wii U `.7z` dumps to verify and generate complete file-level `.hash` manifests in `< 0.1s` from 7-Zip headers without extracting gigabytes to disk (and running `7z t` stream integrity checks in deep verification mode).
- **Multi-ROM Redump / No-Intro DAT Correlation (`NoIntroRedumpDatParser.cs`, `RomManifestCsvService.cs`, `CollectionManifestModule.cs`)**:
  - Added `DatIndex.RomsByGameName` and `EvaluateMultiFileSet` to preserve all `<rom>` tracks per `<game>` (including multi-ROM ClrMamePro text DATs) and verify every disc/audio track across single-disc multi-track and multi-disc games while keeping **1 row per game** in `collection_manifest.csv` with multi-track verification summaries in `Notes`.

### Fixed
- **Redump Multi-Track Primary Track Lookup Shadowing (`NoIntroRedumpDatParser.cs`)**:
  - Fixed `DatIndex.Add` and `LoadIndexForConsole` so `ByGameName` and `ByNormalizedSlug` always prefer the primary data track (largest non-descriptor `<rom>`) rather than locking onto the leading `.cue` descriptor or overwriting with the trailing small audio track.
- **Zero-Leftover External `.hash` Cleanup on Archive Packaging & Folder Compression (`HashSidecarService.cs`, `RomManifestCsvService.cs`, `CollectionManifestModule.cs`, `RomLauncherGeneratorModule.cs`)**:
  - Unpacked game folders now always write `<GameFolder>.hash` beside the folder and automatically purge any stray per-track `.hash` files inside the subfolder (`CleanupStrayInternalFolderHashFiles`).
  - Whenever `PackageInsideArchive` packs `<Game>.hash` inside a `.7z` or `.zip` archive (including after compressing an unpacked folder or loose multi-track bundle to `.7z`), any external `<Game>.7z.hash`, `<Game>.hash`, or per-track `.bin.hash` files outside the archive are automatically removed.

---

## [2.8.5-rc.3] - 2026-10-30

### Added
- **Two-Tier `.hash` Integrity Verifier & Legacy Outer-Archive Self-Healing (`HashSidecarService.cs`, `RomManifestCsvService.cs`, `CollectionManifestModule.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Implemented `VerifySidecarForTargetAsync`, `HealTargetSidecarAsync`, `IsLegacyOuterArchiveSidecar`, and `VerifyAndHealDirectorySidecarsAsync` to validate both external beside `.hash` and internal archive `.hash` sidecars against `7z l -slt` inner-ROM headers (`FILE`, uncompressed `SIZE`, and native 7-Zip `CRC32` in ~15 ms without decompressing gigabytes of data) or full byte-for-byte cryptographic streaming (`SHA-1`, `CRC32`, `MD5` via `7z e -so`).
  - Added on-demand **Verify & Heal `.hash` Files** toolbars in both **Module 4 (ROM & Emulator Launchers)** and **Module 8 (Collection Manifest Auditor)** supporting **Fast 7z Header & Legacy Audit + Heal**, **Deep Byte-for-Byte Cryptographic Verify + Heal**, and **Dry-Run Audit Report Only** with live Terminal progress and Stop/cancellation support.
  - Removed the `500 MB` streaming threshold in `HashSidecarService.HashArchiveInnerRomAsync` so multi-gigabyte disc archives stream directly through `7z e -so` using a pooled 256 KB `ArrayPool<byte>` buffer without writing temporary gigabytes to `%TEMP%`.
- **Global Real-Time Next Run & Multi-Queue Synchronization (`SchedulerAndNotificationService.cs`, `MainWindow.xaml.cs`)**:
  - Implemented `GetGlobalNextScheduleStatus(AppConfig cfg)` to determine the true earliest next run time and target queue name across all enabled queues in the application, resolving an issue where the top-left status bar was tied exclusively to whichever queue happened to be actively selected.
  - Implemented `GetNextOccurrenceUtc(NamedJobQueue queue, DateTime nowUtc)` supporting accurate mathematical next-run calculations for `IntervalCadence`, `DailyTime`, and `SpecificTimes` scheduling modes.
  - Added live dashboard queue card countdown synchronization (`_dashboardQueueCadenceTextBlocks`) updating relative countdowns (e.g. `in 10m` -> `in 9m`) every 2 seconds without rebuilding the visual tree or disturbing user focus.
- **Automated No-Intro Upstream Online DAT Downloads (`DatDownloadService.cs`)**:
  - Registered automated online DAT download mappings for all popular cartridge console systems (NES, SNES, N64, Game Boy, Game Boy Color, Game Boy Advance, Nintendo DS, Sega Genesis / Mega Drive, Game Gear, 32X, Master System, Atari, Neo Geo Pocket, TurboGrafx-16, WonderSwan) via Libretro's upstream No-Intro metadata repository.
  - Aligned automatic DAT file naming to canonical system names (e.g. `Nintendo - Super Nintendo Entertainment System.dat`), immediately indexable by `NoIntroRedumpDatParser`.
- **Atomic Persistence & Automatic Corrupt Config Backup (`ConfigService.cs`, `PendingSwapService.cs`)**:
  - Implemented `AtomicWriteAllText` (write to `.tmp`, flush, and atomically replace via `File.Move(..., overwrite: true)` with retry fallback) across `settings.json`, `history.json`, `pending_swaps.json`, and remote settings pushes.
  - Added `ReadAllTextWithRetry` (3 retries with backoff on transient `IOException` file locks).
  - If `settings.json` fails to parse after retries, `ConfigService.Load()` now preserves a timestamped backup (`settings.json.backup_{timestamp}.bak`) and loads defaults in-memory without overwriting the disk file until the user explicitly saves.
- **Collection Manifest Auditor (Module 8) Inner-ROM & Sidecar Parity (`CollectionManifestModule.cs`)**:
  - Brought Module 8 into full parity with Module 4 (`RomManifestCsvService`): added deep inner-ROM archive hashing (`HashArchiveInnerRomAsync`), internal archive `.hash` cache reading (`TryReadArchiveInternalHashAsync`), two-stage Name-First + Hash-Fallback DAT matching, and clean `PackageInsideArchive` sidecar injection that removes loose beside `.hash` files.
- **CLI Secret Redaction in Terminal & Logs (`ProcessRunnerService.cs`)**:
  - Added `RedactSensitiveCommandLine` to automatically mask `-s`, `--session-auth`, `--token`, and `--password` arguments (`***REDACTED***`) in terminal `CMD` logs and `Housekeeping_Live.log`.
- **1-Second Debounced Window Placement Persistence (`MainWindow.xaml.cs`)**:
  - Window move and resize events now debounce-save to `settings.json` 1 second after dragging finishes, and immediately flush before remote self-update restarts.

### Fixed
- **ROM `.hash` Sidecar Placement Dropdown Enum Inversion (`MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Fixed an off-by-one enum mapping mismatch on `CmbRomHashPlacement` where display indices (`0 = SidecarBeside`, `1 = PackageInsideArchive`, `2 = None`) were cast directly to `HashSidecarMode` (`0 = None`, `1 = SidecarBeside`, `2 = PackageInsideArchive`), causing selecting "Package .hash File Inside .7z / .zip Archive" to save `SidecarBeside` (`1`) instead of `PackageInsideArchive` (`2`). Added explicit `Tag`-backed mapping (`MapHashPlacementToComboIndex` / `MapComboToHashPlacement`) and removed conflicting legacy global overwrites from `LoadConfigIntoUi` and `SyncUiToConfig`.
- **Legacy Outer-Archive `.hash` & CSV Cache Poisoning Prevention (`RomManifestCsvService.cs`, `CollectionManifestModule.cs`, `HashSidecarService.cs`)**:
  - Fixed `ForceRehashExistingManifestEntries` (Module 4) and `SkipExistingVerifiedHashes = false` (Module 8) so they properly bypass `TrustExistingHashFiles` instead of re-stamping stale `.hash` sidecars.
  - Invalidated legacy `collection_manifest.csv` rows and `.hash` sidecars that recorded the outer `.7z`/`.zip`/`.rar` container instead of the inner uncompressed ROM when `DeepInspectAndHashInnerRoms` is enabled, automatically re-hashing and healing both sidecars (including `MD5`, `CRC32`, `SHA-1`, and uncompressed `SIZE`) and CSV records.
  - Fixed `ListArchiveContentsAsync` and `RomLauncherGeneratorModule` `7z l -slt` parsing to skip the pre-`----------` archive header summary block and filter out directory entries (`Folder = +` / `Attributes = D`).
  - Prevented redundant `7z a` re-injection when a valid inner-ROM `.hash` is already present inside an archive (preserving `LastWriteTimeUtc`), refreshed `FileInfo` metadata after injection, and fixed Module 8 `effectivePlacement` resolution so `ChkManifestPackageInto7z` and `ChkManifestSidecars` are never overridden by the default enum value.
- **Process Guard Deferred Schedule Slot Preservation & Master Schedule Fallback (`SchedulerAndNotificationService.cs`, `MainWindow.xaml.cs`)**:
  - Checked `IsGuardedProcessRunning` before consuming due schedule slots (`isDeferredByGuard`) so daily, specific-time, and interval runs deferred while gaming/streaming (RetroArch, OBS) execute cleanly once the guarded process exits, and included legacy `EnableMasterSchedule` in `GetGlobalNextScheduleStatus`.
- **Windows Shell Built-In Command Execution (`ProcessRunnerService.cs`)**:
  - Registered internal Windows shell built-ins (`rmdir`, `rd`, `del`, `erase`, `copy`, `mkdir`, `md`, `move`, `ren`, `rename`, `dir`, `type`) so calls to commands like `rmdir` route through `cmd.exe /d /s /c "<cmd>"` instead of throwing `Win32Exception` (file not found) when invoked directly without `.exe`.
- **Dual-Extension & Fallback Archive ROM Resolution (`RomLauncherGeneratorModule.cs`)**:
  - Added secondary extension evaluation (`RecognizedRomExtensionsFallback`) in archive inspections to resolve valid console ROMs (e.g. `.gb` inside a GBC collection) when the primary extension filter is strict, preventing false-positive "Could not find matching ROM" errors.
- **Resilient Redump HTTPS / HTTP Fallback (`DatDownloadService.cs`)**:
  - Added an automatic fallback to HTTP on `redump.org` endpoints when HTTPS connection attempts on port 443 are actively refused by the server.
- **Native OS Process Handle Leak (`SchedulerAndNotificationService.cs`, `App.xaml.cs`)**:
  - Disposed all `Process` instances returned by `Process.GetProcessesByName()` in `finally` blocks to eliminate native `SafeProcessHandle` leaks on the 2-second background timer.
- **HTML Report XSS / Markup Escaping (`CollectionManifestModule.cs`)**:
  - Wrapped all dynamic fields (`FileName`, `DatMatchName`, `LocationId`, custom columns/values) in `WebUtility.HtmlEncode` when generating HTML visual reports.
- **Safe Directory Enumeration & Compiled Regex Caching (`CollectionManifestModule.cs`, `ArchiveCompressorModule.cs`, `ProxyGeneratorModule.cs`)**:
  - Switched recursive file scans to `EnumerationOptions { IgnoreInaccessible = true }` to prevent `UnauthorizedAccessException` crashes on system folders (`$RECYCLE.BIN`, `System Volume Information`).
  - Cached compiled custom field `Regex` instances with a 250ms timeout in `CollectionManifestModule.ApplyFieldParseRules`.
- **Online DAT Payload Validation (`DatDownloadService.cs`)**:
  - Added `IsValidDatPayload` header verification (`<datafile`, `clrmamepro`, `<?xml`) to reject HTML error/rate-limit pages instead of renaming them to `.dat`, and upgraded Redump endpoints to `https://`.
- **Remote Version Detection Priority & Zero-Warning Build (`RemoteNodeDeploymentService.cs`, `ModuleExecutionContext.cs`, `AppConfig.cs`, `MainWindow.xaml.cs`)**:
  - Aligned `RefreshNodeStatusAsync` to check `version.txt` before `FileVersionInfo` (matching `GetLocalAppVersion`), and resolved all 11 C# nullability compiler warnings (`0 Warning(s)`, `0 Error(s)`).
- **Incremental Manifest Cache Hit Restoration & Single-Pass Directory Scan (`RomManifestCsvService.cs`)**:
  - Fixed a cache key/metadata mismatch in `RomManifestCsvService.GenerateOrUpdateManifestAsync` where `existingCache` was keyed by filename (`ArchiveFileName`) without persisted file size/timestamps while lookups queried `arch.FullName.ToLowerInvariant()` and required exact `FileSize`/`LastModifiedUtc` equality (causing a 0% cache hit rate). Lookup now keys by filename and compares against `manifestLastWriteUtc`, restoring $0\text{ms}$ incremental cache hits and re-evaluating DAT matches in-memory.
  - Combined the double full-tree directory walk into a single-pass enumeration with `EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true }` and pre-compiled `SaveStateExtRegex`.
- **Zero-Allocation Span Levenshtein Fuzzy Matching & DAT File Caching (`NoIntroRedumpDatParser.cs`)**:
  - Replaced the 2D `int[,]` heap allocation in `DatTitleNormalizer.CalculateSimilarity` with a single-row `Span<int>` (`stackalloc` for strings $\le 256$ chars, `ArrayPool<int>.Shared` fallback) and added an $O(1)$ length-ratio pre-filter (`minL / maxL < 0.88`) before computing edit distance.
  - Added thread-safe in-memory caching (`ParsedFileCache` keyed by `(fullPath, lastWriteUtc)`) in `NoIntroRedumpDatParser.ParseFile`.
- **Pooled 256 KB Sequential Hashing & Child Process Deadlock Prevention (`HashSidecarService.cs`, `RomLauncherGeneratorModule.cs`, `MainWindow.xaml.cs`)**:
  - Upgraded `HashSidecarService` to rent 256 KB buffers from `ArrayPool<byte>.Shared` with `FileOptions.SequentialScan`, cached the resolved default `7z.exe` path, and disabled unused `RedirectStandardError` pipes on `7z.exe` invocations to prevent child-process `stderr` buffer deadlocks.
  - Fixed $O(N^2)$ redundant `CleanupOrphanedLaunchersAndShortcuts` sweeps inside the per-console loop in `RomLauncherGeneratorModule.cs` and pre-compiled `.bat`/`.url` regexes.
  - Added `Interlocked.CompareExchange` overlap guards and non-blocking `Dispatcher.BeginInvoke` to background remote telemetry polling and drive status refreshes in `MainWindow.xaml.cs`, using static frozen `SolidColorBrush` instances.

---

## [2.8.4] - 2026-10-30

### Added
- **Intelligent DAT Title Matching & Normalization Engine (`NoIntroRedumpDatParser.cs`)**:
  - Implemented `DatTitleNormalizer` that strips custom user tagging: `(PR)`, `(PRt)`, `(RH)`, `(Hack)`, `(Homebrew)`, `(Proto)`, `(Beta)`, `(Sample)`, `(Demo)`, `(Unl)`, `(Alt)`, `(DumpTag)`, `(Launcher)`, and square bracket dump tags (`[!...]`, `[b1]`, etc.).
  - Added alphanumeric slug normalization stripping accents/diacritics (`Pokémon` -> `Pokemon`), English articles (`the`, `a`, `an`), and punctuation.
  - Added fuzzy Levenshtein similarity catalog matching (threshold >= 0.88) to resolve minor misspellings, differing subtitles, and punctuation variations.
- **Name-First Matching with Hash Fallback (`RomManifestCsvService.cs`)**:
  - Manifest audit now checks name matching first so ROM hacks and custom patched titles (`(PR)`, `(RH)`) are identified correctly rather than marked as `NA`.
  - Added fallback checksum matching: if filename matching fails completely, checks uncompressed local hash against DAT to recover canonical titles for heavily renamed ROMs (`MATCH (via Hash)`).
- **Deep Archive ROM Payload Hashing & Packaging (`HashSidecarService.cs`, `RomManifestCsvService.cs`)**:
  - When deep inner ROM inspection is enabled, 7-Zip stream extraction (`7z e ... -so`) inspects and hashes the uncompressed ROM payload inside `.7z` and `.zip` archives directly in memory.
  - Sidecar `.hash` files now accurately record the uncompressed inner ROM's filename, uncompressed size, and inner ROM SHA-1.
  - Fixed 7-Zip path resolution: added `ResolveSevenZipExecutable` with automatic fallback to bundled tools, `C:\Program Files\7-Zip\7z.exe`, and system PATH.
  - When `PackageInsideArchive` is selected, sidecars are injected into the archive and loose beside `.hash` files are cleaned up.
  - `TrustExistingHashFiles` now inspects internal `.hash` sidecars inside `.7z` and `.zip` archives.
- **Desktop Window Position & Size Persistence (`AppConfig.cs`, `MainWindow.xaml.cs`)**:
  - Added persistence for `WindowLeft`, `WindowTop`, `WindowWidth`, `WindowHeight`, and `IsWindowMaximized` across sessions.
  - Restores geometry on startup with multi-monitor working area bounds verification to prevent off-screen windows.
  - Tray restore honors maximized state.

### Fixed
- **Watchdog Window Reset & Desktop Position Stealing (`App.xaml.cs`)**:
  - Fixed Windows Task Scheduler watchdog (`Housekeeping.exe --minimized --watchdog`) resetting the main window, stealing focus, and losing desktop coordinates every 15 minutes.
  - Single-instance mutex check now cleanly exits without window restore when invoked with `--watchdog` or `--minimized`.
- **Clipboard Access Exception Handling (`MainWindow.xaml.cs`)**:
  - Wrapped terminal clipboard copy in a 5-attempt retry loop with exponential backoff catching `COMException` (`CLIPBRD_E_CANT_OPEN` 0x800401D0).
- **Process Execution Error on Temp Scratch Clean (`RomLauncherGeneratorModule.cs`)**:
  - Replaced `rmdir` shell process call with native C# `Directory.Delete(targetScratchDir, recursive: true)` to eliminate missing executable errors.
- **Excluded Non-Game Save & Metadata Files from Manifest (`RomManifestCsvService.cs`)**:
  - Excluded `.sav`, `.srm`, `.state`, `.st0`..`.st9`, `.png`, `.jpg`, `.nfo`, `.bak`, `.old`, and `.hash` from standalone file manifest scans.
- **Game Boy Color Collection Rule Default Extension (`AppConfig.cs`)**:
  - Updated default GBC collection extensions to `.gbc, .gb` and added primary payload fallback when priority extensions miss.

---

## [2.8.3] - 2026-10-29

### Added
- **Top Bar Version Indicator Badge (`MainWindow.xaml`)**:
  - Surfaced active program version `v2.8.3 RC` badge in the header bar directly beside the application title, icon, and schedule status for immediate version visibility.
- **Windows Task Scheduler Non-Admin Fallback (`WindowsWatchdogService.cs`)**:
  - Resolved Windows Task Scheduler activation returning "Access is denied" when run without Administrator elevation.
  - Implemented dual-tier registration: automatically checks Windows token elevation (`IsInRole(WindowsBuiltInRole.Administrator)`); uses `/rl highest` when running as administrator and automatically falls back to `/rl limited` with user credentials when non-elevated.
- **Dynamic Terminal & Status Bar Verbosity Customizer (`MainWindow.xaml`, `MainWindow.xaml.cs`, `AppConfig.cs`)**:
  - Added new section in Settings tab: `📺 Terminal & Status Bar Verbosity Customizer`.
  - Configurable tags for terminal (`ERROR`, `WARN`, `SUCCESS`, `WORK`, `HEADER`, `INFO`, `CMD`, `OUT`, `SKIP`, `DRYRUN`, `NET`) and bottom status bar.
  - One-click presets: `⚡ Balanced`, `🤫 Minimalist`, `🐞 Debug / All`, and `🔄 Reset Defaults`.
  - Added terminal verbosity filter selector toolbar to built-in terminals mirroring footer verbosity controls (`Errors Only`, `Warnings & Errors`, `Progress & Status`, `Everything`, `Custom Preset...`).
- **Active Module Telemetry & Granular Status Tracking (`MainWindow.xaml`, `MainWindow.xaml.cs`, `CollectionManifestModule.cs`, `RomManifestCsvService.cs`)**:
  - `ACTIVE STREAM:` and bottom status bar `TxtFooterStatus` now consistently display the currently executing module name and badge prefix (e.g., `[Manifest]`, `[ROM Launchers]`).
  - Added 50-file / 50-ROM and 5-second interval heartbeat logging so long runs never appear hung or unresponsive.
  - Granular start/completion telemetry with throughput (MB/s) and file counts for files >250MB.
- **Dedicated File vs Folder Hashing Selectors (`MainWindow.xaml`, `MainWindow.xaml.cs`, `RomManifestCsvService.cs`)**:
  - Added a prominent `📁 File vs Folder Hashing Logic:` selector dropdown with live explanation text directly to Card 2 ("Audit Engine & Cryptographic Verification") in the Collection Manifest module.
  - Renamed the Target Folders DataGrid column header to `Hash Target (Files vs Folders)` with high-visibility header tooltips.
  - Added a dedicated `File vs Folder Hashing Logic:` selector dropdown (`CmbRomStructureMode`) directly to the Game Launcher module's ROM Verification & Auditing panel.
  - Connected `RomManifestCsvService` to composite folder detection: automatically computes composite folder hashes and `.hash` sidecars for multi-file game titles (e.g. PS3, Wii U) and excludes internal files from duplicate loose passes.
- **ROM Collection Hash Pipeline & Structure Detection (`ReleaseStructureDetector.cs`, `HashSidecarService.cs`, `RomManifestCsvService.cs`, `MainWindow.xaml`)**:
  - Added `.hash` placement selector to Game Launchers module: beside ROMs/archives, inside archives (`.7z`/`.zip`), or disable.
  - Fast-cache trust: skips deep recompression if `.hash` sidecar is present and valid.
  - Automatic structure detection: distinguishes single-file ROMs (SNES `.sfc`, Genesis `.bin`, etc.) from composite multi-file directory dumps (PS3 `PS3_GAME`, Wii U decrypted `code/content/meta`, 3DS raw dumps, Blu-ray BDMV, DVD VIDEO_TS).
- **Custom Text Parsing Rule Scoping (`AppConfig.cs`, `CollectionManifestModule.cs`, `MainWindow.xaml`)**:
  - Added `FileExtensionFilter` property to `ManifestFieldParseRule` with UI selector in DataGrid, preventing video parse rules from corrupting audio/music files and vice versa.
- **UI DataGrid Layout & Readability Fixes (`MainWindow.xaml`)**:
  - Fixed row clipping and dropdown heights in Target Folders and Field Extraction Rules DataGrids.
  - Added explicit tooltips explaining Directory Dump Logic and composite detection modes.

---

## [2.8.2] - 2026-10-29

### Added
- **Tailored Personal Regex Presets & General Preset Suites (`ManifestPresetCatalog.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Inspected actual media libraries, AutoCompress codebase, RetroLaunch/LibretroLaunchers, ArchiveGen, ProxyGen, and Master Set directory structures.
  - ⭐ **Personal AutoCompress Media Suite**: `MovieTitle`, `MovieYear`, `ShowTitle`, `Season`, `Episode`, `EpisodeTitle`, `DiscTier` (`DVD|BR|UHD`), `SourceOrRes` (`Original|4K|1080p|720p|480p|360p|240p|144p`), `Edition` (`{edition-...}` tags), `ProxyRes`, `MediaLangFolder` (`Japanese|English|Spanish`).
  - ⭐ **Personal Master Set & ROM Tools**: `GameTitle`, `Region` (`USA|Europe|Japan|World|En,Ja...`), `DiscNumber` (`(Disc 1|2 Discs|Side A...)`), `Revision` (`(Rev X|vX.X|Beta...)`), `DumpTag` (`[!]|[!CR]|[CDI]|[GDI]|[b]...`), `IsLauncher` (`- Launcher.bat`), `RomHackOriginalGame`, `RomHackName`, `RomHackVersion`, `PlatformFromFolder` (`SNES|Genesis|PlayStation...`), `CompanyFromFolder` (`Nintendo|Sega|Sony...`).
  - 🎬 **General Scene / P2P Video Releases**: `SceneTitle`, `Year`, `Season`, `Episode`, `Resolution`, `Source`, `VideoCodec`, `AudioCodec`, `ReleaseGroup`.
  - 📁 **General File & Document Metadata**: `Date`, `Year`, `Version`, `ParentCategory`, `FileExtension`.
  - 🎵 **General Music & Audio Tracks**: `TrackNumber`, `Artist`, `AlbumOrTitle`, `AudioBitrate`.
- **Target Extraction Target Support**:
  - Added `ManifestInspectTarget.FileNameWithoutExtension` in `AppConfig.cs` and `CollectionManifestModule.cs`, allowing matching regex against the pure filename without extension so `.ext` doesn't leak into capture groups.
- **Collection Manifest UI Dropdown Enhancements (`MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Card 1: `Manifest Base Name`, `Primary Output Folder`, and `Secondary Mirror Backup Folder` converted from plain text boxes to editable ComboBoxes (`IsEditable="True"`) with smart suggestions and custom text input support.
  - Card 3 (`GridManifestTargetFolders`):
    - `Location ID`: Dropdown ComboBox with suggestions (`Local-SSD`, `Local-DAS`, `EmuDrive`, `MasterSet`, `Archive-Pool`, `Remote-NAS`, `Backup-Mirror`).
    - `Label / Collection`: Dropdown ComboBox with suggestions (`Master Set`, `Media Library`, `TV Shows`, `Anime`, `Movies`, `Music`, `RomHacks`, `EmuSets`, `Documents`).
    - `Platform / DAT`: Comprehensive dropdown ComboBox pre-populated with all 30+ supported retro and modern consoles (NES, SNES, N64, GameCube, Wii, Wii U, Switch, GameBoy, GBC, GBA, DS, 3DS, Genesis, Master System, 32X, Sega CD, Saturn, Dreamcast, Game Gear, PlayStation, PS2, PS3, PSP, PS Vita, Xbox, Xbox 360, Neo Geo, Arcade, PC).
    - `Ext Filter`: Dropdown ComboBox pre-populated with game, media, and document extension bundles (`.7z|.zip|.rar|.chd|.iso|.ciso|.m3u|.cue|.bin`, `.mkv|.mp4|.avi|.mov|.webm`, `.mp3|.flac|.ogg|.m4a|.wav`, `*.*`).
  - Card 4 (`GridManifestFieldRules`):
    - Added Preset Bundle ComboBox (`CmbManifestPresets`) with 5 rich preset suites.
    - Added `🗑 Clear All` button with confirmation prompt to reset rules.
    - All columns in the rules table converted to in-cell ComboBoxes: `CSV Column Name`, `Inspect Target`, `Match Mode`, `Assigned Value`, `Split Delimiter`, and `Split Index`.
  - Interactive Test Playground:
    - Added `Quick Samples` dropdown (`CmbManifestTestSamplePresets`) with realistic filenames from Dragon Ball, Death Note, Anchorman, Outlaw Star, Danny Phantom, Master Set SNES/Sega CD, Pokemon Crystal Legacy, Scene release strings, documents, and music tracks.
    - Automatically updates sample text, evaluates all inspection targets (relative path, parent folder, full path, filename without extension), and displays real-time extracted chips.
- **Unit Testing**:
  - Added 3 comprehensive unit test suites in `HousekeepingTests.cs` (`CollectionManifest_PersonalMediaPresets_ExtractsAccurately`, `CollectionManifest_PersonalRomPresets_ExtractsAccurately`, `CollectionManifest_FileNameWithoutExtension_WorksReliably`), expanding test suite to 67 tests with 100% pass rate.

---

## [2.8.1] - 2026-10-29

### Added
- **Remote Pipeline & Job Control Engine (`RemoteControlModels.cs`, `RemoteNodeDeploymentService.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Interactive Remote Start Control: Added `[ ▶ Start Remote... ▼ ]` to Terminal Pane 1 & Pane 2 headers and `[ ▶ Start Run... ▼ ]` to Tab 4 Node Cards.
  - Dynamically fetches defined jobs and queues on the fly over SMB (`ReadRemoteQueuesAndJobs`) and displays a high-contrast dark picker context menu allowing users to launch any remote queue or job with a single click.
  - Interactive Remote Stop: Added `[ ⏹ Stop Remote Run ]` / `[ ⏹ Stop Run ]` buttons with confirmation dialog to instantly signal the remote node to abort active pipeline runs.
  - Signal File Daemon (`control_request.json`): Remote instances monitor for control signals in their local directory, handling `StartQueue`, `StartJob`, `StopQueue`, and `RestartApp` autonomously.
  - Live Telemetry Broadcasting: Broadcasts `ActiveQueueId`, `ActiveQueueName`, `CurrentJobIndex`, `TotalJobsInQueue`, and `ActiveJobName` in `telemetry_heartbeat.json`, surfaced in real time across remote terminal headers and live performance metrics (e.g. `[Queue: Nightly Transcodes (Job 2/5)] • 1080p Encode: HandBrake running (45%)`).

- **Collection Manifest Module 8 Overhaul (`CollectionManifestModule.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `AppConfig.cs`)**:
  - Inline Folder Browser: Added `[ 📁 ]` folder browse button directly within every row of the Target Folders DataGrid for instant folder picking.
  - Dedicated browse buttons for Primary Manifest Output Directory and Secondary Mirror Backup Directory.
  - Dynamic Custom Text Parsing & CSV Field Extraction Engine (`ManifestFieldParseRule`):
    - Supports `RegexNamedCapture`, `RegexPattern` (with capture group substitutions like `$1`), `TextContains`, and `SplitToken` across filename, relative path, full path, or parent folder name.
    - Extracted custom tokens are automatically appended as dynamic columns to generated `.csv` headers, `.json` models, and the visual `.html` summary report.
    - Bundled one-click rule presets:
      - 🎬 Video Presets: ShowTitle, Season, Episode, Resolution, Codec, Edition.
      - 🎮 Game Presets: Region, DiscNumber, Revision.
    - Built-in Interactive Rule Test Playground right in the inspector with live badge chips previewing parsed extractions on custom sample strings.
  - Added Module 8 to Tab 3 (`🧩 Modules`) master catalog with full feature documentation, capabilities guide, and "+ Add Job" shortcut.

- **Dramatic `settings.json` Size Optimization (`AppConfig.cs`, `ConfigService.cs`, `HousekeepingJob.cs`)**:
  - Reduced `settings.json` file size by over 93% (from ~270 KB down to ~15-20 KB).
  - Applied `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` to all 8 module config properties on `HousekeepingJob`.
  - Added `StripUnusedModuleConfigs()`: each job now only serializes the configuration of its active module type.
  - Deprecated legacy redundant `Jobs` list duplication on save and enabled `JsonIgnoreCondition.WhenWritingNull` across `ConfigService.JsonOptions`.

### Fixed
- **Startup Crash & Tray-Icon Silent Exit (`MainWindow.xaml.cs`, `App.xaml.cs`, `ConsoleSyncPathsWindow.xaml.cs`, `AppConfig.cs`)**:
  - Resolved fatal `NullReferenceException` in `RefreshAvailableRemotesAndShares()` and `CollectAllPartialCandidateDirectories()` when inspecting jobs whose module type is not `CloudSync` (after null serialization optimization).
  - Fixed issue where the process caught startup errors and initiated shutdown, triggering `OnClosing` to show the notification "Minimized to system tray. Scheduled tasks remain active." before terminating.
  - Implemented early Win32 HWND creation (`WindowInteropHelper.EnsureHandle`) and message hooking in `MainWindow`'s constructor so that hidden instances can reliably receive `HOUSEKEEPING_RESTORE_WINDOW` single-instance broadcast signals.
  - Interactive double-click launches (without `--minimized`) now unconditionally bring up the normal window and restore focus, ensuring the UI always opens on desktop launch.
  - Configured `<PublishReadyToRun>true</PublishReadyToRun>` in `Housekeeping.csproj` for Ahead-of-Time native compilation.
- **Dark Theme ContextMenu Styling (`App.xaml`, `MainWindow.xaml.cs`)**:
  - Resolved white-on-white text in `+ New Job ▼` dropdown menu by applying global dark slate theme styling (`#171E29` background, `#3B82F6` border, `#2563EB` hover highlight, `#F8FAFC` foreground) across all WPF `ContextMenu` and `MenuItem` controls.

---

## [2.8.0] - 2026-10-29

### Added
- **Standalone Module 8: Collection Manifest Auditor (`CollectionManifestModule.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `AppConfig.cs`)**:
  - Author comprehensive media, game, and file manifests across local and network-attached storage with custom location identifiers (e.g., `NAS-Pool1`, `DAS-RAID`).
  - Multi-Folder Mapping: Supports defining multiple target folder paths within a single manifest job with path recursion and custom extension whitelisting/blacklisting.
  - Media & Game Metadata Extraction: Integrated metadata extraction from filenames (title, year, season/episode, resolution, edition, source tag, system alias).
  - Fast Inventory vs Deep Hash Mode: Toggle between lightweight metadata cataloging (instant file size and timestamp enumeration) and full-content hashing (CRC32, MD5, SHA-1).
  - Composite Folder Hashing: Computes deterministic composite hashes for uncompressed folder structures (PS3/PS4 dumps, BDMV trees) by hashing file contents in sorted lexical order.
  - Multi-Format Manifest Reports: Generates structured `.csv`, `.json`, and a standalone dark-themed responsive `.html` summary report.
  - Secondary Storage Mirroring: Automatically syncs completed manifests to an off-site or centralized network backup folder.
- **Windows Task Scheduler Auto-Start & Crash Recovery Watchdog (`WindowsWatchdogService.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Added native Windows Task Scheduler integration (`schtasks.exe`) via `WindowsWatchdogService`.
  - Configures `Housekeeping_Watchdog` task that starts on user logon and runs every N minutes (configurable, default 5 min) to guarantee Housekeeping is revived if accidentally closed or terminated.
  - Added interactive Watchdog settings card in Tab 4 with instant Status Query, Register, and Unregister controls.
  - Replaced the orphaned "Master Automated Schedule" card in Settings with a clear informational banner pointing users to `📋 Job Queues` and `📊 Dashboard` for queue execution schedules.
- **Multi-Level Discord Webhook Notifications & Machine Identification (`AppConfig.cs`, `SchedulerAndNotificationService.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Added granular notification options in Settings & History:
    - Queue Started / Queue Finished
    - Job Started / Job Finished
    - Task / Subprocess Started
    - Job or Pipeline Errors
  - Added Machine Identification Tagging (`DiscordCustomMachineName`): Prepends the originating hostname (e.g. `[SERVER]`, `[MAIN]`, or custom name) to all Discord embed titles, author fields, and footers for instant cluster tracking.
- **Modernized Job Builder Sidebar & Multi-Instance Queue Reuse (`MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Direct `[ + New Job ▼ ]` dropdown menu to instantiate any module type with one click.
  - Real-Time Search & Filtering: Filter sidebar jobs by module type or text query, and sort alphabetically (A-Z) or by module type.
  - Compact vs Detailed View: Added a toolbar toggle to switch between high-density compact rows and detailed cards with descriptions and configuration summaries.
  - Clean Default Job Naming: Standardized default job names with zero emojis (e.g. `Job - HandBrake Video Transcode`, `Job - Collection Manifest Audit`).
  - Direct Multi-Instance Queue Reuse: Allowed the same job to be placed multiple times within a queue or across queues without forced cloning, using independent index-based queue tracking.
  - Danger Zone Factory Reset: Added a secure factory reset with typed `"RESET"` confirmation, automatic backup of current configuration to `settings_prereset_backup.json`, and clean default state restoration.
- **Verbose DAT Matching & Hash Sidecar Packaging (`ConsoleSystemCatalog.cs`, `HashSidecarService.cs`, `AppConfig.cs`)**:
  - `ConsoleSystemCatalog`: Static system dictionary with 25+ game systems and alias mappings for DAT matching.
  - `HashSidecarService`: Standard `.hash` sidecar generator emitting CRC32, MD5, and SHA-1 checksum headers for ROMs and archives.
  - Composite folder hashing for unzipped disc dumps (PS3, PS4, Xbox).
  - In-place 7z packaging integration with checksum sidecars.

### Improved & Fixed
- **Terminal Performance Overhaul & Batch Line Culling (`MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Diagnosed and fixed severe app sluggishness and thread freezing caused by high-throughput stdout streams: individual `RemoveAt(0)` calls on WPF `ObservableCollection` caused layout thrashing and starved the message pump.
  - Implemented configurable Terminal Buffer Limit in Settings: default 400 lines (selectable: 250, 400, 500, 750, 1000 lines).
  - Batch Culling: When terminal lines exceed `maxLines + 50`, Housekeeping culls 50 lines at once in a single dispatch operation, completely eliminating UI stutter and freezing.
  - Autoscroll Reliability: Refactored autoscroll to inspect the visual tree for the internal WPF `ScrollViewer` decorator and call `.ScrollToEnd()`, ensuring rock-solid continuous tailing.
- **Modular Per-Queue Run Logs, Startup Archiver & Rolling Live Logs (`MainWindow.xaml.cs`)**:
  - Queue runs now generate separate modular log files under `logs/runs/Run_<QueueName>_<Date>.log` instead of writing everything to one giant monolithic file.
  - Live log `Housekeeping_Live.log` automatically rolls over to `logs/archive/` when exceeding 2MB.
  - Implemented startup archiver `ArchiveOldLogs()` that sweeps logs older than 7 days into `logs/archive/` and prunes archives older than 30 days.
  - Throttled subprocess encoder milestone updates to 5% increments or 15 seconds to prevent log saturation.
- **Adaptive Remote Node Streamer & Self-Restart Watcher (`RemoteTerminalStreamService.cs`, `RemoteNodeDeploymentService.cs`, `MainWindow.xaml.cs`)**:
  - Adaptive SMB polling frequency: switches dynamically to 400ms during active execution and 1200ms when idle to maximize terminal responsiveness without saturating network bandwidth.
  - Remote Node Self-Restart Signal: Remote instances now watch for `restart_request.signal` in their background tick loop, log the restart reason, and relaunch cleanly via `Process.Start`.
  - Remote Node Restart Action: Added `[ 🔄 Restart App ]` button on remote node cards in Settings to trigger a clean remote application restart over SMB share with 1 click.

---

## [2.7.2] - 2026-10-29

### Fixed
- **Server UI Freezing & High I/O Stutter (`SystemPerformanceTelemetryService`, `MainWindow.xaml.cs`)**:
  - Diagnosed and resolved the root cause of the app running sluggishly/poorly on Windows Server: `BackgroundTimer_Tick` previously executed `_telemetryService.GetLocalPerformanceStats()` synchronously on the WPF UI thread every 2 seconds.
  - `DriveInfo.GetDrives()` was querying all drives including `DriveType.Network` (mapped SMB shares, sleeping RAID drives, ZFS pools) and requesting `d.AvailableFreeSpace` directly on the message pump. On servers with network shares, unspun disks, or complex storage pools, SMB/disk queries blocked the UI thread for seconds at a time, starving the message pump.
  - Fully decoupled telemetry sampling into an asynchronous background worker loop running off the UI thread in `SystemPerformanceTelemetryService`.
  - Filtered disk scanning to only poll local fixed drives, skipping network drives to eliminate SMB I/O timeouts, and cached drive lists across 25-second windows.
  - `GetLocalPerformanceStats()` now returns an instant in-memory cached snapshot in 0ms, eliminating UI lag, stutter, and layout bottlenecks entirely.
- **Remote Node Machine Stats & Terminal Streaming (`RemoteTerminalStreamService`, `SystemPerformanceTelemetryService`, `MainWindow.xaml.cs`)**:
  - Diagnosed why remote connections showed reachability as successful but failed to update terminal lines and machine stats:
    - `node.LastStats` was declared in `AppConfig.cs` but was never populated or polled anywhere in the application.
    - No heartbeat or telemetry status was ever published to disk by the remote instance.
    - Terminal Pane 1 (`TerminalListBox`) was hardwired to local `_filteredTerminalLines` and never switched sources or streamed from remote nodes.
    - Pane 2 skipped local lines when set to a remote node, but possessed no remote log consumer.
    - Housekeeping previously only wrote to log files during active job runs, leaving idle or scheduled server instances without a readable stream on disk.
  - **Telemetry Heartbeat Publisher**: `SystemPerformanceTelemetryService` now continuously publishes atomic `telemetry_heartbeat.json` snapshots to `logs/` and the application root on all running instances, including CPU %, RAM used/total, multi-disk free space, network Mbps, and active subprocess/job status.
  - **Remote Terminal Streamer (`RemoteTerminalStreamService`)**: Implemented dedicated live log tailer that connects to remote nodes over SMB/UNC share (`node.RemoteAppPath\logs\Housekeeping_Live.log` or newest session log), retrieves recent history on connect, and streams newly appended lines in real time using non-blocking `FileShare.ReadWrite`.
  - **Terminal Pane Source Switching (`SwitchTerminalPaneSource`)**: Switching node dropdowns in Pane 1 (`CmbPane1Node`) or Pane 2 (`CmbPane2Node`) now seamlessly swaps the item source between local logs and live remote streams (`_pane1RemoteTerminalLines` / `_pane2RemoteTerminalLines`), and asynchronously polls remote machine stats and active subprocess banners.
- **Assembly Metadata Versioning & version.txt Synchronization (`Housekeeping.csproj`, `RemoteNodeDeploymentService.cs`)**:
  - Synchronized `<Version>`, `<AssemblyVersion>`, `<FileVersion>`, and `<InformationalVersion>` to `2.7.2` in `Housekeeping.csproj` (which was previously hardcoded to `2.6.0`).
  - Added `version.txt` generation in `D:\Programs\Housekeeping\` and updated `RemoteNodeDeploymentService.GetLocalAppVersion()` to read `version.txt` / assembly ProductVersion cleanly.

---

## [2.7.1] - 2026-10-28

### Fixed
- **Queue Synchronization & Job Persistence Bug (`AppConfig.EnsureDefinedJobsSynchronized`, `AppConfig.MigrateLegacyModulesToJobsIfNeeded`)**:
  - Diagnosed and resolved the root cause of secondary queue jobs reverting or reloading upon refresh: `EnsureDefinedJobsSynchronized()` previously contained an unconditional fallback (`if (GetActiveQueue().Jobs.Count == 0 && DefinedJobs.Count > 0) GetActiveQueue().Jobs.AddRange(DefinedJobs);`) that forcibly repopulated any empty queue or emptied queue with all defined jobs every time settings were saved or the UI refreshed.
  - Removed automatic defined job injection from `EnsureDefinedJobsSynchronized()`. Empty queues now remain strictly empty (`Jobs.Count == 0`) and display the clean empty queue banner with options to reuse jobs or populate starter jobs.
  - Added guard to `MigrateLegacyModulesToJobsIfNeeded()` to prevent legacy module migration from running if defined jobs or any named queue jobs already exist.
  - Ensured `ActiveQueueId` is saved to disk immediately whenever changing the active queue via `CmbManageNamedQueue`, `CmbDashboardActiveQueue`, or dashboard card action buttons.
  - Updated `RefreshNamedQueuesDropdown()` to call `Items.Refresh()` when collections match rather than setting `ItemsSource = null`, preventing visual flicker and selection churn.

## [2.7.0] - 2026-10-28

### Added
- **Crash Fix: DataGrid Transaction Refresh Collision (`DataGridExtensions.SafeRefresh`, `App.xaml.cs`, `MainWindow.xaml.cs`, `ConsoleSyncPathsWindow.xaml.cs`)**:
  - Diagnosed crash (`System.InvalidOperationException: 'Refresh' is not allowed during an AddNew or EditItem transaction`) occurring when users clicked row buttons (such as browsing local folders) while a DataGrid row was in an active edit transaction.
  - Created `DataGridExtensions.SafeRefresh()` extension method which safely commits active cell and row edits before refreshing the collection view.
  - Added global `DispatcherUnhandledException` handler in `App.xaml.cs` to capture diagnostic crash dumps in `logs/crash_*.log` and safely absorb non-fatal CollectionView transaction exceptions.
  - Upgraded `GameSyncPathItem`, `ConsoleCollectionRule`, and `CloudSyncJobItem` to fire `OnPropertyChanged()` for all properties, enabling instant two-way WPF binding updates without forced manual collection refreshes.
- **Game Launcher Defined rsync/rclone Share & Remote Selector (`AvailableRemotesAndShares`, `MainWindow.xaml`, `ConsoleSyncPathsWindow.xaml`)**:
  - Replaced manual-only text columns in Game Launcher save sync tables (`GridRomDefaultSyncPaths` and `ConsoleSyncPathsWindow`) with editable dropdown columns (`ComboBox IsEditable="True"`), allowing users to directly select any detected rclone remote (e.g. `EmuDrive:`, `gdrive:`, etc.) or defined cloud sync share from a list while retaining the ability to type subpaths.
  - Added `Quick Remote / Share:` toolbar dropdown with `[ Insert Remote ]` and `[ 🔄 ]` refresh buttons to quickly insert/replace remote prefixes on selected sync path rows.
  - Enhanced `📥 Import from Cloud Sync...` to surface an interactive popup menu when multiple cloud sync jobs exist, letting users select which defined share to import.
- **Global Defined Jobs Repository & Separation of Concerns (`AppConfig.DefinedJobs`, `MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Cleanly decoupled Job Building from Queue sequencing: `🛠 Job Builder` is now exclusively dedicated to designing, inspecting, and configuring all defined jobs across the application (`_allDefinedJobCards`), while `📋 Job Queues` is dedicated to building, sequencing, and scheduling queues.
  - In Job Builder, users can now view and select all defined jobs across the entire application repository in the sidebar regardless of which queue is currently selected or active.
  - Removed the queue switcher dropdown and reuse button from Job Builder sidebar.
  - In-memory reference equality ensures that editing job properties in Job Builder immediately reflects across all queues referencing that job.
- **Tab Re-Ordering (`MainWindow.xaml`, `CaptureUiScreenshotsAsync`)**:
  - Moved the `🧩 Modules` tab to appear immediately after `🛠 Job Builder`.
  - Updated tab sequence: `📊 Dashboard` (Tab 0) ➔ `📋 Job Queues` (Tab 1) ➔ `🛠 Job Builder` (Tab 2) ➔ `🧩 Modules` (Tab 3) ➔ `⚙ Settings` (Tab 4) ➔ `📜 History` (Tab 5).
  - Synchronized automated UI capture routines to match the updated tab order (`03_JobBuilder_...`, `04_ModulesCatalog...`).
- **Empty Queue Banner Hit-Testing Fix (`MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Resolved hit-testing interception where clicking `➕ Add Existing Job from Another Queue` in `EmptyQueueBanner` failed to register clicks due to `ScrollViewer` z-index collision.
  - Configured `ScrollModuleCards` visibility to dynamically collapse when the active queue has 0 jobs, ensuring clean unblocked mouse clicks over `EmptyQueueBanner`.
- **Dedicated `📊 Dashboard` Tab (`MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Re-architected Tab 1 into the primary Dashboard, providing an overview of all configured Job Queues at a glance alongside the built-in terminal.
  - Interactive Queue Cards: displays each queue's name, active status badge (`🟢 Active Queue` or `[ Set Active ]`), job count pill, job execution sequence chips, and next run status.
  - Direct Schedule Toggle: inline `☑ Automated Schedule` checkbox on each card allows enabling/disabling automated runs instantly without digging into settings.
  - On-Demand Queue Actions: 1-click `▶ Run`, `🔍 Dry Run`, and `✏ Edit Queue` buttons directly on every queue card.
- **Queue Renaming & Dynamic Naming Prompts (`InputPromptDialog`, `MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Added direct queue renaming in the **`📋 Job Queues`** tab via both an editable `Name:` text field and a `[ ✏ Rename ]` toolbar button.
  - Added a `[ ✏ Rename ]` button directly on each queue card in the **`📊 Dashboard`** tab to rename queues on-demand without tab switching.
  - Implemented interactive naming prompts (`InputPromptDialog`) when creating a new queue (`➕ New`) and cloning an existing queue (`📋 Clone`).
- **Dedicated `📋 Job Queues` Pipeline Editor (`MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Created a dedicated Tab 2 for managing and authoring queue pipelines.
  - Left Column: Queue Selector, New (`➕`), Clone (`📋`), Set Active (`⭐`), Delete (`🗑`), and full Automated Schedule Editor (Daily time, Interval cadence, Specific times, Day-of-week checkboxes, Notes).
  - Right Column: Queue Execution Sequence with draggable/up-down (`▲ ▼`) job ordering, job cards, inline status badges, and execution controls.
- **Cross-Queue Job Reuse (`ExistingJobItemViewModel`, `MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Added `➕ Add Existing Job...` action in Job Queues to reuse existing jobs across queues without reconfiguring them from scratch.
  - Modal picker surfaces all defined jobs across the entire application with module type badges and summaries, displaying which queues currently include them.
  - Links existing defined job references directly when added to a new queue or deep-clones when duplicating.
- **Dynamic Alphabetical Terminal Disk Display & Culling (`SystemPerformanceTelemetryService`, `SystemPerformanceStats`, `MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Terminal performance bar inspects all ready fixed, removable, and network drives on the system.
  - Shows multiple drive free space statistics (`C: 527 GB Free | D: 1.3 TB Free`) when space permits.
  - Dynamic alphabetical culling: when horizontal space is constrained (e.g. Split Vertical mode or compact windows), drives are culled starting from the last drive alphabetically (dropping `Z:`, then `E:`, then `D:`, preserving `C:`).
- **Secondary Manifest & Database Storage Mirroring (`AppConfig.cs`, `RomManifestCsvService.cs`)**:
  - Added settings toggle `EnableSecondaryManifestStorage` and `SecondaryManifestDirectory`.
  - Automatically mirrors generated collection manifests (e.g. `collection_manifest.csv`) to a centralized backup/cloud location.
- **CLI Argument Handling Enhancements (`App.xaml.cs`)**:
  - Updated `--capture-ui` CLI argument handling to support both standalone flag usage (automatically defaulting to `./ui_captures`) and custom output directory arguments.

---

## [2.6.0] - 2026-10-28

### Added
- **Remote Windows Nodes & SMB Deployment (`RemoteNodeConfig`, `RemoteNodeDeploymentService`, `Settings > Remote Nodes`)**:
  - Remote Windows node management over Windows UNC / SMB (`\\SERVER\c$\Programs\Housekeeping`), OpenSSH, and WinRM.
  - Dedicated `Remote Windows Nodes` tab in Settings with reachability testing, version comparison (`vLocal` vs `vRemote`), and node cards.
  - 1-Click Update Remote App (`[ ⬆ Update Remote App to vX.X.X ]`) copying local binaries over SMB while preserving remote `settings.json`.
  - Settings Synchronization (`[ ⚙️ Update Remote Settings ]`) safely pushing local configuration when versions match.
- **Multi-Terminal Split Views & Live Task Manager Performance Bar (`MainWindow.xaml`, `MainWindow.xaml.cs`, `SystemPerformanceTelemetryService`)**:
  - Terminal view switcher supporting Single (`⬛`), Split Horizontal (`⬒`), and Split Vertical (`⬓`) views capped at 2 simultaneous panes.
  - Independent remote or local node selection per terminal pane via pane dropdowns (`CmbPane1Node`, `CmbPane2Node`).
  - Draggable `GridSplitter` between terminal panes.
  - Live Task Manager Performance Bar positioned directly under the Active Stream in each terminal window, monitoring CPU %, RAM usage / total %, Disk free space %, and network throughput Mbps via Win32 performance counters.
- **ROM Collection Verification Manifest & Redump/No-Intro Auditing (`RomManifestCsvService`, `NoIntroRedumpDatParser`, `DatDownloadService`)**:
  - Automatically generates and incrementally maintains `collection_manifest.csv` in each console ROM collection directory.
  - Dual filename tracking: captures both the `Archive File Name` (e.g. `Game.7z`) and inner `ROM / Folder Name` (e.g. `Game.sfc` or `Game Folder\`).
  - Local SHA-1 and CRC32 hash calculation with DAT querying against official Redump and No-Intro catalogs.
  - Categorization of verification statuses: `MATCH`, `MISMATCH`, and `NA`.
  - Detailed reason classification in `Notes` for `NA` entries (e.g. compressed containers CHD/RVZ/CISO/PBP, multi-track CUE/GDI descriptors, headered NES/SNES ROMs, missing DAT).
  - Automated DAT downloader fetching official Redump and No-Intro DAT packs into `./dats/`.
- **Secondary Manifest & Tracking Storage Mirroring (`RomLauncherGeneratorConfig`, `RomManifestCsvService`)**:
  - Added configuration toggle `EnableSecondaryManifestStorage` and folder path setting `SecondaryManifestDirectory`.
  - Automatically mirrors generated collection manifests to a secondary backup or global tracking folder with formatted naming `{ConsoleName}_{ManifestFileName}`.
- **Named Job Queues with Independent Schedules & Auto-Chaining FIFO Engine (`NamedJobQueue`, `SchedulerAndNotificationService`, `MainWindow.xaml.cs`)**:
  - Replaced single global job queue with unlimited named job queues (e.g. "Hourly Sync", "Nightly Transcode", "Default Queue").
  - Each named queue possesses its own independent schedule (Interval cadence, daily clock times, specific times list, days of week).
  - Top-level Queue Selector dropdown with inline collapsible schedule editor card, Clone queue button, and New queue creation.
  - Sequential Auto-Chaining FIFO Engine: If Queue A is currently executing and Queue B triggers on schedule, Queue B is placed in `_pendingQueueRuns` backlog and auto-launches sequentially the instant Queue A completes.

## [2.5.0] - 2026-10-28

### Added
- **Single-Instance Application Named Mutex (`App.xaml.cs`, `MainWindow.xaml.cs`)**:
  - Acquired Win32 Named Mutex `Global\Housekeeping_2_SingleInstance_Mutex` on startup to ensure only one instance of `Housekeeping.exe` executes per machine.
  - Added Windows broadcast message `HOUSEKEEPING_RESTORE_WINDOW` and `ShowWindow(SW_RESTORE)` / `SetForegroundWindow` activation hooks in `MainWindow.xaml.cs`, cleanly restoring and focusing the existing application window if a user launches a duplicate instance.
- **Pending Swaps Registry & Auto-Finalizer (`PendingSwapService.cs`, `pending_swaps.json`)**:
  - Implemented `PendingSwapService` to track encodes completed successfully that could not be immediately swapped into their final destination due to temporary locks held by active media servers (Plex, Jellyfin) or SMB shares.
  - Persisted pending items in `pending_swaps.json` with file paths, sizes, timestamps, and titles.
  - Added automatic background sweep and finalization: sweeps and commits pending files on application startup, at the completion of every pipeline run, and periodically via background timer when idle.
  - Added smart bypass in `MediaTranscoderModule` and `ProxyGeneratorModule`: detects existing healthy `.new` or pending swap files before starting HandBrake, commits them immediately if unlocked, or skips redundant re-encoding if still locked.
  - Protected pending swap files against accidental deletion during startup or background partial cleans in `NetworkSafetyService.DiscardPartialEncode` and `CleanupOrphanedPartials`.
- **Interactive Pending Swaps Dashboard Banner (`MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Added an alert banner to Tab 1 (`⚡ Job Queue`) displaying pending locked encodes with real-time file counts.
  - Provided action controls: `[ 🔄 Retry Swap ]` (immediate manual commit attempt), `[ 📁 View ]` (opens file in Windows Explorer), and `[ 🗑 Discard ]` (deletes working copy and clears registry).
- **Flexible Multi-Run Job Queue Scheduler (`AppConfig.cs`, `SchedulerAndNotificationService.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Expanded the Master Automated Schedule to support 3 scheduling modes:
    1. **Once Daily**: Triggers at a specified daily clock time (e.g. `03:00`).
    2. **Interval Cadence**: Triggers every *N* minutes/hours (presets: 15m, 30m, 1h, 2h, 4h, 6h, 12h, or custom minutes).
    3. **Multiple Clock Times**: Triggers across multiple specified clock times per day (e.g. `03:00, 12:00, 18:00`).
  - Added active weekday selectors and live next-run countdown status in the top header bar (`TxtScheduleStatus`).
- **Scheduler Overlap Guard (`MainWindow.xaml.cs`, `SchedulerAndNotificationService.cs`)**:
  - If a scheduled trigger becomes due while the job queue is actively running, the scheduler safely acknowledges and skips the run without stacking, queuing, or backlogging jobs.
  - Logs a warning notification in the terminal and advances schedule state to the next interval or clock time slot.

### Fixed
- **Attempt 15 Exception Escape in CommitPartialEncode (`NetworkSafetyService.cs`)**:
  - Removed `when (attempt < maxAttempts)` exception filter on inner retry loop that caused destination `IOException` on attempt 15 to bypass `.new` fallback handling and escape into the outer method catch block.
  - Guaranteed zero-loss fallback: when destination files are locked, completed encodes are preserved in `.new` stubs, registered with `PendingSwapService`, and return `true` so module callers never discard good work.

## [2.4.0] - 2026-10-28

### Added
- **Bottom Status Bar Verbosity Presets (`AppConfig.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Added 4-tier footer verbosity levels (`Errors Only`, `Warnings & Errors`, `Progress & Status`, `Everything`) with `◀` / `▶` stepping buttons and persistent config in `AppConfig.FooterVerbosity`.
  - The footer filter isolates bottom status bar notifications without affecting full terminal logging.
- **Global Bottom Bar Live Subprocess Progress (`MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Added live progress bar (`BarFooterProgress`) and status text (`TxtFooterProgress`) directly to the bottom status bar, visible across all tabs during encoding, compressing, and syncing operations.
- **Terminal Word Wrap (`MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Replaced horizontal scrollbars with automatic window-edge text wrapping using `DockPanel LastChildFill="True"`, `TextWrapping="Wrap"`, and `ScrollViewer.HorizontalScrollBarVisibility="Disabled"`.
  - Added a `☑ Word Wrap` toggle checkbox in the terminal toolbar.
- **Empty / Garbage File Sentinel (`NetworkSafetyService.cs`, Module Engines)**:
  - Added `NetworkSafetyService.PurgeZeroByteFileIfExists` to purge 0-byte corrupt output stubs before starting encodes or checking if up-to-date.
  - Added 0-byte source guards across `MediaTranscoderModule`, `ProxyGeneratorModule`, `ArchiveCompressorModule`, and `RomLauncherGeneratorModule` that skip corrupt 0-byte files with warnings instead of crashing tools.
- **Multi-File Game Archive Parent Folder Retention (`RomLauncherGeneratorModule.cs`)**:
  - Multi-file games (e.g., bin/cue, iso with m3u) retain their parent folder (`Game Title (USA) (PR)\...`) inside `.7z` archives while single ROM files remain at the root of the archive.
  - Auto-m3u generation dynamically accounts for internal folder prefixes.
- **Outdated Launcher & Shortcut Garbage Collection (`RomLauncherGeneratorModule.cs`)**:
  - Added `RomLauncherGeneratorModule.CleanupOrphanedLaunchersAndShortcuts` to audit existing `.bat` launcher scripts and Start Menu `.url` shortcuts, automatically pruning dead links when ROMs are deleted or renamed.
- **Cloud Sync Guided UX & Dynamic Remotes (`MainWindow.xaml`, `MainWindow.xaml.cs`, `DependencyCheckService.cs`)**:
  - Added inline `📁` browse button on local folder path cells in `GridCloudJobs`.
  - Added `rclone listremotes` dropdown and `🔄 Refresh Remotes` button in `Cloud Sync` to quickly insert configured remotes (`EmuDrive:`, `Server-FTP:`).
- **Multi-Path Save/State Sync with Per-Console Overrides (`AppConfig.cs`, `ConsoleSyncPathsWindow.xaml`, `MainWindow.xaml`)**:
  - Added multi-path sync table `GridRomDefaultSyncPaths` to the Game Launchers module, allowing multiple sync paths per job (battery saves, save states, memory cards, configs).
  - Added `OverrideJobSyncPaths` and `ConsoleSyncPathsWindow` to configure custom sync paths per console rule.

### Fixed
- **Resilient Partial Commit & Zero-Loss Fallback (`NetworkSafetyService.cs`)**:
  - In `CommitPartialEncode`: auto-creates destination directories, strips ReadOnly attributes, extends retry loop to 15 attempts (up to ~30s backoff) for files held by indexers/SMB, and falls back to `.new` without discarding good completed encodes.
- **Cancellation & Error Handling Polish (`MainWindow.xaml.cs`, Module Engines, `ProcessRunnerService.cs`)**:
  - Fixed stat reset bug during cancellation in `MainWindow.xaml.cs`: wrapped module execution in `try ... finally` so accumulated stats (`Processed`, `Skipped`) are never lost in `history.json` or cards.
  - Handled exit code `-999` cleanly as `Cancelled` across `MediaTranscoderModule`, `ProxyGeneratorModule`, `ArchiveCompressorModule`, `CloudAndHumbleSyncModules`, and `RomLauncherGeneratorModule` without inflating error counts.
  - In `ProcessRunnerService`: excluded HandBrake `0 decoder errors`, `0 errors`, and `errors: 0` from being falsely flagged as errors in stderr.

## [2.0.0] - 2026-10-27

### Changed
- **Portable & Generalized Refactor (`Models/`, `Services/`, `MainWindow`)**:
  - Removed all hardcoded personal paths, drive letters, session cookies, and Easter eggs from the codebase so the application starts completely clean and portable on a fresh install.
  - Standardized all C# classes, methods, properties, and UI labels with user-agnostic naming:
    1. `ProxyGeneratorModule` (**Video Proxy Generator**)
    2. `ArchiveCompressorModule` (**Video Archive Compressor**)
    3. `MediaTranscoderModule` (**Multi-Tier Media Transcoder**)
    4. `RomLauncherGeneratorModule` (**ROM & Emulator Launcher Generator**)
    5. `CloudSyncModule` (**Cloud Folder Sync (rclone)**)
    6. `HumbleSyncModule` (**Humble Bundle Library Sync**)
    7. `CustomScriptsModule` (**Custom Scripts & CLI Tasks**)

### Added
- **Profile Import & Export System (`ConfigService.cs`, `MainWindow.xaml.cs`)**:
  - Added `Import Profile` and `Export Profile` (`.hkprofile.json`) buttons to the top header bar, with an option on export to strip sensitive credentials (`SessionCookie` and `DiscordWebhookUrl`) for safe public sharing.
- **In-App HandBrake Encoding Profile Builder (`HandBrakeEncodingProfile`, `MainWindow.xaml`)**:
  - Added a built-in HandBrake profile builder (`Proxy_360p`, `Archive_LQ`, `Tier_144p`–`Tier_4K`, `Lang_Edition_480p`) that compiles standalone `HandBrakeCLI` arguments (`-e`, `--encoder-preset`, `-q`, `-X`/`-Y`, `-E`, `-B`, `--audio-lang-list`, `--subtitle-burned`) without requiring HandBrake GUI presets to be installed, while retaining an optional GUI preset override.
- **Portable `./tools/` Directory & External Dependency Checker (`DependencyCheckService.cs`)**:
  - Added automatic `./tools/` directory creation and dependency resolution hierarchy (`Custom Path` → `./tools/` → `System PATH` → `Program Files`) for `HandBrakeCLI`, `7z.exe`, `rclone.exe`, `hbd.exe`, and `MediaInfo.dll`.
- **Multi-Tier Media Transcoder Generalizations (`MediaTranscoderModule.cs`)**:
  - Added dual source detection modes: **Filename Tag Matching** (with user-customizable Source Quality Tiers and Master File Tag) and **MediaInfo Source Resolution Auto-Detection** (inspecting actual source video height in pixels via `MediaInfo.dll` and selecting target resolution ladders automatically).
  - Added support for both custom scan folders and drive-letter + relative subfolder scanning.
  - Generalized alternate audio language edition rules (`LanguageEditionRule`).
- **ROM & Emulator Launcher Generator Enhancements (`RomLauncherGeneratorModule.cs`)**:
  - Added **Automatic `.m3u` Playlist Generation** (`AutoGenerateM3uForMultiDisc`) for multi-disc games both in loose folders and dynamically for multi-disc archives on launch.
  - Added **7-Zip Auto-Archiving with Deep Supplemental File Bundling** (`AutoCompressUnarchivedRomsWith7z`, `DeleteUncompressedOriginalsAfter7z`, & `ResolveSupplementalFilesForBundle`): automatically bundles `.m3u` playlists, all referenced multi-disc `.cue`/`.gdi`/`.chd`/`.iso` files, multi-track `.bin`/`.raw`/`.wav` tracks parsed directly from `.cue` and `.gdi` descriptors, and sibling `.sbi`/`.sub`/`.ccd`/`.mds`/`.mdf` files into a single `.7z` archive using atomic `.partial.7z` verification.
  - Added clean built-in launch command templates for major standalone emulators (`RetroArch`, `PCSX2`, `DuckStation`, `RPCS3`, `PPSSPP`, `Dolphin`, `Flycast`, `Redream`, `Project64`, `melonDS`, `Citra`, `Cemu`, `Xemu`, `Xenia`, and `Custom` CLI templates).
  - Added a `"Load Standard Console Templates..."` helper button that populates 18 console rules mapped to user-selected ROM and core folders.
- **Automated Test Suite (`Housekeeping.Tests`)**:
  - Created xUnit test project (`Housekeeping.Tests.csproj`) covering clean portable defaults, profile sanitization, in-app HandBrake CLI argument compilation, multi-disc `.m3u` generation, deep supplemental ROM bundling (`.m3u` + `.cue` multi-track `.bin` + `.sbi` + `.gdi` tracks), standalone emulator command builders, and atomic `.partial` file safety.
- **Multi-Job Cloud Folder Sync (`CloudSyncModule.cs`)**:
  - Generalized `rclone` integration to support multiple configurable sync jobs (`bisync`, `sync`, `copy`) with a `--resync` recovery trigger.
- **First-Run Setup & Overnight Quality-of-Life Enhancements (`MainWindow.xaml`, `MainWindow.xaml.cs`, `UiOptionsCatalog`, `NativeProcessSuspend.cs`)**:
  - **Batch Subfolder Discovery & Pairing**: Added `+ Add All Subfolders of a Parent Directory...` to Video Proxy Generator and `+ Pair All Subfolders of Source & Archive Parents...` to Video Archive Compressor so users can populate dozens of folder targets/pairs in a single click.
  - **Dropdown Selectors & File Browser Buttons**: Replaced free-text fields with `ComboBox` / `DataGridComboBoxColumn` selectors for HandBrake Profile IDs, Emulator Runners, `rclone` Sync Modes, and HandBrake Video/Audio/Container/Preset/Subtitle fields, and added file-picker `Browse...` buttons for Emulator Executables, Libretro Core `.dll`s, and custom CLI tool path overrides (`HandBrakeCLI.exe`, `7z.exe`, `rclone.exe`, `hbd.exe`).
  - **Prevent Windows Sleep & Post-Run Power Actions**: Added Win32 `SetThreadExecutionState` wake-lock during active pipeline runs and a top-bar `After Run` selector (`Stay Awake`, `Sleep PC`, `Shut Down`) for overnight encoding batches.
  - **Free Disk Space on Drive Status Badges**: Updated the top header drive status badges and tooltips to display live available free disk space (e.g., `D: 420 GB`).
  - **Per-Source & Per-Resolution HandBrake Profile Matrix (`SourceQualityTierRule`, `MediaTranscoderModule.cs`, `MainWindow.xaml`)**: Replaced the single CSV column in `GridTranscoderTiers` with 7 explicit dropdown columns (`144p Profile`, `240p Profile`, `360p Profile`, `480p Profile`, `720p Profile`, `1080p Profile`, `4K Profile`) so each source quality tier (e.g., `DVD` vs `BR` vs `UHD`) can independently select any specific HandBrake profile (or `(Disabled)`) for every target resolution.
  - **One-Click HandBrake Preset Importer & Dynamic Custom/GUI Inspector (`HandBrakeEncodingProfile`, `MainWindow.xaml`, `MainWindow.xaml.cs`)**:
    - Added **`📥 Import HandBrake .json File...`** (which parses exported HandBrake `.json` files, copies them to `./tools/presets/` for portability, and runs them with `--preset-import-file`) and **`🔄 Import Installed HandBrake GUI Presets`** (which automatically imports user presets from `%APPDATA%\HandBrake\presets.json`).
    - Replaced the wide mixed DataGrid in `HandBrake Profiles & Tools` with a clean Master Profile Table + Selected Profile Inspector that automatically hides custom in-app encoder controls when `Use HandBrake GUI / Imported Preset` is checked (and hides GUI preset fields when unchecked).
  - **User-Defined Job Queue & Master-Detail Job Builder (`HousekeepingJob`, `AppConfig.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`)**:
    - Replaced the fixed 7-module Dashboard and cramped 7-subtab bar with a flexible **Job Queue** (`List<HousekeepingJob>`) and a **Master-Detail Job Builder** (`⚙ Job Builder & Setup`).
    - Users can create any number of jobs, reuse the same module across multiple jobs with different folders/settings (`Clone`), add standalone `.bat`/`.cmd`/`.ps1` scripts directly to the queue (`⚡ + Quick .bat/.ps1`), and reorder execution (`▲`/`▼`) from either the Dashboard or the Job Builder.
    - Added automatic migration (`MigrateLegacyModulesToJobsIfNeeded`) so existing `settings.json` configurations migrate seamlessly into jobs on startup.
  - **Guided Emulator Save & Memory Card Sync Builder (`RomLauncherGeneratorConfig`, `MainWindow.xaml`, `MainWindow.xaml.cs`)**:
    - Replaced raw pre/post command textboxes in the ROM & Emulator Launchers module with a guided 3-step save sync panel (`1. Local Emulator Saves Folder` with `Browse...`, `2. Cloud Remote Destination`, `3. rclone Sync Mode` (`bisync`/`sync`/`copy`), and `📥 Copy From Cloud Sync Job...`), plus live-generated `Pre-Launch` and `Post-Launch` command previews.
  - **Dark-Mode WPF Control Templates (`App.xaml`)**:
    - Added custom dark-theme `ControlTemplate`s for `ComboBox` and `ComboBoxItem` (eliminating Windows default light-gray button faces that clashed with white text) and increased `DataGrid` row height and vertical alignment for clear readability.

### Fixed
- **Non-Blocking Background Thread Execution & Live Scan Progress (`MainWindow.xaml.cs`, `MediaTranscoderModule.cs`)**:
  - Wrapped pipeline module execution (`ExecutePipelineAsync`) and drive status polling (`RefreshDriveStatuses`) inside `Task.Run` background threads so recursive directory crawls (`EnumerateFiles`/`GetFiles`), `Directory.Exists`, and `MediaInfo.dll` inspection across mapped network drives (`V:`, `W:`, `X:`, `Y:`, `Z:`) never block the WPF UI thread or delay terminal output rendering.
  - Added `ScanFilesWithProgress` in `MediaTranscoderModule` to log live subfolder scan progress to the terminal and check `CancellationToken` between subfolders for instant cancellation.
- **Direct CLI Executable Launch & Quote Preservation (`ProcessRunnerService.cs`)**:
  - Fixed `'C:\Program' is not recognized as an internal or external command` when running `HandBrakeCLI.exe` from `C:\Program Files\HandBrake\HandBrakeCLI.exe` with media paths containing spaces and parentheses (such as `"Danny Phantom (2004)"`).
  - Added `ProcessRunnerService.SplitCommandLine` so CLI tools (`HandBrakeCLI.exe`, `7z.exe`, `rclone.exe`, `hbd.exe`) are invoked directly (`FileName = exePath`, `Arguments = exeArgs`) without `cmd.exe /c` stripping outer quotes, and updated `cmd.exe` script invocations to use `/d /s /c "\"...\""`.
- **Single-Pass Network Drive Master Scan (`MediaTranscoderModule.cs`)**:
  - Replaced repeated per-extension × per-source-tag directory crawls (`9` separate network crawls per drive) with `ScanAllMasterFilesInRootOnce`, scanning each media root once per run and matching source quality tiers in memory (~9x faster network drive scanning).
- **Rendered UI Readability, Text Contrast & Responsive Layout Fixes (`MainWindow.xaml`, `App.xaml`, `MainWindow.xaml.cs`)**:
  - Fixed dark-on-dark text contrast in the Job Builder: WPF's default `TabControl` forced a black `#000000` foreground onto child text elements, making unstyled `TextBlock` labels unreadable; resolved by explicitly setting light-mode foreground inheritance across all tab controls and text blocks.
  - Added a visual `OFF (DISABLED)` badge and dimming opacity (`CardOpacity = 0.55`) to unchecked job cards on the Dashboard so inactive jobs are immediately distinguishable from active ones.
  - Converted the top header drive status pill container to an adaptive `WrapPanel` so all 9+ connected drives (`C:` through `Z:`) display without being clipped off the header bar.
  - Restructured the built-in terminal toolbar into a clean two-row layout preventing the filter search box from overlapping the terminal checkbox and control buttons.
  - Converted all module footer button bars (`GridArchiveMappings`, `GridRomCollections`, etc.) to `WrapPanel` so buttons never clip beyond the right edge of the window.
  - Added visual warning indicators (`⚠ Profile '...' not found — pick one`) when a module job references a HandBrake profile ID that no longer exists in the imported presets table.


## [2.3.0] - 2026-10-28

### Changed
- **Immutable Module Type in Job Builder (`MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Replaced the mutable `CmbSelectedJobModuleType` ComboBox with a styled, read-only module identity badge (`TxtSelectedJobModuleBadge`, e.g., `[📺 Media Transcoder]`, `[🎮 Game Launchers]`, `[🎬 Proxy Gen]`).
  - Removed the `CmbSelectedJobModuleType_SelectionChanged` event handler and eliminated `_selectedJob.ModuleType` reassignment in `SyncSelectedJobInspectorToModel()`, guaranteeing that a job's module type is strictly immutable once created.
- **Standardized Clean Module Naming Across UI & Engine (`AppConfig.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `DependencyCheckService.cs`)**:
  - Standardized all module names across the app UI (job cards, modules catalog, job builder tabs, dropdowns, logs) to the clean, forward-looking naming scheme:
    1. **`Proxy Gen`** (🎬) *(formerly `Video Proxy Generator` — expanded scope beyond video into audio drafts and preview proxies)*
    2. **`Video Archiver`** (📦) *(formerly `Video Archive Compressor`)*
    3. **`Media Transcoder`** (📺) *(formerly `Multi-Tier Media Transcoder`)*
    4. **`Game Launchers`** (🎮) *(formerly `ROM Archiver & Emulator Launchers` / `Retro Launchers` — expanded scope to support native PC `.exe` games alongside emulated console titles)*
    5. **`Cloud Sync`** (☁) *(formerly `Cloud Folder Sync (rclone)`)*
    6. **`Humble Library`** (📚) *(formerly `Humble Bundle Library Sync`)*
    7. **`Custom Tasks`** (⚡) *(formerly `Custom Scripts & CLI Tasks`)*
  - Updated all 7 Job Builder sub-tab headers, guide banners, modules catalog cards, and creation dropdowns (`CmbDashboardNewJobType`, `CmbBuilderNewJobType`).

### Added
- **Permanent Module Reference Guide (`MODULES_REFERENCE.md`)**:
  - Created a dedicated, comprehensive reference manual in the project root (`MODULES_REFERENCE.md`) specifying the architectural philosophy, core goals, inputs, outputs, boundaries, invariants, and future roadmaps for all 7 pipeline modules, plus standardized guidelines for adding future modules.
- **Unit Test Coverage for Clean Naming & Legacy Migration (`HousekeepingTests.cs`)**:
  - Added test cases verifying that `GetModuleTypeDisplayName()` strictly maps all 7 `ModuleId` entries to the agreed clean names and that `MigrateLegacyModulesToJobsIfNeeded()` generates starter jobs with clean names.
  - All 26 unit tests pass cleanly.

### Tested & Deployed
- Built, tested, and published Release build to `D:\Programs\Housekeeping`.
- Captured updated UI screenshots in `ui_captures` confirming proper badge layout, zero horizontal scrollbars, and clean typography.


## [2.2.0] - 2026-10-28

### Added
- **Modern Application Branding & Executable Icon (`Assets/`, `Housekeeping.csproj`, `MainWindow.xaml`)**:
  - Designed and generated high-resolution application icon and branding assets (`Assets/app_icon.png` 512x512 and `Assets/app_icon.ico` multi-resolution icon covering 16x16 to 256x256).
  - Set `app_icon.ico` as the native Windows application icon in `Housekeeping.csproj` (`<ApplicationIcon>`), embedded `app_icon.png` as an application resource, and applied it to the Window and system tray icons.
  - Replaced the generic `HK 2.0` text badge in the top-left header with the new custom logo image.
- **Multi-Machine Distributed File Locking (`NetworkSafetyService.cs`, `MainWindow.xaml`, `AppConfig.cs`)**:
  - Created companion `.hklock` metadata engine to prevent transcode and file writing collisions when multiple computers run Housekeeping over shared network storage (`V:`, `W:`, `Z:`, or UNC paths).
  - Atomic OS-level lock acquisition using `FileMode.CreateNew` with machine name, username, process ID, and UTC timestamp metadata.
  - Automatic stale lock detection and override for abandoned locks older than `DistributedLockTimeoutMinutes` (default 90 min), and integrated stale `.hklock` sweeping into `CleanupOrphanedPartials`.
  - Added configuration controls under `⚙ Settings -> 🕒 Scheduler & Network Safety`.
- **Settings Schema Versioning & Sequential Auto-Migration (`AppConfig.cs`, `ConfigService.cs`, `MainWindow.xaml.cs`)**:
  - Added `ConfigVersion` (schema v2) to `AppConfig`.
  - Implemented automatic timestamped backups (`settings.json.bak_v1_<timestamp>`) created in the application root prior to applying any schema upgrades.
  - Built sequential migration engine (`ConfigService.MigrateConfig`) that automatically upgrades legacy v1 configs to v2, initializes new defaults, preserves all existing user jobs and custom profiles, and logs migration status to the live terminal.
- **Dynamic New Job Type Selectors (`MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Replaced hardcoded ComboBox choices with dynamic data-bound selectors (`CmbDashboardNewJobType` and `CmbBuilderNewJobType`) populated exclusively with modules that are currently enabled app-wide.
  - Disabled modules disappear from job creation dropdowns immediately upon being toggled off.

### Fixed
- **Resilient `.partial` to Destination File Renaming (`NetworkSafetyService.cs`)**:
  - Fixed Windows/SMB file locking issue during transcode finalization where indexers (Windows Search, Plex scanner), antivirus (Windows Defender), or network latency temporarily locked newly finished video files, causing `.partial` renames to fail and discard transcodes.
  - Upgraded `CommitPartialEncode` with an atomic 10-attempt exponential backoff retry loop with overwrite support.
- **Accurate CLI Tool Detection in Modules Catalog (`MainWindow.xaml.cs`)**:
  - Fixed bug where the Modules tab falsely reported tools like `HandBrakeCLI.exe` as "Missing or Unconfigured" when detected via System PATH or `C:\Program Files\HandBrake\HandBrakeCLI.exe`.
  - Wired module prerequisite status directly into `_dependencyService.ResolveTool()` search hierarchy (`Custom Path` → `./tools/` → `System PATH` → `Program Files`), displaying exact resolved paths.
- **Modules Tab Resizability & Layout (`MainWindow.xaml`)**:
  - Added a draggable `GridSplitter` between the left modules catalog and the right documentation pane.
  - Widened default catalog column to 420px (min 300px, max 800px) and disabled horizontal scrollbars on both panes with clean text wrapping.
- **Strict App-Wide Module Disabling (`MainWindow.xaml.cs`, `ModuleCardViewModel`)**:
  - Blocked execution of jobs belonging to app-wide disabled modules in `StartPipelineRun`, logging clear skip warnings to the terminal (`[Skip] Job 'X' skipped: Module 'Y' is disabled app-wide in Modules.`).
  - Added visual `[MODULE DISABLED]` red badges to disabled job cards and disabled their individual `▶ Run` buttons with descriptive tooltips.
- **Real-Time Terminal Bottom Bar & Dynamic Session Counters (`MainWindow.xaml.cs`, `ModuleExecutionContext.cs`)**:
  - Wired `TxtFooterStatus.Text` directly to `AppendLog` so the bottom status bar mirrors the most recent terminal output in real time.
  - Connected `ModuleRunStats` via `OnStatsDelta` callback so footer session totals (`Processed: X | Skipped: Y | Errors: Z`) update immediately upon each file/job completion rather than waiting for an entire pipeline batch to finish.
- **Streamlined Navigation Header Wordiness (`MainWindow.xaml`)**:
  - Renamed `📜 Run History & Alerts` tab to `📜 History` for a cleaner, compact UI header.

### Tested & Deployed
- Expanded automated unit test suite (`Housekeeping.Tests`) to 24 tests covering schema migrations, distributed locking, partial move retries, and module state toggling.
- Published clean Release build to `D:\Programs\Housekeeping`, safely upgrading existing user settings to schema v2 with an automatic backup (`settings.json.bak_v1_...`).


## [2.1.0] - 2026-10-28

### Fixed
- **Critical Fix: "Run Enabled" Queue Disabling Bug (`MainWindow.xaml.cs`)**:
  - Removed erroneous `_selectedJob.Enabled = ChkSelectedJobEnabled.IsChecked == true;` invocation inside `SyncSelectedJobInspectorToModel()` that was unintentionally overwriting user-checked queue checkboxes with inactive inspector UI states whenever `StartPipelineRun` was triggered.
  - Bidirectionally synchronized `ModuleCardViewModel.IsEnabled` and `ChkSelectedJobEnabled.IsChecked` so toggling job checkboxes on the dashboard or in the inspector stays in perfect sync and never gets reset to false on execution.
- **Fixed Black Text in Job Builder (`App.xaml`, `MainWindow.xaml`)**:
  - Added explicit global WPF dark-mode styles in `App.xaml` for `TextBlock`, `Label`, `ListBoxItem`, `GroupBox`, `RadioButton`, `Expander`, `ToolTip`, `DataGrid`, `DataGridCell`, and `DataGridRow`.
  - Added explicit `Foreground="{StaticResource TextPrimary}"` to over 30 text elements and checkboxes throughout all 7 module views in Job Builder that previously defaulted to black text against dark backgrounds.

### Added
- **Interactive Built-In Terminal CLI (`MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Upgraded the Terminal panel with an interactive command line interface input dock (`CLI >`) with an execution `▶ Run` button and active command cancellation `⏹ Stop` button.
  - Implemented command execution streaming standard output and error asynchronously via `cmd.exe /c` with full UTF-8 encoding support.
  - Added command history navigation via Up and Down arrow keys.
  - Added built-in commands: `cls`/`clear` to clear output, `help` for command reference, `cd <path>` to change working directories, and `pwd` to print current directory.
- **Dedicated Application Modules Catalog (`MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Added a dedicated `🧩 Modules` tab providing comprehensive in-app documentation and app-wide control for all 7 modules:
    1. `🎬 Video Proxy Generator` (Video editing proxy workflow)
    2. `📦 Video Archive Compressor` (Storage optimization)
    3. `📺 Multi-Tier Media Transcoder` (Home server & streaming)
    4. `🎮 ROM & Emulator Launchers` (Retro gaming & preservation)
    5. `☁ Cloud Folder Sync (rclone)` (Cloud storage & bisync)
    6. `📚 Humble Bundle Sync` (Digital purchases archive)
    7. `📜 Custom Scripts & CLI Tasks` (Power user automation)
  - Added master-detail layout displaying module status badge (`ACTIVE` / `DISABLED`), app-wide toggle switch (`Active App-Wide`), real-time CLI dependency detection banner (`✓ HandBrakeCLI detected` / `⚠ Missing tools`), key capabilities, step-by-step setup guides, pro tips & best practices, and a `+ Add Job for This Module` quick-creation button.

### Changed
- **Navigation & Tab Consolidation (`MainWindow.xaml`, `MainWindow.xaml.cs`)**:
  - Renamed `Custom Automation Job Queue` on the dashboard to `Job Queue`.
  - Renamed `Built-In Terminal & Live Output` to `Terminal`.
  - Consolidated `HandBrake Profiles & Tools` and `Scheduler & Network Safety` under a single unified `⚙ Settings` tab with clean, dark-mode sub-tab pills (`🎬 Tools & HandBrake Profiles` and `🕒 Scheduler & Network Safety`).
  - Reorganized main tabs into 5 clear sections:
    1. `⚡ Job Queue`
    2. `🧩 Modules`
    3. `🛠 Job Builder`
    4. `⚙ Settings`
    5. `📜 Run History & Alerts`
- **Published Updated Application (`D:\Programs\Housekeeping`)**:
  - Published clean Release build to `D:\Programs\Housekeeping`, safely preserving user configuration and history files (`settings.json`, `history.json`).

## [2026-11-02]
- Workspace Hygiene (/clean): purged 4.32 MB of disposable caches; created version snapshot Housekeeping-v1.0.0-rc.10-2026-11-02.7z.

