# Security Policy

## Supported Versions

Housekeeping is currently in Pre-1.0 Public Beta (`v0.2.0`). Security fixes target the active `0.2.x` release branch and upcoming milestones.

| Version | Status | Notes |
| :--- | :---: | :--- |
| `0.2.x` (Beta) | Supported | Active public beta branch receiving security, isolation, and packaging updates. |
| `< 0.2.0` | Unsupported | Pre-public development builds; upgrade to `v0.2.0` or newer. |

---

## Reporting a Vulnerability

Please do **not** open a public GitHub issue or pull request for suspected security vulnerabilities.

1. Submit a private report using **[GitHub Security Advisories](https://github.com/StreamItAllGit/Housekeeping/security/advisories/new)**.
2. Include as much detail as possible:
   - Affected Housekeeping version (`Housekeeping.exe --version`), Windows version, and install mode (`Setup EXE`, `Portable ZIP`, `Mother` suite-managed, or source build).
   - Affected component (authentication, Kestrel endpoint, reverse-proxy subpath handling, `rclone` containment, installer extraction, or SMB fleet sync).
   - Minimal steps to reproduce or a proof of concept.
   - Potential impact (arbitrary command execution, path escape, secret leakage, or privilege boundary bypass).
3. Maintainers review private advisories promptly, verify fixes with automated tests before tagging, and credit reporters in release notes unless anonymity is requested.

---

## Security Architecture & Boundaries

### 1. Loopback Default, Managed Suite Bind & 5-Step Tiered Authentication
- **Loopback & Suite Binding:** Kestrel binds to `127.0.0.1:5178` by default when `BindLanAndTailnet` is `false` or `--localhost-only` / `--bind 127.0.0.1` is passed. Under suite management (`--managed-by-mother` or `--managed-by-suite`), `127.0.0.1` loopback binding is enforced unconditionally behind the parent gateway's `/app/housekeeping/` subpath proxy.
- **Direct Loopback vs. Reverse-Proxied Traffic:** Only raw loopback sockets (`127.0.0.1` / `::1`) without `X-Forwarded-For`, `X-Real-IP`, or `Tailscale-User-Login` headers are treated as direct local desktop access (`IsDirectLoopback`).
- **Session Signing Key Hygiene:** 30-day HMAC-SHA256 `hk_session` cookies (`HttpOnly; SameSite=Lax`) are signed with a 32-byte key stored strictly in `<dataDir>\.hk_cookie_secret.key` (legacy `logs\.hk_cookie_secret.key` files are migrated to `data\` with restrictive ACLs and deleted from `logs\`).
- **PBKDF2-SHA256 & Brute-Force Protection:** Remote admin passwords use 16-byte CSPRNG salts and 100,000-iteration PBKDF2-SHA256 hashing. Failed logins are throttled to 5 attempts per 60 seconds per client IP (`HTTP 429` lockout for 30 seconds).
- **Remote Raw Shell Guard (`RestrictRawShellToLocalhost`):** Enabled by default (`true`), blocking remote callers (`HTTP 403`) from `/api/v1/terminal/exec`, modifying `CustomScripts` tasks or CLI tool paths via `PUT /api/v1/config`, restoring snapshots, and importing profiles.

### 2. Release Automation Exclusion (`ExitCode = 2`)
- `Foundation.Automation.dll` and internal UI automation hooks (`--automation-port`, `--automation-token`, `DesktopAutomationBridge`, `InstallerAutomationHook`) are compiled strictly behind `Debug` or `-p:HousekeepingTestAutomation=true`.
- Standard Release binaries (`Housekeeping.exe`, `Housekeeping.Server.exe`, and `Housekeeping-Setup-v<Version>-win-x64.exe`) strip automation assemblies and fail closed immediately with **`ExitCode = 2`** if `--automation-*` flags are supplied.

### 3. Sandbox Path & `rclone` Containment (`ExitCode = 3`)
- When `FOUNDATION_SANDBOX_ROOT` / 4-drive sandbox containment (`SANDBOX_*_DRIVE`) or `APP_ISOLATED_TEST=1` is active, `ConfigService`, `SandboxEnvironmentSeam`, and the installer prevent host Registry, Start Menu, Task Scheduler, and host drive mutations, failing closed with **`ExitCode = 3`** on containment escape attempts.
- All three `rclone` callers (`CloudSyncModule`, `RomLauncherGeneratorModule`, and `DependencyCheckService`) isolate `RCLONE_CONFIG`, `--workdir`, `--cache-dir`, and `RCLONE_CACHE_DIR` under `<dataDir>\rclone\` so isolated or sandboxed runs never write lockfiles or configs to host `%LOCALAPPDATA%\rclone\`.

### 4. Log Redaction, Zip-Slip Protection & Clean-Room Policy
- Command-line loggers redact `--session-auth`, `--cookie`, `--password`, `--token`, and `--secret` arguments; crash bundles scrub user profile paths, tokens, and webhook URLs; profile exports (`?sanitize=true`) strip secrets and credentials.
- Installer payload verification validates SHA-256 checksums and rejects path-traversal (`Zip-Slip`) or symlink/reparse escapes prior to extraction.
- Housekeeping ships zero proprietary third-party binaries (`MediaInfo.dll` is resolved dynamically at runtime), zero credentials, and zero copyrighted media or ROMs.
