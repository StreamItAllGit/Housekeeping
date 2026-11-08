using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Models;

namespace Housekeeping.Services.Modules
{
    public static class ArchiveCompressorModule
    {
        public static async Task RunAsync(ModuleExecutionContext ctx, CancellationToken ct)
        {
            var cfg = ctx.ActiveArchiveConfig;
            var activeMappings = cfg.Mappings
                .Where(m => m.Enabled && !string.IsNullOrWhiteSpace(m.SourcePath) && !string.IsNullOrWhiteSpace(m.ArchiveOutputPath))
                .ToList();

            ctx.Log("HEADER", $"=== Starting Video Archive Compressor ({activeMappings.Count} folder mapping(s)) ===");

            if (activeMappings.Count == 0)
            {
                ctx.Log("WARN", "No enabled source->archive mappings configured. Add mappings in Module Configurations -> 2. Video Archive Compressor.");
                return;
            }

            string hbExe = ctx.Dependencies.ResolveHandBrakeCli();
            var profile = ctx.ConfigService.ResolveHandBrakeProfile(cfg.HandBrakeProfileId, "Archive_LQ");
            string outExt = !string.IsNullOrWhiteSpace(cfg.OutputExtension)
                ? (cfg.OutputExtension.StartsWith('.') ? cfg.OutputExtension : "." + cfg.OutputExtension)
                : (profile.ContainerExtension.StartsWith('.') ? profile.ContainerExtension : "." + profile.ContainerExtension);

            int mapIdx = 0;
            foreach (var mapping in activeMappings)
            {
                ct.ThrowIfCancellationRequested();
                await ctx.WaitIfQueuePausedAsync(ct);

                mapIdx++;
                string inPath = mapping.SourcePath.Trim();
                string outPath = mapping.ArchiveOutputPath.Trim();
                string label = string.IsNullOrWhiteSpace(mapping.Name) ? Path.GetFileName(inPath) : mapping.Name;

                ctx.Log("INFO", $"[Archive Mapping {mapIdx}/{activeMappings.Count}] '{label}': {inPath} -> {outPath}");

                if (!await ctx.NetworkSafety.EnsureDirectoryReachableAsync(inPath, $"Archive Source ({label})", createIfRootExists: false, ct))
                {
                    ctx.Stats.Errors++;
                    continue;
                }

                if (!await ctx.NetworkSafety.EnsureDirectoryReachableAsync(outPath, $"Archive Destination ({label})", createIfRootExists: true, ct))
                {
                    ctx.Stats.Errors++;
                    continue;
                }

                var dirInfo = new DirectoryInfo(inPath);
                var files = new List<FileInfo>();
                var safeEnumOpts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };

                foreach (var ext in cfg.InputExtensions)
                {
                    string cleanExt = ext.StartsWith('.') ? ext : "." + ext;
                    try
                    {
                        files.AddRange(dirInfo.EnumerateFiles("*" + cleanExt, safeEnumOpts));
                    }
                    catch (Exception ex)
                    {
                        ctx.Log("ERROR", $"Failed enumerating '{cleanExt}' in {inPath}: {ex.Message}");
                    }
                }

                files = files.Where(f => !f.Name.Contains(".partial.", StringComparison.OrdinalIgnoreCase)).ToList();
                ctx.Log("INFO", $"Found {files.Count} candidate file(s) in '{label}'.");

                int number = 0;
                DateTime lastSwapSweepUtc = DateTime.UtcNow;
                foreach (var foundFile in files)
                {
                    ct.ThrowIfCancellationRequested();
                    await ctx.WaitIfQueuePausedAsync(ct);

                    number++;
                    double pct = files.Count > 0 ? (number - 1) * 100.0 / files.Count : 0;
                    ctx.UpdateModuleProgress(pct, $"[{label}] {number}/{files.Count}: {foundFile.Name}");

                    if (foundFile.Length == 0)
                    {
                        ctx.Log("WARN", $"[{number}/{files.Count}] Source file '{foundFile.Name}' is 0 bytes (empty/corrupted stub). Skipping.");
                        ctx.Stats.Skipped++;
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(cfg.SkipFilesContainingTag) &&
                        foundFile.Name.Contains(cfg.SkipFilesContainingTag, StringComparison.OrdinalIgnoreCase))
                    {
                        ctx.Log("SKIP", $"[{number}/{files.Count}] Matches skip tag '{cfg.SkipFilesContainingTag}', skipping: {foundFile.Name}");
                        ctx.Stats.Skipped++;
                        continue;
                    }

                    if (!await ctx.NetworkSafety.EnsureDirectoryReachableAsync(outPath, $"Archive Destination ({label})", createIfRootExists: true, ct))
                    {
                        ctx.Stats.Errors++;
                        continue;
                    }

                    string finalFile = Path.Combine(outPath, Path.GetFileNameWithoutExtension(foundFile.Name) + outExt);

                    if (NetworkSafetyService.PurgeZeroByteFileIfExists(finalFile))
                    {
                        ctx.Log("WARN", $"  -> Purged 0-byte invalid archive stub before re-encoding: {Path.GetFileName(finalFile)}");
                    }

                    if (File.Exists(finalFile))
                    {
                        ctx.Log("SKIP", $"[{number}/{files.Count}] Archive exists, skipping: {Path.GetFileName(finalFile)}");
                        ctx.Stats.Skipped++;
                        continue;
                    }

                    if (ctx.IsDryRun)
                    {
                        ctx.Log("DRYRUN", $"[Dry Run] Would archive ({profile.Name}): '{foundFile.FullName}' -> '{finalFile}'");
                        ctx.Stats.Processed++;
                        continue;
                    }

                    var swapCheck = ctx.NetworkSafety.TryResolveOrRecoverPendingEncode(finalFile, ctx.Log);
                    if (swapCheck == PendingSwapCheckResult.Committed)
                    {
                        ctx.Log("SUCCESS", $"[Recovered] Committed archive: {Path.GetFileName(finalFile)} without re-encoding!");
                        ctx.Stats.Processed++;
                        continue;
                    }
                    if (swapCheck == PendingSwapCheckResult.PendingLocked)
                    {
                        ctx.Log("SKIP", $"[Skip] Archive encode is already complete and queued in pending swaps. Skipping re-encode.");
                        ctx.Stats.Skipped++;
                        continue;
                    }

                    if (!ctx.NetworkSafety.TryAcquireDistributedLock(finalFile, out var lockHandle, out var conflictMsg))
                    {
                        ctx.Log("WARN", $"[Skip] {conflictMsg} Target: '{Path.GetFileName(finalFile)}'");
                        ctx.Stats.Skipped++;
                        continue;
                    }

                    using (lockHandle)
                    {
                        string workingOutput = ctx.NetworkSafety.GetSafeEncodeOutputPath(finalFile);
                        if (ctx.NetworkSafety.PendingSwaps != null && ctx.NetworkSafety.PendingSwaps.IsPending(workingOutput))
                        {
                            ctx.Log("WARN", $"[Skip] Working output '{Path.GetFileName(workingOutput)}' is already registered in pending swaps. Skipping to protect completed encode.");
                            ctx.Stats.Skipped++;
                            continue;
                        }

                        ctx.Log("WORK", $"[{number}/{files.Count}] Compressing archive ({profile.Name}): {finalFile}");

                        string hbArgs = profile.BuildHandBrakeArguments(foundFile.FullName, workingOutput);
                        string cmd = $"\"{hbExe}\" {hbArgs}";

                        var runResult = await ctx.ProcessRunner.RunCommandAsync(
                            cmd,
                            useExternalCmdWindow: ctx.ResolveUseExternalCmd(cfg),
                            workingDirectory: outPath,
                            belowNormalPriority: true,
                            logOutputToTerminal: true,
                            ct);

                        if (ct.IsCancellationRequested || runResult.ExitCode == -999)
                        {
                            ctx.NetworkSafety.DiscardPartialEncode(workingOutput, finalFile);
                            ctx.Log("WARN", $"Archive encoding cancelled by user for '{foundFile.Name}'");
                        }
                        else if (runResult.ExitCode == 0 && ctx.NetworkSafety.CommitPartialEncode(workingOutput, finalFile))
                        {
                            if (File.Exists(finalFile))
                            {
                                ctx.Log("SUCCESS", $"Created archive: {Path.GetFileName(finalFile)}");
                            }
                            else
                            {
                                ctx.Log("WARN", $"[Queued Swap] Completed archive encode '{Path.GetFileName(finalFile)}' preserved and queued for background swap");
                            }
                            ctx.Stats.Processed++;
                        }
                        else
                        {
                            ctx.NetworkSafety.DiscardPartialEncode(workingOutput, finalFile);
                            ctx.Log("ERROR", $"Failed archiving '{foundFile.Name}' (Exit Code: {runResult.ExitCode})");
                            ctx.Stats.Errors++;
                        }
                    }

                    if (ctx.NetworkSafety.PendingSwaps != null &&
                        ctx.NetworkSafety.PendingSwaps.Count > 0 &&
                        DateTime.UtcNow - lastSwapSweepUtc >= TimeSpan.FromMinutes(2))
                    {
                        lastSwapSweepUtc = DateTime.UtcNow;
                        ctx.NetworkSafety.PendingSwaps.SweepAndCommitAll(ctx.Log);
                    }
                }
            }

            ctx.UpdateModuleProgress(100, "Video Archive Compressor complete");
        }
    }
}
