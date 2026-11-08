using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
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
using Housekeeping.Services.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Housekeeping.Tests
{
    public sealed class ServerAndOrchestratorTests
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: true) }
        };

        private sealed class FakeNativeDialogBridge : INativeDialogBridge
        {
            public bool IsAvailable { get; set; } = true;
            public string? NextFolderResult { get; set; } = @"D:\MockPickedFolder";
            public string? NextFileResult { get; set; } = @"D:\MockPickedFile.7z";
            public int StandaloneWindowRequests { get; private set; }

            public Task<string?> PickFolderAsync(string? initialPath, string? description)
                => Task.FromResult(NextFolderResult);

            public Task<string?> PickFileAsync(string? initialPath, string? filter, string? title)
                => Task.FromResult(NextFileResult);

            public void RequestOpenStandaloneWindow()
                => StandaloneWindowRequests++;
        }

        private sealed class RecordingHttpHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _responder;
            public ConcurrentQueue<(HttpMethod Method, string Uri, string Body, string? AuthHeader)> Recorded { get; } = new();

            public RecordingHttpHandler(Func<HttpRequestMessage, string, HttpResponseMessage>? responder = null)
            {
                _responder = responder ?? ((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"started\"}", Encoding.UTF8, "application/json")
                });
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string body = request.Content != null
                    ? await request.Content.ReadAsStringAsync(cancellationToken)
                    : "";
                string? auth = request.Headers.Authorization?.ToString();
                Recorded.Enqueue((request.Method, request.RequestUri?.ToString() ?? "", body, auth));
                return _responder(request, body);
            }
        }

        private sealed class LiveServerHarness : IAsyncDisposable
        {
            public string DataDir { get; }
            public int Port { get; }
            public string BaseUrl => $"http://127.0.0.1:{Port}";
            public WebApplication App { get; }
            public HousekeepingRuntimeContainer Runtime { get; }
            public TieredAuthService AuthService { get; }
            public FakeNativeDialogBridge DialogBridge { get; }
            public HttpClient Client { get; }

            private readonly bool _deleteDataDirOnDispose;

            private LiveServerHarness(
                string dataDir,
                int port,
                WebApplication app,
                HousekeepingRuntimeContainer runtime,
                TieredAuthService authService,
                FakeNativeDialogBridge dialogBridge,
                bool deleteDataDirOnDispose)
            {
                DataDir = dataDir;
                Port = port;
                App = app;
                Runtime = runtime;
                AuthService = authService;
                DialogBridge = dialogBridge;
                _deleteDataDirOnDispose = deleteDataDirOnDispose;
                Client = new HttpClient(new HttpClientHandler { UseCookies = false })
                {
                    BaseAddress = new Uri(BaseUrl),
                    Timeout = TimeSpan.FromSeconds(15)
                };
            }

            public static async Task<LiveServerHarness> StartAsync(
                HttpClient? customGreenroomClient = null,
                HttpClient? customProxyClient = null,
                string? existingDataDir = null,
                bool deleteDataDirOnDispose = true)
            {
                string dataDir = string.IsNullOrWhiteSpace(existingDataDir)
                    ? Path.Combine(Path.GetTempPath(), $"hk3_test_{Guid.NewGuid():N}")
                    : existingDataDir;
                Directory.CreateDirectory(dataDir);

                int port = GetFreeLoopbackPort();
                var bridge = new FakeNativeDialogBridge();
                string webRoot = ResolveServerWwwRoot();

                var app = HousekeepingServerHost.BuildWebApplication(
                    Array.Empty<string>(),
                    new HousekeepingServerOptions
                    {
                        PortOverride = port,
                        BindLanOverride = false,
                        CustomDataDirectory = dataDir,
                        CustomWebRootPath = webRoot,
                        StartBackgroundTimers = false,
                        CustomGreenroomHttpClient = customGreenroomClient,
                        CustomRemoteProxyHttpClient = customProxyClient,
                        NativeDialogBridge = bridge
                    });

                await app.StartAsync();

                var runtime = app.Services.GetRequiredService<HousekeepingRuntimeContainer>();
                var auth = app.Services.GetRequiredService<TieredAuthService>();
                return new LiveServerHarness(dataDir, port, app, runtime, auth, bridge, deleteDataDirOnDispose);
            }

            public async Task<AppConfig> FetchConfigAsync(string? cookieHeader = null, string? tokenHeader = null)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/config");
                if (!string.IsNullOrEmpty(cookieHeader))
                    req.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
                if (!string.IsNullOrEmpty(tokenHeader))
                    req.Headers.TryAddWithoutValidation("X-Housekeeping-Token", tokenHeader);

                using var resp = await Client.SendAsync(req);
                resp.EnsureSuccessStatusCode();
                string raw = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(raw);
                string cfgJson = doc.RootElement.GetProperty("config").GetRawText();
                return JsonSerializer.Deserialize<AppConfig>(cfgJson, JsonOpts)!;
            }

            public async ValueTask DisposeAsync()
            {
                Client.Dispose();
                try { await App.StopAsync(); } catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[ServerAndOrchestratorTests] Non-fatal error ignored: {__suppressedEx1.Message}"); }
                try { await App.DisposeAsync(); } catch (System.Exception __suppressedEx2) { System.Diagnostics.Debug.WriteLine($"[ServerAndOrchestratorTests] Non-fatal error ignored: {__suppressedEx2.Message}"); }
                try { Runtime.Dispose(); } catch (System.Exception __suppressedEx3) { System.Diagnostics.Debug.WriteLine($"[ServerAndOrchestratorTests] Non-fatal error ignored: {__suppressedEx3.Message}"); }
                if (_deleteDataDirOnDispose)
                {
                    try
                    {
                        if (Directory.Exists(DataDir))
                            Directory.Delete(DataDir, recursive: true);
                    }
                    catch (System.Exception __suppressedEx4) { System.Diagnostics.Debug.WriteLine($"[ServerAndOrchestratorTests] Non-fatal error ignored: {__suppressedEx4.Message}"); }
                }
            }
        }

        private static int GetFreeLoopbackPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static string ResolveServerWwwRoot()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 6; i++)
            {
                string candidate = Path.Combine(dir, "Housekeeping.Server", "wwwroot");
                if (Directory.Exists(candidate))
                    return candidate;
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            return Path.Combine(AppContext.BaseDirectory, "wwwroot");
        }

        private static string ExtractSessionCookieHeader(HttpResponseMessage response)
        {
            Assert.True(response.Headers.TryGetValues("Set-Cookie", out var setCookies));
            string raw = setCookies!.First(c => c.StartsWith("hk_session=", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("httponly", raw, StringComparison.OrdinalIgnoreCase);
            return raw.Split(';')[0];
        }

        // ==================== 1. HEALTH & STATIC SPA SHELL ==================== //

        [Fact]
        public async Task HealthEndpoint_IsPublicEvenForRemoteCallers_AndReportsEngineOnline()
        {
            await using var server = await LiveServerHarness.StartAsync();
            server.AuthService.SetSimulatedRemoteIpForTesting(IPAddress.Parse("100.88.12.34"));

            using var resp = await server.Client.GetAsync("/api/v1/health");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
            Assert.False(doc.RootElement.GetProperty("isRunning").GetBoolean());
        }

        [Fact]
        public async Task StaticSpaFallback_ServesReactIndexHtmlWithLightDarkMeta_And404sUnknownApiRoutes()
        {
            await using var server = await LiveServerHarness.StartAsync();

            using var rootResp = await server.Client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, rootResp.StatusCode);
            string html = await rootResp.Content.ReadAsStringAsync();
            Assert.Contains("<meta name=\"color-scheme\" content=\"light dark\"", html);

            using var pwaResp = await server.Client.GetAsync("/manifest.webmanifest");
            Assert.Equal(HttpStatusCode.OK, pwaResp.StatusCode);

            using var api404 = await server.Client.GetAsync("/api/v1/does-not-exist");
            Assert.Equal(HttpStatusCode.NotFound, api404.StatusCode);
        }

        // ==================== 2. TIERED AUTH, TAILSCALE & SECURITY GOVERNANCE ==================== //

        [Fact]
        public async Task DirectLoopback_IsAutoTrustedWithoutPassword_AndExposesNativeDialogBridge()
        {
            await using var server = await LiveServerHarness.StartAsync();

            using var statusResp = await server.Client.GetAsync("/api/v1/auth/status");
            statusResp.EnsureSuccessStatusCode();
            using var statusDoc = JsonDocument.Parse(await statusResp.Content.ReadAsStringAsync());

            Assert.True(statusDoc.RootElement.GetProperty("isAuthenticated").GetBoolean());
            Assert.True(statusDoc.RootElement.GetProperty("isDirectLoopback").GetBoolean());
            Assert.Equal("LocalLoopback", statusDoc.RootElement.GetProperty("authMethod").GetString());
            Assert.True(statusDoc.RootElement.GetProperty("nativeDialogsAvailable").GetBoolean());

            var cfg = await server.FetchConfigAsync();
            Assert.NotNull(cfg);
        }

        [Fact]
        public async Task RemoteCaller_IsBlockedWith401BeforeSetup_AndReportsRequiresInitialSetup()
        {
            await using var server = await LiveServerHarness.StartAsync();
            server.AuthService.SetSimulatedRemoteIpForTesting(IPAddress.Parse("100.101.102.103"));

            using var resp = await server.Client.GetAsync("/api/v1/config");
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.Equal("authentication_required", doc.RootElement.GetProperty("error").GetString());
            Assert.True(doc.RootElement.GetProperty("requiresSetup").GetBoolean());
        }

        [Fact]
        public async Task FirstTimePasswordSetup_IssuesSignedHttpOnlySessionCookie_AllowingRemoteAccess()
        {
            await using var server = await LiveServerHarness.StartAsync();
            server.AuthService.SetSimulatedRemoteIpForTesting(IPAddress.Parse("100.101.102.103"));

            using var setupResp = await server.Client.PostAsJsonAsync("/api/v1/auth/setup-password", new { Password = "SuperSecretTailnetPwd!" });
            Assert.Equal(HttpStatusCode.OK, setupResp.StatusCode);
            string sessionCookie = ExtractSessionCookieHeader(setupResp);

            // Second setup call must be rejected once password exists
            using var duplicateSetup = await server.Client.PostAsJsonAsync("/api/v1/auth/setup-password", new { Password = "AnotherPassword123" });
            Assert.Equal(HttpStatusCode.BadRequest, duplicateSetup.StatusCode);

            // Remote request carrying hk_session cookie succeeds
            var cfg = await server.FetchConfigAsync(cookieHeader: sessionCookie);
            Assert.Equal("", cfg.WebServer.AdminPasswordHash); // Never leaked over wire
            Assert.Equal("", cfg.WebServer.AdminPasswordSalt);
        }

        [Fact]
        public async Task RemoteLoginAndLogout_RejectsWrongPassword_AcceptsValidPassword_AndClearsCookie()
        {
            await using var server = await LiveServerHarness.StartAsync();
            server.AuthService.SetAdminPassword("CorrectHorseBatteryStaple");
            server.AuthService.SetSimulatedRemoteIpForTesting(IPAddress.Parse("192.168.1.75"));

            using var badLogin = await server.Client.PostAsJsonAsync("/api/v1/auth/login", new { Password = "WrongPassword" });
            Assert.Equal(HttpStatusCode.Unauthorized, badLogin.StatusCode);

            using var goodLogin = await server.Client.PostAsJsonAsync("/api/v1/auth/login", new { Password = "CorrectHorseBatteryStaple" });
            Assert.Equal(HttpStatusCode.OK, goodLogin.StatusCode);
            string cookie = ExtractSessionCookieHeader(goodLogin);

            using var authedStatusReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/status");
            authedStatusReq.Headers.TryAddWithoutValidation("Cookie", cookie);
            using var authedStatusResp = await server.Client.SendAsync(authedStatusReq);
            using var statusDoc = JsonDocument.Parse(await authedStatusResp.Content.ReadAsStringAsync());
            Assert.True(statusDoc.RootElement.GetProperty("isAuthenticated").GetBoolean());
            Assert.Equal("SessionCookie", statusDoc.RootElement.GetProperty("authMethod").GetString());

            using var logoutResp = await server.Client.PostAsync("/api/v1/auth/logout", null);
            Assert.Equal(HttpStatusCode.OK, logoutResp.StatusCode);
            Assert.True(logoutResp.Headers.TryGetValues("Set-Cookie", out var clearedCookies));
            Assert.Contains(clearedCookies!, c => c.Contains("hk_session=;", StringComparison.OrdinalIgnoreCase) || c.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task TailscaleServeLoopbackHeader_AuthenticatesWhenTrusted_AndBlocksWhenDisabled()
        {
            await using var server = await LiveServerHarness.StartAsync();

            // Proxy arriving on 127.0.0.1 with X-Forwarded-For + Tailscale-User-Login
            using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/status"))
            {
                req.Headers.TryAddWithoutValidation("X-Forwarded-For", "100.64.10.20");
                req.Headers.TryAddWithoutValidation("Tailscale-User-Login", "user@tailnet.example.invalid");
                using var resp = await server.Client.SendAsync(req);
                resp.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                Assert.True(doc.RootElement.GetProperty("isAuthenticated").GetBoolean());
                Assert.True(doc.RootElement.GetProperty("isTailscaleServe").GetBoolean());
                Assert.Equal("TailscaleServe", doc.RootElement.GetProperty("authMethod").GetString());
                Assert.Equal("user@tailnet.example.invalid", doc.RootElement.GetProperty("identity").GetString());
            }

            // Disable TrustTailscaleServeHeaders -> same proxied request is no longer auto-trusted
            server.Runtime.ConfigService.Config.WebServer.TrustTailscaleServeHeaders = false;
            server.Runtime.ConfigService.Save();

            using (var req2 = new HttpRequestMessage(HttpMethod.Get, "/api/v1/config"))
            {
                req2.Headers.TryAddWithoutValidation("X-Forwarded-For", "100.64.10.20");
                req2.Headers.TryAddWithoutValidation("Tailscale-User-Login", "user@tailnet.example.invalid");
                using var resp2 = await server.Client.SendAsync(req2);
                Assert.Equal(HttpStatusCode.Unauthorized, resp2.StatusCode);
            }
        }

        [Fact]
        public async Task NodeApiToken_AuthenticatesRemoteFleetRequests_AndSupportsTokenRotation()
        {
            await using var server = await LiveServerHarness.StartAsync();
            server.Runtime.ConfigService.Config.WebServer.EnsureNodeApiTokenInitialized();
            string initialToken = server.Runtime.ConfigService.Config.WebServer.NodeApiToken;
            Assert.False(string.IsNullOrWhiteSpace(initialToken));

            server.AuthService.SetSimulatedRemoteIpForTesting(IPAddress.Parse("100.90.1.9"));

            var cfg = await server.FetchConfigAsync(tokenHeader: initialToken);
            Assert.NotNull(cfg);

            // Rotate token via API
            using var rotateReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/rotate-node-token");
            rotateReq.Headers.TryAddWithoutValidation("X-Housekeeping-Token", initialToken);
            using var rotateResp = await server.Client.SendAsync(rotateReq);
            rotateResp.EnsureSuccessStatusCode();
            using var rotDoc = JsonDocument.Parse(await rotateResp.Content.ReadAsStringAsync());
            string newToken = rotDoc.RootElement.GetProperty("nodeApiToken").GetString()!;
            Assert.NotEqual(initialToken, newToken);

            // Old token must now return 401
            using var staleReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/config");
            staleReq.Headers.TryAddWithoutValidation("X-Housekeeping-Token", initialToken);
            using var staleResp = await server.Client.SendAsync(staleReq);
            Assert.Equal(HttpStatusCode.Unauthorized, staleResp.StatusCode);

            // New token succeeds
            var cfgAfter = await server.FetchConfigAsync(tokenHeader: newToken);
            Assert.NotNull(cfgAfter);
        }

        [Fact]
        public async Task RestrictRawShellToLocalhost_BlocksRemoteTerminalExecAndCustomScriptsEdits_WhileAllowingLoopback()
        {
            await using var server = await LiveServerHarness.StartAsync();
            server.AuthService.SetAdminPassword("Admin1234!");
            server.Runtime.ConfigService.Config.WebServer.RestrictRawShellToLocalhost = true;
            server.Runtime.ConfigService.Save();

            // 1. Direct loopback CAN still execute CLI commands
            using var localExec = await server.Client.PostAsJsonAsync("/api/v1/terminal/exec", new { Command = "echo LOCAL_ALLOWED" });
            Assert.Equal(HttpStatusCode.OK, localExec.StatusCode);

            // 2. Remote authenticated user gets 403 on /api/v1/terminal/exec and CustomScripts edits
            server.AuthService.SetSimulatedRemoteIpForTesting(IPAddress.Parse("100.77.88.99"));
            using var loginResp = await server.Client.PostAsJsonAsync("/api/v1/auth/login", new { Password = "Admin1234!" });
            string cookie = ExtractSessionCookieHeader(loginResp);

            using (var remoteExecReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/terminal/exec")
            {
                Content = JsonContent.Create(new { Command = "whoami" })
            })
            {
                remoteExecReq.Headers.TryAddWithoutValidation("Cookie", cookie);
                using var remoteExecResp = await server.Client.SendAsync(remoteExecReq);
                Assert.Equal(HttpStatusCode.Forbidden, remoteExecResp.StatusCode);
            }

            // Remote non-script edit (e.g. PostRunPowerAction) succeeds
            var cfg = await server.FetchConfigAsync(cookieHeader: cookie);
            cfg.PostRunPowerAction = "Sleep";
            using (var safePutReq = new HttpRequestMessage(HttpMethod.Put, "/api/v1/config")
            {
                Content = JsonContent.Create(cfg, options: JsonOpts)
            })
            {
                safePutReq.Headers.TryAddWithoutValidation("Cookie", cookie);
                using var safePutResp = await server.Client.SendAsync(safePutReq);
                Assert.Equal(HttpStatusCode.OK, safePutResp.StatusCode);
            }

            // Remote edit adding/modifying a CustomScripts task is blocked with 403
            cfg.CustomScripts.Tasks.Add(new CustomTaskItem { Name = "Injected", CommandOrScriptPath = "powershell.exe", Arguments = "-c calc.exe" });
            using (var scriptPutReq = new HttpRequestMessage(HttpMethod.Put, "/api/v1/config")
            {
                Content = JsonContent.Create(cfg, options: JsonOpts)
            })
            {
                scriptPutReq.Headers.TryAddWithoutValidation("Cookie", cookie);
                using var scriptPutResp = await server.Client.SendAsync(scriptPutReq);
                Assert.Equal(HttpStatusCode.Forbidden, scriptPutResp.StatusCode);
            }
        }

        // ==================== 3. AUTO-SAVE VALIDATION, 20-SNAPSHOT ROLLBACK & PROFILES ==================== //

        [Fact]
        public async Task PutConfig_Validation_RejectsInvalidRegexAndCircularPostJobChaining()
        {
            await using var server = await LiveServerHarness.StartAsync();

            // 1. Invalid Regex in CollectionManifest FieldParseRules -> 400 BadRequest
            var badRegexCfg = await server.FetchConfigAsync();
            badRegexCfg.CollectionManifest.FieldParseRules.Add(new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "BrokenCol",
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = "(?<UnclosedBracket[abc"
            });

            using var regexResp = await server.Client.PutAsJsonAsync("/api/v1/config", badRegexCfg, JsonOpts);
            Assert.Equal(HttpStatusCode.BadRequest, regexResp.StatusCode);
            string regexErrBody = await regexResp.Content.ReadAsStringAsync();
            Assert.Contains("Invalid regex", regexErrBody, StringComparison.OrdinalIgnoreCase);

            // 2. Circular OnSuccessTriggerJobId (Job A -> Job B -> Job A) -> 400 BadRequest
            var cycleCfg = await server.FetchConfigAsync();
            var jobA = new HousekeepingJob { JobId = "job_a", Name = "Job A", ModuleType = ModuleId.CloudSync, OnSuccessTriggerJobId = "job_b", CloudSync = new CloudSyncConfig() };
            var jobB = new HousekeepingJob { JobId = "job_b", Name = "Job B", ModuleType = ModuleId.CloudSync, OnSuccessTriggerJobId = "job_a", CloudSync = new CloudSyncConfig() };
            cycleCfg.DefinedJobs = new List<HousekeepingJob> { jobA, jobB };

            using var cycleResp = await server.Client.PutAsJsonAsync("/api/v1/config", cycleCfg, JsonOpts);
            Assert.Equal(HttpStatusCode.BadRequest, cycleResp.StatusCode);
            string cycleErrBody = await cycleResp.Content.ReadAsStringAsync();
            Assert.Contains("Circular", cycleErrBody, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task RollingSnapshotHistory_DeduplicatesIdenticalSaves_CapsAt20_AndRestoresPriorConfig()
        {
            await using var server = await LiveServerHarness.StartAsync();

            for (int i = 1; i <= 25; i++)
            {
                var cfg = await server.FetchConfigAsync();
                cfg.HumbleSync.LibraryPath = $@"D:\HumbleVault\Iteration_{i:D2}";
                using var putResp = await server.Client.PutAsJsonAsync("/api/v1/config", cfg, JsonOpts);
                putResp.EnsureSuccessStatusCode();
            }

            // Saving identical payload twice does not add redundant snapshots
            var currentCfg = await server.FetchConfigAsync();
            using (var dupResp = await server.Client.PutAsJsonAsync("/api/v1/config", currentCfg, JsonOpts))
            {
                dupResp.EnsureSuccessStatusCode();
            }

            var snapshots = await server.Client.GetFromJsonAsync<List<ConfigSnapshotInfo>>("/api/v1/config/snapshots", JsonOpts);
            Assert.NotNull(snapshots);
            Assert.True(snapshots!.Count <= ConfigService.MaxConfigSnapshots);
            Assert.Equal(20, snapshots.Count);

            // Restore the oldest retained snapshot (Iteration_05) and confirm rollback
            var oldestSnapshot = snapshots.Last();
            using var restoreResp = await server.Client.PostAsync($"/api/v1/config/snapshots/{oldestSnapshot.SnapshotId}/restore", null);
            restoreResp.EnsureSuccessStatusCode();

            var restoredCfg = await server.FetchConfigAsync();
            Assert.NotEqual(@"D:\HumbleVault\Iteration_25", restoredCfg.HumbleSync.LibraryPath);
            Assert.StartsWith(@"D:\HumbleVault\Iteration_", restoredCfg.HumbleSync.LibraryPath);
        }

        [Fact]
        public async Task ProfileExportAndImport_SanitizeFlagStripsAllSecretsAndTokens()
        {
            await using var server = await LiveServerHarness.StartAsync();
            var cfg = await server.FetchConfigAsync();
            cfg.HumbleSync.SessionCookie = "humble_secret_session_999";
            cfg.DiscordWebhookUrl = "https://discord.com/api/webhooks/secret_hook";
            cfg.GreenroomIntegration.CompanionBearerToken = "gr_bearer_secret";
            cfg.RemoteNodes.Add(new RemoteNodeConfig
            {
                Id = "node-lan",
                Name = "LivingRoomPC",
                HostOrIp = "192.168.1.90",
                ApiToken = "remote_node_secret_token"
            });
            cfg.ProxyGenerator.Folders.Add(new FolderTargetItem { Enabled = true, Path = @"V:\RawFootage" });

            using var saveResp = await server.Client.PutAsJsonAsync("/api/v1/config", cfg, JsonOpts);
            saveResp.EnsureSuccessStatusCode();

            // Export sanitized profile
            string sanitizedJson = await server.Client.GetStringAsync("/api/v1/profiles/export?sanitize=true");
            Assert.DoesNotContain("humble_secret_session_999", sanitizedJson);
            Assert.DoesNotContain("secret_hook", sanitizedJson);
            Assert.DoesNotContain("gr_bearer_secret", sanitizedJson);
            Assert.DoesNotContain("remote_node_secret_token", sanitizedJson);
            Assert.Contains(@"V:\\RawFootage", sanitizedJson);

            // Import into a clean server
            await using var targetServer = await LiveServerHarness.StartAsync();
            using var importResp = await targetServer.Client.PostAsJsonAsync("/api/v1/profiles/import", new { Json = sanitizedJson });
            importResp.EnsureSuccessStatusCode();

            var importedCfg = await targetServer.FetchConfigAsync();
            Assert.Single(importedCfg.ProxyGenerator.Folders);
            Assert.Equal(@"V:\RawFootage", importedCfg.ProxyGenerator.Folders[0].Path);
            Assert.Equal("", importedCfg.HumbleSync.SessionCookie);
        }

        // ==================== 4. ORCHESTRATOR SNAPSHOTS, JOB CHAINING & GREENROOM SYNC ==================== //

        [Fact]
        public async Task PipelineOrchestrator_ImmutableRunSnapshot_IsolatesActiveRunFromMidRunAutoSaveMutations()
        {
            await using var server = await LiveServerHarness.StartAsync();
            var cfg = server.Runtime.ConfigService.Config;
            cfg.SetModuleAppWideEnabled(ModuleId.HumbleSync, true);
            var job = ModuleCatalogService.CreateNewJobForModuleType(cfg, ModuleId.HumbleSync, "Humble Snapshot Test");
            job.HumbleSync!.LibraryPath = @"D:\OriginalVaultPath";
            cfg.DefinedJobs = new List<HousekeepingJob> { job };
            cfg.GetActiveQueue().Jobs = new List<HousekeepingJob> { job };
            server.Runtime.ConfigService.Save();

            var moduleEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowModuleContinue = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            string observedLibraryPathInsideRun = "";

            server.Runtime.Orchestrator.CustomModuleExecutor = async (ctx, ct) =>
            {
                moduleEntered.TrySetResult(true);
                await allowModuleContinue.Task.WaitAsync(ct);
                observedLibraryPathInsideRun = ctx.ActiveJob?.HumbleSync?.LibraryPath ?? "";
            };

            var (started, _) = server.Runtime.Orchestrator.StartSingleJobRun(job.JobId);
            Assert.True(started);
            await moduleEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Mutate live config via PUT /api/v1/config while job is actively running!
            var mutated = await server.FetchConfigAsync();
            mutated.DefinedJobs[0].HumbleSync!.LibraryPath = @"Z:\MutatedMidRunPath";
            using var putResp = await server.Client.PutAsJsonAsync("/api/v1/config", mutated, JsonOpts);
            putResp.EnsureSuccessStatusCode();

            allowModuleContinue.TrySetResult(true);
            var execTask = server.Runtime.Orchestrator.ActiveExecutionTask;
            if (execTask != null)
                await execTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(@"D:\OriginalVaultPath", observedLibraryPathInsideRun);
            Assert.Equal(@"Z:\MutatedMidRunPath", server.Runtime.ConfigService.Config.DefinedJobs[0].HumbleSync!.LibraryPath);
        }

        [Fact]
        public async Task PipelineOrchestrator_OnSuccessTriggerJobId_ChainsFollowUpJob_AndGuardsCycles()
        {
            await using var server = await LiveServerHarness.StartAsync();
            var cfg = server.Runtime.ConfigService.Config;
            cfg.SetModuleAppWideEnabled(ModuleId.ProxyGenerator, true);
            cfg.SetModuleAppWideEnabled(ModuleId.CollectionManifest, true);

            var job1 = ModuleCatalogService.CreateNewJobForModuleType(cfg, ModuleId.ProxyGenerator, "Step 1 Proxies");
            var job2 = ModuleCatalogService.CreateNewJobForModuleType(cfg, ModuleId.CollectionManifest, "Step 2 Manifest");
            job1.OnSuccessTriggerJobId = job2.JobId;
            // Intentional runtime cycle back to job1 to verify orchestrator visited-set cycle guard
            job2.OnSuccessTriggerJobId = job1.JobId;

            cfg.DefinedJobs = new List<HousekeepingJob> { job1, job2 };
            cfg.GetActiveQueue().Jobs = new List<HousekeepingJob> { job1 };
            server.Runtime.ConfigService.Save();

            var executedOrder = new List<string>();
            server.Runtime.Orchestrator.CustomModuleExecutor = (ctx, _) =>
            {
                executedOrder.Add(ctx.ActiveJob!.Name);
                return Task.CompletedTask;
            };

            var (started, _) = server.Runtime.Orchestrator.StartSingleJobRun(job1.JobId);
            Assert.True(started);

            var execTask = server.Runtime.Orchestrator.ActiveExecutionTask;
            if (execTask != null)
                await execTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(new[] { "Step 1 Proxies", "Step 2 Manifest" }, executedOrder);
        }

        [Fact]
        public async Task GreenroomPostManifestSync_TriggersCompanionOnlyWhenManifestChanges_AndSkipsDryRun()
        {
            var greenroomHandler = new RecordingHttpHandler();
            using var greenroomHttp = new HttpClient(greenroomHandler);
            await using var server = await LiveServerHarness.StartAsync(customGreenroomClient: greenroomHttp);

            string manifestDir = Path.Combine(server.DataDir, "rom_manifests");
            Directory.CreateDirectory(manifestDir);
            string romCsvPath = Path.Combine(manifestDir, "Master_ROM_Manifest.csv");
            File.WriteAllText(romCsvPath, "Console,GameTitle\nSNES,Chrono Trigger\n", Encoding.UTF8);

            var cfg = server.Runtime.ConfigService.Config;
            cfg.GreenroomIntegration.Enabled = true;
            cfg.GreenroomIntegration.CompanionBaseUrl = "http://127.0.0.1:8420";
            cfg.GreenroomIntegration.AutoSyncAfterRomManifestChanges = true;
            cfg.GreenroomIntegration.AutoSyncAfterCollectionManifestChanges = true;
            cfg.SetModuleAppWideEnabled(ModuleId.RomLauncherGenerator, true);

            var romJob = ModuleCatalogService.CreateNewJobForModuleType(cfg, ModuleId.RomLauncherGenerator, "ROM Audit");
            romJob.RomLauncherGenerator!.LaunchersOutputRootPath = manifestDir;
            romJob.RomLauncherGenerator.ManifestCsvFileName = "Master_ROM_Manifest.csv";
            cfg.DefinedJobs = new List<HousekeepingJob> { romJob };
            cfg.GetActiveQueue().Jobs = new List<HousekeepingJob> { romJob };
            server.Runtime.ConfigService.Save();

            // 1. Run that modifies Master_ROM_Manifest.csv -> triggers collect_library + verify_hashes
            server.Runtime.Orchestrator.CustomModuleExecutor = (_, _) =>
            {
                File.AppendAllText(romCsvPath, "PS1,Castlevania SOTN\n", Encoding.UTF8);
                return Task.CompletedTask;
            };

            var (started1, _) = server.Runtime.Orchestrator.StartSingleJobRun(romJob.JobId, isDryRun: false);
            Assert.True(started1);
            await server.Runtime.Orchestrator.ActiveExecutionTask!.WaitAsync(TimeSpan.FromSeconds(5));

            var callsAfterChange = greenroomHandler.Recorded.ToArray();
            Assert.Equal(2, callsAfterChange.Length);
            Assert.Contains(callsAfterChange, c => c.Uri == "http://127.0.0.1:8420/api/tasks/run" && c.Body.Contains("\"collect_library\""));
            Assert.Contains(callsAfterChange, c => c.Uri == "http://127.0.0.1:8420/api/tasks/run" && c.Body.Contains("\"verify_hashes\""));

            // 2. Second run where manifest CSV does NOT change -> 0 additional Greenroom requests!
            while (greenroomHandler.Recorded.TryDequeue(out _)) { }
            server.Runtime.Orchestrator.CustomModuleExecutor = (_, _) => Task.CompletedTask;

            var (started2, _) = server.Runtime.Orchestrator.StartSingleJobRun(romJob.JobId, isDryRun: false);
            Assert.True(started2);
            await server.Runtime.Orchestrator.ActiveExecutionTask!.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(greenroomHandler.Recorded);

            // 3. Dry-run even if file touched -> 0 Greenroom requests!
            server.Runtime.Orchestrator.CustomModuleExecutor = (_, _) =>
            {
                File.AppendAllText(romCsvPath, "GBA,Metroid Fusion\n", Encoding.UTF8);
                return Task.CompletedTask;
            };
            var (started3, _) = server.Runtime.Orchestrator.StartSingleJobRun(romJob.JobId, isDryRun: true);
            Assert.True(started3);
            await server.Runtime.Orchestrator.ActiveExecutionTask!.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(greenroomHandler.Recorded);
        }

        // ==================== 5. MULTI-NODE TOP-BAR PROXY ==================== //

        [Fact]
        public async Task RemoteNodeProxyMiddleware_ForwardsTargetNodeRequestsWithToken_AndHonorsBypassHeader()
        {
            await using var coordinator = await LiveServerHarness.StartAsync();
            await using var worker = await LiveServerHarness.StartAsync();

            worker.Runtime.ConfigService.Config.WebServer.EnsureNodeApiTokenInitialized();
            string workerToken = worker.Runtime.ConfigService.Config.WebServer.NodeApiToken;
            worker.Runtime.ConfigService.Config.HumbleSync.LibraryPath = @"W:\WorkerExclusiveLibrary";
            worker.Runtime.ConfigService.Save();

            coordinator.Runtime.ConfigService.Config.HumbleSync.LibraryPath = @"C:\CoordinatorLocalLibrary";
            coordinator.Runtime.ConfigService.Config.RemoteNodes.Add(new RemoteNodeConfig
            {
                Id = "worker-nas",
                Name = "EncodingWorker",
                HostOrIp = "127.0.0.1",
                HttpApiPort = worker.Port,
                ApiToken = workerToken,
                PreferHttpApi = true
            });
            coordinator.Runtime.ConfigService.Save();

            // Request sent to Coordinator with X-Target-Node-Id: worker-nas returns Worker's config!
            using (var proxyReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/config"))
            {
                proxyReq.Headers.TryAddWithoutValidation("X-Target-Node-Id", "worker-nas");
                using var proxyResp = await coordinator.Client.SendAsync(proxyReq);
                proxyResp.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await proxyResp.Content.ReadAsStringAsync());
                string workerLib = doc.RootElement.GetProperty("config").GetProperty("HumbleSync").GetProperty("LibraryPath").GetString()!;
                Assert.Equal(@"W:\WorkerExclusiveLibrary", workerLib);
            }

            // Same request with X-Bypass-Node-Proxy: 1 stays on Coordinator
            using (var bypassReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/config"))
            {
                bypassReq.Headers.TryAddWithoutValidation("X-Target-Node-Id", "worker-nas");
                bypassReq.Headers.TryAddWithoutValidation("X-Bypass-Node-Proxy", "1");
                using var bypassResp = await coordinator.Client.SendAsync(bypassReq);
                bypassResp.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await bypassResp.Content.ReadAsStringAsync());
                string localLib = doc.RootElement.GetProperty("config").GetProperty("HumbleSync").GetProperty("LibraryPath").GetString()!;
                Assert.Equal(@"C:\CoordinatorLocalLibrary", localLib);
            }
        }

        // ==================== 6. REGEX PLAYGROUND, SYNC PREVIEW, REPORTS, FILESYSTEM & CLI ==================== //

        [Fact]
        public async Task ManifestRegexPlaygroundEndpoint_EvaluatesCSharpNamedCapturesAccurately()
        {
            await using var server = await LiveServerHarness.StartAsync();

            var rules = ModuleCatalogService.GetManifestPresetBundle("plex_movies");
            using var resp = await server.Client.PostAsJsonAsync("/api/v1/manifest/test-regex", new
            {
                SampleInput = @"V:\Media\Movies\Blade Runner (1982) - UHD.mkv",
                Rules = rules
            }, JsonOpts);
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var extracted = doc.RootElement.GetProperty("ExtractedFields");
            Assert.True(extracted.EnumerateObject().Any());
            Assert.Equal("Blade Runner", extracted.GetProperty("MovieTitle").GetString());
            Assert.Equal("1982", extracted.GetProperty("MovieYear").GetString());
        }

        [Fact]
        public async Task RomMultiRowSyncPreviewEndpoint_EmitsCommandsForAllEnabledSyncRows()
        {
            await using var server = await LiveServerHarness.StartAsync();

            var rom = new RomLauncherGeneratorConfig
            {
                EnablePrePostLaunchSyncCommands = true,
                DefaultSyncPaths = new List<GameSyncPathItem>
                {
                    new() { Enabled = true, Label = "Saves", LocalPath = @"D:\RetroArch\saves", RemoteTarget = "gdrive:Emu/saves", SyncMode = "bisync" },
                    new() { Enabled = true, Label = "States", LocalPath = @"D:\RetroArch\states", RemoteTarget = "gdrive:Emu/states", SyncMode = "sync" },
                    new() { Enabled = false, Label = "DisabledRow", LocalPath = @"D:\RetroArch\screens", RemoteTarget = "gdrive:Emu/screens", SyncMode = "copy" }
                }
            };

            using var resp = await server.Client.PostAsJsonAsync("/api/v1/rom/sync-preview", new
            {
                RomConfig = rom
            }, JsonOpts);
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.Equal(2, doc.RootElement.GetProperty("preLaunchCommands").GetArrayLength());
            Assert.Equal(2, doc.RootElement.GetProperty("postLaunchCommands").GetArrayLength());
        }

        [Fact]
        public async Task ComplianceReportsEndpoints_ServeKpis_DryRunPreview_IgnoreToggle_CsvExport_AndClearResolved()
        {
            await using var server = await LiveServerHarness.StartAsync();
            string fakeArchive = Path.Combine(server.DataDir, "Super Metroid (USA).7z");
            File.WriteAllText(fakeArchive, "stub");

            var sampleReport = new ComplianceReportItem
            {
                ReportId = "snes-super-metroid",
                ConsoleName = "SNES",
                TargetFileName = "Super Metroid (USA).7z",
                TargetFullPath = fakeArchive,
                IsDirectory = false,
                TemplateId = "cartridge-single-rom",
                TemplateDisplayName = "Cartridge / Single-File ROM",
                Severity = ComplianceSeverity.Warning,
                ResolutionStatus = ComplianceReportResolutionStatus.Open,
                SummaryCode = "WARN: ExtraFiles(1)",
                SummaryText = "1 extra non-template file detected (readme.txt)",
                PrimaryCategory = "Extra Files",
                IssueCount = 1,
                ExtraDeletableCount = 1,
                EntryNodes = new List<ArchiveEntryPreviewNode>
                {
                    new() { InternalPath = "Super Metroid (USA).sfc", SizeBytes = 3145728, RoleClassification = "Primary Payload", CanClean = false },
                    new() { InternalPath = "readme.txt", SizeBytes = 512, RoleClassification = "Extra File", CanClean = true, SelectedForCleanup = true }
                }
            };
            server.Runtime.ComplianceReports.UpsertSingleTargetResult(sampleReport);

            // 1. KPIs & list
            using var listResp = await server.Client.GetAsync("/api/v1/reports");
            listResp.EnsureSuccessStatusCode();
            using var listDoc = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync());
            Assert.Equal(1, listDoc.RootElement.GetProperty("kpis").GetProperty("openCount").GetInt32());
            Assert.Equal(1, listDoc.RootElement.GetProperty("kpis").GetProperty("cleanableCount").GetInt32());

            // 2. 7z d dry-run preview
            using var dryResp = await server.Client.PostAsJsonAsync("/api/v1/reports/snes-super-metroid/dry-run-clean", new
            {
                ExplicitInternalPaths = new[] { "readme.txt" }
            });
            dryResp.EnsureSuccessStatusCode();
            string dryJson = await dryResp.Content.ReadAsStringAsync();
            Assert.Contains("readme.txt", dryJson);
            Assert.Contains("Super Metroid (USA).sfc", dryJson);

            // 3. CSV export
            using var csvResp = await server.Client.GetAsync("/api/v1/reports/export-csv");
            csvResp.EnsureSuccessStatusCode();
            string csvText = await csvResp.Content.ReadAsStringAsync();
            Assert.Contains("Super Metroid (USA).7z", csvText);

            // 4. Ignore -> stays muted; MarkResolved -> Clear Resolved purges resolved entries
            using var ignoreResp = await server.Client.PostAsJsonAsync("/api/v1/reports/ignore", new
            {
                ReportIds = new[] { "snes-super-metroid" },
                Ignored = true
            });
            ignoreResp.EnsureSuccessStatusCode();
            Assert.Equal(ComplianceReportResolutionStatus.Ignored, server.Runtime.ComplianceReports.GetAllReports().Single().ResolutionStatus);

            server.Runtime.ComplianceReports.MarkResolved(new[] { "snes-super-metroid" });
            using var clearResp = await server.Client.PostAsync("/api/v1/reports/clear-resolved", null);
            clearResp.EnsureSuccessStatusCode();
            Assert.Empty(server.Runtime.ComplianceReports.GetAllReports());
        }

        [Fact]
        public async Task FilesystemEndpoints_ListDrives_BrowseDirectories_AndInvokeLocalhostNativeDialogBridge()
        {
            await using var server = await LiveServerHarness.StartAsync();
            string subFolder = Path.Combine(server.DataDir, "SampleMediaFolder");
            Directory.CreateDirectory(subFolder);
            File.WriteAllText(Path.Combine(subFolder, "episode01.mkv"), "video");

            using var drivesResp = await server.Client.GetAsync("/api/v1/filesystem/drives");
            drivesResp.EnsureSuccessStatusCode();
            using var drivesDoc = JsonDocument.Parse(await drivesResp.Content.ReadAsStringAsync());
            Assert.True(drivesDoc.RootElement.GetArrayLength() >= 1);

            using var browseResp = await server.Client.GetAsync($"/api/v1/filesystem/browse?path={Uri.EscapeDataString(subFolder)}&includeFiles=true");
            browseResp.EnsureSuccessStatusCode();
            string browseJson = await browseResp.Content.ReadAsStringAsync();
            Assert.Contains("episode01.mkv", browseJson);

            server.DialogBridge.NextFolderResult = subFolder;
            using var pickResp = await server.Client.PostAsJsonAsync("/api/v1/filesystem/native-pick-folder", new
            {
                InitialPath = server.DataDir,
                Title = "Pick Media Folder"
            });
            pickResp.EnsureSuccessStatusCode();
            using var pickDoc = JsonDocument.Parse(await pickResp.Content.ReadAsStringAsync());
            Assert.False(pickDoc.RootElement.GetProperty("cancelled").GetBoolean());
            Assert.Equal(subFolder, pickDoc.RootElement.GetProperty("path").GetString());
        }

        [Fact]
        public async Task InteractiveTerminalEndpoints_ExecuteCommands_TrackWorkingDirectory_AndCaptureLogs()
        {
            await using var server = await LiveServerHarness.StartAsync();
            string childDir = Path.Combine(server.DataDir, "CliSubDir");
            Directory.CreateDirectory(childDir);

            using var cdResp = await server.Client.PostAsJsonAsync("/api/v1/terminal/exec", new { Command = $"cd \"{childDir}\"" });
            cdResp.EnsureSuccessStatusCode();
            using var cdDoc = JsonDocument.Parse(await cdResp.Content.ReadAsStringAsync());
            Assert.Equal(childDir, cdDoc.RootElement.GetProperty("workingDirectory").GetString(), ignoreCase: true);

            using var echoResp = await server.Client.PostAsJsonAsync("/api/v1/terminal/exec", new { Command = "echo HK3_TERMINAL_VERIFIED" });
            echoResp.EnsureSuccessStatusCode();

            using var histResp = await server.Client.GetAsync("/api/v1/terminal/history?limit=100");
            histResp.EnsureSuccessStatusCode();
            string histJson = await histResp.Content.ReadAsStringAsync();
            Assert.Contains("HK3_TERMINAL_VERIFIED", histJson);

            using var clearResp = await server.Client.PostAsync("/api/v1/terminal/clear", null);
            clearResp.EnsureSuccessStatusCode();
        }

        [Fact]
        public async Task SignalRHub_StreamsRealtimePipelineStateAndCoalescedTerminalLogBatches()
        {
            await using var server = await LiveServerHarness.StartAsync();

            var initialStateTcs = new TaskCompletionSource<PipelineRuntimeStateDto>(TaskCreationOptions.RunContinuationsAsynchronously);
            var logBatchTcs = new TaskCompletionSource<List<TerminalLogEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);

            await using var hub = new HubConnectionBuilder()
                .WithUrl($"{server.BaseUrl}/hubs/housekeeping")
                .Build();

            hub.On<PipelineRuntimeStateDto>("PipelineState", state =>
            {
                initialStateTcs.TrySetResult(state);
            });

            hub.On<List<TerminalLogEntry>>("TerminalLogBatch", batch =>
            {
                if (batch.Any(e => e.Message.Contains("SIGNALR_LIVE_STREAM_OK", StringComparison.Ordinal)))
                {
                    logBatchTcs.TrySetResult(batch);
                }
            });

            await hub.StartAsync();

            var receivedState = await initialStateTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(receivedState);
            Assert.False(receivedState.IsRunning);

            server.Runtime.Orchestrator.Log("SUCCESS", "SIGNALR_LIVE_STREAM_OK");
            server.Runtime.LogBuffer.FlushPendingBatch();

            var receivedBatch = await logBatchTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains(receivedBatch, e => e.Message.Contains("SIGNALR_LIVE_STREAM_OK", StringComparison.Ordinal));

            await hub.StopAsync();
        }

        [Fact]
        public void RemoteNodeDeploymentService_AllowsUpgradeFromLegacy2xBetaToV1Rc1_AndSettingsSyncOnMatchingV1()
        {
            var legacyBetaNode = new RemoteNodeConfig
            {
                Name = "Server",
                IsReachable = true,
                DetectedVersion = "2.8.5-rc.4"
            };
            RemoteNodeDeploymentService.EvaluateVersionEligibility(legacyBetaNode, "1.0.0-rc.1");
            Assert.True(legacyBetaNode.CanUpdateApp);
            Assert.False(legacyBetaNode.CanUpdateSettings);

            var matchingV1Node = new RemoteNodeConfig
            {
                Name = "Server",
                IsReachable = true,
                DetectedVersion = "1.0.0-rc.2"
            };
            RemoteNodeDeploymentService.EvaluateVersionEligibility(matchingV1Node, "1.0.0-rc.2");
            Assert.False(matchingV1Node.CanUpdateApp);
            Assert.True(matchingV1Node.CanUpdateSettings);
        }

        [Fact]
        public async Task OfflineRemoteUncNode_FastFailsWithoutBlockingSockets_AndTerminalExecReturnsDirectEntries()
        {
            await using var server = await LiveServerHarness.StartAsync();

            // 1. Add an unreachable TEST-NET-1 (192.0.2.254) remote node with UNC SMB path
            server.Runtime.ConfigService.Config.RemoteNodes.Add(new RemoteNodeConfig
            {
                Id = "offline-node-1",
                Name = "OfflineTestServer",
                HostOrIp = "192.0.2.254",
                RemoteAppPath = @"\\192.0.2.254\OfflineShare\Housekeeping",
                PreferHttpApi = true,
                HttpApiPort = 5178
            });
            server.Runtime.ConfigService.Save();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var tailResp = await server.Client.GetAsync("/api/v1/terminal/remote-tail/offline-node-1?limit=50");
            sw.Stop();
            tailResp.EnsureSuccessStatusCode();
            using var tailDoc = JsonDocument.Parse(await tailResp.Content.ReadAsStringAsync());
            Assert.False(tailDoc.RootElement.GetProperty("online").GetBoolean());
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"Expected fast offline pre-flight < 5s, actual {sw.Elapsed.TotalSeconds:F2}s");

            // Second consecutive call hits 15s offline cooldown in < 2s (HTTP 1.5s + 0ms SMB cooldown)
            sw.Restart();
            using var tailResp2 = await server.Client.GetAsync("/api/v1/terminal/remote-tail/offline-node-1?limit=50");
            sw.Stop();
            tailResp2.EnsureSuccessStatusCode();
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"Expected cached offline SMB cooldown < 3s, actual {sw.Elapsed.TotalSeconds:F2}s");

            // 2. Interactive CLI exec returns direct entries immediately in JSON payload
            using var echoResp = await server.Client.PostAsJsonAsync("/api/v1/terminal/exec", new { Command = "echo RC2_DIRECT_ECHO_OK" });
            echoResp.EnsureSuccessStatusCode();
            string echoBody = await echoResp.Content.ReadAsStringAsync();
            Assert.Contains("RC2_DIRECT_ECHO_OK", echoBody);

            // 3. Background .hash verifier starter starts immediately when an enabled folder exists
            string romDir = Path.Combine(server.DataDir, "SnesRoms");
            Directory.CreateDirectory(romDir);
            var romJob = new HousekeepingJob
            {
                JobId = "rom-verify-test",
                Name = "Test Launchers",
                ModuleType = ModuleId.RomLauncherGenerator,
                RomLauncherGenerator = new RomLauncherGeneratorConfig
                {
                    Collections = new List<ConsoleCollectionRule>
                    {
                        new() { Enabled = true, ConsoleName = "SNES", ArchivesPath = romDir, ExtensionsCsv = ".sfc" }
                    }
                }
            };
            server.Runtime.ConfigService.Config.DefinedJobs.Add(romJob);
            server.Runtime.ConfigService.Save();

            using var verResp = await server.Client.PostAsJsonAsync("/api/v1/hash-verifier/run", new
            {
                JobId = "rom-verify-test",
                SourceModule = "RomLauncherGenerator",
                VerifyMode = "AuditOnlyNoChanges"
            });
            verResp.EnsureSuccessStatusCode();
            using var verDoc = JsonDocument.Parse(await verResp.Content.ReadAsStringAsync());
            Assert.True(verDoc.RootElement.GetProperty("ok").GetBoolean());
            Assert.True(verDoc.RootElement.GetProperty("started").GetBoolean());

            // 4. Stalled SMB session (TCP :445 open, mrxsmb.sys blocked) caps strictly at timeoutMs and caches cooldown
            sw.Restart();
            bool uncRes1 = await RemoteNodeDeploymentService.RunBoundedUncOperationAsync(
                @"\\STALLED-TEST-HOST\Share\Housekeeping",
                () => { Thread.Sleep(4000); return true; },
                fallbackOnTimeoutOrBusy: false,
                timeoutMs: 350,
                cooldownSeconds: 10);
            sw.Stop();
            Assert.False(uncRes1);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1.5), $"Expected bounded UNC timeout < 1.5s, actual {sw.Elapsed.TotalMilliseconds:F0}ms");

            sw.Restart();
            bool uncRes2 = await RemoteNodeDeploymentService.RunBoundedUncOperationAsync(
                @"\\STALLED-TEST-HOST\Share\Housekeeping",
                () => true,
                fallbackOnTimeoutOrBusy: false,
                timeoutMs: 350,
                cooldownSeconds: 10);
            sw.Stop();
            Assert.False(uncRes2);
            Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(150), $"Expected instant cooldown return < 150ms, actual {sw.Elapsed.TotalMilliseconds:F0}ms");
        }

        [Fact]
        public async Task MidShutdownQueueRun_PersistsInterruptedCheckpoint_ResumesRemainingJobsAfterRestart_AndClearsOnExplicitStop()
        {
            string sharedDataDir = Path.Combine(Path.GetTempPath(), $"hk3_rc3_resume_{Guid.NewGuid():N}");
            Directory.CreateDirectory(sharedDataDir);

            try
            {
                string job1Id;
                string job2Id;

                // 1. Start Instance 1, complete Job 1, block mid-execution inside Job 2, then shut down Instance 1!
                {
                    await using var server1 = await LiveServerHarness.StartAsync(existingDataDir: sharedDataDir, deleteDataDirOnDispose: false);
                    var cfg = server1.Runtime.ConfigService.Config;
                    cfg.SetModuleAppWideEnabled(ModuleId.ProxyGenerator, true);
                    cfg.SetModuleAppWideEnabled(ModuleId.CollectionManifest, true);

                    var job1 = ModuleCatalogService.CreateNewJobForModuleType(cfg, ModuleId.ProxyGenerator, "Step 1 Completed Before Shutdown");
                    var job2 = ModuleCatalogService.CreateNewJobForModuleType(cfg, ModuleId.CollectionManifest, "Step 2 Interrupted Mid-Shutdown");
                    job1Id = job1.JobId;
                    job2Id = job2.JobId;

                    cfg.DefinedJobs = new List<HousekeepingJob> { job1, job2 };
                    cfg.GetActiveQueue().Jobs = new List<HousekeepingJob> { job1, job2 };
                    server1.Runtime.ConfigService.Save();

                    var enteredJob2 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    server1.Runtime.Orchestrator.CustomModuleExecutor = async (ctx, ct) =>
                    {
                        if (ctx.ActiveJob?.JobId == job1Id)
                        {
                            ctx.Stats.Processed++;
                            return;
                        }

                        enteredJob2.TrySetResult(true);
                        await Task.Delay(TimeSpan.FromSeconds(30), ct);
                    };

                    var (started, _) = server1.Runtime.Orchestrator.StartQueueRun();
                    Assert.True(started);
                    await enteredJob2.Task.WaitAsync(TimeSpan.FromSeconds(5));
                }

                // Checkpoint file must exist on disk after mid-run server shutdown!
                string cpPath = Path.Combine(sharedDataDir, "interrupted_run_checkpoint.json");
                Assert.True(File.Exists(cpPath), "Expected interrupted_run_checkpoint.json to survive mid-run shutdown.");

                // 2. Boot Instance 2 on the same data directory -> InterruptedRun is surfaced, and resuming runs ONLY Job 2!
                await using var server2 = await LiveServerHarness.StartAsync(existingDataDir: sharedDataDir, deleteDataDirOnDispose: false);
                var bootState = await server2.Client.GetFromJsonAsync<PipelineRuntimeStateDto>("/api/v1/pipeline/state", JsonOpts);
                Assert.NotNull(bootState);
                Assert.NotNull(bootState!.InterruptedRun);
                Assert.Equal("Queue", bootState.InterruptedRun!.RunKind);
                Assert.Contains(job1Id, bootState.InterruptedRun.CompletedJobIds);
                Assert.Equal(job2Id, bootState.InterruptedRun.ActiveJobId);

                var resumedJobIds = new List<string>();
                server2.Runtime.Orchestrator.CustomModuleExecutor = (ctx, _) =>
                {
                    resumedJobIds.Add(ctx.ActiveJob!.JobId);
                    ctx.Stats.Processed++;
                    return Task.CompletedTask;
                };

                using var resumeResp = await server2.Client.PostAsJsonAsync("/api/v1/pipeline/interrupted/resume", new { RestartFullQueue = false });
                resumeResp.EnsureSuccessStatusCode();

                var execTask = server2.Runtime.Orchestrator.ActiveExecutionTask;
                if (execTask != null)
                    await execTask.WaitAsync(TimeSpan.FromSeconds(5));

                Assert.Equal(new[] { job2Id }, resumedJobIds);
                Assert.False(File.Exists(cpPath), "Expected checkpoint file to be cleared after resumed run completes cleanly.");

                // 3. Explicit user Stop clears checkpoint (not treated as mid-shutdown interruption!)
                var enteredStopTest = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                server2.Runtime.Orchestrator.CustomModuleExecutor = async (_, ct) =>
                {
                    enteredStopTest.TrySetResult(true);
                    await Task.Delay(TimeSpan.FromSeconds(30), ct);
                };

                var (startedStopRun, _) = server2.Runtime.Orchestrator.StartSingleJobRun(job2Id);
                Assert.True(startedStopRun);
                await enteredStopTest.Task.WaitAsync(TimeSpan.FromSeconds(5));

                using var stopResp = await server2.Client.PostAsync("/api/v1/pipeline/stop", null);
                stopResp.EnsureSuccessStatusCode();

                var stopTask = server2.Runtime.Orchestrator.ActiveExecutionTask;
                if (stopTask != null)
                    await stopTask.WaitAsync(TimeSpan.FromSeconds(5));

                var afterStopState = await server2.Client.GetFromJsonAsync<PipelineRuntimeStateDto>("/api/v1/pipeline/state", JsonOpts);
                Assert.Null(afterStopState!.InterruptedRun);
                Assert.False(File.Exists(cpPath));
            }
            finally
            {
                try { if (Directory.Exists(sharedDataDir)) Directory.Delete(sharedDataDir, recursive: true); } catch (System.Exception __suppressedEx5) { System.Diagnostics.Debug.WriteLine($"[ServerAndOrchestratorTests] Non-fatal error ignored: {__suppressedEx5.Message}"); }
            }
        }

        [Fact]
        public async Task RealtimeSessionTotals_BroadcastLiveMidJobDeltas_AndBoundedDriveScanReturnsMountedDrives()
        {
            await using var server = await LiveServerHarness.StartAsync();
            var cfg = server.Runtime.ConfigService.Config;
            cfg.SetModuleAppWideEnabled(ModuleId.ProxyGenerator, true);

            var job = ModuleCatalogService.CreateNewJobForModuleType(cfg, ModuleId.ProxyGenerator, "Live Session Totals Test");
            cfg.DefinedJobs = new List<HousekeepingJob> { job };
            cfg.GetActiveQueue().Jobs = new List<HousekeepingJob> { job };
            server.Runtime.ConfigService.Save();

            var midRunBroadcastSeen = new TaskCompletionSource<PipelineRuntimeStateDto>(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowJobFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            server.Runtime.Orchestrator.OnStateChanged += st =>
            {
                if (st.IsRunning && st.SessionProcessed >= 3 && st.SessionSkipped >= 2 && st.SessionErrors >= 1)
                {
                    midRunBroadcastSeen.TrySetResult(st);
                }
            };

            server.Runtime.Orchestrator.CustomModuleExecutor = async (ctx, ct) =>
            {
                ctx.Stats.Processed++;
                ctx.Stats.Processed++;
                ctx.Stats.Processed++;
                ctx.Stats.Skipped++;
                ctx.Stats.Skipped++;
                ctx.Stats.Errors++;
                // Wait past 120ms throttle window and fire one more log/delta if needed
                await Task.Delay(150, ct);
                ctx.Log("WORK", "Mid-job progress tick");
                await allowJobFinish.Task.WaitAsync(ct);
            };

            var (started, _) = server.Runtime.Orchestrator.StartSingleJobRun(job.JobId);
            Assert.True(started);

            var liveMidState = await midRunBroadcastSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(3, liveMidState.SessionProcessed);
            Assert.Equal(2, liveMidState.SessionSkipped);
            Assert.Equal(1, liveMidState.SessionErrors);

            allowJobFinish.TrySetResult(true);
            var execTask = server.Runtime.Orchestrator.ActiveExecutionTask;
            if (execTask != null)
                await execTask.WaitAsync(TimeSpan.FromSeconds(5));

            // Bounded local + network drive scan completes fast and returns system drive
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var scannedDrives = await SystemPerformanceTelemetryService.ScanLocalAndNetworkDrivesBoundedAsync();
            sw.Stop();
            Assert.NotEmpty(scannedDrives);
            Assert.Contains(scannedDrives, d => d.DriveLetter.Equals("C:", StringComparison.OrdinalIgnoreCase));
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"Expected bounded drive scan < 5s, actual {sw.Elapsed.TotalSeconds:F2}s");
        }

        [Fact]
        public async Task PwaManifest_Icons_ServiceWorker_AndTailscalePwaEndpoint_MeetAndroidWebApkRequirements()
        {
            await using var server = await LiveServerHarness.StartAsync();

            // Simulate an unauthenticated remote Android client over Tailscale IP first:
            // manifest, icons, and sw.js MUST all serve 200 OK without 401 gating.
            server.AuthService.SetAdminPassword("pwaTestPass!");
            server.AuthService.SetSimulatedRemoteIpForTesting(IPAddress.Parse("100.125.204.101"));

            using var manifestResp = await server.Client.GetAsync("/manifest.webmanifest");
            Assert.Equal(HttpStatusCode.OK, manifestResp.StatusCode);
            Assert.Equal("application/manifest+json", manifestResp.Content.Headers.ContentType?.MediaType);
            Assert.Contains("no-cache", manifestResp.Headers.CacheControl?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);

            using var manifestDoc = JsonDocument.Parse(await manifestResp.Content.ReadAsStringAsync());
            var root = manifestDoc.RootElement;
            Assert.Equal("/", root.GetProperty("id").GetString());
            Assert.Equal("/", root.GetProperty("start_url").GetString());
            Assert.Equal("standalone", root.GetProperty("display").GetString());

            var icons = root.GetProperty("icons").EnumerateArray().ToList();
            Assert.True(icons.Count >= 4, "Expected separate 192x192 and 512x512 icons for both 'any' and 'maskable' purposes.");

            var seenSizePurpose = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var icon in icons)
            {
                string src = icon.GetProperty("src").GetString() ?? "";
                string sizes = icon.GetProperty("sizes").GetString() ?? "";
                string type = icon.GetProperty("type").GetString() ?? "";
                string purpose = icon.GetProperty("purpose").GetString() ?? "";

                Assert.Equal("image/png", type);
                seenSizePurpose.Add($"{sizes}:{purpose}");

                using var iconResp = await server.Client.GetAsync(src);
                Assert.Equal(HttpStatusCode.OK, iconResp.StatusCode);
                Assert.Equal("image/png", iconResp.Content.Headers.ContentType?.MediaType);

                byte[] pngBytes = await iconResp.Content.ReadAsByteArrayAsync();
                Assert.True(pngBytes.Length > 24, $"Icon '{src}' is too small to be a valid PNG.");
                // PNG signature + big-endian IHDR width (bytes 16..19) & height (bytes 20..23)
                Assert.Equal(0x89, pngBytes[0]);
                Assert.Equal((byte)'P', pngBytes[1]);
                Assert.Equal((byte)'N', pngBytes[2]);
                Assert.Equal((byte)'G', pngBytes[3]);

                int actualWidth = (pngBytes[16] << 24) | (pngBytes[17] << 16) | (pngBytes[18] << 8) | pngBytes[19];
                int actualHeight = (pngBytes[20] << 24) | (pngBytes[21] << 16) | (pngBytes[22] << 8) | pngBytes[23];
                Assert.Equal(sizes, $"{actualWidth}x{actualHeight}");
            }

            Assert.Contains("192x192:any", seenSizePurpose);
            Assert.Contains("512x512:any", seenSizePurpose);
            Assert.Contains("192x192:maskable", seenSizePurpose);
            Assert.Contains("512x512:maskable", seenSizePurpose);

            // Service worker must serve publicly with no-cache and Service-Worker-Allowed: /
            using var swResp = await server.Client.GetAsync("/sw.js");
            Assert.Equal(HttpStatusCode.OK, swResp.StatusCode);
            Assert.Contains("no-cache", swResp.Headers.CacheControl?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
            Assert.True(swResp.Headers.TryGetValues("Service-Worker-Allowed", out var swScopeVals));
            Assert.Contains("/", swScopeVals!);
            string swJs = await swResp.Content.ReadAsStringAsync();
            Assert.Contains("'/api/'", swJs);
            Assert.Contains("'/hubs/'", swJs);

            // Authenticated caller (loopback) can query Tailscale PWA status endpoint
            server.AuthService.SetSimulatedRemoteIpForTesting(null);
            using var tsResp = await server.Client.GetAsync("/api/v1/system/tailscale-pwa");
            Assert.Equal(HttpStatusCode.OK, tsResp.StatusCode);
            using var tsDoc = JsonDocument.Parse(await tsResp.Content.ReadAsStringAsync());
            Assert.True(tsDoc.RootElement.TryGetProperty("TailscaleAvailable", out _));
            Assert.True(tsDoc.RootElement.TryGetProperty("ServeConfigured", out _));
            Assert.True(tsDoc.RootElement.TryGetProperty("StatusMessage", out _));
        }
    }
}
