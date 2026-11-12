# Housekeeping — Roadmap & Milestones

> **Active Packaged Version:** `v0.1.12` (`2026-11-11`, [`active.state`](file:///D:/Projects/Personal/Housekeeping/active.state))  
> **Active In-Dev Target:** `v0.1.13` (Default `PATCH+1` toward `v1.0.0` GA Summit)  
> **Maintained By:** `@tessa` & `@pland` (per [Dev Bible Standard 14](file:///D:/Projects/Personal/_DevBible/14_IMPLEMENTATION_PLANS_AND_BLUEPRINTS.md) & [Standard 16](file:///D:/Projects/Personal/_DevBible/16_PROJECT_DOSSIERS_AND_FEATURE_TRACKING.md))

---

## 1. North Star Goal

Provide a 100% unattended, network-resilient, zero-data-loss media workstation, archive compliance, cloud sync, and cryptographic ROM collection automation engine across local and NAS storage pools—accessible from desktop tray, disposable zero-idle-RAM WebView2, or mobile Tailscale PWA, with strict fail-closed sandbox containment, 4-pillar signed single-EXE installers, and clean `Mother` + Sister-App coexistence hooks.

---

## 2. Active In-Dev Milestone (`v1.0.0` GA Summit)

- **Target Version:** `v1.0.0` (Culmination of the `0.1.x` Pre-1.0 Stabilization Track)
- **Baseline Packaged Build:** `v0.1.12` ([`Housekeeping-Setup-v0.1.12-win-x64.exe`](file:///D:/Projects/Personal/Housekeeping/Installers/Housekeeping-Setup-v0.1.12-win-x64.exe))
- **Latest Completed Plan (`v0.1.12`):** [`Dev/docs/plans/v0.1.12.2026-11-11.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/plans/v0.1.12.2026-11-11.md) (`Completed` — `16/16` targets `T1`–`T16` delivered, `183/183` xUnit tests passing across 11 suites)
- **Status:** `v0.1.12` Shipped & Verified — Advancing `v0.1.13` (`PATCH+1`) Toward `v1.0.0` GA Summit
- **Milestone Deliverables & Hard Set Goals:**
  - [x] **12-Pillar Sister-App Parity Sweep (`v1.0.0-rc.11`):** Delivered WinForms single-surface GUI installer (`650×434`/`650×544`), Clean Release Root (`app\`, `scripts\`, `scripts\interactive\`), Amber (`#f59e0b`) left sidebar rail, `Ctrl+K` Omni-Search, `?` shortcuts modal, `🩺 System Doctor`, Mother disk-arbitration hooks (`/api/scheduler/*`), `--managed-by-mother` tray suppression, table multi-column sorting & CSV/JSON exports, and ANSI-clean `rclone bisync` stale lock recovery.
  - [x] **Clean Release Root `data\` Containment & Native Standalone Window Default (`v0.1.11`):** Strictly contained all user state and databases in `<InstallRoot>\data\` with legacy root migration (`MigrateLegacyRootStateIfPresent`), contained uninstaller/receipts in `<InstallRoot>\app\`, and upgraded `WebServerConfig.DefaultDesktopLaunchMode` to `StandaloneWindow` by default (`Schema v4`).
  - [x] **Dual-Mode Stop Controls & Graceful Shutdown Checkpoint (`CORE-07`, `v0.1.12`):** Separated `Stop Current Job` (`/api/v1/pipeline/stop-job`) from `Stop Entire Queue` (`/api/v1/pipeline/stop-queue`) across Web, SignalR, and System Tray, plus `InitiateGracefulShutdownAbort()` preserving `interrupted_run_checkpoint.json` and `PendingRuns` across upgrades and reboots.
  - [x] **Auto-Scroll Saturation Fix, Word-Wrap Geometry & Resizable Docked Terminal (`UI-05`, `v0.1.12`):** Fixed terminal auto-scroll at the 2,500-line ring cap via `lastEntryKey` + `ResizeObserver` + `45px` latch, added drag-resizable terminal height (`S`/`M`/`L` presets persisted in `AppConfig.UiLayout`), and wired `Ctrl+B` sidebar collapse.
  - [x] **Storage Strip, Mobile Bottom Nav & Form Ergonomics Consolidation (`UI-06`, `v0.1.12`):** Consolidated 10+ local/NAS drives into the sidebar footer (`DrivesTableModal.tsx`), added docked `<MobileBottomNavBar />` (`<= 820px`), single-line Job Builder pills, `<select>` `ProfileSelect` dropdowns with `⚠ Missing` stale-value guards, and 100% Standard 09 2-line hover hints.
  - [x] **Cross-App Zero-Double-Work Contract & 4-Pillar Coexistence Bridge (`FLEET-01` & `FLEET-02`, `v0.1.12`):** Complete Module 4 DAT/sidecar decoupling, 4-mode `HashSidecarMode` (`0..3`), post-injection `.archive_hashes.json`, `SisterAppCoexistenceService` (`.coexistence.json` `v2`, `/api/v1/coexistence/*`), `CoexistenceWarningBanner`, Redroom `.redroom-part`/`{edition-*}`/`.srt` guards, and Standard 19 `pid`+`processStartTimeUtc` `.hklock` coordination.
  - [x] **5-Pillar `/audit` Remediation & Extract-on-Touch Modularity (`v0.1.12`):** Safe exclusive `FileShare.None` SMB pre-probes (`BUG-01`), crash telemetry filtering (`BUG-03`), per-caller `{pid}.{guid}.tmp` atomic writes & Clean-Release-Root SMB fleet sync (`BUG-04`), `ActiveLogsDir` System Doctor & sibling `backups\` state archive (`BUG-05`), one-shot `--status` & `--repair-config` CLI + `.tsv`/`.csv` exports (`BUG-06`), 28-file Extract-on-Touch splits (`0` `ERR-001`), and `@inspecter` 100% Sandbox clean sweep (`183/183` passing tests).
  - [ ] **Foundation Shared SDK Cutover (`0.1.x` $\rightarrow$ `v1.0.0`):** Sync published `Foundation` libraries (`Foundation.Sidecars`, `@foundation/ui`) via `sync_foundation_sdk.py --sync-consumer` once upstream SDK contracts lock.
  - [ ] **24-Hour Production Soak Verification:** Confirm zero memory leaks, zero unhandled crash dumps, and clean `Housekeeping_Live.log` rotation under multi-machine scheduled workloads.
  - [ ] **Stream GA Packaging Approval:** Obtain explicit human approval from Stream to bump `0.1.x` $\rightarrow$ `1.0.0`, package official `v1.0.0` GA installer and 6-file `Releases\` bundle, deploy to canonical `D:\Programs\Housekeeping\`, and advance active development to `v1.1.0`.

---

## 3. Upcoming Horizons: Granular Minor Releases & Major Epochs

### Major Epoch 1.0: The Definitive Local Media Workstation (Current Release Gate)
*Target: Complete feature lock, zero open P0–P2 reports, and signed 4-pillar GA release installer.*

#### `v1.0.0` — General Availability (GA Release Gate)
- **Milestone Thesis:** Production hardening, UX ergonomic consolidation, zero-double-work sister-app coexistence, and official GA distribution.
- **Target Deliverables:**
  - [x] Complete `v0.1.12` regression pass across all modules in `Sandbox\` (`183/183` xUnit tests + dual-viewport CDP verification).
  - [x] Implement `CORE-07` Dual-Mode Stop Controls (`Stop Current Job` vs `Stop Entire Queue`) & graceful shutdown checkpoint preservation.
  - [x] Fix `UI-05` Terminal Auto-Scroll Saturation, word-wrap geometry (`ResizeObserver`), and resizable docked terminal (`S`/`M`/`L` + `Ctrl+B`).
  - [x] Deliver `UI-06` Storage strip consolidation (10+ local and NAS disks), docked `<MobileBottomNavBar />`, and strict `<select>` dropdown enums.
  - [x] Finalize `FLEET-01..02` Cross-App Coexistence Contract with ShadowLibrary, Redroom, uStation, StreamEngine, Greenroom, and Mother.
  - [ ] Complete `Foundation` shared SDK consumer sync where applicable (`Foundation.Sidecars`, `@foundation/ui`).
  - [ ] Complete 24-hour production soak with 0 memory leaks and clean log rotation.
  - [ ] Obtain explicit human approval to cut official `v1.0.0` GA package and synchronize `D:\Projects\Installers\Housekeeping-Setup-v1.0.0-win-x64.exe`.

---

### Roadmap Horizon: Building Toward Major Epoch 2.0

> **Strategic Mental Model:** Roadmaps build **toward** the next Major Release as the destination summit (`0.1.x -> 1.0.0 -> 1.1.0 -> 1.2.0 -> 1.3.0 -> 1.4.0 -> 2.0.0`), rather than starting at it. Each minor release delivers a concrete, production-ready stepping stone.
>
> **Architectural Boundary:** Housekeeping remains strictly a **media workstation, transcoding, archive compliance, and storage automation engine**. It does NOT attempt to be an emulation station (`uStation`) or full collection preservation vault (`ShadowLibrary`).

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│ v0.1.x -> v1.0.0 GA: The Definitive Local Media Workstation (Current Gate)      │
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
│ (Authenticated HTTPS Streaming, HTTP Range Resume, WAN/Tailscale Safe Staging)  │
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
│ v1.4.0: Cluster Analytics, Fleet Monitor & Node Failover Re-Queueing            │
│ (Real-Time Node Topology, Dropped Worker Re-Queueing, Historical ROI Trends)    │
└───────────────────────────────────────┬─────────────────────────────────────────┘
                                        │
                                        ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│ 🌟 v2.0.0 SUMMIT: Full Autonomous Distributed Media & Transcode Cluster         │
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

#### `v1.2.0` — Direct Chunked HTTP WAN Transfer Engine
- **Milestone Thesis:** Break free from local SMB/NFS path dependencies, allowing worker nodes anywhere on Tailscale or WAN to encode files via authenticated, resumable HTTPS streaming.
- **Target Deliverables:**
  - [ ] **`XFER-01` Chunked Payload Streaming (`PUT /api/v1/jobs/{id}/payload`):** Stream source media directly to worker scratch storage.
  - [ ] **Resumable HTTP Range Support:** Resume interrupted payload uploads or completed encode downloads via HTTP `Range` on `.partial_*` files.
  - [ ] **Standard 19 §5 6-Pillar Safety:** Connect (10s) and stall (30s) timeouts, exponential backoff with full jitter, token-bucket bandwidth throttle, and streaming SHA-256 validation.

#### `v1.3.0` — Automated Storage Pool Scrubbing & Low-Priority I/O Throttling
- **Milestone Thesis:** Unattended, background cryptographic integrity assurance across cold disc archives, ROM libraries, and video pools.
- **Target Deliverables:**
  - [ ] **`SCRUB-01` Storage Pool Scrubber Engine:** Background task comparing cold files against 9-column manifests and `.hash` sidecars.
  - [ ] **Low-Priority I/O Throttling:** Throttle disk reads to guarantee zero dropped frames during local media streaming or live broadcast recording.
  - [ ] **Scrub Reporting & Bit Rot Alerts:** Write non-destructive audit reports to `data\scrub_reports\` and emit prominent terminal/disk alerts.

#### `v1.4.0` — Cluster Analytics, Fleet Monitor & Node Failover Re-Queueing
- **Milestone Thesis:** Real-time visibility into distributed transcode operations, cluster health KPIs, and automated self-healing against node crashes.
- **Target Deliverables:**
  - [ ] **`ANLYT-01` Cluster Dashboard:** Real-time topology map in Web PWA displaying node CPU/GPU loads, active encodes, and transcode FPS.
  - [ ] **Automated Node Failover Re-Queueing:** If a remote worker drops heartbeat mid-encode, automatically cancel the remote task and return the job to coordinator queue.
  - [ ] **Historical Storage ROI Trend Rollups:** Interactive charts showing gigabytes reclaimed, compression ratios, and processing throughput over time.

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
| **`v0.1.12`** | `2026-11-11` | Full 5-Pillar `/audit` Remediation, Extract-on-Touch Modularity & Consolidated Fleet Coexistence Suite: Safe exclusive `FileShare.None` SMB pre-probes (`BUG-01`), 3-tier stop controls (`Stop Job` vs `Stop Queue`) + `InitiateGracefulShutdownAbort` checkpoint preservation (`BUG-02` / `CORE-07`), crash filter hardening (`BUG-03`), unique `{pid}.{guid}.tmp` atomic writes & Clean-Release-Root SMB fleet sync (`BUG-04`), `ActiveLogsDir` System Doctor & sibling `backups\` state archive (`BUG-05`), one-shot `--status` & `--repair-config` CLI + `.tsv`/`.csv` exports (`BUG-06`), 4-mode `.hash` placement (`0..3`) + `.archive_hashes.json` + complete Module 4 decoupling (`FLEET-01`), 4-pillar `SisterAppCoexistenceService` (`.coexistence.json` `v2`, Standard 19 `.hklock` sentinels, `CoexistenceWarningBanner` — `FLEET-02`), resizable terminal (`UI-05`), sidebar drive strip + `<MobileBottomNavBar />` + `ProfileSelect` + 2-line hints (`UI-06`), and `183/183` passing xUnit tests. | [`v0.1.12.2026-11-11.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/plans/v0.1.12.2026-11-11.md), [`v0.1.11.2026-11-11_1952`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/reports/archive/v0.1.11.2026-11-11_1952.-.full-audit-optimizations-and-undetected-errors.md) |
| **`v0.1.11`** (`1.0.0-rc.11-hf.2`) | `2026-11-02` | Universal SemVer `0.1.x` Transition & Clean Release Root Containment: Strictly contained all user state in `<InstallRoot>\data\` (`MigrateLegacyRootStateIfPresent`), installer receipts in `<InstallRoot>\app\`, default native Standalone WebView2 Desktop Window (`Schema v4`), and initial outer Git + Manual Shelf baseline. | [`v1.0.0-rc.11-hf.2.2026-11-02.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/plans/archive/v1.0.0-rc.11-hf.2.2026-11-02.md) |
| **`v1.0.0-rc.11-hf.1`** | `2026-11-02` | Hotfix 1: Canonical parent config adoption (`TryAdoptParentCanonicalSettings`), fixed `<GlobalTerminalDrawer />` flex nesting inside `.hk-body-area`, fail-closed installer 3-stage process lifecycle (`CloseMainWindow` -> HTTP shutdown -> tree kill), post-backup legacy root clutter cleanup, automatic daemon relaunch (`--launch`/`--no-launch`), and `POST /api/v1/system/shutdown`. | [`v1.0.0-rc.11-hf.1.2026-11-02.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/plans/archive/v1.0.0-rc.11-hf.1.2026-11-02.md) |
| **`v1.0.0-rc.11`** | `2026-11-02` | 12-Pillar Sister-App Parity & Universal Media Manifest Bit Rot Engine: WinForms single-surface GUI installer (`650×434`/`650×544`), Clean Release Root, Amber (`#f59e0b`) left sidebar rail, `Ctrl+K` Omni-Search, `?` shortcuts modal, `🩺 System Doctor`, `mother.manifest.json` + `/api/scheduler/*` disk arbitration, table multi-column sorting & CSV/JSON exports, ANSI-clean `rclone bisync` stale lock recovery, and Two-Step Archive Bit Rot verification. | [`v1.0.0-rc.11.2026-11-02.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/plans/archive/v1.0.0-rc.11.2026-11-02.md) |
| **`v1.0.0-rc.10-nightly.1`** | `2026-11-02` | Root Documentation Quartet (`README`, `FEATURES`, `ROADMAP`, `CHANGELOG`), 6-file `Releases\` bundles, Standard 15 `[vVer] [PID/T]` `10 MB × 5` log rotation + 30-day run pruning, ANSI-clean `rclone bisync` `.lck` recovery, and exclusive-lock pre-probe in `PendingSwapService.RetrySwap`. | [`v1.0.0-rc.10.2026-11-02_0635`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/reports/archive/v1.0.0-rc.10.2026-11-02_0635.-.audit-log-and-code-sweep.md) |
| **`v1.0.0-rc.10`** | `2026-11-02` | Standard 15 zero-PII JSON crash bundle dumper (`CrashBundleService`), unified `PendingSwapService` recovery/overwrite shield across Modules 1–3, monotonic UTC per-module scheduling (`LastScheduledRunUtc`), and upgrade-safe installer pre-upgrade backups. | [`v1.0.0-rc.10.2026-11-02.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/plans/archive/v1.0.0-rc.10.2026-11-02.md) |
| **`v1.0.0-nightly.20261101`** | `2026-11-01` | Default `rclone bisync --max-lock 2h` protection, stale `.lck` single-retry auto-recovery, and `--test-mode` `Sandbox\app` container preservation on uninstall. | [`CHANGELOG.md`](file:///D:/Projects/Personal/Housekeeping/CHANGELOG.md) |
| **`v1.0.0-rc.9`** | `2026-11-01` | Distinct FIFO `"Up Next"` pending queue engine (`PendingRuns`), manual `⏭ Queue Next` dispatch, DST fall-back UTC scheduler fix, and 4-pillar Authenticode-signed single-EXE Windows installer (`Housekeeping.Installer`). | [`CHANGELOG.md`](file:///D:/Projects/Personal/Housekeeping/CHANGELOG.md) |
| **`v1.0.0-rc.6` – `v1.0.0-rc.8`** | `2026-10-31` – `2026-11-01` | Canonical 6-folder workspace migration, strict `--data-dir`/`--config`/`--log-dir`/`--test-mode`/`--dry-run` sandbox containment, node-scoped SignalR groups, brute-force login rate limiting, and zero bare `catch` blocks. | [`CHANGELOG.md`](file:///D:/Projects/Personal/Housekeeping/CHANGELOG.md) |
| **`v1.0.0-rc.1` – `v1.0.0-rc.5`** | `2026-10-28` – `2026-10-31` | Decoupled .NET 8 Kestrel + SignalR + React 19 PWA architecture, disposable zero-idle-RAM WebView2 host, Tailscale Serve HTTPS mobile PWA provisioner, and mid-shutdown checkpoint auto-resume. | [`CHANGELOG.md`](file:///D:/Projects/Personal/Housekeeping/CHANGELOG.md) |

---

## 5. Icebox & Cutting-Room Floor

*Unscheduled ideas, stretch goals, and parked concepts preserved for future evaluation (enforcing anti-creep scope discipline):*
- **Direct Point-to-Point Sister-App UI Links (Rejected / Parked):** Direct buttons/embeds between Housekeeping, Greenroom, Redroom, and ShadowLibrary are prohibited to prevent brittle point-to-point coupling; all cross-app orchestration routes through `Mother` or shared filesystem/manifest contracts (`.coexistence.json`, `.hklock`, `collection_manifest.csv`).
- **Custom Hardware GPU Power-Limit Profiles per Transcode Tier:** Parked for post-`1.0.0` evaluation so `0.1.x` $\rightarrow$ `v1.0.0` stays strictly focused on reliability, navigation ergonomics, and `Mother` hooks.
- **Built-in Web-Based DAT Diff Viewer:** Parked for future evaluation; current Redump/No-Intro DAT ingestion and Compliance Reports Center already cover all active verification and self-healing workflows.
