using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Housekeeping.Models
{
    public enum ModuleId
    {
        ProxyGenerator,
        ArchiveCompressor,
        MediaTranscoder,
        RomLauncherGenerator,
        CloudSync,
        HumbleSync,
        CustomScripts,
        CollectionManifest
    }

    public enum TaskRunState
    {
        Idle,
        Running,
        Paused,
        WaitingForNetwork,
        Completed,
        Failed,
        Skipped
    }

    public enum SourceDetectionMode
    {
        FilenameTagMatching,
        MediaInfoResolutionScan
    }

    public enum FooterVerbosityLevel
    {
        AlertsOnly,
        Quiet,
        ProgressAndStatus,
        Everything,
        Custom
    }

    public enum VerbosityLevel
    {
        AlertsOnly = 0,
        Quiet = 1,
        ProgressAndStatus = 2,
        Everything = 3,
        Custom = 4
    }

    public enum HashSidecarMode
    {
        None = 0,
        SidecarBeside = 1,
        PackageInsideArchive = 2
    }

    public enum AuditStructureMode
    {
        Auto = 0,
        FilesOnly = 1,
        FolderDumpsOnly = 2
    }

    public class CustomVerbosityConfig
    {
        public List<string> TerminalTags { get; set; } = new() { "ERROR", "WARN", "SUCCESS", "WORK", "HEADER", "INFO", "NET" };
        public List<string> FooterTags { get; set; } = new() { "ERROR", "WARN", "SUCCESS", "WORK", "HEADER" };
    }

    public class ManifestDropdownCatalogConfig
    {
        public List<string> LocationIds { get; set; } = new()
        {
            "Local-SSD", "Local-DAS", "EmuDrive", "MasterSet", "Archive-Pool", "Remote-NAS", "Backup-Mirror"
        };
        public List<string> Labels { get; set; } = new()
        {
            "Master Set", "Media Library", "TV Shows", "Anime", "Movies", "Music", "RomHacks", "EmuSets", "Documents"
        };
        public List<string> Platforms { get; set; } = new()
        {
            "Auto-Detect", "Sony - PlayStation", "Sony - PlayStation 2", "Sony - PlayStation 3", "Sony - PlayStation Portable",
            "Nintendo - Game Boy", "Nintendo - Game Boy Color", "Nintendo - Game Boy Advance", "Nintendo - Nintendo DS", "Nintendo - Nintendo 3DS",
            "Nintendo - Nintendo Entertainment System", "Nintendo - Super Nintendo Entertainment System", "Nintendo - Nintendo 64",
            "Nintendo - GameCube", "Nintendo - Wii", "Nintendo - Wii U", "Nintendo - Switch",
            "Sega - Master System", "Sega - Genesis", "Sega - Sega CD", "Sega - 32X", "Sega - Saturn", "Sega - Dreamcast",
            "SNK - Neo Geo", "Arcade", "PC - Windows", "Media - TV Shows", "Media - Movies", "Media - Music", "Generic Media / Videos"
        };
        public List<string> ExtensionsFilters { get; set; } = new()
        {
            "* (All Files)",
            ".7z, .zip, .rar, .chd, .iso, .ciso, .m3u, .cue, .bin",
            ".mkv, .mp4, .avi, .mov, .webm",
            ".mp3, .flac, .ogg, .m4a, .wav",
            ".pdf, .epub, .cbr, .cbz"
        };
    }

    public enum MasterScheduleMode
    {
        DailyTime = 0,
        IntervalCadence = 1,
        SpecificTimes = 2
    }

    public enum RemoteConnectionType
    {
        WindowsUncShare = 0,
        OpenSsh = 1,
        WinRm = 2
    }

    public enum TerminalSplitMode
    {
        Single = 0,
        SplitHorizontal = 1,
        SplitVertical = 2
    }

    public class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class AppConfig : ObservableObject
    {
        public const int CurrentSchemaVersion = 4;
        public int ConfigVersion { get; set; } = CurrentSchemaVersion;

        // Global & Safety Settings
        public bool DefaultUseBuiltInTerminal { get; set; } = true;
        public bool EnableNetworkDropSafety { get; set; } = true;
        public int NetworkRetryIntervalSeconds { get; set; } = 15;
        public int NetworkMaxRetries { get; set; } = 240;
        public bool EnableAtomicPartialWrites { get; set; } = true;
        public bool CleanupPartialsOnStartup { get; set; } = true;
        public bool EnableDistributedNetworkLocking { get; set; } = true;
        public int DistributedLockTimeoutMinutes { get; set; } = 90;
        public bool MinimizeToTrayOnClose { get; set; } = true;
        public bool StartMinimizedToTray { get; set; } = false;
        public bool StartWithWindows { get; set; } = false;

        // Window Placement & Coordinates Persistence
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }
        public double? WindowWidth { get; set; }
        public double? WindowHeight { get; set; }
        public bool IsWindowMaximized { get; set; }

        // External Tool Custom Overrides (Empty = auto-resolve from ./tools/ then System PATH)
        public ToolPathsConfig ToolPaths { get; set; } = new();

        // In-App HandBrake Encoding Profiles
        public List<HandBrakeEncodingProfile> HandBrakeProfiles { get; set; } = HandBrakeEncodingProfile.CreateDefaultProfiles();

        // Master Schedule (Legacy / Default Queue fallback)
        public bool EnableMasterSchedule { get; set; } = false;
        public MasterScheduleMode MasterScheduleMode { get; set; } = MasterScheduleMode.DailyTime;
        public string MasterScheduleTime { get; set; } = "03:00";
        public int MasterScheduleIntervalMinutes { get; set; } = 60;
        public string MasterScheduleSpecificTimesCsv { get; set; } = "03:00, 12:00, 18:00";
        public List<DayOfWeek> MasterScheduleDays { get; set; } = new()
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
            DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
        };
        public DateTime? LastMasterScheduleRunUtc { get; set; }

        // Process / Game Guard
        public bool EnableProcessGuard { get; set; } = true;
        public List<string> GuardedProcessNames { get; set; } = new()
        {
            "retroarch", "rpcs3", "pcsx2", "duckstation", "Dolphin", "obs64"
        };

        // Notifications
        public bool EnableToastNotifications { get; set; } = true;
        public bool NotifyOnNetworkDrop { get; set; } = true;
        public bool EnableDiscordWebhook { get; set; } = false;
        public string DiscordWebhookUrl { get; set; } = "";
        public bool DiscordNotifyQueueStart { get; set; } = true;
        public bool DiscordNotifyQueueFinish { get; set; } = true;
        public bool DiscordNotifyJobStart { get; set; } = false;
        public bool DiscordNotifyJobFinish { get; set; } = false;
        public bool DiscordNotifyTaskStart { get; set; } = false;
        public bool DiscordNotifyErrors { get; set; } = true;
        public string DiscordCustomMachineName { get; set; } = "";

        // Performance & Terminal Buffer
        public int TerminalMaxLines { get; set; } = 400;

        // Windows Watchdog & Crash Recovery
        public bool EnableWatchdog { get; set; } = false;
        public int WatchdogIntervalMinutes { get; set; } = 15;
        public bool AutoRestartInterruptedRunsOnStartup { get; set; } = false;

        // Job Builder Filtering, Sorting & View
        public string JobBuilderSortMode { get; set; } = "NameAsc";
        public string JobBuilderFilterModule { get; set; } = "All";
        public string JobBuilderViewMode { get; set; } = "Compact";

        // Terminal & Bottom Status Bar Verbosity Level & Custom Tag Filters
        public VerbosityLevel TerminalVerbosity { get; set; } = VerbosityLevel.Everything;
        public FooterVerbosityLevel FooterVerbosity { get; set; } = FooterVerbosityLevel.ProgressAndStatus;
        public CustomVerbosityConfig CustomVerbosity { get; set; } = new();
        public ManifestDropdownCatalogConfig ManifestDropdownCatalog { get; set; } = new();

        // Remote Windows Nodes & Terminal Panes
        public List<RemoteNodeConfig> RemoteNodes { get; set; } = new();
        public TerminalSplitMode SplitMode { get; set; } = TerminalSplitMode.Single;
        public string ActiveNodeIdPane1 { get; set; } = "local";
        public string ActiveNodeIdPane2 { get; set; } = "local";

        // Housekeeping 3.0 Web Server, Security, UI Theme/Density & Greenroom Integration
        public string PostRunPowerAction { get; set; } = "StayAwake"; // "StayAwake", "Sleep", "Shutdown"
        public WebServerConfig WebServer { get; set; } = new();
        public GreenroomIntegrationConfig GreenroomIntegration { get; set; } = new();

        // Named Job Queues (Multi-queue with independent schedules)
        public List<NamedJobQueue> NamedQueues { get; set; } = new();
        public string ActiveQueueId { get; set; } = "";

        // User-Defined Job Queue (Backward compatible wrapper accessing active named queue)
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("Jobs")]
        public List<HousekeepingJob>? RawJobs { get; set; }

        // Repository of all defined jobs across the application
        public List<HousekeepingJob> DefinedJobs { get; set; } = new();

        [JsonIgnore]
        public List<HousekeepingJob> Jobs
        {
            get => GetActiveQueue().Jobs;
            set
            {
                var q = GetActiveQueue();
                q.Jobs = value ?? new List<HousekeepingJob>();
            }
        }

        public void PrepareForSave()
        {
            // Omit redundant legacy Jobs array from JSON serialization
            RawJobs = null;

            // Strip unused module configurations on all jobs so only the active module is saved to disk
            if (DefinedJobs != null)
            {
                foreach (var j in DefinedJobs) j.StripUnusedModuleConfigs();
            }
            if (NamedQueues != null)
            {
                foreach (var q in NamedQueues)
                {
                    if (q.Jobs != null)
                    {
                        foreach (var j in q.Jobs) j.StripUnusedModuleConfigs();
                    }
                }
            }
        }

        // Generalized Module Configurations (Retained for legacy settings migration & default templates)
        public ProxyGeneratorConfig ProxyGenerator { get; set; } = ProxyGeneratorConfig.CreateDefault();
        public ArchiveCompressorConfig ArchiveCompressor { get; set; } = ArchiveCompressorConfig.CreateDefault();
        public MediaTranscoderConfig MediaTranscoder { get; set; } = MediaTranscoderConfig.CreateDefault();
        public RomLauncherGeneratorConfig RomLauncherGenerator { get; set; } = RomLauncherGeneratorConfig.CreateDefault();
        public CloudSyncConfig CloudSync { get; set; } = CloudSyncConfig.CreateDefault();
        public HumbleSyncConfig HumbleSync { get; set; } = HumbleSyncConfig.CreateDefault();
        public CustomScriptsConfig CustomScripts { get; set; } = CustomScriptsConfig.CreateDefault();
        public CollectionManifestConfig CollectionManifest { get; set; } = CollectionManifestConfig.CreateDefault();

        public static AppConfig CreateDefaults()
        {
            var cfg = new AppConfig();
            cfg.EnsureNamedQueuesInitialized();
            cfg.EnsureDefinedJobsSynchronized();
            return cfg;
        }

        public void EnsureDefinedJobsSynchronized()
        {
            EnsureNamedQueuesInitialized();
            DefinedJobs ??= new List<HousekeepingJob>();

            foreach (var q in NamedQueues)
            {
                for (int i = 0; i < q.Jobs.Count; i++)
                {
                    var existingDefined = DefinedJobs.FirstOrDefault(j => j.JobId == q.Jobs[i].JobId);
                    if (existingDefined == null)
                    {
                        DefinedJobs.Add(q.Jobs[i]);
                    }
                    else
                    {
                        q.Jobs[i] = existingDefined;
                    }
                }
            }
        }

        public void EnsureNamedQueuesInitialized()
        {
            NamedQueues ??= new List<NamedJobQueue>();
            if (NamedQueues.Count == 0)
            {
                var defaultQueue = new NamedJobQueue
                {
                    Id = "default-queue",
                    Name = "Default Queue",
                    Description = "Primary Job Queue",
                    Jobs = RawJobs != null && RawJobs.Count > 0 ? RawJobs : new List<HousekeepingJob>(),
                    EnableSchedule = EnableMasterSchedule,
                    ScheduleMode = MasterScheduleMode,
                    ScheduleDailyTime = MasterScheduleTime,
                    ScheduleIntervalMinutes = MasterScheduleIntervalMinutes,
                    ScheduleSpecificTimesCsv = MasterScheduleSpecificTimesCsv,
                    ScheduleDays = new List<DayOfWeek>(MasterScheduleDays ?? new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }),
                    LastRunUtc = LastMasterScheduleRunUtc
                };
                NamedQueues.Add(defaultQueue);
                ActiveQueueId = defaultQueue.Id;
            }

            if (string.IsNullOrWhiteSpace(ActiveQueueId) || !NamedQueues.Any(q => q.Id == ActiveQueueId))
            {
                ActiveQueueId = NamedQueues[0].Id;
            }
        }

        public NamedJobQueue GetActiveQueue()
        {
            EnsureNamedQueuesInitialized();
            var q = NamedQueues.FirstOrDefault(x => x.Id == ActiveQueueId);
            if (q == null)
            {
                q = NamedQueues[0];
                ActiveQueueId = q.Id;
            }
            return q;
        }

        public void SetActiveQueue(string queueId)
        {
            EnsureNamedQueuesInitialized();
            if (NamedQueues.Any(q => q.Id == queueId))
            {
                ActiveQueueId = queueId;
                RawJobs = GetActiveQueue().Jobs;
            }
        }

        public void MigrateLegacyModulesToJobsIfNeeded()
        {
            if ((Jobs != null && Jobs.Count > 0) || (DefinedJobs != null && DefinedJobs.Count > 0) || (NamedQueues != null && NamedQueues.Any(q => q.Jobs.Count > 0)))
                return;

            Jobs ??= new List<HousekeepingJob>();

            // Migrate any legacy module that the user already configured or enabled
            if (MediaTranscoder.Enabled || MediaTranscoder.DrivesToScan.Count > 0 || MediaTranscoder.CustomFoldersToScan.Count > 0)
            {
                Jobs.Add(new HousekeepingJob
                {
                    Name = "Media Transcoder",
                    ModuleType = ModuleId.MediaTranscoder,
                    Enabled = MediaTranscoder.Enabled,
                    RunInExternalCmd = MediaTranscoder.RunInExternalCmd,
                    MediaTranscoder = MediaTranscoder
                });
            }

            if (ProxyGenerator.Enabled || ProxyGenerator.Folders.Count > 0)
            {
                Jobs.Add(new HousekeepingJob
                {
                    Name = "Proxy Gen",
                    ModuleType = ModuleId.ProxyGenerator,
                    Enabled = ProxyGenerator.Enabled,
                    RunInExternalCmd = ProxyGenerator.RunInExternalCmd,
                    ProxyGenerator = ProxyGenerator
                });
            }

            if (ArchiveCompressor.Enabled || ArchiveCompressor.Mappings.Count > 0)
            {
                Jobs.Add(new HousekeepingJob
                {
                    Name = "Video Archiver",
                    ModuleType = ModuleId.ArchiveCompressor,
                    Enabled = ArchiveCompressor.Enabled,
                    RunInExternalCmd = ArchiveCompressor.RunInExternalCmd,
                    ArchiveCompressor = ArchiveCompressor
                });
            }

            if (RomLauncherGenerator.Enabled || RomLauncherGenerator.Collections.Count > 0 || !string.IsNullOrWhiteSpace(RomLauncherGenerator.LaunchersOutputRootPath))
            {
                Jobs.Add(new HousekeepingJob
                {
                    Name = "Game Launchers",
                    ModuleType = ModuleId.RomLauncherGenerator,
                    Enabled = RomLauncherGenerator.Enabled,
                    RunInExternalCmd = RomLauncherGenerator.RunInExternalCmd,
                    RomLauncherGenerator = RomLauncherGenerator
                });
            }

            if (CloudSync.Enabled || CloudSync.Jobs.Count > 0)
            {
                Jobs.Add(new HousekeepingJob
                {
                    Name = "Cloud Sync",
                    ModuleType = ModuleId.CloudSync,
                    Enabled = CloudSync.Enabled,
                    RunInExternalCmd = CloudSync.RunInExternalCmd,
                    CloudSync = CloudSync
                });
            }

            if (HumbleSync.Enabled || !string.IsNullOrWhiteSpace(HumbleSync.LibraryPath) || !string.IsNullOrWhiteSpace(HumbleSync.SessionCookie))
            {
                Jobs.Add(new HousekeepingJob
                {
                    Name = "Humble Library",
                    ModuleType = ModuleId.HumbleSync,
                    Enabled = HumbleSync.Enabled,
                    RunInExternalCmd = HumbleSync.RunInExternalCmd,
                    HumbleSync = HumbleSync
                });
            }

            if (CustomScripts.Enabled || CustomScripts.Tasks.Count > 0)
            {
                Jobs.Add(new HousekeepingJob
                {
                    Name = "Custom Tasks",
                    ModuleType = ModuleId.CustomScripts,
                    Enabled = CustomScripts.Enabled,
                    RunInExternalCmd = CustomScripts.RunInExternalCmd,
                    CustomScripts = CustomScripts
                });
            }
        }

        public bool IsModuleAppWideEnabled(ModuleId moduleId) => moduleId switch
        {
            ModuleId.ProxyGenerator => ProxyGenerator.Enabled,
            ModuleId.ArchiveCompressor => ArchiveCompressor.Enabled,
            ModuleId.MediaTranscoder => MediaTranscoder.Enabled,
            ModuleId.RomLauncherGenerator => RomLauncherGenerator.Enabled,
            ModuleId.CloudSync => CloudSync.Enabled,
            ModuleId.HumbleSync => HumbleSync.Enabled,
            ModuleId.CustomScripts => CustomScripts.Enabled,
            ModuleId.CollectionManifest => CollectionManifest.Enabled,
            _ => true
        };

        public void SetModuleAppWideEnabled(ModuleId moduleId, bool isEnabled)
        {
            switch (moduleId)
            {
                case ModuleId.ProxyGenerator: ProxyGenerator.Enabled = isEnabled; break;
                case ModuleId.ArchiveCompressor: ArchiveCompressor.Enabled = isEnabled; break;
                case ModuleId.MediaTranscoder: MediaTranscoder.Enabled = isEnabled; break;
                case ModuleId.RomLauncherGenerator: RomLauncherGenerator.Enabled = isEnabled; break;
                case ModuleId.CloudSync: CloudSync.Enabled = isEnabled; break;
                case ModuleId.HumbleSync: HumbleSync.Enabled = isEnabled; break;
                case ModuleId.CustomScripts: CustomScripts.Enabled = isEnabled; break;
                case ModuleId.CollectionManifest: CollectionManifest.Enabled = isEnabled; break;
            }
        }

        public AppConfig DeepClone()
        {
            string json = System.Text.Json.JsonSerializer.Serialize(this);
            var clone = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            clone.EnsureNamedQueuesInitialized();
            clone.EnsureDefinedJobsSynchronized();
            return clone;
        }

        public AppConfig CreateSanitizedCopyForSharing()
        {
            var copy = DeepClone();
            copy.HumbleSync.SessionCookie = "";
            copy.DiscordWebhookUrl = "";
            copy.WebServer.AdminPasswordHash = "";
            copy.WebServer.AdminPasswordSalt = "";
            copy.WebServer.NodeApiToken = "";
            copy.GreenroomIntegration.CompanionBearerToken = "";
            foreach (var node in copy.RemoteNodes)
            {
                node.Password = "";
                node.ApiToken = "";
            }
            foreach (var job in copy.DefinedJobs)
            {
                if (job.HumbleSync != null)
                {
                    job.HumbleSync.SessionCookie = "";
                }
            }
            foreach (var q in copy.NamedQueues)
            {
                foreach (var job in q.Jobs)
                {
                    if (job.HumbleSync != null)
                    {
                        job.HumbleSync.SessionCookie = "";
                    }
                }
            }
            return copy;
        }
    }

    public class WebServerConfig
    {
        public int Port { get; set; } = 5178;
        public bool BindLanAndTailnet { get; set; } = true;
        public string AdminPasswordHash { get; set; } = "";
        public string AdminPasswordSalt { get; set; } = "";
        public string NodeApiToken { get; set; } = "";
        public bool TrustTailscaleServeHeaders { get; set; } = true;
        public bool RestrictRawShellToLocalhost { get; set; } = false;
        public string DefaultDesktopLaunchMode { get; set; } = "StandaloneWindow"; // "StandaloneWindow" | "Browser"
        public string ThemePreference { get; set; } = "Dark"; // "Dark" | "Light" | "System"
        public string UiDensity { get; set; } = "Compact"; // "Compact" | "Comfortable" | "Touch"

        public void EnsureNodeApiTokenInitialized()
        {
            if (string.IsNullOrWhiteSpace(NodeApiToken) || NodeApiToken.Length < 24)
            {
                NodeApiToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            }
        }
    }

    public class GreenroomIntegrationConfig
    {
        public bool Enabled { get; set; } = true;
        public string CompanionBaseUrl { get; set; } = "http://127.0.0.1:8420";
        public int CompanionPort { get; set; } = 8420;
        public string CompanionBearerToken { get; set; } = "";
        public string GreenroomRootPath { get; set; } = "";
        public bool AutoSyncAfterRomManifestChanges { get; set; } = true;
        public bool AutoSyncAfterCollectionManifestChanges { get; set; } = true;
    }

    public class ToolPathsConfig : ObservableObject
    {
        public string HandBrakeCliPath { get; set; } = "";
        public string SevenZipPath { get; set; } = "";
        public string RclonePath { get; set; } = "";
        public string HumbleDownloaderPath { get; set; } = "";
    }

    public class HandBrakeEncodingProfile : ObservableObject
    {
        private string _profileId = "";
        private string _name = "";
        private string _containerExtension = ".mp4";
        private string _videoEncoder = "x265";
        private string _encoderPreset = "fast";
        private double _qualityRf = 22.0;
        private int _maxWidth = 1920;
        private int _maxHeight = 1080;
        private string _audioEncoder = "av_aac";
        private int _audioBitrateKbps = 160;
        private string _audioLanguageCode = "";
        private string _subtitleMode = "none"; // "none", "first", "burned"
        private bool _useGuiPresetOverride = false;
        private string _guiPresetName = "";
        private string _guiPresetFilePath = "";
        private string _extraCliFlags = "";

        public string ProfileId
        {
            get => _profileId;
            set { _profileId = value; OnPropertyChanged(); }
        }
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }
        public string ContainerExtension
        {
            get => _containerExtension;
            set { _containerExtension = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActiveModeSummary)); }
        }
        public string VideoEncoder
        {
            get => _videoEncoder;
            set { _videoEncoder = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActiveModeSummary)); }
        }
        public string EncoderPreset
        {
            get => _encoderPreset;
            set { _encoderPreset = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActiveModeSummary)); }
        }
        public double QualityRf
        {
            get => _qualityRf;
            set { _qualityRf = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActiveModeSummary)); }
        }
        public int MaxWidth
        {
            get => _maxWidth;
            set { _maxWidth = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActiveModeSummary)); }
        }
        public int MaxHeight
        {
            get => _maxHeight;
            set { _maxHeight = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActiveModeSummary)); }
        }
        public string AudioEncoder
        {
            get => _audioEncoder;
            set { _audioEncoder = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActiveModeSummary)); }
        }
        public int AudioBitrateKbps
        {
            get => _audioBitrateKbps;
            set { _audioBitrateKbps = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActiveModeSummary)); }
        }
        public string AudioLanguageCode
        {
            get => _audioLanguageCode;
            set { _audioLanguageCode = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActiveModeSummary)); }
        }
        public string SubtitleMode
        {
            get => _subtitleMode;
            set { _subtitleMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActiveModeSummary)); }
        }
        public bool UseGuiPresetOverride
        {
            get => _useGuiPresetOverride;
            set
            {
                _useGuiPresetOverride = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCustomMode));
                OnPropertyChanged(nameof(ActiveModeSummary));
            }
        }
        public string GuiPresetName
        {
            get => _guiPresetName;
            set
            {
                _guiPresetName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActiveModeSummary));
            }
        }
        public string GuiPresetFilePath
        {
            get => _guiPresetFilePath;
            set
            {
                _guiPresetFilePath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActiveModeSummary));
            }
        }
        public string ExtraCliFlags
        {
            get => _extraCliFlags;
            set { _extraCliFlags = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public bool IsCustomMode => !_useGuiPresetOverride;

        [JsonIgnore]
        public string ActiveModeSummary => _useGuiPresetOverride
            ? $"[HandBrake Preset] {(string.IsNullOrWhiteSpace(_guiPresetName) ? "(Enter or import preset name)" : _guiPresetName)}{(!string.IsNullOrWhiteSpace(_guiPresetFilePath) ? $" ({Path.GetFileName(_guiPresetFilePath)})" : " (Installed GUI)")}"
            : $"[Custom In-App] {_videoEncoder} ({_encoderPreset}, RF {_qualityRf:0.##}) • {_maxWidth}x{_maxHeight} • {_audioEncoder} {_audioBitrateKbps}k • Sub: {_subtitleMode}";

        public string BuildHandBrakeArguments(string inputFilePath, string outputFilePath, string? overrideAudioLang = null)
        {
            if (UseGuiPresetOverride && !string.IsNullOrWhiteSpace(GuiPresetName))
            {
                string importFlag = !string.IsNullOrWhiteSpace(GuiPresetFilePath) && File.Exists(GuiPresetFilePath)
                    ? $"--preset-import-file \"{GuiPresetFilePath}\""
                    : "--preset-import-gui";
                return $"{importFlag} -Z \"{GuiPresetName}\" -i \"{inputFilePath}\" -o \"{outputFilePath}\" {ExtraCliFlags}".Trim();
            }

            var args = new List<string>
            {
                $"-i \"{inputFilePath}\"",
                $"-o \"{outputFilePath}\"",
                $"-e {VideoEncoder}",
                $"--encoder-preset {EncoderPreset}",
                $"-q {QualityRf:0.##}"
            };

            if (MaxWidth > 0 && MaxHeight > 0)
            {
                args.Add($"-X {MaxWidth} -Y {MaxHeight} --loose-anamorphic");
            }

            string lang = !string.IsNullOrWhiteSpace(overrideAudioLang) ? overrideAudioLang : AudioLanguageCode;
            if (!string.IsNullOrWhiteSpace(lang))
            {
                args.Add($"--audio-lang-list {lang.ToLowerInvariant()} --first-audio");
            }

            if (!string.IsNullOrWhiteSpace(AudioEncoder))
            {
                args.Add($"-E {AudioEncoder}");
                if (!AudioEncoder.Contains("copy", StringComparison.OrdinalIgnoreCase) && AudioBitrateKbps > 0)
                {
                    args.Add($"-B {AudioBitrateKbps}");
                }
            }

            if (SubtitleMode.Equals("burned", StringComparison.OrdinalIgnoreCase))
            {
                args.Add("--subtitle 1 --subtitle-burned");
            }
            else if (SubtitleMode.Equals("first", StringComparison.OrdinalIgnoreCase))
            {
                args.Add("--subtitle 1");
            }

            if (!string.IsNullOrWhiteSpace(ExtraCliFlags))
            {
                args.Add(ExtraCliFlags.Trim());
            }

            return string.Join(" ", args);
        }

        public static List<HandBrakeEncodingProfile> ParseHandBrakePresetsJson(string jsonContent, string? externalPresetFilePath = null)
        {
            var results = new List<HandBrakeEncodingProfile>();
            if (string.IsNullOrWhiteSpace(jsonContent))
                return results;

            using var doc = JsonDocument.Parse(jsonContent);
            ExtractPresetsFromElement(doc.RootElement, externalPresetFilePath, results);
            return results;
        }

        private static void ExtractPresetsFromElement(JsonElement element, string? externalPresetFilePath, List<HandBrakeEncodingProfile> results)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in element.EnumerateArray())
                    ExtractPresetsFromElement(child, externalPresetFilePath, results);
                return;
            }

            if (element.ValueKind != JsonValueKind.Object)
                return;

            bool isFolder = element.TryGetProperty("Folder", out var folderProp) &&
                            folderProp.ValueKind == JsonValueKind.True;

            if (element.TryGetProperty("PresetList", out var presetListProp) &&
                presetListProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in presetListProp.EnumerateArray())
                    ExtractPresetsFromElement(child, externalPresetFilePath, results);
            }

            if (element.TryGetProperty("ChildrenArray", out var childrenArrayProp) &&
                childrenArrayProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in childrenArrayProp.EnumerateArray())
                    ExtractPresetsFromElement(child, externalPresetFilePath, results);
            }

            if (isFolder)
                return;

            if (!element.TryGetProperty("PresetName", out var nameProp) ||
                nameProp.ValueKind != JsonValueKind.String)
            {
                return;
            }

            string presetName = (nameProp.GetString() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(presetName))
                return;

            string container = ".mkv";
            if (element.TryGetProperty("FileFormat", out var fmtProp) && fmtProp.ValueKind == JsonValueKind.String)
            {
                string fmt = fmtProp.GetString() ?? "";
                if (fmt.Contains("mp4", StringComparison.OrdinalIgnoreCase))
                    container = ".mp4";
            }

            string videoEnc = "x265";
            if (element.TryGetProperty("VideoEncoder", out var veProp) && veProp.ValueKind == JsonValueKind.String)
            {
                string ve = veProp.GetString() ?? "";
                if (!string.IsNullOrWhiteSpace(ve))
                    videoEnc = ve;
            }

            string videoPreset = "medium";
            if (element.TryGetProperty("VideoPreset", out var vpProp) && vpProp.ValueKind == JsonValueKind.String)
            {
                string vp = vpProp.GetString() ?? "";
                if (!string.IsNullOrWhiteSpace(vp))
                    videoPreset = vp;
            }

            double rf = 21.0;
            if (element.TryGetProperty("VideoQualitySlider", out var rfProp) && rfProp.TryGetDouble(out double parsedRf))
            {
                rf = parsedRf;
            }

            int maxW = 1920;
            if (element.TryGetProperty("PictureWidth", out var pwProp) && pwProp.TryGetInt32(out int parsedW) && parsedW > 0)
            {
                maxW = parsedW;
            }

            int maxH = 1080;
            if (element.TryGetProperty("PictureHeight", out var phProp) && phProp.TryGetInt32(out int parsedH) && parsedH > 0)
            {
                maxH = parsedH;
            }

            results.Add(new HandBrakeEncodingProfile
            {
                ProfileId = presetName,
                Name = presetName,
                ContainerExtension = container,
                VideoEncoder = videoEnc,
                EncoderPreset = videoPreset,
                QualityRf = rf,
                MaxWidth = maxW,
                MaxHeight = maxH,
                UseGuiPresetOverride = true,
                GuiPresetName = presetName,
                GuiPresetFilePath = externalPresetFilePath ?? ""
            });
        }

        public static List<HandBrakeEncodingProfile> CreateDefaultProfiles() => new()
        {
            new() { ProfileId = "Proxy_360p", Name = "Fast Editing Proxy (360p)", ContainerExtension = ".mp4", VideoEncoder = "x264", EncoderPreset = "veryfast", QualityRf = 25, MaxWidth = 640, MaxHeight = 360, AudioEncoder = "av_aac", AudioBitrateKbps = 128 },
            new() { ProfileId = "Archive_LQ", Name = "Long-Term LQ Archive (HEVC)", ContainerExtension = ".mkv", VideoEncoder = "x265", EncoderPreset = "fast", QualityRf = 24, MaxWidth = 1920, MaxHeight = 1080, AudioEncoder = "av_aac", AudioBitrateKbps = 160 },
            new() { ProfileId = "Tier_144p", Name = "144p Ultra-Low Bandwidth (HEVC)", ContainerExtension = ".mkv", VideoEncoder = "x265", EncoderPreset = "fast", QualityRf = 24, MaxWidth = 256, MaxHeight = 144, AudioEncoder = "av_aac", AudioBitrateKbps = 96 },
            new() { ProfileId = "Tier_240p", Name = "240p Mobile Low (HEVC)", ContainerExtension = ".mkv", VideoEncoder = "x265", EncoderPreset = "fast", QualityRf = 23, MaxWidth = 426, MaxHeight = 240, AudioEncoder = "av_aac", AudioBitrateKbps = 112 },
            new() { ProfileId = "Tier_360p", Name = "360p SD Low (HEVC)", ContainerExtension = ".mkv", VideoEncoder = "x265", EncoderPreset = "fast", QualityRf = 23, MaxWidth = 640, MaxHeight = 360, AudioEncoder = "av_aac", AudioBitrateKbps = 128 },
            new() { ProfileId = "Tier_480p", Name = "480p Standard Definition (HEVC)", ContainerExtension = ".mkv", VideoEncoder = "x265", EncoderPreset = "medium", QualityRf = 21, MaxWidth = 854, MaxHeight = 480, AudioEncoder = "av_aac", AudioBitrateKbps = 160 },
            new() { ProfileId = "Tier_720p", Name = "720p HD Ready (HEVC)", ContainerExtension = ".mkv", VideoEncoder = "x265", EncoderPreset = "medium", QualityRf = 21, MaxWidth = 1280, MaxHeight = 720, AudioEncoder = "av_aac", AudioBitrateKbps = 192 },
            new() { ProfileId = "Tier_1080p", Name = "1080p Full HD (HEVC)", ContainerExtension = ".mkv", VideoEncoder = "x265", EncoderPreset = "medium", QualityRf = 20, MaxWidth = 1920, MaxHeight = 1080, AudioEncoder = "av_aac", AudioBitrateKbps = 224 },
            new() { ProfileId = "Tier_4K", Name = "4K UHD (HEVC)", ContainerExtension = ".mkv", VideoEncoder = "x265", EncoderPreset = "medium", QualityRf = 20, MaxWidth = 3840, MaxHeight = 2160, AudioEncoder = "ac3", AudioBitrateKbps = 320 },
            new() { ProfileId = "Lang_Edition_480p", Name = "Alternate Audio Language Edition (480p)", ContainerExtension = ".mkv", VideoEncoder = "x265", EncoderPreset = "medium", QualityRf = 21, MaxWidth = 854, MaxHeight = 480, AudioEncoder = "av_aac", AudioBitrateKbps = 160, SubtitleMode = "burned" }
        };
    }

    public abstract class ModuleBaseConfig : ObservableObject
    {
        private bool _enabled = false;
        private bool _runInExternalCmd = false;
        private bool _enableCustomSchedule = false;
        private int _scheduleIntervalMinutes = 0;
        private string _scheduleDailyTime = "04:00";

        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }
        public bool RunInExternalCmd
        {
            get => _runInExternalCmd;
            set { _runInExternalCmd = value; OnPropertyChanged(); }
        }
        public bool EnableCustomSchedule
        {
            get => _enableCustomSchedule;
            set { _enableCustomSchedule = value; OnPropertyChanged(); }
        }
        public int ScheduleIntervalMinutes
        {
            get => _scheduleIntervalMinutes;
            set { _scheduleIntervalMinutes = value; OnPropertyChanged(); }
        }
        public string ScheduleDailyTime
        {
            get => _scheduleDailyTime;
            set { _scheduleDailyTime = value; OnPropertyChanged(); }
        }

        public DateTime? LastScheduledRunUtc { get; set; }

        [JsonIgnore]
        public DateTime? LastScheduledRun
        {
            get => LastScheduledRunUtc?.ToLocalTime();
            set => LastScheduledRunUtc = value.HasValue
                ? (value.Value.Kind == DateTimeKind.Utc ? value.Value : value.Value.ToUniversalTime())
                : null;
        }
    }

    // ==================== MODULE 1: VIDEO PROXY GENERATOR ==================== //

    public class FolderTargetItem : ObservableObject
    {
        private bool _enabled = true;
        private string _path = "";

        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }
        public string Path
        {
            get => _path;
            set { _path = value; OnPropertyChanged(); }
        }
    }

    public class ProxyGeneratorConfig : ModuleBaseConfig
    {
        public string HandBrakeProfileId { get; set; } = "Proxy_360p";
        public string ProxySuffixTag { get; set; } = "360p_proxy";
        public List<string> Extensions { get; set; } = new() { ".mp4", ".mkv", ".mov" };
        public List<FolderTargetItem> Folders { get; set; } = new();

        public static ProxyGeneratorConfig CreateDefault() => new ProxyGeneratorConfig
        {
            Enabled = false,
            HandBrakeProfileId = "Proxy_360p",
            ProxySuffixTag = "360p_proxy",
            Extensions = new() { ".mp4", ".mkv", ".mov" },
            Folders = new()
        };
    }

    // ==================== MODULE 2: VIDEO ARCHIVE COMPRESSOR ==================== //

    public class ArchiveMappingItem : ObservableObject
    {
        private bool _enabled = true;
        private string _name = "";
        private string _sourcePath = "";
        private string _archiveOutputPath = "";

        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }
        public string SourcePath
        {
            get => _sourcePath;
            set { _sourcePath = value; OnPropertyChanged(); }
        }
        public string ArchiveOutputPath
        {
            get => _archiveOutputPath;
            set { _archiveOutputPath = value; OnPropertyChanged(); }
        }
    }

    public class ArchiveCompressorConfig : ModuleBaseConfig
    {
        public string HandBrakeProfileId { get; set; } = "Archive_LQ";
        public string OutputExtension { get; set; } = ".mkv";
        public string SkipFilesContainingTag { get; set; } = "_proxy)";
        public List<string> InputExtensions { get; set; } = new() { ".mp4", ".mkv", ".mov" };
        public List<ArchiveMappingItem> Mappings { get; set; } = new();

        public static ArchiveCompressorConfig CreateDefault() => new ArchiveCompressorConfig
        {
            Enabled = false,
            HandBrakeProfileId = "Archive_LQ",
            OutputExtension = ".mkv",
            SkipFilesContainingTag = "_proxy)",
            InputExtensions = new() { ".mp4", ".mkv", ".mov" },
            Mappings = new()
        };
    }

    // ==================== MODULE 3: MULTI-TIER MEDIA TRANSCODER ==================== //

    public class SourceQualityTierRule : ObservableObject
    {
        public const string DisabledOption = "(Disabled)";

        private bool _enabled = true;
        private string _tierName = "";
        private string _sourceTagsCsv = "";
        private int _maxSourceHeightPx = 1080;
        private string _profile144p = "Tier_144p";
        private string _profile240p = "Tier_240p";
        private string _profile360p = "Tier_360p";
        private string _profile480p = "Tier_480p";
        private string _profile720p = DisabledOption;
        private string _profile1080p = DisabledOption;
        private string _profile4K = DisabledOption;
        private string _targetResolutionsCsv = "";

        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }
        public string TierName
        {
            get => _tierName;
            set { _tierName = value; OnPropertyChanged(); }
        }
        public string SourceTagsCsv
        {
            get => _sourceTagsCsv;
            set { _sourceTagsCsv = value; OnPropertyChanged(); }
        }
        public int MaxSourceHeightPx
        {
            get => _maxSourceHeightPx;
            set { _maxSourceHeightPx = value; OnPropertyChanged(); }
        }
        public string Profile144p
        {
            get => _profile144p;
            set { _profile144p = value; OnPropertyChanged(); }
        }
        public string Profile240p
        {
            get => _profile240p;
            set { _profile240p = value; OnPropertyChanged(); }
        }
        public string Profile360p
        {
            get => _profile360p;
            set { _profile360p = value; OnPropertyChanged(); }
        }
        public string Profile480p
        {
            get => _profile480p;
            set { _profile480p = value; OnPropertyChanged(); }
        }
        public string Profile720p
        {
            get => _profile720p;
            set { _profile720p = value; OnPropertyChanged(); }
        }
        public string Profile1080p
        {
            get => _profile1080p;
            set { _profile1080p = value; OnPropertyChanged(); }
        }
        public string Profile4K
        {
            get => _profile4K;
            set { _profile4K = value; OnPropertyChanged(); }
        }

        // Kept for backward JSON compatibility if a custom CSV was provided
        public string TargetResolutionsCsv
        {
            get => _targetResolutionsCsv;
            set
            {
                _targetResolutionsCsv = value;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    var items = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (items.Contains("720p", StringComparer.OrdinalIgnoreCase) && _profile720p == DisabledOption)
                        _profile720p = "Tier_720p";
                    if (items.Contains("1080p", StringComparer.OrdinalIgnoreCase) && _profile1080p == DisabledOption)
                        _profile1080p = "Tier_1080p";
                    if (items.Contains("4K", StringComparer.OrdinalIgnoreCase) && _profile4K == DisabledOption)
                        _profile4K = "Tier_4K";
                }
                OnPropertyChanged();
            }
        }

        public List<(string ResTag, string ProfileId)> GetActiveResolutionProfiles(MediaTranscoderConfig cfg)
        {
            var result = new List<(string ResTag, string ProfileId)>();

            void AddIfEnabled(bool globalFlag, string resTag, string profileId)
            {
                if (!globalFlag) return;
                if (string.IsNullOrWhiteSpace(profileId) ||
                    profileId.Equals(DisabledOption, StringComparison.OrdinalIgnoreCase) ||
                    profileId.Equals("None", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                result.Add((resTag, profileId.Trim()));
            }

            AddIfEnabled(cfg.Enable144p, "144p", Profile144p);
            AddIfEnabled(cfg.Enable240p, "240p", Profile240p);
            AddIfEnabled(cfg.Enable360p, "360p", Profile360p);
            AddIfEnabled(cfg.Enable480p, "480p", Profile480p);
            AddIfEnabled(cfg.Enable720p, "720p", Profile720p);
            AddIfEnabled(cfg.Enable1080p, "1080p", Profile1080p);
            AddIfEnabled(cfg.Enable4K, "4K", Profile4K);

            return result;
        }
    }

    public class LanguageEditionRule : ObservableObject
    {
        private bool _enabled = false;
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }
        public string LanguageCode { get; set; } = "JPN"; // ISO 639-2 track code
        public string LanguageFolderPrefix { get; set; } = "Japanese"; // e.g., "Movies" -> "Japanese Movies"
        public string TargetResolutionTag { get; set; } = "480p";
        public string HandBrakeProfileId { get; set; } = "Lang_Edition_480p";
    }

    public class MediaTranscoderConfig : ModuleBaseConfig
    {
        public SourceDetectionMode DetectionMode { get; set; } = SourceDetectionMode.FilenameTagMatching;
        public string MasterFileTag { get; set; } = "(Original)";
        public bool RemoteModeSkipMediaInfo { get; set; } = false;
        public bool ReEncodeNonHevcOutputs { get; set; } = true;
        public string DriveSubfolderRelativePath { get; set; } = "Media";
        public List<string> DrivesToScan { get; set; } = new();
        public List<FolderTargetItem> CustomFoldersToScan { get; set; } = new();
        public List<string> Extensions { get; set; } = new() { "mkv", "mp4", "avi" };

        public List<SourceQualityTierRule> SourceTiers { get; set; } = new();
        public List<LanguageEditionRule> LanguageEditionRules { get; set; } = new();

        public bool Enable144p { get; set; } = true;
        public bool Enable240p { get; set; } = true;
        public bool Enable360p { get; set; } = true;
        public bool Enable480p { get; set; } = true;
        public bool Enable720p { get; set; } = true;
        public bool Enable1080p { get; set; } = true;
        public bool Enable4K { get; set; } = true;

        public static MediaTranscoderConfig CreateDefault() => new MediaTranscoderConfig
        {
            Enabled = false,
            DetectionMode = SourceDetectionMode.FilenameTagMatching,
            MasterFileTag = "(Original)",
            RemoteModeSkipMediaInfo = false,
            ReEncodeNonHevcOutputs = true,
            DriveSubfolderRelativePath = "Media",
            DrivesToScan = new(),
            CustomFoldersToScan = new(),
            Extensions = new() { "mkv", "mp4", "avi" },
            SourceTiers = new()
            {
                new()
                {
                    Enabled = true,
                    TierName = "SD / DVD Tier",
                    SourceTagsCsv = "DVD, SD",
                    MaxSourceHeightPx = 576,
                    Profile144p = "Tier_144p",
                    Profile240p = "Tier_240p",
                    Profile360p = "Tier_360p",
                    Profile480p = "Tier_480p",
                    Profile720p = SourceQualityTierRule.DisabledOption,
                    Profile1080p = SourceQualityTierRule.DisabledOption,
                    Profile4K = SourceQualityTierRule.DisabledOption
                },
                new()
                {
                    Enabled = true,
                    TierName = "HD / Blu-Ray Tier",
                    SourceTagsCsv = "BR, BluRay, BDRip, HD",
                    MaxSourceHeightPx = 1080,
                    Profile144p = "Tier_144p",
                    Profile240p = "Tier_240p",
                    Profile360p = "Tier_360p",
                    Profile480p = "Tier_480p",
                    Profile720p = "Tier_720p",
                    Profile1080p = "Tier_1080p",
                    Profile4K = SourceQualityTierRule.DisabledOption
                },
                new()
                {
                    Enabled = true,
                    TierName = "UHD / 4K Tier",
                    SourceTagsCsv = "UHD, 4K, 2160p",
                    MaxSourceHeightPx = 4320,
                    Profile144p = "Tier_144p",
                    Profile240p = "Tier_240p",
                    Profile360p = "Tier_360p",
                    Profile480p = "Tier_480p",
                    Profile720p = "Tier_720p",
                    Profile1080p = "Tier_1080p",
                    Profile4K = "Tier_4K"
                }
            },
            LanguageEditionRules = new()
            {
                new() { Enabled = false, LanguageCode = "JPN", LanguageFolderPrefix = "Japanese", TargetResolutionTag = "480p", HandBrakeProfileId = "Lang_Edition_480p" }
            }
        };
    }

    // ==================== MODULE 4: ROM & EMULATOR LAUNCHER GENERATOR ==================== //

    public class GameSyncPathItem : ObservableObject
    {
        private bool _enabled = true;
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }

        private string _label = "Saves";
        public string Label
        {
            get => _label;
            set { _label = value; OnPropertyChanged(); }
        }

        private string _localPath = "";
        public string LocalPath
        {
            get => _localPath;
            set { _localPath = value; OnPropertyChanged(); }
        }

        private string _remoteTarget = "";
        public string RemoteTarget
        {
            get => _remoteTarget;
            set { _remoteTarget = value; OnPropertyChanged(); }
        }

        private string _syncMode = "bisync";
        public string SyncMode
        {
            get => _syncMode;
            set { _syncMode = value; OnPropertyChanged(); }
        }
    }

    public class ConsoleCollectionRule : ObservableObject
    {
        private bool _enabled = true;
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }

        private string _consoleName = "";
        public string ConsoleName
        {
            get => _consoleName;
            set { _consoleName = value; OnPropertyChanged(); }
        }

        private string _archivesPath = "";
        public string ArchivesPath
        {
            get => _archivesPath;
            set { _archivesPath = value; OnPropertyChanged(); }
        }

        private string _emulatorRunnerType = "RetroArch";
        public string EmulatorRunnerType
        {
            get => _emulatorRunnerType;
            set { _emulatorRunnerType = value; OnPropertyChanged(); }
        }

        private string _emulatorExecutablePath = "retroarch.exe";
        public string EmulatorExecutablePath
        {
            get => _emulatorExecutablePath;
            set { _emulatorExecutablePath = value; OnPropertyChanged(); }
        }

        private string _coreOrCustomArgs = "";
        public string CoreOrCustomArgs
        {
            get => _coreOrCustomArgs;
            set { _coreOrCustomArgs = value; OnPropertyChanged(); }
        }

        private string _extensionsCsv = "";
        public string ExtensionsCsv
        {
            get => _extensionsCsv;
            set { _extensionsCsv = value; OnPropertyChanged(); }
        }

        private string _subFolder = "";
        public string SubFolder
        {
            get => _subFolder;
            set { _subFolder = value; OnPropertyChanged(); }
        }

        private bool _overrideJobSyncPaths = false;
        public bool OverrideJobSyncPaths
        {
            get => _overrideJobSyncPaths;
            set { _overrideJobSyncPaths = value; OnPropertyChanged(); }
        }

        private string _complianceTemplateId = "auto-community";
        public string ComplianceTemplateId
        {
            get => _complianceTemplateId;
            set { _complianceTemplateId = string.IsNullOrWhiteSpace(value) ? "auto-community" : value; OnPropertyChanged(); }
        }

        public List<GameSyncPathItem> CustomSyncPaths { get; set; } = new();
    }

    public class RomLauncherGeneratorConfig : ModuleBaseConfig
    {
        public bool AutoGenerateM3uForMultiDisc { get; set; } = true;
        public bool AutoCompressUnarchivedRomsWith7z { get; set; } = false;
        public bool DeleteUncompressedOriginalsAfter7z { get; set; } = false;
        public bool UseFast7zListInspection { get; set; } = true;
        public bool ExtractArchiveOnLaunch { get; set; } = true;
        public bool CreateStartMenuShortcuts { get; set; } = false;
        public bool CreateLocalLauncherNextToArchive { get; set; } = false;
        public bool RefreshExistingLaunchers { get; set; } = false;

        public string LaunchersOutputRootPath { get; set; } = "";
        public string TempExtractionScratchPath { get; set; } = Path.Combine(Path.GetTempPath(), "HousekeepingRomScratch");
        public string StartMenuShortcutsPath { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs",
            "Games");

        public bool EnablePrePostLaunchSyncCommands { get; set; } = false;
        public bool UseCustomPrePostLaunchCommands { get; set; } = false;
        public string SaveSyncLocalFolder { get; set; } = "";
        public string SaveSyncCloudRemote { get; set; } = "";
        public string SaveSyncMode { get; set; } = "bisync"; // "bisync", "sync", "copy"
        public string PreLaunchCommand { get; set; } = "";
        public string PostLaunchCommand { get; set; } = "";

        // Multi-Path Sync Collections
        public List<GameSyncPathItem> DefaultSyncPaths { get; set; } = new();

        // ROM Collection Verification Manifest & Redump/No-Intro Auditing
        public bool EnableCollectionManifestCsv { get; set; } = true;
        public string ManifestCsvFileName { get; set; } = "collection_manifest.csv";
        public string DatFilesDirectory { get; set; } = "./dats/";
        public bool AutoDownloadMissingDats { get; set; } = true;
        public bool ForceRehashExistingManifestEntries { get; set; } = false;
        public bool EnableSecondaryManifestStorage { get; set; } = false;
        public string SecondaryManifestDirectory { get; set; } = "";

        // Checksum Sidecar Placement, Fast Header CRC32 & Deep Inner-ROM Hashing
        // Legacy compatibility alias; Module 8 / ShadowLibrary manages .hash sidecars universally
        public HashSidecarMode HashFilePlacement { get; set; } = HashSidecarMode.SidecarBeside;
        public bool DeepInspectAndHashInnerRoms { get; set; } = true;
        public bool TrustExistingHashFiles { get; set; } = true;
        public bool UseFastArchiveHeaderCrc32Audit { get; set; } = false;
        public AuditStructureMode StructureMode { get; set; } = AuditStructureMode.Auto;

        // Archive Structure Templates & Compliance Auditing
        public bool EnableArchiveTemplateComplianceScan { get; set; } = true;
        public bool AutoSyncGreenroomOnManifestChange { get; set; } = true;
        public List<ArchiveComplianceTemplate> ArchiveTemplates { get; set; } = ArchiveComplianceCatalog.CreateBuiltInTemplates();

        public void EnsureArchiveTemplatesInitialized()
        {
            ArchiveTemplates = ArchiveComplianceCatalog.EnsureBuiltInsMerged(ArchiveTemplates);
        }

        public void SanitizeAutoGeneratedCommandOverrides()
        {
            if (UseCustomPrePostLaunchCommands)
                return;

            bool hasSyncRows = DefaultSyncPaths.Count > 0 || !string.IsNullOrWhiteSpace(SaveSyncLocalFolder);
            if (!hasSyncRows)
                return;

            if (!string.IsNullOrWhiteSpace(PreLaunchCommand) && LooksLikeAutoGeneratedRclonePreview(PreLaunchCommand))
                PreLaunchCommand = "";
            if (!string.IsNullOrWhiteSpace(PostLaunchCommand) && LooksLikeAutoGeneratedRclonePreview(PostLaunchCommand))
                PostLaunchCommand = "";
        }

        private static bool LooksLikeAutoGeneratedRclonePreview(string cmd)
        {
            string trimmed = cmd.Trim();
            return trimmed.Contains("rclone", StringComparison.OrdinalIgnoreCase) &&
                   (trimmed.Contains("--create-empty-src-dirs", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Contains("--resilient", StringComparison.OrdinalIgnoreCase));
        }

        public (List<string> PreCmds, List<string> PostCmds) ResolveAllSyncCommands(string rcloneExe, ConsoleCollectionRule? col = null)
        {
            string exe = string.IsNullOrWhiteSpace(rcloneExe) ? "rclone.exe" : rcloneExe;
            var preCmds = new List<string>();
            var postCmds = new List<string>();

            // Determine active sync paths: per-console override or job defaults
            var activePaths = (col != null && col.OverrideJobSyncPaths && col.CustomSyncPaths.Count > 0)
                ? col.CustomSyncPaths
                : DefaultSyncPaths;

            bool hasConfiguredSyncRows = activePaths.Count > 0 ||
                (!string.IsNullOrWhiteSpace(SaveSyncLocalFolder) && !string.IsNullOrWhiteSpace(SaveSyncCloudRemote));

            // Use manual custom CLI overrides ONLY when explicitly enabled, OR when no sync rows exist at all (legacy standalone script)
            bool honorManualCliOverride = UseCustomPrePostLaunchCommands ||
                (!hasConfiguredSyncRows && (!string.IsNullOrWhiteSpace(PreLaunchCommand) || !string.IsNullOrWhiteSpace(PostLaunchCommand)));

            if (honorManualCliOverride)
            {
                if (!string.IsNullOrWhiteSpace(PreLaunchCommand))
                    preCmds.Add(PreLaunchCommand.Trim());
                if (!string.IsNullOrWhiteSpace(PostLaunchCommand))
                    postCmds.Add(PostLaunchCommand.Trim());

                if (preCmds.Count > 0 || postCmds.Count > 0)
                    return (preCmds, postCmds);
            }

            // Auto-migrate legacy single folder settings if DefaultSyncPaths is empty
            if (activePaths.Count == 0 && !string.IsNullOrWhiteSpace(SaveSyncLocalFolder) && !string.IsNullOrWhiteSpace(SaveSyncCloudRemote))
            {
                activePaths = new List<GameSyncPathItem>
                {
                    new() { Enabled = true, Label = "Saves", LocalPath = SaveSyncLocalFolder, RemoteTarget = SaveSyncCloudRemote, SyncMode = SaveSyncMode ?? "bisync" }
                };
            }

            foreach (var item in activePaths.Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.LocalPath) && !string.IsNullOrWhiteSpace(p.RemoteTarget)))
            {
                string local = item.LocalPath.Trim();
                string remote = item.RemoteTarget.Trim();
                string mode = (item.SyncMode ?? "bisync").Trim().ToLowerInvariant();

                string pre = mode switch
                {
                    "sync" or "copy" => $"\"{exe}\" {mode} \"{remote}\" \"{local}\" --create-empty-src-dirs --verbose",
                    _ => $"\"{exe}\" bisync \"{local}\" \"{remote}\" --create-empty-src-dirs --resilient --max-lock 2h --verbose"
                };

                string post = mode switch
                {
                    "sync" or "copy" => $"\"{exe}\" {mode} \"{local}\" \"{remote}\" --create-empty-src-dirs --verbose",
                    _ => $"\"{exe}\" bisync \"{local}\" \"{remote}\" --create-empty-src-dirs --resilient --max-lock 2h --verbose"
                };

                preCmds.Add(pre);
                postCmds.Add(post);
            }

            return (preCmds, postCmds);
        }

        public (string PreCmd, string PostCmd) ResolvePreAndPostSyncCommands(string rcloneExe)
        {
            var (pre, post) = ResolveAllSyncCommands(rcloneExe, null);
            return (string.Join(" & ", pre), string.Join(" & ", post));
        }

        public List<ConsoleCollectionRule> Collections { get; set; } = new();

        public static RomLauncherGeneratorConfig CreateDefault() => new RomLauncherGeneratorConfig
        {
            Enabled = false,
            AutoGenerateM3uForMultiDisc = true,
            AutoCompressUnarchivedRomsWith7z = false,
            DeleteUncompressedOriginalsAfter7z = false,
            UseFast7zListInspection = true,
            ExtractArchiveOnLaunch = true,
            CreateStartMenuShortcuts = false,
            CreateLocalLauncherNextToArchive = false,
            RefreshExistingLaunchers = false,
            EnableCollectionManifestCsv = true,
            ManifestCsvFileName = "collection_manifest.csv",
            DatFilesDirectory = "./dats/",
            AutoDownloadMissingDats = true,
            ForceRehashExistingManifestEntries = false,
            EnableSecondaryManifestStorage = false,
            SecondaryManifestDirectory = "",
            Collections = new()
        };

        public static List<ConsoleCollectionRule> CreateStandardTemplates(string romsRoot, string coresRoot, string retroArchExe)
        {
            string r = string.IsNullOrWhiteSpace(romsRoot) ? @"C:\ROMs" : romsRoot.TrimEnd('\\');
            string c = string.IsNullOrWhiteSpace(coresRoot) ? @"C:\RetroArch\cores" : coresRoot.TrimEnd('\\');
            string exe = string.IsNullOrWhiteSpace(retroArchExe) ? "retroarch.exe" : retroArchExe;

            return new List<ConsoleCollectionRule>
            {
                new() { Enabled = true, ConsoleName = "GameBoy", SubFolder = "GB", EmulatorRunnerType = "RetroArch", EmulatorExecutablePath = exe, CoreOrCustomArgs = Path.Combine(c, "gambatte_libretro.dll"), ExtensionsCsv = ".gb", ArchivesPath = Path.Combine(r, "Nintendo", "GameBoy") },
                new() { Enabled = true, ConsoleName = "GBA", SubFolder = "GBA", EmulatorRunnerType = "RetroArch", EmulatorExecutablePath = exe, CoreOrCustomArgs = Path.Combine(c, "mgba_libretro.dll"), ExtensionsCsv = ".gba", ArchivesPath = Path.Combine(r, "Nintendo", "GameBoy Advanced") },
                new() { Enabled = true, ConsoleName = "GBC", SubFolder = "GBC", EmulatorRunnerType = "RetroArch", EmulatorExecutablePath = exe, CoreOrCustomArgs = Path.Combine(c, "gambatte_libretro.dll"), ExtensionsCsv = ".gbc, .gb", ArchivesPath = Path.Combine(r, "Nintendo", "GameBoy Color") },
                new() { Enabled = true, ConsoleName = "NES", SubFolder = "NES", EmulatorRunnerType = "RetroArch", EmulatorExecutablePath = exe, CoreOrCustomArgs = Path.Combine(c, "fceumm_libretro.dll"), ExtensionsCsv = ".nes, .fds", ArchivesPath = Path.Combine(r, "Nintendo", "Nintendo") },
                new() { Enabled = true, ConsoleName = "SNES", SubFolder = "SNES", EmulatorRunnerType = "RetroArch", EmulatorExecutablePath = exe, CoreOrCustomArgs = Path.Combine(c, "snes9x_libretro.dll"), ExtensionsCsv = ".sfc, .smc", ArchivesPath = Path.Combine(r, "Nintendo", "Super Nintendo") },
                new() { Enabled = true, ConsoleName = "N64", SubFolder = "N64", EmulatorRunnerType = "RetroArch", EmulatorExecutablePath = exe, CoreOrCustomArgs = Path.Combine(c, "parallel_n64_libretro.dll"), ExtensionsCsv = ".z64, .n64, .v64", ArchivesPath = Path.Combine(r, "Nintendo", "Nintendo 64") },
                new() { Enabled = true, ConsoleName = "DS", SubFolder = "NDS", EmulatorRunnerType = "RetroArch", EmulatorExecutablePath = exe, CoreOrCustomArgs = Path.Combine(c, "desmume_libretro.dll"), ExtensionsCsv = ".nds", ArchivesPath = Path.Combine(r, "Nintendo", "Nintendo DS") },
                new() { Enabled = true, ConsoleName = "GameCube", SubFolder = "GameCube", EmulatorRunnerType = "RetroArch", EmulatorExecutablePath = exe, CoreOrCustomArgs = Path.Combine(c, "dolphin_libretro.dll"), ExtensionsCsv = ".m3u, .rvz, .ciso, .iso, .gcm", ArchivesPath = Path.Combine(r, "Nintendo", "Gamecube") },
                new() { Enabled = true, ConsoleName = "Wii", SubFolder = "Wii", EmulatorRunnerType = "RetroArch", EmulatorExecutablePath = exe, CoreOrCustomArgs = Path.Combine(c, "dolphin_libretro.dll"), ExtensionsCsv = ".m3u, .rvz, .wbfs, .ciso, .iso", ArchivesPath = Path.Combine(r, "Nintendo", "Wii") },
                new() { Enabled = true, ConsoleName = "Genesis", SubFolder = "Genesis", EmulatorRunnerType = "RetroArch", EmulatorExecutablePath = exe, CoreOrCustomArgs = Path.Combine(c, "genesis_plus_gx_libretro.dll"), ExtensionsCsv = ".md, .gen, .bin", ArchivesPath = Path.Combine(r, "Sega", "Genesis - Megadrive") },
                new() { Enabled = true, ConsoleName = "Sega 32x", SubFolder = "Sega32x", EmulatorRunnerType = "RetroArch", EmulatorExecutablePath = exe, CoreOrCustomArgs = Path.Combine(c, "picodrive_libretro.dll"), ExtensionsCsv = ".32x", ArchivesPath = Path.Combine(r, "Sega", "Sega 32x") },
                new() { Enabled = true, ConsoleName = "Sega CD", SubFolder = "SegaCD", EmulatorRunnerType = "RetroArch", EmulatorExecutablePath = exe, CoreOrCustomArgs = Path.Combine(c, "picodrive_libretro.dll"), ExtensionsCsv = ".m3u, .chd, .cue, .iso, .ciso, .bin, .img", ArchivesPath = Path.Combine(r, "Sega", "Sega CD") },
                new() { Enabled = true, ConsoleName = "Dreamcast", SubFolder = "Dreamcast", EmulatorRunnerType = "RetroArch", EmulatorExecutablePath = exe, CoreOrCustomArgs = Path.Combine(c, "flycast_libretro.dll"), ExtensionsCsv = ".m3u, .chd, .gdi, .cdi, .cue, .iso, .bin, .img", ArchivesPath = Path.Combine(r, "Sega", "Dreamcast") },
                new() { Enabled = true, ConsoleName = "PlayStation", SubFolder = "PS1", EmulatorRunnerType = "RetroArch", EmulatorExecutablePath = exe, CoreOrCustomArgs = Path.Combine(c, "swanstation_libretro.dll"), ExtensionsCsv = ".m3u, .chd, .pbp, .cue, .iso, .ciso, .bin, .img", ArchivesPath = Path.Combine(r, "Sony", "PlayStation") },
                new() { Enabled = true, ConsoleName = "PS2", SubFolder = "PS2", EmulatorRunnerType = "RetroArch", EmulatorExecutablePath = exe, CoreOrCustomArgs = Path.Combine(c, "pcsx2_libretro.dll"), ExtensionsCsv = ".m3u, .chd, .iso, .ciso, .bin, .img, .cue", ArchivesPath = Path.Combine(r, "Sony", "PlayStation 2") },
                new() { Enabled = true, ConsoleName = "PS3", SubFolder = "PS3", EmulatorRunnerType = "RPCS3", EmulatorExecutablePath = "rpcs3.exe", CoreOrCustomArgs = "", ExtensionsCsv = "eboot.bin", ArchivesPath = Path.Combine(r, "Sony", "PlayStation 3") },
                new() { Enabled = true, ConsoleName = "PSP", SubFolder = "PSP", EmulatorRunnerType = "RetroArch", EmulatorExecutablePath = exe, CoreOrCustomArgs = Path.Combine(c, "ppsspp_libretro.dll"), ExtensionsCsv = ".m3u, .chd, .cso, .ciso, .iso, .pbp, .img", ArchivesPath = Path.Combine(r, "Sony", "PSP") },
                new() { Enabled = false, ConsoleName = "Xbox", SubFolder = "Xbox", EmulatorRunnerType = "Xemu", EmulatorExecutablePath = "xemu.exe", CoreOrCustomArgs = "", ExtensionsCsv = ".iso, .xiso", ArchivesPath = Path.Combine(r, "Microsoft", "Xbox") }
            };
        }
    }

    // ==================== MODULE 5: CLOUD FOLDER SYNC (RCLONE) ==================== //

    public class CloudSyncJobItem : ObservableObject
    {
        private bool _enabled = true;
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }

        private string _jobName = "Cloud Folder Sync";
        public string JobName
        {
            get => _jobName;
            set { _jobName = value; OnPropertyChanged(); }
        }

        private string _syncMode = "bisync";
        public string SyncMode
        {
            get => _syncMode;
            set { _syncMode = value; OnPropertyChanged(); }
        }

        private string _localPath = "";
        public string LocalPath
        {
            get => _localPath;
            set { _localPath = value; OnPropertyChanged(); }
        }

        private string _remoteTarget = "";
        public string RemoteTarget
        {
            get => _remoteTarget;
            set { _remoteTarget = value; OnPropertyChanged(); }
        }

        private string _flags = "--create-empty-src-dirs --resilient --verbose";
        public string Flags
        {
            get => _flags;
            set { _flags = value; OnPropertyChanged(); }
        }
    }

    public class CloudSyncConfig : ModuleBaseConfig
    {
        public List<CloudSyncJobItem> Jobs { get; set; } = new();

        public static CloudSyncConfig CreateDefault() => new CloudSyncConfig
        {
            Enabled = false,
            Jobs = new()
        };
    }

    // ==================== MODULE 6: HUMBLE BUNDLE LIBRARY SYNC ==================== //

    public class HumbleSyncConfig : ModuleBaseConfig
    {
        public string SessionCookie { get; set; } = "";
        public string LibraryPath { get; set; } = "";
        public string AdditionalArgs { get; set; } = "--progress";

        public static HumbleSyncConfig CreateDefault() => new HumbleSyncConfig
        {
            Enabled = false,
            SessionCookie = "",
            LibraryPath = "",
            AdditionalArgs = "--progress"
        };
    }

    // ==================== MODULE 7: CUSTOM SCRIPTS & CLI TASKS ==================== //

    public class CustomTaskItem : ObservableObject
    {
        private bool _enabled = true;
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }
        public string Name { get; set; } = "";
        public string CommandOrScriptPath { get; set; } = "";
        public string Arguments { get; set; } = "";
        public string WorkingDirectory { get; set; } = "";
        public string RequiredPathCheck { get; set; } = "";
        public bool RunInExternalCmd { get; set; } = false;
    }

    public class CustomScriptsConfig : ModuleBaseConfig
    {
        public List<CustomTaskItem> Tasks { get; set; } = new();

        public static CustomScriptsConfig CreateDefault() => new CustomScriptsConfig
        {
            Enabled = false,
            Tasks = new()
        };
    }

    public enum ManifestInspectTarget
    {
        FileName,
        FileNameWithoutExtension,
        ParentFolderName,
        RelativePath,
        FullPath
    }

    public enum ManifestMatchMode
    {
        RegexNamedCapture,
        RegexPattern,
        TextContains,
        SplitToken
    }

    public class ManifestFieldParseRule : ObservableObject
    {
        private string _id = Guid.NewGuid().ToString("N")[..8];
        private bool _enabled = true;
        private string _columnName = "CustomField";
        private string _fileExtensionFilter = "*";
        private ManifestInspectTarget _inspectTarget = ManifestInspectTarget.FileName;
        private ManifestMatchMode _matchMode = ManifestMatchMode.RegexNamedCapture;
        private string _pattern = "";
        private string _assignedValue = "";
        private string _splitDelimiter = " - ";
        private int _splitIndex = 0;

        public string Id { get => _id; set { _id = value; OnPropertyChanged(); } }
        public bool Enabled { get => _enabled; set { _enabled = value; OnPropertyChanged(); } }
        public string ColumnName { get => _columnName; set { _columnName = value; OnPropertyChanged(); } }
        public string FileExtensionFilter { get => _fileExtensionFilter; set { _fileExtensionFilter = value; OnPropertyChanged(); } }
        public ManifestInspectTarget InspectTarget { get => _inspectTarget; set { _inspectTarget = value; OnPropertyChanged(); } }
        public ManifestMatchMode MatchMode { get => _matchMode; set { _matchMode = value; OnPropertyChanged(); } }
        public string Pattern { get => _pattern; set { _pattern = value; OnPropertyChanged(); } }
        public string AssignedValue { get => _assignedValue; set { _assignedValue = value; OnPropertyChanged(); } }
        public string SplitDelimiter { get => _splitDelimiter; set { _splitDelimiter = value; OnPropertyChanged(); } }
        public int SplitIndex { get => _splitIndex; set { _splitIndex = value; OnPropertyChanged(); } }
    }

    public class ManifestTargetFolderRule : ObservableObject
    {
        private bool _enabled = true;
        private string _locationId = "Local";
        private string _label = "My Collection";
        private string _folderPath = "";
        private bool _includeSubdirectories = true;
        private int _maxSubdirectoryDepth = 0; // 0 = unlimited
        private string _systemSelection = "Auto-Detect";
        private AuditStructureMode _structureMode = AuditStructureMode.Auto;
        private bool _treatSubfoldersAsGameUnits = false;
        private string _extensionsFilter = "";
        private string _excludedFolderNames = "sample,extras,.deleted,$RECYCLE.BIN,@eaDir";
        private long _minimumFileSizeBytes = 0;
        private DateTime? _lastAuditRunUtc;
        private int _lastMatchCount;
        private int _lastMismatchCount;
        private int _lastTotalCount;

        public bool Enabled { get => _enabled; set { _enabled = value; OnPropertyChanged(); } }
        public string LocationId { get => _locationId; set { _locationId = value; OnPropertyChanged(); } }
        public string Label { get => _label; set { _label = value; OnPropertyChanged(); } }
        public string FolderPath { get => _folderPath; set { _folderPath = value; OnPropertyChanged(); } }
        public bool IncludeSubdirectories { get => _includeSubdirectories; set { _includeSubdirectories = value; OnPropertyChanged(); } }
        public int MaxSubdirectoryDepth { get => _maxSubdirectoryDepth; set { _maxSubdirectoryDepth = value; OnPropertyChanged(); } }
        public string SystemSelection { get => _systemSelection; set { _systemSelection = value; OnPropertyChanged(); } }
        public AuditStructureMode StructureMode
        {
            get => _structureMode;
            set
            {
                _structureMode = value;
                _treatSubfoldersAsGameUnits = (value == AuditStructureMode.FolderDumpsOnly);
                OnPropertyChanged();
                OnPropertyChanged(nameof(TreatSubfoldersAsGameUnits));
            }
        }
        public bool TreatSubfoldersAsGameUnits
        {
            get => _structureMode == AuditStructureMode.FolderDumpsOnly || _treatSubfoldersAsGameUnits;
            set
            {
                _treatSubfoldersAsGameUnits = value;
                _structureMode = value ? AuditStructureMode.FolderDumpsOnly : AuditStructureMode.Auto;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StructureMode));
            }
        }
        public string ExtensionsFilter { get => _extensionsFilter; set { _extensionsFilter = value; OnPropertyChanged(); } }
        public string ExcludedFolderNames { get => _excludedFolderNames; set { _excludedFolderNames = value; OnPropertyChanged(); } }
        public long MinimumFileSizeBytes { get => _minimumFileSizeBytes; set { _minimumFileSizeBytes = value; OnPropertyChanged(); } }
        public DateTime? LastAuditRunUtc { get => _lastAuditRunUtc; set { _lastAuditRunUtc = value; OnPropertyChanged(); } }
        public int LastMatchCount { get => _lastMatchCount; set { _lastMatchCount = value; OnPropertyChanged(); } }
        public int LastMismatchCount { get => _lastMismatchCount; set { _lastMismatchCount = value; OnPropertyChanged(); } }
        public int LastTotalCount { get => _lastTotalCount; set { _lastTotalCount = value; OnPropertyChanged(); } }
    }

    public class CollectionManifestConfig : ModuleBaseConfig
    {
        public List<ManifestTargetFolderRule> TargetFolders { get; set; } = new();
        public List<ManifestFieldParseRule> FieldParseRules { get; set; } = new();
        public bool UnifiedMasterManifest { get; set; } = true;
        public string UnifiedManifestName { get; set; } = "Master_Collection_Manifest";
        public string UnifiedManifestOutputFolder { get; set; } = "";

        // Core Bit Rot & Integrity Engine
        public bool EnableBitRotDetection { get; set; } = true;
        public bool CreateMissingHashFiles { get; set; } = false;
        public bool EnableBitRotAutoHeal { get; set; } = false;
        public bool CheckInnerArchiveAndContainer { get; set; } = true;
        public HashSidecarMode HashFilePlacement { get; set; } = HashSidecarMode.SidecarBeside;
        public string HashAlgorithm { get; set; } = "SHA-1"; // SHA-1, CRC32, MD5, SHA-256

        // Legacy / Compatibility Aliases
        public bool EnableFileHashing { get => EnableBitRotDetection; set => EnableBitRotDetection = value; }
        public bool SkipExistingVerifiedHashes { get; set; } = true;
        public bool GenerateHashSidecars { get => CreateMissingHashFiles; set => CreateMissingHashFiles = value; }
        public bool PackageHashIntoArchives { get => HashFilePlacement == HashSidecarMode.PackageInsideArchive; set { if (value) HashFilePlacement = HashSidecarMode.PackageInsideArchive; } }
        public bool DeepInspectAndHashInnerRoms { get => CheckInnerArchiveAndContainer; set => CheckInnerArchiveAndContainer = value; }
        public bool TrustExistingHashFiles { get; set; } = true;
        public AuditStructureMode StructureMode { get; set; } = AuditStructureMode.Auto;

        // Formatting & Output
        public string CsvDelimiter { get; set; } = ",";
        public string ExcludedExtensions { get; set; } = ".nfo, .txt, .sfv, .srr, .jpg, .png";
        public int LogCadenceItems { get; set; } = 50;
        public int LogCadenceSeconds { get; set; } = 5;
        public long LargeFileNoticeThresholdBytes { get; set; } = 250 * 1024 * 1024; // 250 MB
        public bool ExportCsv { get; set; } = true;
        public bool ExportJson { get; set; } = true;
        public bool ExportHtmlReport { get; set; } = true;
        public string SecondaryMirrorFolder { get; set; } = "";
        public bool AutoSyncGreenroomOnManifestChange { get; set; } = true;

        public static CollectionManifestConfig CreateDefault() => new()
        {
            Enabled = false,
            TargetFolders = new(),
            FieldParseRules = new()
        };
    }

    // ==================== USER-DEFINED JOB SYSTEM ==================== //

    public class HousekeepingJob : ObservableObject
    {
        private string _jobId = Guid.NewGuid().ToString("N");
        private string _name = "New Housekeeping Job";
        private ModuleId _moduleType = ModuleId.MediaTranscoder;
        private bool _enabled = true;
        private bool _runInExternalCmd = false;

        public string JobId
        {
            get => _jobId;
            set { _jobId = value; OnPropertyChanged(); }
        }

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public ModuleId ModuleType
        {
            get => _moduleType;
            set
            {
                _moduleType = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ModuleTypeBadgeText));
                OnPropertyChanged(nameof(ModulePurposeDescription));
                OnPropertyChanged(nameof(ConfigurationSummary));
            }
        }

        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }

        public bool RunInExternalCmd
        {
            get => _runInExternalCmd;
            set { _runInExternalCmd = value; OnPropertyChanged(); }
        }

        private string _onSuccessTriggerJobId = "";
        public string OnSuccessTriggerJobId
        {
            get => _onSuccessTriggerJobId;
            set { _onSuccessTriggerJobId = value ?? ""; OnPropertyChanged(); }
        }

        public HousekeepingJob DeepClone()
        {
            string json = JsonSerializer.Serialize(this);
            return JsonSerializer.Deserialize<HousekeepingJob>(json) ?? new HousekeepingJob();
        }

        // Per-Job Independent Module Configurations (Optimized: Only active module serialized to JSON)
        private ProxyGeneratorConfig? _proxy;
        private ArchiveCompressorConfig? _archive;
        private MediaTranscoderConfig? _transcoder;
        private RomLauncherGeneratorConfig? _rom;
        private CloudSyncConfig? _cloud;
        private HumbleSyncConfig? _humble;
        private CustomScriptsConfig? _scripts;
        private CollectionManifestConfig? _manifest;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ProxyGeneratorConfig? ProxyGenerator
        {
            get => _proxy ?? (_moduleType == ModuleId.ProxyGenerator ? (_proxy = ProxyGeneratorConfig.CreateDefault()) : null);
            set => _proxy = value;
        }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ArchiveCompressorConfig? ArchiveCompressor
        {
            get => _archive ?? (_moduleType == ModuleId.ArchiveCompressor ? (_archive = ArchiveCompressorConfig.CreateDefault()) : null);
            set => _archive = value;
        }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public MediaTranscoderConfig? MediaTranscoder
        {
            get => _transcoder ?? (_moduleType == ModuleId.MediaTranscoder ? (_transcoder = MediaTranscoderConfig.CreateDefault()) : null);
            set => _transcoder = value;
        }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public RomLauncherGeneratorConfig? RomLauncherGenerator
        {
            get => _rom ?? (_moduleType == ModuleId.RomLauncherGenerator ? (_rom = RomLauncherGeneratorConfig.CreateDefault()) : null);
            set => _rom = value;
        }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CloudSyncConfig? CloudSync
        {
            get => _cloud ?? (_moduleType == ModuleId.CloudSync ? (_cloud = CloudSyncConfig.CreateDefault()) : null);
            set => _cloud = value;
        }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public HumbleSyncConfig? HumbleSync
        {
            get => _humble ?? (_moduleType == ModuleId.HumbleSync ? (_humble = HumbleSyncConfig.CreateDefault()) : null);
            set => _humble = value;
        }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CustomScriptsConfig? CustomScripts
        {
            get => _scripts ?? (_moduleType == ModuleId.CustomScripts ? (_scripts = CustomScriptsConfig.CreateDefault()) : null);
            set => _scripts = value;
        }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CollectionManifestConfig? CollectionManifest
        {
            get => _manifest ?? (_moduleType == ModuleId.CollectionManifest ? (_manifest = CollectionManifestConfig.CreateDefault()) : null);
            set => _manifest = value;
        }

        public void StripUnusedModuleConfigs()
        {
            if (ModuleType != ModuleId.ProxyGenerator) _proxy = null;
            if (ModuleType != ModuleId.ArchiveCompressor) _archive = null;
            if (ModuleType != ModuleId.MediaTranscoder) _transcoder = null;
            if (ModuleType != ModuleId.RomLauncherGenerator) _rom = null;
            if (ModuleType != ModuleId.CloudSync) _cloud = null;
            if (ModuleType != ModuleId.HumbleSync) _humble = null;
            if (ModuleType != ModuleId.CustomScripts) _scripts = null;
            if (ModuleType != ModuleId.CollectionManifest) _manifest = null;
        }

        [JsonIgnore]
        public string ModuleTypeBadgeText => GetModuleTypeDisplayName(ModuleType);

        [JsonIgnore]
        public string ModulePurposeDescription => GetModuleTypeDescription(ModuleType);

        [JsonIgnore]
        public string ConfigurationSummary
        {
            get
            {
                return ModuleType switch
                {
                    ModuleId.ProxyGenerator => $"{ProxyGenerator?.Folders.Count(f => f.Enabled) ?? 0} folder(s) • Profile: {ProxyGenerator?.HandBrakeProfileId ?? "Default"} ('{ProxyGenerator?.ProxySuffixTag}')",
                    ModuleId.ArchiveCompressor => $"{ArchiveCompressor?.Mappings.Count(m => m.Enabled) ?? 0} folder pair(s) • Profile: {ArchiveCompressor?.HandBrakeProfileId ?? "Default"} ({ArchiveCompressor?.OutputExtension})",
                    ModuleId.MediaTranscoder => $"{(MediaTranscoder?.DrivesToScan.Count ?? 0) + (MediaTranscoder?.CustomFoldersToScan.Count(f => f.Enabled) ?? 0)} scan root(s) • Mode: {(MediaTranscoder?.DetectionMode == SourceDetectionMode.FilenameTagMatching ? "Filename Tag" : "Auto-Res")} • Master: {MediaTranscoder?.MasterFileTag}",
                    ModuleId.RomLauncherGenerator => $"{RomLauncherGenerator?.Collections.Count(c => c.Enabled) ?? 0} console(s) • Auto .m3u: {(RomLauncherGenerator?.AutoGenerateM3uForMultiDisc == true ? "On" : "Off")} • Auto 7z: {(RomLauncherGenerator?.AutoCompressUnarchivedRomsWith7z == true ? "On" : "Off")} • Save Sync: {(RomLauncherGenerator?.EnablePrePostLaunchSyncCommands == true ? "On" : "Off")}",
                    ModuleId.CloudSync => $"{CloudSync?.Jobs.Count(j => j.Enabled) ?? 0} rclone sync folder(s)",
                    ModuleId.HumbleSync => string.IsNullOrWhiteSpace(HumbleSync?.LibraryPath) ? "Library folder not set yet" : $"Library: {HumbleSync.LibraryPath}",
                    ModuleId.CustomScripts => $"{CustomScripts?.Tasks.Count(t => t.Enabled) ?? 0} script/command task(s)",
                    ModuleId.CollectionManifest => $"{CollectionManifest?.TargetFolders.Count(f => f.Enabled) ?? 0} folder(s) • Hash: {(CollectionManifest?.EnableFileHashing == true ? "Full Hash" : "Fast Inventory")} • Rules: {CollectionManifest?.FieldParseRules.Count(r => r.Enabled) ?? 0}",
                    _ => ""
                };
            }
        }

        public void NotifySummaryChanged()
        {
            OnPropertyChanged(nameof(ModuleTypeBadgeText));
            OnPropertyChanged(nameof(ModulePurposeDescription));
            OnPropertyChanged(nameof(ConfigurationSummary));
        }

        public HousekeepingJob CloneAsDuplicate()
        {
            // Ensure active config is instantiated before serializing
            object? dummy = ModuleType switch
            {
                ModuleId.ProxyGenerator => (object?)ProxyGenerator,
                ModuleId.ArchiveCompressor => ArchiveCompressor,
                ModuleId.MediaTranscoder => MediaTranscoder,
                ModuleId.RomLauncherGenerator => RomLauncherGenerator,
                ModuleId.CloudSync => CloudSync,
                ModuleId.HumbleSync => HumbleSync,
                ModuleId.CustomScripts => CustomScripts,
                ModuleId.CollectionManifest => CollectionManifest,
                _ => null
            };
            string json = JsonSerializer.Serialize(this);
            var copy = JsonSerializer.Deserialize<HousekeepingJob>(json) ?? new HousekeepingJob();
            copy.JobId = Guid.NewGuid().ToString("N");
            copy.Name = $"{Name} (Copy)";
            return copy;
        }

        public static List<HousekeepingJob> CreateDefaultStarterJobs()
        {
            var list = new List<HousekeepingJob>();
            foreach (var mod in Enum.GetValues<ModuleId>())
            {
                list.Add(new HousekeepingJob
                {
                    JobId = Guid.NewGuid().ToString("N")[..10],
                    Name = GetCleanDefaultName(mod),
                    ModuleType = mod,
                    Enabled = true
                });
            }
            return list;
        }

        public static string GetCleanDefaultName(ModuleId id) => id switch
        {
            ModuleId.ProxyGenerator => "Proxy Gen",
            ModuleId.ArchiveCompressor => "Video Archiver",
            ModuleId.MediaTranscoder => "Media Transcoder",
            ModuleId.RomLauncherGenerator => "Game Launchers",
            ModuleId.CloudSync => "Cloud Sync",
            ModuleId.HumbleSync => "Humble Library",
            ModuleId.CustomScripts => "Custom Tasks",
            ModuleId.CollectionManifest => "Collection Manifest",
            _ => id.ToString()
        };

        public static string GetModuleTypeDisplayName(ModuleId id) => id switch
        {
            ModuleId.ProxyGenerator => "🎬 Proxy Gen",
            ModuleId.ArchiveCompressor => "📦 Video Archiver",
            ModuleId.MediaTranscoder => "📺 Media Transcoder",
            ModuleId.RomLauncherGenerator => "🎮 Game Launchers",
            ModuleId.CloudSync => "☁ Cloud Sync",
            ModuleId.HumbleSync => "📚 Humble Library",
            ModuleId.CustomScripts => "⚡ Custom Tasks",
            ModuleId.CollectionManifest => "📋 Collection Manifest",
            _ => id.ToString()
        };

        public static string GetModuleTypeDescription(ModuleId id) => id switch
        {
            ModuleId.ProxyGenerator => "Creates lightweight editing proxies (video, audio, etc.) next to source footage for smooth scrubbing.",
            ModuleId.ArchiveCompressor => "Compresses working video folders into space-efficient long-term archives while keeping folder structure.",
            ModuleId.MediaTranscoder => "Scans media drives for master sources and generates optimized 144p–4K streaming tiers for Plex/Jellyfin.",
            ModuleId.RomLauncherGenerator => "Builds .m3u playlists, archives ROMs to .7z, and generates one-click launchers for retro emulators & PC games.",
            ModuleId.CloudSync => "Synchronizes local folders with cloud remotes (Google Drive, OneDrive, NAS) via rclone.",
            ModuleId.HumbleSync => "Downloads and updates your purchased Humble Bundle eBooks, audio, and DRM-free games via hbd.",
            ModuleId.CustomScripts => "Runs your own .bat, .cmd, .ps1 PowerShell scripts, or command-line tools.",
            ModuleId.CollectionManifest => "Recursively audits video and game collections across local/NAS drives with No-Intro/Redump DAT verification, composite hashing, and .hash sidecars.",
            _ => ""
        };
    }

    public class DiskDriveMetricItem
    {
        public string DriveLetter { get; set; } = "C:";
        public double FreeGb { get; set; }
        public double TotalGb { get; set; }
        public string DriveType { get; set; } = "Fixed";
        public bool IsNetwork { get; set; }
        public bool IsReady { get; set; } = true;
        public string VolumeLabel { get; set; } = "";
        public string RemotePath { get; set; } = "";
        public string FreeText => !IsReady
            ? "Offline (!)"
            : (FreeGb >= 1000 ? $"{FreeGb / 1024.0:F1} TB Free" : $"{FreeGb:0.#} GB Free");
        public string SummaryText => !IsReady ? $"{DriveLetter} !" : $"{DriveLetter} {FreeText}";
    }

    public class SystemPerformanceStats
    {
        public double CpuUsagePercent { get; set; }
        public double RamUsedGb { get; set; }
        public double RamTotalGb { get; set; }
        public double RamUsagePercent => RamTotalGb > 0 ? (RamUsedGb / RamTotalGb) * 100.0 : 0;
        public string DiskDriveLetter { get; set; } = "C:";
        public double DiskFreeGb { get; set; }
        public double DiskTotalGb { get; set; }
        public double DiskReadWriteMbps { get; set; }
        public double NetworkUsageMbps { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public List<DiskDriveMetricItem> Disks { get; set; } = new();

        // Active Subprocess & Job Telemetry for Remote Streaming
        public bool IsRunning { get; set; }
        public string? ActiveQueueId { get; set; }
        public string? ActiveQueueName { get; set; }
        public int CurrentJobIndex { get; set; }
        public int TotalJobsInQueue { get; set; }
        public string? ActiveJobName { get; set; }
        public string? ActiveSubprocessStatus { get; set; }
        public double ActiveSubprocessProgress { get; set; }
        public string? HostMachineName { get; set; }

        [JsonIgnore]
        public string FormattedCpu => $"{CpuUsagePercent:F0}%";
        [JsonIgnore]
        public string FormattedRam => $"{RamUsedGb:F1} / {RamTotalGb:F1} GB ({RamUsagePercent:F0}%)";
        [JsonIgnore]
        public string FormattedDisk => $"{DiskDriveLetter} {DiskFreeGb:F0} GB Free";
        [JsonIgnore]
        public string FormattedNet => $"{NetworkUsageMbps:F1} Mbps";

        public string GetCulledDisksText(int maxDisks = 0)
        {
            if (Disks == null || Disks.Count == 0)
            {
                return FormattedDisk;
            }

            var sorted = Disks.OrderBy(d => d.DriveLetter, StringComparer.OrdinalIgnoreCase).ToList();
            if (maxDisks > 0 && sorted.Count > maxDisks)
            {
                // Cull starting with the last disk alphabetically
                sorted = sorted.Take(maxDisks).ToList();
            }

            return string.Join(" | ", sorted.Select(d => d.SummaryText));
        }
    }

    public class RemoteNodeConfig : ObservableObject
    {
        private string _id = Guid.NewGuid().ToString("N");
        private string _name = "Remote Server";
        private string _hostOrIp = "";
        private string _remoteAppPath = @"C:\Programs\Housekeeping";
        private RemoteConnectionType _connectionType = RemoteConnectionType.WindowsUncShare;
        private int _port = 445;
        private string _username = "";
        private string _password = "";
        private string _privateKeyPath = "";
        private int _httpApiPort = 5178;
        private string _apiToken = "";
        private bool _preferHttpApi = true;

        public string Id { get => _id; set { _id = value; OnPropertyChanged(); } }
        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
        public string HostOrIp { get => _hostOrIp; set { _hostOrIp = value; OnPropertyChanged(); } }
        public string RemoteAppPath { get => _remoteAppPath; set { _remoteAppPath = value; OnPropertyChanged(); } }
        public RemoteConnectionType ConnectionType { get => _connectionType; set { _connectionType = value; OnPropertyChanged(); } }
        public int Port { get => _port; set { _port = value; OnPropertyChanged(); } }
        public string Username { get => _username; set { _username = value; OnPropertyChanged(); } }
        public string Password { get => _password; set { _password = value; OnPropertyChanged(); } }
        public string PrivateKeyPath { get => _privateKeyPath; set { _privateKeyPath = value; OnPropertyChanged(); } }
        public int HttpApiPort { get => _httpApiPort; set { _httpApiPort = value; OnPropertyChanged(); } }
        public string ApiToken { get => _apiToken; set { _apiToken = value ?? ""; OnPropertyChanged(); } }
        public bool PreferHttpApi { get => _preferHttpApi; set { _preferHttpApi = value; OnPropertyChanged(); } }

        // Runtime status (Not serialized)
        [JsonIgnore] public string DetectedVersion { get; set; } = "Unknown";
        [JsonIgnore] public bool IsReachable { get; set; } = false;
        [JsonIgnore] public bool CanUpdateApp { get; set; } = false;
        [JsonIgnore] public bool CanUpdateSettings { get; set; } = false;
        [JsonIgnore] public string StatusMessage { get; set; } = "Not connected";
        [JsonIgnore] public SystemPerformanceStats? LastStats { get; set; }

        public RemoteNodeConfig Clone()
        {
            string json = JsonSerializer.Serialize(this);
            return JsonSerializer.Deserialize<RemoteNodeConfig>(json) ?? new RemoteNodeConfig();
        }
    }

    public class NamedJobQueue : ObservableObject
    {
        private string _id = Guid.NewGuid().ToString("N");
        private string _name = "New Queue";
        private string _description = "";
        private bool _enableSchedule = false;
        private MasterScheduleMode _scheduleMode = MasterScheduleMode.DailyTime;
        private string _scheduleDailyTime = "03:00";
        private int _scheduleIntervalMinutes = 60;
        private string _scheduleSpecificTimesCsv = "03:00, 12:00, 18:00";
        private List<DayOfWeek> _scheduleDays = new()
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
            DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
        };
        private DateTime? _lastRunUtc;
        private List<HousekeepingJob> _jobs = new();

        public string Id { get => _id; set { _id = value; OnPropertyChanged(); } }
        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
        public string Description { get => _description; set { _description = value; OnPropertyChanged(); } }
        public bool EnableSchedule { get => _enableSchedule; set { _enableSchedule = value; OnPropertyChanged(); } }
        public MasterScheduleMode ScheduleMode { get => _scheduleMode; set { _scheduleMode = value; OnPropertyChanged(); } }
        public string ScheduleDailyTime { get => _scheduleDailyTime; set { _scheduleDailyTime = value; OnPropertyChanged(); } }
        public int ScheduleIntervalMinutes { get => _scheduleIntervalMinutes; set { _scheduleIntervalMinutes = value; OnPropertyChanged(); } }
        public string ScheduleSpecificTimesCsv { get => _scheduleSpecificTimesCsv; set { _scheduleSpecificTimesCsv = value; OnPropertyChanged(); } }
        public List<DayOfWeek> ScheduleDays { get => _scheduleDays; set { _scheduleDays = value; OnPropertyChanged(); } }
        public DateTime? LastRunUtc { get => _lastRunUtc; set { _lastRunUtc = value; OnPropertyChanged(); } }
        public List<HousekeepingJob> Jobs { get => _jobs; set { _jobs = value; OnPropertyChanged(); } }

        [JsonIgnore]
        public string JobsCountSummary => $"{Jobs.Count(j => j.Enabled)} / {Jobs.Count} enabled";

        [JsonIgnore]
        public string ScheduleSummary
        {
            get
            {
                if (!EnableSchedule) return "Disabled";
                return ScheduleMode switch
                {
                    MasterScheduleMode.IntervalCadence => $"Every {ScheduleIntervalMinutes}m",
                    MasterScheduleMode.SpecificTimes => $"At {ScheduleSpecificTimesCsv}",
                    _ => $"Daily at {ScheduleDailyTime}"
                };
            }
        }

        public NamedJobQueue CloneAsDuplicate()
        {
            string json = JsonSerializer.Serialize(this);
            var copy = JsonSerializer.Deserialize<NamedJobQueue>(json) ?? new NamedJobQueue();
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name = $"{Name} (Copy)";
            copy.LastRunUtc = null;
            foreach (var j in copy.Jobs)
            {
                j.JobId = Guid.NewGuid().ToString("N");
            }
            return copy;
        }
    }

    public class RunHistoryEntry
    {
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public string TriggerSource { get; set; } = "Manual";
        public string ModulesSummary { get; set; } = "";
        public int ItemsProcessed { get; set; }
        public int ItemsSkipped { get; set; }
        public int ErrorsCount { get; set; }
        public string Status { get; set; } = "Completed";
        public string LogFilePath { get; set; } = "";

        [JsonIgnore]
        public string DurationText => (CompletedAt - StartedAt).ToString(@"hh\:mm\:ss");
    }

    public static class UiOptionsCatalog
    {
        public static readonly string[] EmulatorRunners =
        {
            "RetroArch", "PCSX2", "DuckStation", "RPCS3", "PPSSPP", "Dolphin",
            "Flycast", "Redream", "Project64", "melonDS", "Citra", "Cemu",
            "Xemu", "Xenia", "Custom"
        };

        public static readonly string[] VideoCodecs =
        {
            "x265", "x265_10bit", "x264", "nvenc_h265", "nvenc_h264",
            "qsv_h265", "vce_h265", "svt_av1"
        };

        public static readonly string[] EncoderPresets =
        {
            "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower"
        };

        public static readonly string[] ContainerExtensions =
        {
            ".mkv", ".mp4"
        };

        public static readonly string[] AudioCodecs =
        {
            "av_aac", "ac3", "eac3", "copy", "copy:aac", "copy:ac3"
        };

        public static readonly string[] SubtitleModes =
        {
            "none", "first", "burned"
        };

        public static readonly string[] RcloneModes =
        {
            "bisync", "sync", "copy"
        };

        public static List<FolderTargetItem> DiscoverSubfolderTargets(string parentDirectory)
        {
            var result = new List<FolderTargetItem>();
            if (string.IsNullOrWhiteSpace(parentDirectory) || !Directory.Exists(parentDirectory))
                return result;

            foreach (string dir in Directory.GetDirectories(parentDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                string folderName = Path.GetFileName(dir);
                if (string.IsNullOrWhiteSpace(folderName) || folderName.StartsWith('.'))
                    continue;
                result.Add(new FolderTargetItem { Enabled = true, Path = dir });
            }

            return result;
        }

        public static List<ArchiveMappingItem> DiscoverPairedSubfolderMappings(string parentSourceDir, string parentArchiveDestDir)
        {
            var result = new List<ArchiveMappingItem>();
            if (string.IsNullOrWhiteSpace(parentSourceDir) || !Directory.Exists(parentSourceDir) ||
                string.IsNullOrWhiteSpace(parentArchiveDestDir))
            {
                return result;
            }

            foreach (string subSource in Directory.GetDirectories(parentSourceDir, "*", SearchOption.TopDirectoryOnly))
            {
                string folderName = Path.GetFileName(subSource);
                if (string.IsNullOrWhiteSpace(folderName) || folderName.StartsWith('.'))
                    continue;

                result.Add(new ArchiveMappingItem
                {
                    Enabled = true,
                    Name = folderName,
                    SourcePath = subSource,
                    ArchiveOutputPath = Path.Combine(parentArchiveDestDir, folderName)
                });
            }

            return result;
        }

        public static string FormatFreeSpaceShort(long freeBytes)
        {
            double tb = freeBytes / (1024.0 * 1024 * 1024 * 1024);
            if (tb >= 1.0)
                return $"{tb:0.#} TB";
            double gb = freeBytes / (1024.0 * 1024 * 1024);
            if (gb >= 1.0)
                return $"{gb:0.#} GB";
            double mb = freeBytes / (1024.0 * 1024);
            return $"{mb:0} MB";
        }
    }
}
