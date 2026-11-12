// @module: Housekeeping.Core/Services/RomManifestCsvService.CoexistenceCompat
// @purpose: 9-column/8-column collection_manifest.csv synonym normalization, DAT MD5/SHA1 backfill, and non-degrading .hash sidecar guards.
// @depends-on: Housekeeping.Models, System.Collections.Generic, System.Diagnostics, System.IO, System.Linq, System.Text
// @exports: RomManifestCsvService (partial: LoadExistingManifest, NormalizeManifestMatchStatus, TryBackfillHashesFromDat, ShouldSkipSidecarOverwrite)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Housekeeping.Services
{
    public static partial class RomManifestCsvService
    {
        public static string NormalizeManifestMatchStatus(string? rawStatus)
        {
            if (string.IsNullOrWhiteSpace(rawStatus))
                return "NA";

            string trimmed = rawStatus.Trim();
            return trimmed.ToUpperInvariant() switch
            {
                "MATCH" or "VERIFIED" or "DAT MATCH (REDUMP/NO-INTRO)" => "MATCH",
                "MATCH (CRC32)" or "MATCH (FAST HEADER CRC32)" or "VERIFIED_CRC32" => "MATCH (CRC32)",
                "MATCH (VIA HASH)" => "MATCH (via Hash)",
                "MATCH (CRC32 VIA HASH)" => "MATCH (CRC32 via Hash)",
                "MATCH (MODIFIED/PR/RH)" or "MODIFIED / HACK" or "MODIFIED" => "MATCH (Modified/PR/RH)",
                "MISMATCH" or "MISMATCH (BAD DUMP)" or "CORRUPT" => "MISMATCH",
                "MISSING" => "MISSING",
                "NA" or "N/A" or "UNVERIFIED" or "NOMATCH" or "UNMATCHED (CUSTOM / HACK / NO DAT)" => "NA",
                _ => trimmed
            };
        }

        public static void TryBackfillHashesFromDat(IList<FileHashResult> payloadFiles, DatIndex? datIndex, string fallbackGameTitle)
        {
            if (payloadFiles == null || payloadFiles.Count == 0 || datIndex == null)
                return;

            foreach (var f in payloadFiles)
            {
                if (!string.IsNullOrWhiteSpace(f.Sha1) && !string.IsNullOrWhiteSpace(f.Md5))
                    continue;

                DatRomEntry? match = datIndex.FindMatchByHash(f.Sha1, f.Crc32, f.Size);
                if (match == null)
                {
                    var cand = datIndex.FindMatchByName(f.FileName) ?? datIndex.FindMatchByName(fallbackGameTitle);
                    if (cand != null && DatIndex.HashesMatch(cand, f.Sha1, f.Crc32, f.Size, out _))
                        match = cand;
                }

                if (match != null)
                {
                    if (string.IsNullOrWhiteSpace(f.Sha1) && !string.IsNullOrWhiteSpace(match.Sha1))
                        f.Sha1 = match.Sha1.ToUpperInvariant();
                    if (string.IsNullOrWhiteSpace(f.Md5) && !string.IsNullOrWhiteSpace(match.Md5))
                        f.Md5 = match.Md5.ToUpperInvariant();
                }
            }
        }

        public static bool ShouldPreserveExistingSidecar(
            string targetFileOrArchivePath,
            IReadOnlyList<FileHashResult> candidateFiles,
            out FileHashResult? preservedContainerHash)
        {
            preservedContainerHash = null;
            if (!HashSidecarService.TryReadExistingSidecarDocument(targetFileOrArchivePath, out var existingDoc) || existingDoc == null)
                return false;

            if (existingDoc.HasContainerHash && File.Exists(targetFileOrArchivePath))
            {
                var fi = new FileInfo(targetFileOrArchivePath);
                if (existingDoc.ContainerSize == fi.Length)
                    preservedContainerHash = HashSidecarService.ReconstructContainerHashFromDocument(existingDoc, fi.Name);
            }

            if (existingDoc.IsCompositeFolder || existingDoc.Files.Count == 0)
                return false;

            bool existingHasFullDigests = existingDoc.Files.All(f =>
                !string.IsNullOrWhiteSpace(f.Crc32) &&
                !string.IsNullOrWhiteSpace(f.Md5) &&
                !string.IsNullOrWhiteSpace(f.Sha1));

            bool candidateMissingDigests = candidateFiles == null || candidateFiles.Count == 0 ||
                candidateFiles.Any(f => string.IsNullOrWhiteSpace(f.Md5) || string.IsNullOrWhiteSpace(f.Sha1));

            if (existingHasFullDigests && candidateMissingDigests)
            {
                // Merge existing verified MD5/SHA1 onto matching CRC32+Size entries and skip degrading disk write
                if (candidateFiles != null)
                {
                    foreach (var cf in candidateFiles)
                    {
                        var match = existingDoc.Files.FirstOrDefault(ef =>
                            ef.Size == cf.Size &&
                            !string.IsNullOrWhiteSpace(ef.Crc32) &&
                            ef.Crc32.Equals(cf.Crc32, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            if (string.IsNullOrWhiteSpace(cf.Md5)) cf.Md5 = match.Md5;
                            if (string.IsNullOrWhiteSpace(cf.Sha1)) cf.Sha1 = match.Sha1;
                        }
                    }
                }
                return true;
            }

            return false;
        }

        public static Dictionary<string, RomManifestItem> LoadExistingManifest(string csvPath)
        {
            var cache = new Dictionary<string, RomManifestItem>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(csvPath))
                return cache;

            try
            {
                var lines = File.ReadAllLines(csvPath);
                if (lines.Length <= 1) return cache;

                var headerCols = ParseCsvLine(lines[0]);
                bool hasCrcColumn = headerCols.Any(h => h.Contains("CRC32", StringComparison.OrdinalIgnoreCase));
                bool hasComplianceColumn = headerCols.Any(h => h.Contains("Compliance", StringComparison.OrdinalIgnoreCase));

                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var cols = ParseCsvLine(line);
                    RomManifestItem? item = null;

                    if (cols.Count >= 9 && (hasCrcColumn || hasComplianceColumn))
                    {
                        item = new RomManifestItem
                        {
                            GameTitle = cols[0],
                            ProperName = cols[1],
                            ArchiveFileName = cols[2],
                            RomOrFolderName = cols[3],
                            VerifiedLocalCrc32 = cols[4],
                            VerifiedLocalHash = cols[5],
                            MatchStatus = NormalizeManifestMatchStatus(cols[6]),
                            ArchiveCompliance = string.IsNullOrWhiteSpace(cols[7]) ? "CLEAN" : cols[7],
                            Notes = cols[8]
                        };
                    }
                    else if (cols.Count >= 8 && hasCrcColumn && !hasComplianceColumn)
                    {
                        item = new RomManifestItem
                        {
                            GameTitle = cols[0],
                            ProperName = cols[1],
                            ArchiveFileName = cols[2],
                            RomOrFolderName = cols[3],
                            VerifiedLocalCrc32 = cols[4],
                            VerifiedLocalHash = cols[5],
                            MatchStatus = NormalizeManifestMatchStatus(cols[6]),
                            ArchiveCompliance = "CLEAN",
                            Notes = cols[7]
                        };
                    }
                    else if (cols.Count >= 8 && !hasCrcColumn && hasComplianceColumn)
                    {
                        item = new RomManifestItem
                        {
                            GameTitle = cols[0],
                            ProperName = cols[1],
                            ArchiveFileName = cols[2],
                            RomOrFolderName = cols[3],
                            VerifiedLocalCrc32 = "",
                            VerifiedLocalHash = cols[4],
                            MatchStatus = NormalizeManifestMatchStatus(cols[5]),
                            ArchiveCompliance = string.IsNullOrWhiteSpace(cols[6]) ? "CLEAN" : cols[6],
                            Notes = cols[7]
                        };
                    }
                    else if (cols.Count >= 7)
                    {
                        item = new RomManifestItem
                        {
                            GameTitle = cols[0],
                            ProperName = cols[1],
                            ArchiveFileName = cols[2],
                            RomOrFolderName = cols[3],
                            VerifiedLocalCrc32 = "",
                            VerifiedLocalHash = cols[4],
                            MatchStatus = NormalizeManifestMatchStatus(cols[5]),
                            ArchiveCompliance = "CLEAN",
                            Notes = cols[6]
                        };
                    }

                    if (item != null)
                    {
                        string key = item.ArchiveFileName != "N/A" ? item.ArchiveFileName : item.RomOrFolderName;
                        if (!string.IsNullOrWhiteSpace(key))
                            cache[key] = item;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RomManifestCsvService] LoadExistingManifest error: {ex.Message}");
            }

            return cache;
        }

        public static void WriteManifestToCsv(string csvPath, List<RomManifestItem> items)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Game Title,Proper Redump/No-Intro Name,Archive File Name,ROM / Folder Name,Verified Local CRC32,Verified Local Hash,Match Status,Archive Compliance,Notes");

            foreach (var item in items.OrderBy(x => x.GameTitle, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine(string.Join(",",
                    EscapeCsv(item.GameTitle),
                    EscapeCsv(item.ProperName),
                    EscapeCsv(item.ArchiveFileName),
                    EscapeCsv(item.RomOrFolderName),
                    EscapeCsv(item.VerifiedLocalCrc32),
                    EscapeCsv(item.VerifiedLocalHash),
                    EscapeCsv(NormalizeManifestMatchStatus(item.MatchStatus)),
                    EscapeCsv(string.IsNullOrWhiteSpace(item.ArchiveCompliance) ? "CLEAN" : item.ArchiveCompliance),
                    EscapeCsv(item.Notes)));
            }

            ConfigService.AtomicWriteAllText(csvPath, sb.ToString());
        }

        public static string EscapeCsv(string? field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            return (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
                ? $"\"{field.Replace("\"", "\"\"")}\""
                : field;
        }

        public static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            var cur = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                    else inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(cur.ToString());
                    cur.Clear();
                }
                else cur.Append(c);
            }
            result.Add(cur.ToString());
            return result;
        }

        public static string GuessInnerRomNameFromArchive(FileInfo arch)
        {
            if (arch.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var zip = System.IO.Compression.ZipFile.OpenRead(arch.FullName);
                    var entry = zip.Entries.FirstOrDefault(e => !e.FullName.EndsWith("/") && !e.FullName.EndsWith("\\"));
                    if (entry != null) return entry.Name;
                }
                catch (Exception ex) { Debug.WriteLine($"[RomManifestCsvService] Zip peek ignored: {ex.Message}"); }
            }
            return Path.GetFileNameWithoutExtension(arch.Name);
        }

        public static Task<string> ComputeFileSha1Async(string filePath, CancellationToken ct = default) =>
            Task.Run(() =>
            {
                try
                {
                    using var sha1 = SHA1.Create();
                    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 262144, FileOptions.SequentialScan);
                    return Convert.ToHexString(sha1.ComputeHash(stream));
                }
                catch (Exception shaEx)
                {
                    Debug.WriteLine($"[RomManifestCsvService] ComputeFileSha1Async error: {shaEx.Message}");
                    return "ERROR";
                }
            }, ct);
    }
}
