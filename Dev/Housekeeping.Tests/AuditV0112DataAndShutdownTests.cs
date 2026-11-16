// @module: Housekeeping.Tests.AuditV0112DataAndShutdownTests
// @purpose: Regression suite for BUG-01 (exclusive SMB pre-probe & root migration), BUG-02/T2 (dual-mode stop vs graceful shutdown), BUG-03, and OPT-02.
// @depends-on: System, System.Collections.Concurrent, System.IO, System.Net.Sockets, System.Text.Json, Housekeeping.Models, Housekeeping.Server.Hosting, Housekeeping.Services, Xunit
// @exports: Housekeeping.Tests.AuditV0112DataAndShutdownTests

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
using System.Threading.Tasks;
using Housekeeping.Models;
using Housekeeping.Server.Hosting;
using Housekeeping.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Housekeeping.Tests
{
    public sealed class AuditV0112DataAndShutdownTests
    {
        private static int GetFreeLoopbackPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        private static HousekeepingJob MakeCustomJob(string id, string name) => new()
        {
            JobId = id,
            Name = name,
            Enabled = true,
            ModuleType = ModuleId.CustomScripts,
            CustomScripts = new CustomScriptsConfig { Enabled = true }
        };

        [Fact]
        public void Bug01_CommitPartialEncode_NeverDeletesDestination_WhenWorkingPartialLocked_AndLogsWorkingLock()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"hk_bug01_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                string workingPath = Path.Combine(tempDir, "Episode01.partial.mkv");
                string finalPath = Path.Combine(tempDir, "Episode01.mkv");
                File.WriteAllText(workingPath, "NEW_ENCODED_PAYLOAD");
                File.WriteAllText(finalPath, "ORIGINAL_GOLD_MASTER");

                var logs = new List<string>();
                var pendingSwaps = new PendingSwapService(Path.Combine(tempDir, "pending_swaps.json"));
                var svc = new NetworkSafetyService(() => new AppConfig(), (tag, msg) => logs.Add($"[{tag}] {msg}"), pendingSwaps);

                using (var exclusiveLock = new FileStream(workingPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    bool result = svc.CommitPartialEncode(workingPath, finalPath, maxAttempts: 2, initialDelayMs: 5);
                    Assert.True(result);
                }

                Assert.True(File.Exists(finalPath));
                Assert.Equal("ORIGINAL_GOLD_MASTER", File.ReadAllText(finalPath));
                Assert.True(File.Exists(workingPath));
                Assert.Equal(1, pendingSwaps.Count);
                Assert.Contains(logs, l => l.Contains("Working partial file", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(logs, l => l.Contains("Destination file 'Episode01.mkv' is busy", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                TestCleanupHelper.TryDeleteDirectory(tempDir);
            }
        }

        [Fact]
        public void Bug01_CommitPartialEncode_StagesFallbackNew_WhenDestinationIsExclusivelyLocked()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"hk_bug01_dest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                string workingPath = Path.Combine(tempDir, "Movie.partial.mkv");
                string finalPath = Path.Combine(tempDir, "Movie.mkv");
                File.WriteAllText(workingPath, "ENCODED_HEVC_DATA");
                File.WriteAllText(finalPath, "LOCKED_READER_TARGET");

                var logs = new List<string>();
                var pendingSwaps = new PendingSwapService(Path.Combine(tempDir, "pending_swaps.json"));
                var svc = new NetworkSafetyService(() => new AppConfig(), (tag, msg) => logs.Add($"[{tag}] {msg}"), pendingSwaps);

                using (var readerLock = new FileStream(finalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    bool ok = svc.CommitPartialEncode(workingPath, finalPath, maxAttempts: 2, initialDelayMs: 5);
                    Assert.True(ok);
                }

                Assert.Equal("LOCKED_READER_TARGET", File.ReadAllText(finalPath));
                Assert.True(File.Exists(finalPath + ".new"));
                Assert.Equal(1, pendingSwaps.Count);
                Assert.Contains(logs, l => l.Contains("Destination file 'Movie.mkv' is busy", StringComparison.OrdinalIgnoreCase));

                int committed = pendingSwaps.SweepAndCommitAll((t, m) => logs.Add($"[{t}] {m}"));
                Assert.Equal(1, committed);
                Assert.Equal("ENCODED_HEVC_DATA", File.ReadAllText(finalPath));
            }
            finally
            {
                TestCleanupHelper.TryDeleteDirectory(tempDir);
            }
        }

        [Fact]
        public void Bug01_MigrateLegacyRootStateIfPresent_PromotesRichRootSettings_AndCleansRootResidue()
        {
            string rootDir = Path.Combine(Path.GetTempPath(), $"hk_mig_{Guid.NewGuid():N}");
            string dataDir = Path.Combine(rootDir, "data");
            Directory.CreateDirectory(rootDir);
            Directory.CreateDirectory(dataDir);
            try
            {
                string richJson = new string(' ', 35_000) + "{\"ConfigVersion\":4}";
                File.WriteAllText(Path.Combine(rootDir, "settings.json"), richJson);
                File.WriteAllText(Path.Combine(dataDir, "settings.json"), "{\"ConfigVersion\":4}");
                File.WriteAllText(Path.Combine(rootDir, "history.json"), "[{\"Status\":\"Success\"}]");

                bool migrated = ConfigService.MigrateLegacyRootStateIfPresent(rootDir, dataDir, out string? msg);
                Assert.True(migrated);
                Assert.NotNull(msg);
                Assert.False(File.Exists(Path.Combine(rootDir, "settings.json")));
                Assert.False(File.Exists(Path.Combine(rootDir, "history.json")));
                Assert.Equal(richJson.Length, new FileInfo(Path.Combine(dataDir, "settings.json")).Length);
                Assert.True(File.Exists(Path.Combine(dataDir, "history.json")));
            }
            finally
            {
                TestCleanupHelper.TryDeleteDirectory(rootDir);
            }
        }

        [Fact]
        public async Task Bug02_ThreeTierStopAndGracefulShutdown_PreservesOrClearsCheckpointsDeterministically()
        {
            string dataDir = Path.Combine(Path.GetTempPath(), $"hk_bug02_{Guid.NewGuid():N}");
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
            var rt = app.Services.GetRequiredService<HousekeepingRuntimeContainer>();
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            try
            {
                var cfg = rt.ConfigService.Config.DeepClone();
                cfg.CustomScripts.Enabled = true;
                cfg.SetModuleAppWideEnabled(ModuleId.CustomScripts, true);
                var j1 = MakeCustomJob("j1", "Step 1");
                var j2 = MakeCustomJob("j2", "Step 2");
                var j3 = MakeCustomJob("j3", "Queued Followup");
                cfg.DefinedJobs = new List<HousekeepingJob> { j1, j2, j3 };
                cfg.NamedQueues = new List<NamedJobQueue>
                {
                    new() { Id = "q-main", Name = "Main Queue", Jobs = new List<HousekeepingJob> { j1, j2 } }
                };
                Assert.True(rt.ConfigService.TryUpdateAndSave(cfg, out _));

                // Case A: StopCurrentJob cancels Step 1, executes Step 2, and runs Up Next j3!
                var executed = new ConcurrentQueue<string>();
                var j1Started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var j3Completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                rt.Orchestrator.CustomModuleExecutor = async (ctx, ct) =>
                {
                    string id = ctx.ActiveJob?.JobId ?? "";
                    executed.Enqueue(id);
                    if (id == "j1")
                    {
                        j1Started.TrySetResult(true);
                        await Task.Delay(TimeSpan.FromSeconds(15), ct);
                    }
                    else if (id == "j3")
                    {
                        j3Completed.TrySetResult(true);
                    }
                };

                Assert.True(rt.Orchestrator.StartOrEnqueueQueueRun("q-main", isDryRun: false).Started);
                await j1Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(rt.Orchestrator.StartOrEnqueueSingleJobRun("j3", isDryRun: false).Queued);

                using (var stopJobResp = await client.PostAsync("/api/v1/pipeline/stop-job", null))
                    Assert.Equal(HttpStatusCode.OK, stopJobResp.StatusCode);

                await j3Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (rt.Orchestrator.ActiveExecutionTask != null)
                    await rt.Orchestrator.ActiveExecutionTask.WaitAsync(TimeSpan.FromSeconds(5));

                Assert.Equal(new[] { "j1", "j2", "j3" }, executed.ToArray());

                // Case B: InitiateGracefulShutdownAbort preserves interrupted_run_checkpoint.json AND PendingRuns!
                var j1ShutdownStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                rt.Orchestrator.CustomModuleExecutor = async (ctx, ct) =>
                {
                    if (ctx.ActiveJob?.JobId == "j1")
                    {
                        j1ShutdownStarted.TrySetResult(true);
                        await Task.Delay(TimeSpan.FromSeconds(15), ct);
                    }
                };

                Assert.True(rt.Orchestrator.StartOrEnqueueQueueRun("q-main", isDryRun: false).Started);
                await j1ShutdownStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(rt.Orchestrator.StartOrEnqueueSingleJobRun("j3", isDryRun: false).Queued);

                rt.Orchestrator.InitiateGracefulShutdownAbort("UnitTestShutdown");
                if (rt.Orchestrator.ActiveExecutionTask != null)
                    await TestCleanupHelper.TryAwaitAsync(() => rt.Orchestrator.ActiveExecutionTask!);

                string ckptPath = Path.Combine(dataDir, "interrupted_run_checkpoint.json");
                Assert.True(File.Exists(ckptPath));
                var savedCkpt = JsonSerializer.Deserialize<InterruptedRunCheckpoint>(File.ReadAllText(ckptPath), ConfigService.JsonOptions);
                Assert.NotNull(savedCkpt);
                Assert.Single(savedCkpt!.PendingRuns);
                Assert.Equal("SingleJob:j3", savedCkpt.PendingRuns[0].ItemKey);

                // Case C: StopActiveRun explicitly clears PendingRuns and deletes checkpoint!
                rt.Orchestrator.StopActiveRun();
                Assert.False(File.Exists(ckptPath));
                Assert.Empty(rt.Orchestrator.GetPendingRuns());
            }
            finally
            {
                await TestCleanupHelper.TryAwaitAsync(() => app.StopAsync());
                await TestCleanupHelper.TryAwaitAsync(async () => await app.DisposeAsync());
                TestCleanupHelper.TryDispose(rt);
                TestCleanupHelper.TryDeleteDirectory(dataDir);
            }
        }

        [Fact]
        public void Bug03_And_Opt02_CrashFilter_LogPrefixDedup_AndJsonExtensionData_WorkCleanly()
        {
            // BUG-03: Benign socket reset 10054 & OperationCanceledException suppressed; SetObserved() called
            var resetEx = new AggregateException(new HttpRequestException("Idle reset", new IOException("Read failed", new SocketException((int)SocketError.ConnectionReset))));
            Assert.True(CrashBundleExceptionFilter.IsBenignBackgroundException(resetEx));
            Assert.True(CrashBundleExceptionFilter.IsBenignBackgroundException(new OperationCanceledException()));
            Assert.False(CrashBundleExceptionFilter.IsBenignBackgroundException(new InvalidOperationException("Real bug")));

            string tempLogs = Path.Combine(Path.GetTempPath(), $"hk_crash_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempLogs);
            try
            {
                var args = new UnobservedTaskExceptionEventArgs(resetEx);
                bool wrote = CrashBundleService.HandleUnobservedTaskException(args, logsDirOverride: tempLogs, buildVersionOverride: "0.1.12-hf.1");
                Assert.True(args.Observed);
                Assert.False(wrote);

                // OPT-02: TerminalLogBufferService strips duplicate [WARN] & HandBrake [HH:mm:ss] while keeping [INFO] [WORK]
                string liveLogPath;
                using (var buf = new TerminalLogBufferService(tempLogs))
                {
                    liveLogPath = buf.LiveLogFilePath;
                    buf.Append("WARN", "[WARN] Evicting stale lockfile");
                    buf.Append("OUT", "[14:22:09] Encoding: task 1 of 1, 45.20 %");
                    buf.Append("WORK", "Scanning vault...");
                }
                string liveText = ConfigService.ReadAllTextWithRetry(liveLogPath);
                Assert.DoesNotContain("[WARN] [WARN]", liveText);
                Assert.DoesNotContain("[OUT] [14:22:09]", liveText);
                Assert.Contains("[INFO] [WORK] Scanning vault...", liveText);

                // Standard 19 §2.3: [JsonExtensionData] + UiLayout round-trip preservation
                string rawCfg = "{\"ConfigVersion\":4,\"FutureSisterKey\":\"keep-me\",\"UiLayout\":{\"TerminalHeightPx\":360,\"TerminalSizePreset\":\"large\",\"SidebarCollapsed\":true}}";
                var parsed = JsonSerializer.Deserialize<AppConfig>(rawCfg, ConfigService.JsonOptions)!;
                Assert.Equal(360, parsed.UiLayout.TerminalHeightPx);
                Assert.Equal("large", parsed.UiLayout.TerminalSizePreset);
                Assert.True(parsed.UiLayout.SidebarCollapsed);
                string reserialized = JsonSerializer.Serialize(parsed, ConfigService.JsonOptions);
                Assert.Contains("\"FutureSisterKey\"", reserialized);
                Assert.Contains("\"keep-me\"", reserialized);
            }
            finally
            {
                TestCleanupHelper.TryDeleteDirectory(tempLogs);
            }
        }
    }
}
