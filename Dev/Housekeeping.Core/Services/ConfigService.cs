using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Housekeeping.Models;
using Microsoft.Win32;

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

    public class ConfigService
    {
        public const int MaxConfigSnapshots = 20;

        public static readonly string AppDataDir = ResolveDefaultDataDirectory(AppDomain.CurrentDomain.BaseDirectory);
        public static readonly string ConfigFilePath = Path.Combine(AppDataDir, "settings.json");
        public static readonly string HistoryFilePath = Path.Combine(AppDataDir, "history.json");
        public static readonly string LogsDir = Path.Combine(AppDataDir, "logs");

        public static string ResolveDefaultDataDirectory(string baseDir)
        {
            if (string.IsNullOrWhiteSpace(baseDir))
            {
                return AppDomain.CurrentDomain.BaseDirectory;
            }

            try
            {
                string trimmed = Path.GetFullPath(baseDir)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string folderName = Path.GetFileName(trimmed);
                if (folderName.Equals("app", StringComparison.OrdinalIgnoreCase))
                {
                    DirectoryInfo? parent = Directory.GetParent(trimmed);
                    if (parent != null && parent.Exists)
                    {
                        string dataDir = Path.Combine(parent.FullName, "data");
                        MigrateLegacyRootStateIfPresent(parent.FullName, dataDir, out _);
                        return dataDir;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigService] ResolveDefaultDataDirectory fallback: {ex.Message}");
            }

            return baseDir;
        }

        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private readonly object _ioLock = new();
        private readonly bool _isCustomBaseDir;
        private readonly bool _isCustomConfigPath;
        private readonly bool _isCustomLogsDir;
        private string _lastSnapshotSha256 = "";

        public string InstallRootDirectory { get; }
        public string DataDirectory { get; }
        public string ActiveConfigFilePath { get; }
        public string ActiveHistoryFilePath { get; }
        public string ActiveLogsDir { get; }
        public string SnapshotsDir { get; }
        public bool IsTestMode { get; }
        public bool IsSandboxed => _isCustomBaseDir || _isCustomConfigPath || _isCustomLogsDir || IsTestMode;

        public AppConfig Config { get; private set; } = new();
        public List<RunHistoryEntry> History { get; private set; } = new();
        public string? LastMigrationMessage { get; private set; }

        public ConfigService(
            string? customBaseDir = null,
            string? customConfigFilePath = null,
            string? customLogsDir = null,
            bool isTestMode = false)
        {
            IsTestMode = isTestMode || IsTruthyEnv("HOUSEKEEPING_TEST_MODE") || IsTruthyEnv("TEST_MODE");

            string? resolvedConfigPath = customConfigFilePath;
            if (string.IsNullOrWhiteSpace(resolvedConfigPath))
            {
                resolvedConfigPath = Environment.GetEnvironmentVariable("HOUSEKEEPING_CONFIG")
                                     ?? Environment.GetEnvironmentVariable("CONFIG_PATH");
            }
            _isCustomConfigPath = !string.IsNullOrWhiteSpace(resolvedConfigPath);

            string? resolvedBaseDir = customBaseDir;
            if (string.IsNullOrWhiteSpace(resolvedBaseDir))
            {
                resolvedBaseDir = Environment.GetEnvironmentVariable("HOUSEKEEPING_DATA_DIR")
                                  ?? Environment.GetEnvironmentVariable("DATA_DIR")
                                  ?? Environment.GetEnvironmentVariable("HOUSEKEEPING_HOME")
                                  ?? Environment.GetEnvironmentVariable("APP_HOME");
            }

            if (string.IsNullOrWhiteSpace(resolvedBaseDir) && _isCustomConfigPath)
            {
                resolvedBaseDir = Path.GetDirectoryName(Path.GetFullPath(resolvedConfigPath!));
            }

            _isCustomBaseDir = !string.IsNullOrWhiteSpace(resolvedBaseDir);

            string runtimeBase = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            bool runtimeInApp = string.Equals(Path.GetFileName(runtimeBase), "app", StringComparison.OrdinalIgnoreCase);
            string defaultInstallRoot = runtimeInApp && Directory.GetParent(runtimeBase) is { } rParent
                ? rParent.FullName
                : runtimeBase;

            if (_isCustomBaseDir)
            {
                string normCustom = Path.GetFullPath(resolvedBaseDir!)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string customName = Path.GetFileName(normCustom);

                if (customName.Equals("app", StringComparison.OrdinalIgnoreCase) &&
                    Directory.GetParent(normCustom) is { } appParent && appParent.Exists &&
                    (File.Exists(Path.Combine(appParent.FullName, "settings.json")) ||
                     Directory.Exists(Path.Combine(appParent.FullName, "data"))))
                {
                    InstallRootDirectory = appParent.FullName;
                    DataDirectory = Path.Combine(appParent.FullName, "data");
                }
                else if (customName.Equals("data", StringComparison.OrdinalIgnoreCase) &&
                         Directory.GetParent(normCustom) is { } dataParent && dataParent.Exists)
                {
                    InstallRootDirectory = dataParent.FullName;
                    DataDirectory = normCustom;
                }
                else
                {
                    InstallRootDirectory = normCustom;
                    DataDirectory = normCustom;
                }
            }
            else
            {
                InstallRootDirectory = defaultInstallRoot;
                DataDirectory = runtimeInApp
                    ? Path.Combine(InstallRootDirectory, "data")
                    : AppDataDir;
            }

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

            ActiveConfigFilePath = _isCustomConfigPath
                ? Path.GetFullPath(resolvedConfigPath!)
                : Path.Combine(DataDirectory, "settings.json");
            ActiveHistoryFilePath = Path.Combine(DataDirectory, "history.json");

            bool dataIsInstallRootData = string.Equals(
                DataDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.Combine(InstallRootDirectory, "data"),
                StringComparison.OrdinalIgnoreCase);

            string? resolvedLogsDir = customLogsDir;
            if (string.IsNullOrWhiteSpace(resolvedLogsDir))
            {
                resolvedLogsDir = Environment.GetEnvironmentVariable("HOUSEKEEPING_LOG_DIR")
                                  ?? Environment.GetEnvironmentVariable("LOG_DIR");
            }
            _isCustomLogsDir = !string.IsNullOrWhiteSpace(resolvedLogsDir);
            ActiveLogsDir = _isCustomLogsDir
                ? Path.GetFullPath(resolvedLogsDir!)
                : (dataIsInstallRootData
                    ? Path.Combine(InstallRootDirectory, "logs")
                    : Path.Combine(DataDirectory, "logs"));

            SnapshotsDir = dataIsInstallRootData
                ? Path.Combine(InstallRootDirectory, "backups", "config_snapshots")
                : Path.Combine(DataDirectory, "backups", "config_snapshots");

            Directory.CreateDirectory(DataDirectory);
            string? cfgParent = Path.GetDirectoryName(ActiveConfigFilePath);
            if (!string.IsNullOrWhiteSpace(cfgParent))
            {
                Directory.CreateDirectory(cfgParent);
            }
            Directory.CreateDirectory(ActiveLogsDir);
            Directory.CreateDirectory(SnapshotsDir);
            if (!_isCustomConfigPath)
            {
                TryAdoptParentCanonicalSettings(DataDirectory, ActiveConfigFilePath);
            }
            Load();
            LoadHistory();
        }

        public static bool MigrateLegacyRootStateIfPresent(string installRoot, string dataDirectory, out string? migrationMessage)
        {
            migrationMessage = null;
            bool migratedAny = false;
            try
            {
                string normRoot = Path.GetFullPath(installRoot)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string normData = Path.GetFullPath(dataDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(normRoot, normData, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                Directory.CreateDirectory(normData);

                string rootSettings = Path.Combine(normRoot, "settings.json");
                string appSettings = Path.Combine(normRoot, "app", "settings.json");
                string dataSettings = Path.Combine(normData, "settings.json");

                long rootLen = File.Exists(rootSettings) ? new FileInfo(rootSettings).Length : 0;
                long appLen = File.Exists(appSettings) ? new FileInfo(appSettings).Length : 0;
                long dataLen = File.Exists(dataSettings) ? new FileInfo(dataSettings).Length : 0;

                if (rootLen > 0)
                {
                    if (dataLen == 0 || (rootLen > 30_000 && dataLen < rootLen) || rootLen > dataLen)
                    {
                        File.Copy(rootSettings, dataSettings, overwrite: true);
                        dataLen = rootLen;
                        migrationMessage = $"[ConfigService] Migrated canonical root settings.json ({rootLen} bytes) to data\\settings.json.";
                        migratedAny = true;
                    }
                    try { File.Delete(rootSettings); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ConfigService] Delete root settings.json ignored: {ex.Message}"); }
                }

                if (appLen > 0)
                {
                    if (dataLen == 0 || (appLen > 30_000 && dataLen < appLen))
                    {
                        File.Copy(appSettings, dataSettings, overwrite: true);
                        dataLen = appLen;
                        migrationMessage ??= $"[ConfigService] Migrated app\\settings.json ({appLen} bytes) to data\\settings.json.";
                        migratedAny = true;
                    }
                    try { File.Delete(appSettings); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ConfigService] Delete app settings.json ignored: {ex.Message}"); }
                }

                string[] companionFiles =
                {
                    "settings.json.bak",
                    "settings.json.tmp",
                    "history.json",
                    "compliance_reports.json",
                    "pending_swaps.json",
                    "interrupted_run_checkpoint.json",
                    ".hk_cookie_secret.key"
                };

                foreach (string fileName in companionFiles)
                {
                    if (MigrateSingleRootStateFile(Path.Combine(normRoot, fileName), Path.Combine(normData, fileName)))
                    {
                        migratedAny = true;
                    }
                    string appCompanion = Path.Combine(normRoot, "app", fileName);
                    if (File.Exists(appCompanion))
                    {
                        if (MigrateSingleRootStateFile(appCompanion, Path.Combine(normData, fileName)))
                        {
                            migratedAny = true;
                        }
                    }
                }

                if (Directory.Exists(normRoot))
                {
                    foreach (string bakFile in Directory.EnumerateFiles(normRoot, "settings.json.bak*", SearchOption.TopDirectoryOnly))
                    {
                        string name = Path.GetFileName(bakFile);
                        if (MigrateSingleRootStateFile(bakFile, Path.Combine(normData, name)))
                        {
                            migratedAny = true;
                        }
                    }
                }

                string[] runtimeDataDirs = { "dats", "tools", "manifests", "cache", "crashes" };
                foreach (string dirName in runtimeDataDirs)
                {
                    string srcDir = Path.Combine(normRoot, dirName);
                    string dstDir = Path.Combine(normData, dirName);
                    if (Directory.Exists(srcDir))
                    {
                        MoveOrMergeDirectory(srcDir, dstDir);
                        migratedAny = true;
                    }
                }

                string rootWebViewCache = Path.Combine(normRoot, "WebView2Cache");
                string dstWebViewCache = Path.Combine(normData, "webview2_cache");
                if (Directory.Exists(rootWebViewCache))
                {
                    MoveOrMergeDirectory(rootWebViewCache, dstWebViewCache);
                    migratedAny = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigService] MigrateLegacyRootStateIfPresent ignored: {ex.Message}");
            }

            return migratedAny;
        }

        private static bool MigrateSingleRootStateFile(string srcFile, string dstFile)
        {
            if (!File.Exists(srcFile))
                return false;

            try
            {
                long srcLen = new FileInfo(srcFile).Length;
                long dstLen = File.Exists(dstFile) ? new FileInfo(dstFile).Length : 0;
                if (!File.Exists(dstFile) || srcLen > dstLen)
                {
                    File.Copy(srcFile, dstFile, overwrite: true);
                }
                File.Delete(srcFile);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigService] MigrateSingleRootStateFile ({srcFile}) ignored: {ex.Message}");
                return false;
            }
        }

        private static void MoveOrMergeDirectory(string srcDir, string dstDir)
        {
            try
            {
                if (!Directory.Exists(srcDir))
                    return;

                if (!Directory.Exists(dstDir))
                {
                    try
                    {
                        Directory.Move(srcDir, dstDir);
                        return;
                    }
                    catch (Exception moveEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ConfigService] Directory.Move fallback to recursive merge ({srcDir}): {moveEx.Message}");
                    }
                }

                Directory.CreateDirectory(dstDir);
                foreach (string file in Directory.GetFiles(srcDir, "*", SearchOption.TopDirectoryOnly))
                {
                    string destFile = Path.Combine(dstDir, Path.GetFileName(file));
                    MigrateSingleRootStateFile(file, destFile);
                }

                foreach (string sub in Directory.GetDirectories(srcDir, "*", SearchOption.TopDirectoryOnly))
                {
                    string destSub = Path.Combine(dstDir, Path.GetFileName(sub));
                    MoveOrMergeDirectory(sub, destSub);
                }

                if (!Directory.EnumerateFileSystemEntries(srcDir).Any())
                {
                    Directory.Delete(srcDir, recursive: false);
                }
                else
                {
                    Directory.Delete(srcDir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigService] MoveOrMergeDirectory ({srcDir} -> {dstDir}) ignored: {ex.Message}");
            }
        }

        private void TryAdoptParentCanonicalSettings(string dataDir, string activeConfigPath)
        {
            try
            {
                string trimmed = Path.GetFullPath(dataDir)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string folderName = Path.GetFileName(trimmed);
                if (!folderName.Equals("app", StringComparison.OrdinalIgnoreCase) &&
                    !folderName.Equals("data", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                DirectoryInfo? parent = Directory.GetParent(trimmed);
                if (parent == null || !parent.Exists)
                {
                    return;
                }

                if (MigrateLegacyRootStateIfPresent(parent.FullName, trimmed, out string? migMsg) &&
                    !string.IsNullOrWhiteSpace(migMsg))
                {
                    LastMigrationMessage = migMsg;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigService] TryAdoptParentCanonicalSettings ignored: {ex.Message}");
            }
        }

        private static bool IsTruthyEnv(string name)
        {
            string? val = Environment.GetEnvironmentVariable(name);
            return !string.IsNullOrWhiteSpace(val) &&
                   (val.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                    val.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                    val.Equals("yes", StringComparison.OrdinalIgnoreCase));
        }

        public static void AtomicWriteAllText(string targetPath, string content)
        {
            string? dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string tmpPath = targetPath + ".tmp";
            File.WriteAllText(tmpPath, content, Encoding.UTF8);

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    File.Move(tmpPath, targetPath, overwrite: true);
                    return;
                }
                catch (IOException) when (attempt < 3)
                {
                    System.Threading.Thread.Sleep(50 * attempt);
                }
            }

            File.Copy(tmpPath, targetPath, overwrite: true);
            try { File.Delete(tmpPath); } catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[ConfigService] Non-fatal error ignored: {__suppressedEx1.Message}"); }
        }

        public static string ReadAllTextWithRetry(string path, int maxAttempts = 3, int delayMs = 150)
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var sr = new StreamReader(fs, Encoding.UTF8);
                    return sr.ReadToEnd();
                }
                catch (IOException) when (attempt < maxAttempts)
                {
                    System.Threading.Thread.Sleep(delayMs * attempt);
                }
            }
            return File.ReadAllText(path);
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

        public static void MigrateConfig(AppConfig config, int fromVersion, int toVersion)
        {
            if (fromVersion < 2)
            {
                if (config.HandBrakeProfiles == null || config.HandBrakeProfiles.Count == 0)
                    config.HandBrakeProfiles = HandBrakeEncodingProfile.CreateDefaultProfiles();

                config.MigrateLegacyModulesToJobsIfNeeded();

                if (config.DistributedLockTimeoutMinutes <= 0)
                    config.DistributedLockTimeoutMinutes = 90;
                config.EnableDistributedNetworkLocking = true;
            }

            if (fromVersion < 3)
            {
                config.EnsureNamedQueuesInitialized();
                config.EnsureDefinedJobsSynchronized();
            }

            if (fromVersion < 4)
            {
                if (config.WebServer != null &&
                    (string.IsNullOrWhiteSpace(config.WebServer.DefaultDesktopLaunchMode) ||
                     string.Equals(config.WebServer.DefaultDesktopLaunchMode, "Browser", StringComparison.OrdinalIgnoreCase)))
                {
                    config.WebServer.DefaultDesktopLaunchMode = "StandaloneWindow";
                }
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
                if (!IsSandboxed)
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

        private static void ApplyWindowsStartupSetting(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null) return;

                const string appKey = "Housekeeping2";
                if (enable)
                {
                    string exePath = Environment.ProcessPath ?? Path.Combine(AppDataDir, "Housekeeping.exe");
                    key.SetValue(appKey, $"\"{exePath}\" --minimized");
                }
                else
                {
                    if (key.GetValue(appKey) != null)
                        key.DeleteValue(appKey, false);
                }
            }
            catch (System.Exception __suppressedEx13) { System.Diagnostics.Debug.WriteLine($"[ConfigService] Non-fatal error ignored: {__suppressedEx13.Message}"); }
        }
    }
}
