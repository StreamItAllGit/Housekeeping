## Summary

<!-- Briefly describe what this pull request changes and why. -->

## Type of Change

- [ ] Bug fix (non-breaking change addressing an issue)
- [ ] New feature or module enhancement
- [ ] Web UI / PWA or suite subpath routing update
- [ ] Installer, CI workflow, packaging, or documentation update

## Verification Checklist

- [ ] `npm --prefix Dev/Housekeeping.Web run build` and `npx --prefix Dev/Housekeeping.Web tsc --noEmit` pass cleanly
- [ ] `node Dev/scripts/verify_ui_hints.mjs` passes (`100%` 2-line hint coverage, zero undefined CSS classes, zero bare catches)
- [ ] `APP_ISOLATED_TEST=1` `dotnet test Dev/Housekeeping.slnx -c Release -p:TreatWarningsAsErrors=true` passes with zero warnings
- [ ] No compiled `Dev/lib/*.dll`, `Dev/Housekeeping.Core/MediaInfo.dll`, `Dev/Housekeeping.Server/wwwroot/`, local `data/` or `logs/`, secrets, webhooks, or workstation paths are included
- [ ] Updated `Dev/docs/` manuals and `CHANGELOG.md` (`## [Unreleased]`) where user-facing behavior or interfaces changed
