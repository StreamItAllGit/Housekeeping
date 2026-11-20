// @module: Housekeeping.Tests.V020SecurityAndContainmentTests
// @purpose: Covers v0.2.0 security, crash filtering, SMB push locking, 3-caller rclone containment, MediaInfo truthfulness, --doctor, and SandboxEnvironmentSeam ExitCode=3.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Net.Http, System.Net.Sockets, System.Text.Json, System.Threading.Tasks, Foundation.Core, Housekeeping.Interop, Housekeeping.Models, Housekeeping.Server.Hosting, Housekeeping.Server.Security, Housekeeping.Services, Housekeeping.Services.Modules, Xunit
// @exports: Housekeeping.Tests.V020SecurityAndContainmentTests

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using Foundation.Core;
using Housekeeping.Interop;
using Housekeeping.Models;
using Housekeeping.Server.Hosting;
using Housekeeping.Server.Security;
using Housekeeping.Services;
using Housekeeping.Services.Modules;
using Xunit;

namespace Housekeeping.Tests
{
    public sealed class V020SecurityAndContainmentTests
    {
        [Fact]
        public void TieredAuthService_MigratesCookieSigningKeyFromLogsToData_AndDeletesLegacyLogsCopy()
        {
            string root = Path.Combine(Path.GetTempPath(), "hk_key_mig_" + Guid.NewGuid().ToString("N"));
            string dataDir = Path.Combine(root, "data");
            string logsDir = Path.Combine(root, "logs");
            string cfgPath = Path.Combine(dataDir, "settings.json");
            Directory.CreateDirectory(dataDir);
            Directory.CreateDirectory(logsDir);
            try
            {
                const string legacyHex = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
                string legacyKeyPath = Path.Combine(logsDir, ".hk_cookie_secret.key");
                string dataKeyPath = Path.Combine(dataDir, ".hk_cookie_secret.key");
                File.WriteAllText(legacyKeyPath, legacyHex);

                var cfg = new ConfigService(dataDir, cfgPath, logsDir, isolatedOverride: true);
                _ = new TieredAuthService(cfg);

                Assert.True(File.Exists(dataKeyPath));
                Assert.False(File.Exists(legacyKeyPath));
                Assert.Equal(legacyHex, File.ReadAllText(dataKeyPath).Trim(), StringComparer.OrdinalIgnoreCase);
            }
            finally
            {
                TestCleanupHelper.TryDeleteDirectory(root);
            }
        }

        [Fact]
        public void CrashBundleExceptionFilter_DowngradesClientAbort10054_WithZeroCrashFiles()
        {
            var sock10054 = new SocketException((int)SocketError.ConnectionReset);
            var wrapped = new IOException("Unable to read data from the transport connection", sock10054);
            var agg = new AggregateException(wrapped, new OperationCanceledException());
            var hre = new HttpRequestException("The response ended prematurely.", sock10054);

            Assert.True(CrashBundleExceptionFilter.IsBenignBackgroundException(sock10054));
            Assert.True(CrashBundleExceptionFilter.IsBenignBackgroundException(wrapped));
            Assert.True(CrashBundleExceptionFilter.IsBenignBackgroundException(agg));
            Assert.True(CrashBundleExceptionFilter.IsBenignBackgroundException(hre));
            Assert.Equal("0.2.0", CrashBundleExceptionFilter.DefaultFallbackVersion);

            string logsDir = Path.Combine(Path.GetTempPath(), "hk_crash_sup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(logsDir);
            try
            {
                var eventArgs = new UnobservedTaskExceptionEventArgs(agg);
                bool wroteBundle = Housekeeping.Services.CrashBundleService.HandleUnobservedTaskException(eventArgs, logsDir);
                Assert.False(wroteBundle);
                Assert.True(eventArgs.Observed);
                Assert.Empty(Directory.GetFiles(logsDir, "*crash*.json"));
            }
            finally
            {
                TestCleanupHelper.TryDeleteDirectory(logsDir);
            }
        }

        [Fact]
        public async Task RemoteNodeDeployment_SingleFlightPushNeverLeavesTmpFiles_AndExcludesAgentsMd()
        {
            string root = Path.Combine(Path.GetTempPath(), "hk_smb_push_" + Guid.NewGuid().ToString("N"));
            string localDir = Path.Combine(root, "local");
            string remoteDir = Path.Combine(root, "remote");
            Directory.CreateDirectory(localDir);
            Directory.CreateDirectory(Path.Combine(remoteDir, "app"));
            Directory.CreateDirectory(Path.Combine(remoteDir, "data"));
            try
            {
                File.WriteAllText(Path.Combine(localDir, "AGENTS.md"), "# Should never deploy");
                File.WriteAllText(Path.Combine(localDir, "Housekeeping.Core.dll"), "stub-dll");
                File.WriteAllText(Path.Combine(remoteDir, "data", "settings.json"), "{\"ConfigVersion\":4}");

                var svc = new RemoteNodeDeploymentService();
                var node = new RemoteNodeConfig { Name = "Node-1", RemoteAppPath = remoteDir };

                bool deployed = await svc.UpdateRemoteAppBinariesAsync(node, (_, _) => { }, default, localDir);
                Assert.True(deployed);
                Assert.False(File.Exists(Path.Combine(remoteDir, "AGENTS.md")));
                Assert.False(File.Exists(Path.Combine(remoteDir, "app", "AGENTS.md")));
                Assert.True(File.Exists(Path.Combine(remoteDir, "app", "Housekeeping.Core.dll")));

                var localCfg = new AppConfig();
                var pushTasks = Enumerable.Range(0, 8)
                    .Select(i =>
                    {
                        var clone = localCfg.DeepClone();
                        clone.WebServer.Port = 5178 + i;
                        return svc.PushSettingsToRemoteAsync(node, clone, (_, _) => { });
                    })
                    .ToArray();

                bool[] outcomes = await Task.WhenAll(pushTasks);
                Assert.All(outcomes, ok => Assert.True(ok));
                Assert.Empty(Directory.GetFiles(Path.Combine(remoteDir, "data"), "*.tmp*"));
                using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(remoteDir, "data", "settings.json")));
                Assert.True(doc.RootElement.TryGetProperty("ConfigVersion", out _));
            }
            finally
            {
                TestCleanupHelper.TryDeleteDirectory(root);
            }
        }

