using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Models;

namespace Housekeeping.Services
{
    public sealed class GreenroomStatusDto
    {
        public bool Enabled { get; set; }
        public bool CompanionOnline { get; set; }
        public string CompanionBaseUrl { get; set; } = "";
        public string BrowserCompanionUrl { get; set; } = "";
        public bool CliFallbackAvailable { get; set; }
        public string CliScriptPath { get; set; } = "";
        public string StatusMessage { get; set; } = "";
        public DateTime? LastAutoSyncUtc { get; set; }
        public string LastAutoSyncSummary { get; set; } = "No auto-sync triggered in this session yet.";
    }

    public sealed class GreenroomSyncResult
    {
        public bool Triggered { get; set; }
        public string MethodUsed { get; set; } = "None"; // "CompanionHttp" | "CliBatchFallback" | "Skipped"
        public List<string> TasksRequested { get; set; } = new();
        public string Summary { get; set; } = "";
    }

    /// <summary>
    /// Integrates Housekeeping 3.0 with sister application Greenroom Companion (:8420),
    /// automatically triggering non-blocking catalog/hash ingestion when ROM or Collection manifests change.
    /// </summary>
    public sealed class GreenroomSyncIntegrationService : IDisposable
    {
        private readonly ConfigService _configService;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;

        private DateTime? _lastAutoSyncUtc;
        private string _lastAutoSyncSummary = "No auto-sync triggered in this session yet.";

