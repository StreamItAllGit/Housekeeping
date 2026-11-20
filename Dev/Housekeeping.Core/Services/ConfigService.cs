using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Foundation.Core;
using Housekeeping.Interop;
using Housekeeping.Models;

namespace Housekeeping.Services
{
    public class ConfigSnapshotInfo
    {
        public string SnapshotId { get; set; } = "";
        public DateTime CreatedAtUtc { get; set; }
        public long SizeBytes { get; set; }
        public int DefinedJobCount { get; set; }
        public int QueueCount { get; set; }
    }

    public partial class ConfigService
    {
        public const int MaxConfigSnapshots = 20;

        public static readonly string AppDataDir = ResolveDefaultDataDirectory(AppDomain.CurrentDomain.BaseDirectory);
        public static readonly string ConfigFilePath = Path.Combine(AppDataDir, "settings.json");
        public static readonly string HistoryFilePath = Path.Combine(AppDataDir, "history.json");
        public static readonly string LogsDir = Path.Combine(AppDataDir, "logs");

        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: true) }
        };

        private readonly object _ioLock = new();
        private readonly bool _isCustomBaseDir;
        private readonly bool _isCustomConfigPath;
        private readonly bool _isCustomLogsDir;
        private string _lastSnapshotSha256 = "";

        public SandboxResolution Sandbox { get; }
        public bool IsSandboxActive => Sandbox.IsSandboxActive;
        public bool IsStage1Isolated { get; }
        public bool ShouldSuppressHostMutations => IsStage1Isolated;
        public bool ShouldSuppressLiveHostMutations => IsStage1Isolated || Sandbox.IsSandboxActive;

        public string InstallRootDirectory { get; }
        public string DataDirectory { get; }
        public string ActiveConfigFilePath { get; }
        public string ActiveHistoryFilePath { get; }
        public string ActiveLogsDir { get; }
        public string SnapshotsDir { get; }
        public bool IsIsolatedOverride { get; }

        public AppConfig Config { get; private set; } = new();
        public List<RunHistoryEntry> History { get; private set; } = new();
        public string? LastMigrationMessage { get; private set; }

        public ConfigService(
            string? customBaseDir = null,
            string? customConfigFilePath = null,
            string? customLogsDir = null,
            bool isolatedOverride = false)
        {
            ResolvedServicePaths resolved = ResolvePathsAndSandbox(customBaseDir, customConfigFilePath, customLogsDir, isolatedOverride);
            Sandbox = resolved.Sandbox;
            IsStage1Isolated = resolved.IsStage1Isolated;
            IsIsolatedOverride = resolved.IsIsolatedOverride;
            _isCustomBaseDir = resolved.IsCustomBaseDir;
            _isCustomConfigPath = resolved.IsCustomConfigPath;
            _isCustomLogsDir = resolved.IsCustomLogsDir;

            InstallRootDirectory = resolved.InstallRootDirectory;
            DataDirectory = resolved.DataDirectory;
            ActiveConfigFilePath = resolved.ActiveConfigFilePath;
            ActiveHistoryFilePath = resolved.ActiveHistoryFilePath;
            ActiveLogsDir = resolved.ActiveLogsDir;
            SnapshotsDir = resolved.SnapshotsDir;

            if (!_isCustomConfigPath &&
                !string.Equals(
                    InstallRootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    DataDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                if (MigrateLegacyRootStateIfPresent(InstallRootDirectory, DataDirectory, out string? migMsg) &&
                    !string.IsNullOrWhiteSpace(migMsg))
                {
                    LastMigrationMessage = migMsg;
                }
            }

            Directory.CreateDirectory(DataDirectory);
            string? cfgParent = Path.GetDirectoryName(ActiveConfigFilePath);
            if (!string.IsNullOrWhiteSpace(cfgParent))
                Directory.CreateDirectory(cfgParent);
            Directory.CreateDirectory(ActiveLogsDir);
            Directory.CreateDirectory(SnapshotsDir);
            EnsureSettingsExampleCopied();
            MediaInfoNativeResolver.ConfigureDataDirectory(DataDirectory);

            if (!_isCustomConfigPath)
                TryAdoptParentCanonicalSettings(DataDirectory, ActiveConfigFilePath);

            Load();
            LoadHistory();
        }

        public void Load()
        {
            lock (_ioLock)
            {
                if (File.Exists(ActiveConfigFilePath))
                {
                    try
                    {
                        string json = ReadAllTextWithRetry(ActiveConfigFilePath);

                        int detectedVersion = 1;
                        try
                        {
                            using var doc = JsonDocument.Parse(json);
                            if (doc.RootElement.TryGetProperty("ConfigVersion", out var verProp) && verProp.TryGetInt32(out int v))
                            {
                                detectedVersion = v;
                            }
                        }
                        catch (System.Exception __suppressedEx2) { System.Diagnostics.Debug.WriteLine($"[ConfigService] Non-fatal error ignored: {__suppressedEx2.Message}"); }

                        var loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                        if (loaded != null)
                        {
                            if (detectedVersion < AppConfig.CurrentSchemaVersion)
                            {
                                string backupFileName = $"settings.json.bak_v{detectedVersion}_{DateTime.Now:yyyyMMdd_HHmmss}";
                                string backupFilePath = Path.Combine(DataDirectory, backupFileName);
                                try
                                {
                                    AtomicWriteAllText(backupFilePath, json);
                                }
                                catch (System.Exception __suppressedEx3) { System.Diagnostics.Debug.WriteLine($"[ConfigService] Non-fatal error ignored: {__suppressedEx3.Message}"); }

                                MigrateConfig(loaded, detectedVersion, AppConfig.CurrentSchemaVersion);
                                loaded.ConfigVersion = AppConfig.CurrentSchemaVersion;
                                LastMigrationMessage = $"Migrated configuration from schema v{detectedVersion} to v{AppConfig.CurrentSchemaVersion}. Backup saved as {backupFileName}.";

                                loaded.WebServer.EnsureNodeApiTokenInitialized();
                                Config = loaded;
                                Save();
                                return;
                            }

                            if (loaded.HandBrakeProfiles == null || loaded.HandBrakeProfiles.Count == 0)
                                loaded.HandBrakeProfiles = HandBrakeEncodingProfile.CreateDefaultProfiles();

                            SanitizeDeprecatedModule4ManifestFlags(loaded);
                            loaded.UiLayout ??= new UiLayoutConfig();
                            loaded.MigrateLegacyModulesToJobsIfNeeded();
                            loaded.EnsureNamedQueuesInitialized();
                            loaded.EnsureDefinedJobsSynchronized();
                            bool hadToken = !string.IsNullOrWhiteSpace(loaded.WebServer.NodeApiToken) && loaded.WebServer.NodeApiToken.Length >= 24;
                            loaded.WebServer.EnsureNodeApiTokenInitialized();
                            Config = loaded;
                            _lastSnapshotSha256 = ComputeSha256Hex(json);
                            if (!hadToken)
                            {
                                Save();
                            }
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        string backupName = $"settings.json.backup_{DateTime.Now:yyyyMMdd_HHmmss}.bak";
                        string backupPath = Path.Combine(DataDirectory, backupName);
                        try
                        {
                            File.Copy(ActiveConfigFilePath, backupPath, overwrite: true);
                        }
                        catch (System.Exception __suppressedEx4) { System.Diagnostics.Debug.WriteLine($"[ConfigService] Non-fatal error ignored: {__suppressedEx4.Message}"); }

                        LastMigrationMessage = $"Warning: Unreadable settings.json ({ex.Message}) was backed up to {backupName}. Loaded defaults in-memory without overwriting disk.";
                        Config = AppConfig.CreateDefaults();
                        Config.WebServer.EnsureNodeApiTokenInitialized();
                        return;
                    }
                }

                Config = AppConfig.CreateDefaults();
                Config.WebServer.EnsureNodeApiTokenInitialized();
                Save();
            }
        }

        public static bool ValidateConfig(AppConfig candidate, out List<string> errors)
        {
            var collectedErrors = new List<string>();
            errors = collectedErrors;
            if (candidate == null)
            {
                collectedErrors.Add("Configuration payload cannot be null.");
                return false;
            }

            if (candidate.WebServer != null && (candidate.WebServer.Port < 1 || candidate.WebServer.Port > 65535))
            {
                collectedErrors.Add($"WebServer.Port ({candidate.WebServer.Port}) must be between 1 and 65535.");
            }

            if (candidate.GreenroomIntegration != null && (candidate.GreenroomIntegration.CompanionPort < 1 || candidate.GreenroomIntegration.CompanionPort > 65535))
            {
                collectedErrors.Add($"GreenroomIntegration.CompanionPort ({candidate.GreenroomIntegration.CompanionPort}) must be between 1 and 65535.");
            }

            // Validate Regex rules in root CollectionManifest and all DefinedJobs / NamedQueues
            void CheckFieldRules(string scope, IEnumerable<ManifestFieldParseRule>? rules)
            {
                if (rules == null) return;
                foreach (var rule in rules)
                {
                    if (!rule.Enabled || string.IsNullOrEmpty(rule.Pattern))
                        continue;

                    if (rule.MatchMode == ManifestMatchMode.RegexPattern || rule.MatchMode == ManifestMatchMode.RegexNamedCapture)
                    {
                        try
                        {
                            _ = new Regex(rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                        }
                        catch (ArgumentException rex)
                        {
                            collectedErrors.Add($"[{scope}] Invalid regex in column '{rule.ColumnName}': {rex.Message}");
                        }
                    }
                }
            }

            CheckFieldRules("CollectionManifest", candidate.CollectionManifest?.FieldParseRules);

            var allJobs = new Dictionary<string, HousekeepingJob>(StringComparer.OrdinalIgnoreCase);
            if (candidate.DefinedJobs != null)
            {
                foreach (var job in candidate.DefinedJobs)
                {
                    if (!string.IsNullOrWhiteSpace(job.JobId))
                        allJobs[job.JobId] = job;
                    if (job.CollectionManifest != null)
                        CheckFieldRules($"Job '{job.Name}'", job.CollectionManifest.FieldParseRules);
                }
            }
            if (candidate.NamedQueues != null)
            {
                foreach (var q in candidate.NamedQueues)
                {
                    if (q.Jobs == null) continue;
                    foreach (var job in q.Jobs)
                    {
                        if (!string.IsNullOrWhiteSpace(job.JobId) && !allJobs.ContainsKey(job.JobId))
                            allJobs[job.JobId] = job;
                        if (job.CollectionManifest != null)
                            CheckFieldRules($"Queue '{q.Name}' -> Job '{job.Name}'", job.CollectionManifest.FieldParseRules);
                    }
                }
            }

            // Validate OnSuccessTriggerJobId chains against cycles (A -> B -> A or A -> A)
            foreach (var job in allJobs.Values)
            {
                if (string.IsNullOrWhiteSpace(job.OnSuccessTriggerJobId))
                    continue;

                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { job.JobId };
                string? cursor = job.OnSuccessTriggerJobId;
                while (!string.IsNullOrWhiteSpace(cursor))
                {
                    if (!visited.Add(cursor))
                    {
                        errors.Add($"Circular post-job trigger chain detected starting at job '{job.Name}' ({job.JobId}).");
                        break;
                    }
                    if (!allJobs.TryGetValue(cursor, out var nextJob))
                        break;
                    cursor = nextJob.OnSuccessTriggerJobId;
                }
            }

            return errors.Count == 0;
        }

        public bool TryUpdateAndSave(AppConfig updated, out List<string> validationErrors)
        {
            if (!ValidateConfig(updated, out validationErrors))
            {
                return false;
            }

            lock (_ioLock)
            {
                var candidate = updated.DeepClone();

                // Preserve server-managed auth hashes if client sent masked/blank placeholders
                if (string.IsNullOrEmpty(candidate.WebServer.AdminPasswordHash) && !string.IsNullOrEmpty(Config.WebServer.AdminPasswordHash))
                {
                    candidate.WebServer.AdminPasswordHash = Config.WebServer.AdminPasswordHash;
                    candidate.WebServer.AdminPasswordSalt = Config.WebServer.AdminPasswordSalt;
                }
                if (string.IsNullOrWhiteSpace(candidate.WebServer.NodeApiToken))
                {
                    candidate.WebServer.NodeApiToken = Config.WebServer.NodeApiToken;
                }
                candidate.WebServer.EnsureNodeApiTokenInitialized();

                // Preserve server-owned schedule LastRunUtc timestamps against stale browser/PWA snapshots
                if (Config.LastMasterScheduleRunUtc.HasValue &&
                    (!candidate.LastMasterScheduleRunUtc.HasValue ||
                     candidate.LastMasterScheduleRunUtc.Value < Config.LastMasterScheduleRunUtc.Value))
                {
                    candidate.LastMasterScheduleRunUtc = Config.LastMasterScheduleRunUtc;
                }

                if (Config.NamedQueues != null && candidate.NamedQueues != null)
                {
                    foreach (var candQ in candidate.NamedQueues)
                    {
                        if (candQ == null || string.IsNullOrWhiteSpace(candQ.Id))
                            continue;
                        var serverQ = Config.NamedQueues.FirstOrDefault(sq =>
                            sq != null && string.Equals(sq.Id, candQ.Id, StringComparison.OrdinalIgnoreCase));
                        if (serverQ?.LastRunUtc != null &&
                            (!candQ.LastRunUtc.HasValue || candQ.LastRunUtc.Value < serverQ.LastRunUtc.Value))
                        {
                            candQ.LastRunUtc = serverQ.LastRunUtc;
                        }
                    }
                }

                PreserveModuleScheduleUtc(Config.ProxyGenerator, candidate.ProxyGenerator);
                PreserveModuleScheduleUtc(Config.ArchiveCompressor, candidate.ArchiveCompressor);
                PreserveModuleScheduleUtc(Config.MediaTranscoder, candidate.MediaTranscoder);
                PreserveModuleScheduleUtc(Config.RomLauncherGenerator, candidate.RomLauncherGenerator);
                PreserveModuleScheduleUtc(Config.CloudSync, candidate.CloudSync);
                PreserveModuleScheduleUtc(Config.HumbleSync, candidate.HumbleSync);
                PreserveModuleScheduleUtc(Config.CustomScripts, candidate.CustomScripts);
                PreserveModuleScheduleUtc(Config.CollectionManifest, candidate.CollectionManifest);

                if (!PersistCandidateLocked(candidate, out string? ioError))
                {
                    validationErrors.Add(ioError ?? "Failed to write settings.json to disk.");
                    return false;
                }
                return true;
            }
        }

        private static void PreserveModuleScheduleUtc(ModuleBaseConfig? current, ModuleBaseConfig? candidate)
        {
            if (current?.LastScheduledRunUtc != null &&
                candidate != null &&
                (!candidate.LastScheduledRunUtc.HasValue || candidate.LastScheduledRunUtc.Value < current.LastScheduledRunUtc.Value))
            {
                candidate.LastScheduledRunUtc = current.LastScheduledRunUtc;
            }
        }

        public bool MutateAndSave(Action<AppConfig> mutator, out string? errorMessage)
        {
            if (mutator == null) throw new ArgumentNullException(nameof(mutator));
            lock (_ioLock)
            {
                var draft = Config.DeepClone();
                mutator(draft);
                return PersistCandidateLocked(draft, out errorMessage);
            }
        }

        public bool MutateAndSave(Action<AppConfig> mutator) => MutateAndSave(mutator, out _);

        public void Save() => TrySave(out _);

        public bool TrySave(out string? ioError)
        {
            lock (_ioLock)
            {
                var snapshot = Config.DeepClone();
                return PersistCandidateLocked(snapshot, out ioError);
            }
        }

        private bool PersistCandidateLocked(AppConfig candidate, out string? ioError)
        {
            ioError = null;
            try
            {
                candidate.EnsureNamedQueuesInitialized();
                candidate.EnsureDefinedJobsSynchronized();
                candidate.WebServer.EnsureNodeApiTokenInitialized();
                candidate.PrepareForSave();
                string json = JsonSerializer.Serialize(candidate, JsonOptions);
                AtomicWriteAllText(ActiveConfigFilePath, json);
                WriteRollingSnapshotLocked(json);
                Config = candidate;
                if (!ShouldSuppressHostMutations)
                {
                    ApplyWindowsStartupSetting(candidate.StartWithWindows);
                }
                return true;
            }
            catch (Exception ex)
            {
                ioError = $"Failed to persist settings.json: {ex.Message}";
                return false;
            }
        }

        private void WriteRollingSnapshotLocked(string json)
        {
            try
            {
                string sha = ComputeSha256Hex(json);
                if (string.Equals(sha, _lastSnapshotSha256, StringComparison.OrdinalIgnoreCase))
                    return;

                Directory.CreateDirectory(SnapshotsDir);

                // Also skip if identical to most recent existing file on disk
                var existing = new DirectoryInfo(SnapshotsDir)
                    .GetFiles("settings_*.json")
                    .OrderByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (existing.Count > 0)
                {
                    try
                    {
                        string latestJson = File.ReadAllText(existing[0].FullName, Encoding.UTF8);
                        if (string.Equals(ComputeSha256Hex(latestJson), sha, StringComparison.OrdinalIgnoreCase))
                        {
                            _lastSnapshotSha256 = sha;
                            return;
                        }
                    }
                    catch (System.Exception __suppressedEx5) { System.Diagnostics.Debug.WriteLine($"[ConfigService] Non-fatal error ignored: {__suppressedEx5.Message}"); }
                }

                string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                string snapshotId = $"settings_{stamp}";
                string snapshotPath = Path.Combine(SnapshotsDir, $"{snapshotId}.json");
                int suffix = 1;
                while (File.Exists(snapshotPath))
                {
                    snapshotId = $"settings_{stamp}_{suffix++}";
                    snapshotPath = Path.Combine(SnapshotsDir, $"{snapshotId}.json");
                }

                AtomicWriteAllText(snapshotPath, json);
                _lastSnapshotSha256 = sha;

                // Prune older snapshots beyond MaxConfigSnapshots (20)
                var allFiles = new DirectoryInfo(SnapshotsDir)
                    .GetFiles("settings_*.json")
                    .OrderByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                for (int i = MaxConfigSnapshots; i < allFiles.Count; i++)
                {
                    try { allFiles[i].Delete(); } catch (System.Exception __suppressedEx6) { System.Diagnostics.Debug.WriteLine($"[ConfigService] Non-fatal error ignored: {__suppressedEx6.Message}"); }
                }
            }
            catch (System.Exception __suppressedEx7) { System.Diagnostics.Debug.WriteLine($"[ConfigService] Non-fatal error ignored: {__suppressedEx7.Message}"); }
        }

        public List<ConfigSnapshotInfo> ListSnapshots()
        {
            lock (_ioLock)
            {
                var result = new List<ConfigSnapshotInfo>();
                if (!Directory.Exists(SnapshotsDir))
                    return result;

                var files = new DirectoryInfo(SnapshotsDir)
                    .GetFiles("settings_*.json")
                    .OrderByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(MaxConfigSnapshots)
                    .ToList();

                foreach (var f in files)
                {
                    int jobCount = 0;
                    int queueCount = 0;
                    try
                    {
                        using var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var doc = JsonDocument.Parse(fs);
                        if (doc.RootElement.TryGetProperty("DefinedJobs", out var dj) && dj.ValueKind == JsonValueKind.Array)
                            jobCount = dj.GetArrayLength();
                        if (doc.RootElement.TryGetProperty("NamedQueues", out var nq) && nq.ValueKind == JsonValueKind.Array)
                            queueCount = nq.GetArrayLength();
                    }
                    catch (System.Exception __suppressedEx8) { System.Diagnostics.Debug.WriteLine($"[ConfigService] Non-fatal error ignored: {__suppressedEx8.Message}"); }

                    result.Add(new ConfigSnapshotInfo
                    {
                        SnapshotId = Path.GetFileNameWithoutExtension(f.Name),
                        CreatedAtUtc = f.LastWriteTimeUtc,
                        SizeBytes = f.Length,
                        DefinedJobCount = jobCount,
                        QueueCount = queueCount
                    });
                }

                return result;
            }
        }

        public bool RestoreSnapshot(string snapshotId, out string errorMessage)
        {
            errorMessage = "";
            if (string.IsNullOrWhiteSpace(snapshotId) ||
                snapshotId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                snapshotId.Contains(".."))
            {
                errorMessage = "Invalid snapshot identifier.";
                return false;
            }

            lock (_ioLock)
            {
                string cleanId = snapshotId.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileNameWithoutExtension(snapshotId)
                    : snapshotId;
                string path = Path.Combine(SnapshotsDir, $"{cleanId}.json");
                if (!File.Exists(path))
                {
                    errorMessage = $"Snapshot '{cleanId}' was not found.";
                    return false;
                }

                try
                {
                    string json = ReadAllTextWithRetry(path);
                    var restored = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                    if (restored == null)
                    {
                        errorMessage = "Snapshot did not deserialize into a valid Housekeeping configuration.";
                        return false;
                    }

                    restored.MigrateLegacyModulesToJobsIfNeeded();
                    restored.EnsureNamedQueuesInitialized();
                    restored.EnsureDefinedJobsSynchronized();

                    if (!ValidateConfig(restored, out var valErrors))
                    {
                        errorMessage = string.Join("; ", valErrors);
                        return false;
                    }

                    // Always preserve active host admin credentials & node token across config rollbacks
                    if (!string.IsNullOrEmpty(Config.WebServer.AdminPasswordHash))
                    {
                        restored.WebServer.AdminPasswordHash = Config.WebServer.AdminPasswordHash;
                        restored.WebServer.AdminPasswordSalt = Config.WebServer.AdminPasswordSalt;
                    }
                    if (!string.IsNullOrWhiteSpace(Config.WebServer.NodeApiToken))
                    {
                        restored.WebServer.NodeApiToken = Config.WebServer.NodeApiToken;
                    }
                    restored.WebServer.EnsureNodeApiTokenInitialized();

                    if (!PersistCandidateLocked(restored, out string? ioErr))
                    {
                        errorMessage = ioErr ?? "Failed to write restored settings.json.";
                        return false;
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    errorMessage = ex.Message;
                    return false;
                }
            }
        }

        private static string ComputeSha256Hex(string text)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            return Convert.ToHexString(hash);
        }

        public void ResetToCleanDefaults()
        {
            lock (_ioLock)
            {
                var fresh = AppConfig.CreateDefaults();
                fresh.WebServer.EnsureNodeApiTokenInitialized();
                PersistCandidateLocked(fresh, out _);
            }
        }

        public string ExportProfileJson(bool stripSensitiveTokens)
        {
            lock (_ioLock)
            {
                var target = stripSensitiveTokens ? Config.CreateSanitizedCopyForSharing() : Config;
                return JsonSerializer.Serialize(target, JsonOptions);
            }
        }

        public void ExportProfileToFile(string filePath, bool stripSensitiveTokens)
        {
            string json = ExportProfileJson(stripSensitiveTokens);
            File.WriteAllText(filePath, json, Encoding.UTF8);
        }

        public bool ImportProfileFromJson(string json, out string errorMessage)
        {
            errorMessage = "";
            try
            {
                var imported = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (imported == null)
                {
                    errorMessage = "Profile did not contain a valid Housekeeping configuration.";
                    return false;
                }

                int detectedVersion = 1;
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("ConfigVersion", out var verProp) && verProp.TryGetInt32(out int v))
                    {
                        detectedVersion = v;
                    }
                }
                catch (System.Exception __suppressedEx9) { System.Diagnostics.Debug.WriteLine($"[ConfigService] Non-fatal error ignored: {__suppressedEx9.Message}"); }

                if (detectedVersion < AppConfig.CurrentSchemaVersion)
                {
                    MigrateConfig(imported, detectedVersion, AppConfig.CurrentSchemaVersion);
                    imported.ConfigVersion = AppConfig.CurrentSchemaVersion;
                }

                if (imported.HandBrakeProfiles == null || imported.HandBrakeProfiles.Count == 0)
                    imported.HandBrakeProfiles = HandBrakeEncodingProfile.CreateDefaultProfiles();

                imported.MigrateLegacyModulesToJobsIfNeeded();
                if (!ValidateConfig(imported, out var valErrors))
                {
                    errorMessage = string.Join("; ", valErrors);
                    return false;
                }

                lock (_ioLock)
                {
                    // Always preserve existing host admin password hash & node API token so imported profiles cannot overwrite host auth
                    if (!string.IsNullOrEmpty(Config.WebServer.AdminPasswordHash))
                    {
                        imported.WebServer.AdminPasswordHash = Config.WebServer.AdminPasswordHash;
                        imported.WebServer.AdminPasswordSalt = Config.WebServer.AdminPasswordSalt;
                    }
                    if (!string.IsNullOrWhiteSpace(Config.WebServer.NodeApiToken))
                    {
                        imported.WebServer.NodeApiToken = Config.WebServer.NodeApiToken;
                    }
                    imported.WebServer.EnsureNodeApiTokenInitialized();

                    if (!PersistCandidateLocked(imported, out string? ioErr))
                    {
                        errorMessage = ioErr ?? "Failed to save imported profile to disk.";
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public bool ImportProfileFromFile(string filePath, out string errorMessage)
        {
            try
            {
                string json = File.ReadAllText(filePath, Encoding.UTF8);
                return ImportProfileFromJson(json, out errorMessage);
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public HandBrakeEncodingProfile ResolveHandBrakeProfile(string profileId, string fallbackProfileId)
        {
            foreach (var p in Config.HandBrakeProfiles)
            {
                if (p.ProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Equals(profileId, StringComparison.OrdinalIgnoreCase))
                {
                    return p;
                }
            }

            foreach (var p in Config.HandBrakeProfiles)
            {
                if (p.ProfileId.Equals(fallbackProfileId, StringComparison.OrdinalIgnoreCase))
                    return p;
            }

            return HandBrakeEncodingProfile.CreateDefaultProfiles()[0];
        }

        public void LoadHistory()
        {
            lock (_ioLock)
            {
                try
                {
                    if (File.Exists(ActiveHistoryFilePath))
                    {
                        string json = ReadAllTextWithRetry(ActiveHistoryFilePath);
                        var list = JsonSerializer.Deserialize<List<RunHistoryEntry>>(json, JsonOptions);
                        if (list != null)
                        {
                            History = list;
                            return;
                        }
                    }
                }
                catch (System.Exception __suppressedEx10) { System.Diagnostics.Debug.WriteLine($"[ConfigService] Non-fatal error ignored: {__suppressedEx10.Message}"); }

                History = new List<RunHistoryEntry>();
            }
        }

        public void AddHistoryEntry(RunHistoryEntry entry)
        {
            lock (_ioLock)
            {
                History.Insert(0, entry);
                if (History.Count > 200)
                    History.RemoveRange(200, History.Count - 200);

                try
                {
                    string json = JsonSerializer.Serialize(History, JsonOptions);
                    AtomicWriteAllText(ActiveHistoryFilePath, json);
                }
                catch (System.Exception __suppressedEx11) { System.Diagnostics.Debug.WriteLine($"[ConfigService] Non-fatal error ignored: {__suppressedEx11.Message}"); }
            }
        }

        public void ClearHistory()
        {
            lock (_ioLock)
            {
                History.Clear();
                try
                {
                    if (File.Exists(ActiveHistoryFilePath))
                        File.Delete(ActiveHistoryFilePath);
                }
                catch (System.Exception __suppressedEx12) { System.Diagnostics.Debug.WriteLine($"[ConfigService] Non-fatal error ignored: {__suppressedEx12.Message}"); }
            }
        }
    }
}
