using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Models;

namespace Housekeeping.Services.Modules
{
    public static class CloudSyncModule
    {
        private static readonly Regex AnsiEscapeRegex = new(
            @"\x1B\[[0-9;?]*[a-zA-Z]",
            RegexOptions.Compiled);

        private static readonly Regex PriorBisyncLockRegex = new(
            @"prior lock file found:\s*(?<path>.+?\.lck)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex LockerPidRegex = new(
            @"(?:PID of locker:|by PID)\s*(?<pid>\d+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex LockExpiresRegex = new(
            @"(?:Lock file expires:\s*|lock expires in [^\r\n(]*\()(?<year>\d{4})/",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string StripAnsiCodes(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;
            return AnsiEscapeRegex.Replace(input, string.Empty);
        }

        public static string BuildRcloneCommand(string rcloneExe, CloudSyncJobItem job, bool forceResync)
        {
            string mode = string.IsNullOrWhiteSpace(job.SyncMode) ? "bisync" : job.SyncMode.Trim().ToLowerInvariant();
            string userFlags = (job.Flags ?? "").Trim();

            string maxLockFlag = (mode == "bisync" && !userFlags.Contains("--max-lock", StringComparison.OrdinalIgnoreCase))
                ? " --max-lock 2h"
                : "";
            string resyncFlag = (forceResync && mode == "bisync" && !userFlags.Contains("--resync", StringComparison.OrdinalIgnoreCase))
                ? " --resync"
                : "";
            string flagsSegment = string.IsNullOrEmpty(userFlags) ? "" : $" {userFlags}";

            return $"\"{rcloneExe}\" {mode} \"{job.LocalPath}\" {job.RemoteTarget}{flagsSegment}{maxLockFlag}{resyncFlag}".Trim();
        }

        public static bool TryExtractStaleBisyncLockInfo(
            ProcessRunResult? result,
            out string lockFilePath,
            out int? lockerPid,
            out int? expiresYear)
        {
            lockFilePath = "";
            lockerPid = null;
            expiresYear = null;
            if (result == null)
                return false;

            foreach (string rawLine in result.StdErrLines.Concat(result.StdOutLines))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                    continue;

                string line = StripAnsiCodes(rawLine).Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (string.IsNullOrEmpty(lockFilePath))
                {
                    var match = PriorBisyncLockRegex.Match(line);
                    if (match.Success)
                    {
                        string candidate = new string(match.Groups["path"].Value
                            .Trim()
                            .Trim('"', '\'')
                            .Where(c => c >= 32)
                            .ToArray());

                        if (candidate.EndsWith(".lck", StringComparison.OrdinalIgnoreCase) &&
                            candidate.Contains("bisync", StringComparison.OrdinalIgnoreCase))
                        {
                            lockFilePath = candidate;
                        }
                    }
                }

                if (lockerPid == null)
                {
                    var pidMatch = LockerPidRegex.Match(line);
                    if (pidMatch.Success && int.TryParse(pidMatch.Groups["pid"].Value, out int parsedPid))
                    {
                        lockerPid = parsedPid;
                    }
                }

                if (expiresYear == null)
                {
                    var expMatch = LockExpiresRegex.Match(line);
                    if (expMatch.Success && int.TryParse(expMatch.Groups["year"].Value, out int parsedYear))
                    {
                        expiresYear = parsedYear;
                    }
                }
            }

            return !string.IsNullOrEmpty(lockFilePath);
        }

        public static bool TryExtractStaleBisyncLockPath(ProcessRunResult? result, out string lockFilePath)
        {
            return TryExtractStaleBisyncLockInfo(result, out lockFilePath, out _, out _);
        }

        public static bool HasActiveRcloneProcesses()
        {
            Process[] procs = Process.GetProcessesByName("rclone");
            try
            {
                foreach (var p in procs)
                {
                    try
                    {
                        if (!p.HasExited)
                            return true;
                    }
                    catch (Exception ex)
                    {
                        // Access denied or race during exit — treat conservatively as active if handle still exists
                        Debug.WriteLine($"[CloudSyncModule] HasExited check warning: {ex.Message}");
                        return true;
                    }
                }
                return false;
            }
            finally
            {
                foreach (var p in procs)
                {
                    try { p.Dispose(); } catch (Exception ex) { Debug.WriteLine($"[CloudSyncModule] Dispose ignored: {ex.Message}"); }
                }
            }
        }

