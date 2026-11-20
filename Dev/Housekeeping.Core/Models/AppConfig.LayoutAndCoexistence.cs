// @module: Housekeeping.Models.AppConfig.LayoutAndCoexistence
// @purpose: Persisted UI layout geometry, sister-app coexistence flags, JSON extension data preservation, and fleet node models.
// @depends-on: System, System.Collections.Generic, System.IO, System.Text.Json, System.Text.Json.Serialization
// @exports: UiLayoutConfig, AppConfig (partial), RemoteNodeConfig, RunHistoryEntry, UiOptionsCatalog

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Foundation.Core;

namespace Housekeeping.Models
{
    public class UiLayoutConfig
    {
        public int TerminalHeightPx { get; set; } = 260;
        public string TerminalSizePreset { get; set; } = "medium"; // "small" | "medium" | "large" | "custom"
        public bool TerminalOpen { get; set; } = false;
        public bool TerminalWordWrap { get; set; } = false;
        public bool SidebarCollapsed { get; set; } = false;
    }

    public partial class AppConfig
    {
        public UiLayoutConfig UiLayout { get; set; } = new();
        public bool AutoAlignWithSisterApps { get; set; } = false;

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }

    public class RemoteNodeConfig : ObservableObject
    {
        private string _id = Guid.NewGuid().ToString("N");
        private string _name = "Remote Server";
        private string _hostOrIp = "";
        private string _remoteAppPath = @"%LOCALAPPDATA%\Programs\StreamItAll\Mother\Housekeeping";
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
            "Flycast", "Redream", "Project64", "melonDS", "Citra", "Azahar",
            "Lime3DS", "Cemu", "Ryujinx", "Yuzu", "Vita3K", "Xemu", "Xenia",
            "MAME", "ScummVM", "DOSBox", "PCNative", "Custom"
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

