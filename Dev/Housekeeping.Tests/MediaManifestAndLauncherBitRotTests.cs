using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Models;
using Housekeeping.Services;
using Housekeeping.Services.Modules;
using Xunit;

namespace Housekeeping.Tests
{
    public class MediaManifestAndLauncherBitRotTests
    {
        [Fact]
        public async Task RomLauncher_PackagesUncompressedRoms_WithoutTouchingHashSidecars()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "hk_test_launcher_decouple_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var appCfg = AppConfig.CreateDefaults();
                var dep = new DependencyCheckService(() => appCfg);
                string sevenZip = dep.ResolveSevenZip();

                string romFile = Path.Combine(tempDir, "Chrono Trigger (USA).sfc");
                await File.WriteAllBytesAsync(romFile, Encoding.UTF8.GetBytes("SUPER NINTENDO CHRONO TRIGGER ROM PAYLOAD"));

                var col = new ConsoleCollectionRule
                {
                    Enabled = true,
                    ConsoleName = "Super Nintendo Entertainment System",
                    ArchivesPath = tempDir,
                    ExtensionsCsv = ".sfc"
                };

                var cfg = new RomLauncherGeneratorConfig
                {
                    Enabled = true,
                    AutoCompressUnarchivedRomsWith7z = true,
                    DeleteUncompressedOriginalsAfter7z = false,
                    AutoGenerateM3uForMultiDisc = false,
                    EnableCollectionManifestCsv = false,
                    CreateLocalLauncherNextToArchive = true
                };
                cfg.Collections.Add(col);
                appCfg.RomLauncherGenerator = cfg;

                var ctx = new ModuleExecutionContext
                {
                    Config = appCfg,
                    ConfigService = new ConfigService(),
                    Dependencies = dep,
                    NetworkSafety = new NetworkSafetyService(() => appCfg, (_, _) => { }),
                    ProcessRunner = new ProcessRunnerService((_, _) => { }),
                    IsDryRun = false,
                    WaitIfQueuePausedAsync = _ => Task.CompletedTask,
                    Log = (_, _) => { },
                    UpdateModuleProgress = (_, _) => { }
                };

                await RomLauncherGeneratorModule.RunAsync(ctx, CancellationToken.None);

                string expectedArchive = Path.Combine(tempDir, "Chrono Trigger (USA).7z");
                Assert.True(File.Exists(expectedArchive), "Expected .7z archive to be created by RomLauncherGeneratorModule.");

                // Assert zero .hash sidecar creation or manipulation
                string sidecarBeside = Path.Combine(tempDir, "Chrono Trigger (USA).hash");
                Assert.False(File.Exists(sidecarBeside), "RomLauncherGeneratorModule should NOT create loose .hash sidecars.");

