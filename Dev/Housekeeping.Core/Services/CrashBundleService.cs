using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Housekeeping.Services
{
    public sealed class CrashBundleDto
    {
        [JsonPropertyName("schema_version")]
        public string SchemaVersion { get; set; } = "1.0";

        [JsonPropertyName("crash_id")]
        public string CrashId { get; set; } = "";

        [JsonPropertyName("timestamp_utc")]
        public string TimestampUtc { get; set; } = "";

        [JsonPropertyName("app_name")]
        public string AppName { get; set; } = "Housekeeping";

        [JsonPropertyName("build_version")]
        public string BuildVersion { get; set; } = "";

        [JsonPropertyName("archetype")]
        public string Archetype { get; set; } = "2-tray-companion";

        [JsonPropertyName("test_mode")]
        public bool TestMode { get; set; }

        [JsonPropertyName("process")]
        public CrashProcessInfoDto Process { get; set; } = new();

        [JsonPropertyName("exception")]
        public CrashExceptionInfoDto Exception { get; set; } = new();

        [JsonPropertyName("preceding_log_ring_buffer")]
        public List<string> PrecedingLogRingBuffer { get; set; } = new();
    }

    public sealed class CrashProcessInfoDto
    {
        [JsonPropertyName("pid")]
        public int Pid { get; set; }

        [JsonPropertyName("uptime_seconds")]
        public long UptimeSeconds { get; set; }

        [JsonPropertyName("command_line_sanitized")]
        public string CommandLineSanitized { get; set; } = "";
    }

    public sealed class CrashExceptionInfoDto
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";

        [JsonPropertyName("failing_module")]
        public string FailingModule { get; set; } = "";

        [JsonPropertyName("failing_file")]
        public string FailingFile { get; set; } = "";

        [JsonPropertyName("failing_line")]
        public int FailingLine { get; set; }

        [JsonPropertyName("stack_trace")]
        public List<string> StackTrace { get; set; } = new();
    }

    public static class CrashBundleService
    {
        public const int MaxRetainedCrashBundles = 20;
        public const int DefaultPrecedingLogLineCount = 50;

        private static readonly object SyncLock = new();
        private static readonly DateTime ProcessStartedAtUtc = DateTime.UtcNow;
        private static readonly JsonSerializerOptions BundleJsonOptions = new()
        {
            WriteIndented = true
        };

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
        private static string _buildVersion = "1.0.0-rc.11-hf.1";
        private static bool _testMode;
        private static string _archetype = "2-tray-companion";

        public static void RegisterGlobalHandlers(
            Func<string> getLogsDir,
            string buildVersion,
            bool testMode,
            string archetype = "2-tray-companion",
            Func<TerminalLogBufferService?>? getLogBuffer = null)
        {
            lock (SyncLock)
            {
                _logsDirProvider = getLogsDir;
                _logBufferProvider = getLogBuffer;
                if (!string.IsNullOrWhiteSpace(buildVersion))
                    _buildVersion = buildVersion.Trim();
                _testMode = testMode;
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
                        WriteCrashBundle(ex);
                    }
                    catch
                    {
                        // Never throw from global unhandled exception handler
                    }
                };

                TaskScheduler.UnobservedTaskException += (_, e) =>
                {
                    try
                    {
                        if (e.Exception != null)
                        {
                            WriteCrashBundle(e.Exception.Flatten());
                        }
                    }
                    catch
                    {
                        // Never throw from unobserved task handler
                    }
                };
            }
        }

        public static string? WriteCrashBundle(
            Exception ex,
            string? logsDirOverride = null,
            string? buildVersionOverride = null,
            bool? testModeOverride = null,
            string? archetypeOverride = null,
            TerminalLogBufferService? logBufferOverride = null)
        {
            if (ex == null)
                return null;

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
                        version = "1.0.0-rc.11-hf.1";

                    bool isTestMode = testModeOverride ?? _testMode;
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
                        TestMode = isTestMode,
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
                catch
                {
                    return null;
                }
            }
        }

        public static string SanitizeText(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return "";

            string sanitized = UserProfileRegex.Replace(input, "%USERPROFILE%");
            sanitized = CliSecretRegex.Replace(sanitized, "$1[REDACTED]");
            sanitized = KeyValueSecretRegex.Replace(sanitized, "$1[REDACTED]$3");
            return sanitized;
        }

        public static void PruneCrashBundles(string crashesDir, int maxRetained = MaxRetainedCrashBundles)
        {
            try
            {
                if (!Directory.Exists(crashesDir))
                    return;

                var files = new DirectoryInfo(crashesDir)
                    .GetFiles("crash_*.json")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ThenByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (files.Count <= maxRetained)
                    return;

                for (int i = maxRetained; i < files.Count; i++)
                {
                    try
                    {
                        files[i].Delete();
                    }
                    catch
                    {
                        // Ignore locked or already deleted file
                    }
                }
            }
            catch
            {
                // Ignore pruning errors
            }
        }

        private static (string FailingModule, string FailingFile, int FailingLine, List<string> StackFrames) ExtractExceptionDetails(Exception ex)
        {
            string failingModule = ex.TargetSite?.DeclaringType?.FullName ?? ex.Source ?? "Housekeeping";
            string failingFile = "";
            int failingLine = 0;

            try
            {
                var trace = new StackTrace(ex, fNeedFileInfo: true);
                var frames = trace.GetFrames();
                if (frames != null)
                {
                    foreach (var frame in frames)
                    {
                        var method = frame.GetMethod();
                        if (method?.DeclaringType != null && string.IsNullOrWhiteSpace(failingModule))
                        {
                            failingModule = method.DeclaringType.FullName ?? failingModule;
                        }

                        string? fileName = frame.GetFileName();
                        if (!string.IsNullOrWhiteSpace(fileName))
                        {
                            failingFile = Path.GetFileName(fileName);
                            failingLine = frame.GetFileLineNumber();
                            if (method?.DeclaringType?.FullName != null)
                            {
                                failingModule = method.DeclaringType.FullName;
                            }
                            break;
                        }
                    }
                }
            }
            catch
            {
                // Fallback to string parsing below
            }

            var stackLines = new List<string>();
            string rawStack = ex.ToString();
            foreach (string rawLine in rawStack.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
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
                        {
                            failingLine = parsedLine;
                        }
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
                        {
                            result.Add(SanitizeText(line));
                        }
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
                    {
                        allLines.Add(line);
                    }

                    int skip = Math.Max(0, allLines.Count - count);
                    for (int i = skip; i < allLines.Count; i++)
                    {
                        result.Add(SanitizeText(allLines[i]));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CrashBundleService] CollectPrecedingLogLines ignored: {ex.Message}");
            }

            return result;
        }

        private static string SanitizeFileNameSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unknown";

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            return sb.ToString();
        }
    }
}
