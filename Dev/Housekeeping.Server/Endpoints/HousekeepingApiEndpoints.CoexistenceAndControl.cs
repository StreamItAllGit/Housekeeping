// @module: Housekeeping.Server.Endpoints.HousekeepingApiEndpoints.CoexistenceAndControl
// @purpose: Dual-mode pipeline execution/stop endpoints (stop-job vs stop-queue) and Sister-App Coexistence status/preset endpoints.
// @depends-on: System, System.Threading, System.Threading.Tasks, Housekeeping.Models, Housekeeping.Server.Hosting, Housekeeping.Services, Microsoft.AspNetCore.Builder, Microsoft.AspNetCore.Http
// @exports: Housekeeping.Server.Endpoints.HousekeepingApiEndpoints (partial: MapCoexistenceAndControlEndpoints)

using System;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Housekeeping.Models;
using Housekeeping.Server.Hosting;
using Housekeeping.Services;

namespace Housekeeping.Server.Endpoints
{
    public static partial class HousekeepingApiEndpoints
    {
        private static void MapCoexistenceAndControlEndpoints(this WebApplication app, RouteGroupBuilder api)
        {
            // ==================== PIPELINE ORCHESTRATION & DUAL-MODE STOP CONTROL ==================== //
            api.MapGet("/pipeline/state", (HousekeepingRuntimeContainer rt) =>
                Results.Ok(rt.Orchestrator.GetStateSnapshot()));

            api.MapPost("/pipeline/run-queue", ([FromBody] RunQueuePayload? body, HousekeepingRuntimeContainer rt) =>
            {
                var (accepted, started, queued, message) = rt.Orchestrator.StartOrEnqueueQueueRun(
                    body?.QueueId,
                    isDryRun: body?.IsDryRun ?? false,
                    triggerSource: (body?.IsDryRun ?? false) ? "DryRun" : "Manual",
                    forceCloudResync: body?.ForceCloudResync ?? false,
                    enqueueIfBusy: body?.EnqueueIfBusy ?? true);

                return accepted
                    ? Results.Ok(new { ok = true, started, queued, message, state = rt.Orchestrator.GetStateSnapshot() })
                    : Results.Conflict(new { ok = false, started = false, queued = false, error = message, state = rt.Orchestrator.GetStateSnapshot() });
            });

            api.MapPost("/pipeline/run-job/{jobId}", (string jobId, [FromBody] RunJobPayload? body, HousekeepingRuntimeContainer rt) =>
            {
                var (accepted, started, queued, message) = rt.Orchestrator.StartOrEnqueueSingleJobRun(
                    jobId,
                    isDryRun: body?.IsDryRun ?? false,
                    forceCloudResync: body?.ForceCloudResync ?? false,
                    triggerSource: null,
                    enqueueIfBusy: body?.EnqueueIfBusy ?? true);

                return accepted
                    ? Results.Ok(new { ok = true, started, queued, message, state = rt.Orchestrator.GetStateSnapshot() })
                    : Results.Conflict(new { ok = false, started = false, queued = false, error = message, state = rt.Orchestrator.GetStateSnapshot() });
            });

            api.MapGet("/pipeline/pending-runs", (HousekeepingRuntimeContainer rt) =>
                Results.Ok(rt.Orchestrator.GetPendingRuns()));

            api.MapDelete("/pipeline/pending-runs/{itemKey}", (string itemKey, HousekeepingRuntimeContainer rt) =>
            {
                string decoded = Uri.UnescapeDataString(itemKey ?? "");
                bool removed = rt.Orchestrator.RemovePendingRun(decoded);
                return Results.Ok(new { ok = true, removed, state = rt.Orchestrator.GetStateSnapshot() });
            });

            api.MapPost("/pipeline/pending-runs/clear", (HousekeepingRuntimeContainer rt) =>
            {
                int cleared = rt.Orchestrator.ClearPendingRuns();
                return Results.Ok(new { ok = true, cleared, state = rt.Orchestrator.GetStateSnapshot() });
            });

            api.MapPost("/pipeline/pause", (HousekeepingRuntimeContainer rt) =>
            {
                bool paused = rt.Orchestrator.TogglePause();
                return Results.Ok(new { ok = true, isPaused = paused, state = rt.Orchestrator.GetStateSnapshot() });
            });

            api.MapPost("/pipeline/suspend", (HousekeepingRuntimeContainer rt) =>
            {
                bool suspended = rt.Orchestrator.ToggleSuspendProcess();
                return Results.Ok(new { ok = true, isSuspended = suspended, state = rt.Orchestrator.GetStateSnapshot() });
            });

            var stopCurrentJobHandler = (HousekeepingRuntimeContainer rt) =>
            {
                bool stopped = rt.Orchestrator.StopCurrentJob();
                return Results.Ok(new { ok = true, stoppedJob = stopped, state = rt.Orchestrator.GetStateSnapshot() });
            };

            var stopEntireQueueHandler = (HousekeepingRuntimeContainer rt) =>
            {
                rt.Orchestrator.StopEntireQueue();
                return Results.Ok(new { ok = true, stoppedQueue = true, state = rt.Orchestrator.GetStateSnapshot() });
            };

            api.MapPost("/pipeline/stop-job", stopCurrentJobHandler);
            app.MapPost("/api/pipeline/stop-job", stopCurrentJobHandler);
            api.MapPost("/pipeline/stop-queue", stopEntireQueueHandler);
            app.MapPost("/api/pipeline/stop-queue", stopEntireQueueHandler);
            api.MapPost("/pipeline/stop", stopEntireQueueHandler);

            api.MapPost("/pipeline/interrupted/resume", ([FromBody] ResumeInterruptedPayload? body, HousekeepingRuntimeContainer rt) =>
            {
                var (started, message) = rt.Orchestrator.ResumeInterruptedRun(body?.RestartFullQueue ?? false);
                return started
                    ? Results.Ok(new { ok = true, message, state = rt.Orchestrator.GetStateSnapshot() })
                    : Results.BadRequest(new { ok = false, error = message });
            });

            api.MapPost("/pipeline/interrupted/dismiss", (HousekeepingRuntimeContainer rt) =>
            {
                rt.Orchestrator.DismissInterruptedRun();
                return Results.Ok(new { ok = true, state = rt.Orchestrator.GetStateSnapshot() });
            });

            api.MapPost("/pipeline/post-power", ([FromBody] PostPowerPayload body, HousekeepingRuntimeContainer rt) =>
            {
                string mode = (body?.Action ?? "StayAwake").Trim();
                if (mode is not ("StayAwake" or "Sleep" or "Shutdown"))
                    mode = "StayAwake";
                if (!rt.ConfigService.MutateAndSave(cfg => cfg.PostRunPowerAction = mode, out string? err))
                    return Results.BadRequest(new { error = err ?? "Failed to update post-run power action." });
                return Results.Ok(new { ok = true, postRunPowerAction = mode });
            });

            api.MapGet("/pipeline/pending-swaps", (HousekeepingRuntimeContainer rt) =>
                Results.Ok(rt.PendingSwaps.GetAll()));

            api.MapPost("/pipeline/pending-swaps/retry", async (HousekeepingRuntimeContainer rt) =>
            {
                int committed = await rt.Orchestrator.RetryPendingSwapsAsync();
                return Results.Ok(new { ok = true, committed, remaining = rt.PendingSwaps.Count });
            });

            api.MapPost("/pipeline/pending-swaps/discard", (HousekeepingRuntimeContainer rt) =>
            {
                rt.Orchestrator.DiscardPendingSwaps();
                return Results.Ok(new { ok = true, remaining = rt.PendingSwaps.Count });
            });

            // ==================== SISTER-APP COEXISTENCE STATUS & PRESET ALIGNMENT ==================== //
            var getCoexistenceStatus = async (HousekeepingRuntimeContainer rt, CancellationToken ct) =>
            {
                var status = await rt.Coexistence.GetStatusAsync(ct);
                return Results.Ok(status);
            };

            var postApplyCoexistence = ([FromBody] ApplyCoexistencePresetRequest? body, HousekeepingRuntimeContainer rt) =>
            {
                var (ok, message) = rt.Coexistence.ApplyRecommendationPreset(body?.Preset ?? "", body?.EnableAutoAlign, body?.JobId);
                if (!ok)
                    return Results.BadRequest(new { ok = false, error = message });

                var cfg = rt.ConfigService.Config.DeepClone();
                cfg.WebServer.AdminPasswordHash = "";
                cfg.WebServer.AdminPasswordSalt = "";
                return Results.Ok(new
                {
                    ok = true,
                    preset = body?.Preset ?? "",
                    message,
                    config = cfg,
                    snapshotsCount = rt.ConfigService.ListSnapshots().Count
                });
            };

            api.MapGet("/coexistence/status", getCoexistenceStatus);
            app.MapGet("/api/coexistence/status", getCoexistenceStatus);
            api.MapPost("/coexistence/apply-recommendations", postApplyCoexistence);
            app.MapPost("/api/coexistence/apply-recommendations", postApplyCoexistence);
        }
    }
}
