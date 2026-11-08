using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Models;
using Housekeeping.Server.Hubs;
using Housekeeping.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;

namespace Housekeeping.Server.Hosting
{
    /// <summary>
    /// Bridges Housekeeping.Core event streams (50 ms coalesced terminal log batches, subprocess progress,
    /// pipeline state transitions, compliance KPI updates, and 2 s telemetry/next-run ticks) to node-scoped SignalR groups.
    /// </summary>
    public sealed class RealtimeBroadcastHostedService : BackgroundService
    {
        private static readonly JsonSerializerOptions CaseInsensitiveJson = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HousekeepingRuntimeContainer _runtime;
        private readonly IHubContext<HousekeepingHub> _hubContext;

        public RealtimeBroadcastHostedService(
            HousekeepingRuntimeContainer runtime,
            IHubContext<HousekeepingHub> hubContext)
        {
            _runtime = runtime;
            _hubContext = hubContext;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            void OnLogBatch(IReadOnlyList<TerminalLogEntry> batch)
            {
                _ = _hubContext.Clients.Group(HousekeepingHub.LocalGroupName).SendAsync("TerminalLogBatch", batch, CancellationToken.None);
            }

            void OnTerminalCleared()
            {
                _ = _hubContext.Clients.Group(HousekeepingHub.LocalGroupName).SendAsync("TerminalCleared", CancellationToken.None);
            }

            void OnPipelineState(PipelineRuntimeStateDto state)
            {
                _ = _hubContext.Clients.Group(HousekeepingHub.LocalGroupName).SendAsync("PipelineState", state, CancellationToken.None);
            }

            void OnProgress(ProcessProgressInfo progress)
            {
                _ = _hubContext.Clients.Group(HousekeepingHub.LocalGroupName).SendAsync("SubprocessProgress", progress, CancellationToken.None);
            }

            void OnReportsChanged()
            {
                var all = _runtime.ComplianceReports.GetAllReports();
                int open = all.Count(r => r.ResolutionStatus == ComplianceReportResolutionStatus.Open);
                _ = _hubContext.Clients.Group(HousekeepingHub.LocalGroupName).SendAsync("ComplianceSummary", new
                {
                    OpenCount = open,
                    TotalCount = all.Count
                }, CancellationToken.None);
            }

            _runtime.LogBuffer.OnLogBatch += OnLogBatch;
            _runtime.LogBuffer.OnCleared += OnTerminalCleared;
            _runtime.Orchestrator.OnStateChanged += OnPipelineState;
            _runtime.Orchestrator.OnProgressChanged += OnProgress;
            _runtime.ComplianceReports.ReportsChanged += OnReportsChanged;

            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    var currentConfig = _runtime.ConfigService.Config;
                    var telemetry = _runtime.Telemetry.GetLocalPerformanceStats();
                    var stateSnap = _runtime.Orchestrator.GetStateSnapshot();
                    var (nextUtc, nextStatus, nextQueue) = _runtime.Orchestrator.Scheduler.GetGlobalNextScheduleStatus(currentConfig);
                    var pending = _runtime.PendingSwaps.GetAll();
                    var reports = _runtime.ComplianceReports.GetAllReports();
                    int openReports = reports.Count(r => r.ResolutionStatus == ComplianceReportResolutionStatus.Open);

                    await _hubContext.Clients.Group(HousekeepingHub.LocalGroupName).SendAsync("TelemetrySnapshot", new
                    {
                        Telemetry = telemetry,
                        OpenComplianceCount = openReports,
                        GlobalNextRunText = nextStatus,
                        GlobalNextRunUtc = nextUtc,
                        GlobalNextRunQueueName = nextQueue,
                        PendingSwapCount = pending.Count,
                        PendingSwapSampleFile = pending.Count > 0 ? System.IO.Path.GetFileName(pending[0].TargetPath) : null,
                        SessionProcessed = stateSnap.SessionProcessed,
                        SessionSkipped = stateSnap.SessionSkipped,
                        SessionErrors = stateSnap.SessionErrors,
                        FooterStatusText = stateSnap.FooterStatusText,
                        InterruptedRun = stateSnap.InterruptedRun
                    }, stoppingToken);

                    var remoteTargets = HousekeepingHub.GetActiveRemoteNodeIds();
                    if (remoteTargets.Count > 0)
                    {
                        await BroadcastActiveRemoteNodesAsync(currentConfig, remoteTargets, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown
            }
            finally
            {
                _runtime.LogBuffer.OnLogBatch -= OnLogBatch;
                _runtime.LogBuffer.OnCleared -= OnTerminalCleared;
                _runtime.Orchestrator.OnStateChanged -= OnPipelineState;
                _runtime.Orchestrator.OnProgressChanged -= OnProgress;
                _runtime.ComplianceReports.ReportsChanged -= OnReportsChanged;
            }
        }

        private async Task BroadcastActiveRemoteNodesAsync(
            AppConfig currentConfig,
            IReadOnlyCollection<string> remoteNodeIds,
            CancellationToken ct)
        {
            var configuredNodes = currentConfig.RemoteNodes?.ToList() ?? new List<RemoteNodeConfig>();
            foreach (string remoteId in remoteNodeIds)
            {
                var node = configuredNodes.FirstOrDefault(n => string.Equals(n.Id, remoteId, StringComparison.OrdinalIgnoreCase));
                if (node == null)
                    continue;

                string group = HousekeepingHub.GetGroupName(node.Id);
                try
                {
                    PipelineRuntimeStateDto? remoteState = null;
                    if (node.PreferHttpApi && !string.IsNullOrWhiteSpace(node.HostOrIp))
                    {
                        string baseUrl = node.HostOrIp.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                            ? node.HostOrIp.TrimEnd('/')
                            : $"http://{node.HostOrIp}:{node.HttpApiPort}";
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        linked.CancelAfter(TimeSpan.FromMilliseconds(1500));
                        using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/v1/pipeline/state");
                        if (!string.IsNullOrWhiteSpace(node.ApiToken))
                            req.Headers.TryAddWithoutValidation("X-Housekeeping-Token", node.ApiToken.Trim());

                        using var resp = await _runtime.FleetHttpClient.SendAsync(req, linked.Token);
                        if (resp.IsSuccessStatusCode)
                        {
                            string json = await resp.Content.ReadAsStringAsync(linked.Token);
                            remoteState = JsonSerializer.Deserialize<PipelineRuntimeStateDto>(json, CaseInsensitiveJson);
                        }
                    }

                    if (remoteState != null)
                    {
                        await _hubContext.Clients.Group(group).SendAsync("PipelineState", remoteState, ct);
                    }

                    var remoteTelemetry = await _runtime.Telemetry.GetRemotePerformanceStatsAsync(node.Clone(), ct);
                    if (remoteTelemetry != null)
                    {
                        await _hubContext.Clients.Group(group).SendAsync("TelemetrySnapshot", new
                        {
                            Telemetry = remoteTelemetry,
                            OpenComplianceCount = 0,
                            GlobalNextRunText = remoteState?.GlobalNextRunStatusText ?? "Schedule: Remote",
                            GlobalNextRunUtc = remoteState?.GlobalNextRunUtc,
                            GlobalNextRunQueueName = remoteState?.GlobalNextRunQueueName,
                            PendingSwapCount = remoteState?.PendingSwapCount ?? 0,
                            PendingSwapSampleFile = remoteState?.PendingSwapSampleFile,
                            SessionProcessed = remoteState?.SessionProcessed ?? 0,
                            SessionSkipped = remoteState?.SessionSkipped ?? 0,
                            SessionErrors = remoteState?.SessionErrors ?? 0,
                            FooterStatusText = remoteState?.FooterStatusText ?? $"Connected to {node.Name}",
                            InterruptedRun = remoteState?.InterruptedRun
                        }, ct);
                    }
                }
                catch
                {
                    // Non-fatal remote polling failure; next 2s tick retries cleanly
                }
            }
        }
    }
}
