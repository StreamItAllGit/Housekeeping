using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Models;

namespace Housekeeping.Services
{
    public enum PendingSwapCheckResult
    {
        None,
        Committed,
        PendingLocked
    }

    public class DistributedLockInfo
    {
        public string MachineName { get; set; } = Environment.MachineName;
        public string UserName { get; set; } = Environment.UserName;
        public int ProcessId { get; set; } = Environment.ProcessId;
        public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime HeartbeatAtUtc { get; set; } = DateTime.UtcNow;
        public string TargetFilePath { get; set; } = "";
    }

    public sealed class DistributedFileLock : IDisposable
    {
        private readonly string _lockFilePath;
        private readonly Action<string, string> _log;
        private bool _disposed;

        public DistributedFileLock(string lockFilePath, Action<string, string> log)
        {
            _lockFilePath = lockFilePath;
            _log = log;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (File.Exists(_lockFilePath))
                {
                    File.Delete(_lockFilePath);
                }
            }
            catch (Exception ex)
            {
                _log("WARN", $"Failed to release distributed lock '{Path.GetFileName(_lockFilePath)}': {ex.Message}");
            }
        }
    }

    public class NetworkSafetyService
    {
        private readonly Func<AppConfig> _getConfig;
        private readonly Action<string, string> _log;
        private readonly PendingSwapService? _pendingSwapService;
        private TaskCompletionSource<bool>? _manualRetrySignal;
        private bool _skipRequested;

        public event Action<bool, string>? NetworkWaitStateChanged;
        public PendingSwapService? PendingSwaps => _pendingSwapService;

        public NetworkSafetyService(Func<AppConfig> getConfig, Action<string, string> log, PendingSwapService? pendingSwapService = null)
        {
            _getConfig = getConfig;
            _log = log;
            _pendingSwapService = pendingSwapService;
        }

        public void TriggerManualRetry()
        {
            _manualRetrySignal?.TrySetResult(true);
        }

        public void TriggerSkipWait()
        {
            _skipRequested = true;
            _manualRetrySignal?.TrySetResult(false);
        }

        public static bool IsPathReachable(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                string? root = Path.GetPathRoot(path);
                if (!string.IsNullOrEmpty(root) && !Directory.Exists(root))
                    return false;

                return Directory.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsRootReachable(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                string? root = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(root))
                    return Directory.Exists(path);
                return Directory.Exists(root);
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> EnsureDirectoryReachableAsync(string directoryPath, string contextName, bool createIfRootExists, CancellationToken ct)
        {
            var cfg = _getConfig();

            if (CheckAndPrepareDirectory(directoryPath, createIfRootExists))
                return true;

            if (!cfg.EnableNetworkDropSafety)
            {
                _log("WARN", $"[{contextName}] Path not reachable (Network Safety disabled, skipping): {directoryPath}");
                return false;
            }

            _skipRequested = false;
            int intervalSec = Math.Max(5, cfg.NetworkRetryIntervalSeconds);
            int maxRetries = Math.Max(1, cfg.NetworkMaxRetries);

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                if (ct.IsCancellationRequested)
                {
                    NetworkWaitStateChanged?.Invoke(false, "");
                    return false;
                }

                string msg = $"[{contextName}] Path unreachable: \"{directoryPath}\". Waiting for network/drive reconnection (Attempt {attempt}/{maxRetries}, retry in {intervalSec}s)...";
                _log("NET", msg);
                NetworkWaitStateChanged?.Invoke(true, msg);

                _manualRetrySignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var delayTask = Task.Delay(TimeSpan.FromSeconds(intervalSec), delayCts.Token);

                await Task.WhenAny(delayTask, _manualRetrySignal.Task);
                delayCts.Cancel();

                if (_skipRequested)
                {
                    _log("WARN", $"[{contextName}] User skipped waiting for unreachable path: {directoryPath}");
                    NetworkWaitStateChanged?.Invoke(false, "");
                    return false;
                }

                if (CheckAndPrepareDirectory(directoryPath, createIfRootExists))
                {
                    _log("SUCCESS", $"[{contextName}] Drive/path reconnected: {directoryPath}");
                    NetworkWaitStateChanged?.Invoke(false, "");
                    return true;
                }
            }

            NetworkWaitStateChanged?.Invoke(false, "");
            _log("ERROR", $"[{contextName}] Timed out waiting for path after {maxRetries} attempts: {directoryPath}");
            return false;
        }

        private static bool CheckAndPrepareDirectory(string directoryPath, bool createIfRootExists)
        {
            try
            {
                if (Directory.Exists(directoryPath))
                    return true;

                if (createIfRootExists && IsRootReachable(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                    return Directory.Exists(directoryPath);
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        public string GetSafeEncodeOutputPath(string finalOutputPath)
        {
            var cfg = _getConfig();
            if (!cfg.EnableAtomicPartialWrites)
                return finalOutputPath;

            string dir = Path.GetDirectoryName(finalOutputPath) ?? "";
            string nameNoExt = Path.GetFileNameWithoutExtension(finalOutputPath);
            string ext = Path.GetExtension(finalOutputPath);
            return Path.Combine(dir, $"{nameNoExt}.partial{ext}");
        }

        public PendingSwapCheckResult TryResolveOrRecoverPendingEncode(string finalOutputPath, Action<string, string>? log = null)
        {
            var logger = log ?? _log;

            // 1. Check if PendingSwapService tracks a completed encode for this target
            if (_pendingSwapService != null)
            {
                var pendingItem = _pendingSwapService.FindByTarget(finalOutputPath);
                if (pendingItem != null)
                {
                    if (!File.Exists(pendingItem.WorkingPath) || new FileInfo(pendingItem.WorkingPath).Length == 0)
                    {
                        _pendingSwapService.Discard(pendingItem, logger);
                    }
                    else
                    {
                        if (_pendingSwapService.RetrySwap(pendingItem, logger))
                        {
                            return PendingSwapCheckResult.Committed;
                        }
                        return PendingSwapCheckResult.PendingLocked;
                    }
                }
            }

            // 2. Check for an unregistered .new fallback file
            string fallbackNew = finalOutputPath + ".new";
            if (File.Exists(fallbackNew))
            {
                try
                {
                    if (new FileInfo(fallbackNew).Length > 0)
                    {
                        logger("INFO", $"  -> [Recover] Found completed pending encode '{Path.GetFileName(fallbackNew)}'. Attempting swap...");
                        if (CommitPartialEncode(fallbackNew, finalOutputPath))
                        {
                            return File.Exists(finalOutputPath)
                                ? PendingSwapCheckResult.Committed
                                : PendingSwapCheckResult.PendingLocked;
                        }
                        return PendingSwapCheckResult.PendingLocked;
                    }
                }
                catch (Exception ex)
                {
                    logger("WARN", $"  -> [Recover] Error inspecting fallback file '{Path.GetFileName(fallbackNew)}': {ex.Message}");
                }
            }

            // 3. Check if the .partial.* path itself is registered in PendingSwaps
            string workingPartial = GetSafeEncodeOutputPath(finalOutputPath);
            if (!string.Equals(workingPartial, finalOutputPath, StringComparison.OrdinalIgnoreCase) &&
                _pendingSwapService != null &&
                _pendingSwapService.IsPending(workingPartial))
            {
                var item = _pendingSwapService.GetAll().FirstOrDefault(x =>
                    string.Equals(x.WorkingPath, workingPartial, StringComparison.OrdinalIgnoreCase));
                if (item != null && _pendingSwapService.RetrySwap(item, logger))
                {
                    return PendingSwapCheckResult.Committed;
                }
                return PendingSwapCheckResult.PendingLocked;
            }

            return PendingSwapCheckResult.None;
        }

        public bool CommitPartialEncode(string workingOutputPath, string finalOutputPath, int maxAttempts = 15, int initialDelayMs = 200)
        {
            try
            {
                if (!File.Exists(workingOutputPath))
                    return false;

                var info = new FileInfo(workingOutputPath);
                if (info.Length == 0)
                {
                    TryDeleteFile(workingOutputPath);
                    return false;
                }

                if (string.Equals(workingOutputPath, finalOutputPath, StringComparison.OrdinalIgnoreCase))
                    return true;

                string? targetDir = Path.GetDirectoryName(finalOutputPath);
                if (!string.IsNullOrWhiteSpace(targetDir) && !Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        if (File.Exists(finalOutputPath))
                        {
                            try
                            {
                                File.SetAttributes(finalOutputPath, FileAttributes.Normal);
                                File.Delete(finalOutputPath);
                            }
                            catch { /* Ignore and let Move attempt overwrite */ }
                        }

                        File.Move(workingOutputPath, finalOutputPath, overwrite: true);
                        return true;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        if (attempt < maxAttempts)
                        {
                            int delay = Math.Min(initialDelayMs * attempt, 3000);
                            _log("NET", $"[Retry] Destination file '{Path.GetFileName(finalOutputPath)}' is busy/locked by indexer or SMB ({ex.Message}). Retrying {attempt}/{maxAttempts} in {delay}ms...");
                            Thread.Sleep(delay);
                        }
                    }
                }

                if (File.Exists(finalOutputPath))
                {
                    try
                    {
                        File.SetAttributes(finalOutputPath, FileAttributes.Normal);
                        File.Delete(finalOutputPath);
                    }
                    catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[NetworkSafetyService] Non-fatal error ignored: {__suppressedEx1.Message}"); }
                }

                try
                {
                    File.Move(workingOutputPath, finalOutputPath, overwrite: true);
                    return true;
                }
                catch (Exception ex)
                {
                    // Fallback: If destination remains locked (e.g. active stream reader/indexer), preserve the completed encode!
                    string fallbackNew = finalOutputPath + ".new";
                    try
                    {
                        if (File.Exists(fallbackNew))
                        {
                            try
                            {
                                File.SetAttributes(fallbackNew, FileAttributes.Normal);
                                File.Delete(fallbackNew);
                            }
                            catch (System.Exception __suppressedEx2) { System.Diagnostics.Debug.WriteLine($"[NetworkSafetyService] Non-fatal error ignored: {__suppressedEx2.Message}"); }
                        }

                        File.Move(workingOutputPath, fallbackNew, overwrite: true);
                        _pendingSwapService?.AddOrUpdate(fallbackNew, finalOutputPath);
                        _log("WARN", $"Destination '{Path.GetFileName(finalOutputPath)}' was locked by another process (e.g. Plex/SMB). Saved complete encode to '{Path.GetFileName(fallbackNew)}' and queued for automatic swap.");
                        return true;
                    }
                    catch
                    {
                        _pendingSwapService?.AddOrUpdate(workingOutputPath, finalOutputPath);
                        _log("WARN", $"Destination '{Path.GetFileName(finalOutputPath)}' was locked ({ex.Message}). Completed encode preserved at '{Path.GetFileName(workingOutputPath)}' and queued for automatic swap.");
                        return true; // Return true so caller does not delete the good partial file!
                    }
                }
            }
            catch (Exception ex)
            {
                _log("ERROR", $"Failed to commit partial file '{Path.GetFileName(workingOutputPath)}' -> '{Path.GetFileName(finalOutputPath)}': {ex.Message}");
                return false;
            }
        }

        public bool TryAcquireDistributedLock(string finalOutputPath, out IDisposable? lockHandle, out string? conflictMessage)
        {
            lockHandle = null;
            conflictMessage = null;

            var cfg = _getConfig();
            if (!cfg.EnableDistributedNetworkLocking)
                return true;

            string lockFilePath = finalOutputPath + ".hklock";
            int timeoutMinutes = Math.Max(10, cfg.DistributedLockTimeoutMinutes);

            try
            {
                if (File.Exists(lockFilePath))
                {
                    bool isStale = false;
                    string existingMachine = "another instance";
                    try
                    {
                        string json = File.ReadAllText(lockFilePath);
                        var existing = JsonSerializer.Deserialize<DistributedLockInfo>(json);
                        if (existing != null)
                        {
                            existingMachine = string.IsNullOrWhiteSpace(existing.MachineName) ? "another machine" : existing.MachineName;
                            var age = DateTime.UtcNow - existing.HeartbeatAtUtc;
                            if (age > TimeSpan.FromMinutes(timeoutMinutes))
                            {
                                isStale = true;
                                _log("WARN", $"[Lock] Found stale lock for '{Path.GetFileName(finalOutputPath)}' from '{existingMachine}' ({age.TotalMinutes:F0} min old). Overriding stale lock.");
                            }
                        }
                    }
                    catch
                    {
                        var lastWrite = File.GetLastWriteTimeUtc(lockFilePath);
                        if (DateTime.UtcNow - lastWrite > TimeSpan.FromMinutes(timeoutMinutes))
                        {
                            isStale = true;
                        }
                    }

                    if (isStale)
                    {
                        TryDeleteFile(lockFilePath);
                    }
                    else
                    {
                        conflictMessage = $"Another machine ('{existingMachine}') has locked this file.";
                        return false;
                    }
                }

                var lockInfo = new DistributedLockInfo
                {
                    MachineName = Environment.MachineName,
                    UserName = Environment.UserName,
                    ProcessId = Environment.ProcessId,
                    StartedAtUtc = DateTime.UtcNow,
                    HeartbeatAtUtc = DateTime.UtcNow,
                    TargetFilePath = finalOutputPath
                };

                string jsonPayload = JsonSerializer.Serialize(lockInfo, new JsonSerializerOptions { WriteIndented = true });
                using (var fs = new FileStream(lockFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(fs))
                {
                    writer.Write(jsonPayload);
                }

                lockHandle = new DistributedFileLock(lockFilePath, _log);
                return true;
            }
            catch (IOException)
            {
                conflictMessage = "Another instance simultaneously acquired the lock for this file.";
                return false;
            }
            catch (Exception ex)
            {
                _log("WARN", $"[Lock] Unable to check distributed lock on '{Path.GetFileName(finalOutputPath)}': {ex.Message}");
                return true;
            }
        }

        public void DiscardPartialEncode(string workingOutputPath, string finalOutputPath)
        {
            if (string.IsNullOrWhiteSpace(workingOutputPath))
                return;

            if (_pendingSwapService != null && _pendingSwapService.IsPending(workingOutputPath))
            {
                _log("WARN", $"Preserving completed pending encode from discard: {Path.GetFileName(workingOutputPath)}");
                return;
            }

            // Only delete if it's a .partial file or incomplete output
            if (!string.Equals(workingOutputPath, finalOutputPath, StringComparison.OrdinalIgnoreCase) && File.Exists(workingOutputPath))
            {
                if (TryDeleteFile(workingOutputPath))
                    _log("WARN", $"Cleaned up incomplete partial file: {Path.GetFileName(workingOutputPath)}");
            }
        }

        public int CleanupOrphanedPartials(IEnumerable<string> directories)
        {
            int deleted = 0;
            var cfg = _getConfig();
            int timeoutMinutes = Math.Max(10, cfg.DistributedLockTimeoutMinutes);

            foreach (string dir in directories)
            {
                if (!IsPathReachable(dir)) continue;
                try
                {
                    var partials = Directory.GetFiles(dir, "*.partial.*", SearchOption.AllDirectories);
                    foreach (var p in partials)
                    {
                        if (_pendingSwapService != null && _pendingSwapService.IsPending(p))
                        {
                            _log("INFO", $"[PendingSwap] Preserving pending locked encode: {p}");
                            continue;
                        }

                        if (TryDeleteFile(p))
                        {
                            deleted++;
                            _log("INFO", $"Removed orphaned partial file from previous interrupted run: {p}");
                        }
                    }

                    if (cfg.EnableDistributedNetworkLocking)
                    {
                        var locks = Directory.GetFiles(dir, "*.hklock", SearchOption.AllDirectories);
                        foreach (var lk in locks)
                        {
                            try
                            {
                                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(lk);
                                if (age > TimeSpan.FromMinutes(timeoutMinutes))
                                {
                                    if (TryDeleteFile(lk))
                                    {
                                        deleted++;
                                        _log("INFO", $"Removed stale distributed lock file: {Path.GetFileName(lk)}");
                                    }
                                }
                            }
                            catch (System.Exception __suppressedEx3) { System.Diagnostics.Debug.WriteLine($"[NetworkSafetyService] Non-fatal error ignored: {__suppressedEx3.Message}"); }
                        }
                    }
                }
                catch (System.Exception __suppressedEx4) { System.Diagnostics.Debug.WriteLine($"[NetworkSafetyService] Non-fatal error ignored: {__suppressedEx4.Message}"); }
            }
            return deleted;
        }

        public static bool PurgeZeroByteFileIfExists(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var fi = new FileInfo(path);
                    if (fi.Length == 0)
                    {
                        File.SetAttributes(path, FileAttributes.Normal);
                        File.Delete(path);
                        return true;
                    }
                }
            }
            catch (System.Exception __suppressedEx5) { System.Diagnostics.Debug.WriteLine($"[NetworkSafetyService] Non-fatal error ignored: {__suppressedEx5.Message}"); }
            return false;
        }

        public static bool TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    return true;
                }
            }
            catch (System.Exception __suppressedEx6) { System.Diagnostics.Debug.WriteLine($"[NetworkSafetyService] Non-fatal error ignored: {__suppressedEx6.Message}"); }
            return false;
        }
    }
}
