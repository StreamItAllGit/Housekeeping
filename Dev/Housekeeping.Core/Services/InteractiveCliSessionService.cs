using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Housekeeping.Services
{
    public sealed class InteractiveCliSessionService
    {
        private readonly TerminalLogBufferService _logBuffer;
        private readonly object _lock = new();
        private readonly List<string> _commandHistory = new();
        private CancellationTokenSource? _activeCts;
        private string _workingDirectory;

        public string WorkingDirectory
        {
            get { lock (_lock) return _workingDirectory; }
        }

        public bool IsCommandRunning
        {
            get { lock (_lock) return _activeCts != null; }
        }

        public InteractiveCliSessionService(TerminalLogBufferService logBuffer, string? initialWorkingDirectory = null)
        {
            _logBuffer = logBuffer;
            _workingDirectory = !string.IsNullOrWhiteSpace(initialWorkingDirectory) && Directory.Exists(initialWorkingDirectory)
                ? Path.GetFullPath(initialWorkingDirectory)
                : AppDomain.CurrentDomain.BaseDirectory;
        }

        public List<string> GetCommandHistory()
        {
            lock (_lock)
            {
                return new List<string>(_commandHistory);
            }
        }

        public void CancelActiveCommand()
        {
            CancellationTokenSource? cts;
            lock (_lock)
            {
                cts = _activeCts;
            }
            try { cts?.Cancel(); } catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[InteractiveCliSessionService] Non-fatal error ignored: {__suppressedEx1.Message}"); }
        }

        public async Task<(int ExitCode, string WorkingDirectory)> ExecuteCommandAsync(string rawCommand, CancellationToken externalCt = default)
        {
            string cmd = (rawCommand ?? "").Trim();
            if (string.IsNullOrWhiteSpace(cmd))
                return (0, WorkingDirectory);

            lock (_lock)
            {
                if (_commandHistory.Count == 0 || !string.Equals(_commandHistory[^1], cmd, StringComparison.Ordinal))
                {
                    _commandHistory.Add(cmd);
                    if (_commandHistory.Count > 100)
                        _commandHistory.RemoveAt(0);
                }
            }

            _logBuffer.Append("CLI", $"> {cmd}");

            if (cmd.Equals("cls", StringComparison.OrdinalIgnoreCase) || cmd.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                _logBuffer.Clear();
                return (0, WorkingDirectory);
            }

            if (cmd.Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                _logBuffer.Append("INFO", "Housekeeping Interactive Terminal CLI");
                _logBuffer.Append("INFO", "Built-in commands: 'clear'/'cls' (clear log), 'help' (this guide), 'cd <path>' (change dir), 'pwd' (current dir)");
                _logBuffer.Append("INFO", $"Current Working Directory: {WorkingDirectory}");
                _logBuffer.Append("INFO", "You can run any Windows command, script, or tool (dir, ping, rclone, handbrakecli, 7z).");
                return (0, WorkingDirectory);
            }

            if (cmd.Equals("pwd", StringComparison.OrdinalIgnoreCase))
            {
                _logBuffer.Append("INFO", $"Current Directory: {WorkingDirectory}");
                return (0, WorkingDirectory);
            }

            if (cmd.StartsWith("cd ", StringComparison.OrdinalIgnoreCase) || cmd.Equals("cd", StringComparison.OrdinalIgnoreCase))
            {
                string target = cmd.Length > 3 ? cmd[3..].Trim().Trim('"', '\'') : "";
                if (string.IsNullOrEmpty(target))
                {
                    _logBuffer.Append("INFO", $"Current Directory: {WorkingDirectory}");
                    return (0, WorkingDirectory);
                }

                string candidate = Path.IsPathRooted(target) ? target : Path.Combine(WorkingDirectory, target);
                if (Directory.Exists(candidate))
                {
                    string resolved = Path.GetFullPath(candidate);
                    lock (_lock) { _workingDirectory = resolved; }
                    _logBuffer.Append("SUCCESS", $"Working directory changed to: {resolved}");
                    return (0, resolved);
                }

                _logBuffer.Append("ERROR", $"Directory not found: {target}");
                return (1, WorkingDirectory);
            }

            CancellationTokenSource linkedCts;
            lock (_lock)
            {
                if (_activeCts != null)
                {
                    _logBuffer.Append("WARN", "Another interactive CLI command is currently running. Stop it or wait for completion.");
                    return (-1, WorkingDirectory);
                }
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
                _activeCts = linkedCts;
            }

            var ct = linkedCts.Token;
            string workDir = Directory.Exists(WorkingDirectory) ? WorkingDirectory : AppDomain.CurrentDomain.BaseDirectory;
            int exitCode = -1;

            try
            {
                exitCode = await Task.Run(() =>
                {
                    using var proc = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/d /s /c \"{cmd}\"",
                            WorkingDirectory = workDir,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardInput = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            StandardOutputEncoding = Encoding.UTF8,
                            StandardErrorEncoding = Encoding.UTF8
                        }
                    };

                    proc.OutputDataReceived += (_, args) =>
                    {
                        if (args.Data != null)
                            _logBuffer.Append("CLI", args.Data);
                    };
                    proc.ErrorDataReceived += (_, args) =>
                    {
                        if (args.Data != null)
                            _logBuffer.Append("ERROR", args.Data);
                    };

                    proc.Start();
                    try { proc.StandardInput.Close(); } catch (System.Exception __suppressedEx2) { System.Diagnostics.Debug.WriteLine($"[InteractiveCliSessionService] Non-fatal error ignored: {__suppressedEx2.Message}"); }
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();

                    while (!proc.WaitForExit(150))
                    {
                        if (ct.IsCancellationRequested)
                        {
                            try { proc.Kill(entireProcessTree: true); } catch (System.Exception __suppressedEx3) { System.Diagnostics.Debug.WriteLine($"[InteractiveCliSessionService] Non-fatal error ignored: {__suppressedEx3.Message}"); }
                            _logBuffer.Append("WARN", "[Command terminated by user]");
                            return -1;
                        }
                    }

                    proc.WaitForExit();
                    if (proc.ExitCode == 0)
                        _logBuffer.Append("SUCCESS", "[Command finished successfully (Exit Code: 0)]");
                    else
                        _logBuffer.Append("WARN", $"[Command exited with code {proc.ExitCode}]");

                    return proc.ExitCode;
                }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logBuffer.Append("ERROR", $"Execution error: {ex.Message}");
            }
            finally
            {
                lock (_lock)
                {
                    _activeCts = null;
                }
                linkedCts.Dispose();
            }

            return (exitCode, WorkingDirectory);
        }
    }
}
