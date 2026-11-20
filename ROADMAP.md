# Housekeeping — Roadmap & Milestones

> **Active Packaged Version:** `v0.1.12-hf.1` (`2026-11-15`, [`active.state`](active.state))  
> **Active In-Dev Target:** `v0.2.0` (Public Beta, Mother Suite Integration & Stage 2 Sandbox E2E toward `v1.0.0` GA)

---

## 1. North Star Goal

Deliver a 100% unattended, network-resilient, zero-data-loss media workstation, archive compliance, cloud sync, and cryptographic media collection automation engine across local and NAS storage pools—accessible inside the unified Mother suite shell, as a standalone WebView2 desktop window, or as a scoped mobile Tailscale PWA, with strict path containment, signed clean-room installers, and zero-double-work sister-app coexistence.

---

## 2. Active In-Dev Milestone (`v0.2.0` Public Beta -> `v1.0.0` GA)

- **Target Version:** `v0.2.0` (`Public Beta, Mother Suite Integration & Stage 2 Sandbox E2E`)
- **Baseline Packaged Build:** `v0.1.12-hf.1` (see [CHANGELOG.md](CHANGELOG.md))
- **Upstream Prerequisites:** `Foundation v0.5.3` (`Foundation.Installer`, `Foundation.Launcher`, `Foundation.Core`, `Foundation.Automation`) and `Mother v0.3.0` (pairing cleanly with `Mother v0.4.0` dynamic onboarding)
- **Status:** Scoped & Gate 0 Verified (`v0.2.0` implementation plan refresh pending prior to execution)

### `v0.2.0` Target Deliverables & Verification Criteria

