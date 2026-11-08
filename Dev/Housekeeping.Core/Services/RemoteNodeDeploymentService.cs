using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Models;

namespace Housekeeping.Services
{
    public class RemoteNodeDeploymentService
    {
        private static readonly ConcurrentDictionary<string, DateTime> _offlineUncCooldownUntilUtc = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _uncSingleFlightGates = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Runs a synchronous UNC SMB filesystem operation behind a per-path single-flight gate,
        /// a strict wall-clock timeout (.WaitAsync), and an offline/stalled-session cooldown cache.
        /// Prevents stalled SMB tree-connect/auth sessions (where TCP :445 connects quickly yet
        /// mrxsmb.sys Directory.Exists blocks for 30-60s) from holding HTTP sockets open or
        /// starving ThreadPool workers.
        /// </summary>
        public static async Task<T> RunBoundedUncOperationAsync<T>(
            string? targetUncPath,
            Func<T> syncOperation,
            T fallbackOnTimeoutOrBusy,
            int timeoutMs = 1800,
            int cooldownSeconds = 20,
            CancellationToken ct = default)
        {
            string key = string.IsNullOrWhiteSpace(targetUncPath) ? "__empty__" : targetUncPath.Trim();
            bool isUnc = key.StartsWith(@"\\", StringComparison.Ordinal);

            if (!isUnc)
            {
                return await Task.Run(syncOperation, ct).ConfigureAwait(false);
            }

            string cooldownKey = $"UNC_OP|{key}";
            if (_offlineUncCooldownUntilUtc.TryGetValue(cooldownKey, out var blockedUntil) && DateTime.UtcNow < blockedUntil)
            {
                return fallbackOnTimeoutOrBusy;
            }

            var gate = _uncSingleFlightGates.GetOrAdd(cooldownKey, _ => new SemaphoreSlim(1, 1));
            if (!gate.Wait(0))
            {
                return fallbackOnTimeoutOrBusy;
            }

            Task<T> workerTask = Task.Factory.StartNew(
                () =>
                {
                    try
                    {
                        return syncOperation();
                    }
                    finally
                    {
                        try { gate.Release(); } catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[RemoteNodeDeploymentService] Non-fatal error ignored: {__suppressedEx1.Message}"); }
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            try
            {
                var result = await workerTask
                    .WaitAsync(TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs, 250, 10000)), ct)
                    .ConfigureAwait(false);
                _offlineUncCooldownUntilUtc.TryRemove(cooldownKey, out _);
                return result;
            }
            catch (TimeoutException)
            {
                _offlineUncCooldownUntilUtc[cooldownKey] = DateTime.UtcNow.AddSeconds(Math.Max(5, cooldownSeconds));
                return fallbackOnTimeoutOrBusy;
            }
        }

        /// <summary>
        /// Performs a fast non-blocking TCP port 445 pre-flight check before touching Windows UNC SMB paths
        /// (e.g. \\SERVER\Share\...) so unreachable hosts return in &lt;= 1.2s without wedging ThreadPool
        /// threads inside mrxsmb.sys Directory.Exists() for 45-60 seconds.
        /// </summary>
        public static async Task<bool> IsUncHostReachableFastAsync(
            string? targetPath,
            string? fallbackHostOrIp = null,
            int timeoutMs = 1200,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
                return false;

            string trimmed = targetPath.Trim();
            if (!trimmed.StartsWith(@"\\", StringComparison.Ordinal))
                return true; // Local filesystem path — no SMB network hang risk

            string withoutSlashes = trimmed.TrimStart('\\');
            int slashIdx = withoutSlashes.IndexOfAny(new[] { '\\', '/' });
            string uncHost = slashIdx > 0 ? withoutSlashes.Substring(0, slashIdx).Trim() : withoutSlashes.Trim();
            if (string.IsNullOrWhiteSpace(uncHost))
                return false;

            string cacheKey = $"{uncHost}|{fallbackHostOrIp ?? ""}";
            if (_offlineUncCooldownUntilUtc.TryGetValue(cacheKey, out var blockedUntil) && DateTime.UtcNow < blockedUntil)
            {
                return false;
            }

            bool reachable = await ProbeTcpPortFastAsync(uncHost, 445, timeoutMs, ct).ConfigureAwait(false);
            if (!reachable && !string.IsNullOrWhiteSpace(fallbackHostOrIp) &&
                !string.Equals(uncHost, fallbackHostOrIp.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                string cleanHost = fallbackHostOrIp.Trim();
                if (Uri.TryCreate(cleanHost, UriKind.Absolute, out var parsedUri) && !string.IsNullOrWhiteSpace(parsedUri.Host))
                    cleanHost = parsedUri.Host;
                reachable = await ProbeTcpPortFastAsync(cleanHost, 445, timeoutMs, ct).ConfigureAwait(false);
            }

            if (reachable)
            {
                _offlineUncCooldownUntilUtc.TryRemove(cacheKey, out _);
                return true;
            }

            _offlineUncCooldownUntilUtc[cacheKey] = DateTime.UtcNow.AddSeconds(15);
            return false;
        }

        private static async Task<bool> ProbeTcpPortFastAsync(string host, int port, int timeoutMs, CancellationToken externalCt)
        {
            try
            {
                using var tcp = new TcpClient();
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
                timeoutCts.CancelAfter(Math.Clamp(timeoutMs, 250, 5000));
                await tcp.ConnectAsync(host, port, timeoutCts.Token).ConfigureAwait(false);
                return tcp.Connected;
            }
            catch
            {
                return false;
            }
        }
        public static string GetLocalAppVersion()
        {
            try
            {
                string vPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version.txt");
                if (File.Exists(vPath))
                {
                    string vText = File.ReadAllText(vPath).Trim();
                    if (!string.IsNullOrWhiteSpace(vText))
                        return vText;
                }

                var asm = typeof(RemoteNodeDeploymentService).Assembly;
                var fvi = FileVersionInfo.GetVersionInfo(asm.Location);
                if (!string.IsNullOrWhiteSpace(fvi.ProductVersion))
                    return fvi.ProductVersion.Trim();
                if (!string.IsNullOrWhiteSpace(fvi.FileVersion))
                    return fvi.FileVersion.Trim();
                return asm.GetName().Version?.ToString() ?? "1.0.0-rc.11-hf.1";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RemoteNodeDeploymentService] GetLocalAppVersion fallback: {ex.Message}");
                return "1.0.0-rc.11-hf.1";
            }
        }

        public static Version ParseVersionSafe(string? versionStr)
        {
            if (string.IsNullOrWhiteSpace(versionStr))
                return new Version(0, 0, 0, 0);

            string clean = versionStr.Trim().TrimStart('v', 'V');
            int metaIdx = clean.IndexOf('+');
            if (metaIdx > 0)
                clean = clean.Substring(0, metaIdx);
            int dashIdx = clean.IndexOf('-');
            if (dashIdx > 0)
                clean = clean.Substring(0, dashIdx);

            var parts = clean.Split('.', StringSplitOptions.RemoveEmptyEntries);
            int major = parts.Length > 0 && int.TryParse(parts[0], out int p0) ? p0 : 0;
            int minor = parts.Length > 1 && int.TryParse(parts[1], out int p1) ? p1 : 0;
            int build = parts.Length > 2 && int.TryParse(parts[2], out int p2) ? p2 : 0;
            int rev = parts.Length > 3 && int.TryParse(parts[3], out int p3) ? p3 : 0;
            return new Version(major, minor, build, rev);
        }

        private static string ExtractPreReleaseTag(string? versionStr)
        {
            if (string.IsNullOrWhiteSpace(versionStr))
                return "";

            string clean = versionStr.Trim().TrimStart('v', 'V');
            int plusIdx = clean.IndexOf('+');
            if (plusIdx > 0)
                clean = clean.Substring(0, plusIdx);

            int dashIdx = clean.IndexOf('-');
            return dashIdx > 0 && dashIdx + 1 < clean.Length
                ? clean.Substring(dashIdx + 1).Trim()
                : "";
        }

        /// <summary>
        /// Compares two SemVer 2.0.0 strings including pre-release tags (e.g. 1.0.0-alpha.1 &lt; 1.0.0-beta.1 &lt; 1.0.0-rc.5 &lt; 1.0.0-rc.7 &lt; 1.0.0).
        /// </summary>
        public static int CompareSemVer(string? aStr, string? bStr)
        {
            var va = ParseVersionSafe(aStr);
            var vb = ParseVersionSafe(bStr);

            int coreCmp = va.CompareTo(vb);
            if (coreCmp != 0)
                return coreCmp;

            string preA = ExtractPreReleaseTag(aStr);
            string preB = ExtractPreReleaseTag(bStr);

            if (string.IsNullOrEmpty(preA) && string.IsNullOrEmpty(preB))
                return 0;
            if (string.IsNullOrEmpty(preA))
                return 1; // Final GA > Pre-release
            if (string.IsNullOrEmpty(preB))
                return -1; // Pre-release < Final GA

            var partsA = preA.Split(new[] { '.', '-' }, StringSplitOptions.RemoveEmptyEntries);
            var partsB = preB.Split(new[] { '.', '-' }, StringSplitOptions.RemoveEmptyEntries);
            int minLen = Math.Min(partsA.Length, partsB.Length);

            for (int i = 0; i < minLen; i++)
            {
                bool numA = long.TryParse(partsA[i], out long ia);
                bool numB = long.TryParse(partsB[i], out long ib);

                if (numA && numB)
                {
                    int nCmp = ia.CompareTo(ib);
                    if (nCmp != 0) return nCmp;
                }
                else if (numA != numB)
                {
                    // Per SemVer 2.0.0 §11.4.1, numeric identifiers have lower precedence than non-numeric
                    return numA ? -1 : 1;
                }
                else
                {
                    int sCmp = string.Compare(partsA[i], partsB[i], StringComparison.OrdinalIgnoreCase);
                    if (sCmp != 0) return sCmp;
                }
            }

            return partsA.Length.CompareTo(partsB.Length);
        }

        public static void EvaluateVersionEligibility(RemoteNodeConfig node, string? localVerStr = null)
        {
            if (node == null) return;
            localVerStr ??= GetLocalAppVersion();
            var localVer = ParseVersionSafe(localVerStr);
            var remoteVer = ParseVersionSafe(node.DetectedVersion);

            bool hasValidRemote = remoteVer > new Version(0, 0, 0, 0);
            bool isLegacyPre1Beta = localVer.Major == 1 && remoteVer.Major == 2 && remoteVer.Minor >= 7;
            int semVerCmp = hasValidRemote ? CompareSemVer(node.DetectedVersion, localVerStr) : 1;

            if (isLegacyPre1Beta || (hasValidRemote && semVerCmp < 0))
            {
                node.CanUpdateApp = node.IsReachable;
                node.CanUpdateSettings = false;
                node.StatusMessage = $"Remote is older (v{node.DetectedVersion}) than local (v{localVerStr}). Ready for app update.";
            }
            else if (hasValidRemote && semVerCmp == 0)
            {
                node.CanUpdateApp = false;
                node.CanUpdateSettings = node.IsReachable;
                node.StatusMessage = $"Remote version matches local (v{localVerStr}). Ready for settings sync.";
            }
            else
            {
                node.CanUpdateApp = false;
                node.CanUpdateSettings = false;
                node.StatusMessage = $"Remote version is v{node.DetectedVersion} (Local: v{localVerStr}).";
            }
        }

        public async Task RefreshNodeStatusAsync(RemoteNodeConfig node, CancellationToken ct = default)
        {
            if (node == null) return;

            string targetDir = node.RemoteAppPath;
            if (string.IsNullOrWhiteSpace(targetDir))
            {
                node.IsReachable = false;
                node.DetectedVersion = "Path Not Configured";
                node.CanUpdateApp = false;
                node.CanUpdateSettings = false;
                node.StatusMessage = "Remote application path is empty.";
                return;
            }

            if (!await IsUncHostReachableFastAsync(targetDir, node.HostOrIp, 1200, ct).ConfigureAwait(false))
            {
                node.IsReachable = false;
                node.DetectedVersion = "Unreachable";
                node.CanUpdateApp = false;
                node.CanUpdateSettings = false;
                node.StatusMessage = $"Remote SMB host for '{targetDir}' is offline or unreachable on TCP :445.";
                return;
            }

            bool completedInTime = await RunBoundedUncOperationAsync(
                targetDir,
                () =>
                {
                    try
                    {
                        if (!Directory.Exists(targetDir))
                        {
                            node.IsReachable = false;
                            node.DetectedVersion = "Unreachable";
                            node.CanUpdateApp = false;
                            node.CanUpdateSettings = false;
                            node.StatusMessage = $"Directory '{targetDir}' does not exist or network share is offline.";
                            return true;
                        }

                        node.IsReachable = true;

                        // Locate version.txt first (matching GetLocalAppVersion priority), then fallback to DLL/EXE FileVersion
                        string versionFile = Path.Combine(targetDir, "version.txt");
                        string remoteDll = Path.Combine(targetDir, "Housekeeping.dll");
                        string remoteExe = Path.Combine(targetDir, "Housekeeping.exe");

                        string detectedVer = "Unknown";

                        if (File.Exists(versionFile))
                        {
                            try
                            {
                                string vText = File.ReadAllText(versionFile).Trim();
                                if (!string.IsNullOrWhiteSpace(vText))
                                    detectedVer = vText;
                            }
                            catch (System.Exception __suppressedEx2) { System.Diagnostics.Debug.WriteLine($"[RemoteNodeDeploymentService] Non-fatal error ignored: {__suppressedEx2.Message}"); }
                        }

                        if (detectedVer == "Unknown" && File.Exists(remoteDll))
                        {
                            try
                            {
                                var fvi = FileVersionInfo.GetVersionInfo(remoteDll);
                                detectedVer = fvi.FileVersion ?? fvi.ProductVersion ?? "Unknown";
                            }
                            catch (System.Exception __suppressedEx3) { System.Diagnostics.Debug.WriteLine($"[RemoteNodeDeploymentService] Non-fatal error ignored: {__suppressedEx3.Message}"); }
                        }
                        else if (detectedVer == "Unknown" && File.Exists(remoteExe))
                        {
                            try
                            {
                                var fvi = FileVersionInfo.GetVersionInfo(remoteExe);
                                detectedVer = fvi.FileVersion ?? fvi.ProductVersion ?? "Unknown";
                            }
                            catch (System.Exception __suppressedEx4) { System.Diagnostics.Debug.WriteLine($"[RemoteNodeDeploymentService] Non-fatal error ignored: {__suppressedEx4.Message}"); }
                        }

                        node.DetectedVersion = detectedVer;
                        EvaluateVersionEligibility(node);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        node.IsReachable = false;
                        node.DetectedVersion = "Error";
                        node.CanUpdateApp = false;
                        node.CanUpdateSettings = false;
                        node.StatusMessage = $"Connection error: {ex.Message}";
                        return true;
                    }
                },
                fallbackOnTimeoutOrBusy: false,
                timeoutMs: 2000,
                cooldownSeconds: 20,
                ct: ct).ConfigureAwait(false);

            if (!completedInTime)
            {
                node.IsReachable = false;
                node.DetectedVersion = "SMB Timeout";
                node.CanUpdateApp = false;
                node.CanUpdateSettings = false;
                node.StatusMessage = $"SMB share '{targetDir}' timed out during session negotiation.";
            }
        }

        public async Task<bool> UpdateRemoteAppBinariesAsync(
            RemoteNodeConfig node,
            Action<string, string> log,
            CancellationToken ct = default)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.RemoteAppPath))
            {
                log("ERROR", "Remote node configuration or RemoteAppPath is invalid.");
                return false;
            }

