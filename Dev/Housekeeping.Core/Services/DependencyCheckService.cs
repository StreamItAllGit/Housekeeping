using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Core;
using Housekeeping.Interop;
using Housekeeping.Models;

namespace Housekeeping.Services
{
    public class DependencyStatusItem
    {
        public string ToolName { get; set; } = "";
        public string Description { get; set; } = "";
        public bool IsAvailable { get; set; }
        public string SourceLabel { get; set; } = ""; // "./tools/", "System PATH", "Custom Path", "Missing"
        public string ResolvedPath { get; set; } = "";
    }

    public class DependencyCheckService
    {
        public static readonly string PortableToolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");

        public string ActiveToolsDir { get; }

        private readonly Func<AppConfig> _getConfig;
        private readonly string? _explicitDataDirectory;
        private readonly bool _isolatedOverride;
        private readonly object _rcloneCacheLock = new();
        private List<string> _cachedRcloneRemotes = new();
        private bool _hasCachedRcloneRemotes = false;
        private int _rcloneRefreshInFlight = 0;

        public DependencyCheckService(Func<AppConfig> getConfig, string? customBaseDirectory = null, bool isolatedOverride = false)
        {
            _getConfig = getConfig;
            _isolatedOverride = isolatedOverride;
            _explicitDataDirectory = string.IsNullOrWhiteSpace(customBaseDirectory) ? null : Path.GetFullPath(customBaseDirectory);
            string root = _explicitDataDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            ActiveToolsDir = Path.Combine(root, "tools");
            SandboxResolution sandbox = SandboxEnvironmentSeam.ResolveCurrent();
            if (!sandbox.IsSandboxActive || SandboxEnvironmentSeam.IsPathWithinSandbox(ActiveToolsDir, sandbox))
                EnsureToolsDirectoryExists(ActiveToolsDir);
            MediaInfoNativeResolver.ConfigureDataDirectory(_explicitDataDirectory);
        }

        public bool HasCachedRcloneRemotes
        {
            get { lock (_rcloneCacheLock) return _hasCachedRcloneRemotes; }
        }

        /// <summary>
        /// Non-blocking UI-safe accessor: returns the cached rclone remotes list in 0 ms
        /// and triggers a one-time background refresh if never loaded.
        /// </summary>
        public List<string> GetCachedRcloneRemotes(Action<List<string>>? onBackgroundLoaded = null)
        {
            List<string> snapshot;
            bool shouldWarm;
            lock (_rcloneCacheLock)
            {
                snapshot = new List<string>(_cachedRcloneRemotes);
                shouldWarm = !_hasCachedRcloneRemotes;
            }

            if (shouldWarm)
            {
                _ = RefreshRcloneRemotesAsync(onBackgroundLoaded);
            }

            return snapshot;
        }

        /// <summary>
        /// Runs 'rclone listremotes' off the UI thread and updates the in-memory cache.
        /// </summary>
        public async Task<List<string>> RefreshRcloneRemotesAsync(Action<List<string>>? onCompleted = null)
        {
            if (Interlocked.Exchange(ref _rcloneRefreshInFlight, 1) == 1)
            {
                lock (_rcloneCacheLock)
                    return new List<string>(_cachedRcloneRemotes);
            }

            try
            {
                var fresh = await GetAvailableRcloneRemotesAsync().ConfigureAwait(false);
                lock (_rcloneCacheLock)
                {
                    _cachedRcloneRemotes = new List<string>(fresh);
                    _hasCachedRcloneRemotes = true;
                }
                onCompleted?.Invoke(new List<string>(fresh));
                return fresh;
            }
            finally
            {
                Interlocked.Exchange(ref _rcloneRefreshInFlight, 0);
            }
        }

        public static void EnsureToolsDirectoryExists(string? targetToolsDir = null)
        {
            try
            {
                string dir = string.IsNullOrWhiteSpace(targetToolsDir) ? PortableToolsDir : targetToolsDir;
                Directory.CreateDirectory(dir);
                string readmePath = Path.Combine(dir, "README_TOOLS.txt");
                if (!File.Exists(readmePath))
                {
                    File.WriteAllLines(readmePath, new[]
                    {
                        "Housekeeping — Portable External Tools Folder",
                        "==================================================",
                        "Drop any of the following executables into this folder to use them portably without installing them to your Windows PATH:",
                        "  - HandBrakeCLI.exe   (Used by Proxy Gen, Video Archiver, Media Transcoder)",
                        "  - 7z.exe + 7z.dll    (Used by Game Launchers)",
                        "  - rclone.exe         (Used by Cloud Sync)",
                        "  - hbd.exe            (Used by Humble Library)",
                        "  - MediaInfo.dll      (Resolved from app\\, app\\tools\\, data\\tools\\, or system MediaInfo; used by Media Transcoder)"
                    });
                }
            }
            catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[DependencyCheckService] Non-fatal error ignored: {__suppressedEx1.Message}"); }
        }

