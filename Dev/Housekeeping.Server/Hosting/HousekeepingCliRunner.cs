// @module: Housekeeping.Server.Hosting.HousekeepingCliRunner
// @purpose: Synchronous one-shot --status and --repair-config CLI handlers for batch wrappers and diagnostics.
// @depends-on: System, System.Diagnostics, System.IO, System.Net.Http, System.Text.Json, Housekeeping.Services
// @exports: Housekeeping.Server.Hosting.HousekeepingCliRunner

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using Foundation.Core;
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
                    isolatedOverride: cli.IsIsolatedInvocation);

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
                    isIsolated = cfgSvc.IsIsolatedOverride,
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
                if (string.IsNullOrWhiteSpace(cli.CustomDataDirectory) &&
                    string.IsNullOrWhiteSpace(cli.CustomConfigFilePath) &&
                    string.Equals(Environment.GetEnvironmentVariable("APP_ISOLATED_TEST"), "1", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine("ERROR: Fail-closed isolation violation: APP_ISOLATED_TEST requires --data-dir or --config.");
                    Console.Error.Flush();
                    return 2;
                }

                var cfgSvc = new ConfigService(
                    customBaseDir: cli.CustomDataDirectory,
                    customConfigFilePath: cli.CustomConfigFilePath,
                    customLogsDir: cli.CustomLogsDirectory,
                    isolatedOverride: cli.IsIsolatedInvocation);

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

        public static int RunDoctor(HousekeepingParsedCli cli)
        {
            var checks = new List<object>();
            var tools = new List<object>();
            bool hasFailure = false;
            bool hasWarning = false;

            void AddCheck(string id, string name, string status, string detail)
            {
                if (status == "fail") hasFailure = true;
                else if (status == "warn") hasWarning = true;
                checks.Add(new { id, name, status, detail });
            }

            string version = RemoteNodeDeploymentService.GetLocalAppVersion();
            SandboxResolution sandbox = SandboxResolution.Inactive;
            ConfigService? cfgSvc = null;

            try
            {
                if (string.IsNullOrWhiteSpace(cli.CustomDataDirectory) &&
                    string.IsNullOrWhiteSpace(cli.CustomConfigFilePath) &&
                    string.Equals(Environment.GetEnvironmentVariable(SandboxEnvironmentSeam.EnvIsolatedTest), "1", StringComparison.OrdinalIgnoreCase))
                {
                    AddCheck("isolation", "Test Isolation Guard", "fail", "APP_ISOLATED_TEST=1 requires --data-dir or --config.");
                    return EmitDoctorReport(version, sandbox, null, checks, tools, "fail");
                }

                sandbox = SandboxEnvironmentSeam.ResolveCurrent();
                AddCheck("sandbox-integrity", "Sandbox Integrity", "pass",
                    sandbox.IsSandboxActive ? $"Active sandbox validated at '{sandbox.SandboxRoot}'." : "Sandbox inactive (standard host/isolated mode).");

                if (sandbox.IsSandboxActive)
                {
                    AssertExplicitOverridesContained(cli, sandbox);
                }

                cfgSvc = new ConfigService(
                    customBaseDir: cli.CustomDataDirectory,
                    customConfigFilePath: cli.CustomConfigFilePath,
                    customLogsDir: cli.CustomLogsDirectory,
                    isolatedOverride: cli.IsIsolatedInvocation);

                bool corruptLoad = !string.IsNullOrWhiteSpace(cfgSvc.LastMigrationMessage) &&
                                   cfgSvc.LastMigrationMessage.StartsWith("Warning: Unreadable settings.json", StringComparison.OrdinalIgnoreCase);
                bool validSchema = ConfigService.ValidateConfig(cfgSvc.Config, out List<string> cfgErrors);
                bool cfgExists = File.Exists(cfgSvc.ActiveConfigFilePath);
                if (corruptLoad || !validSchema || !cfgExists)
                {
                    string reason = corruptLoad ? cfgSvc.LastMigrationMessage! : (!cfgExists ? "settings.json missing." : string.Join("; ", cfgErrors));
                    AddCheck("config-health", "Configuration Health", "fail", reason);
                }
                else
                {
                    AddCheck("config-health", "Configuration Health", "pass", $"Valid v{cfgSvc.Config.ConfigVersion} config at '{cfgSvc.ActiveConfigFilePath}'.");
                }

                bool contained = CheckDataAndLogsContainment(cfgSvc, sandbox, out string containmentDetail);
                AddCheck("data-logs-containment", "Data & Logs Containment", contained ? "pass" : "fail", containmentDetail);

                string logSecretPath = Path.Combine(cfgSvc.ActiveLogsDir, ".hk_cookie_secret.key");
                bool distinctLogDir = !string.Equals(
                    Path.GetFullPath(cfgSvc.ActiveLogsDir).TrimEnd('\\', '/'),
                    Path.GetFullPath(cfgSvc.DataDirectory).TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase);
                if (distinctLogDir && File.Exists(logSecretPath))
                {
                    AddCheck("cookie-secret-location", "Signing Key Containment", "warn", $"Legacy signing key found in logs directory '{logSecretPath}'.");
                }
                else
                {
                    AddCheck("cookie-secret-location", "Signing Key Containment", "pass", "No cryptographic signing keys stored in logs directory.");
                }

                foreach (ToolResolutionResult tool in ProbeDoctorTools(cfgSvc))
                {
                    tools.Add(new
                    {
                        toolId = tool.ToolId,
                        isAvailable = tool.IsAvailable,
                        source = tool.Source.ToString(),
                        resolvedPath = tool.ResolvedPath,
                        diagnostic = tool.DiagnosticMessage
                    });
                    AddCheck($"tool-{tool.ToolId}", $"Tool Readiness ({tool.ToolId})", tool.IsAvailable ? "pass" : "warn", tool.DiagnosticMessage);
                }

                string overall = hasFailure ? "fail" : (hasWarning ? "warn" : "pass");
                return EmitDoctorReport(version, sandbox, cfgSvc, checks, tools, overall);
            }
            catch (Exception ex)
            {
                AddCheck("doctor-exception", "Doctor Preflight", "fail", ex.Message);
                return EmitDoctorReport(version, sandbox, cfgSvc, checks, tools, "fail");
            }
        }

        private static void AssertExplicitOverridesContained(HousekeepingParsedCli cli, SandboxResolution sandbox)
        {
            if (!string.IsNullOrWhiteSpace(cli.CustomDataDirectory))
                SandboxEnvironmentSeam.AssertSandboxPathIfActive(cli.CustomDataDirectory, sandbox);
            if (!string.IsNullOrWhiteSpace(cli.CustomConfigFilePath))
                SandboxEnvironmentSeam.AssertSandboxPathIfActive(cli.CustomConfigFilePath, sandbox);
            if (!string.IsNullOrWhiteSpace(cli.CustomLogsDirectory))
                SandboxEnvironmentSeam.AssertSandboxPathIfActive(cli.CustomLogsDirectory, sandbox);
        }

        private static bool CheckDataAndLogsContainment(ConfigService cfgSvc, SandboxResolution sandbox, out string detail)
        {
            if (sandbox.IsSandboxActive)
            {
                string[] paths = { cfgSvc.DataDirectory, cfgSvc.ActiveConfigFilePath, cfgSvc.ActiveLogsDir, cfgSvc.SnapshotsDir };
                foreach (string p in paths)
                {
                    if (!SandboxEnvironmentSeam.IsPathWithinSandbox(p, sandbox))
                    {
                        detail = $"Path '{p}' escapes active sandbox '{sandbox.SandboxRoot}'.";
                        return false;
                    }
                }
            }

            if (!VerifyDirectoryWritable(cfgSvc.DataDirectory, out string dataErr))
            {
                detail = $"Data directory not writable: {dataErr}";
                return false;
            }
            if (!VerifyDirectoryWritable(cfgSvc.ActiveLogsDir, out string logErr))
            {
                detail = $"Logs directory not writable: {logErr}";
                return false;
            }

            detail = $"Verified writable data='{cfgSvc.DataDirectory}' and logs='{cfgSvc.ActiveLogsDir}'.";
            return true;
        }

        private static bool VerifyDirectoryWritable(string dir, out string error)
        {
            error = string.Empty;
            string probe = Path.Combine(dir, $".hk_doctor_{Guid.NewGuid():N}.tmp");
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(probe, "ok");
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                try { if (File.Exists(probe)) File.Delete(probe); } catch (Exception) { /* best-effort probe cleanup */ }
            }
        }

        private static IReadOnlyList<ToolResolutionResult> ProbeDoctorTools(ConfigService cfgSvc)
        {
            string dataToolsDir = Path.Combine(cfgSvc.DataDirectory, "tools");
            string appToolsDir = Path.Combine(cfgSvc.InstallRootDirectory, "app", "tools");
            string appDir = Path.Combine(cfgSvc.InstallRootDirectory, "app");
            string[] sharedExtras = { AppContext.BaseDirectory, dataToolsDir, appToolsDir, appDir };

            string[] mediaInfoExtras = sharedExtras.Concat(new[]
            {
                Path.Combine(KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "MediaInfo"),
                Path.Combine(KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "MediaInfo")
            }).ToArray();

            return new[]
            {
                ToolResolver.ResolveTool("rclone", new[] { "rclone.exe" }, cfgSvc.Config.ToolPaths.RclonePath, cfgSvc.InstallRootDirectory, sharedExtras),
                ToolResolver.ResolveTool("ffmpeg", new[] { "ffmpeg.exe" }, null, cfgSvc.InstallRootDirectory, sharedExtras),
                ToolResolver.ResolveTool("mediainfo", new[] { "MediaInfo.dll" }, null, cfgSvc.InstallRootDirectory, mediaInfoExtras),
                ToolResolver.ResolveTool("7z", new[] { "7z.exe" }, cfgSvc.Config.ToolPaths.SevenZipPath, cfgSvc.InstallRootDirectory, sharedExtras),
                ToolResolver.ResolveTool("handbrake", new[] { "HandBrakeCLI.exe", "handbrakecli.exe" }, cfgSvc.Config.ToolPaths.HandBrakeCliPath, cfgSvc.InstallRootDirectory, sharedExtras)
            };
        }

        private static int EmitDoctorReport(
            string version,
            SandboxResolution sandbox,
            ConfigService? cfgSvc,
            List<object> checks,
            List<object> tools,
            string overall)
        {
            bool ok = !string.Equals(overall, "fail", StringComparison.OrdinalIgnoreCase);
            var payload = new
            {
                ok,
                overall,
                app = "Housekeeping",
                version,
                sandboxActive = sandbox.IsSandboxActive,
                sandboxRoot = sandbox.SandboxRoot,
                isIsolated = cfgSvc?.IsIsolatedOverride ?? false,
                dataDirectory = cfgSvc?.DataDirectory,
                logsDirectory = cfgSvc?.ActiveLogsDir,
                configPath = cfgSvc?.ActiveConfigFilePath,
                checks,
                tools,
                checkedAtUtc = DateTime.UtcNow
            };

            string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            Console.Out.WriteLine(json);
            Console.Out.Flush();
            if (!ok)
            {
                Console.Error.WriteLine($"Doctor failed (overall={overall}).");
                Console.Error.Flush();
                return 2;
            }
            return 0;
        }
    }
}
