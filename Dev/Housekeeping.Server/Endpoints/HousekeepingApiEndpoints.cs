using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Models;
using Housekeeping.Server.Hosting;
using Housekeeping.Server.Security;
using Housekeeping.Server.Services;
using Housekeeping.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace Housekeeping.Server.Endpoints
{
    public sealed record PasswordPayload(string? Password, string? CurrentPassword, string? NewPassword);
    public sealed record RunQueuePayload(string? QueueId, bool IsDryRun = false, bool ForceCloudResync = false, bool EnqueueIfBusy = true);
    public sealed record RunJobPayload(bool IsDryRun = false, bool ForceCloudResync = false, bool EnqueueIfBusy = true);
    public sealed record PostPowerPayload(string? Action);
    public sealed record TerminalExecPayload(string? Command);
    public sealed record CreateJobPayload(ModuleId ModuleType, string? Name, string? QueueId);
    public sealed record ReorderIdsPayload(List<string>? OrderedIds);
    public sealed record RegexTestPayload(string? SampleInput, List<ManifestFieldParseRule>? Rules);
    public sealed record RomSyncPreviewPayload(RomLauncherGeneratorConfig? RomConfig, ConsoleCollectionRule? ConsoleOverride);
    public sealed record ReportsBatchIdsPayload(List<string>? ReportIds, bool Ignored = true);
    public sealed record CleanArchivePayload(List<string>? ReportIds, List<string>? ExplicitInternalPaths);
    public sealed record HashVerifierPayload(string? JobId, ModuleId SourceModule = ModuleId.RomLauncherGenerator, string? VerifyMode = null);
    public sealed record NativePickerPayload(string? InitialPath, string? Title, string? Filter);
    public sealed record RevealPathPayload(string? Path);
    public sealed record GreenroomSyncPayload(ModuleId SourceModule = ModuleId.RomLauncherGenerator);
    public sealed record WatchdogPayload(bool Enable, int IntervalMinutes = 15);
    public sealed record RemoteNodeActionPayload(string? Action, string? QueueId, string? JobId);
    public sealed record ProfileImportPayload(string? Json);
    public sealed record ResumeInterruptedPayload(bool RestartFullQueue = false);
    public sealed record DeferSchedulerPayload(int Minutes = 15);

    public static partial class HousekeepingApiEndpoints
    {
        public static void MapHousekeepingApi(this WebApplication app)
        {
            var api = app.MapGroup("/api/v1");
            app.MapCoexistenceAndControlEndpoints(api);
            app.MapSystemDoctorAndBackupEndpoints(api);

            // ==================== MOTHER DISK ARBITRATION HOOKS ==================== //
            var getSchedulerStatus = (HousekeepingRuntimeContainer rt) =>
            {
                var snapshot = rt.Orchestrator.GetStateSnapshot();
                return Results.Ok(new
                {
                    app = "housekeeping",
                    version = RemoteNodeDeploymentService.GetLocalAppVersion(),
                    isRunning = snapshot.IsRunning,
                    isPaused = snapshot.IsPaused,
                    activeJobName = snapshot.ActiveJobName,
                    activeQueueId = snapshot.ActiveQueueId,
                    deferredUntilUtc = rt.Orchestrator.Scheduler.DeferredUntilUtc,
                    isDiskHeavyJobActive = snapshot.IsRunning,
                    globalNextRunStatusText = snapshot.GlobalNextRunStatusText,
                    globalNextRunUtc = snapshot.GlobalNextRunUtc,
                    timestampUtc = DateTime.UtcNow
                });
            };

            var postSchedulerDefer = (DeferSchedulerPayload? body, HousekeepingRuntimeContainer rt) =>
            {
                int minutes = body?.Minutes ?? 15;
                if (minutes <= 0)
                {
                    rt.Orchestrator.Scheduler.ClearDeferral();
                    rt.Orchestrator.Log("INFO", "[Scheduler] Schedule deferral cleared by external coordinator.");
                    return Results.Ok(new
                    {
                        ok = true,
                        app = "housekeeping",
                        deferredMinutes = 0,
                        deferredUntilUtc = (DateTime?)null,
                        message = "Schedule deferral cleared."
                    });
                }

                rt.Orchestrator.Scheduler.DeferSchedules(TimeSpan.FromMinutes(minutes));
                var until = rt.Orchestrator.Scheduler.DeferredUntilUtc;
                rt.Orchestrator.Log("INFO", $"[Scheduler] Schedules deferred for {minutes}m (until {until:yyyy-MM-dd HH:mm:ss} UTC) by disk arbitration.");
                return Results.Ok(new
                {
                    ok = true,
                    app = "housekeeping",
                    deferredMinutes = minutes,
                    deferredUntilUtc = until,
                    message = $"Schedules deferred until {until:yyyy-MM-dd HH:mm:ss} UTC."
                });
            };

            app.MapGet("/api/scheduler/status", getSchedulerStatus);
            app.MapPost("/api/scheduler/defer", postSchedulerDefer);
            api.MapGet("/scheduler/status", getSchedulerStatus);
            api.MapPost("/scheduler/defer", postSchedulerDefer);

            // ==================== 1. HEALTH & BOOTSTRAP ==================== //
            api.MapGet("/health", (HousekeepingRuntimeContainer rt) => Results.Ok(new
            {
                status = "ok",
                version = RemoteNodeDeploymentService.GetLocalAppVersion(),
                machineName = Environment.MachineName,
                isRunning = rt.Orchestrator.IsRunning,
                isTestMode = rt.ConfigService.IsTestMode,
                isSandboxed = rt.ConfigService.IsSandboxed,
                forceDryRun = rt.Orchestrator.ForceDryRun,
                timestampUtc = DateTime.UtcNow
            }));

            // ==================== 2. AUTHENTICATION & SECURITY ==================== //
            api.MapGet("/auth/status", (HttpContext ctx, TieredAuthService auth, HousekeepingRuntimeContainer rt) =>
            {
                var eval = ctx.Items[TieredAuthMiddleware.AuthResultItemKey] as AuthEvaluationResult ?? auth.EvaluateRequest(ctx);
                var ws = rt.ConfigService.Config.WebServer;
                return Results.Ok(new
                {
                    isAuthenticated = eval.IsAuthenticated,
                    isDirectLoopback = eval.IsDirectLoopback,
                    isTailscaleServe = eval.IsTailscaleServe,
                    requiresInitialPasswordSetup = !auth.IsPasswordConfigured(),
                    authMethod = eval.AuthMethod,
                    identity = eval.Identity,
                    restrictRawShellToLocalhost = ws.RestrictRawShellToLocalhost,
                    rawShellBlockedForCaller = !eval.IsDirectLoopback && ws.RestrictRawShellToLocalhost,
                    nativeDialogsAvailable = eval.IsDirectLoopback && rt.NativeDialogs.IsAvailable,
                    machineName = Environment.MachineName,
                    version = RemoteNodeDeploymentService.GetLocalAppVersion()
                });
            });

            api.MapPost("/auth/setup-password", (HttpContext ctx, [FromBody] PasswordPayload body, TieredAuthService auth) =>
            {
                if (auth.IsPasswordConfigured())
                {
                    return Results.BadRequest(new { error = "Admin password is already configured. Use change-password or sign in." });
                }

                string pwd = body?.Password ?? "";
                if (pwd.Length < 4)
                {
                    return Results.BadRequest(new { error = "Admin password must be at least 4 characters." });
                }

                auth.SetAdminPassword(pwd);
                auth.IssueSessionCookie(ctx.Response, auth.IsEffectiveHttps(ctx), "admin");
                return Results.Ok(new { ok = true, message = "Admin password configured and session established." });
            });

            api.MapPost("/auth/login", (HttpContext ctx, [FromBody] PasswordPayload body, TieredAuthService auth) =>
            {
                if (!auth.IsPasswordConfigured())
                {
                    return Results.BadRequest(new { error = "password_not_configured", requiresSetup = true });
                }

                if (auth.IsLoginRateLimited(ctx, out int retryAfterSec))
                {
                    ctx.Response.Headers.RetryAfter = retryAfterSec.ToString();
                    return Results.Json(new
                    {
                        error = "too_many_attempts",
                        message = $"Too many failed login attempts. Please wait {retryAfterSec}s before trying again."
                    }, statusCode: StatusCodes.Status429TooManyRequests);
                }

                if (!auth.VerifyAdminPassword(body?.Password ?? ""))
                {
                    auth.RecordLoginAttempt(ctx, succeeded: false);
                    return Results.Json(new { error = "Invalid admin password." }, statusCode: StatusCodes.Status401Unauthorized);
                }

                auth.RecordLoginAttempt(ctx, succeeded: true);
                auth.IssueSessionCookie(ctx.Response, auth.IsEffectiveHttps(ctx), "admin");
                return Results.Ok(new { ok = true });
            });

            api.MapPost("/auth/logout", (HttpContext ctx, TieredAuthService auth) =>
            {
                auth.ClearSessionCookie(ctx.Response);
                return Results.Ok(new { ok = true });
            });

            api.MapPost("/auth/change-password", (HttpContext ctx, [FromBody] PasswordPayload body, TieredAuthService auth) =>
            {
                var eval = auth.EvaluateRequest(ctx);
                if (auth.IsPasswordConfigured() && !eval.IsDirectLoopback && !auth.VerifyAdminPassword(body?.CurrentPassword ?? ""))
                {
                    return Results.BadRequest(new { error = "Current admin password is incorrect." });
                }

                string nextPwd = body?.NewPassword ?? body?.Password ?? "";
                if (nextPwd.Length < 4)
                {
                    return Results.BadRequest(new { error = "New password must be at least 4 characters." });
                }

                auth.SetAdminPassword(nextPwd);
                auth.IssueSessionCookie(ctx.Response, auth.IsEffectiveHttps(ctx), "admin");
                return Results.Ok(new { ok = true, message = "Admin password updated." });
            });

            api.MapPost("/auth/rotate-node-token", (HousekeepingRuntimeContainer rt) =>
            {
                string nextToken = "";
                rt.ConfigService.MutateAndSave(cfg =>
                {
                    cfg.WebServer.NodeApiToken = "";
                    cfg.WebServer.EnsureNodeApiTokenInitialized();
                    nextToken = cfg.WebServer.NodeApiToken;
                });
                return Results.Ok(new { nodeApiToken = nextToken });
            });

            // ==================== 3. CONFIG, AUTO-SAVE, SNAPSHOTS & PROFILES ==================== //
            api.MapGet("/config", (HousekeepingRuntimeContainer rt) =>
            {
                var cfg = rt.ConfigService.Config.DeepClone();
                cfg.RomLauncherGenerator.EnsureArchiveTemplatesInitialized();
                foreach (var j in cfg.DefinedJobs.Where(j => j.RomLauncherGenerator != null))
                {
                    j.RomLauncherGenerator!.EnsureArchiveTemplatesInitialized();
                }
                // Do not transmit raw password hash/salt to browser
                cfg.WebServer.AdminPasswordHash = "";
                cfg.WebServer.AdminPasswordSalt = "";
                return Results.Ok(new
                {
                    config = cfg,
                    snapshotsCount = rt.ConfigService.ListSnapshots().Count,
                    consoleCatalog = ConsoleSystemCatalog.AllSystems,
                    archiveTemplatesBuiltIn = ArchiveComplianceCatalog.CreateBuiltInTemplates()
                });
            });

            api.MapPut("/config", (HttpContext ctx, [FromBody] AppConfig updated, TieredAuthService auth, HousekeepingRuntimeContainer rt) =>
            {
                var eval = ctx.Items[TieredAuthMiddleware.AuthResultItemKey] as AuthEvaluationResult ?? auth.EvaluateRequest(ctx);

                // If RestrictRawShellToLocalhost is active and caller is remote, reject edits to CustomScripts or CLI tool binary paths
                if (!eval.IsDirectLoopback && rt.ConfigService.Config.WebServer.RestrictRawShellToLocalhost)
                {
                    if (HasRestrictedExecutionSurfaceChanged(rt.ConfigService.Config, updated))
                    {
                        return Results.Json(new
                        {
                            error = "raw_shell_restricted_to_localhost",
                            message = "Modifying Custom Scripts, CLI tool binary paths, or RestrictRawShellToLocalhost is blocked for remote connections."
                        }, statusCode: StatusCodes.Status403Forbidden);
                    }
                    updated.WebServer.RestrictRawShellToLocalhost = true;
                }

                if (!rt.ConfigService.TryUpdateAndSave(updated, out var errors))
                {
                    return Results.BadRequest(new
                    {
                        error = "validation_failed",
                        errors
                    });
                }

                return Results.Ok(new
                {
                    ok = true,
                    savedAtUtc = DateTime.UtcNow,
                    snapshotsCount = rt.ConfigService.ListSnapshots().Count
                });
            });

            api.MapGet("/config/snapshots", (HousekeepingRuntimeContainer rt) =>
                Results.Ok(rt.ConfigService.ListSnapshots()));

            api.MapPost("/config/snapshots/{snapshotId}/restore", (HttpContext ctx, string snapshotId, TieredAuthService auth, HousekeepingRuntimeContainer rt) =>
            {
                var eval = ctx.Items[TieredAuthMiddleware.AuthResultItemKey] as AuthEvaluationResult ?? auth.EvaluateRequest(ctx);
                if (!eval.IsDirectLoopback && rt.ConfigService.Config.WebServer.RestrictRawShellToLocalhost)
                {
                    return Results.Json(new
                    {
                        error = "raw_shell_restricted_to_localhost",
                        message = "Restoring full configuration snapshots is restricted to localhost while Raw Shell Restriction is enabled."
                    }, statusCode: StatusCodes.Status403Forbidden);
                }

                if (!rt.ConfigService.RestoreSnapshot(snapshotId, out string err))
                {
                    return Results.BadRequest(new { error = err });
                }

                var cfg = rt.ConfigService.Config.DeepClone();
                cfg.WebServer.AdminPasswordHash = "";
                cfg.WebServer.AdminPasswordSalt = "";
                rt.Orchestrator.Log("SUCCESS", $"[Snapshot Restore] Restored settings.json snapshot '{snapshotId}'.");
                return Results.Ok(new { ok = true, config = cfg, snapshots = rt.ConfigService.ListSnapshots() });
            });

            api.MapGet("/profiles/export", ([FromQuery] bool sanitize, HousekeepingRuntimeContainer rt) =>
            {
                string json = rt.ConfigService.ExportProfileJson(stripSensitiveTokens: sanitize);
                string fileName = sanitize ? "Housekeeping_Shareable.hkprofile.json" : "Housekeeping_FullBackup.hkprofile.json";
                return Results.File(Encoding.UTF8.GetBytes(json), "application/json", fileName);
            });

            api.MapPost("/profiles/import", (HttpContext ctx, [FromBody] ProfileImportPayload body, TieredAuthService auth, HousekeepingRuntimeContainer rt) =>
            {
                var eval = ctx.Items[TieredAuthMiddleware.AuthResultItemKey] as AuthEvaluationResult ?? auth.EvaluateRequest(ctx);
                if (!eval.IsDirectLoopback && rt.ConfigService.Config.WebServer.RestrictRawShellToLocalhost)
                {
                    return Results.Json(new
                    {
                        error = "raw_shell_restricted_to_localhost",
                        message = "Importing full configuration profiles is restricted to localhost while Raw Shell Restriction is enabled."
                    }, statusCode: StatusCodes.Status403Forbidden);
                }

                if (string.IsNullOrWhiteSpace(body?.Json))
                    return Results.BadRequest(new { error = "Empty profile JSON." });

                if (!rt.ConfigService.ImportProfileFromJson(body.Json, out string err))
                    return Results.BadRequest(new { error = err });

                rt.Orchestrator.Log("SUCCESS", "[Profile Import] Imported Housekeeping configuration profile.");
                var cfg = rt.ConfigService.Config.DeepClone();
                cfg.WebServer.AdminPasswordHash = "";
                cfg.WebServer.AdminPasswordSalt = "";
                return Results.Ok(new { ok = true, config = cfg });
            });

            // ==================== 4. QUEUES, JOBS, SYNC PREVIEW & REGEX PLAYGROUND ==================== //
            api.MapPost("/jobs/create", ([FromBody] CreateJobPayload body, HousekeepingRuntimeContainer rt) =>
            {
                HousekeepingJob? createdJob = null;
                if (!rt.ConfigService.MutateAndSave(cfg =>
                {
                    cfg.EnsureNamedQueuesInitialized();
                    cfg.EnsureDefinedJobsSynchronized();
                    cfg.SetModuleAppWideEnabled(body.ModuleType, true);

                    var job = ModuleCatalogService.CreateNewJobForModuleType(cfg, body.ModuleType, body.Name);
                    cfg.DefinedJobs.Add(job);

                    var targetQueue = !string.IsNullOrWhiteSpace(body.QueueId)
                        ? cfg.NamedQueues.FirstOrDefault(q => string.Equals(q.Id, body.QueueId, StringComparison.OrdinalIgnoreCase))
                        : cfg.GetActiveQueue();

                    targetQueue ??= cfg.GetActiveQueue();
                    if (!targetQueue.Jobs.Any(j => string.Equals(j.JobId, job.JobId, StringComparison.OrdinalIgnoreCase)))
                    {
                        targetQueue.Jobs.Add(job);
                    }
                    createdJob = job;
                }, out string? err) || createdJob == null)
                {
                    return Results.BadRequest(new { error = err ?? "Failed to create job." });
                }

                rt.Orchestrator.Log("SUCCESS", $"Created job '{createdJob.Name}' ({createdJob.ModuleTypeBadgeText}).");
                return Results.Ok(new { ok = true, job = createdJob });
            });

            api.MapPost("/jobs/{jobId}/clone", (string jobId, HousekeepingRuntimeContainer rt) =>
            {
                var currentSrc = rt.ConfigService.Config.DefinedJobs.FirstOrDefault(j => string.Equals(j.JobId, jobId, StringComparison.OrdinalIgnoreCase));
                if (currentSrc == null)
                    return Results.NotFound(new { error = $"Job '{jobId}' not found." });

                HousekeepingJob? clonedJob = null;
                if (!rt.ConfigService.MutateAndSave(cfg =>
                {
                    var src = cfg.DefinedJobs.FirstOrDefault(j => string.Equals(j.JobId, jobId, StringComparison.OrdinalIgnoreCase));
                    if (src == null) return;
                    var clone = src.DeepClone();
                    clone.JobId = Guid.NewGuid().ToString("N")[..10];
                    clone.Name = $"{src.Name} (Copy)";
                    cfg.DefinedJobs.Add(clone);
                    cfg.GetActiveQueue().Jobs.Add(clone);
                    clonedJob = clone;
                }, out string? err) || clonedJob == null)
                {
                    return Results.BadRequest(new { error = err ?? "Failed to clone job." });
                }

                rt.Orchestrator.Log("INFO", $"Cloned job '{currentSrc.Name}' -> '{clonedJob.Name}'.");
                return Results.Ok(new { ok = true, job = clonedJob });
            });

            api.MapDelete("/jobs/{jobId}", (string jobId, HousekeepingRuntimeContainer rt) =>
            {
                int removed = 0;
                if (!rt.ConfigService.MutateAndSave(cfg =>
                {
                    removed = cfg.DefinedJobs.RemoveAll(j => string.Equals(j.JobId, jobId, StringComparison.OrdinalIgnoreCase));
                    foreach (var q in cfg.NamedQueues)
                    {
                        q.Jobs.RemoveAll(j => string.Equals(j.JobId, jobId, StringComparison.OrdinalIgnoreCase));
                    }
                    foreach (var j in cfg.DefinedJobs.Where(j => string.Equals(j.OnSuccessTriggerJobId, jobId, StringComparison.OrdinalIgnoreCase)))
                    {
                        j.OnSuccessTriggerJobId = "";
                    }
                }, out string? err))
                {
                    return Results.BadRequest(new { error = err ?? "Failed to delete job." });
                }
                return Results.Ok(new { ok = true, removed });
            });

            api.MapPost("/jobs/starter", (HousekeepingRuntimeContainer rt) =>
            {
                int added = ModuleCatalogService.PopulateStarterJobs(rt.ConfigService);
                rt.Orchestrator.Log("INFO", added > 0
                    ? $"Populated {added} starter module job(s)."
                    : "All 8 module types already have at least one defined job.");
                return Results.Ok(new { ok = true, addedCount = added });
            });

            api.MapPost("/rom/sync-preview", ([FromBody] RomSyncPreviewPayload body, HousekeepingRuntimeContainer rt) =>
            {
                var rom = body?.RomConfig ?? rt.Orchestrator.ResolveRomConfig();
                rom.SanitizeAutoGeneratedCommandOverrides();
                string rcloneExe = rt.Dependencies.ResolveRclone();
                var (preCmds, postCmds) = rom.ResolveAllSyncCommands(rcloneExe, body?.ConsoleOverride);
                return Results.Ok(new
                {
                    preLaunchCommands = preCmds,
                    postLaunchCommands = postCmds,
                    preLaunchPreviewText = string.Join(Environment.NewLine, preCmds),
                    postLaunchPreviewText = string.Join(Environment.NewLine, postCmds)
                });
            });

            api.MapPost("/manifest/test-regex", ([FromBody] RegexTestPayload body) =>
            {
                var res = ModuleCatalogService.TestManifestRegexRules(body?.SampleInput, body?.Rules);
                return Results.Ok(res);
            });

            api.MapGet("/manifest/presets/{bundleKey}", (string bundleKey) =>
            {
                var rules = ModuleCatalogService.GetManifestPresetBundle(bundleKey);
                return Results.Ok(rules);
            });

            api.MapGet("/queues/{queueId}/upcoming", (string queueId, HousekeepingRuntimeContainer rt) =>
            {
                var q = rt.ConfigService.Config.NamedQueues.FirstOrDefault(x => string.Equals(x.Id, queueId, StringComparison.OrdinalIgnoreCase));
                if (q == null)
                    return Results.NotFound(new { error = "Queue not found." });

                var upcoming = new List<DateTime>();
                DateTime cursor = DateTime.UtcNow;
                for (int i = 0; i < 5 && q.EnableSchedule; i++)
                {
                    var next = rt.Orchestrator.Scheduler.GetNextOccurrenceUtc(q, cursor);
                    if (!next.HasValue) break;
                    upcoming.Add(next.Value);
                    cursor = next.Value.AddSeconds(61);
                }

                return Results.Ok(new { queueId = q.Id, enableSchedule = q.EnableSchedule, upcomingUtc = upcoming });
            });

            // ==================== 5. MODULES CATALOG & DEPENDENCIES ==================== //
            api.MapGet("/modules", (HousekeepingRuntimeContainer rt) =>
            {
                var catalog = ModuleCatalogService.BuildCatalog(rt.ConfigService.Config, rt.Dependencies);
                var deps = rt.Dependencies.CheckAllDependencies();
                var remotes = rt.Dependencies.GetCachedRcloneRemotes();
                return Results.Ok(new { modules = catalog, dependencies = deps, rcloneRemotes = remotes });
            });

            api.MapPost("/modules/{moduleId}/toggle", (string moduleId, [FromQuery] bool enabled, HousekeepingRuntimeContainer rt) =>
            {
                if (!Enum.TryParse<ModuleId>(moduleId, ignoreCase: true, out var parsed))
                    return Results.BadRequest(new { error = $"Unknown module '{moduleId}'." });

                if (!rt.ConfigService.MutateAndSave(cfg => cfg.SetModuleAppWideEnabled(parsed, enabled), out string? err))
                    return Results.BadRequest(new { error = err ?? "Failed to update module toggle." });

                rt.Orchestrator.Log("INFO", $"Module '{parsed}' is now {(enabled ? "ENABLED" : "DISABLED")} app-wide.");
                return Results.Ok(new { ok = true, module = parsed.ToString(), enabled });
            });

            api.MapPost("/modules/rclone-remotes/refresh", async (HousekeepingRuntimeContainer rt) =>
            {
                var remotes = await rt.Dependencies.RefreshRcloneRemotesAsync();
                return Results.Ok(new { rcloneRemotes = remotes });
            });

            // ==================== 7. TERMINAL & INTERACTIVE CLI ==================== //
            api.MapGet("/terminal/history", ([FromQuery] int? limit, HousekeepingRuntimeContainer rt) =>
                Results.Ok(new
                {
                    entries = rt.LogBuffer.GetRecentEntries(limit ?? 1500),
                    workingDirectory = rt.InteractiveCli.WorkingDirectory,
                    isCommandRunning = rt.InteractiveCli.IsCommandRunning,
                    commandHistory = rt.InteractiveCli.GetCommandHistory(),
                    footerLine = rt.LogBuffer.LatestFooterLine
                }));

            api.MapPost("/terminal/clear", (HousekeepingRuntimeContainer rt) =>
            {
                rt.LogBuffer.Clear();
                return Results.Ok(new { ok = true });
            });

            api.MapPost("/terminal/exec", async ([FromBody] TerminalExecPayload body, HousekeepingRuntimeContainer rt, CancellationToken ct) =>
            {
                long beforeId = rt.LogBuffer.LatestEntryId;
                var (exitCode, cwd) = await rt.InteractiveCli.ExecuteCommandAsync(body?.Command ?? "", ct);
                rt.LogBuffer.FlushPendingBatch();
                var entries = rt.LogBuffer.GetEntriesAfter(beforeId, 500);
                return Results.Ok(new { ok = true, exitCode, workingDirectory = cwd, entries });
            });

            api.MapPost("/terminal/cancel", (HousekeepingRuntimeContainer rt) =>
            {
                rt.InteractiveCli.CancelActiveCommand();
                return Results.Ok(new { ok = true });
            });

            api.MapGet("/terminal/remote-tail/{nodeId}", async (string nodeId, [FromQuery] int? limit, HousekeepingRuntimeContainer rt, CancellationToken ct) =>
            {
                int maxLines = Math.Clamp(limit ?? 300, 20, 2000);
                if (string.Equals(nodeId, "local", StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Ok(new
                    {
                        nodeId = "local",
                        nodeName = Environment.MachineName,
                        status = "Online (Local)",
                        lines = rt.LogBuffer.GetRecentEntries(maxLines).Select(e => e.FormattedLine).ToList()
                    });
                }

                var node = rt.ConfigService.Config.RemoteNodes.FirstOrDefault(n => string.Equals(n.Id, nodeId, StringComparison.OrdinalIgnoreCase));
                if (node == null)
                    return Results.NotFound(new { error = "Node not found." });

                // 1. Prefer Housekeeping HTTP API when PreferHttpApi && HostOrIp is set
                if (node.PreferHttpApi && !string.IsNullOrWhiteSpace(node.HostOrIp))
                {
                    try
                    {
                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(1500));
                        string baseUrl = node.HostOrIp.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                            ? node.HostOrIp.TrimEnd('/')
                            : $"http://{node.HostOrIp}:{node.HttpApiPort}";
                        using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/v1/terminal/history?limit={maxLines}");
                        if (!string.IsNullOrWhiteSpace(node.ApiToken))
                            req.Headers.TryAddWithoutValidation("X-Housekeeping-Token", node.ApiToken.Trim());

                        using var resp = await rt.FleetHttpClient.SendAsync(req, timeoutCts.Token);
                        if (resp.IsSuccessStatusCode)
                        {
                            string json = await resp.Content.ReadAsStringAsync(timeoutCts.Token);
                            using var doc = JsonDocument.Parse(json);
                            var lines = new List<string>();
                            if (doc.RootElement.TryGetProperty("entries", out var arr) && arr.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var el in arr.EnumerateArray())
                                {
                                    if (el.TryGetProperty("FormattedLine", out var fl))
                                        lines.Add(fl.GetString() ?? "");
                                    else if (el.TryGetProperty("formattedLine", out var fl2))
                                        lines.Add(fl2.GetString() ?? "");
                                }
                            }
                            return Results.Ok(new { nodeId = node.Id, nodeName = node.Name, online = true, status = $"Online via HTTP ({baseUrl})", lines });
                        }
                    }
                    catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[HousekeepingApiEndpoints] Non-fatal error ignored: {__suppressedEx1.Message}"); }
                }

                // 2. Fallback: Fast TCP :445 pre-flight + SMB share log tailing for 2.8.x nodes (never blocks ThreadPool on offline UNC hosts)
                if (!await RemoteNodeDeploymentService.IsUncHostReachableFastAsync(node.RemoteAppPath, node.HostOrIp, 1200, ct))
                {
                    return Results.Ok(new
                    {
                        nodeId = node.Id,
                        nodeName = node.Name,
                        online = false,
                        status = $"Offline ({node.HostOrIp ?? node.RemoteAppPath})",
                        lines = new List<string>()
                    });
                }

                var smbLines = await RemoteNodeDeploymentService.RunBoundedUncOperationAsync(
                    node.RemoteAppPath,
                    () => ReadRemoteSmbTail(node.RemoteAppPath, maxLines),
                    fallbackOnTimeoutOrBusy: (Online: false, Lines: new List<string>()),
                    timeoutMs: 1800,
                    cooldownSeconds: 20,
                    ct: ct);
                return Results.Ok(new
                {
                    nodeId = node.Id,
                    nodeName = node.Name,
                    online = smbLines.Online,
                    status = smbLines.Online ? $"Connected via SMB ({node.RemoteAppPath})" : $"Offline / SMB Timeout ({node.RemoteAppPath})",
                    lines = smbLines.Lines
                });
            });

            // ==================== 8. COMPLIANCE REPORTS, TEMPLATES & .HASH VERIFIER ==================== //
            api.MapGet("/reports", (
                [FromQuery] string? console,
                [FromQuery] string? category,
                [FromQuery] string? status,
                [FromQuery] string? search,
                HousekeepingRuntimeContainer rt) =>
            {
                var all = rt.ComplianceReports.GetAllReports();
                int openCount = all.Count(r => r.ResolutionStatus == ComplianceReportResolutionStatus.Open);
                int cleanableCount = all.Count(r => r.ResolutionStatus == ComplianceReportResolutionStatus.Open && r.ExtraDeletableCount > 0);
                int missingOrFormatCount = all.Count(r =>
                    r.ResolutionStatus == ComplianceReportResolutionStatus.Open &&
                    (string.Equals(r.PrimaryCategory, "Missing / Broken Refs", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(r.PrimaryCategory, "Wrong Format", StringComparison.OrdinalIgnoreCase)));
                int resolvedOrIgnoredCount = all.Count(r => r.ResolutionStatus != ComplianceReportResolutionStatus.Open);

                var consoles = all
                    .Select(r => r.ConsoleName)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                IEnumerable<ComplianceReportItem> query = all;
                if (!string.IsNullOrWhiteSpace(console) && !string.Equals(console, "All Consoles", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(r => string.Equals(r.ConsoleName, console, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(category) && !string.Equals(category, "All Categories", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(r => string.Equals(r.PrimaryCategory, category, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(status))
                {
                    query = status switch
                    {
                        "Open Only" or "Open" => query.Where(r => r.ResolutionStatus == ComplianceReportResolutionStatus.Open),
                        "Ignored Only" or "Ignored" => query.Where(r => r.ResolutionStatus == ComplianceReportResolutionStatus.Ignored),
                        "Resolved Only" or "Resolved" => query.Where(r => r.ResolutionStatus == ComplianceReportResolutionStatus.Resolved),
                        _ => query
                    };
                }

                if (!string.IsNullOrWhiteSpace(search))
                {
                    string q = search.Trim();
                    query = query.Where(r =>
                        (r.TargetFileName?.Contains(q, StringComparison.OrdinalIgnoreCase) == true) ||
                        (r.ConsoleName?.Contains(q, StringComparison.OrdinalIgnoreCase) == true) ||
                        (r.SummaryCode?.Contains(q, StringComparison.OrdinalIgnoreCase) == true) ||
                        (r.SummaryText?.Contains(q, StringComparison.OrdinalIgnoreCase) == true) ||
                        (r.TemplateDisplayName?.Contains(q, StringComparison.OrdinalIgnoreCase) == true));
                }

                return Results.Ok(new
                {
                    kpis = new
                    {
                        openCount,
                        cleanableCount,
                        missingOrFormatCount,
                        resolvedOrIgnoredCount,
                        totalCount = all.Count
                    },
                    consoles,
                    items = query.ToList()
                });
            });

            api.MapPost("/reports/scan", async ([FromQuery] string? jobId, [FromQuery] bool? wait, HousekeepingRuntimeContainer rt) =>
            {
                if (wait == true)
                {
                    var (ok, flagged, msg) = await rt.Orchestrator.RunComplianceScanAsync(jobId);
                    return ok ? Results.Ok(new { ok = true, started = true, flaggedCount = flagged, message = msg, state = rt.Orchestrator.GetStateSnapshot() }) : Results.BadRequest(new { ok = false, error = msg });
                }

                var (started, startMsg) = rt.Orchestrator.StartComplianceScan(jobId);
                return started
                    ? Results.Ok(new { ok = true, started = true, flaggedCount = 0, message = startMsg, state = rt.Orchestrator.GetStateSnapshot() })
                    : Results.BadRequest(new { ok = false, error = startMsg });
            });

            api.MapPost("/reports/{reportId}/dry-run-clean", (string reportId, [FromBody] CleanArchivePayload? body, HousekeepingRuntimeContainer rt) =>
            {
                var item = rt.ComplianceReports.GetAllReports()
                    .FirstOrDefault(r => string.Equals(r.ReportId, reportId, StringComparison.OrdinalIgnoreCase));
                if (item == null)
                    return Results.NotFound(new { error = "Compliance report not found." });

                string sevenZip = rt.Dependencies.ResolveSevenZip();
                var paths = body?.ExplicitInternalPaths is { Count: > 0 }
                    ? body.ExplicitInternalPaths
                    : item.EntryNodes.Where(n => n.CanClean && n.SelectedForCleanup).Select(n => n.InternalPath).ToList();

                var preview = ArchiveComplianceService.BuildCleanupDryRunPreview(item, sevenZip, paths);
                return Results.Ok(preview);
            });

            api.MapPost("/reports/clean-extra", async ([FromBody] CleanArchivePayload body, HousekeepingRuntimeContainer rt) =>
            {
                if (rt.Orchestrator.IsRunning)
                    return Results.Conflict(new { error = "Wait for the active job to finish before modifying archives." });

                var ids = body?.ReportIds ?? new List<string>();
                var all = rt.ComplianceReports.GetAllReports();
                var targets = all
                    .Where(r => ids.Contains(r.ReportId, StringComparer.OrdinalIgnoreCase) && !r.IsDirectory && r.ExtraDeletableCount > 0)
                    .ToList();

                if (targets.Count == 0)
                    return Results.BadRequest(new { error = "No eligible archive reports with removable extra files were selected." });

                string sevenZip = rt.Dependencies.ResolveSevenZip();
                var rom = rt.Orchestrator.ResolveRomConfig();
                rom.EnsureArchiveTemplatesInitialized();

                int cleaned = 0;
                int failed = 0;
                var messages = new List<string>();

                foreach (var rep in targets)
                {
                    var col = rom.Collections.FirstOrDefault(c => string.Equals(c.ConsoleName, rep.ConsoleName, StringComparison.OrdinalIgnoreCase));
                    var explicitPaths = (targets.Count == 1 && body?.ExplicitInternalPaths is { Count: > 0 })
                        ? body.ExplicitInternalPaths
                        : null;

                    var (ok, msg, _) = await Task.Run(() => ArchiveComplianceService.CleanExtraFilesFromArchiveAsync(
                        rep, col, rom, sevenZip, rt.ComplianceReports, explicitPaths));

                    messages.Add(msg);
                    if (ok)
                    {
                        cleaned++;
                        rt.Orchestrator.Log("SUCCESS", $"[7z Cleanup] {msg}");
                    }
                    else
                    {
                        failed++;
                        rt.Orchestrator.Log("WARN", $"[7z Cleanup Skipped] '{rep.TargetFileName}': {msg}");
                    }
                }

                return Results.Ok(new { ok = cleaned > 0 && failed == 0, cleaned, failed, messages });
            });

            api.MapPost("/reports/heal-hash", async ([FromBody] ReportsBatchIdsPayload body, HousekeepingRuntimeContainer rt) =>
            {
                if (rt.Orchestrator.IsRunning)
                    return Results.Conflict(new { error = "Wait for the active job to finish before healing .hash files." });

                var ids = body?.ReportIds ?? new List<string>();
                var targets = rt.ComplianceReports.GetAllReports()
                    .Where(r => ids.Contains(r.ReportId, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                if (targets.Count == 0)
                    return Results.BadRequest(new { error = "Select one or more report rows first." });

                string sevenZip = rt.Dependencies.ResolveSevenZip();
                var rom = rt.Orchestrator.ResolveRomConfig();
                var placement = rom.HashFilePlacement == HashSidecarMode.None ? HashSidecarMode.SidecarBeside : rom.HashFilePlacement;
                int healed = 0;

                foreach (var rep in targets)
                {
                    if (!File.Exists(rep.TargetFullPath) && !Directory.Exists(rep.TargetFullPath))
                        continue;

                    var res = await Task.Run(() => HashSidecarService.HealTargetSidecarAsync(
                        sevenZip,
                        rep.TargetFullPath,
                        rom.DeepInspectAndHashInnerRoms,
                        placement,
                        rom.TempExtractionScratchPath,
                        priorVerification: null,
                        structureMode: rom.StructureMode));

                    if (res != null)
                    {
                        healed++;
                        rt.Orchestrator.Log("SUCCESS", $"[.hash Healed] Updated sidecar for '{rep.TargetFileName}'.");
                    }
                }

                return Results.Ok(new { ok = true, healedCount = healed });
            });

            api.MapPost("/reports/ignore", ([FromBody] ReportsBatchIdsPayload body, HousekeepingRuntimeContainer rt) =>
            {
                var ids = body?.ReportIds ?? new List<string>();
                rt.ComplianceReports.MarkIgnored(ids, body?.Ignored ?? true);
                return Results.Ok(new { ok = true });
            });

            api.MapPost("/reports/clear-resolved", (HousekeepingRuntimeContainer rt) =>
            {
                int removed = rt.ComplianceReports.ClearResolvedReports();
                return Results.Ok(new { ok = true, removedCount = removed });
            });

            api.MapGet("/reports/export-csv", (HousekeepingRuntimeContainer rt) =>
            {
                string tmpCsv = Path.Combine(rt.ConfigService.ActiveLogsDir, $"Compliance_Export_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                rt.ComplianceReports.ExportToCsv(tmpCsv);
                byte[] bytes = File.ReadAllBytes(tmpCsv);
                try { File.Delete(tmpCsv); } catch (System.Exception __suppressedEx2) { System.Diagnostics.Debug.WriteLine($"[HousekeepingApiEndpoints] Non-fatal error ignored: {__suppressedEx2.Message}"); }
                return Results.File(bytes, "text/csv", $"Housekeeping_Compliance_Report_{DateTime.Now:yyyyMMdd}.csv");
            });

            api.MapGet("/reports/export-json", (HousekeepingRuntimeContainer rt) =>
            {
                var reports = rt.ComplianceReports.GetAllReports();
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(reports, new JsonSerializerOptions { WriteIndented = true });
                return Results.File(bytes, "application/json", $"Housekeeping_Compliance_Report_{DateTime.Now:yyyyMMdd}.json");
            });

            api.MapPost("/hash-verifier/run", async ([FromQuery] bool? wait, [FromBody] HashVerifierPayload body, HousekeepingRuntimeContainer rt) =>
            {
                var mode = ParseSidecarVerifyMode(body?.VerifyMode);
                if (wait == true)
                {
                    var (ok, report, msg) = await rt.Orchestrator.RunHashSidecarVerifierAsync(
                        body?.JobId,
                        body?.SourceModule ?? ModuleId.RomLauncherGenerator,
                        mode);

                    return ok
                        ? Results.Ok(new { ok = true, started = true, message = msg, report, state = rt.Orchestrator.GetStateSnapshot() })
                        : Results.BadRequest(new { ok = false, error = msg });
                }

                var (started, startMsg) = rt.Orchestrator.StartHashSidecarVerifier(
                    body?.JobId,
                    body?.SourceModule ?? ModuleId.RomLauncherGenerator,
                    mode);

                return started
                    ? Results.Ok(new { ok = true, started = true, message = startMsg, state = rt.Orchestrator.GetStateSnapshot() })
                    : Results.BadRequest(new { ok = false, error = startMsg });
            });

            // ==================== 9. HYBRID FILESYSTEM EXPLORER & NATIVE PICKER BRIDGE ==================== //
            api.MapGet("/filesystem/drives", async (CancellationToken ct) =>
            {
                var scanned = await SystemPerformanceTelemetryService.ScanLocalAndNetworkDrivesBoundedAsync(ct);
                var drives = scanned.Select(d => new
                {
                    name = d.DriveLetter.TrimEnd('\\') + "\\",
                    driveLetter = d.DriveLetter,
                    driveType = d.DriveType,
                    isNetwork = d.IsNetwork,
                    isReady = d.IsReady,
                    volumeLabel = d.VolumeLabel,
                    remotePath = d.RemotePath,
                    freeGb = Math.Round(d.FreeGb, 1),
                    totalGb = Math.Round(d.TotalGb, 1)
                }).ToList();

                return Results.Ok(drives);
            });

            api.MapGet("/filesystem/browse", async ([FromQuery] string? path, [FromQuery] bool includeFiles) =>
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return Results.BadRequest(new { error = "Path parameter is required." });
                }

                string target = path.Trim();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    var payload = await Task.Run(() =>
                    {
                        if (!Directory.Exists(target))
                        {
                            return (Found: false, Data: (object?)null, Error: $"Directory '{target}' does not exist or is offline.");
                        }

                        var di = new DirectoryInfo(target);
                        string? parent = di.Parent?.FullName;

                        var directories = new List<object>();
                        foreach (var sub in di.EnumerateDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).Take(600))
                        {
                            try
                            {
                                if ((sub.Attributes & FileAttributes.System) != 0)
                                    continue;
                                directories.Add(new
                                {
                                    name = sub.Name,
                                    fullPath = sub.FullName,
                                    lastModifiedUtc = sub.LastWriteTimeUtc
                                });
                            }
                            catch (System.Exception __suppressedEx3) { System.Diagnostics.Debug.WriteLine($"[HousekeepingApiEndpoints] Non-fatal error ignored: {__suppressedEx3.Message}"); }
                        }

                        var files = new List<object>();
                        if (includeFiles)
                        {
                            foreach (var f in di.EnumerateFiles().OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Take(600))
                            {
                                try
                                {
                                    files.Add(new
                                    {
                                        name = f.Name,
                                        fullPath = f.FullName,
                                        sizeBytes = f.Length,
                                        extension = f.Extension
                                    });
                                }
                                catch (System.Exception __suppressedEx4) { System.Diagnostics.Debug.WriteLine($"[HousekeepingApiEndpoints] Non-fatal error ignored: {__suppressedEx4.Message}"); }
                            }
                        }

                        return (Found: true, Data: (object?)new
                        {
                            currentPath = di.FullName,
                            parentPath = parent,
                            directories,
                            files
                        }, Error: "");
                    }, cts.Token);

                    return payload.Found
                        ? Results.Ok(payload.Data)
                        : Results.NotFound(new { error = payload.Error });
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = $"Could not browse '{target}': {ex.Message}" });
                }
            });

            api.MapPost("/filesystem/native-pick-folder", async (HttpContext ctx, [FromBody] NativePickerPayload? body, TieredAuthService auth, HousekeepingRuntimeContainer rt) =>
            {
                var eval = auth.EvaluateRequest(ctx);
                if (!eval.IsDirectLoopback || !rt.NativeDialogs.IsAvailable)
                {
                    return Results.BadRequest(new { error = "Native Windows folder dialog is only available when browsing locally with the Desktop Tray Daemon active." });
                }

                string? picked = await rt.NativeDialogs.PickFolderAsync(body?.InitialPath, body?.Title);
                return Results.Ok(new { cancelled = string.IsNullOrEmpty(picked), path = picked });
            });

            api.MapPost("/filesystem/native-pick-file", async (HttpContext ctx, [FromBody] NativePickerPayload? body, TieredAuthService auth, HousekeepingRuntimeContainer rt) =>
            {
                var eval = auth.EvaluateRequest(ctx);
                if (!eval.IsDirectLoopback || !rt.NativeDialogs.IsAvailable)
                {
                    return Results.BadRequest(new { error = "Native Windows file dialog is only available when browsing locally with the Desktop Tray Daemon active." });
                }

                string? picked = await rt.NativeDialogs.PickFileAsync(body?.InitialPath, body?.Filter, body?.Title);
                return Results.Ok(new { cancelled = string.IsNullOrEmpty(picked), path = picked });
            });

            api.MapPost("/filesystem/reveal-in-explorer", (HttpContext ctx, [FromBody] RevealPathPayload? body, TieredAuthService auth) =>
            {
                var eval = auth.EvaluateRequest(ctx);
                if (!eval.IsDirectLoopback)
                {
                    return Results.BadRequest(new { error = "Reveal in Windows Explorer is only supported on localhost." });
                }

                string target = (body?.Path ?? "").Trim();
                if (string.IsNullOrEmpty(target))
                    return Results.BadRequest(new { error = "Path is empty." });

                try
                {
                    if (File.Exists(target))
                        Process.Start("explorer.exe", $"/select,\"{target}\"");
                    else if (Directory.Exists(target))
                        Process.Start("explorer.exe", $"\"{target}\"");
                    else
                        return Results.NotFound(new { error = $"Path '{target}' does not exist on disk." });

                    return Results.Ok(new { ok = true });
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            });

            api.MapPost("/desktop/open-standalone", (HttpContext ctx, TieredAuthService auth, HousekeepingRuntimeContainer rt) =>
            {
                var eval = auth.EvaluateRequest(ctx);
                if (!eval.IsDirectLoopback || !rt.NativeDialogs.IsAvailable)
                    return Results.BadRequest(new { error = "Standalone WebView2 window can only be opened on the local host PC." });

                rt.NativeDialogs.RequestOpenStandaloneWindow();
                return Results.Ok(new { ok = true });
            });

            // ==================== 10. GREENROOM (:8420) ECOSYSTEM INTEGRATION ==================== //
            api.MapGet("/greenroom/status", async (HttpContext ctx, HousekeepingRuntimeContainer rt, CancellationToken ct) =>
            {
                string hostHeader = ctx.Request.Host.Value ?? "127.0.0.1";
                var status = await rt.GreenroomSync.GetStatusAsync(hostHeader, ct);
                return Results.Ok(status);
            });

            api.MapPost("/greenroom/sync-now", async ([FromBody] GreenroomSyncPayload? body, HousekeepingRuntimeContainer rt, CancellationToken ct) =>
            {
                var res = await rt.GreenroomSync.TriggerPostManifestSyncAsync(
                    body?.SourceModule ?? ModuleId.RomLauncherGenerator,
                    changedManifestPaths: new[] { "manual_trigger" },
                    isDryRun: false,
                    log: rt.Orchestrator.Log,
                    forceManual: true,
                    ct: ct);

                return Results.Ok(res);
            });

            // ==================== 11. REMOTE FLEET NODES ==================== //
            api.MapGet("/nodes", async ([FromQuery] bool probe, HousekeepingRuntimeContainer rt, CancellationToken ct) =>
            {
                var nodes = rt.ConfigService.Config.RemoteNodes.Select(n => n.Clone()).ToList();
                var deploySvc = new RemoteNodeDeploymentService();

                if (probe && nodes.Count > 0)
                {
                    await Task.WhenAll(nodes.Select(async node =>
                    {
                        if (node.PreferHttpApi && !string.IsNullOrWhiteSpace(node.HostOrIp))
                        {
                            try
                            {
                                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                                timeoutCts.CancelAfter(TimeSpan.FromSeconds(2.0));
                                string url = node.HostOrIp.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                                    ? $"{node.HostOrIp.TrimEnd('/')}/api/v1/health"
                                    : $"http://{node.HostOrIp}:{node.HttpApiPort}/api/v1/health";
                                using var resp = await rt.FleetHttpClient.GetAsync(url, timeoutCts.Token);
                                if (resp.IsSuccessStatusCode)
                                {
                                    string json = await resp.Content.ReadAsStringAsync(timeoutCts.Token);
                                    using var doc = JsonDocument.Parse(json);
                                    node.IsReachable = true;
                                    node.DetectedVersion = doc.RootElement.TryGetProperty("version", out var vp) ? (vp.GetString() ?? "1.0.0-rc.2") : "1.0.0-rc.2";
                                    RemoteNodeDeploymentService.EvaluateVersionEligibility(node);
                                    node.StatusMessage = $"Online via HTTP API ({node.HostOrIp}:{node.HttpApiPort}) — v{node.DetectedVersion}";
                                    return;
                                }
                            }
                            catch (System.Exception __suppressedEx5) { System.Diagnostics.Debug.WriteLine($"[HousekeepingApiEndpoints] Non-fatal error ignored: {__suppressedEx5.Message}"); }
                        }

                        await deploySvc.RefreshNodeStatusAsync(node, ct);
                    }));
                }

                return Results.Ok(new
                {
                    localMachineName = Environment.MachineName,
                    localVersion = RemoteNodeDeploymentService.GetLocalAppVersion(),
                    nodes = nodes.Select(n => new
                    {
                        n.Id,
                        n.Name,
                        n.HostOrIp,
                        n.HttpApiPort,
                        n.PreferHttpApi,
                        hasApiToken = !string.IsNullOrWhiteSpace(n.ApiToken),
                        n.RemoteAppPath,
                        n.ConnectionType,
                        n.Port,
                        n.Username,
                        n.DetectedVersion,
                        n.IsReachable,
                        n.CanUpdateApp,
                        n.CanUpdateSettings,
                        n.StatusMessage
                    })
                });
            });

            api.MapPost("/nodes/{nodeId}/push-settings", async (string nodeId, HousekeepingRuntimeContainer rt, CancellationToken ct) =>
            {
                var node = rt.ConfigService.Config.RemoteNodes.FirstOrDefault(n => string.Equals(n.Id, nodeId, StringComparison.OrdinalIgnoreCase));
                if (node == null) return Results.NotFound(new { error = "Remote node not found." });

                var deploySvc = new RemoteNodeDeploymentService();
                bool ok = await deploySvc.PushSettingsToRemoteAsync(node, rt.ConfigService.Config, rt.Orchestrator.Log, ct);
                return ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { ok = false, error = "Failed pushing settings to remote node." });
            });

            api.MapPost("/nodes/{nodeId}/push-app", async (string nodeId, HousekeepingRuntimeContainer rt, CancellationToken ct) =>
            {
                var node = rt.ConfigService.Config.RemoteNodes.FirstOrDefault(n => string.Equals(n.Id, nodeId, StringComparison.OrdinalIgnoreCase));
                if (node == null) return Results.NotFound(new { error = "Remote node not found." });

                var deploySvc = new RemoteNodeDeploymentService();
                bool ok = await deploySvc.UpdateRemoteAppBinariesAsync(node, rt.Orchestrator.Log, ct);
                return ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { ok = false, error = "Failed deploying app binaries to remote node." });
            });

            api.MapPost("/nodes/{nodeId}/command", async (string nodeId, [FromBody] RemoteNodeActionPayload body, HousekeepingRuntimeContainer rt, CancellationToken ct) =>
            {
                var node = rt.ConfigService.Config.RemoteNodes.FirstOrDefault(n => string.Equals(n.Id, nodeId, StringComparison.OrdinalIgnoreCase));
                if (node == null) return Results.NotFound(new { error = "Remote node not found." });

                string action = body?.Action ?? "StartQueue";
                if (node.PreferHttpApi && !string.IsNullOrWhiteSpace(node.HostOrIp))
                {
                    try
                    {
                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
                        string baseUrl = node.HostOrIp.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                            ? node.HostOrIp.TrimEnd('/')
                            : $"http://{node.HostOrIp}:{node.HttpApiPort}";
                        string route = action.Equals("Stop", StringComparison.OrdinalIgnoreCase)
                            ? "/api/v1/pipeline/stop"
                            : "/api/v1/pipeline/run-queue";

                        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}{route}");
                        if (!string.IsNullOrWhiteSpace(node.ApiToken))
                            req.Headers.TryAddWithoutValidation("X-Housekeeping-Token", node.ApiToken.Trim());
                        req.Content = new StringContent(JsonSerializer.Serialize(new { QueueId = body?.QueueId, IsDryRun = false }), Encoding.UTF8, "application/json");

                        using var resp = await rt.FleetHttpClient.SendAsync(req, timeoutCts.Token);
                        if (resp.IsSuccessStatusCode)
                            return Results.Ok(new { ok = true, transport = "HttpApi" });
                    }
                    catch (System.Exception __suppressedEx6) { System.Diagnostics.Debug.WriteLine($"[HousekeepingApiEndpoints] Non-fatal error ignored: {__suppressedEx6.Message}"); }
                }

                bool sent = RemoteNodeDeploymentService.SendRemoteControlCommand(node, new RemoteControlRequest
                {
                    Action = action,
                    TargetQueueId = body?.QueueId,
                    TargetJobId = body?.JobId
                }, rt.Orchestrator.Log);

                return sent
                    ? Results.Ok(new { ok = true, transport = "SmbSignal" })
                    : Results.BadRequest(new { ok = false, error = "Could not dispatch command to remote node." });
            });

            // ==================== 12. RUN HISTORY & SETTINGS HELPERS ==================== //
            api.MapGet("/history", (HousekeepingRuntimeContainer rt) =>
                Results.Ok(rt.ConfigService.History));

            api.MapDelete("/history", (HousekeepingRuntimeContainer rt) =>
            {
                rt.ConfigService.ClearHistory();
                return Results.Ok(new { ok = true });
            });

            api.MapGet("/history/{index:int}/log", (int index, HousekeepingRuntimeContainer rt) =>
            {
                var hist = rt.ConfigService.History;
                if (index < 0 || index >= hist.Count)
                    return Results.NotFound(new { error = "History entry not found." });

                var entry = hist[index];
                if (string.IsNullOrWhiteSpace(entry.LogFilePath) || !File.Exists(entry.LogFilePath))
                    return Results.NotFound(new { error = $"Log file '{entry.LogFilePath}' is no longer present on disk." });

                string content = ConfigService.ReadAllTextWithRetry(entry.LogFilePath);
                return Results.Ok(new
                {
                    entry.ModulesSummary,
                    entry.StartedAt,
                    entry.CompletedAt,
                    entry.Status,
                    entry.LogFilePath,
                    content
                });
            });

            api.MapGet("/settings/watchdog", (HousekeepingRuntimeContainer rt) =>
            {
                if (rt.ConfigService.IsTestMode)
                {
                    return Results.Ok(new { isRegistered = rt.ConfigService.Config.EnableWatchdog, taskName = WindowsWatchdogService.TaskName, simulated = true });
                }
                bool registered = WindowsWatchdogService.IsWatchdogRegistered();
                return Results.Ok(new { isRegistered = registered, taskName = WindowsWatchdogService.TaskName });
            });

            api.MapPost("/settings/watchdog", ([FromBody] WatchdogPayload body, HousekeepingRuntimeContainer rt) =>
            {
                if (rt.ConfigService.IsTestMode)
                {
                    rt.ConfigService.MutateAndSave(cfg =>
                    {
                        cfg.EnableWatchdog = body.Enable;
                        if (body.Enable) cfg.WatchdogIntervalMinutes = body.IntervalMinutes;
                    });
                    return Results.Ok(new
                    {
                        ok = true,
                        isRegistered = body.Enable,
                        simulated = true,
                        message = "[TestMode] Simulated watchdog update in sandboxed settings without mutating Windows Task Scheduler."
                    });
                }

                if (body.Enable)
                {
                    var (ok, msg) = WindowsWatchdogService.RegisterWatchdog(body.IntervalMinutes);
                    rt.ConfigService.MutateAndSave(cfg =>
                    {
                        cfg.EnableWatchdog = ok;
                        cfg.WatchdogIntervalMinutes = body.IntervalMinutes;
                    });
                    return ok ? Results.Ok(new { ok = true, isRegistered = true, message = msg }) : Results.BadRequest(new { ok = false, error = msg });
                }
                else
                {
                    var (ok, msg) = WindowsWatchdogService.UnregisterWatchdog();
                    rt.ConfigService.MutateAndSave(cfg => cfg.EnableWatchdog = false);
                    return Results.Ok(new { ok, isRegistered = false, message = msg });
                }
            });

            api.MapPost("/settings/test-discord", async (HousekeepingRuntimeContainer rt) =>
            {
                string url = rt.ConfigService.Config.DiscordWebhookUrl;
                if (string.IsNullOrWhiteSpace(url))
                    return Results.BadRequest(new { error = "Configure a Discord Webhook URL first." });

                await rt.Orchestrator.Scheduler.SendDiscordWebhookAsync(
                    url,
                    "Housekeeping Webhook Test",
                    "Connected! Housekeeping real-time pipeline notifications are active.",
                    isError: false);

                return Results.Ok(new { ok = true, message = "Test embed sent to Discord." });
            });

            api.MapPost("/settings/test-toast", (HousekeepingRuntimeContainer rt) =>
            {
                rt.Orchestrator.Log("INFO", "[Notification Test] Dispatched system tray / browser notification test.");
                return Results.Ok(new { ok = true, message = "Housekeeping Notification Test succeeded!" });
            });

            // ==================== 11. TAILSCALE SERVE HTTPS & MOBILE PWA ==================== //
            api.MapGet("/system/tailscale-pwa", async (HousekeepingRuntimeContainer rt, TailscalePwaService tsPwa, CancellationToken ct) =>
            {
                int port = rt.ConfigService.Config.WebServer.Port;
                var status = await tsPwa.GetStatusAsync(port, ct);
                return Results.Ok(status);
            });

            api.MapPost("/system/tailscale-pwa/enable", async (HousekeepingRuntimeContainer rt, TailscalePwaService tsPwa, CancellationToken ct) =>
            {
                int port = rt.ConfigService.Config.WebServer.Port;
                if (rt.ConfigService.IsTestMode)
                {
                    return Results.Ok(new TailscalePwaStatusDto
                    {
                        Simulated = true,
                        StatusMessage = "[TestMode] Tailscale Serve host mutation is suppressed in --test-mode."
                    });
                }
                var status = await tsPwa.EnableHttpsServeAsync(port, ct);
                if (status.ServeConfigured)
                {
                    rt.Orchestrator.Log("SUCCESS", $"[Tailscale PWA] Enabled HTTPS Serve proxy at {status.HttpsPwaUrl}");
                }
                else if (!string.IsNullOrWhiteSpace(status.PendingAuthUrl))
                {
                    rt.Orchestrator.Log("WARN", $"[Tailscale PWA] One-time Tailscale Serve authorization required: {status.PendingAuthUrl}");
                }
                return Results.Ok(status);
            });

            api.MapPost("/system/tailscale-pwa/disable", async (HousekeepingRuntimeContainer rt, TailscalePwaService tsPwa, CancellationToken ct) =>
            {
                int port = rt.ConfigService.Config.WebServer.Port;
                if (rt.ConfigService.IsTestMode)
                {
                    return Results.Ok(new TailscalePwaStatusDto
                    {
                        Simulated = true,
                        StatusMessage = "[TestMode] Tailscale Serve host mutation is suppressed in --test-mode."
                    });
                }
                var status = await tsPwa.DisableHttpsServeAsync(port, ct);
                rt.Orchestrator.Log("INFO", "[Tailscale PWA] Reset local tailscale serve HTTPS proxy configuration.");
                return Results.Ok(status);
            });
        }

        private static bool HasRestrictedExecutionSurfaceChanged(AppConfig existing, AppConfig incoming)
        {
            static string SerializeRestrictedSurface(AppConfig c)
            {
                var list = new List<object>
                {
                    c.WebServer?.RestrictRawShellToLocalhost ?? true,
                    (c.ToolPaths?.HandBrakeCliPath ?? "").Trim(),
                    (c.ToolPaths?.SevenZipPath ?? "").Trim(),
                    (c.ToolPaths?.RclonePath ?? "").Trim(),
                    (c.ToolPaths?.HumbleDownloaderPath ?? "").Trim(),
                    (c.GreenroomIntegration?.GreenroomRootPath ?? "").Trim()
                };

                if (c.CustomScripts?.Tasks != null)
                    list.Add(c.CustomScripts.Tasks);

                if (c.DefinedJobs != null)
                {
                    foreach (var j in c.DefinedJobs.Where(j => j.ModuleType == ModuleId.CustomScripts && j.CustomScripts != null))
                        list.Add(new { Scope = "DefinedJobs", j.JobId, j.CustomScripts!.Tasks });
                }

                if (c.NamedQueues != null)
                {
                    foreach (var q in c.NamedQueues)
                    {
                        if (q.Jobs == null) continue;
                        foreach (var j in q.Jobs.Where(j => j.ModuleType == ModuleId.CustomScripts && j.CustomScripts != null))
                            list.Add(new { Scope = $"Queue:{q.Id}", j.JobId, j.CustomScripts!.Tasks });
                    }
                }

                return JsonSerializer.Serialize(list);
            }

            return !string.Equals(SerializeRestrictedSurface(existing), SerializeRestrictedSurface(incoming), StringComparison.Ordinal);
        }

        private static (bool Online, List<string> Lines) ReadRemoteSmbTail(string? remoteAppPath, int maxLines)
        {
            if (string.IsNullOrWhiteSpace(remoteAppPath) || !Directory.Exists(remoteAppPath))
                return (false, new List<string>());

            try
            {
                string liveLog = Path.Combine(remoteAppPath, "logs", "Housekeeping_Live.log");
                if (!File.Exists(liveLog))
                {
                    string runsDir = Path.Combine(remoteAppPath, "logs", "runs");
                    if (Directory.Exists(runsDir))
                    {
                        var newest = new DirectoryInfo(runsDir)
                            .GetFiles("*.log")
                            .OrderByDescending(f => f.LastWriteTimeUtc)
                            .FirstOrDefault();
                        if (newest != null)
                            liveLog = newest.FullName;
                    }
                }

                if (!File.Exists(liveLog))
                    return (true, new List<string> { "[Connected to remote share — no log lines emitted yet]" });

                using var fs = new FileStream(liveLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs, Encoding.UTF8);
                var ring = new Queue<string>(maxLines + 1);
                string? line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (ring.Count >= maxLines)
                        ring.Dequeue();
                    ring.Enqueue(line);
                }
                return (true, ring.ToList());
            }
            catch (Exception ex)
            {
                return (false, new List<string> { $"[Error reading remote SMB log: {ex.Message}]" });
            }
        }

        private static SidecarVerifyMode ParseSidecarVerifyMode(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return SidecarVerifyMode.FastHeaderAndHeal;

            string trimmed = raw.Trim();
            if (trimmed.Equals("DeepRecomputeAndHeal", StringComparison.OrdinalIgnoreCase))
                return SidecarVerifyMode.DeepCryptographicAndHeal;
            if (trimmed.Equals("AuditOnlyNoChanges", StringComparison.OrdinalIgnoreCase))
                return SidecarVerifyMode.DryRunReportOnly;

            return Enum.TryParse<SidecarVerifyMode>(trimmed, ignoreCase: true, out var parsed)
                ? parsed
                : SidecarVerifyMode.FastHeaderAndHeal;
        }
    }
}
