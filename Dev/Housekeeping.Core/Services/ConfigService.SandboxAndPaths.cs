// @module: Housekeeping.Services.ConfigService.SandboxAndPaths
// @purpose: Foundation v0.5.3 SandboxEnvironmentSeam/KnownFolderPaths containment, startup registry routing, path resolution, and settings.example.json seeding.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, Foundation.Core
// @exports: Housekeeping.Services.ConfigService (partial: ResolveDefaultDataDirectory, ResolveKnownFolder, EnsureSettingsExampleCopied, GetActiveSandboxDriveRoots)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Foundation.Core;

namespace Housekeeping.Services
{
    public partial class ConfigService
    {
        private sealed record ResolvedServicePaths(
            SandboxResolution Sandbox,
            bool IsStage1Isolated,
            bool IsIsolatedOverride,
            bool IsCustomBaseDir,
            bool IsCustomConfigPath,
            bool IsCustomLogsDir,
            string InstallRootDirectory,
            string DataDirectory,
            string ActiveConfigFilePath,
            string ActiveHistoryFilePath,
            string ActiveLogsDir,
            string SnapshotsDir);

        public static string ResolveDefaultDataDirectory(string baseDir)
        {
            if (string.IsNullOrWhiteSpace(baseDir))
                return AppDomain.CurrentDomain.BaseDirectory;

            try
            {
                string trimmed = Path.GetFullPath(baseDir)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (Path.GetFileName(trimmed).Equals("app", StringComparison.OrdinalIgnoreCase) &&
                    Directory.GetParent(trimmed) is { Exists: true } parent)
                {
                    string dataDir = Path.Combine(parent.FullName, "data");
                    MigrateLegacyRootStateIfPresent(parent.FullName, dataDir, out _);
                    return dataDir;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConfigService] ResolveDefaultDataDirectory fallback: {ex.Message}");
            }

            return baseDir;
        }

        public static string ResolveKnownFolder(Environment.SpecialFolder folder, SandboxResolution? sandbox = null) =>
            KnownFolderPaths.GetFolderPath(folder, sandbox);

        public IReadOnlyList<string> GetActiveSandboxDriveRoots()
        {
            if (!Sandbox.IsSandboxActive)
                return Array.Empty<string>();

            var roots = new List<string>(4);
            foreach (string? raw in new[] { Sandbox.SystemDriveRoot, Sandbox.StorageDriveRoot, Sandbox.RemovableDriveRoot, Sandbox.DiscDriveRoot })
            {
                if (!string.IsNullOrWhiteSpace(raw))
                    roots.Add(raw);
            }
            return roots;
        }

        public bool EnsureSettingsExampleCopied() =>
            CopySettingsExampleIfMissing(InstallRootDirectory, DataDirectory, Sandbox);

