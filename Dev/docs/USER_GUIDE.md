# Housekeeping — End-User Manual & Complete UI Guide

- **Document:** `USER_GUIDE.md` (Technical Manual 2 of 4)
- **Active Build Version:** `v1.0.0-rc.11-hf.2`
- **Default Desktop UI:** Disposable Edge WebView2 Standalone Window (`DefaultDesktopLaunchMode: "StandaloneWindow"`) + System Tray Companion (`http://127.0.0.1:5178`)
- **Remote / Mobile UI:** LAN Web Browser (`http://<host>:5178`) & Tailscale Serve HTTPS PWA (`https://<node>.<tailnet>.ts.net`)

---

## 1. What It Is & Who It's For

**Housekeeping** is your always-on background automation companion for media libraries, retro and modern game ROM collections, bit-rot checksum auditing, and multi-drive cloud backups. It runs quietly in the Windows system tray and exposes a fast, keyboard-driven control center in both a native-feeling standalone desktop window and any web browser on your LAN or Tailscale tailnet.

### Core Capabilities at a Glance (The 8 Automation Modules)
1. **🎬 Module 1 — Proxy Media Generator (`ProxyGenerator`):** Scans raw capture/working footage folders and encodes lightweight editing proxies (such as `Clip01 (360p_proxy).mp4`) right next to source clips using `HandBrakeCLI.exe`.
2. **📦 Module 2 — Video Archive Compressor (`ArchiveCompressor`):** Compresses finished production footage folders into long-term HEVC/AV1 archive directories while preserving subfolder hierarchy.
3. **📺 Module 3 — Multi-Tier Media Transcoder (`MediaTranscoder`):** Scans media drives (`V:`–`Z:`) and custom folders for master files (`(Original)` or `MediaInfo.dll` stream height) and builds multi-resolution streaming tiers (`144p` through `4K UHD`) plus alternate-language audio editions with atomic `.partial` writes and automatic **Pending Swaps** when Plex has a file open.
4. **🎮 Module 4 — ROM & Game Launchers (`RomLauncherGenerator`):** Generates `.m3u` multi-disc playlists, packs loose ROMs into solid `.7z` archives, writes per-game `.bat` and Start Menu `.lnk` launchers with fast SSD scratch extraction and pre/post-launch `rclone` cloud save sync, verifies collections against **No-Intro & Redump DATs** into a 9-column `collection_manifest.csv`, and runs zero-decompression archive compliance checks.
5. **☁ Module 5 — Cloud Folder Sync (`CloudSync`):** Runs two-way `rclone bisync`, one-way `sync`, or `copy` jobs across Google Drive, OneDrive, S3, Dropbox, or NAS remotes with automatic stale `.lck` lockfile recovery.
6. **📚 Module 6 — Humble Bundle Sync (`HumbleSync`):** Incrementally downloads purchased Humble Bundle eBooks, audiobooks, comics, and DRM-free games using `hbd.exe`.
7. **⚡ Module 7 — Custom Scripts & CLI (`CustomScripts`):** Executes ordered `.bat`, `.cmd`, `.ps1`, `.py`, or `.exe` tasks with live terminal streaming, working directory control, and optional required network path checks.
8. **📋 Module 8 — Media Manifest & Bit Rot Auditor (`CollectionManifest`):** Recursively inventories media, ROM, and general storage roots into dynamic `.csv`, `.json`, and visual `.html` catalogs, runs custom C# Regex extraction rules with a live playground, and verifies/auto-heals `.hash` sidecars (including two-step outer+inner archive checks and composite folder dumps for PS3, Wii U, Switch, Xbox 360, Blu-Ray `BDMV`, and DVD `VIDEO_TS`).

---

## 2. Install, Upgrade & Uninstall

