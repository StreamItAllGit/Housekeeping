// @module: Housekeeping.Core/Services/CrashBundleExceptionFilter
// @purpose: Classifies benign unobserved background exceptions (OperationCanceledException, pooled HttpConnection SocketException 10054/995) and holds CrashBundle DTOs.
// @depends-on: System, System.Collections.Generic, System.IO, System.Net.Http, System.Net.Sockets, System.Text.Json.Serialization
// @exports: CrashBundleExceptionFilter, CrashBundleDto, CrashProcessInfoDto, CrashExceptionInfoDto
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json.Serialization;

namespace Housekeeping.Services
{
    public sealed class CrashBundleDto
    {
        [JsonPropertyName("schema_version")] public string SchemaVersion { get; set; } = "1.0";
        [JsonPropertyName("crash_id")] public string CrashId { get; set; } = "";
        [JsonPropertyName("timestamp_utc")] public string TimestampUtc { get; set; } = "";
        [JsonPropertyName("app_name")] public string AppName { get; set; } = "Housekeeping";
        [JsonPropertyName("build_version")] public string BuildVersion { get; set; } = "";
        [JsonPropertyName("archetype")] public string Archetype { get; set; } = "2-tray-companion";
        [JsonPropertyName("isolated_mode")] public bool IsolatedMode { get; set; }
        [JsonPropertyName("process")] public CrashProcessInfoDto Process { get; set; } = new();
        [JsonPropertyName("exception")] public CrashExceptionInfoDto Exception { get; set; } = new();
        [JsonPropertyName("preceding_log_ring_buffer")] public List<string> PrecedingLogRingBuffer { get; set; } = new();
    }

    public sealed class CrashProcessInfoDto
    {
        [JsonPropertyName("pid")] public int Pid { get; set; }
        [JsonPropertyName("uptime_seconds")] public long UptimeSeconds { get; set; }
        [JsonPropertyName("command_line_sanitized")] public string CommandLineSanitized { get; set; } = "";
    }

    public sealed class CrashExceptionInfoDto
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("message")] public string Message { get; set; } = "";
        [JsonPropertyName("failing_module")] public string FailingModule { get; set; } = "";
        [JsonPropertyName("failing_file")] public string FailingFile { get; set; } = "";
        [JsonPropertyName("failing_line")] public int FailingLine { get; set; }
        [JsonPropertyName("stack_trace")] public List<string> StackTrace { get; set; } = new();
    }

    public static class CrashBundleExceptionFilter
    {
        public const string DefaultFallbackVersion = "0.1.12-hf.1";

        public static bool IsBenignBackgroundException(Exception? ex)
        {
            if (ex == null) return true;

            if (ex is AggregateException agg)
            {
                var flattened = agg.Flatten();
                if (flattened.InnerExceptions.Count == 0)
                    return true;

                foreach (var inner in flattened.InnerExceptions)
                {
                    if (!IsSingleLeafBenign(inner))
                        return false;
                }
                return true;
            }

            return IsSingleLeafBenign(ex);
        }

        private static bool IsSingleLeafBenign(Exception leaf)
        {
            if (leaf is OperationCanceledException)
                return true;

            if (leaf is IOException or HttpRequestException or SocketException)
            {
                if (TryFindInnerSocketException(leaf, out var sockEx) &&
                    sockEx.SocketErrorCode is SocketError.ConnectionReset or SocketError.OperationAborted or SocketError.ConnectionAborted or SocketError.Shutdown)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindInnerSocketException(Exception ex, out SocketException socketException)
        {
            Exception? current = ex;
            while (current != null)
            {
                if (current is SocketException s)
                {
                    socketException = s;
                    return true;
                }
                current = current.InnerException;
            }

            socketException = null!;
            return false;
        }
    }
}
