# Housekeeping — Architecture & Modules Reference Guide

> **Document Version:** 1.2 (`v1.0.0-rc.10-nightly.1`)  
> **Last Updated:** November 2026  
> **Target Framework:** .NET 8 (ASP.NET Core Kestrel + SignalR + WinForms/WebView2 Host) & React 19 TypeScript PWA / Windows 11 x64  
> **Purpose:** Permanent architectural specification and reference guide defining the identity, core goals, boundaries, inputs, outputs, and evolution of all 8 Housekeeping pipeline modules.

---

## 1. System Architecture & Design Philosophy

Housekeeping is an automated pipeline runner and media workstation orchestrator designed for unattended local and distributed execution across multiple machines, accessible via desktop browser, disposable native WebView2 window, or Tailscale HTTPS mobile PWA.

### 1.1 Decoupled Job Model
* **Modules** (`ModuleId` enum) define *reusable execution engines* (e.g. `ProxyGen`, `MediaTranscoder`, `GameLaunchers`).
* **Jobs** (`HousekeepingJob`) are *concrete instances* created by the user within the **Job Queue**. Each job possesses its own unique identifier (`JobId`), descriptive user-facing name, execution flags, and independent configuration container.
* **Module Type Immutability**: Once a job is created, its module type is **immutable**. This ensures configuration schema integrity and prevents accidental cross-module data corruption. To configure a different task, users create a new job.

### 1.2 Multi-Machine Distributed Coordination
* Multiple instances of Housekeeping may run simultaneously across different physical machines on the local network (e.g., Desktop encoding while Media Server scans NAS shares).
* **Distributed Locking Engine (`.hklock`)**: Shared resource directories and transcode targets are guarded by non-blocking lockfiles written with `FileMode.CreateNew`.
* Locks record the originating `MachineName`, `ProcessId`, and UTC timestamp. Locks older than 90 minutes or belonging to deceased processes are automatically recycled.

### 1.3 Two-Tier Enablement Architecture
1. **App-Wide Module Switch (`🧩 Modules` tab)**: Master toggle. Disabling a module globally disables all associated jobs, removes the module from creation dropdowns, badges job cards with `[MODULE DISABLED]`, and skips pipeline execution with informational logs.
2. **Per-Job Enabled Switch (`⚡ Job Queue` / `🛠 Job Builder`)**: Granular queue control. Allows individual jobs to be temporarily paused without affecting other jobs powered by the same module.

---

## 2. Module Identity & Capabilities Matrix

| # | Clean Name | Icon | Engine Identifier | Primary Purpose | Key External Tools | Primary Output |
|---|---|---|---|---|---|---|
| **1** | **Proxy Gen** | 🎬 | `ModuleId.ProxyGenerator` | Generates lightweight editing proxies alongside originals | `HandBrakeCLI.exe` | Suffix-tagged proxy media (e.g. `Clip (360p_proxy).mp4`) |
| **2** | **Video Archiver** | 📦 | `ModuleId.ArchiveCompressor` | Compresses raw footage into long-term archive storage | `HandBrakeCLI.exe` | Space-efficient HEVC/AV1 archives preserving folder trees |
| **3** | **Media Transcoder** | 📺 | `ModuleId.MediaTranscoder` | Multi-tier streaming encodes for Plex, Jellyfin, and Emby | `HandBrakeCLI.exe`, `MediaInfo.dll` | Resolution-tiered stream files (144p to 4K UHD) |
| **4** | **Game Launchers** | 🎮 | `ModuleId.RomLauncherGenerator` | Library prep, compression, and launchers for emulators & PC games | `7z.exe`, optional `rclone.exe` | Multi-disc `.m3u`, solid `.7z`, `.bat` launchers, Start Menu shortcuts |
| **5** | **Cloud Sync** | ☁ | `ModuleId.CloudSync` | Two-way & mirror synchronization with cloud/NAS storage | `rclone.exe` | Synced cloud folders, bisync state tracking |
| **6** | **Humble Library** | 📚 | `ModuleId.HumbleSync` | Automated download and audit of Humble Bundle purchases | `hbd.exe` | Organized DRM-free games, ebooks, comics, audiobooks |
| **7** | **Custom Tasks** | ⚡ | `ModuleId.CustomScripts` | Arbitrary shell script, batch, and CLI task orchestration | Windows CMD, PowerShell, Python | Command execution logs, exit codes, process lifecycle |
| **8** | **Collection Manifest** | 📋 | `ModuleId.CollectionManifest` | Multi-location storage pool auditing, hashing & catalog export | Built-in C# cryptographic engine | CSV, JSON, and dark-mode HTML manifests + `.hash` sidecars |

