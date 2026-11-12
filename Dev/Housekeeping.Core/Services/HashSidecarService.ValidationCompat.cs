// @module: Housekeeping.Core/Services/HashSidecarService.ValidationCompat
// @purpose: Sidecar header/archive validation, # Container: serialization retention, .cue/.bin wrapper stripping, and ShadowLibrary composite compat.
// @depends-on: Housekeeping.Models, System.Collections.Generic, System.IO, System.Linq, System.Security.Cryptography, System.Text
// @exports: HashSidecarService (partial: SerializeParsedSidecarDocument, ValidateDocumentAgainstArchiveHeader, ComputeShadowLibraryCompatCompositeSha1)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Housekeeping.Models;

namespace Housekeeping.Services
{
    public static partial class HashSidecarService
    {
        public static FileHashResult? ReconstructContainerHashFromDocument(ParsedSidecarDocument? doc, string fallbackArchiveName = "")
        {
            if (doc == null || !doc.HasContainerHash)
                return null;

            return new FileHashResult
            {
                FileName = string.IsNullOrWhiteSpace(doc.ContainerFileName) ? fallbackArchiveName : doc.ContainerFileName,
                RelativePath = string.IsNullOrWhiteSpace(doc.ContainerFileName) ? fallbackArchiveName : doc.ContainerFileName,
                Size = doc.ContainerSize,
                Crc32 = doc.ContainerCrc32,
                Md5 = doc.ContainerMd5,
                Sha1 = doc.ContainerSha1
            };
        }

        public static string SerializeParsedSidecarDocument(string baseTitle, ParsedSidecarDocument doc, FileHashResult? overrideContainerHash = null)
        {
            if (doc.IsCompositeFolder)
            {
                return GenerateSidecarContentForFolder(new FolderCompositeHashResult
                {
                    FolderName = string.IsNullOrWhiteSpace(doc.TitleOrFolder) ? baseTitle : doc.TitleOrFolder,
                    DumpType = doc.DumpType,
                    TotalFiles = doc.TotalFiles > 0 ? doc.TotalFiles : doc.Files.Count,
                    TotalSizeBytes = doc.TotalSizeBytes > 0 ? doc.TotalSizeBytes : doc.Files.Sum(f => f.Size),
                    CompositeSha1 = doc.CompositeSha1,
                    Files = doc.Files
                });
            }

            string effectiveTitle = string.IsNullOrWhiteSpace(doc.TitleOrFolder) ? baseTitle : doc.TitleOrFolder;
            var container = overrideContainerHash ?? ReconstructContainerHashFromDocument(doc, doc.ContainerFileName);
            return GenerateSidecarContentForFiles(effectiveTitle, doc.Files, container);
        }

        public static string ComputeShadowLibraryCompatCompositeSha1(IEnumerable<FileHashResult> files)
        {
            var sorted = files
                .OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            using var sha1 = SHA1.Create();
            foreach (var f in sorted)
            {
                string line = $"{(f.RelativePath ?? f.FileName).Replace('\\', '/').ToLowerInvariant()}|{f.Size}|{(f.Crc32 ?? "").ToUpperInvariant()}\r\n";
                byte[] bytes = Encoding.UTF8.GetBytes(line);
                sha1.TransformBlock(bytes, 0, bytes.Length, null, 0);
            }
            sha1.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return Convert.ToHexString(sha1.Hash ?? Array.Empty<byte>());
        }

        public static bool CompositeSha1MatchesEitherConvention(string sidecarCompositeSha1, FolderCompositeHashResult expected)
        {
            if (string.IsNullOrWhiteSpace(sidecarCompositeSha1))
                return false;

            if (sidecarCompositeSha1.Equals(expected.CompositeSha1, StringComparison.OrdinalIgnoreCase))
                return true;

            string shadowCompat = ComputeShadowLibraryCompatCompositeSha1(expected.Files);
            return sidecarCompositeSha1.Equals(shadowCompat, StringComparison.OrdinalIgnoreCase);
        }

        private static FileHashResult? FindMatchingSidecarEntry(
            IReadOnlyList<FileHashResult> sidecarFiles,
            string expectedNormalizedRelPath,
            bool allowUniqueBasenameMatch)
        {
            string normalizedTarget = expectedNormalizedRelPath.Replace('\\', '/');
            var rawSidecarPaths = sidecarFiles
                .Select(f => (!string.IsNullOrWhiteSpace(f.RelativePath) ? f.RelativePath : f.FileName).Replace('\\', '/'))
                .ToList();
            var strippedSidecarPaths = ReleaseStructureDetector.NormalizeRelativePathsStrippingSingleWrapper(rawSidecarPaths);

            for (int i = 0; i < sidecarFiles.Count; i++)
            {
                if (rawSidecarPaths[i].Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase) ||
                    strippedSidecarPaths[i].Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase))
                {
                    return sidecarFiles[i];
                }
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

