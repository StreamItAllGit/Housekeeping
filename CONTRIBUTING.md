# Contributing to Housekeeping

Thank you for your interest in contributing to **Housekeeping** (`v0.2.0` Beta, `MPL-2.0`). This guide covers repository hygiene, SDK setup, build/test commands, and pull request expectations.

---

## Community Standards & Clean-Room Policy

- **Code of Conduct:** All contributors and maintainers follow the [Contributor Covenant Code of Conduct](.github/CODE_OF_CONDUCT.md).
- **Clean-Room Rule:** Never commit compiled binaries (`Dev/lib/*.dll`, `Dev/Housekeeping.Core/MediaInfo.dll`), generated SPA bundles (`Dev/Housekeeping.Server/wwwroot/`), `node_modules/`, local runtime `data/` or `logs/`, API tokens, Discord webhooks, private workstation paths, or copyrighted media/ROM/DAT files.

---

## Prerequisites

- **OS:** Windows 10 or Windows 11 (`win-x64`)
- **.NET SDK:** **.NET 8 SDK** (`8.0.x`, targeting `net8.0-windows` / `net8.0`)
- **Node.js & npm:** **Node.js 20 LTS+** (for `Dev/Housekeeping.Web` TypeScript/Vite build and `Dev/scripts/verify_ui_hints.mjs`)
- **Python:** **Python 3.12+** (for Stage 2 sandbox E2E scripts and universal release packaging checks)
- **Optional (NativeAOT Launcher):** MSVC C++ Build Tools (if absent, `build_installer.ps1` falls back cleanly to single-file trimmed publishing)
- **IDE Workspace (Optional):** Open `Dev/Housekeeping.code-workspace` for preconfigured solution defaults, editor formatting, and build/test tasks.

---

## Acquiring the Pinned `Foundation` SDK (`Dev/lib/`)

Compiled `Foundation` SDK assemblies are excluded from Git. Before building `.NET` targets locally:

1. Check `sdkVersion` in `Dev/suite.manifest.json` (`0.5.3`, or fallback `0.5.2` when matching upstream GitHub Releases availability).
2. Download the matching `Foundation-v<Version>.zip` archive from [`StreamItAllGit/Foundation` Releases](https://github.com/StreamItAllGit/Foundation/releases) (or run `sync_foundation_sdk.py --sync-consumer` when building alongside a local `Foundation` checkout).
3. Extract `Foundation.*.dll` (`Foundation.Core.dll`, `Foundation.Installer.dll`, `Foundation.Launcher.dll`, `Foundation.Automation.dll`, plus accompanying XML/manifest files) into `Dev/lib/`.

---

## Building & Running Tests

Run commands from the repository root (`APP_ISOLATED_TEST=1` prevents live host mutations):

```powershell
# 1. Install, typecheck, and build the React 19 + TypeScript SPA
npm --prefix Dev\Housekeeping.Web ci
npx --prefix Dev\Housekeeping.Web tsc --noEmit
npm --prefix Dev\Housekeeping.Web run build

# 2. Verify UI 2-line hover hints, zero bare catches, and CSS parity
node Dev\scripts\verify_ui_hints.mjs

# 3. Build solution in Release mode (zero warnings required)
dotnet build Dev\Housekeeping.slnx -c Release -p:TreatWarningsAsErrors=true -nodeReuse:false

# 4. Run the isolated xUnit suite
$env:APP_ISOLATED_TEST = "1"
dotnet test Dev\Housekeeping.slnx -c Release -p:TreatWarningsAsErrors=true --no-build -nodeReuse:false --logger "console;verbosity=normal"
Remove-Item Env:APP_ISOLATED_TEST
```

### Local Staging, Packaging & Stage 2 Verification

```powershell
# Package Releases\v0.2.0\ and Releases\Latest\ (pass -SkipSigning when no local code-signing cert is installed)
powershell -NoProfile -ExecutionPolicy Bypass -File Dev\scripts\build_installer.ps1 -Version 0.2.0 -SkipSigning

# Verify the TestAutomation build compiles cleanly
dotnet build Dev\Housekeeping.slnx -c Release -p:HousekeepingTestAutomation=true -p:TreatWarningsAsErrors=true -nodeReuse:false
```

---

## Coding & Pull Request Guidelines

- **Modular File Size Discipline (`<= 400` SLOC):** Organize new features into focused partial classes, services, and React components (`<= 250` code SLOC target for new modules, `<= 400` soft cap). When touching large legacy files, extract cohesive helpers so touched monoliths shrink cleanly.
- **Zero-Warning Policy:** Every `.NET` build and test target runs with `-p:TreatWarningsAsErrors=true`. Do not leave bare empty `catch` blocks or suppress warnings without justification.
- **UI & Hint Hygiene:** Every `<input type="checkbox">` and `<th>` must carry `hk-has-hint` and a 2-line `title` hint defined in `src/hints/*.ts`; `verify_ui_hints.mjs` must pass with zero undefined `hk-*` CSS classes.
- **Zero Workstation Literals or Real Secrets:** Use neutral placeholders (`<MediaRoot>\Sets`, `example.invalid`) and temp directories in templates, UI placeholders, and tests.
- **Documentation Sync:** Update `Dev/docs/` (`ARCHITECTURE.md`, `INTERFACES.md`, `USER_GUIDE.md`, `OPERATIONS.md`) and `CHANGELOG.md` (`## [Unreleased]`) for user-facing changes.

---

## Submitting Changes

1. Open an issue first using the [Bug Report](.github/ISSUE_TEMPLATE/bug_report.yml) or [Feature Request](.github/ISSUE_TEMPLATE/feature_request.yml) templates for substantial changes.
2. Branch from `main`, include isolated xUnit tests for new behavior, and confirm SPA build, `verify_ui_hints.mjs`, and `dotnet test` pass with zero warnings.
3. Submit a pull request completing [`.github/PULL_REQUEST_TEMPLATE.md`](.github/PULL_REQUEST_TEMPLATE.md).

---

## License

By contributing to Housekeeping, you agree that your contributions are licensed under the **Mozilla Public License 2.0 (`MPL-2.0`)**. See [LICENSE](LICENSE) for full terms.
