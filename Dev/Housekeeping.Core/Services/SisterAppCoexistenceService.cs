// @module: Housekeeping.Core/Services/SisterAppCoexistenceService
// @purpose: Hybrid HTTP + .coexistence.json (Standard 19 v2) discovery, overlap conflict detection, and one-click preset alignment across Sister Apps.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, System.Linq, System.Net.Http, System.Text.Json, Housekeeping.Models
// @exports: SisterAppCoexistenceService
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Models;

namespace Housekeeping.Services
{
    public sealed class SisterAppCoexistenceService
    {
        private readonly ConfigService _configService;
        private readonly HttpClient _httpClient;
        private readonly Action<string, string>? _log;

        private static readonly (string AppId, string Name, int DefaultPort, string ProcessName)[] KnownPeers =
        {
            ("shadowlibrary", "ShadowLibrary", 5179, "ShadowLibrary"),
            ("redroom", "Redroom", 5180, "Redroom"),
            ("ustation", "uStation", 8460, "uStation"),
            ("streamengine", "StreamEngine", 8430, "StreamEngine"),
            ("greenroom", "Greenroom", 8420, "Greenroom")
        };

        public SisterAppCoexistenceService(ConfigService configService, HttpClient httpClient, Action<string, string>? log = null)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _log = log;
        }

