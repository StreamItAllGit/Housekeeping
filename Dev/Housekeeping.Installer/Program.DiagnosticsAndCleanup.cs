// @module: Housekeeping.Installer.Program.DiagnosticsAndCleanup
// @purpose: Legacy state migration into data\ and Clean Release Root obsolete file/directory cleanup during upgrades.
// @depends-on: System.Diagnostics, System.IO
// @exports: Housekeeping.Installer.Program (partial: MigrateLegacyUserState, CleanupObsoleteInstallationFiles)

using System.Diagnostics;

namespace Housekeeping.Installer;

public static partial class Program
{
    public static void MigrateLegacyUserState(string installDir, string? logDir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir))
                return;

            string dataDir = Path.Combine(installDir, "data");
            string logsSubDir = Path.Combine(installDir, "logs");
            string backupsDir = Path.Combine(installDir, "backups");
            Directory.CreateDirectory(dataDir);
            Directory.CreateDirectory(logsSubDir);
            Directory.CreateDirectory(backupsDir);

            string rootSettings = Path.Combine(installDir, "settings.json");
            string appDir = Path.Combine(installDir, "app");
            string appSettings = Path.Combine(appDir, "settings.json");
            string dataSettings = Path.Combine(dataDir, "settings.json");

            long rootLen = File.Exists(rootSettings) ? new FileInfo(rootSettings).Length : 0;
            long appLen = File.Exists(appSettings) ? new FileInfo(appSettings).Length : 0;
            long dataLen = File.Exists(dataSettings) ? new FileInfo(dataSettings).Length : 0;

            if (rootLen > 0)
            {
                if (dataLen == 0 || (rootLen > 30_000 && dataLen < rootLen) || rootLen > dataLen)
                {
                    File.Copy(rootSettings, dataSettings, overwrite: true);
                    dataLen = rootLen;
                    WriteLog(logDir, "INFO", $"Migrated canonical root settings.json ({rootLen} bytes) to data\\settings.json.");
                }
                try
                {
                    File.SetAttributes(rootSettings, FileAttributes.Normal);
                    File.Delete(rootSettings);
                }
                catch (Exception ex) { Debug.WriteLine($"[Housekeeping.Installer] Delete root settings.json ignored: {ex.Message}"); }
            }

            if (appLen > 0)
            {
                if (dataLen == 0 || (appLen > 30_000 && dataLen < appLen))
                {
                    File.Copy(appSettings, dataSettings, overwrite: true);
                    dataLen = appLen;
                    WriteLog(logDir, "INFO", $"Migrated app\\settings.json ({appLen} bytes) to data\\settings.json.");
                }
                try
                {
                    File.SetAttributes(appSettings, FileAttributes.Normal);
                    File.Delete(appSettings);
                }
                catch (Exception ex) { Debug.WriteLine($"[Housekeeping.Installer] Delete app settings.json ignored: {ex.Message}"); }
            }

            string[] stateFiles =
            [
                "settings.json.bak", "settings.json.tmp", "history.json",
                "compliance_reports.json", "pending_swaps.json",
                "interrupted_run_checkpoint.json", ".hk_cookie_secret.key"
            ];

            foreach (string fileName in stateFiles)
            {
                MigrateSingleStateFileToData(Path.Combine(installDir, fileName), Path.Combine(dataDir, fileName), logDir);
                if (Directory.Exists(appDir))
                    MigrateSingleStateFileToData(Path.Combine(appDir, fileName), Path.Combine(dataDir, fileName), logDir);
            }

            foreach (string bakFile in Directory.EnumerateFiles(installDir, "settings.json.bak*", SearchOption.TopDirectoryOnly))
            {
                MigrateSingleStateFileToData(bakFile, Path.Combine(dataDir, Path.GetFileName(bakFile)), logDir);
            }

            string[] runtimeFoldersToMoveToData = ["dats", "tools", "manifests", "cache", "crashes"];
            foreach (string folderName in runtimeFoldersToMoveToData)
            {
                string srcFolder = Path.Combine(installDir, folderName);
                if (Directory.Exists(srcFolder))
                    MoveOrMergeDirectoryToData(srcFolder, Path.Combine(dataDir, folderName), logDir);
            }

            string rootWebView2 = Path.Combine(installDir, "WebView2Cache");
            if (Directory.Exists(rootWebView2))
                MoveOrMergeDirectoryToData(rootWebView2, Path.Combine(dataDir, "webview2_cache"), logDir);
        }
        catch (Exception ex)
        {
            WriteLog(logDir, "WARN", $"MigrateLegacyUserState warning: {ex.Message}");
        }
    }

    private static void MigrateSingleStateFileToData(string srcFile, string dstFile, string? logDir)
    {
        if (!File.Exists(srcFile)) return;
        try
        {
            long srcLen = new FileInfo(srcFile).Length;
            long dstLen = File.Exists(dstFile) ? new FileInfo(dstFile).Length : 0;
            if (!File.Exists(dstFile) || srcLen > dstLen)
            {
                File.Copy(srcFile, dstFile, overwrite: true);
                WriteLog(logDir, "INFO", $"Migrated '{Path.GetFileName(srcFile)}' ({srcLen} bytes) to data\\.");
            }
            File.SetAttributes(srcFile, FileAttributes.Normal);
            File.Delete(srcFile);
        }
        catch (Exception ex) { Debug.WriteLine($"[Housekeeping.Installer] MigrateSingleStateFileToData ({srcFile}) ignored: {ex.Message}"); }
    }

    private static void MoveOrMergeDirectoryToData(string srcDir, string dstDir, string? logDir)
    {
        try
        {
            if (!Directory.Exists(srcDir)) return;
            if (!Directory.Exists(dstDir))
            {
                try
                {
                    Directory.Move(srcDir, dstDir);
                    WriteLog(logDir, "INFO", $"Moved runtime directory '{Path.GetFileName(srcDir)}' into data\\{Path.GetFileName(dstDir)}.");
                    return;
                }
                catch (Exception moveEx) { Debug.WriteLine($"[Housekeeping.Installer] Directory.Move fallback ({srcDir}): {moveEx.Message}"); }
            }

            Directory.CreateDirectory(dstDir);
            foreach (string file in Directory.GetFiles(srcDir, "*", SearchOption.TopDirectoryOnly))
                MigrateSingleStateFileToData(file, Path.Combine(dstDir, Path.GetFileName(file)), logDir);

            foreach (string sub in Directory.GetDirectories(srcDir, "*", SearchOption.TopDirectoryOnly))
                MoveOrMergeDirectoryToData(sub, Path.Combine(dstDir, Path.GetFileName(sub)), logDir);

            Directory.Delete(srcDir, recursive: true);
        }
        catch (Exception ex) { Debug.WriteLine($"[Housekeeping.Installer] MoveOrMergeDirectoryToData ({srcDir}) ignored: {ex.Message}"); }
    }

    public static int CleanupObsoleteInstallationFiles(
        string installDir,
        IEnumerable<string>? priorManifestFiles,
        string? logDir)
    {
        int cleanedCount = 0;
        if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir))
            return 0;

        string fullRoot = EnsureTrailingSep(Path.GetFullPath(installDir));
        try
        {
            MigrateLegacyUserState(installDir, logDir);

            foreach (string oldFile in Directory.EnumerateFiles(installDir, "*.old_upgrade", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(oldFile, FileAttributes.Normal);
                    File.Delete(oldFile);
                    cleanedCount++;
                }
                catch (Exception ex) { Debug.WriteLine($"[Housekeeping.Installer] Old upgrade file delete ignored ({oldFile}): {ex.Message}"); }
            }

            string[] legacyRootExtensions = [".dll", ".pdb", ".deps.json", ".runtimeconfig.json", ".staticwebassets.endpoints.json", ".xml"];
            HashSet<string> legacyRootFileNames = new(StringComparer.OrdinalIgnoreCase)
            {
                "Housekeeping.Server.exe", "Housekeeping.DesktopHost.exe", "Housekeeping.lnk",
                UninstallerExeName, ".install_manifest.json", "install-manifest.json",
                "version.txt", "web.config", "CHANGELOG.md", "README.md",
                "MODULES_REFERENCE.md", "FEATURES.md", "ROADMAP.md"
            };

            string? selfExe = Environment.ProcessPath;
            foreach (string rootFile in Directory.EnumerateFiles(installDir, "*", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(rootFile);
                if (!string.IsNullOrWhiteSpace(selfExe) &&
                    string.Equals(Path.GetFullPath(selfExe), Path.GetFullPath(rootFile), StringComparison.OrdinalIgnoreCase))
                    continue;

                if (legacyRootFileNames.Contains(fileName) ||
                    legacyRootExtensions.Any(ext => fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        File.SetAttributes(rootFile, FileAttributes.Normal);
                        File.Delete(rootFile);
                        cleanedCount++;
                    }
                    catch (Exception ex) { Debug.WriteLine($"[Housekeeping.Installer] Legacy root file delete ignored ({fileName}): {ex.Message}"); }
                }
            }

            string[] legacyRootDirectories = ["Assets", "runtimes", "wwwroot", "WebView2Cache"];
            foreach (string dirName in legacyRootDirectories)
            {
                string dirPath = Path.Combine(installDir, dirName);
                if (Directory.Exists(dirPath))
                {
                    try { Directory.Delete(dirPath, recursive: true); cleanedCount++; }
                    catch (Exception ex) { Debug.WriteLine($"[Housekeeping.Installer] Legacy root directory delete ignored ({dirName}): {ex.Message}"); }
                }
            }

            if (priorManifestFiles != null)
            {
                foreach (string oldRel in priorManifestFiles)
                {
                    if (string.IsNullOrWhiteSpace(oldRel)) continue;
                    string normOld = oldRel.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                    if (IsProtectedUserRelPath(normOld)) continue;

                    if (!normOld.Contains(Path.DirectorySeparatorChar) &&
                        !normOld.Equals("Housekeeping.exe", StringComparison.OrdinalIgnoreCase) &&
                        !normOld.Equals("mother.manifest.json", StringComparison.OrdinalIgnoreCase) &&
                        !normOld.Equals("AGENTS.md", StringComparison.OrdinalIgnoreCase))
                    {
                        string oldCandidate = Path.GetFullPath(Path.Combine(installDir, normOld));
                        if (oldCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(oldCandidate))
                        {
                            try
                            {
                                File.SetAttributes(oldCandidate, FileAttributes.Normal);
                                File.Delete(oldCandidate);
                                cleanedCount++;
                            }
                            catch (Exception ex) { Debug.WriteLine($"[Housekeeping.Installer] Prior manifest file delete ignored ({normOld}): {ex.Message}"); }
                        }
                    }
                }
            }

            if (cleanedCount > 0)
                WriteLog(logDir, "INFO", $"Cleaned up {cleanedCount} obsolete/legacy installation item(s) from {installDir}.");
        }
        catch (Exception ex)
        {
            WriteLog(logDir, "WARN", $"CleanupObsoleteInstallationFiles warning: {ex.Message}");
        }

        return cleanedCount;
    }
}
