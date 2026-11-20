# Housekeeping — Interfaces, REST API, SignalR & File Contracts Manual

- **Document:** `INTERFACES.md` (Technical Manual 4 of 4)
- **Active Build Version:** `v0.2.0` (`Foundation v0.5.3`)
- **HTTP & SignalR Base URL:** `http://127.0.0.1:5178` · Suite Subpath: `http://127.0.0.1:5100/app/housekeeping/` · Tailscale Serve HTTPS: `https://<node>.<tailnet>.ts.net`
- **Primary Source Anchors:** [HousekeepingApiEndpoints.cs](../Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs) · [HousekeepingApiEndpoints.MotherAndQr.cs](../Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.MotherAndQr.cs) · [HousekeepingHub.cs](../Housekeeping.Server/Hubs/HousekeepingHub.cs) · [TieredAuthService.cs](../Housekeeping.Server/Security/TieredAuthService.cs) · [HashSidecarService.cs](../Housekeeping.Core/Services/HashSidecarService.cs)

---

## 1. Authentication & Transport Security Model

All `/api/*` and `/hubs/*` requests pass through [TieredAuthMiddleware](../Housekeeping.Server/Security/TieredAuthMiddleware.cs) and [TieredAuthService](../Housekeeping.Server/Security/TieredAuthService.cs):

| Priority | Auth Method (`AuthMethod`) | Header / Credential Inspected | Caller Scope & Privilege |
| :---: | :--- | :--- | :--- |
| **0 (Public)** | Unauthenticated Public Bypass | `/api/v1/health`, `/api/v1/auth/status`, `/api/v1/auth/login`, `/api/v1/auth/logout`, `/api/v1/auth/setup-password`, `/manifest.webmanifest`, `/sw.js` | Always accessible without credentials so suite health probes, PWA manifests, and login/setup dialogs work. |
| **1** | `NodeApiToken` | `X-Housekeeping-Token: <token>` or `Authorization: Bearer <token>` | Machine-to-machine Fleet Node & automation access; matched via constant-time comparison against `WebServer.NodeApiToken`. |
| **2** | `TailscaleServe` | Raw loopback (`127.0.0.1`/`::1`) + `Tailscale-User-Login: <user@domain>` (when `TrustTailscaleServeHeaders = true`) | Authenticated Tailscale tailnet identity via `tailscale serve`. Subject to `RestrictRawShellToLocalhost`. |
| **3** | `Loopback` | Raw loopback (`127.0.0.1`/`::1`) with **no** `X-Forwarded-For`, `X-Real-IP`, or `Tailscale-User-Login` headers (`IsDirectLoopback = true`) | Full local desktop host / local browser access, including native Win32 dialogs (`StaNativeDialogBridge`) and interactive raw shell. |
| **4** | `SessionCookie` / `SignalRQueryToken` | Cookie `hk_session={issuedUnix}:{identity}:{hmacSha256}` (30-day `HttpOnly; SameSite=Lax`, signed by `<dataDir>\.hk_cookie_secret.key`) or `?access_token=` on `/hubs/housekeeping` | Authenticated remote LAN/Tailnet browser session after `POST /api/v1/auth/login`. |
| **5** | `Unauthenticated` (`401`) | None / invalid | Rejected with `HTTP 401` (`{ "error": "Authentication required.", "requiresSetup": bool }`). |

