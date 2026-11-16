using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Housekeeping.Models;
using Housekeeping.Services;
using Xunit;

namespace Housekeeping.Tests
{
    public class Rc10PlanRegressionTests
    {
        private static string CreateTempTestDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "Housekeeping_Rc10Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact]
        public void Bug01_TryResolveOrRecoverPendingEncode_RecoversCompletedPartialFromPendingSwapsWithoutReencoding()
        {
            string tempDir = CreateTempTestDir();
            try
            {
                string swapsPath = Path.Combine(tempDir, "pending_swaps.json");
                var pendingSwaps = new PendingSwapService(swapsPath);
                var cfg = AppConfig.CreateDefaults();
                cfg.EnableAtomicPartialWrites = true;
                var logs = new List<string>();
                var netSafety = new NetworkSafetyService(() => cfg, (tag, msg) => logs.Add($"[{tag}] {msg}"), pendingSwaps);

                string finalOutput = Path.Combine(tempDir, "Movie (1080p).mkv");
                string partialOutput = netSafety.GetSafeEncodeOutputPath(finalOutput);
                File.WriteAllText(partialOutput, "completed-hevc-encode-payload", Encoding.UTF8);

                pendingSwaps.AddOrUpdate(partialOutput, finalOutput);
                Assert.Equal(1, pendingSwaps.Count);

                var checkResult = netSafety.TryResolveOrRecoverPendingEncode(finalOutput, (tag, msg) => logs.Add($"[{tag}] {msg}"));

                Assert.Equal(PendingSwapCheckResult.Committed, checkResult);
                Assert.True(File.Exists(finalOutput));
                Assert.False(File.Exists(partialOutput));
                Assert.Equal("completed-hevc-encode-payload", File.ReadAllText(finalOutput, Encoding.UTF8));
                Assert.Equal(0, pendingSwaps.Count);
            }
            finally
            {
                TestCleanupHelper.TryDeleteDirectory(tempDir);
            }
        }