- [ ] **Plan Refresh (`0.2.0`):** Supersede the `2026-11-14` draft plan in `Dev/docs/plans/` to align with `Foundation v0.5.3`, the 7-file transition release bundle, `Mother` subpath routing, and Stage 2 4-Drive Sandbox E2E before coding begins.
- [ ] **Suite Installer & Root Launcher (`Foundation.Installer v0.5.3` + `RootLauncherHost`):** Replace the custom console/WinForms installer (`1,518` lines) and launcher with declarative `installer.manifest.json` (`schemaVersion: 2`), `StandardInstallerHost`, and `RootLauncherHost` (`RootLauncherOptions`), installing into `%LOCALAPPDATA%\Programs\StreamItAll\Mother\Housekeeping\` with `--suite-dir`, `--profile lightweight|full|custom`, and opt-in `--migrate-legacy` (cleaning up legacy `Housekeeping_Watchdog` tasks). Package a transition 7-file bundle (`Setup` EXE + `Portable.zip` + sanitized docs + `SHA256SUMS.txt`) via `build_release_universal.py --allow-portable-zip` with zero Google Drive release uploads.
- [ ] **`Mother` Suite Integration & Scoped Subpath PWA:** Normalize `Dev/suite.manifest.json` to strict `camelCase` (`schemaVersion: 2`, `warm`/`cold`/`disabled` residency, `Global\Mother_Housekeeping_Mutex`, `--managed-by-suite`, relative `/api/v1/*` endpoints, `schedules[]`, `targetDrives[]`); force `127.0.0.1` loopback binding and suppress duplicate tray icons under `--managed-by-suite`; configure Vite `base: './'`, `X-Forwarded-Prefix` / `UsePathBase`, prefix-aware API/SignalR clients, and `/app/housekeeping/manifest.webmanifest` + offline SVG QR pairing.
- [ ] **Security, Crash & Isolation Remediation (`0938` + `2115`):**
  - Atomically migrate `.hk_cookie_secret.key` out of `logs\` into `data\` and remove the `logs\` copy (`TieredAuthService`).
  - Suppress benign aborted-client `SocketException (10054)` unobserved task crashes (`CrashBundleService`) and serialize remote `settings.json` pushes via single-flight locking + unique `.tmp` filenames (`RemoteNodeDeploymentService`, split `<= 400` SLOC).
  - Contain `rclone` (`RCLONE_CONFIG`, `--workdir`, `--cache-dir`) under `<dataDir>\rclone\` across all 3 callers (`CloudSyncModule`, `RomLauncherGeneratorModule`, `DependencyCheckService`) so isolated runs never touch host `%LOCALAPPDATA%\rclone\`.
  - Untrack `MediaInfo.dll` from Git and replace silent `CheckFileIsHevc() == true` error masking with `MediaInfo.Native` NuGet or 5-tier `ToolResolver` + `NativeLibrary.SetDllImportResolver` and an explicit pre-run dependency check.
- [ ] **Default-On Stage 2 4-Drive Sandbox E2E & CLI Parity:**
  - Add `--doctor` self-check CLI flag (`0`/`2`) and Debug/test-only `--automation-port` (`Foundation.Automation` on `127.0.0.1`, stripped with `ExitCode = 2` in Release).
  - Keep `APP_ISOLATED_TEST=1` strictly for ephemeral unit tests while wiring `FOUNDATION_SANDBOX_ROOT` + `SANDBOX_*_DRIVE` (`ExitCode = 3` host-escape guard) for Two-Build Stage 2 E2E across all 8 modules with live video streaming and inspected desktop/mobile screenshots.
  - Add local HTTP (`127.0.0.1`) positive, `404`, Zip-Slip, and tampered-hash installer verification tests.
- [ ] **Public GitHub Repository Polish & Clean-Room Scrub:**
  - Untrack compiled `Dev/Housekeeping.Server/wwwroot/assets/*` (built at package/CI time), update `Directory.Build.props` (`PathMap` -> `.`, `DebugType` -> `none` in Release), clean `.gitignore`, tokenize `AGENTS.md` (relative links + placeholder IDs + local `Dev/data/notion_hub.local.json`), ship `settings.example.json`, and scrub hardcoded workstation paths, test webhooks, and legacy ShellLink CLSIDs.
  - Add `Dev/Housekeeping.code-workspace`, `SECURITY.md`, `CONTRIBUTING.md`, `.github/` issue/PR/Code-of-Conduct templates, `ci.yml`, conditional SignPath `release.yml`, visibility-gated `pages.yml` static docs site, `README.md` `## License & Legal` (`MPL-2.0` + `7-Zip`/`MediaInfo`/`HandBrake`/`rclone` attributions) + `## Code Signing Policy`, and `app\LICENSE`.
  - Complete the human-gated `git-filter-repo` history scrub checklist and pass public repo polish checks (`--prerelease` Beta tag upon explicit human push approval).

---

## 3. Upcoming Horizons: Granular Minor Releases & Major Epochs

```text
v0.1.12-hf.1 (Baseline)
  └──> v0.2.0: Public Beta, Mother Integration & Stage 2 Sandbox E2E (Active Target)
         └──> v0.3.0: Foundation SDK Convergence, Granular Config & Quarantine
                └──> v0.4.0: .NET 10 LTS Upgrade & Full 250/400/600 SLOC Sweep
                       └──> v1.0.0 GA: Production Soak & Authorized Cutover
                              └──> v1.1.0: Profile Catalog & Worker Agent (PROF-01, DIST-01)
                                     └──> v1.2.0: Chunked HTTP WAN Transfer Engine (XFER-01)
                                            └──> v1.3.0: Scheduled Pool Scrubber (SCRUB-01)
                                                   └──> v1.4.0: Cluster Analytics, Failover & MCP/Drops (ANLYT-01, MCP-01)
                                                          └──> v2.0.0 SUMMIT: Autonomous Distributed Media & Transcode Cluster
```

*(Architectural Boundary: Housekeeping remains strictly a media workstation, transcoding, archive compliance, and storage automation engine. It does not duplicate emulation frontends or collection preservation vaults.)*

---

### Horizon Toward `v1.0.0` GA (Pre-1.0 Increments)

#### `v0.3.0` — `Foundation` SDK Convergence, Granular `data\config\` & Quarantine
- **Cornerstone Theme:** Replace duplicated custom core/UI infrastructure with pinned `Foundation` SDK packages, adopt granular `data\config\settings*.json` + DPAPI secrets, add `data\quarantine\` + SQLite WAL, and cut over to the 8-file Dual-Installer.
- **Target Deliverables:**
  - [ ] **Schema Gate First:** Register `quarantine.manifest.json` keys (`originalFilePath`, `quarantinedFileName`, `reason`) and `v5` `camelCase` settings keys in the central data dictionary prior to planning.
  - [ ] **`Foundation` SDK Convergence (`Foundation.Sidecars`, `Foundation.Server`, `@foundation/ui`):** Replace custom `HashSidecarService` (`2,727` lines) with `Foundation.Sidecars`, adopt `Foundation.Server.TieredAuthEngine` + `SisterAppStandardEndpoints`, and migrate terminal, data tables, coexistence banner, sidebar, and path pickers to `@foundation/ui` (preserving zero orphan CSS and 2-line tooltips).
  - [ ] **Granular `data\config\settings*.json` & DPAPI Secrets:** Migrate `PascalCase` `v4` `data\settings.json` to `camelCase` `v5` `data\config\settings.json` (plus cohesive `>= 5`-key domain splits where appropriate, `[JsonExtensionData]` forward compatibility, `20`-snapshot `data\config_history\`, and `.bak` recovery without resetting on corrupt JSON). Migrate all 6 plaintext secrets and the cookie signing key into `data\secrets.dpapi` (`DpapiSecretStore`, failing loudly with actionable errors if decrypted under another user/host and excluded from SMB fleet sync).
  - [ ] **Soft-Delete Quarantine & SQLite WAL Storage:** Introduce `data\quarantine\quarantine.manifest.json` with quota pruning and UI restore, backed by a SQLite WAL database (`data\housekeeping.db`, `VACUUM INTO` hot backups).
  - [ ] **8-File Dual-Installer Cutover:** Adopt `Foundation.Installer` Online Setup + Offline Setup + `ProductName` install ownership checks (`IsOwnInstallManifest`) once upstream `Foundation` lands, verified via `127.0.0.1` HTTP `404`/tampered-hash negative tests.

#### `v0.4.0` — `.NET 10 LTS` Runtime Modernization & Full `250/400/600` SLOC Sweep
- **Cornerstone Theme:** Modernize to `.NET 10 LTS` (coordinated with `Foundation` multi-targeting) and bring 100% of production source files within `250/400/600` code-only SLOC limits.
- **Target Deliverables:**
  - [ ] **`.NET 10 LTS` Target Upgrade:** Migrate all solution `.csproj` files, installer runtime checks, and GitHub Actions workflows from `net8.0-windows` to `net10.0-windows`.
  - [ ] **Full `250/400/600` Code-Only SLOC Decomposition:** Split remaining monolithic files (`PipelineOrchestratorService.cs`, `AppConfig.cs`, `ArchiveComplianceService.cs`, `HousekeepingApiEndpoints.cs`, `RomManifestCsvService.cs`, `ConfigService.cs`, `SchedulerAndNotificationService.cs`, `CollectionManifestModule.cs`, `RomLauncherGeneratorModule.cs`, `SettingsPage.tsx`, `theme.css`, `RomLauncherGeneratorEditor.tsx`) into cohesive vertical slices (`<= 400` SLOC soft / `<= 600` hard) and extract Module 7 `CustomScripts` into its own dedicated module class.
  - [ ] **Automated CI Hygiene Gate:** Enforce automated code-only SLOC checks, `0` warnings under `-p:TreatWarningsAsErrors=true`, `pnpm` + `tsc --noEmit`, and Stage 2 `.NET 10` sandbox verification.

#### `v1.0.0` GA Summit — Production Certification & Authorized Cutover
- **Cornerstone Theme:** Certify multi-day unattended stability alongside `Mother` and promote to General Availability.
- **Target Deliverables:**
  - [ ] **24-Hour Production-Mirror Soak:** Complete a 24-hour isolated 4-Drive Sandbox soak alongside `Mother` with `0` unobserved crash bundles, `0` handle/memory leaks, verified `10 MB x 5` log rotation, and `0` host `.lck` or secret leaks.
  - [ ] **Authorized Production Cutover & GA Release:** After explicit human approval, archive a pre-cutover snapshot, execute opt-in `--migrate-legacy` cutover into `%LOCALAPPDATA%\Programs\StreamItAll\Mother\Housekeeping\`, and publish the non-prerelease `v1.0.0` GA GitHub Release and Discord update.

---

### Horizon Toward `v2.0.0` Summit (Post-1.0 Distributed Cluster)

#### `v1.1.0` — Deterministic Profile Catalog & Worker Agent Daemon (`PROF-01`, `DIST-01`)
- **Cornerstone Theme:** Eliminate transcode quality/size drift via an operator-defined profile catalog and launch the lightweight `hk-worker` remote daemon.
- **Target Deliverables:**
  - [ ] **Schema Gate & `PROF-01` Profile Catalog:** Register profile/worker schema keys; define canonical transcode profiles in `data\config\profiles.json` (HandBrake CLI args, bitrate caps, audio passthrough) with Web UI editor and hardware capability matching (NVENC, QSV, AMF, CPU).
  - [ ] **`DIST-01` Worker Daemon (`hk-worker`):** Lightweight `.NET 10` background worker CLI using `JobObjectGuard` child containment, `X-Housekeeping-Worker-Token` auth, and 15-second heartbeats over LAN or Tailscale.

#### `v1.2.0` — Direct Chunked HTTP WAN Transfer Engine (`XFER-01`)
- **Cornerstone Theme:** Stream source and encoded media directly over authenticated, resumable HTTPS without SMB/NFS dependencies.
- **Target Deliverables:**
  - [ ] **Schema Gate & `XFER-01` Streaming Engine:** Build on `Foundation.Core.SafeNetworkTransfer` (`PUT/GET /api/v1/jobs/{id}/payload`), supporting HTTP `Range` resume on `.partial_*` files, `10s` connect / `30s` stall timeouts, jittered backoff, token-bucket throttling, `1.1x + 512 MB` pre-flight disk checks, per-job upload quotas + TTL orphan cleanup, and streaming SHA-256 verification.
  - [ ] **Verification Criteria:** Isolated loopback transfer + forced mid-transfer TCP drop/resume tests in the 4-Drive Sandbox.

#### `v1.3.0` — Automated Scheduled Storage Pool Scrubbing & I/O Throttling (`SCRUB-01`)
- **Cornerstone Theme:** Unattended cryptographic bit-rot assurance across cold archives and media pools with zero impact on live playback or captures.
- **Target Deliverables:**
  - [ ] **Schema Gate & `SCRUB-01` Pool Scrubber:** Background scrubber built on `Foundation.Sidecars` comparing pools against 9-column manifests, `.hash` sidecars, and `.archive_hashes.json`, honoring `.hklock` / `Mother` drive deferral and low-priority I/O throttling, and emitting structured audit reports to `data\scrub_reports\`.

#### `v1.4.0` — Cluster Analytics, Failover Re-Queueing & Agent Integration (`ANLYT-01`, `MCP-01`)
- **Cornerstone Theme:** Real-time fleet visibility, self-healing node failover, historical storage ROI trends, and safe local AI agent seams.
- **Target Deliverables:**
  - [ ] **Schema Gate & `ANLYT-01` Cluster Monitor:** Real-time topology map in Web UI (wired into `Mother` fleet telemetry), automated job re-queueing on worker heartbeat loss using **fencing epoch/lease tokens** (blocking stale commits from reconnecting workers), and historical storage ROI compression charts.
  - [ ] **`MCP-01` Dual-Transport FastMCP & `data\drops\` Inbox:** Expose read/inspect/safe-enqueue MCP tools (`stdio` + `/mcp`) and atomic `data\drops\inbox\` -> `processed\` ingestion, strictly blocking arbitrary `CustomScripts` execution or host path escapes (`ExitCode = 3` / quarantine) from remote drops or MCP calls.

#### `v2.0.0` GA Summit — Autonomous Distributed Media & Transcode Cluster
- **Cornerstone Theme:** Full multi-node LAN and Tailscale cluster orchestration uniting deterministic profile dispatch, resumable WAN transfers, background pool scrubbing, self-healing failover, and safe agent automation.
- **Target Deliverables:**
  - [ ] Complete end-to-end multi-node heterogeneous cluster soak test over Tailscale (coordinator + remote `hk-worker` nodes across 4-Drive Sandbox scenarios).
  - [ ] Package and publish official `v2.0.0` GA installer and release bundle.

---

## 4. Completed Milestones

| Version | Date | Headline Achievement | Reference |
| :--- | :---: | :--- | :--- |
| **`v0.1.12-hf.1`** | `2026-11-15` | Retired legacy external test harness, standardized `--data-dir`/`--config`/`--log-dir`/`--port`/`--tray-only` (`APP_ISOLATED_TEST=1`) and Tier A 4-folder layout, passing `183/183` Release xUnit tests. | [CHANGELOG.md](CHANGELOG.md#0112-hf1---2026-11-15) |
| **`v0.1.12`** | `2026-11-11` | 5-pillar audit remediation (`BUG-01..06`), dual-mode Stop Job vs Stop Queue (`CORE-07`), 4-pillar Sister-App Coexistence Bridge (`FLEET-01..02`, `.coexistence.json` v2, `.hklock`), 4-mode `.hash` + `.archive_hashes.json`, Module 4 decoupling, resizable terminal (`UI-05`), drive strip + mobile bottom nav + 2-line hints (`UI-06`), and `183/183` tests. | [CHANGELOG.md](CHANGELOG.md#0112---2026-11-11) |
| **`v0.1.11`** (`1.0.0-rc.11-hf.2`) | `2026-11-02` | Universal SemVer `0.1.x` transition, Clean Release Root `data\` containment (`MigrateLegacyRootStateIfPresent`), `app\` metadata containment, and default Standalone WebView2 window (`Schema v4`). | [CHANGELOG.md](CHANGELOG.md#0111-universal-semver-transition-from-100-rc11-hf2---2026-11-02) |
| **`v1.0.0-rc.11` / `hf.1`** | `2026-11-02` | 12-Pillar Parity Sweep (WinForms single-surface installer, Amber sidebar rail, `Ctrl+K`, `?`, `🩺 System Doctor`, `mother.manifest.json`, `/api/scheduler/*` deferral), Universal Media Manifest Bit Rot Auditor, and graceful HTTP shutdown. | [CHANGELOG.md](CHANGELOG.md#100-rc11---2026-11-02) |
| **`v1.0.0-rc.1` – `rc.10`** | `2026-10-28` – `2026-11-02` | Decoupled Kestrel + SignalR + React 19 PWA architecture, Tailscale Serve HTTPS, FIFO `PendingRuns` queue, monotonic UTC scheduler, `PendingSwapService`, and crash bundles. | [CHANGELOG.md](CHANGELOG.md) |

---

## 5. Icebox & Cutting-Room Floor

*Unscheduled ideas and explicit non-goals preserved to prevent scope creep:*
- **Direct Point-to-Point Sister-App UI Embeds (Rejected):** Direct UI links or tightly coupled widgets between peer apps remain out of scope; cross-app orchestration flows through `Mother` or shared filesystem/manifest contracts (`.coexistence.json`, `.hklock`, `collection_manifest.csv`).
- **Built-In Web-Based DAT Diff Viewer (Parked):** Existing Redump/No-Intro DAT ingestion and Compliance Reports Center already cover active verification workflows.
- **Custom Hardware GPU Power-Limit Profiles per Transcode Tier (Parked):** Deferred beyond `v2.0.0` so pre-1.0 stabilization and distributed cluster delivery remain focused.
- **Non-Windows Daemon Hosts for Core Coordinator (Parked):** Remote `hk-worker` agents may expand post-`v1.1.0`, while the primary Windows coordinator remains focused on Windows desktop and `Mother` suite integration.
