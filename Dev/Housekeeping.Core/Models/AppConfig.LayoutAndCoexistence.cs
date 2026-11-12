// @module: Housekeeping.Models.AppConfig.LayoutAndCoexistence
// @purpose: Persisted UI layout geometry, sister-app coexistence flags, JSON extension data preservation, and fleet node models.
// @depends-on: System, System.Collections.Generic, System.IO, System.Text.Json, System.Text.Json.Serialization
// @exports: UiLayoutConfig, AppConfig (partial), RemoteNodeConfig, RunHistoryEntry, UiOptionsCatalog

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

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
}
