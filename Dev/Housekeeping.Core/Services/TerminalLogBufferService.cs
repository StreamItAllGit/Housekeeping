using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Housekeeping.Models;

namespace Housekeeping.Services
{
    public sealed class TerminalLogEntry
    {
        public long Id { get; init; }
        public DateTime TimestampUtc { get; init; }
        public string TimestampText { get; init; } = "";
        public string Tag { get; init; } = "INFO";
        public string Message { get; init; } = "";
        public string FormattedLine { get; init; } = "";
    }

    /// <summary>
    /// Thread-safe ring buffer (5,000 lines), live log file rotator, and 50 ms coalesced batch publisher
    /// for real-time SignalR streaming without UI lockups.
    /// </summary>
    public sealed class TerminalLogBufferService : IDisposable
    {
        private const int DefaultRingBufferCapacity = 5000;
        public const long MaxLiveLogBytes = 10L * 1024L * 1024L; // 10 MB per Dev Bible Standard 12 §3.2
        public const int MaxRotatedGenerations = 5;
        public const int MaxRunLogRetentionDays = 30;

        private readonly object _lock = new();
        private readonly LinkedList<TerminalLogEntry> _ringBuffer = new();
        private readonly List<TerminalLogEntry> _pendingBatch = new();
        private readonly string _logsDir;
        private readonly string _liveLogPath;
        private readonly string _rotatedLogPath;
        private readonly string _appVersion;
        private readonly int _processId;
        private readonly Timer _flushTimer;

        private StreamWriter? _liveLogWriter;
        private long _currentLiveLogBytes;
        private StreamWriter? _activeRunWriter;
        private long _nextEntryId = 1;
        private int _maxCapacity;
        private bool _disposed;

        public event Action<IReadOnlyList<TerminalLogEntry>>? OnLogBatch;
        public event Action? OnCleared;

        public string LatestFooterLine { get; private set; } = "Ready — Housekeeping Engine Online";
        public string LiveLogFilePath => _liveLogPath;
        public long LatestEntryId => Interlocked.Read(ref _nextEntryId);

        public TerminalLogBufferService(string? logsDir = null, int maxCapacity = DefaultRingBufferCapacity)
        {
            string dir = string.IsNullOrWhiteSpace(logsDir) ? ConfigService.LogsDir : logsDir;
            Directory.CreateDirectory(dir);
            _logsDir = dir;
            _liveLogPath = Path.Combine(dir, "Housekeeping_Live.log");
            _rotatedLogPath = Path.Combine(dir, "Housekeeping_Live.old.log");
            _appVersion = RemoteNodeDeploymentService.GetLocalAppVersion();
            _processId = Environment.ProcessId;
            _maxCapacity = Math.Clamp(maxCapacity, 200, 20000);
            _flushTimer = new Timer(_ => FlushPendingBatch(), null, Timeout.Infinite, Timeout.Infinite);
            PruneStaleRunLogs(dir, MaxRunLogRetentionDays);
        }

        public void AttachRunWriter(StreamWriter? writer)
        {
            lock (_lock)
            {
                _activeRunWriter = writer;
            }
        }

        public void DetachRunWriter()
        {
            lock (_lock)
            {
                if (_activeRunWriter != null)
                {
                    try { _activeRunWriter.Flush(); _activeRunWriter.Dispose(); } catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[TerminalLogBufferService] Non-fatal error ignored: {__suppressedEx1.Message}"); }
                    _activeRunWriter = null;
                }
            }
        }

        public void Append(string tag, string message, AppConfig? configForFooter = null)
        {
            if (message == null) return;
            string cleanTag = string.IsNullOrWhiteSpace(tag) ? "INFO" : tag.Trim().ToUpperInvariant();
            var nowLocal = DateTime.Now;
            var nowUtc = nowLocal.ToUniversalTime();
            string tsText = nowLocal.ToString("HH:mm:ss");
            string formatted = $"[{tsText}] [{cleanTag}] {message}";
            string severity = MapSeverityLevel(cleanTag);
            string diskLine = $"{nowUtc:yyyy-MM-ddTHH:mm:ss.fffZ} [v{_appVersion}] [PID:{_processId}/T:{Environment.CurrentManagedThreadId}] [{severity}] [{cleanTag}] {message}";

            var entry = new TerminalLogEntry
            {
                Id = Interlocked.Increment(ref _nextEntryId),
                TimestampUtc = nowUtc,
                TimestampText = tsText,
                Tag = cleanTag,
                Message = message,
                FormattedLine = formatted
            };

            bool scheduleFlush = false;
            lock (_lock)
            {
                _ringBuffer.AddLast(entry);
                while (_ringBuffer.Count > _maxCapacity)
                {
                    _ringBuffer.RemoveFirst();
                }

                if (ShouldUpdateFooter(cleanTag, configForFooter))
                {
                    LatestFooterLine = message;
                }

                if (_pendingBatch.Count == 0)
                {
                    scheduleFlush = true;
                }
                _pendingBatch.Add(entry);

                WriteToDiskLocked(diskLine, formatted);
            }

            if (scheduleFlush && !_disposed)
            {
                try { _flushTimer.Change(50, Timeout.Infinite); } catch (System.Exception __suppressedEx2) { System.Diagnostics.Debug.WriteLine($"[TerminalLogBufferService] Non-fatal error ignored: {__suppressedEx2.Message}"); }
            }
        }

