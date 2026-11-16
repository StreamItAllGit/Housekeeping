// @module: Housekeeping.Tests.CoexistenceAndSidecarV0112Tests
// @purpose: Regression suite for 4-mode .hash sidecars, .archive_hashes.json, # Container: retention, 9-column CSV compat, PID+StartTime .hklock, and Standard 18 DTO wire lockstep.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, System.Linq, System.Net.Http, System.Text.Json, System.Text.RegularExpressions, Housekeeping.Models, Housekeeping.Services, Xunit
// @exports: Housekeeping.Tests.CoexistenceAndSidecarV0112Tests

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Housekeeping.Models;
using Housekeeping.Services;
using Xunit;

namespace Housekeeping.Tests
{
    public sealed class CoexistenceAndSidecarV0112Tests
    {
        [Fact]
        public async Task T14_HashSidecar4Modes_LegacyAlias_ArchiveHashesRegistry_AndContainerHeaderRetention_WorkCleanly()
        {
            // 1. Legacy "None" and numeric 0..3 deserialize to canonical 4-mode HashSidecarMode
            var modeNone = JsonSerializer.Deserialize<HashSidecarMode>("\"None\"", ConfigService.JsonOptions);
            var mode0 = JsonSerializer.Deserialize<HashSidecarMode>("0", ConfigService.JsonOptions);
            var mode3 = JsonSerializer.Deserialize<HashSidecarMode>("\"SidecarAndArchive\"", ConfigService.JsonOptions);
            Assert.Equal(HashSidecarMode.DatabaseOnly, modeNone);
            Assert.Equal(HashSidecarMode.DatabaseOnly, mode0);
            Assert.Equal(HashSidecarMode.SidecarAndArchive, mode3);

            string vaultDir = Path.Combine(Path.GetTempPath(), $"hk_sidecar_{Guid.NewGuid():N}");
            Directory.CreateDirectory(vaultDir);
            try
            {
                // 2. Post-injection .archive_hashes.json round-trip & fast-path mtime+size hit
                string archivePath = Path.Combine(vaultDir, "Super Metroid (JU).7z");
                await File.WriteAllBytesAsync(archivePath, new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0x00, 0x04 });

                var inner = new FileHashResult
                {
                    FileName = "Super Metroid (JU).sfc",
                    RelativePath = "Super Metroid (JU).sfc",
                    Size = 3145728,
                    Crc32 = "D63ED5F8",
                    Md5 = "21F3E98DF4780EE1C667B84E57D88675",
                    Sha1 = "DA957F0D63D14CBCB4BBF7843ADFA5BF1D08DBCF"
                };

                var outer = await HashSidecarService.UpsertPostMutationArchiveHashAsync(
                    archivePath,
                    hasInternalSidecar: true,
                    internalSidecarPath: "Super Metroid (JU).hash",
                    primaryInner: inner,
                    placement: HashSidecarMode.PackageInsideArchive);
                Assert.NotNull(outer);
                Assert.True(File.Exists(Path.Combine(vaultDir, ".archive_hashes.json")));

                Assert.True(HashSidecarService.TryGetVerifiedOuterArchiveRecord(archivePath, out var cachedRec));
                Assert.NotNull(cachedRec);
                Assert.Equal(outer!.Crc32, cachedRec!.ContainerCrc32);
                Assert.True(cachedRec.HasInternalHashSidecar);
                Assert.Equal("D63ED5F8", cachedRec.PrimaryInnerCrc32);

                // 3. # Container: header retention + non-degrading MD5/SHA1 preservation
                string sidecarContent = HashSidecarService.GenerateSidecarContentForFiles("Super Metroid (JU)", new[] { inner }, outer);
                Assert.Contains("# Container:", sidecarContent);
                await File.WriteAllTextAsync(Path.Combine(vaultDir, "Super Metroid (JU).hash"), sidecarContent);

                Assert.True(HashSidecarService.TryReadExistingSidecarDocument(archivePath, out var parsedDoc));
                Assert.True(parsedDoc!.HasContainerHash);
                string reserialized = HashSidecarService.SerializeParsedSidecarDocument("Super Metroid (JU)", parsedDoc);
                Assert.Contains("# Container:", reserialized);

                var crcOnlyCandidate = new List<FileHashResult>
                {
                    new() { FileName = inner.FileName, Size = inner.Size, Crc32 = inner.Crc32, Md5 = "", Sha1 = "" }
                };
                bool preserve = RomManifestCsvService.ShouldPreserveExistingSidecar(archivePath, crcOnlyCandidate, out var preservedContainer);
                Assert.True(preserve);
                Assert.NotNull(preservedContainer);
                Assert.Equal(inner.Md5, crcOnlyCandidate[0].Md5);
                Assert.Equal(inner.Sha1, crcOnlyCandidate[0].Sha1);

                // 4. 9-column collection_manifest.csv synonym normalization
                string csvPath = Path.Combine(vaultDir, "collection_manifest.csv");
                await File.WriteAllLinesAsync(csvPath, new[]
                {
                    "Game Title,Proper Name,Archive Name,Inner Rom Name,CRC32,SHA1,Verified Status,Compliance,Notes",
                    "\"Super Metroid\",\"Super Metroid (JU)\",\"Super Metroid (JU).7z\",\"Super Metroid (JU).sfc\",\"D63ED5F8\",\"DA957F0D63D14CBCB4BBF7843ADFA5BF1D08DBCF\",\"DAT Match (Redump/No-Intro)\",\"CLEAN\",\"\""
                });
                var loadedCsv = RomManifestCsvService.LoadExistingManifest(csvPath);
                Assert.Single(loadedCsv);
                Assert.Equal("MATCH", loadedCsv.Values.First().MatchStatus);
            }
            finally
            {
                TestCleanupHelper.TryDeleteDirectory(vaultDir);
            }
        }