### 2.1 Installing or Upgrading with the Setup Wizard
1. Run **`Housekeeping-Setup-v1.0.0-rc.11-hf.2.exe`** from `Installers\` or `Releases\Latest\`.
2. **Welcome & Target Location:** Choose your installation root (defaults to `D:\Programs\Housekeeping` when an existing canonical install is detected, or `%LOCALAPPDATA%\Programs\Housekeeping`).
3. **Shortcut Options:** Choose whether to create a Windows Start Menu shortcut and root launcher.
4. **Automatic State Preservation on Upgrade:** If Housekeeping is already running, the installer gracefully stops the tray daemon, automatically migrates any legacy root files into `<InstallRoot>\data\`, preserves your `data\settings.json`, `data\history.json`, `data\compliance_reports.json`, `data\dats\`, `data\manifests\`, `backups\`, and `logs\`, extracts the updated `app\` binaries, and relaunches Housekeeping.

### 2.2 Headless / CLI Installer Commands
```powershell
# Verify embedded payload SHA-256 integrity without writing to disk
.\Housekeeping-Setup-v1.0.0-rc.11-hf.2.exe --verify

# Silent install or upgrade to canonical production path and launch
.\Housekeeping-Setup-v1.0.0-rc.11-hf.2.exe --silent --install-dir "D:\Programs\Housekeeping" --launch

# Uninstall binaries while preserving data\ and logs\
.\Housekeeping-Setup-v1.0.0-rc.11-hf.2.exe --uninstall --install-dir "D:\Programs\Housekeeping"

# Full uninstall including user data, settings, and logs
.\Housekeeping-Setup-v1.0.0-rc.11-hf.2.exe --uninstall --install-dir "D:\Programs\Housekeeping" --purge-data
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

### 4.1 Global Shell: Left Sidebar Rail, Top Command Bar & Docked Terminal Drawer

- **Left Sidebar Navigation Rail (`220px` expanded / `56px` icon rail):**
  - Switch between the 7 primary views by clicking or pressing keys **`1`–`7`** whenever focus is outside a text input:
    - **`1` 📊 Dashboard** · **`2` 🗂 Job Queues** · **`3` 🛠 Job Builder** · **`4` 🧩 Modules** · **`5` 🛡 Reports** (displays live open issue badge count) · **`6` ⚙ Settings** · **`7` 📜 History**
  - **Quick Utilities:** **🔍 Search (`Ctrl+K`)** opens the Global Omni-Search modal across views, queues, settings sub-tabs, and storage drives. **⌨️ Shortcuts (`?`)** opens the keyboard shortcut cheat-sheet.
  - **💾 Mounted Storage Footer:** Pinned live list of local and network/NAS drives (`C:` through `Z:`) with color-coded health dots (Green healthy, Amber $<12\%$ free, Red $<25\text{ GB}$ or $<5\%$ free, or `Offline`).
- **Top Command Bar:**
  - **Target Machine Node Switcher (`🖥 Local` vs `🌐 Remote Node`):** Switches the entire UI between your local PC and any configured remote LAN/Tailnet Housekeeping node.
  - **Auto-Save Status Pill & Snapshot History:** Edits auto-save after `650ms` (`✓ Saved` / `⟳ Saving...` / `⚠ Validation Error`). Press **`Ctrl+S`** or click **💾 Save** to force an immediate flush. Click **↺ History (`N`)** to inspect and one-click restore any of the **20 most recent `settings.json` snapshots**.
  - **📥 Import / 📤 Export:** Import a `.json` profile or export either a **🛡 Shareable (Sanitized)** profile (strips Humble cookies, Discord webhooks, and API tokens) or a **🔐 Full Backup**.
  - **Live Drive Free-Space Strip:** Interactive pills for up to 16 local/NAS drives; click any pill to open the **💾 Mounted Local & NAS Drives** modal table.
  - **PWA & Theme Controls:** **📲 Install App** / **🔒 Switch to HTTPS (PWA)** (when browsing over Tailscale), **UI Density** selector (`Compact`, `Comfortable`, `Touch`), and **☀ Light / 🌙 Dark** theme toggle.
  - **Pipeline Execution Controls:**
    - **▶ Run Enabled** (or **⏭ Queue Next** when a run is already active)
    - **🧪 Dry Run** (or **🧪 Queue Dry**)
    - **⏸ Pause Queue / ▶ Resume Queue** (pauses cleanly between files/steps)
    - **❄ Suspend Process / 🔥 Unsuspend** (freezes active `HandBrakeCLI` / `7z` / `rclone` OS threads immediately via `NtSuspendProcess` to free 100% CPU/GPU)
    - **⏹ Stop** (cancels active subprocesses and clears the `Up Next` wait-list)