---

## 3. Deep-Dive Module Specifications

### Module 1: Proxy Gen (`🎬`)
* **Former Name:** Video Proxy Generator
* **Scope Evolution:** Renamed to **Proxy Gen** to reflect future generalization. While its initial focus is video editing proxies, the engine is structured to support audio proxy drafts, contact sheets, and thumbnail suites.

#### Core Goal
Monitors active editing and capture folders (e.g., OBS, ShadowPlay, camera imports) and generates lightweight, low-resolution proxy copies alongside the originals. Video editing software (DaVinci Resolve, Premiere Pro, Final Cut) can instantly toggle these proxies for butter-smooth timeline scrubbing without taxing hardware.

#### Inputs & Configuration
* **Monitored Folders:** Recursive folder trees where raw footage is dropped.
* **HandBrake Profile:** A fast, low-overhead profile (e.g., 360p or 480p with `veryfast` or NVENC).
* **Suffix Tag:** Suffix string appended before extension (default: `360p_proxy`), resulting in `Footage (360p_proxy).mp4`.
* **Extensions:** Comma-separated list (e.g. `.mp4, .mkv, .mov, .avi`).

#### Boundaries & Invariants
* **Zero Modification of Master:** Source files are opened read-only; never touched or altered.
* **Skip Idempotency:** Any file already possessing the proxy suffix or with an existing companion proxy is skipped instantly without invoking the encoder.
* **Atomic Output:** Proxies are encoded to `.partial` files before being committed to prevent corrupted clips in NLEs.

#### Architectural Roadmap
* Multi-track audio proxy generation (e.g. lightweight MP3/Opus proxies for multitrack WAV session files).
* Image contact-sheet proxy batching for photography workflows.
* Direct ffmpeg-based ultra-low-latency proxy engine option alongside HandBrakeCLI.

---

### Module 2: Video Archiver (`📦`)
* **Former Name:** Video Archive Compressor

#### Core Goal
Transfers finished capture directories (e.g., raw gaming recordings, completed project assets) into permanent archive storage, encoding the video with high-efficiency codecs (HEVC/H.265 or AV1) to reclaim 50%–75% of storage space while preserving directory structure.

#### Inputs & Configuration
* **Folder Mappings:** Explicit pairs of `Source Folder` → `Archive Destination Folder`.
* **HandBrake Archive Profile:** High-efficiency profile (e.g., `Archive HEVC RF24`).
* **Container Extension:** User choice of `.mkv` or `.mp4`.
* **Skip Tags:** Ignore files containing specific tags (e.g. `(proxy)`, `(draft)`).

#### Boundaries & Invariants
* **Non-Destructive by Default:** Source files remain intact until the user explicitly enables source cleanup.
* **Directory Hierarchy Preservation:** If `Source\2026-10\Game\Session1.mp4` is archived, it appears at `Archive\2026-10\Game\Session1.mkv`.
* **Verification Pre-Requisites:** Files must pass size and integrity checks before being marked complete.

#### Architectural Roadmap
* Automated storage space savings report per run (e.g. "Reclaimed 142.6 GB across 18 video files").
* Optional SHA-256 integrity validation comparing video stream duration and audio sync before source deletion.

---

### Module 3: Media Transcoder (`📺`)
* **Former Name:** Multi-Tier Media Transcoder

#### Core Goal
Comprehensive streaming server transcode pipeline for home media platforms (Plex, Jellyfin, Emby). Scans master drive collections, detects source resolution and audio tracks, and encodes standardized resolution tiers (144p, 240p, 360p, 480p, 720p, 1080p, 1440p, 4K UHD) into organized file structures.

