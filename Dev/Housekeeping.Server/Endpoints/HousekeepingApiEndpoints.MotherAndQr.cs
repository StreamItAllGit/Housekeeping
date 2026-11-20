// @module: Housekeeping.Server.Endpoints.HousekeepingApiEndpoints.MotherAndQr
// @purpose: Mother disk arbitration hooks, Tailscale Serve HTTPS management, telemetry/manifest endpoints, and offline ISO/IEC 18004 SVG QR pairing routes.
// @depends-on: System, System.Threading, Housekeeping.Server.Hosting, Housekeeping.Server.Services, Housekeeping.Services, Microsoft.AspNetCore.Builder, Microsoft.AspNetCore.Http, Microsoft.AspNetCore.Mvc, Microsoft.AspNetCore.Routing
// @exports: DeferSchedulerPayload, Housekeeping.Server.Endpoints.HousekeepingApiEndpoints (partial: MapMotherAndQrEndpoints)

using System;
using System.Threading;
using Housekeeping.Server.Hosting;
using Housekeeping.Server.Services;
using Housekeeping.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Housekeeping.Server.Endpoints
{
    public sealed record DeferSchedulerPayload(int Minutes = 15);

    public static partial class HousekeepingApiEndpoints
    {
        private static void MapMotherAndQrEndpoints(this WebApplication app, RouteGroupBuilder api)
        {
            // ==================== MOTHER DISK ARBITRATION & TELEMETRY HOOKS ==================== //
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
            api.MapGet("/system/telemetry", (HousekeepingRuntimeContainer rt) =>
                Results.Ok(rt.Telemetry.GetLocalPerformanceStats()));

            // ==================== TAILSCALE SERVE HTTPS, PWA & OFFLINE SVG QR ==================== //
            api.MapGet("/system/tailscale-pwa", async (HousekeepingRuntimeContainer rt, TailscalePwaService tsPwa, CancellationToken ct) =>
            {
                int port = rt.ConfigService.Config.WebServer.Port;
                var status = await tsPwa.GetStatusAsync(port, ct);
                return Results.Ok(status);
            });

            api.MapPost("/system/tailscale-pwa/enable", async (HousekeepingRuntimeContainer rt, TailscalePwaService tsPwa, CancellationToken ct) =>
            {
                int port = rt.ConfigService.Config.WebServer.Port;
                if (rt.ConfigService.IsIsolatedOverride)
                {
                    return Results.Ok(new TailscalePwaStatusDto
                    {
                        Simulated = true,
                        StatusMessage = "[Isolated] Tailscale Serve host mutation is suppressed in isolated mode."
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
                if (rt.ConfigService.IsIsolatedOverride)
                {
                    return Results.Ok(new TailscalePwaStatusDto
                    {
                        Simulated = true,
                        StatusMessage = "[Isolated] Tailscale Serve host mutation is suppressed in isolated mode."
                    });
                }
                var status = await tsPwa.DisableHttpsServeAsync(port, ct);
                rt.Orchestrator.Log("INFO", "[Tailscale PWA] Reset local tailscale serve HTTPS proxy configuration.");
                return Results.Ok(status);
            });

            api.MapGet("/system/tailscale-qr", async (
                HttpContext ctx,
                [FromQuery] string? url,
                [FromQuery] string? variant,
                [FromQuery] string? format,
                HousekeepingRuntimeContainer rt,
                TailscalePwaService tsPwa,
                CancellationToken ct) =>
            {
                try
                {
                    int port = rt.ConfigService.Config.WebServer.Port;
                    string? requestOrigin = ctx.Request.Host.HasValue
                        ? $"{ctx.Request.Scheme}://{ctx.Request.Host}"
                        : null;
                    string? motherGateway = Environment.GetEnvironmentVariable("MOTHER_GATEWAY_URL");
                    if (string.IsNullOrWhiteSpace(motherGateway) && ctx.Request.PathBase.HasValue && requestOrigin != null)
                        motherGateway = requestOrigin;

                    var qr = await tsPwa.BuildQrReportAsync(port, url, requestOrigin, motherGateway, ct);
                    if (string.Equals(format, "svg", StringComparison.OrdinalIgnoreCase))
                    {
                        string svg = string.Equals(variant, "direct", StringComparison.OrdinalIgnoreCase)
                            ? qr.DirectQrSvg
                            : qr.MotherQrSvg;
                        ctx.Response.Headers.CacheControl = "no-store";
                        return Results.Content(svg, "image/svg+xml; charset=utf-8");
                    }
                    return Results.Ok(qr);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            });

            api.MapGet("/system/tailscale-qr.svg", async (
                HttpContext ctx,
                [FromQuery] string? url,
                [FromQuery] string? variant,
                HousekeepingRuntimeContainer rt,
                TailscalePwaService tsPwa,
                CancellationToken ct) =>
            {
                try
                {
                    int port = rt.ConfigService.Config.WebServer.Port;
                    string? requestOrigin = ctx.Request.Host.HasValue
                        ? $"{ctx.Request.Scheme}://{ctx.Request.Host}"
                        : null;
                    string? motherGateway = Environment.GetEnvironmentVariable("MOTHER_GATEWAY_URL");
                    var qr = await tsPwa.BuildQrReportAsync(port, url, requestOrigin, motherGateway, ct);
                    string svg = string.Equals(variant, "direct", StringComparison.OrdinalIgnoreCase)
                        ? qr.DirectQrSvg
                        : qr.MotherQrSvg;
                    ctx.Response.Headers.CacheControl = "no-store";
                    return Results.Content(svg, "image/svg+xml; charset=utf-8");
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            });
        }
    }
}