        [Fact]
        public async Task RcloneContainment_AllThreeCallersIsolateUnderDataDirectory()
        {
            string root = Path.Combine(Path.GetTempPath(), "hk_rclone_iso_" + Guid.NewGuid().ToString("N"));
            string dataDir = Path.Combine(root, "data");
            Directory.CreateDirectory(dataDir);
            try
            {
                RcloneContainmentPaths paths = RcloneContainmentHelper.ResolveContainment(dataDir, forceContainment: true);
                Assert.True(paths.IsContained);
                Assert.StartsWith(Path.Combine(dataDir, "rclone"), paths.ConfigFilePath, StringComparison.OrdinalIgnoreCase);
                Assert.StartsWith(Path.Combine(dataDir, "rclone"), paths.BisyncWorkDir, StringComparison.OrdinalIgnoreCase);
                Assert.StartsWith(Path.Combine(dataDir, "rclone"), paths.CacheDir, StringComparison.OrdinalIgnoreCase);

                var job = new CloudSyncJobItem { Enabled = true, JobName = "IsoJob", LocalPath = Path.Combine(root, "src"), RemoteTarget = "iso:", SyncMode = "bisync" };
                string cloudCmd = CloudSyncModule.BuildRcloneCommand("rclone.exe", job, forceResync: false, dataDirectory: dataDir, forceContainment: true);
                Assert.Contains($"--config \"{paths.ConfigFilePath}\"", cloudCmd, StringComparison.OrdinalIgnoreCase);
                Assert.Contains($"--workdir \"{paths.BisyncWorkDir}\"", cloudCmd, StringComparison.OrdinalIgnoreCase);
                Assert.Contains($"--cache-dir \"{paths.CacheDir}\"", cloudCmd, StringComparison.OrdinalIgnoreCase);

                var romCfg = new RomLauncherGeneratorConfig
                {
                    DefaultSyncPaths =
                    [
                        new GameSyncPathItem
                        {
                            Enabled = true,
                            Label = "Saves",
                            LocalPath = Path.Combine(root, "saves"),
                            RemoteTarget = "iso:saves",
                            SyncMode = "bisync"
                        }
                    ]
                };
                var col = new ConsoleCollectionRule { ConsoleName = "SNES", SubFolder = "snes" };
                var (preCmds, postCmds) = romCfg.ResolveAllSyncCommands("rclone.exe", col, dataDir, forceContainment: true);
                Assert.NotEmpty(preCmds);
                Assert.Contains($"--config \"{paths.ConfigFilePath}\"", preCmds[0], StringComparison.OrdinalIgnoreCase);
                Assert.Contains($"--workdir \"{paths.BisyncWorkDir}\"", preCmds[0], StringComparison.OrdinalIgnoreCase);
                Assert.Contains($"--cache-dir \"{paths.CacheDir}\"", postCmds[0], StringComparison.OrdinalIgnoreCase);

                IReadOnlyDictionary<string, string> envVars = RcloneContainmentHelper.BuildEnvironmentVariables(paths);
                Assert.Equal(paths.ConfigFilePath, envVars["RCLONE_CONFIG"]);
                Assert.Equal(paths.CacheDir, envVars["RCLONE_CACHE_DIR"]);

                var depSvc = new DependencyCheckService(() => new AppConfig(), dataDir, isolatedOverride: true);
                List<string> remotes = await depSvc.GetAvailableRcloneRemotesAsync();
                Assert.NotNull(remotes);
            }
            finally
            {
                TestCleanupHelper.TryDeleteDirectory(root);
            }
        }

