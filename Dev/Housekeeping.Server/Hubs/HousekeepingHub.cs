using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Housekeeping.Models;
using Housekeeping.Server.Hosting;
using Microsoft.AspNetCore.SignalR;

namespace Housekeeping.Server.Hubs
{
    public sealed class HousekeepingHub : Hub
    {
        public const string LocalGroupName = "node:local";

        private static readonly ConcurrentDictionary<string, string> ActiveConnectionTargets =
            new(StringComparer.Ordinal);

        private readonly HousekeepingRuntimeContainer _runtime;

        public HousekeepingHub(HousekeepingRuntimeContainer runtime)
        {
            _runtime = runtime;
        }

        public static string NormalizeNodeId(string? targetNodeId)
        {
            return string.IsNullOrWhiteSpace(targetNodeId) ||
                   string.Equals(targetNodeId.Trim(), "local", StringComparison.OrdinalIgnoreCase)
                ? "local"
                : targetNodeId.Trim();
        }

        public static string GetGroupName(string? targetNodeId)
        {
            string normalized = NormalizeNodeId(targetNodeId);
            return string.Equals(normalized, "local", StringComparison.OrdinalIgnoreCase)
                ? LocalGroupName
                : $"node:{normalized.ToLowerInvariant()}";
        }

        public static IReadOnlyCollection<string> GetActiveRemoteNodeIds()
        {
            return ActiveConnectionTargets.Values
                .Where(v => !string.Equals(v, "local", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();

            string? queryTarget = Context.GetHttpContext()?.Request.Query["targetNodeId"].ToString();
            string targetNodeId = NormalizeNodeId(queryTarget);
            ActiveConnectionTargets[Context.ConnectionId] = targetNodeId;
            await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(targetNodeId));

            if (string.Equals(targetNodeId, "local", StringComparison.OrdinalIgnoreCase))
            {
                await SendLocalSnapshotToCallerAsync();
            }
        }

        public async Task SubscribeToNode(string? nodeId)
        {
            string nextTarget = NormalizeNodeId(nodeId);
            if (ActiveConnectionTargets.TryGetValue(Context.ConnectionId, out string? prevTarget))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupName(prevTarget));
            }

            ActiveConnectionTargets[Context.ConnectionId] = nextTarget;
            await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(nextTarget));

            if (string.Equals(nextTarget, "local", StringComparison.OrdinalIgnoreCase))
            {
                await SendLocalSnapshotToCallerAsync();
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            ActiveConnectionTargets.TryRemove(Context.ConnectionId, out _);
            await base.OnDisconnectedAsync(exception);
        }

        private async Task SendLocalSnapshotToCallerAsync()
        {
            var state = _runtime.Orchestrator.GetStateSnapshot();
            var telemetry = _runtime.Telemetry.GetLocalPerformanceStats();
            var allReports = _runtime.ComplianceReports.GetAllReports();
            int openReports = allReports.Count(r => r.ResolutionStatus == ComplianceReportResolutionStatus.Open);

            await Clients.Caller.SendAsync("PipelineState", state);
            await Clients.Caller.SendAsync("TelemetrySnapshot", new
            {
                Telemetry = telemetry,
                OpenComplianceCount = openReports,
                GlobalNextRunText = state.GlobalNextRunStatusText,
                GlobalNextRunUtc = state.GlobalNextRunUtc,
                GlobalNextRunQueueName = state.GlobalNextRunQueueName,
                PendingSwapCount = state.PendingSwapCount,
                PendingSwapSampleFile = state.PendingSwapSampleFile
            });
        }
    }
}
