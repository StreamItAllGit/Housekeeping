// @module: Housekeeping.Services.Modules.CollectionManifestModule.Exporters
// @purpose: Delimiter-aware CSV/TSV (.csv/.tsv), JSON, and interactive HTML report exporters for Module 8 Collection Manifest.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Net, System.Text, System.Text.Json, Housekeeping.Models
// @exports: Housekeeping.Services.Modules.CollectionManifestModule (partial: ResolveManifestCsvDelimiterAndExtension, ExportManifestsAsync, GenerateHtmlReport)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Models;

namespace Housekeeping.Services.Modules
{
    public static partial class CollectionManifestModule
    {
        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        public static (char Delimiter, string Extension) ResolveManifestCsvDelimiterAndExtension(string? rawDelimiter)
        {
            if (string.IsNullOrEmpty(rawDelimiter))
                return (',', ".csv");

            if (rawDelimiter == "\t" ||
                string.Equals(rawDelimiter.Trim(), "\\t", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rawDelimiter.Trim(), "Tab", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rawDelimiter.Trim(), "TSV", StringComparison.OrdinalIgnoreCase))
            {
                return ('\t', ".tsv");
            }

            string trimmed = rawDelimiter.Trim();
            if (trimmed == ";" || string.Equals(trimmed, "Semicolon", StringComparison.OrdinalIgnoreCase))
                return (';', ".csv");
            if (trimmed == "|" || string.Equals(trimmed, "Pipe", StringComparison.OrdinalIgnoreCase))
                return ('|', ".csv");

            return (',', ".csv");
        }

        public static string FormatDelimitedCell(string? rawValue, char delimiter)
        {
            string val = rawValue ?? "";
            if (delimiter == '\t')
            {
                return val.Replace('\t', ' ').Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
            }

            string escaped = val.Replace("\"", "\"\"");
            return $"\"{escaped}\"";
        }

        public static async Task ExportManifestsAsync(
            List<ManifestRecordItem> records,
            string outputDirectory,
            string baseFileName,
            CollectionManifestConfig cfg,
            Action<string, string> log,
            CancellationToken ct)
        {
            if (!Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            var customCols = (cfg.FieldParseRules ?? new())
                .Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.ColumnName))
                .Select(r => r.ColumnName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (cfg.ExportCsv)
            {
                var (delim, ext) = ResolveManifestCsvDelimiterAndExtension(cfg.CsvDelimiter);
                string delimStr = delim.ToString();
                string csvPath = Path.Combine(outputDirectory, $"{baseFileName}{ext}");
                var sb = new StringBuilder();

                string[] stdHeaders =
                {
                    "LocationId", "RootPath", "RelativePath", "FileName", "CleanTitle", "Category",
                    "YearOrRegion", "ResolutionOrTags", "FileSizeBytes", "FileSizeFormatted",
                    "CRC32", "SHA1", "MD5", "IntegrityStatus", "IntegrityDetail"
                };

                var allHeaders = customCols.Count > 0
                    ? stdHeaders.Concat(customCols).Select(h => delim == '\t' ? FormatDelimitedCell(h, delim) : h)
                    : stdHeaders;
                sb.AppendLine(string.Join(delimStr, allHeaders));

                foreach (var r in records)
                {
                    var cells = new List<string>(15 + customCols.Count)
                    {
                        FormatDelimitedCell(r.LocationId, delim),
                        FormatDelimitedCell(r.RootPath, delim),
                        FormatDelimitedCell(r.RelativePath, delim),
                        FormatDelimitedCell(r.FileName, delim),
                        FormatDelimitedCell(r.CleanTitle, delim),
                        FormatDelimitedCell(r.Category, delim),
                        FormatDelimitedCell(r.YearOrRegion, delim),
                        FormatDelimitedCell(r.ResolutionOrTags, delim),
                        r.FileSizeBytes.ToString(),
                        FormatDelimitedCell(r.FileSizeFormatted, delim),
                        FormatDelimitedCell(r.Crc32, delim),
                        FormatDelimitedCell(r.Sha1, delim),
                        FormatDelimitedCell(r.Md5, delim),
                        FormatDelimitedCell(r.IntegrityStatus, delim),
                        FormatDelimitedCell(r.IntegrityDetail, delim)
                    };

                    foreach (string col in customCols)
                    {
                        cells.Add(FormatDelimitedCell(r.CustomFields.TryGetValue(col, out var v) ? v : "", delim));
                    }

                    sb.AppendLine(string.Join(delimStr, cells));
                }

                await File.WriteAllTextAsync(csvPath, sb.ToString(), Utf8NoBom, ct);
                log("SUCCESS", $"Exported Manifest {(ext == ".tsv" ? "TSV" : "CSV")}: {csvPath}");
                MirrorIfConfigured(csvPath, cfg.SecondaryMirrorFolder, log);
            }

            if (cfg.ExportJson)
            {
                string jsonPath = Path.Combine(outputDirectory, $"{baseFileName}.json");
                string json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(jsonPath, json, Utf8NoBom, ct);
                log("SUCCESS", $"Exported Manifest JSON: {jsonPath}");
                MirrorIfConfigured(jsonPath, cfg.SecondaryMirrorFolder, log);
            }

            if (cfg.ExportHtmlReport)
            {
                string htmlPath = Path.Combine(outputDirectory, $"{baseFileName}_report.html");
                string html = GenerateHtmlReport(records, baseFileName, customCols);
                await File.WriteAllTextAsync(htmlPath, html, Utf8NoBom, ct);
                log("SUCCESS", $"Exported Manifest HTML Visual Report: {htmlPath}");
                MirrorIfConfigured(htmlPath, cfg.SecondaryMirrorFolder, log);
            }
        }