        [Fact]
        public void Bug01_TryResolveOrRecoverPendingEncode_ShieldsLockedPendingPartialFromBeingClobbered()
        {
            string tempDir = CreateTempTestDir();
            try
            {
                string swapsPath = Path.Combine(tempDir, "pending_swaps.json");
                var pendingSwaps = new PendingSwapService(swapsPath);
                var cfg = AppConfig.CreateDefaults();
                cfg.EnableAtomicPartialWrites = true;
                var logs = new List<string>();
                var netSafety = new NetworkSafetyService(() => cfg, (tag, msg) => logs.Add($"[{tag}] {msg}"), pendingSwaps);

                string finalOutput = Path.Combine(tempDir, "Episode01 (720p).mkv");
                string partialOutput = netSafety.GetSafeEncodeOutputPath(finalOutput);

                File.WriteAllText(finalOutput, "old-non-hevc-stream-being-watched", Encoding.UTF8);
                File.WriteAllText(partialOutput, "completed-new-hevc-encode-must-not-be-overwritten", Encoding.UTF8);
                pendingSwaps.AddOrUpdate(partialOutput, finalOutput);

                // Hold an exclusive lock on finalOutput to simulate an active SMB/media stream reader
                using (var readerLock = new FileStream(finalOutput, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    var checkResult = netSafety.TryResolveOrRecoverPendingEncode(finalOutput, (tag, msg) => logs.Add($"[{tag}] {msg}"));

                    Assert.Equal(PendingSwapCheckResult.PendingLocked, checkResult);
                    Assert.True(pendingSwaps.IsPending(partialOutput));
                }

                // Verify the completed partial encode was NOT deleted or truncated
                Assert.True(File.Exists(partialOutput));
                Assert.Equal("completed-new-hevc-encode-must-not-be-overwritten", File.ReadAllText(partialOutput, Encoding.UTF8));
            }
            finally
            {
                TestCleanupHelper.TryDeleteDirectory(tempDir);
            }
        }

        [Fact]
        public void Bug01_TryResolveOrRecoverPendingEncode_RecoversUnregisteredNewFallbackFile()
        {
            string tempDir = CreateTempTestDir();
            try
            {
                string swapsPath = Path.Combine(tempDir, "pending_swaps.json");
                var pendingSwaps = new PendingSwapService(swapsPath);
                var cfg = AppConfig.CreateDefaults();
                var logs = new List<string>();
                var netSafety = new NetworkSafetyService(() => cfg, (tag, msg) => logs.Add($"[{tag}] {msg}"), pendingSwaps);

                string finalOutput = Path.Combine(tempDir, "ArchiveClip.mp4");
                string fallbackNew = finalOutput + ".new";
                File.WriteAllText(fallbackNew, "completed-fallback-new-stream", Encoding.UTF8);

                var checkResult = netSafety.TryResolveOrRecoverPendingEncode(finalOutput, (tag, msg) => logs.Add($"[{tag}] {msg}"));

                Assert.Equal(PendingSwapCheckResult.Committed, checkResult);
                Assert.True(File.Exists(finalOutput));
                Assert.False(File.Exists(fallbackNew));
                Assert.Equal("completed-fallback-new-stream", File.ReadAllText(finalOutput, Encoding.UTF8));
            }
            finally
            {
                TestCleanupHelper.TryDeleteDirectory(tempDir);
            }
        }

        [Fact]
        public void Bug02_GetPerModuleSchedulesDueNow_UsesMonotonicUtcAcrossDstFallBackBoundary()
        {
            var cfg = AppConfig.CreateDefaults();
            cfg.CollectionManifest.EnableCustomSchedule = true;
            cfg.CollectionManifest.ScheduleIntervalMinutes = 30;

            var scheduler = new SchedulerAndNotificationService(
                () => cfg,
                (_, _) => { },
                (_, _, _) => { });

            // Simulate first tick at 05:30 UTC (01:30 EDT before DST fall-back)
            var t0Utc = new DateTime(2026, 11, 1, 5, 30, 0, DateTimeKind.Utc);
            var due0 = scheduler.GetPerModuleSchedulesDueNow(t0Utc);
            Assert.Empty(due0); // First tick initializes baseline LastScheduledRunUtc
            Assert.Equal(t0Utc, cfg.CollectionManifest.LastScheduledRunUtc);

            // Simulate 20 minutes later at 05:50 UTC -> not yet due (interval is 30 min)
            var t1Utc = t0Utc.AddMinutes(20);
            var due1 = scheduler.GetPerModuleSchedulesDueNow(t1Utc);
            Assert.Empty(due1);
            Assert.Equal(t0Utc, cfg.CollectionManifest.LastScheduledRunUtc);

            // Simulate 31 minutes after t0 at 06:01 UTC (across the 02:00 -> 01:00 local DST fall-back hour!)
            var t2Utc = t0Utc.AddMinutes(31);
            var due2 = scheduler.GetPerModuleSchedulesDueNow(t2Utc);
            Assert.Contains(ModuleId.CollectionManifest, due2);
            Assert.Equal(t2Utc, cfg.CollectionManifest.LastScheduledRunUtc);

            // Simulate 30 minutes after t2 at 06:31 UTC -> fires on schedule without stalling for an extra hour
            var t3Utc = t2Utc.AddMinutes(30);
            var due3 = scheduler.GetPerModuleSchedulesDueNow(t3Utc);
            Assert.Contains(ModuleId.CollectionManifest, due3);
            Assert.Equal(t3Utc, cfg.CollectionManifest.LastScheduledRunUtc);
        }

        [Fact]
        public void Bug02_ConfigService_PersistsAndPreservesModuleLastScheduledRunUtcAgainstStaleBrowserSnapshot()
        {
            string tempDir = CreateTempTestDir();
            try
            {
                var configService = new ConfigService(customBaseDir: tempDir, isolatedOverride: true);

                // Browser takes an initial snapshot when LastScheduledRunUtc is null
                var staleBrowserDraft = configService.Config.DeepClone();
                Assert.Null(staleBrowserDraft.CollectionManifest.LastScheduledRunUtc);

                // Server scheduler stamps LastScheduledRunUtc and persists to disk
                var serverStampUtc = new DateTime(2026, 11, 2, 4, 0, 0, DateTimeKind.Utc);
                Assert.True(configService.MutateAndSave(cfg =>
                {
                    cfg.CollectionManifest.EnableCustomSchedule = true;
                    cfg.CollectionManifest.ScheduleIntervalMinutes = 60;
                    cfg.CollectionManifest.LastScheduledRunUtc = serverStampUtc;
                }));

                // Verify settings.json on disk persisted LastScheduledRunUtc
                var reloaded = new ConfigService(customBaseDir: tempDir, isolatedOverride: true);
                Assert.Equal(serverStampUtc, reloaded.Config.CollectionManifest.LastScheduledRunUtc);

                // Browser now saves its stale draft (where LastScheduledRunUtc was still null)
                staleBrowserDraft.EnableAtomicPartialWrites = true;
                Assert.True(configService.TryUpdateAndSave(staleBrowserDraft, out var errors));
                Assert.Empty(errors);

                // Server-owned LastScheduledRunUtc must be preserved!
                Assert.Equal(serverStampUtc, configService.Config.CollectionManifest.LastScheduledRunUtc);
            }
            finally
            {
                TestCleanupHelper.TryDeleteDirectory(tempDir);
            }
        }

        [Fact]
        public void Bug03_CrashBundleService_WritesValidStandard15JsonWith50PrecedingLogLinesAndSanitizesPii()
        {
            string tempDir = CreateTempTestDir();
            try
            {
                string logsDir = Path.Combine(tempDir, "logs");
                using var logBuffer = new TerminalLogBufferService(logsDir, maxCapacity: 200);
                var cfg = AppConfig.CreateDefaults();

                string fakeProfileRoot = string.Concat("C:", "\\", "Users", "\\", "StreamDeveloper");
                for (int i = 1; i <= 65; i++)
                {
                    logBuffer.Append("INFO", $"Log line #{i} referencing {fakeProfileRoot}\\Videos\\episode_{i}.mkv", cfg);
                }

                InvalidOperationException capturedEx;
                try
                {
                    throw new InvalidOperationException($"Simulated pipeline failure at {fakeProfileRoot}\\Secrets\\config.json with --token superSecret123");
                }
                catch (InvalidOperationException ex)
                {
                    capturedEx = ex;
                }

                string? bundlePath = CrashBundleService.WriteCrashBundle(
                    capturedEx,
                    logsDirOverride: logsDir,
                    buildVersionOverride: "1.0.0-rc.10",
                    isolatedModeOverride: true,
                    archetypeOverride: "2-tray-companion",
                    logBufferOverride: logBuffer);

                Assert.NotNull(bundlePath);
                Assert.True(File.Exists(bundlePath!));

                string rawJson = File.ReadAllText(bundlePath!, Encoding.UTF8);
                Assert.DoesNotContain("StreamDeveloper", rawJson, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("superSecret123", rawJson, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("%USERPROFILE%", rawJson, StringComparison.Ordinal);

                var bundle = JsonSerializer.Deserialize<CrashBundleDto>(rawJson);
                Assert.NotNull(bundle);
                Assert.Equal("1.0", bundle!.SchemaVersion);
                Assert.Equal("Housekeeping", bundle.AppName);
                Assert.Equal("1.0.0-rc.10", bundle.BuildVersion);
                Assert.Equal("2-tray-companion", bundle.Archetype);
                Assert.True(bundle.IsolatedMode);
                Assert.True(bundle.Process.Pid > 0);
                Assert.Equal("System.InvalidOperationException", bundle.Exception.Type);
                Assert.Contains("%USERPROFILE%", bundle.Exception.Message);
                Assert.Contains("[REDACTED]", bundle.Exception.Message);
                Assert.NotEmpty(bundle.Exception.StackTrace);

                // Must capture exactly the last 50 preceding log lines (lines #16 through #65)
                Assert.Equal(50, bundle.PrecedingLogRingBuffer.Count);
                Assert.Contains("Log line #16", bundle.PrecedingLogRingBuffer.First());
                Assert.Contains("Log line #65", bundle.PrecedingLogRingBuffer.Last());
            }
            finally
            {
                TestCleanupHelper.TryDeleteDirectory(tempDir);
            }
        }

        [Fact]
        public void Bug03_CrashBundleService_Enforces20FileRetentionCapInCrashesDirectory()
        {
            string tempDir = CreateTempTestDir();
            try
            {
                string logsDir = Path.Combine(tempDir, "logs");
                string crashesDir = Path.Combine(logsDir, "crashes");
                Directory.CreateDirectory(crashesDir);

                // Seed 24 older crash files with staggered timestamps
                var baseTimeUtc = DateTime.UtcNow.AddHours(-2);
                for (int i = 1; i <= 24; i++)
                {
                    string filePath = Path.Combine(crashesDir, $"crash_20261101_0000{i:D2}_v1.0.0-rc.10.json");
                    File.WriteAllText(filePath, "{}", Encoding.UTF8);
                    File.SetLastWriteTimeUtc(filePath, baseTimeUtc.AddMinutes(i));
                }

                // Writing one more crash bundle brings total to 25 and triggers retention pruning to 20
                string? newestBundle = CrashBundleService.WriteCrashBundle(
                    new ApplicationException("Retention cap verification crash"),
                    logsDirOverride: logsDir,
                    buildVersionOverride: "1.0.0-rc.10",
                    isolatedModeOverride: true);

                Assert.NotNull(newestBundle);
                var remaining = Directory.GetFiles(crashesDir, "crash_*.json");
                Assert.Equal(CrashBundleService.MaxRetainedCrashBundles, remaining.Length);

                // Oldest 5 files (01..05) should have been pruned
                for (int i = 1; i <= 5; i++)
                {
                    Assert.False(File.Exists(Path.Combine(crashesDir, $"crash_20261101_0000{i:D2}_v1.0.0-rc.10.json")));
                }
            }
            finally
            {
                TestCleanupHelper.TryDeleteDirectory(tempDir);
            }
        }
    }
}
