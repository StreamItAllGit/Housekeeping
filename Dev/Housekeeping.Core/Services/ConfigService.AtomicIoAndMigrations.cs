// @module: Housekeeping.Services.ConfigService.AtomicIoAndMigrations
// @purpose: Per-caller atomic UTF-8 No-BOM disk writes, legacy root state migration, schema migrations, and --repair-config quarantine/recovery.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, System.Linq, System.Text, System.Text.Json, Housekeeping.Models
// @exports: Housekeeping.Services.ConfigService (partial: AtomicWriteAllText, ReadAllTextWithRetry, MigrateConfig, RepairAndValidateConfig, MigrateLegacyRootStateIfPresent)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Housekeeping.Models;

namespace Housekeeping.Services
{
    public partial class ConfigService
    {
        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        public static void AtomicWriteAllText(string targetPath, string content)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
                throw new ArgumentException("Target path cannot be empty.", nameof(targetPath));

            string fullTarget = Path.GetFullPath(targetPath);
            string? dir = Path.GetDirectoryName(fullTarget);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string tmpPath = $"{fullTarget}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
            try
            {
                using (var fs = new FileStream(tmpPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.WriteThrough))
                using (var writer = new StreamWriter(fs, Utf8NoBom))
                {
                    writer.Write(content);
                    writer.Flush();
                    fs.Flush(flushToDisk: true);
                }

                for (int attempt = 1; attempt <= 5; attempt++)
                {
                    try
                    {
                        File.Move(tmpPath, fullTarget, overwrite: true);
                        return;
                    }
                    catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < 5)
                    {
                        int jitterMs = Random.Shared.Next(40, 181) * attempt;
                        Thread.Sleep(Math.Min(jitterMs, 600));
                    }
                }

