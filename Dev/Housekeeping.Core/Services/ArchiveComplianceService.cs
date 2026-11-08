using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Models;
using ArchiveEntryInfo = Housekeeping.Services.HashSidecarService.ArchiveEntryInfo;

namespace Housekeeping.Services
{
    public class TargetComplianceEvaluation
    {
        public bool IsEvaluated { get; set; }
        public bool IsClean => !IsEvaluated || Issues.Count == 0;
        public string SummaryCode { get; set; } = "CLEAN";
        public string SummaryText { get; set; } = "Compliant with archive template.";
        public ComplianceSeverity MaxSeverity { get; set; } = ComplianceSeverity.Clean;
        public ArchiveComplianceTemplate? Template { get; set; }
        public List<ComplianceIssueDetail> Issues { get; set; } = new();
        public List<ArchiveEntryPreviewNode> EntryNodes { get; set; } = new();
        public ComplianceReportItem? ToReportItem(string consoleName, string targetFullPath, bool isDirectory)
        {
            if (!IsEvaluated)
                return null;

            string fileName = isDirectory
                ? new DirectoryInfo(targetFullPath).Name
                : Path.GetFileName(targetFullPath);

            int deletableCount = EntryNodes.Count(n => n.CanClean);

            return new ComplianceReportItem
            {
                ReportId = BuildReportId(consoleName, targetFullPath),
                ConsoleName = consoleName ?? "",
                TargetFileName = fileName,
                TargetFullPath = targetFullPath,
                IsDirectory = isDirectory,
                TemplateId = Template?.TemplateId ?? "auto-community",
                TemplateDisplayName = Template?.DisplayName ?? "Community: Console Rule Driven (Auto)",
                Severity = MaxSeverity,
                ResolutionStatus = Issues.Count == 0
                    ? ComplianceReportResolutionStatus.Resolved
                    : ComplianceReportResolutionStatus.Open,
                SummaryCode = SummaryCode,
                SummaryText = SummaryText,
                PrimaryCategory = DeterminePrimaryCategory(Issues),
                IssueCount = Issues.Count,
                ExtraDeletableCount = deletableCount,
                FirstDetectedUtc = DateTime.UtcNow,
                LastEvaluatedUtc = DateTime.UtcNow,
                Issues = new List<ComplianceIssueDetail>(Issues),
                EntryNodes = new List<ArchiveEntryPreviewNode>(EntryNodes)
            };
        }

        public static string BuildReportId(string? consoleName, string targetFullPath)
        {
            string normPath = Path.GetFullPath(targetFullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
            return $"{(consoleName ?? "").Trim().ToLowerInvariant()}|{normPath}";
        }

        public static string DeterminePrimaryCategory(IReadOnlyList<ComplianceIssueDetail> issues)
        {
            if (issues == null || issues.Count == 0)
                return "Clean";

            var primary = issues
                .OrderByDescending(i => (int)i.Severity)
                .First();

            return primary.Kind switch
            {
                ComplianceIssueKind.ExtraNonRuleFile or ComplianceIssueKind.Ps3UpdatePupBloat => "Extra Files",
                ComplianceIssueKind.MissingCompanionDescriptor or
                ComplianceIssueKind.MissingReferencedTrack or
                ComplianceIssueKind.OrphanUnreferencedTrack or
                ComplianceIssueKind.MultiDiscSequenceGap or
                ComplianceIssueKind.MissingFolderBootAnchor or
                ComplianceIssueKind.MissingPayloadRom => "Missing / Broken Refs",
                ComplianceIssueKind.WrongInnerPayloadFormat or
                ComplianceIssueKind.WrongOuterContainerFormat => "Wrong Format",
                ComplianceIssueKind.SidecarHashIntegrityIssue => "Hash Integrity",
                _ => "Multi-ROM / Structure"
            };
        }
    }

    public class ArchiveCleanupDryRunResult
    {
        public string ArchivePath { get; set; } = "";
        public bool IsAllowed { get; set; }
        public string CommandPreview { get; set; } = "";
        public string BlockReason { get; set; } = "";
        public List<ArchiveEntryPreviewNode> FilesToDelete { get; set; } = new();
        public List<ArchiveEntryPreviewNode> ProtectedFilesKept { get; set; } = new();
        public long ReclaimedBytes => FilesToDelete.Sum(f => f.SizeBytes);
    }

    public static class ArchiveComplianceService
    {
        private static readonly HashSet<string> KnownGamePayloadExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".gb", ".gbc", ".gba", ".nes", ".fds", ".qd", ".sfc", ".smc", ".z64", ".n64", ".v64", ".ndd",
            ".nds", ".dsi", ".3ds", ".cia", ".cci", ".cxi", ".md", ".gen", ".smd", ".32x", ".sms", ".gg",
            ".sg", ".pce", ".sgx", ".ngp", ".ngc", ".ws", ".wsc", ".a26", ".a52", ".a78", ".lnx", ".j64",
            ".jag", ".vb", ".vec", ".col", ".int", ".chd", ".rvz", ".gcz", ".wia", ".wbfs", ".ciso", ".cso",
            ".dax", ".pbp", ".iso", ".gcm", ".xiso", ".cdi", ".mdf", ".mds", ".nrg", ".cue", ".gdi", ".bin",
            ".raw", ".img", ".rpx", ".wud", ".wux", ".wad", ".dol", ".elf", ".vpk", ".nsp", ".xci"
        };

        private static readonly HashSet<string> NestedArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".7z", ".zip", ".rar", ".ecm", ".001"
        };

