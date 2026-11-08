using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Housekeeping.Models;

namespace Housekeeping.Services
{
    public enum ReleaseTopology
    {
        SingleFileRom,
        MultiFileRomSet,
        CompositeFolderDump
    }

    /// <summary>
    /// Classifies game and media releases across both loose directories and archives (.7z/.zip/.rar)
    /// into Single-File ROMs, Multi-File ROM/Disc Sets (.bin/.cue, .gdi, multi-disc), and
    /// Composite Folder Dumps (PS3, PS4, PS5, Xbox 360, Wii U, 3DS, Switch, BDMV, VIDEO_TS).
    /// </summary>
    public static class ReleaseStructureDetector
    {
        public const string TypePs3FolderDump = "PS3 Folder Dump";
        public const string TypePs4FolderDump = "PS4 Folder Dump";
        public const string TypePs5FolderDump = "PS5 Folder Dump";
        public const string TypeXboxFolderDump = "Xbox Folder Dump";
        public const string TypeWiiUFolderDump = "Wii U Decrypted Dump";
        public const string Type3dsFolderDump = "3DS Raw Dump";
        public const string TypeSwitchFolderDump = "Switch Extracted Dump";
        public const string TypeBdmvDiscDump = "Blu-ray Disc Dump (BDMV)";
        public const string TypeDvdDiscDump = "DVD Video Dump (VIDEO_TS)";
        public const string TypeLargeGameFolderDump = "Large Installed Game Folder";
        public const string TypeMultiTrackDisc = "Multi-Track Disc Set";

        public const int MaxMultiTrackSetFiles = 100;

        private static readonly string[] ArchiveExtensions = { ".7z", ".zip", ".rar" };

        private static readonly string[] DiscDescriptorExtensions =
        {
            ".cue", ".gdi", ".ccd", ".mds", ".m3u"
        };

        private static readonly string[] IgnoredNonPayloadExtensions =
        {
            ".hash", ".txt", ".nfo", ".xml", ".jpg", ".jpeg", ".png", ".sav", ".srm",
            ".state", ".csv", ".bat", ".cmd", ".lnk", ".log", ".bak", ".old", ".json", ".dat", ".url"
        };

        private static readonly HashSet<string> OrganizationalFolderNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "[BIOS]", "BIOS", "Saves", "States", "SaveStates", "Cheats", "Screenshots",
            "Covers", "Artwork", "Manuals", "Extras", "Sample", "Samples",
            ".deleted", "$RECYCLE.BIN", "@eaDir", "System Volume Information", "dats", "logs"
        };

