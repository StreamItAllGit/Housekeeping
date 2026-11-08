using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Models;
using Microsoft.Win32;

namespace Housekeeping.Services
{
    public class SystemPerformanceTelemetryService : IDisposable
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(
            out System.Runtime.InteropServices.ComTypes.FILETIME idleTime,
            out System.Runtime.InteropServices.ComTypes.FILETIME kernelTime,
            out System.Runtime.InteropServices.ComTypes.FILETIME userTime);

        private ulong _prevIdleTime;
        private ulong _prevKernelTime;
        private ulong _prevUserTime;
        private DateTime _prevCpuSampleTime = DateTime.MinValue;
        private double _lastCalculatedCpuPercent = 0.0;

        private long _prevNetBytes = 0;
        private DateTime _prevNetSampleTime = DateTime.MinValue;
        private double _lastCalculatedNetMbps = 0.0;

        // Drive caching to prevent SMB / spun-down disk freezes on telemetry sampling
        private readonly object _diskCacheLock = new();
        private List<DiskDriveMetricItem> _cachedDisks = new();
        private DateTime _lastDiskScanUtc = DateTime.MinValue;
        private int _isRefreshingDisks = 0;

        // Active execution status
        private readonly object _stateLock = new();
        private bool _isRunning = false;
        private string? _activeQueueId = null;
        private string? _activeQueueName = null;
        private int _currentJobIndex = 0;
        private int _totalJobsInQueue = 0;
        private string? _activeJobName = null;
        private string? _activeSubprocessStatus = null;
        private double _activeSubprocessProgress = 0.0;

        // Background worker loop
        private readonly string _logsDirectory;
        private DateTime _lastHeartbeatWriteUtc = DateTime.MinValue;
        private bool _lastHeartbeatWasRunning = false;
        private SystemPerformanceStats _cachedLocalStats = new();
        private CancellationTokenSource? _samplingCts;
        private Task? _samplingTask;
        private bool _disposed = false;

