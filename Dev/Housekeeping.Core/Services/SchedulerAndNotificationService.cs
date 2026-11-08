using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Housekeeping.Models;

namespace Housekeeping.Services
{
    public class SchedulerAndNotificationService
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
        private readonly Func<AppConfig> _getConfig;
        private readonly Action<string, string> _log;
        private readonly Action<string, string, bool> _showTrayNotification;
        private DateTime _lastMasterScheduleTriggerDate = DateTime.MinValue;
        private readonly HashSet<string> _firedSlots = new();
        private readonly Dictionary<string, HashSet<string>> _queueFiredSlots = new();
        private readonly Dictionary<string, DateTime> _queueLastTriggerDate = new();
        private readonly Dictionary<string, string> _queueDeferredSlots = new();
        private volatile bool _cachedIsGuardedRunning;
        private volatile string? _cachedGuardedProcessName;
        private DateTime _lastGuardScanUtc = DateTime.MinValue;
        private int _isScanningGuardedProcesses;

        /// <summary>
        /// External coordinator deferral timestamp (e.g., set by Mother v1.0 disk arbitration).
        /// When set and DateTime.UtcNow &lt; DeferredUntilUtc, all background schedule executions are held.
        /// </summary>
        public DateTime? DeferredUntilUtc { get; private set; }

        public void DeferSchedules(TimeSpan duration)
        {
            var target = DateTime.UtcNow.Add(duration);
            if (!DeferredUntilUtc.HasValue || target > DeferredUntilUtc.Value)
            {
                DeferredUntilUtc = target;
            }
        }

        public void ClearDeferral()
        {
            DeferredUntilUtc = null;
        }

