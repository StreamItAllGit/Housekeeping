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
    public partial class RemoteNodeDeploymentService
    {
        private static readonly ConcurrentDictionary<string, DateTime> _offlineUncCooldownUntilUtc = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _uncSingleFlightGates = new(StringComparer.OrdinalIgnoreCase);

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
                return await Task.Run(syncOperation, ct).ConfigureAwait(false);

            string cooldownKey = $"UNC_OP|{key}";
            if (_offlineUncCooldownUntilUtc.TryGetValue(cooldownKey, out var blockedUntil) && DateTime.UtcNow < blockedUntil)
                return fallbackOnTimeoutOrBusy;

            var gate = _uncSingleFlightGates.GetOrAdd(cooldownKey, _ => new SemaphoreSlim(1, 1));
            if (!gate.Wait(0))
                return fallbackOnTimeoutOrBusy;

            Task<T> workerTask = Task.Factory.StartNew(
                () =>
                {
                    try { return syncOperation(); }
                    finally
                    {
                        try { gate.Release(); }
                        catch (Exception ex) { Debug.WriteLine($"[RemoteNodeDeploymentService] Gate release ignored: {ex.Message}"); }
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
                return true;

            string withoutSlashes = trimmed.TrimStart('\\');
            int slashIdx = withoutSlashes.IndexOfAny(new[] { '\\', '/' });
            string uncHost = slashIdx > 0 ? withoutSlashes.Substring(0, slashIdx).Trim() : withoutSlashes.Trim();
            if (string.IsNullOrWhiteSpace(uncHost))
                return false;

            string cacheKey = $"{uncHost}|{fallbackHostOrIp ?? ""}";
            if (_offlineUncCooldownUntilUtc.TryGetValue(cacheKey, out var blockedUntil) && DateTime.UtcNow < blockedUntil)
                return false;

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
            catch (Exception ex)
            {
                Debug.WriteLine($"[RemoteNodeDeploymentService] TCP probe failed ({host}:{port}): {ex.Message}");
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
                {
                    string pv = fvi.ProductVersion.Trim();
                    int plusIdx = pv.IndexOf('+');
                    return plusIdx > 0 ? pv[..plusIdx] : pv;
                }
                if (!string.IsNullOrWhiteSpace(fvi.FileVersion))
                    return fvi.FileVersion.Trim();
                return asm.GetName().Version?.ToString() ?? "0.1.12";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RemoteNodeDeploymentService] GetLocalAppVersion fallback: {ex.Message}");
                return "0.1.12";
            }
        }

        public static Version ParseVersionSafe(string? versionStr)
        {
            if (string.IsNullOrWhiteSpace(versionStr))
                return new Version(0, 0, 0, 0);

            string clean = versionStr.Trim().TrimStart('v', 'V');
            int metaIdx = clean.IndexOf('+');
            if (metaIdx > 0) clean = clean.Substring(0, metaIdx);
            int dashIdx = clean.IndexOf('-');
            if (dashIdx > 0) clean = clean.Substring(0, dashIdx);

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
            if (plusIdx > 0) clean = clean.Substring(0, plusIdx);

            int dashIdx = clean.IndexOf('-');
            return dashIdx > 0 && dashIdx + 1 < clean.Length ? clean.Substring(dashIdx + 1).Trim() : "";
        }

        public static int CompareSemVer(string? aStr, string? bStr)
        {
            var va = ParseVersionSafe(aStr);
            var vb = ParseVersionSafe(bStr);

            int coreCmp = va.CompareTo(vb);
            if (coreCmp != 0) return coreCmp;

            string preA = ExtractPreReleaseTag(aStr);
            string preB = ExtractPreReleaseTag(bStr);

            if (string.IsNullOrEmpty(preA) && string.IsNullOrEmpty(preB)) return 0;
            if (string.IsNullOrEmpty(preA)) return 1;
            if (string.IsNullOrEmpty(preB)) return -1;

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
                        string remoteRoot = NormalizeRemoteRoot(targetDir);
                        if (!Directory.Exists(targetDir) && !Directory.Exists(remoteRoot))
                        {
                            node.IsReachable = false;
                            node.DetectedVersion = "Unreachable";
                            node.CanUpdateApp = false;
                            node.CanUpdateSettings = false;
                            node.StatusMessage = $"Directory '{targetDir}' does not exist or network share is offline.";
                            return true;
                        }

                        node.IsReachable = true;
                        node.DetectedVersion = DetectRemoteAppVersionFromDisk(targetDir);
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
            CancellationToken ct = default,
            string? localSourceDirOverride = null)
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
                    string localDir = string.IsNullOrWhiteSpace(localSourceDirOverride)
                        ? AppDomain.CurrentDomain.BaseDirectory
                        : localSourceDirOverride;
                    string remoteRoot = NormalizeRemoteRoot(node.RemoteAppPath);
                    string remoteBinaryDir = ResolveRemoteBinaryDir(node.RemoteAppPath);

                    log("HEADER", $"=== Deploying Housekeeping Update to Node: {node.Name} ({remoteBinaryDir}) ===");
                    Directory.CreateDirectory(remoteRoot);
                    Directory.CreateDirectory(remoteBinaryDir);

                    var sourceDir = new DirectoryInfo(localDir);
                    var filesToDeploy = sourceDir.GetFiles("*", SearchOption.AllDirectories)
                        .Where(f =>
                        {
                            string rel = Path.GetRelativePath(localDir, f.FullName);
                            return !ShouldExcludeFromRemoteBinarySync(rel, f.Name);
                        })
                        .ToList();

                    int deployedCount = 0;
                    foreach (var srcFile in filesToDeploy)
                    {
                        ct.ThrowIfCancellationRequested();
                        string relative = Path.GetRelativePath(localDir, srcFile.FullName);
                        string destPath = Path.Combine(remoteBinaryDir, relative);
                        string? destParent = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(destParent))
                            Directory.CreateDirectory(destParent);

                        try
                        {
                            File.Copy(srcFile.FullName, destPath, overwrite: true);
                            deployedCount++;
                        }
                        catch (IOException)
                        {
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

                    // If local host runs inside Clean Release Root (...\app\), also sync outer root launcher/manifest/AGENTS.md
                    string trimmedLocal = localDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (string.Equals(Path.GetFileName(trimmedLocal), "app", StringComparison.OrdinalIgnoreCase) &&
                        Directory.GetParent(trimmedLocal) is { Exists: true } localRoot &&
                        !string.Equals(remoteRoot, remoteBinaryDir, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (string rootItem in new[] { "Housekeeping.exe", "mother.manifest.json", "AGENTS.md" })
                        {
                            string srcRootFile = Path.Combine(localRoot.FullName, rootItem);
                            if (!File.Exists(srcRootFile)) continue;
                            try
                            {
                                File.Copy(srcRootFile, Path.Combine(remoteRoot, rootItem), overwrite: true);
                                deployedCount++;
                            }
                            catch (Exception exRoot) { Debug.WriteLine($"[RemoteNodeDeploymentService] Root file copy skipped ({rootItem}): {exRoot.Message}"); }
                        }
                    }

                    string localVer = GetLocalAppVersion();
                    try { ConfigService.AtomicWriteAllText(Path.Combine(remoteBinaryDir, "version.txt"), localVer); }
                    catch (Exception exVer) { Debug.WriteLine($"[RemoteNodeDeploymentService] Remote version.txt write skipped: {exVer.Message}"); }

                    SendRemoteRestartSignal(node, log);
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
                    string existingSettingsPath = ResolveRemoteSettingsReadPath(node.RemoteAppPath);
                    string targetSettingsPath = ResolveRemoteSettingsWritePath(node.RemoteAppPath);
                    string remoteBackupsDir = ResolveRemoteBackupsDir(node.RemoteAppPath);

                    log("HEADER", $"=== Syncing Settings to Remote Node: {node.Name} ({targetSettingsPath}) ===");
                    string? settingsParent = Path.GetDirectoryName(targetSettingsPath);
                    if (!string.IsNullOrWhiteSpace(settingsParent))
                        Directory.CreateDirectory(settingsParent);

                    var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
                    var payloadToWrite = localConfig.DeepClone();

                    if (File.Exists(existingSettingsPath))
                    {
                        Directory.CreateDirectory(remoteBackupsDir);
                        string bakName = $"settings.json.bak_remote_{DateTime.Now:yyyyMMdd_HHmmss}";
                        string bakPath = Path.Combine(remoteBackupsDir, bakName);
                        try
                        {
                            File.Copy(existingSettingsPath, bakPath, overwrite: true);
                            log("INFO", $"Created remote backup in backups\\: {bakName}");
                        }
                        catch (Exception exBak) { Debug.WriteLine($"[RemoteNodeDeploymentService] Remote backup skipped: {exBak.Message}"); }

                        try
                        {
                            string existingRemoteJson = ConfigService.ReadAllTextWithRetry(existingSettingsPath);
                            var existingRemoteCfg = JsonSerializer.Deserialize<AppConfig>(existingRemoteJson, jsonOptions);
                            if (existingRemoteCfg != null)
                            {
                                if (existingRemoteCfg.WebServer != null)
                                    payloadToWrite.WebServer = existingRemoteCfg.WebServer;
                                if (existingRemoteCfg.RemoteNodes != null && existingRemoteCfg.RemoteNodes.Count > 0)
                                    payloadToWrite.RemoteNodes = existingRemoteCfg.RemoteNodes;
                            }
                        }
                        catch (Exception exRead) { Debug.WriteLine($"[RemoteNodeDeploymentService] Existing remote settings read skipped: {exRead.Message}"); }
                    }
                    else
                    {
                        payloadToWrite.WebServer.AdminPasswordHash = "";
                        payloadToWrite.WebServer.AdminPasswordSalt = "";
                        payloadToWrite.WebServer.NodeApiToken = "";
                        payloadToWrite.WebServer.EnsureNodeApiTokenInitialized();
                    }

                    payloadToWrite.PrepareForSave();
                    string json = JsonSerializer.Serialize(payloadToWrite, ConfigService.JsonOptions);
                    ConfigService.AtomicWriteAllText(targetSettingsPath, json);

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
    }
}
