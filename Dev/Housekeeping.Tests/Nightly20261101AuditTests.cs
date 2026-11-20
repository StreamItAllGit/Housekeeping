using System;
using System.Collections.Generic;
using System.IO;
using Housekeeping.Models;
using Housekeeping.Services;
using Housekeeping.Services.Modules;
using Xunit;

namespace Housekeeping.Tests
{
    public class Nightly20261101AuditTests
    {
        [Fact]
        public void BuildRcloneCommand_AppendsDefaultMaxLockForBisync_WithoutDuplicatingCustomMaxLock()
        {
            var job = new CloudSyncJobItem
            {
                JobName = "Emu Sync",
                LocalPath = Path.Combine(Path.GetTempPath(), "hk_sets", "EmuDrive"),
                RemoteTarget = "EmuDrive:",
                SyncMode = "bisync",
                Flags = "--create-empty-src-dirs --resilient --verbose"
            };

            string cmdDefault = CloudSyncModule.BuildRcloneCommand(@"C:\Tools\rclone.exe", job, forceResync: false);
            Assert.Contains("--max-lock 2h", cmdDefault);
            Assert.DoesNotContain("--resync", cmdDefault);

            string cmdResync = CloudSyncModule.BuildRcloneCommand(@"C:\Tools\rclone.exe", job, forceResync: true);
            Assert.Contains("--max-lock 2h", cmdResync);
            Assert.Contains("--resync", cmdResync);

            job.Flags = "--resilient --max-lock 45m --verbose";
            string cmdCustom = CloudSyncModule.BuildRcloneCommand(@"C:\Tools\rclone.exe", job, forceResync: false);
            Assert.Contains("--max-lock 45m", cmdCustom);
            Assert.DoesNotContain("--max-lock 2h", cmdCustom);

            job.SyncMode = "sync";
            job.Flags = "--verbose";
            string cmdSync = CloudSyncModule.BuildRcloneCommand(@"C:\Tools\rclone.exe", job, forceResync: true);
            Assert.DoesNotContain("--max-lock", cmdSync);
            Assert.DoesNotContain("--resync", cmdSync);
        }

        [Fact]
        public void TryRecoverOrphanedBisyncLock_RemovesLockOnlyWhenNoActiveRcloneProcessExists()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "hk_bisync_test_" + Guid.NewGuid().ToString("N"), "rclone", "bisync");
            Directory.CreateDirectory(tempRoot);
            string lockFile = Path.Combine(tempRoot, "E__Sets_EmuDrive..EmuDrive_.lck");