        public async Task<CoexistenceStatusDto> GetStatusAsync(CancellationToken ct = default)
        {
            var cfg = _configService.Config;
            int port = cfg.WebServer?.Port > 0 ? cfg.WebServer.Port : 5178;
            var status = new CoexistenceStatusDto
            {
                Version = RemoteNodeDeploymentService.GetLocalAppVersion(),
                Port = port,
                UpdatedAtUtc = DateTime.UtcNow.ToString("O"),
                ShouldAutoAlignSisterApps = cfg.AutoAlignWithSisterApps,
                CoexistenceMode = cfg.CollectionManifest.SkipShadowLibraryManagedRoots ? "Delegated" : "Co-Audit (Zero-Rewrite)"
            };

            var roots = CollectManagedRoots(cfg);
            status.ManagedRoots = roots;

            var peerMap = new Dictionary<string, CoexistencePeerSummaryDto>(StringComparer.OrdinalIgnoreCase);
            if (!_configService.IsTestMode)
            {
                var tasks = KnownPeers.Select(p => ProbePeerHttpAsync(p.AppId, p.Name, ResolvePeerPort(cfg, p.AppId, p.DefaultPort), p.ProcessName, ct));
                foreach (var peer in await Task.WhenAll(tasks).ConfigureAwait(false))
                    if (peer != null) peerMap[peer.AppId] = peer;
            }

            int activeLockCount = 0;
            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root.Path) || !Directory.Exists(root.Path)) continue;

                if (NetworkSafetyService.HasActiveDirectoryOrParentLock(root.Path, out DirectorySentinelLockPayload? activeLock, scanImmediateSubdirectories: true) && activeLock != null)
                {
                    activeLockCount++;
                    if (!string.Equals(activeLock.AppId, "housekeeping", StringComparison.OrdinalIgnoreCase))
                    {
                        status.DetectedConflicts.Add(new CoexistenceConflictDto
                        {
                            ConflictId = $"lock:{activeLock.AppId}:{root.NormalizedPath}",
                            SisterAppId = activeLock.AppId,
                            SisterAppName = ResolveDisplayName(activeLock.AppId),
                            SharedPath = root.Path,
                            JobId = root.JobId,
                            JobName = root.JobName,
                            ModuleType = root.ModuleType,
                            Severity = "info",
                            Summary = $"Active '{activeLock.Operation}' sentinel lock held by {activeLock.AppId} (PID {activeLock.Pid}). Housekeeping automatically defers.",
                            RecommendedPresets = new List<string> { "co_audit_zero_rewrite" }
                        });
                    }
                }

                var manifest = TryReadRootCoexistenceManifest(root.Path);
                if (manifest == null) continue;

                foreach (var kvp in manifest.Owners)
                {
                    string ownerId = string.IsNullOrWhiteSpace(kvp.Value.AppId) ? kvp.Key.ToLowerInvariant() : kvp.Value.AppId.ToLowerInvariant();
                    if (string.Equals(ownerId, "housekeeping", StringComparison.OrdinalIgnoreCase)) continue;

                    if (!peerMap.TryGetValue(ownerId, out var peerDto))
                    {
                        peerDto = new CoexistencePeerSummaryDto
                        {
                            AppId = ownerId,
                            Name = ResolveDisplayName(ownerId),
                            Port = kvp.Value.Port,
                            IsOnline = false,
                            Version = string.IsNullOrWhiteSpace(kvp.Value.Version) ? "manifest" : kvp.Value.Version
                        };
                        peerMap[ownerId] = peerDto;
                    }
                    if (!peerDto.SharedRoots.Contains(root.Path, StringComparer.OrdinalIgnoreCase))
                        peerDto.SharedRoots.Add(root.Path);

                    EvaluateManifestConflict(root, ownerId, kvp.Value, status.DetectedConflicts);
                }
            }

            status.Peers = peerMap.Values.OrderBy(p => p.AppId, StringComparer.OrdinalIgnoreCase).ToList();
            status.ActiveHkLocksCount = activeLockCount;
            status.HasActiveConflicts = status.DetectedConflicts.Any(c => string.Equals(c.Severity, "warning", StringComparison.OrdinalIgnoreCase));
            return status;
        }

        public static SharedRootCoexistenceManifest? TryReadRootCoexistenceManifest(string rootPath)
        {
            try
            {
                string path = Path.Combine(rootPath, ".coexistence.json");
                if (!File.Exists(path)) return null;
                return JsonSerializer.Deserialize<SharedRootCoexistenceManifest>(ConfigService.ReadAllTextWithRetry(path), ConfigService.JsonOptions);
            }
            catch (Exception ex) { Debug.WriteLine($"[SisterAppCoexistenceService] Read .coexistence.json skipped ({rootPath}): {ex.Message}"); return null; }
        }

        public static bool IsShadowLibraryManagedRoot(string rootPath) =>
            TryReadRootCoexistenceManifest(rootPath)?.Owners.Keys.Any(k => string.Equals(k, "shadowlibrary", StringComparison.OrdinalIgnoreCase)) == true;

        public static void WriteOrUpdateRootCoexistenceManifest(string rootPath, string coexistenceMode, string hashPlacementMode, bool syncContainerJson = true, IEnumerable<string>? modules = null)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath)) return;
            string now = DateTime.UtcNow.ToString("O");
            string ver = RemoteNodeDeploymentService.GetLocalAppVersion();
            var manifest = TryReadRootCoexistenceManifest(rootPath) ?? new SharedRootCoexistenceManifest();
            manifest.SchemaVersion = 2;
            manifest.AppId = "housekeeping";
            manifest.Version = ver;
            manifest.UpdatedAtUtc = now;
            manifest.CoexistenceMode = coexistenceMode;
            manifest.HashFilePlacement = hashPlacementMode;
            manifest.SyncPostInjectionContainerHashJson = syncContainerJson;
            manifest.Owners["housekeeping"] = new SharedRootOwnerEntry
            {
                AppId = "housekeeping",
                Version = ver,
                Port = 5178,
                UpdatedAtUtc = now,
                HashFilePlacement = hashPlacementMode,
                CoexistenceMode = coexistenceMode,
                Modules = modules?.ToList() ?? new List<string> { "collection-manifest" }
            };
            ConfigService.AtomicWriteAllText(Path.Combine(rootPath, ".coexistence.json"), JsonSerializer.Serialize(manifest, ConfigService.JsonOptions));
        }

        public (bool Ok, string Message) ApplyRecommendationPreset(string presetId, bool? enableAutoAlign = null, string? jobId = null)
        {
            string norm = (presetId ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(norm)) return (false, "Preset identifier is required.");

            _configService.Save(); // Capture pre-mutation snapshot for 1-click rollback
            bool saved = _configService.MutateAndSave(cfg =>
            {
                if (enableAutoAlign.HasValue) cfg.AutoAlignWithSisterApps = enableAutoAlign.Value;
                var mcs = EnumerateCollectionManifestConfigs(cfg, jobId).ToList();
                var rcs = EnumerateRomConfigs(cfg, jobId).ToList();
                var ccs = EnumerateCloudConfigs(cfg, jobId).ToList();

                switch (norm)
                {
                    case "delegate_to_shadowlibrary":
                        foreach (var mc in mcs) mc.SkipShadowLibraryManagedRoots = true;
                        foreach (var rc in rcs) rc.EnableCollectionManifestCsv = false;
                        break;
                    case "co_audit_zero_rewrite":
                        foreach (var mc in mcs)
                        {
                            mc.SkipShadowLibraryManagedRoots = false;
                            mc.TrustExistingHashFiles = true;
                            mc.SkipExistingVerifiedHashes = true;
                            mc.SyncPostInjectionContainerHashJson = true;
                        }
                        break;
                    case "align_cloud_sync":
                        cfg.EnableDistributedNetworkLocking = true;
                        foreach (var cc in ccs) foreach (var j in cc.Jobs) j.Flags = EnsureCoexistenceRcloneExcludes(j.Flags);
                        break;
                    case "align_ustation_saves_and_launchers":
                        cfg.EnableDistributedNetworkLocking = true;
                        foreach (var rc in rcs) { rc.EnableCollectionManifestCsv = false; rc.SanitizeAutoGeneratedCommandOverrides(); }
                        break;
                    default:
                        throw new ArgumentException($"Unknown coexistence preset '{presetId}'.");
                }
            }, out string? err);

            if (!saved) return (false, err ?? "Failed to apply coexistence preset.");
            string msg = $"Applied coexistence preset '{norm}'. Prior configuration preserved in Settings Snapshots.";
            _log?.Invoke("SUCCESS", $"[Coexistence] {msg}");
            return (true, msg);
        }

        public static string EnsureCoexistenceRcloneExcludes(string? existingFlags)
        {
            string flags = (existingFlags ?? "").Trim();
            string[] patterns = { "*.hklock", "*.partial*", "*.redroom-part", ".coexistence.json", ".archive_hashes.json" };
            var sb = new StringBuilder(flags);
            foreach (string pat in patterns)
            {
                if (!flags.Contains(pat, StringComparison.OrdinalIgnoreCase))
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append($"--exclude \"{pat}\"");
                }
            }
            return sb.ToString().Trim();
        }

        private async Task<CoexistencePeerSummaryDto?> ProbePeerHttpAsync(string appId, string name, int port, string procName, CancellationToken ct)
        {
            bool online = false;
            string version = "";
            foreach (string path in new[] { "/api/v1/coexistence/status", "/api/v1/health" })
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromMilliseconds(300));
                    using var resp = await _httpClient.GetAsync($"http://127.0.0.1:{port}{path}", cts.Token).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode) continue;
                    online = true;
                    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false));
                    if (doc.RootElement.TryGetProperty("version", out var vp) && vp.ValueKind == JsonValueKind.String) version = vp.GetString() ?? "";
                    break;
                }
                catch (Exception ex) { Debug.WriteLine($"[SisterAppCoexistenceService] Peer HTTP probe ({appId}:{port}{path}) skipped: {ex.Message}"); }
            }

            if (!online)
            {
                try { online = Process.GetProcessesByName(procName).Length > 0; }
                catch (Exception ex) { Debug.WriteLine($"[SisterAppCoexistenceService] Process probe ({procName}) skipped: {ex.Message}"); }
                if (!online) return null;
            }
            return new CoexistencePeerSummaryDto { AppId = appId, Name = name, Port = port, IsOnline = online, Version = string.IsNullOrWhiteSpace(version) ? "online" : version };
        }

        private static int ResolvePeerPort(AppConfig cfg, string appId, int fallbackPort) =>
            string.Equals(appId, "greenroom", StringComparison.OrdinalIgnoreCase) && cfg.GreenroomIntegration?.CompanionPort > 0 ? cfg.GreenroomIntegration.CompanionPort : fallbackPort;

        private static string ResolveDisplayName(string appId) =>
            KnownPeers.FirstOrDefault(p => string.Equals(p.AppId, appId, StringComparison.OrdinalIgnoreCase)).Name ?? appId;

        private static List<CoexistenceManagedRootDto> CollectManagedRoots(AppConfig cfg)
        {
            var list = new List<CoexistenceManagedRootDto>();
            void AddManifestRoots(CollectionManifestConfig mc, string jobId, string jobName)
            {
                foreach (var t in mc.TargetFolders.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.FolderPath)))
                    list.Add(new CoexistenceManagedRootDto { Path = t.FolderPath, NormalizedPath = t.FolderPath.TrimEnd('\\', '/'), JobId = jobId, JobName = jobName, ModuleType = "CollectionManifest", WritesHashSidecars = mc.CreateMissingHashFiles, HashFilePlacement = mc.HashFilePlacement.ToString(), DeepHashInnerRoms = mc.CheckInnerArchiveAndContainer, WritesCollectionManifestCsv = mc.ExportCsv, DelegatesRomVaultsToShadowLibrary = mc.SkipShadowLibraryManagedRoots, SyncPostInjectionContainerHashJson = mc.SyncPostInjectionContainerHashJson, CleanDisallowedHashFilesOnMigrate = mc.CleanDisallowedHashFilesOnMigrate });
            }
            if (cfg.CollectionManifest != null) AddManifestRoots(cfg.CollectionManifest, "", "Default Collection Manifest");
            foreach (var j in cfg.DefinedJobs.Where(x => x.ModuleType == ModuleId.CollectionManifest && x.CollectionManifest != null)) AddManifestRoots(j.CollectionManifest!, j.JobId, j.Name);
            foreach (var j in cfg.DefinedJobs.Where(x => x.ModuleType == ModuleId.RomLauncherGenerator && x.RomLauncherGenerator != null))
                foreach (var c in j.RomLauncherGenerator!.Collections.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.ArchivesPath)))
                    list.Add(new CoexistenceManagedRootDto { Path = c.ArchivesPath, NormalizedPath = c.ArchivesPath.TrimEnd('\\', '/'), JobId = j.JobId, JobName = j.Name, ModuleType = "RomLauncherGenerator" });
            foreach (var j in cfg.DefinedJobs.Where(x => x.ModuleType == ModuleId.CloudSync && x.CloudSync != null))
                foreach (var s in j.CloudSync!.Jobs.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.LocalPath)))
                    list.Add(new CoexistenceManagedRootDto { Path = s.LocalPath, NormalizedPath = s.LocalPath.TrimEnd('\\', '/'), JobId = j.JobId, JobName = j.Name, ModuleType = "CloudSync" });
            return list;
        }

        private static void EvaluateManifestConflict(CoexistenceManagedRootDto root, string peerId, SharedRootOwnerEntry owner, List<CoexistenceConflictDto> conflicts)
        {
            if (root.ModuleType == "CollectionManifest" && string.Equals(peerId, "shadowlibrary", StringComparison.OrdinalIgnoreCase) && !root.DelegatesRomVaultsToShadowLibrary && root.WritesHashSidecars && !string.Equals(root.HashFilePlacement, owner.HashFilePlacement ?? "SidecarBeside", StringComparison.OrdinalIgnoreCase))
            {
                conflicts.Add(new CoexistenceConflictDto { ConflictId = $"placement:{peerId}:{root.NormalizedPath}", SisterAppId = peerId, SisterAppName = "ShadowLibrary", SharedPath = root.Path, JobId = root.JobId, JobName = root.JobName, ModuleType = root.ModuleType, Severity = "warning", Summary = $"Hash placement mismatch on '{root.Path}' (Housekeeping: {root.HashFilePlacement} vs ShadowLibrary: {owner.HashFilePlacement}).", RecommendedPresets = new List<string> { "delegate_to_shadowlibrary", "co_audit_zero_rewrite" } });
            }
            else if (root.ModuleType == "CloudSync")
            {
                conflicts.Add(new CoexistenceConflictDto { ConflictId = $"cloud:{peerId}:{root.NormalizedPath}", SisterAppId = peerId, SisterAppName = ResolveDisplayName(peerId), SharedPath = root.Path, JobId = root.JobId, JobName = root.JobName, ModuleType = root.ModuleType, Severity = "warning", Summary = $"Cloud Sync overlaps {ResolveDisplayName(peerId)}-managed root '{root.Path}'. Ensure transient .hklock/.partial* exclusions are active.", RecommendedPresets = new List<string> { "align_cloud_sync" } });
            }
        }

        private static IEnumerable<CollectionManifestConfig> EnumerateCollectionManifestConfigs(AppConfig cfg, string? jobId) =>
            (string.IsNullOrWhiteSpace(jobId) ? new[] { cfg.CollectionManifest }.Where(x => x != null)! : Array.Empty<CollectionManifestConfig>())
            .Concat(cfg.DefinedJobs.Where(j => j.ModuleType == ModuleId.CollectionManifest && j.CollectionManifest != null && (string.IsNullOrWhiteSpace(jobId) || j.JobId == jobId)).Select(j => j.CollectionManifest!));

        private static IEnumerable<RomLauncherGeneratorConfig> EnumerateRomConfigs(AppConfig cfg, string? jobId) =>
            (string.IsNullOrWhiteSpace(jobId) ? new[] { cfg.RomLauncherGenerator }.Where(x => x != null)! : Array.Empty<RomLauncherGeneratorConfig>())
            .Concat(cfg.DefinedJobs.Where(j => j.ModuleType == ModuleId.RomLauncherGenerator && j.RomLauncherGenerator != null && (string.IsNullOrWhiteSpace(jobId) || j.JobId == jobId)).Select(j => j.RomLauncherGenerator!));

        private static IEnumerable<CloudSyncConfig> EnumerateCloudConfigs(AppConfig cfg, string? jobId) =>
            (string.IsNullOrWhiteSpace(jobId) ? new[] { cfg.CloudSync }.Where(x => x != null)! : Array.Empty<CloudSyncConfig>())
            .Concat(cfg.DefinedJobs.Where(j => j.ModuleType == ModuleId.CloudSync && j.CloudSync != null && (string.IsNullOrWhiteSpace(jobId) || j.JobId == jobId)).Select(j => j.CloudSync!));
    }
}