                File.Copy(tmpPath, fullTarget, overwrite: true);
            }
            finally
            {
                if (File.Exists(tmpPath))
                {
                    try { File.Delete(tmpPath); }
                    catch (Exception ex) { Debug.WriteLine($"[ConfigService] Leftover atomic tmp cleanup skipped ({tmpPath}): {ex.Message}"); }
                }
            }
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
                    Thread.Sleep(delayMs * attempt);
                }
            }
            return File.ReadAllText(path, Encoding.UTF8);
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

            SanitizeDeprecatedModule4ManifestFlags(config);
            config.UiLayout ??= new UiLayoutConfig();
        }

        public static void SanitizeDeprecatedModule4ManifestFlags(AppConfig config)
        {
            if (config.RomLauncherGenerator != null)
                config.RomLauncherGenerator.EnableCollectionManifestCsv = false;

            if (config.DefinedJobs != null)
            {
                foreach (var job in config.DefinedJobs)
                {
                    if (job.ModuleType == ModuleId.RomLauncherGenerator && job.RomLauncherGenerator != null)
                        job.RomLauncherGenerator.EnableCollectionManifestCsv = false;
                }
            }

            if (config.NamedQueues != null)
            {
                foreach (var q in config.NamedQueues)
                {
                    if (q.Jobs == null) continue;
                    foreach (var job in q.Jobs)
                    {
                        if (job.ModuleType == ModuleId.RomLauncherGenerator && job.RomLauncherGenerator != null)
                            job.RomLauncherGenerator.EnableCollectionManifestCsv = false;
                    }
                }
            }
        }

        public bool RepairAndValidateConfig(out string summaryMessage)
        {
            lock (_ioLock)
            {
                if (File.Exists(ActiveConfigFilePath))
                {
                    try
                    {
                        string rawJson = ReadAllTextWithRetry(ActiveConfigFilePath);
                        int detectedVer = 1;
                        using (var doc = JsonDocument.Parse(rawJson))
                        {
                            if (doc.RootElement.TryGetProperty("ConfigVersion", out var vp) && vp.TryGetInt32(out int v))
                                detectedVer = v;
                        }

                        var parsed = JsonSerializer.Deserialize<AppConfig>(rawJson, JsonOptions);
                        if (parsed != null)
                        {
                            MigrateConfig(parsed, detectedVer, AppConfig.CurrentSchemaVersion);
                            parsed.ConfigVersion = AppConfig.CurrentSchemaVersion;
                            parsed.EnsureNamedQueuesInitialized();
                            parsed.EnsureDefinedJobsSynchronized();
                            parsed.WebServer.EnsureNodeApiTokenInitialized();

                            if (ValidateConfig(parsed, out var valErrors))
                            {
                                if (!PersistCandidateLocked(parsed, out string? saveErr))
                                {
                                    summaryMessage = saveErr ?? "Failed to write repaired settings.json.";
                                    return false;
                                }
                                summaryMessage = $"Validated and normalized settings.json (schema v{AppConfig.CurrentSchemaVersion}, {parsed.DefinedJobs.Count} jobs, {parsed.NamedQueues.Count} queues).";
                                return true;
                            }
                            throw new InvalidOperationException("Validation failed: " + string.Join("; ", valErrors));
                        }
                    }
                    catch (Exception ex)
                    {
                        string quarantineDir = Path.Combine(DataDirectory, "quarantine");
                        Directory.CreateDirectory(quarantineDir);
                        string quarantinePath = Path.Combine(quarantineDir, $"settings.corrupt_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
                        try { File.Copy(ActiveConfigFilePath, quarantinePath, overwrite: true); }
                        catch (Exception qEx) { Debug.WriteLine($"[ConfigService] Quarantine copy warning: {qEx.Message}"); }

                        AppConfig? recovered = TryLoadNewestValidBackupLocked(out string? sourceLabel);
                        if (recovered == null)
                        {
                            recovered = AppConfig.CreateDefaults();
                            sourceLabel = "clean defaults";
                        }

                        MigrateConfig(recovered, 1, AppConfig.CurrentSchemaVersion);
                        recovered.ConfigVersion = AppConfig.CurrentSchemaVersion;
                        recovered.EnsureNamedQueuesInitialized();
                        recovered.EnsureDefinedJobsSynchronized();
                        recovered.WebServer.EnsureNodeApiTokenInitialized();

                        if (!PersistCandidateLocked(recovered, out string? recErr))
                        {
                            summaryMessage = $"Quarantined corrupt settings.json ({ex.Message}) to {quarantinePath}, but recovery write failed: {recErr}";
                            return false;
                        }

                        summaryMessage = $"Quarantined corrupt settings.json ({ex.Message}) to '{quarantinePath}' and restored from {sourceLabel}.";
                        return true;
                    }
                }

                var fresh = AppConfig.CreateDefaults();
                fresh.WebServer.EnsureNodeApiTokenInitialized();
                if (!PersistCandidateLocked(fresh, out string? initErr))
                {
                    summaryMessage = initErr ?? "Failed to initialize clean settings.json.";
                    return false;
                }
                summaryMessage = $"Initialized clean settings.json at '{ActiveConfigFilePath}' (schema v{AppConfig.CurrentSchemaVersion}).";
                return true;
            }
        }

        private AppConfig? TryLoadNewestValidBackupLocked(out string? sourceLabel)
        {
            sourceLabel = null;
            var candidates = new List<FileInfo>();
            if (Directory.Exists(DataDirectory))
                candidates.AddRange(new DirectoryInfo(DataDirectory).GetFiles("settings.json.bak*"));
            if (Directory.Exists(SnapshotsDir))
                candidates.AddRange(new DirectoryInfo(SnapshotsDir).GetFiles("settings_*.json"));

            foreach (var file in candidates.OrderByDescending(f => f.LastWriteTimeUtc))
            {
                try
                {
                    string json = ReadAllTextWithRetry(file.FullName);
                    using var _ = JsonDocument.Parse(json);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                    if (cfg != null && ValidateConfig(cfg, out var errs) && errs.Count == 0)
                    {
                        sourceLabel = file.Name;
                        return cfg;
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[ConfigService] Backup candidate skipped ({file.Name}): {ex.Message}"); }
            }
            return null;
        }
    }
}
