using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Models;
namespace Housekeeping.Services.Modules
{
    public static partial class MediaTranscoderModule
    {
        public static async Task RunAsync(ModuleExecutionContext ctx, CancellationToken ct)
        {
            var cfg = ctx.ActiveTranscoderConfig;
            var rootFoldersToScan = BuildScanRootsList(cfg, ctx);

            ctx.Log("HEADER", $"=== Starting Multi-Tier Media Transcoder ({rootFoldersToScan.Count} root location(s) | Mode: {cfg.DetectionMode}) ===");

            if (rootFoldersToScan.Count == 0)
            {
                ctx.Log("WARN", "No target drives or custom scan folders configured for Multi-Tier Media Transcoder. Add drives or folders in Module Configurations -> 3. Media Transcoder.");
                return;
            }

            string hbExe = ctx.Dependencies.ResolveHandBrakeCli();

            int rootIdx = 0;
            foreach (string rootPath in rootFoldersToScan)
            {
                ct.ThrowIfCancellationRequested();
                await ctx.WaitIfQueuePausedAsync(ct);

                rootIdx++;
                ctx.UpdateModuleProgress((rootIdx - 1) * 100.0 / rootFoldersToScan.Count, $"Scanning '{rootPath}' ({rootIdx}/{rootFoldersToScan.Count})...");

                if (!await ctx.NetworkSafety.EnsureDirectoryReachableAsync(rootPath, $"MediaTranscoder ({rootPath})", createIfRootExists: false, ct))
                {
                    ctx.Stats.Errors++;
                    continue;
                }

                ctx.Log("INFO", $"--- Scanning media root [{rootIdx}/{rootFoldersToScan.Count}]: {rootPath} ---");

                FileInfo[] allMasterFilesInRoot;
                try
                {
                    allMasterFilesInRoot = ScanAllMasterFilesInRootOnce(rootPath, cfg, ctx, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    ctx.Log("ERROR", $"Error scanning '{rootPath}': {ex.Message}");
                    ctx.Stats.Errors++;
                    continue;
                }

                if (allMasterFilesInRoot.Length == 0)
                {
                    ctx.Log("SKIP", $"  No matching master media files found in {rootPath}");
                    continue;
                }

                ctx.Log("INFO", $"  Discovered {allMasterFilesInRoot.Length} total master media file(s) in {rootPath} (single-pass scan).");

                foreach (string ext in cfg.Extensions)
                {
                    ct.ThrowIfCancellationRequested();
                    string cleanExt = ext.TrimStart('.');
                    string dotExt = "." + cleanExt;

                    var extFiles = allMasterFilesInRoot
                        .Where(f => f.Extension.Equals(dotExt, StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                    if (extFiles.Length == 0)
                        continue;

                    if (cfg.DetectionMode == SourceDetectionMode.FilenameTagMatching)
                    {
                        await ProcessTagMatchingModeAsync(ctx, cfg, hbExe, rootPath, cleanExt, extFiles, ct);
                    }
                    else
                    {
                        await ProcessMediaInfoResolutionModeAsync(ctx, cfg, hbExe, rootPath, cleanExt, extFiles, ct);
                    }
                }
            }

            ctx.UpdateModuleProgress(100, "Multi-Tier Media Transcoder complete");
        }

        private static List<string> BuildScanRootsList(MediaTranscoderConfig cfg, ModuleExecutionContext ctx)
        {
            var roots = new List<string>();
            string subRel = (cfg.DriveSubfolderRelativePath ?? "").Trim('\\', '/');

            foreach (var d in cfg.DrivesToScan.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                string letter = d.Trim().TrimEnd(':', '\\', '/');
                string candidate = string.IsNullOrEmpty(subRel) ? $"{letter}:\\" : $"{letter}:\\{subRel}\\";
                if (Directory.Exists(candidate))
                    roots.Add(candidate);
                else
                    ctx.Log("WARN", $"Drive media path '{candidate}' not found. Skipping drive {letter}:.");
            }

            foreach (var folder in cfg.CustomFoldersToScan.Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Path)))
            {
                if (!roots.Contains(folder.Path, StringComparer.OrdinalIgnoreCase))
                    roots.Add(folder.Path);
            }

            return roots;
        }

        private static FileInfo[] ScanAllMasterFilesInRootOnce(
            string rootPath,
            MediaTranscoderConfig cfg,
            ModuleExecutionContext ctx,
            CancellationToken ct)
        {
            string searchPattern = string.IsNullOrWhiteSpace(cfg.MasterFileTag)
                ? "*.*"
                : $"*{cfg.MasterFileTag}*.*";

            var allowedDotExts = new HashSet<string>(
                cfg.Extensions.Select(e => "." + e.Trim().TrimStart('.')),
                StringComparer.OrdinalIgnoreCase);

            ctx.Log("INFO", $"  Performing single-pass scan of '{rootPath}' for '{searchPattern}'...");
            ctx.UpdateModuleProgress(5, $"Scanning {rootPath}...");

            var results = new List<FileInfo>();
            var rootDir = new DirectoryInfo(rootPath);
            var topEnumOpts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false };
            var recurseEnumOpts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };

