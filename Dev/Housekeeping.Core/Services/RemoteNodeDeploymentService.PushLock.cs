// @module: Housekeeping.Services.RemoteNodeDeploymentService.PushLock
// @purpose: Per-node SemaphoreSlim single-flight locking and unique {pid}.{guid}.tmp atomic staging for remote SMB operations.
// @depends-on: System, System.Collections.Concurrent, System.Diagnostics, System.IO, System.Text, System.Text.Json, System.Threading, System.Threading.Tasks, Housekeeping.Models
// @exports: Housekeeping.Services.RemoteNodeDeploymentService (partial: RunBoundedUncOperationAsync, PushSettingsToRemoteAsync)

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Models;

namespace Housekeeping.Services
{
    public partial class RemoteNodeDeploymentService
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _uncSingleFlightGates = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _nodeSettingsPushGates = new(StringComparer.OrdinalIgnoreCase);
        private static readonly UTF8Encoding RemoteUtf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

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

            string lockKey = ResolveNodePushLockKey(node.RemoteAppPath);
            var gate = _nodeSettingsPushGates.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await Task.Run(() => PushSettingsPayloadSingleFlight(node, localConfig, log, ct), ct).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        internal static string ResolveNodePushLockKey(string remoteAppPath) =>
            NormalizeRemoteRoot(remoteAppPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static bool PushSettingsPayloadSingleFlight(
            RemoteNodeConfig node,
            AppConfig localConfig,
            Action<string, string> log,
            CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
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

                ct.ThrowIfCancellationRequested();
                payloadToWrite.PrepareForSave();
                string json = JsonSerializer.Serialize(payloadToWrite, ConfigService.JsonOptions);
                WriteRemoteTextWithUniqueTemp(targetSettingsPath, json);

                log("SUCCESS", $"Settings synchronized to remote node '{node.Name}' successfully.");
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log("ERROR", $"Failed to push settings to '{node.Name}': {ex.Message}");
                return false;
            }
        }

        private static void WriteRemoteTextWithUniqueTemp(string targetPath, string content)
        {
            string fullTarget = Path.GetFullPath(targetPath);
            string? dir = Path.GetDirectoryName(fullTarget);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string tmpPath = $"{fullTarget}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
            try
            {
                using (var fs = new FileStream(tmpPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.WriteThrough))
                using (var writer = new StreamWriter(fs, RemoteUtf8NoBom))
                {
                    writer.Write(content);
                    writer.Flush();
                    fs.Flush(flushToDisk: true);
                }

                for (int attempt = 1; attempt <= 5; attempt++)
                {
                    try
                    {
                        File.Move(tmpPath, fullTarget, overwrite: true);
                        return;
                    }
                    catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < 5)
                    {
                        Thread.Sleep(Math.Min(Random.Shared.Next(40, 181) * attempt, 600));
                    }
                }

                File.Copy(tmpPath, fullTarget, overwrite: true);
            }
            finally
            {
                if (File.Exists(tmpPath))
                {
                    try { File.Delete(tmpPath); } catch (Exception ex) { Debug.WriteLine($"[RemoteNodeDeploymentService] Temp cleanup skipped ({tmpPath}): {ex.Message}"); }
                }
            }
        }
    }
}
