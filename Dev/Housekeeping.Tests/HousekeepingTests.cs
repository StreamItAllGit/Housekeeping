using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Housekeeping.Models;
using Housekeeping.Services;
using Housekeeping.Services.Modules;
using Xunit;

namespace Housekeeping.Tests
{
    public class HousekeepingTests
    {
        [Fact]
        public void DefaultAppConfig_StartsCleanAndPortable_WithZeroPersonalPathsOrSecrets()
        {
            var cfg = AppConfig.CreateDefaults();

            Assert.False(cfg.ProxyGenerator.Enabled);
            Assert.Empty(cfg.ProxyGenerator.Folders);

            Assert.False(cfg.ArchiveCompressor.Enabled);
            Assert.Empty(cfg.ArchiveCompressor.Mappings);

            Assert.False(cfg.MediaTranscoder.Enabled);
            Assert.Empty(cfg.MediaTranscoder.DrivesToScan);
            Assert.Empty(cfg.MediaTranscoder.CustomFoldersToScan);

            Assert.False(cfg.RomLauncherGenerator.Enabled);
            Assert.Empty(cfg.RomLauncherGenerator.Collections);
            Assert.Equal("", cfg.RomLauncherGenerator.LaunchersOutputRootPath);

            Assert.False(cfg.CloudSync.Enabled);
            Assert.Empty(cfg.CloudSync.Jobs);

            Assert.False(cfg.HumbleSync.Enabled);
            Assert.Equal("", cfg.HumbleSync.SessionCookie);
            Assert.Equal("", cfg.HumbleSync.LibraryPath);

            Assert.False(cfg.CustomScripts.Enabled);
            Assert.Empty(cfg.CustomScripts.Tasks);
        }

        [Fact]
        public void SanitizedCopyForSharing_StripsSensitiveTokens_WhileKeepingConfiguration()
        {
            var cfg = AppConfig.CreateDefaults();
            cfg.HumbleSync.SessionCookie = "secret_cookie_123";
            cfg.DiscordWebhookUrl = "https://discord.example.invalid/api/webhooks/000000000000000000/placeholder-token";
            cfg.ProxyGenerator.Folders.Add(new FolderTargetItem { Enabled = true, Path = @"C:\Videos\Edit" });

            var sanitized = cfg.CreateSanitizedCopyForSharing();

            Assert.Equal("", sanitized.HumbleSync.SessionCookie);
            Assert.Equal("", sanitized.DiscordWebhookUrl);
            Assert.Single(sanitized.ProxyGenerator.Folders);
            Assert.Equal(@"C:\Videos\Edit", sanitized.ProxyGenerator.Folders[0].Path);
        }

        [Fact]
        public void HandBrakeEncodingProfile_BuildsStandaloneCliFlags_AndSupportsOverrides()
        {
            var profile = new HandBrakeEncodingProfile
            {
                ProfileId = "Test_1080p",
                Name = "Test 1080p HEVC",
                ContainerExtension = ".mkv",
                VideoEncoder = "x265",
                EncoderPreset = "medium",
                QualityRf = 20,
                MaxWidth = 1920,
                MaxHeight = 1080,
                AudioEncoder = "av_aac",
                AudioBitrateKbps = 192,
                SubtitleMode = "burned"
            };

            string args = profile.BuildHandBrakeArguments(@"C:\in.mkv", @"C:\out.partial.mkv", overrideAudioLang: "JPN");

            Assert.Contains("-i \"C:\\in.mkv\"", args);
            Assert.Contains("-o \"C:\\out.partial.mkv\"", args);
            Assert.Contains("-e x265", args);
            Assert.Contains("--encoder-preset medium", args);
            Assert.Contains("-q 20", args);
            Assert.Contains("-X 1920 -Y 1080 --loose-anamorphic", args);
            Assert.Contains("--audio-lang-list jpn --first-audio", args);
            Assert.Contains("-E av_aac", args);
            Assert.Contains("-B 192", args);
            Assert.Contains("--subtitle 1 --subtitle-burned", args);

            // GUI preset override test
            profile.UseGuiPresetOverride = true;
            profile.GuiPresetName = "MyCustomGuiPreset";
            string guiArgs = profile.BuildHandBrakeArguments(@"C:\in.mkv", @"C:\out.mkv");
            Assert.Contains("--preset-import-gui -Z \"MyCustomGuiPreset\"", guiArgs);
        }

        [Fact]
        public void GenerateM3uPlaylistsInDirectory_CreatesM3uInDiscOrder_PreferringCueOverBin()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "HKTest_M3u_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                // Create multi-disc .cue + .bin files out of order
                File.WriteAllText(Path.Combine(tempDir, "Chrono Cross (Disc 2).cue"), "FILE \"Chrono Cross (Disc 2).bin\" BINARY");
                File.WriteAllText(Path.Combine(tempDir, "Chrono Cross (Disc 2).bin"), "dummy");
                File.WriteAllText(Path.Combine(tempDir, "Chrono Cross (Disc 1).cue"), "FILE \"Chrono Cross (Disc 1).bin\" BINARY");
                File.WriteAllText(Path.Combine(tempDir, "Chrono Cross (Disc 1).bin"), "dummy");

                int created = RomLauncherGeneratorModule.GenerateM3uPlaylistsInDirectory(null, tempDir);
                Assert.Equal(1, created);

                string m3uPath = Path.Combine(tempDir, "Chrono Cross.m3u");
                Assert.True(File.Exists(m3uPath));

                string[] lines = File.ReadAllLines(m3uPath);
                Assert.Equal(2, lines.Length);
                Assert.Equal("Chrono Cross (Disc 1).cue", lines[0]);
                Assert.Equal("Chrono Cross (Disc 2).cue", lines[1]);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void ResolveSupplementalFilesForBundle_IncludesM3u_AllDiscCues_MultiTrackBins_AndSbiFiles()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "HKTest_Bundle_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                // Multi-disc PS1 game with .m3u, .cue referencing multiple .bin tracks, and .sbi LibCrypt subchannel file
                string d1Cue = Path.Combine(tempDir, "Final Fantasy IX (Disc 1).cue");
                string d1Track1 = Path.Combine(tempDir, "Final Fantasy IX (Disc 1) (Track 01).bin");
                string d1Track2 = Path.Combine(tempDir, "Final Fantasy IX (Disc 1) (Track 02).bin");
                string d1Sbi = Path.Combine(tempDir, "Final Fantasy IX (Disc 1).sbi");

                string d2Cue = Path.Combine(tempDir, "Final Fantasy IX (Disc 2).cue");
                string d2Bin = Path.Combine(tempDir, "Final Fantasy IX (Disc 2).bin");

                File.WriteAllLines(d1Cue, new[]
                {
                    "FILE \"Final Fantasy IX (Disc 1) (Track 01).bin\" BINARY",
                    "  TRACK 01 MODE2/2352",
                    "FILE \"Final Fantasy IX (Disc 1) (Track 02).bin\" BINARY",
                    "  TRACK 02 AUDIO"
                });
                File.WriteAllText(d1Track1, "t1");
                File.WriteAllText(d1Track2, "t2");
                File.WriteAllText(d1Sbi, "sbi");

                File.WriteAllText(d2Cue, "FILE \"Final Fantasy IX (Disc 2).bin\" BINARY");
                File.WriteAllText(d2Bin, "d2");

                // Generate .m3u first
                RomLauncherGeneratorModule.GenerateM3uPlaylistsInDirectory(null, tempDir);
                string m3uPath = Path.Combine(tempDir, "Final Fantasy IX.m3u");
                Assert.True(File.Exists(m3uPath));

                // Resolve bundle starting from the .m3u file
                var bundled = RomLauncherGeneratorModule.ResolveSupplementalFilesForBundle(
                    new DirectoryInfo(tempDir),
                    new FileInfo(m3uPath));

                var bundledNames = bundled.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

                Assert.Contains("Final Fantasy IX.m3u", bundledNames);
                Assert.Contains("Final Fantasy IX (Disc 1).cue", bundledNames);
                Assert.Contains("Final Fantasy IX (Disc 1) (Track 01).bin", bundledNames);
                Assert.Contains("Final Fantasy IX (Disc 1) (Track 02).bin", bundledNames);
                Assert.Contains("Final Fantasy IX (Disc 1).sbi", bundledNames);
                Assert.Contains("Final Fantasy IX (Disc 2).cue", bundledNames);
                Assert.Contains("Final Fantasy IX (Disc 2).bin", bundledNames);
                Assert.Equal(7, bundledNames.Count);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void ResolveSupplementalFilesForBundle_IncludesDreamcastGdiTrackFiles()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "HKTest_Gdi_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                string gdiPath = Path.Combine(tempDir, "Sonic Adventure.gdi");
                string t1 = Path.Combine(tempDir, "track01.bin");
                string t2 = Path.Combine(tempDir, "track02.raw");
                string t3 = Path.Combine(tempDir, "track03.bin");

                File.WriteAllLines(gdiPath, new[]
                {
                    "3",
                    "1 0 4 2352 track01.bin 0",
                    "2 600 0 2352 track02.raw 0",
                    "3 45000 4 2352 track03.bin 0"
                });
                File.WriteAllText(t1, "1");
                File.WriteAllText(t2, "2");
                File.WriteAllText(t3, "3");

                var bundled = RomLauncherGeneratorModule.ResolveSupplementalFilesForBundle(
                    new DirectoryInfo(tempDir),
                    new FileInfo(gdiPath));

                var names = bundled.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                Assert.Contains("Sonic Adventure.gdi", names);
                Assert.Contains("track01.bin", names);
                Assert.Contains("track02.raw", names);
                Assert.Contains("track03.bin", names);
                Assert.Equal(4, names.Count);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
        }

        [Theory]
        [InlineData("RetroArch", "retroarch.exe", @"C:\cores\swanstation.dll", @"C:\temp\game.m3u", "-f -L \"C:\\cores\\swanstation.dll\" \"C:\\temp\\game.m3u\"")]
        [InlineData("PCSX2", "pcsx2-qt.exe", "", @"C:\temp\game.iso", "-fullscreen -batch -- \"C:\\temp\\game.iso\"")]
        [InlineData("DuckStation", "duckstation-qt-x64-ReleaseLTCG.exe", "", @"C:\temp\game.m3u", "-fullscreen -batch -- \"C:\\temp\\game.m3u\"")]
        [InlineData("RPCS3", "rpcs3.exe", "", @"C:\temp\eboot.bin", "--no-gui --fullscreen \"C:\\temp\\eboot.bin\"")]
        [InlineData("Dolphin", "Dolphin.exe", "", @"C:\temp\game.rvz", "-b -e \"C:\\temp\\game.rvz\"")]
        [InlineData("Xemu", "xemu.exe", "", @"C:\temp\game.iso", "-full-screen -dvd_path \"C:\\temp\\game.iso\"")]
        public void BuildEmulatorLaunchCommand_SupportsMajorEmulators(
            string runnerType,
            string exe,
            string coreOrArgs,
            string romPath,
            string expectedSubstring)
        {
            var col = new ConsoleCollectionRule
            {
                EmulatorRunnerType = runnerType,
                EmulatorExecutablePath = exe,
                CoreOrCustomArgs = coreOrArgs
            };

            string cmd = RomLauncherGeneratorModule.BuildEmulatorLaunchCommand(col, romPath);
            Assert.Contains(expectedSubstring, cmd);
        }

        [Fact]
        public void NetworkSafetyService_AtomicPartialWrites_CommitAndDiscardWorkProperly()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "HKTest_Atomic_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var cfg = AppConfig.CreateDefaults();
                var svc = new NetworkSafetyService(() => cfg, (_, _) => { });

                string finalFile = Path.Combine(tempDir, "Movie BR (1080p).mkv");
                string partialFile = svc.GetSafeEncodeOutputPath(finalFile);

                Assert.EndsWith(".partial.mkv", partialFile);

                // Write non-empty content and commit
                File.WriteAllText(partialFile, "valid video stream data");
                bool committed = svc.CommitPartialEncode(partialFile, finalFile);

                Assert.True(committed);
                Assert.True(File.Exists(finalFile));
                Assert.False(File.Exists(partialFile));