        public static List<TimeSpan> ParseTimeList(string? csv)
        {
            var result = new List<TimeSpan>();
            if (string.IsNullOrWhiteSpace(csv)) return result;

            var parts = csv.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (TimeSpan.TryParse(part.Trim(), out var ts))
                {
                    if (ts >= TimeSpan.Zero && ts < TimeSpan.FromDays(1) && !result.Contains(ts))
                    {
                        result.Add(ts);
                    }
                }
            }
            return result.OrderBy(t => t).ToList();
        }

        public SchedulerAndNotificationService(
            Func<AppConfig> getConfig,
            Action<string, string> log,
            Action<string, string, bool> showTrayNotification)
        {
            _getConfig = getConfig;
            _log = log;
            _showTrayNotification = showTrayNotification;
        }

        /// <summary>
        /// Non-blocking UI-thread getter for guarded process state; refreshes the OS process snapshot asynchronously in the background.
        /// </summary>
        public bool GetCachedGuardedProcessRunning(out string? activeProcessName)
        {
            var cfg = _getConfig();
            if (!cfg.EnableProcessGuard || cfg.GuardedProcessNames.Count == 0)
            {
                activeProcessName = null;
                return false;
            }

            if ((DateTime.UtcNow - _lastGuardScanUtc).TotalSeconds >= 8.0 &&
                System.Threading.Interlocked.CompareExchange(ref _isScanningGuardedProcesses, 1, 0) == 0)
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        bool running = IsGuardedProcessRunning(out var detected);
                        _cachedGuardedProcessName = detected;
                        _cachedIsGuardedRunning = running;
                        _lastGuardScanUtc = DateTime.UtcNow;
                    }
                    catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[SchedulerAndNotificationService] Non-fatal error ignored: {__suppressedEx1.Message}"); }
                    finally
                    {
                        System.Threading.Interlocked.Exchange(ref _isScanningGuardedProcesses, 0);
                    }
                });
            }

            activeProcessName = _cachedGuardedProcessName;
            return _cachedIsGuardedRunning;
        }

        public bool IsGuardedProcessRunning(out string? activeProcessName)
        {
            activeProcessName = null;
            var cfg = _getConfig();
            if (!cfg.EnableProcessGuard || cfg.GuardedProcessNames.Count == 0)
                return false;

            foreach (var name in cfg.GuardedProcessNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                string clean = name.Trim();
                if (clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    clean = clean.Substring(0, clean.Length - 4);

                try
                {
                    var procs = Process.GetProcessesByName(clean);
                    try
                    {
                        if (procs.Length > 0)
                        {
                            activeProcessName = clean;
                            return true;
                        }
                    }
                    finally
                    {
                        foreach (var p in procs)
                        {
                            try { p.Dispose(); } catch (System.Exception __suppressedEx2) { System.Diagnostics.Debug.WriteLine($"[SchedulerAndNotificationService] Non-fatal error ignored: {__suppressedEx2.Message}"); }
                        }
                    }
                }
                catch (System.Exception __suppressedEx3) { System.Diagnostics.Debug.WriteLine($"[SchedulerAndNotificationService] Non-fatal error ignored: {__suppressedEx3.Message}"); }
            }

            return false;
        }

        public DateTime? GetNextOccurrenceUtc(NamedJobQueue? queue, DateTime nowUtc)
        {
            if (queue == null || !queue.EnableSchedule)
                return null;

            var nowLocal = nowUtc.ToLocalTime();

            switch (queue.ScheduleMode)
            {
                case MasterScheduleMode.IntervalCadence:
                {
                    int interval = Math.Max(5, queue.ScheduleIntervalMinutes);
                    if (!queue.LastRunUtc.HasValue)
                        return nowUtc.AddMinutes(interval);

                    var nextUtc = queue.LastRunUtc.Value.AddMinutes(interval);
                    return nextUtc < nowUtc ? nowUtc : nextUtc;
                }

                case MasterScheduleMode.SpecificTimes:
                {
                    var times = ParseTimeList(queue.ScheduleSpecificTimesCsv);
                    if (times.Count == 0)
                        return null;

                    for (int offset = 0; offset < 8; offset++)
                    {
                        var candidateDate = nowLocal.Date.AddDays(offset);
                        if (queue.ScheduleDays != null && !queue.ScheduleDays.Contains(candidateDate.DayOfWeek))
                            continue;

                        foreach (var targetTime in times.OrderBy(t => t))
                        {
                            var candidateDateTime = candidateDate.Add(targetTime);
                            if (candidateDateTime > nowLocal)
                            {
                                return candidateDateTime.ToUniversalTime();
                            }
                        }
                    }
                    return null;
                }

                case MasterScheduleMode.DailyTime:
                default:
                {
                    if (TimeSpan.TryParse(queue.ScheduleDailyTime, out var targetTime))
                    {
                        for (int offset = 0; offset < 8; offset++)
                        {
                            var candidateDate = nowLocal.Date.AddDays(offset);
                            if (queue.ScheduleDays != null && !queue.ScheduleDays.Contains(candidateDate.DayOfWeek))
                                continue;

                            var candidateDateTime = candidateDate.Add(targetTime);
                            if (candidateDateTime > nowLocal)
                            {
                                return candidateDateTime.ToUniversalTime();
                            }
                        }
                    }
                    return null;
                }
            }
        }

        public DateTime? GetMasterScheduleNextOccurrenceUtc(AppConfig? cfg, DateTime nowUtc)
        {
            if (cfg == null || !cfg.EnableMasterSchedule)
                return null;

            var nowLocal = nowUtc.ToLocalTime();

            switch (cfg.MasterScheduleMode)
            {
                case MasterScheduleMode.IntervalCadence:
                {
                    int interval = Math.Max(5, cfg.MasterScheduleIntervalMinutes);
                    if (!cfg.LastMasterScheduleRunUtc.HasValue)
                        return nowUtc.AddMinutes(interval);

                    var nextUtc = cfg.LastMasterScheduleRunUtc.Value.AddMinutes(interval);
                    return nextUtc < nowUtc ? nowUtc : nextUtc;
                }

                case MasterScheduleMode.SpecificTimes:
                {
                    var times = ParseTimeList(cfg.MasterScheduleSpecificTimesCsv);
                    if (times.Count == 0)
                        return null;

                    for (int offset = 0; offset < 8; offset++)
                    {
                        var candidateDate = nowLocal.Date.AddDays(offset);
                        if (cfg.MasterScheduleDays != null && !cfg.MasterScheduleDays.Contains(candidateDate.DayOfWeek))
                            continue;

                        foreach (var targetTime in times)
                        {
                            var candidateDateTime = candidateDate.Add(targetTime);
                            if (candidateDateTime > nowLocal)
                            {
                                return candidateDateTime.ToUniversalTime();
                            }
                        }
                    }
                    return null;
                }

                case MasterScheduleMode.DailyTime:
                default:
                {
                    if (TimeSpan.TryParse(cfg.MasterScheduleTime, out var targetTime))
                    {
                        for (int offset = 0; offset < 8; offset++)
                        {
                            var candidateDate = nowLocal.Date.AddDays(offset);
                            if (cfg.MasterScheduleDays != null && !cfg.MasterScheduleDays.Contains(candidateDate.DayOfWeek))
                                continue;

                            var candidateDateTime = candidateDate.Add(targetTime);
                            if (candidateDateTime > nowLocal)
                            {
                                return candidateDateTime.ToUniversalTime();
                            }
                        }
                    }
                    return null;
                }
            }
        }

        public (DateTime? NextRunUtc, string StatusText, string? QueueName) GetGlobalNextScheduleStatus(
            AppConfig? cfg,
            DateTime? nowUtcOverride = null)
        {
            if (cfg == null)
                return (null, "Schedule: Disabled", null);

            var nowUtc = nowUtcOverride ?? DateTime.UtcNow;
            DateTime? earliestUtc = null;
            string? earliestName = null;

            if (cfg.NamedQueues != null)
            {
                foreach (var q in cfg.NamedQueues)
                {
                    if (q == null || !q.EnableSchedule)
                        continue;

                    var next = GetNextOccurrenceUtc(q, nowUtc);
                    if (next.HasValue)
                    {
                        if (!earliestUtc.HasValue || next.Value < earliestUtc.Value)
                        {
                            earliestUtc = next.Value;
                            earliestName = q.Name;
                        }
                    }
                }
            }

            if (cfg.EnableMasterSchedule)
            {
                var masterNext = GetMasterScheduleNextOccurrenceUtc(cfg, nowUtc);
                if (masterNext.HasValue && (!earliestUtc.HasValue || masterNext.Value < earliestUtc.Value))
                {
                    earliestUtc = masterNext.Value;
                    earliestName = "Master Schedule";
                }
            }

            if (!earliestUtc.HasValue || string.IsNullOrEmpty(earliestName))
                return (null, "Schedule: Disabled", null);

            var rem = earliestUtc.Value - nowUtc;
            string timeDesc;
            if (rem <= TimeSpan.Zero)
            {
                timeDesc = "Due now";
            }
            else if (rem.TotalMinutes < 1)
            {
                timeDesc = "in < 1m";
            }
            else if (rem.TotalHours < 1)
            {
                timeDesc = $"in {(int)Math.Ceiling(rem.TotalMinutes)}m";
            }
            else if (rem.TotalHours < 24)
            {
                timeDesc = $"in {(int)rem.TotalHours}h {rem.Minutes}m";
            }
            else
            {
                var localNext = earliestUtc.Value.ToLocalTime();
                timeDesc = localNext.ToString("ddd HH:mm");
            }

            string status = $"Next Run: {timeDesc} ({earliestName})";
            return (earliestUtc, status, earliestName);
        }

        public string GetNextScheduleStatusText(NamedJobQueue? queue)
        {
            if (queue == null || !queue.EnableSchedule)
                return "Schedule: Disabled";

            var nowUtc = DateTime.UtcNow;
            var nextUtc = GetNextOccurrenceUtc(queue, nowUtc);
            if (!nextUtc.HasValue)
                return "Schedule: No upcoming runs";

            var rem = nextUtc.Value - nowUtc;
            if (rem <= TimeSpan.Zero)
                return "Next Run: Due now";

            if (queue.ScheduleMode == MasterScheduleMode.IntervalCadence)
            {
                if (rem.TotalMinutes < 1)
                    return "Next Run: in < 1m (Interval)";
                if (rem.TotalHours < 1)
                    return $"Next Run: in {(int)Math.Ceiling(rem.TotalMinutes)}m (Interval)";
                return $"Next Run: in {(int)rem.TotalHours}h {rem.Minutes}m (Interval)";
            }

            if (rem.TotalHours < 24)
            {
                var localTime = nextUtc.Value.ToLocalTime();
                return $"Next Run: {localTime:ddd HH:mm} (in {(int)rem.TotalHours}h {rem.Minutes}m)";
            }

            var localDate = nextUtc.Value.ToLocalTime();
            return $"Next Run: {localDate:ddd MMM d, HH:mm}";
        }

        public bool ShouldTriggerQueueNow(NamedJobQueue queue, DateTime now, bool isDeferredByGuard = false)
        {
            if (queue == null || !queue.EnableSchedule)
                return false;

            if (DeferredUntilUtc.HasValue && DateTime.UtcNow < DeferredUntilUtc.Value)
                return false;

            DateTime nowUtc;
            DateTime nowLocal;
            if (now.Kind == DateTimeKind.Utc)
            {
                nowUtc = now;
                nowLocal = now.ToLocalTime();
            }
            else if (now.Kind == DateTimeKind.Local)
            {
                // Preserve .NET's internal s_kindLocalAmbiguous bit during DST fall-back hours
                nowLocal = now;
                nowUtc = now.ToUniversalTime();
            }
            else
            {
                nowLocal = DateTime.SpecifyKind(now, DateTimeKind.Local);
                nowUtc = nowLocal.ToUniversalTime();
            }

            if (queue.ScheduleDays != null && !queue.ScheduleDays.Contains(nowLocal.DayOfWeek))
                return false;

            string qid = queue.Id;
            if (!_queueFiredSlots.TryGetValue(qid, out var firedSet))
            {
                firedSet = new HashSet<string>();
                _queueFiredSlots[qid] = firedSet;
            }

            if (!_queueLastTriggerDate.TryGetValue(qid, out var lastDate) || lastDate.Date != nowLocal.Date)
            {
                firedSet.Clear();
                _queueDeferredSlots.Remove(qid);
                _queueLastTriggerDate[qid] = nowLocal.Date;
            }

            // If a DailyTime/SpecificTimes slot was deferred earlier today due to Process Guard and guard is now clear, fire it!
            if (!isDeferredByGuard &&
                _queueDeferredSlots.TryGetValue(qid, out var deferredSlot) &&
                deferredSlot.StartsWith($"{nowLocal:yyyyMMdd}_", StringComparison.Ordinal))
            {
                _queueDeferredSlots.Remove(qid);
                if (!firedSet.Contains(deferredSlot))
                {
                    firedSet.Add(deferredSlot);
                    queue.LastRunUtc = nowUtc;
                    return true;
                }
            }

            switch (queue.ScheduleMode)
            {
                case MasterScheduleMode.IntervalCadence:
                {
                    int intervalMinutes = Math.Max(5, queue.ScheduleIntervalMinutes);
                    if (!queue.LastRunUtc.HasValue)
                    {
                        queue.LastRunUtc = nowUtc;
                        return false;
                    }

                    var rawLast = queue.LastRunUtc.Value;
                    var lastUtc = rawLast.Kind == DateTimeKind.Utc
                        ? rawLast
                        : (rawLast.Kind == DateTimeKind.Local
                            ? rawLast.ToUniversalTime()
                            : DateTime.SpecifyKind(rawLast, DateTimeKind.Utc));
                    var elapsed = nowUtc - lastUtc;
                    if (elapsed.TotalMinutes >= intervalMinutes)
                    {
                        if (isDeferredByGuard)
                        {
                            // Do NOT advance LastRunUtc while deferred by guarded process so it triggers once cleared
                            return false;
                        }
                        queue.LastRunUtc = nowUtc;
                        return true;
                    }
                    return false;
                }

                case MasterScheduleMode.SpecificTimes:
                {
                    var times = ParseTimeList(queue.ScheduleSpecificTimesCsv);
                    foreach (var targetTime in times)
                    {
                        string slotKey = $"{nowLocal:yyyyMMdd}_{targetTime:hh\\:mm}";
                        if (firedSet.Contains(slotKey))
                            continue;

                        var diff = nowLocal.TimeOfDay - targetTime;
                        if (diff.TotalMinutes >= 0 && diff.TotalMinutes < 2.5)
                        {
                            if (isDeferredByGuard)
                            {
                                _queueDeferredSlots[qid] = slotKey;
                                return false;
                            }
                            firedSet.Add(slotKey);
                            queue.LastRunUtc = nowUtc;
                            return true;
                        }
                    }
                    return false;
                }

                case MasterScheduleMode.DailyTime:
                default:
                {
                    string dailyKey = $"{nowLocal:yyyyMMdd}_daily";
                    if (firedSet.Contains(dailyKey))
                        return false;

                    if (TimeSpan.TryParse(queue.ScheduleDailyTime, out var targetTime))
                    {
                        var diff = nowLocal.TimeOfDay - targetTime;
                        if (diff.TotalMinutes >= 0 && diff.TotalMinutes < 2.5)
                        {
                            if (isDeferredByGuard)
                            {
                                _queueDeferredSlots[qid] = dailyKey;
                                return false;
                            }
                            firedSet.Add(dailyKey);
                            queue.LastRunUtc = nowUtc;
                            return true;
                        }
                    }
                    return false;
                }
            }
        }

        public string GetNextScheduleStatusText()
        {
            var cfg = _getConfig();
            if (!cfg.EnableMasterSchedule)
                return "Schedule: Disabled";

            var now = DateTime.Now;

            switch (cfg.MasterScheduleMode)
            {
                case MasterScheduleMode.IntervalCadence:
                {
                    int interval = Math.Max(5, cfg.MasterScheduleIntervalMinutes);
                    if (!cfg.LastMasterScheduleRunUtc.HasValue)
                        return $"Next Run: in {interval}m (Interval)";

                    var nextUtc = cfg.LastMasterScheduleRunUtc.Value.AddMinutes(interval);
                    var rem = nextUtc - DateTime.UtcNow;
                    if (rem <= TimeSpan.Zero)
                        return "Next Run: Due now (Interval)";
                    if (rem.TotalHours < 1)
                        return $"Next Run: in {Math.Ceiling(rem.TotalMinutes)}m (Interval)";
                    return $"Next Run: in {(int)rem.TotalHours}h {rem.Minutes}m (Interval)";
                }

                case MasterScheduleMode.SpecificTimes:
                {
                    var times = ParseTimeList(cfg.MasterScheduleSpecificTimesCsv);
                    if (times.Count == 0)
                        return "Schedule: Specific Times (None set)";

                    for (int offset = 0; offset < 8; offset++)
                    {
                        var candidateDate = now.Date.AddDays(offset);
                        if (!cfg.MasterScheduleDays.Contains(candidateDate.DayOfWeek))
                            continue;

                        foreach (var targetTime in times)
                        {
                            var candidateDateTime = candidateDate.Add(targetTime);
                            if (candidateDateTime > now)
                            {
                                var diff = candidateDateTime - now;
                                if (diff.TotalHours < 24)
                                    return $"Next Run: {candidateDateTime:ddd HH:mm} (in {(int)diff.TotalHours}h {diff.Minutes}m)";
                                return $"Next Run: {candidateDateTime:ddd MMM d, HH:mm}";
                            }
                        }
                    }
                    return "Schedule: Specific Times";
                }

                case MasterScheduleMode.DailyTime:
                default:
                {
                    if (TimeSpan.TryParse(cfg.MasterScheduleTime, out var targetTime))
                    {
                        for (int offset = 0; offset < 8; offset++)
                        {
                            var candidateDate = now.Date.AddDays(offset);
                            if (!cfg.MasterScheduleDays.Contains(candidateDate.DayOfWeek))
                                continue;

                            var candidateDateTime = candidateDate.Add(targetTime);
                            if (candidateDateTime > now)
                            {
                                var diff = candidateDateTime - now;
                                if (diff.TotalHours < 24)
                                    return $"Next Run: {candidateDateTime:ddd HH:mm} (in {(int)diff.TotalHours}h {diff.Minutes}m)";
                                return $"Next Run: {candidateDateTime:ddd MMM d, HH:mm}";
                            }
                        }
                    }
                    return $"Schedule: {cfg.MasterScheduleTime}";
                }
            }
        }

        public bool ShouldTriggerMasterScheduleNow()
        {
            var cfg = _getConfig();
            if (!cfg.EnableMasterSchedule)
                return false;

            if (DeferredUntilUtc.HasValue && DateTime.UtcNow < DeferredUntilUtc.Value)
                return false;

            var now = DateTime.Now;
            if (!cfg.MasterScheduleDays.Contains(now.DayOfWeek))
                return false;

            if (_lastMasterScheduleTriggerDate.Date != now.Date)
            {
                _firedSlots.Clear();
                _lastMasterScheduleTriggerDate = now.Date;
            }

            switch (cfg.MasterScheduleMode)
            {
                case MasterScheduleMode.IntervalCadence:
                {
                    int intervalMinutes = Math.Max(5, cfg.MasterScheduleIntervalMinutes);
                    if (!cfg.LastMasterScheduleRunUtc.HasValue)
                    {
                        cfg.LastMasterScheduleRunUtc = DateTime.UtcNow;
                        return false;
                    }

                    var elapsed = DateTime.UtcNow - cfg.LastMasterScheduleRunUtc.Value;
                    if (elapsed.TotalMinutes >= intervalMinutes)
                    {
                        cfg.LastMasterScheduleRunUtc = DateTime.UtcNow;
                        return true;
                    }
                    return false;
                }

                case MasterScheduleMode.SpecificTimes:
                {
                    var times = ParseTimeList(cfg.MasterScheduleSpecificTimesCsv);
                    foreach (var targetTime in times)
                    {
                        string slotKey = $"{now:yyyyMMdd}_{targetTime:hh\\:mm}";
                        if (_firedSlots.Contains(slotKey))
                            continue;

                        var diff = now.TimeOfDay - targetTime;
                        if (diff.TotalMinutes >= 0 && diff.TotalMinutes < 2.5)
                        {
                            _firedSlots.Add(slotKey);
                            cfg.LastMasterScheduleRunUtc = DateTime.UtcNow;
                            return true;
                        }
                    }
                    return false;
                }

                case MasterScheduleMode.DailyTime:
                default:
                {
                    string dailyKey = $"{now:yyyyMMdd}_daily";
                    if (_firedSlots.Contains(dailyKey))
                        return false;

                    if (TimeSpan.TryParse(cfg.MasterScheduleTime, out var targetTime))
                    {
                        var diff = now.TimeOfDay - targetTime;
                        if (diff.TotalMinutes >= 0 && diff.TotalMinutes < 2.5)
                        {
                            _firedSlots.Add(dailyKey);
                            cfg.LastMasterScheduleRunUtc = DateTime.UtcNow;
                            return true;
                        }
                    }
                    return false;
                }
            }
        }

        public List<ModuleId> GetPerModuleSchedulesDueNow(DateTime? nowOverride = null)
        {
            var due = new List<ModuleId>();
            if (DeferredUntilUtc.HasValue && DateTime.UtcNow < DeferredUntilUtc.Value)
                return due;

            var cfg = _getConfig();

            DateTime nowUtc;
            DateTime nowLocal;
            if (!nowOverride.HasValue)
            {
                nowUtc = DateTime.UtcNow;
                nowLocal = DateTime.Now;
            }
            else if (nowOverride.Value.Kind == DateTimeKind.Utc)
            {
                nowUtc = nowOverride.Value;
                nowLocal = nowOverride.Value.ToLocalTime();
            }
            else if (nowOverride.Value.Kind == DateTimeKind.Local)
            {
                nowLocal = nowOverride.Value;
                nowUtc = nowOverride.Value.ToUniversalTime();
            }
            else
            {
                nowLocal = DateTime.SpecifyKind(nowOverride.Value, DateTimeKind.Local);
                nowUtc = nowLocal.ToUniversalTime();
            }

            CheckModule(ModuleId.ProxyGenerator, cfg.ProxyGenerator, nowUtc, nowLocal, due);
            CheckModule(ModuleId.ArchiveCompressor, cfg.ArchiveCompressor, nowUtc, nowLocal, due);
            CheckModule(ModuleId.MediaTranscoder, cfg.MediaTranscoder, nowUtc, nowLocal, due);
            CheckModule(ModuleId.RomLauncherGenerator, cfg.RomLauncherGenerator, nowUtc, nowLocal, due);
            CheckModule(ModuleId.CloudSync, cfg.CloudSync, nowUtc, nowLocal, due);
            CheckModule(ModuleId.HumbleSync, cfg.HumbleSync, nowUtc, nowLocal, due);
            CheckModule(ModuleId.CustomScripts, cfg.CustomScripts, nowUtc, nowLocal, due);
            CheckModule(ModuleId.CollectionManifest, cfg.CollectionManifest, nowUtc, nowLocal, due);

            return due;
        }

        private static void CheckModule(ModuleId id, ModuleBaseConfig modCfg, DateTime nowUtc, DateTime nowLocal, List<ModuleId> due)
        {
            if (!modCfg.EnableCustomSchedule)
                return;

            if (modCfg.ScheduleIntervalMinutes > 0)
            {
                if (!modCfg.LastScheduledRunUtc.HasValue)
                {
                    modCfg.LastScheduledRunUtc = nowUtc;
                    return;
                }

                var elapsed = nowUtc - modCfg.LastScheduledRunUtc.Value;
                if (elapsed.TotalMinutes >= modCfg.ScheduleIntervalMinutes)
                {
                    modCfg.LastScheduledRunUtc = nowUtc;
                    due.Add(id);
                }
            }
            else if (TimeSpan.TryParse(modCfg.ScheduleDailyTime, out var dailyTime))
            {
                var lastLocalDate = modCfg.LastScheduledRunUtc?.ToLocalTime().Date ?? DateTime.MinValue;
                if (lastLocalDate != nowLocal.Date)
                {
                    var diff = nowLocal.TimeOfDay - dailyTime;
                    if (diff.TotalMinutes >= 0 && diff.TotalMinutes < 2.5)
                    {
                        modCfg.LastScheduledRunUtc = nowUtc;
                        due.Add(id);
                    }
                }
            }
        }

        public string GetMachineIdentifier()
        {
            var cfg = _getConfig();
            return !string.IsNullOrWhiteSpace(cfg.DiscordCustomMachineName)
                ? cfg.DiscordCustomMachineName.Trim()
                : Environment.MachineName;
        }

        public async Task NotifyRunCompletedAsync(RunHistoryEntry entry)
        {
            var cfg = _getConfig();
            string machine = GetMachineIdentifier();
            string title = $"Queue Finished — {entry.Status}";
            string summary = $"{entry.ModulesSummary} | Processed: {entry.ItemsProcessed}, Skipped: {entry.ItemsSkipped}, Errors: {entry.ErrorsCount} ({entry.DurationText})";

            if (cfg.EnableToastNotifications)
            {
                _showTrayNotification($"Housekeeping [{machine}] — {entry.Status}", summary, entry.ErrorsCount > 0);
            }

            if (cfg.EnableDiscordWebhook && cfg.DiscordNotifyQueueFinish && !string.IsNullOrWhiteSpace(cfg.DiscordWebhookUrl))
            {
                var fields = new List<object>
                {
                    new { name = "Status", value = entry.Status, inline = true },
                    new { name = "Duration", value = entry.DurationText, inline = true },
                    new { name = "Processed", value = entry.ItemsProcessed.ToString(), inline = true },
                    new { name = "Skipped", value = entry.ItemsSkipped.ToString(), inline = true },
                    new { name = "Errors", value = entry.ErrorsCount.ToString(), inline = true },
                    new { name = "Host Machine", value = machine, inline = true }
                };

                int color = entry.ErrorsCount > 0 ? 0xE74C3C : 0x2ECC71;
                await SendDiscordWebhookAsync(cfg.DiscordWebhookUrl, title, summary, entry.ErrorsCount > 0, color, fields);
            }
        }

        public async Task NotifyQueueStartedAsync(string queueName, int jobCount, bool isDryRun)
        {
            var cfg = _getConfig();
            if (!cfg.EnableDiscordWebhook || !cfg.DiscordNotifyQueueStart || string.IsNullOrWhiteSpace(cfg.DiscordWebhookUrl))
                return;

            string machine = GetMachineIdentifier();
            string title = $"Queue Started: '{queueName}'";
            string desc = $"Beginning execution on **{machine}** with {jobCount} job(s) in queue{(isDryRun ? " [DRY RUN]" : "")}.";

            var fields = new List<object>
            {
                new { name = "Queue", value = queueName, inline = true },
                new { name = "Job Count", value = jobCount.ToString(), inline = true },
                new { name = "Host Machine", value = machine, inline = true }
            };

            await SendDiscordWebhookAsync(cfg.DiscordWebhookUrl, title, desc, false, 0x3498DB, fields);
        }

        public async Task NotifyJobStartedAsync(string jobName, string moduleType, string queueName)
        {
            var cfg = _getConfig();
            if (!cfg.EnableDiscordWebhook || !cfg.DiscordNotifyJobStart || string.IsNullOrWhiteSpace(cfg.DiscordWebhookUrl))
                return;

            string machine = GetMachineIdentifier();
            string title = $"Job Started: '{jobName}'";
            string desc = $"Module: **{moduleType}** | Queue: **{queueName}** on {machine}";

            await SendDiscordWebhookAsync(cfg.DiscordWebhookUrl, title, desc, false, 0x1ABC9C);
        }

        public async Task NotifyJobFinishedAsync(string jobName, string moduleType, int processed, int skipped, int errors, TimeSpan duration)
        {
            var cfg = _getConfig();
            if (!cfg.EnableDiscordWebhook || !cfg.DiscordNotifyJobFinish || string.IsNullOrWhiteSpace(cfg.DiscordWebhookUrl))
                return;

            string machine = GetMachineIdentifier();
            string title = $"Job Finished: '{jobName}'";
            string desc = $"Module: **{moduleType}** | Duration: {duration:mm\\:ss}";

            var fields = new List<object>
            {
                new { name = "Processed", value = processed.ToString(), inline = true },
                new { name = "Skipped", value = skipped.ToString(), inline = true },
                new { name = "Errors", value = errors.ToString(), inline = true }
            };

            int color = errors > 0 ? 0xE67E22 : 0x2ECC71;
            await SendDiscordWebhookAsync(cfg.DiscordWebhookUrl, title, desc, errors > 0, color, fields);
        }

        public async Task NotifyTaskStartedAsync(string taskName, string commandSummary)
        {
            var cfg = _getConfig();
            if (!cfg.EnableDiscordWebhook || !cfg.DiscordNotifyTaskStart || string.IsNullOrWhiteSpace(cfg.DiscordWebhookUrl))
                return;

            string machine = GetMachineIdentifier();
            string title = $"Task Started: '{taskName}'";
            string desc = commandSummary.Length > 200 ? commandSummary[..200] + "..." : commandSummary;

            await SendDiscordWebhookAsync(cfg.DiscordWebhookUrl, title, desc, false, 0x9B59B6);
        }

        public async Task NotifyErrorAsync(string contextTitle, string errorMessage, string? queueOrJobName = null)
        {
            var cfg = _getConfig();
            if (!cfg.EnableDiscordWebhook || !cfg.DiscordNotifyErrors || string.IsNullOrWhiteSpace(cfg.DiscordWebhookUrl))
                return;

            string machine = GetMachineIdentifier();
            string title = $"Error Alert: {contextTitle}";
            string desc = errorMessage.Length > 500 ? errorMessage[..500] + "..." : errorMessage;

            var fields = new List<object>();
            if (!string.IsNullOrEmpty(queueOrJobName))
            {
                fields.Add(new { name = "Scope", value = queueOrJobName, inline = true });
            }
            fields.Add(new { name = "Host Machine", value = machine, inline = true });

            await SendDiscordWebhookAsync(cfg.DiscordWebhookUrl, title, desc, true, 0xE74C3C, fields);
        }

        public async Task SendDiscordWebhookAsync(
            string webhookUrl,
            string title,
            string description,
            bool isError,
            int? customColor = null,
            List<object>? fields = null)
        {
            try
            {
                string machine = GetMachineIdentifier();
                string fullTitle = $"[{machine}] {title}";
                int color = customColor ?? (isError ? 0xE74C3C : 0x2ECC71);

                var embedObj = new Dictionary<string, object>
                {
                    ["title"] = fullTitle,
                    ["description"] = description,
                    ["color"] = color,
                    ["timestamp"] = DateTime.UtcNow.ToString("o"),
                    ["footer"] = new { text = $"Host: {machine} • Housekeeping" }
                };

                if (fields != null && fields.Count > 0)
                {
                    embedObj["fields"] = fields;
                }

                var payload = new
                {
                    username = $"Housekeeping ({machine})",
                    embeds = new[] { embedObj }
                };

                string json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var resp = await Http.PostAsync(webhookUrl, content);
                if (!resp.IsSuccessStatusCode)
                    _log("WARN", $"Discord webhook returned HTTP {(int)resp.StatusCode}.");
            }
            catch (Exception ex)
            {
                _log("WARN", $"Failed sending Discord webhook: {ex.Message}");
            }
        }
    }
}