        private void EnsureLiveWriterLocked()
        {
            if (_liveLogWriter != null)
                return;

            string? dir = Path.GetDirectoryName(_liveLogPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            _currentLiveLogBytes = File.Exists(_liveLogPath) ? new FileInfo(_liveLogPath).Length : 0;
            var fs = new FileStream(_liveLogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096);
            _liveLogWriter = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };
        }

        private void CloseLiveWriterLocked()
        {
            if (_liveLogWriter != null)
            {
                try { _liveLogWriter.Flush(); _liveLogWriter.Dispose(); } catch (System.Exception __suppressedEx3) { System.Diagnostics.Debug.WriteLine($"[TerminalLogBufferService] Non-fatal error ignored: {__suppressedEx3.Message}"); }
                _liveLogWriter = null;
            }
        }

        private static string MapSeverityLevel(string cleanTag) => cleanTag switch
        {
            "ERROR" or "FATAL" => "ERROR",
            "WARN" or "NET" => "WARN",
            "DEBUG" => "DEBUG",
            _ => "INFO"
        };

        public static int PruneStaleRunLogs(string logsDir, int retentionDays = MaxRunLogRetentionDays, DateTime? nowUtcOverride = null)
        {
            int pruned = 0;
            try
            {
                string runsDir = Path.Combine(logsDir, "runs");
                if (!Directory.Exists(runsDir))
                    return 0;

                DateTime cutoffUtc = (nowUtcOverride ?? DateTime.UtcNow).AddDays(-Math.Max(1, retentionDays));
                foreach (var file in new DirectoryInfo(runsDir).EnumerateFiles("*.log"))
                {
                    if (file.LastWriteTimeUtc < cutoffUtc)
                    {
                        try
                        {
                            file.Delete();
                            pruned++;
                        }
                        catch (Exception exDel)
                        {
                            System.Diagnostics.Debug.WriteLine($"[TerminalLogBufferService] PruneStaleRunLogs delete skipped: {exDel.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TerminalLogBufferService] PruneStaleRunLogs warning: {ex.Message}");
            }
            return pruned;
        }

        private void RotateLiveLogGenerationsLocked()
        {
            for (int gen = MaxRotatedGenerations - 1; gen >= 1; gen--)
            {
                string srcGen = $"{_liveLogPath}.{gen}";
                string dstGen = $"{_liveLogPath}.{gen + 1}";
                if (File.Exists(srcGen))
                {
                    File.Move(srcGen, dstGen, overwrite: true);
                }
            }

            if (File.Exists(_liveLogPath))
            {
                string firstGen = $"{_liveLogPath}.1";
                File.Move(_liveLogPath, firstGen, overwrite: true);
                try
                {
                    File.Copy(firstGen, _rotatedLogPath, overwrite: true);
                }
                catch (Exception exCopy)
                {
                    System.Diagnostics.Debug.WriteLine($"[TerminalLogBufferService] Legacy .old.log copy ignored: {exCopy.Message}");
                }
            }

            PruneStaleRunLogs(_logsDir, MaxRunLogRetentionDays);
        }

        private void WriteToDiskLocked(string liveLogLine, string runLogLine)
        {
            try
            {
                EnsureLiveWriterLocked();
                if (_currentLiveLogBytes >= MaxLiveLogBytes)
                {
                    CloseLiveWriterLocked();
                    RotateLiveLogGenerationsLocked();
                    _currentLiveLogBytes = 0;
                    EnsureLiveWriterLocked();
                }

                if (_liveLogWriter != null)
                {
                    _liveLogWriter.WriteLine(liveLogLine);
                    _currentLiveLogBytes += Encoding.UTF8.GetByteCount(liveLogLine) + Environment.NewLine.Length;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TerminalLogBufferService] WriteToDiskLocked warning: {ex.Message}");
                CloseLiveWriterLocked();
            }

            if (_activeRunWriter != null)
            {
                try
                {
                    _activeRunWriter.WriteLine(runLogLine);
                }
                catch (System.Exception __suppressedEx4) { System.Diagnostics.Debug.WriteLine($"[TerminalLogBufferService] Non-fatal error ignored: {__suppressedEx4.Message}"); }
            }
        }

        public void FlushPendingBatch()
        {
            List<TerminalLogEntry>? batch = null;
            lock (_lock)
            {
                if (_pendingBatch.Count > 0)
                {
                    batch = new List<TerminalLogEntry>(_pendingBatch);
                    _pendingBatch.Clear();
                }
            }

            if (batch != null)
            {
                try { OnLogBatch?.Invoke(batch); } catch (System.Exception __suppressedEx5) { System.Diagnostics.Debug.WriteLine($"[TerminalLogBufferService] Non-fatal error ignored: {__suppressedEx5.Message}"); }
            }
        }

        public List<TerminalLogEntry> GetRecentEntries(int limit = 1500)
        {
            lock (_lock)
            {
                int safeLimit = Math.Clamp(limit, 1, _maxCapacity);
                var result = new List<TerminalLogEntry>(Math.Min(safeLimit, _ringBuffer.Count));
                int skip = Math.Max(0, _ringBuffer.Count - safeLimit);
                int idx = 0;
                foreach (var item in _ringBuffer)
                {
                    if (idx++ >= skip)
                        result.Add(item);
                }
                return result;
            }
        }

        public List<TerminalLogEntry> GetEntriesAfter(long afterEntryId, int maxCount = 500)
        {
            lock (_lock)
            {
                int safeLimit = Math.Clamp(maxCount, 1, _maxCapacity);
                var result = new List<TerminalLogEntry>();
                foreach (var item in _ringBuffer)
                {
                    if (item.Id > afterEntryId)
                    {
                        result.Add(item);
                    }
                }
                if (result.Count > safeLimit)
                {
                    return result.GetRange(result.Count - safeLimit, safeLimit);
                }
                return result;
            }
        }

        public List<string> GetPrecedingLogLines(int count = 50)
        {
            lock (_lock)
            {
                int limit = Math.Clamp(count, 1, _maxCapacity);
                int skip = Math.Max(0, _ringBuffer.Count - limit);
                var lines = new List<string>(Math.Min(limit, _ringBuffer.Count));
                int idx = 0;
                foreach (var entry in _ringBuffer)
                {
                    if (idx++ >= skip)
                        lines.Add(entry.FormattedLine);
                }
                return lines;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _ringBuffer.Clear();
                _pendingBatch.Clear();
            }
            try { OnCleared?.Invoke(); } catch (System.Exception __suppressedEx6) { System.Diagnostics.Debug.WriteLine($"[TerminalLogBufferService] Non-fatal error ignored: {__suppressedEx6.Message}"); }
        }

        public static bool ShouldUpdateFooter(string tag, AppConfig? config)
        {
            var level = config?.FooterVerbosity ?? FooterVerbosityLevel.ProgressAndStatus;
            string upper = tag.ToUpperInvariant();
            return level switch
            {
                FooterVerbosityLevel.AlertsOnly => upper is "ERROR" or "NET",
                FooterVerbosityLevel.Quiet => upper is "ERROR" or "WARN" or "NET",
                FooterVerbosityLevel.ProgressAndStatus => upper is "HEADER" or "SUCCESS" or "WORK" or "WARN" or "ERROR" or "NET",
                FooterVerbosityLevel.Custom => config?.CustomVerbosity?.FooterTags?.Exists(t => string.Equals(t, upper, StringComparison.OrdinalIgnoreCase)) == true,
                _ => true
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _flushTimer.Dispose(); } catch (System.Exception __suppressedEx7) { System.Diagnostics.Debug.WriteLine($"[TerminalLogBufferService] Non-fatal error ignored: {__suppressedEx7.Message}"); }
            FlushPendingBatch();
            DetachRunWriter();
            lock (_lock)
            {
                CloseLiveWriterLocked();
            }
        }
    }
}
