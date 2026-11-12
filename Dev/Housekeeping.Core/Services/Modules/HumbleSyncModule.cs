// @module: Housekeeping.Core/Services/Modules/HumbleSyncModule
// @purpose: Executes Humble Bundle Downloader and Custom CLI task pipelines with Standard 19 .hklock deferral and directory sentinel locking.
// @depends-on: System.Linq, System.Threading, System.Threading.Tasks, Housekeeping.Models, Housekeeping.Services
// @exports: HumbleSyncModule, CustomScriptsModule
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Models;

namespace Housekeeping.Services.Modules
{
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

            if (NetworkSafetyService.HasActiveDirectoryOrParentLock(cfg.LibraryPath, "housekeeping", out string? activeOwner, out string? activeLockFile, scanImmediateSubdirectories: true, ctx.Log))
            {
                ctx.Log("WARN", $"[Coexistence Defer] Deferring Humble Sync on '{cfg.LibraryPath}' — active sentinel lock '{activeLockFile}' held by '{activeOwner}'.");
                ctx.Stats.Skipped++;
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

            using var sentinel = NetworkSafetyService.AcquireDirectorySentinelLock(cfg.LibraryPath, "humble-sync");
            ctx.UpdateModuleProgress(15, "Running Humble Bundle Downloader (hbd)...");
            var res = await ctx.ProcessRunner.RunCommandAsync(
                cmd,
                useExternalCmdWindow: ctx.ResolveUseExternalCmd(cfg),
                workingDirectory: cfg.LibraryPath,
                belowNormalPriority: false,
                logOutputToTerminal: true,
                ct);

            if (ct.IsCancellationRequested || res.ExitCode == -999)
                ctx.Log("WARN", "Humble Bundle sync was cancelled.");
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
                    ctx.Log("WARN", $"Custom task '{task.Name}' was cancelled.");
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