#### Inputs & Configuration
* **Scan Roots:** Target drives (e.g., `V`, `W`, `X`, `Y`, `Z`) or custom directory paths.
* **Source Quality Detection:**
  * *Filename Tag Matching:* Classifies source by markers (`(DVD)`, `(BR)`, `(UHD)`).
  * *Native MediaInfo:* Dynamically analyzes stream properties via `MediaInfo.dll`.
* **Resolution Profile Matrix:** Explicit HandBrake profile mapped to each active resolution tier.
* **Language Edition Rules:** Regex/tag rules routing multi-language audio (e.g., Japanese anime with English subtitles) into dedicated edition folders (`English`, `Japanese`, `Dubbed`).
* **Master Identifier Tag:** Reference string designating source files (e.g. `(Original)`).

#### Boundaries & Invariants
* **No Upscaling:** Sources are never upscaled beyond their detected physical resolution ceiling (e.g., a 480p DVD rip will never generate 720p or 1080p tiers).
* **Exponential Backoff Atomic Commit:** Temporary files (`.partial`) are committed with a 10-attempt exponential backoff retry loop with file-sharing flags to prevent network file-locking failures.
* **Concurrent Machine Safety:** Distributed `.hklock` metadata locks guard against dual-machine transcoding conflicts.

#### Architectural Roadmap
* Jellyfin / Plex webhook trigger upon job completion to initiate automated library scans.
* Subtitle extraction and OCR passthrough for external `.srt` companion generation.
* AV1 hardware encoding profile tier support.

---

### Module 4: Game Launchers (`🎮`)
* **Former Name:** ROM Archiver & Emulator Launchers / Retro Launchers
* **Scope Evolution:** Renamed to **Game Launchers** to expand beyond retro emulators and incorporate native PC game executables (`.exe`), DRM-free standalone releases, and custom wrapper environments.

#### Core Goal
Orchestrates entire game libraries across emulators and PC installations:
1. Detects multi-disc games (`Disc 1`, `Disc 2`) and automatically generates standardized `.m3u` playlists.
2. Compresses loose ROMs and disc images into solid 7-Zip (`.7z`) archives, bundling the `.m3u` inside to save massive amounts of disk space.
3. Generates standalone launch scripts (`.bat` / `.exe`) and Windows Start Menu shortcuts that extract games to a fast NVMe scratch folder on demand, execute the target emulator or game executable, and cleanly reclaim scratch storage upon game exit.
4. Synchronizes emulator and PC game saves to cloud storage before launch and after shutdown.
5. Authors 9-column CRC32+SHA-1 `collection_manifest.csv` catalogs, performs zero-decompression `.7z`/`.zip` header CRC32 DAT comparisons against No-Intro/Redump DATs, verifies/heals `.hash` sidecars, evaluates Archive Structure Templates into actionable Compliance Reports, and triggers Greenroom Companion (`:8420`) re-ingestion when manifests change.

#### Inputs & Configuration
* **Console / Category Rules:** Console name, subfolder, emulator executable, core arguments, extensions.
* **Solid 7-Zip Parameters:** Compression level, solid block sizing, source file cleanup switch.
* **Cloud Save Sync Settings:** Local save directory, rclone remote destination, pre- and post-launch sync toggles.
* **Launch Script Generator:** Scratch folder location, window title, process guard list.

#### Boundaries & Invariants
* **Integrity Verification:** 7-Zip archives are always tested with `7z t` before source files are purged.
* **Supplemental Preservation:** Multi-track cues, `.bin`, `.sbi`, `.sub`, and `.ccd` companion files are bundled together in the archive.
* **Scratch Isolation:** Game saves are never written only to scratch; save paths point to persistent local or cloud directories.

#### Architectural Roadmap
* Native PC `.exe` launcher generation with cloud save sync, play-time tracking, and Discord Rich Presence integration.
* Automated CHD / RVZ conversion pipeline options alongside `.7z`.
* Steam and Heroic Games Launcher shortcut export.

---

### Module 5: Cloud Sync (`☁`)
* **Former Name:** Cloud Folder Sync (rclone)