            try
            {
                File.WriteAllText(lockFile, "stale-lock");
                var failedResult = new ProcessRunResult
                {
                    ExitCode = 1,
                    StdErrLines = new List<string>
                    {
                        $"2026/11/01 02:01:13 NOTICE: Failed to bisync: prior lock file found: {lockFile}"
                    }
                };

                var logs = new List<(string Level, string Msg)>();

                // When an active rclone process is reported, lock file MUST NOT be deleted
                bool recoveredWhileActive = CloudSyncModule.TryRecoverOrphanedBisyncLock(
                    failedResult,
                    hasActiveRcloneProcess: () => true,
                    log: (lvl, msg) => logs.Add((lvl, msg)),
                    out string activePath);

                Assert.False(recoveredWhileActive);
                Assert.True(string.IsNullOrEmpty(activePath));
                Assert.True(File.Exists(lockFile));

                // When zero rclone processes are active, orphaned lock file MUST be deleted and return true
                bool recoveredWhenIdle = CloudSyncModule.TryRecoverOrphanedBisyncLock(
                    failedResult,
                    hasActiveRcloneProcess: () => false,
                    log: (lvl, msg) => logs.Add((lvl, msg)),
                    out string cleanedPath);

                Assert.True(recoveredWhenIdle);
                Assert.Equal(lockFile, cleanedPath);
                Assert.False(File.Exists(lockFile));
            }
            finally
            {
                string parentRoot = Path.GetFullPath(Path.Combine(tempRoot, "..", ".."));
                if (Directory.Exists(parentRoot))
                {
                    Directory.Delete(parentRoot, recursive: true);
                }
            }
        }

        [Fact]
        public void TryExtractStaleBisyncLockPath_RejectsNonBisyncOrNonLckPaths()
        {
            var nonBisyncResult = new ProcessRunResult
            {
                ExitCode = 1,
                StdErrLines = new List<string>
                {
                    @"NOTICE: Failed to bisync: prior lock file found: C:\Temp\other\important.lck",
                    @"NOTICE: Failed to bisync: prior lock file found: C:\Temp\rclone\bisync\not_a_lock.txt"
                }
            };

            Assert.False(CloudSyncModule.TryExtractStaleBisyncLockPath(nonBisyncResult, out string extracted));
            Assert.Equal("", extracted);
        }

        [Fact]
        public void CompareSemVer_OrdersNightlyDatesAndProtectsProductionRcFromDowngrade()
        {
            Assert.True(RemoteNodeDeploymentService.CompareSemVer("1.0.0-nightly.20261031", "1.0.0-nightly.20261101") < 0);
            Assert.True(RemoteNodeDeploymentService.CompareSemVer("1.0.0-nightly.20261101", "1.0.0-nightly.20261102") < 0);
            Assert.Equal(0, RemoteNodeDeploymentService.CompareSemVer("1.0.0-nightly.20261101", "v1.0.0-nightly.20261101"));
            Assert.True(RemoteNodeDeploymentService.CompareSemVer("1.0.0-nightly.20261101", "1.0.0-rc.9") < 0);
            Assert.True(RemoteNodeDeploymentService.CompareSemVer("1.0.0-rc.10", "1.0.0-rc.10-nightly.1") < 0);
            Assert.True(RemoteNodeDeploymentService.CompareSemVer("1.0.0-rc.10-nightly.1", "1.0.0-rc.10-nightly.2") < 0);
            Assert.True(RemoteNodeDeploymentService.CompareSemVer("1.0.0-rc.10-nightly.2", "1.0.0-rc.11") < 0);
            Assert.True(RemoteNodeDeploymentService.CompareSemVer("1.0.0-rc.9", "1.0.0") < 0);
        }

        [Fact]
        public void TryExtractStaleBisyncLockInfo_StripsAnsiEscapeCodesAndExtractsPidAndExpiryYear()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "hk_bisync_ansi_" + Guid.NewGuid().ToString("N"), "rclone", "bisync");
            Directory.CreateDirectory(tempRoot);
            string lockFile = Path.Combine(tempRoot, "E__Sets_EmuDrive..EmuDrive_.lck");

            try
            {
                var failedResult = new ProcessRunResult
                {
                    ExitCode = 1,
                    StdErrLines = new List<string>
                    {
                        $"\x1b[36m2026/11/02 01:02:02 \x1b[0m\x1b[33mNOTICE\x1b[0m: \x1b[31mFailed to bisync: \x1b[31mprior lock file found: \x1b[93m{lockFile}\x1b[0m \x1b[31m(lock file created on 2026/10/18 22:18:10 by PID 6776; lock expires in 1750498h16m8s (2226/10/01 09:18:10))\x1b[0m"
                    }
                };

                bool matched = CloudSyncModule.TryExtractStaleBisyncLockInfo(
                    failedResult,
                    out string extractedPath,
                    out int? lockerPid,
                    out int? expiresYear);

                Assert.True(matched);
                Assert.Equal(lockFile, extractedPath);
                Assert.Equal(6776, lockerPid);
                Assert.Equal(2226, expiresYear);
            }
            finally
            {
                string parentRoot = Path.GetFullPath(Path.Combine(tempRoot, "..", ".."));
                if (Directory.Exists(parentRoot))
                {
                    Directory.Delete(parentRoot, recursive: true);
                }
            }
        }

        [Fact]
        public void PendingSwapService_RetrySwap_PreservesTargetFileWhenWorkingFileIsLocked()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "hk_pending_swap_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string storagePath = Path.Combine(tempDir, "pending_swaps.json");
            string workingPath = Path.Combine(tempDir, "video.partial.mkv");
            string targetPath = Path.Combine(tempDir, "video.mkv");

            try
            {
                File.WriteAllText(workingPath, "new-encoded-bytes");
                File.WriteAllText(targetPath, "original-production-bytes");

                var service = new PendingSwapService(storagePath);
                service.AddOrUpdate(workingPath, targetPath, "Test Video");
                var item = Assert.Single(service.GetAll());

                // Hold an exclusive lock on the working file to simulate an active encoder/scanner lock
                using (var workingLock = new FileStream(workingPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    bool swappedWhileLocked = service.RetrySwap(item);
                    Assert.False(swappedWhileLocked);
                    Assert.True(File.Exists(targetPath), "TargetPath must NOT be deleted when WorkingPath is locked!");
                    Assert.Equal("original-production-bytes", File.ReadAllText(targetPath));
                }

                // Once the lock on WorkingPath is released, RetrySwap succeeds atomically
                bool swappedWhenUnlocked = service.RetrySwap(item);
                Assert.True(swappedWhenUnlocked);
                Assert.True(File.Exists(targetPath));
                Assert.False(File.Exists(workingPath));
                Assert.Equal("new-encoded-bytes", File.ReadAllText(targetPath));
                Assert.Empty(service.GetAll());
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        [Fact]
        public void TerminalLogBufferService_WritesStandard15DiskGrammarAndPrunesStaleRunLogs()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "hk_log_buffer_" + Guid.NewGuid().ToString("N"));
            string logsDir = Path.Combine(tempDir, "logs");
            string runsDir = Path.Combine(logsDir, "runs");
            Directory.CreateDirectory(runsDir);

            string staleRunLog = Path.Combine(runsDir, "run_20250101_000000.log");
            string freshRunLog = Path.Combine(runsDir, "run_20261102_070000.log");
            File.WriteAllText(staleRunLog, "old-run");
            File.SetLastWriteTimeUtc(staleRunLog, DateTime.UtcNow.AddDays(-45));
            File.WriteAllText(freshRunLog, "fresh-run");

            try
            {
                string liveLogPath;
                using (var buffer = new TerminalLogBufferService(logsDir: logsDir, maxCapacity: 200))
                {
                    buffer.Append("WORK", "Testing Standard 15 grammar");
                    var lines = buffer.GetPrecedingLogLines(10);
                    Assert.Contains(lines, l => l.Contains("[WORK] Testing Standard 15 grammar"));
                    liveLogPath = buffer.LiveLogFilePath;
                }

                Assert.False(File.Exists(staleRunLog), "Run log older than 30 days should be pruned on startup.");
                Assert.True(File.Exists(freshRunLog), "Recent run log must be preserved.");

                string liveLogText = File.ReadAllText(liveLogPath);
                Assert.Contains("[PID:", liveLogText);
                Assert.Contains("[INFO] [WORK] Testing Standard 15 grammar", liveLogText);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }
    }
}
