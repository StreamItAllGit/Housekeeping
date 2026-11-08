using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Housekeeping.Services
{
    public class DatRomEntry
    {
        public string GameName { get; set; } = "";
        public string RomFileName { get; set; } = "";
        public long Size { get; set; }
        public string Crc32 { get; set; } = "";
        public string Sha1 { get; set; } = "";
        public string DatTitle { get; set; } = "";
    }

    public static class DatTitleNormalizer
    {
        private static readonly Regex CustomTagRegex = new(
            @"\((?:PR|PRt|RH|Hack|Homebrew|Proto|Beta|Sample|Demo|Unl|Alt|DumpTag|Launcher)[^\)]*\)|\[[^\]]+\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex PunctuationRegex = new(
            @"[;:_\-–—.,'""()\[\]{}/\\\s]+",
            RegexOptions.Compiled);

        private static readonly Regex ArticleRegex = new(
            @"\b(the|a|an)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex MultiSpaceRegex = new(
            @"\s+",
            RegexOptions.Compiled);

        public static bool HasCustomTag(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return CustomTagRegex.IsMatch(name);
        }

        public static string CleanUserTags(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            string s = CustomTagRegex.Replace(name, " ");
            return MultiSpaceRegex.Replace(s, " ").Trim();
        }

        public static string NormalizeSlug(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            string cleaned = CleanUserTags(name);

            // Strip diacritics / accents (e.g. Pokémon -> Pokemon)
            string formD = cleaned.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(formD.Length);
            foreach (char c in formD)
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(c);
                if (uc != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }
            string noAccents = sb.ToString();

            // Strip articles (The, A, An)
            string noArticles = ArticleRegex.Replace(noAccents, " ");

            // Remove punctuation
            string noPunct = PunctuationRegex.Replace(noArticles, " ");

            // Retain only lowercase alphanumeric
            var cleanSlug = new StringBuilder(noPunct.Length);
            foreach (char c in noPunct)
            {
                if (char.IsLetterOrDigit(c))
                {
                    cleanSlug.Append(char.ToLowerInvariant(c));
                }
            }

            return cleanSlug.ToString();
        }

        public static double CalculateSimilarity(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2)) return 1.0;
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;
            if (s1.Equals(s2, StringComparison.OrdinalIgnoreCase)) return 1.0;

            int len1 = s1.Length;
            int len2 = s2.Length;
            int maxLen = Math.Max(len1, len2);

            // Single-row Levenshtein distance using stackalloc / ArrayPool (zero 2D array heap allocation)
            int[]? rented = null;
            Span<int> row = len2 + 1 <= 256
                ? stackalloc int[len2 + 1]
                : (rented = ArrayPool<int>.Shared.Rent(len2 + 1)).AsSpan(0, len2 + 1);

            try
            {
                for (int j = 0; j <= len2; j++)
                    row[j] = j;

                for (int i = 1; i <= len1; i++)
                {
                    int prevDiag = row[0];
                    row[0] = i;
                    char c1 = s1[i - 1];

                    for (int j = 1; j <= len2; j++)
                    {
                        int temp = row[j];
                        int cost = (c1 == s2[j - 1]) ? 0 : 1;
                        row[j] = Math.Min(
                            Math.Min(row[j] + 1, row[j - 1] + 1),
                            prevDiag + cost);
                        prevDiag = temp;
                    }
                }

                return 1.0 - (double)row[len2] / maxLen;
            }
            finally
            {
                if (rented != null)
                    ArrayPool<int>.Shared.Return(rented);
            }
        }
    }

    public class MultiFileDatEvaluation
    {
        public bool AnyHashMatched { get; set; }
        public bool AllDataTracksVerified { get; set; }
        public bool AllExpectedDatTracksPresent { get; set; }
        public bool AllDescriptorsVerified { get; set; }
        public bool VerifiedViaCrc32Only { get; set; }
        public int TotalFiles { get; set; }
        public int DataTrackCount { get; set; }
        public int MatchedDataTrackCount { get; set; }
        public int DescriptorFileCount { get; set; }
        public int MatchedDescriptorCount { get; set; }
        public int PlaylistFileCount { get; set; }
        public int ExpectedDatTrackCount { get; set; }
        public int MatchedDistinctGameCount { get; set; }
        public string PrimaryProperName { get; set; } = "";
        public string DatTitle { get; set; } = "";
    }

    public class DatIndex
    {
        private static readonly Regex TrailingDiscTagRegex = new(
            @"\s*\((?:Disc|Disk|CD|Side|Part|DVD|GD)\s*[0-9A-Z]{1,2}\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public string Title { get; set; } = "";
        public Dictionary<string, DatRomEntry> BySha1 { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, DatRomEntry> ByCrc32 { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, DatRomEntry> ByFileName { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, DatRomEntry> ByGameName { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, DatRomEntry> ByNormalizedSlug { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<DatRomEntry>> RomsByGameName { get; } = new(StringComparer.OrdinalIgnoreCase);

        public static bool HashesMatch(
            DatRomEntry datRom,
            string? localSha1,
            string? localCrc32,
            long localSize,
            out bool matchedViaCrc32Only)
        {
            matchedViaCrc32Only = false;
            if (datRom == null)
                return false;

            bool hasDatSha1 = !string.IsNullOrWhiteSpace(datRom.Sha1);
            bool hasLocalSha1 = !string.IsNullOrWhiteSpace(localSha1) &&
                                !string.Equals(localSha1, "N/A", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(localSha1, "ERROR", StringComparison.OrdinalIgnoreCase);

            // When both DAT and local file have SHA-1, SHA-1 is authoritative
            if (hasDatSha1 && hasLocalSha1)
            {
                return datRom.Sha1.Equals(localSha1!.Trim(), StringComparison.OrdinalIgnoreCase);
            }

            // Fallback to CRC32 + Size when either the DAT is CRC32-only or local audit is Fast Header CRC32 mode
            string normDatCrc = HashSidecarService.NormalizeCrc32Hex(datRom.Crc32, datRom.Size);
            string normLocalCrc = HashSidecarService.NormalizeCrc32Hex(localCrc32, localSize);
            if (normDatCrc.Length == 8 && normLocalCrc.Length == 8 &&
                normDatCrc.Equals(normLocalCrc, StringComparison.OrdinalIgnoreCase))
            {
                if (datRom.Size > 0 && localSize > 0 && datRom.Size != localSize)
                    return false;

                matchedViaCrc32Only = hasDatSha1 && !hasLocalSha1;
                return true;
            }

            return false;
        }

        public static bool IsPreferredPrimaryEntry(DatRomEntry candidate, DatRomEntry existing)
        {
            bool candIsDesc = ReleaseStructureDetector.IsDiscDescriptorFile(candidate.RomFileName);
            bool existIsDesc = ReleaseStructureDetector.IsDiscDescriptorFile(existing.RomFileName);
            if (candIsDesc != existIsDesc)
                return !candIsDesc;

            return candidate.Size > existing.Size;
        }

        public void Add(DatRomEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(entry.Sha1))
                BySha1[entry.Sha1] = entry;

            if (!string.IsNullOrWhiteSpace(entry.Crc32))
            {
                entry.Crc32 = HashSidecarService.NormalizeCrc32Hex(entry.Crc32, entry.Size);
                if (!string.IsNullOrWhiteSpace(entry.Crc32))
                    ByCrc32[entry.Crc32] = entry;
            }

            if (!string.IsNullOrWhiteSpace(entry.RomFileName))
                ByFileName[entry.RomFileName] = entry;

            if (!string.IsNullOrWhiteSpace(entry.GameName))
            {
                if (!RomsByGameName.TryGetValue(entry.GameName, out var romList))
                {
                    romList = new List<DatRomEntry>();
                    RomsByGameName[entry.GameName] = romList;
                }

                bool alreadyListed = romList.Any(r =>
                    r.RomFileName.Equals(entry.RomFileName, StringComparison.OrdinalIgnoreCase) &&
                    r.Sha1.Equals(entry.Sha1, StringComparison.OrdinalIgnoreCase) &&
                    r.Crc32.Equals(entry.Crc32, StringComparison.OrdinalIgnoreCase));
                if (!alreadyListed)
                {
                    romList.Add(entry);
                }

                if (!ByGameName.TryGetValue(entry.GameName, out var existingGameEntry) ||
                    IsPreferredPrimaryEntry(entry, existingGameEntry))
                {
                    ByGameName[entry.GameName] = entry;
                }

                string slug = DatTitleNormalizer.NormalizeSlug(entry.GameName);
                if (!string.IsNullOrEmpty(slug))
                {
                    if (!ByNormalizedSlug.TryGetValue(slug, out var existingSlugEntry) ||
                        (existingSlugEntry.GameName.Equals(entry.GameName, StringComparison.OrdinalIgnoreCase) &&
                         IsPreferredPrimaryEntry(entry, existingSlugEntry)))
                    {
                        ByNormalizedSlug[slug] = entry;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(entry.RomFileName))
            {
                string slugRom = DatTitleNormalizer.NormalizeSlug(Path.GetFileNameWithoutExtension(entry.RomFileName));
                if (!string.IsNullOrEmpty(slugRom))
                {
                    if (!ByNormalizedSlug.TryGetValue(slugRom, out var existingRomSlugEntry) ||
                        (existingRomSlugEntry.GameName.Equals(entry.GameName, StringComparison.OrdinalIgnoreCase) &&
                         IsPreferredPrimaryEntry(entry, existingRomSlugEntry)))
                    {
                        ByNormalizedSlug[slugRom] = entry;
                    }
                }
            }
        }

        public IReadOnlyList<DatRomEntry> GetRomsForGame(string? gameName)
        {
            if (!string.IsNullOrWhiteSpace(gameName) && RomsByGameName.TryGetValue(gameName, out var list))
                return list;
            return Array.Empty<DatRomEntry>();
        }

        /// <summary>
        /// Evaluates a complete multi-file ROM/disc/track set (including multi-track CDDA .bin+.cue/.gdi
        /// and multi-disc Disc 1..N + .m3u sets) against all &lt;rom&gt; entries in the DAT index.
        /// </summary>
        public MultiFileDatEvaluation EvaluateMultiFileSet(IReadOnlyList<FileHashResult> localFiles, string? fallbackTitle = null)
        {
            var eval = new MultiFileDatEvaluation
            {
                TotalFiles = localFiles?.Count ?? 0
            };

            if (localFiles == null || localFiles.Count == 0 || (BySha1.Count == 0 && ByCrc32.Count == 0))
                return eval;

            var dataTracks = new List<FileHashResult>();
            var descriptors = new List<FileHashResult>();

            foreach (var f in localFiles)
            {
                string name = !string.IsNullOrWhiteSpace(f.FileName) ? f.FileName : f.RelativePath;
                if (name.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase))
                {
                    eval.PlaylistFileCount++;
                }
                else if (ReleaseStructureDetector.IsDiscDescriptorFile(name))
                {
                    descriptors.Add(f);
                }
                else
                {
                    dataTracks.Add(f);
                }
            }

            eval.DataTrackCount = dataTracks.Count;
            eval.DescriptorFileCount = descriptors.Count;

            var matchedGameNames = new List<string>();
            var matchedGameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void RecordMatchedGame(DatRomEntry datEntry)
            {
                if (!string.IsNullOrWhiteSpace(datEntry.GameName) && matchedGameSet.Add(datEntry.GameName))
                    matchedGameNames.Add(datEntry.GameName);
                if (string.IsNullOrEmpty(eval.DatTitle) && !string.IsNullOrWhiteSpace(datEntry.DatTitle))
                    eval.DatTitle = datEntry.DatTitle;
            }

            // Evaluate data tracks largest-first so unique primary data tracks anchor the <game> identity
            // before any shared 2-second digital-silence audio tracks are checked.
            foreach (var track in dataTracks.OrderByDescending(t => t.Size))
            {
                DatRomEntry? matchedEntry = null;
                bool trackViaCrcOnly = false;

                // First check already-anchored <game> track lists (prevents shared silence tracks from jumping to another game)
                foreach (var gameName in matchedGameNames)
                {
                    if (RomsByGameName.TryGetValue(gameName, out var gameRoms))
                    {
                        foreach (var r in gameRoms)
                        {
                            if (HashesMatch(r, track.Sha1, track.Crc32, track.Size, out bool viaCrc))
                            {
                                matchedEntry = r;
                                trackViaCrcOnly = viaCrc;
                                break;
                            }
                        }
                        if (matchedEntry != null)
                            break;
                    }
                }

                if (matchedEntry == null)
                {
                    matchedEntry = FindMatchByHash(track.Sha1, track.Crc32, track.Size);
                    if (matchedEntry != null)
                    {
                        _ = HashesMatch(matchedEntry, track.Sha1, track.Crc32, track.Size, out trackViaCrcOnly);
                    }
                }

                if (matchedEntry != null)
                {
                    eval.MatchedDataTrackCount++;
                    eval.AnyHashMatched = true;
                    if (trackViaCrcOnly)
                        eval.VerifiedViaCrc32Only = true;
                    RecordMatchedGame(matchedEntry);
                }
            }

            // Evaluate disc descriptors (.cue / .gdi / .ccd / .mds)
            foreach (var desc in descriptors)
            {
                DatRomEntry? matchedDesc = null;
                bool descViaCrcOnly = false;
                foreach (var gameName in matchedGameNames)
                {
                    if (RomsByGameName.TryGetValue(gameName, out var gameRoms))
                    {
                        foreach (var r in gameRoms)
                        {
                            if (ReleaseStructureDetector.IsDiscDescriptorFile(r.RomFileName) &&
                                HashesMatch(r, desc.Sha1, desc.Crc32, desc.Size, out bool viaCrc))
                            {
                                matchedDesc = r;
                                descViaCrcOnly = viaCrc;
                                break;
                            }
                        }
                        if (matchedDesc != null)
                            break;
                    }
                }

                if (matchedDesc == null)
                {
                    matchedDesc = FindMatchByHash(desc.Sha1, desc.Crc32, desc.Size);
                    if (matchedDesc != null)
                    {
                        _ = HashesMatch(matchedDesc, desc.Sha1, desc.Crc32, desc.Size, out descViaCrcOnly);
                    }
                }

                if (matchedDesc != null)
                {
                    eval.MatchedDescriptorCount++;
                    eval.AnyHashMatched = true;
                    if (descViaCrcOnly)
                        eval.VerifiedViaCrc32Only = true;
                    RecordMatchedGame(matchedDesc);
                }
            }

            // If no track matched by hash, attempt name-based game lookup so ExpectedDatTrackCount & ProperName are populated
            if (matchedGameNames.Count == 0 && !string.IsNullOrWhiteSpace(fallbackTitle))
            {
                var nameFallback = FindMatchByName(fallbackTitle);
                if (nameFallback != null)
                {
                    RecordMatchedGame(nameFallback);
                }
            }

            eval.MatchedDistinctGameCount = matchedGameNames.Count;

            // Verify multiset completeness against all expected non-descriptor <rom> tracks across matched <game> entries
            var expectedDataRoms = new List<DatRomEntry>();
            foreach (var gName in matchedGameNames)
            {
                if (RomsByGameName.TryGetValue(gName, out var roms))
                {
                    expectedDataRoms.AddRange(roms.Where(r => !ReleaseStructureDetector.IsDiscDescriptorFile(r.RomFileName)));
                }
            }

            eval.ExpectedDatTrackCount = expectedDataRoms.Count;

            if (dataTracks.Count > 0 && eval.MatchedDataTrackCount == dataTracks.Count && expectedDataRoms.Count > 0)
            {
                // Multiset verification so duplicate silence tracks must also match exact track counts
                var remainingLocal = new List<FileHashResult>(dataTracks);
                bool allExpectedSatisfied = true;

                foreach (var exp in expectedDataRoms)
                {
                    int idx = remainingLocal.FindIndex(l =>
                        HashesMatch(exp, l.Sha1, l.Crc32, l.Size, out _));

                    if (idx >= 0)
                    {
                        remainingLocal.RemoveAt(idx);
                    }
                    else
                    {
                        allExpectedSatisfied = false;
                        break;
                    }
                }

                eval.AllExpectedDatTracksPresent = allExpectedSatisfied && remainingLocal.Count == 0;
                eval.AllDataTracksVerified = eval.AllExpectedDatTracksPresent;
            }

            eval.AllDescriptorsVerified = descriptors.Count == 0 || eval.MatchedDescriptorCount == descriptors.Count;

            if (matchedGameNames.Count == 1)
            {
                eval.PrimaryProperName = matchedGameNames[0];
            }
            else if (matchedGameNames.Count > 1)
            {
                var strippedTitles = matchedGameNames
                    .Select(n => TrailingDiscTagRegex.Replace(n, "").Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                eval.PrimaryProperName = strippedTitles.Count == 1
                    ? $"{strippedTitles[0]} ({matchedGameNames.Count} Discs)"
                    : matchedGameNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).First();
            }

            return eval;
        }

        public DatRomEntry? FindMatchByName(string? romOrArchiveName)
        {
            if (string.IsNullOrWhiteSpace(romOrArchiveName))
                return null;

            string clean = Path.GetFileName(romOrArchiveName.Trim());

            // 1. Direct exact filename match
            if (ByFileName.TryGetValue(clean, out var entryName))
                return entryName;

            string baseName = Path.GetFileNameWithoutExtension(clean);

            // 2. Direct exact game name match
            if (ByGameName.TryGetValue(baseName, out var entryGame))
                return entryGame;

            // 3. User tags stripped match
            string tagCleaned = DatTitleNormalizer.CleanUserTags(baseName);
            if (!string.IsNullOrEmpty(tagCleaned) && ByGameName.TryGetValue(tagCleaned, out var entryTagCleaned))
                return entryTagCleaned;

            // 4. Normalized slug match (ignores articles, punctuation, case, accents)
            string slug = DatTitleNormalizer.NormalizeSlug(baseName);
            if (!string.IsNullOrEmpty(slug) && ByNormalizedSlug.TryGetValue(slug, out var entrySlug))
                return entrySlug;

            // 5. High-confidence fuzzy similarity match across catalog (>= 0.88) with O(1) length-ratio pre-filter
            if (!string.IsNullOrEmpty(slug) && slug.Length >= 5)
            {
                DatRomEntry? bestMatch = null;
                double bestScore = 0.0;
                int sLen = slug.Length;

                foreach (var kvp in ByNormalizedSlug)
                {
                    int kLen = kvp.Key.Length;
                    int minL = Math.Min(sLen, kLen);
                    int maxL = Math.Max(sLen, kLen);
                    // Upper-bound similarity cannot exceed minL / maxL
                    if ((double)minL / maxL < 0.88)
                        continue;

                    double sim = DatTitleNormalizer.CalculateSimilarity(slug, kvp.Key);
                    if (sim >= 0.88 && sim > bestScore)
                    {
                        bestScore = sim;
                        bestMatch = kvp.Value;
                    }
                }

                if (bestMatch != null)
                    return bestMatch;
            }

            return null;
        }

        public DatRomEntry? FindMatchByHash(string? sha1, string? crc32, long localSize = -1)
        {
            if (!string.IsNullOrWhiteSpace(sha1) && BySha1.TryGetValue(sha1.Trim(), out var entrySha1))
                return entrySha1;

            string normCrc = HashSidecarService.NormalizeCrc32Hex(crc32, localSize);
            if (!string.IsNullOrWhiteSpace(normCrc) && ByCrc32.TryGetValue(normCrc, out var entryCrc))
            {
                // If local SHA-1 was provided AND DAT entry also has a SHA-1, do not match on CRC32 alone when SHA-1 differed
                bool bothHaveSha1 = !string.IsNullOrWhiteSpace(sha1) && !string.IsNullOrWhiteSpace(entryCrc.Sha1);
                if (!bothHaveSha1 && (localSize <= 0 || entryCrc.Size <= 0 || entryCrc.Size == localSize))
                    return entryCrc;
            }

            return null;
        }

        public DatRomEntry? FindMatch(string? sha1, string? crc32, string? romOrArchiveName, long localSize = -1)
        {
            // Name match first
            var nameMatch = FindMatchByName(romOrArchiveName);
            if (nameMatch != null)
                return nameMatch;

            // Hash fallback
            return FindMatchByHash(sha1, crc32, localSize);
        }
    }

    public static class NoIntroRedumpDatParser
    {
        private static readonly Regex ClrMameGameBlockRegex = new(
            @"game\s*\(\s*name\s+""(?<name>[^""]+)""(?<body>.*?)\r?\n\s*\)",
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private static readonly Regex ClrMameRomLineRegex = new(
            @"rom\s*\(\s*name\s+""(?<rom>[^""]+)""(?:\s+size\s+(?<size>\d+))?(?:\s+crc\s+(?<crc>[a-fA-F0-9]+))?(?:\s+md5\s+[a-fA-F0-9]+)?(?:\s+sha1\s+(?<sha1>[a-fA-F0-9]+))?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex NonAlphanumericKeyRegex = new(
            @"[^a-zA-Z0-9]",
            RegexOptions.Compiled);

        private static readonly ConcurrentDictionary<string, (DateTime LastWriteUtc, DatIndex Index)> ParsedFileCache =
            new(StringComparer.OrdinalIgnoreCase);

        public static DatIndex ParseFile(string datFilePath)
        {
            var index = new DatIndex();
            if (!File.Exists(datFilePath))
                return index;

            string fullPath = Path.GetFullPath(datFilePath);
            DateTime lastWriteUtc = File.GetLastWriteTimeUtc(fullPath);

            if (ParsedFileCache.TryGetValue(fullPath, out var cached) && cached.LastWriteUtc == lastWriteUtc)
            {
                return cached.Index;
            }

            try
            {
                // Check if XML format
                using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536))
                {
                    var settings = new XmlReaderSettings
                    {
                        DtdProcessing = DtdProcessing.Ignore,
                        IgnoreComments = true,
                        IgnoreWhitespace = true
                    };

                    using var reader = XmlReader.Create(stream, settings);
                    string currentGame = "";
                    while (reader.Read())
                    {
                        if (reader.NodeType == XmlNodeType.Element)
                        {
                            if (reader.Name.Equals("header", StringComparison.OrdinalIgnoreCase))
                            {
                                // read until </header>
                            }
                            else if (reader.Name.Equals("game", StringComparison.OrdinalIgnoreCase) ||
                                     reader.Name.Equals("machine", StringComparison.OrdinalIgnoreCase))
                            {
                                currentGame = reader.GetAttribute("name") ?? "";
                            }
                            else if (reader.Name.Equals("description", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(currentGame))
                            {
                                currentGame = reader.ReadElementContentAsString();
                            }
                            else if (reader.Name.Equals("rom", StringComparison.OrdinalIgnoreCase))
                            {
                                string romName = reader.GetAttribute("name") ?? "";
                                string crc = reader.GetAttribute("crc") ?? "";
                                string sha1 = reader.GetAttribute("sha1") ?? "";
                                string sizeStr = reader.GetAttribute("size") ?? "0";
                                long.TryParse(sizeStr, out long size);

                                var entry = new DatRomEntry
                                {
                                    GameName = string.IsNullOrWhiteSpace(currentGame) ? Path.GetFileNameWithoutExtension(romName) : currentGame,
                                    RomFileName = romName,
                                    Crc32 = crc.ToUpperInvariant(),
                                    Sha1 = sha1.ToUpperInvariant(),
                                    Size = size,
                                    DatTitle = Path.GetFileNameWithoutExtension(datFilePath)
                                };
                                index.Add(entry);
                            }
                        }
                    }

                    if (index.BySha1.Count > 0 || index.ByCrc32.Count > 0 || index.ByFileName.Count > 0)
                    {
                        ParsedFileCache[fullPath] = (lastWriteUtc, index);
                        return index;
                    }
                }
            }
            catch
            {
                // If XML parse failed, fall through to text/ClrMamePro parser
            }

            // Fallback: parse as ClrMamePro text format
            try
            {
                string text = File.ReadAllText(fullPath);
                var gameMatches = ClrMameGameBlockRegex.Matches(text);
                foreach (Match gm in gameMatches)
                {
                    string name = gm.Groups["name"].Value;
                    string body = gm.Groups["body"].Value;
                    var romMatches = ClrMameRomLineRegex.Matches(body);
                    if (romMatches.Count == 0)
                    {
                        index.Add(new DatRomEntry
                        {
                            GameName = name,
                            RomFileName = name,
                            DatTitle = Path.GetFileNameWithoutExtension(datFilePath)
                        });
                        continue;
                    }

                    foreach (Match rm in romMatches)
                    {
                        string rom = rm.Groups["rom"].Value;
                        string crc = rm.Groups["crc"].Value;
                        string sha1 = rm.Groups["sha1"].Value;
                        long.TryParse(rm.Groups["size"].Value, out long size);

                        var entry = new DatRomEntry
                        {
                            GameName = name,
                            RomFileName = string.IsNullOrWhiteSpace(rom) ? name : rom,
                            Crc32 = crc.ToUpperInvariant(),
                            Sha1 = sha1.ToUpperInvariant(),
                            Size = size,
                            DatTitle = Path.GetFileNameWithoutExtension(datFilePath)
                        };
                        index.Add(entry);
                    }
                }
            }
            catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[NoIntroRedumpDatParser] Non-fatal error ignored: {__suppressedEx1.Message}"); }

            if (index.BySha1.Count > 0 || index.ByCrc32.Count > 0 || index.ByFileName.Count > 0)
            {
                ParsedFileCache[fullPath] = (lastWriteUtc, index);
            }

            return index;
        }

        public static DatIndex LoadIndexForConsole(string datDir, string consoleName)
        {
            var combinedIndex = new DatIndex { Title = consoleName };
            if (!Directory.Exists(datDir))
                return combinedIndex;

            string cleanConsole = NormalizeConsoleSearchKey(consoleName);
            var sysEntry = Models.ConsoleSystemCatalog.ResolveSystem(consoleName);

            var safeEnumOpts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };
            var datFiles = Directory.EnumerateFiles(datDir, "*.*", safeEnumOpts)
                .Where(f => f.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));

            foreach (var file in datFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                string cleanFile = NormalizeConsoleSearchKey(fileName);

                bool matches = cleanFile.Contains(cleanConsole, StringComparison.OrdinalIgnoreCase) ||
                               cleanConsole.Contains(cleanFile, StringComparison.OrdinalIgnoreCase);

                if (!matches && sysEntry != null)
                {
                    string canonicalClean = NormalizeConsoleSearchKey(sysEntry.CanonicalName);
                    if (cleanFile.Contains(canonicalClean, StringComparison.OrdinalIgnoreCase) ||
                        canonicalClean.Contains(cleanFile, StringComparison.OrdinalIgnoreCase))
                    {
                        matches = true;
                    }
                    else
                    {
                        foreach (var alias in sysEntry.Aliases)
                        {
                            string aliasClean = NormalizeConsoleSearchKey(alias);
                            if (cleanFile.Contains(aliasClean, StringComparison.OrdinalIgnoreCase) ||
                                aliasClean.Contains(cleanFile, StringComparison.OrdinalIgnoreCase))
                            {
                                matches = true;
                                break;
                            }
                        }
                    }
                }

                if (matches)
                {
                    var parsed = ParseFile(file);
                    foreach (var kvp in parsed.BySha1) combinedIndex.BySha1[kvp.Key] = kvp.Value;
                    foreach (var kvp in parsed.ByCrc32) combinedIndex.ByCrc32[kvp.Key] = kvp.Value;
                    foreach (var kvp in parsed.ByFileName) combinedIndex.ByFileName[kvp.Key] = kvp.Value;
                    foreach (var kvp in parsed.ByGameName)
                    {
                        if (!combinedIndex.ByGameName.TryGetValue(kvp.Key, out var existGame) ||
                            DatIndex.IsPreferredPrimaryEntry(kvp.Value, existGame))
                        {
                            combinedIndex.ByGameName[kvp.Key] = kvp.Value;
                        }
                    }
                    foreach (var kvp in parsed.ByNormalizedSlug)
                    {
                        if (!combinedIndex.ByNormalizedSlug.TryGetValue(kvp.Key, out var existSlug) ||
                            (existSlug.GameName.Equals(kvp.Value.GameName, StringComparison.OrdinalIgnoreCase) &&
                             DatIndex.IsPreferredPrimaryEntry(kvp.Value, existSlug)))
                        {
                            combinedIndex.ByNormalizedSlug[kvp.Key] = kvp.Value;
                        }
                    }
                    foreach (var kvp in parsed.RomsByGameName)
                    {
                        if (!combinedIndex.RomsByGameName.TryGetValue(kvp.Key, out var mergedList))
                        {
                            mergedList = new List<DatRomEntry>(kvp.Value.Count);
                            combinedIndex.RomsByGameName[kvp.Key] = mergedList;
                        }

                        foreach (var r in kvp.Value)
                        {
                            bool dup = mergedList.Any(x =>
                                x.RomFileName.Equals(r.RomFileName, StringComparison.OrdinalIgnoreCase) &&
                                x.Sha1.Equals(r.Sha1, StringComparison.OrdinalIgnoreCase) &&
                                x.Crc32.Equals(r.Crc32, StringComparison.OrdinalIgnoreCase));
                            if (!dup)
                                mergedList.Add(r);
                        }
                    }
                }
            }

            return combinedIndex;
        }

        public static string NormalizeConsoleSearchKey(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            return NonAlphanumericKeyRegex.Replace(name, "").ToLowerInvariant();
        }
    }
}
