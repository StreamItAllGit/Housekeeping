// @module: Housekeeping.Tests.AuditV0112FleetDoctorCliTests
// @purpose: Regression suite for BUG-04 (unique atomic .tmp & Clean-Release-Root SMB), BUG-05 (Doctor --log-dir & sibling backup), and BUG-06 (--status/--repair-config & .tsv/.csv).
// @depends-on: System, System.Collections.Generic, System.IO, System.IO.Compression, System.Net, System.Text.Json, Housekeeping.Models, Housekeeping.Server.Hosting, Housekeeping.Services, Xunit
// @exports: Housekeeping.Tests.AuditV0112FleetDoctorCliTests

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Models;
using Housekeeping.Server.Hosting;
using Housekeeping.Services;
using Housekeeping.Services.Modules;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Housekeeping.Tests
{
    public sealed class AuditV0112FleetDoctorCliTests
    {
        private static int GetFreeLoopbackPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        [Fact]
        public async Task Bug04_AtomicWriteAllText_ConcurrentCallsNeverCollide_AndLeaveZeroTmpFiles()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"hk_atomic_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                string target = Path.Combine(tempDir, "settings.json");
                var tasks = Enumerable.Range(0, 16).Select(i => Task.Run(() =>
                {
                    string payload = $"{{\"ConfigVersion\":4,\"Iter\":{i}}}";
                    ConfigService.AtomicWriteAllText(target, payload);
                }));
                await Task.WhenAll(tasks);

                Assert.True(File.Exists(target));
                using var doc = JsonDocument.Parse(File.ReadAllText(target));
                Assert.Equal(4, doc.RootElement.GetProperty("ConfigVersion").GetInt32());
                Assert.Empty(Directory.GetFiles(tempDir, "*.tmp"));
            }
            finally
            {
                TestCleanupHelper.TryDeleteDirectory(tempDir);
            }
        }

        [Fact]
        public void Bug04_RemoteNodeDeploymentService_ResolvesCleanReleaseRoot_PreservesFlatFallback_AndExcludesRuntimeState()
        {
            string cleanRoot = Path.Combine(Path.GetTempPath(), $"hk_clean_remote_{Guid.NewGuid():N}");
            string legacyRoot = Path.Combine(Path.GetTempPath(), $"hk_flat_remote_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(cleanRoot, "app"));
            Directory.CreateDirectory(Path.Combine(cleanRoot, "data"));
            Directory.CreateDirectory(legacyRoot);
            try
            {
                File.WriteAllText(Path.Combine(cleanRoot, "app", "version.txt"), "0.1.12-hf.1");
                File.WriteAllText(Path.Combine(cleanRoot, "data", "settings.json"), "{\"ConfigVersion\":4}");

                Assert.False(RemoteNodeDeploymentService.IsLegacyFlatRemoteRoot(cleanRoot));
                Assert.Equal(Path.Combine(cleanRoot, "app"), RemoteNodeDeploymentService.ResolveRemoteBinaryDir(cleanRoot));
                Assert.Equal(Path.Combine(cleanRoot, "data", "settings.json"), RemoteNodeDeploymentService.ResolveRemoteSettingsReadPath(cleanRoot));
                Assert.Equal(Path.Combine(cleanRoot, "data", "settings.json"), RemoteNodeDeploymentService.ResolveRemoteSettingsWritePath(cleanRoot));
                Assert.Equal("0.1.12-hf.1", RemoteNodeDeploymentService.DetectRemoteAppVersionFromDisk(cleanRoot));

                // Legacy flat node fallback
                File.WriteAllText(Path.Combine(legacyRoot, "settings.json"), "{\"ConfigVersion\":4}");
                Assert.True(RemoteNodeDeploymentService.IsLegacyFlatRemoteRoot(legacyRoot));
                Assert.Equal(legacyRoot, RemoteNodeDeploymentService.ResolveRemoteBinaryDir(legacyRoot));
                Assert.Equal(Path.Combine(legacyRoot, "settings.json"), RemoteNodeDeploymentService.ResolveRemoteSettingsWritePath(legacyRoot));

                // Runtime exclusions
                Assert.True(RemoteNodeDeploymentService.ShouldExcludeFromRemoteBinarySync("data\\settings.json", "settings.json"));
                Assert.True(RemoteNodeDeploymentService.ShouldExcludeFromRemoteBinarySync("backups\\b1.7z", "b1.7z"));
                Assert.True(RemoteNodeDeploymentService.ShouldExcludeFromRemoteBinarySync("webview2_cache\\lockfile", "lockfile"));
                Assert.True(RemoteNodeDeploymentService.ShouldExcludeFromRemoteBinarySync("pending_swaps.json", "pending_swaps.json"));
                Assert.True(RemoteNodeDeploymentService.ShouldExcludeFromRemoteBinarySync(".hk_cookie_secret.key", ".hk_cookie_secret.key"));
                Assert.True(RemoteNodeDeploymentService.ShouldExcludeFromRemoteBinarySync("vault\\.housekeeping_12.hklock", ".housekeeping_12.hklock"));
                Assert.False(RemoteNodeDeploymentService.ShouldExcludeFromRemoteBinarySync("wwwroot\\index.html", "index.html"));
            }
            finally
            {
                TestCleanupHelper.TryDeleteDirectory(cleanRoot);
                TestCleanupHelper.TryDeleteDirectory(legacyRoot);
            }
        }

        [Fact]
        public async Task Bug05_SystemDoctorExportAndBackup_RespectActiveLogsDir_AndSiblingBackupsWithoutLockFailure()
        {
            string isolatedRoot = Path.Combine(Path.GetTempPath(), $"hk_bug05_{Guid.NewGuid():N}");
            string dataDir = Path.Combine(isolatedRoot, "data");
            string logsDir = Path.Combine(isolatedRoot, "logs");
            Directory.CreateDirectory(dataDir);
            Directory.CreateDirectory(logsDir);

            int port = GetFreeLoopbackPort();
            var app = HousekeepingServerHost.BuildWebApplication(
                Array.Empty<string>(),
                new HousekeepingServerOptions
                {
                    PortOverride = port,
                    BindLanOverride = false,
                    CustomDataDirectory = dataDir,
                    CustomLogsDirectory = logsDir,
                    IsolatedOverride = true,
                    StartBackgroundTimers = false
                });

            await app.StartAsync();
            var rt = app.Services.GetRequiredService<HousekeepingRuntimeContainer>();
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            try
            {
                // Hold an exclusive lock inside webview2_cache & open log handle to prove backup never touches caches/self
                string cacheDir = Path.Combine(dataDir, "webview2_cache");
                Directory.CreateDirectory(cacheDir);
                using var lockedCacheHandle = new FileStream(Path.Combine(cacheDir, "EBWebView.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);

                using var exportResp = await client.PostAsync("/api/v1/system/doctor/export-bundle", null);
                Assert.Equal(HttpStatusCode.OK, exportResp.StatusCode);
                string exportedLogs = Directory.GetFiles(logsDir, "housekeeping_diagnostics_*.json").Single();
                Assert.StartsWith(logsDir, exportedLogs, StringComparison.OrdinalIgnoreCase);

                using var backupResp = await client.PostAsync("/api/v1/system/backup/now", null);
                Assert.Equal(HttpStatusCode.OK, backupResp.StatusCode);
                using var backupDoc = JsonDocument.Parse(await backupResp.Content.ReadAsStringAsync());
                string backupPath = backupDoc.RootElement.GetProperty("backupPath").GetString()!;
                string expectedBackupsDir = Path.Combine(isolatedRoot, "backups");
                Assert.StartsWith(expectedBackupsDir, backupPath, StringComparison.OrdinalIgnoreCase);
                Assert.True(File.Exists(backupPath));
            }
            finally
            {
                await TestCleanupHelper.TryAwaitAsync(() => app.StopAsync());
                await TestCleanupHelper.TryAwaitAsync(async () => await app.DisposeAsync());
                TestCleanupHelper.TryDispose(rt);
                TestCleanupHelper.TryDeleteDirectory(isolatedRoot);
            }
        }

        [Fact]
        public async Task Bug06_And_T6_OneShotCliStatusAndRepair_PlusManifestCsvAndTsvExporters_WorkEndToEnd()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), $"hk_bug06_{Guid.NewGuid():N}");
            string dataDir = Path.Combine(tempRoot, "data");
            string logsDir = Path.Combine(tempRoot, "logs");
            string exportDir = Path.Combine(tempRoot, "exports");
            Directory.CreateDirectory(dataDir);
            Directory.CreateDirectory(logsDir);
            try
            {
                // Seed valid .bak and corrupt active settings.json
                var validCfg = AppConfig.CreateDefaults();
                validCfg.ConfigVersion = AppConfig.CurrentSchemaVersion;
                validCfg.PostRunPowerAction = "Sleep";
                File.WriteAllText(Path.Combine(dataDir, "settings.json.bak"), JsonSerializer.Serialize(validCfg, ConfigService.JsonOptions));
                File.WriteAllText(Path.Combine(dataDir, "settings.json"), "{ CORRUPT JSON !!!");

                var parsedRepair = HousekeepingCliParser.Parse(new[] { "--repair-config", "--data-dir", dataDir, "--log-dir", logsDir });
                Assert.True(parsedRepair.RunRepairConfig);
                Assert.Equal(0, HousekeepingCliRunner.RunRepairConfig(parsedRepair));
                Assert.NotEmpty(Directory.GetFiles(Path.Combine(dataDir, "quarantine"), "settings.corrupt_*.json"));

                var reloaded = new ConfigService(customBaseDir: dataDir, customLogsDir: logsDir, isolatedOverride: true);
                Assert.Equal("Sleep", reloaded.Config.PostRunPowerAction);

                var parsedStatus = HousekeepingCliParser.Parse(new[] { "--status", "--data-dir", dataDir, "--log-dir", logsDir });
                Assert.True(parsedStatus.RunStatus);
                Assert.Equal(0, HousekeepingCliRunner.RunStatusCheck(parsedStatus));

                // Fail-closed isolation check (APP_ISOLATED_TEST=1 without --data-dir/--config returns exit code 2)
                string? prevIso = Environment.GetEnvironmentVariable("APP_ISOLATED_TEST");
                try
                {
                    Environment.SetEnvironmentVariable("APP_ISOLATED_TEST", "1");
                    var unsafeCli = HousekeepingCliParser.Parse(new[] { "--repair-config" });
                    Assert.Equal(2, HousekeepingCliRunner.RunRepairConfig(unsafeCli));
                }
                finally
                {
                    Environment.SetEnvironmentVariable("APP_ISOLATED_TEST", prevIso);
                }

                // Delimiter-aware TSV (.tsv) and Semicolon CSV (.csv) exports
                var sampleRecords = new List<ManifestRecordItem>
                {
                    new() { LocationId = "NAS1", FileName = "Chrono Trigger.sfc", CleanTitle = "Chrono Trigger", FileSizeBytes = 4194304, IntegrityStatus = "Verified" }
                };

                var tsvCfg = new CollectionManifestConfig { ExportCsv = true, ExportJson = false, ExportHtmlReport = false, CsvDelimiter = "\t" };
                await CollectionManifestModule.ExportManifestsAsync(sampleRecords, exportDir, "snes_manifest", tsvCfg, (_, _) => { }, CancellationToken.None);
                string tsvPath = Path.Combine(exportDir, "snes_manifest.tsv");
                Assert.True(File.Exists(tsvPath));
                Assert.Contains("LocationId\tRootPath\tRelativePath", File.ReadAllText(tsvPath));

                var semiCfg = new CollectionManifestConfig { ExportCsv = true, ExportJson = false, ExportHtmlReport = false, CsvDelimiter = ";" };
                await CollectionManifestModule.ExportManifestsAsync(sampleRecords, exportDir, "snes_semi", semiCfg, (_, _) => { }, CancellationToken.None);
                string semiPath = Path.Combine(exportDir, "snes_semi.csv");
                Assert.True(File.Exists(semiPath));
                Assert.Contains("LocationId;RootPath;RelativePath", File.ReadAllText(semiPath));
            }
            finally
            {
                TestCleanupHelper.TryDeleteDirectory(tempRoot);
            }
        }
    }
}