#### Core Goal
Provides network-resilient, unattended synchronization between local folders (documents, game saves, render caches) and cloud remotes (Google Drive, OneDrive, Nextcloud, AWS S3, local NAS) powered by `rclone`.

#### Inputs & Configuration
* **Sync Jobs:** List of individual synchronization items.
* **Sync Mode:**
  * `bisync`: Two-way bidirectional synchronization with conflict tracking.
  * `sync`: One-way mirror (destination becomes an exact replica of source).
  * `copy`: Transfers new and updated files without deleting destination extras.
  * `move`: Transfers files and deletes from source upon successful upload.
* **Network Recovery Settings:** Infinite retry loops with backoff to tolerate Wi-Fi drops and broadband outages.
* **Bandwidth Limits:** Configurable transfer caps (e.g. `--bwlimit 15M`).

#### Boundaries & Invariants
* **Network Drop Resiliency:** Network dropouts pause execution rather than failing jobs.
* **Bisync First-Run Guard:** Bisync operations require an initial `--resync` baseline run, accessible via one-click recovery in the UI.

#### Architectural Roadmap
* Automated conflict file resolution rules (keep newest, keep local, rename duplicate).
* Multi-remote parallel replication (single source synchronized to multiple offsite destinations simultaneously).

---

### Module 6: Humble Library (`📚`)
* **Former Name:** Humble Bundle Library Sync / Downloader

#### Core Goal
Maintains a complete offline, local mirror of the user's purchased Humble Bundle library (DRM-free games, ebooks, audiobooks, comics, soundtracks) using `hbd`.

#### Inputs & Configuration
* **Session Cookie:** Humble Bundle `_simpleauth_sess` session cookie extracted from browser session.
* **Library Path:** Local storage directory for downloaded bundle assets.
* **Format & Platform Filters:** Filter by platform (Windows, Linux, Android) or format (PDF, EPUB, MOBI, FLAC, MP3).
* **Extra CLI Arguments:** Custom passthrough flags for `hbd`.

#### Boundaries & Invariants
* **Secret Sanitization:** Session cookies are masked in UI and strictly stripped whenever sharing or exporting configurations (`CreateSanitizedCopyForSharing`).
* **Non-Destructive Resume:** Existing valid downloads are verified and skipped; interrupted downloads resume seamlessly.

#### Architectural Roadmap
* Multiple account management.
* Unified "Digital Library" expansion incorporating GOG and itch.io sync providers.

---

### Module 7: Custom Tasks (`⚡`)
* **Former Name:** Custom Scripts & CLI Tasks

#### Core Goal
Universal execution environment within the Housekeeping pipeline for custom user scripts, disk utilities, PowerShell automation, Python processors, and external CLI tools.

#### Inputs & Configuration
* **Script / Executable Path:** Absolute or relative path to `.bat`, `.cmd`, `.ps1`, `.py`, or `.exe`.
* **Arguments & Parameters:** Dynamic command-line parameters.
* **Working Directory:** Configurable execution directory.
* **Run in External CMD:** Toggle between running silently inside Housekeeping's embedded Terminal or opening an interactive Windows console window.
* **Network Path Pre-Check:** Ensures mapped network drives or NAS shares are awake before executing scripts.

#### Boundaries & Invariants
* **Process Throttling:** Execution honors the global CPU/process priority settings configured in Housekeeping.
* **Terminal Stream Capture:** Stdout and stderr are captured and color-coded in real-time in the Housekeeping Terminal.

#### Architectural Roadmap
* Cross-platform PowerShell Core (`pwsh`) runner detection.
* Variable interpolation (e.g., passing `${ActiveJobName}`, `${TodayDate}`, `${WorkingDir}` into arguments).
* Conditional job chaining (execute Task B only if Task A returns exit code 0).

---

### Module 8: Collection Manifest Auditor (`📋`)
* **Former Name:** Collection Manifest Generator

#### Core Goal
Enterprise-grade file audit, inventory extraction, cryptographic verification, and multi-format catalog authoring for large media libraries, game dumps, and cold archives across local drives and network storage pools.