        [Fact]
        public async Task T15_Standard19PidStartTimeHkLockEviction_AndSisterAppCoexistencePresets_WorkEndToEnd()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), $"hk_coex_{Guid.NewGuid():N}");
            string dataDir = Path.Combine(tempRoot, "data");
            string sharedVault = Path.Combine(tempRoot, "shared_rom_vault");
            Directory.CreateDirectory(dataDir);
            Directory.CreateDirectory(sharedVault);
            try
            {
                // 1. Dead PID .hklock is automatically evicted
                string deadLock = Path.Combine(sharedVault, ".shadowlibrary_999991.hklock");
                File.WriteAllText(deadLock, JsonSerializer.Serialize(new DirectorySentinelLockPayload
                {
                    OwnerAppId = "shadowlibrary",
                    Pid = 999991,
                    Hostname = Environment.MachineName,
                    ProcessStartTimeUtc = DateTime.UtcNow.ToString("O"),
                    ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10).ToString("O")
                }));
                Assert.False(NetworkSafetyService.HasActiveDirectoryOrParentLock(sharedVault, out DirectorySentinelLockPayload? _));
                Assert.False(File.Exists(deadLock));

                // 2. Reused PID with mismatched processStartTimeUtc is automatically evicted
                using var currentProc = Process.GetCurrentProcess();
                string mismatchedStartLock = Path.Combine(sharedVault, $".redroom_{currentProc.Id}.hklock");
                File.WriteAllText(mismatchedStartLock, JsonSerializer.Serialize(new DirectorySentinelLockPayload
                {
                    OwnerAppId = "redroom",
                    Pid = currentProc.Id,
                    Hostname = Environment.MachineName,
                    ProcessStartTimeUtc = currentProc.StartTime.ToUniversalTime().AddHours(-5).ToString("O"),
                    ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10).ToString("O")
                }));
                Assert.False(NetworkSafetyService.HasActiveDirectoryOrParentLock(sharedVault, out DirectorySentinelLockPayload? _));
                Assert.False(File.Exists(mismatchedStartLock));

                // 3. Live sister-app lock with exact PID + processStartTimeUtc is respected!
                string validForeignLock = Path.Combine(sharedVault, $".shadowlibrary_{currentProc.Id}.hklock");
                File.WriteAllText(validForeignLock, JsonSerializer.Serialize(new DirectorySentinelLockPayload
                {
                    OwnerAppId = "shadowlibrary",
                    Pid = currentProc.Id,
                    Hostname = Environment.MachineName,
                    ProcessStartTimeUtc = currentProc.StartTime.ToUniversalTime().ToString("O"),
                    Operation = "vault-audit",
                    ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10).ToString("O")
                }));
                Assert.True(NetworkSafetyService.HasActiveDirectoryOrParentLock(sharedVault, out DirectorySentinelLockPayload? activePayload));
                Assert.Equal("shadowlibrary", activePayload!.AppId);
                File.Delete(validForeignLock);

                // 4. SisterAppCoexistenceService detects .coexistence.json v2 placement conflict and applies snapshot-backed preset
                var manifest = new SharedRootCoexistenceManifest
                {
                    SchemaVersion = 2,
                    AppId = "shadowlibrary",
                    HashFilePlacement = "PackageInsideArchive"
                };
                manifest.Owners["shadowlibrary"] = new SharedRootOwnerEntry
                {
                    AppId = "shadowlibrary",
                    Port = 5179,
                    HashFilePlacement = "PackageInsideArchive",
                    CoexistenceMode = "Primary"
                };
                File.WriteAllText(Path.Combine(sharedVault, ".coexistence.json"), JsonSerializer.Serialize(manifest, ConfigService.JsonOptions));

                var cfgSvc = new ConfigService(customBaseDir: dataDir, isolatedOverride: true);
                cfgSvc.MutateAndSave(c =>
                {
                    c.CollectionManifest.CreateMissingHashFiles = true;
                    c.CollectionManifest.HashFilePlacement = HashSidecarMode.SidecarBeside;
                    c.CollectionManifest.SkipShadowLibraryManagedRoots = false;
                    c.CollectionManifest.TargetFolders.Add(new ManifestTargetFolderRule { Enabled = true, LocationId = "ROMs", FolderPath = sharedVault });
                }, out _);

                using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(200) };
                var coexSvc = new SisterAppCoexistenceService(cfgSvc, http);
                var statusBefore = await coexSvc.GetStatusAsync();
                Assert.True(statusBefore.HasActiveConflicts);
                Assert.Contains(statusBefore.DetectedConflicts, c => c.SisterAppId == "shadowlibrary");

                var applyRes = coexSvc.ApplyRecommendationPreset("delegate_to_shadowlibrary", enableAutoAlign: true);
                Assert.True(applyRes.Ok);
                Assert.True(cfgSvc.Config.AutoAlignWithSisterApps);
                Assert.True(cfgSvc.Config.CollectionManifest.SkipShadowLibraryManagedRoots);
                Assert.NotEmpty(cfgSvc.ListSnapshots());

                var statusAfter = await coexSvc.GetStatusAsync();
                Assert.False(statusAfter.HasActiveConflicts);
            }
            finally
            {
                TestCleanupHelper.TryDeleteDirectory(tempRoot);
            }
        }

        [Fact]
        public void Standard18_BackendCoexistenceDtos_MatchTypeScriptCoexistenceModelsWireKeysOneToOne()
        {
            string tsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Housekeeping.Web", "src", "types", "coexistenceModels.ts"));
            Assert.True(File.Exists(tsPath), $"Missing canonical TS DTO file at {tsPath}");
            string tsSource = File.ReadAllText(tsPath);

            static HashSet<string> ExtractTsInterfaceKeys(string src, string interfaceName)
            {
                var blockMatch = Regex.Match(src, $@"export\s+interface\s+{interfaceName}\s*\{{(?<body>[^}}]+)\}}", RegexOptions.Multiline);
                Assert.True(blockMatch.Success, $"Interface {interfaceName} not found in coexistenceModels.ts");
                return Regex.Matches(blockMatch.Groups["body"].Value, @"^\s*([A-Za-z0-9_]+)\??\s*:", RegexOptions.Multiline)
                    .Select(m => m.Groups[1].Value)
                    .ToHashSet(StringComparer.Ordinal);
            }

            static HashSet<string> ExtractJsonWireKeys<T>(T sample)
            {
                using var doc = JsonDocument.Parse(JsonSerializer.Serialize(sample));
                return doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            }

            Assert.Equal(ExtractTsInterfaceKeys(tsSource, "CoexistenceManagedRootDto"), ExtractJsonWireKeys(new CoexistenceManagedRootDto()));
            Assert.Equal(ExtractTsInterfaceKeys(tsSource, "CoexistencePeerSummaryDto"), ExtractJsonWireKeys(new CoexistencePeerSummaryDto()));
            Assert.Equal(ExtractTsInterfaceKeys(tsSource, "CoexistenceConflictDto"), ExtractJsonWireKeys(new CoexistenceConflictDto()));
            Assert.Equal(ExtractTsInterfaceKeys(tsSource, "CoexistenceStatusDto"), ExtractJsonWireKeys(new CoexistenceStatusDto()));
            Assert.Equal(ExtractTsInterfaceKeys(tsSource, "ApplyCoexistencePresetRequest"), ExtractJsonWireKeys(new ApplyCoexistencePresetRequest()));
        }
    }
}
