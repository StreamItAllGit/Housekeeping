// @module: Housekeeping.Core/Services/Modules/CloudSyncModule
// @purpose: Executes rclone cloud sync jobs with Standard 19 .hklock sister-app deferral, directory sentinel locking, and transient exclusion flags.
// @depends-on: System.Diagnostics, System.IO, System.Text.RegularExpressions, Housekeeping.Models, Housekeeping.Services
// @exports: CloudSyncModule
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
        private static readonly Regex AnsiEscapeRegex = new(@"\x1B\[[0-9;?]*[a-zA-Z]", RegexOptions.Compiled);
        private static readonly Regex PriorBisyncLockRegex = new(@"prior lock file found:\s*(?<path>.+?\.lck)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex LockerPidRegex = new(@"(?:PID of locker:|by PID)\s*(?<pid>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex LockExpiresRegex = new(@"(?:Lock file expires:\s*|lock expires in [^\r\n(]*\()(?<year>\d{4})/", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string StripAnsiCodes(string? input) =>
            string.IsNullOrEmpty(input) ? string.Empty : AnsiEscapeRegex.Replace(input, string.Empty);

        public static string BuildRcloneCommand(string rcloneExe, CloudSyncJobItem job, bool forceResync) =>
            BuildRcloneCommand(rcloneExe, job, forceResync, dataDirectory: null, forceContainment: false);

        public static string BuildRcloneCommand(
            string rcloneExe,
            CloudSyncJobItem job,
            bool forceResync,
            string? dataDirectory,
            bool forceContainment = false)
        {
            string mode = string.IsNullOrWhiteSpace(job.SyncMode) ? "bisync" : job.SyncMode.Trim().ToLowerInvariant();
            string userFlags = SisterAppCoexistenceService.EnsureCoexistenceRcloneExcludes(job.Flags);

            var containment = RcloneContainmentHelper.ResolveContainment(dataDirectory, forceContainment);
            if (containment.IsContained)
                userFlags = RcloneContainmentHelper.AppendContainmentFlags(userFlags, containment, mode == "bisync");

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
            if (result == null) return false;

            foreach (string rawLine in result.StdErrLines.Concat(result.StdOutLines))
            {
                if (string.IsNullOrWhiteSpace(rawLine)) continue;
                string line = StripAnsiCodes(rawLine).Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (string.IsNullOrEmpty(lockFilePath))
                {
                    var match = PriorBisyncLockRegex.Match(line);
                    if (match.Success)
                    {
                        string candidate = new string(match.Groups["path"].Value.Trim().Trim('"', '\'').Where(c => c >= 32).ToArray());
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
                        lockerPid = parsedPid;
                }

                if (expiresYear == null)
                {
                    var expMatch = LockExpiresRegex.Match(line);
                    if (expMatch.Success && int.TryParse(expMatch.Groups["year"].Value, out int parsedYear))
                        expiresYear = parsedYear;
                }
            }

            return !string.IsNullOrEmpty(lockFilePath);
        }

        public static bool TryExtractStaleBisyncLockPath(ProcessRunResult? result, out string lockFilePath) =>
            TryExtractStaleBisyncLockInfo(result, out lockFilePath, out _, out _);

        public static bool HasActiveRcloneProcesses()
        {
            Process[] procs = Process.GetProcessesByName("rclone");
            try
            {
                foreach (var p in procs)
                {
                    try
                    {
                        if (!p.HasExited) return true;
                    }
                    catch (Exception ex)
                    {
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
            if (!TryExtractStaleBisyncLockPath(failedResult, out string lockPath) || !File.Exists(lockPath))
                return false;

            bool rcloneRunning = hasActiveRcloneProcess != null ? hasActiveRcloneProcess() : HasActiveRcloneProcesses();
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

                if (NetworkSafetyService.HasActiveDirectoryOrParentLock(job.LocalPath, "housekeeping", out string? activeOwner, out string? activeLockFile, scanImmediateSubdirectories: true, ctx.Log))
                {
                    ctx.Log("WARN", $"[Coexistence Defer] Deferring Cloud Sync job '{job.JobName}' on '{job.LocalPath}' — active sentinel lock '{activeLockFile}' held by '{activeOwner}'.");
                    ctx.Stats.Skipped++;
                    continue;
                }

                string mode = string.IsNullOrWhiteSpace(job.SyncMode) ? "bisync" : job.SyncMode.Trim().ToLowerInvariant();
                var containment = RcloneContainmentHelper.ResolveContainment(ctx.ConfigService.DataDirectory, ctx.ConfigService.IsIsolatedOverride);
                string cmd = BuildRcloneCommand(rcloneExe, job, forceResync, ctx.ConfigService.DataDirectory, ctx.ConfigService.IsIsolatedOverride);

                if (ctx.IsDryRun)
                {
                    ctx.Log("DRYRUN", $"[Dry Run] Would execute rclone job '{job.JobName}': {cmd} --dry-run");
                    ctx.Stats.Processed++;
                    continue;
                }

                if (containment.IsContained)
                    RcloneContainmentHelper.EnsureContainmentDirectories(containment);

                var rcloneEnv = containment.IsContained
                    ? RcloneContainmentHelper.BuildEnvironmentVariables(containment)
                    : null;

                using var sentinel = NetworkSafetyService.AcquireDirectorySentinelLock(job.LocalPath, $"cloud-sync-{mode}");
                ctx.Log("WORK", $"[{idx}/{activeJobs.Count}] Running rclone {mode} for '{job.JobName}'...");
                bool useExternal = ctx.ResolveUseExternalCmd(cfg);
                var res = await ctx.ProcessRunner.RunCommandAsync(cmd, useExternal, job.LocalPath, belowNormalPriority: false, logOutputToTerminal: true, ct, rcloneEnv);

                if (mode == "bisync" && !ct.IsCancellationRequested && res.ExitCode != 0 && res.ExitCode != -999 &&
                    TryRecoverOrphanedBisyncLock(res, hasActiveRcloneProcess: null, ctx.Log, out _))
                {
                    ctx.Log("WORK", $"[{idx}/{activeJobs.Count}] Retrying rclone bisync for '{job.JobName}' after orphaned lock cleanup...");
                    res = await ctx.ProcessRunner.RunCommandAsync(cmd, useExternal, job.LocalPath, belowNormalPriority: false, logOutputToTerminal: true, ct, rcloneEnv);
                }

                if (ct.IsCancellationRequested || res.ExitCode == -999)
                    ctx.Log("WARN", $"Cloud sync job '{job.JobName}' was cancelled.");
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
}
