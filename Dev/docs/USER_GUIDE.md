# Housekeeping — End-User Manual & Complete UI Guide

- **Document:** `USER_GUIDE.md` (Technical Manual 2 of 4)
- **Active Build Version:** `v0.2.0`
- **Default Desktop UI:** Disposable Edge WebView2 Standalone Window (`DefaultDesktopLaunchMode: "StandaloneWindow"`) + System Tray Companion (`http://127.0.0.1:5178`) or embedded inside `Mother` at `/app/housekeeping/`
- **Remote / Mobile UI:** LAN Web Browser (`http://<host>:5178`) & Tailscale Serve HTTPS PWA (`https://<node>.<tailnet>.ts.net` with offline QR pairing)

---

## 1. What It Is & Who It's For

**Housekeeping** is your always-on background automation companion for media libraries, retro and modern game ROM collections, bit-rot checksum auditing, and multi-drive cloud backups. It runs quietly in the Windows system tray (or supervised inside the `Mother` suite gateway) and exposes a fast, keyboard-driven control center in a standalone desktop window or mobile Progressive Web App (PWA).

### Core Capabilities at a Glance (The 8 Automation Modules)
1. **🎬 Module 1 — Proxy Media Generator (`ProxyGenerator`):** Scans raw capture/working footage folders and encodes lightweight editing proxies (such as `Clip01 (360p_proxy).mp4`) right next to source clips using `HandBrakeCLI.exe`.
2. **📦 Module 2 — Video Archive Compressor (`ArchiveCompressor`):** Compresses finished production footage folders into long-term HEVC/AV1 archive directories while preserving subfolder hierarchy.
3. **📺 Module 3 — Multi-Tier Media Transcoder (`MediaTranscoder`):** Scans configured media drives and custom folders for master files (`(Original)` or dynamic `MediaInfo.dll` stream height) and builds multi-resolution streaming tiers (`144p` through `4K UHD`) plus alternate-language audio editions with atomic `.partial` writes, `.redroom-part` / `{edition-*}` / `.srt` coexistence guards, and automatic **Pending Swaps** when a media server holds a file lock.
4. **🎮 Module 4 — ROM & Game Launchers (`RomLauncherGenerator`):** Generates `.m3u` multi-disc playlists, packs loose ROMs into solid `.7z` archives (excluding `.hash`, `.partial*`, `.hklock`, and `.archive_hashes.json`), and writes per-game `.bat` and Start Menu `.lnk` launchers across 24 emulator runners with fast `7z l -slt` header inspection, SSD scratch extraction, and `<dataDir>\rclone\`-contained save sync—completely decoupled from DAT and `.hash` sidecar manipulation.
5. **☁ Module 5 — Cloud Folder Sync (`CloudSync`):** Runs two-way `rclone bisync`, one-way `sync`, or `copy` jobs across cloud and NAS remotes using isolated `<dataDir>\rclone\` config/lock state with automatic stale `.lck` recovery.
6. **📚 Module 6 — Humble Bundle Sync (`HumbleSync`):** Incrementally downloads purchased Humble Bundle eBooks, audiobooks, comics, and DRM-free games using `hbd.exe`.
7. **⚡ Module 7 — Custom Scripts & CLI (`CustomScripts`):** Executes ordered `.bat`, `.cmd`, `.ps1`, `.py`, or `.exe` tasks with live terminal streaming, working directory control, and optional required network path checks.
8. **📋 Module 8 — Media Manifest & Bit Rot Auditor (`CollectionManifest`):** Recursively inventories media, ROM, and general storage roots into delimiter-aware `.csv` / `.tsv` (`CsvDelimiter`), `.json`, and visual `.html` catalogs, runs custom C# Regex extraction rules with a live playground, and verifies/auto-heals 4-mode `.hash` sidecars (`DatabaseOnly`, `SidecarBeside`, `PackageInsideArchive`, `SidecarAndArchive`) with post-injection `.archive_hashes.json` tracking, two-step outer+inner archive checks, composite folder dumps (`PS3_GAME`, Wii U, Switch, Xbox 360, `BDMV`, `VIDEO_TS`), and ShadowLibrary zero-rewrite parity.

---

## 2. Install, Upgrade & Uninstall

### 2.1 Installing or Upgrading with the Setup Wizard
1. Download **`Housekeeping-Setup-v0.2.0-win-x64.exe`** (or **`Housekeeping-v0.2.0-win-x64-Portable.zip`**) from GitHub Releases (`Releases\Latest\`).
2. **Welcome, Profile & Target Location:** Choose a profile (`lightweight`, `full` default, or `custom`) and install path. Fresh installations default to `%LOCALAPPDATA%\Programs\StreamItAll\Mother\Housekeeping\` (or pass `--suite-dir` / custom folder).
3. **Optional Legacy Migration (`--migrate-legacy`):** If upgrading from an older standalone flat directory, enable legacy migration to copy existing `data\` state safely and retire legacy `Housekeeping_Watchdog` Scheduled Tasks.
4. **Automatic State & Checkpoint Preservation:** If Housekeeping is already running, the installer triggers a graceful daemon shutdown (`InitiateGracefulShutdownAbort`), preserving active `data\interrupted_run_checkpoint.json` and `Up Next` queue runs, preserves `data\settings.json`, `.hk_cookie_secret.key`, `rclone\`, `history.json`, `compliance_reports.json`, `dats\`, `manifests\`, `backups\`, and `logs\`, updates `app\` binaries, seeds `data\settings.example.json` if missing, and relaunches cleanly.

### 2.2 Unattended / CLI Installer Commands
```powershell
# Verify embedded payload SHA-256 integrity without writing to disk
.\Housekeeping-Setup-v0.2.0-win-x64.exe --verify

