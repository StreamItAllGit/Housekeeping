// @module: Housekeeping.Core/Services/DatTitleNormalizer
// @purpose: Tag stripping, diacritic-free slug normalization, and zero-allocation Levenshtein similarity for ROM DAT matching.
// @depends-on: System.Buffers, System.Globalization, System.Text, System.Text.RegularExpressions
// @exports: DatTitleNormalizer
using System;
using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Housekeeping.Services
{
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
            string noArticles = ArticleRegex.Replace(noAccents, " ");
            string noPunct = PunctuationRegex.Replace(noArticles, " ");

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
}