                // Write orphaned partial and verify cleanup removes it without touching committed file
                File.WriteAllText(partialFile, "interrupted encode");
                int cleaned = svc.CleanupOrphanedPartials(new[] { tempDir });
                Assert.Equal(1, cleaned);
                Assert.False(File.Exists(partialFile));
                Assert.True(File.Exists(finalFile));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void UiOptionsCatalog_BatchSubfolderDiscovery_And_Pairing_WorksCorrectly()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "HKTest_BatchSub_" + Guid.NewGuid().ToString("N"));
            string srcParent = Path.Combine(tempDir, "SourceProjects");
            string dstParent = Path.Combine(tempDir, "ArchivedProjects");
            Directory.CreateDirectory(Path.Combine(srcParent, "Project_Alpha"));
            Directory.CreateDirectory(Path.Combine(srcParent, "Project_Beta"));
            Directory.CreateDirectory(Path.Combine(srcParent, ".git")); // hidden/system dot-folder should be skipped

            try
            {
                var proxyTargets = UiOptionsCatalog.DiscoverSubfolderTargets(srcParent);
                Assert.Equal(2, proxyTargets.Count);
                Assert.Equal("Project_Alpha", Path.GetFileName(proxyTargets[0].Path));
                Assert.Equal("Project_Beta", Path.GetFileName(proxyTargets[1].Path));

                var archiveMappings = UiOptionsCatalog.DiscoverPairedSubfolderMappings(srcParent, dstParent);
                Assert.Equal(2, archiveMappings.Count);
                Assert.Equal("Project_Alpha", archiveMappings[0].Name);
                Assert.Equal(Path.Combine(srcParent, "Project_Alpha"), archiveMappings[0].SourcePath);
                Assert.Equal(Path.Combine(dstParent, "Project_Alpha"), archiveMappings[0].ArchiveOutputPath);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void UiOptionsCatalog_FormatFreeSpaceShort_FormatsBytesCleanly()
        {
            Assert.Equal("500 MB", UiOptionsCatalog.FormatFreeSpaceShort(500L * 1024 * 1024));
            Assert.Equal("420 GB", UiOptionsCatalog.FormatFreeSpaceShort(420L * 1024 * 1024 * 1024));
            Assert.Equal("1.5 TB", UiOptionsCatalog.FormatFreeSpaceShort((long)(1.5 * 1024L * 1024 * 1024 * 1024)));
        }

        [Fact]
        public void HandBrakeEncodingProfile_ParseHandBrakePresetsJson_ImportsNestedPresetsAndParameters()
        {
            string sampleHandBrakeJson = """
            {
              "PresetList": [
                {
                  "Folder": true,
                  "PresetName": "My Custom Presets",
                  "ChildrenArray": [
                    {
                      "Folder": false,
                      "PresetName": "DVD 480p HEVC Custom",
                      "FileFormat": "av_mkv",
                      "VideoEncoder": "x265_10bit",
                      "VideoPreset": "slow",
                      "VideoQualitySlider": 19.5,
                      "PictureWidth": 720,
                      "PictureHeight": 480
                    },
                    {
                      "Folder": false,
                      "PresetName": "Web Proxy MP4",
                      "FileFormat": "av_mp4",
                      "VideoEncoder": "x264",
                      "VideoPreset": "veryfast",
                      "VideoQualitySlider": 24.0,
                      "PictureWidth": 640,
                      "PictureHeight": 360
                    }
                  ]
                }
              ]
            }
            """;

            var imported = HandBrakeEncodingProfile.ParseHandBrakePresetsJson(sampleHandBrakeJson, "");
            Assert.Equal(2, imported.Count);

            var p1 = imported[0];
            Assert.Equal("DVD 480p HEVC Custom", p1.ProfileId);
            Assert.Equal("DVD 480p HEVC Custom", p1.Name);
            Assert.Equal(".mkv", p1.ContainerExtension);
            Assert.Equal("x265_10bit", p1.VideoEncoder);
            Assert.Equal("slow", p1.EncoderPreset);
            Assert.Equal(19.5, p1.QualityRf);
            Assert.Equal(720, p1.MaxWidth);
            Assert.Equal(480, p1.MaxHeight);
            Assert.True(p1.UseGuiPresetOverride);
            Assert.Equal("DVD 480p HEVC Custom", p1.GuiPresetName);

            var p2 = imported[1];
            Assert.Equal(".mp4", p2.ContainerExtension);
        }

        [Fact]
        public void SourceQualityTierRule_AllowsDifferentHandBrakeProfiles_PerSourceAndResolutionTier()
        {
            var cfg = MediaTranscoderConfig.CreateDefault();
            var dvdTier = new SourceQualityTierRule
            {
                Enabled = true,
                TierName = "DVD Source",
                SourceTagsCsv = "DVD",
                Profile144p = SourceQualityTierRule.DisabledOption,
                Profile240p = SourceQualityTierRule.DisabledOption,
                Profile360p = "HB_DVD_360p",
                Profile480p = "HB_DVD_480p",
                Profile720p = SourceQualityTierRule.DisabledOption,
                Profile1080p = SourceQualityTierRule.DisabledOption,
                Profile4K = SourceQualityTierRule.DisabledOption
            };

            var brTier = new SourceQualityTierRule
            {
                Enabled = true,
                TierName = "Blu-Ray Source",
                SourceTagsCsv = "BR",
                Profile144p = SourceQualityTierRule.DisabledOption,
                Profile240p = SourceQualityTierRule.DisabledOption,
                Profile360p = SourceQualityTierRule.DisabledOption,
                Profile480p = "HB_BR_480p",
                Profile720p = "HB_BR_720p",
                Profile1080p = "HB_BR_1080p",
                Profile4K = SourceQualityTierRule.DisabledOption
            };

            var dvdActive = dvdTier.GetActiveResolutionProfiles(cfg);
            Assert.Equal(2, dvdActive.Count);
            Assert.Equal(("360p", "HB_DVD_360p"), dvdActive[0]);
            Assert.Equal(("480p", "HB_DVD_480p"), dvdActive[1]);

            var brActive = brTier.GetActiveResolutionProfiles(cfg);
            Assert.Equal(3, brActive.Count);
            Assert.Equal(("480p", "HB_BR_480p"), brActive[0]);
            Assert.Equal(("720p", "HB_BR_720p"), brActive[1]);
            Assert.Equal(("1080p", "HB_BR_1080p"), brActive[2]);
        }

        [Fact]
        public void SplitCommandLine_CorrectlySeparatesQuotedExecutableWithSpaces_FromArgumentsWithParenthesesAndQuotes()
        {
            string raw = "\"C:\\Program Files\\HandBrake\\HandBrakeCLI.exe\" --preset-import-gui -Z \"240p - DVD\" -i \"Z:\\Media\\TV Shows\\Danny Phantom (2004)\\Danny Phantom (2004) - S02E11 - DVD (Original) {edition-DVD}.mkv\" -o \"Z:\\Media\\TV Shows\\Danny Phantom (2004)\\Danny Phantom (2004) - S02E11 - DVD (240p) {edition-DVD}.partial.mkv\"";

            var (exePath, exeArgs) = ProcessRunnerService.SplitCommandLine(raw);

            Assert.Equal(@"C:\Program Files\HandBrake\HandBrakeCLI.exe", exePath);
            Assert.StartsWith("--preset-import-gui -Z \"240p - DVD\"", exeArgs);
            Assert.Contains("Danny Phantom (2004)", exeArgs);
        }

        [Fact]
        public void MigrateLegacyModulesToJobsIfNeeded_PreservesConfiguredModulesAndAllowsIndependentJobCloning()
        {
            var cfg = new AppConfig();
            cfg.MediaTranscoder.Enabled = true;
            cfg.MediaTranscoder.DrivesToScan = new List<string> { "V", "W", "Z" };

            cfg.MigrateLegacyModulesToJobsIfNeeded();

            Assert.Single(cfg.Jobs);
            var job = cfg.Jobs[0];
            Assert.Equal(ModuleId.MediaTranscoder, job.ModuleType);
            Assert.Equal(new[] { "V", "W", "Z" }, job.MediaTranscoder!.DrivesToScan);

            var clone = job.CloneAsDuplicate();
            Assert.NotEqual(job.JobId, clone.JobId);
            Assert.Contains("(Copy)", clone.Name);
            clone.MediaTranscoder!.DrivesToScan = new List<string> { "X" };

            // Mutating clone does not affect original job
            Assert.Equal(3, job.MediaTranscoder.DrivesToScan.Count);
            Assert.Single(clone.MediaTranscoder.DrivesToScan);
        }

        [Fact]
        public void RomLauncherGeneratorConfig_ResolvePreAndPostSyncCommands_BuildsGuidedRcloneCommands()
        {
            var romCfg = new RomLauncherGeneratorConfig
            {
                EnablePrePostLaunchSyncCommands = true,
                SaveSyncLocalFolder = @"D:\RetroArch\saves",
                SaveSyncCloudRemote = "gdrive:Backups/RetroArchSaves",
                SaveSyncMode = "sync"
            };

            var (preSync, postSync) = romCfg.ResolvePreAndPostSyncCommands(@"C:\Tools\rclone.exe");

            // In "sync" mode, pre-launch pulls Cloud -> Local, post-launch pushes Local -> Cloud
            Assert.Contains("\"C:\\Tools\\rclone.exe\" sync \"gdrive:Backups/RetroArchSaves\" \"D:\\RetroArch\\saves\"", preSync);
            Assert.Contains("\"C:\\Tools\\rclone.exe\" sync \"D:\\RetroArch\\saves\" \"gdrive:Backups/RetroArchSaves\"", postSync);

            romCfg.SaveSyncMode = "bisync";
            romCfg.PreLaunchCommand = "";
            romCfg.PostLaunchCommand = "";
            var (preBi, postBi) = romCfg.ResolvePreAndPostSyncCommands("rclone");
            Assert.Contains("bisync \"D:\\RetroArch\\saves\" \"gdrive:Backups/RetroArchSaves\"", preBi);
            Assert.Contains("--resilient", preBi);
            Assert.Equal(preBi, postBi);
        }

        [Fact]
        public void SchemaMigration_UpgradesV1ToV2_AndPopulatesDefaults()
        {
            var cfg = new AppConfig
            {
                ConfigVersion = 1,
                DistributedLockTimeoutMinutes = 0
            };

            ConfigService.MigrateConfig(cfg, fromVersion: 1, toVersion: 2);

            Assert.True(cfg.EnableDistributedNetworkLocking);
            Assert.Equal(90, cfg.DistributedLockTimeoutMinutes);
            Assert.NotEmpty(cfg.HandBrakeProfiles);
        }

        [Fact]
        public void AppWideModule_TogglesAndReadsCorrectState()
        {
            var cfg = new AppConfig();
            Assert.False(cfg.IsModuleAppWideEnabled(ModuleId.ProxyGenerator));

            cfg.SetModuleAppWideEnabled(ModuleId.ProxyGenerator, true);
            Assert.True(cfg.IsModuleAppWideEnabled(ModuleId.ProxyGenerator));
            Assert.True(cfg.ProxyGenerator.Enabled);

            cfg.SetModuleAppWideEnabled(ModuleId.ProxyGenerator, false);
            Assert.False(cfg.IsModuleAppWideEnabled(ModuleId.ProxyGenerator));
            Assert.False(cfg.ProxyGenerator.Enabled);
        }

        [Fact]
        public void DistributedFileLock_AcquiresReleasesAndDetectsConflicts()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "HKTest_Lock_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var cfg = new AppConfig { EnableDistributedNetworkLocking = true, DistributedLockTimeoutMinutes = 60 };
                var safety = new NetworkSafetyService(() => cfg, (tag, msg) => { });

                string targetFile = Path.Combine(tempDir, "Movie (1080p).mp4");
                string expectedLockFile = targetFile + ".hklock";

                // Acquire lock 1
                bool acquired1 = safety.TryAcquireDistributedLock(targetFile, out var lockHandle1, out var conflictMsg1);
                Assert.True(acquired1);
                Assert.NotNull(lockHandle1);
                Assert.Null(conflictMsg1);
                Assert.True(File.Exists(expectedLockFile));

                // Second attempt to acquire lock while active should fail
                bool acquired2 = safety.TryAcquireDistributedLock(targetFile, out var lockHandle2, out var conflictMsg2);
                Assert.False(acquired2);
                Assert.Null(lockHandle2);
                Assert.NotNull(conflictMsg2);

                // Releasing lock 1 should delete .hklock
                lockHandle1.Dispose();
                Assert.False(File.Exists(expectedLockFile));

                // Now acquiring should succeed again
                bool acquired3 = safety.TryAcquireDistributedLock(targetFile, out var lockHandle3, out _);
                Assert.True(acquired3);
                Assert.NotNull(lockHandle3);
                lockHandle3.Dispose();
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void CommitPartialEncode_AtomicallyMovesFile_WithOverwriting()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "HKTest_Commit_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var cfg = new AppConfig();
                var safety = new NetworkSafetyService(() => cfg, (tag, msg) => { });

                string partial = Path.Combine(tempDir, "video.partial.mp4");
                string final = Path.Combine(tempDir, "video.mp4");

                File.WriteAllText(partial, "encoded-video-content-12345");

                bool success = safety.CommitPartialEncode(partial, final);
                Assert.True(success);
                Assert.False(File.Exists(partial));
                Assert.True(File.Exists(final));
                Assert.Equal("encoded-video-content-12345", File.ReadAllText(final));

                // Empty partial file should be discarded
                File.WriteAllBytes(partial, Array.Empty<byte>());
                bool emptySuccess = safety.CommitPartialEncode(partial, final);
                Assert.False(emptySuccess);
                Assert.False(File.Exists(partial)); // should be cleaned up
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ModuleTypeDisplayNames_MatchCleanNamingStandards()
        {
            Assert.Equal("🎬 Proxy Gen", HousekeepingJob.GetModuleTypeDisplayName(ModuleId.ProxyGenerator));
            Assert.Equal("📦 Video Archiver", HousekeepingJob.GetModuleTypeDisplayName(ModuleId.ArchiveCompressor));
            Assert.Equal("📺 Media Transcoder", HousekeepingJob.GetModuleTypeDisplayName(ModuleId.MediaTranscoder));
            Assert.Equal("🎮 Game Launchers", HousekeepingJob.GetModuleTypeDisplayName(ModuleId.RomLauncherGenerator));
            Assert.Equal("☁ Cloud Sync", HousekeepingJob.GetModuleTypeDisplayName(ModuleId.CloudSync));
            Assert.Equal("📚 Humble Library", HousekeepingJob.GetModuleTypeDisplayName(ModuleId.HumbleSync));
            Assert.Equal("⚡ Custom Tasks", HousekeepingJob.GetModuleTypeDisplayName(ModuleId.CustomScripts));

            var job = new HousekeepingJob { ModuleType = ModuleId.RomLauncherGenerator };
            Assert.Equal("🎮 Game Launchers", job.ModuleTypeBadgeText);
        }

        [Fact]
        public void MigrateLegacyModulesToJobsIfNeeded_UsesStandardCleanNames()
        {
            var cfg = new AppConfig();
            cfg.MediaTranscoder.Enabled = true;
            cfg.ProxyGenerator.Enabled = true;
            cfg.ArchiveCompressor.Enabled = true;
            cfg.RomLauncherGenerator.Enabled = true;
            cfg.CloudSync.Enabled = true;
            cfg.HumbleSync.Enabled = true;
            cfg.CustomScripts.Enabled = true;

            cfg.MigrateLegacyModulesToJobsIfNeeded();

            Assert.Equal(7, cfg.Jobs.Count);
            Assert.Equal("Media Transcoder", cfg.Jobs[0].Name);
            Assert.Equal("Proxy Gen", cfg.Jobs[1].Name);
            Assert.Equal("Video Archiver", cfg.Jobs[2].Name);
            Assert.Equal("Game Launchers", cfg.Jobs[3].Name);
            Assert.Equal("Cloud Sync", cfg.Jobs[4].Name);
            Assert.Equal("Humble Library", cfg.Jobs[5].Name);
            Assert.Equal("Custom Tasks", cfg.Jobs[6].Name);
        }

        [Fact]
        public void NetworkSafetyService_PurgeZeroByteFileIfExists_DeletesZeroByteFiles_AndKeepsValidFiles()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "HKTest_ZeroByte_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                string emptyFile = Path.Combine(tempDir, "empty.mkv");
                File.WriteAllBytes(emptyFile, Array.Empty<byte>());

                string validFile = Path.Combine(tempDir, "valid.mkv");
                File.WriteAllText(validFile, "video-bytes");

                Assert.True(File.Exists(emptyFile));
                bool purged = NetworkSafetyService.PurgeZeroByteFileIfExists(emptyFile);
                Assert.True(purged);
                Assert.False(File.Exists(emptyFile));

                bool notPurged = NetworkSafetyService.PurgeZeroByteFileIfExists(validFile);
                Assert.False(notPurged);
                Assert.True(File.Exists(validFile));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void RomLauncherGenerator_ResolveAllSyncCommands_GeneratesMultiPathCommands_WithConsoleOverrides()
        {
            var cfg = new RomLauncherGeneratorConfig();
            cfg.DefaultSyncPaths.Add(new GameSyncPathItem
            {
                Enabled = true,
                Label = "Battery Saves",
                LocalPath = @"C:\Emulators\RetroArch\saves",
                RemoteTarget = "EmuDrive:Saves",
                SyncMode = "bisync"
            });
            cfg.DefaultSyncPaths.Add(new GameSyncPathItem
            {
                Enabled = true,
                Label = "Save States",
                LocalPath = @"C:\Emulators\RetroArch\states",
                RemoteTarget = "EmuDrive:States",
                SyncMode = "bisync"
            });

            var (defaultPre, defaultPost) = cfg.ResolveAllSyncCommands("rclone.exe", null);
            Assert.Equal(2, defaultPre.Count);
            Assert.Equal(2, defaultPost.Count);
            Assert.Contains("rclone.exe", defaultPre[0]);
            Assert.Contains("EmuDrive:Saves", defaultPre[0]);
            Assert.Contains("EmuDrive:States", defaultPre[1]);

            // Console override test
            var col = new ConsoleCollectionRule
            {
                ConsoleName = "PS1",
                OverrideJobSyncPaths = true
            };
            col.CustomSyncPaths.Add(new GameSyncPathItem
            {
                Enabled = true,
                Label = "DuckStation Memory Cards",
                LocalPath = @"C:\Emulators\DuckStation\memcards",
                RemoteTarget = "EmuDrive:PS1_Memcards",
                SyncMode = "bisync"
            });

            var (overridePre, overridePost) = cfg.ResolveAllSyncCommands("rclone.exe", col);
            Assert.Single(overridePre);
            Assert.Contains("PS1_Memcards", overridePre[0]);
        }

        [Fact]
        public void RomLauncherGenerator_CleanupOrphanedLaunchersAndShortcuts_DeletesDeadLaunchersAndUrls()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "HKTest_Cleanup_" + Guid.NewGuid().ToString("N"));
            string romsDir = Path.Combine(tempDir, "ROMs");
            string launchersDir = Path.Combine(tempDir, "Launchers");
            string shortcutsDir = Path.Combine(tempDir, "Shortcuts");

            Directory.CreateDirectory(romsDir);
            Directory.CreateDirectory(Path.Combine(launchersDir, "SNES"));
            Directory.CreateDirectory(Path.Combine(shortcutsDir, "SNES"));

            try
            {
                // Create an existing game and launcher
                string game7z = Path.Combine(romsDir, "Super Mario World.7z");
                File.WriteAllText(game7z, "rom-content");

                string validBat = Path.Combine(launchersDir, "SNES", "Super Mario World - SNES.bat");
                File.WriteAllText(validBat, $"7z x \"{game7z}\" -y\nretroarch.exe");

                string validUrl = Path.Combine(shortcutsDir, "SNES", "Super Mario World - SNES.url");
                File.WriteAllText(validUrl, $"[InternetShortcut]\nURL=file:///{validBat.Replace('\\', '/')}\n");

                // Create an orphaned launcher and shortcut where ROM is missing
                string missingGame7z = Path.Combine(romsDir, "F-Zero (Deleted).7z");
                string orphanBat = Path.Combine(launchersDir, "SNES", "F-Zero (Deleted) - SNES.bat");
                File.WriteAllText(orphanBat, $"7z x \"{missingGame7z}\" -y\nretroarch.exe");

                string orphanUrl = Path.Combine(shortcutsDir, "SNES", "F-Zero (Deleted) - SNES.url");
                File.WriteAllText(orphanUrl, $"[InternetShortcut]\nURL=file:///{orphanBat.Replace('\\', '/')}\n");

                var cfg = new RomLauncherGeneratorConfig
                {
                    LaunchersOutputRootPath = launchersDir,
                    StartMenuShortcutsPath = shortcutsDir
                };

                var col = new ConsoleCollectionRule
                {
                    ConsoleName = "SNES",
                    ArchivesPath = romsDir
                };

                var appCfg = new AppConfig();
                var dep = new DependencyCheckService(() => appCfg);
                var net = new NetworkSafetyService(() => appCfg, (_, _) => { });
                var proc = new ProcessRunnerService((_, _) => { });
                var ctx = new ModuleExecutionContext
                {
                    Config = appCfg,
                    ConfigService = new ConfigService(),
                    Dependencies = dep,
                    NetworkSafety = net,
                    ProcessRunner = proc,
                    IsDryRun = false,
                    WaitIfQueuePausedAsync = _ => Task.CompletedTask,
                    Log = (_, _) => { },
                    UpdateModuleProgress = (_, _) => { }
                };

                int deleted = RomLauncherGeneratorModule.CleanupOrphanedLaunchersAndShortcuts(ctx, cfg, new List<ConsoleCollectionRule> { col });

                Assert.True(deleted >= 2);
                Assert.True(File.Exists(validBat));
                Assert.True(File.Exists(validUrl));
                Assert.False(File.Exists(orphanBat));
                Assert.False(File.Exists(orphanUrl));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void PendingSwapService_Add_Persist_And_Sweep()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "hk_swap_test_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);

            try
            {
                string jsonPath = Path.Combine(tempDir, "pending_swaps.json");
                var service = new PendingSwapService(jsonPath);

                string workingPath = Path.Combine(tempDir, "movie.mkv.new");
                string targetPath = Path.Combine(tempDir, "movie.mkv");
                File.WriteAllText(workingPath, "complete encoded movie stream");

                service.AddOrUpdate(workingPath, targetPath, "movie.mkv");

                Assert.Equal(1, service.Count);
                Assert.True(service.IsPending(workingPath));
                Assert.True(service.IsPending(targetPath));

                // Verify persistence to JSON
                Assert.True(File.Exists(jsonPath));
                var reloaded = new PendingSwapService(jsonPath);
                Assert.Equal(1, reloaded.Count);

                // Sweep and commit
                int committed = reloaded.SweepAndCommitAll();
                Assert.Equal(1, committed);
                Assert.Equal(0, reloaded.Count);
                Assert.False(File.Exists(workingPath));
                Assert.True(File.Exists(targetPath));
                Assert.Equal("complete encoded movie stream", File.ReadAllText(targetPath));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void CommitPartialEncode_FallbackToNew_And_RegistersPendingSwap()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "hk_commit_fallback_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);

            try
            {
                string jsonPath = Path.Combine(tempDir, "pending_swaps.json");
                var pendingService = new PendingSwapService(jsonPath);

                var cfg = new AppConfig();
                var netSafety = new NetworkSafetyService(() => cfg, (_, _) => { }, pendingService);

                string partialPath = Path.Combine(tempDir, "video.mkv.partial.mkv");
                string targetPath = Path.Combine(tempDir, "video.mkv");

                File.WriteAllText(partialPath, "encoded data");

                // Lock targetPath by opening an exclusive FileStream
                File.WriteAllText(targetPath, "original file held by plex");
                using (var lockStream = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    // Attempt commit: target is locked, so it should fall back to targetPath + ".new"
                    bool committed = netSafety.CommitPartialEncode(partialPath, targetPath, maxAttempts: 2, initialDelayMs: 10);

                    // Must return true to prevent caller from discarding the file!
                    Assert.True(committed);

                    string fallbackNew = targetPath + ".new";
                    Assert.True(File.Exists(fallbackNew));
                    Assert.Equal("encoded data", File.ReadAllText(fallbackNew));

                    // Must be registered in pending swaps
                    Assert.Equal(1, pendingService.Count);
                    Assert.True(pendingService.IsPending(fallbackNew));
                    Assert.True(pendingService.IsPending(targetPath));
                }

                // Now that lockStream is disposed, sweep should succeed
                int swept = pendingService.SweepAndCommitAll();
                Assert.Equal(1, swept);
                Assert.Equal(0, pendingService.Count);
                Assert.Equal("encoded data", File.ReadAllText(targetPath));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void Scheduler_ParseTimeList_SortsAndFilters()
        {
            var parsed = SchedulerAndNotificationService.ParseTimeList("18:30, 03:00; 12:00, 03:00, 25:99, invalid");
            Assert.Equal(3, parsed.Count);
            Assert.Equal(TimeSpan.FromHours(3), parsed[0]);
            Assert.Equal(TimeSpan.FromHours(12), parsed[1]);
            Assert.Equal(new TimeSpan(18, 30, 0), parsed[2]);
        }

        [Fact]
        public void Scheduler_IntervalCadence_TriggersAndAdvances()
        {
            var cfg = new AppConfig
            {
                EnableMasterSchedule = true,
                MasterScheduleMode = MasterScheduleMode.IntervalCadence,
                MasterScheduleIntervalMinutes = 30,
                LastMasterScheduleRunUtc = DateTime.UtcNow.AddMinutes(-31)
            };

            var scheduler = new SchedulerAndNotificationService(() => cfg, (_, _) => { }, (_, _, _) => { });

            // Since 31 minutes > 30 minutes, it should trigger
            bool shouldTrigger = scheduler.ShouldTriggerMasterScheduleNow();
            Assert.True(shouldTrigger);

            // LastMasterScheduleRunUtc should be updated to now
            Assert.NotNull(cfg.LastMasterScheduleRunUtc);
            Assert.True(DateTime.UtcNow - cfg.LastMasterScheduleRunUtc.Value < TimeSpan.FromSeconds(5));

            // Immediate second check should return false because interval hasn't elapsed
            Assert.False(scheduler.ShouldTriggerMasterScheduleNow());
        }

        [Fact]
        public void Scheduler_SpecificTimes_TriggersAndPreventsDuplicateTrigger()
        {
            var now = DateTime.Now;
            string currentTimeStr = $"{now.Hour:D2}:{now.Minute:D2}";

            var cfg = new AppConfig
            {
                EnableMasterSchedule = true,
                MasterScheduleMode = MasterScheduleMode.SpecificTimes,
                MasterScheduleSpecificTimesCsv = $"01:00, {currentTimeStr}, 23:00",
                MasterScheduleDays = new List<DayOfWeek> { now.DayOfWeek }
            };

            var scheduler = new SchedulerAndNotificationService(() => cfg, (_, _) => { }, (_, _, _) => { });

            // Should trigger because currentTime matches
            bool triggered = scheduler.ShouldTriggerMasterScheduleNow();
            Assert.True(triggered);

            // Same slot on same day should not trigger again
            bool secondTrigger = scheduler.ShouldTriggerMasterScheduleNow();
            Assert.False(secondTrigger);
        }

        [Fact]
        public void Scheduler_OverlapGuard_AdvancesScheduleWithoutReTrigger()
        {
            var cfg = new AppConfig
            {
                EnableMasterSchedule = true,
                MasterScheduleMode = MasterScheduleMode.IntervalCadence,
                MasterScheduleIntervalMinutes = 15,
                LastMasterScheduleRunUtc = DateTime.UtcNow.AddMinutes(-20)
            };

            var scheduler = new SchedulerAndNotificationService(() => cfg, (_, _) => { }, (_, _, _) => { });

            // Suppose the job queue is running (_isQueueRunning == true).
            // The background timer checks if the schedule is due:
            bool isDue = scheduler.ShouldTriggerMasterScheduleNow();
            Assert.True(isDue);

            // Because queue is running, MainWindow skips starting pipeline, but scheduler's state was advanced:
            bool isDueImmediatelyAfter = scheduler.ShouldTriggerMasterScheduleNow();
            Assert.False(isDueImmediatelyAfter);
        }

        [Fact]
        public void PendingSwapService_Discard_RemovesFileAndRegistry()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "hk_discard_test_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);

            try
            {
                string jsonPath = Path.Combine(tempDir, "pending_swaps.json");
                var service = new PendingSwapService(jsonPath);

                string workingPath = Path.Combine(tempDir, "failed.new");
                string targetPath = Path.Combine(tempDir, "failed.mkv");
                File.WriteAllText(workingPath, "data to discard");

                service.AddOrUpdate(workingPath, targetPath, "failed.mkv");
                Assert.Equal(1, service.Count);

                var item = service.GetAll()[0];
                service.Discard(item);

                Assert.Equal(0, service.Count);
                Assert.False(File.Exists(workingPath));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void NamedJobQueue_BackwardCompatibility_JobsWrapperDelegatesToActiveQueue()
        {
            var cfg = AppConfig.CreateDefaults();
            Assert.NotEmpty(cfg.NamedQueues);
            var activeQueue = cfg.GetActiveQueue();
            Assert.Equal("Default Queue", activeQueue.Name);

            // Adding to cfg.Jobs
            cfg.Jobs.Add(new HousekeepingJob { Name = "TestJob1", ModuleType = ModuleId.CloudSync });

            Assert.Single(activeQueue.Jobs);
            Assert.Equal("TestJob1", activeQueue.Jobs[0].Name);

            // Switching active queue
            var q2 = new NamedJobQueue { Id = "q2", Name = "Nightly Backup" };
            q2.Jobs.Add(new HousekeepingJob { Name = "BackupJob", ModuleType = ModuleId.ArchiveCompressor });
            cfg.NamedQueues.Add(q2);
            cfg.ActiveQueueId = "q2";

            Assert.Single(cfg.Jobs);
            Assert.Equal("BackupJob", cfg.Jobs[0].Name);
        }

        [Fact]
        public void NamedJobQueue_Clone_ProducesIndependentQueueWithFreshJobIds()
        {
            var original = new NamedJobQueue
            {
                Id = "orig_id",
                Name = "Render Queue",
                EnableSchedule = true,
                ScheduleMode = MasterScheduleMode.DailyTime,
                ScheduleDailyTime = "23:30",
                Jobs = new List<HousekeepingJob>
                {
                    new HousekeepingJob { JobId = "job1", Name = "Transcode 4K", ModuleType = ModuleId.MediaTranscoder }
                }
            };

            var cloned = original.CloneAsDuplicate();

            Assert.NotEqual(original.Id, cloned.Id);
            Assert.Equal("Render Queue (Copy)", cloned.Name);
            Assert.True(cloned.EnableSchedule);
            Assert.Equal("23:30", cloned.ScheduleDailyTime);
            Assert.Single(cloned.Jobs);
            Assert.NotEqual("job1", cloned.Jobs[0].JobId);
            Assert.Equal("Transcode 4K", cloned.Jobs[0].Name);
        }

        [Fact]
        public void NamedJobQueue_SchedulerEvaluation_HandlesIntervalAndSpecificTimes()
        {
            var q = new NamedJobQueue
            {
                EnableSchedule = true,
                ScheduleMode = MasterScheduleMode.IntervalCadence,
                ScheduleIntervalMinutes = 30,
                LastRunUtc = DateTime.UtcNow.AddMinutes(-35)
            };

            var scheduler = new SchedulerAndNotificationService(() => new AppConfig(), (_, _) => { }, (_, _, _) => { });

            DateTime now = DateTime.UtcNow;
            bool shouldRun = scheduler.ShouldTriggerQueueNow(q, now);
            Assert.True(shouldRun);
            Assert.True(q.LastRunUtc.HasValue && q.LastRunUtc.Value >= now.AddSeconds(-5));

            // Immediately checking again should return false (interval not elapsed)
            bool runAgain = scheduler.ShouldTriggerQueueNow(q, now);
            Assert.False(runAgain);
        }

        [Fact]
        public void RemoteNodeConfig_VersionCheck_CorrectlyDeterminesAppAndSettingsUpdateEligibility()
        {
            var node = new RemoteNodeConfig
            {
                Name = "LivingRoomPC",
                HostOrIp = "192.168.1.150",
                IsReachable = true,
                DetectedVersion = "2.5.0"
            };

            // Remote is older (2.5.0) than local (e.g. 2.6.0)
            RemoteNodeDeploymentService.EvaluateVersionEligibility(node, "2.6.0");
            Assert.True(node.CanUpdateApp);
            Assert.False(node.CanUpdateSettings);

            // Remote matches local (2.6.0)
            node.DetectedVersion = "2.6.0";
            RemoteNodeDeploymentService.EvaluateVersionEligibility(node, "2.6.0");
            Assert.False(node.CanUpdateApp);
            Assert.True(node.CanUpdateSettings);

            // Unreachable node cannot update either
            node.IsReachable = false;
            RemoteNodeDeploymentService.EvaluateVersionEligibility(node, "2.6.0");
            Assert.False(node.CanUpdateApp);
            Assert.False(node.CanUpdateSettings);
        }

        [Fact]
        public void SystemPerformanceStats_Formatting_ProducesReadableMetrics()
        {
            var stats = new SystemPerformanceStats
            {
                CpuUsagePercent = 42.0,
                RamTotalGb = 32.0,
                RamUsedGb = 16.0,
                DiskDriveLetter = "D:",
                DiskFreeGb = 372.0,
                DiskTotalGb = 1000.0,
                NetworkUsageMbps = 85.7
            };

            Assert.Equal("42%", stats.FormattedCpu);
            Assert.Equal("16.0 / 32.0 GB (50%)", stats.FormattedRam);
            Assert.Equal("D: 372 GB Free", stats.FormattedDisk);
            Assert.Equal("85.7 Mbps", stats.FormattedNet);
        }

        [Fact]
        public void NoIntroRedumpDatParser_ParsesLogiqxXmlAndClrMamePro_PopulatesIndexes()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "HK_DatTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                // Create dummy Logiqx XML DAT
                string xmlDatPath = Path.Combine(tempDir, "Sony - PlayStation.dat");
                string xmlContent = @"<?xml version=""1.0""?>
<datafile>
  <header>
    <name>Sony - PlayStation</name>
    <description>Sony - PlayStation</description>
  </header>
  <game name=""Crash Bandicoot (USA)"">
    <description>Crash Bandicoot (USA)</description>
    <rom name=""Crash Bandicoot (USA).bin"" size=""540000000"" crc=""11223344"" sha1=""A1B2C3D4E5F60718293A4B5C6D7E8F9012345678"" />
  </game>
</datafile>";
                File.WriteAllText(xmlDatPath, xmlContent);

                // Create dummy ClrMamePro text DAT
                string txtDatPath = Path.Combine(tempDir, "Nintendo - Super Nintendo Entertainment System.dat");
                string txtContent = @"clrmamepro (
	name ""Nintendo - Super Nintendo Entertainment System""
	description ""Nintendo - Super Nintendo Entertainment System""
)

game (
	name ""Super Mario World (USA)""
	description ""Super Mario World (USA)""
	rom ( name ""Super Mario World (USA).sfc"" size 524288 crc 55667788 sha1 b2c3d4e5f60718293a4b5c6d7e8f90123456789a )
)";
                File.WriteAllText(txtDatPath, txtContent);

                // Parse XML DAT
                var xmlIndex = NoIntroRedumpDatParser.ParseFile(xmlDatPath);
                Assert.Single(xmlIndex.BySha1);

                // Lookup by SHA-1 (case insensitive)
                var match1 = xmlIndex.FindMatch("a1b2c3d4e5f60718293a4b5c6d7e8f9012345678", null, null);
                Assert.NotNull(match1);
                Assert.Equal("Crash Bandicoot (USA)", match1.GameName);
                Assert.Equal("Crash Bandicoot (USA).bin", match1.RomFileName);

                // Parse ClrMamePro text DAT
                var txtIndex = NoIntroRedumpDatParser.ParseFile(txtDatPath);
                Assert.Single(txtIndex.BySha1);

                // Lookup by CRC32
                var match2 = txtIndex.FindMatch(null, "55667788", null);
                Assert.NotNull(match2);
                Assert.Equal("Super Mario World (USA)", match2.GameName);
                Assert.Equal("Super Mario World (USA).sfc", match2.RomFileName);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task RomManifestCsvService_TracksDualFilenames_ClassifiesNA_AndMirrorsToSecondary()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "HK_ManifestTest_" + Guid.NewGuid().ToString("N"));
            string secondaryDir = Path.Combine(tempDir, "SecondaryBackups");
            string consoleDir = Path.Combine(tempDir, "SNES_Games");
            Directory.CreateDirectory(consoleDir);

            try
            {
                // Create a dummy game file so scan finds it
                string romPath = Path.Combine(consoleDir, "Super Mario World.sfc");
                File.WriteAllBytes(romPath, new byte[] { 0x01, 0x02, 0x03, 0x04 });

                string csvPath = Path.Combine(consoleDir, "collection_manifest.csv");

                // Add test entry with dual filenames
                var item = new RomManifestItem
                {
                    GameTitle = "Super Mario World",
                    ProperName = "Super Mario World (USA)",
                    ArchiveFileName = "Super Mario World.7z",
                    RomOrFolderName = "Super Mario World.sfc",
                    VerifiedLocalHash = "B2C3D4E5",
                    MatchStatus = "MATCH",
                    Notes = "Verified match against No-Intro"
                };

                var items = new List<RomManifestItem> { item };

                // Save manifest directly
                RomManifestCsvService.WriteManifestToCsv(csvPath, items);

                Assert.True(File.Exists(csvPath));
                string primaryCsvContent = File.ReadAllText(csvPath);
                Assert.Contains("Archive File Name", primaryCsvContent);
                Assert.Contains("ROM / Folder Name", primaryCsvContent);
                Assert.Contains("Super Mario World.7z", primaryCsvContent);
                Assert.Contains("Super Mario World.sfc", primaryCsvContent);
                Assert.Contains("MATCH", primaryCsvContent);

                // Test LoadExistingManifest round-trip
                var loaded = RomManifestCsvService.LoadExistingManifest(csvPath);
                Assert.Single(loaded);
                Assert.True(loaded.ContainsKey("super mario world.7z"));
                Assert.Equal("Super Mario World.sfc", loaded["super mario world.7z"].RomOrFolderName);

                // Test Secondary Mirror creation via GenerateOrUpdateManifestAsync
                var config = new RomLauncherGeneratorConfig
                {
                    EnableCollectionManifestCsv = true,
                    ManifestCsvFileName = "collection_manifest.csv",
                    EnableSecondaryManifestStorage = true,
                    SecondaryManifestDirectory = secondaryDir
                };

                await RomManifestCsvService.GenerateOrUpdateManifestAsync(consoleDir, "SNES", config, new DatIndex(), (_, _) => { });

                string expectedMirrorPath = Path.Combine(secondaryDir, "SNES_collection_manifest.csv");
                Assert.True(File.Exists(expectedMirrorPath));
                string mirrorContent = File.ReadAllText(expectedMirrorPath);
                string updatedPrimaryContent = File.ReadAllText(csvPath);
                Assert.Equal(updatedPrimaryContent, mirrorContent);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void PerformanceStats_DiskCulling_SortsAlphabeticallyAndDropsFromEndWhenConstrained()
        {
            var stats = new SystemPerformanceStats
            {
                Disks = new System.Collections.Generic.List<DiskDriveMetricItem>
                {
                    new DiskDriveMetricItem { DriveLetter = "Z:", FreeGb = 500.0, TotalGb = 1000.0 },
                    new DiskDriveMetricItem { DriveLetter = "C:", FreeGb = 120.0, TotalGb = 512.0 },
                    new DiskDriveMetricItem { DriveLetter = "D:", FreeGb = 450.0, TotalGb = 2000.0 },
                    new DiskDriveMetricItem { DriveLetter = "E:", FreeGb = 80.0, TotalGb = 256.0 }
                }
            };

            // Unconstrained (0) -> All sorted alphabetically: C, D, E, Z
            string allDisks = stats.GetCulledDisksText(0);
            Assert.Contains("C: 120 GB Free", allDisks);
            Assert.Contains("D: 450 GB Free", allDisks);
            Assert.Contains("E: 80 GB Free", allDisks);
            Assert.Contains("Z: 500 GB Free", allDisks);
            Assert.True(allDisks.IndexOf("C:") < allDisks.IndexOf("D:"));
            Assert.True(allDisks.IndexOf("D:") < allDisks.IndexOf("E:"));
            Assert.True(allDisks.IndexOf("E:") < allDisks.IndexOf("Z:"));

            // Max 3 -> Cull last disk alphabetically (Z:)
            string max3 = stats.GetCulledDisksText(3);
            Assert.Contains("C:", max3);
            Assert.Contains("D:", max3);
            Assert.Contains("E:", max3);
            Assert.DoesNotContain("Z:", max3);

            // Max 2 -> Cull last 2 disks alphabetically (Z:, E:)
            string max2 = stats.GetCulledDisksText(2);
            Assert.Contains("C:", max2);
            Assert.Contains("D:", max2);
            Assert.DoesNotContain("E:", max2);
            Assert.DoesNotContain("Z:", max2);

            // Max 1 -> Only C: remains
            string max1 = stats.GetCulledDisksText(1);
            Assert.Contains("C:", max1);
            Assert.DoesNotContain("D:", max1);
            Assert.DoesNotContain("E:", max1);
            Assert.DoesNotContain("Z:", max1);
        }

        [Fact]
        public void CrossQueueJobReuse_ClonesWithIndependentIdAndPreservesConfig()
        {
            var sourceJob = new HousekeepingJob
            {
                JobId = "JOB_ORIG_01",
                Name = "Nightly 4K Encode",
                ModuleType = ModuleId.MediaTranscoder,
                Enabled = true
            };
            sourceJob.MediaTranscoder!.DetectionMode = SourceDetectionMode.FilenameTagMatching;
            sourceJob.MediaTranscoder.MasterFileTag = "[4K-HDR]";

            // Clone for reuse across queues
            var reusedJob = sourceJob.CloneAsDuplicate();

            Assert.NotEqual(sourceJob.JobId, reusedJob.JobId);
            Assert.Equal(sourceJob.ModuleType, reusedJob.ModuleType);
            Assert.Equal(SourceDetectionMode.FilenameTagMatching, reusedJob.MediaTranscoder!.DetectionMode);
            Assert.Equal("[4K-HDR]", reusedJob.MediaTranscoder.MasterFileTag);

            // Modifying the reused job does not alter the source job
            reusedJob.MediaTranscoder.MasterFileTag = "[1080p-SDR]";
            Assert.Equal("[4K-HDR]", sourceJob.MediaTranscoder.MasterFileTag);
            Assert.Equal("[1080p-SDR]", reusedJob.MediaTranscoder.MasterFileTag);
        }

        [Fact]
        public void NamedJobQueue_CloneAsDuplicate_CreatesDistinctQueueAndJobsWithNewIds()
        {
            var q = new NamedJobQueue
            {
                Id = "queue_alpha",
                Name = "Alpha Queue",
                Description = "Alpha processing pipeline",
                EnableSchedule = true,
                ScheduleMode = MasterScheduleMode.IntervalCadence,
                ScheduleIntervalMinutes = 30
            };
            var job1 = new HousekeepingJob { JobId = "J1", Name = "Job 1", ModuleType = ModuleId.ProxyGenerator };
            var job2 = new HousekeepingJob { JobId = "J2", Name = "Job 2", ModuleType = ModuleId.CloudSync };
            q.Jobs.Add(job1);
            q.Jobs.Add(job2);

            var clonedQ = q.CloneAsDuplicate();

            Assert.NotEqual(q.Id, clonedQ.Id);
            Assert.Equal("Alpha Queue (Copy)", clonedQ.Name);
            Assert.Equal(2, clonedQ.Jobs.Count);

            // Cloned jobs must have new distinct JobIds
            Assert.NotEqual(job1.JobId, clonedQ.Jobs[0].JobId);
            Assert.NotEqual(job2.JobId, clonedQ.Jobs[1].JobId);
            Assert.NotEqual(clonedQ.Jobs[0].JobId, clonedQ.Jobs[1].JobId);
        }

        [Fact]
        public void EnsureDefinedJobsSynchronized_SynchronizesDefinedJobsAndPreservesReferences()
        {
            var cfg = new AppConfig();
            cfg.EnsureNamedQueuesInitialized();
            var q1 = cfg.NamedQueues[0];
            var sharedJob = new HousekeepingJob { JobId = "JOB_SHARED", Name = "Transcode Master 4K", ModuleType = ModuleId.MediaTranscoder };
            q1.Jobs.Add(sharedJob);

            var q2 = new NamedJobQueue { Id = "q2", Name = "Second Queue" };
            q2.Jobs.Add(sharedJob);
            cfg.NamedQueues.Add(q2);

            // Execute synchronization
            cfg.EnsureDefinedJobsSynchronized();

            // DefinedJobs contains sharedJob
            Assert.Contains(cfg.DefinedJobs, j => j.JobId == "JOB_SHARED");
            var definedInstance = cfg.DefinedJobs.First(j => j.JobId == "JOB_SHARED");

            // In-memory reference equality across queues
            Assert.Same(definedInstance, q1.Jobs.First(j => j.JobId == "JOB_SHARED"));
            Assert.Same(definedInstance, q2.Jobs.First(j => j.JobId == "JOB_SHARED"));

            // Updating defined job in Job Builder updates both queues simultaneously
            definedInstance.Name = "Renamed Transcode Job";
            Assert.Equal("Renamed Transcode Job", q1.Jobs.First(j => j.JobId == "JOB_SHARED").Name);
            Assert.Equal("Renamed Transcode Job", q2.Jobs.First(j => j.JobId == "JOB_SHARED").Name);
        }

        [Fact]
        public void GameSyncPathItem_NotifiesPropertyChanged_WhenLocalPathOrRemoteTargetChanged()
        {
            var item = new GameSyncPathItem();
            var changedProps = new List<string>();
            item.PropertyChanged += (s, e) => { if (e.PropertyName != null) changedProps.Add(e.PropertyName); };

            item.LocalPath = @"D:\Saves\SNES";
            item.RemoteTarget = "EmuDrive:Saves/SNES";
            item.Label = "SRAM Battery";
            item.SyncMode = "copy";
            item.Enabled = false;

            Assert.Contains(nameof(GameSyncPathItem.LocalPath), changedProps);
            Assert.Contains(nameof(GameSyncPathItem.RemoteTarget), changedProps);
            Assert.Contains(nameof(GameSyncPathItem.Label), changedProps);
            Assert.Contains(nameof(GameSyncPathItem.SyncMode), changedProps);
            Assert.Contains(nameof(GameSyncPathItem.Enabled), changedProps);
        }

        [Fact]
        public void NamedJobQueue_CanBeEmpty_AndDoesNotRePopulateOnSync()
        {
            var cfg = AppConfig.CreateDefaults();
            var starter = HousekeepingJob.CreateDefaultStarterJobs();
            cfg.DefinedJobs = new List<HousekeepingJob>(starter);
            cfg.NamedQueues[0].Jobs = new List<HousekeepingJob>(starter);
            cfg.EnsureDefinedJobsSynchronized();
            Assert.NotEmpty(cfg.DefinedJobs);
            Assert.NotEmpty(cfg.NamedQueues[0].Jobs);

            // Create a second queue with 0 jobs
            var testingQueue = new NamedJobQueue
            {
                Id = "queue_testing",
                Name = "Testing",
                Jobs = new List<HousekeepingJob>()
            };
            cfg.NamedQueues.Add(testingQueue);
            cfg.SetActiveQueue("queue_testing");

            // Execute synchronization
            cfg.EnsureDefinedJobsSynchronized();

            // The testing queue must remain completely empty (0 jobs), not auto-filled with all defined jobs
            Assert.Empty(testingQueue.Jobs);
            Assert.Empty(cfg.Jobs);

            // Now add 2 jobs to testing queue
            var jobA = cfg.DefinedJobs[0];
            var jobB = cfg.DefinedJobs[1];
            testingQueue.Jobs.Add(jobA);
            testingQueue.Jobs.Add(jobB);
            Assert.Equal(2, testingQueue.Jobs.Count);

            // Remove jobA
            testingQueue.Jobs.RemoveAll(j => j.JobId == jobA.JobId);
            Assert.Single(testingQueue.Jobs);
            Assert.Equal(jobB.JobId, testingQueue.Jobs[0].JobId);

            // Sync again
            cfg.EnsureDefinedJobsSynchronized();
            Assert.Single(testingQueue.Jobs);
            Assert.Equal(jobB.JobId, testingQueue.Jobs[0].JobId);

            // Remove jobB (queue becomes empty)
            testingQueue.Jobs.RemoveAll(j => j.JobId == jobB.JobId);
            Assert.Empty(testingQueue.Jobs);

            // Sync again; must remain empty and not reload removed jobs
            cfg.EnsureDefinedJobsSynchronized();
            Assert.Empty(testingQueue.Jobs);
        }

        [Fact]
        public void NamedJobQueue_MultipleQueues_IndependentJobLists_PersistToJson()
        {
            var cfg = AppConfig.CreateDefaults();
            var q1 = cfg.NamedQueues[0]; // Default Queue with 7 starter jobs

            var q2 = new NamedJobQueue
            {
                Id = "queue_testing_custom",
                Name = "Testing Custom",
                Jobs = new List<HousekeepingJob>()
            };
            cfg.NamedQueues.Add(q2);

            // Add only 1 job to q2
            var customJob = new HousekeepingJob
            {
                JobId = "JOB_CUSTOM_1",
                Name = "Dedicated Cloud Backup",
                ModuleType = ModuleId.CloudSync
            };
            cfg.DefinedJobs.Add(customJob);
            q2.Jobs.Add(customJob);
            cfg.SetActiveQueue(q2.Id);

            cfg.EnsureDefinedJobsSynchronized();
            Assert.Equal(q2.Id, cfg.ActiveQueueId);
            Assert.Single(q2.Jobs);
            Assert.Equal("JOB_CUSTOM_1", q2.Jobs[0].JobId);

            // Serialize to JSON
            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
            string json = System.Text.Json.JsonSerializer.Serialize(cfg, options);

            // Deserialize
            var loaded = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json, options);
            Assert.NotNull(loaded);
            loaded.EnsureNamedQueuesInitialized();
            loaded.EnsureDefinedJobsSynchronized();

            Assert.Equal(2, loaded.NamedQueues.Count);
            var loadedQ1 = loaded.NamedQueues.First(q => q.Id == q1.Id);
            var loadedQ2 = loaded.NamedQueues.First(q => q.Id == q2.Id);

            Assert.Equal(q1.Jobs.Count, loadedQ1.Jobs.Count);
            Assert.Single(loadedQ2.Jobs);
            Assert.Equal("JOB_CUSTOM_1", loadedQ2.Jobs[0].JobId);
            Assert.Equal("Dedicated Cloud Backup", loadedQ2.Jobs[0].Name);

            // Now remove the only job from loadedQ2
            loadedQ2.Jobs.Clear();
            loaded.EnsureDefinedJobsSynchronized();
            Assert.Empty(loadedQ2.Jobs);

            // Re-serialize with empty queue and deserialize again
            string jsonEmpty = System.Text.Json.JsonSerializer.Serialize(loaded, options);
            var reloaded = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(jsonEmpty, options);
            Assert.NotNull(reloaded);
            reloaded.EnsureNamedQueuesInitialized();
            reloaded.EnsureDefinedJobsSynchronized();

            var reloadedQ2 = reloaded.NamedQueues.First(q => q.Id == q2.Id);
            Assert.Empty(reloadedQ2.Jobs);
        }

        [Fact]
        public void ConfigService_LoadSimulation_PreservesEmptyQueueAndDoesNotTriggerLegacyMigration()
        {
            var cfg = AppConfig.CreateDefaults();
            var starter = HousekeepingJob.CreateDefaultStarterJobs();
            cfg.DefinedJobs = new List<HousekeepingJob>(starter);
            cfg.NamedQueues[0].Jobs = new List<HousekeepingJob>(starter);

            var q2 = new NamedJobQueue
            {
                Id = "testing_queue_id",
                Name = "testing",
                Jobs = new List<HousekeepingJob>()
            };
            cfg.NamedQueues.Add(q2);
            cfg.SetActiveQueue("testing_queue_id");

            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
            string json = System.Text.Json.JsonSerializer.Serialize(cfg, options);

            // Simulate ConfigService.Load() steps:
            var loaded = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json, options);
            Assert.NotNull(loaded);
            loaded.MigrateLegacyModulesToJobsIfNeeded();
            loaded.EnsureNamedQueuesInitialized();
            loaded.EnsureDefinedJobsSynchronized();

            var loadedTesting = loaded.NamedQueues.First(q => q.Id == "testing_queue_id");
            Assert.Empty(loadedTesting.Jobs);
            Assert.Empty(loaded.Jobs);
            Assert.Equal("testing", loaded.GetActiveQueue().Name);
        }

        [Fact]
        public void TelemetryService_GetLocalPerformanceStats_ReturnsValidNonBlockingSnapshot()
        {
            using var telemetry = new SystemPerformanceTelemetryService();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var stats = telemetry.GetLocalPerformanceStats();
            sw.Stop();

            Assert.NotNull(stats);
            Assert.True(sw.ElapsedMilliseconds < 100, $"Telemetry query must be non-blocking (<100ms), took {sw.ElapsedMilliseconds}ms");
            Assert.InRange(stats.CpuUsagePercent, 0.0, 100.0);
            Assert.True(stats.RamTotalGb > 0, "RamTotalGb should be greater than 0");
            Assert.NotNull(stats.Disks);
            Assert.False(string.IsNullOrEmpty(stats.FormattedCpu));
            Assert.False(string.IsNullOrEmpty(stats.FormattedRam));
        }

        [Fact]
        public async Task TelemetryService_HeartbeatSerializationAndRemoteRetrieval()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "Housekeeping_RemoteTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string logsDir = Path.Combine(tempDir, "logs");
            Directory.CreateDirectory(logsDir);

            try
            {
                var originalStats = new SystemPerformanceStats
                {
                    CpuUsagePercent = 42.5,
                    RamUsedGb = 16.2,
                    RamTotalGb = 64.0,
                    NetworkUsageMbps = 125.4,
                    Timestamp = DateTime.UtcNow,
                    IsRunning = true,
                    ActiveJobName = "Video Archiver - 4K Master",
                    ActiveSubprocessStatus = "Encoding 4K stream: 78.5% (fps: 45.2)",
                    ActiveSubprocessProgress = 78.5,
                    HostMachineName = "REMOTE-SERVER"
                };

                string json = System.Text.Json.JsonSerializer.Serialize(originalStats, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(logsDir, "telemetry_heartbeat.json"), json);

                using var telemetry = new SystemPerformanceTelemetryService();
                var node = new RemoteNodeConfig
                {
                    Name = "Server",
                    HostOrIp = "192.168.1.100",
                    RemoteAppPath = tempDir
                };

                var retrieved = await telemetry.GetRemotePerformanceStatsAsync(node);
                Assert.NotNull(retrieved);
                Assert.Equal(42.5, retrieved.CpuUsagePercent);
                Assert.Equal(16.2, retrieved.RamUsedGb);
                Assert.Equal(64.0, retrieved.RamTotalGb);
                Assert.Equal("REMOTE-SERVER", retrieved.HostMachineName);
                Assert.True(retrieved.IsRunning);
                Assert.Equal("Video Archiver - 4K Master", retrieved.ActiveJobName);
                Assert.Equal("Encoding 4K stream: 78.5% (fps: 45.2)", retrieved.ActiveSubprocessStatus);
                Assert.Equal(78.5, retrieved.ActiveSubprocessProgress);
                Assert.NotNull(node.LastStats);
                Assert.True(node.IsReachable);
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[HousekeepingTests] Non-fatal error ignored: {__suppressedEx1.Message}"); }
            }
        }

        // ==================== HOUSEKEEPING 2.0 NEW FEATURE TESTS ==================== //

        [Fact]
        public void ConsoleSystemCatalog_ResolvesCanonicalNamesAndAliases()
        {
            Assert.Equal("Sony - PlayStation", ConsoleSystemCatalog.ResolveSystem("ps1")?.CanonicalName);
            Assert.Equal("Sony - PlayStation", ConsoleSystemCatalog.ResolveSystem("playstation")?.CanonicalName);
            Assert.Equal("Sony - PlayStation 2", ConsoleSystemCatalog.ResolveSystem("ps2")?.CanonicalName);
            Assert.Equal("Sony - PlayStation 3", ConsoleSystemCatalog.ResolveSystem("ps3")?.CanonicalName);
            Assert.Equal("Nintendo - Nintendo 64", ConsoleSystemCatalog.ResolveSystem("n64")?.CanonicalName);
            Assert.Equal("Nintendo - Super Nintendo Entertainment System", ConsoleSystemCatalog.ResolveSystem("snes")?.CanonicalName);
            Assert.Equal("Nintendo - Game Boy Advance", ConsoleSystemCatalog.ResolveSystem("gba")?.CanonicalName);
            Assert.Equal("Sega - Mega Drive - Genesis", ConsoleSystemCatalog.ResolveSystem("genesis")?.CanonicalName);
            Assert.Equal("Sega - Mega Drive - Genesis", ConsoleSystemCatalog.ResolveSystem("megadrive")?.CanonicalName);
            Assert.Equal("Microsoft - Xbox 360", ConsoleSystemCatalog.ResolveSystem("xbox360")?.CanonicalName);
        }

        [Fact]
        public async Task HashSidecarService_ComputesHashesAndGeneratesSidecar()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "hk_test_hash_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);
            try
            {
                string testFile = Path.Combine(tempDir, "TestGame.iso");
                byte[] data = System.Text.Encoding.UTF8.GetBytes("Super Mario 64 Test Data 12345");
                await File.WriteAllBytesAsync(testFile, data);

                var hashes = await HashSidecarService.ComputeFileHashesAsync(testFile);
                Assert.Equal("TestGame.iso", hashes.RelativePath);
                Assert.Equal(data.Length, hashes.Size);
                Assert.False(string.IsNullOrWhiteSpace(hashes.Crc32));
                Assert.False(string.IsNullOrWhiteSpace(hashes.Md5));
                Assert.False(string.IsNullOrWhiteSpace(hashes.Sha1));
                Assert.Equal(8, hashes.Crc32.Length);
                Assert.Equal(32, hashes.Md5.Length);
                Assert.Equal(40, hashes.Sha1.Length);

                string sidecar = HashSidecarService.GenerateSidecarContentForFiles("TestGame", new[] { hashes });
                Assert.Contains("# Housekeeping Checksum Sidecar", sidecar);
                Assert.Contains("CRC32: " + hashes.Crc32, sidecar);
                Assert.Contains("MD5: " + hashes.Md5, sidecar);
                Assert.Contains("SHA1: " + hashes.Sha1, sidecar);
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch (System.Exception __suppressedEx2) { System.Diagnostics.Debug.WriteLine($"[HousekeepingTests] Non-fatal error ignored: {__suppressedEx2.Message}"); }
            }
        }

        [Fact]
        public async Task HashSidecarService_ComputesFolderCompositeHash_Deterministically()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "hk_test_folder_hash_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);
            try
            {
                string subDir = Path.Combine(tempDir, "PS3_GAME", "USRDIR");
                Directory.CreateDirectory(subDir);
                await File.WriteAllTextAsync(Path.Combine(tempDir, "PARAM.SFO"), "Title=TestGame");
                await File.WriteAllBytesAsync(Path.Combine(subDir, "EBOOT.BIN"), new byte[] { 0x7F, 0x45, 0x4C, 0x46 });

                var folderHash1 = await HashSidecarService.ComputeFolderCompositeHashAsync(tempDir);
                var folderHash2 = await HashSidecarService.ComputeFolderCompositeHashAsync(tempDir);

                Assert.Equal(folderHash1.CompositeSha1, folderHash2.CompositeSha1);
                Assert.Equal(2, folderHash1.TotalFiles);
                Assert.NotEmpty(folderHash1.Files);
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch (System.Exception __suppressedEx3) { System.Diagnostics.Debug.WriteLine($"[HousekeepingTests] Non-fatal error ignored: {__suppressedEx3.Message}"); }
            }
        }

        [Fact]
        public void HousekeepingJob_CleanDefaultNames_ContainZeroEmojis()
        {
            foreach (ModuleId mod in Enum.GetValues<ModuleId>())
            {
                string cleanName = HousekeepingJob.GetCleanDefaultName(mod);
                Assert.False(string.IsNullOrWhiteSpace(cleanName));
                // None of the clean names should contain Unicode characters above 127
                Assert.True(cleanName.All(c => c < 128), $"Clean name '{cleanName}' for {mod} contains non-ASCII characters!");
            }
        }

        [Fact]
        public void CollectionManifestConfig_InitializesWithProperDefaults()
        {
            var cfg = CollectionManifestConfig.CreateDefault();
            Assert.False(cfg.Enabled);
            Assert.True(cfg.UnifiedMasterManifest);
            Assert.True(cfg.EnableFileHashing);
            Assert.False(cfg.GenerateHashSidecars);
            Assert.False(cfg.PackageHashIntoArchives);
            Assert.True(cfg.ExportCsv);
            Assert.True(cfg.ExportJson);
            Assert.True(cfg.ExportHtmlReport);
            Assert.Equal("Master_Collection_Manifest", cfg.UnifiedManifestName);
            Assert.Empty(cfg.TargetFolders);
        }

        [Fact]
        public void SchedulerAndNotificationService_FormatsMachineIdentifier_Accurately()
        {
            var cfg = AppConfig.CreateDefaults();
            var scheduler = new SchedulerAndNotificationService(() => cfg, (_, _) => { }, (_, _, _) => { });

            // Default: blank custom machine name -> uses Environment.MachineName
            string defaultIdentifier = scheduler.GetMachineIdentifier();
            Assert.Equal(Environment.MachineName, defaultIdentifier);

            // Custom machine tag
            cfg.DiscordCustomMachineName = "TEST-SERVER-01";
            string customIdentifier = scheduler.GetMachineIdentifier();
            Assert.Equal("TEST-SERVER-01", customIdentifier);
        }

        [Fact]
        public void WindowsWatchdogService_HasCorrectConstants()
        {
            Assert.Equal("Housekeeping_Watchdog", WindowsWatchdogService.TaskName);
        }

        [Fact]
        public void AppConfig_TerminalMaxLines_DefaultsAndPersists()
        {
            var cfg = AppConfig.CreateDefaults();
            Assert.Equal(400, cfg.TerminalMaxLines);

            cfg.TerminalMaxLines = 250;
            Assert.Equal(250, cfg.TerminalMaxLines);
            cfg.TerminalMaxLines = 1000;
            Assert.Equal(1000, cfg.TerminalMaxLines);
        }

        [Fact]
        public void CollectionManifest_ApplyFieldParseRules_ExtractsMetadataCorrectly()
        {
            var rules = new List<ManifestFieldParseRule>
            {
                new ManifestFieldParseRule
                {
                    Enabled = true,
                    ColumnName = "ShowTitle",
                    InspectTarget = ManifestInspectTarget.FileName,
                    MatchMode = ManifestMatchMode.RegexNamedCapture,
                    Pattern = @"^(?<ShowTitle>.+?)\s*-\s*[Ss]\d+",
                    AssignedValue = "ShowTitle"
                },
                new ManifestFieldParseRule
                {
                    Enabled = true,
                    ColumnName = "Season",
                    InspectTarget = ManifestInspectTarget.FileName,
                    MatchMode = ManifestMatchMode.RegexNamedCapture,
                    Pattern = @"[Ss](?<Season>\d{1,2})[Ee]\d{1,3}",
                    AssignedValue = "Season"
                },
                new ManifestFieldParseRule
                {
                    Enabled = true,
                    ColumnName = "Episode",
                    InspectTarget = ManifestInspectTarget.FileName,
                    MatchMode = ManifestMatchMode.RegexNamedCapture,
                    Pattern = @"[Ss]\d{1,2}[Ee](?<Episode>\d{1,3})",
                    AssignedValue = "Episode"
                },
                new ManifestFieldParseRule
                {
                    Enabled = true,
                    ColumnName = "Resolution",
                    InspectTarget = ManifestInspectTarget.FileName,
                    MatchMode = ManifestMatchMode.RegexPattern,
                    Pattern = @"(2160p|4[Kk]|1080p|720p)",
                    AssignedValue = "$1"
                },
                new ManifestFieldParseRule
                {
                    Enabled = true,
                    ColumnName = "IsRemux",
                    InspectTarget = ManifestInspectTarget.FileName,
                    MatchMode = ManifestMatchMode.TextContains,
                    Pattern = "Remux",
                    AssignedValue = "Yes"
                },
                new ManifestFieldParseRule
                {
                    Enabled = true,
                    ColumnName = "FirstToken",
                    InspectTarget = ManifestInspectTarget.FileName,
                    MatchMode = ManifestMatchMode.SplitToken,
                    SplitDelimiter = "-",
                    SplitIndex = 0
                }
            };

            string sample = "Breaking Bad - S01E01 - Pilot [1080p Remux].mkv";
            var parsed = CollectionManifestModule.ApplyFieldParseRules(sample, sample, Path.Combine(Path.GetTempPath(), "hk_media", sample), rules);

            Assert.Equal("Breaking Bad", parsed["ShowTitle"]);
            Assert.Equal("01", parsed["Season"]);
            Assert.Equal("01", parsed["Episode"]);
            Assert.Equal("1080p", parsed["Resolution"]);
            Assert.Equal("Yes", parsed["IsRemux"]);
            Assert.Equal("Breaking Bad", parsed["FirstToken"]);
        }

        [Fact]
        public void CollectionManifest_PersonalMediaPresets_ExtractsAccurately()
        {
            var rules = ManifestPresetCatalog.GetPersonalMediaPresets();

            // Sample 1: TV episode with disc tier and original flag
            string sample1 = "Dragon Ball - s01e01 - Secret of the Dragon Balls - DVD (Original).mkv";
            var parsed1 = CollectionManifestModule.ApplyFieldParseRules(sample1, sample1, @"V:\Media\Anime\" + sample1, rules);

            Assert.Equal("Dragon Ball", parsed1["ShowTitle"]);
            Assert.Equal("01", parsed1["Season"]);
            Assert.Equal("01", parsed1["Episode"]);
            Assert.Equal("Secret of the Dragon Balls", parsed1["EpisodeTitle"]);
            Assert.Equal("DVD", parsed1["DiscTier"]);
            Assert.Equal("Original", parsed1["SourceOrRes"]);

            // Sample 2: Movie with year, disc tier, and edition tag
            string sample2 = "Anchorman The Legend of Ron Burgundy (2004) - DVD (Original) {edition-DVD, Unrated}.mkv";
            var parsed2 = CollectionManifestModule.ApplyFieldParseRules(sample2, sample2, @"V:\Media\Movies\" + sample2, rules);

            Assert.Equal("Anchorman The Legend of Ron Burgundy", parsed2["MovieTitle"]);
            Assert.Equal("2004", parsed2["MovieYear"]);
            Assert.Equal("DVD", parsed2["DiscTier"]);
            Assert.Equal("DVD, Unrated", parsed2["Edition"]);

            // Sample 3: Path inside Japanese TV Shows folder
            string rel3 = @"Japanese TV Shows\Death Note - S01E36 - 1.28 - BR (Original).mkv";
            string file3 = "Death Note - S01E36 - 1.28 - BR (Original).mkv";
            var parsed3 = CollectionManifestModule.ApplyFieldParseRules(file3, rel3, @"V:\Media\" + rel3, rules);

            Assert.Equal("Death Note", parsed3["ShowTitle"]);
            Assert.Equal("01", parsed3["Season"]);
            Assert.Equal("36", parsed3["Episode"]);
            Assert.Equal("1.28", parsed3["EpisodeTitle"]);
            Assert.Equal("BR", parsed3["DiscTier"]);
            Assert.Equal("Japanese", parsed3["MediaLangFolder"]);
        }

        [Fact]
        public void CollectionManifest_PersonalRomPresets_ExtractsAccurately()
        {
            var rules = ManifestPresetCatalog.GetPersonalRomPresets();

            // Sample 1: Master Set item with company & platform in path
            string rel1 = @"Master Set\Nintendo\SNES\A\A.S.P. - Air Strike Patrol (USA).7z";
            string file1 = "A.S.P. - Air Strike Patrol (USA).7z";
            string setsRoot = Path.Combine(Path.GetTempPath(), "hk_sets");
            var parsed1 = CollectionManifestModule.ApplyFieldParseRules(file1, rel1, Path.Combine(setsRoot, rel1), rules);

            Assert.Equal("A.S.P. - Air Strike Patrol", parsed1["GameTitle"]);
            Assert.Equal("USA", parsed1["Region"]);
            Assert.Equal("Nintendo", parsed1["CompanyFromFolder"]);
            Assert.Equal("SNES", parsed1["PlatformFromFolder"]);

            // Sample 2: Complex disc number, revision, and dump tag
            string file2 = "Final Fantasy VII (USA) (Disc 1) (Rev A) [!].chd";
            var parsed2 = CollectionManifestModule.ApplyFieldParseRules(file2, file2, Path.Combine(setsRoot, "PS1", file2), rules);

            Assert.Equal("USA", parsed2["Region"]);
            Assert.Equal("Disc 1", parsed2["DiscNumber"]);
            Assert.Equal("Rev A", parsed2["Revision"]);
            Assert.Equal("!", parsed2["DumpTag"]);

            // Sample 3: Rom hack with hack name and version
            string file3 = "Pokemon Crystal - Legacy (1.3.1).zip";
            var parsed3 = CollectionManifestModule.ApplyFieldParseRules(file3, file3, Path.Combine(setsRoot, "RomHacks", file3), rules);

            Assert.Equal("Pokemon Crystal", parsed3["RomHackOriginalGame"]);
            Assert.Equal("Legacy", parsed3["RomHackName"]);
            Assert.Equal("1.3.1", parsed3["RomHackVersion"]);

            // Sample 4: Launcher batch file
            string file4 = "Super Mario World - Launcher.bat";
            var parsed4 = CollectionManifestModule.ApplyFieldParseRules(file4, file4, Path.Combine(setsRoot, "Launchers", file4), rules);

            Assert.Equal("True", parsed4["IsLauncher"]);
        }

        [Fact]
        public void CollectionManifest_FileNameWithoutExtension_WorksReliably()
        {
            var rules = new List<ManifestFieldParseRule>
            {
                new ManifestFieldParseRule
                {
                    Enabled = true,
                    ColumnName = "BaseName",
                    InspectTarget = ManifestInspectTarget.FileNameWithoutExtension,
                    MatchMode = ManifestMatchMode.RegexPattern,
                    Pattern = @"^(.*)$",
                    AssignedValue = "$1"
                }
            };

            var parsed = CollectionManifestModule.ApplyFieldParseRules("MyCoolFile.test.mkv", "MyCoolFile.test.mkv", @"C:\Data\MyCoolFile.test.mkv", rules);
            Assert.Equal("MyCoolFile.test", parsed["BaseName"]);
        }

        [Fact]
        public void HousekeepingJob_StripUnusedModuleConfigs_ShrinksSerializedJsonPayload()
        {
            var job = new HousekeepingJob
            {
                Name = "Test Manifest Job",
                ModuleType = ModuleId.CollectionManifest
            };
            // Populating unused module configs simulates a job that has accumulated legacy or multi-module instances
            job.ProxyGenerator = new ProxyGeneratorConfig();
            job.ArchiveCompressor = new ArchiveCompressorConfig();
            job.MediaTranscoder = new MediaTranscoderConfig();
            job.RomLauncherGenerator = new RomLauncherGeneratorConfig();
            job.CloudSync = new CloudSyncConfig();
            job.HumbleSync = new HumbleSyncConfig();
            job.CustomScripts = new CustomScriptsConfig();
            job.CollectionManifest = new CollectionManifestConfig();

            string unstrippedJson = JsonSerializer.Serialize(job);

            job.StripUnusedModuleConfigs();

            var options = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
            string strippedJson = JsonSerializer.Serialize(job, options);

            Assert.True(strippedJson.Length < unstrippedJson.Length / 3, 
                $"Stripped JSON ({strippedJson.Length} bytes) should be less than a third of unstripped JSON ({unstrippedJson.Length} bytes)");
            Assert.DoesNotContain("HandBrakeProfileId", strippedJson);
            Assert.Contains("UnifiedManifestName", strippedJson);
        }

        [Fact]
        public void RemoteControlRequest_SerializationAndRouting_WorksCleanly()
        {
            var req = new RemoteControlRequest
            {
                Action = "StartQueue",
                TargetQueueId = "q_daily_transcodes",
                TargetQueueName = "Daily Transcodes",
                TargetJobName = "Nightly 1080p",
                SenderHost = "LOCAL-DESKTOP",
                IsDryRun = false
            };

            string json = JsonSerializer.Serialize(req, new JsonSerializerOptions { WriteIndented = true });
            var deserialized = JsonSerializer.Deserialize<RemoteControlRequest>(json);

            Assert.NotNull(deserialized);
            Assert.Equal("StartQueue", deserialized.Action);
            Assert.Equal("q_daily_transcodes", deserialized.TargetQueueId);
            Assert.Equal("Daily Transcodes", deserialized.TargetQueueName);
            Assert.Equal("Nightly 1080p", deserialized.TargetJobName);
            Assert.Equal("LOCAL-DESKTOP", deserialized.SenderHost);
            Assert.False(deserialized.IsDryRun);
        }

        [Fact]
        public void ReleaseStructureDetector_IdentifiesCompositeDumpsCorrectly()
        {
            string tempBase = Path.Combine(Path.GetTempPath(), "hk_structure_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempBase);

                // PS3 folder dump
                string ps3Path = Path.Combine(tempBase, "PS3_Game_Test");
                Directory.CreateDirectory(Path.Combine(ps3Path, "PS3_GAME", "USRDIR"));
                File.WriteAllText(Path.Combine(ps3Path, "PS3_DISC.SFB"), "TEST");
                Assert.Equal(ReleaseStructureDetector.TypePs3FolderDump, ReleaseStructureDetector.DetectFolderDumpType(ps3Path));
                Assert.True(ReleaseStructureDetector.IsCompositeDump(ps3Path));
                Assert.True(ReleaseStructureDetector.ShouldTreatAsCompositeDump(ps3Path, AuditStructureMode.Auto));
                Assert.False(ReleaseStructureDetector.ShouldTreatAsCompositeDump(ps3Path, AuditStructureMode.FilesOnly));
                Assert.True(ReleaseStructureDetector.ShouldTreatAsCompositeDump(ps3Path, AuditStructureMode.FolderDumpsOnly));

                // Wii U folder dump
                string wiiUPath = Path.Combine(tempBase, "WiiU_Game_Test");
                Directory.CreateDirectory(Path.Combine(wiiUPath, "code"));
                Directory.CreateDirectory(Path.Combine(wiiUPath, "content"));
                Directory.CreateDirectory(Path.Combine(wiiUPath, "meta"));
                Assert.Equal(ReleaseStructureDetector.TypeWiiUFolderDump, ReleaseStructureDetector.DetectFolderDumpType(wiiUPath));
                Assert.True(ReleaseStructureDetector.IsCompositeDump(wiiUPath));

                // Normal loose directory
                string normalPath = Path.Combine(tempBase, "Normal_Folder");
                Directory.CreateDirectory(normalPath);
                File.WriteAllText(Path.Combine(normalPath, "test.txt"), "hello");
                Assert.Null(ReleaseStructureDetector.DetectFolderDumpType(normalPath));
                Assert.False(ReleaseStructureDetector.IsCompositeDump(normalPath));
                Assert.False(ReleaseStructureDetector.ShouldTreatAsCompositeDump(normalPath, AuditStructureMode.Auto));
            }
            finally
            {
                if (Directory.Exists(tempBase))
                {
                    try { Directory.Delete(tempBase, true); } catch (System.Exception __suppressedEx4) { System.Diagnostics.Debug.WriteLine($"[HousekeepingTests] Non-fatal error ignored: {__suppressedEx4.Message}"); }
                }
            }
        }

        [Fact]
        public void ManifestFieldParseRule_FileExtensionFilter_ScopingWorks()
        {
            var rule = new ManifestFieldParseRule
            {
                ColumnName = "Resolution",
                FileExtensionFilter = ".mkv, .mp4"
            };

            var allowedExts = rule.FileExtensionFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(e => e.StartsWith(".") ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.Contains(".mkv", allowedExts);
            Assert.Contains(".mp4", allowedExts);
            Assert.DoesNotContain(".flac", allowedExts);
            Assert.DoesNotContain(".zip", allowedExts);
        }

        [Fact]
        public void HashSidecarService_TryParseSidecarContent_ExtractsCleanChecksums()
        {
            string sidecarContent = @"# Checksum sidecar generated by Housekeeping 2.8.3
# Target: Super Mario World (USA).sfc
CRC32: 247A833E
MD5: cdd3c8c37322978ca8669b34bc89c804
SHA1: 6b47bfde7b5310ba0866e0e0f6e31103abda4b63
SHA256: 9e320f666fcf9304a9d0d3ca453f60f607d5718dfd9a6951b14a2a197b1a6279
247A833E *Super Mario World (USA).sfc";

            bool success = HashSidecarService.TryParseSidecarContent(sidecarContent, "Super Mario World (USA).sfc", out var result);

            Assert.True(success);
            Assert.NotNull(result);
            Assert.Equal("247a833e", result!.Crc32.ToLowerInvariant());
            Assert.Equal("cdd3c8c37322978ca8669b34bc89c804", result.Md5.ToLowerInvariant());
            Assert.Equal("6b47bfde7b5310ba0866e0e0f6e31103abda4b63", result.Sha1.ToLowerInvariant());
        }

        [Fact]
        public void CustomVerbosityConfig_DefaultValues_ContainExpectedTags()
        {
            var cfg = new CustomVerbosityConfig();

            Assert.Contains("ERROR", cfg.TerminalTags);
            Assert.Contains("WARN", cfg.TerminalTags);
            Assert.Contains("SUCCESS", cfg.TerminalTags);
            Assert.Contains("WORK", cfg.TerminalTags);
            Assert.Contains("HEADER", cfg.TerminalTags);
            Assert.Contains("INFO", cfg.TerminalTags);
            Assert.Contains("NET", cfg.TerminalTags);
            Assert.DoesNotContain("CMD", cfg.TerminalTags);
            Assert.DoesNotContain("OUT", cfg.TerminalTags);

            Assert.Contains("ERROR", cfg.FooterTags);
            Assert.Contains("WARN", cfg.FooterTags);
            Assert.Contains("SUCCESS", cfg.FooterTags);
            Assert.Contains("WORK", cfg.FooterTags);
            Assert.Contains("HEADER", cfg.FooterTags);
            Assert.DoesNotContain("INFO", cfg.FooterTags);
        }

        [Fact]
        public void DatTitleNormalizer_StripsCustomTagsAndArticlesCorrectly()
        {
            string prClean = DatTitleNormalizer.CleanUserTags("Super Mario World (USA) (PR)");
            Assert.Equal("Super Mario World (USA)", prClean);

            string rhClean = DatTitleNormalizer.CleanUserTags("Pokemon - Emerald Version (USA, Europe) (RH)");
            Assert.Equal("Pokemon - Emerald Version (USA, Europe)", rhClean);

            string slug1 = DatTitleNormalizer.NormalizeSlug("The Legend of Zelda: A Link to the Past (USA) (PR)");
            string slug2 = DatTitleNormalizer.NormalizeSlug("Legend of Zelda, The - A Link to the Past (USA)");
            Assert.Equal(slug1, slug2);

            string slugPokemonAccent = DatTitleNormalizer.NormalizeSlug("Pokémon - Emerald Version (USA)");
            string slugPokemonPlain = DatTitleNormalizer.NormalizeSlug("Pokemon - Emerald Version (USA)");
            Assert.Equal(slugPokemonPlain, slugPokemonAccent);
        }

        [Fact]
        public void DatIndex_MatchesByNameWithUserTags()
        {
            var index = new DatIndex();
            index.Add(new DatRomEntry
            {
                GameName = "Super Mario World (USA)",
                RomFileName = "Super Mario World (USA).sfc",
                Sha1 = "6B47BFDE7B5310BA0866E0E0F6E31103ABDA4B63",
                Crc32 = "247A833E",
                DatTitle = "Nintendo - Super Nintendo Entertainment System"
            });

            var matchPr = index.FindMatchByName("Super Mario World (USA) (PR).7z");
            Assert.NotNull(matchPr);
            Assert.Equal("Super Mario World (USA)", matchPr!.GameName);

            var matchRh = index.FindMatchByName("Super Mario World (USA) (RH).sfc");
            Assert.NotNull(matchRh);
            Assert.Equal("Super Mario World (USA)", matchRh!.GameName);
        }

        [Fact]
        public void DatIndex_FallsBackToHashWhenNameFails()
        {
            var index = new DatIndex();
            string expectedSha1 = "ABCDEF1234567890ABCDEF1234567890ABCDEF12";
            index.Add(new DatRomEntry
            {
                GameName = "Dragon Quest IV - Chapters of the Chosen (USA)",
                RomFileName = "Dragon Quest IV - Chapters of the Chosen (USA).nds",
                Sha1 = expectedSha1,
                DatTitle = "Nintendo - Nintendo DS"
            });

            // Name match fails for totally renamed file
            var nameMatch = index.FindMatchByName("MyCustomDump_Game123.nds");
            Assert.Null(nameMatch);

            // Hash fallback succeeds and recovers canonical title
            var hashMatch = index.FindMatchByHash(expectedSha1, null);
            Assert.NotNull(hashMatch);
            Assert.Equal("Dragon Quest IV - Chapters of the Chosen (USA)", hashMatch!.GameName);
        }

        [Fact]
        public void AppConfig_WindowPlacementProperties_Roundtrip()
        {
            var cfg = new AppConfig
            {
                WindowLeft = 120.5,
                WindowTop = 85.0,
                WindowWidth = 1450.0,
                WindowHeight = 920.0,
                IsWindowMaximized = false
            };

            string json = System.Text.Json.JsonSerializer.Serialize(cfg);
            var deserialized = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json);

            Assert.NotNull(deserialized);
            Assert.Equal(120.5, deserialized!.WindowLeft);
            Assert.Equal(85.0, deserialized.WindowTop);
            Assert.Equal(1450.0, deserialized.WindowWidth);
            Assert.Equal(920.0, deserialized.WindowHeight);
            Assert.False(deserialized.IsWindowMaximized);
        }

        [Fact]
        public void AppConfig_GbcConsoleRule_IncludesGbExtension()
        {
            var collections = RomLauncherGeneratorConfig.CreateStandardTemplates(@"D:\Roms", @"C:\RetroArch\cores", "retroarch.exe");
            var gbc = collections.FirstOrDefault(c => c.ConsoleName == "GBC");
            Assert.NotNull(gbc);
            Assert.Contains(".gb", gbc!.ExtensionsCsv);
            Assert.Contains(".gbc", gbc.ExtensionsCsv);
        }

        [Fact]
        public void HashSidecarService_ResolveSevenZipExecutable_ReturnsValidExecutable()
        {
            string resolved = HashSidecarService.ResolveSevenZipExecutable();
            Assert.False(string.IsNullOrWhiteSpace(resolved));
            Assert.True(System.IO.File.Exists(resolved));
        }

        [Fact]
        public void ProcessRunnerService_RedactSensitiveCommandLine_MasksSessionSecrets()
        {
            string raw = "\"C:\\Tools\\hbd.exe\" -s \"my_super_secret_session_cookie_123\" --library-path \"D:\\Humble\"";
            string redacted = ProcessRunnerService.RedactSensitiveCommandLine(raw);

            Assert.DoesNotContain("my_super_secret_session_cookie_123", redacted);
            Assert.Contains("***REDACTED***", redacted);
            Assert.Contains("--library-path \"D:\\Humble\"", redacted);
        }

        [Fact]
        public void CollectionManifestModule_GenerateHtmlReport_EscapesHtmlSpecialCharacters()
        {
            var records = new List<Housekeeping.Services.Modules.ManifestRecordItem>
            {
                new()
                {
                    LocationId = "NAS & Local <1>",
                    FileName = "Sonic & Knuckles <script>alert(1)</script>.7z",
                    Category = "Game / Archive",
                    DatStatus = "Match",
                    DatMatchName = "Sonic & Knuckles (World)"
                }
            };

            string html = Housekeeping.Services.Modules.CollectionManifestModule.GenerateHtmlReport(records, "Audit <Test> & Report");

            Assert.DoesNotContain("<script>alert(1)</script>", html);
            Assert.Contains("Sonic &amp; Knuckles &lt;script&gt;alert(1)&lt;/script&gt;.7z", html);
            Assert.Contains("Audit &lt;Test&gt; &amp; Report", html);
        }

        [Fact]
        public void DatDownloadService_IsValidDatPayload_RejectsHtmlAndAcceptsValidDat()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "hk_dat_valid_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string htmlFile = Path.Combine(tempDir, "error.dat");
                File.WriteAllText(htmlFile, "<!DOCTYPE html><html><body>Rate limit exceeded</body></html>");
                Assert.False(DatDownloadService.IsValidDatPayload(htmlFile));

                string validDat = Path.Combine(tempDir, "valid.dat");
                File.WriteAllText(validDat, "<?xml version=\"1.0\"?><datafile><header><name>SNES</name></header></datafile>");
                Assert.True(DatDownloadService.IsValidDatPayload(validDat));
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ConfigService_AtomicWriteAllText_WritesAndReplacesCleanly()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "hk_atomic_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string target = Path.Combine(tempDir, "settings.json");
                ConfigService.AtomicWriteAllText(target, "{\"ConfigVersion\": 1}");
                Assert.Equal("{\"ConfigVersion\": 1}", File.ReadAllText(target));

                ConfigService.AtomicWriteAllText(target, "{\"ConfigVersion\": 3}");
                Assert.Equal("{\"ConfigVersion\": 3}", File.ReadAllText(target));
                Assert.False(File.Exists(target + ".tmp"));
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task RomManifestCsvService_IncrementalCache_ReusesUnchangedHashesOnSubsequentRun()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "hk_manifest_cache_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string archivePath = Path.Combine(tempDir, "Super Mario World (USA).7z");
                await File.WriteAllBytesAsync(archivePath, new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C });

                string csvPath = Path.Combine(tempDir, "collection_manifest.csv");
                var seededEntry = new RomManifestItem
                {
                    GameTitle = "Super Mario World (USA)",
                    ProperName = "Super Mario World (USA)",
                    ArchiveFileName = "Super Mario World (USA).7z",
                    RomOrFolderName = "Super Mario World (USA).sfc",
                    VerifiedLocalHash = "CACHED_SHA1_11223344556677889900AABBCCDDEEFF",
                    MatchStatus = "MATCH",
                    Notes = "Verified via Seeded Cache"
                };
                RomManifestCsvService.WriteManifestToCsv(csvPath, new List<RomManifestItem> { seededEntry });

                var pastUtc = DateTime.UtcNow.AddMinutes(-5);
                File.SetLastWriteTimeUtc(archivePath, pastUtc);
                File.SetLastWriteTimeUtc(csvPath, DateTime.UtcNow);

                var cfg = new RomLauncherGeneratorConfig
                {
                    EnableCollectionManifestCsv = true,
                    ManifestCsvFileName = "collection_manifest.csv",
                    DeepInspectAndHashInnerRoms = false,
                    TrustExistingHashFiles = true
                };

                await RomManifestCsvService.GenerateOrUpdateManifestAsync(
                    tempDir,
                    "SNES",
                    cfg,
                    datIndex: new DatIndex(),
                    log: (_, _) => { });

                string updatedCsv = await File.ReadAllTextAsync(csvPath);
                Assert.Contains("CACHED_SHA1_11223344556677889900AABBCCDDEEFF", updatedCsv);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void NoIntroRedumpDatParser_SpanLevenshteinAndLengthFilter_MatchesAccurately()
        {
            double exactSim = DatTitleNormalizer.CalculateSimilarity("supermarioworld", "supermarioworld");
            Assert.Equal(1.0, exactSim, 4);

            double nearSim = DatTitleNormalizer.CalculateSimilarity("legendofzeldaalinktothepast", "legendofzeldaalinktothepast!");
            Assert.True(nearSim > 0.90);

            double diffSim = DatTitleNormalizer.CalculateSimilarity("short", "verylongcompletelydifferentgametitle");
            Assert.True(diffSim < 0.30);
        }

        [Fact]
        public async Task ProcessRunnerService_ShellBuiltIn_ExecutesViaCmdWithoutWin32Exception()
        {
            var runner = new ProcessRunnerService((_, _) => { });
            var res = await runner.RunCommandAsync(
                "echo HousekeepingBuiltInTest",
                useExternalCmdWindow: false,
                workingDirectory: null,
                belowNormalPriority: false,
                logOutputToTerminal: false,
                ct: default);

            Assert.Equal(0, res.ExitCode);
            Assert.Contains(res.StdOutLines, l => l.Contains("HousekeepingBuiltInTest"));
        }
        [Fact]
        public void SchedulerService_GetGlobalNextScheduleStatus_SelectsEarliestUpcomingQueue()
        {
            var cfg = AppConfig.CreateDefaults();
            var scheduler = new SchedulerAndNotificationService(() => cfg, (_, _) => { }, (_, _, _) => { });

            var q1 = new NamedJobQueue
            {
                Id = "q1",
                Name = "Nightly Batch",
                EnableSchedule = true,
                ScheduleMode = MasterScheduleMode.IntervalCadence,
                ScheduleIntervalMinutes = 60,
                LastRunUtc = DateTime.UtcNow.AddMinutes(-10) // Next run in 50m
            };

            var q2 = new NamedJobQueue
            {
                Id = "q2",
                Name = "Fast Hourly",
                EnableSchedule = true,
                ScheduleMode = MasterScheduleMode.IntervalCadence,
                ScheduleIntervalMinutes = 15,
                LastRunUtc = DateTime.UtcNow.AddMinutes(-5) // Next run in 10m
            };

            var q3 = new NamedJobQueue
            {
                Id = "q3",
                Name = "Manual Queue",
                EnableSchedule = false
            };

            cfg.NamedQueues.Clear();
            cfg.NamedQueues.Add(q1);
            cfg.NamedQueues.Add(q2);
            cfg.NamedQueues.Add(q3);

            // q3 is currently active in the UI
            cfg.SetActiveQueue("q3");

            var (nextRunUtc, statusText, queueName) = scheduler.GetGlobalNextScheduleStatus(cfg);

            Assert.NotNull(nextRunUtc);
            Assert.Equal("Fast Hourly", queueName);
            Assert.Contains("Fast Hourly", statusText);
            Assert.Contains("in 10m", statusText);
        }

        [Fact]
        public void SchedulerService_GetNextOccurrenceUtc_AccuratelyCalculatesAllModes()
        {
            var scheduler = new SchedulerAndNotificationService(() => new AppConfig(), (_, _) => { }, (_, _, _) => { });
            var baseUtc = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

            // Mode 1: Interval Cadence
            var qInterval = new NamedJobQueue
            {
                EnableSchedule = true,
                ScheduleMode = MasterScheduleMode.IntervalCadence,
                ScheduleIntervalMinutes = 30,
                LastRunUtc = baseUtc.AddMinutes(-10)
            };
            var nextInterval = scheduler.GetNextOccurrenceUtc(qInterval, baseUtc);
            Assert.NotNull(nextInterval);
            Assert.Equal(baseUtc.AddMinutes(20), nextInterval.Value);

            // Mode 2: Daily Time
            var localNow = baseUtc.ToLocalTime();
            var targetTime = localNow.TimeOfDay.Add(TimeSpan.FromHours(2));
            var qDaily = new NamedJobQueue
            {
                EnableSchedule = true,
                ScheduleMode = MasterScheduleMode.DailyTime,
                ScheduleDailyTime = string.Format("{0:D2}:{1:D2}", targetTime.Hours, targetTime.Minutes),
                ScheduleDays = new() { localNow.DayOfWeek }
            };
            var nextDaily = scheduler.GetNextOccurrenceUtc(qDaily, baseUtc);
            Assert.NotNull(nextDaily);
            Assert.True(nextDaily.Value > baseUtc);

            // Mode 3: Specific Times (multiple daily slots)
            var t1 = new TimeSpan(Math.Max(0, localNow.TimeOfDay.Hours - 1), 0, 0);
            var t2 = new TimeSpan((localNow.TimeOfDay.Hours + 1) % 24, 15, 0);
            var t3 = new TimeSpan((localNow.TimeOfDay.Hours + 3) % 24, 45, 0);
            var qSpecific = new NamedJobQueue
            {
                EnableSchedule = true,
                ScheduleMode = MasterScheduleMode.SpecificTimes,
                ScheduleSpecificTimesCsv = $"{t1:hh\\:mm}, {t2:hh\\:mm}, {t3:hh\\:mm}",
                ScheduleDays = Enum.GetValues<DayOfWeek>().ToList()
            };
            var nextSpecific = scheduler.GetNextOccurrenceUtc(qSpecific, baseUtc);
            Assert.NotNull(nextSpecific);
            Assert.True(nextSpecific.Value > baseUtc);
        }

        [Fact]
        public void SchedulerService_AllQueuesDisabled_ReturnsScheduleDisabled_AndSupportsLegacyMasterFallback()
        {
            var cfg = AppConfig.CreateDefaults();
            cfg.EnableMasterSchedule = false;
            foreach (var q in cfg.NamedQueues)
                q.EnableSchedule = false;

            var scheduler = new SchedulerAndNotificationService(() => cfg, (_, _) => { }, (_, _, _) => { });
            var (nextUtc, statusText, queueName) = scheduler.GetGlobalNextScheduleStatus(cfg);

            Assert.Null(nextUtc);
            Assert.Null(queueName);
            Assert.Equal("Schedule: Disabled", statusText);

            // Enable legacy MasterSchedule fallback and verify global status picks it up
            var baseUtc = new DateTime(2026, 10, 31, 14, 0, 0, DateTimeKind.Utc);
            cfg.EnableMasterSchedule = true;
            cfg.MasterScheduleMode = MasterScheduleMode.IntervalCadence;
            cfg.MasterScheduleIntervalMinutes = 45;
            cfg.LastMasterScheduleRunUtc = baseUtc.AddMinutes(-15);

            var (mNext, mStatus, mName) = scheduler.GetGlobalNextScheduleStatus(cfg, baseUtc);
            Assert.NotNull(mNext);
            Assert.Equal(baseUtc.AddMinutes(30), mNext.Value);
            Assert.Equal("Master Schedule", mName);
            Assert.Contains("in 30m (Master Schedule)", mStatus);
        }

        [Fact]
        public void SchedulerService_DeferredByProcessGuard_DoesNotConsumeScheduleSlot()
        {
            var cfg = AppConfig.CreateDefaults();
            var scheduler = new SchedulerAndNotificationService(() => cfg, (_, _) => { }, (_, _, _) => { });

            // 1. DailyTime deferred during 2.5m window, then guarded process exits 25 minutes later
            var dueLocal = new DateTime(2026, 10, 31, 3, 0, 15, DateTimeKind.Local);
            var qDaily = new NamedJobQueue
            {
                Id = "q_daily_guard",
                Name = "Nightly ROM Audit",
                EnableSchedule = true,
                ScheduleMode = MasterScheduleMode.DailyTime,
                ScheduleDailyTime = "03:00",
                ScheduleDays = new() { dueLocal.DayOfWeek }
            };

            Assert.False(scheduler.ShouldTriggerQueueNow(qDaily, dueLocal, isDeferredByGuard: true));
            Assert.Null(qDaily.LastRunUtc);

            // 25 minutes later (outside the normal 2.5m window), RetroArch closes -> deferred slot fires ONCE
            var afterGameExitLocal = dueLocal.AddMinutes(25);
            Assert.True(scheduler.ShouldTriggerQueueNow(qDaily, afterGameExitLocal, isDeferredByGuard: false));
            Assert.NotNull(qDaily.LastRunUtc);

            // Subsequent tick does not double-fire
            Assert.False(scheduler.ShouldTriggerQueueNow(qDaily, afterGameExitLocal.AddSeconds(2), isDeferredByGuard: false));

            // 2. IntervalCadence deferred does not advance LastRunUtc until guard clears
            var startUtc = new DateTime(2026, 10, 31, 10, 0, 0, DateTimeKind.Utc);
            var qInterval = new NamedJobQueue
            {
                Id = "q_int_guard",
                Name = "Hourly Cloud Sync",
                EnableSchedule = true,
                ScheduleMode = MasterScheduleMode.IntervalCadence,
                ScheduleIntervalMinutes = 30,
                LastRunUtc = startUtc,
                ScheduleDays = Enum.GetValues<DayOfWeek>().ToList()
            };

            var dueIntervalUtc = startUtc.AddMinutes(35);
            Assert.False(scheduler.ShouldTriggerQueueNow(qInterval, dueIntervalUtc, isDeferredByGuard: true));
            Assert.Equal(startUtc, qInterval.LastRunUtc);

            Assert.True(scheduler.ShouldTriggerQueueNow(qInterval, dueIntervalUtc.AddMinutes(5), isDeferredByGuard: false));
            Assert.Equal(dueIntervalUtc.AddMinutes(5), qInterval.LastRunUtc);
        }

        [Fact]
        public async Task HashSidecar_DetectsLegacyOuterArchiveSidecar_AndHealsToInnerRomHash()
        {
            string sevenZip = HashSidecarService.ResolveSevenZipExecutable();
            if (!File.Exists(sevenZip))
                return;

            string tempRoot = Path.Combine(Path.GetTempPath(), "hk_test_legacy_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                // Create inner uncompressed .gb ROM (64 KB deterministic payload)
                string romName = "Pokemon Test (USA) (PR).gb";
                string rawRomPath = Path.Combine(tempRoot, romName);
                byte[] romBytes = new byte[65536];
                for (int i = 0; i < romBytes.Length; i++)
                    romBytes[i] = (byte)((i * 31 + 7) & 0xFF);
                await File.WriteAllBytesAsync(rawRomPath, romBytes);

                var expectedInnerHash = await HashSidecarService.ComputeFileHashesAsync(rawRomPath);

                // Compress into .7z and delete raw .gb so only archive remains
                string archivePath = Path.Combine(tempRoot, "Pokemon Test (USA) (PR).7z");
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = sevenZip,
                    Arguments = $"a \"{archivePath}\" \"{rawRomPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var p = System.Diagnostics.Process.Start(psi)!)
                {
                    await p.WaitForExitAsync();
                    Assert.Equal(0, p.ExitCode);
                }
                File.Delete(rawRomPath);

                // Create poisoned legacy outer-archive .7z.hash (matching exact RC1/old production format)
                var outerHash = await HashSidecarService.ComputeFileHashesAsync(archivePath);
                string legacySidecarPath = archivePath + ".hash";
                string legacyContent =
                    "# Housekeeping 2.0 Checksum Sidecar\r\n" +
                    "# Title: Pokemon Test (USA) (PR)\r\n" +
                    $"FILE: Pokemon Test (USA) (PR).7z | SIZE: {outerHash.Size} | CRC32:  | MD5:  | SHA1: {outerHash.Sha1}\r\n";
                await File.WriteAllTextAsync(legacySidecarPath, legacyContent);

                // Also seed a poisoned legacy collection_manifest.csv row
                string csvPath = Path.Combine(tempRoot, "collection_manifest.csv");
                var fiArch = new FileInfo(archivePath);
                string poisonedCsv =
                    "Game Title,Proper Name (Canonical),Archive File Name,ROM / Folder Name,Verified Local Hash,DAT Expected Hash,Match Status,File Size (Bytes),Last Modified (UTC),Notes\r\n" +
                    $"Pokemon Test (USA) (PR),N/A,Pokemon Test (USA) (PR).7z,Pokemon Test (USA) (PR).7z,{outerHash.Sha1},N/A,NA,{fiArch.Length},{fiArch.LastWriteTimeUtc:O},\r\n";
                await File.WriteAllTextAsync(csvPath, poisonedCsv);

                // 1. Direct verification detects LegacyOuterArchiveHash
                var preVerify = await HashSidecarService.VerifySidecarForTargetAsync(
                    sevenZip,
                    archivePath,
                    expectInnerRom: true,
                    deepCryptographicVerify: false);
                Assert.Equal(SidecarVerificationStatus.LegacyOuterArchiveHash, preVerify.Status);

                // 2. Running RomManifestCsvService invalidates both legacy CSV row and legacy .7z.hash, heals to inner .gb hash, packages inside .7z, and removes loose .7z.hash
                var romCfg = new RomLauncherGeneratorConfig
                {
                    EnableCollectionManifestCsv = true,
                    ManifestCsvFileName = "collection_manifest.csv",
                    DeepInspectAndHashInnerRoms = true,
                    TrustExistingHashFiles = true,
                    ForceRehashExistingManifestEntries = false,
                    HashFilePlacement = HashSidecarMode.PackageInsideArchive,
                    TempExtractionScratchPath = Path.Combine(tempRoot, "scratch")
                };

                await RomManifestCsvService.GenerateOrUpdateManifestAsync(
                    tempRoot,
                    "GameBoy",
                    romCfg,
                    new DatIndex(),
                    (_, _) => { },
                    CancellationToken.None);

                Assert.False(File.Exists(legacySidecarPath));

                var postVerify = await HashSidecarService.VerifySidecarForTargetAsync(
                    sevenZip,
                    archivePath,
                    expectInnerRom: true,
                    deepCryptographicVerify: true);

                Assert.Equal(SidecarVerificationStatus.Valid, postVerify.Status);
                Assert.Equal(SidecarLocation.InternalArchive, postVerify.Location);
                Assert.NotNull(postVerify.SidecarHash);
                Assert.Equal(romName, postVerify.SidecarHash!.FileName);
                Assert.Equal(expectedInnerHash.Size, postVerify.SidecarHash.Size);
                Assert.Equal(expectedInnerHash.Sha1, postVerify.SidecarHash.Sha1);
                Assert.Equal(expectedInnerHash.Crc32, postVerify.SidecarHash.Crc32);
                Assert.Equal(expectedInnerHash.Md5, postVerify.SidecarHash.Md5);

                string healedCsv = await File.ReadAllTextAsync(csvPath);
                Assert.Contains(romName, healedCsv);
                Assert.Contains(expectedInnerHash.Sha1, healedCsv);
                Assert.DoesNotContain(outerHash.Sha1, healedCsv);
            }
            finally
            {
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch (System.Exception __suppressedEx5) { System.Diagnostics.Debug.WriteLine($"[HousekeepingTests] Non-fatal error ignored: {__suppressedEx5.Message}"); }
            }
        }

        [Fact]
        public async Task HashSidecar_VerifyFastAndDeep_DetectsCorruptChecksumMismatch_AndHeals()
        {
            string sevenZip = HashSidecarService.ResolveSevenZipExecutable();
            if (!File.Exists(sevenZip))
                return;

            string tempRoot = Path.Combine(Path.GetTempPath(), "hk_test_verify_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                string romName = "Chrono Trigger (USA).sfc";
                string rawRomPath = Path.Combine(tempRoot, romName);
                byte[] romBytes = new byte[32768];
                for (int i = 0; i < romBytes.Length; i++)
                    romBytes[i] = (byte)((i * 17 + 3) & 0xFF);
                await File.WriteAllBytesAsync(rawRomPath, romBytes);

                var trueInner = await HashSidecarService.ComputeFileHashesAsync(rawRomPath);
                string archivePath = Path.Combine(tempRoot, "Chrono Trigger (USA).7z");
                using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = sevenZip,
                    Arguments = $"a \"{archivePath}\" \"{rawRomPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                })!)
                {
                    await p.WaitForExitAsync();
                    Assert.Equal(0, p.ExitCode);
                }
                File.Delete(rawRomPath);

                string sidecarPath = archivePath + ".hash";

                // Case A: Wrong CRC32 caught instantly by Fast 7z Header check
                var badCrc = new FileHashResult
                {
                    FileName = romName,
                    Size = trueInner.Size,
                    Crc32 = "DEADBEEF",
                    Md5 = trueInner.Md5,
                    Sha1 = trueInner.Sha1
                };
                await File.WriteAllTextAsync(sidecarPath, HashSidecarService.GenerateSidecarContentForFiles("Chrono Trigger (USA)", new[] { badCrc }));

                var fastCrcRes = await HashSidecarService.VerifySidecarForTargetAsync(sevenZip, archivePath, expectInnerRom: true, deepCryptographicVerify: false);
                Assert.Equal(SidecarVerificationStatus.HeaderMismatch, fastCrcRes.Status);

                // Case B: Matching FILE + SIZE + 7z Header CRC32, but corrupted SHA1 -> passes Fast Header, caught by Deep Cryptographic Verify
                var badSha1 = new FileHashResult
                {
                    FileName = romName,
                    Size = trueInner.Size,
                    Crc32 = trueInner.Crc32,
                    Md5 = trueInner.Md5,
                    Sha1 = "0000000000000000000000000000000000000000"
                };
                await File.WriteAllTextAsync(sidecarPath, HashSidecarService.GenerateSidecarContentForFiles("Chrono Trigger (USA)", new[] { badSha1 }));

                var fastPassRes = await HashSidecarService.VerifySidecarForTargetAsync(sevenZip, archivePath, expectInnerRom: true, deepCryptographicVerify: false);
                Assert.Equal(SidecarVerificationStatus.Valid, fastPassRes.Status);

                var deepCatchRes = await HashSidecarService.VerifySidecarForTargetAsync(sevenZip, archivePath, expectInnerRom: true, deepCryptographicVerify: true);
                Assert.Equal(SidecarVerificationStatus.CryptographicMismatch, deepCatchRes.Status);

                // Batch Verify & Heal heals the cryptographic mismatch
                var report = await HashSidecarService.VerifyAndHealDirectorySidecarsAsync(
                    new[] { tempRoot },
                    sevenZip,
                    expectInnerRom: true,
                    targetPlacement: HashSidecarMode.SidecarBeside,
                    structureMode: AuditStructureMode.FilesOnly,
                    verifyMode: SidecarVerifyMode.DeepCryptographicAndHeal);

                Assert.Equal(1, report.TotalInspected);
                Assert.Equal(1, report.CryptographicMismatchCount);
                Assert.Equal(1, report.HealedCount);

                var postDeepRes = await HashSidecarService.VerifySidecarForTargetAsync(sevenZip, archivePath, expectInnerRom: true, deepCryptographicVerify: true);
                Assert.Equal(SidecarVerificationStatus.Valid, postDeepRes.Status);
                Assert.Equal(trueInner.Sha1, postDeepRes.SidecarHash!.Sha1);
            }
            finally
            {
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch (System.Exception __suppressedEx6) { System.Diagnostics.Debug.WriteLine($"[HousekeepingTests] Non-fatal error ignored: {__suppressedEx6.Message}"); }
            }
        }

        [Fact]
        public async Task RomManifest_ForceRehash_IgnoresExistingSidecarAndRecomputes()
        {
            string sevenZip = HashSidecarService.ResolveSevenZipExecutable();
            if (!File.Exists(sevenZip))
                return;

            string tempRoot = Path.Combine(Path.GetTempPath(), "hk_test_forcerehash_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                string romName = "Super Mario Bros. (World).nes";
                string rawRomPath = Path.Combine(tempRoot, romName);
                byte[] romBytes = new byte[40976];
                for (int i = 0; i < romBytes.Length; i++)
                    romBytes[i] = (byte)((i * 13 + 5) & 0xFF);
                await File.WriteAllBytesAsync(rawRomPath, romBytes);

                var trueInner = await HashSidecarService.ComputeFileHashesAsync(rawRomPath);
                string archivePath = Path.Combine(tempRoot, "Super Mario Bros. (World).7z");
                using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = sevenZip,
                    Arguments = $"a \"{archivePath}\" \"{rawRomPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                })!)
                {
                    await p.WaitForExitAsync();
                    Assert.Equal(0, p.ExitCode);
                }
                File.Delete(rawRomPath);

                // Seed a sidecar that has matching header fields (FILE, SIZE, CRC32) but sentinel fake SHA1
                string sentinelSha1 = "1111111111111111111111111111111111111111";
                var fakeHeaderValidSidecar = new FileHashResult
                {
                    FileName = romName,
                    Size = trueInner.Size,
                    Crc32 = trueInner.Crc32,
                    Md5 = trueInner.Md5,
                    Sha1 = sentinelSha1
                };
                string sidecarPath = archivePath + ".hash";
                await File.WriteAllTextAsync(sidecarPath, HashSidecarService.GenerateSidecarContentForFiles("Super Mario Bros. (World)", new[] { fakeHeaderValidSidecar }));

                var cfg = new RomLauncherGeneratorConfig
                {
                    EnableCollectionManifestCsv = true,
                    ManifestCsvFileName = "collection_manifest.csv",
                    DeepInspectAndHashInnerRoms = true,
                    TrustExistingHashFiles = true,
                    ForceRehashExistingManifestEntries = true,
                    HashFilePlacement = HashSidecarMode.SidecarBeside,
                    TempExtractionScratchPath = Path.Combine(tempRoot, "scratch")
                };

                await RomManifestCsvService.GenerateOrUpdateManifestAsync(
                    tempRoot,
                    "NES",
                    cfg,
                    new DatIndex(),
                    (_, _) => { },
                    CancellationToken.None);

                string csvContent = await File.ReadAllTextAsync(Path.Combine(tempRoot, "collection_manifest.csv"));
                Assert.Contains(trueInner.Sha1, csvContent);
                Assert.DoesNotContain(sentinelSha1, csvContent);

                Assert.True(HashSidecarService.TryReadExistingSidecar(archivePath, out var updatedSidecar));
                Assert.Equal(trueInner.Sha1, updatedSidecar!.Sha1);
            }
            finally
            {
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch (System.Exception __suppressedEx7) { System.Diagnostics.Debug.WriteLine($"[HousekeepingTests] Non-fatal error ignored: {__suppressedEx7.Message}"); }
            }
        }

        [Fact]
        public async Task HashSidecar_MultiTrackArchive_HashesAllBinsAndCue_AndMatchesMultiRomRedumpDat()
        {
            string sevenZip = HashSidecarService.ResolveSevenZipExecutable();
            if (!File.Exists(sevenZip))
                return;

            string tempRoot = Path.Combine(Path.GetTempPath(), "hk_test_multitrack_" + Guid.NewGuid().ToString("N"));
            string ps1Dir = Path.Combine(tempRoot, "PlayStation");
            string stagingFolder = Path.Combine(tempRoot, "staging", "Cool Boarders 2 (USA) (PR)");
            Directory.CreateDirectory(ps1Dir);
            Directory.CreateDirectory(stagingFolder);

            try
            {
                string cueName = "Cool Boarders 2 (USA).cue";
                string track1Name = "Cool Boarders 2 (USA) (Track 01).bin";
                string track2Name = "Cool Boarders 2 (USA) (Track 02).bin";
                string track3Name = "Cool Boarders 2 (USA) (Track 03).bin";

                string cueContent =
                    $"FILE \"{track1Name}\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n" +
                    $"FILE \"{track2Name}\" BINARY\r\n  TRACK 02 AUDIO\r\n    INDEX 01 00:00:00\r\n" +
                    $"FILE \"{track3Name}\" BINARY\r\n  TRACK 03 AUDIO\r\n    INDEX 01 00:00:00\r\n";

                string cuePath = Path.Combine(stagingFolder, cueName);
                string t1Path = Path.Combine(stagingFolder, track1Name);
                string t2Path = Path.Combine(stagingFolder, track2Name);
                string t3Path = Path.Combine(stagingFolder, track3Name);

                await File.WriteAllTextAsync(cuePath, cueContent, System.Text.Encoding.ASCII);
                byte[] b1 = new byte[49152];
                byte[] b2 = new byte[16384];
                byte[] b3 = new byte[12288];
                for (int i = 0; i < b1.Length; i++) b1[i] = (byte)((i * 23 + 11) & 0xFF);
                for (int i = 0; i < b2.Length; i++) b2[i] = (byte)((i * 19 + 5) & 0xFF);
                for (int i = 0; i < b3.Length; i++) b3[i] = (byte)((i * 29 + 3) & 0xFF);

                await File.WriteAllBytesAsync(t1Path, b1);
                await File.WriteAllBytesAsync(t2Path, b2);
                await File.WriteAllBytesAsync(t3Path, b3);

                var hCue = await HashSidecarService.ComputeFileHashesAsync(cuePath);
                var hT1 = await HashSidecarService.ComputeFileHashesAsync(t1Path);
                var hT2 = await HashSidecarService.ComputeFileHashesAsync(t2Path);
                var hT3 = await HashSidecarService.ComputeFileHashesAsync(t3Path);

                // Compress into solid .7z preserving wrapper folder (matching real collection structure)
                string archivePath = Path.Combine(ps1Dir, "Cool Boarders 2 (USA) (PR).7z");
                using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = sevenZip,
                    Arguments = $"a \"{archivePath}\" \"Cool Boarders 2 (USA) (PR)\" -y",
                    WorkingDirectory = Path.Combine(tempRoot, "staging"),
                    UseShellExecute = false,
                    CreateNoWindow = true
                })!)
                {
                    await p.WaitForExitAsync();
                    Assert.Equal(0, p.ExitCode);
                }

                // Create multi-<rom> Redump XML DAT listing .cue first, then Track 01..03 (exact Redump ordering)
                string datPath = Path.Combine(tempRoot, "Sony - PlayStation.dat");
                string datXml = $@"<?xml version=""1.0""?>
<datafile>
  <header>
    <name>Sony - PlayStation</name>
    <description>Sony - PlayStation</description>
  </header>
  <game name=""Cool Boarders 2 (USA)"">
    <description>Cool Boarders 2 (USA)</description>
    <rom name=""{cueName}"" size=""{hCue.Size}"" crc=""{hCue.Crc32}"" md5=""{hCue.Md5}"" sha1=""{hCue.Sha1}"" />
    <rom name=""{track1Name}"" size=""{hT1.Size}"" crc=""{hT1.Crc32}"" md5=""{hT1.Md5}"" sha1=""{hT1.Sha1}"" />
    <rom name=""{track2Name}"" size=""{hT2.Size}"" crc=""{hT2.Crc32}"" md5=""{hT2.Md5}"" sha1=""{hT2.Sha1}"" />
    <rom name=""{track3Name}"" size=""{hT3.Size}"" crc=""{hT3.Crc32}"" md5=""{hT3.Md5}"" sha1=""{hT3.Sha1}"" />
  </game>
</datafile>";
                await File.WriteAllTextAsync(datPath, datXml);
                var datIndex = NoIntroRedumpDatParser.ParseFile(datPath);

                // Confirm single-entry lookup prefers largest non-.cue data track (Track 01) instead of .cue or Track 03
                var primaryNameMatch = datIndex.FindMatchByName("Cool Boarders 2 (USA) (PR)");
                Assert.NotNull(primaryNameMatch);
                Assert.Equal(track1Name, primaryNameMatch!.RomFileName);
                Assert.Equal(4, datIndex.GetRomsForGame("Cool Boarders 2 (USA)").Count);

                var cfg = new RomLauncherGeneratorConfig
                {
                    EnableCollectionManifestCsv = true,
                    ManifestCsvFileName = "collection_manifest.csv",
                    DeepInspectAndHashInnerRoms = true,
                    TrustExistingHashFiles = true,
                    HashFilePlacement = HashSidecarMode.PackageInsideArchive,
                    TempExtractionScratchPath = Path.Combine(tempRoot, "scratch")
                };

                await RomManifestCsvService.GenerateOrUpdateManifestAsync(
                    ps1Dir,
                    "PlayStation",
                    cfg,
                    datIndex,
                    (_, _) => { },
                    CancellationToken.None);

                // Zero leftover external .hash files
                Assert.Empty(HashSidecarService.GetExistingExternalSidecarPaths(archivePath));

                // Deep verify internal .hash confirms all 4 files (3 .bin + 1 .cue) are recorded and byte-for-byte valid
                var verify = await HashSidecarService.VerifySidecarForTargetAsync(
                    sevenZip,
                    archivePath,
                    expectInnerRom: true,
                    deepCryptographicVerify: true,
                    scratchDir: cfg.TempExtractionScratchPath);

                Assert.Equal(SidecarVerificationStatus.Valid, verify.Status);
                Assert.Equal(SidecarLocation.InternalArchive, verify.Location);
                Assert.Equal(ReleaseTopology.MultiFileRomSet, verify.Topology);
                Assert.Equal(4, verify.SidecarFiles.Count);
                Assert.Contains(verify.SidecarFiles, f => f.FileName == cueName && f.Sha1 == hCue.Sha1 && f.Md5 == hCue.Md5);
                Assert.Contains(verify.SidecarFiles, f => f.FileName == track1Name && f.Sha1 == hT1.Sha1 && f.Md5 == hT1.Md5);
                Assert.Contains(verify.SidecarFiles, f => f.FileName == track2Name && f.Sha1 == hT2.Sha1 && f.Md5 == hT2.Md5);
                Assert.Contains(verify.SidecarFiles, f => f.FileName == track3Name && f.Sha1 == hT3.Sha1 && f.Md5 == hT3.Md5);

                // Manifest CSV has 1 row for the game with full 4/4 multi-file MATCH
                var csvItems = RomManifestCsvService.LoadExistingManifest(Path.Combine(ps1Dir, "collection_manifest.csv"));
                Assert.Single(csvItems);
                var row = csvItems.Values.Single();
                Assert.Equal("Cool Boarders 2 (USA)", row.ProperName);
                Assert.Equal("MATCH", row.MatchStatus);
                Assert.Equal(hT1.Sha1, row.VerifiedLocalHash);
                Assert.Contains("Verified 4/4 files", row.Notes);
            }
            finally
            {
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch (System.Exception __suppressedEx8) { System.Diagnostics.Debug.WriteLine($"[HousekeepingTests] Non-fatal error ignored: {__suppressedEx8.Message}"); }
            }
        }

        [Fact]
        public async Task HashSidecar_IncompleteOneFileSidecarInMultiDiscArchive_DetectsIncompleteAndHealsAllDiscs()
        {
            string sevenZip = HashSidecarService.ResolveSevenZipExecutable();
            if (!File.Exists(sevenZip))
                return;

            string tempRoot = Path.Combine(Path.GetTempPath(), "hk_test_multidisc_" + Guid.NewGuid().ToString("N"));
            string gamesDir = Path.Combine(tempRoot, "Games");
            string staging = Path.Combine(tempRoot, "staging");
            Directory.CreateDirectory(gamesDir);
            Directory.CreateDirectory(staging);

            try
            {
                // 1. Seed an already-valid 1-file RC3 .7z archive (must remain 100% Valid and untouched!)
                string singleIsoPath = Path.Combine(staging, "Final Fantasy X (USA).iso");
                byte[] singleBytes = new byte[20480];
                for (int i = 0; i < singleBytes.Length; i++) singleBytes[i] = (byte)((i * 17 + 9) & 0xFF);
                await File.WriteAllBytesAsync(singleIsoPath, singleBytes);
                var hSingle = await HashSidecarService.ComputeFileHashesAsync(singleIsoPath);

                string singleSidecarStaging = Path.Combine(staging, "Final Fantasy X (USA).hash");
                await File.WriteAllTextAsync(
                    singleSidecarStaging,
                    HashSidecarService.GenerateSidecarContentForFiles("Final Fantasy X (USA)", new[] { hSingle }));

                string singleArchivePath = Path.Combine(gamesDir, "Final Fantasy X (USA).7z");
                using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = sevenZip,
                    Arguments = $"a \"{singleArchivePath}\" \"{singleIsoPath}\" \"{singleSidecarStaging}\" -y",
                    UseShellExecute = false,
                    CreateNoWindow = true
                })!)
                {
                    await p.WaitForExitAsync();
                    Assert.Equal(0, p.ExitCode);
                }

                // 2. Seed a 5-file multi-disc .7z archive containing an RC3-style incomplete 1-file .hash (only Disc 2.bin) + stray external .hash
                string ccWrapper = Path.Combine(staging, "Chrono Cross (USA) (PR)");
                Directory.CreateDirectory(ccWrapper);

                string d1Bin = Path.Combine(ccWrapper, "Chrono Cross (USA) (Disc 1).bin");
                string d1Cue = Path.Combine(ccWrapper, "Chrono Cross (USA) (Disc 1).cue");
                string d2Bin = Path.Combine(ccWrapper, "Chrono Cross (USA) (Disc 2).bin");
                string d2Cue = Path.Combine(ccWrapper, "Chrono Cross (USA) (Disc 2).cue");
                string m3u = Path.Combine(ccWrapper, "Chrono Cross (USA) (PR).m3u");

                byte[] bD1 = new byte[24576];
                byte[] bD2 = new byte[32768];
                for (int i = 0; i < bD1.Length; i++) bD1[i] = (byte)((i * 7 + 1) & 0xFF);
                for (int i = 0; i < bD2.Length; i++) bD2[i] = (byte)((i * 13 + 4) & 0xFF);

                await File.WriteAllBytesAsync(d1Bin, bD1);
                await File.WriteAllTextAsync(d1Cue, "FILE \"Chrono Cross (USA) (Disc 1).bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n");
                await File.WriteAllBytesAsync(d2Bin, bD2);
                await File.WriteAllTextAsync(d2Cue, "FILE \"Chrono Cross (USA) (Disc 2).bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n");
                await File.WriteAllTextAsync(m3u, "Chrono Cross (USA) (Disc 1).cue\r\nChrono Cross (USA) (Disc 2).cue\r\n");

                var hD1Bin = await HashSidecarService.ComputeFileHashesAsync(d1Bin);
                var hD1Cue = await HashSidecarService.ComputeFileHashesAsync(d1Cue);
                var hD2Bin = await HashSidecarService.ComputeFileHashesAsync(d2Bin);
                var hD2Cue = await HashSidecarService.ComputeFileHashesAsync(d2Cue);
                var hM3u = await HashSidecarService.ComputeFileHashesAsync(m3u);

                string ccArchivePath = Path.Combine(gamesDir, "Chrono Cross (USA) (PR).7z");
                using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = sevenZip,
                    Arguments = $"a \"{ccArchivePath}\" \"Chrono Cross (USA) (PR)\" -y",
                    WorkingDirectory = staging,
                    UseShellExecute = false,
                    CreateNoWindow = true
                })!)
                {
                    await p.WaitForExitAsync();
                    Assert.Equal(0, p.ExitCode);
                }

                // Inject incomplete 1-file RC3 sidecar (only Disc 2.bin) + plant a stray external .hash outside the .7z
                string rc3PartialSidecar = Path.Combine(staging, "Chrono Cross (USA) (PR).hash");
                await File.WriteAllTextAsync(
                    rc3PartialSidecar,
                    HashSidecarService.GenerateSidecarContentForFiles("Chrono Cross (USA) (PR)", new[] { hD2Bin }));
                Assert.True(await HashSidecarService.InjectSidecarIntoArchiveAsync(sevenZip, ccArchivePath, rc3PartialSidecar));

                string strayExternalHash = Path.Combine(gamesDir, "Chrono Cross (USA) (PR).hash");
                await File.WriteAllTextAsync(strayExternalHash, "# stale external hash");

                // Pre-check confirms IncompleteMultiFileSet
                var preCheck = await HashSidecarService.VerifySidecarForTargetAsync(sevenZip, ccArchivePath, expectInnerRom: true, deepCryptographicVerify: false);
                Assert.Equal(SidecarVerificationStatus.IncompleteMultiFileSet, preCheck.Status);

                // Run batch Verify & Heal across directory
                var report = await HashSidecarService.VerifyAndHealDirectorySidecarsAsync(
                    new[] { gamesDir },
                    sevenZip,
                    expectInnerRom: true,
                    targetPlacement: HashSidecarMode.PackageInsideArchive,
                    structureMode: AuditStructureMode.Auto,
                    verifyMode: SidecarVerifyMode.FastHeaderAndHeal,
                    scratchDir: Path.Combine(tempRoot, "scratch"));

                Assert.Equal(2, report.TotalInspected);
                Assert.Equal(1, report.ValidCount);
                Assert.Equal(1, report.IncompleteMultiFileCount);
                Assert.Equal(1, report.HealedCount);

                // Single-file ISO archive remained Valid with WasHealed == false (zero wasted re-hashing)
                var ffxItem = report.Items.Single(i => i.TargetPath.EndsWith("Final Fantasy X (USA).7z", StringComparison.OrdinalIgnoreCase));
                Assert.Equal(SidecarVerificationStatus.Valid, ffxItem.Status);
                Assert.False(ffxItem.WasHealed);

                // Multi-disc archive healed all 5 files inside .7z and deleted stray external .hash
                Assert.False(File.Exists(strayExternalHash));
                Assert.Empty(HashSidecarService.GetExistingExternalSidecarPaths(ccArchivePath));

                var postVerify = await HashSidecarService.VerifySidecarForTargetAsync(
                    sevenZip,
                    ccArchivePath,
                    expectInnerRom: true,
                    deepCryptographicVerify: true,
                    scratchDir: Path.Combine(tempRoot, "scratch"));

                Assert.Equal(SidecarVerificationStatus.Valid, postVerify.Status);
                Assert.Equal(SidecarLocation.InternalArchive, postVerify.Location);
                Assert.Equal(5, postVerify.SidecarFiles.Count);
                Assert.Contains(postVerify.SidecarFiles, f => f.Sha1 == hD1Bin.Sha1);
                Assert.Contains(postVerify.SidecarFiles, f => f.Sha1 == hD1Cue.Sha1);
                Assert.Contains(postVerify.SidecarFiles, f => f.Sha1 == hD2Bin.Sha1);
                Assert.Contains(postVerify.SidecarFiles, f => f.Sha1 == hD2Cue.Sha1);
                Assert.Contains(postVerify.SidecarFiles, f => f.Sha1 == hM3u.Sha1);

                // Verify multi-disc Redump DAT evaluation matches across both Disc 1 and Disc 2 + .m3u playlist
                var multiDiscDat = new DatIndex { Title = "Sony - PlayStation" };
                multiDiscDat.Add(new DatRomEntry { GameName = "Chrono Cross (USA) (Disc 1)", RomFileName = "Chrono Cross (USA) (Disc 1).cue", Size = hD1Cue.Size, Crc32 = hD1Cue.Crc32, Sha1 = hD1Cue.Sha1, DatTitle = "Sony - PlayStation" });
                multiDiscDat.Add(new DatRomEntry { GameName = "Chrono Cross (USA) (Disc 1)", RomFileName = "Chrono Cross (USA) (Disc 1).bin", Size = hD1Bin.Size, Crc32 = hD1Bin.Crc32, Sha1 = hD1Bin.Sha1, DatTitle = "Sony - PlayStation" });
                multiDiscDat.Add(new DatRomEntry { GameName = "Chrono Cross (USA) (Disc 2)", RomFileName = "Chrono Cross (USA) (Disc 2).cue", Size = hD2Cue.Size, Crc32 = hD2Cue.Crc32, Sha1 = hD2Cue.Sha1, DatTitle = "Sony - PlayStation" });
                multiDiscDat.Add(new DatRomEntry { GameName = "Chrono Cross (USA) (Disc 2)", RomFileName = "Chrono Cross (USA) (Disc 2).bin", Size = hD2Bin.Size, Crc32 = hD2Bin.Crc32, Sha1 = hD2Bin.Sha1, DatTitle = "Sony - PlayStation" });

                var eval = multiDiscDat.EvaluateMultiFileSet(postVerify.SidecarFiles, "Chrono Cross (USA) (PR)");
                Assert.True(eval.AllDataTracksVerified);
                Assert.True(eval.AllDescriptorsVerified);
                Assert.Equal(2, eval.MatchedDistinctGameCount);
                Assert.Equal(1, eval.PlaylistFileCount);
                Assert.Equal("Chrono Cross (USA) (2 Discs)", eval.PrimaryProperName);
            }
            finally
            {
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch (System.Exception __suppressedEx9) { System.Diagnostics.Debug.WriteLine($"[HousekeepingTests] Non-fatal error ignored: {__suppressedEx9.Message}"); }
            }
        }

        [Fact]
        public async Task RomManifest_UncompressedMultiTrackGameFolder_VsOrganizationalFolder()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "hk_test_folder_vs_org_" + Guid.NewGuid().ToString("N"));
            string ps1Root = Path.Combine(tempRoot, "PlayStation");
            string coolGameDir = Path.Combine(ps1Root, "CoolGame (USA)");
            string snesRoot = Path.Combine(tempRoot, "SNES");
            string snesUsaDir = Path.Combine(snesRoot, "USA");

            Directory.CreateDirectory(coolGameDir);
            Directory.CreateDirectory(snesUsaDir);

            try
            {
                // 1. Unpacked cohesive multi-track PS1 game folder + stray per-track .hash inside subfolder
                await File.WriteAllTextAsync(Path.Combine(coolGameDir, "CoolGame (USA).cue"), "FILE \"CoolGame (USA) (Track 1).bin\" BINARY\r\n");
                await File.WriteAllBytesAsync(Path.Combine(coolGameDir, "CoolGame (USA) (Track 1).bin"), new byte[] { 10, 20, 30, 40, 50 });
                await File.WriteAllBytesAsync(Path.Combine(coolGameDir, "CoolGame (USA) (Track 2).bin"), new byte[] { 60, 70, 80, 90 });
                await File.WriteAllBytesAsync(Path.Combine(coolGameDir, "CoolGame (USA) (Track 3).bin"), new byte[] { 11, 22, 33 });
                string strayInnerTrackHash = Path.Combine(coolGameDir, "CoolGame (USA) (Track 1).bin.hash");
                await File.WriteAllTextAsync(strayInnerTrackHash, "# old per-track hash");

                // 2. Organizational folder containing 2 unrelated loose SNES ROMs
                string smwPath = Path.Combine(snesUsaDir, "Super Mario World (USA).sfc");
                string actPath = Path.Combine(snesUsaDir, "ActRaiser (USA).sfc");
                await File.WriteAllBytesAsync(smwPath, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
                await File.WriteAllBytesAsync(actPath, new byte[] { 9, 10, 11, 12, 13, 14, 15, 16 });

                Assert.True(ReleaseStructureDetector.IsCohesiveMultiFileGameFolder(coolGameDir));
                Assert.False(ReleaseStructureDetector.IsCohesiveMultiFileGameFolder(snesUsaDir));

                var cfg = new RomLauncherGeneratorConfig
                {
                    EnableCollectionManifestCsv = true,
                    ManifestCsvFileName = "collection_manifest.csv",
                    HashFilePlacement = HashSidecarMode.PackageInsideArchive, // Even in PackageInsideArchive mode, unpacked folders must place .hash beside the folder
                    StructureMode = AuditStructureMode.Auto
                };

                await RomManifestCsvService.GenerateOrUpdateManifestAsync(ps1Root, "PlayStation", cfg, new DatIndex(), (_, _) => { });
                await RomManifestCsvService.GenerateOrUpdateManifestAsync(snesRoot, "SNES", cfg, new DatIndex(), (_, _) => { });

                // PS1 unpacked game folder: 1 beside .hash with 4 FILE: lines, stray inner .hash deleted, 1 CSV row
                string expectedFolderSidecar = Path.Combine(ps1Root, "CoolGame (USA).hash");
                Assert.True(File.Exists(expectedFolderSidecar));
                Assert.False(File.Exists(strayInnerTrackHash));
                Assert.Empty(Directory.GetFiles(coolGameDir, "*.hash", SearchOption.AllDirectories));

                Assert.True(HashSidecarService.TryReadExistingSidecarDocument(coolGameDir, out var folderDoc));
                Assert.NotNull(folderDoc);
                Assert.Equal(4, folderDoc!.Files.Count);

                var ps1Csv = RomManifestCsvService.LoadExistingManifest(Path.Combine(ps1Root, "collection_manifest.csv"));
                Assert.Single(ps1Csv);
                Assert.True(ps1Csv.ContainsKey("CoolGame (USA)"));

                // SNES organizational folder: 2 separate games in CSV, 2 individual .sfc.hash sidecars
                var snesCsv = RomManifestCsvService.LoadExistingManifest(Path.Combine(snesRoot, "collection_manifest.csv"));
                Assert.Equal(2, snesCsv.Count);
                Assert.True(File.Exists(smwPath + ".hash"));
                Assert.True(File.Exists(actPath + ".hash"));
            }
            finally
            {
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch (System.Exception __suppressedEx10) { System.Diagnostics.Debug.WriteLine($"[HousekeepingTests] Non-fatal error ignored: {__suppressedEx10.Message}"); }
            }
        }

        [Fact]
        public async Task HashSidecar_CompositeDump_LooseAndArchivedPs3Ps4_UsesFastCompositeManifest()
        {
            string sevenZip = HashSidecarService.ResolveSevenZipExecutable();
            if (!File.Exists(sevenZip))
                return;

            string tempRoot = Path.Combine(Path.GetTempPath(), "hk_test_composite_" + Guid.NewGuid().ToString("N"));
            string looseCollectionDir = Path.Combine(tempRoot, "PS3_Loose");
            string archiveCollectionDir = Path.Combine(tempRoot, "PS3_Archived");
            string ps4SampleDir = Path.Combine(tempRoot, "PS4_Test", "Bloodborne_CUSA00900");
            string xboxSampleDir = Path.Combine(tempRoot, "Xbox360_Test", "Halo3");

            Directory.CreateDirectory(looseCollectionDir);
            Directory.CreateDirectory(archiveCollectionDir);
            Directory.CreateDirectory(Path.Combine(ps4SampleDir, "sce_sys"));
            Directory.CreateDirectory(xboxSampleDir);

            try
            {
                // Verify expanded PS4, PS5 & Xbox 360 signatures on disk and archive paths
                await File.WriteAllBytesAsync(Path.Combine(ps4SampleDir, "sce_sys", "param.sfo"), new byte[] { 0x00, 0x50, 0x53, 0x46 });
                await File.WriteAllBytesAsync(Path.Combine(ps4SampleDir, "eboot.bin"), new byte[] { 0x4F, 0x15, 0x3D, 0x1D });
                await File.WriteAllBytesAsync(Path.Combine(xboxSampleDir, "default.xex"), new byte[] { 0x58, 0x45, 0x58, 0x32 });

                Assert.Equal(ReleaseStructureDetector.TypePs4FolderDump, ReleaseStructureDetector.DetectFolderDumpType(ps4SampleDir));
                Assert.Equal(ReleaseStructureDetector.TypeXboxFolderDump, ReleaseStructureDetector.DetectFolderDumpType(xboxSampleDir));
                Assert.Equal(
                    ReleaseTopology.CompositeFolderDump,
                    ReleaseStructureDetector.ClassifyArchiveEntries(new[] { "Wrapper/sce_sys/param.json", "Wrapper/eboot.bin" }));

                // Create a realistic PS3 folder dump tree
                string ps3LooseFolder = Path.Combine(looseCollectionDir, "AC Brotherhood (USA) (PR) [BLUS30537]");
                Directory.CreateDirectory(Path.Combine(ps3LooseFolder, "PS3_GAME", "USRDIR"));
                await File.WriteAllTextAsync(Path.Combine(ps3LooseFolder, "PS3_DISC.SFB"), "PS3_DISC_SFB_HEADER");
                await File.WriteAllBytesAsync(Path.Combine(ps3LooseFolder, "PS3_GAME", "PARAM.SFO"), new byte[] { 1, 2, 3, 4, 5, 6 });
                await File.WriteAllBytesAsync(Path.Combine(ps3LooseFolder, "PS3_GAME", "USRDIR", "EBOOT.BIN"), new byte[] { 0x7F, 0x45, 0x4C, 0x46, 9, 8, 7 });

                byte[] bigAsset = new byte[65536];
                for (int i = 0; i < bigAsset.Length; i++) bigAsset[i] = (byte)((i * 37 + 13) & 0xFF);
                await File.WriteAllBytesAsync(Path.Combine(ps3LooseFolder, "PS3_GAME", "USRDIR", "DataPC.forge"), bigAsset);

                // Pack the exact same PS3 folder tree into a solid .7z archive
                string ps3ArchivePath = Path.Combine(archiveCollectionDir, "AC Brotherhood (USA) (PR) [BLUS30537].7z");
                using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = sevenZip,
                    Arguments = $"a \"{ps3ArchivePath}\" \"AC Brotherhood (USA) (PR) [BLUS30537]\" -y",
                    WorkingDirectory = looseCollectionDir,
                    UseShellExecute = false,
                    CreateNoWindow = true
                })!)
                {
                    await p.WaitForExitAsync();
                    Assert.Equal(0, p.ExitCode);
                }

                // Run Verify & Heal on both loose PS3 folder and .7z archived PS3 folder
                var report = await HashSidecarService.VerifyAndHealDirectorySidecarsAsync(
                    new[] { looseCollectionDir, archiveCollectionDir },
                    sevenZip,
                    expectInnerRom: true,
                    targetPlacement: HashSidecarMode.PackageInsideArchive,
                    structureMode: AuditStructureMode.Auto,
                    verifyMode: SidecarVerifyMode.FastHeaderAndHeal);

                Assert.Equal(2, report.TotalInspected);
                Assert.Equal(2, report.MissingCount);
                Assert.Equal(2, report.HealedCount);

                // Loose folder gets beside .hash; .7z archive gets internal .hash with zero external .hash
                string looseFolderHashPath = ps3LooseFolder + ".hash";
                Assert.True(File.Exists(looseFolderHashPath));
                Assert.Empty(HashSidecarService.GetExistingExternalSidecarPaths(ps3ArchivePath));

                var verifyLoose = await HashSidecarService.VerifySidecarForTargetAsync(sevenZip, ps3LooseFolder, expectInnerRom: true, deepCryptographicVerify: true);
                var verifyArchive = await HashSidecarService.VerifySidecarForTargetAsync(sevenZip, ps3ArchivePath, expectInnerRom: true, deepCryptographicVerify: true);

                Assert.Equal(SidecarVerificationStatus.Valid, verifyLoose.Status);
                Assert.Equal(ReleaseTopology.CompositeFolderDump, verifyLoose.Topology);
                Assert.Equal(SidecarLocation.ExternalBeside, verifyLoose.Location);

                Assert.Equal(SidecarVerificationStatus.Valid, verifyArchive.Status);
                Assert.Equal(ReleaseTopology.CompositeFolderDump, verifyArchive.Topology);
                Assert.Equal(SidecarLocation.InternalArchive, verifyArchive.Location);

                // Loose folder CRC32 manifest and 7-Zip header CRC32 manifest produce the EXACT same COMPOSITE_SHA1 and 4 ENTRY lines!
                Assert.Equal(4, verifyLoose.SidecarFiles.Count);
                Assert.Equal(4, verifyArchive.SidecarFiles.Count);
                Assert.NotNull(verifyLoose.ParsedDocument);
                Assert.NotNull(verifyArchive.ParsedDocument);
                Assert.Equal(verifyLoose.ParsedDocument!.CompositeSha1, verifyArchive.ParsedDocument!.CompositeSha1);
                Assert.True(verifyArchive.ParsedDocument.AllEntriesHaveCrc32);
            }
            finally
            {
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch (System.Exception __suppressedEx11) { System.Diagnostics.Debug.WriteLine($"[HousekeepingTests] Non-fatal error ignored: {__suppressedEx11.Message}"); }
            }
        }

        [Fact]
        public void DatDownloadService_ResolveOnlineDatUrl_SupportsBothRedumpAndNoIntroUpstream()
        {
            // Disc systems map to Redump
            string? ps2Url = DatDownloadService.ResolveOnlineDatUrl("PS2");
            Assert.NotNull(ps2Url);
            Assert.Contains("redump.org/datfile/ps2/", ps2Url);

            string? gcUrl = DatDownloadService.ResolveOnlineDatUrl("GameCube");
            Assert.NotNull(gcUrl);
            Assert.Contains("redump.org/datfile/gc/", gcUrl);

            // Cartridge systems map to Libretro upstream No-Intro repository
            string? nesUrl = DatDownloadService.ResolveOnlineDatUrl("NES");
            Assert.NotNull(nesUrl);
            Assert.Contains("Nintendo%20-%20Nintendo%20Entertainment%20System.dat", nesUrl);

            string? snesUrl = DatDownloadService.ResolveOnlineDatUrl("SNES");
            Assert.NotNull(snesUrl);
            Assert.Contains("Nintendo%20-%20Super%20Nintendo%20Entertainment%20System.dat", snesUrl);

            string? genesisUrl = DatDownloadService.ResolveOnlineDatUrl("Genesis");
            Assert.NotNull(genesisUrl);
            Assert.Contains("Sega%20-%20Mega%20Drive%20-%20Genesis.dat", genesisUrl);

            string? gbaUrl = DatDownloadService.ResolveOnlineDatUrl("GBA");
            Assert.NotNull(gbaUrl);
            Assert.Contains("Nintendo%20-%20Game%20Boy%20Advance.dat", gbaUrl);
        }

        // ==================== v2.8.5-rc.5 TESTS (MULTI-SYNC, CRC32 MANIFEST, FAST HEADER AUDIT, ARCHIVE TEMPLATES & CLEANUP) ==================== //

        [Fact]
        public void RomLauncherConfig_MultiRowSync_FiresAllRowsAndSanitizesPoisonedPreviewOverrides()
        {
            var cfg = new RomLauncherGeneratorConfig
            {
                EnablePrePostLaunchSyncCommands = true,
                UseCustomPrePostLaunchCommands = false,
                // Simulate legacy poisoned preview string written from row 1 only
                PreLaunchCommand = "\"rclone\" bisync \"EmuDrive:Saves\" \"D:\\Emu\\Saves\" --resilient -P",
                PostLaunchCommand = "\"rclone\" bisync \"D:\\Emu\\Saves\" \"EmuDrive:Saves\" --resilient -P"
            };

            cfg.DefaultSyncPaths.Add(new GameSyncPathItem
            {
                Enabled = true,
                Label = "Battery Saves",
                SyncMode = "bisync",
                LocalPath = @"D:\Emu\Saves",
                RemoteTarget = "EmuDrive:Saves"
            });
            cfg.DefaultSyncPaths.Add(new GameSyncPathItem
            {
                Enabled = true,
                Label = "Save States",
                SyncMode = "sync",
                LocalPath = @"D:\Emu\States",
                RemoteTarget = "EmuDrive:States"
            });
            cfg.DefaultSyncPaths.Add(new GameSyncPathItem
            {
                Enabled = true,
                Label = "Memory Cards",
                SyncMode = "copy",
                LocalPath = @"D:\Emu\Memcards",
                RemoteTarget = "EmuDrive:Memcards"
            });

            cfg.SanitizeAutoGeneratedCommandOverrides();
            Assert.Equal("", cfg.PreLaunchCommand);
            Assert.Equal("", cfg.PostLaunchCommand);

            var (preList, postList) = cfg.ResolveAllSyncCommands("rclone.exe", null);
            Assert.Equal(3, preList.Count);
            Assert.Equal(3, postList.Count);
            Assert.Contains(preList, c => c.Contains("EmuDrive:Saves") && c.Contains("bisync"));
            Assert.Contains(preList, c => c.Contains("EmuDrive:States") && c.Contains(" sync "));
            Assert.Contains(preList, c => c.Contains("EmuDrive:Memcards") && c.Contains(" copy "));

            Assert.Contains(postList, c => c.Contains("EmuDrive:Saves") && c.Contains("bisync"));
            Assert.Contains(postList, c => c.Contains("EmuDrive:States") && c.Contains(" sync "));
            Assert.Contains(postList, c => c.Contains("EmuDrive:Memcards") && c.Contains(" copy "));

            // Explicit custom script override is honored ONLY when UseCustomPrePostLaunchCommands == true
            cfg.UseCustomPrePostLaunchCommands = true;
            cfg.PreLaunchCommand = "echo CustomPre";
            cfg.PostLaunchCommand = "echo CustomPost";

            var (customPre, customPost) = cfg.ResolveAllSyncCommands("rclone.exe", null);
            Assert.Single(customPre);
            Assert.Single(customPost);
            Assert.Equal("echo CustomPre", customPre[0]);
            Assert.Equal("echo CustomPost", customPost[0]);
        }

        [Fact]
        public async Task RomManifestCsv_NineColumnCrc32BesideSha1_AndLegacySevenColumnAutoBackfillWithoutDecompression()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "hk_rc5_csv9_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                string romName = "Super Metroid (Japan, USA).sfc";
                byte[] romBytes = new byte[8192];
                for (int i = 0; i < romBytes.Length; i++) romBytes[i] = (byte)((i * 31 + 7) & 0xFF);

                string tempRom = Path.Combine(tempRoot, romName);
                await File.WriteAllBytesAsync(tempRom, romBytes);
                var expectedHashes = await HashSidecarService.ComputeFileHashesAsync(tempRom);
                File.Delete(tempRom);

                string zipPath = Path.Combine(tempRoot, "Super Metroid (Japan, USA).zip");
                using (var za = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
                {
                    var entry = za.CreateEntry(romName, System.IO.Compression.CompressionLevel.Fastest);
                    await using var es = entry.Open();
                    await es.WriteAsync(romBytes);
                }

                // Write a legacy 7-column CSV (no CRC32 column, no Archive Compliance column) newer than the archive
                string csvPath = Path.Combine(tempRoot, "collection_manifest.csv");
                string legacy7ColCsv =
                    "Game Title,Proper Redump/No-Intro Name,Archive File Name,ROM / Folder Name,Verified Local Hash,Match Status,Notes\r\n" +
                    $"\"Super Metroid (Japan, USA)\",\"Super Metroid (Japan, USA)\",\"Super Metroid (Japan, USA).zip\",\"{romName}\",{expectedHashes.Sha1},MATCH,\r\n";
                await File.WriteAllTextAsync(csvPath, legacy7ColCsv);
                File.SetLastWriteTimeUtc(zipPath, DateTime.UtcNow.AddMinutes(-10));
                File.SetLastWriteTimeUtc(csvPath, DateTime.UtcNow.AddMinutes(-5));

                var dat = new DatIndex { Title = "Nintendo - Super Nintendo Entertainment System" };
                dat.Add(new DatRomEntry
                {
                    GameName = "Super Metroid (Japan, USA)",
                    RomFileName = romName,
                    Size = expectedHashes.Size,
                    Crc32 = expectedHashes.Crc32,
                    Sha1 = expectedHashes.Sha1
                });

                var cfg = new RomLauncherGeneratorConfig
                {
                    EnableCollectionManifestCsv = true,
                    ManifestCsvFileName = "collection_manifest.csv",
                    DeepInspectAndHashInnerRoms = true,
                    TrustExistingHashFiles = true,
                    ForceRehashExistingManifestEntries = false,
                    HashFilePlacement = HashSidecarMode.None,
                    EnableArchiveTemplateComplianceScan = true
                };
                var colRule = new ConsoleCollectionRule
                {
                    ConsoleName = "SNES",
                    ArchivesPath = tempRoot,
                    ExtensionsCsv = ".sfc, .smc",
                    ComplianceTemplateId = "auto-community"
                };

                await RomManifestCsvService.GenerateOrUpdateManifestAsync(
                    tempRoot,
                    "SNES",
                    cfg,
                    dat,
                    (_, _) => { },
                    CancellationToken.None,
                    consoleRule: colRule);

                string upgradedRaw = await File.ReadAllTextAsync(csvPath);
                string firstLine = upgradedRaw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
                Assert.Equal(
                    "Game Title,Proper Redump/No-Intro Name,Archive File Name,ROM / Folder Name,Verified Local CRC32,Verified Local Hash,Match Status,Archive Compliance,Notes",
                    firstLine);

                var loaded = RomManifestCsvService.LoadExistingManifest(csvPath);
                Assert.Single(loaded);
                var row = loaded.Values.Single();
                Assert.Equal(expectedHashes.Crc32, row.VerifiedLocalCrc32);
                Assert.Equal(expectedHashes.Sha1, row.VerifiedLocalHash);
                Assert.Equal("MATCH", row.MatchStatus);
                Assert.Equal("CLEAN", row.ArchiveCompliance);
            }
            finally
            {
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch (System.Exception __suppressedEx12) { System.Diagnostics.Debug.WriteLine($"[HousekeepingTests] Non-fatal error ignored: {__suppressedEx12.Message}"); }
            }
        }

        [Fact]
        public async Task RomManifestCsv_TimestampChangedArchive_ReusesVerifiedSha1WhenHeaderCrc32Matches_AndInvalidatesWhenChanged()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "hk_rc5_tsproof_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                string romName = "Chrono Trigger (USA).sfc";
                byte[] romBytesV1 = new byte[4096];
                for (int i = 0; i < romBytesV1.Length; i++) romBytesV1[i] = (byte)((i * 13 + 3) & 0xFF);

                string tmpFile = Path.Combine(tempRoot, romName);
                await File.WriteAllBytesAsync(tmpFile, romBytesV1);
                var hashesV1 = await HashSidecarService.ComputeFileHashesAsync(tmpFile);
                File.Delete(tmpFile);

                string zipPath = Path.Combine(tempRoot, "Chrono Trigger (USA).zip");
                using (var za = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
                {
                    var entry = za.CreateEntry(romName);
                    await using var es = entry.Open();
                    await es.WriteAsync(romBytesV1);
                }

                // Seed CSV with sentinel SHA-1 + real V1 CRC32 so we can prove SHA-1 was reused WITHOUT decompressing!
                string sentinelCachedSha1 = "1111111111111111111111111111111111111111";
                string csvPath = Path.Combine(tempRoot, "collection_manifest.csv");
                await File.WriteAllTextAsync(csvPath,
                    "Game Title,Proper Redump/No-Intro Name,Archive File Name,ROM / Folder Name,Verified Local CRC32,Verified Local Hash,Match Status,Archive Compliance,Notes\r\n" +
                    $"\"Chrono Trigger (USA)\",\"Chrono Trigger (USA)\",\"Chrono Trigger (USA).zip\",\"{romName}\",{hashesV1.Crc32},{sentinelCachedSha1},MATCH,CLEAN,\r\n");

                // Touch the archive timestamp 2 hours INTO THE FUTURE relative to CSV (simulating NAS move/touch)
                File.SetLastWriteTimeUtc(csvPath, DateTime.UtcNow.AddHours(-1));
                File.SetLastWriteTimeUtc(zipPath, DateTime.UtcNow.AddHours(1));

                var cfg = new RomLauncherGeneratorConfig
                {
                    EnableCollectionManifestCsv = true,
                    ManifestCsvFileName = "collection_manifest.csv",
                    DeepInspectAndHashInnerRoms = true,
                    TrustExistingHashFiles = false,
                    ForceRehashExistingManifestEntries = false,
                    HashFilePlacement = HashSidecarMode.None
                };

                await RomManifestCsvService.GenerateOrUpdateManifestAsync(
                    tempRoot,
                    "SNES",
                    cfg,
                    datIndex: new DatIndex(),
                    (_, _) => { },
                    CancellationToken.None);

                var afterTouch = RomManifestCsvService.LoadExistingManifest(csvPath).Values.Single();
                Assert.Equal(hashesV1.Crc32, afterTouch.VerifiedLocalCrc32);
                Assert.Equal(sentinelCachedSha1, afterTouch.VerifiedLocalHash); // Reused via header CRC32 match!

                // Now mutate the inner ROM bytes (changing header CRC32) and touch timestamp again -> MUST invalidate cache and compute true V2 SHA-1!
                byte[] romBytesV2 = (byte[])romBytesV1.Clone();
                romBytesV2[100] ^= 0xFF;
                await File.WriteAllBytesAsync(tmpFile, romBytesV2);
                var hashesV2 = await HashSidecarService.ComputeFileHashesAsync(tmpFile);
                File.Delete(tmpFile);

                File.Delete(zipPath);
                using (var za = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
                {
                    var entry = za.CreateEntry(romName);
                    await using var es = entry.Open();
                    await es.WriteAsync(romBytesV2);
                }
                File.SetLastWriteTimeUtc(zipPath, DateTime.UtcNow.AddHours(2));

                await RomManifestCsvService.GenerateOrUpdateManifestAsync(
                    tempRoot,
                    "SNES",
                    cfg,
                    datIndex: new DatIndex(),
                    (_, _) => { },
                    CancellationToken.None);

                var afterMutate = RomManifestCsvService.LoadExistingManifest(csvPath).Values.Single();
                Assert.Equal(hashesV2.Crc32, afterMutate.VerifiedLocalCrc32);
                Assert.Equal(hashesV2.Sha1, afterMutate.VerifiedLocalHash);
                Assert.NotEqual(sentinelCachedSha1, afterMutate.VerifiedLocalHash);
            }
            finally
            {
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch (System.Exception __suppressedEx13) { System.Diagnostics.Debug.WriteLine($"[HousekeepingTests] Non-fatal error ignored: {__suppressedEx13.Message}"); }
            }
        }

        [Fact]
        public async Task RomManifestCsv_FastHeaderCrc32AuditMode_MatchesDatInZeroDecompression_AndUpgradesToSha1WhenDisabled()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "hk_rc5_fastcrc_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                string romName = "Castlevania - Aria of Sorrow (USA).gba";
                byte[] romBytes = new byte[6144];
                for (int i = 0; i < romBytes.Length; i++) romBytes[i] = (byte)((i * 17 + 19) & 0xFF);

                string tmpRom = Path.Combine(tempRoot, romName);
                await File.WriteAllBytesAsync(tmpRom, romBytes);
                var trueHashes = await HashSidecarService.ComputeFileHashesAsync(tmpRom);
                File.Delete(tmpRom);

                string zipPath = Path.Combine(tempRoot, "Aria_Custom_Name.zip");
                using (var za = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
                {
                    var entry = za.CreateEntry(romName);
                    await using var es = entry.Open();
                    await es.WriteAsync(romBytes);
                }

                // DAT index with BOTH CRC32 and SHA1 (also test CRC32-only DAT matching)
                var dat = new DatIndex { Title = "Nintendo - Game Boy Advance" };
                dat.Add(new DatRomEntry
                {
                    GameName = "Castlevania - Aria of Sorrow (USA)",
                    RomFileName = romName,
                    Size = trueHashes.Size,
                    Crc32 = trueHashes.Crc32.ToLowerInvariant(), // verify lowercase DAT crc normalizes to 8-hex uppercase
                    Sha1 = trueHashes.Sha1
                });

                var cfg = new RomLauncherGeneratorConfig
                {
                    EnableCollectionManifestCsv = true,
                    ManifestCsvFileName = "collection_manifest.csv",
                    DeepInspectAndHashInnerRoms = true,
                    UseFastArchiveHeaderCrc32Audit = true,
                    HashFilePlacement = HashSidecarMode.SidecarBeside
                };

                await RomManifestCsvService.GenerateOrUpdateManifestAsync(
                    tempRoot,
                    "GBA",
                    cfg,
                    dat,
                    (_, _) => { },
                    CancellationToken.None);

                string csvPath = Path.Combine(tempRoot, "collection_manifest.csv");
                var fastRow = RomManifestCsvService.LoadExistingManifest(csvPath).Values.Single();
                Assert.Equal(trueHashes.Crc32, fastRow.VerifiedLocalCrc32);
                Assert.Equal("", fastRow.VerifiedLocalHash); // Zero decompression performed!
                Assert.Equal("Castlevania - Aria of Sorrow (USA)", fastRow.ProperName);
                Assert.Equal("MATCH (CRC32)", fastRow.MatchStatus);
                // Must NOT write a partial SHA1-less .hash sidecar during Fast Header CRC32 mode
                Assert.Empty(HashSidecarService.GetExistingExternalSidecarPaths(zipPath));

                // Switch UseFastArchiveHeaderCrc32Audit back to false -> upgrades row to full SHA-1 MATCH and writes .hash sidecar!
                cfg.UseFastArchiveHeaderCrc32Audit = false;
                await RomManifestCsvService.GenerateOrUpdateManifestAsync(
                    tempRoot,
                    "GBA",
                    cfg,
                    dat,
                    (_, _) => { },
                    CancellationToken.None);

                var fullRow = RomManifestCsvService.LoadExistingManifest(csvPath).Values.Single();
                Assert.Equal(trueHashes.Crc32, fullRow.VerifiedLocalCrc32);
                Assert.Equal(trueHashes.Sha1, fullRow.VerifiedLocalHash);
                Assert.Equal("MATCH", fullRow.MatchStatus);
                Assert.True(HashSidecarService.TryReadExistingSidecar(zipPath, out var sidecar));
                Assert.Equal(trueHashes.Sha1, sidecar!.Sha1);
            }
            finally
            {
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch (System.Exception __suppressedEx14) { System.Diagnostics.Debug.WriteLine($"[HousekeepingTests] Non-fatal error ignored: {__suppressedEx14.Message}"); }
            }
        }

        [Fact]
        public async Task ArchiveCompliance_CommunityVsOfficialNoIntroCartridge_EvaluatesExtraFilesMultiRomFormatAndWrapper()
        {
            string sevenZip = HashSidecarService.ResolveSevenZipExecutable();
            string tempRoot = Path.Combine(Path.GetTempPath(), "hk_rc5_cart_comp_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                var cfg = new RomLauncherGeneratorConfig();
                cfg.EnsureArchiveTemplatesInitialized();

                // 1. Archive with primary .sfc + scene bloat (.nfo + .txt) inside 1 wrapper folder
                string bloatZip = Path.Combine(tempRoot, "EarthBound (USA).zip");
                using (var za = System.IO.Compression.ZipFile.Open(bloatZip, System.IO.Compression.ZipArchiveMode.Create))
                {
                    await using (var s = za.CreateEntry("EarthBound/EarthBound (USA).sfc").Open())
                        await s.WriteAsync(new byte[1024]);
                    await using (var s = za.CreateEntry("EarthBound/release.nfo").Open())
                        await s.WriteAsync(new byte[128]);
                    await using (var s = za.CreateEntry("EarthBound/readme.txt").Open())
                        await s.WriteAsync(new byte[64]);
                }

                var communityCol = new ConsoleCollectionRule
                {
                    ConsoleName = "SNES",
                    ArchivesPath = tempRoot,
                    ExtensionsCsv = ".sfc, .smc",
                    ComplianceTemplateId = "auto-community"
                };

                var evalCommunity = await ArchiveComplianceService.EvaluateTargetComplianceAsync(
                    bloatZip, isDirectory: false, communityCol, cfg, sevenZip);
                Assert.False(evalCommunity.IsClean);
                Assert.Contains("ExtraFiles(2)", evalCommunity.SummaryCode);
                // Community allows 1 wrapper folder
                Assert.DoesNotContain(evalCommunity.Issues, i => i.Kind == ComplianceIssueKind.DisallowedWrapperFolder);

                // Official No-Intro disallows wrapper subfolders AND flags .smc when only canonical No-Intro extensions are allowed
                var officialCol = new ConsoleCollectionRule
                {
                    ConsoleName = "SNES",
                    ArchivesPath = tempRoot,
                    ExtensionsCsv = ".sfc, .smc",
                    ComplianceTemplateId = "official-nointro-cartridge"
                };

                var evalOfficialWrapper = await ArchiveComplianceService.EvaluateTargetComplianceAsync(
                    bloatZip, isDirectory: false, officialCol, cfg, sevenZip);
                Assert.Contains(evalOfficialWrapper.Issues, i => i.Kind == ComplianceIssueKind.DisallowedWrapperFolder);
                Assert.Contains(evalOfficialWrapper.Issues, i => i.Kind == ComplianceIssueKind.ExtraNonRuleFile);

                // 2. Wrong format (.smc copier header under Official No-Intro) + Multi-ROM pack (2 primary ROMs in 1 archive)
                string smcMultiZip = Path.Combine(tempRoot, "Mario Pack.zip");
                using (var za = System.IO.Compression.ZipFile.Open(smcMultiZip, System.IO.Compression.ZipArchiveMode.Create))
                {
                    await using (var s = za.CreateEntry("Super Mario World (USA).sfc").Open())
                        await s.WriteAsync(new byte[1024]);
                    await using (var s = za.CreateEntry("Super Mario All-Stars (USA).sfc").Open())
                        await s.WriteAsync(new byte[1024]);
                }

                var evalMulti = await ArchiveComplianceService.EvaluateTargetComplianceAsync(
                    smcMultiZip, isDirectory: false, officialCol, cfg, sevenZip);
                Assert.Contains(evalMulti.Issues, i => i.Kind == ComplianceIssueKind.MultiplePrimaryRomsInArchive);

                string smcZip = Path.Combine(tempRoot, "Copier Dump.zip");
                using (var za = System.IO.Compression.ZipFile.Open(smcZip, System.IO.Compression.ZipArchiveMode.Create))
                {
                    await using (var s = za.CreateEntry("Copier Dump.smc").Open())
                        await s.WriteAsync(new byte[1024]);
                }
                var evalSmcOfficial = await ArchiveComplianceService.EvaluateTargetComplianceAsync(
                    smcZip, isDirectory: false, officialCol, cfg, sevenZip);
                Assert.Contains(evalSmcOfficial.Issues, i => i.Kind == ComplianceIssueKind.WrongInnerPayloadFormat);
            }
            finally
            {
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch (System.Exception __suppressedEx15) { System.Diagnostics.Debug.WriteLine($"[HousekeepingTests] Non-fatal error ignored: {__suppressedEx15.Message}"); }
            }
        }

        [Fact]
        public async Task ArchiveCompliance_OpticalCueBinAndSiblingMultiDisc_DetectsMissingCueBrokenTrackAndMissingDisc2()
        {
            string sevenZip = HashSidecarService.ResolveSevenZipExecutable();
            string tempRoot = Path.Combine(Path.GetTempPath(), "hk_rc5_optical_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                var cfg = new RomLauncherGeneratorConfig();
                cfg.EnsureArchiveTemplatesInitialized();
                var ps1Col = new ConsoleCollectionRule
                {
                    ConsoleName = "PlayStation",
                    ArchivesPath = tempRoot,
                    ExtensionsCsv = ".cue, .chd, .pbp",
                    ComplianceTemplateId = "official-redump-cd"
                };

                // 1. Raw .bin WITHOUT a .cue sheet
                string noCueZip = Path.Combine(tempRoot, "Ridge Racer (USA).zip");
                using (var za = System.IO.Compression.ZipFile.Open(noCueZip, System.IO.Compression.ZipArchiveMode.Create))
                {
                    await using var s = za.CreateEntry("Ridge Racer (USA) (Track 01).bin").Open();
                    await s.WriteAsync(new byte[4096]);
                }
                var evalNoCue = await ArchiveComplianceService.EvaluateTargetComplianceAsync(
                    noCueZip, isDirectory: false, ps1Col, cfg, sevenZip);
                Assert.Contains(evalNoCue.Issues, i => i.Kind == ComplianceIssueKind.MissingCompanionDescriptor);

                // 2. .cue referencing a missing Track 02.bin
                string brokenCueZip = Path.Combine(tempRoot, "Wipeout (USA).zip");
                using (var za = System.IO.Compression.ZipFile.Open(brokenCueZip, System.IO.Compression.ZipArchiveMode.Create))
                {
                    string cueText =
                        "FILE \"Wipeout (USA) (Track 01).bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n" +
                        "FILE \"Wipeout (USA) (Track 02).bin\" BINARY\r\n  TRACK 02 AUDIO\r\n    INDEX 01 00:00:00\r\n";
                    await using (var cs = new StreamWriter(za.CreateEntry("Wipeout (USA).cue").Open()))
                        await cs.WriteAsync(cueText);
                    await using (var bs = za.CreateEntry("Wipeout (USA) (Track 01).bin").Open())
                        await bs.WriteAsync(new byte[4096]);
                }
                var evalBrokenCue = await ArchiveComplianceService.EvaluateTargetComplianceAsync(
                    brokenCueZip, isDirectory: false, ps1Col, cfg, sevenZip);
                Assert.Contains(evalBrokenCue.Issues, i => i.Kind == ComplianceIssueKind.MissingReferencedTrack);

                // 3. Sibling Multi-Disc Continuity: Disc 1 + Disc 3 present in console folder, Disc 2 missing!
                string multiDir = Path.Combine(tempRoot, "MultiDiscCheck");
                Directory.CreateDirectory(multiDir);
                ps1Col.ArchivesPath = multiDir;

                foreach (int d in new[] { 1, 3 })
                {
                    string discZip = Path.Combine(multiDir, $"Final Fantasy VII (USA) (Disc {d}).zip");
                    using var za = System.IO.Compression.ZipFile.Open(discZip, System.IO.Compression.ZipArchiveMode.Create);
                    string cueName = $"Final Fantasy VII (USA) (Disc {d}).cue";
                    string binName = $"Final Fantasy VII (USA) (Disc {d}).bin";
                    await using (var cs = new StreamWriter(za.CreateEntry(cueName).Open()))
                        await cs.WriteAsync($"FILE \"{binName}\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n");
                    await using (var bs = za.CreateEntry(binName).Open())
                        await bs.WriteAsync(new byte[4096]);
                }

                var reports = await ArchiveComplianceService.ScanConsoleCollectionAsync(ps1Col, cfg, sevenZip);
                Assert.Equal(2, reports.Count);
                Assert.All(reports, r => Assert.Contains(r.Issues, i => i.Kind == ComplianceIssueKind.MultiDiscSequenceGap));
            }
            finally
            {
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch (System.Exception __suppressedEx16) { System.Diagnostics.Debug.WriteLine($"[HousekeepingTests] Non-fatal error ignored: {__suppressedEx16.Message}"); }
            }
        }

        [Fact]
        public async Task ArchiveCompliance_CommunityModernDiscVsOfficialRedumpCd_ValidatesChdRvzVersusCueBin()
        {
            string sevenZip = HashSidecarService.ResolveSevenZipExecutable();
            string tempRoot = Path.Combine(Path.GetTempPath(), "hk_rc5_disc_modes_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                var cfg = new RomLauncherGeneratorConfig();
                cfg.EnsureArchiveTemplatesInitialized();

                // Create a loose .chd file and a .zip containing raw .cue + .bin
                string chdPath = Path.Combine(tempRoot, "Metal Gear Solid (USA).chd");
                await File.WriteAllBytesAsync(chdPath, new byte[2048]);

                string cueBinZip = Path.Combine(tempRoot, "Metal Gear Solid (USA).zip");
                using (var za = System.IO.Compression.ZipFile.Open(cueBinZip, System.IO.Compression.ZipArchiveMode.Create))
                {
                    await using (var cs = new StreamWriter(za.CreateEntry("Metal Gear Solid (USA).cue").Open()))
                        await cs.WriteAsync("FILE \"Metal Gear Solid (USA).bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n");
                    await using (var bs = za.CreateEntry("Metal Gear Solid (USA).bin").Open())
                        await bs.WriteAsync(new byte[4096]);
                }

                var modernCol = new ConsoleCollectionRule
                {
                    ConsoleName = "PlayStation",
                    ArchivesPath = tempRoot,
                    ExtensionsCsv = ".chd, .rvz",
                    ComplianceTemplateId = "community-modern-disc"
                };

                var chdUnderModern = await ArchiveComplianceService.EvaluateTargetComplianceAsync(chdPath, false, modernCol, cfg, sevenZip);
                Assert.True(chdUnderModern.IsClean);

                var cueBinUnderModern = await ArchiveComplianceService.EvaluateTargetComplianceAsync(cueBinZip, false, modernCol, cfg, sevenZip);
                Assert.False(cueBinUnderModern.IsClean);
                Assert.Contains(cueBinUnderModern.Issues, i => i.Kind == ComplianceIssueKind.WrongInnerPayloadFormat);

                var officialRedumpCol = new ConsoleCollectionRule
                {
                    ConsoleName = "PlayStation",
                    ArchivesPath = tempRoot,
                    ExtensionsCsv = ".cue, .chd",
                    ComplianceTemplateId = "official-redump-cd"
                };

                var cueBinUnderRedump = await ArchiveComplianceService.EvaluateTargetComplianceAsync(cueBinZip, false, officialRedumpCol, cfg, sevenZip);
                Assert.True(cueBinUnderRedump.IsClean);

                var chdUnderRedump = await ArchiveComplianceService.EvaluateTargetComplianceAsync(chdPath, false, officialRedumpCol, cfg, sevenZip);
                Assert.False(chdUnderRedump.IsClean);
                Assert.Contains(chdUnderRedump.Issues, i => i.Kind == ComplianceIssueKind.WrongInnerPayloadFormat);
            }
            finally
            {
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch (System.Exception __suppressedEx17) { System.Diagnostics.Debug.WriteLine($"[HousekeepingTests] Non-fatal error ignored: {__suppressedEx17.Message}"); }
            }
        }

        [Fact]
        public async Task ArchiveCompliance_GuardedCleanup7zDelete_RemovesExtraClutterPreservesRomAndHash_AndResolvesReport()
        {
            string sevenZip = HashSidecarService.ResolveSevenZipExecutable();
            if (!File.Exists(sevenZip))
                return;

            string tempRoot = Path.Combine(Path.GetTempPath(), "hk_rc5_cleanup_" + Guid.NewGuid().ToString("N"));
            string snesDir = Path.Combine(tempRoot, "SNES");
            string staging = Path.Combine(tempRoot, "staging");
            Directory.CreateDirectory(snesDir);
            Directory.CreateDirectory(staging);

            try
            {
                string romPath = Path.Combine(staging, "F-Zero (USA).sfc");
                byte[] romBytes = new byte[4096];
                for (int i = 0; i < romBytes.Length; i++) romBytes[i] = (byte)((i * 41 + 13) & 0xFF);
                await File.WriteAllBytesAsync(romPath, romBytes);
                var expectedRomHash = await HashSidecarService.ComputeFileHashesAsync(romPath);

                string hashPath = Path.Combine(staging, "F-Zero (USA).hash");
                await File.WriteAllTextAsync(
                    hashPath,
                    HashSidecarService.GenerateSidecarContentForFiles("F-Zero (USA)", new[] { expectedRomHash }));

                string nfoPath = Path.Combine(staging, "scene_group.nfo");
                string txtPath = Path.Combine(staging, "downloaded_from_site.txt");
                await File.WriteAllTextAsync(nfoPath, "SCENE NFO BLOAT");
                await File.WriteAllTextAsync(txtPath, "README BLOAT");

                string archivePath = Path.Combine(snesDir, "F-Zero (USA).7z");
                using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = sevenZip,
                    Arguments = $"a \"{archivePath}\" \"{romPath}\" \"{hashPath}\" \"{nfoPath}\" \"{txtPath}\" -y",
                    UseShellExecute = false,
                    CreateNoWindow = true
                })!)
                {
                    await p.WaitForExitAsync();
                    Assert.Equal(0, p.ExitCode);
                }

                string reportsJsonPath = Path.Combine(tempRoot, "compliance_reports.json");
                var reportStore = new ComplianceReportService(reportsJsonPath);
                var cfg = new RomLauncherGeneratorConfig();
                cfg.EnsureArchiveTemplatesInitialized();
                var col = new ConsoleCollectionRule
                {
                    ConsoleName = "SNES",
                    ArchivesPath = snesDir,
                    ExtensionsCsv = ".sfc",
                    ComplianceTemplateId = "auto-community"
                };

                var found = await ArchiveComplianceService.ScanConsoleCollectionAsync(col, cfg, sevenZip, reportStore);
                Assert.Single(found);
                Assert.Equal(1, reportStore.GetOpenCount());
                var reportItem = found[0];
                Assert.Equal(2, reportItem.ExtraDeletableCount);

                // Dry-run preview must list the 2 bloat files for removal and keep both the .sfc ROM and .hash sidecar protected
                var dryRun = ArchiveComplianceService.BuildCleanupDryRunPreview(reportItem, sevenZip);
                Assert.True(dryRun.IsAllowed);
                Assert.Equal(2, dryRun.FilesToDelete.Count);
                Assert.Contains(dryRun.FilesToDelete, f => f.InternalPath.EndsWith(".nfo", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(dryRun.FilesToDelete, f => f.InternalPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(dryRun.ProtectedFilesKept, f => f.InternalPath.EndsWith(".sfc", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(dryRun.ProtectedFilesKept, f => f.InternalPath.EndsWith(".hash", StringComparison.OrdinalIgnoreCase));

                // Execute guarded cleanup via 7z d
                var (ok, _, postEval) = await ArchiveComplianceService.CleanExtraFilesFromArchiveAsync(
                    reportItem, col, cfg, sevenZip, reportStore);
                Assert.True(ok);
                Assert.NotNull(postEval);
                Assert.True(postEval!.IsClean);
                Assert.Equal("CLEAN", postEval.SummaryCode);
                Assert.Equal(0, reportStore.GetOpenCount());
                Assert.Equal(ComplianceReportResolutionStatus.Resolved, reportStore.GetAllReports().Single().ResolutionStatus);

                // Verify remaining archive entries: strictly F-Zero (USA).sfc + F-Zero (USA).hash, byte-for-byte valid!
                var remainingEntries = await HashSidecarService.ListArchiveContentsAsync(sevenZip, archivePath);
                Assert.Equal(2, remainingEntries.Count(e => !e.IsDirectory));
                Assert.DoesNotContain(remainingEntries, e => e.Path.EndsWith(".nfo", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(remainingEntries, e => e.Path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));

                var deepVerify = await HashSidecarService.VerifySidecarForTargetAsync(
                    sevenZip, archivePath, expectInnerRom: true, deepCryptographicVerify: true);
                Assert.Equal(SidecarVerificationStatus.Valid, deepVerify.Status);
                Assert.Equal(expectedRomHash.Sha1, deepVerify.SidecarHash!.Sha1);
            }
            finally
            {
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch (System.Exception __suppressedEx18) { System.Diagnostics.Debug.WriteLine($"[HousekeepingTests] Non-fatal error ignored: {__suppressedEx18.Message}"); }
            }
        }
    }
}