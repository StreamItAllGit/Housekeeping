using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Housekeeping.Models;

namespace Housekeeping.Services
{
    public class ComplianceReportService
    {
        public static readonly string DefaultReportsFilePath = Path.Combine(ConfigService.AppDataDir, "compliance_reports.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private readonly string _reportsFilePath;
        private readonly object _syncLock = new();
        private Dictionary<string, ComplianceReportItem> _reportsById = new(StringComparer.OrdinalIgnoreCase);

        public event Action? ReportsChanged;

        public ComplianceReportService(string? customReportsFilePath = null)
        {
            _reportsFilePath = string.IsNullOrWhiteSpace(customReportsFilePath)
                ? DefaultReportsFilePath
                : customReportsFilePath;
            Load();
        }

        public void Load()
        {
            lock (_syncLock)
            {
                _reportsById.Clear();
                if (!File.Exists(_reportsFilePath))
                    return;

                try
                {
                    string json = File.ReadAllText(_reportsFilePath, Encoding.UTF8);
                    var items = JsonSerializer.Deserialize<List<ComplianceReportItem>>(json, JsonOptions);
                    if (items != null)
                    {
                        foreach (var item in items.Where(i => !string.IsNullOrWhiteSpace(i.ReportId)))
                        {
                            _reportsById[item.ReportId] = item;
                        }
                    }
                }
                catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[ComplianceReportService] Non-fatal error ignored: {__suppressedEx1.Message}"); }
            }
        }

        public void Save()
        {
            lock (_syncLock)
            {
                try
                {
                    var ordered = _reportsById.Values
                        .OrderBy(r => r.ResolutionStatus)
                        .ThenByDescending(r => (int)r.Severity)
                        .ThenBy(r => r.ConsoleName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(r => r.TargetFileName, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    string json = JsonSerializer.Serialize(ordered, JsonOptions);
                    ConfigService.AtomicWriteAllText(_reportsFilePath, json);
                }
                catch (System.Exception __suppressedEx2) { System.Diagnostics.Debug.WriteLine($"[ComplianceReportService] Non-fatal error ignored: {__suppressedEx2.Message}"); }
            }
        }

        public List<ComplianceReportItem> GetAllReports()
        {
            lock (_syncLock)
            {
                return _reportsById.Values
                    .OrderBy(r => r.ResolutionStatus)
                    .ThenByDescending(r => (int)r.Severity)
                    .ThenBy(r => r.ConsoleName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(r => r.TargetFileName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        public int GetOpenCount()
        {
            lock (_syncLock)
            {
                return _reportsById.Values.Count(r => r.ResolutionStatus == ComplianceReportResolutionStatus.Open);
            }
        }

        public void UpsertSingleTargetResult(ComplianceReportItem evaluatedItem)
        {
            if (evaluatedItem == null || string.IsNullOrWhiteSpace(evaluatedItem.ReportId))
                return;

            lock (_syncLock)
            {
                MergeEvaluatedItemNoLock(evaluatedItem);
                Save();
            }
            ReportsChanged?.Invoke();
        }

        public void UpsertConsoleScanResults(string consoleName, IEnumerable<ComplianceReportItem> evaluatedTargets)
        {
            lock (_syncLock)
            {
                foreach (var item in evaluatedTargets)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.ReportId))
                        continue;
                    MergeEvaluatedItemNoLock(item);
                }

                // Prune stale open reports for targets on this console that no longer exist on disk
                var staleIds = _reportsById.Values
                    .Where(r => string.Equals(r.ConsoleName, consoleName, StringComparison.OrdinalIgnoreCase) &&
                                !File.Exists(r.TargetFullPath) && !Directory.Exists(r.TargetFullPath))
                    .Select(r => r.ReportId)
                    .ToList();

                foreach (string id in staleIds)
                    _reportsById.Remove(id);

                Save();
            }
            ReportsChanged?.Invoke();
        }

        public void UpsertHashVerificationFinding(
            string consoleOrLabel,
            string targetFullPath,
            bool isDirectory,
            string issueSummary,
            ComplianceSeverity severity = ComplianceSeverity.Warning)
        {
            if (string.IsNullOrWhiteSpace(targetFullPath))
                return;

            string id = TargetComplianceEvaluation.BuildReportId(consoleOrLabel, targetFullPath);
            string fileName = isDirectory ? new DirectoryInfo(targetFullPath).Name : Path.GetFileName(targetFullPath);

            var item = new ComplianceReportItem
            {
                ReportId = id,
                ConsoleName = string.IsNullOrWhiteSpace(consoleOrLabel) ? "Hash Verifier" : consoleOrLabel,
                TargetFileName = fileName,
                TargetFullPath = targetFullPath,
                IsDirectory = isDirectory,
                TemplateId = "hash-sidecar-audit",
                TemplateDisplayName = ".hash Integrity Audit",
                Severity = severity,
                ResolutionStatus = ComplianceReportResolutionStatus.Open,
                SummaryCode = "WARN: HashMismatch",
                SummaryText = issueSummary,
                PrimaryCategory = "Hash Integrity",
                IssueCount = 1,
                ExtraDeletableCount = 0,
                FirstDetectedUtc = DateTime.UtcNow,
                LastEvaluatedUtc = DateTime.UtcNow,
                Issues = new List<ComplianceIssueDetail>
                {
                    new()
                    {
                        Kind = ComplianceIssueKind.SidecarHashIntegrityIssue,
                        Severity = severity,
                        InternalPath = fileName,
                        Message = issueSummary
                    }
                }
            };

            UpsertSingleTargetResult(item);
        }

        public void MarkIgnored(IEnumerable<string> reportIds, bool ignored = true)
        {
            lock (_syncLock)
            {
                foreach (string id in reportIds)
                {
                    if (_reportsById.TryGetValue(id, out var existing))
                    {
                        existing.ResolutionStatus = ignored
                            ? ComplianceReportResolutionStatus.Ignored
                            : ComplianceReportResolutionStatus.Open;
                    }
                }
                Save();
            }
            ReportsChanged?.Invoke();
        }

        public void MarkResolved(IEnumerable<string> reportIds)
        {
            lock (_syncLock)
            {
                foreach (string id in reportIds)
                {
                    if (_reportsById.TryGetValue(id, out var existing))
                    {
                        existing.ResolutionStatus = ComplianceReportResolutionStatus.Resolved;
                        existing.LastEvaluatedUtc = DateTime.UtcNow;
                    }
                }
                Save();
            }
            ReportsChanged?.Invoke();
        }

        public int ClearResolvedReports()
        {
            int removed = 0;
            lock (_syncLock)
            {
                var ids = _reportsById.Values
                    .Where(r => r.ResolutionStatus == ComplianceReportResolutionStatus.Resolved)
                    .Select(r => r.ReportId)
                    .ToList();

                foreach (string id in ids)
                {
                    _reportsById.Remove(id);
                    removed++;
                }

                if (removed > 0)
                    Save();
            }

            if (removed > 0)
                ReportsChanged?.Invoke();

            return removed;
        }

        public void ExportToCsv(string csvPath, IEnumerable<ComplianceReportItem>? subset = null)
        {
            var rows = (subset ?? GetAllReports()).ToList();
            var sb = new StringBuilder();
            sb.AppendLine("Console,Archive / Folder,Severity,Status,Category,Summary Code,Template,Issue Count,Removable Extra Files,Details,Full Path,Last Evaluated");
            foreach (var r in rows)
            {
                sb.AppendLine(string.Join(",",
                    EscapeCsv(r.ConsoleName),
                    EscapeCsv(r.TargetFileName),
                    EscapeCsv(r.SeverityDisplay),
                    EscapeCsv(r.StatusDisplay),
                    EscapeCsv(r.PrimaryCategory),
                    EscapeCsv(r.SummaryCode),
                    EscapeCsv(r.TemplateDisplayName),
                    r.IssueCount.ToString(),
                    r.ExtraDeletableCount.ToString(),
                    EscapeCsv(r.SummaryText),
                    EscapeCsv(r.TargetFullPath),
                    EscapeCsv(r.LastEvaluatedDisplay)));
            }
            ConfigService.AtomicWriteAllText(csvPath, sb.ToString());
        }

        private void MergeEvaluatedItemNoLock(ComplianceReportItem evaluated)
        {
            bool isClean = evaluated.IssueCount == 0 ||
                           string.Equals(evaluated.SummaryCode, "CLEAN", StringComparison.OrdinalIgnoreCase);

            if (isClean)
            {
                if (_reportsById.TryGetValue(evaluated.ReportId, out var prev))
                {
                    prev.ResolutionStatus = ComplianceReportResolutionStatus.Resolved;
                    prev.Severity = ComplianceSeverity.Clean;
                    prev.SummaryCode = "CLEAN";
                    prev.SummaryText = "Resolved — archive now passes template compliance.";
                    prev.IssueCount = 0;
                    prev.ExtraDeletableCount = 0;
                    prev.Issues.Clear();
                    prev.EntryNodes = evaluated.EntryNodes;
                    prev.LastEvaluatedUtc = DateTime.UtcNow;
                }
                return;
            }

            if (_reportsById.TryGetValue(evaluated.ReportId, out var existing))
            {
                evaluated.FirstDetectedUtc = existing.FirstDetectedUtc;
                if (existing.ResolutionStatus == ComplianceReportResolutionStatus.Ignored)
                {
                    evaluated.ResolutionStatus = ComplianceReportResolutionStatus.Ignored;
                }
                else
                {
                    evaluated.ResolutionStatus = ComplianceReportResolutionStatus.Open;
                }
            }

            _reportsById[evaluated.ReportId] = evaluated;
        }

        private static string EscapeCsv(string? val)
        {
            if (string.IsNullOrEmpty(val))
                return "";
            if (val.Contains(',') || val.Contains('"') || val.Contains('\n') || val.Contains('\r'))
                return "\"" + val.Replace("\"", "\"\"") + "\"";
            return val;
        }
    }
}