# Silent install or upgrade under default Mother suite umbrella and launch
.\Housekeeping-Setup-v0.2.0-win-x64.exe --silent --suite-dir "$env:LOCALAPPDATA\Programs\StreamItAll\Mother" --profile full --launch

# Silent install with opt-in legacy migration from an older flat install
.\Housekeeping-Setup-v0.2.0-win-x64.exe --silent --migrate-legacy --launch

# Uninstall binaries while preserving data\ and logs\
.\Housekeeping-Setup-v0.2.0-win-x64.exe --uninstall --install-dir "$env:LOCALAPPDATA\Programs\StreamItAll\Mother\Housekeeping"

# Full uninstall including user data, settings, and logs
.\Housekeeping-Setup-v0.2.0-win-x64.exe --uninstall --install-dir "$env:LOCALAPPDATA\Programs\StreamItAll\Mother\Housekeeping" --purge-data
```

---

## 3. First-Run Setup & Starter Jobs

1. **Launch Housekeeping:** Run `<InstallRoot>\Housekeeping.exe` (or open Housekeeping directly from the `Mother` shell at `/app/housekeeping/`). Standalone mode opens in a dark-themed **Standalone WebView2 Window** (`http://127.0.0.1:5178`).
2. **Verify External CLI Tools (`Settings` -> `🔧 Tools & HandBrake Profiles` or `Housekeeping.exe --doctor`):**
   - Inspect status indicators for `HandBrakeCLI.exe`, `7z.exe`, `rclone.exe`, `hbd.exe`, and `MediaInfo.dll`.
   - Housekeeping auto-detects executables in `<InstallRoot>\data\tools\`, `<InstallRoot>\app\tools\`, `<InstallRoot>\app\`, standard install folders, and `PATH`. Note: if `MediaInfo.dll` is not present on disk, quality tier probes report explicit tool unavailability rather than guessing codec status.
3. **Reference Template (`data\settings.example.json`):** A clean, zero-PII `settings.example.json` reference file is staged into `app\config\` and seeded to `<dataDir>\settings.example.json` if missing.
4. **Populate Starter Jobs (`Job Builder` -> `+ Add All 8 Starter Jobs`) & Dry-Run First (`🧪 Dry Run`):**
   - Seed starter jobs across all 8 modules and always test new rules with **🧪 Dry Run** first so planned operations appear in `[DRY]` tags without altering files.

---

## 4. Screen-by-Screen Tour

### 4.1 Global Shell: Left Sidebar Rail, Mobile Bottom Nav, Top Command Bar & Resizable Terminal
- **Left Sidebar Navigation Rail (`220px` expanded / `56px` icon rail, `Ctrl+B` toggle):**
  - Switch between **`1` 📊 Dashboard**, **`2` 🗂 Job Queues**, **`3` 🛠 Job Builder**, **`4` 🧩 Modules**, **`5` 🛡 Reports**, **`6` ⚙ Settings**, and **`7` 📜 History**.
  - **Quick Utilities:** **🔍 Search (`Ctrl+K`)** opens Global Omni-Search; **⌨️ Shortcuts (`?`)** opens the keyboard shortcut cheat-sheet.
  - **💾 Consolidated Mounted Storage Strip & `DrivesTableModal`:** Shows local and NAS drives with free-space and color health indicators (`Green` healthy, `Amber` `<12%`, `Red` `<25 GB` or `<5%`, `Offline`).
- **Docked Mobile Bottom Navigation Bar (`<= 820px`):** Fast thumb switching across primary views, search, and storage sheets on phones and tablets.
- **Top Command Bar & Extracted PWA Controls (`TopBarPwaControls`):**
  - **Target Node Switcher:** Switches the UI between `Local` and any configured remote LAN/Tailnet Housekeeping node.
  - **Auto-Save Status Pill & Snapshot History (`Ctrl+S`, `↺ History`):** Debounced `650ms` save with one-click restore across the 20 newest `settings.json` snapshots.
  - **📥 Import / 📤 Export:** Import profiles or export **🛡 Shareable (Sanitized)** profiles (stripping Humble cookies, Discord webhooks, and API tokens) or full backups.
  - **PWA, QR & Theme Controls:** **📲 Install App**, HTTPS PWA toggle, UI Density (`Compact`, `Comfortable`, `Touch`), and `Light` / `Dark` theme switch—fully subpath-aware when accessed via `Mother` at `/app/housekeeping/`.
  - **Execution & Dual-Mode Stop Controls:** **▶ Run Enabled** / **⏭ Queue Next**, **🧪 Dry Run**, **⏸ Pause Queue**, **❄ Suspend Process**, **⏹ Stop Job** (cancels active job only, advances queue, keeps `Up Next`), and **⏹ Stop Queue** (clears `Up Next` & checkpoint).
- **Interrupted Mid-Shutdown Run Recovery Banner:** Offers **▶ Resume Remaining Jobs**, **↺ Restart Entire Queue**, **Auto-restart on startup**, and **✕ Dismiss** after graceful upgrades or reboots.
- **Global Live Terminal Drawer (`Ctrl+\``):** `S` / `M` / `L` height presets + drag resize handle, Verbosity filter, and Session Totals.

