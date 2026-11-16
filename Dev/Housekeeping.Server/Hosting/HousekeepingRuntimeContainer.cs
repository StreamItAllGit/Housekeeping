using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Housekeeping.Services;

namespace Housekeeping.Server.Hosting
{
    public interface INativeDialogBridge
    {
        bool IsAvailable { get; }
        Task<string?> PickFolderAsync(string? initialPath, string? description);
        Task<string?> PickFileAsync(string? initialPath, string? filter, string? title);
        void RequestOpenStandaloneWindow();
    }

    public sealed class NullNativeDialogBridge : INativeDialogBridge
    {
        public bool IsAvailable => false;
        public Task<string?> PickFolderAsync(string? initialPath, string? description) => Task.FromResult<string?>(null);
        public Task<string?> PickFileAsync(string? initialPath, string? filter, string? title) => Task.FromResult<string?>(null);
        public void RequestOpenStandaloneWindow() { }
    }

    /// <summary>
    /// Owns the full Housekeeping 3.0 backend runtime graph for either standalone server execution,
    /// in-process System Tray + WebView2 daemon hosting, or isolated integration test hosts.
    /// </summary>
    public sealed class HousekeepingRuntimeContainer : IDisposable
    {
        public ConfigService ConfigService { get; }
        public TerminalLogBufferService LogBuffer { get; }
        public InteractiveCliSessionService InteractiveCli { get; }
        public DependencyCheckService Dependencies { get; }
        public PendingSwapService PendingSwaps { get; }
        public ComplianceReportService ComplianceReports { get; }
        public SystemPerformanceTelemetryService Telemetry { get; }
        public GreenroomSyncIntegrationService GreenroomSync { get; }
        public SisterAppCoexistenceService Coexistence { get; }
        public PipelineOrchestratorService Orchestrator { get; }
        public HttpClient FleetHttpClient { get; }
        public INativeDialogBridge NativeDialogs { get; set; }

        private bool _disposed;

        public HousekeepingRuntimeContainer(
            string? customDataDirectory = null,
            HttpClient? customGreenroomHttpClient = null,
            INativeDialogBridge? nativeDialogs = null,
            bool startBackgroundTimers = true,
            string? customConfigFilePath = null,
            string? customLogsDirectory = null,
            bool isolatedOverride = false,
            bool forceDryRun = false)
        {
            ConfigService = new ConfigService(
                customBaseDir: customDataDirectory,
                customConfigFilePath: customConfigFilePath,
                customLogsDir: customLogsDirectory,
                isolatedOverride: isolatedOverride);
            LogBuffer = new TerminalLogBufferService(ConfigService.ActiveLogsDir);
            CrashBundleService.RegisterGlobalHandlers(
                () => ConfigService.ActiveLogsDir,
                RemoteNodeDeploymentService.GetLocalAppVersion(),
                ConfigService.IsIsolatedOverride,
                archetype: "2-tray-companion",
                getLogBuffer: () => LogBuffer);
            InteractiveCli = new InteractiveCliSessionService(LogBuffer, ConfigService.DataDirectory);
            Dependencies = new DependencyCheckService(() => ConfigService.Config, ConfigService.DataDirectory);
            PendingSwaps = new PendingSwapService(Path.Combine(ConfigService.DataDirectory, "pending_swaps.json"));
            ComplianceReports = new ComplianceReportService(Path.Combine(ConfigService.DataDirectory, "compliance_reports.json"));
            Telemetry = new SystemPerformanceTelemetryService(ConfigService.ActiveLogsDir);
            FleetHttpClient = new HttpClient(new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(25),
                ConnectTimeout = TimeSpan.FromSeconds(5)
            })
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            FleetHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                $"Housekeeping/{RemoteNodeDeploymentService.GetLocalAppVersion()} (+{Environment.MachineName}; Suite/mother)");
            GreenroomSync = new GreenroomSyncIntegrationService(ConfigService, customGreenroomHttpClient);
            Coexistence = new SisterAppCoexistenceService(
                ConfigService,
                FleetHttpClient,
                (tag, msg) => LogBuffer.Append(tag, msg, ConfigService.Config));
            Orchestrator = new PipelineOrchestratorService(
                ConfigService,
                LogBuffer,
                Dependencies,
                PendingSwaps,
                ComplianceReports,
                Telemetry,
                GreenroomSync);
            if (forceDryRun)
            {
                Orchestrator.ForceDryRun = true;
            }
            NativeDialogs = nativeDialogs ?? new NullNativeDialogBridge();

            if (!string.IsNullOrWhiteSpace(ConfigService.LastMigrationMessage))
            {
                LogBuffer.Append("INFO", ConfigService.LastMigrationMessage, ConfigService.Config);
            }

            if (startBackgroundTimers)
            {
                Telemetry.Start();
                Orchestrator.StartBackgroundScheduler();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { Orchestrator.Dispose(); } catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[HousekeepingRuntimeContainer] Non-fatal error ignored: {__suppressedEx1.Message}"); }
            try { Telemetry.Stop(); } catch (System.Exception __suppressedEx2) { System.Diagnostics.Debug.WriteLine($"[HousekeepingRuntimeContainer] Non-fatal error ignored: {__suppressedEx2.Message}"); }
            try { GreenroomSync.Dispose(); } catch (System.Exception __suppressedEx3) { System.Diagnostics.Debug.WriteLine($"[HousekeepingRuntimeContainer] Non-fatal error ignored: {__suppressedEx3.Message}"); }
            try { FleetHttpClient.Dispose(); } catch (System.Exception __suppressedEx4) { System.Diagnostics.Debug.WriteLine($"[HousekeepingRuntimeContainer] Non-fatal error ignored: {__suppressedEx4.Message}"); }
            try { LogBuffer.Dispose(); } catch (System.Exception __suppressedEx5) { System.Diagnostics.Debug.WriteLine($"[HousekeepingRuntimeContainer] Non-fatal error ignored: {__suppressedEx5.Message}"); }
        }
    }
}
