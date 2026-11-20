using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Interop;

namespace Housekeeping.Services
{
    public class ProcessProgressInfo
    {
        public double Percent { get; set; }
        public string StatusText { get; set; } = "";
    }

    public class ProcessRunResult
    {
        public int ExitCode { get; set; } = -1;
        public List<string> StdOutLines { get; set; } = new();
        public List<string> StdErrLines { get; set; } = new();
    }

    public class ProcessRunnerService
    {
        private static readonly Regex HandBrakeProgressRegex = new(
            @"Encoding:\s*task\s+\d+\s+of\s+\d+,\s*(?<pct>\d+(?:\.\d+)?)\s*%(?:\s*\((?<fps>[\d\.]+)\s*fps,\s*avg\s*(?<avg>[\d\.]+)\s*fps,\s*ETA\s*(?<eta>\S+)\))?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex GenericPercentRegex = new(
            @"(?<pct>\d{1,3}(?:\.\d+)?)\s*%",
            RegexOptions.Compiled);

        private static readonly Regex SensitiveCliFlagRegex = new(
            @"(?<flag>(?:^|\s)(?:-s|--session-auth|--token|--password|--pass))\s+(?:""[^""]*""|\S+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string RedactSensitiveCommandLine(string commandLine)
        {
            if (string.IsNullOrEmpty(commandLine)) return "";
            return SensitiveCliFlagRegex.Replace(commandLine, "${flag} \"***REDACTED***\"");
        }

        private readonly Action<string, string> _log;
        private readonly Action<ProcessProgressInfo>? _onProgress;
        private Process? _activeProcess;
        private bool _isSuspended;
        private double _lastLoggedMilestonePct = -10.0;
        private DateTime _lastLoggedMilestoneTime = DateTime.MinValue;

        public bool IsSuspended => _isSuspended;

        public ProcessRunnerService(Action<string, string> log, Action<ProcessProgressInfo>? onProgress = null)
        {
            _log = log;
            _onProgress = onProgress;
        }

        public void ToggleSuspendActiveProcess()
        {
            if (_activeProcess == null || _activeProcess.HasExited)
                return;

            try
            {
                if (!_isSuspended)
                {
                    NativeProcessSuspend.SuspendProcessTree(_activeProcess.Id);
                    _isSuspended = true;
                    _log("WARN", $"[Process Suspended] PID {_activeProcess.Id} paused. Click Resume Process to continue.");
                }
                else
                {
                    NativeProcessSuspend.ResumeProcessTree(_activeProcess.Id);
                    _isSuspended = false;
                    _log("INFO", $"[Process Resumed] PID {_activeProcess.Id} resumed.");
                }
            }
            catch (Exception ex)
            {
                _log("ERROR", $"Failed to toggle process suspend state: {ex.Message}");
            }
        }

        private static readonly HashSet<string> WindowsShellBuiltIns = new(StringComparer.OrdinalIgnoreCase)
        {
            "rmdir", "rd", "del", "erase", "copy", "mkdir", "md", "move", "ren", "rename", "dir", "type", "cls", "echo"
        };

        public static (string Executable, string Arguments) SplitCommandLine(string rawCommandLine)
        {
            string trimmed = (rawCommandLine ?? "").Trim();
            if (string.IsNullOrEmpty(trimmed))
                return ("cmd.exe", "");

            if (trimmed.StartsWith('"'))
            {
                int closingQuote = trimmed.IndexOf('"', 1);
                if (closingQuote > 1)
                {
                    string exe = trimmed.Substring(1, closingQuote - 1);
                    string args = trimmed.Substring(closingQuote + 1).TrimStart();
                    return (exe, args);
                }
            }

            int firstSpace = trimmed.IndexOf(' ');
            if (firstSpace > 0)
            {
                return (trimmed.Substring(0, firstSpace), trimmed.Substring(firstSpace + 1).TrimStart());
            }

            return (trimmed, "");
        }

        public Task<ProcessRunResult> RunCommandAsync(
            string rawCommandLine,
            bool useExternalCmdWindow,
            string? workingDirectory,
            bool belowNormalPriority,
            bool logOutputToTerminal,
            CancellationToken ct) =>
            RunCommandAsync(rawCommandLine, useExternalCmdWindow, workingDirectory, belowNormalPriority, logOutputToTerminal, ct, environmentVariables: null);

        public async Task<ProcessRunResult> RunCommandAsync(
            string rawCommandLine,
            bool useExternalCmdWindow,
            string? workingDirectory,
            bool belowNormalPriority,
            bool logOutputToTerminal,
            CancellationToken ct,
            IReadOnlyDictionary<string, string>? environmentVariables = null)
        {
            var result = new ProcessRunResult();
            var (exePath, exeArgs) = SplitCommandLine(rawCommandLine);
            bool isBatchScript = exePath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ||
                                 exePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
            bool isShellBuiltIn = WindowsShellBuiltIns.Contains(exePath);

            ProcessStartInfo startInfo;
            if (!useExternalCmdWindow && !isBatchScript && !isShellBuiltIn)
            {
                // Launch the CLI binary (HandBrakeCLI.exe, 7z.exe, rclone.exe, hbd.exe) directly without cmd.exe quote mangling
                startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = exeArgs,
                    WorkingDirectory = !string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory)
                        ? workingDirectory
                        : Environment.CurrentDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
            }
            else
            {
                // Use cmd.exe /d /s /c "<rawCommandLine>" so outer quotes are stripped cleanly without breaking "C:\Program Files\..."
                string effectiveCmd = useExternalCmdWindow
                    ? PrependCmdEnvironmentAssignments(rawCommandLine, environmentVariables)
                    : rawCommandLine;
                startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/d /s /c \"{effectiveCmd}\"",
                    WorkingDirectory = !string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory)
                        ? workingDirectory
                        : Environment.CurrentDirectory
                };