        public static readonly string[] ContainerExtensions = { ".mkv", ".mp4" };
        public static readonly string[] AudioCodecs = { "av_aac", "ac3", "eac3", "copy", "copy:aac", "copy:ac3" };
        public static readonly string[] SubtitleModes = { "none", "first", "burned" };
        public static readonly string[] RcloneModes = { "bisync", "sync", "copy" };

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
                return result;

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
            if (tb >= 1.0) return $"{tb:0.#} TB";
            double gb = freeBytes / (1024.0 * 1024 * 1024);
            if (gb >= 1.0) return $"{gb:0.#} GB";
            double mb = freeBytes / (1024.0 * 1024);
            return $"{mb:0} MB";
        }
    }

    public sealed record RcloneContainmentPaths(
        bool IsContained,
        string RcloneRootDir,
        string ConfigFilePath,
        string BisyncWorkDir,
        string CacheDir);

    public static class RcloneContainmentHelper
    {
        private static readonly string[] PathOverrideEnvVars =
        {
            "HOUSEKEEPING_RCLONE_DIR", "HOUSEKEEPING_DATA_DIR", "DATA_DIR",
            "HOUSEKEEPING_HOME", "APP_HOME", "HOUSEKEEPING_CONFIG",
            "CONFIG_PATH", "HOUSEKEEPING_LOG_DIR", "LOG_DIR"
        };

        public static RcloneContainmentPaths ResolveContainment(string? dataDirectory = null, bool forceContainment = false)
        {
            SandboxResolution sandbox = SandboxEnvironmentSeam.ResolveCurrent();
            bool isolatedEnv = forceContainment || sandbox.IsSandboxActive || IsTruthyEnv("APP_ISOLATED_TEST") || HasExplicitOverrideEnv();

            string? effectiveDataDir = !string.IsNullOrWhiteSpace(dataDirectory)
                ? Path.GetFullPath(dataDirectory)
                : ResolveEnvDataDir();

            if (!string.IsNullOrWhiteSpace(effectiveDataDir) && sandbox.IsSandboxActive)
                SandboxEnvironmentSeam.AssertSandboxPathIfActive(effectiveDataDir, sandbox);

            if (string.IsNullOrWhiteSpace(effectiveDataDir) && isolatedEnv)
            {
                effectiveDataDir = sandbox.IsSandboxActive
                    ? Path.Combine(sandbox.StorageDriveRoot ?? sandbox.SandboxRoot!, "Housekeeping", "data")
                    : Path.Combine(Path.GetTempPath(), "Housekeeping_Isolated");
            }

            if (string.IsNullOrWhiteSpace(effectiveDataDir))
                return new RcloneContainmentPaths(false, "", "", "", "");

            string rcloneDir = Path.Combine(effectiveDataDir, "rclone");
            string confPath = Path.Combine(rcloneDir, "rclone.conf");
            string bisyncDir = Path.Combine(rcloneDir, "bisync");
            string cacheDir = Path.Combine(rcloneDir, "cache");
            bool shouldContain = isolatedEnv || File.Exists(confPath);
            return new RcloneContainmentPaths(shouldContain, rcloneDir, confPath, bisyncDir, cacheDir);
        }

        public static void EnsureContainmentDirectories(RcloneContainmentPaths paths)
        {
            if (!paths.IsContained) return;
            Directory.CreateDirectory(paths.RcloneRootDir);
            Directory.CreateDirectory(paths.BisyncWorkDir);
            Directory.CreateDirectory(paths.CacheDir);
            if (!File.Exists(paths.ConfigFilePath))
                File.WriteAllText(paths.ConfigFilePath, "", new UTF8Encoding(false));
        }

        public static IReadOnlyDictionary<string, string> BuildEnvironmentVariables(RcloneContainmentPaths paths)
        {
            if (!paths.IsContained)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["RCLONE_CONFIG"] = paths.ConfigFilePath,
                ["RCLONE_CACHE_DIR"] = paths.CacheDir
            };
        }

        public static string AppendContainmentFlags(string flags, RcloneContainmentPaths paths, bool includeBisyncWorkdir)
        {
            if (!paths.IsContained) return flags;
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(flags)) parts.Add(flags.Trim());
            if (!ContainsCliFlag(flags, "--config")) parts.Add($"--config \"{paths.ConfigFilePath}\"");
            if (includeBisyncWorkdir && !ContainsCliFlag(flags, "--workdir")) parts.Add($"--workdir \"{paths.BisyncWorkDir}\"");
            if (!ContainsCliFlag(flags, "--cache-dir")) parts.Add($"--cache-dir \"{paths.CacheDir}\"");
            return string.Join(" ", parts);
        }

        private static bool ContainsCliFlag(string flags, string flag) =>
            !string.IsNullOrWhiteSpace(flags) &&
            Regex.IsMatch(flags, $@"(?:^|\s){Regex.Escape(flag)}(?:=|\s|$)", RegexOptions.IgnoreCase);

        private static bool HasExplicitOverrideEnv() =>
            Array.Exists(PathOverrideEnvVars, v => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(v)));

        private static string? ResolveEnvDataDir()
        {
            foreach (string key in new[] { "HOUSEKEEPING_RCLONE_DIR", "HOUSEKEEPING_DATA_DIR", "DATA_DIR", "HOUSEKEEPING_HOME", "APP_HOME" })
            {
                string? val = Environment.GetEnvironmentVariable(key);
                if (!string.IsNullOrWhiteSpace(val)) return Path.GetFullPath(val.Trim());
            }
            string? cfg = Environment.GetEnvironmentVariable("HOUSEKEEPING_CONFIG") ?? Environment.GetEnvironmentVariable("CONFIG_PATH");
            return !string.IsNullOrWhiteSpace(cfg) ? Path.GetDirectoryName(Path.GetFullPath(cfg.Trim())) : null;
        }

        private static bool IsTruthyEnv(string name)
        {
            string? v = Environment.GetEnvironmentVariable(name);
            return !string.IsNullOrWhiteSpace(v) && (v.Equals("1", StringComparison.OrdinalIgnoreCase) || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("yes", StringComparison.OrdinalIgnoreCase));
        }
    }

    public partial class RomLauncherGeneratorConfig
    {
        public void SanitizeAutoGeneratedCommandOverrides()
        {
            if (UseCustomPrePostLaunchCommands) return;
            if (DefaultSyncPaths.Count == 0 && string.IsNullOrWhiteSpace(SaveSyncLocalFolder)) return;

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

        public (List<string> PreCmds, List<string> PostCmds) ResolveAllSyncCommands(string rcloneExe, ConsoleCollectionRule? col = null) =>
            ResolveAllSyncCommands(rcloneExe, col, dataDirectory: null, forceContainment: false);

        public (List<string> PreCmds, List<string> PostCmds) ResolveAllSyncCommands(
            string rcloneExe,
            ConsoleCollectionRule? col,
            string? dataDirectory,
            bool forceContainment = false)
        {
            string exe = string.IsNullOrWhiteSpace(rcloneExe) ? "rclone.exe" : rcloneExe;
            var preCmds = new List<string>();
            var postCmds = new List<string>();
            var activePaths = (col != null && col.OverrideJobSyncPaths && col.CustomSyncPaths.Count > 0) ? col.CustomSyncPaths : DefaultSyncPaths;

            bool hasConfiguredSyncRows = activePaths.Count > 0 ||
                (!string.IsNullOrWhiteSpace(SaveSyncLocalFolder) && !string.IsNullOrWhiteSpace(SaveSyncCloudRemote));
            bool honorManualCliOverride = UseCustomPrePostLaunchCommands ||
                (!hasConfiguredSyncRows && (!string.IsNullOrWhiteSpace(PreLaunchCommand) || !string.IsNullOrWhiteSpace(PostLaunchCommand)));

            if (honorManualCliOverride)
            {
                if (!string.IsNullOrWhiteSpace(PreLaunchCommand)) preCmds.Add(PreLaunchCommand.Trim());
                if (!string.IsNullOrWhiteSpace(PostLaunchCommand)) postCmds.Add(PostLaunchCommand.Trim());
                if (preCmds.Count > 0 || postCmds.Count > 0) return (preCmds, postCmds);
            }

            if (activePaths.Count == 0 && !string.IsNullOrWhiteSpace(SaveSyncLocalFolder) && !string.IsNullOrWhiteSpace(SaveSyncCloudRemote))
            {
                activePaths = new List<GameSyncPathItem>
                {
                    new() { Enabled = true, Label = "Saves", LocalPath = SaveSyncLocalFolder, RemoteTarget = SaveSyncCloudRemote, SyncMode = SaveSyncMode ?? "bisync" }
                };
            }

            var containment = RcloneContainmentHelper.ResolveContainment(dataDirectory, forceContainment);
            foreach (var item in activePaths.Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.LocalPath) && !string.IsNullOrWhiteSpace(p.RemoteTarget)))
            {
                string local = item.LocalPath.Trim();
                string remote = item.RemoteTarget.Trim();
                string mode = (item.SyncMode ?? "bisync").Trim().ToLowerInvariant();
                bool isBisync = mode is not ("sync" or "copy");

                string basePre = isBisync
                    ? $"\"{exe}\" bisync \"{local}\" \"{remote}\" --create-empty-src-dirs --resilient --max-lock 2h --verbose"
                    : $"\"{exe}\" {mode} \"{remote}\" \"{local}\" --create-empty-src-dirs --verbose";
                string basePost = isBisync
                    ? $"\"{exe}\" bisync \"{local}\" \"{remote}\" --create-empty-src-dirs --resilient --max-lock 2h --verbose"
                    : $"\"{exe}\" {mode} \"{local}\" \"{remote}\" --create-empty-src-dirs --verbose";

                preCmds.Add(RcloneContainmentHelper.AppendContainmentFlags(basePre, containment, isBisync));
                postCmds.Add(RcloneContainmentHelper.AppendContainmentFlags(basePost, containment, isBisync));
            }

            return (preCmds, postCmds);
        }

        public (string PreCmd, string PostCmd) ResolvePreAndPostSyncCommands(string rcloneExe) =>
            ResolvePreAndPostSyncCommands(rcloneExe, dataDirectory: null, forceContainment: false);

        public (string PreCmd, string PostCmd) ResolvePreAndPostSyncCommands(string rcloneExe, string? dataDirectory, bool forceContainment = false)
        {
            var (pre, post) = ResolveAllSyncCommands(rcloneExe, null, dataDirectory, forceContainment);
            return (string.Join(" & ", pre), string.Join(" & ", post));
        }
    }
}
