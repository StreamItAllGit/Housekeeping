using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Housekeeping.Models;
using Housekeeping.Services.Modules;

namespace Housekeeping.Services
{
    public sealed class ModuleCatalogItemDto
    {
        public ModuleId Id { get; set; }
        public string ModuleKey { get; set; } = "";
        public string Name { get; set; } = "";
        public string Icon { get; set; } = "";
        public string Category { get; set; } = "";
        public string ShortSummary { get; set; } = "";
        public string LongDescription { get; set; } = "";
        public string Capabilities { get; set; } = "";
        public string RequiredTools { get; set; } = "";
        public string SetupGuide { get; set; } = "";
        public string BestPractices { get; set; } = "";
        public bool IsAppWideEnabled { get; set; }
        public bool IsToolReady { get; set; }
        public string ToolStatusText { get; set; } = "";
        public int DefinedJobCount { get; set; }
    }

    public sealed class RegexPlaygroundResultDto
    {
        public string SampleInput { get; set; } = "";
        public string FileName { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public string FullPath { get; set; } = "";
        public Dictionary<string, string> ExtractedFields { get; set; } = new();
        public List<string> ValidationErrors { get; set; } = new();
    }

    public static class ModuleCatalogService
    {
        public static List<ModuleCatalogItemDto> BuildCatalog(AppConfig cfg, DependencyCheckService dependencies)
        {
            var items = new List<ModuleCatalogItemDto>
            {
                new()
                {
                    Id = ModuleId.ProxyGenerator,
                    ModuleKey = nameof(ModuleId.ProxyGenerator),
                    Name = "Proxy Gen",
                    Icon = "🎬",
                    Category = "PROXY MEDIA WORKFLOW",
                    ShortSummary = "Generates lightweight proxy media alongside source files for fast editing and previews.",
                    LongDescription = "Scans working directories and automatically creates fast, low-bitrate proxies (video, audio drafts, preview assets) right next to high-bitrate master files. Proxy filenames append your configured suffix (such as ' (360p_proxy).mp4') so editing suites like Premiere Pro, DaVinci Resolve, or Final Cut can attach proxies with one click.",
                    Capabilities = "• Recursive folder tree scanning for .mp4, .mkv, .mov files\n• Automatic skip detection: existing proxies are never re-encoded\n• Ultra-fast hardware (NVENC) or software (x264 veryfast) encoding\n• Native integration with HandBrake Profiles catalog",
                    RequiredTools = "HandBrakeCLI.exe",
                    SetupGuide = "1. Ensure HandBrakeCLI is detected in the Settings tab.\n2. Choose or create a fast, low-resolution HandBrake profile (e.g. 'Proxy').\n3. Click '+ Add Job for This Module' to create a proxy generation job.\n4. In Job Builder, add your raw footage folders.\n5. Enable the job in a Job Queue and click '▶ Run Enabled' anytime!",
                    BestPractices = "• Use 'veryfast' or 'faster' encoder presets with RF 18-22 for maximum generation speed.\n• Keep proxies on a fast local NVMe SSD while keeping raw masters on your archive or NAS.\n• Name your suffix tag consistently so your NLE can batch-attach proxies with one click."
                },
                new()
                {
                    Id = ModuleId.ArchiveCompressor,
                    ModuleKey = nameof(ModuleId.ArchiveCompressor),
                    Name = "Video Archiver",
                    Icon = "📦",
                    Category = "STORAGE OPTIMIZATION",
                    ShortSummary = "Compresses completed working video folders into space-saving long-term archives.",
                    LongDescription = "Transfers and compresses finished recording folders (OBS captures, camera dumps, render exports) into a designated archive repository while faithfully maintaining subfolder structure. Transcodes completed videos using efficient HEVC/H.265 or AV1 compression, saving 50% to 75% of disk space.",
                    Capabilities = "• Flexible folder pair mappings (source directory → target directory)\n• Skip tag filtering (e.g. ignores files with '(proxy)' or '(draft)')\n• Preserves folder directory hierarchies in destination\n• Container conversion (e.g. .mp4 to .mkv) with custom audio bitrate",
                    RequiredTools = "HandBrakeCLI.exe",
                    SetupGuide = "1. Verify HandBrakeCLI.exe in Settings.\n2. Choose an archive profile (e.g. 'LQ Archive' with NVENC H.265 or CPU x265).\n3. Create an archive job and add source → destination folder mappings.\n4. Run or schedule the job from Job Queues!",
                    BestPractices = "• Use GPU hardware encoding (nvenc_h265 or qsv_h265) for large capture libraries.\n• For master archives, use RF 22-26 with slower presets for optimal compression ratio."
                },
                new()
                {
                    Id = ModuleId.MediaTranscoder,
                    ModuleKey = nameof(ModuleId.MediaTranscoder),
                    Name = "Media Transcoder",
                    Icon = "📺",
                    Category = "HOME SERVER & STREAMING",
                    ShortSummary = "Multi-tier video transcoder generating 144p through 4K optimized streams for media servers.",
                    LongDescription = "Monitors media drives and libraries for master source files, automatically producing complete multi-resolution tiers (144p to 4K UHD) with language-edition detection and network-resilient Pending Swap commits. Intelligently classifies source video quality using filename tag matching or native MediaInfo.dll stream resolution analysis.",
                    Capabilities = "• Dual source detection: Filename tags (DVD, BR, UHD) or native MediaInfo resolution inspection\n• Per-tier HandBrake profile matrix (144p through 4K)\n• Language Edition Rules (auto-routes anime or foreign language tracks to edition folders)\n• Network drop safety with atomic .partial file writes & Pending Swaps\n• Re-encodes non-HEVC existing files or skips them via Fast Network Mode",
                    RequiredTools = "HandBrakeCLI.exe, MediaInfo.dll",
                    SetupGuide = "1. Ensure HandBrakeCLI and MediaInfo.dll are detected.\n2. Configure resolution profiles in Settings.\n3. Specify drive letters to scan (e.g. 'V, W, X, Y, Z') and media subfolder (e.g. 'Media').\n4. Toggle active resolution tiers (144p through 4K) in the matrix.\n5. Run or schedule the job!",
                    BestPractices = "• Enable 'Fast Network Mode' when scanning SMB network drives.\n• Keep 'Atomic Partial Writes' enabled in Settings so active Plex streams defer swaps cleanly via Pending Swaps."
                },
                new()
                {
                    Id = ModuleId.RomLauncherGenerator,
                    ModuleKey = nameof(ModuleId.RomLauncherGenerator),
                    Name = "Game Launchers",
                    Icon = "🎮",
                    Category = "GAME LAUNCHERS & PRESERVATION",
                    ShortSummary = "Manages retro ROMs and PC games with solid 7z archives, .m3u playlists, CRC32+SHA1 manifests, archive templates, and cloud save sync.",
                    LongDescription = "Builds one-click launcher scripts (.bat) and Start Menu shortcuts for emulator titles and PC games. Handles multi-disc .m3u playlist generation, solid 7-Zip compression, 9-column CRC32+SHA-1 collection_manifest.csv auditing against No-Intro & Redump DATs, zero-decompression archive compliance templates, multi-row bidirectional cloud save/state synchronization, and automatic Greenroom (:8420) post-manifest sync.",
                    Capabilities = "• Automated multi-disc .m3u playlist & solid 7-Zip compression\n• 9-column CRC32 + SHA-1 collection_manifest.csv with Fast 7z/ZIP Header CRC32 auditing\n• Built-in & custom Archive Structure Compliance Templates + guarded '7z d' clutter cleanup\n• Multi-row Pre-launch & Post-launch cloud save/state/screenshot synchronization via rclone\n• Automatic Greenroom Companion (:8420) catalog sync when manifest rows change",
                    RequiredTools = "7z.exe (7-Zip CLI), optional rclone.exe for save sync",
                    SetupGuide = "1. Ensure 7z.exe is detected in Settings.\n2. Configure your emulator console collections or PC game rules in Job Builder.\n3. Configure Archive Compliance Templates and optional Multi-Row Save/State Sync.\n4. Run the job to generate launchers, verify DATs, and sync Greenroom!",
                    BestPractices = "• Use a local SSD scratch folder so large .7z archives extract in seconds on launch.\n• Enable Fast Archive Header CRC32 Audit for near-instant zero-decompression verification across terabytes of NAS ROMs."
                },
                new()
                {
                    Id = ModuleId.CloudSync,
                    ModuleKey = nameof(ModuleId.CloudSync),
                    Name = "Cloud Sync",
                    Icon = "☁️",
                    Category = "BACKUP & CLOUD STORAGE",
                    ShortSummary = "Synchronizes local and cloud storage using rclone with network resilience.",
                    LongDescription = "Performs bidirectional (bisync) or mirror synchronization between local directories and cloud providers (Google Drive, OneDrive, Dropbox, S3, Nextcloud, SFTP) with automated reconnection and retry loops.",
                    Capabilities = "• Multiple sync modes: bisync (two-way), sync (one-way mirror), copy, move\n• Built-in --resync recovery option for bisync jobs\n• Network retry loop that waits for connection recovery instead of failing\n• Bandwidth limit controls and dry-run previews",
                    RequiredTools = "rclone.exe",
                    SetupGuide = "1. Ensure rclone.exe is configured ('rclone config' to add cloud remotes).\n2. Add sync items with your local folder path and remote path (e.g. 'gdrive:Backups/Saves').\n3. For new bisync jobs, run once with '--resync' enabled.\n4. Schedule or run from Job Queues anytime!",
                    BestPractices = "• Use 'copy' if you never want files deleted in the cloud.\n• Use 'bisync' to keep emulator saves and configs synced across multiple PCs."
                },
                new()
                {
                    Id = ModuleId.HumbleSync,
                    ModuleKey = nameof(ModuleId.HumbleSync),
                    Name = "Humble Library",
                    Icon = "📚",
                    Category = "DIGITAL PURCHASES ARCHIVE",
                    ShortSummary = "Downloads and audits purchased Humble Bundle ebooks, audio, and DRM-free games.",
                    LongDescription = "Connects to your Humble Bundle library via session cookie and downloads DRM-free books, audio, comics, and games directly to your local library folder using hbd.",
                    Capabilities = "• Full library backup with resume support and checksum validation\n• Platform filtering and format selection\n• Live streaming progress into Housekeeping Terminal\n• Automated scheduled runs for newly claimed bundles",
                    RequiredTools = "hbd.exe (or python with humble-bundle-downloader)",
                    SetupGuide = "1. Log into humblebundle.com in your browser.\n2. Copy the '_simpleauth_sess' cookie value from Developer Tools → Application → Cookies.\n3. Paste your cookie value and choose your destination library folder in Job Builder.\n4. Run or schedule the job!",
                    BestPractices = "• Refresh your session cookie if downloads ever report unauthorized errors.\n• Add '--progress' in additional args for live download speeds."
                },
                new()
                {
                    Id = ModuleId.CustomScripts,
                    ModuleKey = nameof(ModuleId.CustomScripts),
                    Name = "Custom Tasks",
                    Icon = "📜",
                    Category = "AUTOMATION & SCRIPTS",
                    ShortSummary = "Executes arbitrary batch, PowerShell, Python, or command-line tasks in the pipeline.",
                    LongDescription = "Runs your own custom .bat, .cmd, .ps1, .py, or CLI tasks as integrated pipeline jobs, with process throttling, network readiness pre-checks, post-job chaining, and live terminal logging.",
                    Capabilities = "• Run any .bat, .cmd, .ps1, .py, or .exe command\n• Stream inside Housekeeping's real-time Terminal or launch in separate CMD window\n• Required network path verification before launching\n• Chain follow-up Housekeeping jobs automatically on completion",
                    RequiredTools = "Windows Command Prompt or PowerShell (built into Windows)",
                    SetupGuide = "1. Create a Custom Tasks job in Job Builder.\n2. Add tasks with script/executable path, arguments, and optional working directory.\n3. Run individually or inside any scheduled queue!",
                    BestPractices = "• Enable 'Restrict Raw Shell & Script Editing to Localhost' in Settings if exposing Housekeeping directly beyond your private Tailnet."
                },
                new()
                {
                    Id = ModuleId.CollectionManifest,
                    ModuleKey = nameof(ModuleId.CollectionManifest),
                    Name = "Collection Manifest",
                    Icon = "📋",
                    Category = "COLLECTION AUDITING & INTEGRITY",
                    ShortSummary = "Scans storage pools across multiple network locations, hashes files, extracts custom regex metadata, and exports CSV/JSON/HTML manifests.",
                    LongDescription = "Performs high-speed inventory or deep cryptographic checksum audits across local drives and network shares (SMB/UNC). Recursively parses media and game ROMs, applies custom C# regex/token field extraction rules into dynamic CSV/JSON/HTML columns, computes composite folder hashes, mirrors manifests to backup storage, and triggers Greenroom (:8420) sync when manifests update.",
                    Capabilities = "• Multi-Target Folder Rules with custom Location IDs (e.g. NAS-V through NAS-Z)\n• Fast Inventory Mode vs Deep Cryptographic Hashing (CRC32, MD5, SHA-1, SHA-256)\n• Custom Text Parsing & CSV Field Extraction with C#-backed live Regex Playground\n• Composite Folder Hashing & .hash Sidecar Verifier/Healer\n• CSV, JSON, and dark-mode HTML export + Secondary Mirror backup + Greenroom Auto-Sync",
                    RequiredTools = "Built-in C# engine (no external CLI tools required)",
                    SetupGuide = "1. Create a Collection Manifest job in Job Builder.\n2. Add target directories with Location IDs and Labels.\n3. Configure or load preset Custom Text Parsing rules and test them in the live Regex Playground.\n4. Choose output formats (CSV, JSON, HTML) and optional secondary mirror path.\n5. Run or schedule the job!",
                    BestPractices = "• Use Fast Inventory or Skip Existing Verified Hashes for rapid incremental NAS audits.\n• Tag each drive/pool with a distinct Location ID (NAS-V, NAS-W, etc.) for unified multi-drive catalogs."
                }
            };

            foreach (var item in items)
            {
                item.IsAppWideEnabled = cfg.IsModuleAppWideEnabled(item.Id);
                item.DefinedJobCount = cfg.DefinedJobs.Count(j => j.ModuleType == item.Id);

                string? resolvedPath = item.Id switch
                {
                    ModuleId.ProxyGenerator or ModuleId.ArchiveCompressor or ModuleId.MediaTranscoder => dependencies.ResolveHandBrakeCli(),
                    ModuleId.RomLauncherGenerator => dependencies.ResolveSevenZip(),
                    ModuleId.CloudSync => dependencies.ResolveRclone(),
                    ModuleId.HumbleSync => dependencies.ResolveHumbleDownloader(),
                    _ => null
                };

                bool toolOk = item.Id switch
                {
                    ModuleId.ProxyGenerator or ModuleId.ArchiveCompressor or ModuleId.MediaTranscoder or
                    ModuleId.RomLauncherGenerator or ModuleId.CloudSync or ModuleId.HumbleSync
                        => !string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath),
                    _ => true
                };

                item.IsToolReady = toolOk;
                item.ToolStatusText = toolOk
                    ? (string.IsNullOrWhiteSpace(resolvedPath) ? $"✓ {item.RequiredTools} (Built-in / Ready)" : $"✓ Ready: {resolvedPath}")
                    : $"⚠ Missing: {item.RequiredTools} — Place in ./tools/ or set path in Settings";
            }

