using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

    public partial class NetworkSafetyService
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

        public void TriggerManualRetry() => _manualRetrySignal?.TrySetResult(true);

        public void TriggerSkipWait()
        {
            _skipRequested = true;
            _manualRetrySignal?.TrySetResult(false);
        }

        public static bool IsPathReachable(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                string? root = Path.GetPathRoot(path);
                if (!string.IsNullOrEmpty(root) && !Directory.Exists(root)) return false;
                return Directory.Exists(path);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NetworkSafetyService] IsPathReachable check error: {ex.Message}");
                return false;
            }
        }

        public static bool IsRootReachable(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                string? root = Path.GetPathRoot(path);
                return string.IsNullOrEmpty(root) ? Directory.Exists(path) : Directory.Exists(root);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NetworkSafetyService] IsRootReachable check error: {ex.Message}");
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
                if (Directory.Exists(directoryPath)) return true;
                if (createIfRootExists && IsRootReachable(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                    return Directory.Exists(directoryPath);
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[NetworkSafetyService] CheckAndPrepareDirectory error: {ex.Message}"); }
            return false;
        }

        public string GetSafeEncodeOutputPath(string finalOutputPath)
        {
            var cfg = _getConfig();
            if (!cfg.EnableAtomicPartialWrites) return finalOutputPath;

            string dir = Path.GetDirectoryName(finalOutputPath) ?? "";
            string nameNoExt = Path.GetFileNameWithoutExtension(finalOutputPath);
            string ext = Path.GetExtension(finalOutputPath);
            return Path.Combine(dir, $"{nameNoExt}.partial{ext}");
        }

        public PendingSwapCheckResult TryResolveOrRecoverPendingEncode(string finalOutputPath, Action<string, string>? log = null)
        {
            var logger = log ?? _log;
            if (_pendingSwapService != null)
            {
                var pendingItem = _pendingSwapService.FindByTarget(finalOutputPath);
                if (pendingItem != null)
                {
                    if (!File.Exists(pendingItem.WorkingPath) || new FileInfo(pendingItem.WorkingPath).Length == 0)
                        _pendingSwapService.Discard(pendingItem, logger);
                    else
                        return _pendingSwapService.RetrySwap(pendingItem, logger) ? PendingSwapCheckResult.Committed : PendingSwapCheckResult.PendingLocked;
                }
            }

            string fallbackNew = finalOutputPath + ".new";
            if (File.Exists(fallbackNew))
            {
                try
                {
                    if (new FileInfo(fallbackNew).Length > 0)
                    {
                        logger("INFO", $"  -> [Recover] Found completed pending encode '{Path.GetFileName(fallbackNew)}'. Attempting swap...");
                        if (CommitPartialEncode(fallbackNew, finalOutputPath))
                            return File.Exists(finalOutputPath) ? PendingSwapCheckResult.Committed : PendingSwapCheckResult.PendingLocked;
                        return PendingSwapCheckResult.PendingLocked;
                    }
                }
                catch (Exception ex) { logger("WARN", $"  -> [Recover] Error inspecting fallback file '{Path.GetFileName(fallbackNew)}': {ex.Message}"); }
            }

            string workingPartial = GetSafeEncodeOutputPath(finalOutputPath);
            if (!string.Equals(workingPartial, finalOutputPath, StringComparison.OrdinalIgnoreCase) &&
                _pendingSwapService != null &&
                _pendingSwapService.IsPending(workingPartial))
            {
                var item = _pendingSwapService.GetAll().FirstOrDefault(x =>
                    string.Equals(x.WorkingPath, workingPartial, StringComparison.OrdinalIgnoreCase));
                return item != null && _pendingSwapService.RetrySwap(item, logger)
                    ? PendingSwapCheckResult.Committed
                    : PendingSwapCheckResult.PendingLocked;
            }

            return PendingSwapCheckResult.None;
        }

        public bool CommitPartialEncode(string workingOutputPath, string finalOutputPath, int maxAttempts = 15, int initialDelayMs = 200)
        {
            try
            {
                if (!File.Exists(workingOutputPath)) return false;

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
                    Directory.CreateDirectory(targetDir);

                maxAttempts = Math.Max(1, maxAttempts);
                string? lastError = null;

                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    if (!TryProbeExclusiveAccess(workingOutputPath, out string? workErr))
                    {
                        lastError = workErr;
                        if (attempt < maxAttempts)
                        {
                            int delay = Math.Min(initialDelayMs * attempt, 3000);
                            _log("NET", $"[Retry] Working partial file '{Path.GetFileName(workingOutputPath)}' is still held by encoder/AV/SMB ({workErr}). Retrying {attempt}/{maxAttempts} in {delay}ms...");
                            Thread.Sleep(delay);
                        }
                        continue;
                    }

                    if (File.Exists(finalOutputPath) && !TryProbeExclusiveAccess(finalOutputPath, out string? destErr))
                    {
                        lastError = destErr;
                        if (attempt < maxAttempts)
                        {
                            int delay = Math.Min(initialDelayMs * attempt, 3000);
                            _log("NET", $"[Retry] Destination file '{Path.GetFileName(finalOutputPath)}' is busy/locked by reader/SMB ({destErr}). Retrying {attempt}/{maxAttempts} in {delay}ms...");
                            Thread.Sleep(delay);
                        }
                        continue;
                    }

                    try
                    {
                        File.Move(workingOutputPath, finalOutputPath, overwrite: true);
                        return true;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        lastError = ex.Message;
                        if (attempt < maxAttempts)
                        {
                            int delay = Math.Min(initialDelayMs * attempt, 3000);
                            _log("NET", $"[Retry] Atomic move '{Path.GetFileName(workingOutputPath)}' -> '{Path.GetFileName(finalOutputPath)}' contended ({ex.Message}). Retrying {attempt}/{maxAttempts} in {delay}ms...");
                            Thread.Sleep(delay);
                        }
                    }
                }

                if (!TryProbeExclusiveAccess(workingOutputPath, out string? finalWorkErr))
                {
                    _pendingSwapService?.AddOrUpdate(workingOutputPath, finalOutputPath);
                    _log("WARN", $"Working partial file '{Path.GetFileName(workingOutputPath)}' remained locked ({finalWorkErr ?? lastError}). Preserved completed encode and queued for automatic swap without modifying '{Path.GetFileName(finalOutputPath)}'.");
                    return true;
                }

                string fallbackNew = finalOutputPath + ".new";
                try
                {
                    if (!File.Exists(fallbackNew) || TryProbeExclusiveAccess(fallbackNew, out _))
                    {
                        File.Move(workingOutputPath, fallbackNew, overwrite: true);
                        _pendingSwapService?.AddOrUpdate(fallbackNew, finalOutputPath);
                        _log("WARN", $"Destination '{Path.GetFileName(finalOutputPath)}' was locked ({lastError}). Staged complete encode to '{Path.GetFileName(fallbackNew)}' and queued for automatic swap.");
                        return true;
                    }
                }
                catch (Exception exFallback) { Debug.WriteLine($"[NetworkSafetyService] Fallback .new move skipped: {exFallback.Message}"); }

                _pendingSwapService?.AddOrUpdate(workingOutputPath, finalOutputPath);
                _log("WARN", $"Destination '{Path.GetFileName(finalOutputPath)}' was locked ({lastError}). Completed encode preserved at '{Path.GetFileName(workingOutputPath)}' and queued for automatic swap.");
                return true;
            }
            catch (Exception ex)
            {
                _log("ERROR", $"Failed to commit partial file '{Path.GetFileName(workingOutputPath)}' -> '{Path.GetFileName(finalOutputPath)}': {ex.Message}");
                return false;
            }
        }

        public void DiscardPartialEncode(string workingOutputPath, string finalOutputPath)
        {
            if (string.IsNullOrWhiteSpace(workingOutputPath)) return;
            if (_pendingSwapService != null && _pendingSwapService.IsPending(workingOutputPath))
            {
                _log("WARN", $"Preserving completed pending encode from discard: {Path.GetFileName(workingOutputPath)}");
                return;
            }
            if (!string.Equals(workingOutputPath, finalOutputPath, StringComparison.OrdinalIgnoreCase) && File.Exists(workingOutputPath))
            {
                if (TryDeleteFile(workingOutputPath))
                    _log("WARN", $"Cleaned up incomplete partial file: {Path.GetFileName(workingOutputPath)}");
            }
        }

        public static bool PurgeZeroByteFileIfExists(string path)
        {
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length == 0)
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                    return true;
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[NetworkSafetyService] Non-fatal zero-byte purge error: {ex.Message}"); }
            return false;
        }

        public static bool TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) { File.Delete(path); return true; } }
            catch (Exception ex) { Debug.WriteLine($"[NetworkSafetyService] Non-fatal file delete error: {ex.Message}"); }
            return false;
        }
    }
}