        public SystemPerformanceTelemetryService(string? logsDirectory = null)
        {
            _logsDirectory = string.IsNullOrWhiteSpace(logsDirectory)
                ? ConfigService.LogsDir
                : Path.GetFullPath(logsDirectory);
            double ramUsedGb = 0.0;
            double ramTotalGb = 0.0;
            try
            {
                var mem = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(mem) && mem.ullTotalPhys > 0)
                {
                    ramTotalGb = Math.Round(mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0), 1);
                    ulong usedBytes = mem.ullTotalPhys - mem.ullAvailPhys;
                    ramUsedGb = Math.Round(usedBytes / (1024.0 * 1024.0 * 1024.0), 1);
                }
            }
            catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[SystemPerformanceTelemetryService] Non-fatal error ignored: {__suppressedEx1.Message}"); }

            // Non-blocking initial snapshot (in-memory Win32 RAM read only; zero disk/SMB I/O).
            // Start() populates CPU, network, and fixed-drive metrics on a background thread.
            _cachedLocalStats = new SystemPerformanceStats
            {
                Timestamp = DateTime.UtcNow,
                HostMachineName = Environment.MachineName,
                RamUsedGb = ramUsedGb,
                RamTotalGb = ramTotalGb > 0 ? ramTotalGb : 1.0
            };
        }

        public void Start()
        {
            if (_samplingTask != null && !_samplingTask.IsCompleted)
                return;

            _samplingCts = new CancellationTokenSource();
            _samplingTask = Task.Run(() => BackgroundSamplingLoopAsync(_samplingCts.Token));
        }

        public void Stop()
        {
            try
            {
                _samplingCts?.Cancel();
            }
            catch (System.Exception __suppressedEx2) { System.Diagnostics.Debug.WriteLine($"[SystemPerformanceTelemetryService] Non-fatal error ignored: {__suppressedEx2.Message}"); }
        }

        public void SetExecutionState(
            bool isRunning,
            string? activeJobName,
            string? subprocessStatus,
            double subprocessProgress,
            string? queueId = null,
            string? queueName = null,
            int jobIndex = 0,
            int totalJobs = 0)
        {
            lock (_stateLock)
            {
                _isRunning = isRunning;
                _activeJobName = activeJobName;
                _activeSubprocessStatus = subprocessStatus;
                _activeSubprocessProgress = subprocessProgress;
                if (queueId != null) _activeQueueId = queueId;
                if (queueName != null) _activeQueueName = queueName;
                if (jobIndex > 0) _currentJobIndex = jobIndex;
                if (totalJobs > 0) _totalJobsInQueue = totalJobs;
                if (!isRunning)
                {
                    _currentJobIndex = 0;
                    _totalJobsInQueue = 0;
                }
            }
        }

        private async Task BackgroundSamplingLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !_disposed)
            {
                try
                {
                    var sample = SampleHardwareTelemetryInternal();

                    lock (_stateLock)
                    {
                        sample.IsRunning = _isRunning;
                        sample.ActiveQueueId = _activeQueueId;
                        sample.ActiveQueueName = _activeQueueName;
                        sample.CurrentJobIndex = _currentJobIndex;
                        sample.TotalJobsInQueue = _totalJobsInQueue;
                        sample.ActiveJobName = _activeJobName;
                        sample.ActiveSubprocessStatus = _activeSubprocessStatus;
                        sample.ActiveSubprocessProgress = _activeSubprocessProgress;
                        sample.HostMachineName = Environment.MachineName;
                    }

                    _cachedLocalStats = sample;

                    // Publish heartbeat to local log directory for remote consumers
                    PublishHeartbeatFile(sample);
                }
                catch (System.Exception __suppressedEx3) { System.Diagnostics.Debug.WriteLine($"[SystemPerformanceTelemetryService] Non-fatal error ignored: {__suppressedEx3.Message}"); }

                try
                {
                    await Task.Delay(2500, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void PublishHeartbeatFile(SystemPerformanceStats stats)
        {
            try
            {
                var now = DateTime.UtcNow;
                bool stateChanged = stats.IsRunning != _lastHeartbeatWasRunning;
                double minIntervalSec = stats.IsRunning ? 2.4 : 15.0;
                if (!stateChanged && (now - _lastHeartbeatWriteUtc).TotalSeconds < minIntervalSec)
                {
                    return;
                }

                _lastHeartbeatWriteUtc = now;
                _lastHeartbeatWasRunning = stats.IsRunning;

                string json = JsonSerializer.Serialize(stats, new JsonSerializerOptions { WriteIndented = true });

                if (!Directory.Exists(_logsDirectory))
                    Directory.CreateDirectory(_logsDirectory);

                string logsHeartbeat = Path.Combine(_logsDirectory, "telemetry_heartbeat.json");
                ConfigService.AtomicWriteAllText(logsHeartbeat, json);
            }
            catch (System.Exception __suppressedEx4) { System.Diagnostics.Debug.WriteLine($"[SystemPerformanceTelemetryService] Non-fatal error ignored: {__suppressedEx4.Message}"); }
        }

        private SystemPerformanceStats SampleHardwareTelemetryInternal()
        {
            var stats = new SystemPerformanceStats
            {
                Timestamp = DateTime.UtcNow,
                HostMachineName = Environment.MachineName
            };

            // 1. RAM via GlobalMemoryStatusEx (< 0.1ms)
            try
            {
                var memStatus = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(memStatus))
                {
                    double totalGb = memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
                    double availGb = memStatus.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
                    stats.RamTotalGb = totalGb;
                    stats.RamUsedGb = Math.Max(0, totalGb - availGb);
                }
            }
            catch (System.Exception __suppressedEx5) { System.Diagnostics.Debug.WriteLine($"[SystemPerformanceTelemetryService] Non-fatal error ignored: {__suppressedEx5.Message}"); }

            // 2. CPU via GetSystemTimes (< 0.1ms)
            try
            {
                if (GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
                {
                    ulong idle = ((ulong)idleFt.dwHighDateTime << 32) | (uint)idleFt.dwLowDateTime;
                    ulong kernel = ((ulong)kernelFt.dwHighDateTime << 32) | (uint)kernelFt.dwLowDateTime;
                    ulong user = ((ulong)userFt.dwHighDateTime << 32) | (uint)userFt.dwLowDateTime;

                    if (_prevCpuSampleTime != DateTime.MinValue)
                    {
                        ulong idleDiff = idle - _prevIdleTime;
                        ulong kernelDiff = kernel - _prevKernelTime;
                        ulong userDiff = user - _prevUserTime;
                        ulong totalDiff = kernelDiff + userDiff;

                        if (totalDiff > 0)
                        {
                            double busy = (double)(totalDiff - idleDiff) / totalDiff;
                            _lastCalculatedCpuPercent = Math.Clamp(busy * 100.0, 0.0, 100.0);
                        }
                    }

                    _prevIdleTime = idle;
                    _prevKernelTime = kernel;
                    _prevUserTime = user;
                    _prevCpuSampleTime = DateTime.UtcNow;
                }
            }
            catch (System.Exception __suppressedEx6) { System.Diagnostics.Debug.WriteLine($"[SystemPerformanceTelemetryService] Non-fatal error ignored: {__suppressedEx6.Message}"); }
            stats.CpuUsagePercent = _lastCalculatedCpuPercent;

            // 3. Disk Free Space — return cached Local + Mapped Network/NAS drives immediately;
            // trigger bounded background scan every 20s so offline SMB shares never stall CPU/RAM sampling.
            try
            {
                var now = DateTime.UtcNow;
                bool needsInitialLocalSeed;
                lock (_diskCacheLock)
                {
                    needsInitialLocalSeed = _cachedDisks.Count == 0;
                }

                if (needsInitialLocalSeed)
                {
                    var localSeed = ScanLocalFixedDrivesFast();
                    lock (_diskCacheLock)
                    {
                        if (_cachedDisks.Count == 0)
                            _cachedDisks = localSeed;
                    }
                }

                if ((now - _lastDiskScanUtc).TotalSeconds > 20 &&
                    Interlocked.CompareExchange(ref _isRefreshingDisks, 1, 0) == 0)
                {
                    _lastDiskScanUtc = now;
                    var token = _samplingCts?.Token ?? CancellationToken.None;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var fullDisks = await ScanLocalAndNetworkDrivesBoundedAsync(token).ConfigureAwait(false);
                            lock (_diskCacheLock)
                            {
                                _cachedDisks = fullDisks;
                            }
                            if (_cachedLocalStats != null)
                            {
                                _cachedLocalStats.Disks = new List<DiskDriveMetricItem>(fullDisks);
                            }
                        }
                        catch (System.Exception __suppressedEx7) { System.Diagnostics.Debug.WriteLine($"[SystemPerformanceTelemetryService] Non-fatal error ignored: {__suppressedEx7.Message}"); }
                        finally
                        {
                            Interlocked.Exchange(ref _isRefreshingDisks, 0);
                        }
                    });
                }

                lock (_diskCacheLock)
                {
                    stats.Disks = new List<DiskDriveMetricItem>(_cachedDisks);
                }

                var firstReady = stats.Disks.FirstOrDefault(d => d.IsReady) ?? stats.Disks.FirstOrDefault();
                if (firstReady != null)
                {
                    stats.DiskDriveLetter = firstReady.DriveLetter;
                    stats.DiskFreeGb = firstReady.FreeGb;
                    stats.DiskTotalGb = firstReady.TotalGb;
                }
            }
            catch (System.Exception __suppressedEx8) { System.Diagnostics.Debug.WriteLine($"[SystemPerformanceTelemetryService] Non-fatal error ignored: {__suppressedEx8.Message}"); }

            // 4. Network Throughput
            try
            {
                var now = DateTime.UtcNow;
                long currentNetBytes = 0;
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var ni in interfaces)
                {
                    if (ni.OperationalStatus == OperationalStatus.Up &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        try
                        {
                            var ipStats = ni.GetIPv4Statistics();
                            currentNetBytes += ipStats.BytesReceived + ipStats.BytesSent;
                        }
                        catch (System.Exception __suppressedEx9) { System.Diagnostics.Debug.WriteLine($"[SystemPerformanceTelemetryService] Non-fatal error ignored: {__suppressedEx9.Message}"); }
                    }
                }

                if (_prevNetSampleTime != DateTime.MinValue && now > _prevNetSampleTime)
                {
                    double elapsedSec = (now - _prevNetSampleTime).TotalSeconds;
                    if (elapsedSec > 0.2)
                    {
                        long diffBytes = currentNetBytes - _prevNetBytes;
                        if (diffBytes >= 0)
                        {
                            _lastCalculatedNetMbps = (diffBytes * 8.0) / (elapsedSec * 1_000_000.0);
                        }
                    }
                }

                _prevNetBytes = currentNetBytes;
                _prevNetSampleTime = now;
            }
            catch (System.Exception __suppressedEx10) { System.Diagnostics.Debug.WriteLine($"[SystemPerformanceTelemetryService] Non-fatal error ignored: {__suppressedEx10.Message}"); }
            stats.NetworkUsageMbps = Math.Round(_lastCalculatedNetMbps, 1);

            return stats;
        }

        private static List<DiskDriveMetricItem> ScanLocalFixedDrivesFast()
        {
            var list = new List<DiskDriveMetricItem>();
            try
            {
                var localDrives = DriveInfo.GetDrives()
                    .Where(d => d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable)
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var d in localDrives)
                {
                    try
                    {
                        string letter = d.Name.TrimEnd('\\');
                        bool ready = d.IsReady;
                        list.Add(new DiskDriveMetricItem
                        {
                            DriveLetter = letter,
                            DriveType = d.DriveType.ToString(),
                            IsNetwork = false,
                            IsReady = ready,
                            VolumeLabel = ready ? SafeReadString(() => d.VolumeLabel) : "",
                            FreeGb = ready ? d.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0) : 0.0,
                            TotalGb = ready ? d.TotalSize / (1024.0 * 1024.0 * 1024.0) : 0.0
                        });
                    }
                    catch (System.Exception __suppressedEx11) { System.Diagnostics.Debug.WriteLine($"[SystemPerformanceTelemetryService] Non-fatal error ignored: {__suppressedEx11.Message}"); }
                }
            }
            catch (System.Exception __suppressedEx12) { System.Diagnostics.Debug.WriteLine($"[SystemPerformanceTelemetryService] Non-fatal error ignored: {__suppressedEx12.Message}"); }
            return list;
        }

        /// <summary>
        /// Enumerates all mounted local drives plus all mapped network/NAS drives (from DriveInfo + HKCU\Network)
        /// behind fast TCP :445 pre-flight and bounded 1.2s single-flight SMB timeouts so offline NAS shares
        /// report IsReady=false ("V: !") cleanly without blocking telemetry or sockets.
        /// </summary>
        public static async Task<List<DiskDriveMetricItem>> ScanLocalAndNetworkDrivesBoundedAsync(CancellationToken ct = default)
        {
            var resultsByLetter = new Dictionary<string, DiskDriveMetricItem>(StringComparer.OrdinalIgnoreCase);

            // 1. Read persistent mapped network drive letters -> UNC RemotePath from HKCU\Network
            var mappedNetworkPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var netKey = Registry.CurrentUser.OpenSubKey("Network");
                if (netKey != null)
                {
                    foreach (string subName in netKey.GetSubKeyNames())
                    {
                        if (string.IsNullOrWhiteSpace(subName)) continue;
                        string letter = subName.Trim().TrimEnd(':').ToUpperInvariant() + ":";
                        using var driveKey = netKey.OpenSubKey(subName);
                        string remotePath = (driveKey?.GetValue("RemotePath") as string)?.Trim() ?? "";
                        if (!string.IsNullOrWhiteSpace(remotePath))
                        {
                            mappedNetworkPaths[letter] = remotePath;
                        }
                    }
                }
            }
            catch (System.Exception __suppressedEx13) { System.Diagnostics.Debug.WriteLine($"[SystemPerformanceTelemetryService] Non-fatal error ignored: {__suppressedEx13.Message}"); }

            // 2. Scan Local Fixed/Removable drives synchronously (< 1 ms) and collect candidate Network drives
            var networkCandidates = new Dictionary<string, (DriveInfo? Drive, string RemotePath)>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    if (d.DriveType == DriveType.CDRom)
                        continue;

                    string letter = d.Name.TrimEnd('\\').ToUpperInvariant();
                    bool isMappedNet = d.DriveType == DriveType.Network || mappedNetworkPaths.ContainsKey(letter);

                    if (!isMappedNet && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable || d.DriveType == DriveType.Ram))
                    {
                        try
                        {
                            bool ready = d.IsReady;
                            resultsByLetter[letter] = new DiskDriveMetricItem
                            {
                                DriveLetter = letter,
                                DriveType = d.DriveType.ToString(),
                                IsNetwork = false,
                                IsReady = ready,
                                VolumeLabel = ready ? SafeReadString(() => d.VolumeLabel) : "",
                                FreeGb = ready ? d.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0) : 0.0,
                                TotalGb = ready ? d.TotalSize / (1024.0 * 1024.0 * 1024.0) : 0.0
                            };
                        }
                        catch (System.Exception __suppressedEx14) { System.Diagnostics.Debug.WriteLine($"[SystemPerformanceTelemetryService] Non-fatal error ignored: {__suppressedEx14.Message}"); }
                    }
                    else
                    {
                        mappedNetworkPaths.TryGetValue(letter, out string? rem);
                        networkCandidates[letter] = (d, rem ?? "");
                    }
                }
            }
            catch (System.Exception __suppressedEx15) { System.Diagnostics.Debug.WriteLine($"[SystemPerformanceTelemetryService] Non-fatal error ignored: {__suppressedEx15.Message}"); }

            // Include any HKCU\Network drive letters that Windows hasn't instantiated in DriveInfo.GetDrives() yet
            foreach (var kvp in mappedNetworkPaths)
            {
                if (!resultsByLetter.ContainsKey(kvp.Key) && !networkCandidates.ContainsKey(kvp.Key))
                {
                    DriveInfo? di = null;
                    try { di = new DriveInfo(kvp.Key + "\\"); } catch (System.Exception __suppressedEx16) { System.Diagnostics.Debug.WriteLine($"[SystemPerformanceTelemetryService] Non-fatal error ignored: {__suppressedEx16.Message}"); }
                    networkCandidates[kvp.Key] = (di, kvp.Value);
                }
            }

            // 3. Probe all Network/NAS drives concurrently with fast TCP :445 preflight + 1.2s bounded UNC guard
            var netTasks = networkCandidates.Select(async kvp =>
            {
                string letter = kvp.Key;
                var (driveInfo, remotePath) = kvp.Value;

                var offlineFallback = new DiskDriveMetricItem
                {
                    DriveLetter = letter,
                    DriveType = "Network",
                    IsNetwork = true,
                    IsReady = false,
                    RemotePath = remotePath,
                    VolumeLabel = "",
                    FreeGb = 0.0,
                    TotalGb = 0.0
                };

                if (!string.IsNullOrWhiteSpace(remotePath) && remotePath.StartsWith(@"\\", StringComparison.Ordinal))
                {
                    bool tcpOk = await RemoteNodeDeploymentService.IsUncHostReachableFastAsync(remotePath, null, 900, ct).ConfigureAwait(false);
                    if (!tcpOk)
                        return offlineFallback;
                }

                string guardKey = !string.IsNullOrWhiteSpace(remotePath) ? remotePath : $@"\\LOCAL-DRIVE-PROBE\{letter}";
                return await RemoteNodeDeploymentService.RunBoundedUncOperationAsync(
                    guardKey,
                    () =>
                    {
                        try
                        {
                            var di = driveInfo ?? new DriveInfo(letter + "\\");
                            bool ready = di.IsReady;
                            if (!ready)
                                return offlineFallback;

                            return new DiskDriveMetricItem
                            {
                                DriveLetter = letter,
                                DriveType = "Network",
                                IsNetwork = true,
                                IsReady = true,
                                RemotePath = remotePath,
                                VolumeLabel = SafeReadString(() => di.VolumeLabel),
                                FreeGb = di.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0),
                                TotalGb = di.TotalSize / (1024.0 * 1024.0 * 1024.0)
                            };
                        }
                        catch
                        {
                            return offlineFallback;
                        }
                    },
                    fallbackOnTimeoutOrBusy: offlineFallback,
                    timeoutMs: 1200,
                    cooldownSeconds: 20,
                    ct: ct).ConfigureAwait(false);
            });

            var probedNetworkDrives = await Task.WhenAll(netTasks).ConfigureAwait(false);
            foreach (var item in probedNetworkDrives)
            {
                resultsByLetter[item.DriveLetter] = item;
            }

            return resultsByLetter.Values
                .OrderBy(d => d.DriveLetter, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string SafeReadString(Func<string> fn)
        {
            try { return fn() ?? ""; }
            catch { return ""; }
        }

        /// <summary>
        /// Instantly returns the cached local performance stats (0ms, non-blocking for UI thread).
        /// </summary>
        public SystemPerformanceStats GetLocalPerformanceStats()
        {
            return _cachedLocalStats;
        }

        /// <summary>
        /// Reads remote machine telemetry from heartbeat file over network share or falls back to UNC root drive space.
        /// </summary>
        public async Task<SystemPerformanceStats?> GetRemotePerformanceStatsAsync(RemoteNodeConfig node, CancellationToken ct = default)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.RemoteAppPath))
                return null;

            return await Task.Run(() =>
            {
                try
                {
                    string targetDir = node.RemoteAppPath;
                    if (!Directory.Exists(targetDir))
                    {
                        node.IsReachable = false;
                        return null;
                    }

                    node.IsReachable = true;

                    // 1. Try reading logs\telemetry_heartbeat.json first, then root telemetry_heartbeat.json
                    string logsHeartbeat = Path.Combine(targetDir, "logs", "telemetry_heartbeat.json");
                    string rootHeartbeat = Path.Combine(targetDir, "telemetry_heartbeat.json");
                    string activeHeartbeat = File.Exists(logsHeartbeat) ? logsHeartbeat : (File.Exists(rootHeartbeat) ? rootHeartbeat : "");

                    if (!string.IsNullOrEmpty(activeHeartbeat))
                    {
                        try
                        {
                            using var fs = new FileStream(activeHeartbeat, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            using var sr = new StreamReader(fs, System.Text.Encoding.UTF8);
                            string json = sr.ReadToEnd();
                            var hb = JsonSerializer.Deserialize<SystemPerformanceStats>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            if (hb != null)
                            {
                                // Fresh heartbeat within 90 seconds
                                if ((DateTime.UtcNow - hb.Timestamp).TotalSeconds < 90)
                                {
                                    node.LastStats = hb;
                                    node.StatusMessage = $"Online ({hb.HostMachineName}) — Updated {hb.Timestamp.ToLocalTime():HH:mm:ss}";
                                    return hb;
                                }
                                else
                                {
                                    node.StatusMessage = $"Heartbeat stale ({hb.Timestamp.ToLocalTime():HH:mm:ss})";
                                }
                            }
                        }
                        catch (System.Exception __suppressedEx17) { System.Diagnostics.Debug.WriteLine($"[SystemPerformanceTelemetryService] Non-fatal error ignored: {__suppressedEx17.Message}"); }
                    }

                    // 2. Fallback: inspect remote UNC root drive space
                    var fallback = new SystemPerformanceStats
                    {
                        Timestamp = DateTime.UtcNow,
                        HostMachineName = node.Name
                    };

                    try
                    {
                        string? root = Path.GetPathRoot(targetDir);
                        if (!string.IsNullOrEmpty(root))
                        {
                            var drive = new DriveInfo(root);
                            if (drive.IsReady)
                            {
                                fallback.DiskDriveLetter = drive.Name.TrimEnd('\\');
                                fallback.DiskFreeGb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                                fallback.DiskTotalGb = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                                fallback.Disks.Add(new DiskDriveMetricItem
                                {
                                    DriveLetter = fallback.DiskDriveLetter,
                                    FreeGb = fallback.DiskFreeGb,
                                    TotalGb = fallback.DiskTotalGb
                                });
                            }
                        }
                    }
                    catch (System.Exception __suppressedEx18) { System.Diagnostics.Debug.WriteLine($"[SystemPerformanceTelemetryService] Non-fatal error ignored: {__suppressedEx18.Message}"); }

                    node.LastStats = fallback;
                    return fallback;
                }
                catch
                {
                    node.IsReachable = false;
                    return null;
                }
            }, ct);
        }

        public void Dispose()
        {
            _disposed = true;
            Stop();
        }
    }
}
