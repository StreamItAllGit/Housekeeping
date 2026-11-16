# Housekeeping — End-User Manual & Complete UI Guide

- **Document:** `USER_GUIDE.md` (Technical Manual 2 of 4)
- **Active Build Version:** `v0.1.12-hf.1`
- **Default Desktop UI:** Disposable Edge WebView2 Standalone Window (`DefaultDesktopLaunchMode: "StandaloneWindow"`) + System Tray Companion (`http://127.0.0.1:5178`)
- **Remote / Mobile UI:** LAN Web Browser (`http://<host>:5178`) & Tailscale Serve HTTPS PWA (`https://<node>.<tailnet>.ts.net`)

---

## 1. What It Is & Who It's For

**Housekeeping** is your always-on background automation companion for media libraries, retro and modern game ROM collections, bit-rot checksum auditing, and multi-drive cloud backups. It runs quietly in the Windows system tray and exposes a fast, keyboard-driven control center in both a native-feeling standalone desktop window and any web browser on your LAN or Tailscale tailnet.

### Core Capabilities at a Glance (The 8 Automation Modules)
1. **🎬 Module 1 — Proxy Media Generator (`ProxyGenerator`):** Scans raw capture/working footage folders and encodes lightweight editing proxies (such as `Clip01 (360p_proxy).mp4`) right next to source clips using `HandBrakeCLI.exe`.
2. **📦 Module 2 — Video Archive Compressor (`ArchiveCompressor`):** Compresses finished production footage folders into long-term HEVC/AV1 archive directories while preserving subfolder hierarchy.
3. **📺 Module 3 — Multi-Tier Media Transcoder (`MediaTranscoder`):** Scans media drives (`V:`–`Z:`) and custom folders for master files (`(Original)` or `MediaInfo.dll` stream height) and builds multi-resolution streaming tiers (`144p` through `4K UHD`) plus alternate-language audio editions with atomic `.partial` writes, Redroom `.redroom-part` / `{edition-*}` / `.srt` coexistence guards, and automatic **Pending Swaps** when Plex has a file open.
4. **🎮 Module 4 — ROM & Game Launchers (`RomLauncherGenerator`):** Generates `.m3u` multi-disc playlists, packs loose ROMs into solid `.7z` archives (excluding `.hash`, `.partial*`, `.hklock`, and `.archive_hashes.json`), and writes per-game `.bat` and Start Menu `.lnk` launchers across 16 emulator runners with fast `7z l -slt` header inspection, SSD scratch extraction, and pre/post-launch `rclone` cloud save sync—completely decoupled from DAT and `.hash` sidecar manipulation.
5. **☁ Module 5 — Cloud Folder Sync (`CloudSync`):** Runs two-way `rclone bisync`, one-way `sync`, or `copy` jobs across Google Drive, OneDrive, S3, Dropbox, or NAS remotes with automatic stale `.lck` lockfile recovery.
6. **📚 Module 6 — Humble Bundle Sync (`HumbleSync`):** Incrementally downloads purchased Humble Bundle eBooks, audiobooks, comics, and DRM-free games using `hbd.exe`.
7. **⚡ Module 7 — Custom Scripts & CLI (`CustomScripts`):** Executes ordered `.bat`, `.cmd`, `.ps1`, `.py`, or `.exe` tasks with live terminal streaming, working directory control, and optional required network path checks.
8. **📋 Module 8 — Media Manifest & Bit Rot Auditor (`CollectionManifest`):** Recursively inventories media, ROM, and general storage roots into delimiter-aware `.csv` / `.tsv` (`CsvDelimiter`), `.json`, and visual `.html` catalogs, runs custom C# Regex extraction rules with a live playground, and verifies/auto-heals 4-mode `.hash` sidecars (`DatabaseOnly`, `SidecarBeside`, `PackageInsideArchive`, `SidecarAndArchive`) with post-injection `.archive_hashes.json` tracking, two-step outer+inner archive checks, composite folder dumps (`PS3_GAME`, Wii U, Switch, Xbox 360, `BDMV`, `VIDEO_TS`), and ShadowLibrary zero-rewrite parity.

---

## 2. Install, Upgrade & Uninstall

