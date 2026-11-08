using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Models;

namespace Housekeeping.Services
{
    public class RemoteTerminalStreamService : IDisposable
    {
        private readonly Action<string, string>? _logger;
        private readonly Dictionary<int, CancellationTokenSource> _paneCts = new();
        private readonly object _lock = new();
        private bool _disposed = false;

        public RemoteTerminalStreamService(Action<string, string>? logger = null)
        {
            _logger = logger;
        }

        public void StartStreaming(
            int paneNumber,
            RemoteNodeConfig node,
            Action<List<string>> onNewLinesReceived,
            Action<string>? onStatusChanged = null)
        {
            StopStreaming(paneNumber);

            if (node == null || string.IsNullOrWhiteSpace(node.RemoteAppPath))
                return;

            lock (_lock)
            {
                var cts = new CancellationTokenSource();
                _paneCts[paneNumber] = cts;
                Task.Run(() => StreamWorkerAsync(paneNumber, node, onNewLinesReceived, onStatusChanged, cts.Token), cts.Token);
            }
        }

        public void StopStreaming(int paneNumber)
        {
            lock (_lock)
            {
                if (_paneCts.TryGetValue(paneNumber, out var cts))
                {
                    try { cts.Cancel(); cts.Dispose(); } catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[RemoteTerminalStreamService] Non-fatal error ignored: {__suppressedEx1.Message}"); }
                    _paneCts.Remove(paneNumber);
                }
            }
        }

        private async Task StreamWorkerAsync(
            int paneNumber,
            RemoteNodeConfig node,
            Action<List<string>> onNewLinesReceived,
            Action<string>? onStatusChanged,
            CancellationToken ct)
        {
            string targetDir = node.RemoteAppPath;
            string remoteLogsDir = Path.Combine(targetDir, "logs");

            onStatusChanged?.Invoke($"Connecting to {node.Name} ({node.HostOrIp})...");

            string? activeLogFile = null;
            long lastReadOffset = 0;
            bool isFirstRead = true;
            FileStream? persistentStream = null;

            while (!ct.IsCancellationRequested && !_disposed)
            {
                int delayMs = 1200;
                try
                {
                    if (!Directory.Exists(targetDir))
                    {
                        persistentStream?.Dispose();
                        persistentStream = null;
                        onStatusChanged?.Invoke($"Remote path '{targetDir}' offline");
                        await Task.Delay(3000, ct);
                        continue;
                    }

                    // Locate active log file
                    string candidate = ResolveActiveRemoteLog(remoteLogsDir, targetDir);

                    if (string.IsNullOrEmpty(candidate) || !File.Exists(candidate))
                    {
                        persistentStream?.Dispose();
                        persistentStream = null;
                        onStatusChanged?.Invoke($"Connected to {node.Name} — Waiting for log file");
                        await Task.Delay(2500, ct);
                        continue;
                    }

                    // If file changed (e.g. rolled or new session started)
                    if (!string.Equals(activeLogFile, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        persistentStream?.Dispose();
                        persistentStream = null;
                        activeLogFile = candidate;
                        lastReadOffset = 0;
                        isFirstRead = true;
                    }

                    if (string.IsNullOrEmpty(activeLogFile))
                        continue;

                    var newLines = new List<string>();

                    persistentStream ??= new FileStream(activeLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var fs = persistentStream;

                    if (isFirstRead)
                    {
                        isFirstRead = false;
                        // On first connect, grab the last ~32KB (up to ~100 lines)
                        if (fs.Length > 32768)
                        {
                            fs.Seek(fs.Length - 32768, SeekOrigin.Begin);
                            // Skip partial line
                            while (fs.Position < fs.Length)
                            {
                                int b = fs.ReadByte();
                                if (b == '\n') break;
                            }
                        }
                        else
                        {
                            fs.Seek(0, SeekOrigin.Begin);
                        }

                        using (var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true))
                        {
                            string? line;
                            while ((line = sr.ReadLine()) != null)
                            {
                                if (!string.IsNullOrWhiteSpace(line))
                                    newLines.Add(line);
                            }
                        }

                        lastReadOffset = fs.Position;
                        onStatusChanged?.Invoke($"Streaming logs: {node.Name} ({Path.GetFileName(activeLogFile)})");
                    }
                    else
                    {
                        if (fs.Length > lastReadOffset)
                        {
                            fs.Seek(lastReadOffset, SeekOrigin.Begin);
                            using (var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true))
                            {
                                string? line;
                                while ((line = sr.ReadLine()) != null)
                                {
                                    if (!string.IsNullOrWhiteSpace(line))
                                        newLines.Add(line);
                                }
                            }
                            lastReadOffset = fs.Position;
                        }
                        else if (fs.Length < lastReadOffset)
                        {
                            // Log file was rolled / truncated
                            lastReadOffset = 0;
                        }
                    }

                    if (newLines.Count > 0)
                    {
                        onNewLinesReceived(newLines);
                        delayMs = 400; // Adaptive fast poll when active output arrives
                    }
                    else
                    {
                        delayMs = 1200; // Backoff when idle
                    }
                }
                catch (OperationCanceledException)
                {
                    persistentStream?.Dispose();
                    break;
                }
                catch (Exception ex)
                {
                    persistentStream?.Dispose();
                    persistentStream = null;
                    onStatusChanged?.Invoke($"Remote connection error: {ex.Message}");
                    await Task.Delay(3000, ct);
                    continue;
                }

                try
                {
                    await Task.Delay(delayMs, ct);
                }
                catch (OperationCanceledException)
                {
                    persistentStream?.Dispose();
                    break;
                }
            }

            persistentStream?.Dispose();
        }

        private static string ResolveActiveRemoteLog(string remoteLogsDir, string remoteAppDir)
        {
            try
            {
                // Priority 1: logs\Housekeeping_Live.log
                string liveLog = Path.Combine(remoteLogsDir, "Housekeeping_Live.log");
                if (File.Exists(liveLog))
                    return liveLog;

                // Priority 2: Newest Housekeeping_*.log in remoteLogsDir
                if (Directory.Exists(remoteLogsDir))
                {
                    var newest = new DirectoryInfo(remoteLogsDir)
                        .GetFiles("Housekeeping_*.log")
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .FirstOrDefault();

                    if (newest != null)
                        return newest.FullName;
                }

                // Priority 3: Check app root directory for Housekeeping_Live.log
                string rootLive = Path.Combine(remoteAppDir, "Housekeeping_Live.log");
                if (File.Exists(rootLive))
                    return rootLive;
            }
            catch (System.Exception __suppressedEx2) { System.Diagnostics.Debug.WriteLine($"[RemoteTerminalStreamService] Non-fatal error ignored: {__suppressedEx2.Message}"); }

            return "";
        }

        public void Dispose()
        {
            _disposed = true;
            StopStreaming(1);
            StopStreaming(2);
        }
    }
}