        public string ResolveHandBrakeCli()
        {
            var cfg = _getConfig().ToolPaths;
            return ResolveTool(
                cfg.HandBrakeCliPath,
                new[] { "HandBrakeCLI.exe", "handbrakecli.exe" },
                new[] { @"C:\Program Files\HandBrake\HandBrakeCLI.exe" }).resolvedPath;
        }

        public string ResolveSevenZip()
        {
            var cfg = _getConfig().ToolPaths;
            return ResolveTool(
                cfg.SevenZipPath,
                new[] { "7z.exe" },
                new[] { @"C:\Program Files\7-Zip\7z.exe", @"C:\Program Files (x86)\7-Zip\7z.exe" }).resolvedPath;
        }

        public string ResolveRclone()
        {
            var cfg = _getConfig().ToolPaths;
            return ResolveTool(
                cfg.RclonePath,
                new[] { "rclone.exe" },
                Array.Empty<string>()).resolvedPath;
        }

        public List<string> GetAvailableRcloneRemotes() =>
            Task.Run(() => GetAvailableRcloneRemotesAsync()).GetAwaiter().GetResult();

        public async Task<List<string>> GetAvailableRcloneRemotesAsync(CancellationToken ct = default)
        {
            var remotes = new List<string>();
            string rcloneExe = ResolveRclone();
            if (string.IsNullOrWhiteSpace(rcloneExe) || !File.Exists(rcloneExe))
                return remotes;

            try
            {
                bool forceContainment = _isolatedOverride || IsNonDefaultDataDirectory(_explicitDataDirectory);
                var containment = RcloneContainmentHelper.ResolveContainment(_explicitDataDirectory, forceContainment);
                if (containment.IsContained)
                    RcloneContainmentHelper.EnsureContainmentDirectories(containment);

                string args = containment.IsContained
                    ? $"listremotes --config \"{containment.ConfigFilePath}\" --cache-dir \"{containment.CacheDir}\""
                    : "listremotes";

                var psi = new ProcessStartInfo
                {
                    FileName = rcloneExe,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                if (containment.IsContained)
                {
                    foreach (var kv in RcloneContainmentHelper.BuildEnvironmentVariables(containment))
                        psi.Environment[kv.Key] = kv.Value;
                }

                using var proc = Process.Start(psi);
                if (proc == null)
                    return remotes;

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(3000);
                Task<string> stdOutTask = proc.StandardOutput.ReadToEndAsync(timeoutCts.Token);
                Task<string> stdErrTask = proc.StandardError.ReadToEndAsync(timeoutCts.Token);

                try
                {
                    await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
                    string output = await stdOutTask.ConfigureAwait(false);
                    _ = await stdErrTask.ConfigureAwait(false);
                    if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                    {
                        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        foreach (var line in lines)
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                                remotes.Add(line.EndsWith(':') ? line : line + ":");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* best-effort kill on timeout */ }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DependencyCheckService] Non-fatal error ignored: {ex.Message}");
            }

            return remotes;
        }

        private static bool IsNonDefaultDataDirectory(string? explicitDataDir)
        {
            if (string.IsNullOrWhiteSpace(explicitDataDir)) return false;
            string normExplicit = Path.GetFullPath(explicitDataDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normDefault = Path.GetFullPath(ConfigService.AppDataDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return !string.Equals(normExplicit, normDefault, StringComparison.OrdinalIgnoreCase);
        }

        public string ResolveHumbleDownloader()
        {
            var cfg = _getConfig().ToolPaths;
            return ResolveTool(
                cfg.HumbleDownloaderPath,
                new[] { "hbd.exe", "hbd.cmd", "hbd.bat" },
                Array.Empty<string>()).resolvedPath;
        }

        public List<DependencyStatusItem> CheckAllDependencies()
        {
            var cfg = _getConfig().ToolPaths;
            var list = new List<DependencyStatusItem>();

            var hb = ResolveTool(cfg.HandBrakeCliPath, new[] { "HandBrakeCLI.exe", "handbrakecli.exe" }, new[] { @"C:\Program Files\HandBrake\HandBrakeCLI.exe" });
            list.Add(new DependencyStatusItem
            {
                ToolName = "HandBrakeCLI",
                Description = "Video encoding engine (Proxy Gen, Video Archiver, Media Transcoder)",
                IsAvailable = hb.found,
                SourceLabel = hb.source,
                ResolvedPath = hb.resolvedPath
            });

            var sz = ResolveTool(cfg.SevenZipPath, new[] { "7z.exe" }, new[] { @"C:\Program Files\7-Zip\7z.exe", @"C:\Program Files (x86)\7-Zip\7z.exe" });
            list.Add(new DependencyStatusItem
            {
                ToolName = "7-Zip (7z.exe)",
                Description = "Archive extraction, fast ROM inspection & 7z ROM compression",
                IsAvailable = sz.found,
                SourceLabel = sz.source,
                ResolvedPath = sz.resolvedPath
            });

            var rc = ResolveTool(cfg.RclonePath, new[] { "rclone.exe" }, Array.Empty<string>());
            list.Add(new DependencyStatusItem
            {
                ToolName = "rclone",
                Description = "Cloud storage sync & bisync engine",
                IsAvailable = rc.found,
                SourceLabel = rc.source,
                ResolvedPath = rc.resolvedPath
            });

            var hbd = ResolveTool(cfg.HumbleDownloaderPath, new[] { "hbd.exe", "hbd.cmd", "hbd.bat" }, Array.Empty<string>());
            list.Add(new DependencyStatusItem
            {
                ToolName = "Humble Bundle CLI (hbd)",
                Description = "Humble Bundle Library Downloader",
                IsAvailable = hbd.found,
                SourceLabel = hbd.source,
                ResolvedPath = hbd.resolvedPath
            });

            ToolResolutionResult miRes = MediaInfoNativeResolver.Resolve(_explicitDataDirectory);
            bool miLoadable = MediaInfoNativeResolver.TryVerifyLoadable(_explicitDataDirectory, null, out _, out string? miResolvedPath);
            list.Add(new DependencyStatusItem
            {
                ToolName = "MediaInfo.dll",
                Description = "Source video resolution, HEVC codec & audio language track inspector",
                IsAvailable = miLoadable,
                SourceLabel = miLoadable
                    ? (miRes.Source == ToolResolutionSource.BundledAppTools ? "Bundled / Tools" : miRes.Source.ToString())
                    : (miRes.IsAvailable ? "Load Failed" : "Missing"),
                ResolvedPath = miResolvedPath ?? "MediaInfo.dll"
            });

            return list;
        }

        private (bool found, string source, string resolvedPath) ResolveTool(
            string customSettingPath,
            string[] exeNames,
            string[] commonInstallLocations)
        {
            // 1. Custom User Override
            if (!string.IsNullOrWhiteSpace(customSettingPath) && File.Exists(customSettingPath))
            {
                return (true, "Custom Path", customSettingPath);
            }

            // 2. Active Data/Portable ./tools/ Directory (plus read-only bundled ./tools/ fallback)
            foreach (string exe in exeNames)
            {
                string activeCandidate = Path.Combine(ActiveToolsDir, exe);
                if (File.Exists(activeCandidate))
                    return (true, "./tools/ (Portable)", activeCandidate);

                string bundledCandidate = Path.Combine(PortableToolsDir, exe);
                if (File.Exists(bundledCandidate))
                    return (true, "./tools/ (Portable)", bundledCandidate);
            }

            // 3. System PATH
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            string[] pathDirs = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (string dir in pathDirs)
            {
                foreach (string exe in exeNames)
                {
                    try
                    {
                        string candidate = Path.Combine(dir, exe);
                        if (File.Exists(candidate))
                            return (true, "System PATH", candidate);
                    }
                    catch (System.Exception __suppressedEx3) { System.Diagnostics.Debug.WriteLine($"[DependencyCheckService] Non-fatal error ignored: {__suppressedEx3.Message}"); }
                }
            }

            // 4. Common Windows Install Locations
            foreach (string common in commonInstallLocations)
            {
                if (File.Exists(common))
                    return (true, "Program Files", common);
            }

            return (false, "Missing", exeNames.FirstOrDefault() ?? "");
        }
    }
}