### 2.1 Installing or Upgrading with the Setup Wizard
1. Run **`Housekeeping-Setup-v0.1.12-hf.1-win-x64.exe`** from `Installers\` or `Releases\Latest\`.
2. **Welcome & Target Location:** Choose your installation root (defaults to `D:\Programs\Housekeeping` when an existing canonical install is detected, or `%LOCALAPPDATA%\Programs\Housekeeping`).
3. **Shortcut Options:** Choose whether to create a Windows Start Menu shortcut and root launcher.
4. **Automatic State & Checkpoint Preservation on Upgrade:** If Housekeeping is already running, the installer triggers a graceful daemon shutdown (`InitiateGracefulShutdownAbort`), preserving any active `data\interrupted_run_checkpoint.json` and `Up Next` queue (`PendingRuns`), migrates any legacy root files into `<InstallRoot>\data\`, preserves `data\settings.json`, `data\history.json`, `data\compliance_reports.json`, `data\dats\`, `data\manifests\`, `backups\`, and `logs\`, extracts the updated `app\` binaries, and relaunches Housekeeping.

### 2.2 Unattended / CLI Installer Commands
```powershell
# Verify embedded payload SHA-256 integrity without writing to disk
.\Housekeeping-Setup-v0.1.12-hf.1-win-x64.exe --verify

# Silent install or upgrade to canonical production path and launch
.\Housekeeping-Setup-v0.1.12-hf.1-win-x64.exe --silent --install-dir "D:\Programs\Housekeeping" --launch

# Uninstall binaries while preserving data\ and logs\
.\Housekeeping-Setup-v0.1.12-hf.1-win-x64.exe --uninstall --install-dir "D:\Programs\Housekeeping"