            if (!doc.IsCompositeFolder && doc.Files.Count == 1 && IsLegacyOuterArchiveSidecar(doc.Files[0], archiveFile, primaryEntry))
            {
                string expectedDesc = topology == ReleaseTopology.CompositeFolderDump
                    ? "inner folder dump"
                    : (primaryEntry != null ? Path.GetFileName(primaryEntry.Path) : "inner ROM");
                status = SidecarVerificationStatus.LegacyOuterArchiveHash;
                detail = $"Legacy outer-archive hash detected (FILE: '{doc.Files[0].FileName}', SIZE: {doc.Files[0].Size} B) instead of '{expectedDesc}'.";
                return false;
            }

            if (topology == ReleaseTopology.CompositeFolderDump)
            {
                string baseTitle = Path.GetFileNameWithoutExtension(archiveFile.Name);
                var expectedComp = BuildCompositeHashFromArchiveEntries(baseTitle, archiveFile.FullName, allEntries);
                int effectiveTotalFiles = doc.TotalFiles > 0 ? doc.TotalFiles : doc.Files.Count;
                long effectiveTotalSize = doc.TotalSizeBytes > 0 ? doc.TotalSizeBytes : doc.Files.Sum(f => f.Size);

                if (!doc.IsCompositeFolder || effectiveTotalFiles != expectedComp.TotalFiles || doc.Files.Count != expectedComp.TotalFiles || !doc.AllEntriesHaveCrc32)
                {
                    status = SidecarVerificationStatus.IncompleteMultiFileSet;
                    detail = $"Sidecar records {Math.Max(effectiveTotalFiles, doc.Files.Count)} of {expectedComp.TotalFiles} files in archived folder dump.";
                    return false;
                }

                if (effectiveTotalSize != expectedComp.TotalSizeBytes || !CompositeSha1MatchesEitherConvention(doc.CompositeSha1, expectedComp))
                {
                    status = SidecarVerificationStatus.HeaderMismatch;
                    detail = $"Composite sidecar metadata/SHA1 mismatch against 7-Zip header.";
                    return false;
                }

                status = SidecarVerificationStatus.Valid;
                detail = $"Verified all {expectedComp.TotalFiles} files against 7-Zip header CRC32 manifest.";
                return true;
            }

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

            // Accept ShadowLibrary 1-primary-entry sidecar carrying outer # Container: header during header checks
            if (payloadEntries.Count > 1 && doc.Files.Count == 1 && doc.HasContainerHash && primaryEntry != null)
            {
                var single = doc.Files[0];
                string expPrimaryCrc = string.IsNullOrWhiteSpace(primaryEntry.Crc) && primaryEntry.Size == 0 ? "00000000" : primaryEntry.Crc;
                if (single.Size == primaryEntry.Size &&
                    !string.IsNullOrWhiteSpace(single.Sha1) &&
                    !string.IsNullOrWhiteSpace(single.Md5) &&
                    (string.IsNullOrWhiteSpace(expPrimaryCrc) || single.Crc32.Equals(expPrimaryCrc, StringComparison.OrdinalIgnoreCase)))
                {
                    status = SidecarVerificationStatus.Valid;
                    detail = "Verified 1-primary-entry sister-app sidecar with outer # Container: header.";
                    return true;
                }
            }

            if (payloadEntries.Count > 1 && doc.Files.Count < payloadEntries.Count)
            {
                status = SidecarVerificationStatus.IncompleteMultiFileSet;
                detail = $"Sidecar only records {doc.Files.Count} of {payloadEntries.Count} payload files in archive.";
                return false;
            }

            if (doc.Files.Count != payloadEntries.Count)
            {
                status = SidecarVerificationStatus.HeaderMismatch;
                detail = $"Sidecar file count ({doc.Files.Count}) does not match archive payload count ({payloadEntries.Count}).";
                return false;
            }

            bool allowBasenameMatch = payloadEntries.Select(p => Path.GetFileName(p.NormalizedRelativePath)).Distinct(StringComparer.OrdinalIgnoreCase).Count() == payloadEntries.Count;
            foreach (var (entry, normRelPath) in payloadEntries)
            {
                var sc = FindMatchingSidecarEntry(doc.Files, normRelPath, allowBasenameMatch);
                if (sc == null)
                {
                    status = payloadEntries.Count > 1 ? SidecarVerificationStatus.IncompleteMultiFileSet : SidecarVerificationStatus.HeaderMismatch;
                    detail = $"Sidecar is missing inner ROM file '{normRelPath}'.";
                    return false;
                }

                string expectedCrc = string.IsNullOrWhiteSpace(entry.Crc) && entry.Size == 0 ? "00000000" : entry.Crc;
                if (sc.Size != entry.Size ||
                    (!string.IsNullOrWhiteSpace(expectedCrc) && !sc.Crc32.Equals(expectedCrc, StringComparison.OrdinalIgnoreCase)) ||
                    string.IsNullOrWhiteSpace(sc.Sha1))
                {
                    status = SidecarVerificationStatus.HeaderMismatch;
                    detail = $"Sidecar entry '{normRelPath}' header size/CRC32/SHA1 mismatch.";
                    return false;
                }
            }

            status = SidecarVerificationStatus.Valid;
            detail = $"Verified {payloadEntries.Count} inner file(s) against 7-Zip header.";
            return true;
        }
    }
}
