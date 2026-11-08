using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Models;

namespace Housekeeping.Services
{
    public class FileHashResult
    {
        public string RelativePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public long Size { get; set; }
        public string Crc32 { get; set; } = "";
        public string Md5 { get; set; } = "";
        public string Sha1 { get; set; } = "";
    }

    public class FolderCompositeHashResult
    {
        public string FolderName { get; set; } = "";
        public string FolderPath { get; set; } = "";
        public string DumpType { get; set; } = "";
        public int TotalFiles { get; set; }
        public long TotalSizeBytes { get; set; }
        public string CompositeSha1 { get; set; } = "";
        public List<FileHashResult> Files { get; set; } = new();
    }

    public class ParsedSidecarDocument
    {
        public bool IsCompositeFolder { get; set; }
        public string TitleOrFolder { get; set; } = "";
        public string DumpType { get; set; } = "";
        public int TotalFiles { get; set; }
        public long TotalSizeBytes { get; set; }
        public string CompositeSha1 { get; set; } = "";
        public bool AllEntriesHaveCrc32 { get; set; } = true;
        public string ContainerFileName { get; set; } = "";
        public long ContainerSize { get; set; }
        public string ContainerCrc32 { get; set; } = "";
        public string ContainerMd5 { get; set; } = "";
        public string ContainerSha1 { get; set; } = "";
        public bool HasContainerHash => ContainerSize > 0 && !string.IsNullOrWhiteSpace(ContainerSha1);
        public List<FileHashResult> Files { get; set; } = new();

        public FileHashResult? GetPrimaryHashResult(string defaultFileName)
        {
            if (IsCompositeFolder && !string.IsNullOrWhiteSpace(CompositeSha1))
            {
                string name = string.IsNullOrWhiteSpace(TitleOrFolder) ? defaultFileName : TitleOrFolder;
                return new FileHashResult
                {
                    FileName = name,
                    RelativePath = name,
                    Size = TotalSizeBytes,
                    Sha1 = CompositeSha1
                };
            }

            if (Files.Count == 0)
                return null;

            return Files
                .OrderByDescending(f => !ReleaseStructureDetector.IsDiscDescriptorFile(f.FileName))
                .ThenByDescending(f => f.Size)
                .First();
        }
    }

    public enum SidecarVerificationStatus
    {
        Valid,
        LegacyOuterArchiveHash,
        IncompleteMultiFileSet,
        HeaderMismatch,
        CryptographicMismatch,
        Missing,
        Error
    }

    public enum SidecarLocation
    {
        None,
        ExternalBeside,
        InternalArchive,
        Both
    }

    public enum SidecarVerifyMode
    {
        FastHeaderAndHeal,
        DeepCryptographicAndHeal,
        DryRunReportOnly
    }

    public class SidecarItemVerificationResult
    {
        public string TargetPath { get; set; } = "";
        public ReleaseTopology Topology { get; set; } = ReleaseTopology.SingleFileRom;
        public SidecarVerificationStatus Status { get; set; }
        public SidecarLocation Location { get; set; }
        public ParsedSidecarDocument? ParsedDocument { get; set; }
        public FileHashResult? SidecarHash { get; set; }
        public List<FileHashResult> SidecarFiles { get; set; } = new();
        public HashSidecarService.ArchiveEntryInfo? PrimaryInnerEntry { get; set; }
        public FileHashResult? ComputedHash { get; set; }
        public List<FileHashResult> ComputedFiles { get; set; } = new();
        public FolderCompositeHashResult? ComputedComposite { get; set; }
        public string DetailMessage { get; set; } = "";
        public bool WasHealed { get; set; }
    }

    public class SidecarVerificationReport
    {
        public int TotalInspected { get; set; }
        public int ValidCount { get; set; }
        public int LegacyOuterArchiveCount { get; set; }
        public int IncompleteMultiFileCount { get; set; }
        public int HeaderMismatchCount { get; set; }
        public int CryptographicMismatchCount { get; set; }
        public int MissingCount { get; set; }
        public int HealedCount { get; set; }
        public int ErrorCount { get; set; }
        public List<SidecarItemVerificationResult> Items { get; set; } = new();
    }

    /// <summary>
    /// Generates, verifies, and self-heals '.hash' sidecar files for single ROMs,
    /// multi-file disc/track/multi-disc sets (Redump CRC32 + MD5 + SHA1 on every file),
    /// and huge composite console folder dumps (PS3/PS4/PS5/Wii U/3DS/Xbox 360 using Scene .sfv / 7-Zip CRC32 per file).
    /// </summary>
    public static class HashSidecarService
    {
        private const int HashBufferSizeBytes = 262144; // 256 KB
        private static string? _cachedDefaultSevenZipExe;

