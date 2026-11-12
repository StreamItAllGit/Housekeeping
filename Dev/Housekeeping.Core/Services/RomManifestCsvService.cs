using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Models;

namespace Housekeeping.Services
{
    public class RomManifestItem
    {
        public string GameTitle { get; set; } = "";
        public string ProperName { get; set; } = "";
        public string ArchiveFileName { get; set; } = "N/A";
        public string RomOrFolderName { get; set; } = "";
        public string VerifiedLocalCrc32 { get; set; } = "";
        public string VerifiedLocalHash { get; set; } = "";
        public string MatchStatus { get; set; } = "NA"; // "MATCH", "MATCH (CRC32)", "MISMATCH", "NA"
        public string ArchiveCompliance { get; set; } = "CLEAN";
        public string Notes { get; set; } = "";

        // Cache helpers
        public long FileSize { get; set; }
        public long PayloadRomSize { get; set; }
        public DateTime LastModifiedUtc { get; set; }
    }

    public static partial class RomManifestCsvService
    {
        private static readonly string[] CompressedContainerExtensions = { ".chd", ".rvz", ".ciso", ".cso", ".pbp" };
        private static readonly Regex SafeConsoleNameRegex = new(@"[^a-zA-Z0-9_\-]", RegexOptions.Compiled);

