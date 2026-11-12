// @module: Housekeeping.Services.ConfigService.LegacyRootMigration
// @purpose: One-time migration of legacy flat-root settings.json and companion runtime state into Clean Release Root data\.
// @depends-on: System, System.Diagnostics, System.IO, System.Linq
// @exports: Housekeeping.Services.ConfigService (partial: MigrateLegacyRootStateIfPresent)

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Housekeeping.Services
{
    public partial class ConfigService
    {
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
                    return false;

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
                    try { File.Delete(rootSettings); }
                    catch (Exception ex) { Debug.WriteLine($"[ConfigService] Delete root settings.json ignored: {ex.Message}"); }
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
                    try { File.Delete(appSettings); }
                    catch (Exception ex) { Debug.WriteLine($"[ConfigService] Delete app settings.json ignored: {ex.Message}"); }
                }

                string[] companionFiles =
                {
                    "settings.json.bak", "settings.json.tmp", "history.json",
                    "compliance_reports.json", "pending_swaps.json",
                    "interrupted_run_checkpoint.json", ".hk_cookie_secret.key"
                };

                foreach (string fileName in companionFiles)
                {
                    if (MigrateSingleRootStateFile(Path.Combine(normRoot, fileName), Path.Combine(normData, fileName)))
                        migratedAny = true;
                    string appCompanion = Path.Combine(normRoot, "app", fileName);
                    if (File.Exists(appCompanion) && MigrateSingleRootStateFile(appCompanion, Path.Combine(normData, fileName)))
                        migratedAny = true;
                }

                if (Directory.Exists(normRoot))
                {
                    foreach (string bakFile in Directory.EnumerateFiles(normRoot, "settings.json.bak*", SearchOption.TopDirectoryOnly))
                    {
                        if (MigrateSingleRootStateFile(bakFile, Path.Combine(normData, Path.GetFileName(bakFile))))
                            migratedAny = true;
                    }
                }

                string[] runtimeDataDirs = { "dats", "tools", "manifests", "cache", "crashes" };
                foreach (string dirName in runtimeDataDirs)
                {
                    string srcDir = Path.Combine(normRoot, dirName);
                    if (Directory.Exists(srcDir))
                    {
                        MoveOrMergeDirectory(srcDir, Path.Combine(normData, dirName));
                        migratedAny = true;
                    }
                }

                string rootWebViewCache = Path.Combine(normRoot, "WebView2Cache");
                if (Directory.Exists(rootWebViewCache))
                {
                    MoveOrMergeDirectory(rootWebViewCache, Path.Combine(normData, "webview2_cache"));
                    migratedAny = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConfigService] MigrateLegacyRootStateIfPresent ignored: {ex.Message}");
            }

            return migratedAny;
        }

        private static bool MigrateSingleRootStateFile(string srcFile, string dstFile)
        {
            if (!File.Exists(srcFile)) return false;
            try
            {
                long srcLen = new FileInfo(srcFile).Length;
                long dstLen = File.Exists(dstFile) ? new FileInfo(dstFile).Length : 0;
                if (!File.Exists(dstFile) || srcLen > dstLen)
                    File.Copy(srcFile, dstFile, overwrite: true);
                File.Delete(srcFile);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConfigService] MigrateSingleRootStateFile ({srcFile}) ignored: {ex.Message}");
                return false;
            }
        }

        private static void MoveOrMergeDirectory(string srcDir, string dstDir)
        {
            try
            {
                if (!Directory.Exists(srcDir)) return;
                if (!Directory.Exists(dstDir))
                {
                    try { Directory.Move(srcDir, dstDir); return; }
                    catch (Exception moveEx) { Debug.WriteLine($"[ConfigService] Directory.Move fallback ({srcDir}): {moveEx.Message}"); }
                }

                Directory.CreateDirectory(dstDir);
                foreach (string file in Directory.GetFiles(srcDir, "*", SearchOption.TopDirectoryOnly))
                    MigrateSingleRootStateFile(file, Path.Combine(dstDir, Path.GetFileName(file)));

                foreach (string sub in Directory.GetDirectories(srcDir, "*", SearchOption.TopDirectoryOnly))
                    MoveOrMergeDirectory(sub, Path.Combine(dstDir, Path.GetFileName(sub)));

                Directory.Delete(srcDir, recursive: true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConfigService] MoveOrMergeDirectory ({srcDir}) ignored: {ex.Message}");
            }
        }
    }
}