        public static async Task<FileHashResult> ComputeStreamHashesAsync(
            Stream stream,
            string fileName,
            string relativePath,
            CancellationToken ct = default)
        {
            using var sha1 = SHA1.Create();
            using var md5 = MD5.Create();

            byte[] buffer = ArrayPool<byte>.Shared.Rent(HashBufferSizeBytes);
            try
            {
                uint crc = 0xFFFFFFFF;
                long totalBytes = 0;
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, HashBufferSizeBytes), ct)) > 0)
                {
                    totalBytes += bytesRead;
                    sha1.TransformBlock(buffer, 0, bytesRead, null, 0);
                    md5.TransformBlock(buffer, 0, bytesRead, null, 0);

                    for (int i = 0; i < bytesRead; i++)
                    {
                        crc = Crc32Table[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
                    }
                }

                sha1.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                crc = ~crc;

                return new FileHashResult
                {
                    FileName = fileName,
                    RelativePath = relativePath,
                    Size = totalBytes,
                    Sha1 = Convert.ToHexString(sha1.Hash ?? Array.Empty<byte>()),
                    Md5 = Convert.ToHexString(md5.Hash ?? Array.Empty<byte>()),
                    Crc32 = crc.ToString("X8")
                };
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public static async Task<string> ComputeFileCrc32Async(string filePath, CancellationToken ct = default)
        {
            var fi = new FileInfo(filePath);
            if (!fi.Exists)
                throw new FileNotFoundException("File not found for CRC32 hashing", filePath);

            if (fi.Length == 0)
                return "00000000";

            using var fs = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                HashBufferSizeBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            byte[] buffer = ArrayPool<byte>.Shared.Rent(HashBufferSizeBytes);
            try
            {
                uint crc = 0xFFFFFFFF;
                int bytesRead;
                while ((bytesRead = await fs.ReadAsync(buffer.AsMemory(0, HashBufferSizeBytes), ct)) > 0)
                {
                    for (int i = 0; i < bytesRead; i++)
                    {
                        crc = Crc32Table[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
                    }
                }

                return (~crc).ToString("X8");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public static async Task<FileHashResult> ComputeFileHashesAsync(string filePath, CancellationToken ct = default)
        {
            var fi = new FileInfo(filePath);
            if (!fi.Exists)
                throw new FileNotFoundException("File not found for hashing", filePath);

            using var fs = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                HashBufferSizeBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            return await ComputeStreamHashesAsync(fs, fi.Name, fi.Name, ct);
        }

        /// <summary>
        /// Hashes all payload files inside a cohesive unpacked multi-file game directory
        /// (e.g., PS1/Sega CD/Dreamcast multi-bin + .cue or multi-disc game folder) using full Redump CRC32 + MD5 + SHA1.
        /// </summary>
        public static async Task<List<FileHashResult>> HashMultiFileGameFolderAsync(
            string folderPath,
            CancellationToken ct = default)
        {
            var di = new DirectoryInfo(folderPath);
            if (!di.Exists)
                throw new DirectoryNotFoundException($"Game folder not found: {folderPath}");

            var enumOpts = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true
            };

            var rawFiles = di.EnumerateFiles("*", enumOpts)
                .Where(f => f.Length >= 0 &&
                            !IsArchiveFilePath(f.FullName) &&
                            !ReleaseStructureDetector.IsIgnoredNonPayloadFile(f.Name))
                .ToList();

            var rawRelPaths = rawFiles
                .Select(f => Path.GetRelativePath(folderPath, f.FullName).Replace('\\', '/'))
                .ToList();

            var normalizedRelPaths = ReleaseStructureDetector.NormalizeRelativePathsStrippingSingleWrapper(rawRelPaths);

            var paired = rawFiles
                .Select((file, idx) => (File: file, RelPath: normalizedRelPaths[idx]))
                .OrderBy(x => x.RelPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var results = new List<FileHashResult>(paired.Count);
            foreach (var item in paired)
            {
                ct.ThrowIfCancellationRequested();
                var h = await ComputeFileHashesAsync(item.File.FullName, ct);
                h.FileName = item.RelPath;
                h.RelativePath = item.RelPath;
                results.Add(h);
            }

            return results;
        }

        /// <summary>
        /// Computes a Scene .sfv / 7-Zip compatible per-file CRC32 + Size manifest and deterministic
        /// COMPOSITE_SHA1 over the sorted file table for huge console folder dumps (PS3/PS4/PS5/Wii U/3DS/Xbox 360).
        /// </summary>
        public static async Task<FolderCompositeHashResult> ComputeFolderCompositeHashAsync(string folderPath, CancellationToken ct = default)
        {
            var di = new DirectoryInfo(folderPath);
            if (!di.Exists)
                throw new DirectoryNotFoundException($"Folder not found: {folderPath}");

            var enumOpts = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true
            };

            var rawFiles = di.EnumerateFiles("*", enumOpts)
                .Where(f => !f.Name.EndsWith(".hash", StringComparison.OrdinalIgnoreCase) &&
                            !f.Name.Contains(".partial.", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var rawRelPaths = rawFiles
                .Select(f => Path.GetRelativePath(folderPath, f.FullName).Replace('\\', '/'))
                .ToList();

            var normalizedRelPaths = ReleaseStructureDetector.NormalizeRelativePathsStrippingSingleWrapper(rawRelPaths);
            string dumpType = ReleaseStructureDetector.DetectFolderDumpType(folderPath)
                              ?? ReleaseStructureDetector.DetectCompositeDumpTypeFromPaths(normalizedRelPaths)
                              ?? "";

            var paired = rawFiles
                .Select((file, idx) => (File: file, RelPath: normalizedRelPaths[idx]))
                .OrderBy(x => x.RelPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var result = new FolderCompositeHashResult
            {
                FolderName = di.Name,
                FolderPath = folderPath,
                DumpType = dumpType,
                TotalFiles = paired.Count,
                TotalSizeBytes = paired.Sum(p => p.File.Length)
            };

            using var compositeSha1 = SHA1.Create();
            foreach (var (file, rel) in paired)
            {
                ct.ThrowIfCancellationRequested();
                string crc32 = await ComputeFileCrc32Async(file.FullName, ct);

                string canonicalLine = $"{rel}|{file.Length}|{crc32}\n";
                byte[] lineBytes = Encoding.UTF8.GetBytes(canonicalLine);
                compositeSha1.TransformBlock(lineBytes, 0, lineBytes.Length, null, 0);

                result.Files.Add(new FileHashResult
                {
                    FileName = Path.GetFileName(rel),
                    RelativePath = rel,
                    Size = file.Length,
                    Crc32 = crc32
                });
            }

            compositeSha1.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            result.CompositeSha1 = Convert.ToHexString(compositeSha1.Hash ?? Array.Empty<byte>());
            return result;
        }

        /// <summary>
        /// Builds the exact same Scene .sfv / 7-Zip CRC32 composite folder manifest directly from
        /// 7-Zip's technical archive header in &lt;0.1s without decompressing multi-GB PS3/PS4/Wii U archives.
        /// </summary>
        public static FolderCompositeHashResult BuildCompositeHashFromArchiveEntries(
            string folderOrBaseTitle,
            string archivePath,
            IReadOnlyList<ArchiveEntryInfo> entries,
            string? dumpType = null)
        {
            var fileEntries = entries
                .Where(e => !e.IsDirectory &&
                            !string.IsNullOrWhiteSpace(e.Path) &&
                            !e.Path.EndsWith(".hash", StringComparison.OrdinalIgnoreCase) &&
                            !e.Path.Contains(".partial.", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var rawPaths = fileEntries.Select(e => e.Path).ToList();
            var normalizedPaths = ReleaseStructureDetector.NormalizeRelativePathsStrippingSingleWrapper(rawPaths);
            string resolvedDumpType = dumpType
                                      ?? ReleaseStructureDetector.DetectCompositeDumpTypeFromPaths(normalizedPaths)
                                      ?? "";

            var paired = fileEntries
                .Select((entry, idx) => (Entry: entry, RelPath: normalizedPaths[idx]))
                .OrderBy(x => x.RelPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var result = new FolderCompositeHashResult
            {
                FolderName = folderOrBaseTitle,
                FolderPath = archivePath,
                DumpType = resolvedDumpType,
                TotalFiles = paired.Count,
                TotalSizeBytes = paired.Sum(p => p.Entry.Size)
            };

            using var compositeSha1 = SHA1.Create();
            foreach (var (entry, rel) in paired)
            {
                string crc32 = string.IsNullOrWhiteSpace(entry.Crc)
                    ? (entry.Size == 0 ? "00000000" : "")
                    : entry.Crc.Trim().ToUpperInvariant();

                string canonicalLine = $"{rel}|{entry.Size}|{crc32}\n";
                byte[] lineBytes = Encoding.UTF8.GetBytes(canonicalLine);
                compositeSha1.TransformBlock(lineBytes, 0, lineBytes.Length, null, 0);

                result.Files.Add(new FileHashResult
                {
                    FileName = Path.GetFileName(rel),
                    RelativePath = rel,
                    Size = entry.Size,
                    Crc32 = crc32
                });
            }

            compositeSha1.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            result.CompositeSha1 = Convert.ToHexString(compositeSha1.Hash ?? Array.Empty<byte>());
            return result;
        }

        public static string GenerateSidecarContentForFiles(string title, IEnumerable<FileHashResult> files, FileHashResult? containerHash = null)
        {
            var list = files?.ToList() ?? new List<FileHashResult>();
            var sb = new StringBuilder();
            sb.AppendLine("# Housekeeping Checksum Sidecar");
            sb.AppendLine($"# Title: {title}");
            if (containerHash != null && !string.IsNullOrWhiteSpace(containerHash.Sha1))
            {
                sb.AppendLine($"# Container: {containerHash.FileName} | SIZE: {containerHash.Size} | CRC32: {containerHash.Crc32} | MD5: {containerHash.Md5} | SHA1: {containerHash.Sha1}");
            }
            if (list.Count > 1)
                sb.AppendLine($"# TotalFiles: {list.Count}");
            sb.AppendLine($"# Generated: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}");
            sb.AppendLine();

            foreach (var f in list)
            {
                string displayPath = !string.IsNullOrWhiteSpace(f.RelativePath) ? f.RelativePath : f.FileName;
                sb.AppendLine($"FILE: {displayPath} | SIZE: {f.Size} | CRC32: {f.Crc32} | MD5: {f.Md5} | SHA1: {f.Sha1}");
            }

            return sb.ToString();
        }

        public static string GenerateSidecarContentForFolder(FolderCompositeHashResult folderResult)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Housekeeping Folder Composite Checksum");
            sb.AppendLine($"# Folder: {folderResult.FolderName}");
            if (!string.IsNullOrWhiteSpace(folderResult.DumpType))
                sb.AppendLine($"# DumpType: {folderResult.DumpType}");
            sb.AppendLine($"# Generated: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}");
            sb.AppendLine($"TOTAL_FILES: {folderResult.TotalFiles}");
            sb.AppendLine($"TOTAL_SIZE: {folderResult.TotalSizeBytes}");
            sb.AppendLine($"COMPOSITE_SHA1: {folderResult.CompositeSha1}");
            sb.AppendLine();
            sb.AppendLine("# Manifest Files (Relative Paths & CRC32):");
            foreach (var f in folderResult.Files)
            {
                if (!string.IsNullOrWhiteSpace(f.Crc32))
                    sb.AppendLine($"ENTRY: {f.RelativePath} | SIZE: {f.Size} | CRC32: {f.Crc32}");
                else
                    sb.AppendLine($"ENTRY: {f.RelativePath} | SIZE: {f.Size}");
            }

            return sb.ToString();
        }

        private static string NormalizeTargetPath(string targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
                return "";

            string trimmed = targetPath.Trim();
            string? root = Path.GetPathRoot(trimmed);
            if (!string.IsNullOrEmpty(root) && trimmed.Equals(root, StringComparison.OrdinalIgnoreCase))
                return trimmed;

            return trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public static string GetStandardSidecarPath(string targetPath)
        {
            return NormalizeTargetPath(targetPath) + ".hash";
        }

        public static List<string> GetExistingExternalSidecarPaths(string targetPath)
        {
            var found = new List<string>();
            string normalized = NormalizeTargetPath(targetPath);
            if (string.IsNullOrEmpty(normalized))
                return found;

            string candidate1 = normalized + ".hash";
            if (File.Exists(candidate1))
                found.Add(candidate1);

            if (!Directory.Exists(normalized))
            {
                string candidate2 = Path.ChangeExtension(normalized, ".hash");
                if (!candidate2.Equals(candidate1, StringComparison.OrdinalIgnoreCase) &&
                    !candidate2.Equals(normalized, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(candidate2))
                {
                    found.Add(candidate2);
                }
            }

            return found;
        }

        /// <summary>
        /// Deletes any stray per-track .hash files scattered inside an unpacked game folder,
        /// ensuring only the single external &lt;GameFolder&gt;.hash beside the folder remains.
        /// </summary>
        public static void CleanupStrayInternalFolderHashFiles(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return;

            try
            {
                var opts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };
                foreach (var stray in Directory.EnumerateFiles(folderPath, "*.hash", opts))
                {
                    try { File.Delete(stray); } catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[HashSidecarService] Non-fatal error ignored: {__suppressedEx1.Message}"); }
                }
            }
            catch (System.Exception __suppressedEx2) { System.Diagnostics.Debug.WriteLine($"[HashSidecarService] Non-fatal error ignored: {__suppressedEx2.Message}"); }
        }

        public static bool TryReadExistingSidecarDocument(string targetPath, out ParsedSidecarDocument? doc)
        {
            doc = null;
            try
            {
                string normalized = NormalizeTargetPath(targetPath);
                var candidates = GetExistingExternalSidecarPaths(normalized);
                if (candidates.Count == 0)
                    return false;

                string content = File.ReadAllText(candidates[0]);
                return TryParseSidecarDocument(content, Path.GetFileName(normalized), out doc);
            }
            catch
            {
                return false;
            }
        }

        public static bool TryReadExistingSidecar(string targetPath, out FileHashResult? hashResult)
        {
            hashResult = null;
            if (!TryReadExistingSidecarDocument(targetPath, out var doc) || doc == null)
                return false;

            hashResult = doc.GetPrimaryHashResult(Path.GetFileName(NormalizeTargetPath(targetPath)));
            return hashResult != null;
        }

        public static bool TryParseSidecarContent(string content, string defaultFileName, out FileHashResult? hashResult)
        {
            hashResult = null;
            if (!TryParseSidecarDocument(content, defaultFileName, out var doc) || doc == null)
                return false;

            hashResult = doc.GetPrimaryHashResult(defaultFileName);
            return hashResult != null;
        }

        public static bool TryParseSidecarDocument(string content, string defaultFileName, out ParsedSidecarDocument? document)
        {
            document = null;
            if (string.IsNullOrWhiteSpace(content))
                return false;

            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var doc = new ParsedSidecarDocument { TitleOrFolder = defaultFileName };
            var legacySingle = new FileHashResult { FileName = defaultFileName, RelativePath = defaultFileName };
            bool foundLegacySingle = false;

            foreach (var line in lines)
            {
                if (line.StartsWith('#'))
                {
                    if (line.StartsWith("# Folder:", StringComparison.OrdinalIgnoreCase))
                        doc.TitleOrFolder = line["# Folder:".Length..].Trim();
                    else if (line.StartsWith("# Title:", StringComparison.OrdinalIgnoreCase))
                        doc.TitleOrFolder = line["# Title:".Length..].Trim();
                    else if (line.StartsWith("# DumpType:", StringComparison.OrdinalIgnoreCase))
                        doc.DumpType = line["# DumpType:".Length..].Trim();
                    else if (line.StartsWith("# Container:", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (string seg in line.Split('|', StringSplitOptions.TrimEntries))
                        {
                            if (seg.StartsWith("# Container:", StringComparison.OrdinalIgnoreCase))
                                doc.ContainerFileName = seg["# Container:".Length..].Trim();
                            else if (seg.StartsWith("SIZE:", StringComparison.OrdinalIgnoreCase) &&
                                     long.TryParse(seg["SIZE:".Length..].Trim(), out long csz))
                                doc.ContainerSize = csz;
                            else if (seg.StartsWith("CRC32:", StringComparison.OrdinalIgnoreCase))
                                doc.ContainerCrc32 = seg["CRC32:".Length..].Trim().ToUpperInvariant();
                            else if (seg.StartsWith("MD5:", StringComparison.OrdinalIgnoreCase))
                                doc.ContainerMd5 = seg["MD5:".Length..].Trim().ToUpperInvariant();
                            else if (seg.StartsWith("SHA1:", StringComparison.OrdinalIgnoreCase))
                                doc.ContainerSha1 = seg["SHA1:".Length..].Trim().ToUpperInvariant();
                        }
                    }
                    continue;
                }

                if (line.StartsWith("FILE:", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split('|', StringSplitOptions.TrimEntries);
                    var fileItem = new FileHashResult { FileName = defaultFileName, RelativePath = defaultFileName };

                    foreach (var part in parts)
                    {
                        if (part.StartsWith("FILE:", StringComparison.OrdinalIgnoreCase))
                        {
                            string rawName = part["FILE:".Length..].Trim().Replace('\\', '/');
                            fileItem.FileName = rawName;
                            fileItem.RelativePath = rawName;
                        }
                        else if (part.StartsWith("SIZE:", StringComparison.OrdinalIgnoreCase) &&
                                 long.TryParse(part["SIZE:".Length..].Trim(), out long sz))
                        {
                            fileItem.Size = sz;
                        }
                        else if (part.StartsWith("CRC32:", StringComparison.OrdinalIgnoreCase))
                        {
                            fileItem.Crc32 = part["CRC32:".Length..].Trim();
                        }
                        else if (part.StartsWith("MD5:", StringComparison.OrdinalIgnoreCase))
                        {
                            fileItem.Md5 = part["MD5:".Length..].Trim();
                        }
                        else if (part.StartsWith("SHA1:", StringComparison.OrdinalIgnoreCase))
                        {
                            fileItem.Sha1 = part["SHA1:".Length..].Trim();
                        }
                    }

                    if (!string.IsNullOrEmpty(fileItem.Sha1) ||
                        !string.IsNullOrEmpty(fileItem.Crc32) ||
                        !string.IsNullOrEmpty(fileItem.Md5))
                    {
                        doc.Files.Add(fileItem);
                    }
                }
                else if (line.StartsWith("TOTAL_FILES:", StringComparison.OrdinalIgnoreCase) &&
                         int.TryParse(line["TOTAL_FILES:".Length..].Trim(), out int tFiles))
                {
                    doc.TotalFiles = tFiles;
                    doc.IsCompositeFolder = true;
                }
                else if (line.StartsWith("TOTAL_SIZE:", StringComparison.OrdinalIgnoreCase) &&
                         long.TryParse(line["TOTAL_SIZE:".Length..].Trim(), out long tSz))
                {
                    doc.TotalSizeBytes = tSz;
                    doc.IsCompositeFolder = true;
                }
                else if (line.StartsWith("COMPOSITE_SHA1:", StringComparison.OrdinalIgnoreCase))
                {
                    doc.CompositeSha1 = line["COMPOSITE_SHA1:".Length..].Trim();
                    doc.IsCompositeFolder = true;
                }
                else if (line.StartsWith("ENTRY:", StringComparison.OrdinalIgnoreCase))
                {
                    doc.IsCompositeFolder = true;
                    var parts = line.Split('|', StringSplitOptions.TrimEntries);
                    var entryItem = new FileHashResult();
                    bool hasCrc = false;

                    foreach (var part in parts)
                    {
                        if (part.StartsWith("ENTRY:", StringComparison.OrdinalIgnoreCase))
                        {
                            string rel = part["ENTRY:".Length..].Trim().Replace('\\', '/');
                            entryItem.RelativePath = rel;
                            entryItem.FileName = Path.GetFileName(rel);
                        }
                        else if (part.StartsWith("SIZE:", StringComparison.OrdinalIgnoreCase) &&
                                 long.TryParse(part["SIZE:".Length..].Trim(), out long eSz))
                        {
                            entryItem.Size = eSz;
                        }
                        else if (part.StartsWith("CRC32:", StringComparison.OrdinalIgnoreCase))
                        {
                            entryItem.Crc32 = part["CRC32:".Length..].Trim();
                            if (!string.IsNullOrWhiteSpace(entryItem.Crc32))
                                hasCrc = true;
                        }
                    }

                    if (!hasCrc)
                        doc.AllEntriesHaveCrc32 = false;

                    if (!string.IsNullOrEmpty(entryItem.RelativePath))
                        doc.Files.Add(entryItem);
                }
                else if (line.StartsWith("CRC32:", StringComparison.OrdinalIgnoreCase))
                {
                    legacySingle.Crc32 = line["CRC32:".Length..].Trim();
                    foundLegacySingle = true;
                }
                else if (line.StartsWith("MD5:", StringComparison.OrdinalIgnoreCase))
                {
                    legacySingle.Md5 = line["MD5:".Length..].Trim();
                    foundLegacySingle = true;
                }
                else if (line.StartsWith("SHA1:", StringComparison.OrdinalIgnoreCase))
                {
                    legacySingle.Sha1 = line["SHA1:".Length..].Trim();
                    foundLegacySingle = true;
                }
                else if (line.StartsWith("SIZE:", StringComparison.OrdinalIgnoreCase) &&
                         long.TryParse(line["SIZE:".Length..].Trim(), out long sz))
                {
                    legacySingle.Size = sz;
                }
                else
                {
                    // Standard <hex> [* ]<filename> format (.sha1 / .md5 / .sfv style)
                    var spaceIdx = line.IndexOfAny(new[] { ' ', '\t' });
                    if (spaceIdx > 0)
                    {
                        string token = line[..spaceIdx].Trim();
                        string trailingName = line[(spaceIdx + 1)..].TrimStart(' ', '\t', '*').Trim();
                        bool isHex = token.All(Uri.IsHexDigit);

                        if (isHex && (token.Length == 8 || token.Length == 32 || token.Length == 40))
                        {
                            // Support multi-line standard .sha1/.md5/.sfv files if multiple filenames appear
                            if (!string.IsNullOrEmpty(trailingName) &&
                                foundLegacySingle &&
                                !string.IsNullOrEmpty(legacySingle.FileName) &&
                                !legacySingle.FileName.Equals(defaultFileName, StringComparison.OrdinalIgnoreCase) &&
                                !legacySingle.FileName.Equals(trailingName, StringComparison.OrdinalIgnoreCase))
                            {
                                doc.Files.Add(legacySingle);
                                legacySingle = new FileHashResult { FileName = defaultFileName, RelativePath = defaultFileName };
                            }

                            if (token.Length == 8 && string.IsNullOrEmpty(legacySingle.Crc32))
                                legacySingle.Crc32 = token;
                            else if (token.Length == 32 && string.IsNullOrEmpty(legacySingle.Md5))
                                legacySingle.Md5 = token;
                            else if (token.Length == 40 && string.IsNullOrEmpty(legacySingle.Sha1))
                                legacySingle.Sha1 = token;

                            if (!string.IsNullOrEmpty(trailingName))
                            {
                                string cleanTrail = trailingName.Replace('\\', '/');
                                legacySingle.FileName = cleanTrail;
                                legacySingle.RelativePath = cleanTrail;
                            }

                            foundLegacySingle = true;
                        }
                    }
                }
            }

            if (!doc.IsCompositeFolder &&
                doc.Files.Count == 0 &&
                foundLegacySingle &&
                (!string.IsNullOrEmpty(legacySingle.Sha1) ||
                 !string.IsNullOrEmpty(legacySingle.Crc32) ||
                 !string.IsNullOrEmpty(legacySingle.Md5)))
            {
                doc.Files.Add(legacySingle);
            }
            else if (!doc.IsCompositeFolder &&
                     doc.Files.Count > 0 &&
                     foundLegacySingle &&
                     (!string.IsNullOrEmpty(legacySingle.Sha1) ||
                      !string.IsNullOrEmpty(legacySingle.Crc32) ||
                      !string.IsNullOrEmpty(legacySingle.Md5)))
            {
                doc.Files.Add(legacySingle);
            }

            if (doc.IsCompositeFolder)
            {
                if (string.IsNullOrWhiteSpace(doc.CompositeSha1))
                    return false;
                document = doc;
                return true;
            }

            if (doc.Files.Count > 0)
            {
                document = doc;
                return true;
            }

            return false;
        }

        public static string ResolveSevenZipExecutable(string? candidate = null)
        {
            if (!string.IsNullOrWhiteSpace(candidate) &&
                !candidate.Equals("7z.exe", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(candidate))
            {
                return candidate;
            }

            if (_cachedDefaultSevenZipExe != null && File.Exists(_cachedDefaultSevenZipExe))
                return _cachedDefaultSevenZipExe;

            string localTools = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "7z", "7z.exe");
            if (File.Exists(localTools))
                return _cachedDefaultSevenZipExe = localTools;

            string progFiles = @"C:\Program Files\7-Zip\7z.exe";
            if (File.Exists(progFiles))
                return _cachedDefaultSevenZipExe = progFiles;

            string progFilesX86 = @"C:\Program Files (x86)\7-Zip\7z.exe";
            if (File.Exists(progFilesX86))
                return _cachedDefaultSevenZipExe = progFilesX86;

            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    string p = Path.Combine(dir, "7z.exe");
                    if (File.Exists(p))
                        return _cachedDefaultSevenZipExe = p;
                }
                catch (System.Exception __suppressedEx3) { System.Diagnostics.Debug.WriteLine($"[HashSidecarService] Non-fatal error ignored: {__suppressedEx3.Message}"); }
            }

            return "7z.exe";
        }

        public class ArchiveEntryInfo
        {
            public string Path { get; set; } = "";
            public long Size { get; set; }
            public string Crc { get; set; } = "";
            public bool IsDirectory { get; set; }
        }

        private static readonly string[] ArchiveFileExtensions = { ".7z", ".zip", ".rar" };
        private static readonly string[] WritableArchiveExtensions = { ".7z", ".zip" };

        public static bool IsArchiveFilePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;
            string ext = Path.GetExtension(path);
            return ArchiveFileExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }

        public static bool IsWritableArchiveFilePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;
            string ext = Path.GetExtension(path);
            return WritableArchiveExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }

        public static List<(ArchiveEntryInfo Entry, string NormalizedRelativePath)> GetNormalizedPayloadArchiveEntries(
            IReadOnlyList<ArchiveEntryInfo> entries)
        {
            var result = new List<(ArchiveEntryInfo, string)>();
            if (entries == null || entries.Count == 0)
                return result;

            var fileEntries = entries
                .Where(e => !e.IsDirectory && !string.IsNullOrWhiteSpace(e.Path))
                .ToList();

            var payloadEntries = fileEntries
                .Where(e => !ReleaseStructureDetector.IsIgnoredNonPayloadFile(e.Path))
                .ToList();

            if (payloadEntries.Count == 0)
            {
                payloadEntries = fileEntries
                    .Where(e => !e.Path.EndsWith(".hash", StringComparison.OrdinalIgnoreCase) &&
                                !e.Path.Contains(".partial.", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (payloadEntries.Count == 0)
                return result;

            var rawPaths = payloadEntries.Select(e => e.Path).ToList();
            var normalizedPaths = ReleaseStructureDetector.NormalizeRelativePathsStrippingSingleWrapper(rawPaths);

            for (int i = 0; i < payloadEntries.Count; i++)
            {
                result.Add((payloadEntries[i], normalizedPaths[i]));
            }

            return result
                .OrderBy(x => x.Item2, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static ArchiveEntryInfo? SelectPrimaryRomEntry(IReadOnlyList<ArchiveEntryInfo> entries)
        {
            var payload = GetNormalizedPayloadArchiveEntries(entries);
            if (payload.Count == 0)
                return null;

            return payload
                .OrderByDescending(p => !ReleaseStructureDetector.IsDiscDescriptorFile(p.NormalizedRelativePath))
                .ThenByDescending(p => p.Entry.Size)
                .First()
                .Entry;
        }

        public static bool IsLegacyOuterArchiveSidecar(
            FileHashResult sidecar,
            FileInfo archiveFile,
            ArchiveEntryInfo? primaryInnerEntry = null)
        {
            if (sidecar == null || archiveFile == null)
                return false;

            string scFile = Path.GetFileName(sidecar.FileName?.Trim() ?? "");
            string innerName = primaryInnerEntry != null ? Path.GetFileName(primaryInnerEntry.Path) : "";

            bool scHasArchiveExt = IsArchiveFilePath(scFile);
            bool scMatchesOuterName = !string.IsNullOrEmpty(scFile) &&
                                      scFile.Equals(archiveFile.Name, StringComparison.OrdinalIgnoreCase);

            // If FILE: points to the outer .7z/.zip/.rar instead of an inner ROM filename
            if ((scMatchesOuterName || scHasArchiveExt) &&
                (string.IsNullOrEmpty(innerName) || !scFile.Equals(innerName, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // Legacy outer-archive sidecars recorded compressed archive length with blank CRC32
            if (string.IsNullOrWhiteSpace(sidecar.Crc32) &&
                sidecar.Size > 0 &&
                archiveFile.Exists &&
                sidecar.Size == archiveFile.Length &&
                (primaryInnerEntry == null || primaryInnerEntry.Size != archiveFile.Length))
            {
                return true;
            }

            // Compressed outer size recorded when inner uncompressed size differs
            if (sidecar.Size > 0 &&
                archiveFile.Exists &&
                sidecar.Size == archiveFile.Length &&
                primaryInnerEntry != null &&
                primaryInnerEntry.Size > 0 &&
                primaryInnerEntry.Size != archiveFile.Length &&
                (string.IsNullOrEmpty(innerName) || !scFile.Equals(innerName, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return false;
        }

        public static string NormalizeCrc32Hex(string? rawCrc, long sizeBytes = -1)
        {
            string trimmed = (rawCrc ?? "").Trim();
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[2..];

            if (string.IsNullOrEmpty(trimmed))
                return sizeBytes == 0 ? "00000000" : "";

            if (trimmed.Length > 8)
                return "";

            foreach (char c in trimmed)
            {
                bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHex)
                    return "";
            }

            return trimmed.PadLeft(8, '0').ToUpperInvariant();
        }

        public static bool TryExtractArchivePayloadHeaderHashes(
            IReadOnlyList<ArchiveEntryInfo> entries,
            out List<FileHashResult> payloadFiles)
        {
            payloadFiles = new List<FileHashResult>();
            var normalized = GetNormalizedPayloadArchiveEntries(entries);
            if (normalized.Count == 0)
                return false;

            foreach (var (entry, relPath) in normalized)
            {
                string crc = NormalizeCrc32Hex(entry.Crc, entry.Size);
                if (crc.Length != 8)
                {
                    payloadFiles.Clear();
                    return false;
                }

                payloadFiles.Add(new FileHashResult
                {
                    RelativePath = relPath,
                    FileName = Path.GetFileName(relPath),
                    Size = entry.Size,
                    Crc32 = crc,
                    Md5 = "",
                    Sha1 = ""
                });
            }

            return payloadFiles.Count > 0;
        }

        public static async Task<string?> ReadSmallArchiveTextEntryAsync(
            string sevenZipExe,
            string archivePath,
            string internalEntryPath,
            int maxBytes = 65536,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(archivePath) || string.IsNullOrWhiteSpace(internalEntryPath) || !File.Exists(archivePath))
                return null;

            string normalizedTarget = internalEntryPath.Replace('\\', '/').TrimStart('/');

            // Fast in-process read for .zip archives
            if (Path.GetExtension(archivePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var zip = ZipFile.OpenRead(archivePath);
                    var match = zip.Entries.FirstOrDefault(e =>
                        string.Equals(e.FullName.Replace('\\', '/').TrimStart('/'), normalizedTarget, StringComparison.OrdinalIgnoreCase));
                    if (match != null && match.Length <= maxBytes)
                    {
                        using var stream = match.Open();
                        using var ms = new MemoryStream();
                        byte[] buf = new byte[4096];
                        int total = 0;
                        int read;
                        while ((read = await stream.ReadAsync(buf.AsMemory(0, Math.Min(buf.Length, maxBytes - total)), ct)) > 0)
                        {
                            ms.Write(buf, 0, read);
                            total += read;
                            if (total >= maxBytes) break;
                        }
                        return Encoding.UTF8.GetString(ms.ToArray());
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (System.Exception __suppressedEx4) { System.Diagnostics.Debug.WriteLine($"[HashSidecarService] Non-fatal error ignored: {__suppressedEx4.Message}"); }
            }

            // Stream single small entry via 7z e -so
            sevenZipExe = ResolveSevenZipExecutable(sevenZipExe);
            if (!File.Exists(sevenZipExe))
                return null;

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = sevenZipExe,
                    Arguments = $"e \"{archivePath}\" -so \"{internalEntryPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return null;

                using var ms = new MemoryStream();
                byte[] buffer = new byte[4096];
                int totalRead = 0;
                while (totalRead < maxBytes)
                {
                    int toRead = Math.Min(buffer.Length, maxBytes - totalRead);
                    int n = await proc.StandardOutput.BaseStream.ReadAsync(buffer.AsMemory(0, toRead), ct);
                    if (n <= 0) break;
                    ms.Write(buffer, 0, n);
                    totalRead += n;
                }

                try
                {
                    if (!proc.HasExited)
                        proc.Kill();
                }
                catch (System.Exception __suppressedEx5) { System.Diagnostics.Debug.WriteLine($"[HashSidecarService] Non-fatal error ignored: {__suppressedEx5.Message}"); }

                await proc.WaitForExitAsync(ct);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                return null;
            }
        }

        public static async Task<(bool Success, string Output)> DeleteArchiveInternalPathsAsync(
            string sevenZipExe,
            string archivePath,
            IReadOnlyList<string> internalPaths,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
                return (false, "Archive file does not exist.");

            if (!IsWritableArchiveFilePath(archivePath))
                return (false, $"Container '{Path.GetExtension(archivePath)}' does not support in-place 7z deletion (only .7z and .zip are supported).");

            if (internalPaths == null || internalPaths.Count == 0)
                return (false, "No internal paths were specified for deletion.");

            sevenZipExe = ResolveSevenZipExecutable(sevenZipExe);
            if (!File.Exists(sevenZipExe))
                return (false, "7z.exe could not be located.");

            var distinctPaths = internalPaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (distinctPaths.Count == 0)
                return (false, "No valid internal paths specified.");

            string quotedArgs = string.Join(" ", distinctPaths.Select(p => $"\"{p}\""));
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = sevenZipExe,
                Arguments = $"d \"{archivePath}\" {quotedArgs} -y",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            try
            {
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null)
                    return (false, "Failed to start 7z.exe.");

                string stdout = await proc.StandardOutput.ReadToEndAsync(ct);
                string stderr = await proc.StandardError.ReadToEndAsync(ct);
                await proc.WaitForExitAsync(ct);
                string combined = (stdout + "\n" + stderr).Trim();
                return (proc.ExitCode == 0, combined);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public static async Task<List<ArchiveEntryInfo>> ListArchiveContentsAsync(
            string sevenZipExe,
            string archivePath,
            CancellationToken ct = default)
        {
            var entries = new List<ArchiveEntryInfo>();
            if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
                return entries;

            // Fast <1 ms in-process Central Directory inspection for standard .zip archives
            if (Path.GetExtension(archivePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    using var zip = ZipFile.OpenRead(archivePath);
                    foreach (var ze in zip.Entries)
                    {
                        bool isDir = string.IsNullOrEmpty(ze.Name) &&
                                     (ze.FullName.EndsWith('/') || ze.FullName.EndsWith('\\'));
                        entries.Add(new ArchiveEntryInfo
                        {
                            Path = ze.FullName.TrimEnd('/', '\\'),
                            Size = isDir ? 0 : ze.Length,
                            Crc = isDir ? "" : $"{ze.Crc32:X8}",
                            IsDirectory = isDir
                        });
                    }
                    return entries;
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    entries.Clear();
                    // Fall back to 7z.exe below (e.g., LZMA/Deflate64/non-standard zip headers)
                }
            }

            sevenZipExe = ResolveSevenZipExecutable(sevenZipExe);
            if (!File.Exists(sevenZipExe))
                return entries;

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = sevenZipExe,
                    Arguments = $"l -slt \"{archivePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = false
                };

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return entries;
                string output = await proc.StandardOutput.ReadToEndAsync(ct);
                await proc.WaitForExitAsync(ct);

                ArchiveEntryInfo? current = null;
                bool inEntriesSection = false;
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    string trimmed = line.Trim();
                    if (!inEntriesSection)
                    {
                        if (trimmed.StartsWith("----------", StringComparison.Ordinal))
                            inEntriesSection = true;
                        continue;
                    }

                    int eqIdx = line.IndexOf('=');
                    if (eqIdx <= 0) continue;
                    string key = line[..eqIdx].Trim();
                    string val = line[(eqIdx + 1)..].Trim();

                    if (key.Equals("Path", StringComparison.OrdinalIgnoreCase))
                    {
                        if (current != null && !string.IsNullOrWhiteSpace(current.Path))
                        {
                            if (!current.IsDirectory)
                                current.Crc = NormalizeCrc32Hex(current.Crc, current.Size);
                            entries.Add(current);
                        }
                        current = new ArchiveEntryInfo { Path = val };
                    }
                    else if (current != null)
                    {
                        if (key.Equals("Size", StringComparison.OrdinalIgnoreCase) && long.TryParse(val, out long sz))
                        {
                            current.Size = sz;
                        }
                        else if (key.Equals("CRC", StringComparison.OrdinalIgnoreCase))
                        {
                            current.Crc = val;
                        }
                        else if (key.Equals("Attributes", StringComparison.OrdinalIgnoreCase))
                        {
                            if (val.Contains("D", StringComparison.OrdinalIgnoreCase))
                                current.IsDirectory = true;
                        }
                        else if (key.Equals("Folder", StringComparison.OrdinalIgnoreCase))
                        {
                            if (val.Equals("+") || val.Equals("1"))
                                current.IsDirectory = true;
                        }
                    }
                }
                if (current != null && !string.IsNullOrWhiteSpace(current.Path))
                {
                    if (!current.IsDirectory)
                        current.Crc = NormalizeCrc32Hex(current.Crc, current.Size);
                    entries.Add(current);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (System.Exception __suppressedEx6) { System.Diagnostics.Debug.WriteLine($"[HashSidecarService] Non-fatal error ignored: {__suppressedEx6.Message}"); }

            return entries;
        }

        public static async Task<bool> TestArchiveIntegrityAsync(
            string sevenZipExe,
            string archivePath,
            CancellationToken ct = default)
        {
            sevenZipExe = ResolveSevenZipExecutable(sevenZipExe);
            if (!File.Exists(sevenZipExe) || !File.Exists(archivePath))
                return false;

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = sevenZipExe,
                Arguments = $"t \"{archivePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null)
                return false;

            try
            {
                _ = await proc.StandardOutput.ReadToEndAsync(ct);
                await proc.WaitForExitAsync(ct);
                return proc.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch (System.Exception __suppressedEx7) { System.Diagnostics.Debug.WriteLine($"[HashSidecarService] Non-fatal error ignored: {__suppressedEx7.Message}"); }
                throw;
            }
        }

        private static async Task<string?> ExtractArchiveEntryTextAsync(
            string sevenZipExe,
            string archivePath,
            string entryPath,
            CancellationToken ct = default)
        {
            sevenZipExe = ResolveSevenZipExecutable(sevenZipExe);
            if (!File.Exists(sevenZipExe) || !File.Exists(archivePath))
                return null;

            try
            {
                string args = string.IsNullOrWhiteSpace(entryPath)
                    ? $"e \"{archivePath}\" *.hash -so"
                    : $"e \"{archivePath}\" \"{entryPath}\" -so";

                var streamPsi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = sevenZipExe,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = false
                };

                using var streamProc = System.Diagnostics.Process.Start(streamPsi);
                if (streamProc == null) return null;
                string content = await streamProc.StandardOutput.ReadToEndAsync(ct);
                await streamProc.WaitForExitAsync(ct);
                return streamProc.ExitCode == 0 ? content : null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        public static async Task<ParsedSidecarDocument?> TryReadArchiveInternalHashDocumentAsync(
            string sevenZipExe,
            string archivePath,
            CancellationToken ct = default)
        {
            sevenZipExe = ResolveSevenZipExecutable(sevenZipExe);
            if (!File.Exists(sevenZipExe) || !File.Exists(archivePath))
                return null;

            try
            {
                var entries = await ListArchiveContentsAsync(sevenZipExe, archivePath, ct);
                var hashEntry = entries.FirstOrDefault(e => !e.IsDirectory && e.Path.EndsWith(".hash", StringComparison.OrdinalIgnoreCase));
                if (hashEntry == null)
                    return null;

                string? hashContent = await ExtractArchiveEntryTextAsync(sevenZipExe, archivePath, hashEntry.Path, ct);
                if (!string.IsNullOrWhiteSpace(hashContent) &&
                    TryParseSidecarDocument(hashContent, Path.GetFileName(archivePath), out var parsed))
                {
                    return parsed;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (System.Exception __suppressedEx8) { System.Diagnostics.Debug.WriteLine($"[HashSidecarService] Non-fatal error ignored: {__suppressedEx8.Message}"); }

            return null;
        }

        public static async Task<FileHashResult?> TryReadArchiveInternalHashAsync(
            string sevenZipExe,
            string archivePath,
            CancellationToken ct = default)
        {
            var doc = await TryReadArchiveInternalHashDocumentAsync(sevenZipExe, archivePath, ct);
            return doc?.GetPrimaryHashResult(Path.GetFileName(archivePath));
        }

        public static async Task<bool> DeleteArchiveInternalHashFilesAsync(
            string sevenZipExe,
            string archivePath,
            CancellationToken ct = default)
        {
            sevenZipExe = ResolveSevenZipExecutable(sevenZipExe);
            if (!File.Exists(sevenZipExe) || !File.Exists(archivePath) || !IsWritableArchiveFilePath(archivePath))
                return false;

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = sevenZipExe,
                    Arguments = $"d \"{archivePath}\" *.hash -r",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };

                using var p = System.Diagnostics.Process.Start(psi);
                if (p == null) return false;
                await p.WaitForExitAsync(ct);
                return p.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
        }

        private static FileHashResult? FindMatchingSidecarEntry(
            IReadOnlyList<FileHashResult> sidecarFiles,
            string expectedNormalizedRelPath,
            bool allowUniqueBasenameMatch)
        {
            string normalizedTarget = expectedNormalizedRelPath.Replace('\\', '/');
            foreach (var f in sidecarFiles)
            {
                string candRel = (!string.IsNullOrWhiteSpace(f.RelativePath) ? f.RelativePath : f.FileName).Replace('\\', '/');
                if (candRel.Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase))
                    return f;
            }

            if (allowUniqueBasenameMatch)
            {
                string expectedBase = Path.GetFileName(normalizedTarget);
                var baseMatches = sidecarFiles
                    .Where(f => Path.GetFileName((!string.IsNullOrWhiteSpace(f.RelativePath) ? f.RelativePath : f.FileName).Replace('\\', '/'))
                        .Equals(expectedBase, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (baseMatches.Count == 1)
                    return baseMatches[0];
            }

            return null;
        }

        private static bool ValidateDocumentAgainstArchiveHeader(
            ParsedSidecarDocument doc,
            FileInfo archiveFile,
            bool expectInnerRom,
            ReleaseTopology topology,
            IReadOnlyList<ArchiveEntryInfo> allEntries,
            IReadOnlyList<(ArchiveEntryInfo Entry, string NormalizedRelativePath)> payloadEntries,
            ArchiveEntryInfo? primaryEntry,
            out SidecarVerificationStatus status,
            out string detail)
        {
            if (!expectInnerRom)
            {
                if (doc.IsCompositeFolder || doc.Files.Count != 1)
                {
                    status = SidecarVerificationStatus.HeaderMismatch;
                    detail = "Container-mode sidecar does not match single archive file format.";
                    return false;
                }

                return ValidateSingleLooseFileSidecar(doc.Files[0], archiveFile, out status, out detail);
            }

            // Detect legacy outer-archive .hash (pre-RC3)
            if (!doc.IsCompositeFolder &&
                doc.Files.Count == 1 &&
                IsLegacyOuterArchiveSidecar(doc.Files[0], archiveFile, primaryEntry))
            {
                string expectedDesc = topology == ReleaseTopology.CompositeFolderDump
                    ? "inner folder dump"
                    : (primaryEntry != null ? Path.GetFileName(primaryEntry.Path) : "inner ROM");
                status = SidecarVerificationStatus.LegacyOuterArchiveHash;
                detail = $"Legacy outer-archive hash detected (FILE: '{doc.Files[0].FileName}', SIZE: {doc.Files[0].Size} B) instead of '{expectedDesc}'.";
                return false;
            }

            // Case 1: Archived Composite Folder Dump (e.g., PS3_GAME / PS4 / PS5 / Wii U inside .7z/.zip)
            if (topology == ReleaseTopology.CompositeFolderDump)
            {
                string baseTitle = Path.GetFileNameWithoutExtension(archiveFile.Name);
                var expectedComp = BuildCompositeHashFromArchiveEntries(baseTitle, archiveFile.FullName, allEntries);

                if (!doc.IsCompositeFolder)
                {
                    status = SidecarVerificationStatus.IncompleteMultiFileSet;
                    detail = $"Archived folder dump contains {expectedComp.TotalFiles} files, but sidecar only records {doc.Files.Count} file(s).";
                    return false;
                }

                if (doc.TotalFiles != expectedComp.TotalFiles || doc.Files.Count != expectedComp.TotalFiles)
                {
                    status = SidecarVerificationStatus.IncompleteMultiFileSet;
                    detail = $"Sidecar records {Math.Max(doc.TotalFiles, doc.Files.Count)} of {expectedComp.TotalFiles} files in archived folder dump.";
                    return false;
                }

                if (!doc.AllEntriesHaveCrc32)
                {
                    status = SidecarVerificationStatus.IncompleteMultiFileSet;
                    detail = "Folder dump sidecar ENTRY lines are missing per-file CRC32 checksums.";
                    return false;
                }

                if (doc.TotalSizeBytes != expectedComp.TotalSizeBytes)
                {
                    status = SidecarVerificationStatus.HeaderMismatch;
                    detail = $"Sidecar TOTAL_SIZE ({doc.TotalSizeBytes} B) != archive uncompressed folder size ({expectedComp.TotalSizeBytes} B).";
                    return false;
                }

                if (!doc.CompositeSha1.Equals(expectedComp.CompositeSha1, StringComparison.OrdinalIgnoreCase))
                {
                    status = SidecarVerificationStatus.HeaderMismatch;
                    detail = $"Sidecar COMPOSITE_SHA1 ({doc.CompositeSha1}) != 7-Zip header CRC32 tree ({expectedComp.CompositeSha1}).";
                    return false;
                }

                status = SidecarVerificationStatus.Valid;
                detail = $"Verified all {expectedComp.TotalFiles} files against 7-Zip header CRC32 manifest.";
                return true;
            }

            // Case 2: Single-File or Multi-File ROM / Disc Set inside Archive
            if (payloadEntries.Count == 0)
            {
                status = SidecarVerificationStatus.Error;
                detail = "Archive contains no inner ROM payload entries.";
                return false;
            }

            if (doc.IsCompositeFolder)
            {
                status = SidecarVerificationStatus.HeaderMismatch;
                detail = "ROM archive has a folder-composite sidecar instead of per-file Redump SHA-1/MD5/CRC32 entries.";
                return false;
            }

            if (payloadEntries.Count > 1 && doc.Files.Count < payloadEntries.Count)
            {
                bool uniqueBasenames = payloadEntries
                    .Select(p => Path.GetFileName(p.NormalizedRelativePath))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() == payloadEntries.Count;

                var firstMissing = payloadEntries
                    .FirstOrDefault(p => FindMatchingSidecarEntry(doc.Files, p.NormalizedRelativePath, uniqueBasenames) == null);
                string missingLabel = firstMissing.Entry != null ? firstMissing.NormalizedRelativePath : "additional tracks";

                status = SidecarVerificationStatus.IncompleteMultiFileSet;
                detail = $"Sidecar only records {doc.Files.Count} of {payloadEntries.Count} payload files in archive (missing {payloadEntries.Count - doc.Files.Count} file(s), e.g. '{missingLabel}').";
                return false;
            }

            if (doc.Files.Count != payloadEntries.Count)
            {
                status = SidecarVerificationStatus.HeaderMismatch;
                detail = $"Sidecar file count ({doc.Files.Count}) does not match archive payload count ({payloadEntries.Count}).";
                return false;
            }

            bool allowBasenameMatch = payloadEntries
                .Select(p => Path.GetFileName(p.NormalizedRelativePath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == payloadEntries.Count;

            foreach (var (entry, normRelPath) in payloadEntries)
            {
                var sc = FindMatchingSidecarEntry(doc.Files, normRelPath, allowBasenameMatch);
                if (sc == null)
                {
                    status = payloadEntries.Count > 1
                        ? SidecarVerificationStatus.IncompleteMultiFileSet
                        : SidecarVerificationStatus.HeaderMismatch;
                    detail = $"Sidecar is missing inner ROM file '{normRelPath}'.";
                    return false;
                }

                if (sc.Size != entry.Size)
                {
                    status = SidecarVerificationStatus.HeaderMismatch;
                    detail = $"Sidecar SIZE ({sc.Size} B) for '{normRelPath}' != archive uncompressed size ({entry.Size} B).";
                    return false;
                }

                string expectedCrc = string.IsNullOrWhiteSpace(entry.Crc) && entry.Size == 0
                    ? "00000000"
                    : entry.Crc;

                if (!string.IsNullOrWhiteSpace(expectedCrc))
                {
                    if (string.IsNullOrWhiteSpace(sc.Crc32))
                    {
                        status = SidecarVerificationStatus.HeaderMismatch;
                        detail = $"Sidecar entry '{normRelPath}' is missing CRC32 (archive header expects {expectedCrc}).";
                        return false;
                    }

                    if (!sc.Crc32.Equals(expectedCrc, StringComparison.OrdinalIgnoreCase))
                    {
                        status = SidecarVerificationStatus.HeaderMismatch;
                        detail = $"Sidecar CRC32 ({sc.Crc32}) for '{normRelPath}' != archive header CRC32 ({expectedCrc}).";
                        return false;
                    }
                }

                if (string.IsNullOrWhiteSpace(sc.Sha1))
                {
                    status = SidecarVerificationStatus.HeaderMismatch;
                    detail = $"Sidecar entry '{normRelPath}' is missing SHA1 checksum.";
                    return false;
                }
            }

            status = SidecarVerificationStatus.Valid;
            detail = payloadEntries.Count > 1
                ? $"Verified all {payloadEntries.Count} inner ROM/track files against 7-Zip header."
                : "Verified against inner ROM 7-Zip header.";
            return true;
        }

        private static bool ValidateSingleLooseFileSidecar(
            FileHashResult sc,
            FileInfo targetFile,
            out SidecarVerificationStatus status,
            out string detail)
        {
            if (string.IsNullOrWhiteSpace(sc.Sha1) &&
                string.IsNullOrWhiteSpace(sc.Crc32) &&
                string.IsNullOrWhiteSpace(sc.Md5))
            {
                status = SidecarVerificationStatus.HeaderMismatch;
                detail = "Sidecar contains no checksum values.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(sc.FileName) &&
                !Path.GetFileName(sc.FileName).Equals(targetFile.Name, StringComparison.OrdinalIgnoreCase))
            {
                status = SidecarVerificationStatus.HeaderMismatch;
                detail = $"Sidecar FILE '{sc.FileName}' does not match file '{targetFile.Name}'.";
                return false;
            }

            if (sc.Size > 0 && sc.Size != targetFile.Length)
            {
                status = SidecarVerificationStatus.HeaderMismatch;
                detail = $"Sidecar SIZE ({sc.Size} B) does not match file size ({targetFile.Length} B).";
                return false;
            }

            if (string.IsNullOrWhiteSpace(sc.Sha1))
            {
                status = SidecarVerificationStatus.HeaderMismatch;
                detail = "Sidecar is missing SHA1 checksum.";
                return false;
            }

            status = SidecarVerificationStatus.Valid;
            detail = "Verified against file metadata.";
            return true;
        }

        private static bool ValidateDocumentAgainstComputedFiles(
            ParsedSidecarDocument doc,
            IReadOnlyList<FileHashResult> computedFiles,
            out string detail)
        {
            if (doc.IsCompositeFolder || doc.Files.Count != computedFiles.Count)
            {
                detail = $"Sidecar file count ({doc.Files.Count}) != computed payload file count ({computedFiles.Count}).";
                return false;
            }

            bool allowBasenameMatch = computedFiles
                .Select(c => Path.GetFileName(c.RelativePath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == computedFiles.Count;

            foreach (var computed in computedFiles)
            {
                string normPath = !string.IsNullOrWhiteSpace(computed.RelativePath) ? computed.RelativePath : computed.FileName;
                var sc = FindMatchingSidecarEntry(doc.Files, normPath, allowBasenameMatch);
                if (sc == null)
                {
                    detail = $"Sidecar is missing computed file '{normPath}'.";
                    return false;
                }

                if (sc.Size > 0 && sc.Size != computed.Size)
                {
                    detail = $"Sidecar SIZE ({sc.Size} B) != computed size ({computed.Size} B) for '{normPath}'.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(sc.Sha1) ||
                    !sc.Sha1.Equals(computed.Sha1, StringComparison.OrdinalIgnoreCase))
                {
                    detail = $"Sidecar SHA1 ({sc.Sha1}) != computed SHA1 ({computed.Sha1}) for '{normPath}'.";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(sc.Crc32) &&
                    !sc.Crc32.Equals(computed.Crc32, StringComparison.OrdinalIgnoreCase))
                {
                    detail = $"Sidecar CRC32 ({sc.Crc32}) != computed CRC32 ({computed.Crc32}) for '{normPath}'.";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(sc.Md5) &&
                    !sc.Md5.Equals(computed.Md5, StringComparison.OrdinalIgnoreCase))
                {
                    detail = $"Sidecar MD5 ({sc.Md5}) != computed MD5 ({computed.Md5}) for '{normPath}'.";
                    return false;
                }
            }

            detail = "Cryptographically verified byte-for-byte.";
            return true;
        }

        public static async Task<SidecarItemVerificationResult> VerifySidecarForTargetAsync(
            string sevenZipExe,
            string targetPath,
            bool expectInnerRom = true,
            bool deepCryptographicVerify = false,
            string scratchDir = "",
            AuditStructureMode structureMode = AuditStructureMode.Auto,
            CancellationToken ct = default)
        {
            sevenZipExe = ResolveSevenZipExecutable(sevenZipExe);
            string normalizedPath = NormalizeTargetPath(targetPath);
            var res = new SidecarItemVerificationResult { TargetPath = normalizedPath };

            try
            {
                // ==================== DIRECTORY TARGETS (UNPACKED GAME FOLDERS) ====================
                if (Directory.Exists(normalizedPath))
                {
                    var di = new DirectoryInfo(normalizedPath);
                    bool isCompositeFolder = ReleaseStructureDetector.ShouldTreatAsCompositeDump(normalizedPath, structureMode);
                    res.Topology = isCompositeFolder ? ReleaseTopology.CompositeFolderDump : ReleaseTopology.MultiFileRomSet;

                    var extPaths = GetExistingExternalSidecarPaths(normalizedPath);
                    if (extPaths.Count == 0)
                    {
                        res.Location = SidecarLocation.None;
                        res.Status = SidecarVerificationStatus.Missing;
                        res.DetailMessage = "No folder .hash sidecar found beside game folder.";
                        return res;
                    }

                    res.Location = SidecarLocation.ExternalBeside;
                    if (!TryReadExistingSidecarDocument(normalizedPath, out var folderDoc) || folderDoc == null)
                    {
                        res.Status = SidecarVerificationStatus.HeaderMismatch;
                        res.DetailMessage = "Folder .hash sidecar is malformed or unreadable.";
                        return res;
                    }

                    res.ParsedDocument = folderDoc;
                    res.SidecarFiles = folderDoc.Files;
                    res.SidecarHash = folderDoc.GetPrimaryHashResult(di.Name);

                    if (isCompositeFolder)
                    {
                        if (!folderDoc.IsCompositeFolder || string.IsNullOrWhiteSpace(folderDoc.CompositeSha1))
                        {
                            res.Status = SidecarVerificationStatus.HeaderMismatch;
                            res.DetailMessage = "Composite dump folder .hash is missing COMPOSITE_SHA1.";
                            return res;
                        }

                        var actualFiles = di.EnumerateFiles("*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true })
                            .Where(f => !f.Name.EndsWith(".hash", StringComparison.OrdinalIgnoreCase) &&
                                        !f.Name.Contains(".partial.", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        long actualBytes = actualFiles.Sum(f => f.Length);
                        if (folderDoc.TotalFiles > 0 && folderDoc.TotalFiles != actualFiles.Count)
                        {
                            res.Status = SidecarVerificationStatus.IncompleteMultiFileSet;
                            res.DetailMessage = $"Folder sidecar TOTAL_FILES ({folderDoc.TotalFiles}) != directory file count ({actualFiles.Count}).";
                            return res;
                        }

                        if (folderDoc.Files.Count != actualFiles.Count || !folderDoc.AllEntriesHaveCrc32)
                        {
                            res.Status = SidecarVerificationStatus.IncompleteMultiFileSet;
                            res.DetailMessage = !folderDoc.AllEntriesHaveCrc32
                                ? "Folder sidecar ENTRY lines are missing per-file CRC32 checksums."
                                : $"Folder sidecar lists {folderDoc.Files.Count} of {actualFiles.Count} files.";
                            return res;
                        }

                        if (folderDoc.TotalSizeBytes > 0 && folderDoc.TotalSizeBytes != actualBytes)
                        {
                            res.Status = SidecarVerificationStatus.HeaderMismatch;
                            res.DetailMessage = $"Folder sidecar TOTAL_SIZE ({folderDoc.TotalSizeBytes} B) != directory size ({actualBytes} B).";
                            return res;
                        }

                        if (deepCryptographicVerify)
                        {
                            var comp = await ComputeFolderCompositeHashAsync(normalizedPath, ct);
                            res.ComputedComposite = comp;
                            res.ComputedFiles = comp.Files;
                            res.ComputedHash = new FileHashResult
                            {
                                FileName = di.Name,
                                RelativePath = di.Name,
                                Size = comp.TotalSizeBytes,
                                Sha1 = comp.CompositeSha1
                            };

                            if (!folderDoc.CompositeSha1.Equals(comp.CompositeSha1, StringComparison.OrdinalIgnoreCase))
                            {
                                res.Status = SidecarVerificationStatus.CryptographicMismatch;
                                res.DetailMessage = $"Folder COMPOSITE_SHA1 ({folderDoc.CompositeSha1}) != computed CRC32 tree ({comp.CompositeSha1}).";
                                return res;
                            }
                        }

                        res.Status = SidecarVerificationStatus.Valid;
                        res.DetailMessage = deepCryptographicVerify
                            ? $"Folder dump CRC32 manifest verified byte-for-byte ({actualFiles.Count} files)."
                            : $"Folder dump sidecar metadata verified ({actualFiles.Count} files).";
                        return res;
                    }
                    else
                    {
                        // Unpacked Cohesive Multi-File ROM / Disc Folder (PS1 / Sega CD / Dreamcast / Multi-Disc)
                        var diskFiles = di.EnumerateFiles("*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true })
                            .Where(f => f.Length >= 0 &&
                                        !IsArchiveFilePath(f.FullName) &&
                                        !ReleaseStructureDetector.IsIgnoredNonPayloadFile(f.Name))
                            .ToList();

                        var rawRels = diskFiles.Select(f => Path.GetRelativePath(normalizedPath, f.FullName).Replace('\\', '/')).ToList();
                        var normRels = ReleaseStructureDetector.NormalizeRelativePathsStrippingSingleWrapper(rawRels);

                        if (folderDoc.IsCompositeFolder)
                        {
                            res.Status = SidecarVerificationStatus.HeaderMismatch;
                            res.DetailMessage = "Multi-file ROM folder has a composite dump sidecar instead of per-file Redump SHA-1/MD5/CRC32 entries.";
                            return res;
                        }

                        if (folderDoc.Files.Count < diskFiles.Count)
                        {
                            res.Status = SidecarVerificationStatus.IncompleteMultiFileSet;
                            res.DetailMessage = $"Game folder sidecar only records {folderDoc.Files.Count} of {diskFiles.Count} files.";
                            return res;
                        }

                        if (folderDoc.Files.Count != diskFiles.Count)
                        {
                            res.Status = SidecarVerificationStatus.HeaderMismatch;
                            res.DetailMessage = $"Game folder sidecar file count ({folderDoc.Files.Count}) != folder payload count ({diskFiles.Count}).";
                            return res;
                        }

                        bool allowBase = normRels.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == normRels.Count;
                        for (int i = 0; i < diskFiles.Count; i++)
                        {
                            var sc = FindMatchingSidecarEntry(folderDoc.Files, normRels[i], allowBase);
                            if (sc == null)
                            {
                                res.Status = SidecarVerificationStatus.IncompleteMultiFileSet;
                                res.DetailMessage = $"Game folder sidecar is missing '{normRels[i]}'.";
                                return res;
                            }

                            if (sc.Size != diskFiles[i].Length)
                            {
                                res.Status = SidecarVerificationStatus.HeaderMismatch;
                                res.DetailMessage = $"Sidecar SIZE ({sc.Size} B) != file size ({diskFiles[i].Length} B) for '{normRels[i]}'.";
                                return res;
                            }

                            if (string.IsNullOrWhiteSpace(sc.Sha1) || string.IsNullOrWhiteSpace(sc.Crc32))
                            {
                                res.Status = SidecarVerificationStatus.HeaderMismatch;
                                res.DetailMessage = $"Sidecar entry '{normRels[i]}' is missing SHA1 or CRC32.";
                                return res;
                            }
                        }

                        if (deepCryptographicVerify)
                        {
                            var computedFolderFiles = await HashMultiFileGameFolderAsync(normalizedPath, ct);
                            res.ComputedFiles = computedFolderFiles;
                            res.ComputedHash = computedFolderFiles.FirstOrDefault();

                            if (!ValidateDocumentAgainstComputedFiles(folderDoc, computedFolderFiles, out string cryptoDetail))
                            {
                                res.Status = SidecarVerificationStatus.CryptographicMismatch;
                                res.DetailMessage = cryptoDetail;
                                return res;
                            }
                        }

                        res.Status = SidecarVerificationStatus.Valid;
                        res.DetailMessage = deepCryptographicVerify
                            ? $"All {diskFiles.Count} game folder files cryptographically verified byte-for-byte."
                            : $"All {diskFiles.Count} game folder files verified.";
                        return res;
                    }
                }

                // ==================== FILE & ARCHIVE TARGETS ====================
                var fi = new FileInfo(normalizedPath);
                if (!fi.Exists)
                {
                    res.Status = SidecarVerificationStatus.Error;
                    res.DetailMessage = "Target file does not exist.";
                    return res;
                }

                bool isArchive = IsArchiveFilePath(fi.FullName);
                var externalPaths = GetExistingExternalSidecarPaths(fi.FullName);
                bool hasExternalFile = externalPaths.Count > 0;
                ParsedSidecarDocument? externalDoc = null;
                bool externalParseFailed = false;

                if (hasExternalFile)
                {
                    if (!TryReadExistingSidecarDocument(fi.FullName, out externalDoc) || externalDoc == null)
                        externalParseFailed = true;
                }

                bool hasInternalFile = false;
                bool multipleInternalFiles = false;
                bool internalParseFailed = false;
                ParsedSidecarDocument? internalDoc = null;
                List<ArchiveEntryInfo> allArchiveEntries = new();
                List<(ArchiveEntryInfo Entry, string NormalizedRelativePath)> payloadEntries = new();
                ArchiveEntryInfo? primaryEntry = null;

                if (isArchive)
                {
                    allArchiveEntries = await ListArchiveContentsAsync(sevenZipExe, fi.FullName, ct);
                    var nonDirPaths = allArchiveEntries
                        .Where(e => !e.IsDirectory && !string.IsNullOrWhiteSpace(e.Path))
                        .Select(e => e.Path)
                        .ToList();

                    res.Topology = expectInnerRom
                        ? ReleaseStructureDetector.ClassifyArchiveEntries(nonDirPaths, structureMode)
                        : ReleaseTopology.SingleFileRom;

                    payloadEntries = GetNormalizedPayloadArchiveEntries(allArchiveEntries);
                    primaryEntry = SelectPrimaryRomEntry(allArchiveEntries);
                    res.PrimaryInnerEntry = primaryEntry;

                    var internalHashEntries = allArchiveEntries
                        .Where(e => !e.IsDirectory && e.Path.EndsWith(".hash", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (internalHashEntries.Count > 0)
                    {
                        hasInternalFile = true;
                        if (internalHashEntries.Count > 1)
                            multipleInternalFiles = true;

                        string? hashText = await ExtractArchiveEntryTextAsync(sevenZipExe, fi.FullName, internalHashEntries[0].Path, ct);
                        if (string.IsNullOrWhiteSpace(hashText) ||
                            !TryParseSidecarDocument(hashText, fi.Name, out internalDoc) ||
                            internalDoc == null)
                        {
                            internalParseFailed = true;
                        }
                    }
                }
                else
                {
                    res.Topology = ReleaseTopology.SingleFileRom;
                }

                res.Location = (hasExternalFile, hasInternalFile) switch
                {
                    (true, true) => SidecarLocation.Both,
                    (false, true) => SidecarLocation.InternalArchive,
                    (true, false) => SidecarLocation.ExternalBeside,
                    _ => SidecarLocation.None
                };

                if (res.Location == SidecarLocation.None)
                {
                    res.Status = SidecarVerificationStatus.Missing;
                    res.DetailMessage = "No .hash sidecar found.";
                    return res;
                }

                var activeDoc = internalDoc ?? externalDoc;
                res.ParsedDocument = activeDoc;
                res.SidecarFiles = activeDoc?.Files ?? new List<FileHashResult>();
                res.SidecarHash = activeDoc?.GetPrimaryHashResult(fi.Name);

                if (internalParseFailed || multipleInternalFiles)
                {
                    res.Status = SidecarVerificationStatus.HeaderMismatch;
                    res.DetailMessage = multipleInternalFiles
                        ? "Archive contains multiple conflicting internal .hash files."
                        : "Existing internal .hash sidecar inside archive is malformed or unreadable.";
                    return res;
                }

                if (isArchive)
                {
                    if (internalDoc != null &&
                        !ValidateDocumentAgainstArchiveHeader(internalDoc, fi, expectInnerRom, res.Topology, allArchiveEntries, payloadEntries, primaryEntry, out var iStatus, out var iDetail))
                    {
                        res.ParsedDocument = internalDoc;
                        res.SidecarFiles = internalDoc.Files;
                        res.SidecarHash = internalDoc.GetPrimaryHashResult(fi.Name);
                        res.Status = iStatus;
                        res.DetailMessage = $"[Internal .hash] {iDetail}";
                        return res;
                    }

                    if (externalParseFailed)
                    {
                        res.Status = SidecarVerificationStatus.HeaderMismatch;
                        res.DetailMessage = "[External .hash] Existing external .hash sidecar is malformed or unreadable.";
                        return res;
                    }

                    if (externalDoc != null &&
                        !ValidateDocumentAgainstArchiveHeader(externalDoc, fi, expectInnerRom, res.Topology, allArchiveEntries, payloadEntries, primaryEntry, out var eStatus, out var eDetail))
                    {
                        res.ParsedDocument = externalDoc;
                        res.SidecarFiles = externalDoc.Files;
                        res.SidecarHash = externalDoc.GetPrimaryHashResult(fi.Name);
                        res.Status = eStatus;
                        res.DetailMessage = $"[External .hash] {eDetail}";
                        return res;
                    }
                }
                else
                {
                    if (externalParseFailed || externalDoc == null || externalDoc.IsCompositeFolder || externalDoc.Files.Count != 1)
                    {
                        res.Status = SidecarVerificationStatus.HeaderMismatch;
                        res.DetailMessage = "Standalone file sidecar must contain exactly 1 FILE entry.";
                        return res;
                    }

                    if (!ValidateSingleLooseFileSidecar(externalDoc.Files[0], fi, out var fStatus, out var fDetail))
                    {
                        res.Status = fStatus;
                        res.DetailMessage = fDetail;
                        return res;
                    }
                }

                if (deepCryptographicVerify)
                {
                    if (isArchive && expectInnerRom && res.Topology == ReleaseTopology.CompositeFolderDump)
                    {
                        bool archiveStreamOk = await TestArchiveIntegrityAsync(sevenZipExe, fi.FullName, ct);
                        if (!archiveStreamOk)
                        {
                            res.Status = SidecarVerificationStatus.CryptographicMismatch;
                            res.DetailMessage = "7-Zip integrity test (7z t) reported corrupted compressed stream or CRC mismatch.";
                            return res;
                        }
                    }
                    else
                    {
                        List<FileHashResult> computedList = (isArchive && expectInnerRom)
                            ? await HashArchivePayloadFilesAsync(sevenZipExe, fi.FullName, scratchDir, reusableVerifiedFiles: null, ct)
                            : new List<FileHashResult> { await ComputeFileHashesAsync(fi.FullName, ct) };

                        res.ComputedFiles = computedList;
                        res.ComputedHash = computedList
                            .OrderByDescending(f => !ReleaseStructureDetector.IsDiscDescriptorFile(f.FileName))
                            .ThenByDescending(f => f.Size)
                            .FirstOrDefault();

                        if (internalDoc != null && !ValidateDocumentAgainstComputedFiles(internalDoc, computedList, out string iCryptoMsg))
                        {
                            res.ParsedDocument = internalDoc;
                            res.SidecarFiles = internalDoc.Files;
                            res.SidecarHash = internalDoc.GetPrimaryHashResult(fi.Name);
                            res.Status = SidecarVerificationStatus.CryptographicMismatch;
                            res.DetailMessage = $"[Internal .hash] {iCryptoMsg}";
                            return res;
                        }

                        if (externalDoc != null && !ValidateDocumentAgainstComputedFiles(externalDoc, computedList, out string eCryptoMsg))
                        {
                            res.ParsedDocument = externalDoc;
                            res.SidecarFiles = externalDoc.Files;
                            res.SidecarHash = externalDoc.GetPrimaryHashResult(fi.Name);
                            res.Status = SidecarVerificationStatus.CryptographicMismatch;
                            res.DetailMessage = $"[External .hash] {eCryptoMsg}";
                            return res;
                        }
                    }
                }

                res.Status = SidecarVerificationStatus.Valid;
                res.DetailMessage = deepCryptographicVerify
                    ? "Cryptographically verified byte-for-byte."
                    : (isArchive && expectInnerRom
                        ? (res.Topology == ReleaseTopology.CompositeFolderDump
                            ? $"Verified archived folder dump ({activeDoc?.TotalFiles ?? 0} files)."
                            : $"Verified {payloadEntries.Count} inner file(s) against 7-Zip header.")
                        : "Verified against file metadata.");
                return res;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                res.Status = SidecarVerificationStatus.Error;
                res.DetailMessage = ex.Message;
                return res;
            }
        }

        public static string SerializeParsedSidecarDocument(string baseTitle, ParsedSidecarDocument doc)
        {
            if (doc.IsCompositeFolder)
            {
                return GenerateSidecarContentForFolder(new FolderCompositeHashResult
                {
                    FolderName = string.IsNullOrWhiteSpace(doc.TitleOrFolder) ? baseTitle : doc.TitleOrFolder,
                    DumpType = doc.DumpType,
                    TotalFiles = doc.TotalFiles > 0 ? doc.TotalFiles : doc.Files.Count,
                    TotalSizeBytes = doc.TotalSizeBytes,
                    CompositeSha1 = doc.CompositeSha1,
                    Files = doc.Files
                });
            }

            return GenerateSidecarContentForFiles(baseTitle, doc.Files);
        }

        public static async Task<FileHashResult?> HealTargetSidecarAsync(
            string sevenZipExe,
            string targetPath,
            bool expectInnerRom,
            HashSidecarMode placementMode,
            string scratchDir = "",
            SidecarItemVerificationResult? priorVerification = null,
            AuditStructureMode structureMode = AuditStructureMode.Auto,
            CancellationToken ct = default)
        {
            sevenZipExe = ResolveSevenZipExecutable(sevenZipExe);
            string normalized = NormalizeTargetPath(targetPath);

            // ==================== UNPACKED GAME FOLDERS (ALWAYS PLACE .hash BESIDE FOLDER) ====================
            if (Directory.Exists(normalized))
            {
                var di = new DirectoryInfo(normalized);
                string parentDir = di.Parent?.FullName ?? "";
                if (string.IsNullOrEmpty(parentDir))
                    return null;

                CleanupStrayInternalFolderHashFiles(normalized);

                if (ReleaseStructureDetector.ShouldTreatAsCompositeDump(normalized, structureMode))
                {
                    var comp = priorVerification?.ComputedComposite
                               ?? await ComputeFolderCompositeHashAsync(normalized, ct);
                    await CreateFolderSidecarFileAsync(parentDir, comp, ct);
                    return new FileHashResult
                    {
                        FileName = di.Name,
                        RelativePath = di.Name,
                        Size = comp.TotalSizeBytes,
                        Sha1 = comp.CompositeSha1
                    };
                }
                else
                {
                    var folderFiles = (priorVerification?.ComputedFiles != null && priorVerification.ComputedFiles.Count > 0)
                        ? priorVerification.ComputedFiles
                        : await HashMultiFileGameFolderAsync(normalized, ct);

                    if (folderFiles.Count == 0)
                        return null;

                    await CreateOrUpdateSidecarFileAsync(parentDir, di.Name, folderFiles, ct);
                    return folderFiles
                        .OrderByDescending(f => !ReleaseStructureDetector.IsDiscDescriptorFile(f.FileName))
                        .ThenByDescending(f => f.Size)
                        .First();
                }
            }

            // ==================== ARCHIVES & STANDALONE FILES ====================
            var fi = new FileInfo(normalized);
            if (!fi.Exists)
                return null;

            bool isArchive = IsArchiveFilePath(fi.FullName);
            bool isWritableArchive = IsWritableArchiveFilePath(fi.FullName);
            string baseTitle = Path.GetFileNameWithoutExtension(fi.Name);

            string sidecarContent;
            FileHashResult primaryResult;

            if (isArchive && expectInnerRom)
            {
                var entries = await ListArchiveContentsAsync(sevenZipExe, fi.FullName, ct);
                var nonDirPaths = entries
                    .Where(e => !e.IsDirectory && !string.IsNullOrWhiteSpace(e.Path))
                    .Select(e => e.Path)
                    .ToList();
                var topology = ReleaseStructureDetector.ClassifyArchiveEntries(nonDirPaths, structureMode);

                if (topology == ReleaseTopology.CompositeFolderDump)
                {
                    var comp = BuildCompositeHashFromArchiveEntries(baseTitle, fi.FullName, entries);
                    sidecarContent = GenerateSidecarContentForFolder(comp);
                    primaryResult = new FileHashResult
                    {
                        FileName = baseTitle,
                        RelativePath = baseTitle,
                        Size = comp.TotalSizeBytes,
                        Sha1 = comp.CompositeSha1
                    };
                }
                else
                {
                    List<FileHashResult> allFiles;
                    if (priorVerification?.ComputedFiles != null && priorVerification.ComputedFiles.Count > 0)
                    {
                        allFiles = priorVerification.ComputedFiles;
                    }
                    else
                    {
                        // Reuse already header-CRC-verified entries ONLY during IncompleteMultiFileSet upgrades
                        var reusable = priorVerification?.Status == SidecarVerificationStatus.IncompleteMultiFileSet
                            ? priorVerification.SidecarFiles
                            : null;

                        allFiles = await HashArchivePayloadFilesAsync(
                            sevenZipExe,
                            fi.FullName,
                            scratchDir,
                            reusableVerifiedFiles: reusable,
                            ct: ct);
                    }

                    if (allFiles.Count == 0)
                        return null;

                    sidecarContent = GenerateSidecarContentForFiles(baseTitle, allFiles);
                    primaryResult = allFiles
                        .OrderByDescending(f => !ReleaseStructureDetector.IsDiscDescriptorFile(f.FileName))
                        .ThenByDescending(f => f.Size)
                        .First();
                }
            }
            else
            {
                FileHashResult singleHash = (priorVerification?.ComputedHash != null && !string.IsNullOrWhiteSpace(priorVerification.ComputedHash.Sha1))
                    ? priorVerification.ComputedHash
                    : await ComputeFileHashesAsync(fi.FullName, ct);

                sidecarContent = GenerateSidecarContentForFiles(baseTitle, new[] { singleHash });
                primaryResult = singleHash;
            }

            HashSidecarMode effectiveMode = placementMode;
            if (effectiveMode == HashSidecarMode.None)
            {
                effectiveMode = (priorVerification?.Location is SidecarLocation.InternalArchive or SidecarLocation.Both) && isWritableArchive
                    ? HashSidecarMode.PackageInsideArchive
                    : HashSidecarMode.SidecarBeside;
            }

            if (effectiveMode == HashSidecarMode.PackageInsideArchive && isWritableArchive)
            {
                if (priorVerification == null ||
                    priorVerification.Location == SidecarLocation.InternalArchive ||
                    priorVerification.Location == SidecarLocation.Both)
                {
                    await DeleteArchiveInternalHashFilesAsync(sevenZipExe, fi.FullName, ct);
                }

                string tempDir = Path.Combine(Path.GetTempPath(), "hk_hash_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                string tempSidecar = Path.Combine(tempDir, $"{baseTitle}.hash");
                try
                {
                    await File.WriteAllTextAsync(tempSidecar, sidecarContent, Encoding.UTF8, ct);
                    bool injected = await InjectSidecarIntoArchiveAsync(sevenZipExe, fi.FullName, tempSidecar, ct);
                    if (injected)
                    {
                        // Always remove any external .hash files (<Game>.7z.hash and <Game>.hash) when packed inside archive
                        foreach (var extSidecar in GetExistingExternalSidecarPaths(fi.FullName))
                        {
                            try { File.Delete(extSidecar); } catch (System.Exception __suppressedEx9) { System.Diagnostics.Debug.WriteLine($"[HashSidecarService] Non-fatal error ignored: {__suppressedEx9.Message}"); }
                        }
                    }
                }
                finally
                {
                    try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch (System.Exception __suppressedEx10) { System.Diagnostics.Debug.WriteLine($"[HashSidecarService] Non-fatal error ignored: {__suppressedEx10.Message}"); }
                }
            }
            else
            {
                if (isWritableArchive &&
                    priorVerification != null &&
                    priorVerification.Status != SidecarVerificationStatus.Valid &&
                    (priorVerification.Location == SidecarLocation.InternalArchive || priorVerification.Location == SidecarLocation.Both))
                {
                    await DeleteArchiveInternalHashFilesAsync(sevenZipExe, fi.FullName, ct);
                }

                string standardPath = GetStandardSidecarPath(fi.FullName);
                await File.WriteAllTextAsync(standardPath, sidecarContent, Encoding.UTF8, ct);
            }

            return primaryResult;
        }

        /// <summary>
        /// Discovers all logical game units (composite folder dumps, cohesive multi-file ROM subfolders,
        /// archives, and standalone loose ROM files) inside a root directory, recursing into organizational folders.
        /// </summary>
        public static void DiscoverDirectoryGameTargets(
            string rootDir,
            AuditStructureMode structureMode,
            List<DirectoryInfo> compositeDirs,
            List<DirectoryInfo> multiFileGameDirs,
            List<FileInfo> archives,
            List<FileInfo> standaloneFiles)
        {
            if (string.IsNullOrWhiteSpace(rootDir) || !Directory.Exists(rootDir))
                return;

            var topOpts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false };
            var dirQueue = new Queue<DirectoryInfo>();
            dirQueue.Enqueue(new DirectoryInfo(rootDir));

            while (dirQueue.Count > 0)
            {
                var currentDir = dirQueue.Dequeue();

                // Enumerate direct files in this directory
                try
                {
                    foreach (var f in currentDir.EnumerateFiles("*", topOpts))
                    {
                        if (f.Length <= 0 || f.Name.Contains(".partial.", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (IsArchiveFilePath(f.FullName))
                        {
                            archives.Add(f);
                            continue;
                        }

                        if (ReleaseStructureDetector.IsIgnoredNonPayloadFile(f.Name))
                            continue;

                        standaloneFiles.Add(f);
                    }
                }
                catch (System.Exception __suppressedEx11) { System.Diagnostics.Debug.WriteLine($"[HashSidecarService] Non-fatal error ignored: {__suppressedEx11.Message}"); }

                // Inspect direct subdirectories: classify as CompositeDump, CohesiveMultiFileGameFolder, or Organizational folder to recurse into
                try
                {
                    foreach (var subDir in currentDir.EnumerateDirectories("*", topOpts))
                    {
                        if (structureMode != AuditStructureMode.FilesOnly &&
                            !ReleaseStructureDetector.IsOrganizationalFolderName(subDir.Name))
                        {
                            if (ReleaseStructureDetector.ShouldTreatAsCompositeDump(subDir.FullName, structureMode))
                            {
                                compositeDirs.Add(subDir);
                                continue;
                            }

                            if (ReleaseStructureDetector.IsCohesiveMultiFileGameFolder(subDir.FullName, structureMode))
                            {
                                multiFileGameDirs.Add(subDir);
                                continue;
                            }
                        }

                        dirQueue.Enqueue(subDir);
                    }
                }
                catch (System.Exception __suppressedEx12) { System.Diagnostics.Debug.WriteLine($"[HashSidecarService] Non-fatal error ignored: {__suppressedEx12.Message}"); }
            }
        }

        public static async Task<SidecarVerificationReport> VerifyAndHealDirectorySidecarsAsync(
            IEnumerable<string> rootDirectories,
            string sevenZipExe,
            bool expectInnerRom,
            HashSidecarMode targetPlacement,
            AuditStructureMode structureMode,
            SidecarVerifyMode verifyMode,
            string scratchDir = "",
            Action<string, string>? log = null,
            CancellationToken ct = default,
            Action<int, int, int>? onStatsDelta = null)
        {
            var report = new SidecarVerificationReport();
            sevenZipExe = ResolveSevenZipExecutable(sevenZipExe);
            bool deepVerify = verifyMode == SidecarVerifyMode.DeepCryptographicAndHeal;
            bool healEnabled = verifyMode != SidecarVerifyMode.DryRunReportOnly;

            var uniqueDirs = rootDirectories
                .Where(d => !string.IsNullOrWhiteSpace(d) && Directory.Exists(d))
                .Select(NormalizeTargetPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var targets = new List<string>();
            foreach (var dir in uniqueDirs)
            {
                ct.ThrowIfCancellationRequested();
                var compDirs = new List<DirectoryInfo>();
                var multiGameDirs = new List<DirectoryInfo>();
                var archs = new List<FileInfo>();
                var loose = new List<FileInfo>();

                DiscoverDirectoryGameTargets(dir, structureMode, compDirs, multiGameDirs, archs, loose);

                targets.AddRange(compDirs.Select(d => d.FullName));
                targets.AddRange(multiGameDirs.Select(d => d.FullName));
                targets.AddRange(archs.Select(a => a.FullName));
                targets.AddRange(loose.Select(f => f.FullName));
            }

            targets = targets.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            log?.Invoke("INFO", $"[.hash Verifier] Scanning {targets.Count} game target(s) across {uniqueDirs.Count} folder(s) • Mode: {verifyMode}...");

            int idx = 0;
            var lastLogUtc = DateTime.UtcNow;

            foreach (var target in targets)
            {
                ct.ThrowIfCancellationRequested();
                idx++;

                var itemRes = await VerifySidecarForTargetAsync(
                    sevenZipExe,
                    target,
                    expectInnerRom,
                    deepVerify,
                    scratchDir,
                    structureMode,
                    ct);

                report.TotalInspected++;
                string shortName = Path.GetFileName(target);
                bool isDirTarget = Directory.Exists(target);

                switch (itemRes.Status)
                {
                    case SidecarVerificationStatus.Valid:
                        report.ValidCount++;
                        if (healEnabled && isDirTarget)
                        {
                            CleanupStrayInternalFolderHashFiles(target);
                        }
                        else if (healEnabled &&
                                 targetPlacement == HashSidecarMode.PackageInsideArchive &&
                                 IsWritableArchiveFilePath(target) &&
                                 itemRes.ParsedDocument != null &&
                                 itemRes.Location != SidecarLocation.InternalArchive)
                        {
                            if (itemRes.Location == SidecarLocation.ExternalBeside)
                            {
                                string baseTitle = Path.GetFileNameWithoutExtension(target);
                                string content = SerializeParsedSidecarDocument(baseTitle, itemRes.ParsedDocument);
                                string tempDir = Path.Combine(Path.GetTempPath(), "hk_hash_" + Guid.NewGuid().ToString("N"));
                                Directory.CreateDirectory(tempDir);
                                string tempSidecar = Path.Combine(tempDir, $"{baseTitle}.hash");
                                try
                                {
                                    await File.WriteAllTextAsync(tempSidecar, content, Encoding.UTF8, ct);
                                    if (await InjectSidecarIntoArchiveAsync(sevenZipExe, target, tempSidecar, ct))
                                    {
                                        foreach (var extPath in GetExistingExternalSidecarPaths(target))
                                        {
                                            try { File.Delete(extPath); } catch (System.Exception __suppressedEx13) { System.Diagnostics.Debug.WriteLine($"[HashSidecarService] Non-fatal error ignored: {__suppressedEx13.Message}"); }
                                        }
                                        itemRes.WasHealed = true;
                                        report.HealedCount++;
                                        log?.Invoke("SUCCESS", $"[Migrated -> Inside Archive] '{shortName}' (valid .hash packaged & external sidecar removed)");
                                    }
                                }
                                finally
                                {
                                    try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch (System.Exception __suppressedEx14) { System.Diagnostics.Debug.WriteLine($"[HashSidecarService] Non-fatal error ignored: {__suppressedEx14.Message}"); }
                                }
                            }
                            else if (itemRes.Location == SidecarLocation.Both)
                            {
                                foreach (var extPath in GetExistingExternalSidecarPaths(target))
                                {
                                    try { File.Delete(extPath); } catch (System.Exception __suppressedEx15) { System.Diagnostics.Debug.WriteLine($"[HashSidecarService] Non-fatal error ignored: {__suppressedEx15.Message}"); }
                                }
                                itemRes.WasHealed = true;
                                report.HealedCount++;
                            }
                        }
                        break;

                    case SidecarVerificationStatus.LegacyOuterArchiveHash:
                        report.LegacyOuterArchiveCount++;
                        log?.Invoke("WARN", $"[Legacy Outer Hash] '{shortName}' -> {itemRes.DetailMessage}");
                        break;

                    case SidecarVerificationStatus.IncompleteMultiFileSet:
                        report.IncompleteMultiFileCount++;
                        log?.Invoke("WARN", $"[Incomplete Multi-File .hash] '{shortName}' -> {itemRes.DetailMessage}");
                        break;

                    case SidecarVerificationStatus.HeaderMismatch:
                        report.HeaderMismatchCount++;
                        log?.Invoke("WARN", $"[Header Mismatch] '{shortName}' -> {itemRes.DetailMessage}");
                        break;

                    case SidecarVerificationStatus.CryptographicMismatch:
                        report.CryptographicMismatchCount++;
                        log?.Invoke("ERROR", $"[Checksum Mismatch] '{shortName}' -> {itemRes.DetailMessage}");
                        break;

                    case SidecarVerificationStatus.Missing:
                        report.MissingCount++;
                        log?.Invoke("INFO", $"[Missing .hash] '{shortName}'");
                        break;

                    case SidecarVerificationStatus.Error:
                        report.ErrorCount++;
                        log?.Invoke("ERROR", $"[Error] '{shortName}' -> {itemRes.DetailMessage}");
                        break;
                }

                bool shouldHeal = healEnabled &&
                    (itemRes.Status == SidecarVerificationStatus.LegacyOuterArchiveHash ||
                     itemRes.Status == SidecarVerificationStatus.IncompleteMultiFileSet ||
                     itemRes.Status == SidecarVerificationStatus.HeaderMismatch ||
                     itemRes.Status == SidecarVerificationStatus.CryptographicMismatch ||
                     (itemRes.Status == SidecarVerificationStatus.Missing && targetPlacement != HashSidecarMode.None));

                bool healFailed = false;
                if (shouldHeal)
                {
                    try
                    {
                        var healed = await HealTargetSidecarAsync(
                            sevenZipExe,
                            target,
                            expectInnerRom,
                            targetPlacement,
                            scratchDir,
                            itemRes,
                            structureMode,
                            ct);

                        if (healed != null)
                        {
                            itemRes.ComputedHash = healed;
                            itemRes.WasHealed = true;
                            report.HealedCount++;
                            log?.Invoke("SUCCESS", $"[Healed .hash] '{shortName}' -> Primary: {healed.FileName} | SIZE: {healed.Size} | SHA1: {healed.Sha1}");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        healFailed = true;
                        report.ErrorCount++;
                        log?.Invoke("ERROR", $"[Heal Failed] '{shortName}': {ex.Message}");
                    }
                }

                if (onStatsDelta != null)
                {
                    if (healFailed || (!itemRes.WasHealed && itemRes.Status is SidecarVerificationStatus.Error or SidecarVerificationStatus.CryptographicMismatch))
                        onStatsDelta(0, 0, 1);
                    else if (itemRes.WasHealed)
                        onStatsDelta(1, 0, 0);
                    else if (itemRes.Status == SidecarVerificationStatus.Valid)
                        onStatsDelta(0, 1, 0);
                    else
                        onStatsDelta(1, 0, 0);
                }

                report.Items.Add(itemRes);

                if (idx % 50 == 0 || (DateTime.UtcNow - lastLogUtc).TotalSeconds >= 4.0)
                {
                    log?.Invoke("INFO", $"[.hash Verifier] Progress [{idx}/{targets.Count}] • Valid: {report.ValidCount} • Legacy Outer: {report.LegacyOuterArchiveCount} • Incomplete Multi-File: {report.IncompleteMultiFileCount} • Mismatched: {report.HeaderMismatchCount + report.CryptographicMismatchCount} • Missing: {report.MissingCount} • Healed: {report.HealedCount}");
                    lastLogUtc = DateTime.UtcNow;
                }
            }

            log?.Invoke("SUCCESS", $"[.hash Verifier Complete] Inspected: {report.TotalInspected} | Valid: {report.ValidCount} | Legacy Outer-Archive: {report.LegacyOuterArchiveCount} | Incomplete Multi-File: {report.IncompleteMultiFileCount} | Header Mismatches: {report.HeaderMismatchCount} | Crypto Mismatches: {report.CryptographicMismatchCount} | Missing: {report.MissingCount} | Healed: {report.HealedCount} | Errors: {report.ErrorCount}");
            return report;
        }

        /// <summary>
        /// Hashes all ROM / disc / track / descriptor payload entries inside an archive (.7z, .zip, .rar).
        /// - 1-file archives stream directly via 7z stdout pipe in RAM (zero disk writes).
        /// - Multi-file .zip archives stream every entry directly in RAM via ZipArchive (zero disk writes).
        /// - Multi-file .7z/.rar archives decompress once per archive to scratch (or delta-extract only missing entries
        ///   when reusableVerifiedFiles are supplied), avoiding O(N^2) solid block re-decompression.
        /// </summary>
        public static async Task<List<FileHashResult>> HashArchivePayloadFilesAsync(
            string sevenZipExe,
            string archivePath,
            string scratchDirectory,
            IReadOnlyList<FileHashResult>? reusableVerifiedFiles = null,
            CancellationToken ct = default)
        {
            sevenZipExe = ResolveSevenZipExecutable(sevenZipExe);
            var allEntries = await ListArchiveContentsAsync(sevenZipExe, archivePath, ct);
            var payloadEntries = GetNormalizedPayloadArchiveEntries(allEntries);

            if (payloadEntries.Count == 0)
            {
                // Fallback for single-stream raw extraction if listing returned empty
                var singleFallback = await StreamSingleArchiveEntryAsync(sevenZipExe, archivePath, "", Path.GetFileNameWithoutExtension(archivePath), ct);
                if (singleFallback != null)
                    return new List<FileHashResult> { singleFallback };
                throw new FileNotFoundException("No ROM payload files were found inside archive.", archivePath);
            }

            // Fast check for reusable header-CRC-verified entries during IncompleteMultiFileSet upgrades
            var verifiedByRelPath = new Dictionary<string, FileHashResult>(StringComparer.OrdinalIgnoreCase);
            var missingEntries = new List<(ArchiveEntryInfo Entry, string NormalizedRelativePath)>();

            bool allowBaseReuse = payloadEntries
                .Select(p => Path.GetFileName(p.NormalizedRelativePath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == payloadEntries.Count;

            foreach (var (entry, normRel) in payloadEntries)
            {
                FileHashResult? reused = null;
                if (reusableVerifiedFiles != null && reusableVerifiedFiles.Count > 0)
                {
                    var candidate = FindMatchingSidecarEntry(reusableVerifiedFiles, normRel, allowBaseReuse);
                    string expectedCrc = string.IsNullOrWhiteSpace(entry.Crc) && entry.Size == 0 ? "00000000" : entry.Crc;
                    if (candidate != null &&
                        candidate.Size == entry.Size &&
                        !string.IsNullOrWhiteSpace(expectedCrc) &&
                        !string.IsNullOrWhiteSpace(candidate.Crc32) &&
                        candidate.Crc32.Equals(expectedCrc, StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(candidate.Sha1) &&
                        !string.IsNullOrWhiteSpace(candidate.Md5))
                    {
                        reused = new FileHashResult
                        {
                            FileName = normRel,
                            RelativePath = normRel,
                            Size = candidate.Size,
                            Crc32 = candidate.Crc32.ToUpperInvariant(),
                            Md5 = candidate.Md5.ToUpperInvariant(),
                            Sha1 = candidate.Sha1.ToUpperInvariant()
                        };
                    }
                }

                if (reused != null)
                    verifiedByRelPath[normRel] = reused;
                else
                    missingEntries.Add((entry, normRel));
            }

            if (missingEntries.Count == 0)
            {
                return payloadEntries
                    .Select(p => verifiedByRelPath[p.NormalizedRelativePath])
                    .OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            // Path A: .zip archives support random-access in-memory entry streaming with zero disk writes for 1..N files
            if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var zip = ZipFile.OpenRead(archivePath);
                    var zipLookup = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                    foreach (var ze in zip.Entries)
                    {
                        string key = ze.FullName.Replace('\\', '/').Trim('/');
                        if (!string.IsNullOrEmpty(key))
                            zipLookup[key] = ze;
                    }

                    foreach (var (entry, normRel) in missingEntries)
                    {
                        ct.ThrowIfCancellationRequested();
                        string rawKey = entry.Path.Replace('\\', '/').Trim('/');
                        if (!zipLookup.TryGetValue(rawKey, out var ze))
                            throw new FileNotFoundException($"ZIP entry not found: {rawKey}");

                        using var entryStream = ze.Open();
                        var h = await ComputeStreamHashesAsync(entryStream, normRel, normRel, ct);
                        verifiedByRelPath[normRel] = h;
                    }

                    return payloadEntries
                        .Select(p => verifiedByRelPath[p.NormalizedRelativePath])
                        .OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Fall through to 7z extraction if ZIP uses unsupported compression method (e.g. LZMA/Deflate64 in .zip)
                }
            }

            // Path B: Single missing entry -> stream directly in RAM via 7z e -so (zero scratch disk writes)
            if (missingEntries.Count == 1 && payloadEntries.Count == 1)
            {
                var (singleEntry, singleNormRel) = missingEntries[0];
                var streamed = await StreamSingleArchiveEntryAsync(
                    sevenZipExe,
                    archivePath,
                    singleEntry.Path,
                    singleNormRel,
                    ct,
                    allowZeroByte: singleEntry.Size == 0);

                if (streamed != null)
                {
                    return new List<FileHashResult> { streamed };
                }
            }

            // Path C: Multi-file .7z/.rar archive (or fallback) -> single-pass 7z x to scratch directory
            string effectiveScratch = string.IsNullOrWhiteSpace(scratchDirectory)
                ? Path.Combine(Path.GetTempPath(), "HousekeepingRomScratch")
                : scratchDirectory;

            if (!Directory.Exists(effectiveScratch))
                Directory.CreateDirectory(effectiveScratch);

            string extractFolder = Path.Combine(effectiveScratch, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(extractFolder);
            string? listFilePath = null;

            try
            {
                string extractArgs;
                if (missingEntries.Count < payloadEntries.Count)
                {
                    listFilePath = Path.Combine(effectiveScratch, "hk_list_" + Guid.NewGuid().ToString("N") + ".txt");
                    await File.WriteAllLinesAsync(listFilePath, missingEntries.Select(m => m.Entry.Path), new UTF8Encoding(false), ct);
                    extractArgs = $"x \"{archivePath}\" -o\"{extractFolder}\" -scsUTF-8 \"@{listFilePath}\" -y";
                }
                else
                {
                    extractArgs = $"x \"{archivePath}\" -o\"{extractFolder}\" -xr!*.hash -y";
                }

                var extractPsi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = sevenZipExe,
                    Arguments = extractArgs,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };

                using var proc = System.Diagnostics.Process.Start(extractPsi);
                if (proc == null)
                    throw new InvalidOperationException("Failed to spawn 7-Zip extraction process.");

                try
                {
                    await proc.WaitForExitAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch (System.Exception __suppressedEx16) { System.Diagnostics.Debug.WriteLine($"[HashSidecarService] Non-fatal error ignored: {__suppressedEx16.Message}"); }
                    throw;
                }

                if (proc.ExitCode != 0)
                    throw new InvalidOperationException($"7-Zip exited with code {proc.ExitCode} while extracting '{Path.GetFileName(archivePath)}'.");

                foreach (var (entry, normRel) in missingEntries)
                {
                    ct.ThrowIfCancellationRequested();
                    string diskRel = entry.Path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                    string extractedFilePath = Path.Combine(extractFolder, diskRel);

                    if (!File.Exists(extractedFilePath))
                    {
                        // Fallback filename search inside extractFolder
                        string targetName = Path.GetFileName(diskRel);
                        var fallback = Directory.EnumerateFiles(extractFolder, "*", SearchOption.AllDirectories)
                            .FirstOrDefault(p => Path.GetFileName(p).Equals(targetName, StringComparison.OrdinalIgnoreCase));
                        if (fallback != null)
                            extractedFilePath = fallback;
                    }

                    if (!File.Exists(extractedFilePath))
                        throw new FileNotFoundException($"Expected inner archive file was not extracted: '{entry.Path}'", extractedFilePath);

                    var h = await ComputeFileHashesAsync(extractedFilePath, ct);
                    h.FileName = normRel;
                    h.RelativePath = normRel;
                    verifiedByRelPath[normRel] = h;
                }

                return payloadEntries
                    .Select(p => verifiedByRelPath[p.NormalizedRelativePath])
                    .OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            finally
            {
                if (listFilePath != null)
                {
                    try { if (File.Exists(listFilePath)) File.Delete(listFilePath); } catch (System.Exception __suppressedEx17) { System.Diagnostics.Debug.WriteLine($"[HashSidecarService] Non-fatal error ignored: {__suppressedEx17.Message}"); }
                }
                try
                {
                    if (Directory.Exists(extractFolder))
                        Directory.Delete(extractFolder, recursive: true);
                }
                catch (System.Exception __suppressedEx18) { System.Diagnostics.Debug.WriteLine($"[HashSidecarService] Non-fatal error ignored: {__suppressedEx18.Message}"); }
            }
        }

        private static async Task<FileHashResult?> StreamSingleArchiveEntryAsync(
            string sevenZipExe,
            string archivePath,
            string innerEntryRawPath,
            string normalizedDisplayName,
            CancellationToken ct,
            bool allowZeroByte = false)
        {
            try
            {
                string streamArgs = string.IsNullOrWhiteSpace(innerEntryRawPath)
                    ? $"e \"{archivePath}\" -so"
                    : $"e \"{archivePath}\" \"{innerEntryRawPath}\" -so";

                var streamPsi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = sevenZipExe,
                    Arguments = streamArgs,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = false
                };

                using var p = System.Diagnostics.Process.Start(streamPsi);
                if (p == null)
                    return null;

                try
                {
                    using var rawStream = p.StandardOutput.BaseStream;
                    var hash = await ComputeStreamHashesAsync(rawStream, normalizedDisplayName, normalizedDisplayName, ct);
                    await p.WaitForExitAsync(ct);

                    if (p.ExitCode == 0 && (hash.Size > 0 || allowZeroByte))
                        return hash;
                }
                catch (OperationCanceledException)
                {
                    try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch (System.Exception __suppressedEx19) { System.Diagnostics.Debug.WriteLine($"[HashSidecarService] Non-fatal error ignored: {__suppressedEx19.Message}"); }
                    throw;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (System.Exception __suppressedEx20) { System.Diagnostics.Debug.WriteLine($"[HashSidecarService] Non-fatal error ignored: {__suppressedEx20.Message}"); }

            return null;
        }

        public static async Task<FileHashResult> HashArchiveInnerRomAsync(
            string sevenZipExe,
            string archivePath,
            string scratchDirectory,
            CancellationToken ct = default)
        {
            var allFiles = await HashArchivePayloadFilesAsync(sevenZipExe, archivePath, scratchDirectory, reusableVerifiedFiles: null, ct);
            return allFiles
                .OrderByDescending(f => !ReleaseStructureDetector.IsDiscDescriptorFile(f.FileName))
                .ThenByDescending(f => f.Size)
                .First();
        }

        public static async Task<bool> InjectSidecarIntoArchiveAsync(
            string sevenZipExe,
            string archivePath,
            string sidecarFilePath,
            CancellationToken ct = default)
        {
            sevenZipExe = ResolveSevenZipExecutable(sevenZipExe);
            if (!File.Exists(sevenZipExe) || !File.Exists(archivePath) || !File.Exists(sidecarFilePath))
                return false;

            try
            {
                string sidecarDir = Path.GetDirectoryName(sidecarFilePath) ?? "";
                string sidecarFileName = Path.GetFileName(sidecarFilePath);

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = sevenZipExe,
                    Arguments = $"a \"{archivePath}\" \"{sidecarFileName}\"",
                    WorkingDirectory = string.IsNullOrEmpty(sidecarDir) ? null : sidecarDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };

                using var p = System.Diagnostics.Process.Start(psi);
                if (p == null) return false;
                await p.WaitForExitAsync(ct);
                return p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<string> CreateOrUpdateSidecarFileAsync(
            string outputDirectory,
            string baseTitle,
            IEnumerable<FileHashResult> files,
            CancellationToken ct = default)
        {
            string sidecarPath = Path.Combine(outputDirectory, $"{baseTitle}.hash");
            string content = GenerateSidecarContentForFiles(baseTitle, files);
            await File.WriteAllTextAsync(sidecarPath, content, Encoding.UTF8, ct);
            return sidecarPath;
        }

        public static async Task<string> CreateFolderSidecarFileAsync(
            string parentDirectory,
            FolderCompositeHashResult folderResult,
            CancellationToken ct = default)
        {
            string sidecarPath = Path.Combine(parentDirectory, $"{folderResult.FolderName}.hash");
            string content = GenerateSidecarContentForFolder(folderResult);
            await File.WriteAllTextAsync(sidecarPath, content, Encoding.UTF8, ct);
            return sidecarPath;
        }

        // Standard CRC32 table
        private static readonly uint[] Crc32Table = Enumerable.Range(0, 256).Select(i =>
        {
            uint entry = (uint)i;
            for (int j = 0; j < 8; j++)
            {
                if ((entry & 1) == 1)
                    entry = (entry >> 1) ^ 0xEDB88320;
                else
                    entry >>= 1;
            }
            return entry;
        }).ToArray();
    }
}
