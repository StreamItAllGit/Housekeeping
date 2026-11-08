using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Housekeeping.Installer;

public sealed record EmbeddedMetadata(
    string AppName,
    string Version,
    string PayloadSha256,
    int FileCount);

public static class Program
{
    private const string PayloadResourceName = "Housekeeping.Installer.Embedded.payload.zip";
    private const string MetadataResourceName = "Housekeeping.Installer.Embedded.metadata.json";
    private const string UninstallRegistrySubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Housekeeping";
    private const string UninstallerExeName = "Housekeeping-Uninstall.exe";
    private const string ShortcutFileName = "Housekeeping.lnk";
    private const long MaxSingleEntryBytes = 256L * 1024L * 1024L;
    private const long MaxTotalArchiveBytes = 2L * 1024L * 1024L * 1024L;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);
    private const int AttachParentProcess = -1;

    // Canonical user runtime files & directories that must NEVER be overwritten on upgrade
    // and must NEVER be deleted on uninstall unless --purge-data is explicitly requested.
    private static readonly HashSet<string> ProtectedUserFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "settings.json",
        "settings.json.bak",
        "settings.json.tmp",
        "history.json",
        "compliance_reports.json",
        "pending_swaps.json",
        "interrupted_run_checkpoint.json"
    };

    private static readonly string[] ProtectedUserDirectories =
    [
        "backups",
        "dats",
        "logs",
        "manifests",
        "tools",
        "data",
        "cache",
        "crashes"
    ];

    private static bool IsProtectedUserRelPath(string normalizedRel)
    {
        if (string.IsNullOrWhiteSpace(normalizedRel))
            return false;

        string clean = normalizedRel.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        if (ProtectedUserFiles.Contains(clean))
            return true;

        string topSegment = clean.Split(Path.DirectorySeparatorChar)[0];
        if (ProtectedUserDirectories.Contains(topSegment, StringComparer.OrdinalIgnoreCase))
            return true;

        string fileName = Path.GetFileName(clean);
        if (fileName.StartsWith("settings.json.bak", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string ext = Path.GetExtension(clean);
        if (ext.Equals(".bak", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".db", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".sqlite", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".sqlite3", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".new", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".hklock", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public static int Main(string[] args)
    {
        bool hasCliArgs = args != null && args.Length > 0;
        if (hasCliArgs ||
            IsTruthy(Env("HOUSEKEEPING_TEST_MODE")) ||
            IsTruthy(Env("HOUSEKEEPING_HEADLESS")))
        {
            try
            {
                AttachConsole(AttachParentProcess);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AttachConsole notice: {ex.Message}");
            }
        }

        CliOptions opts;
        try
        {
            opts = ParseArgs(args ?? Array.Empty<string>());
        }
        catch (Exception argEx)
        {
            Console.Error.WriteLine($"ERROR: {argEx.Message}");
            return 2;
        }

        var meta = LoadMetadata();

        if (opts.Help)
        {
            PrintBanner(meta);
            PrintUsage();
            return 0;
        }

        if (opts.ShowVersion)
        {
            Console.WriteLine(meta.Version);
            return 0;
        }

        string? dataDir = FirstNonEmpty(opts.DataDir, Env("HOUSEKEEPING_DATA_DIR"), Env("DATA_DIR"), Env("APP_HOME"));
        string? logDir = FirstNonEmpty(opts.LogDir, Env("HOUSEKEEPING_LOG_DIR"), Env("LOG_DIR"));
        string? cfgPath = FirstNonEmpty(opts.ConfigPath, Env("HOUSEKEEPING_CONFIG_FILE"), Env("CONFIG_PATH"), Env("CONFIG_FILE"));
        bool testMode = opts.TestMode || IsTruthy(Env("HOUSEKEEPING_TEST_MODE"));
        bool headless = opts.Headless || IsTruthy(Env("HOUSEKEEPING_HEADLESS"));
        bool dryRun = opts.DryRun || IsTruthy(Env("HOUSEKEEPING_DRY_RUN"));
        bool silent = opts.Silent || headless || testMode;

        if (!string.IsNullOrWhiteSpace(cfgPath) && File.Exists(cfgPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(cfgPath, Encoding.UTF8));
                if (string.IsNullOrWhiteSpace(opts.InstallDir) &&
                    doc.RootElement.TryGetProperty("install_dir", out var instProp) &&
                    instProp.ValueKind == JsonValueKind.String)
                {
                    opts.InstallDir = instProp.GetString();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Invalid --config JSON ({cfgPath}): {ex.Message}");
                return 1;
            }
        }

        if (string.IsNullOrWhiteSpace(logDir) && !string.IsNullOrWhiteSpace(dataDir))
        {
            logDir = Path.Combine(dataDir, "logs");
        }

        try
        {
            if (testMode && string.IsNullOrWhiteSpace(dataDir))
            {
                throw new InvalidOperationException("Fail-closed sandbox violation: --test-mode requires --data-dir (or DATA_DIR / APP_HOME).");
            }

            // Launch the Dark WinForms GUI Setup Wizard when invoked interactively without CLI flags
            if (!hasCliArgs && !silent && !testMode && !headless && !dryRun && !opts.VerifyOnly)
            {
                string defaultGuiDir = ResolveInstallDirectory(opts.InstallDir, dataDir, testMode: false, silent: true, dryRun: false);
                ApplicationConfiguration.Initialize();
                using var wizard = new InstallerWizardForm(
                    meta: meta,
                    initialInstallDir: defaultGuiDir,
                    dataDir: dataDir,
                    logDir: logDir,
                    testMode: false,
                    isUninstallMode: opts.Uninstall,
                    initialCreateShortcuts: !opts.NoShortcuts);
                Application.Run(wizard);
                return wizard.ExitCode;
            }

            if (!silent)
            {
                PrintBanner(meta);
            }

            if (opts.VerifyOnly)
            {
                string actualHash = VerifyEmbeddedPayload(meta);
                WriteLog(logDir, "INFO", $"Verified embedded payload SHA-256 ({actualHash}) for {meta.AppName} v{meta.Version} ({meta.FileCount} files).");
                Console.WriteLine($"OK: Verified embedded {meta.AppName} v{meta.Version} payload ({meta.FileCount} files, SHA-256: {actualHash}).");
                return 0;
            }

            string installDir = ResolveInstallDirectory(opts.InstallDir, dataDir, testMode, silent, dryRun);
            ValidateContainment(installDir, dataDir, logDir, testMode);

            if (opts.Uninstall)
            {
                return RunUninstall(installDir, dataDir, logDir, testMode, dryRun, opts.PurgeData, meta);
            }

            return RunInstall(
                installDir,
                dataDir,
                logDir,
                testMode,
                dryRun,
                !opts.NoShortcuts,
                meta,
                autoRelaunchIfRunning: !testMode && !opts.NoLaunch,
                forceLaunch: opts.LaunchAfter);
        }
        catch (Exception ex)
        {
            WriteLog(logDir, "ERROR", $"Installer aborted: {ex.Message}");
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }

    internal static bool IsExistingInstallation(string installDir, out string? priorVersion)
    {
        priorVersion = null;
        if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir))
            return false;

        string appReceiptPath = Path.Combine(installDir, "app", ".install_manifest.json");
        string rootReceiptPath = Path.Combine(installDir, ".install_manifest.json");
        string appVersionTxtPath = Path.Combine(installDir, "app", "version.txt");
        string rootVersionTxtPath = Path.Combine(installDir, "version.txt");

        bool isUpgrade = File.Exists(appReceiptPath) ||
                         File.Exists(rootReceiptPath) ||
                         File.Exists(appVersionTxtPath) ||
                         File.Exists(rootVersionTxtPath) ||
                         File.Exists(Path.Combine(installDir, "Housekeeping.exe")) ||
                         File.Exists(Path.Combine(installDir, "data", "settings.json")) ||
                         File.Exists(Path.Combine(installDir, "settings.json"));

        string? verFile = File.Exists(appVersionTxtPath)
            ? appVersionTxtPath
            : (File.Exists(rootVersionTxtPath) ? rootVersionTxtPath : null);
        if (verFile != null)
        {
            try { priorVersion = File.ReadAllText(verFile, Encoding.UTF8).Trim(); }
            catch (Exception ex) { Debug.WriteLine($"[Housekeeping.Installer] Read prior version ignored: {ex.Message}"); }
        }
        return isUpgrade;
    }

    internal static void TryLaunchInstalledHousekeeping(string installDir, string? logDir)
    {
        try
        {
            string exe = Path.Combine(installDir, "Housekeeping.exe");
            if (File.Exists(exe))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = installDir,
                    UseShellExecute = true
                };
                Process.Start(psi);
                WriteLog(logDir, "INFO", $"Launched installed application: {exe}");
            }
        }
        catch (Exception ex)
        {
            WriteLog(logDir, "WARN", $"Could not auto-launch {installDir}: {ex.Message}");
        }
    }

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
            {
                return true;
            }

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

    public static bool StopRunningProcessesInDirectory(string installDir, string? logDir, int timeoutMs = 8000)
    {
        if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir))
            return false;

        bool wasRunning = false;
        int selfPid = Environment.ProcessId;
        int port = ResolveConfiguredServerPort(installDir);
        bool sentHttpShutdown = false;

        string[] processNames =
        [
            "Housekeeping",
            "Housekeeping.Server",
            "Housekeeping.DesktopHost"
        ];

        var matchedProcesses = new List<Process>();
        foreach (string procName in processNames)
        {
            Process[] candidates;
            try
            {
                candidates = Process.GetProcessesByName(procName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Housekeeping.Installer] GetProcessesByName({procName}) ignored: {ex.Message}");
                continue;
            }

            foreach (var proc in candidates)
            {
                try
                {
                    if (proc.Id == selfPid || proc.HasExited)
                    {
                        proc.Dispose();
                        continue;
                    }

                    string? exePath = proc.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(exePath) && IsWithinDirectory(installDir, exePath))
                    {
                        matchedProcesses.Add(proc);
                    }
                    else
                    {
                        proc.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    // Fail-closed: never terminate a process if its path cannot be verified inside installDir
                    Debug.WriteLine($"[Housekeeping.Installer] Process inspection skipped (PID {proc.Id}): {ex.Message}");
                    proc.Dispose();
                }
            }
        }

        if (matchedProcesses.Count == 0)
            return false;

        wasRunning = true;
        WriteLog(logDir, "INFO", $"Detected {matchedProcesses.Count} active Housekeeping process(es) inside {installDir}. Initiating graceful shutdown...");

        try
        {
            // Stage 1: Graceful UI close
            foreach (var proc in matchedProcesses)
            {
                try
                {
                    if (!proc.HasExited && proc.MainWindowHandle != IntPtr.Zero)
                    {
                        proc.CloseMainWindow();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Housekeeping.Installer] CloseMainWindow ignored: {ex.Message}");
                }
            }

            // Stage 2: Graceful HTTP shutdown via /api/v1/system/shutdown
            if (matchedProcesses.Any(p => !SafeHasExited(p)) && !sentHttpShutdown)
            {
                sentHttpShutdown = true;
                TrySendHttpShutdownSignal(port, logDir);
            }

            int waitSliceMs = Math.Min(1500, Math.Max(300, timeoutMs / 3));
            foreach (var proc in matchedProcesses)
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        proc.WaitForExit(waitSliceMs);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Housekeeping.Installer] WaitForExit ignored: {ex.Message}");
                }
            }

            // Stage 3: Force-kill fallback for any remaining process
            foreach (var proc in matchedProcesses)
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        WriteLog(logDir, "INFO", $"Force-terminating remaining Housekeeping process PID {proc.Id} ({proc.ProcessName}).");
                        proc.Kill(entireProcessTree: true);
                        proc.WaitForExit(Math.Max(1000, timeoutMs / 2));
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Housekeeping.Installer] Process.Kill fallback ignored: {ex.Message}");
                }
            }

            Thread.Sleep(300);
        }
        finally
        {
            foreach (var proc in matchedProcesses)
            {
                try { proc.Dispose(); } catch { }
            }
        }

        return wasRunning;
    }

    private static bool SafeHasExited(Process proc)
    {
        try { return proc.HasExited; }
        catch { return true; }
    }

    private static int ResolveConfiguredServerPort(string installDir)
    {
        string[] candidateConfigs =
        [
            Path.Combine(installDir, "settings.json"),
            Path.Combine(installDir, "app", "settings.json"),
            Path.Combine(installDir, "data", "settings.json")
        ];

        foreach (string cfgPath in candidateConfigs)
        {
            try
            {
                if (!File.Exists(cfgPath))
                    continue;

                using var doc = JsonDocument.Parse(File.ReadAllText(cfgPath, Encoding.UTF8));
                if (doc.RootElement.TryGetProperty("WebServer", out var ws) &&
                    ws.ValueKind == JsonValueKind.Object &&
                    ws.TryGetProperty("Port", out var portProp) &&
                    portProp.TryGetInt32(out int port) &&
                    port > 0 && port <= 65535)
                {
                    return port;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Housekeeping.Installer] ResolveConfiguredServerPort ignored ({cfgPath}): {ex.Message}");
            }
        }

        return 5178;
    }

    private static void TrySendHttpShutdownSignal(int port, string? logDir)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1500) };
            using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/api/v1/system/shutdown");
            using var resp = client.Send(req);
            WriteLog(logDir, "INFO", $"Sent HTTP shutdown signal to 127.0.0.1:{port} (HTTP {(int)resp.StatusCode}).");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Housekeeping.Installer] HTTP shutdown probe ignored: {ex.Message}");
        }
    }

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
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Housekeeping.Installer] Delete root settings.json ignored: {ex.Message}");
                }
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
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Housekeeping.Installer] Delete app settings.json ignored: {ex.Message}");
                }
            }

            string[] stateFiles =
            [
                "settings.json.bak",
                "settings.json.tmp",
                "history.json",
                "compliance_reports.json",
                "pending_swaps.json",
                "interrupted_run_checkpoint.json",
                ".hk_cookie_secret.key"
            ];

            foreach (string fileName in stateFiles)
            {
                MigrateSingleStateFileToData(Path.Combine(installDir, fileName), Path.Combine(dataDir, fileName), logDir);
                if (Directory.Exists(appDir))
                {
                    MigrateSingleStateFileToData(Path.Combine(appDir, fileName), Path.Combine(dataDir, fileName), logDir);
                }
            }

            foreach (string bakFile in Directory.EnumerateFiles(installDir, "settings.json.bak*", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(bakFile);
                MigrateSingleStateFileToData(bakFile, Path.Combine(dataDir, fileName), logDir);
            }

            string[] runtimeFoldersToMoveToData =
            [
                "dats",
                "tools",
                "manifests",
                "cache",
                "crashes"
            ];

            foreach (string folderName in runtimeFoldersToMoveToData)
            {
                string srcFolder = Path.Combine(installDir, folderName);
                string dstFolder = Path.Combine(dataDir, folderName);
                if (Directory.Exists(srcFolder))
                {
                    MoveOrMergeDirectoryToData(srcFolder, dstFolder, logDir);
                }
            }

            string rootWebView2 = Path.Combine(installDir, "WebView2Cache");
            string dstWebView2 = Path.Combine(dataDir, "webview2_cache");
            if (Directory.Exists(rootWebView2))
            {
                MoveOrMergeDirectoryToData(rootWebView2, dstWebView2, logDir);
            }
        }
        catch (Exception ex)
        {
            WriteLog(logDir, "WARN", $"MigrateLegacyUserState warning: {ex.Message}");
        }
    }

    private static void MigrateSingleStateFileToData(string srcFile, string dstFile, string? logDir)
    {
        if (!File.Exists(srcFile))
            return;

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
        catch (Exception ex)
        {
            Debug.WriteLine($"[Housekeeping.Installer] MigrateSingleStateFileToData ({srcFile}) ignored: {ex.Message}");
        }
    }

    private static void MoveOrMergeDirectoryToData(string srcDir, string dstDir, string? logDir)
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
                    WriteLog(logDir, "INFO", $"Moved runtime directory '{Path.GetFileName(srcDir)}' into data\\{Path.GetFileName(dstDir)}.");
                    return;
                }
                catch (Exception moveEx)
                {
                    Debug.WriteLine($"[Housekeeping.Installer] Directory.Move fallback to recursive merge ({srcDir}): {moveEx.Message}");
                }
            }

            Directory.CreateDirectory(dstDir);
            foreach (string file in Directory.GetFiles(srcDir, "*", SearchOption.TopDirectoryOnly))
            {
                string destFile = Path.Combine(dstDir, Path.GetFileName(file));
                MigrateSingleStateFileToData(file, destFile, logDir);
            }

            foreach (string sub in Directory.GetDirectories(srcDir, "*", SearchOption.TopDirectoryOnly))
            {
                string destSub = Path.Combine(dstDir, Path.GetFileName(sub));
                MoveOrMergeDirectoryToData(sub, destSub, logDir);
            }

            Directory.Delete(srcDir, recursive: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Housekeeping.Installer] MoveOrMergeDirectoryToData ({srcDir} -> {dstDir}) ignored: {ex.Message}");
        }
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
            // Ensure legacy root user state and runtime folders are migrated into data\ first
            MigrateLegacyUserState(installDir, logDir);

            // Sweep 1: Remove all *.old_upgrade files recursively
            foreach (string oldFile in Directory.EnumerateFiles(installDir, "*.old_upgrade", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(oldFile, FileAttributes.Normal);
                    File.Delete(oldFile);
                    cleanedCount++;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Housekeeping.Installer] Old upgrade file delete ignored ({oldFile}): {ex.Message}");
                }
            }

            // Sweep 2: Enforce Clean Release Root on top-level files (only Housekeeping.exe and mother.manifest.json remain in root)
            string[] legacyRootExtensions =
            [
                ".dll",
                ".pdb",
                ".deps.json",
                ".runtimeconfig.json",
                ".staticwebassets.endpoints.json",
                ".xml"
            ];

            HashSet<string> legacyRootFileNames = new(StringComparer.OrdinalIgnoreCase)
            {
                "Housekeeping.Server.exe",
                "Housekeeping.DesktopHost.exe",
                "Housekeeping.lnk",
                UninstallerExeName,
                ".install_manifest.json",
                "install-manifest.json",
                "version.txt",
                "web.config",
                "CHANGELOG.md",
                "README.md",
                "MODULES_REFERENCE.md",
                "FEATURES.md",
                "ROADMAP.md"
            };

            string? selfExe = Environment.ProcessPath;

            foreach (string rootFile in Directory.EnumerateFiles(installDir, "*", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(rootFile);
                if (!string.IsNullOrWhiteSpace(selfExe) &&
                    string.Equals(Path.GetFullPath(selfExe), Path.GetFullPath(rootFile), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool shouldDelete = legacyRootFileNames.Contains(fileName) ||
                                    legacyRootExtensions.Any(ext => fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

                if (shouldDelete)
                {
                    try
                    {
                        File.SetAttributes(rootFile, FileAttributes.Normal);
                        File.Delete(rootFile);
                        cleanedCount++;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Housekeeping.Installer] Legacy root file delete ignored ({fileName}): {ex.Message}");
                    }
                }
            }

            // Sweep 3: Remove legacy root asset and cache directories that now live inside app\ or data\
            string[] legacyRootDirectories =
            [
                "Assets",
                "runtimes",
                "wwwroot",
                "WebView2Cache"
            ];

            foreach (string dirName in legacyRootDirectories)
            {
                string dirPath = Path.Combine(installDir, dirName);
                if (Directory.Exists(dirPath))
                {
                    try
                    {
                        Directory.Delete(dirPath, recursive: true);
                        cleanedCount++;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Housekeeping.Installer] Legacy root directory delete ignored ({dirName}): {ex.Message}");
                    }
                }
            }

            // Sweep 4: Remove obsolete prior manifest entries that were rooted outside app\
            if (priorManifestFiles != null)
            {
                foreach (string oldRel in priorManifestFiles)
                {
                    if (string.IsNullOrWhiteSpace(oldRel))
                        continue;

                    string normOld = oldRel.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                    if (IsProtectedUserRelPath(normOld))
                        continue;

                    if (!normOld.Contains(Path.DirectorySeparatorChar) &&
                        !normOld.Equals("Housekeeping.exe", StringComparison.OrdinalIgnoreCase) &&
                        !normOld.Equals("mother.manifest.json", StringComparison.OrdinalIgnoreCase))
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
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[Housekeeping.Installer] Prior manifest file delete ignored ({normOld}): {ex.Message}");
                            }
                        }
                    }
                }
            }

            if (cleanedCount > 0)
            {
                WriteLog(logDir, "INFO", $"Cleaned up {cleanedCount} obsolete/legacy installation item(s) from {installDir}.");
            }
        }
        catch (Exception ex)
        {
            WriteLog(logDir, "WARN", $"CleanupObsoleteInstallationFiles warning: {ex.Message}");
        }

        return cleanedCount;
    }

    internal static int RunInstall(
        string installDir,
        string? dataDir,
        string? logDir,
        bool testMode,
        bool dryRun,
        bool createShortcuts,
        EmbeddedMetadata meta,
        bool autoRelaunchIfRunning = true,
        bool forceLaunch = false)
    {
        string verifiedSha = VerifyEmbeddedPayload(meta);

        if (dryRun)
        {
            WriteLog(logDir, "INFO", $"[Dry-Run] Would install {meta.AppName} v{meta.Version} ({meta.FileCount} files, SHA-256 {verifiedSha}) to {installDir}.");
            Console.WriteLine($"[Dry-Run] {meta.AppName} v{meta.Version} payload verified ({meta.FileCount} files). Planned install target: {installDir}");
            return 0;
        }

        Directory.CreateDirectory(installDir);
        string fullRoot = EnsureTrailingSep(Path.GetFullPath(installDir));

        string appMetaDir = Path.Combine(installDir, "app");
        string appReceiptPath = Path.Combine(appMetaDir, ".install_manifest.json");
        string rootReceiptPath = Path.Combine(installDir, ".install_manifest.json");
        string appVersionTxtPath = Path.Combine(appMetaDir, "version.txt");
        string rootVersionTxtPath = Path.Combine(installDir, "version.txt");

        bool isUpgrade = IsExistingInstallation(installDir, out string? priorVersion);

        // Stop any running Housekeeping instances inside installDir before modifying files
        bool wasRunning = StopRunningProcessesInDirectory(installDir, logDir);

        // Read prior manifest from either app\.install_manifest.json or legacy root .install_manifest.json
        var priorManifestFiles = ReadPriorManifestFiles(File.Exists(appReceiptPath) ? appReceiptPath : rootReceiptPath);

        // Create atomic pre-upgrade backup of user state files, migrate canonical settings into data\, and clean up legacy root clutter
        if (isUpgrade)
        {
            CreatePreUpgradeBackup(installDir, priorVersion, logDir);
            MigrateLegacyUserState(installDir, logDir);
            CleanupObsoleteInstallationFiles(installDir, priorManifestFiles, logDir);
        }
        else
        {
            Directory.CreateDirectory(Path.Combine(installDir, "data"));
            Directory.CreateDirectory(Path.Combine(installDir, "logs"));
            Directory.CreateDirectory(Path.Combine(installDir, "backups"));
            CleanupStaleOldUpgradeFiles(installDir);
        }

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName)
            ?? throw new InvalidOperationException("Embedded payload archive stream is missing from installer binary.");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        long totalExtracted = 0;
        int extractedFiles = 0;
        var installedRelPaths = new List<string>();
        var installedRelSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith('/'))
            {
                continue;
            }

            string normalizedRel = entry.FullName.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            string destPath = Path.GetFullPath(Path.Combine(installDir, normalizedRel));
            if (!destPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Archive containment violation blocked (Zip-Slip): {entry.FullName}");
            }

            // Never clobber canonical user state files or directories on upgrade
            if (IsProtectedUserRelPath(normalizedRel) && File.Exists(destPath))
            {
                continue;
            }

            if (entry.Length > MaxSingleEntryBytes)
            {
                throw new InvalidOperationException($"Archive entry exceeds safety cap ({entry.FullName}: {entry.Length} bytes).");
            }

            string? parent = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            using var entryStream = entry.Open();
            using var outStream = OpenUpgradeSafeOutputStream(destPath, logDir);
            byte[] buffer = new byte[65536];
            int read;
            long fileWritten = 0;
            while ((read = entryStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                fileWritten += read;
                totalExtracted += read;
                if (fileWritten > MaxSingleEntryBytes || totalExtracted > MaxTotalArchiveBytes)
                {
                    throw new InvalidOperationException("Archive extraction exceeded bounded byte limit.");
                }
                outStream.Write(buffer, 0, read);
            }
            outStream.Flush(flushToDisk: true);
            extractedFiles++;
            string relSlash = normalizedRel.Replace(Path.DirectorySeparatorChar, '/');
            installedRelPaths.Add(relSlash);
            installedRelSet.Add(relSlash);
        }

        // Remove obsolete application assets from previous versions that are no longer in the payload
        if (priorManifestFiles.Count > 0)
        {
            foreach (string oldRel in priorManifestFiles)
            {
                if (installedRelSet.Contains(oldRel))
                    continue;

                string normOld = oldRel.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                if (IsProtectedUserRelPath(normOld))
                    continue;

                string oldCandidate = Path.GetFullPath(Path.Combine(installDir, normOld));
                if (oldCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(oldCandidate))
                {
                    try { File.Delete(oldCandidate); } catch (Exception ex) { Debug.WriteLine($"[Housekeeping.Installer] Obsolete asset delete ignored: {ex.Message}"); }
                }
            }
        }

        string metaContainerDir = Directory.Exists(appMetaDir) ? appMetaDir : installDir;
        Directory.CreateDirectory(metaContainerDir);

        // Copy self as Housekeeping-Uninstall.exe inside app\ (metaContainerDir)
        string? selfExe = Environment.ProcessPath;
        string uninstallerPath = Path.Combine(metaContainerDir, UninstallerExeName);
        if (!string.IsNullOrWhiteSpace(selfExe) && File.Exists(selfExe))
        {
            if (!string.Equals(Path.GetFullPath(selfExe), Path.GetFullPath(uninstallerPath), StringComparison.OrdinalIgnoreCase))
            {
                CopyFileUpgradeSafe(selfExe, uninstallerPath, logDir);
            }
        }

        // Write version.txt and installation receipt inside app\ (metaContainerDir)
        File.WriteAllText(Path.Combine(metaContainerDir, "version.txt"), meta.Version + Environment.NewLine, Encoding.UTF8);

        string activeReceiptPath = Path.Combine(metaContainerDir, ".install_manifest.json");
        var receipt = new Dictionary<string, object?>
        {
            ["app"] = meta.AppName,
            ["version"] = meta.Version,
            ["upgraded_from_version"] = priorVersion,
            ["payload_sha256"] = verifiedSha,
            ["installed_files"] = extractedFiles,
            ["installed_at_utc"] = DateTime.UtcNow.ToString("O"),
            ["test_mode"] = testMode,
            ["files"] = installedRelPaths
        };
        File.WriteAllText(activeReceiptPath, JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);

        // Remove any stale root copies of Housekeeping-Uninstall.exe, .install_manifest.json, and version.txt when stored inside app\
        if (!string.Equals(metaContainerDir, installDir, StringComparison.OrdinalIgnoreCase))
        {
            string[] rootMetaFiles = [UninstallerExeName, ".install_manifest.json", "install-manifest.json", "version.txt"];
            foreach (string rmName in rootMetaFiles)
            {
                string rmPath = Path.Combine(installDir, rmName);
                if (File.Exists(rmPath) &&
                    (string.IsNullOrWhiteSpace(selfExe) ||
                     !string.Equals(Path.GetFullPath(selfExe), Path.GetFullPath(rmPath), StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        File.SetAttributes(rmPath, FileAttributes.Normal);
                        File.Delete(rmPath);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Housekeeping.Installer] Root metadata cleanup ignored ({rmName}): {ex.Message}");
                    }
                }
            }
        }

        if (createShortcuts)
        {
            CreateShortcuts(installDir, dataDir, testMode);
        }

        RegisterUninstaller(installDir, uninstallerPath, dataDir, testMode, meta);

        // Final sweep to ensure all user state lives inside data\ and outer root is 100% clean
        MigrateLegacyUserState(installDir, logDir);
        CleanupObsoleteInstallationFiles(installDir, null, logDir);

        string actionVerb = isUpgrade ? "Upgraded" : "Installed";
        WriteLog(logDir, "INFO", $"{actionVerb} {meta.AppName} v{meta.Version} ({extractedFiles} files) to {installDir} (prior={priorVersion ?? "none"}, test_mode={testMode}).");
        Console.WriteLine($"{actionVerb} {meta.AppName} v{meta.Version} ({extractedFiles} files) -> {installDir}");

        if ((forceLaunch || (wasRunning && autoRelaunchIfRunning)) && !testMode && !dryRun)
        {
            TryLaunchInstalledHousekeeping(installDir, logDir);
        }

        return 0;
    }

    internal static int RunUninstall(
        string installDir,
        string? dataDir,
        string? logDir,
        bool testMode,
        bool dryRun,
        bool purgeData,
        EmbeddedMetadata meta)
    {
        if (dryRun)
        {
            WriteLog(logDir, "INFO", $"[Dry-Run] Would uninstall {meta.AppName} from {installDir} (purge_data={purgeData}).");
            Console.WriteLine($"[Dry-Run] Would uninstall {meta.AppName} from {installDir} (purge_data={purgeData}).");
            return 0;
        }

        StopRunningProcessesInDirectory(installDir, logDir);
        RemoveShortcuts(installDir, dataDir, testMode);
        RemoveUninstallerRegistration(dataDir, testMode);

        if (Directory.Exists(installDir))
        {
            if (!purgeData)
            {
                MigrateLegacyUserState(installDir, logDir);
            }

            string fullRoot = EnsureTrailingSep(Path.GetFullPath(installDir));
            string[] receiptPaths =
            [
                Path.Combine(installDir, "app", ".install_manifest.json"),
                Path.Combine(installDir, ".install_manifest.json")
            ];

            foreach (string receiptPath in receiptPaths)
            {
                if (!File.Exists(receiptPath))
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(receiptPath, Encoding.UTF8));
                    if (doc.RootElement.TryGetProperty("files", out var filesArr) && filesArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in filesArr.EnumerateArray())
                        {
                            string? rel = item.GetString();
                            if (string.IsNullOrWhiteSpace(rel)) continue;
                            string norm = rel.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                            if (ProtectedUserFiles.Contains(norm)) continue;

                            string candidate = Path.GetFullPath(Path.Combine(installDir, norm));
                            if (candidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
                            {
                                File.Delete(candidate);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Housekeeping.Installer] Malformed prior manifest ignored: {ex.Message}");
                }

                try { File.Delete(receiptPath); } catch { }
            }

            string? selfExe = Environment.ProcessPath;
            string appUninstExe = Path.Combine(installDir, "app", UninstallerExeName);
            if (File.Exists(appUninstExe) &&
                (string.IsNullOrWhiteSpace(selfExe) ||
                 !string.Equals(Path.GetFullPath(selfExe), Path.GetFullPath(appUninstExe), StringComparison.OrdinalIgnoreCase)))
            {
                try { File.Delete(appUninstExe); } catch { }
            }

            string[] appFolders = ["app", "Assets", "runtimes", "wwwroot", "scripts", "WebView2Cache"];
            foreach (string sub in appFolders)
            {
                string p = Path.Combine(installDir, sub);
                if (Directory.Exists(p))
                {
                    try { Directory.Delete(p, recursive: true); } catch { }
                }
            }

            string[] coreFiles =
            [
                "Housekeeping.exe",
                "mother.manifest.json",
                "Housekeeping.dll",
                "Housekeeping.deps.json",
                "Housekeeping.runtimeconfig.json",
                "Housekeeping.Core.dll",
                "Housekeeping.Server.exe",
                "Housekeeping.Server.dll",
                "Housekeeping.Server.deps.json",
                "Housekeeping.Server.runtimeconfig.json",
                "MediaInfo.dll",
                "WebView2Loader.dll",
                "Microsoft.Web.WebView2.Core.dll",
                "Microsoft.Web.WebView2.Core.xml",
                "Microsoft.Web.WebView2.WinForms.dll",
                "Microsoft.Web.WebView2.WinForms.xml",
                "version.txt",
                ShortcutFileName
            ];
            foreach (string fn in coreFiles)
            {
                string fp = Path.Combine(installDir, fn);
                if (File.Exists(fp))
                {
                    File.Delete(fp);
                }
            }

            if (purgeData)
            {
                foreach (string uf in ProtectedUserFiles)
                {
                    string ufp = Path.Combine(installDir, uf);
                    if (File.Exists(ufp))
                    {
                        File.Delete(ufp);
                    }
                }

                foreach (string ud in ProtectedUserDirectories)
                {
                    string udp = Path.Combine(installDir, ud);
                    if (Directory.Exists(udp))
                    {
                        Directory.Delete(udp, recursive: true);
                    }
                }
            }

            string uninstExe = Path.Combine(installDir, UninstallerExeName);
            if (File.Exists(uninstExe) &&
                (string.IsNullOrWhiteSpace(selfExe) ||
                 !string.Equals(Path.GetFullPath(selfExe), Path.GetFullPath(uninstallerPathOrSelf(uninstExe)), StringComparison.OrdinalIgnoreCase)))
            {
                File.Delete(uninstExe);
            }

            if (!testMode && !Directory.EnumerateFileSystemEntries(installDir).Any())
            {
                Directory.Delete(installDir, recursive: false);
            }
        }

        WriteLog(logDir, "INFO", $"Uninstalled {meta.AppName} v{meta.Version} from {installDir} (purge_data={purgeData}, test_mode={testMode}).");
        Console.WriteLine($"Uninstalled {meta.AppName} from {installDir}");
        return 0;
    }

    private static void CleanupStaleOldUpgradeFiles(string installDir)
    {
        try
        {
            if (!Directory.Exists(installDir))
                return;

            foreach (string oldFile in Directory.EnumerateFiles(installDir, "*.old_upgrade", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(oldFile, FileAttributes.Normal);
                    File.Delete(oldFile);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Housekeeping.Installer] Old upgrade file still in use: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Housekeeping.Installer] CleanupStaleOldUpgradeFiles ignored: {ex.Message}");
        }
    }

    private static List<string> ReadPriorManifestFiles(string receiptPath)
    {
        var list = new List<string>();
        try
        {
            if (!File.Exists(receiptPath))
                return list;

            using var doc = JsonDocument.Parse(File.ReadAllText(receiptPath, Encoding.UTF8));
            if (doc.RootElement.TryGetProperty("files", out var filesArr) && filesArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in filesArr.EnumerateArray())
                {
                    string? rel = item.GetString();
                    if (!string.IsNullOrWhiteSpace(rel))
                    {
                        list.Add(rel.Replace('\\', '/').TrimStart('/'));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Housekeeping.Installer] ReadPriorManifestFiles ignored: {ex.Message}");
        }
        return list;
    }

    private static void CreatePreUpgradeBackup(string installDir, string? priorVersion, string? logDir)
    {
        try
        {
            string dataSubDir = Path.Combine(installDir, "data");
            var existingProtected = new List<(string Name, string FullPath)>();
            foreach (string f in ProtectedUserFiles)
            {
                string dataFile = Path.Combine(dataSubDir, f);
                string rootFile = Path.Combine(installDir, f);
                if (File.Exists(dataFile))
                {
                    existingProtected.Add((f, dataFile));
                }
                else if (File.Exists(rootFile))
                {
                    existingProtected.Add((f, rootFile));
                }
            }

            if (existingProtected.Count == 0)
                return;

            string backupsRoot = Path.Combine(installDir, "backups");
            string safePrior = string.IsNullOrWhiteSpace(priorVersion)
                ? "prior"
                : new string(priorVersion.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
            string snapFolder = Path.Combine(backupsRoot, $"upgrade_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{safePrior}");
            Directory.CreateDirectory(snapFolder);

            foreach (var (name, fullPath) in existingProtected)
            {
                string dest = Path.Combine(snapFolder, name);
                File.Copy(fullPath, dest, overwrite: true);
            }

            // Retain the 10 most recent pre-upgrade snapshot folders
            var upgradeDirs = new DirectoryInfo(backupsRoot)
                .GetDirectories("upgrade_*")
                .OrderByDescending(d => d.CreationTimeUtc)
                .ThenByDescending(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (int i = 10; i < upgradeDirs.Count; i++)
            {
                try { upgradeDirs[i].Delete(recursive: true); } catch (Exception ex) { Debug.WriteLine($"[Housekeeping.Installer] Old backup prune ignored: {ex.Message}"); }
            }

            WriteLog(logDir, "INFO", $"Created pre-upgrade backup of {existingProtected.Count} user state file(s) in {snapFolder}.");
        }
        catch (Exception ex)
        {
            WriteLog(logDir, "WARN", $"Pre-upgrade backup warning: {ex.Message}");
        }
    }

    private static FileStream OpenUpgradeSafeOutputStream(string destPath, string? logDir)
    {
        if (File.Exists(destPath))
        {
            try
            {
                File.SetAttributes(destPath, FileAttributes.Normal);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Housekeeping.Installer] SetAttributes ignored: {ex.Message}");
            }
        }

        try
        {
            return new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Destination binary/DLL is locked by a running process; rename in-place to .old_upgrade so extraction succeeds!
            string oldUpgradePath = destPath + ".old_upgrade";
            try
            {
                if (File.Exists(oldUpgradePath))
                {
                    File.SetAttributes(oldUpgradePath, FileAttributes.Normal);
                    File.Delete(oldUpgradePath);
                }
            }
            catch (Exception exOld)
            {
                Debug.WriteLine($"[Housekeeping.Installer] Old upgrade fallback timestamped: {exOld.Message}");
                oldUpgradePath = $"{destPath}.{DateTime.UtcNow.Ticks}.old_upgrade";
            }

            File.Move(destPath, oldUpgradePath, overwrite: true);
            WriteLog(logDir, "WARN", $"Renamed locked in-use file '{Path.GetFileName(destPath)}' -> '{Path.GetFileName(oldUpgradePath)}' for live in-place upgrade.");
            return new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
        }
    }

    private static void CopyFileUpgradeSafe(string sourcePath, string destPath, string? logDir)
    {
        try
        {
            if (File.Exists(destPath))
            {
                try { File.SetAttributes(destPath, FileAttributes.Normal); } catch (Exception ex) { Debug.WriteLine($"[Housekeeping.Installer] SetAttributes ignored: {ex.Message}"); }
            }
            File.Copy(sourcePath, destPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            string oldUpgradePath = destPath + ".old_upgrade";
            try
            {
                if (File.Exists(oldUpgradePath))
                {
                    File.SetAttributes(oldUpgradePath, FileAttributes.Normal);
                    File.Delete(oldUpgradePath);
                }
            }
            catch (Exception exOld)
            {
                Debug.WriteLine($"[Housekeeping.Installer] Uninstaller fallback timestamped: {exOld.Message}");
                oldUpgradePath = $"{destPath}.{DateTime.UtcNow.Ticks}.old_upgrade";
            }

            File.Move(destPath, oldUpgradePath, overwrite: true);
            WriteLog(logDir, "WARN", $"Renamed locked uninstaller '{Path.GetFileName(destPath)}' -> '{Path.GetFileName(oldUpgradePath)}' for live upgrade.");
            File.Copy(sourcePath, destPath, overwrite: true);
        }
    }

    private static string uninstallerPathOrSelf(string p) => p;

    private static string VerifyEmbeddedPayload(EmbeddedMetadata meta)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName)
            ?? throw new InvalidOperationException("Embedded payload archive resource is not present in this executable.");
        byte[] hashBytes = SHA256.HashData(stream);
        string actualHex = Convert.ToHexString(hashBytes).ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(meta.PayloadSha256) &&
            !string.Equals(actualHex, meta.PayloadSha256.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Payload SHA-256 mismatch: expected {meta.PayloadSha256}, computed {actualHex}.");
        }
        return actualHex;
    }

    private static EmbeddedMetadata LoadMetadata()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(MetadataResourceName);
        if (stream == null)
        {
            string asmVer = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0-rc.10-nightly.1";
            int plusIdx = asmVer.IndexOf('+');
            if (plusIdx > 0)
            {
                asmVer = asmVer[..plusIdx];
            }
            return new EmbeddedMetadata("Housekeeping", asmVer, "", 0);
        }

        var meta = JsonSerializer.Deserialize<EmbeddedMetadata>(stream);
        return meta ?? new EmbeddedMetadata("Housekeeping", "1.0.0-rc.10-nightly.1", "", 0);
    }

    private static string ResolveInstallDirectory(string? explicitDir, string? dataDir, bool testMode, bool silent, bool dryRun)
    {
        if (!string.IsNullOrWhiteSpace(explicitDir))
        {
            return Path.GetFullPath(explicitDir);
        }

        if (testMode && !string.IsNullOrWhiteSpace(dataDir))
        {
            return Path.GetFullPath(Path.Combine(dataDir, "installed_app"));
        }

        string? existingInstall = DetectExistingInstallLocation();
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string defaultDir = !string.IsNullOrWhiteSpace(existingInstall)
            ? existingInstall
            : Path.Combine(localAppData, "Programs", "Housekeeping");

        if (!silent && !dryRun && !Console.IsInputRedirected)
        {
            Console.Write($"Target install directory [{defaultDir}]: ");
            string? entered = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(entered))
            {
                return Path.GetFullPath(entered.Trim());
            }
        }

        return Path.GetFullPath(defaultDir);
    }

    private static string? DetectExistingInstallLocation()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Registry.CurrentUser.OpenSubKey(UninstallRegistrySubKey);
                string? regLoc = key?.GetValue("InstallLocation") as string;
                if (!string.IsNullOrWhiteSpace(regLoc) && Directory.Exists(regLoc))
                {
                    return Path.GetFullPath(regLoc);
                }
            }

            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                    continue;

                string candidate = Path.Combine(drive.RootDirectory.FullName, "Programs", "Housekeeping");
                if (Directory.Exists(candidate) &&
                    (File.Exists(Path.Combine(candidate, "Housekeeping.exe")) ||
                     File.Exists(Path.Combine(candidate, "settings.json"))))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Housekeeping.Installer] DetectExistingInstallLocation ignored: {ex.Message}");
        }

        return null;
    }

    private static void ValidateContainment(string installDir, string? dataDir, string? logDir, bool testMode)
    {
        if (!testMode && string.IsNullOrWhiteSpace(dataDir))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(dataDir))
        {
            throw new InvalidOperationException("Fail-closed sandbox violation: --data-dir is required in --test-mode.");
        }

        string allowedData = EnsureTrailingSep(Path.GetFullPath(dataDir));
        string? allowedParent = Directory.GetParent(Path.GetFullPath(dataDir))?.FullName;
        string allowedSandbox = string.IsNullOrWhiteSpace(allowedParent)
            ? allowedData
            : EnsureTrailingSep(Path.GetFullPath(allowedParent));

        string normInstall = EnsureTrailingSep(Path.GetFullPath(installDir));
        if (!normInstall.StartsWith(allowedData, StringComparison.OrdinalIgnoreCase) &&
            !normInstall.StartsWith(allowedSandbox, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Fail-closed sandbox containment violation: install target '{installDir}' escapes sandbox '{allowedSandbox}'.");
        }

        if (!string.IsNullOrWhiteSpace(logDir))
        {
            string normLog = EnsureTrailingSep(Path.GetFullPath(logDir));
            if (!normLog.StartsWith(allowedData, StringComparison.OrdinalIgnoreCase) &&
                !normLog.StartsWith(allowedSandbox, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Fail-closed sandbox containment violation: log directory '{logDir}' escapes sandbox '{allowedSandbox}'.");
            }
        }
    }

    private static void CreateShortcuts(string installDir, string? dataDir, bool testMode)
    {
        string targetExe = Path.Combine(installDir, "Housekeeping.exe");
        string iconPath = Path.Combine(installDir, "app", "Assets", "app_icon.ico");
        if (!File.Exists(iconPath))
        {
            iconPath = Path.Combine(installDir, "Assets", "app_icon.ico");
        }

        if (testMode && !string.IsNullOrWhiteSpace(dataDir))
        {
            string virtualStartMenu = Path.Combine(dataDir, "virtual_shortcuts");
            Directory.CreateDirectory(virtualStartMenu);
            var shortcutMeta = new Dictionary<string, string>
            {
                ["name"] = ShortcutFileName,
                ["target"] = targetExe,
                ["working_directory"] = installDir,
                ["icon"] = iconPath
            };
            File.WriteAllText(
                Path.Combine(virtualStartMenu, "Housekeeping.shortcut.json"),
                JsonSerializer.Serialize(shortcutMeta, new JsonSerializerOptions { WriteIndented = true }),
                Encoding.UTF8);
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string startMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        if (!string.IsNullOrWhiteSpace(startMenu))
        {
            string lnkPath = Path.Combine(startMenu, ShortcutFileName);
            WriteMinimalWindowsShortcut(lnkPath, targetExe, installDir, iconPath);
        }
    }

    private static void RemoveShortcuts(string installDir, string? dataDir, bool testMode)
    {
        if (testMode && !string.IsNullOrWhiteSpace(dataDir))
        {
            string virtualDir = Path.Combine(dataDir, "virtual_shortcuts");
            if (Directory.Exists(virtualDir))
            {
                Directory.Delete(virtualDir, recursive: true);
            }
            return;
        }

        string localLnk = Path.Combine(installDir, ShortcutFileName);
        if (File.Exists(localLnk))
        {
            File.Delete(localLnk);
        }

        string startMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        if (!string.IsNullOrWhiteSpace(startMenu))
        {
            string smLnk = Path.Combine(startMenu, ShortcutFileName);
            if (File.Exists(smLnk))
            {
                File.Delete(smLnk);
            }
        }
    }

    /// <summary>
    /// Writes a standards-compliant [MS-SHLLINK] (.lnk) binary file without spawning PowerShell or COM WScript.Shell.
    /// </summary>
    private static void WriteMinimalWindowsShortcut(string lnkPath, string targetExePath, string workingDir, string iconPath)
    {
        string? dir = Path.GetDirectoryName(lnkPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        const uint linkFlags = 0x00000010 | 0x00000020 | 0x00000040 | 0x00000080 | 0x00000100 | 0x00000200;
        const uint swShowMinNoActive = 7;
        string cmdExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        string args = $"/c start \"\" \"{targetExePath}\"";

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.Unicode, leaveOpen: true);

        // 76-byte ShellLinkHeader ([MS-SHLLINK] 2.1)
        bw.Write(0x0000004CU); // HeaderSize
        bw.Write(new Guid("00021401-0000-0000-C000-000000000046").ToByteArray()); // LinkCLSID
        bw.Write(linkFlags);
        bw.Write(0x00000020U); // FileAttributes (ARCHIVE)
        bw.Write(0UL); // CreationTime
        bw.Write(0UL); // AccessTime
        bw.Write(0UL); // WriteTime
        bw.Write(0U);  // FileSize
        bw.Write(0);   // IconIndex
        bw.Write(swShowMinNoActive); // ShowCommand
        bw.Write((ushort)0); // HotKey
        bw.Write((ushort)0); // Reserved1
        bw.Write(0U);        // Reserved2
        bw.Write(0U);        // Reserved3

        // StringData: WORKING_DIR, COMMAND_LINE_ARGUMENTS, ICON_LOCATION
        WriteCountedUnicode(bw, workingDir);
        WriteCountedUnicode(bw, args);
        WriteCountedUnicode(bw, File.Exists(iconPath) ? iconPath : targetExePath);

        // EnvironmentVariableDataBlock ([MS-SHLLINK] 2.5.4) -> 788 bytes pointing to cmd.exe
        bw.Write(0x00000314U); // BlockSize (788)
        bw.Write(0xA0000001U); // BlockSignature
        byte[] ansiBuf = new byte[260];
        byte[] ansiBytes = Encoding.ASCII.GetBytes(cmdExe.Length < 259 ? cmdExe : "%SystemRoot%\\System32\\cmd.exe");
        Array.Copy(ansiBytes, ansiBuf, Math.Min(ansiBytes.Length, 259));
        bw.Write(ansiBuf);

        byte[] uniBuf = new byte[520];
        byte[] uniBytes = Encoding.Unicode.GetBytes(cmdExe.Length < 259 ? cmdExe : "%SystemRoot%\\System32\\cmd.exe");
        Array.Copy(uniBytes, uniBuf, Math.Min(uniBytes.Length, 518));
        bw.Write(uniBuf);

        // TerminalBlock
        bw.Write(0U);
        bw.Flush();

        File.WriteAllBytes(lnkPath, ms.ToArray());
    }

    private static void WriteCountedUnicode(BinaryWriter bw, string value)
    {
        bw.Write((ushort)value.Length);
        bw.Write(Encoding.Unicode.GetBytes(value));
    }

    private static void RegisterUninstaller(
        string installDir,
        string uninstallerPath,
        string? dataDir,
        bool testMode,
        EmbeddedMetadata meta)
    {
        string iconPath = Path.Combine(installDir, "app", "Assets", "app_icon.ico");
        if (!File.Exists(iconPath))
        {
            iconPath = Path.Combine(installDir, "Assets", "app_icon.ico");
        }
        var entries = new Dictionary<string, object>
        {
            ["DisplayName"] = meta.AppName,
            ["DisplayVersion"] = meta.Version,
            ["Publisher"] = "StreamItAll",
            ["InstallLocation"] = installDir,
            ["DisplayIcon"] = File.Exists(iconPath) ? iconPath : Path.Combine(installDir, "Housekeeping.exe"),
            ["UninstallString"] = $"\"{uninstallerPath}\" --uninstall --install-dir \"{installDir}\"",
            ["QuietUninstallString"] = $"\"{uninstallerPath}\" --uninstall --silent --install-dir \"{installDir}\"",
            ["NoModify"] = 1,
            ["NoRepair"] = 1
        };

        if (testMode && !string.IsNullOrWhiteSpace(dataDir))
        {
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(
                Path.Combine(dataDir, "uninstall-registry.json"),
                JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }),
                Encoding.UTF8);
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(UninstallRegistrySubKey);
        if (key != null)
        {
            foreach (var kv in entries)
            {
                if (kv.Value is int dword)
                {
                    key.SetValue(kv.Key, dword, RegistryValueKind.DWord);
                }
                else
                {
                    key.SetValue(kv.Key, Convert.ToString(kv.Value) ?? "", RegistryValueKind.String);
                }
            }
        }
    }

    private static void RemoveUninstallerRegistration(string? dataDir, bool testMode)
    {
        if (testMode && !string.IsNullOrWhiteSpace(dataDir))
        {
            string regJson = Path.Combine(dataDir, "uninstall-registry.json");
            if (File.Exists(regJson))
            {
                File.Delete(regJson);
            }
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Registry.CurrentUser.DeleteSubKeyTree(UninstallRegistrySubKey, throwOnMissingSubKey: false);
    }

    private static void WriteLog(string? logDir, string level, string message)
    {
        if (string.IsNullOrWhiteSpace(logDir))
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(logDir);
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(logDir, "installer.log"), line, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Housekeeping.Installer] WriteLog sink ignored: {ex.Message}");
        }
    }

    private static string EnsureTrailingSep(string p) =>
        p.EndsWith(Path.DirectorySeparatorChar) ? p : p + Path.DirectorySeparatorChar;

    private static string? Env(string key)
    {
        string? val = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(val) ? null : val.Trim();
    }

    private static string? FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

    private static bool IsTruthy(string? v) =>
        v != null && (v.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                      v.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                      v.Equals("yes", StringComparison.OrdinalIgnoreCase));

    private static void PrintBanner(EmbeddedMetadata meta)
    {
        Console.WriteLine("============================================================");
        Console.WriteLine($"  {meta.AppName} — Self-Contained Setup (v{meta.Version})");
        Console.WriteLine("============================================================");
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  Housekeeping-Setup.exe [options]");
        Console.WriteLine("  Housekeeping-Setup.exe --silent [--install-dir <targetDir>]");
        Console.WriteLine("  Housekeeping-Setup.exe --verify");
        Console.WriteLine("  Housekeeping-Setup.exe --uninstall [--install-dir <targetDir>] [--purge-data]\n");
        Console.WriteLine("Options:");
        Console.WriteLine("  --install-dir|--target-dir <dir>  Target installation directory (default: %LOCALAPPDATA%\\Programs\\Housekeeping)");
        Console.WriteLine("  --silent|--quiet | --headless     Non-interactive execution without console prompts");
        Console.WriteLine("  --launch | -l                     Launch Housekeeping after setup completes");
        Console.WriteLine("  --no-launch                       Do not automatically relaunch Housekeeping after upgrade");
        Console.WriteLine("  --no-shortcuts                    Skip Start Menu and application folder .lnk creation");
        Console.WriteLine("  --verify                          Verify embedded payload SHA-256 without writing files");
        Console.WriteLine("  --uninstall                       Remove installed binaries, shortcuts, and Windows Uninstall entry");
        Console.WriteLine("  --purge-data                      Also delete user settings/history/logs/dats directories on --uninstall");
        Console.WriteLine("  --data-dir <dir>                  Sandbox containment root (overrides HOUSEKEEPING_DATA_DIR / DATA_DIR / APP_HOME)");
        Console.WriteLine("  --config <jsonPath>               JSON config override (overrides HOUSEKEEPING_CONFIG_FILE / CONFIG_PATH)");
        Console.WriteLine("  --log-dir <dir>                   Directory for structured installer.log (overrides HOUSEKEEPING_LOG_DIR / LOG_DIR)");
        Console.WriteLine("  --test-mode                       Enforce fail-closed sandbox containment & virtualize Registry/shortcuts");
        Console.WriteLine("  --dry-run                         Verify payload and print target path without mutating disk");
        Console.WriteLine("  --version | -v                    Print embedded installer version and exit");
        Console.WriteLine("  --help | -h | /?                  Show this help message");
    }

    private sealed class CliOptions
    {
        public string? InstallDir { get; set; }
        public string? DataDir { get; set; }
        public string? ConfigPath { get; set; }
        public string? LogDir { get; set; }
        public bool Uninstall { get; set; }
        public bool PurgeData { get; set; }
        public bool VerifyOnly { get; set; }
        public bool NoShortcuts { get; set; }
        public bool LaunchAfter { get; set; }
        public bool NoLaunch { get; set; }
        public bool Silent { get; set; }
        public bool TestMode { get; set; }
        public bool DryRun { get; set; }
        public bool Headless { get; set; }
        public bool ShowVersion { get; set; }
        public bool Help { get; set; }
    }

    private static CliOptions ParseArgs(string[] args)
    {
        var o = new CliOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "--install-dir":
                case "--target-dir":
                    if (i + 1 >= args.Length) throw new ArgumentException($"{a} requires a directory path.");
                    o.InstallDir = args[++i];
                    break;
                case "--data-dir":
                    if (i + 1 >= args.Length) throw new ArgumentException("--data-dir requires a directory path.");
                    o.DataDir = args[++i];
                    break;
                case "--config":
                    if (i + 1 >= args.Length) throw new ArgumentException("--config requires a file path.");
                    o.ConfigPath = args[++i];
                    break;
                case "--log-dir":
                    if (i + 1 >= args.Length) throw new ArgumentException("--log-dir requires a directory path.");
                    o.LogDir = args[++i];
                    break;
                case "--uninstall":
                    o.Uninstall = true;
                    break;
                case "--purge-data":
                    o.PurgeData = true;
                    break;
                case "--verify":
                    o.VerifyOnly = true;
                    break;
                case "--no-shortcuts":
                    o.NoShortcuts = true;
                    break;
                case "--launch":
                case "-l":
                    o.LaunchAfter = true;
                    break;
                case "--no-launch":
                    o.NoLaunch = true;
                    break;
                case "--silent":
                case "--quiet":
                    o.Silent = true;
                    break;
                case "--test-mode":
                    o.TestMode = true;
                    break;
                case "--dry-run":
                    o.DryRun = true;
                    break;
                case "--headless":
                    o.Headless = true;
                    break;
                case "-v":
                case "--version":
                    o.ShowVersion = true;
                    break;
                case "-h":
                case "--help":
                case "/?":
                case "help":
                    o.Help = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {a}. Pass --help for usage.");
            }
        }
        return o;
    }
}
