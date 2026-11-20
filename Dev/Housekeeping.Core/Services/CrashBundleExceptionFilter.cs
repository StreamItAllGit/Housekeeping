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
        public const string DefaultFallbackVersion = "0.2.0";

        private static readonly string[] DisconnectMessageMarkers =
        {
            "10054",
            "forcibly closed",
            "response ended prematurely",
            "unable to read data from the transport connection",
            "unable to write data to the transport connection",
            "client reset the request stream",
            "connection reset",
            "connection aborted",
            "broken pipe",
            "operation was aborted"
        };

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

        public static string DescribeTransientDisconnect(Exception? ex)
        {
            if (ex == null) return "null";
            Exception baseEx = ex.GetBaseException();
            if (baseEx is SocketException sock)
                return $"SocketException({sock.SocketErrorCode}/{(int)sock.SocketErrorCode})";
            if (ex is HttpRequestException hre)
                return $"HttpRequestException({hre.HttpRequestError})";
            return baseEx.GetType().Name;
        }

        private static bool IsSingleLeafBenign(Exception leaf)
        {
            if (leaf is OperationCanceledException)
                return true;

            if (leaf is SocketException directSock && IsDisconnectSocketError(directSock.SocketErrorCode))
                return true;

            if (leaf is HttpRequestException httpEx)
            {
                if (HasBenignInnerCause(httpEx) ||
                    httpEx.HttpRequestError is HttpRequestError.ConnectionError or HttpRequestError.ResponseEnded
                        or HttpRequestError.ProxyTunnelError or HttpRequestError.NameResolutionError ||
                    ContainsDisconnectMarker(httpEx.Message))
                {
                    return true;
                }
            }

            if (leaf is IOException ioEx &&
                ioEx is not FileNotFoundException &&
                ioEx is not DirectoryNotFoundException &&
                ioEx is not DriveNotFoundException &&
                ioEx is not PathTooLongException)
            {
                if (ioEx is HttpIOException || HasBenignInnerCause(ioEx) || ContainsDisconnectMarker(ioEx.Message))
                    return true;
            }

            return false;
        }

        private static bool HasBenignInnerCause(Exception ex)
        {
            Exception? current = ex.InnerException;
            while (current != null)
            {
                if (current is OperationCanceledException or ObjectDisposedException or HttpIOException)
                    return true;
                if (current is SocketException sock && IsDisconnectSocketError(sock.SocketErrorCode))
                    return true;
                if (current is AggregateException agg)
                    return IsBenignBackgroundException(agg);
                current = current.InnerException;
            }
            return false;
        }

        private static bool IsDisconnectSocketError(SocketError code) => code is
            SocketError.ConnectionReset or SocketError.OperationAborted or SocketError.ConnectionAborted or
            SocketError.Shutdown or SocketError.Disconnecting or SocketError.TimedOut or
            SocketError.NetworkReset or SocketError.NotConnected or SocketError.Interrupted or
            SocketError.ConnectionRefused or SocketError.HostUnreachable or SocketError.NetworkUnreachable;

        private static bool ContainsDisconnectMarker(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            foreach (string marker in DisconnectMessageMarkers)
            {
                if (message.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