            foreach (var f in rootDir.EnumerateFiles(searchPattern, topEnumOpts))
            {
                ct.ThrowIfCancellationRequested();
                if (allowedDotExts.Contains(f.Extension) && !NetworkSafetyService.IsPartialOrTransientArtifact(f.Name))
                    results.Add(f);
            }

            foreach (var subDir in rootDir.EnumerateDirectories("*", topEnumOpts))
            {
                ct.ThrowIfCancellationRequested();
                if (subDir.Name.StartsWith('.') || subDir.Name.Equals("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase) ||
                    subDir.Name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ctx.UpdateModuleProgress(10, $"Scanning {subDir.FullName} — {results.Count} master file(s) found...");
                foreach (var f in subDir.EnumerateFiles(searchPattern, recurseEnumOpts))
                {
                    ct.ThrowIfCancellationRequested();
                    if (allowedDotExts.Contains(f.Extension) && !NetworkSafetyService.IsPartialOrTransientArtifact(f.Name))
                        results.Add(f);
                }
            }

            return results.ToArray();
        }

        private static async Task ProcessTagMatchingModeAsync(
            ModuleExecutionContext ctx,
            MediaTranscoderConfig cfg,
            string hbExe,
            string rootPath,
            string ext,
            FileInfo[] extMasterFiles,
            CancellationToken ct)
        {
            var activeTiers = cfg.SourceTiers.Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.SourceTagsCsv)).ToList();