        private static void MirrorIfConfigured(string sourceFile, string? mirrorFolder, Action<string, string> log)
        {
            if (string.IsNullOrWhiteSpace(mirrorFolder)) return;
            try
            {
                if (!Directory.Exists(mirrorFolder)) Directory.CreateDirectory(mirrorFolder);
                string dest = Path.Combine(mirrorFolder, Path.GetFileName(sourceFile));
                File.Copy(sourceFile, dest, overwrite: true);
                log("INFO", $"Mirrored manifest to secondary storage: {dest}");
            }
            catch (Exception ex)
            {
                log("WARN", $"Failed mirroring to '{mirrorFolder}': {ex.Message}");
            }
        }

        public static string GenerateHtmlReport(List<ManifestRecordItem> records, string title, List<string>? customCols = null)
        {
            int total = records.Count;
            int verified = records.Count(r => r.IntegrityStatus == "Verified" || r.IntegrityStatus == "Match");
            int captured = records.Count(r => r.IntegrityStatus == "Baseline Captured");
            int warnings = records.Count(r => r.IntegrityStatus == "Bit Rot Warning" || r.IntegrityStatus == "Mismatch");
            int missingOrNa = records.Count(r => r.IntegrityStatus == "Missing" || r.IntegrityStatus == "N/A");
            customCols ??= new List<string>();
            string safeTitle = WebUtility.HtmlEncode(title ?? "Media & Collection Manifest");

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html lang='en'><head><meta charset='utf-8'/>");
            sb.AppendLine($"<title>{safeTitle} — Housekeeping Audit Report</title><style>");
            sb.AppendLine("body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #0f172a; color: #f8fafc; margin: 0; padding: 24px; }");
            sb.AppendLine("h1 { margin-top: 0; color: #38bdf8; font-size: 24px; } .stats { display: flex; gap: 16px; margin-bottom: 24px; flex-wrap: wrap; }");
            sb.AppendLine(".card { background: #1e293b; padding: 16px 20px; border-radius: 8px; flex: 1; min-width: 160px; border: 1px solid #334155; } .card-num { font-size: 28px; font-weight: bold; margin-bottom: 4px; }");
            sb.AppendLine(".badge-match { color: #4ade80; } .badge-captured { color: #38bdf8; } .badge-mismatch { color: #f87171; } .badge-na { color: #94a3b8; }");
            sb.AppendLine("input[type=text] { width: 100%; box-sizing: border-box; padding: 12px; background: #1e293b; border: 1px solid #475569; color: #f8fafc; border-radius: 6px; margin-bottom: 16px; font-size: 14px; }");
            sb.AppendLine("table { width: 100%; border-collapse: collapse; background: #1e293b; border-radius: 8px; overflow: hidden; border: 1px solid #334155; }");
            sb.AppendLine("th, td { padding: 10px 12px; text-align: left; border-bottom: 1px solid #334155; font-size: 12.5px; } th { background: #0f172a; color: #94a3b8; font-weight: 600; } tr:hover { background: #243247; }");
            sb.AppendLine(".pill { display: inline-block; padding: 2px 8px; border-radius: 4px; font-size: 11px; font-weight: bold; }");
            sb.AppendLine(".pill-Verified { background: #065f46; color: #34d399; } .pill-Baseline { background: #0e7490; color: #38bdf8; } .pill-Warning { background: #7f1d1d; color: #f87171; } .pill-Missing { background: #78350f; color: #fbbf24; } .pill-NA { background: #334155; color: #cbd5e1; }");
            sb.AppendLine(".tag-custom { display: inline-block; padding: 1px 6px; border-radius: 3px; font-size: 11px; background: #1e3a8a; color: #93c5fd; }</style></head><body>");
            sb.AppendLine($"<h1>📋 {safeTitle}</h1><p style='color: #94a3b8;'>Generated by Housekeeping • {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p><div class='stats'>");
            sb.AppendLine($"<div class='card'><div class='card-num'>{total}</div><div style='color:#94a3b8;'>Total Items</div></div>");
            sb.AppendLine($"<div class='card'><div class='card-num badge-match'>{verified}</div><div style='color:#94a3b8;'>Verified (No Bit Rot)</div></div>");
            sb.AppendLine($"<div class='card'><div class='card-num badge-captured'>{captured}</div><div style='color:#94a3b8;'>Baseline Captured</div></div>");
            sb.AppendLine($"<div class='card'><div class='card-num badge-mismatch'>{warnings}</div><div style='color:#94a3b8;'>Bit Rot Warnings</div></div>");
            sb.AppendLine($"<div class='card'><div class='card-num badge-na'>{missingOrNa}</div><div style='color:#94a3b8;'>Missing .hash / N/A</div></div></div>");
            sb.AppendLine("<input type='text' id='search' placeholder='🔍 Search items by title, location, category, CRC32, SHA-1, or custom fields...' onkeyup='filterTable()'>");
            sb.Append("<table id='auditTable'><thead><tr><th>Location</th><th>File / Title</th><th>Category</th><th>Size</th><th>CRC32</th><th>SHA-1</th><th>Integrity Status</th><th>Detail</th>");
            foreach (var col in customCols) sb.Append($"<th>{WebUtility.HtmlEncode(col)}</th>");
            sb.AppendLine("</tr></thead><tbody>");

            foreach (var r in records)
            {
                string pillClass = r.IntegrityStatus switch
                {
                    "Verified" or "Match" => "pill-Verified",
                    "Baseline Captured" => "pill-Baseline",
                    "Bit Rot Warning" or "Mismatch" => "pill-Warning",
                    "Missing" => "pill-Missing",
                    _ => "pill-NA"
                };
                sb.Append($"<tr><td><b>{WebUtility.HtmlEncode(r.LocationId)}</b></td><td>{WebUtility.HtmlEncode(r.FileName)}</td><td>{WebUtility.HtmlEncode(r.Category)}</td><td>{r.FileSizeFormatted}</td><td><code>{WebUtility.HtmlEncode(r.Crc32)}</code></td><td><code>{WebUtility.HtmlEncode(r.Sha1)}</code></td><td><span class='pill {pillClass}'>{WebUtility.HtmlEncode(r.IntegrityStatus)}</span></td><td>{WebUtility.HtmlEncode(r.IntegrityDetail)}</td>");
                foreach (var col in customCols)
                {
                    string val = r.CustomFields.TryGetValue(col, out var cv) ? cv : "";
                    sb.Append($"<td>{(string.IsNullOrWhiteSpace(val) ? "—" : $"<span class='tag-custom'>{WebUtility.HtmlEncode(val)}</span>")}</td>");
                }
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</tbody></table><script>function filterTable(){var input=document.getElementById('search').value.toLowerCase();var rows=document.getElementById('auditTable').getElementsByTagName('tbody')[0].getElementsByTagName('tr');for(var i=0;i<rows.length;i++){rows[i].style.display=rows[i].textContent.toLowerCase().indexOf(input)>-1?'':'none';}}</script></body></html>");
            return sb.ToString();
        }

        public static Dictionary<string, string> ApplyFieldParseRules(
            string fileName,
            string relativePath,
            string fullPath,
            List<ManifestFieldParseRule>? rules)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (rules == null || rules.Count == 0) return dict;

            string parentFolderName = "";
            try { parentFolderName = Path.GetFileName(Path.GetDirectoryName(fullPath) ?? "") ?? ""; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[CollectionManifestModule] Parent folder fallback: {ex.Message}"); }

            string fileNameWithoutExt = fileName;
            try { fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[CollectionManifestModule] GetFileNameWithoutExtension fallback: {ex.Message}"); }

            string fileExt = "";
            try { fileExt = Path.GetExtension(fileName).ToLowerInvariant(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[CollectionManifestModule] Extension fallback: {ex.Message}"); }

            foreach (var rule in rules.Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.ColumnName)))
            {
                if (!string.IsNullOrWhiteSpace(rule.FileExtensionFilter) && rule.FileExtensionFilter != "*" && rule.FileExtensionFilter != "* (All Files)")
                {
                    var allowedExts = rule.FileExtensionFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (!allowedExts.Contains("*") && !allowedExts.Contains(".*") && !allowedExts.Contains(fileExt))
                        continue;
                }

                string textToInspect = rule.InspectTarget switch
                {
                    ManifestInspectTarget.FileNameWithoutExtension => fileNameWithoutExt,
                    ManifestInspectTarget.ParentFolderName => parentFolderName,
                    ManifestInspectTarget.RelativePath => relativePath,
                    ManifestInspectTarget.FullPath => fullPath,
                    _ => fileName
                };
                if (string.IsNullOrEmpty(textToInspect)) continue;
                if (rule.MatchMode != ManifestMatchMode.SplitToken && string.IsNullOrEmpty(rule.Pattern)) continue;

                try
                {
                    switch (rule.MatchMode)
                    {
                        case ManifestMatchMode.RegexNamedCapture:
                            var regex = GetOrCreateCachedRegex(rule.Pattern);
                            if (regex != null)
                            {
                                var match = regex.Match(textToInspect);
                                if (match.Success)
                                {
                                    var group = match.Groups[rule.ColumnName];
                                    dict[rule.ColumnName] = (group.Success && !string.IsNullOrWhiteSpace(group.Value)
                                        ? group.Value
                                        : (!string.IsNullOrWhiteSpace(rule.AssignedValue) ? match.Result(rule.AssignedValue) : match.Value)).Trim();
                                }
                            }
                            break;
                        case ManifestMatchMode.RegexPattern:
                            var regPattern = GetOrCreateCachedRegex(rule.Pattern);
                            if (regPattern != null)
                            {
                                var pMatch = regPattern.Match(textToInspect);
                                if (pMatch.Success)
                                    dict[rule.ColumnName] = (!string.IsNullOrWhiteSpace(rule.AssignedValue) ? pMatch.Result(rule.AssignedValue) : pMatch.Value).Trim();
                            }
                            break;
                        case ManifestMatchMode.TextContains:
                            if (textToInspect.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase))
                                dict[rule.ColumnName] = !string.IsNullOrWhiteSpace(rule.AssignedValue) ? rule.AssignedValue : rule.Pattern;
                            break;
                        case ManifestMatchMode.SplitToken:
                            string delim = string.IsNullOrEmpty(rule.SplitDelimiter) ? " - " : rule.SplitDelimiter;
                            string[] tokens = textToInspect.Split(new[] { delim }, StringSplitOptions.None);
                            if (rule.SplitIndex >= 0 && rule.SplitIndex < tokens.Length)
                                dict[rule.ColumnName] = tokens[rule.SplitIndex].Trim();
                            break;
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[CollectionManifestModule] Field rule ignored: {ex.Message}"); }
            }
            return dict;
        }
    }
}