### 4.2 📊 View 1 — Dashboard (`DashboardPage`)
- **Banners:** **🤝 Sister-App Coexistence Warning Banner** (1-click presets for `ShadowLibrary`, `Redroom`, `uStation`, `StreamEngine`), **🌐 Waiting for Network**, **⏳ Deferred Pending Swap(s) Ready** (**🔄 Commit Swaps Now** / **🗑 Discard**), active progress bar, and FIFO **⏭ Up Next** runs.
- **4 Live KPI Cards:** Active Pipeline Engine, Host CPU & Memory, Disk I/O & Network Throughput, and Archive Compliance & Integrity.
- **Named Job Queues & Dual-Pane Live Terminal:** Single or split-pane live logs across Local or Remote Fleet Nodes with ring-buffer-safe auto-scroll and an interactive PowerShell/CMD prompt (localhost-restricted by default).

### 4.3 🗂 View 2 — Job Queues (`JobQueuesPage`)
- Create/edit named queues, toggle **Enable Automatic Schedule** (`Daily`, `Interval`, or `Multiple Specific Times`), select days of the week, preview **Next 5 Calculated Runs**, reorder queue steps (`↑ / ↓`), or export queue JSON.

### 4.4 🛠 View 3 — Job Builder (`JobBuilderPage` & 8 Module Editors)
- Create, filter, sort, and reorder jobs across all 8 modules with `ProfileSelect` `<select>` guards (flagging stale profiles with `⚠ Missing`) and 2-line hover hints on every toggle and column header:
  1. **Proxy Gen:** HandBrake profile, suffix tag (`360p_proxy`), extensions, and footage folders.
  2. **Video Archiver:** HandBrake profile, output container, skip tag, and `Source -> Archive` mappings.
  3. **Media Transcoder:** `FilenameTagMatching` vs `MediaInfoResolutionScan`, `(Original)` master tag, drive list, `144p`–`4K UHD` profile matrix, and alternate language edition rules.
  4. **ROM & Game Launchers (Decoupled):** `.m3u`, `.7z`, `7z l -slt`, SSD scratch extraction, `.bat`/`.lnk` launchers across 24 runners, and `<dataDir>\rclone\`-contained save sync (pointing DAT/sidecar auditing to Module 8 or `ShadowLibrary`).
  5. **Cloud Sync:** Refreshes remotes from `<dataDir>\rclone\`, runs `--resync`, and manages `bisync`/`sync`/`copy` pairs.
  6. **Humble Library:** Masked `_simpleauth_sess` cookie, download path, and extra CLI args.
  7. **Custom Tasks:** Ordered `.bat`/`.ps1`/`.py`/`.exe` tasks with working directory, required path check, and external CMD option.
  8. **Collection Manifest & Bit Rot Editor:** `.csv`/`.tsv`/`.json`/`.html` exports, Greenroom `:8420` sync, 4-mode `.hash` placement (`0..3`), `.archive_hashes.json` post-injection sync, `SkipShadowLibraryManagedRoots`, and live C# Regex Playground.

### 4.5 🧩 View 4 — Modules Catalog (`ModulesCatalogPage`)
- Top readiness bar for `HandBrakeCLI`, `7z`, `rclone`, `hbd`, and `MediaInfo.dll`; left module switcher with app-wide master toggles; right setup guide and **+ Add Job for `<Module>`**.

### 4.6 🛡 View 5 — Reports & Compliance Center (`ReportsCenterPage`)
- KPI summary cards, filters (`Console`, `Category`, `Status`), **🔍 Scan Archives Now**, **🧪 Preview 7z d Cleanup** (`ReportCleanupDryRunModal` protecting primary ROMs/tracks), **🧹 Clean Extra Files (7z d)**, **🔧 Heal .hash Sidecar**, **🙈 Ignore**, and CSV/JSON export.

### 4.7 ⚙ View 6 — Settings (`SettingsPage` — 8 Sub-Tabs)
1. **🔧 Tools & HandBrake Profiles:** Override tool paths and manage the HandBrake profile catalog.
2. **🛡 Startup & Process Guard:** Windows logon startup, silent tray startup, auto-restart interrupted runs, and guarded process list (`obs64`, `retroarch`, `pcsx2`, etc.).
3. **🌐 Network Safety & Locking:** NAS drop retry intervals, `.partial` + Pending Swaps, and `.hklock` distributed locking.
4. **🔐 Web, Tailnet & Desktop UI (`SettingsTailscalePwaCard`):** Configure HTTP port (`5178`), tray double-click behavior (`StandaloneWindow` vs `Browser`), theme/density, LAN/Tailnet bind (automatically locked to `127.0.0.1` when managed by `Mother`), `RestrictRawShellToLocalhost`, admin password, `NodeApiToken`, Tailscale Serve provisioning, and **offline inline SVG QR pairing** (both direct URL and `Mother` `/app/housekeeping/` subpath URL).
5. **🟢 Sister-App Coexistence & Greenroom (`SettingsIntegrationsAndDoctorCard`):** `AutoAlignWithSisterApps`, live peer health across all 5 sister apps, 1-click alignment presets, and Greenroom `:8420` sync.
6. **🔔 Verbosity & Discord:** Terminal buffer size, custom log tag filters, tray balloons, and opt-in Discord webhook notifications (empty by default).
7. **🖧 Remote Fleet Nodes:** Manage and sync Clean-Release-Root remote nodes (`<RemoteRoot>\app\` & `data\`) with single-flight SMB locking.
8. **🩺 System Doctor:** Live version/host/port/Tailscale diagnostics, dependency readiness (`MediaInfoNativeResolver`, `rclone`, `7z`, `HandBrakeCLI`, `hbd`), drive space bars, **📦 Backup Config & State Now** (writes clean JSON state archive to sibling `backups\`), and **🩺 Export Diagnostic Bundle (JSON)** (saved in `<LogsDir>`).

### 4.8 📜 View 7 — Run History (`HistoryPage`)
- Multi-column sorting, status filters, bulk row selection, CSV/JSON export, and **📄 View Log** for per-run transcripts in `logs\runs\`.

---

## 5. Everyday Workflows

### Workflow 1 — Auditing & Cleaning Clutter Inside ROM Archives Without Decompressing
1. Open **🛡 Reports** (`5`) and click **🔍 Scan Archives Now**.
2. Filter by **Category: `Extra Files`**, select a flagged archive, and click **🧪 Preview 7z d Cleanup** to verify protected ROMs/tracks.
3. Click **🧹 Clean Extra Files (7z d)** and **🔧 Heal .hash Sidecar** so `# Container:` and `.archive_hashes.json` stay synchronized.