            return items;
        }

        public static HousekeepingJob CreateNewJobForModuleType(AppConfig cfg, ModuleId moduleType, string? customName = null)
        {
            string cleanTypeName = HousekeepingJob.GetCleanDefaultName(moduleType);
            string defaultName = customName ?? cleanTypeName;
            int existingCount = cfg.DefinedJobs.Count(j => j.ModuleType == moduleType);
            if (customName == null && existingCount > 0)
            {
                defaultName = $"{cleanTypeName} #{existingCount + 1}";
            }

            return new HousekeepingJob
            {
                JobId = Guid.NewGuid().ToString("N")[..10],
                Name = defaultName,
                ModuleType = moduleType,
                Enabled = true
            };
        }

        public static int PopulateStarterJobs(ConfigService configService)
        {
            int added = 0;
            configService.MutateAndSave(cfg =>
            {
                cfg.EnsureNamedQueuesInitialized();
                cfg.EnsureDefinedJobsSynchronized();

                foreach (var mod in Enum.GetValues<ModuleId>())
                {
                    if (cfg.DefinedJobs.Any(j => j.ModuleType == mod))
                        continue;

                    var job = CreateNewJobForModuleType(cfg, mod, HousekeepingJob.GetModuleTypeDisplayName(mod));
                    cfg.DefinedJobs.Add(job);
                    var activeQ = cfg.GetActiveQueue();
                    if (!activeQ.Jobs.Any(j => j.JobId == job.JobId))
                    {
                        activeQ.Jobs.Add(job);
                    }
                    added++;
                }
            });

            return added;
        }