# Full uninstall including user data, settings, and logs
.\Housekeeping-Setup-v0.1.12-hf.1-win-x64.exe --uninstall --install-dir "D:\Programs\Housekeeping" --purge-data
```

---

## 3. First-Run Setup & Starter Jobs

1. **Launch Housekeeping:** Run `<InstallRoot>\Housekeeping.exe` (or double-click the system tray icon). By default, Housekeeping opens in a dark-themed **Standalone WebView2 Window** (`http://127.0.0.1:5178`).
2. **Verify External CLI Tools (`Settings` $\rightarrow$ `🔧 Tools & HandBrake Profiles`):**
   - Check the dependency pills at the top of the card (`HandBrakeCLI.exe`, `7z.exe`, `rclone.exe`, `hbd.exe`, `MediaInfo.dll`).
   - Housekeeping auto-detects binaries placed inside `<InstallRoot>\data\tools\` (or `<InstallRoot>\app\tools\`) and on your Windows `PATH`. You can also browse to custom `.exe` paths using the **Hybrid Path Picker** (`📂`).
3. **Populate Starter Jobs (`Job Builder` $\rightarrow$ `+ Add All 8 Starter Jobs`):**
   - If you are setting up a fresh instance or want template jobs for all 8 modules, open **🛠 Job Builder** (`3`) and click **+ Add All 8 Starter Jobs** in the left sidebar footer (or create individual jobs from **🧩 Modules** `4`).
4. **Always Dry-Run First (`🧪 Dry Run`):**
   - Before running a new transcode, ROM launcher, or cloud sync queue for the first time, click **🧪 Dry Run** in the Top Command Bar. Every command that *would* execute is logged in purple `[DRY]` tags in the live terminal without modifying a single file on disk.

---

## 4. Screen-by-Screen Tour

### 4.1 Global Shell: Left Sidebar Rail, Mobile Bottom Nav, Top Command Bar & Resizable Terminal
- **Left Sidebar Navigation Rail (`220px` expanded / `56px` icon rail, `Ctrl+B` toggle):**
  - Switch between the 7 primary views by clicking or pressing keys **`1`–`7`** whenever focus is outside a text input, or press **`Ctrl+B`** to collapse/expand the sidebar (`AppConfig.UiLayout` persists your layout):
    - **`1` 📊 Dashboard** · **`2` 🗂 Job Queues** · **`3` 🛠 Job Builder** · **`4` 🧩 Modules** · **`5` 🛡 Reports** (displays live open issue badge count) · **`6` ⚙ Settings** · **`7` 📜 History**
  - **Quick Utilities:** **🔍 Search (`Ctrl+K`)** opens the Global Omni-Search modal across views, queues, settings sub-tabs, and storage drives. **⌨️ Shortcuts (`?`)** opens the keyboard shortcut cheat-sheet.
  - **💾 Consolidated Mounted Storage Strip & `DrivesTableModal`:** Pinned flex-priority list in the sidebar footer displaying 10+ local and NAS drives (`C:` through `Z:`) with free-space readout and color-coded health dots (Green healthy, Amber $<12\%$ free, Red $<25\text{ GB}$ or $<5\%$ free, or `Offline`). Click any drive row (or open from the mobile drawer) to inspect the shared **💾 Mounted Local & NAS Drives** modal table (the duplicate top-bar drive strip has been retired for a cleaner command bar).
- **Docked Mobile Bottom Navigation Bar (`<= 820px`):**
  - On phones and narrow portrait/landscape viewports (`<= 820px`), `<MobileBottomNavBar />` docks cleanly along the bottom edge for instant thumb switching across primary views, search, and the mobile navigation/storage sheet.
- **Top Command Bar:**
  - **Target Machine Node Switcher (`🖥 Local` vs `🌐 Remote Node`):** Switches the entire UI between your local PC and any configured remote LAN/Tailnet Housekeeping node.
  - **Auto-Save Status Pill & Snapshot History:** Edits auto-save after `650ms` (`✓ Saved` / `⟳ Saving...` / `⚠ Validation Error`). Press **`Ctrl+S`** or click **💾 Save** to force an immediate flush. Click **↺ History (`N`)** to inspect and one-click restore any of the **20 most recent `settings.json` snapshots**.
  - **📥 Import / 📤 Export:** Import a `.json` profile or export either a **🛡 Shareable (Sanitized)** profile (strips Humble cookies, Discord webhooks, and API tokens) or a **🔐 Full Backup**.
  - **PWA & Theme Controls:** **📲 Install App** / **🔒 Switch to HTTPS (PWA)** (when browsing over Tailscale), **UI Density** selector (`Compact`, `Comfortable`, `Touch`), and **☀ Light / 🌙 Dark** theme toggle.
  - **Pipeline Execution & Dual-Mode Stop Controls:**
    - **▶ Run Enabled** (or **⏭ Queue Next** when a run is already active)
    - **🧪 Dry Run** (or **🧪 Queue Dry**)
    - **⏸ Pause Queue / ▶ Resume Queue** (pauses cleanly between files/steps)
    - **❄ Suspend Process / 🔥 Unsuspend** (freezes active `HandBrakeCLI` / `7z` / `rclone` OS threads immediately via `NtSuspendProcess` to free 100% CPU/GPU)
    - **⏹ Stop Job** (`StopCurrentJob` — cancels only the currently running job/subprocess and advances the active queue to its next step while preserving `Up Next`)
    - **⏹ Stop Queue** (`StopActiveRun` — terminates the active subprocess, clears the `Up Next` wait-list, and removes `interrupted_run_checkpoint.json`)
- **Interrupted Mid-Shutdown Run Recovery Banner:**
  - If Housekeeping was gracefully shut down during an upgrade/exit or the PC rebooted while a queue, job, `.hash` verification, or compliance scan was running, an amber banner appears at the top of the workspace on next launch showing completed vs. remaining steps with **▶ Resume Remaining Jobs**, **↺ Restart Entire Queue**, **Auto-restart on startup**, and **✕ Dismiss**.
- **Global Live Terminal Drawer & Resizable Pane (`Ctrl+\``):**
  - The bottom footer bar shows a real-time SignalR connection dot, current active subprocess status and progress bar, **`S` / `M` / `L`** terminal height presets + a drag-resize handle (`120px` up to `body - 160px`, persisted in `settings.json` `UiLayout`), a **Verbosity** selector (`Progress & Status`, `Everything`, `Quiet`, `Alerts Only`, `Custom`), and **Session Totals** (`Processed | Skipped | Errors`).
  - On any tab other than Dashboard, click the footer bar or press **`Ctrl+\``** to slide up the **Live Terminal Drawer**.

### 4.2 📊 View 1 — Dashboard (`DashboardPage`)
- **Alert & Coexistence Banners:**
  - **🤝 Sister-App Coexistence Warning Banner (`CoexistenceWarningBanner`):** Automatically surfaces when configured storage paths overlap with `ShadowLibrary (:5179)`, `Redroom (:5180)`, `uStation (:8460)`, or `StreamEngine (:8430)`, offering 1-click snapshot-backed presets (**Delegate ROM Vault Auditing to ShadowLibrary**, **Align Placement & Zero-Rewrite Parity**, or **Enable Auto-Align**).
  - Real-time operational banners for **🌐 Waiting for Network / NAS Reconnection**, **⏳ Deferred Pending Swap(s) Ready** (**🔄 Commit Swaps Now** / **🗑 Discard**), active pipeline progress (`ActivePipelineControlBar` with split **Stop Job** / **Stop Queue**), and FIFO **⏭ Up Next** runs.