- **Interrupted Mid-Shutdown Run Recovery Banner:**
  - If Housekeeping was closed or the PC rebooted while a queue, job, `.hash` verification, or compliance scan was running, an amber banner appears at the top of the workspace on next launch showing completed vs. remaining steps with **▶ Resume Remaining Jobs**, **↺ Restart Entire Queue**, **Auto-restart on startup**, and **✕ Dismiss**.
- **Global Live Terminal Drawer & Status Footer (`Ctrl+\``):**
  - The bottom footer bar shows a real-time SignalR connection dot, current active subprocess status and progress bar, a **Verbosity** selector (`Progress & Status`, `Everything`, `Quiet`, `Alerts Only`, `Custom`), and **Session Totals** (`Processed | Skipped | Errors`).
  - On any tab other than Dashboard, click the footer bar or press **`Ctrl+\``** to slide up the **Live Terminal Drawer**.

### 4.2 📊 View 1 — Dashboard (`DashboardPage`)
- **Alert Banners:** Displays real-time banners when waiting for a dropped NAS share (**🌐 Waiting for Network / NAS Reconnection**), when transcodes are waiting on locked files (**⏳ Deferred Pending Swap(s) Ready** with **🔄 Commit Swaps Now** and **🗑 Discard**), or when runs are waiting in the FIFO **⏭ Up Next** queue.
- **4 Live KPI Cards:**
  1. **Active Pipeline Engine:** Current queue/job name, step counter (`Current/Total`), subprocess status text, next scheduled run countdown, and progress bar.
  2. **Host CPU & Memory:** Live CPU utilization `%` and RAM `Used / Total GB`.
  3. **Disk I/O & Network Throughput:** Live aggregate disk read/write `MB/s`, network `Mbps`, and mounted drive count.
  4. **Archive Compliance & Integrity:** Count of open compliance/hash findings (click to jump directly to **🛡 Reports**).
- **🗂 Named Job Queues & Quick Dispatch Strip:** Cards for every Named Job Queue showing enabled job count, schedule badge, **▶ Run** / **⏭ Queue Next**, **🧪 Dry**, **Set Active**, and **Configure →**, plus a **+ Quick New Job** form in the header.
- **Dual-Pane Live Terminal (`DualPaneTerminal`):**
  - Switch between **Single Pane** and **Dual-Pane Split** (`Side-by-Side` or `Stacked`).
  - Each pane has its own **Target Node** selector (`Local` or any remote Fleet Node), live search filter, **Wrap** toggle, **Auto-Scroll** lock, **🧹 Clear**, and a bottom **Interactive PowerShell / CMD Bar** (`ArrowUp`/`ArrowDown` command history, live working directory prompt, and **Stop** button).

### 4.3 🗂 View 2 — Job Queues (`JobQueuesPage`)
- **Left Sidebar (`Named Queues`):** Select any queue, see its enabled job ratio and schedule mode, or click **+ New Queue**.
- **Queue Header & Actions:** Edit **Queue Name** and **Description**, or click **▶ Run This Queue**, **🧪 Dry Run**, or **🗑 Delete Queue**.
- **⏰ Independent Queue Schedule Configurator:**
  - Toggle **Enable Automatic Schedule** per queue.
  - Choose **Schedule Cadence Mode**: `Daily at Specific Time (HH:mm)`, `Interval Cadence (Every N Minutes)`, or `Multiple Specific Times (CSV)` (e.g. `03:00, 15:00`).
  - Toggle active **Days of Week** (`Monday`–`Sunday`) and inspect the live **Next 5 Calculated Runs** preview strip.
