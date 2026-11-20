// @module: Housekeeping.Core/Services/SisterAppCoexistenceModels
// @purpose: Canonical Standard 18/19 DTOs for .hklock sentinels, .coexistence.json v2 manifests, .archive_hashes.json registries, and peer status.
// @depends-on: System.Diagnostics, System.IO, System.Text, System.Text.Json
// @exports: DirectorySentinelLockPayload, DirectorySentinelLockHandle, DistributedLockInfo, DistributedFileLock, CoexistenceStatusDto, ArchiveHashesRegistryDocument
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Housekeeping.Services
{
    public class DistributedLockInfo
    {
        public string MachineName { get; set; } = Environment.MachineName;
        public string UserName { get; set; } = Environment.UserName;
        public int ProcessId { get; set; } = Environment.ProcessId;
        public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime HeartbeatAtUtc { get; set; } = DateTime.UtcNow;
        public string TargetFilePath { get; set; } = "";
    }

    public sealed class DistributedFileLock : IDisposable
    {
        private readonly string _lockFilePath;
        private readonly Action<string, string> _log;
        private bool _disposed;

        public DistributedFileLock(string lockFilePath, Action<string, string> log)
        {
            _lockFilePath = lockFilePath;
            _log = log;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { if (File.Exists(_lockFilePath)) File.Delete(_lockFilePath); }
            catch (Exception ex) { _log("WARN", $"Failed to release distributed lock '{Path.GetFileName(_lockFilePath)}': {ex.Message}"); }
        }
    }

    public sealed class DirectorySentinelLockPayload
    {
        [JsonPropertyName("$schema")] public string Schema { get; set; } = "https://schemas.local/lockfile.v1.json";
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;
        [JsonPropertyName("lockType")] public string LockType { get; set; } = "directory-mutation";
        [JsonPropertyName("suiteId")] public string SuiteId { get; set; } = "mother";
        [JsonPropertyName("ownerAppId")] public string OwnerAppId { get; set; } = "housekeeping";
        [JsonPropertyName("version")] public string Version { get; set; } = "0.2.0";
        [JsonPropertyName("pid")] public int Pid { get; set; }
        [JsonPropertyName("processStartTimeUtc")] public string? ProcessStartTimeUtc { get; set; }
        [JsonPropertyName("hostname")] public string Hostname { get; set; } = Environment.MachineName;
        [JsonPropertyName("port")] public int Port { get; set; } = 5178;
        [JsonPropertyName("operation")] public string Operation { get; set; } = "directory-scan";
        [JsonPropertyName("targetPath")] public string TargetPath { get; set; } = "";
        [JsonPropertyName("acquiredAtUtc")] public string AcquiredAtUtc { get; set; } = "";
        [JsonPropertyName("heartbeatAtUtc")] public string HeartbeatAtUtc { get; set; } = "";
        [JsonPropertyName("expiresAtUtc")] public string ExpiresAtUtc { get; set; } = "";
        [JsonPropertyName("app")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? LegacyApp { get; set; }
        [JsonPropertyName("host")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? LegacyHost { get; set; }
        [JsonIgnore] public string AppId => !string.IsNullOrWhiteSpace(OwnerAppId) ? OwnerAppId : (LegacyApp ?? "sister-app");
    }

    public sealed class DirectorySentinelLockHandle : IDisposable
    {
        private static readonly JsonSerializerOptions LockJsonOpts = new() { WriteIndented = true };
        private readonly string _lockFilePath;
        private readonly DirectorySentinelLockPayload _payload;
        private readonly TimeSpan _leaseDuration;
        private bool _disposed;

        public string LockFilePath => _lockFilePath;

        internal DirectorySentinelLockHandle(string lockFilePath, DirectorySentinelLockPayload payload, TimeSpan leaseDuration)
        {
            _lockFilePath = lockFilePath;
            _payload = payload;
            _leaseDuration = leaseDuration;
        }

        public void RefreshHeartbeat()
        {
            if (_disposed) return;
            try
            {
                var now = DateTime.UtcNow;
                _payload.HeartbeatAtUtc = now.ToString("O");
                _payload.ExpiresAtUtc = now.Add(_leaseDuration).ToString("O");
                File.WriteAllText(_lockFilePath, JsonSerializer.Serialize(_payload, LockJsonOpts), new UTF8Encoding(false));
            }
            catch (Exception ex) { Debug.WriteLine($"[DirectorySentinelLockHandle] Heartbeat refresh skipped: {ex.Message}"); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            NetworkSafetyService.TryDeleteFile(_lockFilePath);
        }
    }

    public sealed class ArchiveContainerHashRecord
    {
        [JsonPropertyName("fileName")] public string FileName { get; set; } = "";
        [JsonPropertyName("containerSize")] public long ContainerSize { get; set; }
        [JsonPropertyName("lastWriteUtc")] public string LastWriteUtc { get; set; } = "";
        [JsonPropertyName("containerCrc32")] public string ContainerCrc32 { get; set; } = "";
        [JsonPropertyName("containerMd5")] public string ContainerMd5 { get; set; } = "";
        [JsonPropertyName("containerSha1")] public string ContainerSha1 { get; set; } = "";
        [JsonPropertyName("hasInternalHashSidecar")] public bool HasInternalHashSidecar { get; set; }
        [JsonPropertyName("internalSidecarPath")] public string InternalSidecarPath { get; set; } = "";
        [JsonPropertyName("primaryInnerFileName")] public string PrimaryInnerFileName { get; set; } = "";
        [JsonPropertyName("primaryInnerSize")] public long PrimaryInnerSize { get; set; }
        [JsonPropertyName("primaryInnerCrc32")] public string PrimaryInnerCrc32 { get; set; } = "";
        [JsonPropertyName("primaryInnerMd5")] public string PrimaryInnerMd5 { get; set; } = "";
        [JsonPropertyName("primaryInnerSha1")] public string PrimaryInnerSha1 { get; set; } = "";
    }

    public sealed class ArchiveHashesRegistryDocument
    {
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;
        [JsonPropertyName("updatedAtUtc")] public string UpdatedAtUtc { get; set; } = "";
        [JsonPropertyName("updatedByApp")] public string UpdatedByApp { get; set; } = "housekeeping";
        [JsonPropertyName("updatedByVersion")] public string UpdatedByVersion { get; set; } = "0.2.0";
        [JsonPropertyName("hashFilePlacement")] public string HashFilePlacement { get; set; } = "SidecarBeside";
        [JsonPropertyName("archives")] public Dictionary<string, ArchiveContainerHashRecord> Archives { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class SharedRootOwnerEntry
    {
        [JsonPropertyName("appId")] public string AppId { get; set; } = "";
        [JsonPropertyName("version")] public string Version { get; set; } = "0.2.0";
        [JsonPropertyName("port")] public int Port { get; set; }
        [JsonPropertyName("updatedAtUtc")] public string UpdatedAtUtc { get; set; } = "";
        [JsonPropertyName("modules")] public List<string> Modules { get; set; } = new();
        [JsonPropertyName("hashFilePlacement")] public string? HashFilePlacement { get; set; }
        [JsonPropertyName("coexistenceMode")] public string? CoexistenceMode { get; set; }
    }

    public sealed class SharedRootCoexistenceManifest
    {
        [JsonPropertyName("$schema")] public string Schema { get; set; } = "https://schemas.local/manifest.v2.json";
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 2;
        [JsonPropertyName("manifestType")] public string ManifestType { get; set; } = "shared-root-coexistence";
        [JsonPropertyName("appId")] public string AppId { get; set; } = "housekeeping";
        [JsonPropertyName("version")] public string Version { get; set; } = "0.2.0";
        [JsonPropertyName("updatedAtUtc")] public string UpdatedAtUtc { get; set; } = "";
        [JsonPropertyName("coexistenceMode")] public string CoexistenceMode { get; set; } = "Co-Audit (Zero-Rewrite)";
        [JsonPropertyName("hashFilePlacement")] public string HashFilePlacement { get; set; } = "SidecarBeside";
        [JsonPropertyName("syncPostInjectionContainerHashJson")] public bool SyncPostInjectionContainerHashJson { get; set; } = true;
        [JsonPropertyName("owners")] public Dictionary<string, SharedRootOwnerEntry> Owners { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class CoexistenceManagedRootDto
    {
        [JsonPropertyName("path")] public string Path { get; set; } = "";
        [JsonPropertyName("normalizedPath")] public string NormalizedPath { get; set; } = "";
        [JsonPropertyName("jobId")] public string JobId { get; set; } = "";
        [JsonPropertyName("jobName")] public string JobName { get; set; } = "";
        [JsonPropertyName("moduleType")] public string ModuleType { get; set; } = "";
        [JsonPropertyName("writesHashSidecars")] public bool WritesHashSidecars { get; set; }
        [JsonPropertyName("hashFilePlacement")] public string HashFilePlacement { get; set; } = "SidecarBeside";
        [JsonPropertyName("deepHashInnerRoms")] public bool DeepHashInnerRoms { get; set; }
        [JsonPropertyName("writesCollectionManifestCsv")] public bool WritesCollectionManifestCsv { get; set; }
        [JsonPropertyName("delegatesRomVaultsToShadowLibrary")] public bool DelegatesRomVaultsToShadowLibrary { get; set; }
        [JsonPropertyName("syncPostInjectionContainerHashJson")] public bool SyncPostInjectionContainerHashJson { get; set; } = true;
        [JsonPropertyName("cleanDisallowedHashFilesOnMigrate")] public bool CleanDisallowedHashFilesOnMigrate { get; set; }
    }

    public sealed class CoexistencePeerSummaryDto
    {
        [JsonPropertyName("appId")] public string AppId { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("port")] public int Port { get; set; }
        [JsonPropertyName("isOnline")] public bool IsOnline { get; set; }
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("sharedRoots")] public List<string> SharedRoots { get; set; } = new();
    }

    public sealed class CoexistenceConflictDto
    {
        [JsonPropertyName("conflictId")] public string ConflictId { get; set; } = "";
        [JsonPropertyName("sisterAppId")] public string SisterAppId { get; set; } = "";
        [JsonPropertyName("sisterAppName")] public string SisterAppName { get; set; } = "";
        [JsonPropertyName("sharedPath")] public string SharedPath { get; set; } = "";
        [JsonPropertyName("jobId")] public string JobId { get; set; } = "";
        [JsonPropertyName("jobName")] public string JobName { get; set; } = "";
        [JsonPropertyName("moduleType")] public string ModuleType { get; set; } = "";
        [JsonPropertyName("severity")] public string Severity { get; set; } = "warning";
        [JsonPropertyName("summary")] public string Summary { get; set; } = "";
        [JsonPropertyName("recommendedPresets")] public List<string> RecommendedPresets { get; set; } = new();
    }

    public sealed class CoexistenceStatusDto
    {
        [JsonPropertyName("appId")] public string AppId { get; set; } = "housekeeping";
        [JsonPropertyName("suiteId")] public string SuiteId { get; set; } = "mother";
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 2;
        [JsonPropertyName("version")] public string Version { get; set; } = "0.2.0";
        [JsonPropertyName("port")] public int Port { get; set; } = 5178;
        [JsonPropertyName("updatedAtUtc")] public string UpdatedAtUtc { get; set; } = "";
        [JsonPropertyName("shouldAutoAlignSisterApps")] public bool ShouldAutoAlignSisterApps { get; set; }
        [JsonPropertyName("hasActiveConflicts")] public bool HasActiveConflicts { get; set; }
        [JsonPropertyName("activeHkLocksCount")] public int ActiveHkLocksCount { get; set; }
        [JsonPropertyName("coexistenceMode")] public string CoexistenceMode { get; set; } = "Co-Audit (Zero-Rewrite)";
        [JsonPropertyName("managedRoots")] public List<CoexistenceManagedRootDto> ManagedRoots { get; set; } = new();
        [JsonPropertyName("peers")] public List<CoexistencePeerSummaryDto> Peers { get; set; } = new();
        [JsonPropertyName("detectedConflicts")] public List<CoexistenceConflictDto> DetectedConflicts { get; set; } = new();
    }

    public sealed class ApplyCoexistencePresetRequest
    {
        [JsonPropertyName("jobId")] public string? JobId { get; set; }
        [JsonPropertyName("preset")] public string Preset { get; set; } = "";
        [JsonPropertyName("enableAutoAlign")] public bool? EnableAutoAlign { get; set; }
    }
}