        public static bool TryRecoverOrphanedBisyncLock(
            ProcessRunResult? failedResult,
            Func<bool>? hasActiveRcloneProcess,
            Action<string, string>? log,
            out string recoveredLockPath)
        {
            recoveredLockPath = "";
            if (!TryExtractStaleBisyncLockPath(failedResult, out string lockPath))
                return false;

            if (!File.Exists(lockPath))
                return false;

            bool rcloneRunning = hasActiveRcloneProcess != null
                ? hasActiveRcloneProcess()
                : HasActiveRcloneProcesses();

            if (rcloneRunning)
            {
                log?.Invoke("WARN", $"rclone bisync lock '{Path.GetFileName(lockPath)}' is held by an active rclone process; skipping lock cleanup.");
                return false;
            }

            try
            {
                File.Delete(lockPath);
                recoveredLockPath = lockPath;
                log?.Invoke("WARN", $"Removed orphaned rclone bisync lock '{Path.GetFileName(lockPath)}' (no active rclone process found).");
                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke("WARN", $"Unable to remove orphaned rclone bisync lock '{Path.GetFileName(lockPath)}': {ex.Message}");
                return false;
            }
        }

        public static async Task RunAsync(ModuleExecutionContext ctx, bool forceResync, CancellationToken ct)
        {
            var cfg = ctx.ActiveCloudConfig;
            var activeJobs = cfg.Jobs
                .Where(j => j.Enabled && !string.IsNullOrWhiteSpace(j.LocalPath) && !string.IsNullOrWhiteSpace(j.RemoteTarget))
                .ToList();

            ctx.Log("HEADER", $"=== Starting Cloud Folder Sync (rclone) ({activeJobs.Count} job(s)) ===");

            if (activeJobs.Count == 0)
            {
                ctx.Log("WARN", "No enabled Cloud Folder Sync jobs configured. Add sync jobs in Module Configurations -> 5. Cloud Folder Sync.");
                return;
            }

            string rcloneExe = ctx.Dependencies.ResolveRclone();

            int idx = 0;
            foreach (var job in activeJobs)
            {
                ct.ThrowIfCancellationRequested();
                await ctx.WaitIfQueuePausedAsync(ct);

                idx++;
                ctx.UpdateModuleProgress((idx - 1) * 100.0 / activeJobs.Count, $"Syncing '{job.JobName}' ({idx}/{activeJobs.Count})...");

                if (!await ctx.NetworkSafety.EnsureDirectoryReachableAsync(job.LocalPath, $"CloudSync ({job.JobName})", createIfRootExists: false, ct))
                {
                    ctx.Stats.Errors++;
                    continue;
                }

                string mode = string.IsNullOrWhiteSpace(job.SyncMode) ? "bisync" : job.SyncMode.Trim().ToLowerInvariant();
                string cmd = BuildRcloneCommand(rcloneExe, job, forceResync);

                if (ctx.IsDryRun)
                {
                    ctx.Log("DRYRUN", $"[Dry Run] Would execute rclone job '{job.JobName}': {cmd} --dry-run");
                    ctx.Stats.Processed++;
                    continue;
                }

                ctx.Log("WORK", $"[{idx}/{activeJobs.Count}] Running rclone {mode} for '{job.JobName}'...");
                bool useExternal = ctx.ResolveUseExternalCmd(cfg);
                var res = await ctx.ProcessRunner.RunCommandAsync(
                    cmd,
                    useExternalCmdWindow: useExternal,
                    workingDirectory: job.LocalPath,
                    belowNormalPriority: false,
                    logOutputToTerminal: true,
                    ct);

                if (mode == "bisync" && !ct.IsCancellationRequested && res.ExitCode != 0 && res.ExitCode != -999 &&
                    TryRecoverOrphanedBisyncLock(res, hasActiveRcloneProcess: null, ctx.Log, out _))
                {
                    ctx.Log("WORK", $"[{idx}/{activeJobs.Count}] Retrying rclone bisync for '{job.JobName}' after orphaned lock cleanup...");
                    res = await ctx.ProcessRunner.RunCommandAsync(
                        cmd,
                        useExternalCmdWindow: useExternal,
                        workingDirectory: job.LocalPath,
                        belowNormalPriority: false,
                        logOutputToTerminal: true,
                        ct);
                }

                if (ct.IsCancellationRequested || res.ExitCode == -999)
                {
                    ctx.Log("WARN", $"Cloud sync job '{job.JobName}' was cancelled.");
                }
                else if (res.ExitCode == 0)
                {
                    ctx.Log("SUCCESS", $"Cloud sync job '{job.JobName}' completed successfully.");
                    ctx.Stats.Processed++;
                }
                else
                {
                    ctx.Log("ERROR", $"Cloud sync job '{job.JobName}' exited with code {res.ExitCode}.");
                    ctx.Stats.Errors++;
                }
            }

            ctx.UpdateModuleProgress(100, "Cloud Folder Sync complete");
        }
    }