        public static async Task GenerateOrUpdateManifestAsync(
            string collectionDir,
            string consoleName,
            RomLauncherGeneratorConfig cfg,
            DatIndex datIndex,
            Action<string, string> log,
            CancellationToken ct = default,
            ConsoleCollectionRule? consoleRule = null,
            ComplianceReportService? reportStore = null)
        {
            if (!cfg.EnableCollectionManifestCsv || string.IsNullOrWhiteSpace(collectionDir) || !Directory.Exists(collectionDir))
                return;

            if (NetworkSafetyService.HasActiveDirectoryOrParentLock(collectionDir, "housekeeping", out string? activeOwner, out string? activeLockFile))
            {
                log("WARN", $"[Coexistence Defer] Skipping ROM manifest update in '{collectionDir}' — active sentinel lock '{activeLockFile}' held by '{activeOwner}'.");
                return;
            }

            using var hkSentinel = NetworkSafetyService.AcquireDirectorySentinelLock(collectionDir, "rom-manifest-audit");

            string manifestFileName = string.IsNullOrWhiteSpace(cfg.ManifestCsvFileName) ? "collection_manifest.csv" : cfg.ManifestCsvFileName;
            string manifestFilePath = Path.Combine(collectionDir, manifestFileName);

            log("INFO", $"Updating ROM verification manifest for {consoleName}: '{manifestFilePath}'...");

            // Load existing manifest for incremental caching, recording when the manifest was last written
            DateTime manifestLastWriteUtc = File.Exists(manifestFilePath)
                ? File.GetLastWriteTimeUtc(manifestFilePath)
                : DateTime.MinValue;
            var existingCache = LoadExistingManifest(manifestFilePath);
            var currentItems = new List<RomManifestItem>();
            var complianceEvalsByPath = new Dictionary<string, TargetComplianceEvaluation>(StringComparer.OrdinalIgnoreCase);
            var manifestItemsByPath = new Dictionary<string, RomManifestItem>(StringComparer.OrdinalIgnoreCase);

            var effectiveRule = consoleRule ?? cfg.Collections.FirstOrDefault(c =>
                string.Equals(c.ConsoleName, consoleName, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(c.ArchivesPath) &&
                 string.Equals(Path.GetFullPath(c.ArchivesPath).TrimEnd('\\', '/'), Path.GetFullPath(collectionDir).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)));

            async Task ApplyComplianceAsync(
                RomManifestItem targetItem,
                string fullPath,
                bool isDir,
                IReadOnlyList<HashSidecarService.ArchiveEntryInfo>? preListed = null)
            {
                manifestItemsByPath[fullPath] = targetItem;
                if (!cfg.EnableArchiveTemplateComplianceScan)
                {
                    targetItem.ArchiveCompliance = "SKIPPED";
                    return;
                }

                var compEval = await ArchiveComplianceService.EvaluateTargetComplianceAsync(
                    fullPath,
                    isDir,
                    effectiveRule,
                    cfg,
                    HashSidecarService.ResolveSevenZipExecutable(),
                    preListed,
                    ct);

                if (compEval.IsEvaluated)
                {
                    targetItem.ArchiveCompliance = compEval.SummaryCode;
                    complianceEvalsByPath[fullPath] = compEval;
                }
                else
                {
                    targetItem.ArchiveCompliance = "SKIPPED";
                }
            }

            var compositeDirs = new List<DirectoryInfo>();
            var multiFileGameDirs = new List<DirectoryInfo>();
            var archives = new List<FileInfo>();
            var standaloneFiles = new List<FileInfo>();

            HashSidecarService.DiscoverDirectoryGameTargets(
                collectionDir,
                cfg.StructureMode,
                compositeDirs,
                multiFileGameDirs,
                archives,
                standaloneFiles);

            // Process Archives
            int aIdx = 0;
            var swTotal = System.Diagnostics.Stopwatch.StartNew();
            var lastCadenceTime = DateTime.UtcNow;
            int lastCadenceCount = 0;
            string sevenZip = HashSidecarService.ResolveSevenZipExecutable();

            foreach (var arch in archives)
            {
                ct.ThrowIfCancellationRequested();
                aIdx++;

                string archiveName = arch.Name;
                string baseTitle = Path.GetFileNameWithoutExtension(arch.Name);
                List<HashSidecarService.ArchiveEntryInfo>? preListedArchiveEntries = null;

                RomManifestItem? cached = null;
                bool hasCachedEntry = !cfg.ForceRehashExistingManifestEntries &&
                    (existingCache.TryGetValue(archiveName, out cached) ||
                     existingCache.TryGetValue(arch.FullName, out cached)) &&
                    cached != null;

                bool cachedHasUsableSha1 = hasCachedEntry &&
                    !string.IsNullOrWhiteSpace(cached!.VerifiedLocalHash) &&
                    cached.VerifiedLocalHash != "N/A" &&
                    cached.VerifiedLocalHash != "ERROR";

                bool cachedHasUsableCrcOnly = hasCachedEntry &&
                    cfg.UseFastArchiveHeaderCrc32Audit &&
                    !string.IsNullOrWhiteSpace(cached!.VerifiedLocalCrc32) &&
                    cached.VerifiedLocalCrc32.Length == 8;

                bool timestampUnchanged = hasCachedEntry &&
                    ((cached!.FileSize == arch.Length && cached.LastModifiedUtc == arch.LastWriteTimeUtc) ||
                     (manifestLastWriteUtc > DateTime.MinValue && arch.LastWriteTimeUtc <= manifestLastWriteUtc));

                // Timestamp-Proof Header CRC32 Validation: if archive timestamp changed (e.g., touched/moved/repacked)
                // but inner single-ROM CRC32 in the 7z/.zip header still matches cached VerifiedLocalCrc32, reuse VerifiedLocalHash!
                bool headerCrcValidatedAfterTimestampChange = false;
                if (!timestampUnchanged &&
                    cachedHasUsableSha1 &&
                    cfg.DeepInspectAndHashInnerRoms &&
                    !string.IsNullOrWhiteSpace(cached!.VerifiedLocalCrc32) &&
                    cached.VerifiedLocalCrc32.Length == 8)
                {
                    preListedArchiveEntries = await HashSidecarService.ListArchiveContentsAsync(sevenZip, arch.FullName, ct);
                    if (HashSidecarService.TryExtractArchivePayloadHeaderHashes(preListedArchiveEntries, out var headerPayloads) &&
                        headerPayloads.Count == 1 &&
                        headerPayloads[0].Crc32.Equals(cached.VerifiedLocalCrc32, StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrWhiteSpace(cached.RomOrFolderName) ||
                         string.Equals(Path.GetFileName(headerPayloads[0].FileName), Path.GetFileName(cached.RomOrFolderName), StringComparison.OrdinalIgnoreCase)))
                    {
                        headerCrcValidatedAfterTimestampChange = true;
                        cached.PayloadRomSize = headerPayloads[0].Size;
                    }
                }

                // Check incremental manifest cache if archive has not been modified (or header CRC32 proved inner ROM unchanged)
                if ((cachedHasUsableSha1 || cachedHasUsableCrcOnly) &&
                    (timestampUnchanged || headerCrcValidatedAfterTimestampChange))
                {
                    bool cachedIsLegacyOuter = cfg.DeepInspectAndHashInnerRoms &&
                        (string.IsNullOrWhiteSpace(cached!.RomOrFolderName) ||
                         cached.RomOrFolderName.Equals(arch.Name, StringComparison.OrdinalIgnoreCase) ||
                         HashSidecarService.IsArchiveFilePath(cached.RomOrFolderName));

                    if (!cachedIsLegacyOuter)
                    {
                        bool canUseCachedRow = true;
                        SidecarItemVerificationResult? cachedVerify = null;

                        if (!cachedHasUsableSha1)
                        {
                            // Fast Header CRC32 cached row: skip .hash sidecar writes since SHA-1 hasn't been computed yet
                        }
                        else if (cfg.HashFilePlacement == HashSidecarMode.PackageInsideArchive &&
                            HashSidecarService.IsWritableArchiveFilePath(arch.FullName))
                        {
                            cachedVerify = await HashSidecarService.VerifySidecarForTargetAsync(
                                sevenZip,
                                arch.FullName,
                                expectInnerRom: cfg.DeepInspectAndHashInnerRoms,
                                deepCryptographicVerify: false,
                                scratchDir: cfg.TempExtractionScratchPath,
                                structureMode: cfg.StructureMode,
                                ct: ct);

                            if (cachedVerify.Status == SidecarVerificationStatus.Valid &&
                                cachedVerify.Location == SidecarLocation.InternalArchive)
                            {
                                // Valid internal .hash already present; keep archive untouched
                            }
                            else if (cachedVerify.Status == SidecarVerificationStatus.Valid &&
                                     cachedVerify.Location == SidecarLocation.Both)
                            {
                                foreach (var extSidecar in HashSidecarService.GetExistingExternalSidecarPaths(arch.FullName))
                                {
                                    try { File.Delete(extSidecar); } catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[RomManifestCsvService] Non-fatal error ignored: {__suppressedEx1.Message}"); }
                                }
                            }
                            else if (cachedVerify.Status == SidecarVerificationStatus.Valid &&
                                     cachedVerify.Location == SidecarLocation.ExternalBeside &&
                                     cachedVerify.ParsedDocument != null)
                            {
                                // Migrate verified full multi-file or composite sidecar document into archive without re-decompressing ROMs
                                string tempDir = Path.Combine(Path.GetTempPath(), "hk_hash_" + Guid.NewGuid().ToString("N"));
                                Directory.CreateDirectory(tempDir);
                                string tempSidecar = Path.Combine(tempDir, $"{baseTitle}.hash");
                                try
                                {
                                    string sidecarContent = HashSidecarService.SerializeParsedSidecarDocument(baseTitle, cachedVerify.ParsedDocument);
                                    await File.WriteAllTextAsync(tempSidecar, sidecarContent, Encoding.UTF8, ct);
                                    if (await HashSidecarService.InjectSidecarIntoArchiveAsync(sevenZip, arch.FullName, tempSidecar, ct))
                                    {
                                        foreach (var extSidecar in HashSidecarService.GetExistingExternalSidecarPaths(arch.FullName))
                                        {
                                            try { File.Delete(extSidecar); } catch (System.Exception __suppressedEx2) { System.Diagnostics.Debug.WriteLine($"[RomManifestCsvService] Non-fatal error ignored: {__suppressedEx2.Message}"); }
                                        }
                                        arch.Refresh();
                                    }
                                }
                                catch (System.Exception __suppressedEx3) { System.Diagnostics.Debug.WriteLine($"[RomManifestCsvService] Non-fatal error ignored: {__suppressedEx3.Message}"); }
                                finally
                                {
                                    try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch (System.Exception __suppressedEx4) { System.Diagnostics.Debug.WriteLine($"[RomManifestCsvService] Non-fatal error ignored: {__suppressedEx4.Message}"); }
                                }
                            }
                            else
                            {
                                // Sidecar is missing, legacy outer-archive, incomplete multi-file set, or header-mismatched -> fall through to rehash & heal
                                if (cachedVerify.Status is SidecarVerificationStatus.LegacyOuterArchiveHash
                                    or SidecarVerificationStatus.IncompleteMultiFileSet
                                    or SidecarVerificationStatus.HeaderMismatch)
                                {
                                    log("WARN", $"[Healing Stale .hash] '{arch.Name}' -> {cachedVerify.DetailMessage}");
                                }
                                canUseCachedRow = false;
                            }
                        }
                        else if (cfg.HashFilePlacement == HashSidecarMode.SidecarBeside ||
                                 (cfg.HashFilePlacement == HashSidecarMode.PackageInsideArchive && !HashSidecarService.IsWritableArchiveFilePath(arch.FullName)))
                        {
                            if (HashSidecarService.GetExistingExternalSidecarPaths(arch.FullName).Count > 0)
                            {
                                if (cfg.DeepInspectAndHashInnerRoms)
                                {
                                    cachedVerify = await HashSidecarService.VerifySidecarForTargetAsync(
                                        sevenZip,
                                        arch.FullName,
                                        expectInnerRom: true,
                                        deepCryptographicVerify: false,
                                        scratchDir: cfg.TempExtractionScratchPath,
                                        structureMode: cfg.StructureMode,
                                        ct: ct);

                                    if (cachedVerify.Status != SidecarVerificationStatus.Valid)
                                    {
                                        log("WARN", $"[Healing Stale .hash] '{arch.Name}' -> {cachedVerify.DetailMessage}");
                                        canUseCachedRow = false;
                                    }
                                }
                            }
                            else
                            {
                                try
                                {
                                    if (cfg.DeepInspectAndHashInnerRoms)
                                    {
                                        preListedArchiveEntries ??= await HashSidecarService.ListArchiveContentsAsync(sevenZip, arch.FullName, ct);
                                        var archEntries = preListedArchiveEntries;
                                        var nonDirPaths = archEntries
                                            .Where(e => !e.IsDirectory && !string.IsNullOrWhiteSpace(e.Path))
                                            .Select(e => e.Path)
                                            .ToList();
                                        var archTopology = ReleaseStructureDetector.ClassifyArchiveEntries(nonDirPaths, cfg.StructureMode);

                                        if (archTopology == ReleaseTopology.CompositeFolderDump)
                                        {
                                            var comp = HashSidecarService.BuildCompositeHashFromArchiveEntries(baseTitle, arch.FullName, archEntries);
                                            string scContent = HashSidecarService.GenerateSidecarContentForFolder(comp);
                                            await File.WriteAllTextAsync(HashSidecarService.GetStandardSidecarPath(arch.FullName), scContent, Encoding.UTF8, ct);
                                            cached!.VerifiedLocalHash = comp.CompositeSha1;
                                            cached.VerifiedLocalCrc32 = "COMPOSITE";
                                        }
                                        else
                                        {
                                            var payloadEntries = HashSidecarService.GetNormalizedPayloadArchiveEntries(archEntries);
                                            if (payloadEntries.Count == 1)
                                            {
                                                var (primary, normRel) = payloadEntries[0];
                                                cached!.VerifiedLocalCrc32 = primary.Crc;
                                                cached.PayloadRomSize = primary.Size;
                                                var scItem = new FileHashResult
                                                {
                                                    FileName = normRel,
                                                    RelativePath = normRel,
                                                    Size = primary.Size,
                                                    Crc32 = primary.Crc,
                                                    Sha1 = cached.VerifiedLocalHash
                                                };
                                                string scContent = HashSidecarService.GenerateSidecarContentForFiles(baseTitle, new[] { scItem });
                                                await File.WriteAllTextAsync(HashSidecarService.GetStandardSidecarPath(arch.FullName), scContent, Encoding.UTF8, ct);
                                            }
                                            else
                                            {
                                                // Multi-file archive needs full per-track hashing to write all FILE: entries
                                                canUseCachedRow = false;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        var scItem = new FileHashResult
                                        {
                                            FileName = arch.Name,
                                            RelativePath = arch.Name,
                                            Size = arch.Length,
                                            Sha1 = cached!.VerifiedLocalHash
                                        };
                                        string scContent = HashSidecarService.GenerateSidecarContentForFiles(baseTitle, new[] { scItem });
                                        await File.WriteAllTextAsync(HashSidecarService.GetStandardSidecarPath(arch.FullName), scContent, Encoding.UTF8, ct);
                                    }
                                }
                                catch (Exception scEx)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[RomManifestCsvService] Cached sidecar backfill skipped: {scEx.Message}");
                                    if (cfg.DeepInspectAndHashInnerRoms)
                                        canUseCachedRow = false;
                                }
                            }
                        }

                        if (canUseCachedRow)
                        {
                            cached!.FileSize = arch.Length;
                            cached.LastModifiedUtc = arch.LastWriteTimeUtc;

                            // Zero-decompression CRC32 auto-backfill for legacy 7-column CSV rows
                            if (string.IsNullOrWhiteSpace(cached.VerifiedLocalCrc32) && cfg.DeepInspectAndHashInnerRoms)
                            {
                                if (cachedVerify?.Topology == ReleaseTopology.CompositeFolderDump ||
                                    cachedVerify?.ParsedDocument?.IsCompositeFolder == true)
                                {
                                    cached.VerifiedLocalCrc32 = "COMPOSITE";
                                }
                                else if (cachedVerify?.SidecarHash != null && !string.IsNullOrWhiteSpace(cachedVerify.SidecarHash.Crc32))
                                {
                                    cached.VerifiedLocalCrc32 = cachedVerify.SidecarHash.Crc32;
                                    cached.PayloadRomSize = cachedVerify.SidecarHash.Size;
                                }
                                else
                                {
                                    preListedArchiveEntries ??= await HashSidecarService.ListArchiveContentsAsync(sevenZip, arch.FullName, ct);
                                    var primaryEntry = HashSidecarService.SelectPrimaryRomEntry(preListedArchiveEntries);
                                    if (primaryEntry != null && !string.IsNullOrWhiteSpace(primaryEntry.Crc))
                                    {
                                        cached.VerifiedLocalCrc32 = primaryEntry.Crc;
                                        cached.PayloadRomSize = primaryEntry.Size;
                                    }
                                }
                            }

                            if (cachedVerify?.Topology == ReleaseTopology.CompositeFolderDump ||
                                cachedVerify?.ParsedDocument?.IsCompositeFolder == true)
                            {
                                EvaluateCompositeDumpMatchStatus(
                                    cached,
                                    cachedVerify.ParsedDocument?.DumpType ?? "",
                                    cachedVerify.ParsedDocument?.TotalFiles ?? cachedVerify.SidecarFiles.Count,
                                    datIndex);
                            }
                            else if (cachedVerify != null && cachedVerify.SidecarFiles.Count > 1)
                            {
                                EvaluateMultiFileMatchStatus(cached, cachedVerify.SidecarFiles, datIndex, arch.Extension);
                            }
                            else
                            {
                                EvaluateMatchStatus(cached, datIndex, Path.GetExtension(cached.RomOrFolderName));
                            }

                            await ApplyComplianceAsync(cached, arch.FullName, isDir: false, preListedArchiveEntries);
                            currentItems.Add(cached);
                            continue;
                        }
                    }
                    else
                    {
                        log("WARN", $"[Healing Legacy CSV Entry] '{arch.Name}' recorded outer archive '{cached!.RomOrFolderName}' instead of inner ROM; re-hashing...");
                    }
                }

                // Verified sidecar check (bypassed when ForceRehashExistingManifestEntries is true)
                SidecarItemVerificationResult? sidecarVerify = null;
                if (cfg.TrustExistingHashFiles && !cfg.ForceRehashExistingManifestEntries)
                {
                    sidecarVerify = await HashSidecarService.VerifySidecarForTargetAsync(
                        sevenZip,
                        arch.FullName,
                        expectInnerRom: cfg.DeepInspectAndHashInnerRoms,
                        deepCryptographicVerify: false,
                        scratchDir: cfg.TempExtractionScratchPath,
                        structureMode: cfg.StructureMode,
                        ct: ct);

                    if (sidecarVerify.Status is SidecarVerificationStatus.LegacyOuterArchiveHash
                        or SidecarVerificationStatus.IncompleteMultiFileSet
                        or SidecarVerificationStatus.HeaderMismatch)
                    {
                        log("WARN", $"[Healing Stale .hash] '{arch.Name}' -> {sidecarVerify.DetailMessage}");
                    }
                }

                var item = new RomManifestItem
                {
                    GameTitle = baseTitle,
                    ArchiveFileName = archiveName,
                    FileSize = arch.Length,
                    LastModifiedUtc = arch.LastWriteTimeUtc
                };

                bool isLarge = arch.Length >= (250 * 1024 * 1024);
                var swFile = isLarge ? System.Diagnostics.Stopwatch.StartNew() : null;

                if (isLarge)
                {
                    log("WORK", $"[Hashing Large ROM] '{arch.Name}' ({arch.Length / (1024.0 * 1024 * 1024):F2} GB) [SHA1]...");
                }

                string sidecarContentToPersist = "";
                bool sidecarAlreadyVerified = sidecarVerify?.Status == SidecarVerificationStatus.Valid;

                if (cfg.DeepInspectAndHashInnerRoms)
                {
                    List<HashSidecarService.ArchiveEntryInfo>? listedEntries = preListedArchiveEntries;
                    ReleaseTopology archTopology;
                    if (sidecarVerify != null)
                    {
                        archTopology = sidecarVerify.Topology;
                    }
                    else
                    {
                        listedEntries ??= await HashSidecarService.ListArchiveContentsAsync(sevenZip, arch.FullName, ct);
                        var nonDir = listedEntries
                            .Where(e => !e.IsDirectory && !string.IsNullOrWhiteSpace(e.Path))
                            .Select(e => e.Path)
                            .ToList();
                        archTopology = ReleaseStructureDetector.ClassifyArchiveEntries(nonDir, cfg.StructureMode);
                    }

                    if (archTopology == ReleaseTopology.CompositeFolderDump)
                    {
                        listedEntries ??= await HashSidecarService.ListArchiveContentsAsync(sevenZip, arch.FullName, ct);
                        var comp = HashSidecarService.BuildCompositeHashFromArchiveEntries(baseTitle, arch.FullName, listedEntries);
                        item.RomOrFolderName = baseTitle;
                        item.VerifiedLocalCrc32 = "COMPOSITE";
                        item.VerifiedLocalHash = comp.CompositeSha1;
                        EvaluateCompositeDumpMatchStatus(item, comp.DumpType, comp.TotalFiles, datIndex);
                        sidecarContentToPersist = HashSidecarService.GenerateSidecarContentForFolder(comp);
                    }
                    else
                    {
                        List<FileHashResult> payloadFiles;
                        if (sidecarAlreadyVerified && sidecarVerify!.SidecarFiles.Count > 0)
                        {
                            payloadFiles = sidecarVerify.SidecarFiles;
                        }
                        else if (cfg.UseFastArchiveHeaderCrc32Audit)
                        {
                            listedEntries ??= await HashSidecarService.ListArchiveContentsAsync(sevenZip, arch.FullName, ct);
                            if (!HashSidecarService.TryExtractArchivePayloadHeaderHashes(listedEntries, out payloadFiles))
                            {
                                payloadFiles = await HashSidecarService.HashArchivePayloadFilesAsync(
                                    sevenZip,
                                    arch.FullName,
                                    cfg.TempExtractionScratchPath,
                                    ct: ct);
                            }
                        }
                        else
                        {
                            var reusable = (sidecarVerify?.Status == SidecarVerificationStatus.IncompleteMultiFileSet &&
                                            !cfg.ForceRehashExistingManifestEntries)
                                ? sidecarVerify.SidecarFiles
                                : null;

                            payloadFiles = await HashSidecarService.HashArchivePayloadFilesAsync(
                                sevenZip,
                                arch.FullName,
                                cfg.TempExtractionScratchPath,
                                reusableVerifiedFiles: reusable,
                                ct: ct);
                        }

                        if (!cfg.UseFastArchiveHeaderCrc32Audit)
                        {
                            TryBackfillHashesFromDat(payloadFiles, datIndex, baseTitle);
                        }
                        bool preserveRicherSidecar = ShouldPreserveExistingSidecar(arch.FullName, payloadFiles, out var preservedContainer);

                        var primaryFile = payloadFiles
                            .OrderByDescending(f => !ReleaseStructureDetector.IsDiscDescriptorFile(f.FileName))
                            .ThenByDescending(f => f.Size)
                            .First();

                        item.RomOrFolderName = Path.GetFileName(primaryFile.FileName);
                        item.VerifiedLocalCrc32 = primaryFile.Crc32;
                        item.PayloadRomSize = primaryFile.Size;
                        item.VerifiedLocalHash = primaryFile.Sha1;
                        EvaluateMultiFileMatchStatus(item, payloadFiles, datIndex, Path.GetExtension(primaryFile.FileName));

                        if (!preserveRicherSidecar && payloadFiles.All(f => !string.IsNullOrWhiteSpace(f.Sha1)))
                        {
                            var containerToPersist = preservedContainer
                                ?? HashSidecarService.ReconstructContainerHashFromDocument(sidecarVerify?.ParsedDocument, arch.Name)
                                ?? await HashSidecarService.ComputeFileHashesAsync(arch.FullName, ct);
                            sidecarContentToPersist = HashSidecarService.GenerateSidecarContentForFiles(baseTitle, payloadFiles, containerToPersist);
                        }
                    }

                    await ApplyComplianceAsync(item, arch.FullName, isDir: false, listedEntries);
                }
                else
                {
                    FileHashResult outerResult;
                    if (sidecarAlreadyVerified && sidecarVerify?.SidecarHash != null && !string.IsNullOrEmpty(sidecarVerify.SidecarHash.Sha1))
                    {
                        outerResult = sidecarVerify.SidecarHash;
                    }
                    else
                    {
                        outerResult = await HashSidecarService.ComputeFileHashesAsync(arch.FullName, ct);
                    }

                    item.RomOrFolderName = GuessInnerRomNameFromArchive(arch);
                    item.VerifiedLocalCrc32 = outerResult.Crc32;
                    item.PayloadRomSize = outerResult.Size;
                    item.VerifiedLocalHash = outerResult.Sha1;
                    EvaluateMatchStatus(item, datIndex, arch.Extension);
                    if (!ShouldPreserveExistingSidecar(arch.FullName, new[] { outerResult }, out _))
                    {
                        sidecarContentToPersist = HashSidecarService.GenerateSidecarContentForFiles(baseTitle, new[] { outerResult }, outerResult);
                    }
                    await ApplyComplianceAsync(item, arch.FullName, isDir: false, null);
                }

                if (isLarge && swFile != null)
                {
                    double mbps = (arch.Length / (1024.0 * 1024.0)) / Math.Max(0.01, swFile.Elapsed.TotalSeconds);
                    log("SUCCESS", $"[Hashed Large ROM] '{arch.Name}' ({arch.Length / (1024.0 * 1024 * 1024):F2} GB) in {swFile.Elapsed.TotalSeconds:F1}s ({mbps:F1} MB/s) • Status: {item.MatchStatus}");
                }

                // Cadence log every 50 ROMs or 5 seconds
                var now = DateTime.UtcNow;
                if (aIdx % 50 == 0 || (now - lastCadenceTime).TotalSeconds >= 5.0)
                {
                    double rate = (aIdx - lastCadenceCount) / Math.Max(0.1, (now - lastCadenceTime).TotalSeconds);
                    log("INFO", $"[ROM Manifest] Auditing [{aIdx}/{archives.Count}] ({(aIdx * 100.0 / archives.Count):F1}%) • '{arch.Name}' • {rate:F1} roms/s");
                    lastCadenceTime = now;
                    lastCadenceCount = aIdx;
                }

                // Generate / package .hash sidecar if configured
                if (cfg.HashFilePlacement != HashSidecarMode.None &&
                    !string.IsNullOrEmpty(item.VerifiedLocalHash) &&
                    !string.IsNullOrEmpty(sidecarContentToPersist))
                {
                    try
                    {
                        string standardSidecarPath = HashSidecarService.GetStandardSidecarPath(arch.FullName);
                        bool writableArchive = HashSidecarService.IsWritableArchiveFilePath(arch.FullName);
                        bool writeExternal = cfg.HashFilePlacement is HashSidecarMode.SidecarBeside or HashSidecarMode.SidecarAndArchive || !writableArchive;
                        bool writeInternal = cfg.HashFilePlacement is HashSidecarMode.PackageInsideArchive or HashSidecarMode.SidecarAndArchive && writableArchive;

                        if (writeExternal)
                        {
                            if (!writeInternal && writableArchive &&
                                sidecarVerify != null &&
                                sidecarVerify.Status != SidecarVerificationStatus.Valid &&
                                (sidecarVerify.Location == SidecarLocation.InternalArchive || sidecarVerify.Location == SidecarLocation.Both))
                            {
                                await HashSidecarService.DeleteArchiveInternalHashFilesAsync(sevenZip, arch.FullName, ct);
                                arch.Refresh();
                                item.FileSize = arch.Length;
                                item.LastModifiedUtc = arch.LastWriteTimeUtc;
                            }

                            if (!(sidecarAlreadyVerified && File.Exists(standardSidecarPath)))
                            {
                                await File.WriteAllTextAsync(standardSidecarPath, sidecarContentToPersist, new UTF8Encoding(false), ct);
                            }
                        }

                        if (writeInternal)
                        {
                            bool alreadyValidInside = sidecarAlreadyVerified &&
                                sidecarVerify != null &&
                                (sidecarVerify.Location == SidecarLocation.InternalArchive || sidecarVerify.Location == SidecarLocation.Both);

                            if (alreadyValidInside)
                            {
                                if (!writeExternal)
                                {
                                    foreach (var extSidecar in HashSidecarService.GetExistingExternalSidecarPaths(arch.FullName))
                                        NetworkSafetyService.TryDeleteFile(extSidecar);
                                }
                            }
                            else
                            {
                                if (cfg.ForceRehashExistingManifestEntries ||
                                    (sidecarVerify != null &&
                                     (sidecarVerify.Location == SidecarLocation.InternalArchive || sidecarVerify.Location == SidecarLocation.Both)))
                                {
                                    await HashSidecarService.DeleteArchiveInternalHashFilesAsync(sevenZip, arch.FullName, ct, syncPostMutationContainerHashJson: false);
                                }

                                string tempDir = Path.Combine(Path.GetTempPath(), "hk_hash_" + Guid.NewGuid().ToString("N"));
                                Directory.CreateDirectory(tempDir);
                                string tempSidecar = Path.Combine(tempDir, $"{baseTitle}.hash");
                                try
                                {
                                    await File.WriteAllTextAsync(tempSidecar, sidecarContentToPersist, new UTF8Encoding(false), ct);
                                    bool injected = await HashSidecarService.InjectSidecarIntoArchiveAsync(sevenZip, arch.FullName, tempSidecar, ct);
                                    if (injected)
                                    {
                                        if (!writeExternal)
                                        {
                                            foreach (var extSidecar in HashSidecarService.GetExistingExternalSidecarPaths(arch.FullName))
                                                NetworkSafetyService.TryDeleteFile(extSidecar);
                                        }
                                        arch.Refresh();
                                        item.FileSize = arch.Length;
                                        item.LastModifiedUtc = arch.LastWriteTimeUtc;
                                    }
                                }
                                finally
                                {
                                    try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch (System.Exception __suppressedEx7) { System.Diagnostics.Debug.WriteLine($"[RomManifestCsvService] Non-fatal error ignored: {__suppressedEx7.Message}"); }
                                }
                            }
                        }
                    }
                    catch (System.Exception __suppressedEx8) { System.Diagnostics.Debug.WriteLine($"[RomManifestCsvService] Non-fatal error ignored: {__suppressedEx8.Message}"); }
                }

                currentItems.Add(item);
            }

            // Process Unpacked Cohesive Multi-File Game Folders (PS1 / Sega CD / Dreamcast / Multi-Disc Subfolders)
            int mIdx = 0;
            foreach (var gameDir in multiFileGameDirs)
            {
                ct.ThrowIfCancellationRequested();
                mIdx++;

                string folderName = gameDir.Name;
                string parentDir = gameDir.Parent?.FullName ?? collectionDir;

                var diskPayloadFiles = gameDir.EnumerateFiles("*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true })
                    .Where(f => f.Length >= 0 &&
                                !HashSidecarService.IsArchiveFilePath(f.FullName) &&
                                !ReleaseStructureDetector.IsIgnoredNonPayloadFile(f.Name))
                    .ToList();

                long folderTotalSize = diskPayloadFiles.Sum(f => f.Length);
                DateTime folderLatestWriteUtc = diskPayloadFiles.Count > 0
                    ? diskPayloadFiles.Max(f => f.LastWriteTimeUtc)
                    : gameDir.LastWriteTimeUtc;
                if (gameDir.LastWriteTimeUtc > folderLatestWriteUtc)
                    folderLatestWriteUtc = gameDir.LastWriteTimeUtc;

                if (!cfg.ForceRehashExistingManifestEntries &&
                    (existingCache.TryGetValue(folderName, out var cachedFolder) ||
                     existingCache.TryGetValue(gameDir.FullName, out cachedFolder)) &&
                    !string.IsNullOrWhiteSpace(cachedFolder.VerifiedLocalHash) &&
                    ((cachedFolder.FileSize == folderTotalSize && cachedFolder.LastModifiedUtc == folderLatestWriteUtc) ||
                     (manifestLastWriteUtc > DateTime.MinValue && folderLatestWriteUtc <= manifestLastWriteUtc)))
                {
                    bool canReuseFolderRow = true;
                    SidecarItemVerificationResult? cachedFolderVerify = null;

                    if (cfg.HashFilePlacement != HashSidecarMode.None)
                    {
                        cachedFolderVerify = await HashSidecarService.VerifySidecarForTargetAsync(
                            sevenZip,
                            gameDir.FullName,
                            expectInnerRom: false,
                            deepCryptographicVerify: false,
                            scratchDir: cfg.TempExtractionScratchPath,
                            structureMode: cfg.StructureMode,
                            ct: ct);

                        if (cachedFolderVerify.Status != SidecarVerificationStatus.Valid)
                            canReuseFolderRow = false;
                    }

                    if (canReuseFolderRow)
                    {
                        HashSidecarService.CleanupStrayInternalFolderHashFiles(gameDir.FullName);
                        cachedFolder.FileSize = folderTotalSize;
                        cachedFolder.LastModifiedUtc = folderLatestWriteUtc;
                        if (string.IsNullOrWhiteSpace(cachedFolder.VerifiedLocalCrc32) && cachedFolderVerify?.SidecarHash != null)
                        {
                            cachedFolder.VerifiedLocalCrc32 = cachedFolderVerify.SidecarHash.Crc32;
                            cachedFolder.PayloadRomSize = cachedFolderVerify.SidecarHash.Size;
                        }
                        if (cachedFolderVerify != null && cachedFolderVerify.SidecarFiles.Count > 1)
                            EvaluateMultiFileMatchStatus(cachedFolder, cachedFolderVerify.SidecarFiles, datIndex, "");
                        else
                            EvaluateMatchStatus(cachedFolder, datIndex, "");
                        await ApplyComplianceAsync(cachedFolder, gameDir.FullName, isDir: true, null);
                        currentItems.Add(cachedFolder);
                        continue;
                    }
                }

                log("WORK", $"[Hashing Multi-File Game Folder {mIdx}/{multiFileGameDirs.Count}] '{folderName}' ({diskPayloadFiles.Count} files)...");

                SidecarItemVerificationResult? multiVerify = null;
                if (cfg.TrustExistingHashFiles && !cfg.ForceRehashExistingManifestEntries)
                {
                    multiVerify = await HashSidecarService.VerifySidecarForTargetAsync(
                        sevenZip,
                        gameDir.FullName,
                        expectInnerRom: false,
                        deepCryptographicVerify: false,
                        scratchDir: cfg.TempExtractionScratchPath,
                        structureMode: cfg.StructureMode,
                        ct: ct);
                }

                List<FileHashResult> folderHashes;
                if (multiVerify?.Status == SidecarVerificationStatus.Valid && multiVerify.SidecarFiles.Count > 0)
                {
                    folderHashes = multiVerify.SidecarFiles;
                }
                else
                {
                    folderHashes = await HashSidecarService.HashMultiFileGameFolderAsync(gameDir.FullName, ct);
                    if (cfg.HashFilePlacement != HashSidecarMode.None && folderHashes.Count > 0)
                    {
                        await HashSidecarService.CreateOrUpdateSidecarFileAsync(parentDir, folderName, folderHashes, ct);
                    }
                }

                if (cfg.HashFilePlacement != HashSidecarMode.None)
                {
                    HashSidecarService.CleanupStrayInternalFolderHashFiles(gameDir.FullName);
                }

                var primaryTrack = folderHashes
                    .OrderByDescending(f => !ReleaseStructureDetector.IsDiscDescriptorFile(f.FileName))
                    .ThenByDescending(f => f.Size)
                    .FirstOrDefault();

                var folderItem = new RomManifestItem
                {
                    GameTitle = folderName,
                    ArchiveFileName = "N/A",
                    RomOrFolderName = folderName,
                    FileSize = folderTotalSize,
                    PayloadRomSize = primaryTrack?.Size ?? 0,
                    LastModifiedUtc = folderLatestWriteUtc,
                    VerifiedLocalCrc32 = primaryTrack?.Crc32 ?? "",
                    VerifiedLocalHash = primaryTrack?.Sha1 ?? ""
                };

                EvaluateMultiFileMatchStatus(
                    folderItem,
                    folderHashes,
                    datIndex,
                    primaryTrack != null ? Path.GetExtension(primaryTrack.FileName) : "");

                await ApplyComplianceAsync(folderItem, gameDir.FullName, isDir: true, null);
                currentItems.Add(folderItem);
            }

            // Process Composite Folder Dumps (PS3 / PS4 / PS5 / Wii U / 3DS / Xbox 360)
            int fIdx = 0;
            foreach (var compDir in compositeDirs)
            {
                ct.ThrowIfCancellationRequested();
                fIdx++;

                string folderName = compDir.Name;
                string parentDir = compDir.Parent?.FullName ?? collectionDir;

                if (!cfg.ForceRehashExistingManifestEntries &&
                    (existingCache.TryGetValue(folderName, out var cached) ||
                     existingCache.TryGetValue(compDir.FullName, out cached)) &&
                    !string.IsNullOrWhiteSpace(cached.VerifiedLocalHash) &&
                    ((cached.LastModifiedUtc == compDir.LastWriteTimeUtc) ||
                     (manifestLastWriteUtc > DateTime.MinValue && compDir.LastWriteTimeUtc <= manifestLastWriteUtc)))
                {
                    bool canReuseComp = true;
                    SidecarItemVerificationResult? cachedCompVerify = null;

                    if (cfg.HashFilePlacement != HashSidecarMode.None)
                    {
                        cachedCompVerify = await HashSidecarService.VerifySidecarForTargetAsync(
                            sevenZip,
                            compDir.FullName,
                            expectInnerRom: false,
                            deepCryptographicVerify: false,
                            scratchDir: cfg.TempExtractionScratchPath,
                            structureMode: cfg.StructureMode,
                            ct: ct);

                        if (cachedCompVerify.Status != SidecarVerificationStatus.Valid)
                            canReuseComp = false;
                    }

                    if (canReuseComp)
                    {
                        HashSidecarService.CleanupStrayInternalFolderHashFiles(compDir.FullName);
                        cached.LastModifiedUtc = compDir.LastWriteTimeUtc;
                        if (string.IsNullOrWhiteSpace(cached.VerifiedLocalCrc32))
                            cached.VerifiedLocalCrc32 = "COMPOSITE";
                        EvaluateCompositeDumpMatchStatus(
                            cached,
                            cachedCompVerify?.ParsedDocument?.DumpType ?? ReleaseStructureDetector.DetectFolderDumpType(compDir.FullName) ?? "",
                            cachedCompVerify?.ParsedDocument?.TotalFiles ?? cachedCompVerify?.SidecarFiles.Count ?? 0,
                            datIndex);
                        await ApplyComplianceAsync(cached, compDir.FullName, isDir: true, null);
                        currentItems.Add(cached);
                        continue;
                    }
                }

                log("WORK", $"[Hashing Folder Dump {fIdx}/{compositeDirs.Count}] '{folderName}'...");
                string folderSha1;
                long totalSize;
                string dumpType;
                int totalFiles;

                SidecarItemVerificationResult? folderVerify = null;
                if (cfg.TrustExistingHashFiles && !cfg.ForceRehashExistingManifestEntries)
                {
                    folderVerify = await HashSidecarService.VerifySidecarForTargetAsync(
                        sevenZip,
                        compDir.FullName,
                        expectInnerRom: false,
                        deepCryptographicVerify: false,
                        scratchDir: cfg.TempExtractionScratchPath,
                        structureMode: cfg.StructureMode,
                        ct: ct);
                }

                if (folderVerify?.Status == SidecarVerificationStatus.Valid &&
                    folderVerify.SidecarHash != null &&
                    !string.IsNullOrEmpty(folderVerify.SidecarHash.Sha1))
                {
                    folderSha1 = folderVerify.SidecarHash.Sha1;
                    totalSize = folderVerify.SidecarHash.Size;
                    dumpType = folderVerify.ParsedDocument?.DumpType ?? ReleaseStructureDetector.DetectFolderDumpType(compDir.FullName) ?? "";
                    totalFiles = folderVerify.ParsedDocument?.TotalFiles ?? folderVerify.SidecarFiles.Count;
                }
                else
                {
                    var compResult = await HashSidecarService.ComputeFolderCompositeHashAsync(compDir.FullName, ct);
                    folderSha1 = compResult.CompositeSha1;
                    totalSize = compResult.TotalSizeBytes;
                    dumpType = compResult.DumpType;
                    totalFiles = compResult.TotalFiles;

                    if (cfg.HashFilePlacement != HashSidecarMode.None)
                    {
                        await HashSidecarService.CreateFolderSidecarFileAsync(parentDir, compResult, ct);
                    }
                }

                if (cfg.HashFilePlacement != HashSidecarMode.None)
                {
                    HashSidecarService.CleanupStrayInternalFolderHashFiles(compDir.FullName);
                }

                var item = new RomManifestItem
                {
                    GameTitle = folderName,
                    ArchiveFileName = "N/A",
                    RomOrFolderName = folderName,
                    FileSize = totalSize,
                    LastModifiedUtc = compDir.LastWriteTimeUtc,
                    VerifiedLocalCrc32 = "COMPOSITE",
                    VerifiedLocalHash = folderSha1
                };

                EvaluateCompositeDumpMatchStatus(item, dumpType, totalFiles, datIndex);
                await ApplyComplianceAsync(item, compDir.FullName, isDir: true, null);

                currentItems.Add(item);
            }

            // Process Standalone Files
            int sIdx = 0;
            foreach (var file in standaloneFiles)
            {
                ct.ThrowIfCancellationRequested();
                sIdx++;

                if (!cfg.ForceRehashExistingManifestEntries &&
                    (existingCache.TryGetValue(file.Name, out var cached) ||
                     existingCache.TryGetValue(file.FullName, out cached)) &&
                    !string.IsNullOrWhiteSpace(cached.VerifiedLocalHash) &&
                    cached.VerifiedLocalHash != "ERROR" &&
                    ((cached.FileSize == file.Length && cached.LastModifiedUtc == file.LastWriteTimeUtc) ||
                     (manifestLastWriteUtc > DateTime.MinValue && file.LastWriteTimeUtc <= manifestLastWriteUtc)))
                {
                    cached.FileSize = file.Length;
                    cached.PayloadRomSize = file.Length;
                    cached.LastModifiedUtc = file.LastWriteTimeUtc;
                    if (!CompressedContainerExtensions.Contains(file.Extension.ToLowerInvariant()))
                    {
                        EvaluateMatchStatus(cached, datIndex, file.Extension);
                    }
                    await ApplyComplianceAsync(cached, file.FullName, isDir: false, null);
                    currentItems.Add(cached);
                    continue;
                }

                string baseTitle = Path.GetFileNameWithoutExtension(file.Name);
                var item = new RomManifestItem
                {
                    GameTitle = baseTitle,
                    ArchiveFileName = "N/A",
                    RomOrFolderName = file.Name,
                    FileSize = file.Length,
                    PayloadRomSize = file.Length,
                    LastModifiedUtc = file.LastWriteTimeUtc
                };

                // Check if compressed modern format like CHD/RVZ
                string ext = file.Extension.ToLowerInvariant();
                if (CompressedContainerExtensions.Contains(ext))
                {
                    item.VerifiedLocalCrc32 = "N/A";
                    item.VerifiedLocalHash = "N/A";
                    item.MatchStatus = "NA";
                    item.Notes = ext switch
                    {
                        ".chd" => "CHD compressed disc image; Redump indexes uncompressed BIN/CUE",
                        ".rvz" => "RVZ compressed disc image; Redump indexes raw ISO",
                        ".ciso" or ".cso" => "Compressed ISO format; Redump indexes uncompressed ISO",
                        ".pbp" => "PSP EBOOT/PBP compressed image; Redump indexes raw disc",
                        _ => "Modern container format"
                    };
                }
                else
                {
                    bool isLarge = file.Length >= (250 * 1024 * 1024);
                    var swFile = isLarge ? System.Diagnostics.Stopwatch.StartNew() : null;
                    if (isLarge)
                    {
                        log("WORK", $"[Hashing Large File] '{file.Name}' ({file.Length / (1024.0 * 1024 * 1024):F2} GB)...");
                    }

                    FileHashResult? sidecarHash = null;
                    if (cfg.TrustExistingHashFiles && !cfg.ForceRehashExistingManifestEntries)
                    {
                        var fileVerify = await HashSidecarService.VerifySidecarForTargetAsync(
                            sevenZip,
                            file.FullName,
                            expectInnerRom: false,
                            deepCryptographicVerify: false,
                            scratchDir: cfg.TempExtractionScratchPath,
                            ct: ct);

                        if (fileVerify.Status == SidecarVerificationStatus.Valid && fileVerify.SidecarHash != null)
                        {
                            sidecarHash = fileVerify.SidecarHash;
                        }
                    }

                    FileHashResult resolvedFileHash;
                    if (sidecarHash != null && !string.IsNullOrEmpty(sidecarHash.Sha1))
                    {
                        resolvedFileHash = sidecarHash;
                    }
                    else
                    {
                        resolvedFileHash = await HashSidecarService.ComputeFileHashesAsync(file.FullName, ct);
                    }

                    item.VerifiedLocalCrc32 = resolvedFileHash.Crc32;
                    item.PayloadRomSize = resolvedFileHash.Size;
                    item.VerifiedLocalHash = resolvedFileHash.Sha1;
                    EvaluateMatchStatus(item, datIndex, file.Extension);

                    if (cfg.HashFilePlacement != HashSidecarMode.None && !string.IsNullOrEmpty(resolvedFileHash.Sha1))
                    {
                        try
                        {
                            string sidecarContent = HashSidecarService.GenerateSidecarContentForFiles(baseTitle, new[] { resolvedFileHash });
                            await File.WriteAllTextAsync(HashSidecarService.GetStandardSidecarPath(file.FullName), sidecarContent, Encoding.UTF8, ct);
                        }
                        catch (System.Exception __suppressedEx9) { System.Diagnostics.Debug.WriteLine($"[RomManifestCsvService] Non-fatal error ignored: {__suppressedEx9.Message}"); }
                    }

                    if (isLarge && swFile != null)
                    {
                        double mbps = (file.Length / (1024.0 * 1024.0)) / Math.Max(0.01, swFile.Elapsed.TotalSeconds);
                        log("SUCCESS", $"[Hashed Large File] '{file.Name}' in {swFile.Elapsed.TotalSeconds:F1}s ({mbps:F1} MB/s) • Status: {item.MatchStatus}");
                    }
                }

                await ApplyComplianceAsync(item, file.FullName, isDir: false, null);
                currentItems.Add(item);
            }

            // Cross-archive multi-disc sequence check (Disc 1..N across sibling archives/files) & Report Store Sync
            if (cfg.EnableArchiveTemplateComplianceScan && complianceEvalsByPath.Count > 0)
            {
                cfg.EnsureArchiveTemplatesInitialized();
                var activeTemplate = ArchiveComplianceCatalog.ResolveTemplateForConsole(
                    effectiveRule?.ComplianceTemplateId, effectiveRule, cfg.ArchiveTemplates);

                if (activeTemplate != null && activeTemplate.ValidateMultiDiscSequenceContinuity)
                {
                    var allTopFiles = archives.Concat(standaloneFiles).ToList();
                    ArchiveComplianceService.CheckSiblingMultiDiscContinuity(
                        new DirectoryInfo(collectionDir),
                        allTopFiles,
                        complianceEvalsByPath,
                        activeTemplate);

                    foreach (var kvp in complianceEvalsByPath)
                    {
                        if (manifestItemsByPath.TryGetValue(kvp.Key, out var mItem))
                            mItem.ArchiveCompliance = kvp.Value.SummaryCode;
                    }
                }

                if (reportStore != null)
                {
                    var reportItems = complianceEvalsByPath
                        .Select(kvp => kvp.Value.ToReportItem(consoleName, kvp.Key, Directory.Exists(kvp.Key)))
                        .Where(r => r != null)
                        .Select(r => r!)
                        .ToList();
                    reportStore.UpsertConsoleScanResults(consoleName, reportItems);
                }
            }

            // Write collection_manifest.csv
            WriteManifestToCsv(manifestFilePath, currentItems);
            log("SUCCESS", $"Manifest saved: {currentItems.Count} game(s) recorded ({currentItems.Count(i => i.MatchStatus.StartsWith("MATCH", StringComparison.OrdinalIgnoreCase))} MATCH, {currentItems.Count(i => i.MatchStatus == "MISMATCH")} MISMATCH, {currentItems.Count(i => i.MatchStatus == "NA")} NA).");

            // Secondary mirror handling
            if (cfg.EnableSecondaryManifestStorage && !string.IsNullOrWhiteSpace(cfg.SecondaryManifestDirectory))
            {
                try
                {
                    if (!Directory.Exists(cfg.SecondaryManifestDirectory))
                    {
                        Directory.CreateDirectory(cfg.SecondaryManifestDirectory);
                    }

                    string safeConsoleName = SafeConsoleNameRegex.Replace(consoleName, "_");
                    string secondaryFileName = $"{safeConsoleName}_{manifestFileName}";
                    string secondaryPath = Path.Combine(cfg.SecondaryManifestDirectory, secondaryFileName);

                    File.Copy(manifestFilePath, secondaryPath, overwrite: true);
                    log("INFO", $"Secondary manifest mirror created: '{secondaryPath}'");
                }
                catch (Exception ex)
                {
                    log("WARN", $"Failed to mirror manifest to secondary location '{cfg.SecondaryManifestDirectory}': {ex.Message}");
                }
            }
        }

        private static void EvaluateCompositeDumpMatchStatus(
            RomManifestItem item,
            string dumpType,
            int totalFiles,
            DatIndex datIndex)
        {
            var matchedByName = (datIndex != null && (datIndex.BySha1.Count > 0 || datIndex.ByCrc32.Count > 0 || datIndex.ByFileName.Count > 0))
                ? (datIndex.FindMatchByName(item.GameTitle) ?? datIndex.FindMatchByName(item.RomOrFolderName))
                : null;

            item.ProperName = matchedByName != null ? matchedByName.GameName : "N/A";
            item.MatchStatus = "NA";

            string typeLabel = string.IsNullOrWhiteSpace(dumpType) ? "Composite folder dump" : dumpType;
            string countPart = totalFiles > 0 ? $"{totalFiles} files verified via CRC32 manifest" : "verified via CRC32 manifest";
            item.Notes = matchedByName != null
                ? $"{typeLabel} ({countPart}); DAT '{matchedByName.DatTitle}' indexes single-file disc image"
                : $"{typeLabel} ({countPart})";
        }

        private static void EvaluateMultiFileMatchStatus(
            RomManifestItem item,
            IReadOnlyList<FileHashResult> files,
            DatIndex datIndex,
            string primaryFileExtension)
        {
            if (files == null || files.Count <= 1)
            {
                EvaluateMatchStatus(item, datIndex, primaryFileExtension);
                return;
            }

            if (files.Any(f => CompressedContainerExtensions.Contains(Path.GetExtension(f.FileName).ToLowerInvariant())))
            {
                var nameMatch = (datIndex != null && (datIndex.BySha1.Count > 0 || datIndex.ByFileName.Count > 0))
                    ? (datIndex.FindMatchByName(item.GameTitle) ?? datIndex.FindMatchByName(item.RomOrFolderName))
                    : null;
                item.ProperName = nameMatch != null ? nameMatch.GameName : "N/A";
                item.MatchStatus = "NA";
                item.Notes = $"Hashed {files.Count} files (compressed disc container set; Redump indexes raw BIN/CUE or ISO)";
                return;
            }

            if (datIndex == null || (datIndex.BySha1.Count == 0 && datIndex.ByCrc32.Count == 0 && datIndex.ByFileName.Count == 0))
            {
                item.ProperName = "N/A";
                item.MatchStatus = "NA";
                item.Notes = $"Hashed {files.Count} game files (no DAT file available for console verification)";
                return;
            }

            var eval = datIndex.EvaluateMultiFileSet(files, item.GameTitle);
            if (eval.AllDataTracksVerified)
            {
                item.ProperName = !string.IsNullOrWhiteSpace(eval.PrimaryProperName) ? eval.PrimaryProperName : item.GameTitle;
                item.MatchStatus = eval.VerifiedViaCrc32Only ? "MATCH (CRC32)" : "MATCH";

                string datSource = string.IsNullOrWhiteSpace(eval.DatTitle) ? "DAT" : eval.DatTitle;
                string discSuffix = eval.MatchedDistinctGameCount > 1 ? $" across {eval.MatchedDistinctGameCount} discs" : "";
                string crcSuffix = eval.VerifiedViaCrc32Only ? " [fast header CRC32]" : "";

                if (eval.AllDescriptorsVerified)
                {
                    int verifiedRedumpCount = eval.MatchedDataTrackCount + eval.MatchedDescriptorCount;
                    string playlistSuffix = eval.PlaylistFileCount > 0 ? $" (+ {eval.PlaylistFileCount} .m3u playlist)" : "";
                    item.Notes = $"Verified {verifiedRedumpCount}/{verifiedRedumpCount} files{discSuffix}{playlistSuffix} against {datSource}{crcSuffix}";
                }
                else
                {
                    item.Notes = $"Verified {eval.MatchedDataTrackCount}/{eval.DataTrackCount} disc track(s){discSuffix} against {datSource} (custom descriptor/playlist){crcSuffix}";
                }
                return;
            }

            bool hasCustomTag = DatTitleNormalizer.HasCustomTag(item.GameTitle) ||
                                DatTitleNormalizer.HasCustomTag(item.RomOrFolderName);

            string? matchedTitle = !string.IsNullOrWhiteSpace(eval.PrimaryProperName)
                ? eval.PrimaryProperName
                : (datIndex.FindMatchByName(item.GameTitle)?.GameName ?? datIndex.FindMatchByName(item.RomOrFolderName)?.GameName);

            if (!string.IsNullOrWhiteSpace(matchedTitle))
            {
                item.ProperName = matchedTitle;
                if (hasCustomTag)
                {
                    item.MatchStatus = "MATCH (Modified/PR/RH)";
                    item.Notes = $"Identified via DAT title '{matchedTitle}'; {eval.MatchedDataTrackCount}/{eval.DataTrackCount} track(s) matched unmodified, custom user tag detected";
                    return;
                }

                item.MatchStatus = "MISMATCH";
                if (eval.ExpectedDatTrackCount > 0 && eval.MatchedDataTrackCount < eval.ExpectedDatTrackCount)
                {
                    item.Notes = $"Matched {eval.MatchedDataTrackCount}/{eval.ExpectedDatTrackCount} expected disc track(s) for '{matchedTitle}' ({files.Count} local files hashed)";
                }
                else
                {
                    item.Notes = $"Title matched DAT ('{matchedTitle}') but {Math.Max(1, eval.DataTrackCount - eval.MatchedDataTrackCount)} of {eval.DataTrackCount} track hash(es) did not match DAT";
                }
                return;
            }

            item.ProperName = "N/A";
            item.MatchStatus = "NA";
            item.Notes = $"Hashed {files.Count} files; game title and hashes not found in DAT database";
        }

        private static void EvaluateMatchStatus(RomManifestItem item, DatIndex datIndex, string fileExtension)
        {
            if (datIndex == null || (datIndex.BySha1.Count == 0 && datIndex.ByCrc32.Count == 0 && datIndex.ByFileName.Count == 0))
            {
                item.ProperName = "N/A";
                item.MatchStatus = "NA";
                item.Notes = "No DAT file available for console verification";
                return;
            }

            // =========================================================================
            // 1. PRIMARY: Match by Name (strip custom user tags like PR, RH, slug match)
            // =========================================================================
            var matchedByName = datIndex.FindMatchByName(item.RomOrFolderName) ??
                                datIndex.FindMatchByName(item.GameTitle);

            if (matchedByName != null)
            {
                item.ProperName = matchedByName.GameName;

                // Compare SHA-1 primary, falling back to CRC32 + Size when DAT is CRC32-only or local audit is Fast Header CRC32
                if (DatIndex.HashesMatch(
                        matchedByName,
                        item.VerifiedLocalHash,
                        item.VerifiedLocalCrc32,
                        item.PayloadRomSize,
                        out bool matchedViaCrcOnly))
                {
                    item.MatchStatus = matchedViaCrcOnly ? "MATCH (CRC32)" : "MATCH";
                    item.Notes = matchedViaCrcOnly
                        ? $"Fast header CRC32 + Size match against {matchedByName.DatTitle} (SHA-1 not yet computed)"
                        : (string.IsNullOrWhiteSpace(matchedByName.Sha1)
                            ? $"Verified CRC32 match against {matchedByName.DatTitle}"
                            : $"Verified match against {matchedByName.DatTitle}");
                    return;
                }

                // If hash didn't match, check if file carries a custom hack or dump tag (PR, RH, etc.)
                bool hasCustomTag = DatTitleNormalizer.HasCustomTag(item.GameTitle) ||
                                    DatTitleNormalizer.HasCustomTag(item.RomOrFolderName);

                if (hasCustomTag)
                {
                    item.MatchStatus = "MATCH (Modified/PR/RH)";
                    item.Notes = $"Identified via DAT title '{matchedByName.GameName}'; custom user tag detected (hash modified)";
                    return;
                }

                // Known header/format reasons
                string ext = fileExtension.ToLowerInvariant();
                if (ext == ".nes")
                {
                    item.MatchStatus = "NA";
                    item.Notes = "Headered NES ROM (iNES 16-byte header prepended); No-Intro DAT indexes headerless ROM data";
                }
                else if (ext == ".sfc" || ext == ".smc")
                {
                    item.MatchStatus = "NA";
                    item.Notes = "Possible 512-byte copier header present on SNES ROM; DAT indexes headerless ROM";
                }
                else if (ext == ".cue" || ext == ".gdi")
                {
                    item.MatchStatus = "NA";
                    item.Notes = "Multi-track cue/gdi descriptor; Redump indexes individual track bin files";
                }
                else
                {
                    item.MatchStatus = "MISMATCH";
                    string localDisp = !string.IsNullOrWhiteSpace(item.VerifiedLocalHash) ? item.VerifiedLocalHash : $"CRC32:{item.VerifiedLocalCrc32}";
                    string datDisp = !string.IsNullOrWhiteSpace(matchedByName.Sha1) ? matchedByName.Sha1 : $"CRC32:{matchedByName.Crc32}";
                    item.Notes = $"Title matched DAT ('{matchedByName.GameName}') but local hash ({localDisp}) did not match DAT expected hash ({datDisp})";
                }
                return;
            }

            // =========================================================================
            // 2. FALLBACK: Match by Hash (SHA-1 or CRC32 + Size) in DAT if Name Matching Failed
            // =========================================================================
            if (!string.IsNullOrWhiteSpace(item.VerifiedLocalHash) || !string.IsNullOrWhiteSpace(item.VerifiedLocalCrc32))
            {
                var matchedByHash = datIndex.FindMatchByHash(item.VerifiedLocalHash, item.VerifiedLocalCrc32, item.PayloadRomSize);
                if (matchedByHash != null)
                {
                    _ = DatIndex.HashesMatch(matchedByHash, item.VerifiedLocalHash, item.VerifiedLocalCrc32, item.PayloadRomSize, out bool hashViaCrcOnly);
                    item.ProperName = matchedByHash.GameName;
                    item.MatchStatus = hashViaCrcOnly ? "MATCH (CRC32 via Hash)" : "MATCH (via Hash)";
                    string algoName = (!hashViaCrcOnly && !string.IsNullOrWhiteSpace(item.VerifiedLocalHash) && !string.IsNullOrWhiteSpace(matchedByHash.Sha1))
                        ? "SHA-1"
                        : "CRC32";
                    item.Notes = $"Identified via {algoName} checksum in {matchedByHash.DatTitle} (recovered original title from renamed file)";
                    return;
                }
            }

            // =========================================================================
            // 3. Neither Name nor Hash found in DAT
            // =========================================================================
            item.ProperName = "N/A";
            item.MatchStatus = "NA";
            item.Notes = "Game title and hash not found in DAT database";
        }
    }
}