### Workflow 2 — Detecting Bit Rot & Coexisting with ShadowLibrary
1. Open **🛠 Job Builder** (`3`) -> **Collection Manifest** -> **🔐 Integrity & Bit Rot Engine**.
2. Select `HashSidecarMode` (`0: DatabaseOnly`, `1: SidecarBeside` recommended for co-auditing, `2: PackageInsideArchive`, or `3: SidecarAndArchive`) and click **🔐 Run .hash Verifier Now**.

### Workflow 3 — Installing Housekeeping as a Mobile PWA (Direct or via `Mother` `/app/housekeeping/`)
1. Open **⚙ Settings** (`6`) -> **🔐 Web, Tailnet & Desktop UI** and enable **Tailscale Serve HTTPS (PWA)**.
2. Scan the offline inline **SVG QR Code** (or copy the generated direct / `Mother` `/app/housekeeping/` URL) on Android Chrome or iOS Safari and tap **📲 Install App** (`Add to Home Screen`).

---

## 6. Notifications, Tray & Scheduling

- **System Tray Menu (`TrayMenuFactory`):** Open Window/Browser, Run/Dry-Run Active Queue, **Stop Current Job** vs **Stop Entire Queue**, Pause Schedules (1 Hour), Open Data/Logs, and Exit (`InitiateGracefulShutdownAbort()`). Automatically suppressed when supervised under `--managed-by-mother` or `--managed-by-suite` (`Mother` hosts the single suite tray icon).
- **Process Guard & Post-Run Power:** Defers scheduled runs while guarded games/capture apps run, and executes `StayAwake` / `Sleep` / `Shutdown` post-run actions (automatically skipped under `--dry-run`, `APP_ISOLATED_TEST=1`, and sandboxed runs).