            return await Task.Run(() =>
            {
                try
                {
                    string localDir = AppDomain.CurrentDomain.BaseDirectory;
                    string remoteDir = node.RemoteAppPath;

                    log("HEADER", $"=== Deploying Housekeeping Update to Node: {node.Name} ({remoteDir}) ===");

                    if (!Directory.Exists(remoteDir))
                    {
                        log("INFO", $"Creating remote target directory: '{remoteDir}'...");
                        Directory.CreateDirectory(remoteDir);
                    }

                    // Discover deployable files, strictly excluding settings.json, logs, history, and user data
                    var sourceDir = new DirectoryInfo(localDir);
                    var filesToDeploy = sourceDir.GetFiles("*", SearchOption.AllDirectories)
                        .Where(f => !f.Name.Equals("settings.json", StringComparison.OrdinalIgnoreCase) &&
                                    !f.Name.Equals("history.json", StringComparison.OrdinalIgnoreCase) &&
                                    !f.FullName.Contains(@"\logs\", StringComparison.OrdinalIgnoreCase) &&
                                    !f.FullName.Contains(@"\dats\", StringComparison.OrdinalIgnoreCase) &&
                                    !f.FullName.Contains(@"\.git\", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    int deployedCount = 0;
                    foreach (var srcFile in filesToDeploy)
                    {
                        ct.ThrowIfCancellationRequested();

                        string relative = Path.GetRelativePath(localDir, srcFile.FullName);
                        string destPath = Path.Combine(remoteDir, relative);

                        string? destParent = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(destParent) && !Directory.Exists(destParent))
                        {
                            Directory.CreateDirectory(destParent);
                        }

                        // Copy with overwrite, handling locked files via temp rename if needed
                        try
                        {
                            File.Copy(srcFile.FullName, destPath, overwrite: true);
                            deployedCount++;
                        }
                        catch (IOException)
                        {
                            // If file is locked, try renaming existing to .old and re-copy
                            try
                            {
                                string oldPath = destPath + ".old";
                                if (File.Exists(oldPath)) File.Delete(oldPath);
                                File.Move(destPath, oldPath);
                                File.Copy(srcFile.FullName, destPath, overwrite: true);
                                deployedCount++;
                            }
                            catch (Exception ex2)
                            {
                                log("WARN", $"Skipped locked remote file '{relative}': {ex2.Message}");
                            }
                        }
                    }

                    // Write version.txt marker on remote
                    string localVer = GetLocalAppVersion();
                    try
                    {
                        File.WriteAllText(Path.Combine(remoteDir, "version.txt"), localVer);
                    }
                    catch (System.Exception __suppressedEx5) { System.Diagnostics.Debug.WriteLine($"[RemoteNodeDeploymentService] Non-fatal error ignored: {__suppressedEx5.Message}"); }

                    try
                    {
                        File.WriteAllText(Path.Combine(remoteDir, "restart_request.signal"), $"Restart after update to v{localVer} at {DateTime.UtcNow:o}");
                        log("INFO", $"Dispatched auto-restart signal to '{node.Name}'.");
                    }
                    catch (System.Exception __suppressedEx6) { System.Diagnostics.Debug.WriteLine($"[RemoteNodeDeploymentService] Non-fatal error ignored: {__suppressedEx6.Message}"); }

                    log("SUCCESS", $"Remote Node '{node.Name}' updated successfully! Deployed {deployedCount} file(s) to v{localVer}.");
                    return true;
                }
                catch (Exception ex)
                {
                    log("ERROR", $"Failed to update remote app binaries on '{node.Name}': {ex.Message}");
                    return false;
                }
            }, ct);
        }

        public async Task<bool> PushSettingsToRemoteAsync(
            RemoteNodeConfig node,
            AppConfig localConfig,
            Action<string, string> log,
            CancellationToken ct = default)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.RemoteAppPath))
            {
                log("ERROR", "Remote node configuration or RemoteAppPath is invalid.");
                return false;
            }