                if (useExternalCmdWindow)
                {
                    startInfo.UseShellExecute = true;
                    startInfo.CreateNoWindow = false;
                    startInfo.WindowStyle = ProcessWindowStyle.Normal;
                }
                else
                {
                    startInfo.UseShellExecute = false;
                    startInfo.CreateNoWindow = true;
                    startInfo.RedirectStandardOutput = true;
                    startInfo.RedirectStandardError = true;
                    startInfo.StandardOutputEncoding = Encoding.UTF8;
                    startInfo.StandardErrorEncoding = Encoding.UTF8;
                }
            }

            if (!startInfo.UseShellExecute && environmentVariables != null)
            {
                foreach (var kvp in environmentVariables)
                {
                    if (!string.IsNullOrWhiteSpace(kvp.Key))
                        startInfo.Environment[kvp.Key] = kvp.Value ?? "";
                }
            }

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _activeProcess = process;
            _isSuspended = false;

            try
            {
                if (logOutputToTerminal)
                    _log("CMD", $"> {RedactSensitiveCommandLine(rawCommandLine)}");

                process.Start();

                if (belowNormalPriority)
                {
                    try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[ProcessRunnerService] Non-fatal error ignored: {__suppressedEx1.Message}"); }
                }

                if (!useExternalCmdWindow)
                {
                    var stdOutTask = ReadStreamWithCarriageReturnsAsync(
                        process.StandardOutput,
                        line =>
                        {
                            lock (result.StdOutLines) { result.StdOutLines.Add(line); }
                            HandleOutputLine(line, isError: false, logOutputToTerminal);
                        },
                        ct);

                    var stdErrTask = ReadStreamWithCarriageReturnsAsync(
                        process.StandardError,
                        line =>
                        {
                            lock (result.StdErrLines) { result.StdErrLines.Add(line); }
                            HandleOutputLine(line, isError: true, logOutputToTerminal);
                        },
                        ct);

                    using var reg = ct.Register(() => TryKillProcessTree(process));
                    await Task.WhenAll(process.WaitForExitAsync(ct), stdOutTask, stdErrTask);
                }
                else
                {
                    using var reg = ct.Register(() => TryKillProcessTree(process));
                    await process.WaitForExitAsync(ct);
                }

                result.ExitCode = process.ExitCode;
            }
            catch (OperationCanceledException)
            {
                TryKillProcessTree(process);
                result.ExitCode = -999;
                _log("WARN", "Process execution cancelled by user.");
            }
            catch (Exception ex)
            {
                result.ExitCode = -1;
                _log("ERROR", $"Process execution error: {ex.Message}");
            }
            finally
            {
                if (_isSuspended && _activeProcess != null)
                {
                    try { NativeProcessSuspend.ResumeProcessTree(_activeProcess.Id); } catch (System.Exception __suppressedEx2) { System.Diagnostics.Debug.WriteLine($"[ProcessRunnerService] Non-fatal error ignored: {__suppressedEx2.Message}"); }
                    _isSuspended = false;
                }
                _activeProcess = null;
            }