- **4 Live KPI Cards:**
  1. **Active Pipeline Engine:** Current queue/job name, step counter (`Current/Total`), subprocess status text, next scheduled run countdown, and progress bar.
  2. **Host CPU & Memory:** Live CPU utilization `%` and RAM `Used / Total GB`.
  3. **Disk I/O & Network Throughput:** Live aggregate disk read/write `MB/s`, network `Mbps`, and mounted drive count.
  4. **Archive Compliance & Integrity:** Count of open compliance/hash findings (click to jump directly to **🛡 Reports**).
- **🗂 Named Job Queues & Quick Dispatch Strip:** Cards for every Named Job Queue showing enabled job count, schedule badge, **▶ Run** / **⏭ Queue Next**, **🧪 Dry**, **Set Active**, and **Configure →**, plus a **+ Quick New Job** form in the header.
- **Dual-Pane Live Terminal (`DualPaneTerminal`):**
  - Switch between **Single Pane** and **Dual-Pane Split** (`Side-by-Side` or `Stacked`), drag the top resize handle or click **`S` / `M` / `L`** height presets.
  - Each pane has its own **Target Node** selector (`Local` or any remote Fleet Node), live search filter, **Wrap** toggle, ring-buffer-safe **Auto-Scroll** (`ResizeObserver` + `45px` proximity latch that never stalls at the 2,500-line cap), **🧹 Clear**, and a bottom **Interactive PowerShell / CMD Bar** (`ArrowUp`/`ArrowDown` command history, live working directory prompt, and **Stop** button).

### 4.3 🗂 View 2 — Job Queues (`JobQueuesPage`)
- **Left Sidebar (`Named Queues`):** Select any queue, see its enabled job ratio and schedule mode, or click **+ New Queue**.
- **Queue Header & Actions:** Edit **Queue Name** and **Description**, export queue JSON, or click **▶ Run This Queue**, **🧪 Dry Run**, or **🗑 Delete Queue**.
- **⏰ Independent Queue Schedule Configurator:**
  - Toggle **Enable Automatic Schedule** per queue (equipped with 2-line hover hints across every toggle and cadence control).
  - Choose **Schedule Cadence Mode**: `Daily at Specific Time (HH:mm)`, `Interval Cadence (Every N Minutes)`, or `Multiple Specific Times (CSV)` (e.g. `03:00, 15:00`).
  - Toggle active **Days of Week** (`Monday`–`Sunday`) and inspect the live **Next 5 Calculated Runs** preview strip.
- **Ordered Pipeline Steps:** Attach jobs from your Defined Jobs repository, reorder steps with **↑ / ↓**, toggle individual steps on/off, jump to **Edit in Builder →**, or remove a job from the queue.

### 4.4 🛠 View 3 — Job Builder (`JobBuilderPage` & 8 Module Editors)
- **Left Sidebar (`Defined Jobs Repository`):**
  - Create jobs via **+ New Job ▾**, filter by module, sort by `Name (A → Z)`, `By Module`, or `Manual Order`, and switch between **Compact** (single-line icon module pill) and **Detailed** (full Row 2 module badge) list cards.
  - Footer utilities: **↑ / ↓** manual reorder and **+ Add All 8 Starter Jobs**.
- **Ergonomic Form Controls (`ProfileSelect` & 2-Line Hover Hints):**
  - Fixed-vocabulary fields (such as HandBrake profiles and emulator `RUNNER_TYPES`) use strict `<select>` dropdowns (`ProfileSelect`) that automatically flag stale/deleted profile names with a **`⚠ Missing`** guard so broken references are immediately visible.
  - Hovering any checkbox, toggle, or matrix column header displays a concise 2-line explanation of what the setting controls and when to enable it.