        private static readonly Regex DiscSubfolderRegex = new(
            @"^(?:Disc|Disk|CD|Side|Part|DVD|GD)\s*[0-9A-Z]{1,2}$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex SaveStateExtRegex = new(
            @"^\.(?:state|st)\d+$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex PartSuffixRegex = new(
            @"[\(\[\-_ ]*(?:Disc|Disk|CD|Side|Part|Track|Trk)\s*[0-9A-Z]{1,3}[\)\]]*",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool IsIgnoredNonPayloadFile(string? pathOrName)
        {
            if (string.IsNullOrWhiteSpace(pathOrName))
                return true;

            string fileName = Path.GetFileName(pathOrName);
            if (string.IsNullOrWhiteSpace(fileName) ||
                fileName.Contains(".partial.", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string ext = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(ext))
                return false;

            return IgnoredNonPayloadExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase) ||
                   SaveStateExtRegex.IsMatch(ext);
        }

        public static bool IsDiscDescriptorFile(string? pathOrName)
        {
            if (string.IsNullOrWhiteSpace(pathOrName))
                return false;
            string ext = Path.GetExtension(pathOrName);
            return DiscDescriptorExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Normalizes path separators to '/' and strips a single shared top-level wrapper folder
        /// if every path in the list lives inside that same single top-level directory.
        /// Preserves 1:1 index alignment with <paramref name="rawPaths"/>.
        /// </summary>
        public static List<string> NormalizeRelativePathsStrippingSingleWrapper(IReadOnlyList<string> rawPaths)
        {
            var normalized = new List<string>(rawPaths.Count);
            if (rawPaths.Count == 0)
                return normalized;

            string? sharedPrefix = null;
            bool canStrip = true;

            for (int i = 0; i < rawPaths.Count; i++)
            {
                string clean = (rawPaths[i] ?? "").Replace('\\', '/').Trim('/');
                normalized.Add(clean);

                int slashIdx = clean.IndexOf('/');
                if (slashIdx <= 0 || slashIdx >= clean.Length - 1)
                {
                    canStrip = false;
                }
                else if (canStrip)
                {
                    string firstSeg = clean[..slashIdx];
                    if (DiscSubfolderRegex.IsMatch(firstSeg) ||
                        firstSeg.Equals("PS3_GAME", StringComparison.OrdinalIgnoreCase) ||
                        firstSeg.Equals("PS3_UPDATE", StringComparison.OrdinalIgnoreCase) ||
                        firstSeg.Equals("sce_sys", StringComparison.OrdinalIgnoreCase) ||
                        firstSeg.Equals("sce_module", StringComparison.OrdinalIgnoreCase) ||
                        firstSeg.Equals("code", StringComparison.OrdinalIgnoreCase) ||
                        firstSeg.Equals("content", StringComparison.OrdinalIgnoreCase) ||
                        firstSeg.Equals("meta", StringComparison.OrdinalIgnoreCase) ||
                        firstSeg.Equals("romfs", StringComparison.OrdinalIgnoreCase) ||
                        firstSeg.Equals("exefs", StringComparison.OrdinalIgnoreCase) ||
                        firstSeg.Equals("BDMV", StringComparison.OrdinalIgnoreCase) ||
                        firstSeg.Equals("VIDEO_TS", StringComparison.OrdinalIgnoreCase))
                    {
                        canStrip = false;
                    }
                    else if (sharedPrefix == null)
                    {
                        sharedPrefix = firstSeg;
                    }
                    else if (!sharedPrefix.Equals(firstSeg, StringComparison.OrdinalIgnoreCase))
                    {
                        canStrip = false;
                    }
                }
            }

            if (canStrip && !string.IsNullOrEmpty(sharedPrefix))
            {
                int cut = sharedPrefix.Length + 1;
                for (int i = 0; i < normalized.Count; i++)
                {
                    normalized[i] = normalized[i][cut..];
                }
            }

            return normalized;
        }

        /// <summary>
        /// Detects composite console/media dump signatures from a list of relative file paths
        /// (used for both 7-Zip / ZIP archive headers and directory trees).
        /// </summary>
        public static string? DetectCompositeDumpTypeFromPaths(IReadOnlyList<string> rawRelativePaths)
        {
            if (rawRelativePaths == null || rawRelativePaths.Count == 0)
                return null;

            var cleanInputs = rawRelativePaths
                .Where(p => !string.IsNullOrWhiteSpace(p) &&
                            !p.EndsWith(".hash", StringComparison.OrdinalIgnoreCase) &&
                            !p.Contains(".partial.", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (cleanInputs.Count == 0)
                return null;

            var paths = NormalizeRelativePathsStrippingSingleWrapper(cleanInputs);

            bool hasPs3Game = false;
            bool hasSceParamJson = false;
            bool hasSceParamSfo = false;
            bool hasSceModule = false;
            bool hasEbootBin = false;
            bool hasXboxExec = false;
            bool hasWiiUCode = false, hasWiiUContent = false, hasWiiUMeta = false;
            bool hasRomFs = false, hasExeFs = false, hasMainNpdm = false;
            bool hasBdmv = false;
            bool hasVideoTs = false;
            bool hasDiscDescriptor = false;
            int nonIgnoredCount = 0;

            foreach (var p in paths)
            {
                if (string.IsNullOrEmpty(p))
                    continue;

                if (p.Equals("PS3_DISC.SFB", StringComparison.OrdinalIgnoreCase) ||
                    p.StartsWith("PS3_GAME/", StringComparison.OrdinalIgnoreCase) ||
                    p.Contains("/PS3_GAME/", StringComparison.OrdinalIgnoreCase))
                {
                    hasPs3Game = true;
                }

                if (p.Equals("sce_sys/param.json", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith("/sce_sys/param.json", StringComparison.OrdinalIgnoreCase))
                {
                    hasSceParamJson = true;
                }

                if (p.Equals("sce_sys/param.sfo", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith("/sce_sys/param.sfo", StringComparison.OrdinalIgnoreCase))
                {
                    hasSceParamSfo = true;
                }

                if (p.StartsWith("sce_module/", StringComparison.OrdinalIgnoreCase) ||
                    p.Contains("/sce_module/", StringComparison.OrdinalIgnoreCase))
                {
                    hasSceModule = true;
                }

                if (p.Equals("eboot.bin", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith("/eboot.bin", StringComparison.OrdinalIgnoreCase))
                {
                    hasEbootBin = true;
                }

                if (p.Equals("default.xex", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals("default.xbe", StringComparison.OrdinalIgnoreCase))
                {
                    hasXboxExec = true;
                }

                if (p.StartsWith("code/", StringComparison.OrdinalIgnoreCase)) hasWiiUCode = true;
                if (p.StartsWith("content/", StringComparison.OrdinalIgnoreCase)) hasWiiUContent = true;
                if (p.StartsWith("meta/", StringComparison.OrdinalIgnoreCase)) hasWiiUMeta = true;

                if (p.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase)) hasRomFs = true;
                if (p.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase))
                {
                    hasExeFs = true;
                    if (p.EndsWith("/main.npdm", StringComparison.OrdinalIgnoreCase))
                        hasMainNpdm = true;
                }

                if (p.StartsWith("BDMV/", StringComparison.OrdinalIgnoreCase)) hasBdmv = true;
                if (p.StartsWith("VIDEO_TS/", StringComparison.OrdinalIgnoreCase)) hasVideoTs = true;

                if (IsDiscDescriptorFile(p))
                    hasDiscDescriptor = true;

                if (!IsIgnoredNonPayloadFile(p))
                    nonIgnoredCount++;
            }

            if (hasPs3Game) return TypePs3FolderDump;
            if (hasSceParamJson) return TypePs5FolderDump;
            if (hasSceParamSfo || (hasSceModule && hasEbootBin)) return TypePs4FolderDump;
            if (hasXboxExec) return TypeXboxFolderDump;
            if (hasWiiUCode && hasWiiUContent && hasWiiUMeta) return TypeWiiUFolderDump;
            if (hasRomFs && hasExeFs) return hasMainNpdm ? TypeSwitchFolderDump : Type3dsFolderDump;
            if (hasBdmv) return TypeBdmvDiscDump;
            if (hasVideoTs) return TypeDvdDiscDump;

            if (!hasDiscDescriptor && nonIgnoredCount > MaxMultiTrackSetFiles)
                return TypeLargeGameFolderDump;

            return null;
        }

        public static string? DetectFolderDumpType(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return null;

            try
            {
                string? directMatch = ProbeDirectorySignatures(folderPath);
                if (directMatch != null)
                    return directMatch;

                // Check 1-level wrapper subfolder if root has 0 non-hash files and 1 subdirectory
                var topOpts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false };
                var rootFiles = Directory.EnumerateFiles(folderPath, "*", topOpts)
                    .Where(f => !f.EndsWith(".hash", StringComparison.OrdinalIgnoreCase))
                    .Take(2)
                    .ToList();

                if (rootFiles.Count == 0)
                {
                    var subDirs = Directory.EnumerateDirectories(folderPath, "*", topOpts).Take(2).ToList();
                    if (subDirs.Count == 1)
                    {
                        string? nestedMatch = ProbeDirectorySignatures(subDirs[0]);
                        if (nestedMatch != null)
                            return nestedMatch;
                    }
                }

                // Fallback: Large >100-file installed game tree without .cue/.gdi/.ccd/.mds/.m3u disc descriptors
                var recurseOpts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };
                int payloadCount = 0;
                foreach (var f in Directory.EnumerateFiles(folderPath, "*", recurseOpts))
                {
                    if (f.EndsWith(".hash", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string ext = Path.GetExtension(f);
                    if (ArchiveExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                        return null; // Organizational folder containing archives

                    if (IsDiscDescriptorFile(f))
                        return null; // Multi-track / multi-disc ROM set

                    if (!IsIgnoredNonPayloadFile(f))
                    {
                        payloadCount++;
                        if (payloadCount > MaxMultiTrackSetFiles)
                            return TypeLargeGameFolderDump;
                    }
                }
            }
            catch
            {
                // Access issues or long path issues; treat as non-composite
            }

            return null;
        }

        private static string? ProbeDirectorySignatures(string folderPath)
        {
            // 1. Sony PlayStation 3 (JB Folder / Redump Decrypted)
            string ps3Game = Path.Combine(folderPath, "PS3_GAME");
            string ps3Disc = Path.Combine(folderPath, "PS3_DISC.SFB");
            string usrDir = Path.Combine(ps3Game, "USRDIR");
            if (Directory.Exists(ps3Game) || File.Exists(ps3Disc) || Directory.Exists(usrDir))
                return TypePs3FolderDump;

            // 2. Sony PlayStation 5 & PlayStation 4 Extracted Dumps
            string sceSys = Path.Combine(folderPath, "sce_sys");
            if (Directory.Exists(sceSys))
            {
                if (File.Exists(Path.Combine(sceSys, "param.json")))
                    return TypePs5FolderDump;
                if (File.Exists(Path.Combine(sceSys, "param.sfo")))
                    return TypePs4FolderDump;
            }
            if (Directory.Exists(Path.Combine(folderPath, "sce_module")) &&
                File.Exists(Path.Combine(folderPath, "eboot.bin")))
            {
                return TypePs4FolderDump;
            }

            // 3. Microsoft Xbox 360 / Original Xbox Extracted Folder
            if (File.Exists(Path.Combine(folderPath, "default.xex")) ||
                File.Exists(Path.Combine(folderPath, "default.xbe")))
            {
                return TypeXboxFolderDump;
            }

            // 4. Nintendo Wii U Decrypted / Loadiine Dump
            if (Directory.Exists(Path.Combine(folderPath, "code")) &&
                Directory.Exists(Path.Combine(folderPath, "content")) &&
                Directory.Exists(Path.Combine(folderPath, "meta")))
            {
                return TypeWiiUFolderDump;
            }

            // 5. Nintendo Switch / 3DS Extracted RomFS+ExeFS Dump
            string romFs = Path.Combine(folderPath, "romfs");
            string exeFs = Path.Combine(folderPath, "exefs");
            if (Directory.Exists(romFs) && Directory.Exists(exeFs))
            {
                return File.Exists(Path.Combine(exeFs, "main.npdm"))
                    ? TypeSwitchFolderDump
                    : Type3dsFolderDump;
            }

            // 6. Blu-ray BDMV / DVD VIDEO_TS
            if (Directory.Exists(Path.Combine(folderPath, "BDMV")))
                return TypeBdmvDiscDump;

            if (Directory.Exists(Path.Combine(folderPath, "VIDEO_TS")))
                return TypeDvdDiscDump;

            return null;
        }

        public static bool IsCompositeDump(string folderPath)
        {
            return DetectFolderDumpType(folderPath) != null;
        }

        public static bool ShouldTreatAsCompositeDump(string folderPath, AuditStructureMode mode)
        {
            return mode switch
            {
                AuditStructureMode.FolderDumpsOnly => true,
                AuditStructureMode.FilesOnly => false,
                AuditStructureMode.Auto => IsCompositeDump(folderPath),
                _ => IsCompositeDump(folderPath)
            };
        }

        public static bool IsOrganizationalFolderName(string? folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName))
                return true;

            string trimmed = folderName.Trim();
            if (trimmed.Length == 1 && (char.IsLetterOrDigit(trimmed[0]) || trimmed == "#"))
                return true;

            return OrganizationalFolderNames.Contains(trimmed);
        }

        /// <summary>
        /// Determines whether an uncompressed directory on disk represents a single cohesive multi-file
        /// game unit (e.g., PS1/Sega CD/Dreamcast multi-track .bin/.cue/.gdi folder or multi-disc game folder)
        /// rather than an organizational folder holding multiple separate games or archives.
        /// </summary>
        public static bool IsCohesiveMultiFileGameFolder(string folderPath, AuditStructureMode mode = AuditStructureMode.Auto)
        {
            if (mode == AuditStructureMode.FilesOnly ||
                string.IsNullOrWhiteSpace(folderPath) ||
                !Directory.Exists(folderPath))
            {
                return false;
            }

            try
            {
                var di = new DirectoryInfo(folderPath);
                if (IsOrganizationalFolderName(di.Name))
                    return false;

                // Composite dumps (PS3/PS4/PS5/Wii U/etc.) are handled by the CompositeFolderDump pipeline
                if (IsCompositeDump(folderPath))
                    return false;

                var topOpts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false };
                foreach (var childDir in di.EnumerateDirectories("*", topOpts))
                {
                    if (!DiscSubfolderRegex.IsMatch(childDir.Name))
                        return false;
                }

                var recurseOpts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };
                var payloadFiles = new List<FileInfo>();
                var descriptorFiles = new List<FileInfo>();

                foreach (var f in di.EnumerateFiles("*", recurseOpts))
                {
                    if (f.Length <= 0 || f.Name.Contains(".partial.", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (ArchiveExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
                        return false;

                    if (IsIgnoredNonPayloadFile(f.Name))
                        continue;

                    payloadFiles.Add(f);
                    if (IsDiscDescriptorFile(f.Name))
                        descriptorFiles.Add(f);

                    if (payloadFiles.Count > MaxMultiTrackSetFiles)
                        return false;
                }

                if (payloadFiles.Count < 2)
                    return false;

                // Case A: Folder contains disc/playlist descriptor(s) (.cue, .gdi, .ccd, .mds, .m3u)
                if (descriptorFiles.Count > 0)
                {
                    var descriptorSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var desc in descriptorFiles)
                    {
                        string baseKey = ExtractCohesiveBaseSlug(Path.GetFileNameWithoutExtension(desc.Name));
                        if (!string.IsNullOrEmpty(baseKey) &&
                            !baseKey.Equals("disc", StringComparison.OrdinalIgnoreCase) &&
                            !baseKey.Equals("game", StringComparison.OrdinalIgnoreCase) &&
                            !baseKey.Equals("track", StringComparison.OrdinalIgnoreCase))
                        {
                            descriptorSlugs.Add(baseKey);
                        }
                    }

                    // At most 1 distinct game title among all cue/gdi/m3u descriptors in this folder
                    return descriptorSlugs.Count <= 1;
                }

                // Case B: Multi-disc / multi-track files without a descriptor yet (e.g. Disc 1.iso + Disc 2.iso, or Disk A + Disk B)
                string folderBaseSlug = ExtractCohesiveBaseSlug(di.Name);
                var fileSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var pf in payloadFiles)
                {
                    string fileBase = ExtractCohesiveBaseSlug(Path.GetFileNameWithoutExtension(pf.Name));
                    if (!string.IsNullOrEmpty(fileBase) &&
                        !fileBase.Equals("track", StringComparison.OrdinalIgnoreCase) &&
                        !fileBase.Equals("disc", StringComparison.OrdinalIgnoreCase))
                    {
                        fileSlugs.Add(fileBase);
                    }
                }

                if (fileSlugs.Count == 1)
                    return true;

                if (fileSlugs.Count == 0 && !string.IsNullOrEmpty(folderBaseSlug))
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        public static ReleaseTopology ClassifyArchiveEntries(IReadOnlyList<string> rawEntryPaths, AuditStructureMode mode = AuditStructureMode.Auto)
        {
            if (rawEntryPaths == null || rawEntryPaths.Count == 0)
                return ReleaseTopology.SingleFileRom;

            if (mode != AuditStructureMode.FilesOnly && DetectCompositeDumpTypeFromPaths(rawEntryPaths) != null)
                return ReleaseTopology.CompositeFolderDump;

            int payloadCount = rawEntryPaths.Count(p => !IsIgnoredNonPayloadFile(p));
            return payloadCount > 1 ? ReleaseTopology.MultiFileRomSet : ReleaseTopology.SingleFileRom;
        }

        private static string ExtractCohesiveBaseSlug(string nameWithoutExt)
        {
            if (string.IsNullOrWhiteSpace(nameWithoutExt))
                return "";

            string stripped = PartSuffixRegex.Replace(nameWithoutExt, " ");
            return DatTitleNormalizer.NormalizeSlug(stripped);
        }
    }
}