#### Inputs & Configuration
* **Unified Master vs Discrete Manifests:** Combine all target folders into a single unified master manifest or output separate manifests per location.
* **Target Directories & Locations DataGrid:**
  * Location Identifier (e.g. `NAS-Pool1`, `DAS-RAID`, `LocalSSD`).
  * Target folder paths with inline `[ 📁 ]` folder browser picker.
  * Subdirectory recursion toggle.
  * System / Platform DAT matching selection.
  * Folder Dump mode (treat top-level subdirectories as atomic units, e.g. for PS3/PS4 disc rips or multi-file game folders).
  * Custom file extension filters.
* **Audit Engine & Cryptographic Verification:**
  * File Hashing toggle (Fast metadata enumeration vs deep content hashing).
  * Algorithm selection: CRC32, SHA-1, MD5, SHA-256.
  * Skip Existing Verified Hashes: Leverages existing CSV/manifest hash values for unchanged files (matching size & timestamp) to avoid redundant disk I/O.
  * Generate `.hash` sidecars and optional 7-Zip archive packaging.
* **Custom Text Parsing & CSV Field Extraction Engine (`ManifestFieldParseRule`):**
  * Extract dynamic custom metadata fields from file names, relative paths, full paths, or parent folders into new columns.
  * Match modes: Regex Named Capture, Regex Pattern with substitutions (`$1`), Text Contains, and Split Token by delimiter and index.
  * Bundled presets: 🎬 Video Presets (ShowTitle, Season, Episode, Resolution, Codec, Edition), 🎮 Game Presets (Region, DiscNumber, Revision).
  * Interactive Rule Test Playground right in the inspector.
* **Multi-Format Export & Mirroring:**
  * Generates structured `.csv` (configurable delimiter: comma, semicolon, tab, pipe), `.json`, and a standalone responsive `.html` summary report.
  * Secondary Mirror Backup directory: Automatically replicates generated manifests to off-site or centralized storage.

#### Boundaries & Invariants
* **Zero Corruption Guarantee:** Reads all files strictly in shared read-only mode (`FileShare.ReadWrite`).
* **Deterministic Composite Folder Hashing:** Uncompressed game/disc folders compute deterministic composite hashes by scanning all contained files in sorted lexical order.
* **Dynamic Column Schemas:** Custom parsed tokens automatically expand CSV headers and HTML columns without requiring database migrations.

---

## 4. Guidelines for Adding Future Modules

When introducing new modules into Housekeeping, follow this standardized architecture:

1. **Enum Definition:** Add the module to `ModuleId` enum in [`AppConfig.cs`](./Housekeeping.Core/Models/AppConfig.cs).
2. **Display Names & Metadata:** Register clean naming, icon, category, and required CLI tools in [`ModuleCatalogService.cs`](./Housekeeping.Core/Services/ModuleCatalogService.cs) and `HousekeepingJob` badge helpers in [`AppConfig.cs`](./Housekeeping.Core/Models/AppConfig.cs).
3. **Data Model:** Create an independent configuration class implementing `ObservableObject` with a `.DeepClone()` helper and defaults. Add its per-job property to `HousekeepingJob`.
4. **Execution Logic:** Implement execution under [`Housekeeping.Core/Services/Modules/`](./Housekeeping.Core/Services/Modules/) adhering to `ModuleExecutionContext`, streaming logs to `ctx.Log()`, updating counters via `ctx.RecordProcessed()` / `ctx.RecordSkipped()` / `ctx.RecordError()`, and honoring `ctx.IsDryRun` and `CancellationToken`, then wire dispatch into [`PipelineOrchestratorService.cs`](./Housekeeping.Core/Services/PipelineOrchestratorService.cs).
5. **Web UI Editor:** Add a dedicated TypeScript/React module editor under [`Housekeeping.Web/src/pages/editors/`](./Housekeeping.Web/src/pages/editors/) and mount it in [`JobBuilderPage.tsx`](./Housekeeping.Web/src/pages/JobBuilderPage.tsx).
6. **Documentation & Tests:** Add the module specification to this reference document (`MODULES_REFERENCE.md`), add xUnit coverage in [`Housekeeping.Tests`](./Housekeeping.Tests/), and log the addition in [`CHANGELOG.md`](./CHANGELOG.md).
