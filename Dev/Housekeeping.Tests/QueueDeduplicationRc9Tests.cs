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
using Housekeeping.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Housekeeping.Tests
{
    public sealed class QueueDeduplicationRc9Tests
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

        private sealed class Rc9ServerHarness : IAsyncDisposable
        {
            public string DataDir { get; }
            public int Port { get; }
            public string BaseUrl => $"http://127.0.0.1:{Port}";
            public WebApplication App { get; }
            public HousekeepingRuntimeContainer Runtime { get; }
            public HttpClient Client { get; }

            private Rc9ServerHarness(string dataDir, int port, WebApplication app, HousekeepingRuntimeContainer runtime)
            {
                DataDir = dataDir;
                Port = port;
                App = app;
                Runtime = runtime;
                Client = new HttpClient
                {
                    BaseAddress = new Uri(BaseUrl),
                    Timeout = TimeSpan.FromSeconds(15)
                };
            }

            public static async Task<Rc9ServerHarness> StartAsync()
            {
                string dataDir = Path.Combine(Path.GetTempPath(), $"hk_rc9_test_{Guid.NewGuid():N}");
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
                return new Rc9ServerHarness(dataDir, port, app, runtime);
            }

            public async Task<AppConfig> FetchConfigAsync()
            {
                using var resp = await Client.GetAsync("/api/v1/config");
                resp.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                return JsonSerializer.Deserialize<AppConfig>(doc.RootElement.GetProperty("config").GetRawText(), JsonOpts)!;
            }

            public async ValueTask DisposeAsync()
            {
                Client.Dispose();
                await TestCleanupHelper.TryAwaitAsync(() => App.StopAsync(), "App.StopAsync");
                await TestCleanupHelper.TryAwaitAsync(async () => await App.DisposeAsync(), "App.DisposeAsync");
                TestCleanupHelper.TryDispose(Runtime, "HousekeepingRuntimeContainer");
                TestCleanupHelper.TryDeleteDirectory(DataDir);
            }
        }

        private static HousekeepingJob MakeTestJob(string id, string name) => new()
        {
            JobId = id,
            Name = name,
            Enabled = true,
            ModuleType = ModuleId.CustomScripts,
            CustomScripts = new CustomScriptsConfig { Enabled = true }
        };

        private static void EnableCustomScriptsAppWide(AppConfig cfg)
        {
            cfg.CustomScripts.Enabled = true;
            cfg.SetModuleAppWideEnabled(ModuleId.CustomScripts, true);
        }

        [Fact]
        public void Dst001_ShouldTriggerQueueNow_PreservesAmbiguousDstFallbackBit_AndNeverFiresEvery15Seconds()
        {
            var cfg = new AppConfig();
            var scheduler = new SchedulerAndNotificationService(
                () => cfg,
                (_, _) => { },
                (_, _, _) => { });

            // 2026-11-01 05:24:40 UTC is inside the 1:00-2:00 AM fall-back hour across US DST zones (e.g. Eastern/Central).
            // Converting from UTC via .ToLocalTime() sets .NET's internal s_kindLocalAmbiguous bit on local DST systems.
            var baseUtc = new DateTime(2026, 11, 1, 5, 24, 40, DateTimeKind.Utc);
            var queue = new NamedJobQueue
            {
                Id = "hourly-updates",
                Name = "Hourly Updates",
                EnableSchedule = true,
                ScheduleMode = MasterScheduleMode.IntervalCadence,
                ScheduleIntervalMinutes = 60,
                LastRunUtc = baseUtc
            };

            // 15 seconds later as DateTimeKind.Local (preserving ambiguous bit if local zone has DST fall-back at this instant)
            var tick15sLocal = baseUtc.AddSeconds(15).ToLocalTime();
            Assert.False(scheduler.ShouldTriggerQueueNow(queue, tick15sLocal));
            Assert.Equal(baseUtc, queue.LastRunUtc);

            // Also verify direct UTC evaluation at +15s and +59m
            Assert.False(scheduler.ShouldTriggerQueueNow(queue, baseUtc.AddSeconds(15)));
            Assert.False(scheduler.ShouldTriggerQueueNow(queue, baseUtc.AddMinutes(59)));

            // At +60m, the queue must trigger once and advance LastRunUtc cleanly
            var dueLocal = baseUtc.AddMinutes(60).ToLocalTime();
            Assert.True(scheduler.ShouldTriggerQueueNow(queue, dueLocal));
            Assert.Equal(baseUtc.AddMinutes(60), queue.LastRunUtc);

            // Immediately 15 seconds after triggering, it must NOT re-trigger
            Assert.False(scheduler.ShouldTriggerQueueNow(queue, baseUtc.AddMinutes(60).AddSeconds(15).ToLocalTime()));
        }

        [Fact]
        public async Task Cfg002_PutConfig_PreservesNewerServerOwnedLastRunUtc_AgainstStaleClientPayload()
        {
            await using var harness = await Rc9ServerHarness.StartAsync();

            // Fetch stale snapshot BEFORE the server stamps LastRunUtc
            var staleClientCfg = await harness.FetchConfigAsync();
            Assert.NotEmpty(staleClientCfg.NamedQueues);
            string targetQueueId = staleClientCfg.NamedQueues[0].Id;
            staleClientCfg.NamedQueues[0].LastRunUtc = new DateTime(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc);
            staleClientCfg.LastMasterScheduleRunUtc = new DateTime(2026, 11, 1, 2, 0, 0, DateTimeKind.Utc);

            // Simulate server stamping newer LastRunUtc timestamps during scheduled execution
            var serverQueueStamp = new DateTime(2026, 11, 1, 6, 15, 0, DateTimeKind.Utc);
            var serverMasterStamp = new DateTime(2026, 11, 1, 6, 10, 0, DateTimeKind.Utc);
            var serverClone = harness.Runtime.ConfigService.Config.DeepClone();
            serverClone.NamedQueues.First(q => q.Id == targetQueueId).LastRunUtc = serverQueueStamp;
            serverClone.LastMasterScheduleRunUtc = serverMasterStamp;
            Assert.True(harness.Runtime.ConfigService.TryUpdateAndSave(serverClone, out _));

            // Stale browser tab saves an unrelated UI edit via PUT /api/v1/config with older LastRunUtc
            staleClientCfg.NamedQueues[0].Description = "Updated from browser tab";
            using var putContent = new StringContent(JsonSerializer.Serialize(staleClientCfg, ConfigService.JsonOptions), Encoding.UTF8, "application/json");
            using var putResp = await harness.Client.PutAsync("/api/v1/config", putContent);
            Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);

            var savedCfg = harness.Runtime.ConfigService.Config;
            var savedQueue = savedCfg.NamedQueues.First(q => q.Id == targetQueueId);
            Assert.Equal("Updated from browser tab", savedQueue.Description);
            Assert.Equal(serverQueueStamp, savedQueue.LastRunUtc);
            Assert.Equal(serverMasterStamp, savedCfg.LastMasterScheduleRunUtc);
        }

        [Fact]
        public async Task Queue003_DistinctFifoWaitList_RejectsActiveAndDuplicateItems_AndExecutesInFifoOrder()
        {
            await using var harness = await Rc9ServerHarness.StartAsync();

            var cfg = harness.Runtime.ConfigService.Config.DeepClone();
            EnableCustomScriptsAppWide(cfg);

            var job1 = MakeTestJob("job-q1", "Job Q1");
            var job2 = MakeTestJob("job-q2", "Job Q2");
            var job3 = MakeTestJob("job-j3", "Standalone Job 3");

            cfg.NamedQueues = new List<NamedJobQueue>
            {
                new NamedJobQueue { Id = "q1", Name = "Hourly Updates", Jobs = new List<HousekeepingJob> { job1 } },
                new NamedJobQueue { Id = "q2", Name = "Nightly Manifests", Jobs = new List<HousekeepingJob> { job2 } },
                new NamedJobQueue { Id = "q3", Name = "Adhoc Pool", Jobs = new List<HousekeepingJob> { job3 } }
            };
            cfg.ActiveQueueId = "q1";
            Assert.True(harness.Runtime.ConfigService.TryUpdateAndSave(cfg, out var saveErr), string.Join("; ", saveErr));

            var executionOrder = new ConcurrentQueue<string>();
            var gates = new ConcurrentDictionary<string, TaskCompletionSource<bool>>(StringComparer.OrdinalIgnoreCase)
            {
                ["job-q1"] = new(TaskCreationOptions.RunContinuationsAsynchronously),
                ["job-q2"] = new(TaskCreationOptions.RunContinuationsAsynchronously),
                ["job-j3"] = new(TaskCreationOptions.RunContinuationsAsynchronously)
            };
            var startedSignals = new ConcurrentDictionary<string, TaskCompletionSource<bool>>(StringComparer.OrdinalIgnoreCase)
            {
                ["job-q1"] = new(TaskCreationOptions.RunContinuationsAsynchronously),
                ["job-q2"] = new(TaskCreationOptions.RunContinuationsAsynchronously),
                ["job-j3"] = new(TaskCreationOptions.RunContinuationsAsynchronously)
            };

            harness.Runtime.Orchestrator.CustomModuleExecutor = async (ctx, ct) =>
            {
                string jid = ctx.ActiveJob?.JobId ?? "unknown";
                executionOrder.Enqueue(jid);
                if (startedSignals.TryGetValue(jid, out var startedTcs))
                    startedTcs.TrySetResult(true);
                if (gates.TryGetValue(jid, out var gateTcs))
                    await gateTcs.Task.WaitAsync(ct);
            };

            // 1. Start Queue q1
            using var startQ1Resp = await harness.Client.PostAsync(
                "/api/v1/pipeline/run-queue",
                new StringContent("{\"queueId\":\"q1\",\"isDryRun\":false}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, startQ1Resp.StatusCode);
            await startedSignals["job-q1"].Task.WaitAsync(TimeSpan.FromSeconds(5));

            var startQ1Stamp = harness.Runtime.ConfigService.Config.NamedQueues.First(q => q.Id == "q1").LastRunUtc;
            Assert.NotNull(startQ1Stamp);

            // 2. Trying to enqueue q1 while q1 is actively running must be rejected with 409 Conflict
            using var dupActiveResp = await harness.Client.PostAsync(
                "/api/v1/pipeline/run-queue",
                new StringContent("{\"queueId\":\"q1\",\"isDryRun\":false}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Conflict, dupActiveResp.StatusCode);

            // 3. Enqueue q2 -> Accepted (queued = true)
            using var queueQ2Resp = await harness.Client.PostAsync(
                "/api/v1/pipeline/run-queue",
                new StringContent("{\"queueId\":\"q2\",\"isDryRun\":false}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, queueQ2Resp.StatusCode);
            using (var doc = JsonDocument.Parse(await queueQ2Resp.Content.ReadAsStringAsync()))
            {
                Assert.True(doc.RootElement.GetProperty("queued").GetBoolean());
                Assert.False(doc.RootElement.GetProperty("started").GetBoolean());
            }

            // 4. Trying to enqueue q2 a second time must be rejected with 409 Conflict
            using var dupQ2Resp = await harness.Client.PostAsync(
                "/api/v1/pipeline/run-queue",
                new StringContent("{\"queueId\":\"q2\",\"isDryRun\":false}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Conflict, dupQ2Resp.StatusCode);

            // 5. Enqueue standalone single job job-j3 -> Accepted (#2 in FIFO Up Next wait-list)
            using var queueJ3Resp = await harness.Client.PostAsync(
                "/api/v1/pipeline/run-job/job-j3",
                new StringContent("{\"isDryRun\":false}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, queueJ3Resp.StatusCode);

            // Duplicate single job must also be rejected with 409 Conflict
            using var dupJ3Resp = await harness.Client.PostAsync(
                "/api/v1/pipeline/run-job/job-j3",
                new StringContent("{\"isDryRun\":false}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Conflict, dupJ3Resp.StatusCode);

            var pending = harness.Runtime.Orchestrator.GetPendingRuns();
            Assert.Equal(2, pending.Count);
            Assert.Equal("Queue:q2", pending[0].ItemKey);
            Assert.Equal("SingleJob:job-j3", pending[1].ItemKey);

            // 6. Release q1 -> q2 auto-starts in FIFO order, and q1 completion refreshes LastRunUtc
            await Task.Delay(30);
            gates["job-q1"].TrySetResult(true);
            await startedSignals["job-q2"].Task.WaitAsync(TimeSpan.FromSeconds(5));

            var finishQ1Stamp = harness.Runtime.ConfigService.Config.NamedQueues.First(q => q.Id == "q1").LastRunUtc;
            Assert.NotNull(finishQ1Stamp);
            Assert.True(finishQ1Stamp.Value >= startQ1Stamp.Value);

            // 7. Release q2 -> job-j3 auto-starts in FIFO order
            gates["job-q2"].TrySetResult(true);
            await startedSignals["job-j3"].Task.WaitAsync(TimeSpan.FromSeconds(5));
            gates["job-j3"].TrySetResult(true);

            var activeTask = harness.Runtime.Orchestrator.ActiveExecutionTask;
            if (activeTask != null)
                await activeTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(new[] { "job-q1", "job-q2", "job-j3" }, executionOrder.ToArray());
            Assert.Empty(harness.Runtime.Orchestrator.GetPendingRuns());
        }

        [Fact]
        public async Task Queue004_SchedulerAutoEnqueue_NeverQueuesInfiniteOrConsecutiveDuplicates()
        {
            await using var harness = await Rc9ServerHarness.StartAsync();

            var cfg = harness.Runtime.ConfigService.Config.DeepClone();
            EnableCustomScriptsAppWide(cfg);

            cfg.NamedQueues = new List<NamedJobQueue>
            {
                new NamedJobQueue
                {
                    Id = "q-active",
                    Name = "Active Long Job",
                    EnableSchedule = true,
                    ScheduleMode = MasterScheduleMode.IntervalCadence,
                    ScheduleIntervalMinutes = 60,
                    LastRunUtc = DateTime.UtcNow.AddHours(-3),
                    Jobs = new List<HousekeepingJob> { MakeTestJob("j-active", "Active Job") }
                },
                new NamedJobQueue
                {
                    Id = "q-hourly",
                    Name = "Hourly Updates",
                    EnableSchedule = true,
                    ScheduleMode = MasterScheduleMode.IntervalCadence,
                    ScheduleIntervalMinutes = 60,
                    LastRunUtc = DateTime.UtcNow.AddHours(-3),
                    Jobs = new List<HousekeepingJob> { MakeTestJob("j-hourly", "Hourly Job") }
                }
            };
            Assert.True(harness.Runtime.ConfigService.TryUpdateAndSave(cfg, out _));

            var gateActive = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var startedActive = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            harness.Runtime.Orchestrator.CustomModuleExecutor = async (ctx, ct) =>
            {
                if (ctx.ActiveJob?.JobId == "j-active")
                {
                    startedActive.TrySetResult(true);
                    await gateActive.Task.WaitAsync(ct);
                }
            };

            var startRes = harness.Runtime.Orchestrator.StartOrEnqueueQueueRun("q-active", isDryRun: false);
            Assert.True(startRes.Started);
            await startedActive.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Hammer EvaluateQueueSchedules 10 times, even forcing LastRunUtc back in the past each iteration
            for (int i = 0; i < 10; i++)
            {
                foreach (var q in harness.Runtime.ConfigService.Config.NamedQueues)
                {
                    q.LastRunUtc = DateTime.UtcNow.AddHours(-3);
                }
                harness.Runtime.Orchestrator.EvaluateQueueSchedules();
            }

            // q-active must NOT be queued because it is currently running, and q-hourly must appear AT MOST ONCE!
            var pending = harness.Runtime.Orchestrator.GetPendingRuns();
            Assert.Single(pending);
            Assert.Equal("Queue:q-hourly", pending[0].ItemKey);

            harness.Runtime.Orchestrator.StopActiveRun();
            gateActive.TrySetCanceled();
            var t = harness.Runtime.Orchestrator.ActiveExecutionTask;
            if (t != null) await TestCleanupHelper.TryAwaitAsync(() => t.WaitAsync(TimeSpan.FromSeconds(5)), "ActiveExecutionTask");
        }

        [Fact]
        public async Task Queue005_PendingRunRemoveClear_AndExplicitStop_ClearsWaitListWithoutAutoChaining()
        {
            await using var harness = await Rc9ServerHarness.StartAsync();

            var cfg = harness.Runtime.ConfigService.Config.DeepClone();
            EnableCustomScriptsAppWide(cfg);

            cfg.NamedQueues = new List<NamedJobQueue>
            {
                new NamedJobQueue { Id = "q1", Name = "Queue 1", Jobs = new List<HousekeepingJob> { MakeTestJob("j1", "Job 1") } },
                new NamedJobQueue { Id = "q2", Name = "Queue 2", Jobs = new List<HousekeepingJob> { MakeTestJob("j2", "Job 2") } },
                new NamedJobQueue { Id = "q3", Name = "Queue 3", Jobs = new List<HousekeepingJob> { MakeTestJob("j3", "Job 3") } }
            };
            Assert.True(harness.Runtime.ConfigService.TryUpdateAndSave(cfg, out _));

            var executedJobs = new ConcurrentQueue<string>();
            var gateJ1 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var startedJ1 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            harness.Runtime.Orchestrator.CustomModuleExecutor = async (ctx, ct) =>
            {
                string jid = ctx.ActiveJob?.JobId ?? "";
                executedJobs.Enqueue(jid);
                if (jid == "j1")
                {
                    startedJ1.TrySetResult(true);
                    await gateJ1.Task.WaitAsync(ct);
                }
            };

            Assert.True(harness.Runtime.Orchestrator.StartOrEnqueueQueueRun("q1").Started);
            await startedJ1.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(harness.Runtime.Orchestrator.StartOrEnqueueQueueRun("q2").Queued);
            Assert.True(harness.Runtime.Orchestrator.StartOrEnqueueSingleJobRun("j3").Queued);
            Assert.Equal(2, harness.Runtime.Orchestrator.GetPendingRuns().Count);

            // Remove Queue:q2 via REST endpoint
            using var delResp = await harness.Client.DeleteAsync("/api/v1/pipeline/pending-runs/Queue%3Aq2");
            Assert.Equal(HttpStatusCode.OK, delResp.StatusCode);
            Assert.Single(harness.Runtime.Orchestrator.GetPendingRuns());
            Assert.Equal("SingleJob:j3", harness.Runtime.Orchestrator.GetPendingRuns()[0].ItemKey);

            // Re-add q2, test Clear endpoint, then re-add q2 + j3 and click Stop
            Assert.True(harness.Runtime.Orchestrator.StartOrEnqueueQueueRun("q2").Queued);
            using var clearResp = await harness.Client.PostAsync("/api/v1/pipeline/pending-runs/clear", null);
            Assert.Equal(HttpStatusCode.OK, clearResp.StatusCode);
            Assert.Empty(harness.Runtime.Orchestrator.GetPendingRuns());

            Assert.True(harness.Runtime.Orchestrator.StartOrEnqueueQueueRun("q2").Queued);
            Assert.True(harness.Runtime.Orchestrator.StartOrEnqueueSingleJobRun("j3").Queued);
            Assert.Equal(2, harness.Runtime.Orchestrator.GetPendingRuns().Count);

            // Clicking Stop must clear PendingRuns AND prevent finally-block auto-chaining!
            using var stopResp = await harness.Client.PostAsync("/api/v1/pipeline/stop", null);
            Assert.Equal(HttpStatusCode.OK, stopResp.StatusCode);
            Assert.Empty(harness.Runtime.Orchestrator.GetPendingRuns());

            var activeTask = harness.Runtime.Orchestrator.ActiveExecutionTask;
            if (activeTask != null)
                await activeTask.WaitAsync(TimeSpan.FromSeconds(5));

            await Task.Delay(150);
            Assert.False(harness.Runtime.Orchestrator.IsRunning);
            Assert.Equal(new[] { "j1" }, executedJobs.ToArray());
        }
    }
}
