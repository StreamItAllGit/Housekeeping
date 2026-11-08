using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Models;

namespace Housekeeping.Services.Modules
{
    public static class ProxyGeneratorModule
    {
        public static async Task RunAsync(ModuleExecutionContext ctx, CancellationToken ct)
        {
            var cfg = ctx.ActiveProxyConfig;
            var activeFolders = cfg.Folders.Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Path)).ToList();

            ctx.Log("HEADER", $"=== Starting Video Proxy Generator ({activeFolders.Count} folder(s)) ===");

            if (activeFolders.Count == 0)
            {
                ctx.Log("WARN", "No target folders are configured or enabled for Video Proxy Generator. Add folders in Module Configurations -> 1. Video Proxy Generator.");
                return;
            }

            string hbExe = ctx.Dependencies.ResolveHandBrakeCli();
            var profile = ctx.ConfigService.ResolveHandBrakeProfile(cfg.HandBrakeProfileId, "Proxy_360p");
            string outExt = profile.ContainerExtension.StartsWith('.') ? profile.ContainerExtension : "." + profile.ContainerExtension;
            string proxyMarker = $" ({cfg.ProxySuffixTag})";

            int folderIdx = 0;
            foreach (var folderItem in activeFolders)
            {
                ct.ThrowIfCancellationRequested();
                await ctx.WaitIfQueuePausedAsync(ct);

                folderIdx++;
                string path = folderItem.Path.Trim();
                ctx.Log("INFO", $"[Proxy Folder {folderIdx}/{activeFolders.Count}] Scanning: {path}");

                if (!await ctx.NetworkSafety.EnsureDirectoryReachableAsync(path, "ProxyGenerator", createIfRootExists: false, ct))
                {
                    ctx.Stats.Errors++;
                    continue;
                }

                var dirInfo = new DirectoryInfo(path);
                var files = new List<FileInfo>();
                var safeEnumOpts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };

                foreach (var ext in cfg.Extensions)
                {
                    string cleanExt = ext.StartsWith('.') ? ext : "." + ext;
                    try
                    {
                        files.AddRange(dirInfo.EnumerateFiles("*" + cleanExt, safeEnumOpts));
                    }
                    catch (Exception ex)
                    {
                        ctx.Log("ERROR", $"Failed enumerating '{cleanExt}' in {path}: {ex.Message}");
                    }
                }

                files = files.Where(f => !f.Name.Contains(".partial.", StringComparison.OrdinalIgnoreCase)).ToList();
                ctx.Log("INFO", $"Found {files.Count} media file(s) in '{dirInfo.Name}'.");

                int number = 0;
                DateTime lastSwapSweepUtc = DateTime.UtcNow;
                foreach (var foundFile in files)
                {
                    ct.ThrowIfCancellationRequested();
                    await ctx.WaitIfQueuePausedAsync(ct);

                    number++;
                    double pct = files.Count > 0 ? (number - 1) * 100.0 / files.Count : 0;
                    ctx.UpdateModuleProgress(pct, $"[{dirInfo.Name}] {number}/{files.Count}: {foundFile.Name}");

                    if (foundFile.Length == 0)
                    {
                        ctx.Log("WARN", $"[{number}/{files.Count}] Source file '{foundFile.Name}' is 0 bytes (empty/corrupted stub). Skipping.");
                        ctx.Stats.Skipped++;
                        continue;
                    }

                    if (foundFile.Name.Contains(proxyMarker, StringComparison.OrdinalIgnoreCase) ||
                        foundFile.Name.Contains("_proxy)", StringComparison.OrdinalIgnoreCase))
                    {
                        ctx.Log("SKIP", $"[{number}/{files.Count}] Is proxy file, skipping: {foundFile.Name}");
                        ctx.Stats.Skipped++;
                        continue;
                    }

                    string fileDir = foundFile.DirectoryName ?? path;
                    if (!await ctx.NetworkSafety.EnsureDirectoryReachableAsync(fileDir, "ProxyGenerator", createIfRootExists: false, ct))
                    {
                        ctx.Stats.Errors++;
                        continue;
                    }

                    string finalFile = Path.Combine(
                        fileDir,
                        $"{Path.GetFileNameWithoutExtension(foundFile.Name)}{proxyMarker}{outExt}");

                    if (NetworkSafetyService.PurgeZeroByteFileIfExists(finalFile))
                    {
                        ctx.Log("WARN", $"  -> Purged 0-byte invalid proxy stub before re-encoding: {Path.GetFileName(finalFile)}");
                    }

                    if (File.Exists(finalFile))
                    {
                        ctx.Log("SKIP", $"[{number}/{files.Count}] Proxy exists, skipping: {Path.GetFileName(finalFile)}");
                        ctx.Stats.Skipped++;
                        continue;
                    }

                    if (ctx.IsDryRun)
                    {
                        ctx.Log("DRYRUN", $"[Dry Run] Would encode proxy ({profile.Name}): {finalFile}");
                        ctx.Stats.Processed++;
                        continue;
                    }

                    var swapCheck = ctx.NetworkSafety.TryResolveOrRecoverPendingEncode(finalFile, ctx.Log);
                    if (swapCheck == PendingSwapCheckResult.Committed)
                    {
                        ctx.Log("SUCCESS", $"[Recovered] Committed proxy: {Path.GetFileName(finalFile)} without re-encoding!");
                        ctx.Stats.Processed++;
                        continue;
                    }
                    if (swapCheck == PendingSwapCheckResult.PendingLocked)
                    {
                        ctx.Log("SKIP", $"[Skip] Proxy encode is already complete and queued in pending swaps. Skipping re-encode.");
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

                        ctx.Log("WORK", $"[{number}/{files.Count}] Encoding proxy ({profile.Name}): {finalFile}");

                        string hbArgs = profile.BuildHandBrakeArguments(foundFile.FullName, workingOutput);
                        string cmd = $"\"{hbExe}\" {hbArgs}";

                        var runResult = await ctx.ProcessRunner.RunCommandAsync(
                            cmd,
                            useExternalCmdWindow: ctx.ResolveUseExternalCmd(cfg),
                            workingDirectory: fileDir,
                            belowNormalPriority: true,
                            logOutputToTerminal: true,
                            ct);

                        if (ct.IsCancellationRequested || runResult.ExitCode == -999)
                        {
                            ctx.NetworkSafety.DiscardPartialEncode(workingOutput, finalFile);
                            ctx.Log("WARN", $"Proxy encoding cancelled by user for '{foundFile.Name}'");
                        }
                        else if (runResult.ExitCode == 0 && ctx.NetworkSafety.CommitPartialEncode(workingOutput, finalFile))
                        {
                            if (File.Exists(finalFile))
                            {
                                ctx.Log("SUCCESS", $"Created proxy: {Path.GetFileName(finalFile)}");
                            }
                            else
                            {
                                ctx.Log("WARN", $"[Queued Swap] Completed proxy encode '{Path.GetFileName(finalFile)}' preserved and queued for background swap");
                            }
                            ctx.Stats.Processed++;
                        }
                        else
                        {
                            ctx.NetworkSafety.DiscardPartialEncode(workingOutput, finalFile);
                            ctx.Log("ERROR", $"Failed creating proxy for '{foundFile.Name}' (Exit Code: {runResult.ExitCode})");
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

            ctx.UpdateModuleProgress(100, "Video Proxy Generator complete");
        }
    }
}
