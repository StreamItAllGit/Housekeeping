// @module: Housekeeping.Core/Services/NetworkSafetyService.DistributedLocks
// @purpose: Per-file distributed .hklock lease acquisition and startup sweep of orphaned .partial/.hklock artifacts.
// @depends-on: System.Collections.Generic, System.Diagnostics, System.IO, System.Text.Json, Housekeeping.Models
// @exports: NetworkSafetyService (partial)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Housekeeping.Services
{
    public partial class NetworkSafetyService
    {
        public bool TryAcquireDistributedLock(string finalOutputPath, out IDisposable? lockHandle, out string? conflictMessage)
        {
            lockHandle = null;
            conflictMessage = null;
            var cfg = _getConfig();
            if (!cfg.EnableDistributedNetworkLocking) return true;

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
                        var existing = JsonSerializer.Deserialize<DistributedLockInfo>(File.ReadAllText(lockFilePath));
                        if (existing != null)
                        {
                            existingMachine = string.IsNullOrWhiteSpace(existing.MachineName) ? "another machine" : existing.MachineName;
                            if (DateTime.UtcNow - existing.HeartbeatAtUtc > TimeSpan.FromMinutes(timeoutMinutes))
                                isStale = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[NetworkSafetyService] Lock read fallback: {ex.Message}");
                        if (DateTime.UtcNow - File.GetLastWriteTimeUtc(lockFilePath) > TimeSpan.FromMinutes(timeoutMinutes))
                            isStale = true;
                    }
                    if (isStale) TryDeleteFile(lockFilePath);
                    else { conflictMessage = $"Another machine ('{existingMachine}') has locked this file."; return false; }
                }

                var lockInfo = new DistributedLockInfo { TargetFilePath = finalOutputPath };
                using (var fs = new FileStream(lockFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(fs))
                    writer.Write(JsonSerializer.Serialize(lockInfo, SentinelJsonOpts));

                lockHandle = new DistributedFileLock(lockFilePath, _log);
                return true;
            }
            catch (IOException) { conflictMessage = "Another instance simultaneously acquired the lock for this file."; return false; }
            catch (Exception ex) { _log("WARN", $"[Lock] Unable to check distributed lock on '{Path.GetFileName(finalOutputPath)}': {ex.Message}"); return true; }
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
                    foreach (var p in Directory.GetFiles(dir, "*.partial.*", SearchOption.AllDirectories))
                    {
                        if (_pendingSwapService != null && _pendingSwapService.IsPending(p)) continue;
                        if (TryDeleteFile(p)) { deleted++; _log("INFO", $"Removed orphaned partial file from previous interrupted run: {p}"); }
                    }
                    if (cfg.EnableDistributedNetworkLocking)
                    {
                        foreach (var lk in Directory.GetFiles(dir, "*.hklock", SearchOption.AllDirectories))
                        {
                            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(lk) > TimeSpan.FromMinutes(timeoutMinutes) && TryDeleteFile(lk))
                            {
                                deleted++;
                                _log("INFO", $"Removed stale distributed lock file: {Path.GetFileName(lk)}");
                            }
                        }
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[NetworkSafetyService] Non-fatal dir cleanup error: {ex.Message}"); }
            }
            return deleted;
        }
    }
}