            foreach (var tier in activeTiers)
            {
                var tags = tier.SourceTagsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var targetResolutionProfiles = tier.GetActiveResolutionProfiles(cfg);

                if (targetResolutionProfiles.Count == 0)
                {
                    ctx.Log("WARN", $"Source tier '{tier.TierName}' has no active target resolution profiles selected. Skipping tier.");
                    continue;
                }

                foreach (string sourceTag in tags)
                {
                    ct.ThrowIfCancellationRequested();
                    await ctx.WaitIfQueuePausedAsync(ct);

                    string requiredMarker = string.IsNullOrWhiteSpace(cfg.MasterFileTag)
                        ? sourceTag
                        : $" {sourceTag} {cfg.MasterFileTag}";

                    var files = extMasterFiles
                        .Where(f => f.Name.Contains(requiredMarker, StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                    if (files.Length == 0)
                        continue;

                    ctx.Log("INFO", $"Found {files.Length} '{sourceTag}' file(s) (.{ext}) in {rootPath}");

                    int idx = 0;
                    DateTime lastSwapSweepUtc = DateTime.UtcNow;
                    foreach (var foundFile in files)
                    {
                        ct.ThrowIfCancellationRequested();
                        await ctx.WaitIfQueuePausedAsync(ct);

                        idx++;
                        ctx.Log("INFO", $"[{sourceTag} {idx}/{files.Length}] {foundFile.Name}");

                        foreach (var (resTag, profileId) in targetResolutionProfiles)
                        {
                            ct.ThrowIfCancellationRequested();
                            await ctx.WaitIfQueuePausedAsync(ct);
                            ctx.UpdateModuleProgress((idx - 1) * 100.0 / files.Length, $"[{sourceTag} {idx}/{files.Length}] {foundFile.Name} -> {resTag} ({profileId})");

                            await EncodeResolutionTierAsync(ctx, cfg, hbExe, foundFile, resTag, profileId, sourceTag, ct);
                        }

                        await ProcessLanguageEditionRulesAsync(ctx, cfg, hbExe, foundFile, sourceTag, ct);

                        if (ctx.NetworkSafety.PendingSwaps != null &&
                            ctx.NetworkSafety.PendingSwaps.Count > 0 &&
                            DateTime.UtcNow - lastSwapSweepUtc >= TimeSpan.FromMinutes(2))
                        {
                            lastSwapSweepUtc = DateTime.UtcNow;
                            ctx.NetworkSafety.PendingSwaps.SweepAndCommitAll(ctx.Log);
                        }
                    }
                }
            }
        }

        private static async Task ProcessMediaInfoResolutionModeAsync(
            ModuleExecutionContext ctx,
            MediaTranscoderConfig cfg,
            string hbExe,
            string rootPath,
            string ext,
            FileInfo[] files,
            CancellationToken ct)
        {
            if (files.Length == 0)
                return;

            ctx.Log("INFO", $"Found {files.Length} master file(s) (.{ext}) in {rootPath} for MediaInfo resolution inspection.");

            var orderedTiers = cfg.SourceTiers.Where(t => t.Enabled).OrderBy(t => t.MaxSourceHeightPx).ToList();
            if (orderedTiers.Count == 0) return;

            int idx = 0;
            DateTime lastSwapSweepUtc = DateTime.UtcNow;
            foreach (var foundFile in files)
            {
                ct.ThrowIfCancellationRequested();
                await ctx.WaitIfQueuePausedAsync(ct);

                idx++;
                int heightPx = GetVideoHeightPx(foundFile.FullName);
                var matchedTier = orderedTiers.FirstOrDefault(t => heightPx > 0 && heightPx <= t.MaxSourceHeightPx)
                                  ?? orderedTiers.Last();

                string sourceTagLabel = matchedTier.SourceTagsCsv
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault() ?? "SRC";

                ctx.Log("INFO", $"[AutoRes {idx}/{files.Length}] {foundFile.Name} (Detected {heightPx}p -> {matchedTier.TierName})");

                var targetResolutionProfiles = matchedTier.GetActiveResolutionProfiles(cfg);
                foreach (var (resTag, profileId) in targetResolutionProfiles)
                {
                    ct.ThrowIfCancellationRequested();
                    await ctx.WaitIfQueuePausedAsync(ct);
                    ctx.UpdateModuleProgress((idx - 1) * 100.0 / files.Length, $"[{idx}/{files.Length}] {foundFile.Name} -> {resTag} ({profileId})");

                    await EncodeResolutionTierAsync(ctx, cfg, hbExe, foundFile, resTag, profileId, sourceTagLabel, ct);
                }

                await ProcessLanguageEditionRulesAsync(ctx, cfg, hbExe, foundFile, sourceTagLabel, ct);

                if (ctx.NetworkSafety.PendingSwaps != null &&
                    ctx.NetworkSafety.PendingSwaps.Count > 0 &&
                    DateTime.UtcNow - lastSwapSweepUtc >= TimeSpan.FromMinutes(2))
                {
                    lastSwapSweepUtc = DateTime.UtcNow;
                    ctx.NetworkSafety.PendingSwaps.SweepAndCommitAll(ctx.Log);
                }
            }
        }

        private static async Task EncodeResolutionTierAsync(
            ModuleExecutionContext ctx,
            MediaTranscoderConfig cfg,
            string hbExe,
            FileInfo foundFile,
            string resCode,
            string selectedProfileId,
            string sourceTag,
            CancellationToken ct)
        {
            if (foundFile.Length == 0)
            {
                ctx.Log("WARN", $"  -> [Skip] Source file '{foundFile.Name}' is 0 bytes (empty/corrupted stub). Skipping.");
                ctx.Stats.Skipped++;
                return;
            }

            string sourceDir = foundFile.DirectoryName ?? "";
            if (!await ctx.NetworkSafety.EnsureDirectoryReachableAsync(sourceDir, $"MediaTranscoder ({resCode})", createIfRootExists: false, ct))
            {
                ctx.Stats.Errors++;
                return;
            }

            if (ShouldDeferForSisterMediaLock(sourceDir, ctx.Log))
            {
                ctx.Stats.Skipped++;
                return;
            }

            string newFile = BuildOutputTierFilePath(foundFile, sourceTag, cfg.MasterFileTag, resCode);
            if (NetworkSafetyService.PurgeZeroByteFileIfExists(newFile))
            {
                ctx.Log("WARN", $"  -> Purged 0-byte invalid output stub before re-encoding: {Path.GetFileName(newFile)}");
            }

            bool exists = File.Exists(newFile);
            bool keepVersion = false;

            if (exists)
                keepVersion = !cfg.ReEncodeNonHevcOutputs || CheckFileIsHevc(newFile, cfg.RemoteModeSkipMediaInfo);

            if (exists && keepVersion)
            {
                ctx.Log("SKIP", $"  -> [Skip] {resCode} up-to-date");
                ctx.Stats.Skipped++;
                return;
            }

            if (exists && !keepVersion)
            {
                if (ctx.IsDryRun)
                {
                    ctx.Log("DRYRUN", $"  -> [Dry Run] Would replace non-HEVC {resCode} file: {Path.GetFileName(newFile)}");
                    ctx.Stats.Processed++;
                    return;
                }

                ctx.Log("WARN", $"  -> [Update] Deleting non-HEVC {resCode} file for re-encoding...");
                try { File.Delete(newFile); }
                catch (Exception ex) { ctx.Log("ERROR", $"     Could not delete file '{Path.GetFileName(newFile)}': {ex.Message}"); }
                exists = File.Exists(newFile);
            }

            if (!exists)
            {
                var swapCheck = ctx.NetworkSafety.TryResolveOrRecoverPendingEncode(newFile, ctx.Log);
                if (swapCheck == PendingSwapCheckResult.Committed)
                {
                    ctx.Log("SUCCESS", $"  -> [Recovered] Successfully committed {resCode} without re-encoding!");
                    ctx.Stats.Processed++;
                    return;
                }
                if (swapCheck == PendingSwapCheckResult.PendingLocked)
                {
                    ctx.Log("SKIP", $"  -> [Skip] {resCode} encode is already complete and queued in pending swaps. Skipping re-encode.");
                    ctx.Stats.Skipped++;
                    return;
                }

                var profile = ctx.ConfigService.ResolveHandBrakeProfile(selectedProfileId, $"Tier_{resCode}");

                if (ctx.IsDryRun)
                {
                    ctx.Log("DRYRUN", $"  -> [Dry Run] Would encode {resCode} ({profile.Name}): {Path.GetFileName(newFile)}");
                    ctx.Stats.Processed++;
                    return;
                }

                if (!ctx.NetworkSafety.TryAcquireDistributedLock(newFile, out var lockHandle, out var conflictMsg))
                {
                    ctx.Log("WARN", $"  -> [Skip] {conflictMsg} Target: '{Path.GetFileName(newFile)}'");
                    ctx.Stats.Skipped++;
                    return;
                }

                using (lockHandle)
                {
                    string workingOutput = ctx.NetworkSafety.GetSafeEncodeOutputPath(newFile);
                    if (ctx.NetworkSafety.PendingSwaps != null && ctx.NetworkSafety.PendingSwaps.IsPending(workingOutput))
                    {
                        ctx.Log("WARN", $"  -> [Skip] Working output '{Path.GetFileName(workingOutput)}' is already registered in pending swaps. Skipping to protect completed encode.");
                        ctx.Stats.Skipped++;
                        return;
                    }

                    ctx.Log("WORK", $"  -> [Encode] Starting HandBrake for {resCode} ({profile.Name})...");

                    string hbArgs = profile.BuildHandBrakeArguments(foundFile.FullName, workingOutput);
                    string cmd = $"\"{hbExe}\" {hbArgs}";

                    var res = await ctx.ProcessRunner.RunCommandAsync(
                        cmd,
                        useExternalCmdWindow: ctx.ResolveUseExternalCmd(cfg),
                        workingDirectory: sourceDir,
                        belowNormalPriority: true,
                        logOutputToTerminal: true,
                        ct);

                    if (ct.IsCancellationRequested || res.ExitCode == -999)
                    {
                        ctx.NetworkSafety.DiscardPartialEncode(workingOutput, newFile);
                        ctx.Log("WARN", $"  -> [Cancelled] Transcoding was cancelled for {resCode}");
                    }
                    else if (res.ExitCode == 0 && ctx.NetworkSafety.CommitPartialEncode(workingOutput, newFile))
                    {
                        if (File.Exists(newFile))
                        {
                            CopyCompanionSubtitlesForOutput(foundFile, newFile, ctx.Log);
                            ctx.Log("SUCCESS", $"  -> [Done] Created {resCode}");
                        }
                        else
                        {
                            ctx.Log("WARN", $"  -> [Queued Swap] Completed {resCode} encode preserved and queued for background swap");
                        }
                        ctx.Stats.Processed++;
                    }
                    else
                    {
                        ctx.NetworkSafety.DiscardPartialEncode(workingOutput, newFile);
                        ctx.Log("ERROR", $"  -> [Error] HandBrake failed for {resCode} (Exit Code: {res.ExitCode})");
                        ctx.Stats.Errors++;
                    }
                }
            }
        }

        private static string BuildOutputTierFilePath(FileInfo foundFile, string sourceTag, string masterTag, string resCode)
        {
            string full = foundFile.FullName;
            string combinedMarker = $"{sourceTag} {masterTag}".Trim();
            int markerIdx = !string.IsNullOrWhiteSpace(combinedMarker)
                ? full.IndexOf(combinedMarker, StringComparison.OrdinalIgnoreCase)
                : -1;

            if (markerIdx < 0 && !string.IsNullOrWhiteSpace(masterTag))
                markerIdx = full.IndexOf(masterTag, StringComparison.OrdinalIgnoreCase);

            if (markerIdx >= 0)
            {
                string prefix = full.Substring(0, markerIdx);
                if (full.Contains('{'))
                {
                    int braceIdx = full.IndexOf('{');
                    string editionSuffix = " " + full.Substring(braceIdx);
                    return $"{prefix}{sourceTag} ({resCode}){editionSuffix}";
                }
                return $"{prefix}{sourceTag} ({resCode}){foundFile.Extension}";
            }

            string dir = foundFile.DirectoryName ?? "";
            string baseName = Path.GetFileNameWithoutExtension(foundFile.Name);
            int baseBraceIdx = baseName.IndexOf('{');
            if (baseBraceIdx > 0)
            {
                string cleanStem = baseName.Substring(0, baseBraceIdx).TrimEnd();
                string editionPart = baseName.Substring(baseBraceIdx);
                return Path.Combine(dir, $"{cleanStem} ({resCode}) {editionPart}{foundFile.Extension}");
            }
            return Path.Combine(dir, $"{baseName} ({resCode}){foundFile.Extension}");
        }

        private static async Task ProcessLanguageEditionRulesAsync(
            ModuleExecutionContext ctx,
            MediaTranscoderConfig cfg,
            string hbExe,
            FileInfo foundFile,
            string sourceTag,
            CancellationToken ct)
        {
            if (cfg.RemoteModeSkipMediaInfo)
                return;

            foreach (var langRule in cfg.LanguageEditionRules.Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.LanguageCode)))
            {
                ct.ThrowIfCancellationRequested();
                await ctx.WaitIfQueuePausedAsync(ct);

                if (!HasLanguageTrack(foundFile.FullName, langRule.LanguageCode))
                    continue;

                string resCode = string.IsNullOrWhiteSpace(langRule.TargetResolutionTag) ? "480p" : langRule.TargetResolutionTag;
                string newFile = BuildLanguageEditionPath(foundFile, sourceTag, cfg.MasterFileTag, resCode, langRule.LanguageFolderPrefix, langRule.LanguageCode);

                string targetDir = Path.GetDirectoryName(newFile) ?? "";
                if (!await ctx.NetworkSafety.EnsureDirectoryReachableAsync(targetDir, $"MediaTranscoder ({langRule.LanguageCode})", createIfRootExists: true, ct))
                {
                    ctx.Stats.Errors++;
                    continue;
                }

                if (NetworkSafetyService.PurgeZeroByteFileIfExists(newFile))
                {
                    ctx.Log("WARN", $"  -> Purged 0-byte invalid edition stub before re-encoding: {Path.GetFileName(newFile)}");
                }

                if (File.Exists(newFile))
                {
                    ctx.Log("SKIP", $"  -> [Skip] {langRule.LanguageCode} {resCode} up-to-date");
                    ctx.Stats.Skipped++;
                    continue;
                }

                var swapCheck = ctx.NetworkSafety.TryResolveOrRecoverPendingEncode(newFile, ctx.Log);
                if (swapCheck == PendingSwapCheckResult.Committed)
                {
                    ctx.Log("SUCCESS", $"  -> [Recovered] Successfully committed {langRule.LanguageCode} {resCode} edition without re-encoding!");
                    ctx.Stats.Processed++;
                    continue;
                }
                if (swapCheck == PendingSwapCheckResult.PendingLocked)
                {
                    ctx.Log("SKIP", $"  -> [Skip] {langRule.LanguageCode} {resCode} encode is already complete and queued in pending swaps. Skipping re-encode.");
                    ctx.Stats.Skipped++;
                    continue;
                }

                var profile = ctx.ConfigService.ResolveHandBrakeProfile(langRule.HandBrakeProfileId, "Lang_Edition_480p");

                if (ctx.IsDryRun)
                {
                    ctx.Log("DRYRUN", $"  -> [Dry Run] Would create {langRule.LanguageCode} {resCode} edition: {newFile}");
                    ctx.Stats.Processed++;
                    continue;
                }

                if (!ctx.NetworkSafety.TryAcquireDistributedLock(newFile, out var lockHandle, out var conflictMsg))
                {
                    ctx.Log("WARN", $"  -> [Skip] {conflictMsg} Target: '{Path.GetFileName(newFile)}'");
                    ctx.Stats.Skipped++;
                    continue;
                }

                using (lockHandle)
                {
                    string workingOutput = ctx.NetworkSafety.GetSafeEncodeOutputPath(newFile);
                    if (ctx.NetworkSafety.PendingSwaps != null && ctx.NetworkSafety.PendingSwaps.IsPending(workingOutput))
                    {
                        ctx.Log("WARN", $"  -> [Skip] Working output '{Path.GetFileName(workingOutput)}' is already registered in pending swaps. Skipping to protect completed encode.");
                        ctx.Stats.Skipped++;
                        continue;
                    }

                    ctx.Log("WORK", $"  -> [Encode] Starting HandBrake for {langRule.LanguageCode} {resCode} edition...");

                    string hbArgs = profile.BuildHandBrakeArguments(foundFile.FullName, workingOutput, overrideAudioLang: langRule.LanguageCode);
                    string cmd = $"\"{hbExe}\" {hbArgs}";

                    var res = await ctx.ProcessRunner.RunCommandAsync(
                        cmd,
                        useExternalCmdWindow: ctx.ResolveUseExternalCmd(cfg),
                        workingDirectory: targetDir,
                        belowNormalPriority: true,
                        logOutputToTerminal: true,
                        ct);

                    if (ct.IsCancellationRequested || res.ExitCode == -999)
                    {
                        ctx.NetworkSafety.DiscardPartialEncode(workingOutput, newFile);
                        ctx.Log("WARN", $"  -> [Cancelled] Transcoding was cancelled for {langRule.LanguageCode} {resCode}");
                    }
                    else if (res.ExitCode == 0 && ctx.NetworkSafety.CommitPartialEncode(workingOutput, newFile))
                    {
                        if (File.Exists(newFile))
                        {
                            ctx.Log("SUCCESS", $"  -> [Done] Created {langRule.LanguageCode} {resCode} edition");
                        }
                        else
                        {
                            ctx.Log("WARN", $"  -> [Queued Swap] Completed {langRule.LanguageCode} {resCode} edition encode preserved and queued for background swap");
                        }
                        ctx.Stats.Processed++;
                    }
                    else
                    {
                        ctx.NetworkSafety.DiscardPartialEncode(workingOutput, newFile);
                        ctx.Log("ERROR", $"  -> [Error] HandBrake failed for {langRule.LanguageCode} {resCode} (Exit Code: {res.ExitCode})");
                        ctx.Stats.Errors++;
                    }
                }
            }
        }