---

## 7. Troubleshooting & FAQ

| Symptom / Question | Cause | Resolution |
| :--- | :--- | :--- |
| **Dashboard shows `⏳ N Deferred Pending Swap(s) Ready`** | A media server, Defender, or SMB client held a handle lock (`FileShare.None` pre-probe) on the `.partial` or destination file. | Click **🔄 Commit Swaps Now** once playback/scanning stops. Existing destination media files are never deleted before move. |
| **Dashboard shows `🤝 Sister-App Coexistence Warning`** | A target folder overlaps a shared vault managed by `ShadowLibrary`, `Redroom`, `uStation`, or `StreamEngine`. | Click a 1-click alignment button on the banner (`Delegate ROM Vault Auditing` or `Align Placement`); Housekeeping creates a config snapshot automatically first. |
| **Cloud Sync hits `prior lock file found: ... .lck`** | A prior `rclone bisync` process stopped before removing its lockfile inside `<dataDir>\rclone\bisync\`. | Housekeeping strips ANSI codes, verifies no active `rclone.exe` holds the lock, removes the stale `.lck`, and retries once. If state diverged, run once with `--resync`. |
| **Media Transcoder reports `MediaInfo` probe error** | `MediaInfo.dll` could not be loaded (`MediaInfoNativeResolver`) or failed to open the file. | Place `MediaInfo.dll` (x64) in `app\`, `app\tools\`, or `data\tools\`, or switch to `FilenameTagMatching`. Housekeeping reports errors explicitly instead of assuming HEVC. |
| **Raw CLI / Custom Scripts shows `🔒 Restricted to Localhost`** | Connected from a non-loopback origin while `RestrictRawShellToLocalhost` is `true`. | Access Housekeeping directly on `127.0.0.1:5178` or toggle the setting locally if remote scripting is intentionally desired. |
| **Passing `--automation-port` exits immediately with code `2`** | Release binaries strip `Foundation.Automation` and fail closed on automation flags. | Use a Debug or `-p:HousekeepingTestAutomation=true` build for GUI automation testing. |

---

## 8. Glossary

| Term | Definition |
| :--- | :--- |
| **`.archive_hashes.json`** | Hidden per-directory JSON registry (`schemaVersion: 1`) recording post-injection/post-cleanup outer archive size, `lastWriteUtc`, `CRC32`, `MD5`, and `SHA1`. |
| **`.coexistence.json`** | Shared-root manifest (`schemaVersion: 2`) coordinating ownership and `.hash` placement across peer apps. |
| **`.hash` Sidecar** | Human-readable checksum file (`<File>.hash` or `<Folder>.hash`) containing `# Container:` outer metadata and `FILE:` or `COMPOSITE_SHA1:`/`ENTRY:` payload hashes across modes `0..3`. |
| **`.hklock`** | Per-file transcode lock (`<target>.hklock`) and PID+StartTime directory mutation sentinel (`<Dir>\.<appId>_<pid>.hklock`). |
| **`.partial` File** | Temporary encode target atomically swapped into the final filename via exclusive `FileShare.None` pre-probing after exit code `0`. |
| **Two-Step Archive Verification** | Checks `.archive_hashes.json` / outer `# Container:` hash first and falls back to 7-Zip's inner CRC32 header (`7z l -slt`) to distinguish benign repacks from bit rot. |
