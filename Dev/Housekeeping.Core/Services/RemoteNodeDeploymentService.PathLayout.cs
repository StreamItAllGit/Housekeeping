// @module: Housekeeping.Services.RemoteNodeDeploymentService.PathLayout
// @purpose: Clean-Release-Root (<RemoteRoot>\app\ & <RemoteRoot>\data\) path resolvers, binary sync exclusions, and remote queue/signal helpers.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, System.Linq, System.Text.Json, Housekeeping.Models
// @exports: Housekeeping.Services.RemoteNodeDeploymentService (partial)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Housekeeping.Models;

namespace Housekeeping.Services
{
    public partial class RemoteNodeDeploymentService
    {
        private static readonly HashSet<string> ExcludedRemoteDirSegments = new(StringComparer.OrdinalIgnoreCase)
        {
            "data", "backups", "logs", "dats", "webview2_cache", "WebView2Cache",
            ".git", "quarantine", "crashes", "manifests", "tools", "cache"
        };

        private static readonly HashSet<string> ExcludedRemoteFileNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "settings.json", "history.json", "pending_swaps.json", "compliance_reports.json",
            "interrupted_run_checkpoint.json", ".hk_cookie_secret.key",
            "restart_request.signal", "control_request.json"
        };

        private static readonly string[] ExcludedRemoteExtensions =
        {
            ".lock", ".hklock", ".tmp", ".pdb", ".log", ".old", ".old_upgrade"
        };

