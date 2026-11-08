# Housekeeping — Interfaces, REST API, SignalR & File Contracts Manual

- **Document:** `INTERFACES.md` (Technical Manual 4 of 4)
- **Active Build Version:** `v1.0.0-rc.11-hf.2` (In-Dev Target: `v1.0.0-rc.12`)
- **HTTP & SignalR Base URL:** `http://127.0.0.1:5178` (Sandbox: `http://127.0.0.1:15178` · Tailscale Serve HTTPS: `https://<node>.<tailnet>.ts.net`)
- **Primary Source Anchors:** [`HousekeepingApiEndpoints.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L25-L1675) · [`HousekeepingHub.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Hubs/HousekeepingHub.cs#L14-L108) · [`TieredAuthService.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Security/TieredAuthService.cs#L17-L396) · [`HashSidecarService.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/HashSidecarService.cs#L425-L803)

---

## 1. Authentication & Transport Security Model

All `/api/*` and `/hubs/*` requests pass through [`TieredAuthMiddleware`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Security/TieredAuthMiddleware.cs#L12-L94) and [`TieredAuthService.EvaluateRequest`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Security/TieredAuthService.cs#L63-L163):

| Priority | Auth Method (`AuthMethod`) | Header / Credential Inspected | Caller Scope & Privilege |
| :---: | :--- | :--- | :--- |
| **0 (Public)** | Unauthenticated Public Bypass | `/api/v1/health`, `/api/v1/auth/status`, `/api/v1/auth/login`, `/api/v1/auth/logout`, `/api/v1/auth/setup-password` | Always accessible without credentials so Mother health probes and login/setup dialogs work. |
| **1** | `NodeApiToken` | `X-Housekeeping-Token: <token>` or `Authorization: Bearer <token>` | Machine-to-machine Fleet Node & automation access; matched via constant-time comparison against `WebServer.NodeApiToken`. |
| **2** | `TailscaleServe` | Raw loopback (`127.0.0.1`/`::1`) + `Tailscale-User-Login: <user@domain>` (when `TrustTailscaleServeHeaders = true`) | Authenticated Tailscale tailnet identity via `tailscale serve`. Subject to `RestrictRawShellToLocalhost`. |
| **3** | `Loopback` | Raw loopback (`127.0.0.1`/`::1`) with **no** `X-Forwarded-For`, `X-Real-IP`, or `Tailscale-User-Login` headers (`IsDirectLoopback = true`) | Full local desktop host / local browser access, including native Win32 dialogs (`StaNativeDialogBridge`) and interactive raw shell. |
| **4** | `SessionCookie` / `SignalRQueryToken` | Cookie `hk_session={issuedUnix}:{identity}:{hmacSha256}` (30-day `HttpOnly; SameSite=Lax`) or `?access_token=` on `/hubs/housekeeping` | Authenticated remote LAN/Tailnet browser session after `POST /api/v1/auth/login`. |
| **5** | `Unauthenticated` (`401`) | None / invalid | Rejected with `HTTP 401` (`{ "error": "Authentication required.", "requiresSetup": bool }`). |

- **Brute-Force Rate Limiter ([`TieredAuthService.cs#L186-L238`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Security/TieredAuthService.cs#L186-L238)):** `POST /api/v1/auth/login` allows at most **5 failed attempts per 60-second window** per client IP (or first-hop `X-Forwarded-For`), returning `HTTP 429 Too Many Requests` with `Retry-After` for **30 seconds**.
- **Remote Raw Shell Guard (`RestrictRawShellToLocalhost = true`):** Blocks non-loopback callers (`HTTP 403`) from `POST /api/v1/terminal/exec`, modifying `CustomScripts` tasks or CLI tool paths in `PUT /api/v1/config`, restoring snapshots, and importing profiles.

---

## 2. Complete REST API Reference (`/api/v1/*` & `/api/*`)

All routes are mapped in [`HousekeepingApiEndpoints.MapHousekeepingEndpoints`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L25-L1526). When a request includes header **`X-Target-Node-Id: <remoteNodeId>`** (where `<remoteNodeId>` $\ne$ `"local"`), [`RemoteNodeProxyMiddleware`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Proxy/RemoteNodeProxyMiddleware.cs#L42-L136) forwards the `/api/v1/*` call to that remote Fleet Node's `http://<HostOrIp>:<HttpApiPort>` with `X-Housekeeping-Token` attached.

### 2.1 Health, Mother Fleet Supervision & Graceful Shutdown
| Method | Route | Source Line | Request / Query | Response Payload & Notes |
| :---: | :--- | :---: | :--- | :--- |
| `GET` | `/api/v1/health` | [`L31-L55`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L31-L55) | Public | `{ status: "healthy", service: "Housekeeping", version, archetype: "2-tray-companion", port, pid, uptimeSeconds, testMode, dryRun, isRunning, activeQueue, activeJob, progressPercent, pendingSwapCount, timestampUtc }` |
| `GET` | `/api/scheduler/status` | [`L61-L82`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L61-L82) | Auth | `{ service: "Housekeeping", isRunning, isPaused, activeQueueId, activeQueueName, activeJobId, activeJobName, progressPercent, deferredUntilUtc, nextScheduledRunUtc, nextScheduledQueueName, nextRunStatusText, guardedProcessRunning, activeGuardedProcess }` |
| `POST` | `/api/scheduler/defer` | [`L84-L110`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L84-L110) | `{ "minutes": 30, "reason": "Mother disk arbitration" }` | Defers background schedule evaluation for `minutes` (`0` clears deferral). Returns `{ ok: true, deferredUntilUtc, message }`. |
| `POST` | `/api/system/shutdown` | [`L112-L135`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L112-L135) | Direct Loopback or `NodeApiToken` (`403` otherwise) | Persists active checkpoint if running, stops pipeline, and calls `IHostApplicationLifetime.StopApplication()` after 150ms. |

### 2.2 Authentication, Tailscale Serve HTTPS & Desktop Window Bridge
| Method | Route | Source Line | Request Body | Response Payload & Notes |
| :---: | :--- | :---: | :--- | :--- |
| `GET` | `/api/v1/auth/status` | [`L141-L163`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L141-L163) | Public | `{ isAuthenticated, authMethod, identity, isDirectLoopback, requiresInitialPasswordSetup, rawShellBlockedForCaller, nativeDialogsAvailable, machineName, version }` |
| `POST` | `/api/v1/auth/setup-password` | [`L165-L194`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L165-L194) | `{ "password": "..." }` ($\ge 4$ chars) | Sets initial remote admin password when `AdminPasswordHash` is empty and sets `hk_session` cookie. |
| `POST` | `/api/v1/auth/login` | [`L196-L240`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L196-L240) | `{ "password": "..." }` | Verifies PBKDF2-SHA256 password (rate-limited to 5 failures/60s) and sets 30-day `hk_session` cookie. |
| `POST` | `/api/v1/auth/logout` | [`L242-L247`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L242-L247) | — | Clears `hk_session` cookie (`{ ok: true }`). |
| `POST` | `/api/v1/auth/change-password` | [`L249-L280`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L249-L280) | `{ "currentPassword": "...", "newPassword": "..." }` | Updates admin password (loopback callers may omit `currentPassword`). |
| `POST` | `/api/v1/auth/rotate-token` | [`L282-L296`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L282-L296) | — | Generates a fresh 48-hex `WebServer.NodeApiToken` (`{ ok: true, nodeApiToken }`). |
| `GET` | `/api/v1/tailscale/pwa-status` | [`L302-L309`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L302-L309) | — | Returns `TailscalePwaStatusDto` (`TailscaleAvailable`, `BackendState`, `DnsName`, `HttpsPwaUrl`, `ServeConfigured`, `PendingAuthUrl`). |
| `POST` | `/api/v1/tailscale/enable-pwa` | [`L311-L319`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L311-L319) | — | Provisions `tailscale serve --bg --https=443 http://127.0.0.1:<port>`. |
| `POST` | `/api/v1/tailscale/disable-pwa` | [`L321-L329`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L321-L329) | — | Runs `tailscale serve reset`. |
| `POST` | `/api/v1/desktop/open-window` | [`L331-L343`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L331-L343) | — | Invokes `TrayDaemonApplicationContext.ShowOrActivateStandaloneWindow()` on the desktop host (`400` if running `--headless`). |

### 2.3 Configuration, 20-Snapshot History & Profile Import/Export
| Method | Route | Source Line | Request / Query | Response Payload & Notes |
| :---: | :--- | :---: | :--- | :--- |
| `GET` | `/api/v1/config/bootstrap` | [`L349-L368`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L349-L368) | — | `{ config, consoleCatalog, builtInArchiveTemplates, snapshotsCount, pipelineState, greenroomStatus }` |
| `GET` | `/api/v1/config` | [`L370-L374`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L370-L374) | — | Returns active `AppConfig` JSON. |
| `PUT` | `/api/v1/config` | [`L376-L406`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L376-L406) | Full `AppConfig` JSON | Validates via `ConfigService.ValidateConfig`, enforces `HasRestrictedRemoteScriptOrToolChanges` (`403` if remote caller edits scripts/tool paths), writes `settings.json`, and creates a deduplicated snapshot (`{ ok: true, config, snapshotsCount }`). |
| `GET` | `/api/v1/config/snapshots` | [`L408-L412`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L408-L412) | — | `{ snapshots: List<ConfigSnapshotInfo> }` (newest 20 snapshots). |
| `POST` | `/api/v1/config/snapshots/{id}/restore` | [`L414-L428`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L414-L428) | Path `id` | Restores snapshot `id` to `settings.json` (`{ ok: true, config, snapshotsCount }`). |
| `GET` | `/api/v1/profiles/export` | [`L1301-L1318`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L1301-L1318) | `?sanitize=true\|false` | Downloads `Housekeeping_Profile_<date>.hkprofile.json` (scrubbed of secrets when `sanitize=true`). |
| `POST` | `/api/v1/profiles/import` | [`L1320-L1354`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L1320-L1354) | `{ "profileJson": "..." }` | Validates and imports a `.hkprofile.json` configuration (`{ ok: true, config }`). |

### 2.4 Pipeline Execution, Up-Next Queue, Interrupted Recovery & Jobs CRUD
| Method | Route | Source Line | Request / Query | Response Payload & Notes |
| :---: | :--- | :---: | :--- | :--- |
| `GET` | `/api/v1/pipeline/state` | [`L434-L438`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L434-L438) | — | Returns `PipelineRuntimeStateDto`. |
| `POST` | `/api/v1/pipeline/run-queue` | [`L440-L453`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L440-L453) | `{ queueId?, isDryRun, forceCloudResync }` | Starts queue immediately or enqueues in FIFO `Up Next` (`{ accepted, started, queued, message, state }`). |
| `POST` | `/api/v1/pipeline/run-job` | [`L455-L469`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L455-L469) | `{ jobId, isDryRun, forceCloudResync }` | Starts single job or enqueues in `Up Next`. |
| `POST` | `/api/v1/pipeline/pause` | [`L471-L476`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L471-L476) | — | Toggles queue pause between steps (`{ isPaused, state }`). |
| `POST` | `/api/v1/pipeline/suspend` | [`L478-L483`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L478-L483) | — | Freezes/unfreezes active child process threads via `NtSuspendProcess` (`{ isSuspended, state }`). |
| `POST` | `/api/v1/pipeline/stop` | [`L485-L490`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L485-L490) | — | Cancels active run and clears `Up Next` queue (`{ ok: true, state }`). |
| `POST` | `/api/v1/pipeline/pending-runs/remove` | [`L492-L501`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L492-L501) | `{ "itemKey": "Queue:..." }` | Removes a single item from `Up Next`. |
| `POST` | `/api/v1/pipeline/pending-runs/clear` | [`L503-L508`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L503-L508) | — | Clears all pending runs (`{ ok: true, cleared, state }`). |
| `POST` | `/api/v1/pipeline/interrupted/resume` | [`L510-L522`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L510-L522) | `{ "restartFullQueue": bool }` | Resumes remaining jobs (or restarts full queue) from `interrupted_run_checkpoint.json`. |
| `POST` | `/api/v1/pipeline/interrupted/dismiss` | [`L524-L529`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L524-L529) | — | Deletes `interrupted_run_checkpoint.json` (`{ ok: true, state }`). |
| `POST` | `/api/v1/pipeline/pending-swaps/retry` | [`L531-L537`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L531-L537) | — | Sweeps and commits `pending_swaps.json` (`{ committed, remaining, state }`). |
| `POST` | `/api/v1/pipeline/pending-swaps/discard` | [`L539-L544`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L539-L544) | — | Discards all pending `.partial` swaps. |
| `POST` | `/api/v1/pipeline/network-wait/retry` & `/skip` | [`L546-L557`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L546-L557) | — | Signals `NetworkSafetyService` to retry immediately or skip the unreachable NAS share. |
| `GET` | `/api/v1/queues/{queueId}/upcoming` | [`L563-L580`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L563-L580) | Path `queueId` | Returns `{ upcomingUtc: List<string> }` (next 5 scheduled trigger times). |
| `POST` | `/api/v1/jobs/create` | [`L586-L619`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L586-L619) | `{ moduleType, name?, attachToQueueId? }` | Creates a new `HousekeepingJob` and optionally attaches it to a queue. |
| `POST` | `/api/v1/jobs/{jobId}/clone` | [`L621-L651`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L621-L651) | Path `jobId` | Deep-clones job `jobId` (`{ ok: true, job }`). |
| `DELETE` | `/api/v1/jobs/{jobId}` | [`L653-L674`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L653-L674) | Path `jobId` | Deletes job from `DefinedJobs` and all `NamedQueues`. |
| `POST` | `/api/v1/jobs/populate-starters` | [`L676-L682`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L676-L682) | — | Ensures at least 1 starter job exists for each of the 8 modules (`{ ok: true, addedCount }`). |

### 2.5 Modules Catalog, `.hash` Verifier, Archive Compliance Reports & Terminal
| Method | Route | Source Line | Request / Query | Response Payload & Notes |
| :---: | :--- | :---: | :--- | :--- |
| `GET` | `/api/v1/modules/catalog` | [`L688-L700`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L688-L700) | — | `{ modules, dependencies, rcloneRemotes }` |
| `POST` | `/api/v1/modules/rclone-remotes/refresh` | [`L702-L707`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L702-L707) | — | Queries `rclone listremotes` asynchronously (`{ rcloneRemotes }`). |
| `POST` | `/api/v1/modules/{moduleId}/toggle` | [`L709-L725`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L709-L725) | `{ "enabled": bool }` | Toggles a module's app-wide `Enabled` master switch. |
| `GET` | `/api/v1/manifest/presets/{bundleKey}` | [`L731-L736`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L731-L736) | Path `bundleKey` | Returns `List<ManifestFieldParseRule>` for `PersonalMedia`, `PersonalRoms`, `GeneralMedia`, `GeneralFiles`, or `GeneralAudio`. |
| `POST` | `/api/v1/manifest/regex-test` | [`L738-L744`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L738-L744) | `{ sampleInput, rules }` | Evaluates C# Regex extraction rules against a sample file path (`RegexPlaygroundResultDto`). |
| `POST` | `/api/v1/rom/preview-sync` | [`L746-L762`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L746-L762) | `{ config, consoleOverride? }` | Generates multi-line `rclone` pre-launch and post-launch save sync command previews. |
| `POST` | `/api/v1/hash-verifier/run` | [`L764-L785`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L764-L785) | `{ jobId?, sourceModule, verifyMode }` | Starts background `.hash` verification (`FastHeaderAndHeal`, `DeepCryptographicAndHeal`, or `DryRunReportOnly`). |
| `GET` | `/api/v1/reports` | [`L791-L867`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L791-L867) | `?console=&category=&status=&search=` | Returns `{ items, consoles, kpis: { openCount, cleanableCount, missingOrFormatCount, resolvedOrIgnoredCount } }`. |
| `POST` | `/api/v1/reports/scan` | [`L869-L876`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L869-L876) | `{ jobId? }` | Starts background zero-decompression Archive Structure Compliance Scan. |
| `POST` | `/api/v1/reports/dry-run-clean` | [`L878-L904`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L878-L904) | `{ reportId, explicitInternalPaths? }` | Returns `ArchiveCleanupDryRunResult` previewing `7z d` clutter removal vs protected ROM files. |
| `POST` | `/api/v1/reports/clean-extra` | [`L906-L953`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L906-L953) | `{ reportIds, explicitInternalPaths? }` | Executes `7z d` clutter removal and re-evaluates archive compliance (`{ cleaned, failed }`). |
| `POST` | `/api/v1/reports/heal-hashes` | [`L955-L1006`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L955-L1006) | `{ reportIds }` | Recomputes/heals `.hash` sidecars for the selected report targets (`{ healedCount }`). |
| `POST` | `/api/v1/reports/ignore` & `/clear-resolved` | [`L1008-L1026`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L1008-L1026) | `{ reportIds, ignored }` | Marks reports ignored/reopened or purges resolved items. |
| `GET` | `/api/v1/reports/export-csv` & `/export-json` | [`L1028-L1059`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L1028-L1059) | — | Downloads `Housekeeping_Compliance_Reports.<csv\|json>`. |
| `GET` | `/api/v1/terminal/logs` | [`L1065-L1077`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L1065-L1077) | `?limit=1500&afterId=0` | Returns `{ entries, latestId, workingDirectory, isCommandRunning, commandHistory }`. |
| `POST` | `/api/v1/terminal/clear` | [`L1079-L1085`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L1079-L1085) | — | Clears in-memory terminal ring buffer and broadcasts `TerminalCleared`. |
| `POST` | `/api/v1/terminal/exec` & `/cancel` | [`L1087-L1119`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L1087-L1119) | `{ "command": "..." }` | Runs interactive PowerShell/CMD command in `InteractiveCliSessionService` (blocked with `403` for remote callers when `RestrictRawShellToLocalhost = true`). |
| `GET` | `/api/v1/terminal/remote/{nodeId}` | [`L1121-L1168`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L1121-L1168) | `?limit=350` | Polls remote node's `/api/v1/terminal/logs` (or tails `\\share\logs\Housekeeping_Live.log` via SMB fallback). |

### 2.6 Filesystem Pickers, Fleet Nodes, Greenroom, History & System Doctor
| Method | Route | Source Line | Request / Query | Response Payload & Notes |
| :---: | :--- | :---: | :--- | :--- |
| `GET` | `/api/v1/fs/drives` & `/browse` | [`L1174-L1242`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L1174-L1242) | `?path=&includeFiles=false` | Server-side directory/file browser for web & remote clients. |
| `POST` | `/api/v1/fs/native-pick-folder`, `/native-pick-file` & `/reveal` | [`L1244-L1295`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L1244-L1295) | `{ initialPath?, filter? }` / `{ path }` | Opens native Win32 STA folder/file picker or highlights target in `explorer.exe /select` (loopback only). |
| `GET` | `/api/v1/history` & `/api/v1/history/{index}/log` | [`L1360-L1395`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L1360-L1395) | Path `index` | Lists `history.json` entries or reads the per-run log file from `logs\runs\`. |
| `DELETE` | `/api/v1/history` | [`L1397-L1402`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L1397-L1402) | — | Clears `history.json`. |
| `GET` / `POST` | `/api/v1/greenroom/status` & `/sync` | [`L1408-L1424`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L1408-L1424) | `{ sourceModule }` | Probes Greenroom `:8420` or triggers immediate catalog/hash ingestion. |
| `POST` | `/api/v1/notifications/test-discord` & `/test-toast` | [`L1430-L1448`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L1430-L1448) | — | Sends test Discord Rich Embed or Windows tray balloon alert. |
| `GET` / `POST` | `/api/v1/fleet/nodes`, `/{nodeId}/command`, `/{nodeId}/push-settings` | [`L1454-L1525`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L1454-L1525) | `?probe=true` / `{ action }` | Probes remote Fleet Nodes, dispatches `StartQueue`/`StopQueue`/`RestartApp`, or pushes `settings.json` over SMB. |
| `POST` / `GET` | `/api/v1/doctor/backup` & `/diagnostic-bundle` | [`L1531-L1566`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Endpoints/HousekeepingApiEndpoints.cs#L1531-L1566) | — | Creates `.7z`/`.zip` state backup in `backups\config_snapshots\` or downloads a sanitized JSON diagnostic bundle. |

---

## 3. SignalR Realtime Hub (`/hubs/housekeeping`)

Implemented in [`HousekeepingHub.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Hubs/HousekeepingHub.cs#L14-L108) and driven by [`RealtimeBroadcastHostedService.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Hosting/RealtimeBroadcastHostedService.cs#L18-L194):

- **Client-to-Server Hub Method:**
  - `SubscribeToNode(string? nodeId)` ([`HousekeepingHub.cs#L58-L106`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Server/Hubs/HousekeepingHub.cs#L58-L106)): Moves the connection between SignalR group `node:local` and `node:<remoteNodeId>`. When subscribed to `node:local`, immediately pushes an initial snapshot (`TerminalLogBatch`, `PipelineState`, `ComplianceSummary`, `TelemetrySnapshot`).
- **Server-to-Client Events (sent to group `node:local`):**
  1. `TerminalLogBatch(List<TerminalLogEntry> batch)` — Batched terminal log lines flushed every `80ms`.
  2. `TerminalCleared()` — Fired when the terminal buffer is cleared.
  3. `PipelineState(PipelineRuntimeStateDto state)` — Fired on any queue, job, pause, suspend, pending swap, or `Up Next` state transition.
  4. `SubprocessProgress(ProcessProgressInfo info)` — Throttled (`90ms`) live percentage and status text from `HandBrakeCLI`, `7z`, `rclone`, or `.hash` verifier.
  5. `ComplianceSummary(object summary)` — `{ openCount, totalCount }` pushed after compliance scans or report updates.
  6. `TelemetrySnapshot(SystemPerformanceStats stats)` — Pushed every 3 seconds with CPU `%`, RAM `GB`, Disk `MB/s`, Network `Mbps`, and mounted drive free-space metrics.

---

## 4. Model Context Protocol (MCP) & Sister-App Integration Seams

Housekeeping **does not expose an MCP server (`/mcp`)**. Instead, sister applications in the 3-Tier Ecosystem integrate with Housekeeping through three deterministic seams:
1. **Mother v1.0 Fleet Daemon ([`mother.manifest.json`](file:///D:/Projects/Personal/Housekeeping/Dev/mother.manifest.json)):** Polls `GET /api/v1/health` and `<LogsDir>\telemetry_heartbeat.json`, inspects `GET /api/scheduler/status`, defers heavy disk/transcode schedules via `POST /api/scheduler/defer`, and triggers graceful shutdown via `POST /api/system/shutdown`.
2. **Greenroom Companion (`:8420` via [`GreenroomSyncIntegrationService.cs`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/GreenroomSyncIntegrationService.cs#L170-L321)):** Whenever Module 4 (`RomLauncherGenerator`) or Module 8 (`CollectionManifest`) writes a changed manifest CSV/JSON (verified by SHA-256 file fingerprint diffing in [`PipelineOrchestratorService.cs#L1508-L1593`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/PipelineOrchestratorService.cs#L1508-L1593)), Housekeeping POSTs `{ "tasks": ["collect_library", "verify_hashes"], "source": "Housekeeping", "changedManifests": [...] }` to `http://127.0.0.1:8420/api/sync` (or invokes `run_greenroom.bat` if `:8420` is offline).
3. **ShadowLibrary Shared Storage & `.hash` Sidecars:** Housekeeping and ShadowLibrary share the `.hash` sidecar format (Section 6.1) across ROM and media roots.

---

## 5. CLI Flags & Operational Batch Scripts Reference

| Script / Binary | Path | Invocation & Purpose |
| :--- | :--- | :--- |
| `Housekeeping.exe` | `<InstallRoot>\Housekeeping.exe` | Root clean-layout shim; forwards all arguments to `<InstallRoot>\app\Housekeeping.exe`. |
| `start.bat` | `<InstallRoot>\scripts\start.bat` | Starts `Housekeeping.exe --minimized` in the background if not already running. |
| `stop.bat` | `<InstallRoot>\scripts\stop.bat` | Gracefully stops `Housekeeping.exe` and `Housekeeping.Server.exe` via `taskkill /IM`. |
| `restart.bat` | `<InstallRoot>\scripts\restart.bat` | Calls `stop.bat`, waits 2 seconds, and calls `start.bat`. |
| `backup-data.bat` | `<InstallRoot>\scripts\backup-data.bat` | Compresses `<InstallRoot>\data\` into `<InstallRoot>\backups\config_snapshots\data_backup_<timestamp>.7z` (or `.zip`). |
| `launch-ui.bat` | `<InstallRoot>\scripts\interactive\launch-ui.bat` | Launches `Housekeeping.exe` (or restores the active Standalone WebView2 Window via `HOUSEKEEPING_RESTORE_WINDOW`). |

---

## 6. File Contracts & Sister-App Schemas

### 6.1 `.hash` Checksum Sidecar Contract ([`HashSidecarService.cs#L425-L471`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/HashSidecarService.cs#L425-L471))
- **Variant A — Single-File or Multi-File Game/Media Sidecar (`<Target>.hash`):**
  ```text
  # Housekeeping Checksum Sidecar
  # Title: Chrono Trigger (USA)
  # Container: Chrono Trigger (USA).7z | SIZE: 2948120 | CRC32: A1B2C3D4 | MD5: 0123456789ABCDEF0123456789ABCDEF | SHA1: 11223344556677889900AABBCCDDEEFF00112233
  # Generated: 2026-11-04T12:00:00Z

  FILE: Chrono Trigger (USA).sfc | SIZE: 4194304 | CRC32: 2D206BF7 | MD5: A2BC447961E52FD2227BAED164F729DC | SHA1: DE5DC2662F232F22B4C251247B5220A0D0753C9E
  ```
- **Variant B — Composite Folder Dump Sidecar (`<Folder>.hash` for PS3 / Wii U / Switch / BDMV / VIDEO_TS):**
  ```text
  # Housekeeping Folder Composite Checksum
  # Folder: Demon's Souls [BLUS30443]
  # DumpType: PS3 Disc Folder Dump (PS3_GAME)
  # Generated: 2026-11-04T12:00:00Z
  TOTAL_FILES: 412
  TOTAL_SIZE: 7948206080
  COMPOSITE_SHA1: 8F9E7D6C5B4A3F2E1D0C9B8A7F6E5D4C3B2A1F0E

  # Manifest Files (Relative Paths & CRC32):
  ENTRY: PS3_GAME/PARAM.SFO | SIZE: 1480 | CRC32: 9A8B7C6D
  ENTRY: PS3_GAME/USRDIR/EBOOT.BIN | SIZE: 12582912 | CRC32: 1E2F3A4B
  ```
  *Canonical `COMPOSITE_SHA1` Formula:* UTF-8 SHA-1 over sorted lines `"{NormalizedForwardSlashRelPath}|{SizeBytes}|{UPPER_CRC32}\n"` ([`HashSidecarService.cs#L345-L359,L407-L421`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/HashSidecarService.cs#L345-L359)).

### 6.2 Distributed Transcode Lockfile (`<Target>.hklock`)
Written by [`NetworkSafetyService.TryAcquireDistributedLock`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/NetworkSafetyService.cs#L383-L466):
```json
{
  "MachineName": "MEDIA-WORKSTATION",
  "UserName": "Stream",
  "ProcessId": 14820,
  "AcquiredAtUtc": "2026-11-04T08:15:00Z",
  "HeartbeatAtUtc": "2026-11-04T08:29:00Z",
  "TargetFilePath": "V:\\Media\\Movies\\Movie (1080p).mkv"
}
```

### 6.3 Module 4 9-Column ROM Manifest (`collection_manifest.csv`)
Written by [`RomManifestCsvService.WriteManifestToCsv`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/RomManifestCsvService.cs#L1415-L1435):
`GameTitle,ProperName,ArchiveFileName,RomOrFolderName,VerifiedLocalCrc32,VerifiedLocalHash,MatchStatus,ArchiveCompliance,Notes`

### 6.4 Module 8 Universal Collection Manifest (`<ManifestName>.csv` / `.json` / `.html`)
Written by [`CollectionManifestModule.ExportManifestsAsync`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/Modules/CollectionManifestModule.cs#L1003-L1069):
Base columns: `LocationID,RootPath,RelativePath,FileName,CleanTitle,Category,YearOrRegion,ResolutionOrTags,FileSizeBytes,FileSizeFormatted,CRC32,SHA1,MD5,IntegrityStatus,IntegrityDetail` followed dynamically by every enabled custom regex/text rule column name (`CustomFields`).

---

## 7. JSON Persistence Schema Reference (`data\*.json`)

| File Name | Root Type | Key Top-Level Fields |
| :--- | :--- | :--- |
| `data\settings.json` | [`AppConfig`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Models/AppConfig.cs#L137-L310) | `ConfigVersion` (`4`), `ActiveQueueId`, `NamedQueues[]`, `DefinedJobs[]`, `HandBrakeProfiles[]`, `ToolPaths`, `WebServer`, `GreenroomIntegration`, `RemoteNodes[]`, `GuardedProcessNames[]`, `PostRunPowerAction`. |
| `data\history.json` | `List<RunHistoryEntry>` | `StartedAt`, `CompletedAt`, `TriggerSource`, `ModulesSummary`, `ItemsProcessed`, `ItemsSkipped`, `ErrorsCount`, `Status`, `LogFilePath`. |
| `data\compliance_reports.json` | `List<ComplianceReportItem>` | `ReportId` (`"{Console}|{FullPath}"`), `ConsoleName`, `TargetFileName`, `TargetFullPath`, `IsDirectory`, `TemplateId`, `SummaryCode`, `PrimaryCategory`, `HighestSeverity`, `ResolutionStatus` (`Open`, `Ignored`, `Resolved`), `CleanableExtraFileCount`, `CleanableExtraBytes`, `Issues[]`, `EntryNodes[]`. |
| `data\pending_swaps.json` | `List<PendingSwapItem>` | `WorkingPath` (`.partial`), `TargetPath`, `FileSize`, `CreatedAtUtc`, `LastAttemptUtc`, `AttemptCount`, `Title`. |
| `data\interrupted_run_checkpoint.json` | `InterruptedRunCheckpoint` | `RunKind` (`Queue`, `SingleJob`, `HashVerifier`, `ComplianceScan`), `QueueId`, `QueueName`, `InitialJobIds[]`, `CompletedJobIds[]`, `ActiveJobId`, `PendingRuns[]`, `IsDryRun`, `LastProgressPercent`, `LastStatusText`, `StartedAtUtc`, `UpdatedAtUtc`. |
| `logs\telemetry_heartbeat.json` | `TelemetryHeartbeatDto` | `Service` (`"Housekeeping"`), `Version`, `ProcessId`, `TimestampUtc`, `IsRunning`, `ActiveQueueId`, `ActiveQueueName`, `CurrentJobIndex`, `TotalJobsInQueue`, `ActiveJobName`, `ActiveStatus`, `ProgressPercent`, `CpuUsagePercent`, `RamUsedGb`, `DiskReadWriteMbps`, `NetworkUsageMbps`. |

---

## 8. Compatibility & Integration Notes

- **Backward-Compatible `.hash` Parsing:** [`HashSidecarService.TryParseSidecarDocument`](file:///D:/Projects/Personal/Housekeeping/Dev/Housekeeping.Core/Services/HashSidecarService.cs#L575-L803) natively parses Housekeeping `FILE:` / `ENTRY:` sidecars, ShadowLibrary sidecars, legacy single-key `CRC32:`/`MD5:`/`SHA1:` files, and standard GNU/BSD `<hex> *<filename>` (`.sha1`, `.md5`, `.sfv`) files.
- **Upcoming `v1.0.0-rc.12` Cross-App File Lock Contract:** Per [`v1.0.0-rc.12.2026-11-04.md`](file:///D:/Projects/Personal/Housekeeping/Dev/docs/plans/v1.0.0-rc.12.2026-11-04.md), Housekeeping `rc.12` extends `HashSidecarService` and `RomLauncherGeneratorModule` to honor `<Folder>\.coexistence.json` and per-archive `.hklock` files alongside ShadowLibrary so concurrent runs across both apps never double-hash or contend on the same archive.
