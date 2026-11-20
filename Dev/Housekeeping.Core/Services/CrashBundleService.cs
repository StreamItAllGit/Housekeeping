using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Housekeeping.Services
{
    public static class CrashBundleService
    {
        public const int MaxRetainedCrashBundles = 20;
        public const int DefaultPrecedingLogLineCount = 50;

        private static readonly object SyncLock = new();
        private static readonly DateTime ProcessStartedAtUtc = DateTime.UtcNow;
        private static readonly JsonSerializerOptions BundleJsonOptions = new() { WriteIndented = true };

        private static readonly Regex UserProfileRegex = new(
            @"(?i)[A-Za-z]:[\\/]+Users[\\/]+(?!Public|Default)[^\\/""'\s:;]+",
            RegexOptions.Compiled);

        private static readonly Regex CliSecretRegex = new(
            @"(?i)(--(?:token|api-key|secret|password|cookie)\s+)([^\s""']+|""[^""]*""|'[^']*')",
            RegexOptions.Compiled);

        private static readonly Regex KeyValueSecretRegex = new(
            @"(?i)([""']?(?:SessionCookie|NodeApiToken|ApiKey|Token|Password|Secret)[""']?\s*[:=]\s*[""'])([^""']+)([""'])",
            RegexOptions.Compiled);

        private static readonly Regex StackFrameLocationRegex = new(
            @"\sin\s+(?<file>.+?):line\s+(?<line>\d+)",
            RegexOptions.Compiled);

        private static bool _handlersRegistered;
        private static Func<string>? _logsDirProvider;
        private static Func<TerminalLogBufferService?>? _logBufferProvider;
        private static string _buildVersion = CrashBundleExceptionFilter.DefaultFallbackVersion;
        private static bool _isolatedMode;
        private static string _archetype = "2-tray-companion";

        public static void RegisterGlobalHandlers(
            Func<string> getLogsDir,
            string buildVersion,
            bool isolatedMode,
            string archetype = "2-tray-companion",
            Func<TerminalLogBufferService?>? getLogBuffer = null)
        {
            lock (SyncLock)
            {
                _logsDirProvider = getLogsDir;
                _logBufferProvider = getLogBuffer;
                if (!string.IsNullOrWhiteSpace(buildVersion))
                    _buildVersion = buildVersion.Trim();
                _isolatedMode = isolatedMode;
                if (!string.IsNullOrWhiteSpace(archetype))
                    _archetype = archetype.Trim();

                if (_handlersRegistered)
                    return;

                _handlersRegistered = true;

                AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                {
                    try
                    {
                        var ex = e.ExceptionObject as Exception
                                 ?? new Exception($"Unhandled non-Exception object: {e.ExceptionObject}");
                        if (!e.IsTerminating && CrashBundleExceptionFilter.IsBenignBackgroundException(ex))
                        {
                            EmitBenignDisconnectWarning(ex, null, null, null);
                            return;
                        }
                        WriteCrashBundle(ex);
                    }
                    catch (Exception handlerEx)
                    {
                        Debug.WriteLine($"[CrashBundleService] UnhandledException handler error: {handlerEx.Message}");
                    }
                };

                TaskScheduler.UnobservedTaskException += (_, e) => HandleUnobservedTaskException(e);
            }
        }

        public static bool HandleUnobservedTaskException(
            UnobservedTaskExceptionEventArgs e,
            string? logsDirOverride = null,
            string? buildVersionOverride = null,
            TerminalLogBufferService? logBufferOverride = null)
        {
            if (e == null) return false;
            try
            {
                e.SetObserved();
                if (e.Exception == null || CrashBundleExceptionFilter.IsBenignBackgroundException(e.Exception))
                {
                    EmitBenignDisconnectWarning(e.Exception, logsDirOverride, buildVersionOverride, logBufferOverride);
                    return false;
                }

                string? written = WriteCrashBundle(
                    e.Exception.Flatten(),
                    logsDirOverride: logsDirOverride,
                    buildVersionOverride: buildVersionOverride,
                    logBufferOverride: logBufferOverride);
                return !string.IsNullOrWhiteSpace(written);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CrashBundleService] HandleUnobservedTaskException ignored: {ex.Message}");
                return false;
            }
        }

        private static void EmitBenignDisconnectWarning(
            Exception? ex,
            string? logsDirOverride,
            string? buildVersionOverride,
            TerminalLogBufferService? logBufferOverride)
        {
            string kind = CrashBundleExceptionFilter.DescribeTransientDisconnect(ex);
            string detail = SanitizeText(ex?.GetBaseException().Message);
            string warnMsg = $"[CrashBundleService] Suppressed transient background disconnect ({kind}): {detail}";
            Trace.TraceWarning(warnMsg);

            try
            {
                var logBuffer = logBufferOverride ?? _logBufferProvider?.Invoke();
                if (logBuffer != null)
                {
                    logBuffer.Append("WARN", warnMsg);
                    return;
                }

                string? logsDir = !string.IsNullOrWhiteSpace(logsDirOverride) ? logsDirOverride : _logsDirProvider?.Invoke();
                if (!string.IsNullOrWhiteSpace(logsDir))
                {
                    Directory.CreateDirectory(logsDir);
                    string version = !string.IsNullOrWhiteSpace(buildVersionOverride) ? buildVersionOverride.Trim() : _buildVersion;
                    if (string.IsNullOrWhiteSpace(version)) version = CrashBundleExceptionFilter.DefaultFallbackVersion;
                    string line = $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} [v{version}] [PID:{Environment.ProcessId}/T:{Environment.CurrentManagedThreadId}] [WARN] [CrashBundleService] {warnMsg}{Environment.NewLine}";
                    lock (SyncLock)
                    {
                        using var fs = new FileStream(Path.Combine(logsDir, "Housekeeping_Live.log"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096);
                        using var writer = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                        writer.Write(line);
                    }
                }
            }
            catch (Exception logEx)
            {
                Debug.WriteLine($"[CrashBundleService] Benign disconnect telemetry skipped: {logEx.Message}");
            }
        }

        public static string? WriteCrashBundle(
            Exception ex,
            string? logsDirOverride = null,
            string? buildVersionOverride = null,
            bool? isolatedModeOverride = null,
            string? archetypeOverride = null,
            TerminalLogBufferService? logBufferOverride = null)
        {
            if (ex == null) return null;

            lock (SyncLock)
            {
                try
                {
                    string logsDir = !string.IsNullOrWhiteSpace(logsDirOverride)
                        ? logsDirOverride!
                        : (_logsDirProvider?.Invoke() ?? Path.Combine(AppContext.BaseDirectory, "logs"));

                    string version = !string.IsNullOrWhiteSpace(buildVersionOverride)
                        ? buildVersionOverride!.Trim()
                        : _buildVersion;
                    if (string.IsNullOrWhiteSpace(version))
                        version = CrashBundleExceptionFilter.DefaultFallbackVersion;

                    bool isIsolated = isolatedModeOverride ?? _isolatedMode;
                    string archetype = !string.IsNullOrWhiteSpace(archetypeOverride)
                        ? archetypeOverride!.Trim()
                        : _archetype;

                    var logBuffer = logBufferOverride ?? _logBufferProvider?.Invoke();

                    string crashesDir = Path.Combine(logsDir, "crashes");
                    Directory.CreateDirectory(crashesDir);

                    var nowUtc = DateTime.UtcNow;
                    string safeVersion = SanitizeFileNameSegment(version);
                    string crashId = $"crash_{nowUtc:yyyyMMdd_HHmmss}_v{safeVersion}";
                    string bundlePath = Path.Combine(crashesDir, $"{crashId}.json");

                    int collisionIndex = 1;
                    while (File.Exists(bundlePath))
                    {
                        crashId = $"crash_{nowUtc:yyyyMMdd_HHmmss}_{collisionIndex}_v{safeVersion}";
                        bundlePath = Path.Combine(crashesDir, $"{crashId}.json");
                        collisionIndex++;
                    }

                    var (failingModule, failingFile, failingLine, stackFrames) = ExtractExceptionDetails(ex);
                    var precedingLines = CollectPrecedingLogLines(logBuffer, logsDir, DefaultPrecedingLogLineCount);

                    long uptimeSeconds = Math.Max(0L, (long)(nowUtc - ProcessStartedAtUtc).TotalSeconds);
                    var dto = new CrashBundleDto
                    {
                        SchemaVersion = "1.0",
                        CrashId = crashId,
                        TimestampUtc = nowUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        AppName = "Housekeeping",
                        BuildVersion = version,
                        Archetype = archetype,
                        IsolatedMode = isIsolated,
                        Process = new CrashProcessInfoDto
                        {
                            Pid = Environment.ProcessId,
                            UptimeSeconds = uptimeSeconds,
                            CommandLineSanitized = SanitizeText(Environment.CommandLine)
                        },
                        Exception = new CrashExceptionInfoDto
                        {
                            Type = ex.GetType().FullName ?? ex.GetType().Name,
                            Message = SanitizeText(ex.Message),
                            FailingModule = SanitizeText(failingModule),
                            FailingFile = SanitizeText(failingFile),
                            FailingLine = failingLine,
                            StackTrace = stackFrames
                        },
                        PrecedingLogRingBuffer = precedingLines
                    };

                    string json = JsonSerializer.Serialize(dto, BundleJsonOptions);
                    File.WriteAllText(bundlePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                    PruneCrashBundles(crashesDir, MaxRetainedCrashBundles);
                    return bundlePath;
                }
                catch (Exception writeEx)
                {
                    Debug.WriteLine($"[CrashBundleService] WriteCrashBundle failed: {writeEx.Message}");
                    return null;
                }
            }
        }

        public static string SanitizeText(string? input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            string sanitized = UserProfileRegex.Replace(input, "%USERPROFILE%");
            sanitized = CliSecretRegex.Replace(sanitized, "$1[REDACTED]");
            sanitized = KeyValueSecretRegex.Replace(sanitized, "$1[REDACTED]$3");
            return sanitized;
        }

        public static void PruneCrashBundles(string crashesDir, int maxRetained = MaxRetainedCrashBundles)
        {
            try
            {
                if (!Directory.Exists(crashesDir)) return;
                var files = new DirectoryInfo(crashesDir)
                    .GetFiles("crash_*.json")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ThenByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (files.Count <= maxRetained) return;
                for (int i = maxRetained; i < files.Count; i++)
                {
                    try { files[i].Delete(); }
                    catch (Exception delEx) { Debug.WriteLine($"[CrashBundleService] Prune delete skipped: {delEx.Message}"); }
                }
            }
            catch (Exception pruneEx) { Debug.WriteLine($"[CrashBundleService] PruneCrashBundles ignored: {pruneEx.Message}"); }
        }

        private static (string FailingModule, string FailingFile, int FailingLine, List<string> StackFrames) ExtractExceptionDetails(Exception ex)
        {
            string failingModule = ex.TargetSite?.DeclaringType?.FullName ?? ex.Source ?? "Housekeeping";
            string failingFile = "";
            int failingLine = 0;

            try
            {
                var frames = new StackTrace(ex, fNeedFileInfo: true).GetFrames();
                if (frames != null)
                {
                    foreach (var frame in frames)
                    {
                        var method = frame.GetMethod();
                        if (method?.DeclaringType != null && string.IsNullOrWhiteSpace(failingModule))
                            failingModule = method.DeclaringType.FullName ?? failingModule;

                        string? fileName = frame.GetFileName();
                        if (!string.IsNullOrWhiteSpace(fileName))
                        {
                            failingFile = Path.GetFileName(fileName);
                            failingLine = frame.GetFileLineNumber();
                            if (method?.DeclaringType?.FullName != null)
                                failingModule = method.DeclaringType.FullName;
                            break;
                        }
                    }
                }
            }
            catch (Exception traceEx) { Debug.WriteLine($"[CrashBundleService] StackTrace reflection fallback: {traceEx.Message}"); }

            var stackLines = new List<string>();
            foreach (string rawLine in ex.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = rawLine.Trim();
                stackLines.Add(SanitizeText(trimmed));

                if (string.IsNullOrWhiteSpace(failingFile))
                {
                    var match = StackFrameLocationRegex.Match(trimmed);
                    if (match.Success)
                    {
                        failingFile = Path.GetFileName(match.Groups["file"].Value.Trim());
                        if (int.TryParse(match.Groups["line"].Value, out int parsedLine))
                            failingLine = parsedLine;
                    }
                }
            }

            return (failingModule, failingFile, failingLine, stackLines);
        }

        private static List<string> CollectPrecedingLogLines(TerminalLogBufferService? logBuffer, string logsDir, int count)
        {
            var result = new List<string>();
            try
            {
                if (logBuffer != null)
                {
                    var buffered = logBuffer.GetPrecedingLogLines(count);
                    if (buffered.Count > 0)
                    {
                        foreach (string line in buffered)
                            result.Add(SanitizeText(line));
                        return result;
                    }
                }

                string liveLogPath = Path.Combine(logsDir, "Housekeeping_Live.log");
                if (File.Exists(liveLogPath))
                {
                    var allLines = new List<string>();
                    using var fs = new FileStream(liveLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(fs, Encoding.UTF8);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                        allLines.Add(line);

                    int skip = Math.Max(0, allLines.Count - count);
                    for (int i = skip; i < allLines.Count; i++)
                        result.Add(SanitizeText(allLines[i]));
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[CrashBundleService] CollectPrecedingLogLines ignored: {ex.Message}"); }
            return result;
        }

        private static string SanitizeFileNameSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unknown";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString();
        }
    }
}
