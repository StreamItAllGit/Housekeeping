using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Models;

namespace Housekeeping.Services.Modules
{
    public class ManifestRecordItem
    {
        public string LocationId { get; set; } = "";
        public string RootPath { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public string CleanTitle { get; set; } = "";
        public string Category { get; set; } = ""; // Media, Game, Generic
        public string YearOrRegion { get; set; } = "";
        public string ResolutionOrTags { get; set; } = "";
        public long FileSizeBytes { get; set; }
        public string FileSizeFormatted => FileSizeBytes >= 1024 * 1024 * 1024
            ? $"{FileSizeBytes / (1024.0 * 1024 * 1024):F2} GB"
            : $"{FileSizeBytes / (1024.0 * 1024):F1} MB";
        public string Crc32 { get; set; } = "";
        public string Sha1 { get; set; } = "";
        public string Md5 { get; set; } = "";
        public string IntegrityStatus { get; set; } = "N/A"; // Verified, Baseline Captured, Bit Rot Warning, Missing, Error, N/A
        public string IntegrityDetail { get; set; } = "";

        public string DatStatus
        {
            get => IntegrityStatus;
            set => IntegrityStatus = value;
        }

        public string DatMatchName
        {
            get => IntegrityDetail;
            set => IntegrityDetail = value;
        }

        public bool IsFolderDump { get; set; } = false;
        public Dictionary<string, string> CustomFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Standalone Module 8: Collection Manifest Auditor.
    /// Scans multi-target directories across local & network NAS paths, parses filenames, computes hashes or fast inventories,
    /// checks Redump/No-Intro DATs, generates .hash sidecars, and exports unified CSV/JSON/HTML reports.
    /// </summary>
    public static partial class CollectionManifestModule
    {
        private static readonly Regex MediaYearRegex = new(@"(?<=\b|\()(?<year>19\d{2}|20\d{2})(?=\b|\))", RegexOptions.Compiled);
        private static readonly Regex MediaResRegex = new(@"(?<res>4K|2160p|1080p|720p|480p|576p|UHD)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex MediaEditionRegex = new(@"(?:\{edition-(?<ed>[^}]+)\}|\b(?<ed>Director'?s\s+Cut|Extended|Remastered|Unrated|Theatrical)\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex MediaSeriesRegex = new(@"(?:S(?<season>\d{1,2})E(?<ep>\d{1,3})|Season\s*(?<s2>\d+))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex GameRegionRegex = new(@"\((?<region>USA|Europe|Japan|En,Ja|En|World|Asia|Australia|France|Germany|Spain|Italy)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex GameDiscRegex = new(@"\((?<disc>Disc\s*\d+|Tape\s*\d+|Side\s*[A-Z])\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly ConcurrentDictionary<string, Regex?> CustomRuleRegexCache = new(StringComparer.Ordinal);

        private static Regex? GetOrCreateCachedRegex(string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return null;
            return CustomRuleRegexCache.GetOrAdd(pattern, p =>
            {
                try { return new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(250)); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[CollectionManifestModule] Regex cache compile skipped: {ex.Message}"); return null; }
            });
        }

        public static async Task RunAsync(ModuleExecutionContext ctx, CancellationToken ct)
        {
            var cfg = ctx.ActiveCollectionManifestConfig;
            var targetRules = cfg.TargetFolders.Where(t => t.Enabled).ToList();

            if (targetRules.Count == 0)
            {
                ctx.Log("WARN", "[Collection Manifest] No enabled target directories configured to audit.");
                return;
            }

            ctx.Log("HEADER", $"=== Running Media Manifest & Bit Rot Auditor ({targetRules.Count} folder rule(s)) ===");
            ctx.Log("INFO", $"Mode: {(cfg.EnableBitRotDetection ? "Cryptographic Bit Rot Audit (.hash Verification & Baseline Creation)" : "Fast Inventory (Metadata & Sizing Only)")}");

            // Sister-App Probing (Dev Bible Standard 16 / ShadowLibrary Parity)
            bool shadowLibPresent = false;
            try
            {
                string? shadowHome = Environment.GetEnvironmentVariable("SHADOWLIBRARY_HOME");
                if (!string.IsNullOrWhiteSpace(shadowHome) && Directory.Exists(shadowHome))
                {
                    shadowLibPresent = true;
                }
                else
                {
                    shadowLibPresent = System.Diagnostics.Process.GetProcessesByName("ShadowLibrary").Length > 0;
                    if (!shadowLibPresent)
                    {
                        var baseDir = AppContext.BaseDirectory;
                        var parent = Directory.GetParent(baseDir)?.FullName;
                        if (!string.IsNullOrEmpty(parent))
                        {
                            var sibling = Path.Combine(parent, "ShadowLibrary");
                            if (Directory.Exists(sibling)) shadowLibPresent = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CollectionManifestModule] Sister-app probe skipped: {ex.Message}");
            }
            if (shadowLibPresent)
            {
                ctx.Log("INFO", "[Sister-App Probing] Detected ShadowLibrary. Coexistence mode active (standard sidecar format & .hklock compatibility enabled).");
            }

            var allRecords = new List<ManifestRecordItem>();

            for (int rIdx = 0; rIdx < targetRules.Count; rIdx++)
            {
                ct.ThrowIfCancellationRequested();
                await ctx.WaitIfQueuePausedAsync(ct);

                var rule = targetRules[rIdx];
                ctx.Log("HEADER", $"--- Scanning Target [{rIdx + 1}/{targetRules.Count}]: '{rule.Label}' ({rule.LocationId}) ---");

                if (string.IsNullOrWhiteSpace(rule.FolderPath) || !Directory.Exists(rule.FolderPath))
                {
                    ctx.Log("WARN", $"Target folder does not exist or is unreachable: '{rule.FolderPath}'. Skipping.");
                    ctx.Stats.Skipped++;
                    continue;
                }

                if (cfg.SkipShadowLibraryManagedRoots && SisterAppCoexistenceService.IsShadowLibraryManagedRoot(rule.FolderPath))
                {
                    ctx.Log("INFO", $"[Coexistence] Skipping ShadowLibrary-managed root '{rule.FolderPath}' (SkipShadowLibraryManagedRoots=true).");
                    ctx.Stats.Skipped++;
                    continue;
                }

                if (NetworkSafetyService.HasActiveDirectoryOrParentLock(rule.FolderPath, out DirectorySentinelLockPayload? activeSentinel, scanImmediateSubdirectories: true))
                {
                    ctx.Log("WARN", $"[Coexistence] Deferring '{rule.FolderPath}' — active '{activeSentinel?.Operation}' lock held by {activeSentinel?.AppId} (PID {activeSentinel?.Pid}).");
                    ctx.Stats.Skipped++;
                    continue;
                }

                using var manifestSentinel = (!ctx.IsDryRun && (cfg.CreateMissingHashFiles || cfg.EnableBitRotAutoHeal))
                    ? NetworkSafetyService.AcquireDirectorySentinelLock(rule.FolderPath, "manifest_audit")
                    : null;

                // Resolve System / Category
                string systemName = rule.SystemSelection;
                if (string.IsNullOrWhiteSpace(systemName) || systemName.Equals("Auto-Detect", StringComparison.OrdinalIgnoreCase))
                {
                    var detectedSys = ConsoleSystemCatalog.ResolveSystem(rule.Label)
                                      ?? ConsoleSystemCatalog.ResolveSystem(Path.GetFileName(rule.FolderPath));
                    systemName = detectedSys?.CanonicalName ?? "Generic Media";
                }

                ctx.Log("INFO", $"Resolved category for '{rule.Label}': {systemName}");

                var ruleRecords = new List<ManifestRecordItem>();
                var excludedNames = (rule.ExcludedFolderNames ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                var extFilters = (rule.ExtensionsFilter ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Smart Structure Detection (Auto / FolderDumpsOnly / FilesOnly)
                var swRule = System.Diagnostics.Stopwatch.StartNew();
                var effectiveMode = rule.StructureMode != AuditStructureMode.Auto ? rule.StructureMode : cfg.StructureMode;
                var effectivePlacement = ctx.IsDryRun
                    ? HashSidecarMode.None
                    : (cfg.CreateMissingHashFiles || cfg.GenerateHashSidecars || cfg.EnableBitRotAutoHeal
                        ? cfg.HashFilePlacement
                        : HashSidecarMode.None);
                if (ctx.IsDryRun && (cfg.CreateMissingHashFiles || cfg.GenerateHashSidecars || cfg.EnableBitRotAutoHeal))
                {
                    ctx.Log("INFO", "[DRY RUN] Suppressing .hash sidecar file writes and archive injections.");
                }
                string sevenZip = ctx.Dependencies.ResolveSevenZip();

                bool IsExcludedPath(string fullPath, string rootPath)
                {
                    if (excludedNames.Length == 0)
                        return false;

                    string relPath = Path.GetRelativePath(rootPath, fullPath);
                    var segments = relPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
                    return segments.Any(seg => excludedNames.Any(ex => seg.Equals(ex, StringComparison.OrdinalIgnoreCase)));
                }

                var compositeDirs = new List<DirectoryInfo>();
                var multiFileGameDirs = new List<DirectoryInfo>();
                var filesToAudit = new List<FileInfo>();

                if (effectiveMode == AuditStructureMode.FolderDumpsOnly)
                {
                    foreach (var sub in Directory.GetDirectories(rule.FolderPath))
                    {
                        if (!IsExcludedPath(sub, rule.FolderPath))
                            compositeDirs.Add(new DirectoryInfo(sub));
                    }
                }
                else
                {
                    var topOpts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false };
                    var dirQueue = new Queue<DirectoryInfo>();
                    dirQueue.Enqueue(new DirectoryInfo(rule.FolderPath));

                    while (dirQueue.Count > 0)
                    {
                        var currentDir = dirQueue.Dequeue();

                        try
                        {
                            foreach (var fi in currentDir.EnumerateFiles("*", topOpts))
                            {
                                if (IsExcludedPath(fi.FullName, rule.FolderPath))
                                    continue;

                                string ext = fi.Extension;
                                if (extFilters.Count > 0 && !extFilters.Contains(ext))
                                    continue;

                                if (ext.Equals(".hash", StringComparison.OrdinalIgnoreCase) ||
                                    ext.Equals(".hklock", StringComparison.OrdinalIgnoreCase) ||
                                    ext.Equals(".sav", StringComparison.OrdinalIgnoreCase) ||
                                    ext.Equals(".srm", StringComparison.OrdinalIgnoreCase) ||
                                    ext.Equals(".state", StringComparison.OrdinalIgnoreCase) ||
                                    fi.Name.Contains(".partial.", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                if (rule.MinimumFileSizeBytes > 0 && fi.Length < rule.MinimumFileSizeBytes)
                                    continue;

                                filesToAudit.Add(fi);
                            }
                        }
                        catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[CollectionManifestModule] Non-fatal error ignored: {__suppressedEx1.Message}"); }

                        try
                        {
                            foreach (var subDir in currentDir.EnumerateDirectories("*", topOpts))
                            {
                                if (IsExcludedPath(subDir.FullName, rule.FolderPath))
                                    continue;

                                if (effectiveMode != AuditStructureMode.FilesOnly &&
                                    !ReleaseStructureDetector.IsOrganizationalFolderName(subDir.Name))
                                {
                                    if (ReleaseStructureDetector.ShouldTreatAsCompositeDump(subDir.FullName, effectiveMode))
                                    {
                                        compositeDirs.Add(subDir);
                                        continue;
                                    }

                                    if (ReleaseStructureDetector.IsCohesiveMultiFileGameFolder(subDir.FullName, effectiveMode))
                                    {
                                        multiFileGameDirs.Add(subDir);
                                        continue;
                                    }
                                }

                                if (rule.IncludeSubdirectories)
                                {
                                    dirQueue.Enqueue(subDir);
                                }
                            }
                        }
                        catch (System.Exception __suppressedEx2) { System.Diagnostics.Debug.WriteLine($"[CollectionManifestModule] Non-fatal error ignored: {__suppressedEx2.Message}"); }
                    }
                }

                int totalUnits = compositeDirs.Count + multiFileGameDirs.Count + filesToAudit.Count;
                int processedUnits = 0;
                ctx.Log("INFO", $"Discovered {totalUnits} target unit(s) in '{rule.Label}' ({compositeDirs.Count} composite dump folder(s), {multiFileGameDirs.Count} multi-file game folder(s), {filesToAudit.Count} archive/file(s)).");

                // 1. Audit Unpacked Composite Folder Dumps (PS3 / PS4 / PS5 / Wii U / 3DS / Xbox 360)
                for (int sIdx = 0; sIdx < compositeDirs.Count; sIdx++)
                {
                    ct.ThrowIfCancellationRequested();
                    await ctx.WaitIfQueuePausedAsync(ct);

                    var compDir = compositeDirs[sIdx];
                    string sub = compDir.FullName;
                    string folderName = compDir.Name;
                    string parentDir = compDir.Parent?.FullName ?? rule.FolderPath;

                    ctx.UpdateModuleProgress((double)processedUnits / Math.Max(1, totalUnits) * 100.0, $"Hashing folder dump: {folderName}");
                    processedUnits++;

                    var record = new ManifestRecordItem
                    {
                        LocationId = rule.LocationId,
                        RootPath = rule.FolderPath,
                        RelativePath = Path.GetRelativePath(rule.FolderPath, sub),
                        FileName = folderName,
                        CleanTitle = folderName,
                        Category = "Folder Dump Game",
                        IsFolderDump = true
                    };

                    if (cfg.EnableBitRotDetection)
                    {
                        try
                        {
                            var swDump = System.Diagnostics.Stopwatch.StartNew();
                            ctx.Log("WORK", $"[Auditing Folder Dump] '{folderName}'...");

                            FolderCompositeHashResult folderHash;
                            var folderVerify = await HashSidecarService.VerifySidecarForTargetAsync(
                                sevenZip,
                                sub,
                                expectInnerRom: false,
                                deepCryptographicVerify: false,
                                structureMode: effectiveMode,
                                ct: ct);

                            if (folderVerify.Status == SidecarVerificationStatus.Valid &&
                                folderVerify.SidecarHash != null &&
                                !string.IsNullOrEmpty(folderVerify.SidecarHash.Sha1))
                            {
                                folderHash = new FolderCompositeHashResult
                                {
                                    FolderName = folderName,
                                    FolderPath = sub,
                                    CompositeSha1 = folderVerify.SidecarHash.Sha1,
                                    TotalSizeBytes = folderVerify.SidecarHash.Size
                                };
                                record.IntegrityStatus = "Verified";
                                record.IntegrityDetail = "Composite SHA-1 Verified";
                            }
                            else if (folderVerify.Status == SidecarVerificationStatus.Missing)
                            {
                                folderHash = await HashSidecarService.ComputeFolderCompositeHashAsync(sub, ct);
                                if (cfg.CreateMissingHashFiles && effectivePlacement != HashSidecarMode.None)
                                {
                                    await HashSidecarService.CreateFolderSidecarFileAsync(parentDir, folderHash, ct);
                                    record.IntegrityStatus = "Baseline Captured";
                                    record.IntegrityDetail = "Created Folder .hash Baseline";
                                }
                                else
                                {
                                    record.IntegrityStatus = "Missing";
                                    record.IntegrityDetail = "No folder .hash sidecar found";
                                }
                            }
                            else
                            {
                                folderHash = await HashSidecarService.ComputeFolderCompositeHashAsync(sub, ct);
                                if (folderVerify.SidecarHash != null &&
                                    !string.IsNullOrEmpty(folderVerify.SidecarHash.Sha1) &&
                                    !folderVerify.SidecarHash.Sha1.Equals(folderHash.CompositeSha1, StringComparison.OrdinalIgnoreCase))
                                {
                                    record.IntegrityStatus = "Bit Rot Warning";
                                    record.IntegrityDetail = $"Composite Hash Mismatch (Expected: {folderVerify.SidecarHash.Sha1}, Computed: {folderHash.CompositeSha1})";
                                    ctx.Log("ERROR", $"[BIT ROT WARNING] Composite dump corruption in '{folderName}'! {record.IntegrityDetail}");
                                    ctx.Stats.Errors++;
                                }
                                else
                                {
                                    record.IntegrityStatus = "Verified";
                                    record.IntegrityDetail = "Composite Verified";
                                }
                            }

                            record.FileSizeBytes = folderHash.TotalSizeBytes;
                            record.Sha1 = folderHash.CompositeSha1;
                            HashSidecarService.CleanupStrayInternalFolderHashFiles(sub);

                            double mbps = (folderHash.TotalSizeBytes / (1024.0 * 1024.0)) / Math.Max(0.01, swDump.Elapsed.TotalSeconds);
                            ctx.Log("SUCCESS", $"[Hashed Folder Dump] '{folderName}' ({record.FileSizeFormatted}) in {swDump.Elapsed.TotalSeconds:F1}s ({mbps:F1} MB/s) • Status: {record.IntegrityStatus}");
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            ctx.Log("WARN", $"Failed hashing folder dump '{folderName}': {ex.Message}");
                            record.IntegrityStatus = "Error";
                            record.IntegrityDetail = ex.Message;
                            ctx.Stats.Errors++;
                        }
                    }
                    else
                    {
                        record.IntegrityStatus = "N/A";
                        record.IntegrityDetail = "Fast Inventory (Hashing Disabled)";
                        try
                        {
                            var safeEnumOpts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };
                            record.FileSizeBytes = compDir.EnumerateFiles("*", safeEnumOpts).Sum(f => f.Length);
                        }
                        catch (System.Exception __suppressedEx3) { System.Diagnostics.Debug.WriteLine($"[CollectionManifestModule] Non-fatal error ignored: {__suppressedEx3.Message}"); }
                    }

                    record.CustomFields = ApplyFieldParseRules(folderName, record.RelativePath, sub, cfg.FieldParseRules);
                    ruleRecords.Add(record);
                    ctx.Stats.Processed++;
                }

                // 2. Audit Unpacked Cohesive Multi-File Game Folders (PS1 / Sega CD / Dreamcast / Multi-Disc Subfolders)
                for (int mIdx = 0; mIdx < multiFileGameDirs.Count; mIdx++)
                {
                    ct.ThrowIfCancellationRequested();
                    await ctx.WaitIfQueuePausedAsync(ct);

                    var gameDir = multiFileGameDirs[mIdx];
                    string folderName = gameDir.Name;
                    string parentDir = gameDir.Parent?.FullName ?? rule.FolderPath;
                    string rel = Path.GetRelativePath(rule.FolderPath, gameDir.FullName);

                    ctx.UpdateModuleProgress((double)processedUnits / Math.Max(1, totalUnits) * 100.0, $"Auditing multi-file game folder: {folderName}");
                    processedUnits++;

                    var safeEnumOpts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };
                    long folderBytes = 0;
                    try
                    {
                        folderBytes = gameDir.EnumerateFiles("*", safeEnumOpts)
                            .Where(f => !ReleaseStructureDetector.IsIgnoredNonPayloadFile(f.Name))
                            .Sum(f => f.Length);
                    }
                    catch (System.Exception __suppressedEx4) { System.Diagnostics.Debug.WriteLine($"[CollectionManifestModule] Non-fatal error ignored: {__suppressedEx4.Message}"); }

                    var record = ParseFileMetadata(folderName, rel, rule.LocationId, rule.FolderPath, folderBytes);
                    record.CleanTitle = folderName;
                    record.Category = "Game / Multi-File Set";

                    if (cfg.EnableBitRotDetection)
                    {
                        try
                        {
                            List<FileHashResult>? folderFiles = null;
                            bool usedVerifiedFolderSidecar = false;

                            var multiVerify = await HashSidecarService.VerifySidecarForTargetAsync(
                                sevenZip,
                                gameDir.FullName,
                                expectInnerRom: false,
                                deepCryptographicVerify: false,
                                structureMode: effectiveMode,
                                ct: ct);

                            if (multiVerify.Status == SidecarVerificationStatus.Valid && multiVerify.SidecarFiles.Count > 0)
                            {
                                folderFiles = multiVerify.SidecarFiles;
                                usedVerifiedFolderSidecar = true;
                                record.IntegrityStatus = "Verified";
                                record.IntegrityDetail = "Multi-File Set Verified";
                            }
                            else if (multiVerify.Status == SidecarVerificationStatus.Missing)
                            {
                                folderFiles = await HashSidecarService.HashMultiFileGameFolderAsync(gameDir.FullName, ct);
                                if (cfg.CreateMissingHashFiles && effectivePlacement != HashSidecarMode.None)
                                {
                                    await HashSidecarService.CreateOrUpdateSidecarFileAsync(parentDir, folderName, folderFiles, ct);
                                    record.IntegrityStatus = "Baseline Captured";
                                    record.IntegrityDetail = "Created Multi-File Set .hash Baseline";
                                }
                                else
                                {
                                    record.IntegrityStatus = "Missing";
                                    record.IntegrityDetail = "No .hash sidecar found";
                                }
                            }
                            else
                            {
                                folderFiles = await HashSidecarService.HashMultiFileGameFolderAsync(gameDir.FullName, ct);
                                record.IntegrityStatus = "Bit Rot Warning";
                                record.IntegrityDetail = multiVerify.DetailMessage;
                                ctx.Log("ERROR", $"[BIT ROT WARNING] Multi-file corruption in '{folderName}'! {multiVerify.DetailMessage}");
                                ctx.Stats.Errors++;
                            }

                            if (folderFiles.Count > 0)
                            {
                                record.FileSizeBytes = folderFiles.Sum(f => f.Size);
                                var primary = folderFiles
                                    .OrderByDescending(f => !ReleaseStructureDetector.IsDiscDescriptorFile(f.FileName))
                                    .ThenByDescending(f => f.Size)
                                    .First();

                                record.Crc32 = primary.Crc32;
                                record.Sha1 = primary.Sha1;
                                record.Md5 = primary.Md5;

                                if (effectivePlacement != HashSidecarMode.None && !usedVerifiedFolderSidecar && cfg.CreateMissingHashFiles)
                                {
                                    await HashSidecarService.CreateOrUpdateSidecarFileAsync(parentDir, folderName, folderFiles, ct);
                                    HashSidecarService.CleanupStrayInternalFolderHashFiles(gameDir.FullName);
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            ctx.Log("WARN", $"Failed hashing multi-file game folder '{folderName}': {ex.Message}");
                            record.IntegrityStatus = "Error";
                            record.IntegrityDetail = ex.Message;
                            ctx.Stats.Errors++;
                        }
                    }
                    else
                    {
                        record.IntegrityStatus = "N/A";
                        record.IntegrityDetail = "Fast Inventory (Hashing Disabled)";
                    }

                    record.CustomFields = ApplyFieldParseRules(folderName, rel, gameDir.FullName, cfg.FieldParseRules);
                    ruleRecords.Add(record);
                    ctx.Stats.Processed++;
                }

                // 3. Audit Archives (.7z / .zip / .rar) and Standalone Loose Files
                {
                    var lastCadenceTime = DateTime.UtcNow;
                    int lastCadenceCount = 0;

                    for (int fIdx = 0; fIdx < filesToAudit.Count; fIdx++)
                    {
                        ct.ThrowIfCancellationRequested();
                        await ctx.WaitIfQueuePausedAsync(ct);

                        var fi = filesToAudit[fIdx];
                        string rel = Path.GetRelativePath(rule.FolderPath, fi.FullName);
                        ctx.UpdateModuleProgress((double)processedUnits / Math.Max(1, totalUnits) * 100.0, $"Auditing: {fi.Name}");
                        processedUnits++;

                        var record = ParseFileMetadata(fi.Name, rel, rule.LocationId, rule.FolderPath, fi.Length);

                        if (cfg.EnableBitRotDetection)
                        {
                            try
                            {
                                bool isLarge = fi.Length >= cfg.LargeFileNoticeThresholdBytes;
                                var swFile = isLarge ? System.Diagnostics.Stopwatch.StartNew() : null;
                                bool isArchive = HashSidecarService.IsArchiveFilePath(fi.FullName);
                                string baseTitle = Path.GetFileNameWithoutExtension(fi.Name);

                                if (isLarge)
                                {
                                    ctx.Log("WORK", $"[Hashing Large File] '{fi.Name}' ({fi.Length / (1024.0 * 1024 * 1024):F2} GB)...");
                                }

                                if (isArchive && cfg.CheckInnerArchiveAndContainer)
                                {
                                    if (HashSidecarService.TryGetVerifiedOuterArchiveRecord(fi.FullName, out var regRec) && regRec != null)
                                    {
                                        record.Crc32 = !string.IsNullOrWhiteSpace(regRec.PrimaryInnerCrc32) ? regRec.PrimaryInnerCrc32 : regRec.ContainerCrc32;
                                        record.Md5 = !string.IsNullOrWhiteSpace(regRec.PrimaryInnerMd5) ? regRec.PrimaryInnerMd5 : regRec.ContainerMd5;
                                        record.Sha1 = !string.IsNullOrWhiteSpace(regRec.PrimaryInnerSha1) ? regRec.PrimaryInnerSha1 : regRec.ContainerSha1;
                                        record.IntegrityStatus = "Verified";
                                        record.IntegrityDetail = "Container Verified (.archive_hashes.json Fast Hit)";
                                    }
                                    else
                                    {
                                    // Two-Step Archive Bit Rot Engine (ShadowLibrary Parity)
                                    var sidecarVerify = await HashSidecarService.VerifySidecarForTargetAsync(
                                        sevenZip,
                                        fi.FullName,
                                        expectInnerRom: true,
                                        deepCryptographicVerify: false,
                                        structureMode: effectiveMode,
                                        ct: ct);

                                    bool hasExistingSidecar = sidecarVerify.Status != SidecarVerificationStatus.Missing;
                                    var doc = sidecarVerify.ParsedDocument;

                                    if (hasExistingSidecar && doc != null)
                                    {
                                        // Step 1: Check Outer Container Hash (Ultra-fast single pass)
                                        bool containerMatch = false;
                                        if (doc.HasContainerHash && doc.ContainerSize == fi.Length)
                                        {
                                            var containerHash = await HashSidecarService.ComputeFileHashesAsync(fi.FullName, ct);
                                            record.Sha1 = containerHash.Sha1;
                                            record.Crc32 = containerHash.Crc32;
                                            record.Md5 = containerHash.Md5;

                                            if (string.Equals(containerHash.Sha1, doc.ContainerSha1, StringComparison.OrdinalIgnoreCase))
                                            {
                                                containerMatch = true;
                                                record.IntegrityStatus = "Verified";
                                                record.IntegrityDetail = "Container Verified (Fast Single-Pass)";
                                            }
                                        }

                                        if (!containerMatch)
                                        {
                                            // Step 2: Inner Payload Fallback (Two-Step Scrub)
                                            var payloadFiles = await HashSidecarService.HashArchivePayloadFilesAsync(
                                                sevenZip,
                                                fi.FullName,
                                                Path.GetTempPath(),
                                                ct: ct);

                                            if (payloadFiles.Count > 0)
                                            {
                                                var primary = payloadFiles
                                                    .OrderByDescending(f => !ReleaseStructureDetector.IsDiscDescriptorFile(f.FileName))
                                                    .ThenByDescending(f => f.Size)
                                                    .First();

                                                record.Crc32 = primary.Crc32;
                                                record.Sha1 = primary.Sha1;
                                                record.Md5 = primary.Md5;

                                                // Validate payload against sidecar entries
                                                bool payloadValid = doc.Files.Count > 0 &&
                                                    payloadFiles.All(pf => doc.Files.Any(df =>
                                                        string.Equals(Path.GetFileName(df.RelativePath), Path.GetFileName(pf.RelativePath), StringComparison.OrdinalIgnoreCase) &&
                                                        (string.Equals(df.Sha1, pf.Sha1, StringComparison.OrdinalIgnoreCase) ||
                                                         string.Equals(df.Crc32, pf.Crc32, StringComparison.OrdinalIgnoreCase))));

                                                if (payloadValid)
                                                {
                                                    record.IntegrityStatus = "Verified";
                                                    record.IntegrityDetail = "Payload Verified (Container Repacked)";
                                                    ctx.Log("INFO", $"[Payload Verified] '{fi.Name}' inner payload 100% intact (repacked container).");

                                                    if (cfg.EnableBitRotAutoHeal && effectivePlacement != HashSidecarMode.None)
                                                    {
                                                        var newContainerHash = await HashSidecarService.ComputeFileHashesAsync(fi.FullName, ct);
                                                        string newSidecarContent = HashSidecarService.GenerateSidecarContentForFiles(baseTitle, payloadFiles, newContainerHash);
                                                        string stdSidecar = HashSidecarService.GetExistingExternalSidecarPaths(fi.FullName).FirstOrDefault()
                                                            ?? HashSidecarService.GetStandardSidecarPath(fi.FullName);
                                                        await File.WriteAllTextAsync(stdSidecar, newSidecarContent, Encoding.UTF8, ct);
                                                        ctx.Log("SUCCESS", $"[Auto-Heal] Re-baselined sidecar with updated container hash for '{fi.Name}'.");
                                                    }
                                                }
                                                else
                                                {
                                                    record.IntegrityStatus = "Bit Rot Warning";
                                                    record.IntegrityDetail = "Inner Payload Checksum Mismatch - Corrupted Archive!";
                                                    ctx.Log("ERROR", $"[BIT ROT WARNING] Archive corruption detected in '{fi.Name}'! Inner payload checksum mismatch.");
                                                    ctx.Stats.Errors++;
                                                }
                                            }
                                            else
                                            {
                                                record.IntegrityStatus = "Bit Rot Warning";
                                                record.IntegrityDetail = "Archive payload empty or unreadable";
                                                ctx.Stats.Errors++;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // No sidecar found
                                        if (cfg.CreateMissingHashFiles && effectivePlacement != HashSidecarMode.None)
                                        {
                                            var containerHash = await HashSidecarService.ComputeFileHashesAsync(fi.FullName, ct);
                                            var payloadFiles = await HashSidecarService.HashArchivePayloadFilesAsync(
                                                sevenZip,
                                                fi.FullName,
                                                Path.GetTempPath(),
                                                ct: ct);

                                            var primary = payloadFiles.Count > 0
                                                ? payloadFiles.OrderByDescending(f => !ReleaseStructureDetector.IsDiscDescriptorFile(f.FileName)).ThenByDescending(f => f.Size).First()
                                                : containerHash;

                                            record.Crc32 = primary.Crc32;
                                            record.Sha1 = primary.Sha1;
                                            record.Md5 = primary.Md5;

                                            string sidecarContent = HashSidecarService.GenerateSidecarContentForFiles(baseTitle, payloadFiles.Count > 0 ? payloadFiles : new[] { containerHash }, containerHash);
                                            string stdSidecar = HashSidecarService.GetStandardSidecarPath(fi.FullName);

                                            if ((effectivePlacement == HashSidecarMode.SidecarBeside || effectivePlacement == HashSidecarMode.SidecarAndArchive) ||
                                                !HashSidecarService.IsWritableArchiveFilePath(fi.FullName))
                                            {
                                                await File.WriteAllTextAsync(stdSidecar, sidecarContent, Utf8NoBom, ct);
                                            }

                                            if ((effectivePlacement == HashSidecarMode.PackageInsideArchive || effectivePlacement == HashSidecarMode.SidecarAndArchive) &&
                                                HashSidecarService.IsWritableArchiveFilePath(fi.FullName))
                                            {
                                                string tempDir = Path.Combine(Path.GetTempPath(), "hk_hash_" + Guid.NewGuid().ToString("N"));
                                                Directory.CreateDirectory(tempDir);
                                                string tempSidecar = Path.Combine(tempDir, $"{baseTitle}.hash");
                                                try
                                                {
                                                    await File.WriteAllTextAsync(tempSidecar, sidecarContent, Utf8NoBom, ct);
                                                    await HashSidecarService.InjectSidecarIntoArchiveAsync(sevenZip, fi.FullName, tempSidecar, ct, cfg.SyncPostInjectionContainerHashJson);
                                                }
                                                finally
                                                {
                                                    try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
                                                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[CollectionManifestModule] Temp sidecar cleanup skipped: {ex.Message}"); }
                                                }
                                            }

                                            record.IntegrityStatus = "Baseline Captured";
                                            record.IntegrityDetail = "Created Baseline .hash";
                                            ctx.Log("SUCCESS", $"[Baseline Captured] Created .hash for '{fi.Name}'.");
                                        }
                                        else
                                        {
                                            var containerHash = await HashSidecarService.ComputeFileHashesAsync(fi.FullName, ct);
                                            record.Sha1 = containerHash.Sha1;
                                            record.Crc32 = containerHash.Crc32;
                                            record.Md5 = containerHash.Md5;
                                            record.IntegrityStatus = "Missing";
                                            record.IntegrityDetail = "No .hash sidecar found";
                                        }
                                    }
                                    }
                                }
                                else
                                {
                                    // Standalone loose file (or archive without inner inspection)
                                    var fileHashes = await HashSidecarService.ComputeFileHashesAsync(fi.FullName, ct);
                                    record.Crc32 = fileHashes.Crc32;
                                    record.Sha1 = fileHashes.Sha1;
                                    record.Md5 = fileHashes.Md5;

                                    if (HashSidecarService.TryReadExistingSidecarDocument(fi.FullName, out var looseDoc) && looseDoc != null && looseDoc.Files.Count > 0)
                                    {
                                        var expected = looseDoc.Files[0];
                                        bool match = string.Equals(expected.Sha1, fileHashes.Sha1, StringComparison.OrdinalIgnoreCase) ||
                                                     string.Equals(expected.Crc32, fileHashes.Crc32, StringComparison.OrdinalIgnoreCase);

                                        if (match)
                                        {
                                            record.IntegrityStatus = "Verified";
                                            record.IntegrityDetail = "Cryptographically Verified";
                                        }
                                        else
                                        {
                                            record.IntegrityStatus = "Bit Rot Warning";
                                            record.IntegrityDetail = $"Hash Mismatch: expected {expected.Sha1}, got {fileHashes.Sha1}";
                                            ctx.Log("ERROR", $"[BIT ROT WARNING] Bit rot detected in '{fi.Name}'! {record.IntegrityDetail}");
                                            ctx.Stats.Errors++;
                                        }
                                    }
                                    else
                                    {
                                        if (cfg.CreateMissingHashFiles && effectivePlacement != HashSidecarMode.None)
                                        {
                                            string sidecarContent = HashSidecarService.GenerateSidecarContentForFiles(baseTitle, new[] { fileHashes });
                                            string stdSidecar = HashSidecarService.GetStandardSidecarPath(fi.FullName);
                                            await File.WriteAllTextAsync(stdSidecar, sidecarContent, Encoding.UTF8, ct);

                                            record.IntegrityStatus = "Baseline Captured";
                                            record.IntegrityDetail = "Created Baseline .hash";
                                            ctx.Log("SUCCESS", $"[Baseline Captured] Created .hash for '{fi.Name}'.");
                                        }
                                        else
                                        {
                                            record.IntegrityStatus = "Missing";
                                            record.IntegrityDetail = "No .hash sidecar found";
                                        }
                                    }
                                }

                                if (isLarge && swFile != null)
                                {
                                    double mbps = (fi.Length / (1024.0 * 1024.0)) / Math.Max(0.01, swFile.Elapsed.TotalSeconds);
                                    ctx.Log("SUCCESS", $"[Hashed Large File] '{fi.Name}' ({fi.Length / (1024.0 * 1024 * 1024):F2} GB) in {swFile.Elapsed.TotalSeconds:F1}s ({mbps:F1} MB/s) • SHA1: {record.Sha1} • Status: {record.IntegrityStatus}");
                                }

                                // Periodic Cadence Terminal Log every N files or N seconds
                                var now = DateTime.UtcNow;
                                if ((fIdx + 1) % cfg.LogCadenceItems == 0 || (now - lastCadenceTime).TotalSeconds >= cfg.LogCadenceSeconds)
                                {
                                    double rate = (fIdx + 1 - lastCadenceCount) / Math.Max(0.1, (now - lastCadenceTime).TotalSeconds);
                                    ctx.Log("INFO", $"[Collection Manifest] Auditing [{(fIdx + 1)}/{filesToAudit.Count}] ({((fIdx + 1) * 100.0 / filesToAudit.Count):F1}%) • '{fi.Name}' • {rate:F1} files/s");
                                    lastCadenceTime = now;
                                    lastCadenceCount = fIdx + 1;
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                ctx.Log("WARN", $"Could not hash '{fi.Name}': {ex.Message}");
                                record.IntegrityStatus = "Error";
                                record.IntegrityDetail = ex.Message;
                                ctx.Stats.Errors++;
                            }
                        }
                        else
                        {
                            record.IntegrityStatus = "N/A";
                            record.IntegrityDetail = "Fast Inventory (Hashing Disabled)";
                        }

                        record.CustomFields = ApplyFieldParseRules(fi.Name, rel, fi.FullName, cfg.FieldParseRules);
                        ruleRecords.Add(record);
                        ctx.Stats.Processed++;
                    }
                }

                // Summary Block
                int verified = ruleRecords.Count(r => r.IntegrityStatus == "Verified" || r.IntegrityStatus == "Match");
                int captured = ruleRecords.Count(r => r.IntegrityStatus == "Baseline Captured");
                int warnings = ruleRecords.Count(r => r.IntegrityStatus == "Bit Rot Warning" || r.IntegrityStatus == "Mismatch");
                int missing = ruleRecords.Count(r => r.IntegrityStatus == "Missing");
                int errors = ruleRecords.Count(r => r.IntegrityStatus == "Error");
                ctx.Log("HEADER", $"════ Target [{rIdx + 1}/{targetRules.Count}] '{rule.Label}' Complete ════");
                ctx.Log("INFO", $"Audited: {ruleRecords.Count} item(s) | Verified: {verified} | Baselines: {captured} | Warnings: {warnings} | Missing: {missing} | Errors: {errors} | Elapsed: {swRule.Elapsed.TotalSeconds:F1}s");

                // Update rule execution telemetry
                rule.LastAuditRunUtc = DateTime.UtcNow;
                rule.LastTotalCount = ruleRecords.Count;
                rule.LastMatchCount = verified + captured;
                rule.LastMismatchCount = warnings;

                allRecords.AddRange(ruleRecords);

                // Per-location manifest export
                if (!cfg.UnifiedMasterManifest)
                {
                    if (ctx.IsDryRun)
                    {
                        ctx.Log("INFO", $"[DRY RUN] Would export per-location manifest '{rule.LocationId}_{rule.Label}_Manifest' ({ruleRecords.Count} item(s)) to '{rule.FolderPath}'.");
                    }
                    else
                    {
                        await ExportManifestsAsync(ruleRecords, rule.FolderPath, $"{rule.LocationId}_{rule.Label}_Manifest", cfg, ctx.Log, ct);
                    }
                }
            }

            // Unified Master Manifest export
            if (cfg.UnifiedMasterManifest)
            {
                string outDir = string.IsNullOrWhiteSpace(cfg.UnifiedManifestOutputFolder)
                    ? targetRules[0].FolderPath
                    : cfg.UnifiedManifestOutputFolder;

                string baseName = string.IsNullOrWhiteSpace(cfg.UnifiedManifestName) ? "Master_Collection_Manifest" : cfg.UnifiedManifestName;
                if (ctx.IsDryRun)
                {
                    ctx.Log("INFO", $"[DRY RUN] Would export unified manifest '{baseName}' ({allRecords.Count} item(s)) to '{outDir}' (CSV={cfg.ExportCsv}, JSON={cfg.ExportJson}, HTML={cfg.ExportHtmlReport}).");
                }
                else
                {
                    if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
                    await ExportManifestsAsync(allRecords, outDir, baseName, cfg, ctx.Log, ct);
                }
            }

            ctx.Log("SUCCESS", $"=== Collection Manifest Auditor Finished! Processed: {allRecords.Count} item(s) ===");
        }

        private static ManifestRecordItem ParseFileMetadata(string fileName, string relPath, string locationId, string rootPath, long size)
        {
            var item = new ManifestRecordItem
            {
                LocationId = locationId,
                RootPath = rootPath,
                RelativePath = relPath,
                FileName = fileName,
                FileSizeBytes = size,
                CleanTitle = Path.GetFileNameWithoutExtension(fileName)
            };

            // Detect Media Attributes
            var yearMatch = MediaYearRegex.Match(fileName);
            if (yearMatch.Success) item.YearOrRegion = yearMatch.Groups["year"].Value;

            var resMatch = MediaResRegex.Match(fileName);
            if (resMatch.Success) item.ResolutionOrTags = resMatch.Groups["res"].Value;

            var edMatch = MediaEditionRegex.Match(fileName);
            if (edMatch.Success)
            {
                string ed = edMatch.Groups["ed"].Value;
                item.ResolutionOrTags = string.IsNullOrEmpty(item.ResolutionOrTags) ? ed : $"{item.ResolutionOrTags} [{ed}]";
            }

            // Detect Game Attributes
            var regionMatch = GameRegionRegex.Match(fileName);
            if (regionMatch.Success) item.YearOrRegion = regionMatch.Groups["region"].Value;

            var discMatch = GameDiscRegex.Match(fileName);
            if (discMatch.Success) item.ResolutionOrTags = discMatch.Groups["disc"].Value;

            if (MediaResRegex.IsMatch(fileName) || fileName.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            {
                item.Category = "Media";
            }
            else
            {
                item.Category = "Game / Archive";
            }

            return item;
        }
    }
}
