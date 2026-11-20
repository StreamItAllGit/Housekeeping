using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Housekeeping.Models;
using Housekeeping.Server.Hosting;
using Housekeeping.Services;
using Xunit;

namespace Housekeeping.Tests
{
    public class Rc11ParityAndMotherTests
    {
        [Fact]
        public void CliParser_ManagedByMother_SetsFlagAndMinimized()
        {
            var cli = HousekeepingCliParser.Parse(new[] { "--managed-by-mother", "--port", "5198" });
            Assert.True(cli.ManagedByMother);
            Assert.True(cli.StartMinimized);
            Assert.Equal(5198, cli.PortOverride);
        }

        [Fact]
        public void Scheduler_DeferSchedules_SetsDeferredUntilUtc_AndShouldTriggerQueueNow_ReturnsFalse()
        {
            var cfg = AppConfig.CreateDefaults();
            var scheduler = new SchedulerAndNotificationService(
                () => cfg,
                (tag, msg) => { },
                (title, msg, warn) => { });

            Assert.Null(scheduler.DeferredUntilUtc);

            // Defer schedules by 30 minutes
            scheduler.DeferSchedules(TimeSpan.FromMinutes(30));
            Assert.NotNull(scheduler.DeferredUntilUtc);
            Assert.True(scheduler.DeferredUntilUtc > DateTime.UtcNow.AddMinutes(25));

            var queue = new NamedJobQueue
            {
                Id = "test-q",
                Name = "Test Queue",
                EnableSchedule = true,
                ScheduleMode = MasterScheduleMode.IntervalCadence,
                ScheduleIntervalMinutes = 5,
                LastRunUtc = DateTime.UtcNow.AddHours(-1)
            };

            // Even though it is due, ShouldTriggerQueueNow must return false due to deferral
            bool due = scheduler.ShouldTriggerQueueNow(queue, DateTime.UtcNow);
            Assert.False(due);

            // Clearing deferral allows it to trigger
            scheduler.ClearDeferral();
            Assert.Null(scheduler.DeferredUntilUtc);

            bool dueAfterClear = scheduler.ShouldTriggerQueueNow(queue, DateTime.UtcNow);
            Assert.True(dueAfterClear);
        }

        [Fact]
        public void MotherManifest_ExistsAndIsValid()
        {
            // Verify mother.manifest.json in Dev root
            string manifestPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "mother.manifest.json");
            if (!File.Exists(manifestPath))
            {
                manifestPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mother.manifest.json");
            }

            if (File.Exists(manifestPath))
            {
                string json = File.ReadAllText(manifestPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                Assert.Equal("housekeeping", root.GetProperty("id").GetString());
                Assert.Equal(5178, root.GetProperty("port").GetInt32());
                Assert.Equal("#f59e0b", root.GetProperty("brand_accent").GetString());
            }
        }

        [Fact]
        public void Installer_IsWithinDirectory_EnforcesFailClosedContainment()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "hk_test_install_" + Guid.NewGuid().ToString("N"));
            string childExe = Path.Combine(baseDir, "app", "Housekeeping.exe");
            string siblingDir = baseDir + "_sibling";
            string outsideExe = Path.Combine(siblingDir, "Housekeeping.exe");