                // Assert no .hash injected inside the .7z archive
                var archiveEntries = await HashSidecarService.ListArchiveContentsAsync(sevenZip, expectedArchive);
                Assert.DoesNotContain(archiveEntries, e => e.Path.EndsWith(".hash", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task MediaManifest_BitRot_DetectsHashMismatch_AndLogsWarning()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "hk_test_bitrot_detect_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var appCfg = AppConfig.CreateDefaults();
                var dep = new DependencyCheckService(() => appCfg);

                string mediaFile = Path.Combine(tempDir, "Blade Runner (1982) [1080p].mkv");
                await File.WriteAllBytesAsync(mediaFile, Encoding.UTF8.GetBytes("ORIGINAL PRISTINE VIDEO STREAM"));

                // Compute baseline hashes and create .hash sidecar
                var pristineHashes = await HashSidecarService.ComputeFileHashesAsync(mediaFile);
                string sidecarContent = HashSidecarService.GenerateSidecarContentForFiles("Blade Runner (1982) [1080p]", new[] { pristineHashes });
                string sidecarPath = Path.Combine(tempDir, "Blade Runner (1982) [1080p].hash");
                await File.WriteAllTextAsync(sidecarPath, sidecarContent, Encoding.UTF8);

                // Introduce bit rot into media file
                await File.WriteAllBytesAsync(mediaFile, Encoding.UTF8.GetBytes("CORRUPTED MEDIA STREAM WITH BIT ROT"));

                var cmCfg = new CollectionManifestConfig
                {
                    Enabled = true,
                    EnableBitRotDetection = true,
                    CreateMissingHashFiles = false,
                    ExportCsv = true,
                    ExportJson = true,
                    ExportHtmlReport = false,
                    UnifiedMasterManifest = true,
                    UnifiedManifestOutputFolder = tempDir,
                    UnifiedManifestName = "Test_BitRot_Manifest"
                };

                cmCfg.TargetFolders.Add(new ManifestTargetFolderRule
                {
                    Enabled = true,
                    LocationId = "NAS-Movies",
                    Label = "Movies",
                    FolderPath = tempDir,
                    IncludeSubdirectories = false,
                    StructureMode = AuditStructureMode.FilesOnly
                });

                appCfg.CollectionManifest = cmCfg;

                var loggedWarnings = new List<string>();
                var ctx = new ModuleExecutionContext
                {
                    Config = appCfg,
                    ConfigService = new ConfigService(),
                    Dependencies = dep,
                    NetworkSafety = new NetworkSafetyService(() => appCfg, (_, _) => { }),
                    ProcessRunner = new ProcessRunnerService((_, _) => { }),
                    IsDryRun = false,
                    WaitIfQueuePausedAsync = _ => Task.CompletedTask,
                    Log = (level, msg) =>
                    {
                        if (level == "ERROR" || level == "WARN")
                            loggedWarnings.Add(msg);
                    },
                    UpdateModuleProgress = (_, _) => { }
                };

                await CollectionManifestModule.RunAsync(ctx, CancellationToken.None);

                Assert.True(ctx.Stats.Errors > 0, "Expected error count to be incremented on bit rot detection.");
                Assert.Contains(loggedWarnings, m => m.Contains("BIT ROT WARNING", StringComparison.OrdinalIgnoreCase));

                // Verify CSV output records Bit Rot Warning
                string csvPath = Path.Combine(tempDir, "Test_BitRot_Manifest.csv");
                Assert.True(File.Exists(csvPath));
                string csvText = await File.ReadAllTextAsync(csvPath);
                Assert.Contains("Bit Rot Warning", csvText);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task MediaManifest_BitRot_RepackedArchive_FallsBackToPayload_AndRebaselines()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "hk_test_bitrot_repack_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var appCfg = AppConfig.CreateDefaults();
                var dep = new DependencyCheckService(() => appCfg);
                string sevenZip = dep.ResolveSevenZip();

                // Create inner file
                string payloadFile = Path.Combine(tempDir, "game.bin");
                byte[] payloadBytes = Encoding.UTF8.GetBytes("PRISTINE DISC IMAGE TRACK 01 BINARY PAYLOAD");
                await File.WriteAllBytesAsync(payloadFile, payloadBytes);

                // Compute inner file hash
                var innerHash = await HashSidecarService.ComputeFileHashesAsync(payloadFile);

                // Package into first 7z archive
                string archivePath = Path.Combine(tempDir, "Panzer Dragoon (USA).7z");
                using (var p1 = Process.Start(new ProcessStartInfo
                {
                    FileName = sevenZip,
                    Arguments = $"a \"{archivePath}\" \"{payloadFile}\" -mx=1 -y",
                    UseShellExecute = false,
                    CreateNoWindow = true
                })!)
                {
                    await p1.WaitForExitAsync();
                    Assert.Equal(0, p1.ExitCode);
                }

                // Delete loose payload
                File.Delete(payloadFile);

                // Create initial sidecar recording the old container hash and payload hash (before repack)
                var initialContainerHash = new FileHashResult
                {
                    FileName = "Panzer Dragoon (USA).7z",
                    Size = 999999,
                    Sha1 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                    Crc32 = "12345678"
                };
                string sidecarContent = HashSidecarService.GenerateSidecarContentForFiles(
                    "Panzer Dragoon (USA)",
                    new[] { innerHash },
                    initialContainerHash);
                string sidecarPath = Path.Combine(tempDir, "Panzer Dragoon (USA).hash");
                await File.WriteAllTextAsync(sidecarPath, sidecarContent, Encoding.UTF8);

                var newContainerHash = await HashSidecarService.ComputeFileHashesAsync(archivePath);
                // Outer hash has changed!
                Assert.NotEqual(initialContainerHash.Sha1, newContainerHash.Sha1);

                // Run Collection Manifest Auditor with Bit Rot Detection + Two-Step Inspection + Auto-Heal
                var cmCfg = new CollectionManifestConfig
                {
                    Enabled = true,
                    EnableBitRotDetection = true,
                    CheckInnerArchiveAndContainer = true,
                    EnableBitRotAutoHeal = true,
                    ExportCsv = true,
                    UnifiedMasterManifest = true,
                    UnifiedManifestOutputFolder = tempDir,
                    UnifiedManifestName = "Test_Repack_Manifest"
                };

                cmCfg.TargetFolders.Add(new ManifestTargetFolderRule
                {
                    Enabled = true,
                    LocationId = "Games-Saturn",
                    Label = "Sega Saturn",
                    FolderPath = tempDir,
                    IncludeSubdirectories = false,
                    StructureMode = AuditStructureMode.FilesOnly
                });

                appCfg.CollectionManifest = cmCfg;

                var ctx = new ModuleExecutionContext
                {
                    Config = appCfg,
                    ConfigService = new ConfigService(),
                    Dependencies = dep,
                    NetworkSafety = new NetworkSafetyService(() => appCfg, (_, _) => { }),
                    ProcessRunner = new ProcessRunnerService((_, _) => { }),
                    IsDryRun = false,
                    WaitIfQueuePausedAsync = _ => Task.CompletedTask,
                    Log = (_, _) => { },
                    UpdateModuleProgress = (_, _) => { }
                };

                await CollectionManifestModule.RunAsync(ctx, CancellationToken.None);

                // Assert zero errors: outer container was repacked, but inner payload was 100% verified!
                Assert.Equal(0, ctx.Stats.Errors);

                // Verify the sidecar was auto-healed with the new container hash
                string updatedSidecarText = await File.ReadAllTextAsync(sidecarPath);
                Assert.Contains($"# Container: Panzer Dragoon (USA).7z | SIZE: {newContainerHash.Size}", updatedSidecarText);
                Assert.Contains(newContainerHash.Sha1, updatedSidecarText);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task MediaManifest_CreatesMissingHashSidecar_WithContainerAndPayload()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "hk_test_missing_baseline_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var appCfg = AppConfig.CreateDefaults();
                var dep = new DependencyCheckService(() => appCfg);
                string sevenZip = dep.ResolveSevenZip();

                // Create archive with inner file
                string musicFile = Path.Combine(tempDir, "01 - Track.flac");
                await File.WriteAllBytesAsync(musicFile, Encoding.UTF8.GetBytes("LOSSLESS FLAC AUDIO STREAM"));

                string archivePath = Path.Combine(tempDir, "Album (2024).7z");
                using (var p = Process.Start(new ProcessStartInfo
                {
                    FileName = sevenZip,
                    Arguments = $"a \"{archivePath}\" \"{musicFile}\" -mx=5 -y",
                    UseShellExecute = false,
                    CreateNoWindow = true
                })!)
                {
                    await p.WaitForExitAsync();
                    Assert.Equal(0, p.ExitCode);
                }

                File.Delete(musicFile);

                string sidecarPath = HashSidecarService.GetStandardSidecarPath(archivePath);
                Assert.False(File.Exists(sidecarPath), "Precondition: .hash sidecar does not exist yet.");

                var cmCfg = new CollectionManifestConfig
                {
                    Enabled = true,
                    EnableBitRotDetection = true,
                    CreateMissingHashFiles = true,
                    CheckInnerArchiveAndContainer = true,
                    HashFilePlacement = HashSidecarMode.SidecarBeside,
                    ExportCsv = true,
                    UnifiedMasterManifest = true,
                    UnifiedManifestOutputFolder = tempDir,
                    UnifiedManifestName = "Test_Baseline_Manifest"
                };

                cmCfg.TargetFolders.Add(new ManifestTargetFolderRule
                {
                    Enabled = true,
                    LocationId = "Music-FLAC",
                    Label = "Music",
                    FolderPath = tempDir,
                    IncludeSubdirectories = false,
                    StructureMode = AuditStructureMode.FilesOnly
                });

                appCfg.CollectionManifest = cmCfg;

                var logs = new List<string>();
                var ctx = new ModuleExecutionContext
                {
                    Config = appCfg,
                    ConfigService = new ConfigService(),
                    Dependencies = dep,
                    NetworkSafety = new NetworkSafetyService(() => appCfg, (_, _) => { }),
                    ProcessRunner = new ProcessRunnerService((_, _) => { }),
                    IsDryRun = false,
                    WaitIfQueuePausedAsync = _ => Task.CompletedTask,
                    Log = (lvl, msg) => logs.Add($"[{lvl}] {msg}"),
                    UpdateModuleProgress = (_, _) => { }
                };

                await CollectionManifestModule.RunAsync(ctx, CancellationToken.None);

                Assert.True(File.Exists(sidecarPath), $"Expected .hash sidecar to be captured and created. Logs: {string.Join(" | ", logs)}");

                string sidecarContent = await File.ReadAllTextAsync(sidecarPath);
                Assert.Contains("# Container: Album (2024).7z", sidecarContent);
                Assert.Contains("01 - Track.flac", sidecarContent);

                // Check CSV exported Baseline Captured status
                string csvPath = Path.Combine(tempDir, "Test_Baseline_Manifest.csv");
                Assert.True(File.Exists(csvPath));
                string csv = await File.ReadAllTextAsync(csvPath);
                Assert.Contains("Baseline Captured", csv);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }
    }
}