        public static bool CopySettingsExampleIfMissing(
            string appBaseOrInstallRoot,
            string dataDirectory,
            SandboxResolution? sandbox = null,
            string? explicitBaseDirectory = null)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory))
                return false;

            SandboxResolution res = sandbox ?? SandboxEnvironmentSeam.ResolveCurrent();
            string destPath = Path.Combine(Path.GetFullPath(dataDirectory), "settings.example.json");
            if (res.IsSandboxActive)
                SandboxEnvironmentSeam.AssertSandboxPathIfActive(destPath, res);

            if (File.Exists(destPath))
                return false;

            string baseDir = !string.IsNullOrWhiteSpace(explicitBaseDirectory)
                ? Path.GetFullPath(explicitBaseDirectory)
                : AppDomain.CurrentDomain.BaseDirectory;
            string rootDir = string.IsNullOrWhiteSpace(appBaseOrInstallRoot) ? baseDir : Path.GetFullPath(appBaseOrInstallRoot);

            string[] candidates =
            {
                Path.Combine(baseDir, "config", "settings.example.json"),
                Path.Combine(rootDir, "app", "config", "settings.example.json"),
                Path.Combine(rootDir, "config", "settings.example.json"),
                Path.Combine(baseDir, "settings.example.json")
            };

            foreach (string src in candidates)
            {
                if (!File.Exists(src))
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                File.Copy(src, destPath, overwrite: false);
                return true;
            }

            return false;
        }

        private static ResolvedServicePaths ResolvePathsAndSandbox(
            string? customBaseDir, string? customConfigFilePath, string? customLogsDir, bool isolatedOverride)
        {
            SandboxResolution sandbox = SandboxEnvironmentSeam.ResolveCurrent();

            string? resolvedConfigPath = !string.IsNullOrWhiteSpace(customConfigFilePath)
                ? customConfigFilePath
                : (Environment.GetEnvironmentVariable("HOUSEKEEPING_CONFIG") ?? Environment.GetEnvironmentVariable("CONFIG_PATH"));
            bool isCustomConfigPath = !string.IsNullOrWhiteSpace(resolvedConfigPath);

            string? resolvedBaseDir = !string.IsNullOrWhiteSpace(customBaseDir)
                ? customBaseDir
                : (Environment.GetEnvironmentVariable("HOUSEKEEPING_DATA_DIR") ?? Environment.GetEnvironmentVariable("DATA_DIR")
                    ?? Environment.GetEnvironmentVariable("HOUSEKEEPING_HOME") ?? Environment.GetEnvironmentVariable("APP_HOME"));

            if (string.IsNullOrWhiteSpace(resolvedBaseDir) && isCustomConfigPath)
                resolvedBaseDir = Path.GetDirectoryName(Path.GetFullPath(resolvedConfigPath!));

            bool isCustomBaseDir = !string.IsNullOrWhiteSpace(resolvedBaseDir);
            string? resolvedLogsDir = !string.IsNullOrWhiteSpace(customLogsDir)
                ? customLogsDir
                : (Environment.GetEnvironmentVariable("HOUSEKEEPING_LOG_DIR") ?? Environment.GetEnvironmentVariable("LOG_DIR"));
            bool isCustomLogsDir = !string.IsNullOrWhiteSpace(resolvedLogsDir);

            bool appIsolatedEnv = IsTruthyEnv(SandboxEnvironmentSeam.EnvIsolatedTest);
            bool isStage1Isolated = appIsolatedEnv || (!sandbox.IsSandboxActive && (isolatedOverride || isCustomBaseDir || isCustomConfigPath || isCustomLogsDir));
            bool isIsolatedOverride = isStage1Isolated || sandbox.IsSandboxActive;

            string runtimeBase = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            bool runtimeInApp = string.Equals(Path.GetFileName(runtimeBase), "app", StringComparison.OrdinalIgnoreCase);
            string defaultInstallRoot = runtimeInApp && Directory.GetParent(runtimeBase) is { } rParent ? rParent.FullName : runtimeBase;

            string installRootDir;
            string dataDir;
            if (isCustomBaseDir)
            {
                string normCustom = Path.GetFullPath(resolvedBaseDir!).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string customName = Path.GetFileName(normCustom);
                if (customName.Equals("app", StringComparison.OrdinalIgnoreCase) && Directory.GetParent(normCustom) is { Exists: true } appParent &&
                    (File.Exists(Path.Combine(appParent.FullName, "settings.json")) || Directory.Exists(Path.Combine(appParent.FullName, "data"))))
                {
                    installRootDir = appParent.FullName;
                    dataDir = Path.Combine(appParent.FullName, "data");
                }
                else if (customName.Equals("data", StringComparison.OrdinalIgnoreCase) && Directory.GetParent(normCustom) is { Exists: true } dataParent)
                {
                    installRootDir = dataParent.FullName;
                    dataDir = normCustom;
                }
                else
                {
                    installRootDir = normCustom;
                    dataDir = normCustom;
                }
            }
            else
            {
                installRootDir = defaultInstallRoot;
                dataDir = runtimeInApp ? Path.Combine(installRootDir, "data") : AppDataDir;
            }

            string activeConfigFilePath = isCustomConfigPath ? Path.GetFullPath(resolvedConfigPath!) : Path.Combine(dataDir, "settings.json");
            string activeHistoryFilePath = Path.Combine(dataDir, "history.json");
            bool dataIsInstallRootData = string.Equals(dataDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), Path.Combine(installRootDir, "data"), StringComparison.OrdinalIgnoreCase);
            string activeLogsDir = isCustomLogsDir ? Path.GetFullPath(resolvedLogsDir!) : (dataIsInstallRootData ? Path.Combine(installRootDir, "logs") : Path.Combine(dataDir, "logs"));
            string snapshotsDir = dataIsInstallRootData ? Path.Combine(installRootDir, "backups", "config_snapshots") : Path.Combine(dataDir, "backups", "config_snapshots");

            if (sandbox.IsSandboxActive)
            {
                foreach (string p in new[] { installRootDir, dataDir, activeConfigFilePath, activeLogsDir, snapshotsDir })
                    SandboxEnvironmentSeam.AssertSandboxPathIfActive(p, sandbox);
            }

            return new ResolvedServicePaths(
                sandbox, isStage1Isolated, isIsolatedOverride, isCustomBaseDir, isCustomConfigPath, isCustomLogsDir,
                installRootDir, dataDir, activeConfigFilePath, activeHistoryFilePath, activeLogsDir, snapshotsDir);
        }

        private void TryAdoptParentCanonicalSettings(string dataDir, string activeConfigPath)
        {
            try
            {
                string trimmed = Path.GetFullPath(dataDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string folderName = Path.GetFileName(trimmed);
                if (!folderName.Equals("app", StringComparison.OrdinalIgnoreCase) &&
                    !folderName.Equals("data", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                DirectoryInfo? parent = Directory.GetParent(trimmed);
                if (parent == null || !parent.Exists)
                    return;

                if (MigrateLegacyRootStateIfPresent(parent.FullName, trimmed, out string? migMsg) &&
                    !string.IsNullOrWhiteSpace(migMsg))
                {
                    LastMigrationMessage = migMsg;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConfigService] TryAdoptParentCanonicalSettings ignored: {ex.Message}");
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

        private void ApplyWindowsStartupSetting(bool enable)
        {
            try
            {
                IPlatformRegistry registry = PlatformRegistryFactory.CreateDefault(Sandbox, suppressMutations: ShouldSuppressHostMutations);
                const string runSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
                const string appKey = "Housekeeping2";

                if (enable)
                {
                    string exePath = Environment.ProcessPath ?? Path.Combine(InstallRootDirectory, "Housekeeping.exe");
                    registry.SetValue(PlatformRegistryHive.CurrentUser, runSubKey, appKey, $"\"{exePath}\" --minimized", PlatformRegistryValueKind.String);
                }
                else if (registry.GetValue(PlatformRegistryHive.CurrentUser, runSubKey, appKey) != null)
                {
                    registry.DeleteValue(PlatformRegistryHive.CurrentUser, runSubKey, appKey, throwOnMissingValue: false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConfigService] Startup registry sync ignored: {ex.Message}");
            }
        }
    }
}
