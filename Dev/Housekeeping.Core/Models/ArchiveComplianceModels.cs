using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Housekeeping.Models
{
    public enum ArchiveStandardCategory
    {
        Community,
        OfficialArchival,
        Custom
    }

    public enum ComplianceSeverity
    {
        Clean,
        Info,
        Warning,
        Error
    }

    public enum ComplianceIssueKind
    {
        ExtraNonRuleFile,
        MultiplePrimaryRomsInArchive,
        WrongInnerPayloadFormat,
        WrongOuterContainerFormat,
        NestedArchiveInsideArchive,
        DisallowedWrapperFolder,
        MissingCompanionDescriptor,
        MissingReferencedTrack,
        OrphanUnreferencedTrack,
        MultiDiscSequenceGap,
        MissingFolderBootAnchor,
        Ps3UpdatePupBloat,
        MissingPayloadRom,
        SidecarHashIntegrityIssue
    }

    public enum ComplianceReportResolutionStatus
    {
        Open,
        Resolved,
        Ignored
    }

    public class ArchiveTemplateSelectorOption
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public override string ToString() => Label;
    }

    public class ArchiveComplianceTemplate : ObservableObject
    {
        private string _templateId = "auto-community";
        public string TemplateId
        {
            get => _templateId;
            set { _templateId = value ?? ""; OnPropertyChanged(); }
        }

        private string _displayName = "Community: Console Rule Driven (Auto)";
        public string DisplayName
        {
            get => _displayName;
            set { _displayName = value ?? ""; OnPropertyChanged(); }
        }

        private ArchiveStandardCategory _category = ArchiveStandardCategory.Community;
        public ArchiveStandardCategory Category
        {
            get => _category;
            set { _category = value; OnPropertyChanged(); OnPropertyChanged(nameof(CategoryBadge)); }
        }

        private bool _isBuiltIn = true;
        public bool IsBuiltIn
        {
            get => _isBuiltIn;
            set { _isBuiltIn = value; OnPropertyChanged(); }
        }

        private string _description = "";
        public string Description
        {
            get => _description;
            set { _description = value ?? ""; OnPropertyChanged(); }
        }

        private bool _useConsoleRuleExtensionsAsPayloads = false;
        public bool UseConsoleRuleExtensionsAsPayloads
        {
            get => _useConsoleRuleExtensionsAsPayloads;
            set { _useConsoleRuleExtensionsAsPayloads = value; OnPropertyChanged(); }
        }

        private string _allowedPayloadExtensionsCsv = "";
        public string AllowedPayloadExtensionsCsv
        {
            get => _allowedPayloadExtensionsCsv;
            set { _allowedPayloadExtensionsCsv = value ?? ""; OnPropertyChanged(); }
        }

        private string _allowedCompanionExtensionsCsv = ".hash";
        public string AllowedCompanionExtensionsCsv
        {
            get => _allowedCompanionExtensionsCsv;
            set { _allowedCompanionExtensionsCsv = value ?? ""; OnPropertyChanged(); }
        }

        private string _allowedOuterContainerExtensionsCsv = ".7z, .zip";
        public string AllowedOuterContainerExtensionsCsv
        {
            get => _allowedOuterContainerExtensionsCsv;
            set { _allowedOuterContainerExtensionsCsv = value ?? ""; OnPropertyChanged(); }
        }

        private bool _requireArchivedContainer = false;
        public bool RequireArchivedContainer
        {
            get => _requireArchivedContainer;
            set { _requireArchivedContainer = value; OnPropertyChanged(); }
        }

        private bool _allowUnpackedFolderTargets = true;
        public bool AllowUnpackedFolderTargets
        {
            get => _allowUnpackedFolderTargets;
            set { _allowUnpackedFolderTargets = value; OnPropertyChanged(); }
        }

        private bool _allowSingleWrapperFolder = true;
        public bool AllowSingleWrapperFolder
        {
            get => _allowSingleWrapperFolder;
            set { _allowSingleWrapperFolder = value; OnPropertyChanged(); }
        }

        private int _maxPrimaryRomsPerArchive = 1;
        public int MaxPrimaryRomsPerArchive
        {
            get => _maxPrimaryRomsPerArchive;
            set { _maxPrimaryRomsPerArchive = Math.Max(0, value); OnPropertyChanged(); }
        }

        private bool _requireCueOrGdiForRawBinTracks = true;
        public bool RequireCueOrGdiForRawBinTracks
        {
            get => _requireCueOrGdiForRawBinTracks;
            set { _requireCueOrGdiForRawBinTracks = value; OnPropertyChanged(); }
        }

        private bool _validateCueGdiM3uReferences = true;
        public bool ValidateCueGdiM3uReferences
        {
            get => _validateCueGdiM3uReferences;
            set { _validateCueGdiM3uReferences = value; OnPropertyChanged(); }
        }

        private bool _flagOrphanTrackBins = true;
        public bool FlagOrphanTrackBins
        {
            get => _flagOrphanTrackBins;
            set { _flagOrphanTrackBins = value; OnPropertyChanged(); }
        }

        private bool _validateMultiDiscSequenceContinuity = true;
        public bool ValidateMultiDiscSequenceContinuity
        {
            get => _validateMultiDiscSequenceContinuity;
            set { _validateMultiDiscSequenceContinuity = value; OnPropertyChanged(); }
        }

        private bool _requireM3uForMultiDiscSet = false;
        public bool RequireM3uForMultiDiscSet
        {
            get => _requireM3uForMultiDiscSet;
            set { _requireM3uForMultiDiscSet = value; OnPropertyChanged(); }
        }

        private bool _flagNestedArchives = true;
        public bool FlagNestedArchives
        {
            get => _flagNestedArchives;
            set { _flagNestedArchives = value; OnPropertyChanged(); }
        }

        private bool _validateFolderDumpBootAnchors = true;
        public bool ValidateFolderDumpBootAnchors
        {
            get => _validateFolderDumpBootAnchors;
            set { _validateFolderDumpBootAnchors = value; OnPropertyChanged(); }
        }

        private bool _flagPs3UpdatePupBloat = false;
        public bool FlagPs3UpdatePupBloat
        {
            get => _flagPs3UpdatePupBloat;
            set { _flagPs3UpdatePupBloat = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public string CategoryBadge => Category switch
        {
            ArchiveStandardCategory.Community => "Community",
            ArchiveStandardCategory.OfficialArchival => "Official (Redump / No-Intro)",
            _ => "Custom"
        };

        public ArchiveComplianceTemplate Clone() => new ArchiveComplianceTemplate
        {
            TemplateId = TemplateId,
            DisplayName = DisplayName,
            Category = Category,
            IsBuiltIn = IsBuiltIn,
            Description = Description,
            UseConsoleRuleExtensionsAsPayloads = UseConsoleRuleExtensionsAsPayloads,
            AllowedPayloadExtensionsCsv = AllowedPayloadExtensionsCsv,
            AllowedCompanionExtensionsCsv = AllowedCompanionExtensionsCsv,
            AllowedOuterContainerExtensionsCsv = AllowedOuterContainerExtensionsCsv,
            RequireArchivedContainer = RequireArchivedContainer,
            AllowUnpackedFolderTargets = AllowUnpackedFolderTargets,
            AllowSingleWrapperFolder = AllowSingleWrapperFolder,
            MaxPrimaryRomsPerArchive = MaxPrimaryRomsPerArchive,
            RequireCueOrGdiForRawBinTracks = RequireCueOrGdiForRawBinTracks,
            ValidateCueGdiM3uReferences = ValidateCueGdiM3uReferences,
            FlagOrphanTrackBins = FlagOrphanTrackBins,
            ValidateMultiDiscSequenceContinuity = ValidateMultiDiscSequenceContinuity,
            RequireM3uForMultiDiscSet = RequireM3uForMultiDiscSet,
            FlagNestedArchives = FlagNestedArchives,
            ValidateFolderDumpBootAnchors = ValidateFolderDumpBootAnchors,
            FlagPs3UpdatePupBloat = FlagPs3UpdatePupBloat
        };
    }

    public class ComplianceIssueDetail
    {
        public ComplianceIssueKind Kind { get; set; }
        public ComplianceSeverity Severity { get; set; } = ComplianceSeverity.Warning;
        public string InternalPath { get; set; } = "";
        public long SizeBytes { get; set; }
        public string Crc32 { get; set; } = "";
        public string Message { get; set; } = "";
        public bool IsSafeExtraFileDeletable { get; set; }
    }

    public class ArchiveEntryPreviewNode : ObservableObject
    {
        public string InternalPath { get; set; } = "";
        public long SizeBytes { get; set; }
        public string Crc32 { get; set; } = "";
        public string RoleClassification { get; set; } = "Primary Payload";
        public string StatusBadge { get; set; } = "OK";
        public bool CanClean { get; set; }

        private bool _selectedForCleanup;
        public bool SelectedForCleanup
        {
            get => _selectedForCleanup;
            set { _selectedForCleanup = CanClean && value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public string SizeDisplay
        {
            get
            {
                if (SizeBytes >= 1024L * 1024 * 1024)
                    return $"{SizeBytes / (1024.0 * 1024 * 1024):F2} GB";
                if (SizeBytes >= 1024L * 1024)
                    return $"{SizeBytes / (1024.0 * 1024):F2} MB";
                if (SizeBytes >= 1024L)
                    return $"{SizeBytes / 1024.0:F1} KB";
                return $"{SizeBytes} B";
            }
        }
    }

    public class ComplianceReportItem : ObservableObject
    {
        public string ReportId { get; set; } = "";
        public string ConsoleName { get; set; } = "";
        public string TargetFileName { get; set; } = "";
        public string TargetFullPath { get; set; } = "";
        public bool IsDirectory { get; set; }
        public string TemplateId { get; set; } = "";
        public string TemplateDisplayName { get; set; } = "";

        private ComplianceSeverity _severity = ComplianceSeverity.Warning;
        public ComplianceSeverity Severity
        {
            get => _severity;
            set { _severity = value; OnPropertyChanged(); OnPropertyChanged(nameof(SeverityDisplay)); }
        }

        private ComplianceReportResolutionStatus _resolutionStatus = ComplianceReportResolutionStatus.Open;
        public ComplianceReportResolutionStatus ResolutionStatus
        {
            get => _resolutionStatus;
            set { _resolutionStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusDisplay)); }
        }

        private string _summaryCode = "CLEAN";
        public string SummaryCode
        {
            get => _summaryCode;
            set { _summaryCode = value ?? "CLEAN"; OnPropertyChanged(); }
        }

        private string _summaryText = "";
        public string SummaryText
        {
            get => _summaryText;
            set { _summaryText = value ?? ""; OnPropertyChanged(); }
        }

        public string PrimaryCategory { get; set; } = "Extra Files";
        public int IssueCount { get; set; }
        public int ExtraDeletableCount { get; set; }
        public DateTime FirstDetectedUtc { get; set; } = DateTime.UtcNow;
        public DateTime LastEvaluatedUtc { get; set; } = DateTime.UtcNow;

        public List<ComplianceIssueDetail> Issues { get; set; } = new();
        public List<ArchiveEntryPreviewNode> EntryNodes { get; set; } = new();

        [JsonIgnore]
        public string SeverityDisplay => Severity switch
        {
            ComplianceSeverity.Error => "ERROR",
            ComplianceSeverity.Warning => "WARN",
            ComplianceSeverity.Info => "INFO",
            _ => "CLEAN"
        };

        [JsonIgnore]
        public string StatusDisplay => ResolutionStatus switch
        {
            ComplianceReportResolutionStatus.Resolved => "Resolved",
            ComplianceReportResolutionStatus.Ignored => "Ignored",
            _ => "Open"
        };

        [JsonIgnore]
        public string LastEvaluatedDisplay => LastEvaluatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

        [JsonIgnore]
        public bool CanCleanExtraFiles => ExtraDeletableCount > 0 && !IsDirectory && ResolutionStatus == ComplianceReportResolutionStatus.Open;

        [JsonIgnore]
        public bool CanHealHash => Issues.Any(i => i.Kind == ComplianceIssueKind.SidecarHashIntegrityIssue);
    }

    public static class ArchiveComplianceCatalog
    {
        public static List<ArchiveComplianceTemplate> CreateBuiltInTemplates()
        {
            return new List<ArchiveComplianceTemplate>
            {
                new ArchiveComplianceTemplate
                {
                    TemplateId = "auto-community",
                    DisplayName = "Community: Console Rule Driven (Auto)",
                    Category = ArchiveStandardCategory.Community,
                    IsBuiltIn = true,
                    Description = "Uses each Console Collection row's ExtensionsCsv as allowed payloads. Allows .hash, .m3u, .sbi, .cue, .gdi companions and a single wrapper folder. Flags scene clutter (.nfo/.txt/.url/.jpg/.sav/patches), missing .cue/.gdi, broken track references, Disc 1..N gaps, multi-ROM merged packs, and nested archives.",
                    UseConsoleRuleExtensionsAsPayloads = true,
                    AllowedPayloadExtensionsCsv = "",
                    AllowedCompanionExtensionsCsv = ".hash, .m3u, .sbi, .cue, .gdi",
                    AllowedOuterContainerExtensionsCsv = ".7z, .zip",
                    RequireArchivedContainer = false,
                    AllowUnpackedFolderTargets = true,
                    AllowSingleWrapperFolder = true,
                    MaxPrimaryRomsPerArchive = 1,
                    RequireCueOrGdiForRawBinTracks = true,
                    ValidateCueGdiM3uReferences = true,
                    FlagOrphanTrackBins = true,
                    ValidateMultiDiscSequenceContinuity = true,
                    RequireM3uForMultiDiscSet = false,
                    FlagNestedArchives = true,
                    ValidateFolderDumpBootAnchors = true,
                    FlagPs3UpdatePupBloat = false
                },
                new ArchiveComplianceTemplate
                {
                    TemplateId = "community-modern-disc",
                    DisplayName = "Community: Modern Lossless Disc (.chd / .rvz)",
                    Category = ArchiveStandardCategory.Community,
                    IsBuiltIn = true,
                    Description = "Community emulation standard for optical disc systems (PS1, PS2, Saturn, Dreamcast, Sega CD, PSP, GameCube, Wii). Enforces modern lossless compressed disc formats (.chd, .rvz, .cso, .pbp, .wbfs, .xiso), requires .m3u for multi-disc sets, and flags unconverted raw .bin+.cue / .iso sets and extra clutter.",
                    UseConsoleRuleExtensionsAsPayloads = false,
                    AllowedPayloadExtensionsCsv = ".chd, .rvz, .cso, .pbp, .wbfs, .xiso",
                    AllowedCompanionExtensionsCsv = ".hash, .m3u, .sbi",
                    AllowedOuterContainerExtensionsCsv = ".7z, .zip",
                    RequireArchivedContainer = false,
                    AllowUnpackedFolderTargets = true,
                    AllowSingleWrapperFolder = true,
                    MaxPrimaryRomsPerArchive = 1,
                    RequireCueOrGdiForRawBinTracks = true,
                    ValidateCueGdiM3uReferences = true,
                    FlagOrphanTrackBins = true,
                    ValidateMultiDiscSequenceContinuity = true,
                    RequireM3uForMultiDiscSet = true,
                    FlagNestedArchives = true,
                    ValidateFolderDumpBootAnchors = false,
                    FlagPs3UpdatePupBloat = false
                },
                new ArchiveComplianceTemplate
                {
                    TemplateId = "community-cartridge-7z",
                    DisplayName = "Community: Clean 1-Game-Per-7z Cartridge",
                    Category = ArchiveStandardCategory.Community,
                    IsBuiltIn = true,
                    Description = "Clean 1G1R community cartridge packaging: requires .7z outer archive containing exactly 1 emulator cartridge ROM (allows common community extensions like .sfc/.smc, .z64/.n64/.v64, .md/.gen/.bin) and optional .hash sidecar. Flags GoodSet multi-ROM packs, .zip/.rar archives, patches (.ips/.bps), saves (.sav/.srm), and .nfo/.txt clutter.",
                    UseConsoleRuleExtensionsAsPayloads = false,
                    AllowedPayloadExtensionsCsv = ".gb, .gbc, .gba, .nes, .fds, .sfc, .smc, .z64, .n64, .v64, .nds, .3ds, .cia, .md, .gen, .bin, .smd, .32x, .sms, .gg, .pce, .ngp, .ngc, .ws, .wsc, .a26, .a52, .a78, .lnx, .j64",
                    AllowedCompanionExtensionsCsv = ".hash",
                    AllowedOuterContainerExtensionsCsv = ".7z",
                    RequireArchivedContainer = true,
                    AllowUnpackedFolderTargets = false,
                    AllowSingleWrapperFolder = true,
                    MaxPrimaryRomsPerArchive = 1,
                    RequireCueOrGdiForRawBinTracks = false,
                    ValidateCueGdiM3uReferences = false,
                    FlagOrphanTrackBins = false,
                    ValidateMultiDiscSequenceContinuity = false,
                    RequireM3uForMultiDiscSet = false,
                    FlagNestedArchives = true,
                    ValidateFolderDumpBootAnchors = false,
                    FlagPs3UpdatePupBloat = false
                },
                new ArchiveComplianceTemplate
                {
                    TemplateId = "official-nointro-cartridge",
                    DisplayName = "Official: No-Intro Cartridge Preservation",
                    Category = ArchiveStandardCategory.OfficialArchival,
                    IsBuiltIn = true,
                    Description = "Strict No-Intro archival preservation standard: flat archive root (.7z or .zip, no wrapper subfolders), exactly 1 canonical raw ROM per archive (rejects copier/byte-swapped formats such as .smc, .v64, .smd), and zero extra files aside from .hash.",
                    UseConsoleRuleExtensionsAsPayloads = false,
                    AllowedPayloadExtensionsCsv = ".gb, .gbc, .gba, .nes, .fds, .sfc, .z64, .nds, .3ds, .md, .bin, .32x, .sms, .gg, .pce, .ngp, .ngc, .ws, .wsc, .a26, .a52, .a78, .lnx, .j64",
                    AllowedCompanionExtensionsCsv = ".hash",
                    AllowedOuterContainerExtensionsCsv = ".7z, .zip",
                    RequireArchivedContainer = true,
                    AllowUnpackedFolderTargets = false,
                    AllowSingleWrapperFolder = false,
                    MaxPrimaryRomsPerArchive = 1,
                    RequireCueOrGdiForRawBinTracks = false,
                    ValidateCueGdiM3uReferences = false,
                    FlagOrphanTrackBins = false,
                    ValidateMultiDiscSequenceContinuity = false,
                    RequireM3uForMultiDiscSet = false,
                    FlagNestedArchives = true,
                    ValidateFolderDumpBootAnchors = false,
                    FlagPs3UpdatePupBloat = false
                },
                new ArchiveComplianceTemplate
                {
                    TemplateId = "official-redump-cd",
                    DisplayName = "Official: Redump CD / GD-ROM (.cue + .bin / .gdi)",
                    Category = ArchiveStandardCategory.OfficialArchival,
                    IsBuiltIn = true,
                    Description = "Official Redump preservation standard for CD-ROM & GD-ROM systems (PS1, Saturn, Sega CD, Dreamcast, PCE-CD). Requires flat .7z/.zip archive with raw .cue + .bin (or .gdi + .bin/.raw), validates every track reference & flags orphan .bins, allows .sbi & .hash, and flags repacked containers (.chd, .pbp, .cdi, .cso) or scene .nfo/.sfv/.ccd/.sub clutter.",
                    UseConsoleRuleExtensionsAsPayloads = false,
                    AllowedPayloadExtensionsCsv = ".cue, .gdi, .bin, .raw",
                    AllowedCompanionExtensionsCsv = ".hash, .sbi",
                    AllowedOuterContainerExtensionsCsv = ".7z, .zip",
                    RequireArchivedContainer = true,
                    AllowUnpackedFolderTargets = false,
                    AllowSingleWrapperFolder = false,
                    MaxPrimaryRomsPerArchive = 1,
                    RequireCueOrGdiForRawBinTracks = true,
                    ValidateCueGdiM3uReferences = true,
                    FlagOrphanTrackBins = true,
                    ValidateMultiDiscSequenceContinuity = true,
                    RequireM3uForMultiDiscSet = false,
                    FlagNestedArchives = true,
                    ValidateFolderDumpBootAnchors = false,
                    FlagPs3UpdatePupBloat = false
                },
                new ArchiveComplianceTemplate
                {
                    TemplateId = "official-redump-dvd",
                    DisplayName = "Official: Redump DVD / Modern ISO (1:1 .iso)",
                    Category = ArchiveStandardCategory.OfficialArchival,
                    IsBuiltIn = true,
                    Description = "Official Redump preservation standard for DVD / Optical ISO systems (PS2, GameCube, Wii, PSP, Xbox, Xbox 360). Requires unmodified 1:1 .iso (or .cue+.bin for PS2 CDs) inside flat .7z/.zip archive, and flags scrubbed/compressed formats (.rvz, .wbfs, .ciso, .cso, .chd, .gcm, .xiso) and extra clutter.",
                    UseConsoleRuleExtensionsAsPayloads = false,
                    AllowedPayloadExtensionsCsv = ".iso, .cue, .bin",
                    AllowedCompanionExtensionsCsv = ".hash",
                    AllowedOuterContainerExtensionsCsv = ".7z, .zip",
                    RequireArchivedContainer = true,
                    AllowUnpackedFolderTargets = false,
                    AllowSingleWrapperFolder = false,
                    MaxPrimaryRomsPerArchive = 1,
                    RequireCueOrGdiForRawBinTracks = true,
                    ValidateCueGdiM3uReferences = true,
                    FlagOrphanTrackBins = true,
                    ValidateMultiDiscSequenceContinuity = true,
                    RequireM3uForMultiDiscSet = false,
                    FlagNestedArchives = true,
                    ValidateFolderDumpBootAnchors = false,
                    FlagPs3UpdatePupBloat = false
                },
                new ArchiveComplianceTemplate
                {
                    TemplateId = "official-folder-dump",
                    DisplayName = "Official: Clean Folder / JB Dump (PS3 / Wii U)",
                    Category = ArchiveStandardCategory.OfficialArchival,
                    IsBuiltIn = true,
                    Description = "Structured directory dump validation for PS3 (PS3_GAME/PARAM.SFO + USRDIR/EBOOT.BIN) and Wii U (code/*.rpx + meta/meta.xml). Flags missing boot anchors, scene .nfo/.sfv/.url/.jpg clutter, and optional 256 MB PS3_UPDATE/PS3UPDAT.PUP firmware partition bloat.",
                    UseConsoleRuleExtensionsAsPayloads = true,
                    AllowedPayloadExtensionsCsv = ".sfo, .bin, .rpx, .xml, .self, .sprx, .edat, .sdat, .pkg, .3ds, .vpk",
                    AllowedCompanionExtensionsCsv = ".hash",
                    AllowedOuterContainerExtensionsCsv = ".7z, .zip",
                    RequireArchivedContainer = false,
                    AllowUnpackedFolderTargets = true,
                    AllowSingleWrapperFolder = true,
                    MaxPrimaryRomsPerArchive = 0,
                    RequireCueOrGdiForRawBinTracks = false,
                    ValidateCueGdiM3uReferences = false,
                    FlagOrphanTrackBins = false,
                    ValidateMultiDiscSequenceContinuity = false,
                    RequireM3uForMultiDiscSet = false,
                    FlagNestedArchives = false,
                    ValidateFolderDumpBootAnchors = true,
                    FlagPs3UpdatePupBloat = true
                }
            };
        }

        public static List<ArchiveComplianceTemplate> EnsureBuiltInsMerged(List<ArchiveComplianceTemplate>? existing)
        {
            var builtIns = CreateBuiltInTemplates();
            if (existing == null || existing.Count == 0)
                return builtIns;

            var result = new List<ArchiveComplianceTemplate>();
            var existingById = existing
                .Where(t => !string.IsNullOrWhiteSpace(t.TemplateId))
                .GroupBy(t => t.TemplateId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // Preserve user customizations to built-in templates while ensuring every built-in exists
            foreach (var b in builtIns)
            {
                if (existingById.TryGetValue(b.TemplateId, out var userVer))
                {
                    userVer.IsBuiltIn = true;
                    result.Add(userVer);
                    existingById.Remove(b.TemplateId);
                }
                else
                {
                    result.Add(b);
                }
            }

            // Append custom templates
            foreach (var custom in existing.Where(t => !string.IsNullOrWhiteSpace(t.TemplateId) && existingById.ContainsKey(t.TemplateId)))
            {
                result.Add(custom);
                existingById.Remove(custom.TemplateId);
            }

            return result;
        }

        public static List<ArchiveTemplateSelectorOption> GetSelectorOptions(IReadOnlyList<ArchiveComplianceTemplate>? templates)
        {
            var merged = EnsureBuiltInsMerged(templates?.ToList());
            var options = new List<ArchiveTemplateSelectorOption>
            {
                new() { Id = "auto-community", Label = "Auto (Community Console Rule)" },
                new() { Id = "auto-official", Label = "Auto (Official Redump / No-Intro)" },
                new() { Id = "disabled", Label = "Disabled (Skip Compliance)" }
            };

            foreach (var t in merged.Where(t => !string.Equals(t.TemplateId, "auto-community", StringComparison.OrdinalIgnoreCase)))
            {
                options.Add(new ArchiveTemplateSelectorOption
                {
                    Id = t.TemplateId,
                    Label = t.DisplayName
                });
            }

            return options;
        }

        public static ArchiveComplianceTemplate? ResolveTemplateForConsole(
            string? templateIdOrMode,
            ConsoleCollectionRule? col,
            IReadOnlyList<ArchiveComplianceTemplate>? configuredTemplates)
        {
            string mode = string.IsNullOrWhiteSpace(templateIdOrMode) ? "auto-community" : templateIdOrMode.Trim();
            if (string.Equals(mode, "disabled", StringComparison.OrdinalIgnoreCase))
                return null;

            var templates = EnsureBuiltInsMerged(configuredTemplates?.ToList());
            var byId = templates.ToDictionary(t => t.TemplateId, t => t, StringComparer.OrdinalIgnoreCase);

            if (string.Equals(mode, "auto-official", StringComparison.OrdinalIgnoreCase))
            {
                string consoleLower = ((col?.ConsoleName ?? "") + " " + (col?.SubFolder ?? "") + " " + (col?.ExtensionsCsv ?? "")).ToLowerInvariant();
                if (consoleLower.Contains("ps3") || consoleLower.Contains("playstation 3") ||
                    consoleLower.Contains("wii u") || consoleLower.Contains("wiiu") ||
                    consoleLower.Contains(".sfo") || consoleLower.Contains(".rpx"))
                {
                    return byId.GetValueOrDefault("official-folder-dump");
                }

                if (consoleLower.Contains("ps2") || consoleLower.Contains("playstation 2") ||
                    consoleLower.Contains("gamecube") || consoleLower.Contains("gc") ||
                    (consoleLower.Contains("wii") && !consoleLower.Contains("wii u")) ||
                    consoleLower.Contains("psp") || consoleLower.Contains("xbox"))
                {
                    return byId.GetValueOrDefault("official-redump-dvd");
                }

                if (consoleLower.Contains("ps1") || consoleLower.Contains("psx") || consoleLower.Contains("playstation") ||
                    consoleLower.Contains("saturn") || consoleLower.Contains("dreamcast") ||
                    consoleLower.Contains("sega cd") || consoleLower.Contains("mega-cd") ||
                    consoleLower.Contains("pc engine cd") || consoleLower.Contains("turbografx-cd") ||
                    consoleLower.Contains(".cue") || consoleLower.Contains(".gdi") || consoleLower.Contains(".chd"))
                {
                    return byId.GetValueOrDefault("official-redump-cd");
                }

                return byId.GetValueOrDefault("official-nointro-cartridge");
            }

            if (byId.TryGetValue(mode, out var exact))
                return exact;

            return byId.GetValueOrDefault("auto-community");
        }
    }
}