        private static readonly Regex CueFileRegex = new(
            @"^\s*FILE\s+(?:""([^""]+)""|(\S+))\s+",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex GdiTrackRegex = new(
            @"^\s*\d+\s+\d+\s+\d+\s+\d+\s+(?:""([^""]+)""|(\S+))",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex DiscTagRegex = new(
            @"[\(\[\s._-](?:Disc|Disk|CD)\s*0*(\d+)(?:[\)\]\s._-]|$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static HashSet<string> ParseExtensionSet(string? csv)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(csv))
                return set;

            foreach (var part in csv.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string ext = part.StartsWith('.') ? part : "." + part;
                if (ext.Length > 1)
                    set.Add(ext);
            }
            return set;
        }

        public static async Task<TargetComplianceEvaluation> EvaluateTargetComplianceAsync(
            string targetFullPath,
            bool isDirectory,
            ConsoleCollectionRule? col,
            RomLauncherGeneratorConfig cfg,
            string sevenZipExe,
            IReadOnlyList<ArchiveEntryInfo>? preListedArchiveEntries = null,
            CancellationToken ct = default)
        {
            var eval = new TargetComplianceEvaluation();
            if (cfg == null || !cfg.EnableArchiveTemplateComplianceScan)
                return eval;

            cfg.EnsureArchiveTemplatesInitialized();
            var template = ArchiveComplianceCatalog.ResolveTemplateForConsole(col?.ComplianceTemplateId, col, cfg.ArchiveTemplates);
            if (template == null)
                return eval;

            eval.IsEvaluated = true;
            eval.Template = template;

            var allowedPayloads = ResolveAllowedPayloadExtensions(template, col);
            var allowedCompanions = ParseExtensionSet(template.AllowedCompanionExtensionsCsv);
            allowedCompanions.Add(".hash"); // Sidecar .hash files are always protected companions
            var allowedOuterContainers = ParseExtensionSet(template.AllowedOuterContainerExtensionsCsv);

            if (isDirectory)
            {
                EvaluateUnpackedDirectory(targetFullPath, col, template, allowedPayloads, allowedCompanions, eval, ct);
                FinalizeEvaluationSummary(eval);
                return eval;
            }

            if (!File.Exists(targetFullPath))
            {
                eval.IsEvaluated = false;
                return eval;
            }

            bool isArchive = HashSidecarService.IsArchiveFilePath(targetFullPath);
            if (!isArchive)
            {
                EvaluateLooseFile(targetFullPath, template, allowedPayloads, allowedOuterContainers, eval);
                FinalizeEvaluationSummary(eval);
                return eval;
            }

            // 1. Validate Outer Archive Container Format
            string outerExt = Path.GetExtension(targetFullPath);
            if (allowedOuterContainers.Count > 0 && !allowedOuterContainers.Contains(outerExt))
            {
                eval.Issues.Add(new ComplianceIssueDetail
                {
                    Kind = ComplianceIssueKind.WrongOuterContainerFormat,
                    Severity = ComplianceSeverity.Warning,
                    InternalPath = Path.GetFileName(targetFullPath),
                    Message = $"Outer archive container '{outerExt}' is not in template's allowed outer formats ({string.Join(", ", allowedOuterContainers)})."
                });
            }

            bool isWritableArchive = HashSidecarService.IsWritableArchiveFilePath(targetFullPath);
            var entries = preListedArchiveEntries?.ToList()
                ?? await HashSidecarService.ListArchiveContentsAsync(sevenZipExe, targetFullPath, ct);

            var fileEntries = entries
                .Where(e => !e.IsDirectory && !string.IsNullOrWhiteSpace(e.Path))
                .ToList();

            if (fileEntries.Count == 0)
            {
                eval.Issues.Add(new ComplianceIssueDetail
                {
                    Kind = ComplianceIssueKind.MissingPayloadRom,
                    Severity = ComplianceSeverity.Error,
                    InternalPath = "",
                    Message = "Archive contains no readable files (empty or unreadable header)."
                });
                FinalizeEvaluationSummary(eval);
                return eval;
            }

            // 2. Check Wrapper Folder Depth
            CheckArchiveWrapperDepth(fileEntries, template, eval);

            // 3. Parse In-RAM .cue / .gdi / .m3u Descriptors (< 64 KB) for Track & Playlist Validation
            var descriptorReferencedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var descriptorParentAllowed = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var entryLookupByNormPath = fileEntries
                .GroupBy(e => NormalizeInternalPath(e.Path), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var descriptorEntries = fileEntries
                .Where(e =>
                {
                    string ext = Path.GetExtension(e.Path);
                    return ext.Equals(".cue", StringComparison.OrdinalIgnoreCase) ||
                           ext.Equals(".gdi", StringComparison.OrdinalIgnoreCase) ||
                           ext.Equals(".m3u", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            foreach (var desc in descriptorEntries)
            {
                string descExt = Path.GetExtension(desc.Path);
                bool descAllowedAsPayloadOrCompanion = allowedPayloads.Contains(descExt) || allowedCompanions.Contains(descExt);

                if (desc.Size <= 65536)
                {
                    string? content = await HashSidecarService.ReadSmallArchiveTextEntryAsync(
                        sevenZipExe, targetFullPath, desc.Path, 65536, ct);

                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        var refs = ExtractDescriptorReferences(descExt, content);
                        string descDir = GetInternalDirectory(desc.Path);

                        foreach (string rawRef in refs)
                        {
                            string resolvedInternal = ResolveRelativeInternalPath(descDir, rawRef);
                            ArchiveEntryInfo? matchedTarget = null;
                            if (entryLookupByNormPath.TryGetValue(resolvedInternal, out var exactMatch))
                            {
                                matchedTarget = exactMatch;
                            }
                            else
                            {
                                string refFileOnly = Path.GetFileName(rawRef.Replace('\\', '/'));
                                matchedTarget = fileEntries.FirstOrDefault(fe =>
                                    string.Equals(Path.GetFileName(fe.Path.Replace('\\', '/')), refFileOnly, StringComparison.OrdinalIgnoreCase));
                            }

                            if (matchedTarget != null)
                            {
                                string normMatched = NormalizeInternalPath(matchedTarget.Path);
                                descriptorReferencedPaths.Add(normMatched);
                                bool parentIsPayload = allowedPayloads.Contains(descExt) ||
                                    (descExt.Equals(".m3u", StringComparison.OrdinalIgnoreCase) && allowedPayloads.Contains(Path.GetExtension(matchedTarget.Path)));
                                descriptorParentAllowed[normMatched] = parentIsPayload;
                            }
                            else if (template.ValidateCueGdiM3uReferences)
                            {
                                // For .m3u inside single-disc archives, check sibling archives on disk before flagging missing
                                bool foundBesideArchive = false;
                                if (descExt.Equals(".m3u", StringComparison.OrdinalIgnoreCase))
                                {
                                    string? archiveDir = Path.GetDirectoryName(targetFullPath);
                                    if (!string.IsNullOrEmpty(archiveDir))
                                    {
                                        string siblingCandidate = Path.Combine(archiveDir, rawRef.Trim());
                                        foundBesideArchive = File.Exists(siblingCandidate) ||
                                            File.Exists(Path.ChangeExtension(siblingCandidate, ".7z")) ||
                                            File.Exists(Path.ChangeExtension(siblingCandidate, ".zip"));
                                    }
                                }

                                if (!foundBesideArchive)
                                {
                                    eval.Issues.Add(new ComplianceIssueDetail
                                    {
                                        Kind = ComplianceIssueKind.MissingReferencedTrack,
                                        Severity = ComplianceSeverity.Error,
                                        InternalPath = desc.Path,
                                        SizeBytes = desc.Size,
                                        Crc32 = desc.Crc,
                                        Message = $"Descriptor '{Path.GetFileName(desc.Path)}' references missing file '{rawRef}'."
                                    });
                                }
                            }
                        }
                    }
                }
            }

            bool hasCueOrGdi = descriptorEntries.Any(d =>
                d.Path.EndsWith(".cue", StringComparison.OrdinalIgnoreCase) ||
                d.Path.EndsWith(".gdi", StringComparison.OrdinalIgnoreCase));
            bool hasM3u = descriptorEntries.Any(d =>
                d.Path.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase));
            bool treatsRawBinAsOpticalTrack = ShouldTreatRawBinAsOpticalTrack(template, col, allowedPayloads, fileEntries);

            var primaryPayloadEntries = new List<ArchiveEntryInfo>();

            // 4. Classify Every Inner Entry
            foreach (var entry in fileEntries)
            {
                string normPath = NormalizeInternalPath(entry.Path);
                string ext = Path.GetExtension(entry.Path);
                string fileName = Path.GetFileName(entry.Path);

                // Sidecar .hash packaged inside archive
                if (ext.Equals(".hash", StringComparison.OrdinalIgnoreCase))
                {
                    eval.EntryNodes.Add(new ArchiveEntryPreviewNode
                    {
                        InternalPath = entry.Path,
                        SizeBytes = entry.Size,
                        Crc32 = entry.Crc,
                        RoleClassification = "Allowed Companion (.hash)",
                        StatusBadge = "OK",
                        CanClean = false
                    });
                    continue;
                }

                // Nested archive check
                if (template.FlagNestedArchives && NestedArchiveExtensions.Contains(ext))
                {
                    eval.Issues.Add(new ComplianceIssueDetail
                    {
                        Kind = ComplianceIssueKind.NestedArchiveInsideArchive,
                        Severity = ComplianceSeverity.Warning,
                        InternalPath = entry.Path,
                        SizeBytes = entry.Size,
                        Crc32 = entry.Crc,
                        Message = $"Nested compressed file '{entry.Path}' found inside archive."
                    });
                    eval.EntryNodes.Add(new ArchiveEntryPreviewNode
                    {
                        InternalPath = entry.Path,
                        SizeBytes = entry.Size,
                        Crc32 = entry.Crc,
                        RoleClassification = "Nested Archive",
                        StatusBadge = "WARN",
                        CanClean = false
                    });
                    continue;
                }

                // PS3_UPDATE/PS3UPDAT.PUP bloat check
                if (template.FlagPs3UpdatePupBloat &&
                    normPath.EndsWith("PS3_UPDATE/PS3UPDAT.PUP", StringComparison.OrdinalIgnoreCase))
                {
                    eval.Issues.Add(new ComplianceIssueDetail
                    {
                        Kind = ComplianceIssueKind.Ps3UpdatePupBloat,
                        Severity = ComplianceSeverity.Warning,
                        InternalPath = entry.Path,
                        SizeBytes = entry.Size,
                        Crc32 = entry.Crc,
                        IsSafeExtraFileDeletable = isWritableArchive,
                        Message = $"PS3 firmware update partition bloat '{entry.Path}' ({entry.Size / (1024 * 1024)} MB)."
                    });
                    eval.EntryNodes.Add(new ArchiveEntryPreviewNode
                    {
                        InternalPath = entry.Path,
                        SizeBytes = entry.Size,
                        Crc32 = entry.Crc,
                        RoleClassification = "Extra Non-Rule File",
                        StatusBadge = "EXTRA",
                        CanClean = isWritableArchive,
                        SelectedForCleanup = isWritableArchive
                    });
                    continue;
                }

                // Descriptor-referenced track (.bin / .raw / .chd / .rvz referenced by .cue / .gdi / .m3u)
                if (descriptorReferencedPaths.Contains(normPath))
                {
                    bool parentAllowed = descriptorParentAllowed.GetValueOrDefault(normPath, true);
                    if (parentAllowed)
                    {
                        eval.EntryNodes.Add(new ArchiveEntryPreviewNode
                        {
                            InternalPath = entry.Path,
                            SizeBytes = entry.Size,
                            Crc32 = entry.Crc,
                            RoleClassification = "Referenced Track",
                            StatusBadge = "OK",
                            CanClean = false
                        });
                    }
                    else
                    {
                        eval.Issues.Add(new ComplianceIssueDetail
                        {
                            Kind = ComplianceIssueKind.WrongInnerPayloadFormat,
                            Severity = ComplianceSeverity.Error,
                            InternalPath = entry.Path,
                            SizeBytes = entry.Size,
                            Crc32 = entry.Crc,
                            Message = $"Track '{fileName}' uses format '{ext}' instead of template's allowed formats ({FormatAllowedList(allowedPayloads)})."
                        });
                        eval.EntryNodes.Add(new ArchiveEntryPreviewNode
                        {
                            InternalPath = entry.Path,
                            SizeBytes = entry.Size,
                            Crc32 = entry.Crc,
                            RoleClassification = "Wrong Format Payload",
                            StatusBadge = "ERROR",
                            CanClean = false
                        });
                    }
                    continue;
                }

                // Unreferenced .bin / .raw optical track check
                if ((ext.Equals(".bin", StringComparison.OrdinalIgnoreCase) || ext.Equals(".raw", StringComparison.OrdinalIgnoreCase)) &&
                    treatsRawBinAsOpticalTrack)
                {
                    if (hasCueOrGdi && template.FlagOrphanTrackBins)
                    {
                        eval.Issues.Add(new ComplianceIssueDetail
                        {
                            Kind = ComplianceIssueKind.OrphanUnreferencedTrack,
                            Severity = ComplianceSeverity.Warning,
                            InternalPath = entry.Path,
                            SizeBytes = entry.Size,
                            Crc32 = entry.Crc,
                            Message = $"Track '{fileName}' is present in the archive but not referenced by any .cue/.gdi descriptor."
                        });
                        eval.EntryNodes.Add(new ArchiveEntryPreviewNode
                        {
                            InternalPath = entry.Path,
                            SizeBytes = entry.Size,
                            Crc32 = entry.Crc,
                            RoleClassification = "Orphan Track",
                            StatusBadge = "WARN",
                            CanClean = false
                        });
                        continue;
                    }

                    if (!hasCueOrGdi)
                    {
                        bool cueAllowed = allowedPayloads.Contains(".cue") || allowedCompanions.Contains(".cue") ||
                                          allowedPayloads.Contains(".gdi") || allowedCompanions.Contains(".gdi");

                        if (!cueAllowed && !allowedPayloads.Contains(ext))
                        {
                            eval.Issues.Add(new ComplianceIssueDetail
                            {
                                Kind = ComplianceIssueKind.WrongInnerPayloadFormat,
                                Severity = ComplianceSeverity.Error,
                                InternalPath = entry.Path,
                                SizeBytes = entry.Size,
                                Crc32 = entry.Crc,
                                Message = $"Raw track '{fileName}' is not in allowed payload formats ({FormatAllowedList(allowedPayloads)}) and has no .cue/.gdi."
                            });
                            eval.EntryNodes.Add(new ArchiveEntryPreviewNode
                            {
                                InternalPath = entry.Path,
                                SizeBytes = entry.Size,
                                Crc32 = entry.Crc,
                                RoleClassification = "Wrong Format Payload",
                                StatusBadge = "ERROR",
                                CanClean = false
                            });
                        }
                        else if (template.RequireCueOrGdiForRawBinTracks)
                        {
                            primaryPayloadEntries.Add(entry);
                            eval.Issues.Add(new ComplianceIssueDetail
                            {
                                Kind = ComplianceIssueKind.MissingCompanionDescriptor,
                                Severity = ComplianceSeverity.Error,
                                InternalPath = entry.Path,
                                SizeBytes = entry.Size,
                                Crc32 = entry.Crc,
                                Message = $"Raw disc track '{fileName}' is missing its required companion .cue or .gdi descriptor."
                            });
                            eval.EntryNodes.Add(new ArchiveEntryPreviewNode
                            {
                                InternalPath = entry.Path,
                                SizeBytes = entry.Size,
                                Crc32 = entry.Crc,
                                RoleClassification = "Primary Payload (Missing .cue)",
                                StatusBadge = "ERROR",
                                CanClean = false
                            });
                        }
                        continue;
                    }
                }

                // Descriptor files (.cue / .gdi / .m3u)
                if (ext.Equals(".cue", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".gdi", StringComparison.OrdinalIgnoreCase))
                {
                    bool cueFormatAllowed = allowedPayloads.Contains(ext) ||
                        (allowedCompanions.Contains(ext) && (allowedPayloads.Contains(".bin") || allowedPayloads.Contains(".iso") || template.UseConsoleRuleExtensionsAsPayloads));

                    // Note: If template is e.g. community-modern-disc (.chd/.rvz only), .cue is neither in allowedPayloads nor allowedCompanions -> flagged WrongInnerPayloadFormat!
                    if (cueFormatAllowed)
                    {
                        primaryPayloadEntries.Add(entry);
                        eval.EntryNodes.Add(new ArchiveEntryPreviewNode
                        {
                            InternalPath = entry.Path,
                            SizeBytes = entry.Size,
                            Crc32 = entry.Crc,
                            RoleClassification = $"Descriptor ({ext})",
                            StatusBadge = "OK",
                            CanClean = false
                        });
                    }
                    else
                    {
                        eval.Issues.Add(new ComplianceIssueDetail
                        {
                            Kind = ComplianceIssueKind.WrongInnerPayloadFormat,
                            Severity = ComplianceSeverity.Error,
                            InternalPath = entry.Path,
                            SizeBytes = entry.Size,
                            Crc32 = entry.Crc,
                            Message = $"Disc descriptor '{fileName}' does not match template's required format ({FormatAllowedList(allowedPayloads)})."
                        });
                        eval.EntryNodes.Add(new ArchiveEntryPreviewNode
                        {
                            InternalPath = entry.Path,
                            SizeBytes = entry.Size,
                            Crc32 = entry.Crc,
                            RoleClassification = "Wrong Format Payload",
                            StatusBadge = "ERROR",
                            CanClean = false
                        });
                    }
                    continue;
                }

                if (ext.Equals(".m3u", StringComparison.OrdinalIgnoreCase))
                {
                    if (allowedCompanions.Contains(".m3u") || allowedPayloads.Contains(".m3u"))
                    {
                        eval.EntryNodes.Add(new ArchiveEntryPreviewNode
                        {
                            InternalPath = entry.Path,
                            SizeBytes = entry.Size,
                            Crc32 = entry.Crc,
                            RoleClassification = "Descriptor (.m3u)",
                            StatusBadge = "OK",
                            CanClean = false
                        });
                    }
                    else
                    {
                        eval.Issues.Add(new ComplianceIssueDetail
                        {
                            Kind = ComplianceIssueKind.ExtraNonRuleFile,
                            Severity = ComplianceSeverity.Warning,
                            InternalPath = entry.Path,
                            SizeBytes = entry.Size,
                            Crc32 = entry.Crc,
                            IsSafeExtraFileDeletable = isWritableArchive,
                            Message = $"Playlist '{fileName}' is not permitted by strict template '{template.DisplayName}'."
                        });
                        eval.EntryNodes.Add(new ArchiveEntryPreviewNode
                        {
                            InternalPath = entry.Path,
                            SizeBytes = entry.Size,
                            Crc32 = entry.Crc,
                            RoleClassification = "Extra Non-Rule File",
                            StatusBadge = "EXTRA",
                            CanClean = isWritableArchive,
                            SelectedForCleanup = isWritableArchive
                        });
                    }
                    continue;
                }

                // Allowed Primary Payload
                if (allowedPayloads.Contains(ext))
                {
                    primaryPayloadEntries.Add(entry);
                    eval.EntryNodes.Add(new ArchiveEntryPreviewNode
                    {
                        InternalPath = entry.Path,
                        SizeBytes = entry.Size,
                        Crc32 = entry.Crc,
                        RoleClassification = "Primary Payload",
                        StatusBadge = "OK",
                        CanClean = false
                    });
                    continue;
                }

                // Allowed Companion (e.g., .sbi)
                if (allowedCompanions.Contains(ext))
                {
                    eval.EntryNodes.Add(new ArchiveEntryPreviewNode
                    {
                        InternalPath = entry.Path,
                        SizeBytes = entry.Size,
                        Crc32 = entry.Crc,
                        RoleClassification = "Allowed Companion",
                        StatusBadge = "OK",
                        CanClean = false
                    });
                    continue;
                }

                // Known ROM / Disc format that violates the active template's allowed payload formats
                if (KnownGamePayloadExtensions.Contains(ext))
                {
                    eval.Issues.Add(new ComplianceIssueDetail
                    {
                        Kind = ComplianceIssueKind.WrongInnerPayloadFormat,
                        Severity = ComplianceSeverity.Error,
                        InternalPath = entry.Path,
                        SizeBytes = entry.Size,
                        Crc32 = entry.Crc,
                        Message = $"Inner ROM/disc '{fileName}' has format '{ext}', expected ({FormatAllowedList(allowedPayloads)})."
                    });
                    eval.EntryNodes.Add(new ArchiveEntryPreviewNode
                    {
                        InternalPath = entry.Path,
                        SizeBytes = entry.Size,
                        Crc32 = entry.Crc,
                        RoleClassification = "Wrong Format Payload",
                        StatusBadge = "ERROR",
                        CanClean = false
                    });
                    continue;
                }

                // Extra non-rule clutter file (.nfo, .txt, .url, .jpg, .sav, .ips, .sfv, etc.)
                eval.Issues.Add(new ComplianceIssueDetail
                {
                    Kind = ComplianceIssueKind.ExtraNonRuleFile,
                    Severity = ComplianceSeverity.Warning,
                    InternalPath = entry.Path,
                    SizeBytes = entry.Size,
                    Crc32 = entry.Crc,
                    IsSafeExtraFileDeletable = isWritableArchive,
                    Message = $"Extra non-rule file '{entry.Path}' ({FormatBytesShort(entry.Size)}) is not part of template '{template.DisplayName}'."
                });
                eval.EntryNodes.Add(new ArchiveEntryPreviewNode
                {
                    InternalPath = entry.Path,
                    SizeBytes = entry.Size,
                    Crc32 = entry.Crc,
                    RoleClassification = "Extra Non-Rule File",
                    StatusBadge = "EXTRA",
                    CanClean = isWritableArchive,
                    SelectedForCleanup = isWritableArchive
                });
            }

            // 5. Missing Primary Payload Check
            bool hasAnyWrongFormatPayload = eval.Issues.Any(i => i.Kind == ComplianceIssueKind.WrongInnerPayloadFormat);
            bool hasNestedArchive = eval.Issues.Any(i => i.Kind == ComplianceIssueKind.NestedArchiveInsideArchive);
            if (primaryPayloadEntries.Count == 0 && !hasAnyWrongFormatPayload && !hasNestedArchive)
            {
                eval.Issues.Add(new ComplianceIssueDetail
                {
                    Kind = ComplianceIssueKind.MissingPayloadRom,
                    Severity = ComplianceSeverity.Error,
                    InternalPath = "",
                    Message = $"Archive contains zero matching ROM/disc payload files for ({FormatAllowedList(allowedPayloads)})."
                });
            }

            // 6. Multi-ROM Pack & Intra-Archive Multi-Disc Sequence Continuity Check
            EvaluatePrimaryCountAndIntraArchiveDiscs(primaryPayloadEntries, hasM3u, template, eval);

            FinalizeEvaluationSummary(eval);
            return eval;
        }

        /// <summary>
        /// Fast zero-decompression compliance scan across all enabled (or filtered) console collections,
        /// including cross-archive multi-disc sequence continuity (Disc 1..N across sibling archives).
        /// </summary>
        public static async Task<List<ComplianceReportItem>> ScanConsoleCollectionAsync(
            ConsoleCollectionRule col,
            RomLauncherGeneratorConfig cfg,
            string sevenZipExe,
            ComplianceReportService? reportStore = null,
            Action<string>? log = null,
            CancellationToken ct = default,
            Action<int, int, int>? onStatsDelta = null)
        {
            var reports = new List<ComplianceReportItem>();
            if (col == null || string.IsNullOrWhiteSpace(col.ArchivesPath) || !Directory.Exists(col.ArchivesPath))
                return reports;

            cfg.EnsureArchiveTemplatesInitialized();
            var template = ArchiveComplianceCatalog.ResolveTemplateForConsole(col.ComplianceTemplateId, col, cfg.ArchiveTemplates);
            if (template == null)
                return reports;

            var dirInfo = new DirectoryInfo(col.ArchivesPath);
            var topFiles = dirInfo.GetFiles("*", SearchOption.TopDirectoryOnly)
                .Where(f => !f.Name.EndsWith(".hash", StringComparison.OrdinalIgnoreCase) &&
                            !f.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) &&
                            !f.Name.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) &&
                            !f.Name.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) &&
                            !f.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                            !f.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase) &&
                            !f.Name.Contains(".partial.", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var topDirs = dirInfo.GetDirectories("*", SearchOption.TopDirectoryOnly)
                .Where(d => !d.Name.StartsWith(".", StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var evaluatedByPath = new Dictionary<string, TargetComplianceEvaluation>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in topFiles)
            {
                ct.ThrowIfCancellationRequested();
                var eval = await EvaluateTargetComplianceAsync(file.FullName, isDirectory: false, col, cfg, sevenZipExe, null, ct);
                if (eval.IsEvaluated)
                {
                    evaluatedByPath[file.FullName] = eval;
                    if (onStatsDelta != null)
                    {
                        if (eval.IsClean) onStatsDelta(0, 1, 0);
                        else if (eval.MaxSeverity == ComplianceSeverity.Error) onStatsDelta(0, 0, 1);
                        else onStatsDelta(1, 0, 0);
                    }
                }
            }

            foreach (var subDir in topDirs)
            {
                ct.ThrowIfCancellationRequested();
                var eval = await EvaluateTargetComplianceAsync(subDir.FullName, isDirectory: true, col, cfg, sevenZipExe, null, ct);
                if (eval.IsEvaluated)
                {
                    evaluatedByPath[subDir.FullName] = eval;
                    if (onStatsDelta != null)
                    {
                        if (eval.IsClean) onStatsDelta(0, 1, 0);
                        else if (eval.MaxSeverity == ComplianceSeverity.Error) onStatsDelta(0, 0, 1);
                        else onStatsDelta(1, 0, 0);
                    }
                }
            }

            // Cross-archive multi-disc sequence check (e.g., Disc 1.7z + Disc 3.7z missing Disc 2.7z)
            if (template.ValidateMultiDiscSequenceContinuity)
            {
                CheckSiblingMultiDiscContinuity(dirInfo, topFiles, evaluatedByPath, template);
            }

            var allTargetItems = new List<ComplianceReportItem>();
            foreach (var kvp in evaluatedByPath)
            {
                bool isDir = Directory.Exists(kvp.Key);
                var item = kvp.Value.ToReportItem(col.ConsoleName, kvp.Key, isDir);
                if (item != null)
                {
                    allTargetItems.Add(item);
                    if (!kvp.Value.IsClean)
                        reports.Add(item);
                }
            }

            reportStore?.UpsertConsoleScanResults(col.ConsoleName, allTargetItems);
            log?.Invoke($"[Compliance] '{col.ConsoleName}' ({template.DisplayName}): Scanned {allTargetItems.Count} target(s) -> {reports.Count} with findings, {allTargetItems.Count - reports.Count} CLEAN.");
            return reports;
        }

        public static ArchiveCleanupDryRunResult BuildCleanupDryRunPreview(
            ComplianceReportItem report,
            string sevenZipExe,
            IReadOnlyList<string>? explicitSelectedInternalPaths = null)
        {
            var preview = new ArchiveCleanupDryRunResult
            {
                ArchivePath = report?.TargetFullPath ?? ""
            };

            if (report == null || string.IsNullOrWhiteSpace(report.TargetFullPath) || report.IsDirectory)
            {
                preview.BlockReason = "Cleanup is only available for archive files (.7z / .zip).";
                return preview;
            }

            if (!File.Exists(report.TargetFullPath))
            {
                preview.BlockReason = $"Archive not found on disk: {report.TargetFullPath}";
                return preview;
            }

            if (!HashSidecarService.IsWritableArchiveFilePath(report.TargetFullPath))
            {
                preview.BlockReason = $"Archive container '{Path.GetExtension(report.TargetFullPath)}' is read-only for in-place 7z cleanup (only .7z and .zip are supported).";
                return preview;
            }

            HashSet<string>? requestedSet = explicitSelectedInternalPaths != null
                ? new HashSet<string>(explicitSelectedInternalPaths, StringComparer.OrdinalIgnoreCase)
                : null;

            foreach (var node in report.EntryNodes)
            {
                bool wantDelete = node.CanClean &&
                    !node.InternalPath.EndsWith(".hash", StringComparison.OrdinalIgnoreCase) &&
                    (requestedSet != null ? requestedSet.Contains(node.InternalPath) : (node.SelectedForCleanup || explicitSelectedInternalPaths == null));

                if (wantDelete)
                    preview.FilesToDelete.Add(node);
                else
                    preview.ProtectedFilesKept.Add(node);
            }

            if (preview.FilesToDelete.Count == 0)
            {
                preview.BlockReason = "No removable extra non-rule files are selected for cleanup.";
                return preview;
            }

            bool keepsPrimaryPayload = preview.ProtectedFilesKept.Any(n =>
                n.RoleClassification.StartsWith("Primary Payload", StringComparison.OrdinalIgnoreCase) ||
                n.RoleClassification.StartsWith("Descriptor", StringComparison.OrdinalIgnoreCase) ||
                n.RoleClassification.StartsWith("Referenced Track", StringComparison.OrdinalIgnoreCase));

            if (!keepsPrimaryPayload)
            {
                preview.FilesToDelete.Clear();
                preview.BlockReason = "Safety Guard Blocked Cleanup: Archive has no verified primary ROM/disc payload remaining; refusing to modify archive.";
                return preview;
            }

            string resolved7z = string.IsNullOrWhiteSpace(sevenZipExe) ? "7z.exe" : sevenZipExe;
            string quotedTargets = string.Join(" ", preview.FilesToDelete.Select(f => $"\"{f.InternalPath}\""));
            preview.CommandPreview = $"\"{resolved7z}\" d \"{report.TargetFullPath}\" {quotedTargets} -y";
            preview.IsAllowed = true;
            return preview;
        }

        public static async Task<(bool Success, string Message, TargetComplianceEvaluation? UpdatedEvaluation)> CleanExtraFilesFromArchiveAsync(
            ComplianceReportItem report,
            ConsoleCollectionRule? col,
            RomLauncherGeneratorConfig cfg,
            string sevenZipExe,
            ComplianceReportService? reportStore = null,
            IReadOnlyList<string>? explicitSelectedInternalPaths = null,
            CancellationToken ct = default)
        {
            if (report == null)
                return (false, "No compliance report item provided.", null);

            // Re-evaluate live archive header before modifying anything on disk
            var liveEval = await EvaluateTargetComplianceAsync(
                report.TargetFullPath,
                isDirectory: false,
                col,
                cfg,
                sevenZipExe,
                preListedArchiveEntries: null,
                ct);

            var liveReport = liveEval.ToReportItem(report.ConsoleName, report.TargetFullPath, isDirectory: false);
            if (liveReport == null)
                return (false, "Unable to inspect live archive before cleanup.", null);

            // Preserve user's checkbox selections from UI if provided, intersected with live CanClean safety check
            var selectedPaths = explicitSelectedInternalPaths ??
                report.EntryNodes.Where(n => n.CanClean && n.SelectedForCleanup).Select(n => n.InternalPath).ToList();

            if (selectedPaths.Count == 0)
            {
                selectedPaths = liveReport.EntryNodes.Where(n => n.CanClean).Select(n => n.InternalPath).ToList();
            }

            var dryRun = BuildCleanupDryRunPreview(liveReport, sevenZipExe, selectedPaths);
            if (!dryRun.IsAllowed)
                return (false, dryRun.BlockReason, liveEval);

            var safePaths = dryRun.FilesToDelete.Select(f => f.InternalPath).ToList();
            var (deletedOk, output) = await HashSidecarService.DeleteArchiveInternalPathsAsync(
                sevenZipExe,
                report.TargetFullPath,
                safePaths,
                ct);

            if (!deletedOk)
                return (false, $"7z d failed on '{report.TargetFileName}': {output}", liveEval);

            // Re-verify archive post-cleanup
            var postEval = await EvaluateTargetComplianceAsync(
                report.TargetFullPath,
                isDirectory: false,
                col,
                cfg,
                sevenZipExe,
                preListedArchiveEntries: null,
                ct);

            var postReport = postEval.ToReportItem(report.ConsoleName, report.TargetFullPath, isDirectory: false);
            if (postReport != null && reportStore != null)
            {
                reportStore.UpsertSingleTargetResult(postReport);
            }

            string summary = $"Removed {safePaths.Count} extra file(s) ({FormatBytesShort(dryRun.ReclaimedBytes)}) from '{report.TargetFileName}'. Post-cleanup status: {postEval.SummaryCode}.";
            return (true, summary, postEval);
        }

        private static HashSet<string> ResolveAllowedPayloadExtensions(ArchiveComplianceTemplate template, ConsoleCollectionRule? col)
        {
            if (template.UseConsoleRuleExtensionsAsPayloads && col != null && !string.IsNullOrWhiteSpace(col.ExtensionsCsv))
            {
                var set = ParseExtensionSet(col.ExtensionsCsv);
                if (set.Count > 0)
                    return set;
            }

            var explicitSet = ParseExtensionSet(template.AllowedPayloadExtensionsCsv);
            if (explicitSet.Count > 0)
                return explicitSet;

            if (col != null && !string.IsNullOrWhiteSpace(col.ExtensionsCsv))
                return ParseExtensionSet(col.ExtensionsCsv);

            return new HashSet<string>(KnownGamePayloadExtensions, StringComparer.OrdinalIgnoreCase);
        }

        private static bool ShouldTreatRawBinAsOpticalTrack(
            ArchiveComplianceTemplate template,
            ConsoleCollectionRule? col,
            HashSet<string> allowedPayloads,
            IReadOnlyList<ArchiveEntryInfo> fileEntries)
        {
            if (!template.RequireCueOrGdiForRawBinTracks)
                return false;

            if (template.TemplateId.Contains("redump-cd", StringComparison.OrdinalIgnoreCase) ||
                template.TemplateId.Contains("modern-disc", StringComparison.OrdinalIgnoreCase) ||
                template.TemplateId.Contains("redump-dvd", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (allowedPayloads.Contains(".cue") || allowedPayloads.Contains(".gdi") ||
                allowedPayloads.Contains(".chd") || allowedPayloads.Contains(".pbp") ||
                allowedPayloads.Contains(".iso") || allowedPayloads.Contains(".rvz"))
            {
                return true;
            }

            string consoleLower = ((col?.ConsoleName ?? "") + " " + (col?.SubFolder ?? "")).ToLowerInvariant();
            if (consoleLower.Contains("ps1") || consoleLower.Contains("psx") || consoleLower.Contains("playstation") ||
                consoleLower.Contains("saturn") || consoleLower.Contains("sega cd") || consoleLower.Contains("mega-cd") ||
                consoleLower.Contains("dreamcast") || consoleLower.Contains("pc engine cd") || consoleLower.Contains("turbografx-cd") ||
                consoleLower.Contains("3do") || consoleLower.Contains("neo geo cd"))
            {
                return true;
            }

            // Any .bin over 32 MB is an optical disc track rather than a cartridge ROM
            return fileEntries.Any(e =>
                e.Path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) &&
                e.Size > 32L * 1024 * 1024);
        }

        private static void CheckArchiveWrapperDepth(
            IReadOnlyList<ArchiveEntryInfo> fileEntries,
            ArchiveComplianceTemplate template,
            TargetComplianceEvaluation eval)
        {
            var nonHashPaths = fileEntries
                .Where(e => !e.Path.EndsWith(".hash", StringComparison.OrdinalIgnoreCase))
                .Select(e => NormalizeInternalPath(e.Path))
                .ToList();

            if (nonHashPaths.Count == 0)
                return;

            int maxDepth = nonHashPaths.Max(p => p.Count(c => c == '/'));
            if (maxDepth == 0)
                return;

            bool looksLikeStructuredFolderDump = nonHashPaths.Any(p =>
                p.Contains("PS3_GAME/", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("code/", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("meta/", StringComparison.OrdinalIgnoreCase));

            if (looksLikeStructuredFolderDump && template.AllowUnpackedFolderTargets)
                return;

            if (!template.AllowSingleWrapperFolder)
            {
                eval.Issues.Add(new ComplianceIssueDetail
                {
                    Kind = ComplianceIssueKind.DisallowedWrapperFolder,
                    Severity = ComplianceSeverity.Warning,
                    InternalPath = nonHashPaths.First(p => p.Contains('/')),
                    Message = $"Archive contains subfolder paths, violating flat-root template '{template.DisplayName}'."
                });
            }
            else if (maxDepth > 1)
            {
                eval.Issues.Add(new ComplianceIssueDetail
                {
                    Kind = ComplianceIssueKind.DisallowedWrapperFolder,
                    Severity = ComplianceSeverity.Warning,
                    InternalPath = nonHashPaths.First(p => p.Count(c => c == '/') > 1),
                    Message = $"Archive nests files {maxDepth} folder levels deep (maximum 1 wrapper folder allowed)."
                });
            }
        }

        private static void EvaluatePrimaryCountAndIntraArchiveDiscs(
            IReadOnlyList<ArchiveEntryInfo> primaryPayloadEntries,
            bool hasM3u,
            ArchiveComplianceTemplate template,
            TargetComplianceEvaluation eval)
        {
            if (primaryPayloadEntries.Count <= 1)
                return;

            // Check if all primary entries are Disc 1..N of the same game
            var discParsed = primaryPayloadEntries
                .Select(e => (Entry: e, Parsed: TryParseDiscNumber(Path.GetFileNameWithoutExtension(e.Path))))
                .ToList();

            bool allAreDiscsOfSameGame = discParsed.All(d => d.Parsed.HasDisc) &&
                discParsed.Select(d => d.Parsed.BaseTitle).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;

            if (allAreDiscsOfSameGame)
            {
                var discNums = discParsed.Select(d => d.Parsed.DiscNumber).Distinct().OrderBy(n => n).ToList();
                if (template.ValidateMultiDiscSequenceContinuity && discNums.Count > 0)
                {
                    int maxDisc = discNums.Max();
                    var missing = Enumerable.Range(1, maxDisc).Except(discNums).ToList();
                    if (missing.Count > 0)
                    {
                        eval.Issues.Add(new ComplianceIssueDetail
                        {
                            Kind = ComplianceIssueKind.MultiDiscSequenceGap,
                            Severity = ComplianceSeverity.Error,
                            InternalPath = primaryPayloadEntries[0].Path,
                            Message = $"Multi-disc sequence gap inside archive: found Disc(s) [{string.Join(", ", discNums)}], missing Disc(s) [{string.Join(", ", missing)}]."
                        });
                    }
                }

                if (template.RequireM3uForMultiDiscSet && discNums.Count >= 2 && !hasM3u)
                {
                    eval.Issues.Add(new ComplianceIssueDetail
                    {
                        Kind = ComplianceIssueKind.MissingCompanionDescriptor,
                        Severity = ComplianceSeverity.Warning,
                        InternalPath = "",
                        Message = $"Multi-disc archive ({discNums.Count} discs) is missing a required .m3u playlist."
                    });
                }

                // Official Redump packages strictly 1 disc per archive
                if (template.Category == ArchiveStandardCategory.OfficialArchival &&
                    template.MaxPrimaryRomsPerArchive > 0 &&
                    primaryPayloadEntries.Count > template.MaxPrimaryRomsPerArchive)
                {
                    eval.Issues.Add(new ComplianceIssueDetail
                    {
                        Kind = ComplianceIssueKind.MultiplePrimaryRomsInArchive,
                        Severity = ComplianceSeverity.Warning,
                        InternalPath = "",
                        Message = $"Archive bundles {primaryPayloadEntries.Count} discs together; '{template.DisplayName}' packages 1 disc per archive."
                    });
                }
                return;
            }

            if (template.MaxPrimaryRomsPerArchive > 0 && primaryPayloadEntries.Count > template.MaxPrimaryRomsPerArchive)
            {
                eval.Issues.Add(new ComplianceIssueDetail
                {
                    Kind = ComplianceIssueKind.MultiplePrimaryRomsInArchive,
                    Severity = ComplianceSeverity.Warning,
                    InternalPath = primaryPayloadEntries[1].Path,
                    Message = $"Multi-ROM archive contains {primaryPayloadEntries.Count} primary ROMs (template allows at most {template.MaxPrimaryRomsPerArchive})."
                });
            }
        }

        private static void EvaluateLooseFile(
            string filePath,
            ArchiveComplianceTemplate template,
            HashSet<string> allowedPayloads,
            HashSet<string> allowedOuterContainers,
            TargetComplianceEvaluation eval)
        {
            string ext = Path.GetExtension(filePath);
            string fileName = Path.GetFileName(filePath);
            var fi = new FileInfo(filePath);

            if (template.RequireArchivedContainer)
            {
                eval.Issues.Add(new ComplianceIssueDetail
                {
                    Kind = ComplianceIssueKind.WrongOuterContainerFormat,
                    Severity = ComplianceSeverity.Warning,
                    InternalPath = fileName,
                    SizeBytes = fi.Length,
                    Message = $"Uncompressed loose file '{fileName}' is not packaged in a required archive container ({string.Join(", ", allowedOuterContainers)})."
                });
            }

            if (allowedPayloads.Contains(ext))
            {
                eval.EntryNodes.Add(new ArchiveEntryPreviewNode
                {
                    InternalPath = fileName,
                    SizeBytes = fi.Length,
                    RoleClassification = "Primary Payload (Loose)",
                    StatusBadge = template.RequireArchivedContainer ? "WARN" : "OK",
                    CanClean = false
                });
            }
            else if (KnownGamePayloadExtensions.Contains(ext))
            {
                eval.Issues.Add(new ComplianceIssueDetail
                {
                    Kind = ComplianceIssueKind.WrongInnerPayloadFormat,
                    Severity = ComplianceSeverity.Error,
                    InternalPath = fileName,
                    SizeBytes = fi.Length,
                    Message = $"Loose ROM '{fileName}' uses format '{ext}', expected ({FormatAllowedList(allowedPayloads)})."
                });
                eval.EntryNodes.Add(new ArchiveEntryPreviewNode
                {
                    InternalPath = fileName,
                    SizeBytes = fi.Length,
                    RoleClassification = "Wrong Format Payload",
                    StatusBadge = "ERROR",
                    CanClean = false
                });
            }
            else
            {
                eval.Issues.Add(new ComplianceIssueDetail
                {
                    Kind = ComplianceIssueKind.ExtraNonRuleFile,
                    Severity = ComplianceSeverity.Warning,
                    InternalPath = fileName,
                    SizeBytes = fi.Length,
                    Message = $"Extra non-rule loose file '{fileName}' found in console directory."
                });
                eval.EntryNodes.Add(new ArchiveEntryPreviewNode
                {
                    InternalPath = fileName,
                    SizeBytes = fi.Length,
                    RoleClassification = "Extra Non-Rule File (Loose)",
                    StatusBadge = "EXTRA",
                    CanClean = false
                });
            }
        }

        private static void EvaluateUnpackedDirectory(
            string dirPath,
            ConsoleCollectionRule? col,
            ArchiveComplianceTemplate template,
            HashSet<string> allowedPayloads,
            HashSet<string> allowedCompanions,
            TargetComplianceEvaluation eval,
            CancellationToken ct)
        {
            if (!Directory.Exists(dirPath))
            {
                eval.IsEvaluated = false;
                return;
            }

            if (!template.AllowUnpackedFolderTargets)
            {
                eval.Issues.Add(new ComplianceIssueDetail
                {
                    Kind = ComplianceIssueKind.WrongOuterContainerFormat,
                    Severity = ComplianceSeverity.Warning,
                    InternalPath = new DirectoryInfo(dirPath).Name,
                    Message = $"Unpacked game directory violates template '{template.DisplayName}', which requires archived ROM packages."
                });
            }

            var allFiles = Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f))
                .ToList();

            if (allFiles.Count == 0)
            {
                eval.Issues.Add(new ComplianceIssueDetail
                {
                    Kind = ComplianceIssueKind.MissingPayloadRom,
                    Severity = ComplianceSeverity.Error,
                    Message = "Game directory is empty."
                });
                return;
            }

            var relMap = allFiles
                .ToDictionary(
                    f => Path.GetRelativePath(dirPath, f.FullName).Replace('\\', '/'),
                    f => f,
                    StringComparer.OrdinalIgnoreCase);

            bool isPs3Tree = Directory.Exists(Path.Combine(dirPath, "PS3_GAME")) ||
                             ((col?.ConsoleName ?? "").Contains("PS3", StringComparison.OrdinalIgnoreCase));
            bool isWiiUTree = (Directory.Exists(Path.Combine(dirPath, "code")) && Directory.Exists(Path.Combine(dirPath, "meta"))) ||
                              ((col?.ConsoleName ?? "").Contains("Wii U", StringComparison.OrdinalIgnoreCase)) ||
                              ((col?.ConsoleName ?? "").Contains("WiiU", StringComparison.OrdinalIgnoreCase));

            if (template.ValidateFolderDumpBootAnchors)
            {
                if (isPs3Tree)
                {
                    if (!relMap.ContainsKey("PS3_GAME/PARAM.SFO"))
                    {
                        eval.Issues.Add(new ComplianceIssueDetail
                        {
                            Kind = ComplianceIssueKind.MissingFolderBootAnchor,
                            Severity = ComplianceSeverity.Error,
                            InternalPath = "PS3_GAME/PARAM.SFO",
                            Message = "PS3 folder dump is missing required 'PS3_GAME/PARAM.SFO'."
                        });
                    }
                    if (!relMap.ContainsKey("PS3_GAME/USRDIR/EBOOT.BIN"))
                    {
                        eval.Issues.Add(new ComplianceIssueDetail
                        {
                            Kind = ComplianceIssueKind.MissingFolderBootAnchor,
                            Severity = ComplianceSeverity.Error,
                            InternalPath = "PS3_GAME/USRDIR/EBOOT.BIN",
                            Message = "PS3 folder dump is missing required boot binary 'PS3_GAME/USRDIR/EBOOT.BIN'."
                        });
                    }
                }
                else if (isWiiUTree)
                {
                    if (!relMap.ContainsKey("meta/meta.xml"))
                    {
                        eval.Issues.Add(new ComplianceIssueDetail
                        {
                            Kind = ComplianceIssueKind.MissingFolderBootAnchor,
                            Severity = ComplianceSeverity.Error,
                            InternalPath = "meta/meta.xml",
                            Message = "Wii U folder dump is missing required 'meta/meta.xml'."
                        });
                    }
                    bool hasRpx = relMap.Keys.Any(k => k.StartsWith("code/", StringComparison.OrdinalIgnoreCase) &&
                                                       k.EndsWith(".rpx", StringComparison.OrdinalIgnoreCase));
                    if (!hasRpx)
                    {
                        eval.Issues.Add(new ComplianceIssueDetail
                        {
                            Kind = ComplianceIssueKind.MissingFolderBootAnchor,
                            Severity = ComplianceSeverity.Error,
                            InternalPath = "code/*.rpx",
                            Message = "Wii U folder dump is missing required executable 'code/*.rpx'."
                        });
                    }
                }
            }

            if (template.FlagPs3UpdatePupBloat && relMap.TryGetValue("PS3_UPDATE/PS3UPDAT.PUP", out var pupFile))
            {
                eval.Issues.Add(new ComplianceIssueDetail
                {
                    Kind = ComplianceIssueKind.Ps3UpdatePupBloat,
                    Severity = ComplianceSeverity.Warning,
                    InternalPath = "PS3_UPDATE/PS3UPDAT.PUP",
                    SizeBytes = pupFile.Length,
                    Message = $"PS3 firmware update bloat 'PS3_UPDATE/PS3UPDAT.PUP' ({FormatBytesShort(pupFile.Length)}) present."
                });
            }

            // Check root-level scene clutter (.nfo, .sfv, .url, .jpg, .txt) in folder dumps
            var clutterExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".nfo", ".sfv", ".url", ".html", ".htm", ".diz", ".torrent"
            };

            foreach (var (rel, fi) in relMap.Take(250))
            {
                ct.ThrowIfCancellationRequested();
                string ext = fi.Extension;
                if (clutterExts.Contains(ext))
                {
                    eval.Issues.Add(new ComplianceIssueDetail
                    {
                        Kind = ComplianceIssueKind.ExtraNonRuleFile,
                        Severity = ComplianceSeverity.Warning,
                        InternalPath = rel,
                        SizeBytes = fi.Length,
                        Message = $"Scene clutter file '{rel}' found inside game folder."
                    });
                    eval.EntryNodes.Add(new ArchiveEntryPreviewNode
                    {
                        InternalPath = rel,
                        SizeBytes = fi.Length,
                        RoleClassification = "Extra Non-Rule File",
                        StatusBadge = "EXTRA",
                        CanClean = false
                    });
                }
                else if (!isPs3Tree && !isWiiUTree)
                {
                    bool ok = allowedPayloads.Contains(ext) || allowedCompanions.Contains(ext) ||
                              ext.Equals(".bin", StringComparison.OrdinalIgnoreCase);
                    eval.EntryNodes.Add(new ArchiveEntryPreviewNode
                    {
                        InternalPath = rel,
                        SizeBytes = fi.Length,
                        RoleClassification = ok ? "Folder Payload" : "Folder File",
                        StatusBadge = ok ? "OK" : "INFO",
                        CanClean = false
                    });
                }
            }
        }

        public static void CheckSiblingMultiDiscContinuity(
            DirectoryInfo consoleDir,
            IReadOnlyList<FileInfo> topFiles,
            Dictionary<string, TargetComplianceEvaluation> evaluatedByPath,
            ArchiveComplianceTemplate template)
        {
            var m3uBaseNames = consoleDir.GetFiles("*.m3u", SearchOption.TopDirectoryOnly)
                .Select(f => Path.GetFileNameWithoutExtension(f.Name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var groups = topFiles
                .Select(f => (File: f, Disc: TryParseDiscNumber(Path.GetFileNameWithoutExtension(f.Name))))
                .Where(x => x.Disc.HasDisc && !string.IsNullOrWhiteSpace(x.Disc.BaseTitle))
                .GroupBy(x => x.Disc.BaseTitle, StringComparer.OrdinalIgnoreCase);

            foreach (var grp in groups)
            {
                var discNums = grp.Select(x => x.Disc.DiscNumber).Distinct().OrderBy(n => n).ToList();
                if (discNums.Count == 0)
                    continue;

                int maxDisc = discNums.Max();
                var missing = Enumerable.Range(1, maxDisc).Except(discNums).ToList();

                if (missing.Count > 0)
                {
                    foreach (var member in grp)
                    {
                        if (evaluatedByPath.TryGetValue(member.File.FullName, out var eval))
                        {
                            eval.Issues.Add(new ComplianceIssueDetail
                            {
                                Kind = ComplianceIssueKind.MultiDiscSequenceGap,
                                Severity = ComplianceSeverity.Warning,
                                InternalPath = member.File.Name,
                                Message = $"Multi-disc set '{grp.Key}' is missing Disc(s) [{string.Join(", ", missing)}] (found Discs [{string.Join(", ", discNums)}])."
                            });
                            FinalizeEvaluationSummary(eval);
                        }
                    }
                }

                if (template.RequireM3uForMultiDiscSet && discNums.Count >= 2)
                {
                    bool hasMatchingM3u = m3uBaseNames.Any(m =>
                        m.Equals(grp.Key, StringComparison.OrdinalIgnoreCase) ||
                        m.StartsWith(grp.Key, StringComparison.OrdinalIgnoreCase));

                    if (!hasMatchingM3u)
                    {
                        var firstMember = grp.OrderBy(x => x.Disc.DiscNumber).First();
                        if (evaluatedByPath.TryGetValue(firstMember.File.FullName, out var eval))
                        {
                            eval.Issues.Add(new ComplianceIssueDetail
                            {
                                Kind = ComplianceIssueKind.MissingCompanionDescriptor,
                                Severity = ComplianceSeverity.Warning,
                                InternalPath = grp.Key + ".m3u",
                                Message = $"Multi-disc set '{grp.Key}' ({discNums.Count} discs) has no companion .m3u playlist in console folder."
                            });
                            FinalizeEvaluationSummary(eval);
                        }
                    }
                }
            }
        }

        private static (bool HasDisc, string BaseTitle, int DiscNumber) TryParseDiscNumber(string fileNameWithoutExt)
        {
            if (string.IsNullOrWhiteSpace(fileNameWithoutExt))
                return (false, "", 0);

            var m = DiscTagRegex.Match(fileNameWithoutExt);
            if (!m.Success || !int.TryParse(m.Groups[1].Value, out int discNum) || discNum <= 0)
                return (false, "", 0);

            string baseTitle = (fileNameWithoutExt[..m.Index] + " " + fileNameWithoutExt[(m.Index + m.Length)..]).Trim();
            baseTitle = Regex.Replace(baseTitle, @"\s{2,}", " ").Trim(' ', '-', '_');
            return (true, baseTitle, discNum);
        }

        private static List<string> ExtractDescriptorReferences(string descExt, string content)
        {
            var refs = new List<string>();
            if (descExt.Equals(".cue", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match m in CueFileRegex.Matches(content))
                {
                    string val = !string.IsNullOrWhiteSpace(m.Groups[1].Value) ? m.Groups[1].Value : m.Groups[2].Value;
                    if (!string.IsNullOrWhiteSpace(val))
                        refs.Add(val.Trim());
                }
            }
            else if (descExt.Equals(".gdi", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match m in GdiTrackRegex.Matches(content))
                {
                    string val = !string.IsNullOrWhiteSpace(m.Groups[1].Value) ? m.Groups[1].Value : m.Groups[2].Value;
                    if (!string.IsNullOrWhiteSpace(val))
                        refs.Add(val.Trim());
                }
            }
            else if (descExt.Equals(".m3u", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var line in content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!line.StartsWith('#'))
                        refs.Add(line);
                }
            }
            return refs;
        }

        private static void FinalizeEvaluationSummary(TargetComplianceEvaluation eval)
        {
            if (!eval.IsEvaluated || eval.Issues.Count == 0)
            {
                eval.MaxSeverity = ComplianceSeverity.Clean;
                eval.SummaryCode = "CLEAN";
                eval.SummaryText = "Compliant with archive template.";
                return;
            }

            eval.MaxSeverity = eval.Issues.Max(i => i.Severity);
            string prefix = eval.MaxSeverity == ComplianceSeverity.Error ? "ERROR" : "WARN";

            var grouped = eval.Issues
                .GroupBy(i => i.Kind)
                .OrderByDescending(g => g.Max(x => (int)x.Severity))
                .ToList();

            var tags = new List<string>();
            foreach (var g in grouped)
            {
                string tag = g.Key switch
                {
                    ComplianceIssueKind.ExtraNonRuleFile => $"ExtraFiles({g.Count()})",
                    ComplianceIssueKind.Ps3UpdatePupBloat => "Ps3UpdateBloat",
                    ComplianceIssueKind.MultiplePrimaryRomsInArchive => "MultiRomPack",
                    ComplianceIssueKind.WrongInnerPayloadFormat => "WrongFormat",
                    ComplianceIssueKind.WrongOuterContainerFormat => "OuterContainer",
                    ComplianceIssueKind.NestedArchiveInsideArchive => "NestedArchive",
                    ComplianceIssueKind.DisallowedWrapperFolder => "WrapperFolder",
                    ComplianceIssueKind.MissingCompanionDescriptor => "MissingDescriptor",
                    ComplianceIssueKind.MissingReferencedTrack => $"MissingTrack({g.Count()})",
                    ComplianceIssueKind.OrphanUnreferencedTrack => $"OrphanTrack({g.Count()})",
                    ComplianceIssueKind.MultiDiscSequenceGap => "MissingDisc",
                    ComplianceIssueKind.MissingFolderBootAnchor => "MissingBootAnchor",
                    ComplianceIssueKind.MissingPayloadRom => "NoPayloadRom",
                    ComplianceIssueKind.SidecarHashIntegrityIssue => "HashMismatch",
                    _ => g.Key.ToString()
                };
                tags.Add(tag);
            }

            eval.SummaryCode = $"{prefix}: {string.Join(" + ", tags)}";
            eval.SummaryText = string.Join(" | ", eval.Issues.Select(i => i.Message).Take(3));
        }

        private static string NormalizeInternalPath(string path)
            => (path ?? "").Replace('\\', '/').Trim('/');

        private static string GetInternalDirectory(string path)
        {
            string norm = NormalizeInternalPath(path);
            int idx = norm.LastIndexOf('/');
            return idx > 0 ? norm[..idx] : "";
        }

        private static string ResolveRelativeInternalPath(string baseDir, string relativeRef)
        {
            string normRef = NormalizeInternalPath(relativeRef);
            if (normRef.StartsWith("./", StringComparison.Ordinal))
                normRef = normRef[2..];

            if (string.IsNullOrEmpty(baseDir))
                return normRef;

            return $"{baseDir}/{normRef}";
        }

        private static string FormatAllowedList(HashSet<string> exts)
            => exts.Count == 0 ? "any" : string.Join(", ", exts.OrderBy(e => e));

        private static string FormatBytesShort(long bytes)
        {
            if (bytes >= 1024L * 1024)
                return $"{bytes / (1024.0 * 1024):F1} MB";
            if (bytes >= 1024L)
                return $"{bytes / 1024.0:F1} KB";
            return $"{bytes} B";
        }
    }
}