- **Cookie Signing Key Migration (`<dataDir>\.hk_cookie_secret.key`):** On startup, `TieredAuthService.LoadOrCreateSigningKey` checks `<legacyLogsDir>\.hk_cookie_secret.key`; if present, it atomically moves/copies the key into `<dataDir>\.hk_cookie_secret.key` with restrictive ACLs, verifies persistence, and deletes the `logs\` copy so log directories contain zero cryptographic secrets.
- **Brute-Force Rate Limiter:** `POST /api/v1/auth/login` allows at most **5 failed attempts per 60-second window** per client IP (or first-hop `X-Forwarded-For`), returning `HTTP 429 Too Many Requests` with `Retry-After` for **30 seconds**.
- **Remote Raw Shell Guard (`RestrictRawShellToLocalhost = true`):** Blocks non-loopback callers (`HTTP 403`) from `POST /api/v1/terminal/exec`, modifying `CustomScripts` tasks or CLI tool paths in `PUT /api/v1/config`, restoring snapshots, and importing profiles.

---

## 2. Subpath Reverse-Proxy Contract (`/app/housekeeping/`)

When `Mother` or a reverse proxy mounts Housekeeping at `/app/housekeeping/` (stripping the prefix upstream and sending `X-Forwarded-Prefix: /app/housekeeping`):
- [HousekeepingServerHost.cs](../Housekeeping.Server/Hosting/HousekeepingServerHost.cs) inspects `X-Forwarded-Prefix` (and `UsePathBase`) cleanly.
- **`GET /sw.js`:** Returns `Cache-Control: no-cache, must-revalidate` and **`Service-Worker-Allowed`** matching `<prefix>/` (e.g., `/app/housekeeping/`) when proxied or `/` when served at root.
- **`GET /manifest.webmanifest`:** Served dynamically with prefix-scoped `start_url` and `scope` (`<prefix>/` or `./`) and relative `./icon-*.png` icon paths.
- **Managed Loopback Enforcement:** Launching with `--managed-by-mother` or `--managed-by-suite` forces `127.0.0.1` Kestrel binding regardless of `BindLanAndTailnet` or `--bind` overrides.
- **Frontend Prefix Detection (`subpathUrl.ts`):** Vite builds with `base: './'`, and `src/utils/subpathUrl.ts` detects active `/app/housekeeping` prefixes for all REST (`apiClient.ts`, `coexistenceAndSystemApi.ts`), SignalR (`/hubs/housekeeping`), and static asset URLs.

---

## 3. Complete REST API Reference (`/api/v1/*` & `/api/*`)

When a request includes **`X-Target-Node-Id: <remoteNodeId>`** (`!= "local"`), [RemoteNodeProxyMiddleware](../Housekeeping.Server/Proxy/RemoteNodeProxyMiddleware.cs) forwards `/api/v1/*` calls to that remote Fleet Node with `X-Housekeeping-Token` attached.

### 3.1 Health, Suite Supervision & Graceful Shutdown
| Method | Route | Auth / Caller | Request / Query | Response Payload & Notes |
| :---: | :--- | :---: | :--- | :--- |
| `GET` | `/api/v1/health` | Public | — | `{ status: "ok", version, machineName, isRunning, isIsolated, forceDryRun, timestampUtc }` |
| `GET` | `/api/scheduler/status` (`/api/v1/scheduler/status`) | Auth | — | `{ service: "Housekeeping", isRunning, isPaused, activeQueueId, activeQueueName, activeJobId, activeJobName, progressPercent, deferredUntilUtc, nextScheduledRunUtc, nextScheduledQueueName, nextRunStatusText, guardedProcessRunning, activeGuardedProcess }` |
| `POST` | `/api/scheduler/defer` (`/api/v1/scheduler/defer`) | Auth | `{ "minutes": 30, "reason": "Suite disk arbitration" }` | Defers background schedule evaluation for `minutes` (`0` clears deferral). Returns `{ ok: true, deferredUntilUtc, message }`. |
| `POST` | `/api/v1/system/shutdown` (`/api/system/shutdown`) | Direct Loopback or `NodeApiToken` | — | Invokes `InitiateGracefulShutdownAbort()` (preserving `interrupted_run_checkpoint.json` & `PendingRuns`) and stops Kestrel/STA host after 150ms. |

### 3.2 Authentication, Tailscale Serve HTTPS, Offline SVG QR & Desktop Window Bridge
| Method | Route | Source Module | Request Body | Response Payload & Notes |
| :---: | :--- | :--- | :--- | :--- |
| `GET` | `/api/v1/auth/status` | `HousekeepingApiEndpoints.cs` | Public | `{ isAuthenticated, authMethod, identity, isDirectLoopback, requiresInitialPasswordSetup, rawShellBlockedForCaller, nativeDialogsAvailable, machineName, version }` |
| `POST` | `/api/v1/auth/setup-password` | `HousekeepingApiEndpoints.cs` | `{ "password": "..." }` (`>= 4` chars) | Sets initial remote admin password when `AdminPasswordHash` is empty and sets `hk_session` cookie. |
| `POST` | `/api/v1/auth/login` | `HousekeepingApiEndpoints.cs` | `{ "password": "..." }` | Verifies PBKDF2-SHA256 password (rate-limited to 5 failures/60s) and sets 30-day `hk_session` cookie. |
| `POST` | `/api/v1/auth/logout` | `HousekeepingApiEndpoints.cs` | — | Clears `hk_session` cookie (`{ ok: true }`). |
| `POST` | `/api/v1/auth/change-password` | `HousekeepingApiEndpoints.cs` | `{ "currentPassword": "...", "newPassword": "..." }` | Updates admin password (loopback callers may omit `currentPassword`). |
| `POST` | `/api/v1/auth/rotate-node-token` | `HousekeepingApiEndpoints.cs` | — | Generates a fresh 48-hex `WebServer.NodeApiToken` (`{ ok: true, nodeApiToken }`). |
| `GET` | `/api/v1/system/tailscale-pwa` | `HousekeepingApiEndpoints.MotherAndQr.cs` | — | Returns `TailscalePwaStatusDto` (`TailscaleAvailable`, `BackendState`, `DnsName`, `HttpsPwaUrl`, `ServeConfigured`, `PendingAuthUrl`). |
| `POST` | `/api/v1/system/tailscale-pwa/enable` | `HousekeepingApiEndpoints.MotherAndQr.cs` | — | Provisions `tailscale serve --bg --yes http://127.0.0.1:<port>`. |
| `POST` | `/api/v1/system/tailscale-pwa/disable` | `HousekeepingApiEndpoints.MotherAndQr.cs` | — | Runs `tailscale serve reset`. |
| `GET` | `/api/v1/system/tailscale-qr` (`/api/v1/system/tailscale-qr.svg`) | `HousekeepingApiEndpoints.MotherAndQr.cs` | `?url=&variant=direct\|mother&format=json\|svg` | Offline ISO/IEC 18004 SVG QR via `Foundation.Core.QrCodeEncoder.RenderSvg`: returns `{ directUrl, directQrSvg, motherUrl, motherQrSvg, httpsPwaUrl, tailscaleAvailable, serveConfigured, dnsName, statusMessage }` (or `image/svg+xml` when `format=svg` or `.svg`). |
| `POST` | `/api/v1/desktop/open-standalone` | `HousekeepingApiEndpoints.cs` | — | Invokes `TrayDaemonApplicationContext.ShowOrActivateStandaloneWindow()` on the desktop host (`400` if no interactive tray context is attached). |

### 3.3 Configuration, 20-Snapshot History & Profile Import/Export
| Method | Route | Request / Query | Response Payload & Notes |
| :---: | :--- | :--- | :--- |
| `GET` | `/api/v1/config/bootstrap` | — | `{ config, consoleCatalog, builtInArchiveTemplates, snapshotsCount, pipelineState, greenroomStatus }` |
| `GET` | `/api/v1/config` | — | Returns active `AppConfig` JSON. |
| `PUT` | `/api/v1/config` | Full `AppConfig` JSON | Validates via `ConfigService.ValidateConfig`, enforces `HasRestrictedRemoteScriptOrToolChanges` (`403` if remote caller edits scripts/tool paths), writes `settings.json`, and creates a deduplicated snapshot (`{ ok: true, config, snapshotsCount }`). |
| `GET` | `/api/v1/config/snapshots` | — | `{ snapshots: List<ConfigSnapshotInfo> }` (newest 20 snapshots). |
| `POST` | `/api/v1/config/snapshots/{snapshotId}/restore` | Path `snapshotId` | Restores snapshot to `settings.json` (`{ ok: true, config, snapshotsCount }`). |
| `GET` | `/api/v1/profiles/export` | `?sanitize=true\|false` | Downloads `Housekeeping_Profile_<date>.hkprofile.json` (scrubbed of secrets when `sanitize=true`). |
| `POST` | `/api/v1/profiles/import` | `{ "profileJson": "..." }` | Validates and imports a `.hkprofile.json` configuration (`{ ok: true, config }`). |

### 3.4 Pipeline Execution, Up-Next Queue, Interrupted Recovery & Jobs CRUD
| Method | Route | Request / Query | Response Payload & Notes |
| :---: | :--- | :--- | :--- |
| `GET` | `/api/v1/pipeline/state` | — | Returns `PipelineRuntimeStateDto`. |
| `POST` | `/api/v1/pipeline/run-queue` | `{ queueId?, isDryRun, forceCloudResync }` | Starts queue immediately or enqueues in FIFO `Up Next` (`{ accepted, started, queued, message, state }`). |
| `POST` | `/api/v1/pipeline/run-job/{jobId}` | `{ isDryRun, forceCloudResync }` | Starts single job or enqueues in `Up Next`. |
| `POST` | `/api/v1/pipeline/pause` | — | Toggles queue pause between steps (`{ isPaused, state }`). |
| `POST` | `/api/v1/pipeline/suspend` | — | Freezes/unfreezes active child process threads via `NtSuspendProcess` (`{ isSuspended, state }`). |
| `POST` | `/api/v1/pipeline/stop-job` (`/api/pipeline/stop-job`) | — | Cancels only the active job (`_activeJobCts`), advances active queue to its next step, and preserves `Up Next` (`{ ok: true, state }`). |
| `POST` | `/api/v1/pipeline/stop-queue` (`/api/pipeline/stop-queue`, `/stop`) | — | Explicit user stop: cancels active run, clears `Up Next` queue, and deletes `interrupted_run_checkpoint.json` (`{ ok: true, state }`). |
| `GET` / `DELETE` / `POST` | `/api/v1/pipeline/pending-runs`, `/{itemKey}`, `/clear` | Path `itemKey` | Lists, removes single item from, or clears `Up Next`. |
| `POST` | `/api/v1/pipeline/interrupted/resume` & `/dismiss` | `{ "restartFullQueue": bool }` | Resumes remaining jobs (or restarts full queue) from `interrupted_run_checkpoint.json`, or dismisses it. |
| `GET` / `POST` | `/api/v1/pipeline/pending-swaps`, `/retry`, `/discard` | — | Lists, retries/commits (`FileShare.None` pre-probe), or discards pending `.partial` swaps. |
| `POST` | `/api/v1/pipeline/network-wait/retry` & `/skip` | — | Signals `NetworkSafetyService` to retry immediately or skip the unreachable NAS share. |
| `GET` | `/api/v1/queues/{queueId}/upcoming` | Path `queueId` | Returns `{ upcomingUtc: List<string> }` (next 5 scheduled trigger times). |
| `POST` / `DELETE` | `/api/v1/jobs/create`, `/{jobId}/clone`, `/{jobId}`, `/populate-starters` | Job DTOs / `jobId` | Creates, clones, deletes, or seeds starter jobs across all 8 modules. |

### 3.5 Modules Catalog, `.hash` Verifier, Archive Compliance Reports & Terminal
| Method | Route | Request / Query | Response Payload & Notes |
| :---: | :--- | :--- | :--- |
| `GET` / `POST` | `/api/v1/modules/catalog`, `/rclone-remotes/refresh`, `/{moduleId}/toggle` | `{ "enabled": bool }` | Lists modules/dependencies/remotes (using contained `<dataDir>\rclone\` async probe), refreshes remotes, or toggles app-wide module switch. |
| `GET` / `POST` | `/api/v1/manifest/presets/{bundleKey}` & `/regex-test` | `bundleKey` / `{ sampleInput, rules }` | Returns `List<ManifestFieldParseRule>` (`PersonalMedia`, `PersonalRoms`, `GeneralMedia`, `GeneralFiles`, `GeneralAudio`) or tests regex rules live. |
| `POST` | `/api/v1/rom/preview-sync` | `{ config, consoleOverride? }` | Generates multi-line `rclone` pre-launch and post-launch save sync command previews. |
| `POST` | `/api/v1/hash-verifier/run` | `{ jobId?, sourceModule, verifyMode }` | Runs background `.hash` verification (`FastHeaderAndHeal`, `DeepCryptographicAndHeal`, or `DryRunReportOnly`). |
| `GET` / `POST` | `/api/v1/reports`, `/scan`, `/dry-run-clean`, `/clean-extra`, `/heal-hash`, `/ignore`, `/clear-resolved`, `/export-csv`, `/export-json` | Query filters / `{ reportIds }` | Filters compliance findings, triggers `7z l -slt` scans, previews/executes `7z d` clutter removal, heals `.hash` sidecars, and exports CSV/JSON. |
| `GET` / `POST` | `/api/v1/terminal/logs`, `/clear`, `/exec`, `/cancel`, `/remote/{nodeId}` | `?limit=1500&afterId=0` / `{ "command": "..." }` | Reads/clears terminal ring buffer, runs interactive PowerShell/CMD (`403` remote when restricted), or tails remote node logs. |

### 3.6 Filesystem Pickers, Fleet Nodes, Greenroom, History & System Doctor
| Method | Route | Request / Query | Response Payload & Notes |
| :---: | :--- | :--- | :--- |
| `GET` / `POST` | `/api/v1/filesystem/drives`, `/browse`, `/native-pick-folder`, `/native-pick-file`, `/reveal-in-explorer` | `?path=&includeFiles=false` / `{ path }` | Enumerates drives/directories or invokes Win32 STA native dialogs (loopback only). |
| `GET` / `DELETE` | `/api/v1/history` & `/api/v1/history/{index}/log` | Path `index` | Lists `history.json` entries, reads per-run transcript logs from `logs\runs\`, or clears history. |
| `GET` / `POST` | `/api/v1/greenroom/status` & `/sync-now` | `{ sourceModule }` | Probes Greenroom `:8420` or triggers immediate catalog/hash ingestion. |
| `POST` | `/api/v1/settings/test-discord` & `/test-toast` | — | Sends test Discord Rich Embed or Windows tray balloon alert. |
| `GET` / `POST` | `/api/v1/nodes`, `/{nodeId}/command`, `/{nodeId}/push-settings`, `/{nodeId}/push-app` | `?probe=true` / `{ action }` | Probes remote Fleet Nodes, dispatches commands, or executes single-flight `SemaphoreSlim` SMB pushes (never copying `AGENTS.md`). |
| `GET` / `POST` | `/api/v1/coexistence/status` (`/api/coexistence/status`) & `/apply-recommendations` | `{ preset, enableAutoAlign? }` | Returns `CoexistenceStatusDto` (`schemaVersion: 2`) or applies `"delegate_to_shadowlibrary"`, `"align_placement"`, or `"enable_auto_align"`. |
| `GET` / `POST` | `/api/v1/system/doctor/diagnostics`, `/export-bundle`, `/system/backup/now` | — | Returns live dependency checks (`MediaInfoNativeResolver`, `HandBrakeCLI`, `7z`, `rclone`, `hbd`), exports sanitized JSON diagnostics strictly inside `ActiveLogsDir`, or writes clean JSON state-only backups into sibling `backups\`. |

---

## 4. SignalR Realtime Hub (`/hubs/housekeeping`)

Implemented in [HousekeepingHub.cs](../Housekeeping.Server/Hubs/HousekeepingHub.cs) and [RealtimeBroadcastHostedService.cs](../Housekeeping.Server/Hosting/RealtimeBroadcastHostedService.cs):
- **Client-to-Server Hub Method:** `SubscribeToNode(string? nodeId)` switches groups between `node:local` and `node:<remoteNodeId>`, pushing an immediate initial snapshot.
- **Server-to-Client Events (`node:local`):**
  1. `TerminalLogBatch(List<TerminalLogEntry> batch)` — Batched terminal lines flushed every `80ms`.
  2. `TerminalCleared()` — Fired when the terminal buffer is cleared.
  3. `PipelineState(PipelineRuntimeStateDto state)` — Fired on queue, job, pause, suspend, pending swap, or `Up Next` transitions.
  4. `SubprocessProgress(ProcessProgressInfo info)` — Throttled (`90ms`) live percentage and status from `HandBrakeCLI`, `7z`, `rclone`, or `.hash` verifier.
  5. `ComplianceSummary(object summary)` — `{ openCount, totalCount }` pushed after compliance scans or report updates.
  6. `TelemetrySnapshot(SystemPerformanceStats stats)` — Pushed every 3 seconds with CPU `%`, RAM `GB`, Disk `MB/s`, Network `Mbps`, and mounted drive metrics.

---

## 5. Suite Manifest (`v2`), CLI Flags & Operational Batch Scripts

- **Suite Companion Manifest (`Dev/suite.manifest.json`, staged to `app\suite.manifest.json`, `schemaVersion: 2`):** Strict `camelCase` (`manifestType: "suite-app"`, `appId: "housekeeping"`, `version: "0.2.0"`, `sdkVersion: "0.5.3"`, `minSuiteVersion: "0.3.0"`, `installProfile: "full"`, `umbrellaSubpath: "StreamItAll\\Mother\\Housekeeping"`, `executable: "app\\Housekeeping.exe"`, `launcherExecutable: "Housekeeping.exe"`, `internalPort: 5178`, `httpsPort: 8478`, `brandAccent: "#f59e0b"`, `workerArgs: ["--managed-by-suite", "--suite-id", "mother", "--bind", "127.0.0.1", "--port", "{port}"]`, `residency` `{ warm, cold, disabled }`, string `commands` `{ startDaemon, stopDaemon, statusCheck, backupNow }`, relative `endpoints` with `health: "/api/v1/health"`, `paths`, `schedules: []`, `targetDrives: []`).
- **Greenroom Integration (`:8420`):** Whenever Module 4 or Module 8 writes a changed manifest CSV/JSON (SHA-256 diffed), Housekeeping POSTs `{ "tasks": ["collect_library", "verify_hashes"], "source": "Housekeeping", "changedManifests": [...] }` to `http://127.0.0.1:8420/api/sync` (or `run_greenroom.bat` fallback).
- **CLI & Operational Scripts (`<InstallRoot>\scripts\`):**

| Binary / Script | Path | Invocation & Purpose |
| :--- | :--- | :--- |
| `Housekeeping.exe` | `<InstallRoot>\Housekeeping.exe` | `RootLauncherHost` root shim forwarding CLI flags (incl. synchronous `--status`, `--repair-config`, `--doctor`, `--bind`, `--suite-id`, `--automation-*`) to `app\Housekeeping.exe`. |
| `start-daemon.bat` | `<InstallRoot>\scripts\start-daemon.bat` | Starts `Housekeeping.exe --minimized --data-dir "<Root>\data" --log-dir "<Root>\logs"` in the background. |
| `stop-daemon.bat` | `<InstallRoot>\scripts\stop-daemon.bat` | Dispatches `POST http://127.0.0.1:5178/api/v1/system/shutdown` (`InitiateGracefulShutdownAbort`, preserving active checkpoints & `PendingRuns`). |
| `backup-now.bat` | `<InstallRoot>\scripts\backup-now.bat` | Compresses `data\settings.json` & `data\history.json` into `<InstallRoot>\backups\manual_backup_<timestamp>.7z` (or `.zip` fallback). |
| `status-check.bat` / `--status` | `<InstallRoot>\scripts\status-check.bat` | Synchronous one-shot JSON daemon & config probe; exits `0`. |
| `--doctor` | `Housekeeping.exe --doctor` | Synchronous one-shot diagnostic check (`0` healthy, `2` unhealthy) inspecting config, `data\`/`logs\` containment, sandbox bounds, and `HandBrakeCLI`/`7z`/`rclone`/`MediaInfo` availability. |
| `inspect-status.bat` | `<InstallRoot>\scripts\interactive\inspect-status.bat` | Interactive wrapper invoking `status-check.bat` followed by `pause`. |
| `repair-config.bat` / `--repair-config` | `<InstallRoot>\scripts\interactive\repair-config.bat` | Quarantines corrupt `settings.json` to `.corrupt_<timestamp>.json`, restores from snapshot/`.bak` or defaults, prints JSON, and exits `0`. |
| `tail-logs.bat` | `<InstallRoot>\scripts\interactive\tail-logs.bat` | Streams `<InstallRoot>\logs\Housekeeping_Live.log` live (`Get-Content -Wait -Tail 40`). |

---

## 6. File Contracts & Sister-App Schemas

### 6.1 `.hash` Checksum Sidecar Contract
- **Variant A — Single-File or Multi-File Game/Media Sidecar (`<Target>.hash`):**
  ```text
  # Housekeeping Checksum Sidecar
  # Title: Sample Game (USA)
  # Container: Sample Game (USA).7z | SIZE: 2948120 | CRC32: A1B2C3D4 | MD5: 0123456789ABCDEF0123456789ABCDEF | SHA1: 11223344556677889900AABBCCDDEEFF00112233
  # Generated: 2026-11-20T12:00:00Z

  FILE: Sample Game (USA).sfc | SIZE: 4194304 | CRC32: 2D206BF7 | MD5: A2BC447961E52FD2227BAED164F729DC | SHA1: DE5DC2662F232F22B4C251247B5220A0D0753C9E
  ```
- **Variant B — Composite Folder Dump Sidecar (`<Folder>.hash` for PS3 / Wii U / Switch / BDMV / VIDEO_TS):**
  ```text
  # Housekeeping Folder Composite Checksum
  # Folder: Sample Disc [GAME00001]
  # DumpType: PS3 Disc Folder Dump (PS3_GAME)
  # Generated: 2026-11-20T12:00:00Z
  TOTAL_FILES: 412
  TOTAL_SIZE: 7948206080
  COMPOSITE_SHA1: 8F9E7D6C5B4A3F2E1D0C9B8A7F6E5D4C3B2A1F0E

  # Manifest Files (Relative Paths & CRC32):
  ENTRY: PS3_GAME/PARAM.SFO | SIZE: 1480 | CRC32: 9A8B7C6D
  ```
  *Canonical `COMPOSITE_SHA1` Formula:* UTF-8 SHA-1 over sorted lines `"{NormalizedForwardSlashRelPath}|{SizeBytes}|{UPPER_CRC32}\n"`.

### 6.2 Distributed File Lock (`<Target>.hklock`) & Directory Sentinel (`<TargetDir>\.<appId>_<pid>.hklock`)
- **Per-File Transcode Lock (`<Target>.hklock`):** JSON holding `MachineName`, `UserName`, `ProcessId`, `StartedAtUtc`, `HeartbeatAtUtc`, and `TargetFilePath`.
- **Directory Mutation Sentinel (`<TargetDir>\.<appId>_<pid>.hklock`, `schemaVersion: 1`):**
  ```json
  {
    "$schema": "https://schemas.local/lockfile.v1.json",
    "schemaVersion": 1,
    "lockType": "directory-mutation",
    "suiteId": "mother",
    "ownerAppId": "housekeeping",
    "version": "0.2.0",
    "pid": 14820,
    "processStartTimeUtc": "2026-11-20T08:00:00.0000000Z",
    "hostname": "WORKSTATION-01",
    "port": 5178,
    "operation": "manifest-audit",
    "targetPath": "<MediaRoot>\\ROMs\\SNES",
    "acquiredAtUtc": "2026-11-20T08:15:00.0000000Z",
    "heartbeatAtUtc": "2026-11-20T08:16:00.0000000Z",
    "expiresAtUtc": "2026-11-20T08:45:00.0000000Z"
  }
  ```

### 6.3 Manifests, Coexistence & Post-Injection Registries
- **9-Column ROM Manifest (`collection_manifest.csv`):** `GameTitle,ProperName,ArchiveFileName,RomOrFolderName,VerifiedLocalCrc32,VerifiedLocalHash,MatchStatus,ArchiveCompliance,Notes`
- **Module 8 Universal Collection Manifest (`.csv` / `.tsv` / `.json` / `.html`):** Base columns `LocationID,RootPath,RelativePath,FileName,CleanTitle,Category,YearOrRegion,ResolutionOrTags,FileSizeBytes,FileSizeFormatted,CRC32,SHA1,MD5,IntegrityStatus,IntegrityDetail` plus enabled custom regex columns.
- **Shared-Root Coexistence Manifest (`<SharedRoot>\.coexistence.json`, `schemaVersion: 2`):** Coordinates `coexistenceMode`, `hashFilePlacement`, `syncPostInjectionContainerHashJson`, and `owners` across `Housekeeping`, `ShadowLibrary`, `Redroom`, `uStation`, `StreamEngine`, and `Greenroom`.
- **Post-Injection Outer Archive Registry (`<TargetDir>\.archive_hashes.json`, `schemaVersion: 1`):** Stores outer archive size, `lastWriteUtc`, `containerCrc32`, `containerMd5`, `containerSha1`, and primary inner file digests after `.hash` injection or `7z d` clutter removal.

---

## 7. JSON Persistence Schema Reference (`data\*.json`)

| File Name | Root Type | Key Top-Level Fields |
| :--- | :--- | :--- |
| `data\settings.json` | [AppConfig](../Housekeeping.Core/Models/AppConfig.cs) | `ConfigVersion` (`4`), `ActiveQueueId`, `NamedQueues[]`, `DefinedJobs[]`, `HandBrakeProfiles[]`, `ToolPaths`, `WebServer`, `GreenroomIntegration`, `RemoteNodes[]`, `GuardedProcessNames[]`, `PostRunPowerAction` (clean template in `Dev\config\settings.example.json` / `app\config\settings.example.json`). |
| `data\history.json` | `List<RunHistoryEntry>` | `StartedAt`, `CompletedAt`, `TriggerSource`, `ModulesSummary`, `ItemsProcessed`, `ItemsSkipped`, `ErrorsCount`, `Status`, `LogFilePath`. |
| `data\compliance_reports.json` | `List<ComplianceReportItem>` | `ReportId` (`"{Console}|{FullPath}"`), `ConsoleName`, `TargetFileName`, `TargetFullPath`, `IsDirectory`, `TemplateId`, `SummaryCode`, `PrimaryCategory`, `HighestSeverity`, `ResolutionStatus` (`Open`, `Ignored`, `Resolved`), `CleanableExtraFileCount`, `CleanableExtraBytes`, `Issues[]`, `EntryNodes[]`. |
| `data\pending_swaps.json` | `List<PendingSwapItem>` | `WorkingPath` (`.partial`), `TargetPath`, `FileSize`, `CreatedAtUtc`, `LastAttemptUtc`, `AttemptCount`, `Title`. |
| `data\interrupted_run_checkpoint.json` | `InterruptedRunCheckpoint` | `RunKind` (`Queue`, `SingleJob`, `HashVerifier`, `ComplianceScan`), `QueueId`, `QueueName`, `InitialJobIds[]`, `CompletedJobIds[]`, `ActiveJobId`, `PendingRuns[]`, `IsDryRun`, `LastProgressPercent`, `LastStatusText`, `StartedAtUtc`, `UpdatedAtUtc`. |
| `logs\telemetry_heartbeat.json` | `TelemetryHeartbeatDto` | `Service` (`"Housekeeping"`), `Version`, `ProcessId`, `TimestampUtc`, `IsRunning`, `ActiveQueueId`, `ActiveQueueName`, `CurrentJobIndex`, `TotalJobsInQueue`, `ActiveJobName`, `ActiveStatus`, `ProgressPercent`, `CpuUsagePercent`, `RamUsedGb`, `DiskReadWriteMbps`, `NetworkUsageMbps`. |