        private static string BuildLanguageEditionPath(
            FileInfo foundFile,
            string sourceTag,
            string masterTag,
            string resCode,
            string languageFolderPrefix,
            string languageCode)
        {
            string full = foundFile.FullName;
            string combinedMarker = $"{sourceTag} {masterTag}".Trim();
            int markerIdx = !string.IsNullOrWhiteSpace(combinedMarker)
                ? full.IndexOf(combinedMarker, StringComparison.OrdinalIgnoreCase)
                : -1;

            if (markerIdx < 0 && !string.IsNullOrWhiteSpace(masterTag))
                markerIdx = full.IndexOf(masterTag, StringComparison.OrdinalIgnoreCase);

            string newFile;
            if (markerIdx >= 0)
            {
                string prefix = full.Substring(0, markerIdx);
                if (full.Contains('{'))
                {
                    int braceIdx = full.IndexOf('{');
                    string editionCode = " " + full.Substring(braceIdx).Replace("}", $", {sourceTag} - {languageCode}}}");
                    newFile = $"{prefix}{sourceTag} ({resCode}){editionCode}";
                }
                else
                {
                    newFile = $"{prefix}{sourceTag} ({resCode}) {{edition-{sourceTag} - {languageCode}}}{foundFile.Extension}";
                }
            }
            else
            {
                string dir = foundFile.DirectoryName ?? "";
                string baseName = Path.GetFileNameWithoutExtension(foundFile.Name);
                newFile = Path.Combine(dir, $"{baseName} ({resCode}) {{edition-{languageCode}}}{foundFile.Extension}");
            }

            if (!string.IsNullOrWhiteSpace(languageFolderPrefix))
            {
                if (newFile.Contains("\\TV Shows\\", StringComparison.OrdinalIgnoreCase))
                    newFile = newFile.Replace("\\TV Shows\\", $"\\{languageFolderPrefix} TV Shows\\", StringComparison.OrdinalIgnoreCase);

                if (newFile.Contains("\\Movies\\", StringComparison.OrdinalIgnoreCase))
                    newFile = newFile.Replace("\\Movies\\", $"\\{languageFolderPrefix} Movies\\", StringComparison.OrdinalIgnoreCase);
            }

            return newFile;
        }
    }
}
