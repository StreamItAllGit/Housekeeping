// @module: Housekeeping.Server.Hosting.HousekeepingCliRunner
// @purpose: Synchronous one-shot --status and --repair-config CLI handlers for batch wrappers and headless diagnostics.
// @depends-on: System, System.Diagnostics, System.IO, System.Net.Http, System.Text.Json, Housekeeping.Services
// @exports: Housekeeping.Server.Hosting.HousekeepingCliRunner

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Housekeeping.Services;

namespace Housekeeping.Server.Hosting
{
    public static class HousekeepingCliRunner
    {
        public static int RunStatusCheck(HousekeepingParsedCli cli)
        {
            try
            {
                var cfgSvc = new ConfigService(
                    customBaseDir: cli.CustomDataDirectory,
                    customConfigFilePath: cli.CustomConfigFilePath,
                    customLogsDir: cli.CustomLogsDirectory,
                    isTestMode: cli.TestMode);

                int port = cli.PortOverride ?? cfgSvc.Config.WebServer.Port;
                bool httpOnline = false;
                bool isRunning = false;
                string activeVersion = RemoteNodeDeploymentService.GetLocalAppVersion();

                try
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(800) };
                    using var resp = client.GetAsync($"http://127.0.0.1:{port}/api/v1/health").GetAwaiter().GetResult();
                    if (resp.IsSuccessStatusCode)
                    {
                        httpOnline = true;
                        string json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("isRunning", out var ir) && ir.ValueKind is JsonValueKind.True or JsonValueKind.False)
                            isRunning = ir.GetBoolean();
                        if (doc.RootElement.TryGetProperty("version", out var vp) && vp.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(vp.GetString()))
                            activeVersion = vp.GetString()!;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[HousekeepingCliRunner] Local HTTP status probe skipped (port {port}): {ex.Message}");
                }

                var pendingSwaps = new PendingSwapService(Path.Combine(cfgSvc.DataDirectory, "pending_swaps.json"));
                string heartbeatPath = Path.Combine(cfgSvc.ActiveLogsDir, "telemetry_heartbeat.json");
                DateTime? lastHeartbeatUtc = File.Exists(heartbeatPath)
                    ? File.GetLastWriteTimeUtc(heartbeatPath)
                    : null;

                var payload = new
                {
                    app = "Housekeeping",
                    version = activeVersion,
                    daemonOnline = httpOnline,
                    isRunning,
                    port,
                    isTestMode = cfgSvc.IsTestMode,
                    isSandboxed = cfgSvc.IsSandboxed,
                    configPath = cfgSvc.ActiveConfigFilePath,
                    dataDirectory = cfgSvc.DataDirectory,
                    logsDirectory = cfgSvc.ActiveLogsDir,
                    definedJobsCount = cfgSvc.Config.DefinedJobs.Count,
                    namedQueuesCount = cfgSvc.Config.NamedQueues.Count,
                    pendingSwapsCount = pendingSwaps.Count,
                    lastHeartbeatUtc,
                    checkedAtUtc = DateTime.UtcNow
                };

                Console.Out.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
                Console.Out.Flush();
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR: Status check failed: {ex.Message}");
                Console.Error.Flush();
                return 1;
            }
        }

        public static int RunRepairConfig(HousekeepingParsedCli cli)
        {
            try
            {
                if (cli.TestMode && string.IsNullOrWhiteSpace(cli.CustomDataDirectory) && string.IsNullOrWhiteSpace(cli.CustomConfigFilePath))
                {
                    Console.Error.WriteLine("ERROR: Fail-closed sandbox violation: --test-mode requires --data-dir or --config.");
                    Console.Error.Flush();
                    return 2;
                }

                var cfgSvc = new ConfigService(
                    customBaseDir: cli.CustomDataDirectory,
                    customConfigFilePath: cli.CustomConfigFilePath,
                    customLogsDir: cli.CustomLogsDirectory,
                    isTestMode: cli.TestMode);

                bool ok = cfgSvc.RepairAndValidateConfig(out string summary);
                var payload = new
                {
                    ok,
                    app = "Housekeeping",
                    version = RemoteNodeDeploymentService.GetLocalAppVersion(),
                    configPath = cfgSvc.ActiveConfigFilePath,
                    dataDirectory = cfgSvc.DataDirectory,
                    summary,
                    repairedAtUtc = DateTime.UtcNow
                };

                if (ok)
                {
                    Console.Out.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
                    Console.Out.Flush();
                    return 0;
                }

                Console.Error.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
                Console.Error.Flush();
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR: Configuration repair failed: {ex.Message}");
                Console.Error.Flush();
                return 1;
            }
        }
    }
}