        public static string NormalizeRemoteRoot(string remoteAppPath)
        {
            string trimmed = remoteAppPath.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(Path.GetFileName(trimmed), "app", StringComparison.OrdinalIgnoreCase))
            {
                string? parent = Path.GetDirectoryName(trimmed);
                if (!string.IsNullOrWhiteSpace(parent))
                    return parent;
            }
            return trimmed;
        }

        public static bool IsLegacyFlatRemoteRoot(string remoteRoot)
        {
            string appSub = Path.Combine(remoteRoot, "app");
            string dataSub = Path.Combine(remoteRoot, "data");
            if (Directory.Exists(appSub) || Directory.Exists(dataSub))
                return false;

            return File.Exists(Path.Combine(remoteRoot, "Housekeeping.Core.dll")) ||
                   File.Exists(Path.Combine(remoteRoot, "Housekeeping.dll")) ||
                   File.Exists(Path.Combine(remoteRoot, "settings.json"));
        }

        public static string ResolveRemoteBinaryDir(string remoteAppPath)
        {
            string root = NormalizeRemoteRoot(remoteAppPath);
            if (string.Equals(Path.GetFileName(remoteAppPath.Trim().TrimEnd('\\', '/')), "app", StringComparison.OrdinalIgnoreCase))
                return remoteAppPath.Trim();

            return IsLegacyFlatRemoteRoot(root) ? root : Path.Combine(root, "app");
        }

        public static string ResolveRemoteSettingsReadPath(string remoteAppPath)
        {
            string root = NormalizeRemoteRoot(remoteAppPath);
            string dataSettings = Path.Combine(root, "data", "settings.json");
            if (File.Exists(dataSettings))
                return dataSettings;

            string flatSettings = Path.Combine(root, "settings.json");
            if (File.Exists(flatSettings))
                return flatSettings;

            string appSettings = Path.Combine(root, "app", "settings.json");
            if (File.Exists(appSettings))
                return appSettings;

            return dataSettings;
        }

        public static string ResolveRemoteSettingsWritePath(string remoteAppPath)
        {
            string root = NormalizeRemoteRoot(remoteAppPath);
            string dataSettings = Path.Combine(root, "data", "settings.json");
            if (File.Exists(dataSettings) || Directory.Exists(Path.Combine(root, "data")) || Directory.Exists(Path.Combine(root, "app")))
                return dataSettings;

            if (IsLegacyFlatRemoteRoot(root))
                return Path.Combine(root, "settings.json");

            return dataSettings;
        }

        public static string ResolveRemoteBackupsDir(string remoteAppPath)
        {
            string root = NormalizeRemoteRoot(remoteAppPath);
            return Path.Combine(root, "backups");
        }

        public static string DetectRemoteAppVersionFromDisk(string remoteAppPath)
        {
            string root = NormalizeRemoteRoot(remoteAppPath);
            string[] versionFiles =
            {
                Path.Combine(root, "app", "version.txt"),
                Path.Combine(root, "version.txt")
            };

            foreach (string vf in versionFiles)
            {
                if (!File.Exists(vf)) continue;
                try
                {
                    string vText = File.ReadAllText(vf).Trim();
                    if (!string.IsNullOrWhiteSpace(vText))
                        return vText;
                }
                catch (Exception ex) { Debug.WriteLine($"[RemoteNodeDeploymentService] Read version.txt skipped ({vf}): {ex.Message}"); }
            }

            string[] binaries =
            {
                Path.Combine(root, "app", "Housekeeping.Core.dll"),
                Path.Combine(root, "app", "Housekeeping.dll"),
                Path.Combine(root, "Housekeeping.Core.dll"),
                Path.Combine(root, "Housekeeping.dll"),
                Path.Combine(root, "Housekeeping.exe")
            };

            foreach (string bin in binaries)
            {
                if (!File.Exists(bin)) continue;
                try
                {
                    var fvi = FileVersionInfo.GetVersionInfo(bin);
                    string? raw = !string.IsNullOrWhiteSpace(fvi.ProductVersion) ? fvi.ProductVersion : fvi.FileVersion;
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        int plusIdx = raw.IndexOf('+');
                        return (plusIdx > 0 ? raw[..plusIdx] : raw).Trim();
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[RemoteNodeDeploymentService] Binary version probe skipped ({bin}): {ex.Message}"); }
            }

            return "Unknown";
        }

        public static bool ShouldExcludeFromRemoteBinarySync(string relativePath, string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return true;

            if (ExcludedRemoteFileNames.Contains(fileName))
                return true;

            if (fileName.StartsWith("settings", StringComparison.OrdinalIgnoreCase) &&
                (fileName.Contains(".json", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)))
                return true;

            if (ExcludedRemoteExtensions.Any(ext => fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                return true;

            string norm = relativePath.Replace('/', '\\');
            string[] segments = norm.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (ExcludedRemoteDirSegments.Contains(segments[i]))
                    return true;
            }

            return false;
        }

        public static bool SendRemoteRestartSignal(RemoteNodeConfig node, Action<string, string> log)
        {
            try
            {
                if (node == null || string.IsNullOrWhiteSpace(node.RemoteAppPath)) return false;
                string root = NormalizeRemoteRoot(node.RemoteAppPath);
                string signalDir = Directory.Exists(Path.Combine(root, "data")) ? Path.Combine(root, "data") : root;
                Directory.CreateDirectory(signalDir);
                ConfigService.AtomicWriteAllText(Path.Combine(signalDir, "restart_request.signal"), $"Manual restart requested at {DateTime.UtcNow:o}");
                log("INFO", $"Dispatched restart signal to '{node.Name}'. The remote app will restart automatically.");
                return true;
            }
            catch (Exception ex)
            {
                log("ERROR", $"Failed to send restart signal to '{node?.Name}': {ex.Message}");
                return false;
            }
        }

        public static bool SendRemoteControlCommand(RemoteNodeConfig node, RemoteControlRequest req, Action<string, string> log)
        {
            try
            {
                if (node == null || string.IsNullOrWhiteSpace(node.RemoteAppPath)) return false;
                string root = NormalizeRemoteRoot(node.RemoteAppPath);
                string signalDir = Directory.Exists(Path.Combine(root, "data")) ? Path.Combine(root, "data") : root;
                Directory.CreateDirectory(signalDir);
                string json = JsonSerializer.Serialize(req, new JsonSerializerOptions { WriteIndented = true });
                ConfigService.AtomicWriteAllText(Path.Combine(signalDir, "control_request.json"), json);
                log("INFO", $"Dispatched '{req.Action}' command to remote node '{node.Name}' (TargetQueue: {req.TargetQueueName ?? req.TargetQueueId ?? "Active"}, TargetJob: {req.TargetJobName ?? req.TargetJobId ?? "None"}).");
                return true;
            }
            catch (Exception ex)
            {
                log("ERROR", $"Failed to send '{req.Action}' command to '{node?.Name}': {ex.Message}");
                return false;
            }
        }

        public static (List<NamedJobQueue> Queues, List<HousekeepingJob> Jobs) ReadRemoteQueuesAndJobs(RemoteNodeConfig node)
        {
            try
            {
                if (node == null || string.IsNullOrWhiteSpace(node.RemoteAppPath))
                    return (new List<NamedJobQueue>(), new List<HousekeepingJob>());

                string settingsPath = ResolveRemoteSettingsReadPath(node.RemoteAppPath);
                if (File.Exists(settingsPath))
                {
                    string json = ConfigService.ReadAllTextWithRetry(settingsPath);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (cfg != null)
                    {
                        cfg.EnsureNamedQueuesInitialized();
                        return (cfg.NamedQueues ?? new(), cfg.DefinedJobs ?? new());
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[RemoteNodeDeploymentService] ReadRemoteQueuesAndJobs skipped: {ex.Message}"); }
            return (new List<NamedJobQueue>(), new List<HousekeepingJob>());
        }
    }
}
