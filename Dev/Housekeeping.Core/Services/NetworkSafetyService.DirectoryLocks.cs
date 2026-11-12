// @module: Housekeeping.Core/Services/NetworkSafetyService.DirectoryLocks
// @purpose: Exclusive SMB file-handle probing, Standard 19 §4 PID+StartTime .hklock sentinel coordination, and partial/lock cleanup.
// @depends-on: System.Diagnostics, System.IO, System.Text, System.Text.Json, Housekeeping.Models
// @exports: NetworkSafetyService (partial)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Housekeeping.Services
{
    public partial class NetworkSafetyService
    {
        private static readonly Regex EmbeddedPidRegex = new(@"_(\d+)\.hklock$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly JsonSerializerOptions SentinelJsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public static bool TryProbeExclusiveAccess(string filePath, out string? lockError)
        {
            lockError = null;
            if (string.IsNullOrWhiteSpace(filePath))
            {
                lockError = "File path is empty.";
                return false;
            }
            try
            {
                if (!File.Exists(filePath))
                {
                    lockError = $"File '{filePath}' does not exist.";
                    return false;
                }
                var attrs = File.GetAttributes(filePath);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(filePath, attrs & ~FileAttributes.ReadOnly);
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lockError = ex.Message;
                return false;
            }
        }

        public static bool IsPartialOrTransientArtifact(string? pathOrFileName)
        {
            if (string.IsNullOrWhiteSpace(pathOrFileName)) return false;
            string name = Path.GetFileName(pathOrFileName);
            if (string.IsNullOrEmpty(name)) return false;

            return name.Contains(".partial.", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains(".partial_", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(".partial", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(".redroom-part", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(".hklock", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals(".coexistence.json", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals(".archive_hashes.json", StringComparison.OrdinalIgnoreCase);
        }

        public static string GetCurrentProcessStartTimeUtcIso()
        {
            try
            {
                using var proc = Process.GetCurrentProcess();
                return proc.StartTime.ToUniversalTime().ToString("O");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NetworkSafetyService] Process start time fallback: {ex.Message}");
                return DateTime.UtcNow.ToString("O");
            }
        }

        public static DirectorySentinelLockHandle? AcquireDirectorySentinelLock(
            string directoryPath,
            string operation,
            int port = 5178,
            TimeSpan? leaseDuration = null)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
                return null;

            var lease = leaseDuration ?? TimeSpan.FromMinutes(15);
            var now = DateTime.UtcNow;
            int pid = Environment.ProcessId;
            string lockPath = Path.Combine(directoryPath, $".housekeeping_{pid}.hklock");

            var payload = new DirectorySentinelLockPayload
            {
                OwnerAppId = "housekeeping",
                Version = RemoteNodeDeploymentService.GetLocalAppVersion(),
                Pid = pid,
                ProcessStartTimeUtc = GetCurrentProcessStartTimeUtcIso(),
                Hostname = Environment.MachineName,
                Port = port,
                Operation = string.IsNullOrWhiteSpace(operation) ? "directory-mutation" : operation,
                TargetPath = Path.GetFullPath(directoryPath),
                AcquiredAtUtc = now.ToString("O"),
                HeartbeatAtUtc = now.ToString("O"),
                ExpiresAtUtc = now.Add(lease).ToString("O")
            };

            try
            {
                File.WriteAllText(lockPath, JsonSerializer.Serialize(payload, SentinelJsonOpts), new UTF8Encoding(false));
                return new DirectorySentinelLockHandle(lockPath, payload, lease);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NetworkSafetyService] Could not write sentinel lock '{lockPath}': {ex.Message}");
                return null;
            }
        }

        public static bool HasActiveDirectoryOrParentLock(
            string directoryPath,
            string selfAppId,
            out string? activeOwner,
            out string? activeLockFile,
            bool scanImmediateSubdirectories = false,
            Action<string, string>? log = null)
        {
            activeOwner = null;
            activeLockFile = null;
            bool found = TryFindActiveSentinelLock(
                directoryPath,
                TimeSpan.FromMinutes(15),
                scanImmediateSubdirectories,
                ignoreOwnProcessLock: true,
                selfAppId,
                log,
                out var payload,
                out string? lockPath,
                out _);
            if (!found) return false;
            activeOwner = payload?.AppId ?? "sister-app";
            activeLockFile = lockPath != null ? Path.GetFileName(lockPath) : null;
            return true;
        }

        public static bool HasActiveDirectoryOrParentLock(
            string directoryPath,
            out DirectorySentinelLockPayload? activeLock,
            bool scanImmediateSubdirectories = false,
            bool ignoreOwnProcessLock = true,
            Action<string, string>? log = null)
        {
            return TryFindActiveSentinelLock(
                directoryPath,
                TimeSpan.FromMinutes(15),
                scanImmediateSubdirectories,
                ignoreOwnProcessLock,
                "housekeeping",
                log,
                out activeLock,
                out _,
                out _);
        }

        public static bool HasActiveDirectoryOrParentLock(
            string directoryPath,
            out string? lockDetail,
            TimeSpan maxLockAge,
            bool scanImmediateSubdirectories = false,
            bool ignoreOwnProcessLock = true,
            Action<string, string>? log = null)
        {
            return TryFindActiveSentinelLock(
                directoryPath,
                maxLockAge,
                scanImmediateSubdirectories,
                ignoreOwnProcessLock,
                "housekeeping",
                log,
                out _,
                out _,
                out lockDetail);
        }

        private static bool TryFindActiveSentinelLock(
            string directoryPath,
            TimeSpan timeout,
            bool scanImmediateSubdirectories,
            bool ignoreOwnProcessLock,
            string selfAppId,
            Action<string, string>? log,
            out DirectorySentinelLockPayload? activePayload,
            out string? matchedLockFile,
            out string? lockDetail)
        {
            activePayload = null;
            matchedLockFile = null;
            lockDetail = null;
            if (string.IsNullOrWhiteSpace(directoryPath)) return false;

            var dirsToInspect = new List<string>();
            try
            {
                string full = Path.GetFullPath(directoryPath);
                string? cur = full;
                for (int level = 0; level <= 2 && !string.IsNullOrWhiteSpace(cur); level++)
                {
                    if (Directory.Exists(cur)) dirsToInspect.Add(cur);
                    string? parent = Path.GetDirectoryName(cur);
                    if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, cur, StringComparison.OrdinalIgnoreCase)) break;
                    cur = parent;
                }
                if (scanImmediateSubdirectories && Directory.Exists(full))
                    dirsToInspect.AddRange(Directory.EnumerateDirectories(full));
            }
            catch (Exception ex) { Debug.WriteLine($"[NetworkSafetyService] Lock path enumeration warning: {ex.Message}"); }

            foreach (string dir in dirsToInspect.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string[] lockFiles;
                try { lockFiles = Directory.GetFiles(dir, "*.hklock", SearchOption.TopDirectoryOnly); }
                catch (Exception ex) { Debug.WriteLine($"[NetworkSafetyService] Lock scan skipped '{dir}': {ex.Message}"); continue; }

                foreach (string lockFile in lockFiles)
                {
                    if (EvaluateSingleHkLockFile(lockFile, timeout, ignoreOwnProcessLock, selfAppId, log, out var parsed, out string? detail))
                    {
                        activePayload = parsed;
                        matchedLockFile = lockFile;
                        lockDetail = detail;
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool EvaluateSingleHkLockFile(
            string lockFile,
            TimeSpan maxLockAge,
            bool ignoreOwnProcessLock,
            string selfAppId,
            Action<string, string>? log,
            out DirectorySentinelLockPayload? activePayload,
            out string? activeDetail)
        {
            activePayload = null;
            activeDetail = null;
            string fileName = Path.GetFileName(lockFile);
            var now = DateTime.UtcNow;

            DirectorySentinelLockPayload? parsed = null;
            try
            {
                string raw = File.ReadAllText(lockFile);
                if (!string.IsNullOrWhiteSpace(raw) && raw.TrimStart().StartsWith('{'))
                    parsed = JsonSerializer.Deserialize<DirectorySentinelLockPayload>(raw, SentinelJsonOpts);
            }
            catch (Exception ex) { Debug.WriteLine($"[NetworkSafetyService] Lock JSON parse fallback '{fileName}': {ex.Message}"); }

            int pid = parsed?.Pid ?? 0;
            if (pid <= 0)
            {
                var m = EmbeddedPidRegex.Match(fileName);
                if (m.Success && int.TryParse(m.Groups[1].Value, out int parsedPid)) pid = parsedPid;
            }

            string host = !string.IsNullOrWhiteSpace(parsed?.Hostname) ? parsed!.Hostname : (parsed?.LegacyHost ?? Environment.MachineName);
            string ownerApp = !string.IsNullOrWhiteSpace(parsed?.OwnerAppId) ? parsed!.OwnerAppId : (parsed?.LegacyApp ?? InferOwnerAppFromFileName(fileName));
            bool isLocalHost = string.Equals(host, Environment.MachineName, StringComparison.OrdinalIgnoreCase);

            if (ignoreOwnProcessLock && isLocalHost && pid == Environment.ProcessId &&
                string.Equals(ownerApp, selfAppId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (isLocalHost && pid > 0)
            {
                Process? proc = null;
                try
                {
                    proc = Process.GetProcessById(pid);
                    if (proc.HasExited)
                    {
                        log?.Invoke("WARN", $"[WARN] Evicting stale lockfile (PID {pid} no longer exists): {fileName}");
                        TryDeleteFile(lockFile);
                        return false;
                    }
                    if (!string.IsNullOrWhiteSpace(parsed?.ProcessStartTimeUtc) &&
                        DateTimeOffset.TryParse(parsed.ProcessStartTimeUtc, out var expectedStart) &&
                        Math.Abs((proc.StartTime.ToUniversalTime() - expectedStart.UtcDateTime).TotalSeconds) > 2.0)
                    {
                        log?.Invoke("WARN", $"[WARN] Evicting PID-reused stale lockfile (PID {pid} start time mismatch): {fileName}");
                        TryDeleteFile(lockFile);
                        return false;
                    }
                }
                catch (ArgumentException)
                {
                    log?.Invoke("WARN", $"[WARN] Evicting stale lockfile (PID {pid} no longer exists): {fileName}");
                    TryDeleteFile(lockFile);
                    return false;
                }
                catch (Exception ex) { Debug.WriteLine($"[NetworkSafetyService] Process start check non-fatal (PID {pid}): {ex.Message}"); }
                finally { proc?.Dispose(); }
            }

            if (!string.IsNullOrWhiteSpace(parsed?.ExpiresAtUtc) && DateTimeOffset.TryParse(parsed.ExpiresAtUtc, out var expiresAt))
            {
                if (now > expiresAt.UtcDateTime)
                {
                    log?.Invoke("WARN", $"[WARN] Evicting expired lockfile '{fileName}' (expired at {expiresAt:O}).");
                    TryDeleteFile(lockFile);
                    return false;
                }
            }
            else
            {
                try
                {
                    if (now - File.GetLastWriteTimeUtc(lockFile) > maxLockAge)
                    {
                        log?.Invoke("WARN", $"[WARN] Evicting stale lockfile '{fileName}' (> {maxLockAge.TotalMinutes:F0}m old).");
                        TryDeleteFile(lockFile);
                        return false;
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[NetworkSafetyService] Stat error on '{fileName}': {ex.Message}"); return false; }
            }

            string op = string.IsNullOrWhiteSpace(parsed?.Operation) ? "active-mutation" : parsed!.Operation;
            activePayload = parsed ?? new DirectorySentinelLockPayload { OwnerAppId = ownerApp, Pid = pid, Hostname = host, Operation = op };
            activeDetail = $"{ownerApp} (PID {(pid > 0 ? pid.ToString() : "n/a")} on {host}, op={op}, file={fileName})";
            return true;
        }

        private static string InferOwnerAppFromFileName(string fileName)
        {
            if (fileName.StartsWith(".shadowlibrary", StringComparison.OrdinalIgnoreCase)) return "shadowlibrary";
            if (fileName.StartsWith(".redroom", StringComparison.OrdinalIgnoreCase)) return "redroom";
            if (fileName.StartsWith(".ustation", StringComparison.OrdinalIgnoreCase)) return "ustation";
            if (fileName.StartsWith(".streamengine", StringComparison.OrdinalIgnoreCase)) return "streamengine";
            if (fileName.StartsWith(".greenroom", StringComparison.OrdinalIgnoreCase)) return "greenroom";
            if (fileName.StartsWith(".housekeeping", StringComparison.OrdinalIgnoreCase)) return "housekeeping";
            return "sister-app";
        }
    }
}