        public static List<ManifestFieldParseRule> GetManifestPresetBundle(string bundleKey)
        {
            return (bundleKey ?? "").Trim().ToLowerInvariant() switch
            {
                "personalmedia" or "movies_tv" or "plex_movies" => ManifestPresetCatalog.GetPersonalMediaPresets(),
                "personalrom" or "retro_roms" => ManifestPresetCatalog.GetPersonalRomPresets(),
                "generalmedia" or "video_quality" => ManifestPresetCatalog.GetGeneralMediaPresets(),
                "generalfiles" => ManifestPresetCatalog.GetGeneralFilesPresets(),
                "generalaudio" => ManifestPresetCatalog.GetGeneralAudioPresets(),
                _ => new List<ManifestFieldParseRule>()
            };
        }

        public static RegexPlaygroundResultDto TestManifestRegexRules(string? rawSample, IReadOnlyList<ManifestFieldParseRule>? rules)
        {
            string sample = string.IsNullOrWhiteSpace(rawSample)
                ? "Dragon Ball - s01e01 - Secret of the Dragon Balls - DVD (Original).mkv"
                : rawSample.Trim();

            var dto = new RegexPlaygroundResultDto { SampleInput = sample };
            var ruleList = rules?.ToList() ?? new List<ManifestFieldParseRule>();

            foreach (var rule in ruleList)
            {
                if (!rule.Enabled || string.IsNullOrEmpty(rule.Pattern))
                    continue;
                if (rule.MatchMode == ManifestMatchMode.RegexPattern || rule.MatchMode == ManifestMatchMode.RegexNamedCapture)
                {
                    try
                    {
                        _ = new Regex(rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                    }
                    catch (ArgumentException ex)
                    {
                        dto.ValidationErrors.Add($"Column '{rule.ColumnName}': {ex.Message}");
                    }
                }
            }

            string fileName = Path.GetFileName(sample);
            if (string.IsNullOrEmpty(fileName)) fileName = sample;

            string relativePath = sample;
            string fullPath = Path.IsPathRooted(sample)
                ? sample
                : ((sample.Contains('\\') || sample.Contains('/'))
                    ? Path.Combine("<MediaRoot>", "Sets", sample)
                    : Path.Combine("<MediaRoot>", "Video", sample));

            dto.FileName = fileName;
            dto.RelativePath = relativePath;
            dto.FullPath = fullPath;

            if (dto.ValidationErrors.Count == 0)
            {
                dto.ExtractedFields = CollectionManifestModule.ApplyFieldParseRules(fileName, relativePath, fullPath, ruleList);
            }

            return dto;
        }
    }
}
