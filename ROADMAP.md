# Housekeeping — Roadmap & Milestones

> **Active Packaged Version:** `v0.1.11` (`2026-11-07`, [`active.state`](file:///D:/Projects/Personal/Housekeeping/active.state))  
> **Active In-Dev Target:** `v0.1.12` (Default `PATCH+1` toward `v1.0.0` GA Summit)  
> **Maintained By:** `@tessa` & `@pland` (per [Dev Bible Standard 14](file:///D:/Projects/Personal/_DevBible/14_IMPLEMENTATION_PLANS_AND_BLUEPRINTS.md) & [Standard 16](file:///D:/Projects/Personal/_DevBible/16_PROJECT_DOSSIERS_AND_FEATURE_TRACKING.md))

---

## 1. North Star Goal

Provide a 100% unattended, network-resilient, zero-data-loss media workstation, archive compliance, cloud sync, and cryptographic ROM collection automation engine across local and NAS storage pools—accessible from desktop tray, disposable zero-idle-RAM WebView2, or mobile Tailscale PWA, with strict fail-closed sandbox containment, 4-pillar signed single-EXE installers, and clean `Mother` ecosystem orchestration hooks.

---

## 2. Active In-Dev Milestone (`v1.0.0` GA Summit)

- **Target Version:** `v1.0.0` (Culmination of the `v1.0.0` Release Candidate Epoch)
- **Baseline Packaged Build:** `v1.0.0-rc.11-hf.2` ([`Housekeeping-Setup-v1.0.0-rc.11-hf.2-win-x64.exe`](file:///D:/Projects/Personal/Housekeeping/Installers/Housekeeping-Setup-v1.0.0-rc.11-hf.2-win-x64.exe))
- **Active `RC+1` Plan (`v1.0.0-rc.12`):** [`Dev/docs/plans/v1.0.0-rc.12.2026-11-03.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/plans/v1.0.0-rc.12.2026-11-03.md) (`Design Locked — Awaiting Execution Directive` — Fleet Inter-App Operability, Opt-In `AutoAlignWithSisterApps`, & Media/ROM/Save/VOD Coexistence)
- **Status:** `v1.0.0-rc.12` Planned & Locked Ahead of `v1.0.0` GA Summit
- **Milestone Deliverables & Hard Set Goals:**
  - [x] **12-Pillar Sister-App Parity Sweep (`v1.0.0-rc.11`):** Delivered WinForms single-surface GUI installer (`650×434`/`650×544`), Clean Release Root (`app\`, `scripts\`, `scripts\interactive\`), Amber (`#f59e0b`) left sidebar rail, `Ctrl+K` Omni-Search, `?` shortcuts modal, `🩺 System Doctor`, Mother disk-arbitration hooks (`/api/scheduler/*`), `--managed-by-mother` tray suppression, table multi-column sorting & CSV/JSON exports, decoupled legacy sister buttons & Task Scheduler watchdog, ANSI-clean `rclone bisync` stale lock recovery, and atomic `PendingSwapService.RetrySwap`.
  - [x] **Game Launcher Decoupling & Universal Media Manifest Bit Rot Engine (`v1.0.0-rc.11`):** Complete removal of redundant `.hash` manipulation from Module 4 (`RomLauncherGeneratorModule`); transformed Module 8 (`CollectionManifestModule`) from ROM DAT matching into a universal media cataloger with Two-Step Archive Bit Rot verification (`# Container:` + inner payload fallback), baseline capture, auto-healing, and ShadowLibrary parity.
  - [x] **Hotfix 1 Parity & Lifecycle Hardening (`v1.0.0-rc.11-hf.1`):** Canonical Parent Configuration Adoption (`<Root>\settings.json` detected/adopted from `app\`), fixed `<GlobalTerminalDrawer />` flex nesting in `App.tsx` (restoring full-width dashboard layout and docked bottom footer), fail-closed installer 3-stage process termination & `POST /api/v1/system/shutdown`, automatic post-upgrade daemon relaunch (`--launch`/`--no-launch`), and post-backup root clutter cleanup (`CleanupObsoleteInstallationFiles`).
  - [x] **Hotfix 2 Sister-App Clean Release Root & Desktop Window Parity (`v1.0.0-rc.11-hf.2`):** Strictly contained all user state and databases in `<InstallRoot>\data\` with legacy root migration (`MigrateLegacyRootStateIfPresent`), contained uninstaller and installer receipts in `<InstallRoot>\app\`, and upgraded `WebServerConfig.DefaultDesktopLaunchMode` to `StandaloneWindow` by default (`Schema v4`).
  - [x] **Sandbox Clean Sweep Sign-Off (`@inspecter`):** Confirmed 100% clean sweep in `Sandbox\`, 0 open defects in `Dev\docs\reports\`, and 160/160 passing tests.
  - [ ] **Dual-Mode Stop Controls (`CORE-07`):** Unambiguous execution controls across Web, SignalR, and Tray to abort active subprocess (`Stop Current Job`) without purging `Up Next`, alongside `Stop Entire Queue`.
  - [ ] **Auto-Scroll Saturation Fix & Word-Wrap Geometry (`UI-05`):** Monotonic line counter latching and `ResizeObserver` row measurements preventing terminal auto-scroll freeze during rolling log buffer saturation.
  - [ ] **Storage Strip & Form Ergonomics Consolidation (`UI-06`):** Pinned storage strip in sidebar displaying 10+ mounted disks, single-line module badges, strict dropdowns vs. combos, and Standard 09 2-line hover hints.
  - [ ] **Cross-App Zero-Double-Work Contract (`FLEET-01` & `FLEET-02`):** Full coexistence contract with ShadowLibrary (zero ROM hashing rewrite), Redroom (edition/SRT preservation), and Mother (lock arbitration and VOD deferral).
  - [ ] **24-Hour Production Soak Verification:** Confirm zero memory leaks, zero unhandled crash dumps, and clean `Housekeeping_Live.log` rotation under multi-machine scheduled workloads.
  - [ ] **Stream GA Packaging Approval:** Obtain explicit human approval to strip `-rc.<n>`, package official `v1.0.0` GA installer and 6-file `Releases\` bundle, deploy to canonical `D:\Programs\Housekeeping\`, and advance active development to `v1.1.0-rc.1`.
- **Testing & Verification Areas:**
  - Active job cancellation vs. queue clearing verification in xUnit and live SignalR.
  - Terminal buffer saturation benchmark (`>400` log lines under active word-wrap).
  - Live mounted disk enumeration testing with $\ge 9$ local and NAS drive letters.
  - Multi-app `.hklock` contention simulation with ShadowLibrary and Redroom.

---

## 3. Upcoming Horizons: Granular Minor Releases & Major Epochs

### Major Epoch 1.0: The Definitive Local Media Workstation (Current Release Gate)
*Target: Complete feature lock, zero open P0–P2 reports, and signed 4-pillar GA release installer.*

#### `v1.0.0` — General Availability (GA Release Gate)
- **Milestone Thesis:** Production hardening, UX ergonomic consolidation, zero-double-work sister-app coexistence, and official GA distribution.
- **Target Deliverables:**
  - [ ] Complete final regression pass across all modules in `Sandbox\`.
  - [ ] Implement `CORE-07` Dual-Mode Stop Controls (`Stop Current Job` vs `Stop Entire Queue`).
  - [ ] Fix `UI-05` Terminal Auto-Scroll Saturation and word-wrap geometry via `ResizeObserver`.
  - [ ] Deliver `UI-06` Storage strip consolidation (10+ local and NAS disks) and strict dropdown enums.
  - [ ] Finalize `FLEET-01..02` Cross-App Coexistence Contract with ShadowLibrary, Redroom, and Mother.
  - [ ] Complete 24-hour production soak with 0 memory leaks and clean log rotation.
  - [ ] Obtain explicit human approval to strip `-rc.<n>` and cut official `v1.0.0` GA package.
  - [ ] Synchronize central `D:\Projects\Installers\` with signed `Housekeeping-Setup-v1.0.0-win-x64.exe`.

---

### Roadmap Horizon: Building Toward Major Epoch 2.0

> **Strategic Mental Model:** Roadmaps build **toward** the next Major Release as the destination summit (`1.1 -> 1.2 -> 1.3 -> 1.4 -> 2.0`), rather than starting at it. Each minor release delivers a concrete, production-ready stepping stone.
>
> **Architectural Boundary:** Housekeeping remains strictly a **media workstation, transcoding, archive compliance, and storage automation engine**. It does NOT attempt to be an emulation station (uStation) or full collection preservation vault (ShadowLibrary).

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│ v1.0.0 GA: The Definitive Local Media Workstation (Current Release Gate)        │
└───────────────────────────────────────┬─────────────────────────────────────────┘
                                        │
                                        ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│ v1.1.0: Deterministic Profile Catalog & Worker Agent Daemon (hk-worker)         │
│ (Canonical Profile Catalog, Headless Worker CLI, Token Auth, 15s Heartbeats)    │
└───────────────────────────────────────┬─────────────────────────────────────────┘
                                        │
                                        ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│ v1.2.0: Direct Chunked HTTP WAN Transfer Engine (Standard 19 §5 6-Pillar)       │
│ (Authenticated HTTPS Streaming, HTTP Range Resume, WAN/Tailscale Safe Staging)   │
└───────────────────────────────────────┬─────────────────────────────────────────┘
                                        │
                                        ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│ v1.3.0: Automated Scheduled Storage Pool Scrubbing & Low-Priority I/O Throttling│
│ (Unattended Bit Rot Verification, 9-Column Manifest Comparison, Non-Destructive)│
└───────────────────────────────────────┬─────────────────────────────────────────┘
                                        │
                                        ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│ v1.4.0: Cluster Analytics, Fleet Monitor & Node Failover Re-Queueing           │
│ (Real-Time Node Topology, Dropped Worker Re-Queueing, Historical ROI Trends)   │
└───────────────────────────────────────┬─────────────────────────────────────────┘
                                        │
                                        ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│ 🌟 v2.0.0 SUMMIT: Full Autonomous Distributed Media & Transcode Cluster        │
│ (Uniting LAN & Tailscale Workers, Deterministic Dispatch, Auto-Scrubbing & Mesh)│
└─────────────────────────────────────────────────────────────────────────────────┘
```

---

#### `v1.1.0` — Deterministic Profile Catalog & Worker Agent Daemon
- **Milestone Thesis:** Eliminate unexpected transcode quality/size drift by establishing a centralized operator-defined profile catalog and shipping the lightweight headless `hk-worker` daemon.
- **Target Deliverables:**
  - [ ] **`PROF-01` Centralized Profile Catalog:** Define canonical transcode profiles in `data\profiles.json` (exact HandBrake CLI arguments, bitrate caps, audio passthrough rules) with Web PWA editor.
  - [ ] **Capability Matching Engine:** Worker nodes report supported hardware accelerators (NVENC, QSV, AMF, CPU cores); coordinator only dispatches matching jobs.
  - [ ] **`DIST-01` Lightweight Worker Agent (`hk-worker`):** Standalone headless CLI/daemon runnable on `WORKER-NODE`, `MINISFORUM`, and NAS over LAN or Tailscale.
  - [ ] **Mutual Token Authentication & Heartbeats:** `X-Housekeeping-Worker-Token` validation and 15s structured heartbeats with child process tree containment.
- **Testing & Verification Areas:**
  - Profile parameter validation and CLI argument injection prevention.
  - Worker node registration and capability negotiation tests.
  - 15s heartbeat watchdog timeout detection.

#### `v1.2.0` — Direct Chunked HTTP WAN Transfer Engine
- **Milestone Thesis:** Break free from local SMB/NFS path dependencies, allowing worker nodes anywhere on Tailscale or WAN to encode files via authenticated, resumable HTTPS streaming.
- **Target Deliverables:**
  - [ ] **`XFER-01` Chunked Payload Streaming (`PUT /api/v1/jobs/{id}/payload`):** Stream source media directly to worker scratch storage.
  - [ ] **Resumable HTTP Range Support:** Resume interrupted payload uploads or completed encode downloads via HTTP `Range` on `.partial_*` files.
  - [ ] **Standard 19 §5 6-Pillar Safety:** Connect (10s) and stall (30s) timeouts, exponential backoff with full jitter, token-bucket bandwidth throttle, and streaming SHA-256 validation.
- **Testing & Verification Areas:**
  - Sever WAN connection at 50% chunk upload; verify clean Range resume without corrupting file.
  - Enforce token-bucket throttle and verify zero network saturation on host machine.

#### `v1.3.0` — Automated Storage Pool Scrubbing & Low-Priority I/O Throttling
- **Milestone Thesis:** Unattended, background cryptographic integrity assurance across cold disc archives, ROM libraries, and video pools.
- **Target Deliverables:**
  - [ ] **`SCRUB-01` Storage Pool Scrubber Engine:** Background task comparing cold files against 9-column manifests and `.hash` sidecars.
  - [ ] **Low-Priority I/O Throttling:** Throttle disk reads to guarantee zero dropped frames during local media streaming or live broadcast recording.
  - [ ] **Scrub Reporting & Bit Rot Alerts:** Write non-destructive audit reports to `data\scrub_reports\` and emit prominent terminal/disk alerts.
- **Testing & Verification Areas:**
  - Corrupt 1 byte in a test archive; verify scrubber flags Bit Rot without halting or modifying the file.
  - I/O throughput benchmarks during simultaneous 4K media playback.

#### `v1.4.0` — Cluster Analytics, Fleet Monitor & Node Failover Re-Queueing
- **Milestone Thesis:** Real-time visibility into distributed transcode operations, cluster health KPIs, and automated self-healing against node crashes.
- **Target Deliverables:**
  - [ ] **`ANLYT-01` Cluster Dashboard:** Real-time topology map in Web PWA displaying node CPU/GPU loads, active encodes, and transcode FPS.
  - [ ] **Automated Node Failover Re-Queueing:** If a remote worker drops heartbeat mid-encode, automatically cancel the remote task and return the job to coordinator queue.
  - [ ] **Historical Storage ROI Trend Rollups:** Interactive charts showing gigabytes reclaimed, compression ratios, and processing throughput over time.
- **Testing & Verification Areas:**
  - Kill worker process mid-encode; verify coordinator detects failure and re-queues job to another node or local pool.
  - Storage ROI math verification against actual disk footprint deltas.

#### `v2.0.0` GA Summit — Full Autonomous Distributed Media & Transcode Cluster
- **Milestone Thesis:** The crowning release uniting local and remote workers across LAN and Tailscale, deterministic profile dispatch, resumable WAN streaming, automated storage scrubbing, and fleet-wide Mother integration.
- **Target Deliverables:**
  - [ ] Full end-to-end multi-node cluster soak test across heterogeneous hardware over Tailscale.
  - [ ] Complete cluster management plane in Web PWA and Mother suite integration.
  - [ ] Package official `v2.0.0` GA installer and 6-file release bundle.

---

## 4. Completed Milestones

| Version | Date | Headline Achievement | Plan / Report Reference |
| :--- | :---: | :--- | :--- |
| **`v1.0.0-rc.11-hf.2`** | `2026-11-02` | Hotfix 2: Sister-App Clean Release Root containment (`data\` user state, `logs\`, `backups\`, `app\` installer metadata containment in 100% parity with `ShadowLibrary`), automatic legacy root migration (`MigrateLegacyRootStateIfPresent`), and default native Standalone WebView2 Desktop Window (`WebServerConfig.DefaultDesktopLaunchMode = "StandaloneWindow"` via Schema v4). | [`v1.0.0-rc.11-hf.2.2026-11-02.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/plans/v1.0.0-rc.11-hf.2.2026-11-02.md) |
| **`v1.0.0-rc.11-hf.1`** | `2026-11-02` | Hotfix 1: Canonical parent config adoption (`TryAdoptParentCanonicalSettings` / `--data-dir <Root>` forwarding), fixed `<GlobalTerminalDrawer />` flex nesting inside `.hk-body-area` (restoring full-width dashboard and docked bottom footer), fail-closed installer 3-stage process lifecycle (`CloseMainWindow` -> HTTP shutdown -> tree kill), post-backup legacy root clutter cleanup (`CleanupObsoleteInstallationFiles`), automatic daemon relaunch (`--launch`/`--no-launch`), and `POST /api/v1/system/shutdown`. | [`v1.0.0-rc.11-hf.1.2026-11-02.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/plans/v1.0.0-rc.11-hf.1.2026-11-02.md) |
| **`v1.0.0-rc.11`** | `2026-11-02` | 12-Pillar Sister-App Parity, Game Launcher Decoupling & Universal Media Manifest Bit Rot Engine: WinForms single-surface GUI installer (`650×434`/`650×544`), Clean Release Root (`app\`, `scripts\`, `scripts\interactive\`), Amber (`#f59e0b`) left sidebar rail, `Ctrl+K` Omni-Search, `?` shortcuts modal, `🩺 System Doctor`, Mother integration manifest (`mother.manifest.json`) + `/api/scheduler/*` disk arbitration, table multi-column sorting & CSV/JSON exports, decoupled sister-app buttons, ANSI-clean `rclone bisync` stale lock recovery, exclusive `FileShare.None` pre-probe in `PendingSwapService`, complete removal of `.hash` sidecar manipulation from game launchers, and universal media manifest cataloging with Two-Step Archive Bit Rot verification and ShadowLibrary parity. | [`v1.0.0-rc.11.2026-11-02.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/plans/v1.0.0-rc.11.2026-11-02.md), [`2026-11-02_v1.0.0-rc.11_plan_game-launcher-cleanup-and-media-manifest-bitrot.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/plans/2026-11-02_v1.0.0-rc.11_plan_game-launcher-cleanup-and-media-manifest-bitrot.md) |
| **`v1.0.0-rc.10-nightly.1`** | `2026-11-02` | Root Documentation Quartet (`README`, `FEATURES`, `ROADMAP`, `CHANGELOG`), 6-file `Releases\` bundles, Standard 15 `[vVer] [PID/T]` `10 MB × 5` log rotation + 30-day run pruning, ANSI-clean `rclone bisync` `.lck` recovery, and exclusive-lock pre-probe in `PendingSwapService.RetrySwap`. | [`v1.0.0-rc.10.2026-11-02_0635.-.audit-log-and-code-sweep.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/reports/v1.0.0-rc.10.2026-11-02_0635.-.audit-log-and-code-sweep.md) |
| **`v1.0.0-rc.10`** | `2026-11-02` | Standard 15 zero-PII JSON crash bundle dumper (`CrashBundleService`), unified `PendingSwapService` recovery/overwrite shield across Modules 1–3, monotonic UTC per-module scheduling (`LastScheduledRunUtc`), and upgrade-safe installer pre-upgrade backups. | [`v1.0.0-rc.10.2026-11-02.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/plans/archive/v1.0.0-rc.10.2026-11-02.md) |
| **`v1.0.0-nightly.20261101`** | `2026-11-01` | Default `rclone bisync --max-lock 2h` protection, stale `.lck` single-retry auto-recovery, and `--test-mode` `Sandbox\app` container preservation on uninstall. | [`CHANGELOG.md`](file:///D:/Projects/Personal/Housekeeping/CHANGELOG.md) |
| **`v1.0.0-rc.9`** | `2026-11-01` | Distinct FIFO `"Up Next"` pending queue engine (`PendingRuns`), manual `⏭ Queue Next` dispatch, DST fall-back UTC scheduler fix, and 4-pillar Authenticode-signed single-EXE Windows installer (`Housekeeping.Installer`). | [`CHANGELOG.md`](file:///D:/Projects/Personal/Housekeeping/CHANGELOG.md) |
| **`v1.0.0-rc.6` – `v1.0.0-rc.8`** | `2026-10-31` – `2026-11-01` | Canonical 6-folder workspace migration, strict `--data-dir`/`--config`/`--log-dir`/`--test-mode`/`--dry-run` sandbox containment, node-scoped SignalR groups, brute-force login rate limiting, and zero bare `catch` blocks. | [`CHANGELOG.md`](file:///D:/Projects/Personal/Housekeeping/CHANGELOG.md) |
| **`v1.0.0-rc.1` – `v1.0.0-rc.5`** | `2026-10-28` – `2026-10-31` | Decoupled .NET 8 Kestrel + SignalR + React 19 PWA architecture, disposable zero-idle-RAM WebView2 host, Tailscale Serve HTTPS mobile PWA provisioner, and mid-shutdown checkpoint auto-resume. | [`CHANGELOG.md`](file:///D:/Projects/Personal/Housekeeping/CHANGELOG.md) |

---

## 5. Icebox & Cutting-Room Floor

*Unscheduled ideas, stretch goals, and parked concepts preserved for future evaluation (enforcing anti-creep scope discipline):*
- **Direct Point-to-Point Sister-App UI Links (Rejected / Parked):** Direct buttons/embeds between Housekeeping, Greenroom, Redroom, and ShadowLibrary are prohibited to prevent brittle point-to-point coupling; all cross-app orchestration routes through `Mother` or shared filesystem/manifest contracts (`collection_manifest.csv`).
- **Custom Hardware GPU Power-Limit Profiles per Transcode Tier:** Parked for post-`1.0.0` evaluation so `v1.0.0` stays strictly focused on reliability, navigation ergonomics, and `Mother` hooks.
- **Built-in Web-Based DAT Diff Viewer:** Parked for future evaluation; current Redump/No-Intro DAT ingestion and Compliance Reports Center already cover all active verification and self-healing workflows.