- **Module-Specific Configuration Editors:**
  1. **Proxy Generator Editor:** Select `HandBrake Encoding Profile`, `Proxy Filename Suffix Tag` (`360p_proxy`), `Source Extensions`, and target footage folders.
  2. **Archive Compressor Editor:** Select `HandBrake Profile`, `Output Container` (`.mkv`/`.mp4`), `Skip Files Containing Tag` (`_proxy)`), `Input Extensions`, and `Source → Archive Folder Mappings`.
  3. **Media Transcoder Editor:** Choose `Source Detection Mode` (`FilenameTagMatching` vs `MediaInfoResolutionScan`), `Master File Tag` (`(Original)`), `Drives to Scan` (`V, W, X, Y, Z`), `Drive Relative Subfolder`, active output resolution checkboxes (`144p`–`4K UHD`), the **Source Quality Tier → Output Profile Matrix**, custom scan folders, and **Alternate Language Edition Rules**.
  4. **ROM & Game Launchers Editor (Decoupled from DAT/`.hash` Auditing):**
     - Toggle `.m3u` generation, `.7z` auto-compression, fast `7z l -slt` header inspection, SSD scratch extraction on launch, Start Menu `.lnk` creation, and local `.bat` launchers.
     - Displays a clean **Checksum & Manifest Auditing Pointer** directing DAT/manifest cataloging and `.hash` bit-rot verification to **Module 8 (`Collection Manifest`)** or **ShadowLibrary** so game launcher generation stays fast and sidecar-free.
     - Configure **Pre/Post-Launch Cloud Save & State Sync** (`rclone` multi-row sync paths with a live **Pre-Launch / Post-Launch Command Preview**).
     - Manage the **Console Collection Rules Table** (45+ canonical consoles, 16 emulator runners via `<select>`, extensions priority, archive path, compliance template override, and per-console save sync overrides).
  5. **Cloud Sync Editor:** Refresh detected `rclone` remotes, click **⚡ Run Once with --resync** to initialize `bisync`, and configure `bisync` / `sync` / `copy` folder pairs.
  6. **Humble Sync Editor:** Enter your `_simpleauth_sess` session cookie (with **👁 Reveal / 🙈 Hide**), target download folder, and extra CLI flags.
  7. **Custom Scripts Editor:** Build an ordered table of `.bat` / `.ps1` / `.py` / `.exe` tasks with arguments, working directory, optional `Required Path Check` (e.g. `V:\`), and `External CMD` toggle.
  8. **Collection Manifest & Bit Rot Editor:**
     - Configure `Unified Master Manifest`, base name, output formats (`CSV (.csv)` / `TSV (.tsv)` checkbox paired with `Delimiter:` — `Comma`, `Tab` $\rightarrow$ `.tsv`, `Pipe`, or `Semicolon`; plus `JSON` and `Visual HTML`), **🟢 Auto-Sync Greenroom (:8420) on Change**, primary output folder, and secondary mirror folder.
     - Configure the **🔐 Integrity & Bit Rot Engine**: `Enable Bit Rot Detection`, `Create Missing .hash Files`, `Auto-Heal Repacked Containers`, `Two-Step Archive Inspection`, 4-mode **`.hash` Placement (`HashSidecarMode`)** (`0: DatabaseOnly`, `1: SidecarBeside`, `2: PackageInsideArchive`, `3: SidecarAndArchive`), **`.archive_hashes.json` Post-Injection Sync (`SyncPostInjectionContainerHashJson`)**, `SkipShadowLibraryManagedRoots`, algorithm (`SHA-1`/`CRC32`/`MD5`/`SHA-256`), and **🔐 Run .hash Verifier Now**.
     - Manage **Target Scan Folders** and **Dynamic Metadata Field Parsing Rules** (load 1-click preset bundles for `Personal Media`, `Personal ROMs`, `General Media`, `General Files`, or `General Audio`, and test rules live in the **C# .NET Regex Playground**).

### 4.5 🧩 View 4 — Modules Catalog (`ModulesCatalogPage`)
- Top **🔧 External Tool & Engine Readiness** strip shows resolved paths for `HandBrakeCLI`, `7z`, `rclone`, `hbd`, and `MediaInfo.dll`.
- Left column lists all 8 modules with an **Active** app-wide master switch and defined job count; right pane shows **✨ Core Capabilities**, **🧭 Quick Setup Guide**, **💡 Best Practices & Required Tools**, and **+ Add Job for `<Module>`**.

### 4.6 🛡 View 5 — Reports & Compliance Center (`ReportsCenterPage`)
- **4 KPI Summary Cards:** `Open Archive & Hash Issues`, `Cleanable Extra Clutter (7z d)`, `Missing Refs / Format Issues`, and `Resolved / Ignored`.
- **Filter & Remediation Toolbar:** Filter by Console, Issue Category (`Extra Files`, `Missing / Broken Refs`, `Wrong Format`, `Multi-ROM / Structure`, `Hash Integrity`), and Resolution Status (`Open Only`, `All`, `Ignored Only`, `Resolved Only`), or click **📐 Archive Templates**, **🔍 Scan Archives Now**, **📥 Export CSV**, and **📥 Export JSON**.
- **Master-Detail Split View & Safe `7z d` Cleanup:**
  - Select any flagged archive on the left to inspect its internal entry tree on the right (`PrimaryPayload`, `DiscTrackOrCue`, `CompanionHash`, or `ExtraClutter`).
  - Click **🧪 Preview 7z d Cleanup** to open a dry-run modal (`ReportCleanupDryRunModal`) showing the exact `7z d` command, every clutter file slated for deletion, and every protected ROM/track file kept (`ReclaimedBytes`).
  - Click **🧹 Clean Extra Files (7z d)** to strip clutter without decompressing the archive, **🔧 Heal .hash Sidecar** to update its checksum sidecar, or **🙈 Ignore** / **🧹 Clear Resolved**.

### 4.7 ⚙ View 6 — Settings (`SettingsPage` — 8 Sub-Tabs)
1. **🔧 Tools & HandBrake Profiles:** Override paths for `HandBrakeCLI.exe`, `7z.exe`, `rclone.exe`, and `hbd.exe`, and edit the shared **HandBrake Encoding Profiles Catalog**.
2. **🛡 Startup & Process Guard:** Toggle `Launch Housekeeping Tray Daemon Automatically at Windows Logon`, `Start Silently in System Tray`, `Auto-Restart Jobs & Queues Interrupted Mid-Shutdown on Startup`, and configure the **🎮 Game / Streaming Process Guard** list (`obs64`, `retroarch`, `pcsx2`, etc.).
3. **🌐 Network Safety & Locking:** Toggle NAS drop pause/retry, `.partial` + Pending Swaps, startup `.partial` cleanup, Standard 19 `.hklock` distributed file/directory locking, and tune retry intervals and lock timeouts.
4. **🔐 Web, Tailnet & Desktop UI:** Configure HTTP port (`5178`), tray double-click default UI (`StandaloneWindow` vs `Browser`), 3-state color scheme (`system`, `dark`, `light`), UI density, `0.0.0.0` LAN/Tailnet binding, `Tailscale-User-Login` header trust, `RestrictRawShellToLocalhost`, **Remote Admin Password**, **Fleet Node API Token (`X-Housekeeping-Token`)**, and **📱 Mobile PWA & Tailscale Serve HTTPS** provisioning.
5. **🟢 Sister-App Coexistence & Greenroom (`SettingsIntegrationsAndDoctorCard`):** Toggle **`AutoAlignWithSisterApps`**, inspect live peer coexistence status across `ShadowLibrary (:5179)`, `Redroom (:5180)`, `uStation (:8460)`, `StreamEngine (:8430)`, and `Greenroom (:8420)`, apply 1-click alignment presets, probe `:8420`, and configure post-manifest Greenroom sync.
6. **🔔 Verbosity & Discord:** Configure terminal buffer size (`200`–`5000` lines), terminal/footer verbosity levels, custom log tag checkboxes, Windows tray balloon alerts, and **Discord Rich Embed Webhook** notifications (with **📨 Send Test Discord Embed**).
7. **🖧 Remote Fleet Nodes:** Add, probe, and sync Clean-Release-Root (`app\` + `data\`) remote LAN/Tailnet Housekeeping nodes (`HostOrIp`, `HttpApiPort: 5178`, `Node API Token`, `PreferHttpApi`, and `SMB Share Path`).
8. **🩺 System Doctor:** Inspect live version/host/port/Tailscale health cards, refresh core executable dependencies, review mounted storage free-space bars, and run one-click **📦 Backup Config & State Now** (writes a clean JSON state-only archive into sibling `<InstallRoot>\backups\`) or **🩺 Export Diagnostic Bundle (JSON)** (saved inside `<LogsDir>`).

### 4.8 📜 View 7 — Run History (`HistoryPage`)
- Filter historical pipeline runs by status (`All`, `Completed`, `Errors`, `Cancelled/Interrupted`) or search text, sort by any column, select rows to export filtered **CSV** or **JSON** reports, and click **📄 View Log** on any row to inspect the full per-run log transcript (`logs\runs\Run_*.log`).

---

## 5. Everyday Workflows

### Workflow 1 — Auditing & Cleaning Clutter Inside ROM Archives Without Decompressing
1. Open **🛡 Reports** (`5`) and click **🔍 Scan Archives Now**.
2. Filter by **Category: `Extra Files`** to view `.7z`/`.zip` archives containing scene `.nfo`, `.txt`, or `.url` clutter.
3. Click an archive row, click **🧪 Preview 7z d Cleanup** to verify that your primary ROM and `.cue`/`.bin` tracks are marked **Protected**, and click **🧹 Clean Extra Files (7z d)**.
4. If the archive has a `.hash` sidecar, click **🔧 Heal .hash Sidecar** so `# Container:` and `.archive_hashes.json` match the newly cleaned archive.

### Workflow 2 — Detecting Bit Rot, Choosing 4-Mode `.hash` Placement & Coexisting with ShadowLibrary
1. Open **🛠 Job Builder** (`3`), select your **Collection Manifest** (Module 8) job, and scroll to the **🔐 Integrity & Bit Rot Engine** card.
2. Choose your **`.hash` Sidecar Placement (`HashSidecarMode`)**:
   - **`0 — DatabaseOnly`:** Stores checksums only in the manifest CSV/TSV/JSON without writing `.hash` files.
   - **`1 — SidecarBeside`:** Writes `<Archive>.7z.hash` right beside each file (recommended for zero-rewrite ShadowLibrary co-auditing).
   - **`2 — PackageInsideArchive`:** Packages `.hash` inside the archive and records the post-injection outer digest in `<Directory>\.archive_hashes.json`.
   - **`3 — SidecarAndArchive`:** Maintains both external and embedded `.hash` copies plus `.archive_hashes.json`.
3. Select verification mode (`FastHeaderAndHeal`, `DeepCryptographicAndHeal`, or `DryRunReportOnly`) and click **🔐 Run .hash Verifier Now**. Any unhealed mismatches appear in **🛡 Reports** under `Hash Integrity`.

### Workflow 3 — Installing Housekeeping as a Mobile PWA via Tailscale HTTPS
1. Open **⚙ Settings** (`6`) $\rightarrow$ **🔐 Web, Tailnet & Desktop UI**.
2. In the **📱 Mobile PWA & Tailscale Serve HTTPS** card, click **⚡ Enable Tailscale HTTPS (PWA)**.
3. If Tailscale Serve is not yet authorized on your tailnet, click the **🔑 Authorize Tailscale Serve & HTTPS ↗** button that appears; Housekeeping detects approval automatically within 4 seconds and provisions your `https://<machine>.<tailnet>.ts.net` certificate.
4. Open that `https://` URL on Android Chrome or iOS Safari—Housekeeping automatically presents the docked `<MobileBottomNavBar />`—and tap **📲 Install App** (or **Add to Home Screen**).

---

## 6. Notifications, Tray & Scheduling

- **System Tray Icon & Context Menu (`TrayMenuFactory`):** Right-clicking the Housekeeping tray icon provides quick actions without opening the UI:
  - **Open Housekeeping Window** (or **Open in Web Browser**)
  - **▶ Run Active Queue Now** / **🧪 Dry-Run Active Queue**
  - **⏹ Stop Current Job** (advances active queue, keeps `Up Next`) / **⏹ Stop Entire Queue** (clears `Up Next` & checkpoint)
  - **⏸ Pause Schedules for 1 Hour** (or **▶ Resume Schedules**)
  - **📂 Open Data Folder** / **📄 Open Live Log**
  - **✕ Exit Housekeeping** (invokes `InitiateGracefulShutdownAbort()` so any active `interrupted_run_checkpoint.json` and `Up Next` runs are preserved for next launch)
- **Game & Streaming Process Guard:** When `EnableProcessGuard` is checked in **Settings $\rightarrow$ Startup & Process Guard**, Housekeeping checks every 15 seconds whether any guarded executable (`obs64`, `retroarch`, `pcsx2-qt`, `duckstation`, `rpcs3`, `dolphin`, etc.) is running. Scheduled queues due during gameplay are cleanly deferred until the game or stream closes.
- **Post-Run Power Actions:** Configure `StayAwake`, `Sleep`, or `Shutdown` to trigger automatically after a long overnight transcode or backup queue finishes (skipped automatically during `--dry-run` and `APP_ISOLATED_TEST=1` test runs).

---

## 7. Troubleshooting & FAQ

| Symptom / Question | Cause | Resolution |
| :--- | :--- | :--- |
| **Dashboard shows `⏳ N Deferred Pending Swap(s) Ready`** | A media file finished transcoding in Module 1/2/3, but Plex, Windows Defender, or an SMB client held a file handle lock (`FileShare.None` pre-probe) on the `.partial` or destination file. | Click **🔄 Commit Swaps Now** on the Dashboard banner once playback finishes, or let Housekeeping auto-sweep after the next run. Destination media files are never deleted before move. |
| **Dashboard shows `🤝 Sister-App Coexistence Warning`** | One of your Module 3/4/5/8 target paths overlaps a shared vault managed by `ShadowLibrary`, `Redroom`, `uStation`, or `StreamEngine`. | Click one of the 1-click alignment buttons directly on the banner (for example **Delegate ROM Vault Auditing to ShadowLibrary** or **Align Placement**)—Housekeeping snapshots `settings.json` automatically before applying changes. |
| **Cloud Sync fails with `prior lock file found: ... .lck`** | A previous `rclone bisync` process was interrupted before removing its local `.lck` file. | Housekeeping automatically strips ANSI codes, verifies no `rclone.exe` process is active, deletes the orphaned `.lck` file, and retries once. If a lock is corrupt beyond single-retry recovery, click **⚡ Run Once with --resync** in the Cloud Sync Job Editor. |
| **Interactive Terminal CLI or Custom Scripts editor is disabled (`🔒 Raw Shell Restricted to Localhost`)** | You are connected from a remote LAN or Tailnet browser while `RestrictRawShellToLocalhost` is enabled (`true` by default). | Open Housekeeping directly on the host machine (`127.0.0.1:5178`), or uncheck `RestrictRawShellToLocalhost` in **Settings $\rightarrow$ Web, Tailnet & Desktop UI** if you intentionally want remote shell access. |
| **A dropdown shows `⚠ Missing (<ProfileName>)`** | A job references a HandBrake encoding profile or runner name that was renamed or deleted from Settings. | Select a valid replacement from the `ProfileSelect` dropdown or recreate the profile in **Settings $\rightarrow$ Tools & HandBrake Profiles**. |

---

## 8. Glossary

| Term | Definition |
| :--- | :--- |
| **`.archive_hashes.json`** | Hidden per-directory JSON registry (`schemaVersion: 1`) recording post-injection/post-cleanup outer archive size, `lastWriteUtc`, `CRC32`, `MD5`, and `SHA1` so modes `2` (`PackageInsideArchive`) and `3` (`SidecarAndArchive`) verify in $O(1)$ without false bit-rot alarms. |
| **`.coexistence.json`** | Shared-root manifest (`schemaVersion: 2`) placed at vault roots to coordinate ownership and `.hash` placement across `Housekeeping`, `ShadowLibrary`, `Redroom`, `uStation`, `StreamEngine`, and `Greenroom`. |
| **`.hash` Sidecar** | Human-readable checksum file (`<File>.hash` or `<Folder>.hash`) containing `# Container:` outer metadata and `FILE:` or `COMPOSITE_SHA1:`/`ENTRY:` payload hashes across 4 placement modes (`0..3`). |
| **`.hklock`** | Per-file transcode lock (`<target>.hklock`) and Standard 19 §4 directory mutation sentinel (`<Dir>\.<appId>_<pid>.hklock` with `pid` + `processStartTimeUtc`) preventing multi-node and cross-app write collisions. |
| **`.partial` File** | Temporary working output file written during `HandBrakeCLI` encoding and atomically swapped into the final filename via exclusive `FileShare.None` pre-probing only after exit code `0`. |
| **Composite Folder Dump** | Multi-file console or disc directory structure (`PS3_GAME`, Wii U `code`/`content`/`meta`, `BDMV`, `VIDEO_TS`) hashed as a single deterministic `COMPOSITE_SHA1` unit via per-file CRC32 + Size entries. |
| **Pending Swap** | A completed `.partial` encode recorded in `data\pending_swaps.json` waiting for an SMB/Defender or Plex file handle lock to release so it can atomically replace the target file. |
| **Two-Step Archive Verification** | Bit-rot check that verifies `.archive_hashes.json` / outer `# Container:` hash first and falls back to 7-Zip's inner ROM CRC32 header (`7z l -slt`) to distinguish benign archive repacks from real payload corruption. |