            return await Task.Run(() =>
            {
                try
                {
                    string remoteDir = node.RemoteAppPath;
                    string remoteSettingsPath = Path.Combine(remoteDir, "settings.json");

                    log("HEADER", $"=== Syncing Settings to Remote Node: {node.Name} ===");

                    if (!Directory.Exists(remoteDir))
                    {
                        Directory.CreateDirectory(remoteDir);
                    }

                    var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
                    var payloadToWrite = localConfig.DeepClone();

                    // Create backup of existing remote settings.json and preserve remote host's WebServer identity & RemoteNodes list
                    if (File.Exists(remoteSettingsPath))
                    {
                        string bakName = $"settings.json.bak_remote_{DateTime.Now:yyyyMMdd_HHmmss}";
                        string bakPath = Path.Combine(remoteDir, bakName);
                        try
                        {
                            File.Copy(remoteSettingsPath, bakPath, overwrite: true);
                            log("INFO", $"Created remote backup: {bakName}");
                        }
                        catch (System.Exception __suppressedEx7) { System.Diagnostics.Debug.WriteLine($"[RemoteNodeDeploymentService] Non-fatal error ignored: {__suppressedEx7.Message}"); }

                        try
                        {
                            string existingRemoteJson = ConfigService.ReadAllTextWithRetry(remoteSettingsPath);
                            var existingRemoteCfg = JsonSerializer.Deserialize<AppConfig>(existingRemoteJson, jsonOptions);
                            if (existingRemoteCfg != null)
                            {
                                if (existingRemoteCfg.WebServer != null)
                                {
                                    payloadToWrite.WebServer = existingRemoteCfg.WebServer;
                                }
                                if (existingRemoteCfg.RemoteNodes != null && existingRemoteCfg.RemoteNodes.Count > 0)
                                {
                                    payloadToWrite.RemoteNodes = existingRemoteCfg.RemoteNodes;
                                }
                            }
                        }
                        catch (System.Exception __suppressedEx8) { System.Diagnostics.Debug.WriteLine($"[RemoteNodeDeploymentService] Non-fatal error ignored: {__suppressedEx8.Message}"); }
                    }
                    else
                    {
                        // Never copy local host's admin password hash or NodeApiToken onto a brand-new remote node
                        payloadToWrite.WebServer.AdminPasswordHash = "";
                        payloadToWrite.WebServer.AdminPasswordSalt = "";
                        payloadToWrite.WebServer.NodeApiToken = "";
                        payloadToWrite.WebServer.EnsureNodeApiTokenInitialized();
                    }

                    // Serialize settings and write atomically to remote
                    string json = JsonSerializer.Serialize(payloadToWrite, jsonOptions);
                    ConfigService.AtomicWriteAllText(remoteSettingsPath, json);

                    log("SUCCESS", $"Settings synchronized to remote node '{node.Name}' successfully.");
                    return true;
                }
                catch (Exception ex)
                {
                    log("ERROR", $"Failed to push settings to '{node.Name}': {ex.Message}");
                    return false;
                }
            }, ct);
        }

        public static bool SendRemoteRestartSignal(RemoteNodeConfig node, Action<string, string> log)
        {
            try
            {
                if (node == null || string.IsNullOrWhiteSpace(node.RemoteAppPath)) return false;
                string signalPath = Path.Combine(node.RemoteAppPath, "restart_request.signal");
                File.WriteAllText(signalPath, $"Manual restart requested at {DateTime.UtcNow:o}");
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
                string signalPath = Path.Combine(node.RemoteAppPath, "control_request.json");
                string json = JsonSerializer.Serialize(req, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(signalPath, json);
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

                string settingsPath = Path.Combine(node.RemoteAppPath, "settings.json");
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
            catch (System.Exception __suppressedEx9) { System.Diagnostics.Debug.WriteLine($"[RemoteNodeDeploymentService] Non-fatal error ignored: {__suppressedEx9.Message}"); }
            return (new List<NamedJobQueue>(), new List<HousekeepingJob>());
        }
    }
}
