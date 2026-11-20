<div align="center">

<img src="Dev/Housekeeping.DesktopHost/Assets/app_icon.png" alt="Housekeeping Logo" width="96" />

# Housekeeping

Automated Windows storage hygiene, multi-tier media transcoding, archive compliance, cryptographic bit-rot auditing, cloud synchronization, and game library automation.

[![Version](https://img.shields.io/badge/version-v0.2.0--beta-f59e0b?style=flat-square)](https://github.com/StreamItAllGit/Housekeeping/releases)
[![License](https://img.shields.io/badge/license-MPL--2.0-10b981?style=flat-square)](LICENSE)
[![Runtime](https://img.shields.io/badge/runtime-.NET%208-512BD4?style=flat-square)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-0ea5e9?style=flat-square)](https://github.com/StreamItAllGit/Housekeeping/releases)
[![CI Build & Tests](https://github.com/StreamItAllGit/Housekeeping/actions/workflows/ci.yml/badge.svg)](https://github.com/StreamItAllGit/Housekeeping/actions/workflows/ci.yml)
[![Release & SignPath](https://github.com/StreamItAllGit/Housekeeping/actions/workflows/release.yml/badge.svg)](https://github.com/StreamItAllGit/Housekeeping/actions/workflows/release.yml)

</div>

> **Beta Status (Pre-1.0 — `v0.2.0`):** Housekeeping `v0.2.0` is a Pre-1.0 public beta release (`active.state`). Core 8-module pipeline automation, `Mother` reverse-proxy subpath embedding (`/app/housekeeping/`), `rclone` and sandbox path containment, and 4-Drive Sandbox E2E verification are active while `Foundation` SDK convergence and granular configuration hardening continue toward `v1.0.0`.

---

## 1. What It Is & Core Capabilities

**Housekeeping** is a persistent Windows system tray daemon and local/Tailnet web workstation built on `.NET 8` (ASP.NET Core Kestrel + SignalR + WinForms System Tray + Disposable WebView2) and a React 19 + TypeScript + Vite 6 Progressive Web App (PWA). It executes unattended pipelines across local drives, NAS/SMB shares, and cloud remotes while coordinating disk access safely with companion applications.

### 8 Automation Modules & Core Engine
- **Unattended Queue & Job Orchestrator:** Schedule independent named queues (`PendingRuns` FIFO queue), chain follow-up jobs, inspect live SignalR logs in a drag-resizable terminal, separate `Stop Current Job` from `Stop Entire Queue`, and preserve `interrupted_run_checkpoint.json` across graceful shutdowns and upgrades.
- **Media Processing (Modules 1–3):** Generate fast editing proxies (`ProxyGenerator`), compress finished captures into space-saving HEVC/AV1 archives (`ArchiveCompressor`), and build `144p`–`4K UHD` streaming tiers with language-edition rules, `.partial` exclusive handle pre-probing (`PendingSwapService`), and dynamic `MediaInfo` stream inspection (`MediaTranscoder`).
- **Decoupled Game Launchers (Module 4):** Build `.m3u` multi-disc playlists, solid `.7z` archives, `.bat`/`.lnk` scratch launchers across 24 runner presets, and optional `rclone` save sync—decoupled from DAT and `.hash` auditing.
- **Cloud Sync, Humble Library & Custom Tasks (Modules 5–7):** Automate `rclone` (`bisync`/`sync`/`copy`) with `<dataDir>\rclone\` config/lock isolation and stale `.lck` recovery (`CloudSync`), download DRM-free Humble Bundle libraries via `hbd` (`HumbleSync`), and run ordered `.bat`/`.ps1`/`.py`/CLI tasks (`CustomScripts`).
- **Universal Collection Manifest & Bit-Rot Auditor (Module 8):** Catalog media, ROMs, and files into delimiter-aware `.csv`/`.tsv`/`.json`/`.html` exports with a live C# Regex Playground, 4-mode `.hash` placement (`0..3`), post-injection `.archive_hashes.json` tracking, two-step outer+inner archive bit-rot checks, and zero-decompression archive compliance templates (`7z l -slt` / `7z d`).
- **Mother Suite & Mobile PWA Integration:** Embed behind the `Mother` gateway at `/app/housekeeping/` (`X-Forwarded-Prefix`, `Service-Worker-Allowed`, scoped `manifest.webmanifest`, `--bind 127.0.0.1`, `Global\Mother_Housekeeping_Mutex`) or pair phones over Tailscale Serve HTTPS with offline SVG QR generation (`/api/v1/system/tailscale-qr`).

---

## 2. Quickstart, Releases & Runtime CLI

### End-User Installation (`Windows x64`)
1. Download **`Housekeeping-Setup-v0.2.0-win-x64.exe`** or **`Housekeeping-v0.2.0-win-x64-Portable.zip`** from [GitHub Releases](https://github.com/StreamItAllGit/Housekeeping/releases) and verify against `SHA256SUMS.txt`.
2. Run the installer (`%LOCALAPPDATA%\Programs\StreamItAll\Mother\Housekeeping\` default target, or customize via `--install-dir`, `--suite-dir`, `--profile lightweight|full|custom`, and opt-in `--migrate-legacy`).
3. Launch `Housekeeping.exe` (root launcher shim delegating to `app\Housekeeping.exe`) to open the disposable WebView2 desktop window or browse to `http://127.0.0.1:5178`.

### Build From Source & Run Isolated Tests
```powershell
git clone https://github.com/StreamItAllGit/Housekeeping.git
cd Housekeeping

# 1. Populate pinned Foundation v0.5.3 SDK assemblies into Dev\lib\ (see CONTRIBUTING.md)
# 2. Install, typecheck, and build the React 19 + TypeScript SPA
npm --prefix Dev\Housekeeping.Web ci
npx --prefix Dev\Housekeeping.Web tsc --noEmit
npm --prefix Dev\Housekeeping.Web run build
node Dev\scripts\verify_ui_hints.mjs

# 3. Build & run isolated xUnit tests (zero warnings required)
$env:APP_ISOLATED_TEST = "1"
dotnet test Dev\Housekeeping.slnx -c Release -p:TreatWarningsAsErrors=true -nodeReuse:false
Remove-Item Env:APP_ISOLATED_TEST

# 4. Stage & package 7-file Releases\v0.2.0\ + Releases\Latest\ bundle
powershell -NoProfile -ExecutionPolicy Bypass -File Dev\scripts\build_installer.ps1 -Version 0.2.0 -SkipSigning
```

### Runtime & Diagnostic CLI Flags
| CLI Flag / Env Var | Default | Purpose |
| :--- | :--- | :--- |
| `--data-dir <dir>` / `HOUSEKEEPING_DATA_DIR` | `<InstallRoot>\data` | Redirects runtime JSON state, `.hk_cookie_secret.key`, `rclone\` containment, and caches to `<dir>`. |
| `--config <file>` / `HOUSEKEEPING_CONFIG` | `<data-dir>\settings.json` | Loads and persists configuration at `<file>` with 20-snapshot rollback history. |
| `--log-dir <dir>` / `HOUSEKEEPING_LOG_DIR` | `<InstallRoot>\logs` | Writes `Housekeeping_Live.log` (`10 MB x 5`), `runs\`, `crashes\`, doctor bundles, and heartbeats. |
| `--port <1-65535>` / `HOUSEKEEPING_PORT` | `5178` | Overrides Kestrel HTTP and SignalR listening port. |
| `--bind <host>` / `--localhost-only` | `127.0.0.1` | Sets Kestrel bind host or forces `127.0.0.1` loopback listening. |
| `--managed-by-mother`, `--managed-by-suite` | `false` | Suppresses standalone tray icon, sets `--suite-id`, and enforces `127.0.0.1` loopback binding. |
| `--suite-id <id>` | `mother` | Identifies the supervising suite coordinator ID. |
| `--tray-only`, `--minimized`, `--silent` | `false` | Starts background daemon in System Tray without opening a desktop window on startup. |
| `--doctor` | `false` | One-shot JSON health/containment/tool-readiness diagnostic check (exits `0` healthy, `2` on issues). |
| `--status` / `--repair-config` | `false` | One-shot synchronous JSON daemon status probe or corrupt-config quarantine/repair (`0` on success). |
| `--dry-run` / `HOUSEKEEPING_DRY_RUN=1` | `false` | Forces all pipeline and job dispatches into non-destructive simulation mode. |
| `APP_ISOLATED_TEST=1` / `FOUNDATION_SANDBOX_ROOT` | `false` | Suppresses host Registry/Startup/power mutations (`FOUNDATION_SANDBOX_ROOT` enforces `ExitCode = 3` containment). |
| `--automation-port` / `--automation-token` | Disabled | Debug / TestAutomation builds only; fails closed with `ExitCode = 2` in Release builds. |
| `--version`, `-v` / `--help`, `-h` | N/A | Prints application version or CLI usage reference and exits cleanly. |

---

## 3. Architecture & Repository Structure

- **4-Folder Project Layout:**
  - `Archive/` — Cold compressed version snapshots (`.7z`).
  - `Builds/` — Local compiled Clean Release Root staging directories (`Builds/v0.2.0/`, containing `Housekeeping.exe`, `app/`, `scripts/`).
  - `Dev/` — Source solutions (`Housekeeping.Core`, `Housekeeping.Server`, `Housekeeping.DesktopHost`, `Housekeeping.Launcher`, `Housekeeping.Installer`, `Housekeeping.Web`, `Housekeeping.Tests`), config templates (`Dev/config/settings.example.json`), VS Code workspace (`Dev/Housekeeping.code-workspace`), and technical manuals (`Dev/docs/`).
  - `Releases/` — 7-file end-user release bundles (`Housekeeping-Setup-v<Ver>-win-x64.exe`, `Housekeeping-v<Ver>-win-x64-Portable.zip`, `README.md`, `FEATURES.md`, `CHANGELOG.md`, `RELEASE_NOTES.md`, `SHA256SUMS.txt`) NTFS-hardlinked in `Releases/Latest/`.
- **Persistence & Containment:** Atomic per-caller `{pid}.{guid}.tmp` copy-on-write JSON persistence (`settings.json` `ConfigVersion = 4`, `history.json`, `compliance_reports.json`, `pending_swaps.json`, `interrupted_run_checkpoint.json`), 20-snapshot rollback history (`backups/config_snapshots/`), cookie signing secret strictly in `<dataDir>\.hk_cookie_secret.key`, and isolated `<dataDir>\rclone\` config/work/cache paths.

---

## 4. Documentation & Community

- **Feature Map & Code Registry:** [FEATURES.md](FEATURES.md)
- **Strategic Roadmap & Milestones:** [ROADMAP.md](ROADMAP.md)
- **Version History Ledger:** [CHANGELOG.md](CHANGELOG.md)
- **Security Policy:** [SECURITY.md](SECURITY.md)
- **Contributing Guide:** [CONTRIBUTING.md](CONTRIBUTING.md)
- **Code of Conduct:** [.github/CODE_OF_CONDUCT.md](.github/CODE_OF_CONDUCT.md)
- **Technical Manuals (`Dev/docs/`):**
  - [User Guide](Dev/docs/USER_GUIDE.md) — Setup wizard, 8 modules, UI views, mobile PWA, Tailscale QR, and troubleshooting FAQ
  - [Architecture Manual](Dev/docs/ARCHITECTURE.md) — System context diagrams, assembly graph, 5 pipelines, file registry, and extension playbooks
  - [Interfaces & API Reference](Dev/docs/INTERFACES.md) — 5-step auth model, REST routes, `/api/v1/system/tailscale-qr`, subpath headers, SignalR events, `.hash` / `.hklock` / `.coexistence.json` schemas
  - [Operations Runbook](Dev/docs/OPERATIONS.md) — Deployment topology, CLI flags (`--doctor`, `--bind`, `--suite-id`, `--migrate-legacy`), backups, Stage 2 sandbox testing, and incident playbooks

---

## Code Signing Policy

Free code signing provided by SignPath.io, certificate by SignPath Foundation.

- **Authors:** Repository contributors and maintainers ([`@StreamItAllGit`](https://github.com/StreamItAllGit))
- **Reviewers:** Repository maintainers ([`@StreamItAllGit`](https://github.com/StreamItAllGit))
- **Approvers:** Repository owners ([`@StreamItAllGit`](https://github.com/StreamItAllGit))
- **Privacy Policy:** This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

See [SignPath.io](https://signpath.io/) and [SignPath Foundation](https://signpath.org/) for program details.

---

## License & Legal

- **License:** Housekeeping is distributed under the **Mozilla Public License Version 2.0 (`MPL-2.0`)**. See [LICENSE](LICENSE) (`app\LICENSE` in packaged builds) for complete terms.
- **Third-Party External Tools (Optional Integrations):**
  - **7-Zip (`7z.exe`)** — GNU LGPL-2.1+ (optional external archive inspection, clutter cleanup, and `.7z` compression)
  - **MediaInfo (`MediaInfo.dll`)** — BSD-2-Clause (optional native video stream/codec library resolved dynamically at runtime; not redistributed in source control)
  - **HandBrake (`HandBrakeCLI.exe`)** — GNU GPL-2.0 (optional external video encoding engine invoked when configured)
  - **rclone (`rclone.exe`)** — MIT License (optional external cloud/remote file sync engine isolated under `<dataDir>\rclone\`)
- **Bring Your Own Media & Non-Affiliation:** Housekeeping includes **zero** copyrighted ROMs, disc images, BIOS firmware, media files, or commercial DAT catalogs; operators supply their own legally acquired files and catalogs. Product, console, emulator, and cloud service names referenced for interoperability are trademarks of their respective owners. Housekeeping is an independent, unaffiliated open-source project.