            return result;
        }

        private void HandleOutputLine(string line, bool isError, bool logOutputToTerminal)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            var hbMatch = HandBrakeProgressRegex.Match(line);
            if (hbMatch.Success && double.TryParse(hbMatch.Groups["pct"].Value, out double hbPct))
            {
                string eta = hbMatch.Groups["eta"].Success ? $" | ETA {hbMatch.Groups["eta"].Value}" : "";
                string fps = hbMatch.Groups["fps"].Success ? $" ({hbMatch.Groups["fps"].Value} fps)" : "";
                string statusText = $"Encoding {hbPct:F1}%{fps}{eta}";
                _onProgress?.Invoke(new ProcessProgressInfo
                {
                    Percent = hbPct,
                    StatusText = statusText
                });

                if (logOutputToTerminal && ShouldEmitMilestone(hbPct))
                {
                    _log("WORK", statusText);
                }
                return; // Don't flood terminal with thousands of HandBrake tick lines
            }

            var pctMatch = GenericPercentRegex.Match(line);
            if (pctMatch.Success && double.TryParse(pctMatch.Groups["pct"].Value, out double genPct) && genPct <= 100.0)
            {
                _onProgress?.Invoke(new ProcessProgressInfo
                {
                    Percent = genPct,
                    StatusText = line.Length > 70 ? line.Substring(0, 70) + "..." : line
                });
            }

            if (logOutputToTerminal)
            {
                // HandBrakeCLI writes normal diagnostics to stderr; only flag real errors
                bool looksLikeError = isError && (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
                                                  || line.Contains("Failed", StringComparison.OrdinalIgnoreCase)
                                                  || line.Contains("Fatal", StringComparison.OrdinalIgnoreCase));
                if (looksLikeError && (line.Contains("0 decoder errors", StringComparison.OrdinalIgnoreCase)
                                       || line.Contains("0 errors", StringComparison.OrdinalIgnoreCase)
                                       || line.Contains("errors: 0", StringComparison.OrdinalIgnoreCase)))
                {
                    looksLikeError = false;
                }
                _log(looksLikeError ? "ERROR" : "OUT", line);
            }
        }

        private static async Task ReadStreamWithCarriageReturnsAsync(
            StreamReader reader,
            Action<string> onLine,
            CancellationToken ct)
        {
            char[] buffer = new char[512];
            var sb = new StringBuilder();

            while (!ct.IsCancellationRequested)
            {
                int read;
                try
                {
                    read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                }
                catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException)
                {
                    break;
                }

                if (read <= 0)
                    break;

                for (int i = 0; i < read; i++)
                {
                    char c = buffer[i];
                    if (c == '\r' || c == '\n')
                    {
                        if (sb.Length > 0)
                        {
                            onLine(sb.ToString());
                            sb.Clear();
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }

            if (sb.Length > 0)
            {
                onLine(sb.ToString());
            }
        }

        public void ResumeActiveProcessIfSuspended()
        {
            var proc = _activeProcess;
            if (!_isSuspended || proc == null) return;
            try { if (!proc.HasExited) NativeProcessSuspend.ResumeProcessTree(proc.Id); }
            catch (Exception ex) { Debug.WriteLine($"[ProcessRunnerService] ResumeActiveProcessIfSuspended ignored: {ex.Message}"); }
            finally { _isSuspended = false; }
        }

        public void KillActiveProcess()
        {
            var proc = _activeProcess;
            if (proc != null) TryKillProcessTree(proc);
        }

        private void TryKillProcessTree(Process process)
        {
            try
            {
                if (_isSuspended)
                {
                    try { NativeProcessSuspend.ResumeProcessTree(process.Id); }
                    catch (Exception resumeEx) { Debug.WriteLine($"[ProcessRunnerService] Resume before kill ignored: {resumeEx.Message}"); }
                    _isSuspended = false;
                }
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (System.Exception __suppressedEx3) { System.Diagnostics.Debug.WriteLine($"[ProcessRunnerService] Non-fatal error ignored: {__suppressedEx3.Message}"); }
        }

        private static string PrependCmdEnvironmentAssignments(string rawCommandLine, IReadOnlyDictionary<string, string>? environmentVariables)
        {
            if (environmentVariables == null || environmentVariables.Count == 0)
                return rawCommandLine;

            var prefixes = new List<string>();
            foreach (var kvp in environmentVariables)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key) || kvp.Key.IndexOfAny(['=', '"', '&', '|', '<', '>', '\r', '\n']) >= 0)
                    continue;
                string val = (kvp.Value ?? "").Replace("\r", "").Replace("\n", "").Replace("\"", "");
                prefixes.Add($"set \"{kvp.Key}={val}\"");
            }
            return prefixes.Count == 0 ? rawCommandLine : $"{string.Join(" && ", prefixes)} && {rawCommandLine}";
        }

        private bool ShouldEmitMilestone(double pct)
        {
            var now = DateTime.UtcNow;
            if (pct - _lastLoggedMilestonePct >= 5.0 || (now - _lastLoggedMilestoneTime).TotalSeconds >= 15.0)
            {
                _lastLoggedMilestonePct = Math.Floor(pct / 5.0) * 5.0;
                _lastLoggedMilestoneTime = now;
                return true;
            }
            return false;
        }
    }
}