- **Ordered Pipeline Steps:** Attach jobs from your Defined Jobs repository, reorder steps with **↑ / ↓**, toggle individual steps on/off, jump to **Edit in Builder →**, or remove a job from the queue.

### 4.4 🛠 View 3 — Job Builder (`JobBuilderPage` & 8 Module Editors)
- **Left Sidebar (`Defined Jobs Repository`):**
  - Create jobs via **+ New Job ▾**, filter by module, sort by `Name (A → Z)`, `By Module`, or `Manual Order`, and switch between **Compact** and **Detailed** list cards.
  - Footer utilities: **↑ / ↓** manual reorder and **+ Add All 8 Starter Jobs**.
- **Job Header Card:** Edit the job's **Name**, toggle **Enabled**, choose **Post-Job Chain (`OnSuccessTriggerJobId`)** to automatically trigger another job when this job succeeds, and click **▶ Run Job Only**, **🧪 Dry Run Job**, **📋 Clone**, or **🗑 Delete**.
- **Module-Specific Configuration Editors:**
  1. **Proxy Generator Editor:** Select `HandBrake Encoding Profile`, `Proxy Filename Suffix Tag` (`360p_proxy`), `Source Extensions`, and target footage folders.
  2. **Archive Compressor Editor:** Select `HandBrake Profile`, `Output Container` (`.mkv`/`.mp4`), `Skip Files Containing Tag` (`_proxy)`), `Input Extensions`, and `Source → Archive Folder Mappings`.
  3. **Media Transcoder Editor:** Choose `Source Detection Mode` (`FilenameTagMatching` vs `MediaInfoResolutionScan`), `Master File Tag` (`(Original)`), `Drives to Scan` (`V, W, X, Y, Z`), `Drive Relative Subfolder`, active output resolution checkboxes (`144p`–`4K UHD`), the **Source Quality Tier → Output Profile Matrix**, custom scan folders, and **Alternate Language Edition Rules**.
  4. **ROM & Game Launchers Editor:**
     - Toggle `.m3u` generation, `.7z` auto-compression, fast `7z l -slt` header inspection, SSD scratch extraction on launch, Start Menu `.lnk` creation, and local `.bat` launchers.
     - Configure **9-Column CRC32+SHA-1 Manifest & DAT Auditing** (`No-Intro & Redump DAT Folder`, auto-download missing DATs, `.hash` placement, two-step archive inspection, **🔍 Scan Compliance Now**, **🔐 Verify & Heal .hash Now**, and **📐 Manage Archive Templates**).
     - Configure **Pre/Post-Launch Cloud Save & State Sync** (`rclone` multi-row sync paths with a live **Pre-Launch / Post-Launch Command Preview**).
     - Manage the **Console Collection Rules Table** (45+ canonical consoles, 16 emulator runners, extensions priority, archive path, compliance template override, and per-console save sync overrides).
  5. **Cloud Sync Editor:** Refresh detected `rclone` remotes, click **⚡ Run Once with --resync** to initialize `bisync`, and configure `bisync` / `sync` / `copy` folder pairs.
  6. **Humble Sync Editor:** Enter your `_simpleauth_sess` session cookie (with **👁 Reveal / 🙈 Hide**), target download folder, and extra CLI flags.
  7. **Custom Scripts Editor:** Build an ordered table of `.bat` / `.ps1` / `.py` / `.exe` tasks with arguments, working directory, optional `Required Path Check` (e.g. `V:\`), and `External CMD` toggle.
  8. **Collection Manifest & Bit Rot Editor:**
     - Configure `Unified Master Manifest`, base name, CSV delimiter, output formats (`CSV`, `JSON`, `Visual HTML`), **🟢 Auto-Sync Greenroom (:8420) on Change**, primary output folder, and secondary mirror folder.
     - Configure the **🔐 Integrity & Bit Rot Engine** (`Enable Bit Rot Detection`, `Create Missing .hash Files`, `Auto-Heal Repacked Containers`, `Two-Step Archive Inspection`, `.hash` placement, algorithm `SHA-1`/`CRC32`/`MD5`/`SHA-256`, `File vs Folder Hashing Logic`, and **🔐 Run .hash Verifier Now**).
     - Manage **Target Scan Folders** and **Dynamic Metadata Field Parsing Rules** (load 1-click preset bundles for `Personal Media`, `Personal ROMs`, `General Media`, `General Files`, or `General Audio`, and test rules live in the **C# .NET Regex Playground**).

### 4.5 🧩 View 4 — Modules Catalog (`ModulesCatalogPage`)
- Top **🔧 External Tool & Engine Readiness** strip shows resolved paths for `HandBrakeCLI`, `7z`, `rclone`, `hbd`, and `MediaInfo.dll`.
- Left column lists all 8 modules with an **Active** app-wide master switch and defined job count; right pane shows **✨ Core Capabilities**, **🧭 Quick Setup Guide**, **💡 Best Practices & Required Tools**, and **+ Add Job for `<Module>`**.

### 4.6 🛡 View 5 — Reports & Compliance Center (`ReportsCenterPage`)
- **4 KPI Summary Cards:** `Open Archive & Hash Issues`, `Cleanable Extra Clutter (7z d)`, `Missing Refs / Format Issues`, and `Resolved / Ignored`.
- **Filter & Remediation Toolbar:** Filter by Console, Issue Category (`Extra Files`, `Missing / Broken Refs`, `Wrong Format`, `Multi-ROM / Structure`, `Hash Integrity`), and Resolution Status (`Open Only`, `All`, `Ignored Only`, `Resolved Only`), or click **📐 Archive Templates**, **🔍 Scan Archives Now**, **📥 Export CSV**, and **📥 Export JSON**.
- **Master-Detail Split View & Safe `7z d` Cleanup:**
  - Select any flagged archive on the left to inspect its internal entry tree on the right (`PrimaryPayload`, `DiscTrackOrCue`, `CompanionHash`, or `ExtraClutter`).
  - Click **🧪 Preview 7z d Cleanup** to open a dry-run modal showing the exact `7z d` command, every clutter file slated for deletion, and every protected ROM/track file kept (`ReclaimedBytes`).
  - Click **🧹 Clean Extra Files (7z d)** to strip clutter without decompressing the archive, **🔧 Heal .hash Sidecar** to update its checksum sidecar, or **🙈 Ignore** / **🧹 Clear Resolved**.

### 4.7 ⚙ View 6 — Settings (`SettingsPage` — 8 Sub-Tabs)
1. **🔧 Tools & HandBrake Profiles:** Override paths for `HandBrakeCLI.exe`, `7z.exe`, `rclone.exe`, and `hbd.exe`, and edit the shared **HandBrake Encoding Profiles Catalog**.
2. **🛡 Startup & Process Guard:** Toggle `Launch Housekeeping Tray Daemon Automatically at Windows Logon`, `Start Silently in System Tray`, `Auto-Restart Jobs & Queues Interrupted Mid-Shutdown on Startup`, and configure the **🎮 Game / Streaming Process Guard** list (`obs64`, `retroarch`, `pcsx2`, etc.).
3. **🌐 Network Safety & Locking:** Toggle NAS drop pause/retry, `.partial` + Pending Swaps, startup `.partial` cleanup, distributed `.hklock` locking, and tune retry intervals and lock timeouts.
4. **🔐 Web, Tailnet & Desktop UI:** Configure HTTP port (`5178`), tray double-click default UI (`StandaloneWindow` vs `Browser`), 3-state color scheme (`system`, `dark`, `light`), UI density, `0.0.0.0` LAN/Tailnet binding, `Tailscale-User-Login` header trust, `RestrictRawShellToLocalhost`, **Remote Admin Password**, **Fleet Node API Token (`X-Housekeeping-Token`)**, and **📱 Mobile PWA & Tailscale Serve HTTPS** provisioning.
5. **🟢 Greenroom (`:8420`):** Enable Greenroom companion integration, probe `:8420`, trigger manual sync, and toggle auto-sync after Module 4 (`collect_library` + `verify_hashes`) or Module 8 (`collect_library` + `collect_plex`) manifest changes.
6. **🔔 Verbosity & Discord:** Configure terminal buffer size (`200`–`5000` lines), terminal/footer verbosity levels, custom log tag checkboxes, Windows tray balloon alerts, and **Discord Rich Embed Webhook** notifications (with **📨 Send Test Discord Embed**).
7. **🖧 Remote Fleet Nodes:** Add and probe remote LAN/Tailnet Housekeeping nodes (`HostOrIp`, `HttpApiPort: 5178`, `Node API Token`, `PreferHttpApi`, and `SMB Share Path` fallback).
8. **🩺 System Doctor:** Inspect live version/host/port/Tailscale health cards, refresh core executable dependencies, review mounted storage free-space bars, and run one-click **📦 Backup Config & State Now (`.7z`)** or **🩺 Export Diagnostic Bundle (JSON)**.

### 4.8 📜 View 7 — Run History (`HistoryPage`)
- Filter historical pipeline runs by status (`All`, `Completed`, `Errors`, `Cancelled/Interrupted`) or search text, sort by any column, select rows to export filtered **CSV** or **JSON** reports, and click **📄 View Log** on any row to inspect the full per-run log transcript (`logs\runs\Run_*.log`).

---

## 5. Everyday Workflows

### Workflow 1 — Auditing & Cleaning Clutter Inside ROM Archives Without Decompressing
1. Open **🛡 Reports** (`5`) and click **🔍 Scan Archives Now** (or run your Module 4 job with Archive Compliance enabled).
2. Filter by **Category: `Extra Files`** to view `.7z`/`.zip` archives containing scene `.nfo`, `.txt`, or `.url` clutter.
3. Click an archive row, click **🧪 Preview 7z d Cleanup** to verify that your primary ROM and `.cue`/`.bin` tracks are marked **Protected**, and click **🧹 Clean Extra Files (7z d)**.
4. If the archive has an external `.hash` sidecar, click **🔧 Heal .hash Sidecar** so the `# Container:` outer checksum matches the newly cleaned archive.

### Workflow 2 — Detecting Bit Rot & Healing Repacked Containers
1. Open **🛠 Job Builder** (`3`), select your **Collection Manifest** (Module 8) or **ROM Launchers** (Module 4) job, and scroll to the **Integrity & Bit Rot Engine** card.
2. Select verification mode:
   - **`FastHeaderAndHeal`:** Checks outer `# Container:` metadata first, falls back to `7z l -slt` inner CRC32 headers, and automatically heals `.hash` sidecars when repacked containers still hold 100% matching inner ROM payloads.
   - **`DeepCryptographicAndHeal`:** Recomputes full stream SHA-1/MD5/CRC32 across every file.
   - **`DryRunReportOnly`:** Audits sidecars without modifying any `.hash` files.
3. Click **🔐 Run .hash Verifier Now** and watch live progress in the Terminal Drawer (`Ctrl+\``). Any unhealed mismatches are logged directly into **🛡 Reports** under `Hash Integrity`.

### Workflow 3 — Installing Housekeeping as a Mobile PWA via Tailscale HTTPS
1. Open **⚙ Settings** (`6`) $\rightarrow$ **🔐 Web, Tailnet & Desktop UI**.
2. In the **📱 Mobile PWA & Tailscale Serve HTTPS** card, click **⚡ Enable Tailscale HTTPS (PWA)**.
3. If Tailscale Serve is not yet authorized on your tailnet, click the **🔑 Authorize Tailscale Serve & HTTPS ↗** button that appears; Housekeeping detects approval automatically within 4 seconds and provisions your `https://<machine>.<tailnet>.ts.net` certificate.
4. Open that `https://` URL on Android Chrome or iOS Safari and tap **📲 Install App** (or **Add to Home Screen**).

---

## 6. Notifications, Tray & Scheduling

- **System Tray Icon & Context Menu:** Right-clicking the Housekeeping tray icon provides quick actions without opening the UI:
  - **Open Housekeeping Window** (or **Open in Web Browser**)
  - **▶ Run Active Queue Now** / **🧪 Dry-Run Active Queue** / **⏹ Stop Active Run**
  - **⏸ Pause Schedules for 1 Hour** (or **▶ Resume Schedules**)
  - **📂 Open Data Folder** / **📄 Open Live Log**
  - **✕ Exit Housekeeping** (prompts for confirmation if a pipeline job is currently active so mid-run work is never accidentally aborted)
- **Game & Streaming Process Guard:** When `EnableProcessGuard` is checked in **Settings $\rightarrow$ Startup & Process Guard**, Housekeeping checks every 15 seconds whether any guarded executable (`obs64`, `retroarch`, `pcsx2-qt`, `duckstation`, `rpcs3`, `dolphin`, etc.) is running. Scheduled queues due during gameplay are cleanly deferred until the game or stream closes.
- **Post-Run Power Actions:** Configure `StayAwake`, `Sleep`, or `Shutdown` to trigger automatically after a long overnight transcode or backup queue finishes (skipped automatically during Dry Runs and `--test-mode`).

---

## 7. Troubleshooting & FAQ

| Symptom / Question | Cause | Resolution |
| :--- | :--- | :--- |
| **Dashboard shows `⏳ N Deferred Pending Swap(s) Ready`** | A media file finished transcoding in Module 1/2/3, but Plex or an SMB client had the destination file open when Housekeeping tried to swap the `.partial` file in. | Click **🔄 Commit Swaps Now** on the Dashboard banner once playback finishes, or let Housekeeping auto-sweep after the next run. |
| **Cloud Sync fails with `prior lock file found: ... .lck`** | A previous `rclone bisync` process was interrupted before removing its local `.lck` file. | Housekeeping automatically strips ANSI codes, verifies no `rclone.exe` process is active, deletes the orphaned `.lck` file, and retries once. If a lock is corrupt beyond single-retry recovery, click **⚡ Run Once with --resync** in the Cloud Sync Job Editor. |
| **Interactive Terminal CLI or Custom Scripts editor is disabled (`🔒 Raw Shell Restricted to Localhost`)** | You are connected from a remote LAN or Tailnet browser while `RestrictRawShellToLocalhost` is enabled (`true` by default). | Open Housekeeping directly on the host machine (`127.0.0.1:5178`), or uncheck `RestrictRawShellToLocalhost` in **Settings $\rightarrow$ Web, Tailnet & Desktop UI** if you intentionally want remote shell access. |
| **A job in my queue was skipped with `Module is disabled app-wide`** | The module's master switch is turned off in **🧩 Modules** (`4`). | Open **🧩 Modules**, check **Active** on the module card, and re-run the queue. |

---

## 8. Glossary

| Term | Definition |
| :--- | :--- |
| **`.hash` Sidecar** | Human-readable checksum file stored beside a media/ROM file (`<File>.hash`) or folder dump (`<Folder>.hash`) containing `# Container:` outer metadata and `FILE:` or `COMPOSITE_SHA1:`/`ENTRY:` payload hashes. |
| **`.hklock`** | Distributed JSON lockfile (`<target>.hklock`) refreshed every 60s on shared NAS storage so multiple Fleet Nodes never transcode the same file simultaneously. |
| **`.partial` File** | Temporary working output file written during `HandBrakeCLI` encoding and atomically swapped into the final filename only after a clean exit code `0`. |
| **Composite Folder Dump** | Multi-file console or disc directory structure (`PS3_GAME`, Wii U `code`/`content`/`meta`, `BDMV`, `VIDEO_TS`) hashed as a single deterministic `COMPOSITE_SHA1` unit via per-file CRC32 + Size entries. |
| **Pending Swap** | A completed `.partial` encode recorded in `data\pending_swaps.json` waiting for a file handle lock (e.g. Plex playback) to release so it can replace the target file. |
| **Two-Step Archive Verification** | Bit-rot check that verifies the outer archive `# Container:` hash first and falls back to 7-Zip's inner ROM CRC32 header (`7z l -slt`) to distinguish benign archive repacks from real payload corruption. |