        [Fact]
        public void MediaInfoResolver_AndHevcProbe_ReturnTruthfulFailureWithoutSilentTrueMasking()
        {
            string missingPath = Path.Combine(Path.GetTempPath(), "hk_mi_missing_" + Guid.NewGuid().ToString("N"), "MediaInfo.dll");
            ToolResolutionResult missingRes = MediaInfoNativeResolver.Resolve(Path.GetDirectoryName(missingPath), missingPath);
            Assert.False(missingRes.IsAvailable);

            string sampleFile = Path.Combine(Path.GetTempPath(), "hk_empty_" + Guid.NewGuid().ToString("N") + ".mkv");
            try
            {
                File.WriteAllBytes(sampleFile, []);
                HevcProbeResult probe = MediaTranscoderModule.ProbeFileIsHevc(sampleFile, isRemoteMode: false);
                Assert.False(probe.ShouldKeepExisting);
                Assert.False(probe.ProbeSucceeded);
                Assert.NotEqual(HevcProbeStatus.Hevc, probe.Status);
            }
            finally
            {
                if (File.Exists(sampleFile)) File.Delete(sampleFile);
            }
        }

        [Fact]
        public void DoctorCli_SettingsExampleCopy_AndSandboxContainment_EnforceExitCodes()
        {
            string root = Path.Combine(Path.GetTempPath(), "hk_doc_" + Guid.NewGuid().ToString("N"));
            string dataDir = Path.Combine(root, "data");
            string logsDir = Path.Combine(root, "logs");
            string cfgPath = Path.Combine(dataDir, "settings.json");
            Directory.CreateDirectory(dataDir);
            Directory.CreateDirectory(logsDir);
            try
            {
                string devConfigSrc = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "config"));
                string fakeAppBase = Path.Combine(root, "staged");
                Directory.CreateDirectory(Path.Combine(fakeAppBase, "config"));
                string srcExample = Path.Combine(devConfigSrc, "settings.example.json");
                Assert.True(File.Exists(srcExample), $"Missing tracked template: {srcExample}");
                File.Copy(srcExample, Path.Combine(fakeAppBase, "config", "settings.example.json"), overwrite: true);

                Assert.True(ConfigService.CopySettingsExampleIfMissing(fakeAppBase, dataDir, SandboxResolution.Inactive, fakeAppBase));
                Assert.True(File.Exists(Path.Combine(dataDir, "settings.example.json")));

                HousekeepingParsedCli okCli = HousekeepingCliParser.Parse(new[] { "--doctor", "--data-dir", dataDir, "--config", cfgPath, "--log-dir", logsDir });
                Assert.True(okCli.RunDoctor);
                Assert.Equal(0, HousekeepingCliRunner.RunDoctor(okCli));

                File.WriteAllText(cfgPath, "{ broken-json");
                Assert.Equal(2, HousekeepingCliRunner.RunDoctor(okCli));

                var sandboxEnv = new Dictionary<string, string?>
                {
                    [SandboxEnvironmentSeam.EnvSandboxRoot] = root,
                    [SandboxEnvironmentSeam.EnvIsolatedTest] = null
                };
                SandboxResolution sbx = SandboxEnvironmentSeam.Resolve(sandboxEnv);
                Assert.True(sbx.IsSandboxActive);
                Exception ex = Assert.ThrowsAny<Exception>(() => SandboxEnvironmentSeam.AssertSandboxPathIfActive(@"D:\Programs\Housekeeping\data", sbx));
                Assert.True(SandboxEnvironmentSeam.IsContainmentViolation(ex));
                Assert.Equal(3, SandboxEnvironmentSeam.ContainmentExitCode);
            }
            finally
            {
                TestCleanupHelper.TryDeleteDirectory(root);
            }
        }
    }
}