            Assert.True(Housekeeping.Installer.Program.IsWithinDirectory(baseDir, childExe));
            Assert.True(Housekeeping.Installer.Program.IsWithinDirectory(baseDir, Path.Combine(baseDir, "Housekeeping.exe")));
            Assert.False(Housekeeping.Installer.Program.IsWithinDirectory(baseDir, outsideExe));
            Assert.False(Housekeeping.Installer.Program.IsWithinDirectory(baseDir, @"C:\Windows\System32\cmd.exe"));
            Assert.False(Housekeeping.Installer.Program.IsWithinDirectory("", childExe));
            Assert.False(Housekeeping.Installer.Program.IsWithinDirectory(baseDir, ""));
        }

        [Fact]
        public void ConfigService_And_Installer_AdoptCanonicalParentSettings_OverDefaultAppStub()
        {
            string rootDir = Path.Combine(Path.GetTempPath(), "hk_legacy_adopt_" + Guid.NewGuid().ToString("N"));
            string appDir = Path.Combine(rootDir, "app");
            Directory.CreateDirectory(appDir);

            try
            {
                // Create a realistic >35 KB canonical settings.json in rootDir with custom queues and jobs
                var canonicalCfg = AppConfig.CreateDefaults();
                canonicalCfg.NamedQueues = new List<NamedJobQueue>
                {
                    new NamedJobQueue
                    {
                        Id = "nightly-main",
                        Name = "Nightly Media & ROM Maintenance",
                        Description = new string('X', 36000),
                        EnableSchedule = true,
                        Jobs = new List<HousekeepingJob>
                        {
                            new HousekeepingJob
                            {
                                JobId = "job-1",
                                Name = "Universal Bit Rot Scrub",
                                ModuleType = ModuleId.CollectionManifest
                            }
                        }
                    }
                };
                string rootSettingsPath = Path.Combine(rootDir, "settings.json");
                File.WriteAllText(rootSettingsPath, JsonSerializer.Serialize(canonicalCfg, ConfigService.JsonOptions));

                // Create a small <30 KB stub settings.json inside app\
                string appSettingsPath = Path.Combine(appDir, "settings.json");
                File.WriteAllText(appSettingsPath, "{ \"NamedQueues\": [] }");

                // 1. Verify ResolveDefaultDataDirectory resolves to rootDir\data and migrates root settings.json into data\settings.json
                string expectedDataDir = Path.Combine(rootDir, "data");
                string resolvedDir = ConfigService.ResolveDefaultDataDirectory(appDir);
                Assert.Equal(Path.GetFullPath(expectedDataDir), Path.GetFullPath(resolvedDir));
                string dataSettingsPath = Path.Combine(expectedDataDir, "settings.json");
                Assert.True(File.Exists(dataSettingsPath));
                Assert.True(new FileInfo(dataSettingsPath).Length > 35000);
                Assert.False(File.Exists(rootSettingsPath));
                Assert.False(File.Exists(appSettingsPath));

                // 2. Verify ConfigService initialized on appDir with a root settings.json migrates to data\ and resolves sibling logs\ & backups\
                File.WriteAllText(rootSettingsPath, JsonSerializer.Serialize(canonicalCfg, ConfigService.JsonOptions));
                File.WriteAllText(appSettingsPath, "{ \"NamedQueues\": [] }");
                var svc = new ConfigService(customBaseDir: appDir, isolatedOverride: true);
                Assert.Equal(Path.GetFullPath(expectedDataDir), Path.GetFullPath(svc.DataDirectory));
                Assert.Equal(Path.GetFullPath(Path.Combine(rootDir, "logs")), Path.GetFullPath(svc.ActiveLogsDir));
                Assert.Equal(Path.GetFullPath(Path.Combine(rootDir, "backups", "config_snapshots")), Path.GetFullPath(svc.SnapshotsDir));
                Assert.Contains(svc.Config.NamedQueues, q => q.Id == "nightly-main" && q.Name == "Nightly Media & ROM Maintenance");
            }
            finally
            {
                TestCleanupHelper.TryDeleteDirectory(rootDir);
            }
        }

        [Fact]
        public void ConfigService_SchemaV4_MigratesLegacyBrowserLaunchMode_ToStandaloneWindow()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "hk_schema_v4_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                string legacyV3Json = """
                {
                  "ConfigVersion": 3,
                  "WebServer": {
                    "Port": 5178,
                    "DefaultDesktopLaunchMode": "Browser"
                  }
                }
                """;
                File.WriteAllText(Path.Combine(tempDir, "settings.json"), legacyV3Json);

                var svc = new ConfigService(customBaseDir: tempDir, isolatedOverride: true);
                Assert.Equal(4, svc.Config.ConfigVersion);
                Assert.Equal("StandaloneWindow", svc.Config.WebServer.DefaultDesktopLaunchMode);
            }
            finally
            {
                TestCleanupHelper.TryDeleteDirectory(tempDir);
            }
        }

        [Fact]
        public void Installer_CleanupObsoleteInstallationFiles_RemovesLegacyRootClutter_AndMigratesUserStateToData()
        {
            string installDir = Path.Combine(Path.GetTempPath(), "hk_cleanup_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(installDir);

            try
            {
                // Protected user state & folders in legacy root
                File.WriteAllText(Path.Combine(installDir, "settings.json"), "{ \"user\": true }");
                File.WriteAllText(Path.Combine(installDir, "settings.json.bak_20261031"), "{ \"backup\": true }");
                File.WriteAllText(Path.Combine(installDir, "compliance_reports.json"), "[]");
                File.WriteAllText(Path.Combine(installDir, "history.json"), "[]");
                File.WriteAllText(Path.Combine(installDir, "pending_swaps.json"), "[]");
                File.WriteAllText(Path.Combine(installDir, "interrupted_run_checkpoint.json"), "{}");
                Directory.CreateDirectory(Path.Combine(installDir, "backups"));
                File.WriteAllText(Path.Combine(installDir, "backups", "snap.json"), "{}");
                Directory.CreateDirectory(Path.Combine(installDir, "logs"));
                Directory.CreateDirectory(Path.Combine(installDir, "tools"));
                File.WriteAllText(Path.Combine(installDir, "tools", "README_TOOLS.txt"), "tools");
                Directory.CreateDirectory(Path.Combine(installDir, "dats"));
                Directory.CreateDirectory(Path.Combine(installDir, "WebView2Cache"));
                File.WriteAllText(Path.Combine(installDir, "WebView2Cache", "Preferences"), "{}");

                // Whitelisted Clean Release Root files
                File.WriteAllText(Path.Combine(installDir, "Housekeeping.exe"), "stub");
                File.WriteAllText(Path.Combine(installDir, "mother.manifest.json"), "{}");

                // Legacy root clutter that must be cleaned up (including root version.txt & .install_manifest.json)
                File.WriteAllText(Path.Combine(installDir, "version.txt"), "1.0.0-rc.11-hf.1");
                File.WriteAllText(Path.Combine(installDir, ".install_manifest.json"), "{}");
                File.WriteAllText(Path.Combine(installDir, "Housekeeping.Core.dll"), "old-dll");
                File.WriteAllText(Path.Combine(installDir, "MediaInfo.dll"), "old-dll");
                File.WriteAllText(Path.Combine(installDir, "Housekeeping.Server.exe"), "old-exe");
                File.WriteAllText(Path.Combine(installDir, "Housekeeping.exe.old_upgrade"), "old-upg");
                File.WriteAllText(Path.Combine(installDir, "Housekeeping.deps.json"), "{}");
                File.WriteAllText(Path.Combine(installDir, "CHANGELOG.md"), "# Old");
                File.WriteAllText(Path.Combine(installDir, "README.md"), "# Old");
                File.WriteAllText(Path.Combine(installDir, "MODULES_REFERENCE.md"), "# Old");
                Directory.CreateDirectory(Path.Combine(installDir, "wwwroot"));
                File.WriteAllText(Path.Combine(installDir, "wwwroot", "index.html"), "<html/>");
                Directory.CreateDirectory(Path.Combine(installDir, "Assets"));
                Directory.CreateDirectory(Path.Combine(installDir, "runtimes"));

                int cleaned = Housekeeping.Installer.Program.CleanupObsoleteInstallationFiles(installDir, null, null);
                Assert.True(cleaned >= 11);

                // Verify legacy root clutter is gone
                Assert.False(File.Exists(Path.Combine(installDir, "Housekeeping.Core.dll")));
                Assert.False(File.Exists(Path.Combine(installDir, "MediaInfo.dll")));
                Assert.False(File.Exists(Path.Combine(installDir, "Housekeeping.Server.exe")));
                Assert.False(File.Exists(Path.Combine(installDir, "Housekeeping.exe.old_upgrade")));
                Assert.False(File.Exists(Path.Combine(installDir, "CHANGELOG.md")));
                Assert.False(File.Exists(Path.Combine(installDir, "README.md")));
                Assert.False(File.Exists(Path.Combine(installDir, "MODULES_REFERENCE.md")));
                Assert.False(File.Exists(Path.Combine(installDir, "version.txt")));
                Assert.False(File.Exists(Path.Combine(installDir, ".install_manifest.json")));
                Assert.False(Directory.Exists(Path.Combine(installDir, "wwwroot")));
                Assert.False(Directory.Exists(Path.Combine(installDir, "Assets")));
                Assert.False(Directory.Exists(Path.Combine(installDir, "runtimes")));
                Assert.False(Directory.Exists(Path.Combine(installDir, "WebView2Cache")));

                // Verify user state was migrated from root into data\
                Assert.False(File.Exists(Path.Combine(installDir, "settings.json")));
                Assert.True(File.Exists(Path.Combine(installDir, "data", "settings.json")));
                Assert.True(File.Exists(Path.Combine(installDir, "data", "settings.json.bak_20261031")));
                Assert.True(File.Exists(Path.Combine(installDir, "data", "compliance_reports.json")));
                Assert.True(File.Exists(Path.Combine(installDir, "data", "history.json")));
                Assert.True(File.Exists(Path.Combine(installDir, "data", "pending_swaps.json")));
                Assert.True(File.Exists(Path.Combine(installDir, "data", "interrupted_run_checkpoint.json")));
                Assert.True(File.Exists(Path.Combine(installDir, "data", "tools", "README_TOOLS.txt")));
                Assert.True(Directory.Exists(Path.Combine(installDir, "data", "dats")));
                Assert.True(File.Exists(Path.Combine(installDir, "data", "webview2_cache", "Preferences")));

                // Verify Clean Release Root files & sibling folders remain intact
                Assert.True(File.Exists(Path.Combine(installDir, "backups", "snap.json")));
                Assert.True(Directory.Exists(Path.Combine(installDir, "logs")));
                Assert.True(File.Exists(Path.Combine(installDir, "Housekeeping.exe")));
                Assert.True(File.Exists(Path.Combine(installDir, "mother.manifest.json")));
            }
            finally
            {
                TestCleanupHelper.TryDeleteDirectory(installDir);
            }
        }

        [Fact]
        public void SuiteManifestV2_ExistsAndIsValid_StrictCamelCaseAndStringCommands()
        {
            string? manifestPath = new[]
            {
                Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "suite.manifest.json")),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "suite.manifest.json")
            }.FirstOrDefault(File.Exists);

            Assert.False(string.IsNullOrWhiteSpace(manifestPath), "Expected Dev\\suite.manifest.json to exist.");
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath!));
            JsonElement root = doc.RootElement;

            Assert.Equal(2, root.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("housekeeping", root.GetProperty("appId").GetString());
            Assert.Equal("0.2.0", root.GetProperty("version").GetString());
            Assert.Equal("0.5.3", root.GetProperty("sdkVersion").GetString());
            Assert.Equal("0.3.0", root.GetProperty("minSuiteVersion").GetString());
            Assert.Equal(JsonValueKind.Array, root.GetProperty("workerArgs").ValueKind);

            JsonElement commands = root.GetProperty("commands");
            foreach (string key in new[] { "startDaemon", "stopDaemon", "statusCheck", "backupNow" })
                Assert.Equal(JsonValueKind.String, commands.GetProperty(key).ValueKind);

            Assert.Equal("/api/v1/health", root.GetProperty("endpoints").GetProperty("health").GetString());
            Assert.False(root.TryGetProperty("archetype", out _));
            AssertNoSnakeCaseKeys(root);
        }

        [Fact]
        public async Task LegacyInstallCleanup_CopyNonDestructive_AndDescriptor_AlignWithMotherSuite()
        {
            var descriptor = Housekeeping.Installer.HousekeepingInstallerRunner.ResolveEffectiveDescriptor("0.2.0");
            Assert.Equal("Housekeeping", descriptor.ProductName);
            Assert.Equal("StreamItAll", descriptor.StudioName);
            Assert.Equal("mother", descriptor.SuiteName, ignoreCase: true);
            Assert.Equal(Path.Combine("StreamItAll", "Mother", "Housekeeping"), descriptor.UmbrellaSubpath);
            Assert.True(descriptor.AllowLegacyMigration);

            string legacyDir = Path.Combine(Path.GetTempPath(), "hk_legacy_src_" + Guid.NewGuid().ToString("N"));
            string targetDir = Path.Combine(Path.GetTempPath(), "hk_suite_dst_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(legacyDir);
                Directory.CreateDirectory(Path.Combine(targetDir, "data"));
                File.WriteAllText(Path.Combine(legacyDir, "settings.json"), "{\"ConfigVersion\":4,\"Marker\":\"legacy\"}");
                File.WriteAllText(Path.Combine(legacyDir, "history.json"), "[{\"Id\":\"h1\"}]");
                File.WriteAllText(Path.Combine(targetDir, "data", "history.json"), "[{\"Id\":\"existing-target\"}]");

                int copied = await Housekeeping.Installer.LegacyInstallCleanup.CopyLegacyStateNonDestructivelyAsync(legacyDir, targetDir, default);
                Assert.True(copied >= 1);
                Assert.True(File.Exists(Path.Combine(targetDir, "data", "settings.json")));
                Assert.Contains("existing-target", File.ReadAllText(Path.Combine(targetDir, "data", "history.json")));
            }
            finally
            {
                TestCleanupHelper.TryDeleteDirectory(legacyDir);
                TestCleanupHelper.TryDeleteDirectory(targetDir);
            }
        }

        private static void AssertNoSnakeCaseKeys(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty prop in element.EnumerateObject())
                {
                    Assert.DoesNotContain('_', prop.Name);
                    AssertNoSnakeCaseKeys(prop.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                    AssertNoSnakeCaseKeys(item);
            }
        }
    }
}