        public GreenroomSyncIntegrationService(ConfigService configService, HttpClient? httpClient = null)
        {
            _configService = configService;
            if (httpClient != null)
            {
                _httpClient = httpClient;
                _ownsHttpClient = false;
            }
            else
            {
                _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3.5) };
                _ownsHttpClient = true;
            }
        }

        public string ResolveBrowserCompanionUrl(string? requestHostHeader)
        {
            var gr = _configService.Config.GreenroomIntegration;
            int port = gr.CompanionPort > 0 ? gr.CompanionPort : 8420;

            if (!string.IsNullOrWhiteSpace(requestHostHeader))
            {
                string hostOnly = requestHostHeader.Trim();
                // Strip port from IPv6 "[::1]:5178" or IPv4/hostname "100.x.y.z:5178"
                if (hostOnly.StartsWith('['))
                {
                    int closeBracket = hostOnly.IndexOf(']');
                    if (closeBracket > 0)
                        hostOnly = hostOnly[..(closeBracket + 1)];
                }
                else
                {
                    int colon = hostOnly.IndexOf(':');
                    if (colon > 0)
                        hostOnly = hostOnly[..colon];
                }

                if (!string.IsNullOrWhiteSpace(hostOnly))
                {
                    return $"http://{hostOnly}:{port}";
                }
            }

            return string.IsNullOrWhiteSpace(gr.CompanionBaseUrl)
                ? $"http://127.0.0.1:{port}"
                : gr.CompanionBaseUrl.TrimEnd('/');
        }

        public string ResolveCliBatchPath()
        {
            var gr = _configService.Config.GreenroomIntegration;
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(gr.GreenroomRootPath))
            {
                candidates.Add(Path.Combine(gr.GreenroomRootPath, "scripts", "run_greenroom.bat"));
            }

            string? envRoot = Environment.GetEnvironmentVariable("GREENROOM_ROOT");
            if (!string.IsNullOrWhiteSpace(envRoot))
            {
                candidates.Add(Path.Combine(envRoot.Trim(), "scripts", "run_greenroom.bat"));
            }

            if (!_configService.IsTestMode)
            {
                string siblingGreenroom = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Greenroom"));
                candidates.Add(Path.Combine(siblingGreenroom, "scripts", "run_greenroom.bat"));
            }
            else if (candidates.Count == 0)
            {
                candidates.Add(Path.Combine(_configService.DataDirectory, "Greenroom", "scripts", "run_greenroom.bat"));
            }

            return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
        }

        public async Task<GreenroomStatusDto> GetStatusAsync(string? requestHostHeader = null, CancellationToken ct = default)
        {
            var gr = _configService.Config.GreenroomIntegration;
            string baseUrl = string.IsNullOrWhiteSpace(gr.CompanionBaseUrl)
                ? "http://127.0.0.1:8420"
                : gr.CompanionBaseUrl.TrimEnd('/');
            string cliPath = ResolveCliBatchPath();
            bool cliExists = File.Exists(cliPath);
            bool online = false;
            string msg;

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(1.8));
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/health");
                AttachAuthIfConfigured(req, gr);
                using var resp = await _httpClient.SendAsync(req, cts.Token).ConfigureAwait(false);
                online = resp.IsSuccessStatusCode;
                msg = online
                    ? $"Greenroom Companion online at {baseUrl}"
                    : $"Greenroom Companion returned HTTP {(int)resp.StatusCode}";
            }
            catch
            {
                msg = cliExists
                    ? $"Companion (:8420) offline — CLI fallback ready ({cliPath})"
                    : $"Companion (:8420) offline and CLI script not found at {cliPath}";
            }

            return new GreenroomStatusDto
            {
                Enabled = gr.Enabled,
                CompanionOnline = online,
                CompanionBaseUrl = baseUrl,
                BrowserCompanionUrl = ResolveBrowserCompanionUrl(requestHostHeader),
                CliFallbackAvailable = cliExists,
                CliScriptPath = cliPath,
                StatusMessage = msg,
                LastAutoSyncUtc = _lastAutoSyncUtc,
                LastAutoSyncSummary = _lastAutoSyncSummary
            };
        }

        public async Task<GreenroomSyncResult> TriggerPostManifestSyncAsync(
            ModuleId sourceModule,
            IReadOnlyList<string> changedManifestPaths,
            bool isDryRun,
            Action<string, string>? log = null,
            bool forceManual = false,
            CancellationToken ct = default)
        {
            var gr = _configService.Config.GreenroomIntegration;
            if (isDryRun)
            {
                return new GreenroomSyncResult
                {
                    Triggered = false,
                    MethodUsed = "Skipped",
                    Summary = "Dry-run active — skipped Greenroom post-manifest sync."
                };
            }

            if (!forceManual)
            {
                if (!gr.Enabled)
                {
                    return new GreenroomSyncResult { Triggered = false, MethodUsed = "Skipped", Summary = "Greenroom integration disabled." };
                }
                if (sourceModule == ModuleId.RomLauncherGenerator && !gr.AutoSyncAfterRomManifestChanges)
                {
                    return new GreenroomSyncResult { Triggered = false, MethodUsed = "Skipped", Summary = "ROM manifest Greenroom auto-sync disabled." };
                }
                if (sourceModule == ModuleId.CollectionManifest && !gr.AutoSyncAfterCollectionManifestChanges)
                {
                    return new GreenroomSyncResult { Triggered = false, MethodUsed = "Skipped", Summary = "Collection manifest Greenroom auto-sync disabled." };
                }
                if (changedManifestPaths == null || changedManifestPaths.Count == 0)
                {
                    log?.Invoke("INFO", "[Greenroom Sync] No manifest rows changed during this run — skipping Greenroom re-ingest.");
                    return new GreenroomSyncResult { Triggered = false, MethodUsed = "Skipped", Summary = "No manifest files changed." };
                }
            }

            var tasks = sourceModule == ModuleId.RomLauncherGenerator
                ? new List<string> { "collect_library", "verify_hashes" }
                : new List<string> { "collect_library", "collect_plex" };

            string baseUrl = string.IsNullOrWhiteSpace(gr.CompanionBaseUrl)
                ? "http://127.0.0.1:8420"
                : gr.CompanionBaseUrl.TrimEnd('/');

            // 1. Primary Path: Greenroom Companion HTTP REST trigger (/api/tasks/run)
            bool companionReachable = false;
            var triggeredHttpTasks = new List<string>();
            foreach (string taskId in tasks)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/tasks/run");
                    AttachAuthIfConfigured(req, gr);
                    string payload = JsonSerializer.Serialize(new { task_id = taskId, wait = false });
                    req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                    using var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
                    companionReachable = true;

                    if (resp.IsSuccessStatusCode)
                    {
                        triggeredHttpTasks.Add(taskId);
                        log?.Invoke("SUCCESS", $"[Greenroom Sync] Triggered Companion task '{taskId}' via {baseUrl}/api/tasks/run.");
                    }
                    else if ((int)resp.StatusCode == 409)
                    {
                        triggeredHttpTasks.Add(taskId);
                        log?.Invoke("INFO", $"[Greenroom Sync] Companion task '{taskId}' is already running (HTTP 409).");
                    }
                    else
                    {
                        log?.Invoke("WARN", $"[Greenroom Sync] Companion returned HTTP {(int)resp.StatusCode} for task '{taskId}'.");
                    }
                }
                catch (Exception) when (!companionReachable)
                {
                    // Companion offline — break out to CLI fallback
                    break;
                }
                catch (Exception ex)
                {
                    log?.Invoke("WARN", $"[Greenroom Sync] Error triggering '{taskId}' on Companion: {ex.Message}");
                }
            }

            if (companionReachable && triggeredHttpTasks.Count > 0)
            {
                string summary = $"Triggered Greenroom Companion ({string.Join(", ", triggeredHttpTasks)}) at {DateTime.Now:HH:mm:ss}.";
                _lastAutoSyncUtc = DateTime.UtcNow;
                _lastAutoSyncSummary = summary;
                return new GreenroomSyncResult
                {
                    Triggered = true,
                    MethodUsed = "CompanionHttp",
                    TasksRequested = triggeredHttpTasks,
                    Summary = summary
                };
            }

            // 2. Fallback Path: run_greenroom.bat CLI execution
            string cliScript = ResolveCliBatchPath();
            if (File.Exists(cliScript))
            {
                log?.Invoke("INFO", $"[Greenroom Sync] Companion (:8420) offline — falling back to CLI '{cliScript}'...");
                _ = Task.Run(() =>
                {
                    foreach (string taskId in tasks)
                    {
                        try
                        {
                            using var p = Process.Start(new ProcessStartInfo
                            {
                                FileName = "cmd.exe",
                                Arguments = $"/d /s /c \"\"{cliScript}\" run {taskId}\"",
                                WorkingDirectory = Path.GetDirectoryName(cliScript) ?? AppDomain.CurrentDomain.BaseDirectory,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            });
                            p?.WaitForExit(180_000);
                        }
                        catch (Exception cliEx)
                        {
                            log?.Invoke("WARN", $"[Greenroom Sync] CLI fallback error for '{taskId}': {cliEx.Message}");
                        }
                    }
                }, CancellationToken.None);

                string summary = $"Launched Greenroom CLI fallback ({string.Join(", ", tasks)}) at {DateTime.Now:HH:mm:ss}.";
                _lastAutoSyncUtc = DateTime.UtcNow;
                _lastAutoSyncSummary = summary;
                return new GreenroomSyncResult
                {
                    Triggered = true,
                    MethodUsed = "CliBatchFallback",
                    TasksRequested = tasks,
                    Summary = summary
                };
            }

            log?.Invoke("WARN", $"[Greenroom Sync] Companion ({baseUrl}) is offline and fallback script '{cliScript}' was not found.");
            return new GreenroomSyncResult
            {
                Triggered = false,
                MethodUsed = "Skipped",
                TasksRequested = tasks,
                Summary = "Greenroom Companion offline and CLI fallback unavailable."
            };
        }

        private static void AttachAuthIfConfigured(HttpRequestMessage req, GreenroomIntegrationConfig gr)
        {
            if (!string.IsNullOrWhiteSpace(gr.CompanionBearerToken))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", gr.CompanionBearerToken.Trim());
            }
        }

        public void Dispose()
        {
            if (_ownsHttpClient)
            {
                try { _httpClient.Dispose(); } catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[GreenroomSyncIntegrationService] Non-fatal error ignored: {__suppressedEx1.Message}"); }
            }
        }
    }
}
