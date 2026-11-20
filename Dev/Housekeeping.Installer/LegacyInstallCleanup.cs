// @module: Housekeeping.Installer.LegacyInstallCleanup
// @purpose: Fail-closed directory containment, legacy state migration into data\, non-destructive legacy copy, and Clean Release Root cleanup.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, System.Linq, System.Runtime.CompilerServices, System.Text, System.Threading, System.Threading.Tasks
// @exports: LegacyInstallCleanup

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("Housekeeping.Tests")]

namespace Housekeeping.Installer;

internal static class LegacyInstallCleanup
{
    private const string UninstallerExeName = "Housekeeping-Uninstall.exe";

    internal static readonly string[] LegacyStateFiles =
    [
        "settings.json", "settings.json.bak", "settings.json.tmp", "history.json",
        "compliance_reports.json", "pending_swaps.json", "interrupted_run_checkpoint.json", ".hk_cookie_secret.key"
    ];

    internal static readonly string[] LegacyRuntimeFolders =
    [
        "dats", "tools", "manifests", "cache", "crashes"
    ];

    public static bool IsWithinDirectory(string parentDir, string childPath)
    {
        if (string.IsNullOrWhiteSpace(parentDir) || string.IsNullOrWhiteSpace(childPath))
            return false;

        try
        {
            string normParent = EnsureTrailingSep(
                Path.GetFullPath(parentDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string normChild = Path.GetFullPath(childPath);
            if (normChild.StartsWith(normParent, StringComparison.OrdinalIgnoreCase))
                return true;

            string trimParent = normParent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string trimChild = normChild.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(trimParent, trimChild, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Housekeeping.Installer] IsWithinDirectory check failed: {ex.Message}");
            return false;
        }
    }

    public static void MigrateLegacyUserState(string installDir, string? logDir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir))
                return;

            string dataDir = Path.Combine(installDir, "data");
            Directory.CreateDirectory(dataDir);
            Directory.CreateDirectory(Path.Combine(installDir, "logs"));
            Directory.CreateDirectory(Path.Combine(installDir, "backups"));

            string appDir = Path.Combine(installDir, "app");
            string dataSettings = Path.Combine(dataDir, "settings.json");
            long dataLen = AdoptSettingsFile(Path.Combine(installDir, "settings.json"), dataSettings, 0, logDir);
            if (Directory.Exists(appDir))
                AdoptSettingsFile(Path.Combine(appDir, "settings.json"), dataSettings, dataLen, logDir);

            foreach (string fileName in LegacyStateFiles)
            {
                if (fileName.Equals("settings.json", StringComparison.OrdinalIgnoreCase)) continue;
                MigrateSingleStateFileToData(Path.Combine(installDir, fileName), Path.Combine(dataDir, fileName), logDir);
                if (Directory.Exists(appDir))
                    MigrateSingleStateFileToData(Path.Combine(appDir, fileName), Path.Combine(dataDir, fileName), logDir);
            }

            foreach (string bakFile in Directory.EnumerateFiles(installDir, "settings.json.bak*", SearchOption.TopDirectoryOnly))
                MigrateSingleStateFileToData(bakFile, Path.Combine(dataDir, Path.GetFileName(bakFile)), logDir);

            foreach (string folderName in LegacyRuntimeFolders)
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
            WriteInstallerLog(logDir, "WARN", $"MigrateLegacyUserState warning: {ex.Message}");
        }
    }

    public static async Task<int> CopyLegacyStateNonDestructivelyAsync(
        string legacyRoot,
        string targetRoot,
        CancellationToken ct)
    {
        string targetData = Path.Combine(targetRoot, "data");
        Directory.CreateDirectory(targetData);
        Directory.CreateDirectory(Path.Combine(targetRoot, "logs"));
        Directory.CreateDirectory(Path.Combine(targetRoot, "backups"));

        int copied = 0;
        foreach (string sub in new[] { "data", "logs", "backups" })
        {
            string srcSub = Path.Combine(legacyRoot, sub);
            if (Directory.Exists(srcSub))
                copied += await CopyDirectoryMissingAsync(srcSub, Path.Combine(targetRoot, sub), ct).ConfigureAwait(false);
        }

        foreach (string folderName in LegacyRuntimeFolders)
        {
            string srcSub = Path.Combine(legacyRoot, folderName);
            if (Directory.Exists(srcSub))
                copied += await CopyDirectoryMissingAsync(srcSub, Path.Combine(targetData, folderName), ct).ConfigureAwait(false);
        }

        string legacyWebView = Path.Combine(legacyRoot, "WebView2Cache");
        if (Directory.Exists(legacyWebView))
            copied += await CopyDirectoryMissingAsync(legacyWebView, Path.Combine(targetData, "webview2_cache"), ct).ConfigureAwait(false);

        foreach (string fileName in LegacyStateFiles)
        {
            if (CopyIfMissingOrLarger(Path.Combine(legacyRoot, fileName), Path.Combine(targetData, fileName)))
                copied++;
        }

        foreach (string bak in Directory.EnumerateFiles(legacyRoot, "settings.json.bak*", SearchOption.TopDirectoryOnly))
        {
            if (CopyIfMissingOrLarger(bak, Path.Combine(targetData, Path.GetFileName(bak))))
                copied++;
        }

        return copied;
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
                if (TryDeleteFile(oldFile))
                    cleanedCount++;
            }

            string[] legacyRootExtensions = [".dll", ".pdb", ".deps.json", ".runtimeconfig.json", ".staticwebassets.endpoints.json", ".xml"];
            HashSet<string> legacyRootFileNames = new(StringComparer.OrdinalIgnoreCase)
            {
                "Housekeeping.Server.exe", "Housekeeping.DesktopHost.exe", "Housekeeping.lnk",
                UninstallerExeName, ".install_manifest.json", "install-manifest.json",
                "version.txt", "web.config", "CHANGELOG.md", "README.md",
                "MODULES_REFERENCE.md", "FEATURES.md", "ROADMAP.md", "AGENTS.md"
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
                    if (TryDeleteFile(rootFile))
                        cleanedCount++;
                }
            }

            foreach (string dirName in new[] { "Assets", "runtimes", "wwwroot", "WebView2Cache" })
            {
                string dirPath = Path.Combine(installDir, dirName);
                if (Directory.Exists(dirPath))
                {
                    try { Directory.Delete(dirPath, recursive: true); cleanedCount++; }
                    catch (Exception ex) { Debug.WriteLine($"[Housekeeping.Installer] Legacy root dir delete ignored ({dirName}): {ex.Message}"); }
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
                        if (oldCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) &&
                            File.Exists(oldCandidate) &&
                            TryDeleteFile(oldCandidate))
                        {
                            cleanedCount++;
                        }
                    }
                }
            }

            if (cleanedCount > 0)
                WriteInstallerLog(logDir, "INFO", $"Cleaned up {cleanedCount} obsolete/legacy installation item(s) from {installDir}.");
        }
        catch (Exception ex)
        {
            WriteInstallerLog(logDir, "WARN", $"CleanupObsoleteInstallationFiles warning: {ex.Message}");
        }

        return cleanedCount;
    }

    private static async Task<int> CopyDirectoryMissingAsync(string sourceDir, string targetDir, CancellationToken ct)
    {
        Directory.CreateDirectory(targetDir);
        int count = 0;
        foreach (string file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            string dest = Path.Combine(targetDir, Path.GetRelativePath(sourceDir, file));
            string? parent = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            if (!File.Exists(dest))
            {
                await using var inFs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, useAsync: true);
                await using var outFs = new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await inFs.CopyToAsync(outFs, ct).ConfigureAwait(false);
                count++;
            }
        }
        return count;
    }

    private static bool CopyIfMissingOrLarger(string srcFile, string dstFile)
    {
        if (!File.Exists(srcFile)) return false;
        long srcLen = new FileInfo(srcFile).Length;
        long dstLen = File.Exists(dstFile) ? new FileInfo(dstFile).Length : 0;
        if (File.Exists(dstFile) && srcLen <= dstLen) return false;
        File.Copy(srcFile, dstFile, overwrite: true);
        return true;
    }

    private static long AdoptSettingsFile(string srcPath, string dataSettingsPath, long knownDataLen, string? logDir)
    {
        if (!File.Exists(srcPath)) return knownDataLen;
        long srcLen = new FileInfo(srcPath).Length;
        long dataLen = knownDataLen > 0 ? knownDataLen : (File.Exists(dataSettingsPath) ? new FileInfo(dataSettingsPath).Length : 0);
        if (srcLen > 0 && (dataLen == 0 || (srcLen > 30_000 && dataLen < srcLen) || srcLen > dataLen))
        {
            File.Copy(srcPath, dataSettingsPath, overwrite: true);
            dataLen = srcLen;
            WriteInstallerLog(logDir, "INFO", $"Migrated '{Path.GetFileName(srcPath)}' ({srcLen} bytes) to data\\settings.json.");
        }
        TryDeleteFile(srcPath);
        return dataLen;
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
                WriteInstallerLog(logDir, "INFO", $"Migrated '{Path.GetFileName(srcFile)}' ({srcLen} bytes) to data\\.");
            }
            TryDeleteFile(srcFile);
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
                    WriteInstallerLog(logDir, "INFO", $"Moved runtime directory '{Path.GetFileName(srcDir)}' into data\\{Path.GetFileName(dstDir)}.");
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

    private static bool IsProtectedUserRelPath(string normalizedRel)
    {
        string clean = normalizedRel.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        string topSegment = clean.Split(Path.DirectorySeparatorChar)[0];
        if (topSegment is "backups" or "dats" or "logs" or "manifests" or "tools" or "data" or "cache" or "crashes")
            return true;

        string fileName = Path.GetFileName(clean);
        if (fileName.StartsWith("settings.json", StringComparison.OrdinalIgnoreCase) ||
            fileName is "history.json" or "compliance_reports.json" or "pending_swaps.json" or "interrupted_run_checkpoint.json")
            return true;

        string ext = Path.GetExtension(clean);
        return ext.Equals(".bak", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".db", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".sqlite", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".sqlite3", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".new", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".hklock", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryDeleteFile(string path)
    {
        try
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Housekeeping.Installer] File delete ignored ({path}): {ex.Message}");
            return false;
        }
    }

    private static string EnsureTrailingSep(string p) =>
        p.EndsWith(Path.DirectorySeparatorChar) ? p : p + Path.DirectorySeparatorChar;

    private static void WriteInstallerLog(string? logDir, string level, string message)
    {
        if (string.IsNullOrWhiteSpace(logDir)) return;
        try
        {
            Directory.CreateDirectory(logDir);
            string line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(logDir, "installer.log"), line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) { Debug.WriteLine($"[Housekeeping.Installer] WriteInstallerLog ignored: {ex.Message}"); }
    }
}