    public static class HumbleSyncModule
    {
        public static async Task RunAsync(ModuleExecutionContext ctx, CancellationToken ct)
        {
            var cfg = ctx.ActiveHumbleConfig;
            ctx.Log("HEADER", "=== Starting Humble Bundle Library Sync ===");

            if (string.IsNullOrWhiteSpace(cfg.LibraryPath) || string.IsNullOrWhiteSpace(cfg.SessionCookie))
            {
                ctx.Log("WARN", "Humble Sync requires both a Session Cookie and a Target Library Path. Configure them in Module Configurations -> 6. Humble Bundle Sync.");
                return;
            }

            await ctx.WaitIfQueuePausedAsync(ct);

            if (!await ctx.NetworkSafety.EnsureDirectoryReachableAsync(cfg.LibraryPath, "HumbleSync", createIfRootExists: true, ct))
            {
                ctx.Stats.Errors++;
                return;
            }

            string hbdExe = ctx.Dependencies.ResolveHumbleDownloader();
            string cmd = $"\"{hbdExe}\" -s \"{cfg.SessionCookie}\" --library-path \"{cfg.LibraryPath}\" {cfg.AdditionalArgs}".Trim();

            if (ctx.IsDryRun)
            {
                ctx.Log("DRYRUN", $"[Dry Run] Would execute Humble Bundle sync into '{cfg.LibraryPath}'");
                ctx.Stats.Processed++;
                return;
            }

            ctx.UpdateModuleProgress(15, "Running Humble Bundle Downloader (hbd)...");
            var res = await ctx.ProcessRunner.RunCommandAsync(
                cmd,
                useExternalCmdWindow: ctx.ResolveUseExternalCmd(cfg),
                workingDirectory: cfg.LibraryPath,
                belowNormalPriority: false,
                logOutputToTerminal: true,
                ct);

            if (ct.IsCancellationRequested || res.ExitCode == -999)
            {
                ctx.Log("WARN", "Humble Bundle sync was cancelled.");
            }
            else if (res.ExitCode == 0)
            {
                ctx.Log("SUCCESS", "Humble Bundle sync finished successfully.");
                ctx.Stats.Processed++;
            }
            else
            {
                ctx.Log("ERROR", $"Humble Bundle sync exited with code {res.ExitCode}.");
                ctx.Stats.Errors++;
            }

            ctx.UpdateModuleProgress(100, "Humble Sync complete");
        }
    }

    public static class CustomScriptsModule
    {
        public static async Task RunAsync(ModuleExecutionContext ctx, CancellationToken ct)
        {
            var cfg = ctx.ActiveCustomConfig;
            var activeTasks = cfg.Tasks.Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.CommandOrScriptPath)).ToList();

            ctx.Log("HEADER", $"=== Starting Custom Scripts & CLI Tasks ({activeTasks.Count} enabled) ===");

            if (activeTasks.Count == 0)
            {
                ctx.Log("INFO", "No custom scripts currently enabled.");
                return;
            }

            int idx = 0;
            foreach (var task in activeTasks)
            {
                ct.ThrowIfCancellationRequested();
                await ctx.WaitIfQueuePausedAsync(ct);

                idx++;
                ctx.UpdateModuleProgress((idx - 1) * 100.0 / activeTasks.Count, $"Running '{task.Name}' ({idx}/{activeTasks.Count})...");
                ctx.Log("INFO", $"[Custom Script {idx}/{activeTasks.Count}] {task.Name}");

                if (!string.IsNullOrWhiteSpace(task.RequiredPathCheck))
                {
                    if (!await ctx.NetworkSafety.EnsureDirectoryReachableAsync(task.RequiredPathCheck, task.Name, createIfRootExists: false, ct))
                    {
                        ctx.Stats.Errors++;
                        continue;
                    }
                }

                string cmd = string.IsNullOrWhiteSpace(task.Arguments)
                    ? $"\"{task.CommandOrScriptPath}\""
                    : $"\"{task.CommandOrScriptPath}\" {task.Arguments}";

                if (ctx.IsDryRun)
                {
                    ctx.Log("DRYRUN", $"[Dry Run] Would execute custom task '{task.Name}': {cmd}");
                    ctx.Stats.Processed++;
                    continue;
                }

                bool useExternal = task.RunInExternalCmd || ctx.ResolveUseExternalCmd(cfg);
                var res = await ctx.ProcessRunner.RunCommandAsync(
                    cmd,
                    useExternalCmdWindow: useExternal,
                    workingDirectory: task.WorkingDirectory,
                    belowNormalPriority: false,
                    logOutputToTerminal: true,
                    ct);

                if (ct.IsCancellationRequested || res.ExitCode == -999)
                {
                    ctx.Log("WARN", $"Custom task '{task.Name}' was cancelled.");
                }
                else if (res.ExitCode == 0)
                {
                    ctx.Log("SUCCESS", $"Custom task '{task.Name}' completed.");
                    ctx.Stats.Processed++;
                }
                else
                {
                    ctx.Log("ERROR", $"Custom task '{task.Name}' exited with code {res.ExitCode}.");
                    ctx.Stats.Errors++;
                }
            }

            ctx.UpdateModuleProgress(100, "Custom Scripts complete");
        }
    }
}
