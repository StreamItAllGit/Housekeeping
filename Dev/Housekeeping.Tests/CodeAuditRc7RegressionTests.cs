using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Models;
using Housekeeping.Server.Hosting;
using Housekeeping.Server.Security;
using Housekeeping.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Housekeeping.Tests
{
    public sealed class CodeAuditRc7RegressionTests
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: true) }
        };

        private static int GetFreeLoopbackPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        private sealed class AuditServerHarness : IAsyncDisposable
        {
            public string DataDir { get; }
            public int Port { get; }
            public string BaseUrl => $"http://127.0.0.1:{Port}";
            public WebApplication App { get; }
            public HousekeepingRuntimeContainer Runtime { get; }
            public TieredAuthService AuthService { get; }
            public HttpClient Client { get; }

            private AuditServerHarness(
                string dataDir,
                int port,
                WebApplication app,
                HousekeepingRuntimeContainer runtime,
                TieredAuthService authService)
            {
                DataDir = dataDir;
                Port = port;
                App = app;
                Runtime = runtime;
                AuthService = authService;
                Client = new HttpClient(new HttpClientHandler { UseCookies = false })
                {
                    BaseAddress = new Uri(BaseUrl),
                    Timeout = TimeSpan.FromSeconds(15)
                };
            }

            public static async Task<AuditServerHarness> StartAsync()
            {
                string dataDir = Path.Combine(Path.GetTempPath(), $"hk_rc7_audit_{Guid.NewGuid():N}");
                Directory.CreateDirectory(dataDir);
                int port = GetFreeLoopbackPort();

                var app = HousekeepingServerHost.BuildWebApplication(
                    Array.Empty<string>(),
                    new HousekeepingServerOptions
                    {
                        PortOverride = port,
                        BindLanOverride = false,
                        CustomDataDirectory = dataDir,
                        StartBackgroundTimers = false
                    });

                await app.StartAsync();
                var runtime = app.Services.GetRequiredService<HousekeepingRuntimeContainer>();
                var auth = app.Services.GetRequiredService<TieredAuthService>();
                return new AuditServerHarness(dataDir, port, app, runtime, auth);
            }

            public async Task<AppConfig> FetchConfigAsync(string? tokenHeader = null)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/config");
                if (!string.IsNullOrEmpty(tokenHeader))
                    req.Headers.TryAddWithoutValidation("X-Housekeeping-Token", tokenHeader);

                using var resp = await Client.SendAsync(req);
                resp.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                return JsonSerializer.Deserialize<AppConfig>(doc.RootElement.GetProperty("config").GetRawText(), JsonOpts)!;
            }

            public async ValueTask DisposeAsync()
            {
                Client.Dispose();
                try { await App.StopAsync(); } catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[CodeAuditRc7RegressionTests] Non-fatal error ignored: {__suppressedEx1.Message}"); }
                try { await App.DisposeAsync(); } catch (System.Exception __suppressedEx2) { System.Diagnostics.Debug.WriteLine($"[CodeAuditRc7RegressionTests] Non-fatal error ignored: {__suppressedEx2.Message}"); }
                try { Runtime.Dispose(); } catch (System.Exception __suppressedEx3) { System.Diagnostics.Debug.WriteLine($"[CodeAuditRc7RegressionTests] Non-fatal error ignored: {__suppressedEx3.Message}"); }
                try
                {
                    if (Directory.Exists(DataDir))
                        Directory.Delete(DataDir, recursive: true);
                }
                catch (System.Exception __suppressedEx4) { System.Diagnostics.Debug.WriteLine($"[CodeAuditRc7RegressionTests] Non-fatal error ignored: {__suppressedEx4.Message}"); }
            }
        }

        [Fact]
        public async Task Sec001_RemoteCaller_CannotBypassRawShellRestriction_ViaImport_SnapshotRestore_OrBinaryPaths()
        {
            await using var harness = await AuditServerHarness.StartAsync();
            harness.AuthService.SetAdminPassword("AuditSecret#1");
            string nodeToken = harness.Runtime.ConfigService.Config.WebServer.NodeApiToken;

            // Create a valid snapshot while on localhost with RestrictRawShellToLocalhost enabled
            var baseCfg = await harness.FetchConfigAsync();
            baseCfg.NetworkMaxRetries = 4;
            baseCfg.WebServer.RestrictRawShellToLocalhost = true;
            using (var saveLocal = new HttpRequestMessage(HttpMethod.Put, "/api/v1/config")
            {
                Content = new StringContent(JsonSerializer.Serialize(baseCfg), Encoding.UTF8, "application/json")
            })
            {
                using var saveResp = await harness.Client.SendAsync(saveLocal);
                saveResp.EnsureSuccessStatusCode();
            }

            var snapshots = harness.Runtime.ConfigService.ListSnapshots();
            Assert.NotEmpty(snapshots);

            // Simulate remote non-loopback caller authenticated via NodeApiToken
            harness.AuthService.SetSimulatedRemoteIpForTesting(IPAddress.Parse("100.88.10.25"));

            // 1. Remote PUT /config attempting to override ToolPaths.SevenZipPath -> 403 Forbidden
            var maliciousBinaryCfg = await harness.FetchConfigAsync(tokenHeader: nodeToken);
            maliciousBinaryCfg.ToolPaths.SevenZipPath = @"C:\Temp\evil_binary.exe";
            using (var req = new HttpRequestMessage(HttpMethod.Put, "/api/v1/config")
            {
                Content = new StringContent(JsonSerializer.Serialize(maliciousBinaryCfg), Encoding.UTF8, "application/json")
            })
            {
                req.Headers.TryAddWithoutValidation("X-Housekeeping-Token", nodeToken);
                using var resp = await harness.Client.SendAsync(req);
                Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            }

            // 2. Remote POST /profiles/import -> 403 Forbidden
            var maliciousProfile = AppConfig.CreateDefaults();
            maliciousProfile.WebServer.RestrictRawShellToLocalhost = false;
            string importBody = JsonSerializer.Serialize(new { Json = JsonSerializer.Serialize(maliciousProfile) });
            using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/profiles/import")
            {
                Content = new StringContent(importBody, Encoding.UTF8, "application/json")
            })
            {
                req.Headers.TryAddWithoutValidation("X-Housekeeping-Token", nodeToken);
                using var resp = await harness.Client.SendAsync(req);
                Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            }

            // 3. Remote POST /config/snapshots/{id}/restore -> 403 Forbidden
            using (var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/config/snapshots/{snapshots[0].SnapshotId}/restore"))
            {
                req.Headers.TryAddWithoutValidation("X-Housekeeping-Token", nodeToken);
                using var resp = await harness.Client.SendAsync(req);
                Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            }
        }

        [Fact]
        public async Task Sec002_LoginRateLimit_Blocks6thFailedAttempt_And_ReverseProxyHttpsSetsSecureCookie()
        {
            await using var harness = await AuditServerHarness.StartAsync();
            harness.AuthService.SetAdminPassword("CorrectHorse99");

            // 1. Verify X-Forwarded-Proto: https from loopback proxy sets Secure attribute on hk_session
            using (var httpsLogin = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
            {
                Content = new StringContent("{\"Password\":\"CorrectHorse99\"}", Encoding.UTF8, "application/json")
            })
            {
                httpsLogin.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
                httpsLogin.Headers.TryAddWithoutValidation("X-Forwarded-For", "100.90.1.5");
                using var resp = await harness.Client.SendAsync(httpsLogin);
                resp.EnsureSuccessStatusCode();
                string setCookie = string.Join("; ", resp.Headers.GetValues("Set-Cookie"));
                Assert.Contains("hk_session=", setCookie, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
            }

            // 2. Simulate 5 failed logins from remote IP -> 401, then 6th attempt -> 429 TooManyRequests
            harness.AuthService.SetSimulatedRemoteIpForTesting(IPAddress.Parse("192.168.50.77"));
            for (int i = 1; i <= 5; i++)
            {
                using var badReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
                {
                    Content = new StringContent("{\"Password\":\"WrongPass\"}", Encoding.UTF8, "application/json")
                };
                using var badResp = await harness.Client.SendAsync(badReq);
                Assert.Equal(HttpStatusCode.Unauthorized, badResp.StatusCode);
            }

            using (var throttledReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
            {
                Content = new StringContent("{\"Password\":\"CorrectHorse99\"}", Encoding.UTF8, "application/json")
            })
            {
                using var throttledResp = await harness.Client.SendAsync(throttledReq);
                Assert.Equal(HttpStatusCode.TooManyRequests, throttledResp.StatusCode);
            }
        }

        [Fact]
        public void Sec002_RestoreSnapshot_And_ImportProfile_PreserveHostAdminPasswordAndNodeToken()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"hk_rc7_snap_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                var svc = new ConfigService(tempDir);
                // Snapshot 1: before admin password exists
                svc.MutateAndSave(c => c.NetworkRetryIntervalSeconds = 22);
                var prePasswordSnap = svc.ListSnapshots().First();

                // Configure admin password & capture host token
                var auth = new TieredAuthService(svc);
                auth.SetAdminPassword("KeepThisPassword!7");
                string expectedHash = svc.Config.WebServer.AdminPasswordHash;
                string expectedSalt = svc.Config.WebServer.AdminPasswordSalt;
                string expectedToken = svc.Config.WebServer.NodeApiToken;

                // Restore older snapshot that had blank AdminPasswordHash
                Assert.True(svc.RestoreSnapshot(prePasswordSnap.SnapshotId, out string restoreErr), restoreErr);
                Assert.Equal(22, svc.Config.NetworkRetryIntervalSeconds);
                Assert.Equal(expectedHash, svc.Config.WebServer.AdminPasswordHash);
                Assert.Equal(expectedSalt, svc.Config.WebServer.AdminPasswordSalt);
                Assert.Equal(expectedToken, svc.Config.WebServer.NodeApiToken);

                // Import external profile attempting to inject a foreign AdminPasswordHash & foreign NodeApiToken
                var foreign = AppConfig.CreateDefaults();
                foreign.NetworkRetryIntervalSeconds = 33;
                foreign.WebServer.AdminPasswordHash = "AABBCCDDEEFF";
                foreign.WebServer.AdminPasswordSalt = "112233445566";
                foreign.WebServer.NodeApiToken = "foreign-attacker-token-000000000000";

                Assert.True(svc.ImportProfileFromJson(JsonSerializer.Serialize(foreign), out string importErr), importErr);
                Assert.Equal(33, svc.Config.NetworkRetryIntervalSeconds);
                Assert.Equal(expectedHash, svc.Config.WebServer.AdminPasswordHash);
                Assert.Equal(expectedSalt, svc.Config.WebServer.AdminPasswordSalt);
                Assert.Equal(expectedToken, svc.Config.WebServer.NodeApiToken);
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch (System.Exception __suppressedEx5) { System.Diagnostics.Debug.WriteLine($"[CodeAuditRc7RegressionTests] Non-fatal error ignored: {__suppressedEx5.Message}"); }
            }
        }

        [Fact]
        public async Task Log001_SignalR_RemoteTargetNodeSubscriber_DoesNotReceiveLocalHostBroadcasts()
        {
            await using var harness = await AuditServerHarness.StartAsync();

            var localStates = new ConcurrentQueue<PipelineRuntimeStateDto>();
            var remoteStates = new ConcurrentQueue<PipelineRuntimeStateDto>();

            await using var localConn = new HubConnectionBuilder()
                .WithUrl($"{harness.BaseUrl}/hubs/housekeeping")
                .Build();
            await using var remoteConn = new HubConnectionBuilder()
                .WithUrl($"{harness.BaseUrl}/hubs/housekeeping?targetNodeId=nas-worker-2")
                .Build();

            localConn.On<PipelineRuntimeStateDto>("PipelineState", s => localStates.Enqueue(s));
            remoteConn.On<PipelineRuntimeStateDto>("PipelineState", s => remoteStates.Enqueue(s));

            await localConn.StartAsync();
            await remoteConn.StartAsync();
            await Task.Delay(250);

            // Clear initial connect snapshot on localConn, verify remoteConn received 0 local snapshots
            Assert.NotEmpty(localStates);
            Assert.Empty(remoteStates);
            while (localStates.TryDequeue(out _)) { }

            // Trigger a local pipeline state broadcast (toggle post-run power or dismiss)
            harness.Runtime.Orchestrator.DismissInterruptedRun();
            await Task.Delay(250);

            Assert.NotEmpty(localStates);
            Assert.Empty(remoteStates);
        }

        [Fact]
        public async Task Log002_SemVerPreReleaseComparator_And_PushSettingsPreserveRemoteWebServer()
        {
            // 1. SemVer RC-to-RC and RC-to-GA ordering
            Assert.True(RemoteNodeDeploymentService.CompareSemVer("1.0.0-alpha.2", "1.0.0-beta.1") < 0);
            Assert.True(RemoteNodeDeploymentService.CompareSemVer("1.0.0-beta.3", "1.0.0-rc.1") < 0);
            Assert.True(RemoteNodeDeploymentService.CompareSemVer("1.0.0-rc.2", "1.0.0-rc.7") < 0);
            Assert.True(RemoteNodeDeploymentService.CompareSemVer("1.0.0-rc.7", "1.0.0-rc.10") < 0);
            Assert.True(RemoteNodeDeploymentService.CompareSemVer("1.0.0-rc.7", "1.0.0") < 0);
            Assert.Equal(0, RemoteNodeDeploymentService.CompareSemVer("v1.0.0-rc.7", "1.0.0-rc.7"));

            var olderRcNode = new RemoteNodeConfig { IsReachable = true, DetectedVersion = "1.0.0-rc.5" };
            RemoteNodeDeploymentService.EvaluateVersionEligibility(olderRcNode, "1.0.0-rc.7");
            Assert.True(olderRcNode.CanUpdateApp);
            Assert.False(olderRcNode.CanUpdateSettings);

            var matchingRcNode = new RemoteNodeConfig { IsReachable = true, DetectedVersion = "1.0.0-rc.7" };
            RemoteNodeDeploymentService.EvaluateVersionEligibility(matchingRcNode, "1.0.0-rc.7");
            Assert.False(matchingRcNode.CanUpdateApp);
            Assert.True(matchingRcNode.CanUpdateSettings);

            // 2. PushSettingsToRemoteAsync preserves remote node's WebServer port/token/password
            string remoteDir = Path.Combine(Path.GetTempPath(), $"hk_rc7_remote_{Guid.NewGuid():N}");
            Directory.CreateDirectory(remoteDir);
            try
            {
                var remoteExisting = AppConfig.CreateDefaults();
                remoteExisting.WebServer.Port = 5299;
                remoteExisting.WebServer.NodeApiToken = "remote-unique-node-token-1234567890";
                remoteExisting.WebServer.AdminPasswordHash = "REMOTE_HASH";
                remoteExisting.WebServer.AdminPasswordSalt = "REMOTE_SALT";
                File.WriteAllText(Path.Combine(remoteDir, "settings.json"), JsonSerializer.Serialize(remoteExisting));

                var localCfg = AppConfig.CreateDefaults();
                localCfg.NetworkMaxRetries = 9;
                localCfg.WebServer.Port = 5178;
                localCfg.WebServer.NodeApiToken = "local-master-token-9999999999";
                localCfg.WebServer.AdminPasswordHash = "LOCAL_HASH";

                var deploySvc = new RemoteNodeDeploymentService();
                var targetNode = new RemoteNodeConfig { Name = "MediaBox", RemoteAppPath = remoteDir };
                bool pushed = await deploySvc.PushSettingsToRemoteAsync(targetNode, localCfg, (_, _) => { });
                Assert.True(pushed);

                var merged = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(Path.Combine(remoteDir, "settings.json")), JsonOpts)!;
                Assert.Equal(9, merged.NetworkMaxRetries);
                Assert.Equal(5299, merged.WebServer.Port);
                Assert.Equal("remote-unique-node-token-1234567890", merged.WebServer.NodeApiToken);
                Assert.Equal("REMOTE_HASH", merged.WebServer.AdminPasswordHash);
            }
            finally
            {
                try { Directory.Delete(remoteDir, recursive: true); } catch (System.Exception __suppressedEx6) { System.Diagnostics.Debug.WriteLine($"[CodeAuditRc7RegressionTests] Non-fatal error ignored: {__suppressedEx6.Message}"); }
            }
        }

        [Fact]
        public async Task Asy001_And_Err001_ConcurrentMutateAndSave_IsThreadSafe_And_LockedFilePropagatesIoError()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"hk_rc7_conc_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                var svc = new ConfigService(tempDir);
                var exceptions = new ConcurrentBag<Exception>();

                // Concurrent writers + concurrent readers enumerating DefinedJobs & NamedQueues
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));
                var writerTask = Task.Run(() =>
                {
                    int idx = 0;
                    while (!cts.IsCancellationRequested && idx < 40)
                    {
                        try
                        {
                            int n = ++idx;
                            svc.MutateAndSave(cfg =>
                            {
                                var job = ModuleCatalogService.CreateNewJobForModuleType(cfg, ModuleId.CloudSync, $"Sync #{n}");
                                cfg.DefinedJobs.Add(job);
                                cfg.GetActiveQueue().Jobs.Add(job);
                            });
                        }
                        catch (Exception ex) { exceptions.Add(ex); }
                    }
                });

                var readerTask = Task.Run(() =>
                {
                    while (!cts.IsCancellationRequested)
                    {
                        try
                        {
                            var snap = svc.Config;
                            _ = snap.DefinedJobs.Count(j => j.Enabled);
                            foreach (var q in snap.NamedQueues)
                            {
                                _ = q.Jobs.Count(j => j.Enabled);
                            }
                        }
                        catch (Exception ex) { exceptions.Add(ex); }
                    }
                });

                await Task.WhenAll(writerTask, readerTask);
                Assert.Empty(exceptions);

                // ERR-001: Exclusively lock settings.json and verify TryUpdateAndSave returns false + rolls back memory
                int beforeRetries = svc.Config.NetworkMaxRetries;
                var candidate = svc.Config.DeepClone();
                candidate.NetworkMaxRetries = 77;

                // AtomicWriteAllText writes settings.json.tmp first and moves over settings.json; locking settings.json blocks overwrite
                using (var exclusiveLock = new FileStream(svc.ActiveConfigFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    bool saved = svc.TryUpdateAndSave(candidate, out var errors);
                    Assert.False(saved);
                    Assert.NotEmpty(errors);
                    Assert.Equal(beforeRetries, svc.Config.NetworkMaxRetries);
                }
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch (System.Exception __suppressedEx7) { System.Diagnostics.Debug.WriteLine($"[CodeAuditRc7RegressionTests] Non-fatal error ignored: {__suppressedEx7.Message}"); }
            }
        }

        [Fact]
        public void Perf001_PersistentTerminalLogWriter_AllowsConcurrentSharedReads()
        {
            string logDir = Path.Combine(Path.GetTempPath(), $"hk_rc7_logs_{Guid.NewGuid():N}");
            Directory.CreateDirectory(logDir);
            try
            {
                using var buffer = new TerminalLogBufferService(logDir);
                for (int i = 0; i < 200; i++)
                {
                    buffer.Append("INFO", $"High-throughput log line #{i}");
                }

                // External reader (e.g. SMB tail or history viewer) must be able to read while StreamWriter is open
                string onDisk = ConfigService.ReadAllTextWithRetry(buffer.LiveLogFilePath);
                Assert.Contains("High-throughput log line #0", onDisk);
                Assert.Contains("High-throughput log line #199", onDisk);
            }
            finally
            {
                try { Directory.Delete(logDir, recursive: true); } catch (System.Exception __suppressedEx8) { System.Diagnostics.Debug.WriteLine($"[CodeAuditRc7RegressionTests] Non-fatal error ignored: {__suppressedEx8.Message}"); }
            }
        }

        [Fact]
        public void Env001_CliParser_SupportsAllIsolationFlags_EqualsSyntax_And_EnvFallbacks()
        {
            var parsed = HousekeepingCliParser.Parse(new[]
            {
                "--data-dir", @"D:\Temp\IsolatedHk\data",
                "--config=D:\\Temp\\IsolatedHk\\data\\custom_settings.json",
                "--log-dir", @"D:\Temp\IsolatedHk\logs",
                "--port=5199",
                "--localhost-only",
                "--tray-only",
                "--dry-run"
            });

            Assert.Null(parsed.ParseError);
            Assert.Equal(@"D:\Temp\IsolatedHk\data", parsed.CustomDataDirectory);
            Assert.Equal(@"D:\Temp\IsolatedHk\data\custom_settings.json", parsed.CustomConfigFilePath);
            Assert.Equal(@"D:\Temp\IsolatedHk\logs", parsed.CustomLogsDirectory);
            Assert.Equal(5199, parsed.PortOverride);
            Assert.False(parsed.BindLanOverride);
            Assert.True(parsed.StartMinimized);
            Assert.True(parsed.DryRun);
            Assert.True(parsed.IsIsolatedInvocation);

            var badPort = HousekeepingCliParser.Parse(new[] { "--port", "99999" });
            Assert.NotNull(badPort.ParseError);

            var helpOnly = HousekeepingCliParser.Parse(new[] { "--help", "--version" });
            Assert.True(helpOnly.ShowHelp);
            Assert.True(helpOnly.ShowVersion);
            string helpText = HousekeepingCliParser.BuildHelpText("Housekeeping.exe");
            Assert.Contains("--data-dir", helpText);
            Assert.Contains("--config", helpText);
            Assert.Contains("--log-dir", helpText);
            Assert.Contains("--tray-only", helpText);
            Assert.Contains("--dry-run", helpText);

            // Environment variable fallbacks
            string? prevHome = Environment.GetEnvironmentVariable("APP_HOME");
            string? prevCfg = Environment.GetEnvironmentVariable("CONFIG_PATH");
            string? prevLog = Environment.GetEnvironmentVariable("LOG_DIR");
            string? prevPort = Environment.GetEnvironmentVariable("PORT");
            string? prevIso = Environment.GetEnvironmentVariable("APP_ISOLATED_TEST");
            string? prevDry = Environment.GetEnvironmentVariable("HOUSEKEEPING_DRY_RUN");
            try
            {
                Environment.SetEnvironmentVariable("APP_HOME", @"D:\Temp\EnvHome");
                Environment.SetEnvironmentVariable("CONFIG_PATH", @"D:\Temp\EnvHome\env_cfg.json");
                Environment.SetEnvironmentVariable("LOG_DIR", @"D:\Temp\EnvLogs");
                Environment.SetEnvironmentVariable("PORT", "5288");
                Environment.SetEnvironmentVariable("APP_ISOLATED_TEST", "1");
                Environment.SetEnvironmentVariable("HOUSEKEEPING_DRY_RUN", "1");

                var fromEnv = HousekeepingCliParser.Parse(Array.Empty<string>());
                Assert.Equal(@"D:\Temp\EnvHome", fromEnv.CustomDataDirectory);
                Assert.Equal(@"D:\Temp\EnvHome\env_cfg.json", fromEnv.CustomConfigFilePath);
                Assert.Equal(@"D:\Temp\EnvLogs", fromEnv.CustomLogsDirectory);
                Assert.Equal(5288, fromEnv.PortOverride);
                Assert.True(fromEnv.IsIsolatedInvocation);
                Assert.True(fromEnv.DryRun);
            }
            finally
            {
                Environment.SetEnvironmentVariable("APP_HOME", prevHome);
                Environment.SetEnvironmentVariable("CONFIG_PATH", prevCfg);
                Environment.SetEnvironmentVariable("LOG_DIR", prevLog);
                Environment.SetEnvironmentVariable("PORT", prevPort);
                Environment.SetEnvironmentVariable("APP_ISOLATED_TEST", prevIso);
                Environment.SetEnvironmentVariable("HOUSEKEEPING_DRY_RUN", prevDry);
            }
        }

        [Fact]
        public async Task Env002_IsolatedMode_And_ForceDryRun_ContainAllFiles_And_SimulateHostSideEffects()
        {
            string rootDir = Path.Combine(Path.GetTempPath(), $"hk_rc7_isolated_{Guid.NewGuid():N}");
            string dataDir = Path.Combine(rootDir, "data");
            string logDir = Path.Combine(rootDir, "logs");
            string customCfgPath = Path.Combine(dataDir, "isolated_custom.json");
            Directory.CreateDirectory(dataDir);
            Directory.CreateDirectory(logDir);

            int port = GetFreeLoopbackPort();
            var app = HousekeepingServerHost.BuildWebApplication(
                Array.Empty<string>(),
                new HousekeepingServerOptions
                {
                    PortOverride = port,
                    BindLanOverride = false,
                    CustomDataDirectory = dataDir,
                    CustomConfigFilePath = customCfgPath,
                    CustomLogsDirectory = logDir,
                    IsolatedOverride = true,
                    DryRun = true,
                    StartBackgroundTimers = false
                });

            await app.StartAsync();
            try
            {
                var runtime = app.Services.GetRequiredService<HousekeepingRuntimeContainer>();
                Assert.True(runtime.ConfigService.IsIsolatedOverride);
                Assert.True(runtime.Orchestrator.ForceDryRun);
                Assert.Equal(Path.GetFullPath(customCfgPath), runtime.ConfigService.ActiveConfigFilePath);
                Assert.Equal(Path.GetFullPath(logDir), runtime.ConfigService.ActiveLogsDir);

                // Cookie secret must reside in dataDir, keeping logDir strictly for log files
                Assert.True(File.Exists(Path.Combine(dataDir, ".hk_cookie_secret.key")));
                Assert.False(File.Exists(Path.Combine(logDir, ".hk_cookie_secret.key")));

                // Default GreenroomRootPath must contain zero hardcoded host paths and resolve inside dataDir when isolated
                Assert.Equal("", AppConfig.CreateDefaults().GreenroomIntegration.GreenroomRootPath);
                string grCli = runtime.GreenroomSync.ResolveCliBatchPath();
                Assert.StartsWith(Path.GetFullPath(dataDir), Path.GetFullPath(grCli), StringComparison.OrdinalIgnoreCase);

                using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

                // 1. /api/v1/health exposes isolated & dry-run telemetry
                using (var healthResp = await client.GetAsync("/api/v1/health"))
                {
                    healthResp.EnsureSuccessStatusCode();
                    using var doc = JsonDocument.Parse(await healthResp.Content.ReadAsStringAsync());
                    Assert.True(doc.RootElement.GetProperty("isIsolated").GetBoolean());
                    Assert.True(doc.RootElement.GetProperty("forceDryRun").GetBoolean());
                }

                // 2. /api/v1/settings/watchdog simulates when isolated without mutating Windows Task Scheduler
                using (var wdResp = await client.PostAsync(
                    "/api/v1/settings/watchdog",
                    new StringContent("{\"Enabled\":true}", Encoding.UTF8, "application/json")))
                {
                    wdResp.EnsureSuccessStatusCode();
                    using var doc = JsonDocument.Parse(await wdResp.Content.ReadAsStringAsync());
                    Assert.True(doc.RootElement.GetProperty("simulated").GetBoolean());
                }

                // 3. Mutate config and verify custom_settings.json (not settings.json) is written
                runtime.ConfigService.MutateAndSave(cfg =>
                {
                    cfg.SetModuleAppWideEnabled(ModuleId.CustomScripts, true);
                    var job = ModuleCatalogService.CreateNewJobForModuleType(cfg, ModuleId.CustomScripts, "DryRunProbe");
                    job.Enabled = true;
                    cfg.DefinedJobs.Clear();
                    cfg.DefinedJobs.Add(job);
                });
                Assert.True(File.Exists(customCfgPath));
                Assert.False(File.Exists(Path.Combine(dataDir, "settings.json")));

                // 4. ForceDryRun overrides StartSingleJobRun(..., isDryRun: false) to ctx.IsDryRun == true
                bool? observedDryRun = null;
                var executedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                runtime.Orchestrator.CustomModuleExecutor = (ctx, _) =>
                {
                    observedDryRun = ctx.IsDryRun;
                    executedTcs.TrySetResult(true);
                    return Task.CompletedTask;
                };

                string jobId = runtime.ConfigService.Config.DefinedJobs[0].JobId;
                var (started, startMsg) = runtime.Orchestrator.StartSingleJobRun(jobId, isDryRun: false);
                Assert.True(started, startMsg);
                await executedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(observedDryRun);
            }
            finally
            {
                try { await app.StopAsync(); } catch (System.Exception __suppressedEx9) { System.Diagnostics.Debug.WriteLine($"[CodeAuditRc7RegressionTests] Non-fatal error ignored: {__suppressedEx9.Message}"); }
                try { await app.DisposeAsync(); } catch (System.Exception __suppressedEx10) { System.Diagnostics.Debug.WriteLine($"[CodeAuditRc7RegressionTests] Non-fatal error ignored: {__suppressedEx10.Message}"); }
                try { Directory.Delete(rootDir, recursive: true); } catch (System.Exception __suppressedEx11) { System.Diagnostics.Debug.WriteLine($"[CodeAuditRc7RegressionTests] Non-fatal error ignored: {__suppressedEx11.Message}"); }
            }
        }

        [Fact]
        public void Log003_Scheduler_EvaluatesCollectionManifestPerModuleSchedule()
        {
            var cfg = AppConfig.CreateDefaults();
            cfg.CollectionManifest.EnableCustomSchedule = true;
            cfg.CollectionManifest.ScheduleIntervalMinutes = 30;
            cfg.CollectionManifest.LastScheduledRun = DateTime.Now.AddMinutes(-45);

            var scheduler = new SchedulerAndNotificationService(
                () => cfg,
                (_, _) => { },
                (_, _, _) => { });

            var due = scheduler.GetPerModuleSchedulesDueNow();
            Assert.Contains(ModuleId.CollectionManifest, due);
        }
    }
}
