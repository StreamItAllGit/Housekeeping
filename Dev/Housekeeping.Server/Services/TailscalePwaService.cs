using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Housekeeping.Server.Services
{
    public sealed class TailscalePwaStatusDto
    {
        public bool TailscaleAvailable { get; set; }
        public string BackendState { get; set; } = "Unknown";
        public string DnsName { get; set; } = "";
        public string TailscaleIp { get; set; } = "";
        public bool HttpsCertsEnabled { get; set; }
        public bool ServeConfigured { get; set; }
        public string HttpsPwaUrl { get; set; } = "";
        public string PendingAuthUrl { get; set; } = "";
        public string ServeSummary { get; set; } = "";
        public string StatusMessage { get; set; } = "";
        public bool Simulated { get; set; }
    }

    /// <summary>
    /// Inspects local Tailscale MagicDNS / HTTPS certificate readiness and manages
    /// `tailscale serve --bg` HTTPS proxying so mobile browsers (Android Chrome WebAPK / iOS Safari)
    /// receive a trusted Let's Encrypt HTTPS origin for standalone PWA installation.
    /// </summary>
    public sealed class TailscalePwaService
    {
        private static readonly Regex UrlRegex = new(@"https://login\.tailscale\.com/\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly object _gate = new();
        private Process? _pendingAuthProcess;
        private string _pendingAuthUrl = "";
        private DateTime _pendingStartedUtc = DateTime.MinValue;

        public async Task<TailscalePwaStatusDto> GetStatusAsync(int localPort, CancellationToken ct = default)
        {
            if (localPort <= 0 || localPort > 65535)
                localPort = 5178;

            var dto = new TailscalePwaStatusDto();

            var (okStatus, statusOut, _) = await RunCliBoundedAsync("status --json", 4000, ct);
            if (!okStatus || string.IsNullOrWhiteSpace(statusOut))
            {
                dto.TailscaleAvailable = false;
                dto.StatusMessage = "Tailscale CLI was not detected or tailscaled is not running on this host.";
                return dto;
            }

            try
            {
                using var doc = JsonDocument.Parse(statusOut);
                var root = doc.RootElement;
                dto.TailscaleAvailable = true;

                if (root.TryGetProperty("BackendState", out var bs) && bs.ValueKind == JsonValueKind.String)
                    dto.BackendState = bs.GetString() ?? "Unknown";

                if (root.TryGetProperty("Self", out var self) && self.ValueKind == JsonValueKind.Object)
                {
                    if (self.TryGetProperty("DNSName", out var dns) && dns.ValueKind == JsonValueKind.String)
                        dto.DnsName = (dns.GetString() ?? "").Trim().TrimEnd('.');

                    if (self.TryGetProperty("TailscaleIPs", out var ips) && ips.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var ipEl in ips.EnumerateArray())
                        {
                            string ip = ipEl.GetString() ?? "";
                            if (!string.IsNullOrWhiteSpace(ip) && !ip.Contains(':'))
                            {
                                dto.TailscaleIp = ip;
                                break;
                            }
                        }
                    }
                }

                if (root.TryGetProperty("CertDomains", out var certs) && certs.ValueKind == JsonValueKind.Array)
                {
                    dto.HttpsCertsEnabled = certs.GetArrayLength() > 0;
                }
            }
            catch (Exception ex)
            {
                dto.TailscaleAvailable = false;
                dto.StatusMessage = $"Unable to parse Tailscale status: {ex.Message}";
                return dto;
            }

            var (okServe, serveText, _) = await RunCliBoundedAsync("serve status", 4000, ct);
            string trimmedServe = (serveText ?? "").Trim();
            dto.ServeSummary = trimmedServe;

            bool hasActiveServe = okServe
                && !string.IsNullOrWhiteSpace(trimmedServe)
                && !trimmedServe.StartsWith("No serve config", StringComparison.OrdinalIgnoreCase);

            if (hasActiveServe)
            {
                dto.ServeConfigured = true;
                ClearPendingProcessIfFinished(forceKill: true);

                // Extract exact https:// URL from `tailscale serve status` if printed, else default to https://<DnsName>
                var httpsMatch = Regex.Match(trimmedServe, @"https://[a-zA-Z0-9\.\-]+(?::\d+)?");
                if (httpsMatch.Success)
                {
                    dto.HttpsPwaUrl = httpsMatch.Value.TrimEnd('/');
                }
                else if (!string.IsNullOrWhiteSpace(dto.DnsName))
                {
                    dto.HttpsPwaUrl = $"https://{dto.DnsName}";
                }

                dto.StatusMessage = $"Tailscale Serve HTTPS is active at {dto.HttpsPwaUrl}";
                return dto;
            }

            ClearPendingProcessIfFinished(forceKill: false);
            lock (_gate)
            {
                dto.PendingAuthUrl = _pendingAuthUrl;
            }

            if (!string.IsNullOrWhiteSpace(dto.DnsName))
            {
                dto.HttpsPwaUrl = $"https://{dto.DnsName}";
            }

            if (!string.IsNullOrWhiteSpace(dto.PendingAuthUrl))
            {
                dto.StatusMessage = "Waiting for one-time Tailscale Serve & HTTPS authorization in your Tailscale Admin Console.";
            }
            else if (!dto.HttpsCertsEnabled)
            {
                dto.StatusMessage = "Tailscale Serve HTTPS is not configured yet (HTTPS Certificates not yet enabled on tailnet).";
            }
            else
            {
                dto.StatusMessage = "Tailscale HTTPS certificates are ready — click 'Enable Tailscale HTTPS' to expose Housekeeping for Android/iOS PWA install.";
            }

            return dto;
        }

        public async Task<TailscalePwaStatusDto> EnableHttpsServeAsync(int localPort, CancellationToken ct = default)
        {
            if (localPort <= 0 || localPort > 65535)
                localPort = 5178;

            ClearPendingProcessIfFinished(forceKill: true);

            var psi = new ProcessStartInfo
            {
                FileName = "tailscale",
                Arguments = $"serve --bg --yes http://127.0.0.1:{localPort}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process proc;
            try
            {
                proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start tailscale process.");
            }
            catch (Exception ex)
            {
                return new TailscalePwaStatusDto
                {
                    TailscaleAvailable = false,
                    StatusMessage = $"Failed to invoke tailscale CLI: {ex.Message}"
                };
            }

            var outputBuffer = new StringBuilder();
            var authUrlTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            void HandleLine(string? line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    return;
                lock (outputBuffer)
                {
                    outputBuffer.AppendLine(line);
                }
                var m = UrlRegex.Match(line);
                if (m.Success)
                {
                    authUrlTcs.TrySetResult(m.Value.Trim());
                }
            }

            proc.OutputDataReceived += (_, e) => HandleLine(e.Data);
            proc.ErrorDataReceived += (_, e) => HandleLine(e.Data);
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            Task exitTask = proc.WaitForExitAsync(timeoutCts.Token);
            Task completed = await Task.WhenAny(exitTask, authUrlTcs.Task);

            if (completed == authUrlTcs.Task)
            {
                string authUrl = await authUrlTcs.Task;
                RegisterPendingAuthProcess(proc, authUrl);

                var pendingStatus = await GetStatusAsync(localPort, ct);
                pendingStatus.PendingAuthUrl = authUrl;
                return pendingStatus;
            }

            try
            {
                await exitTask;
                proc.Dispose();
            }
            catch (OperationCanceledException)
            {
                // Still running without printing an auth link yet — check collected buffer
                string collected;
                lock (outputBuffer) { collected = outputBuffer.ToString(); }
                var m = UrlRegex.Match(collected);
                if (m.Success)
                {
                    RegisterPendingAuthProcess(proc, m.Value.Trim());
                }
                else
                {
                    try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[TailscalePwaService] Non-fatal error ignored: {__suppressedEx1.Message}"); }
                    proc.Dispose();
                }
            }

            var status = await GetStatusAsync(localPort, ct);
            if (!status.ServeConfigured && string.IsNullOrWhiteSpace(status.PendingAuthUrl))
            {
                string raw;
                lock (outputBuffer) { raw = outputBuffer.ToString().Trim(); }
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    status.StatusMessage = raw.Split('\n').FirstOrDefault()?.Trim() ?? status.StatusMessage;
                }
            }
            return status;
        }

        public async Task<TailscalePwaStatusDto> DisableHttpsServeAsync(int localPort, CancellationToken ct = default)
        {
            ClearPendingProcessIfFinished(forceKill: true);
            await RunCliBoundedAsync("serve reset", 5000, ct);
            return await GetStatusAsync(localPort, ct);
        }

        private void RegisterPendingAuthProcess(Process proc, string authUrl)
        {
            lock (_gate)
            {
                _pendingAuthProcess = proc;
                _pendingAuthUrl = authUrl;
                _pendingStartedUtc = DateTime.UtcNow;
            }

            // Auto-expire background waiter after 10 minutes so no orphan process can linger indefinitely
            _ = Task.Run(async () =>
            {
                try
                {
                    using var expiry = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                    await proc.WaitForExitAsync(expiry.Token);
                }
                catch
                {
                    try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch (System.Exception __suppressedEx2) { System.Diagnostics.Debug.WriteLine($"[TailscalePwaService] Non-fatal error ignored: {__suppressedEx2.Message}"); }
                }
                finally
                {
                    ClearPendingProcessIfFinished(forceKill: false);
                }
            });
        }

        private void ClearPendingProcessIfFinished(bool forceKill)
        {
            lock (_gate)
            {
                if (_pendingAuthProcess == null)
                    return;

                bool expired = (DateTime.UtcNow - _pendingStartedUtc) > TimeSpan.FromMinutes(10);
                try
                {
                    if (forceKill || expired)
                    {
                        if (!_pendingAuthProcess.HasExited)
                            _pendingAuthProcess.Kill(entireProcessTree: true);
                    }

                    if (_pendingAuthProcess.HasExited || forceKill || expired)
                    {
                        _pendingAuthProcess.Dispose();
                        _pendingAuthProcess = null;
                        _pendingAuthUrl = "";
                    }
                }
                catch
                {
                    _pendingAuthProcess = null;
                    _pendingAuthUrl = "";
                }
            }
        }

        private static async Task<(bool Ok, string StdOut, string StdErr)> RunCliBoundedAsync(
            string arguments,
            int timeoutMs,
            CancellationToken ct)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "tailscale",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                    return (false, "", "Failed to start tailscale CLI.");

                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linked.CancelAfter(timeoutMs);

                try
                {
                    Task<string> outTask = proc.StandardOutput.ReadToEndAsync(linked.Token);
                    Task<string> errTask = proc.StandardError.ReadToEndAsync(linked.Token);
                    await proc.WaitForExitAsync(linked.Token);

                    string stdOut = await outTask;
                    string stdErr = await errTask;
                    return (proc.ExitCode == 0, stdOut, stdErr);
                }
                catch (OperationCanceledException)
                {
                    try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch (System.Exception __suppressedEx3) { System.Diagnostics.Debug.WriteLine($"[TailscalePwaService] Non-fatal error ignored: {__suppressedEx3.Message}"); }
                    return (false, "", "tailscale CLI command timed out.");
                }
            }
            catch (Exception ex)
            {
                return (false, "", ex.Message);
            }
        }
    }
}
