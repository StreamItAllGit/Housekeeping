// @module: Housekeeping.Core/Services/HashSidecarService.ArchiveRegistry
// @purpose: 4-mode .hash sidecar placement (0..3) and post-injection/post-deletion .archive_hashes.json outer container hash registry.
// @depends-on: Housekeeping.Models, System.Diagnostics, System.IO, System.Linq, System.Text, System.Text.Json
// @exports: HashSidecarService (partial: InjectSidecarIntoArchiveAsync, DeleteArchiveInternalHashFilesAsync, TryGetVerifiedOuterArchiveRecord, ApplySidecarPlacementAsync)
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Models;

namespace Housekeeping.Services
{
    public static partial class HashSidecarService
    {
        public const string ArchiveHashesRegistryFileName = ".archive_hashes.json";
        private static readonly UTF8Encoding SidecarUtf8NoBom = new(false);
        private static readonly JsonSerializerOptions RegistryJsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static string GetArchiveHashesRegistryPath(string directoryOrArchivePath)
        {
            string normalized = NormalizeTargetPath(directoryOrArchivePath);
            string dir = Directory.Exists(normalized)
                ? normalized
                : (Path.GetDirectoryName(normalized) ?? normalized);
            return Path.Combine(dir, ArchiveHashesRegistryFileName);
        }

        public static ArchiveHashesRegistryDocument LoadArchiveHashesRegistry(string directoryPath)
        {
            string regPath = Path.Combine(directoryPath, ArchiveHashesRegistryFileName);
            if (!File.Exists(regPath))
                return new ArchiveHashesRegistryDocument();

            try
            {
                string json = ConfigService.ReadAllTextWithRetry(regPath);
                var doc = JsonSerializer.Deserialize<ArchiveHashesRegistryDocument>(json, RegistryJsonOptions);
                if (doc == null)
                    return new ArchiveHashesRegistryDocument();

                doc.Archives = new System.Collections.Generic.Dictionary<string, ArchiveContainerHashRecord>(
                    doc.Archives ?? new(),
                    StringComparer.OrdinalIgnoreCase);
                return doc;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HashSidecarService] Failed reading {ArchiveHashesRegistryFileName}: {ex.Message}");
                return new ArchiveHashesRegistryDocument();
            }
        }

        public static void SaveArchiveHashesRegistry(string directoryPath, ArchiveHashesRegistryDocument doc)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
                return;

            doc.SchemaVersion = 1;
            doc.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
            doc.UpdatedByApp = "housekeeping";
            doc.UpdatedByVersion = "0.2.0";

            string regPath = Path.Combine(directoryPath, ArchiveHashesRegistryFileName);
            string json = JsonSerializer.Serialize(doc, RegistryJsonOptions);
            ConfigService.AtomicWriteAllText(regPath, json);
        }

        public static bool TryGetVerifiedOuterArchiveRecord(string archivePath, out ArchiveContainerHashRecord? record)
        {
            record = null;
            if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
                return false;

            try
            {
                var fi = new FileInfo(archivePath);
                string dir = fi.DirectoryName ?? "";
                if (string.IsNullOrEmpty(dir))
                    return false;

                var reg = LoadArchiveHashesRegistry(dir);
                if (!reg.Archives.TryGetValue(fi.Name, out var found) || found == null)
                    return false;

                if (found.ContainerSize != fi.Length || string.IsNullOrWhiteSpace(found.ContainerCrc32))
                    return false;

                if (!DateTime.TryParse(found.LastWriteUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var recordedUtc))
                    return false;

                if (Math.Abs((recordedUtc.ToUniversalTime() - fi.LastWriteTimeUtc).TotalSeconds) > 2.0)
                    return false;

                record = found;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HashSidecarService] Outer archive registry check skipped: {ex.Message}");
                return false;
            }
        }

        public static async Task<FileHashResult?> UpsertPostMutationArchiveHashAsync(
            string archivePath,
            bool hasInternalSidecar,
            string internalSidecarPath = "",
            FileHashResult? primaryInner = null,
            HashSidecarMode placement = HashSidecarMode.PackageInsideArchive,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
                return null;

            var fi = new FileInfo(archivePath);
            string dir = fi.DirectoryName ?? "";
            if (string.IsNullOrEmpty(dir))
                return null;

            var outerHash = await ComputeFileHashesAsync(fi.FullName, ct);
            fi.Refresh();

            var reg = LoadArchiveHashesRegistry(dir);
            reg.HashFilePlacement = placement.ToString();
            reg.Archives[fi.Name] = new ArchiveContainerHashRecord
            {
                FileName = fi.Name,
                ContainerSize = fi.Length,
                LastWriteUtc = fi.LastWriteTimeUtc.ToString("O"),
                ContainerCrc32 = outerHash.Crc32,
                ContainerMd5 = outerHash.Md5,
                ContainerSha1 = outerHash.Sha1,
                HasInternalHashSidecar = hasInternalSidecar,
                InternalSidecarPath = internalSidecarPath,
                PrimaryInnerFileName = primaryInner?.FileName ?? "",
                PrimaryInnerSize = primaryInner?.Size ?? 0,
                PrimaryInnerCrc32 = primaryInner?.Crc32 ?? "",
                PrimaryInnerMd5 = primaryInner?.Md5 ?? "",
                PrimaryInnerSha1 = primaryInner?.Sha1 ?? ""
            };
            SaveArchiveHashesRegistry(dir, reg);
            return outerHash;
        }

        public static async Task<bool> InjectSidecarIntoArchiveAsync(
            string sevenZipExe,
            string archivePath,
            string sidecarFilePath,
            CancellationToken ct = default,
            bool syncPostInjectionContainerHashJson = true)
        {
            sevenZipExe = ResolveSevenZipExecutable(sevenZipExe);
            if (!File.Exists(sevenZipExe) || !File.Exists(archivePath) || !File.Exists(sidecarFilePath))
                return false;

            try
            {
                string sidecarDir = Path.GetDirectoryName(sidecarFilePath) ?? "";
                string sidecarFileName = Path.GetFileName(sidecarFilePath);

                var psi = new ProcessStartInfo
                {
                    FileName = sevenZipExe,
                    Arguments = $"a \"{archivePath}\" \"{sidecarFileName}\"",
                    WorkingDirectory = string.IsNullOrEmpty(sidecarDir) ? null : sidecarDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };

                using var p = Process.Start(psi);
                if (p == null) return false;
                await p.WaitForExitAsync(ct);
                if (p.ExitCode != 0) return false;

                if (syncPostInjectionContainerHashJson)
                {
                    FileHashResult? primaryInner = null;
                    string scText = await File.ReadAllTextAsync(sidecarFilePath, ct);
                    if (TryParseSidecarDocument(scText, Path.GetFileName(archivePath), out var parsedDoc) && parsedDoc != null)
                        primaryInner = parsedDoc.GetPrimaryHashResult(Path.GetFileName(archivePath));

                    var newOuter = await UpsertPostMutationArchiveHashAsync(
                        archivePath,
                        hasInternalSidecar: true,
                        internalSidecarPath: sidecarFileName,
                        primaryInner: primaryInner,
                        placement: HashSidecarMode.PackageInsideArchive,
                        ct: ct);

                    if (newOuter != null && parsedDoc != null && !parsedDoc.IsCompositeFolder)
                    {
                        foreach (var extSidecar in GetExistingExternalSidecarPaths(archivePath))
                        {
                            string updatedExternal = GenerateSidecarContentForFiles(
                                string.IsNullOrWhiteSpace(parsedDoc.TitleOrFolder) ? Path.GetFileNameWithoutExtension(archivePath) : parsedDoc.TitleOrFolder,
                                parsedDoc.Files,
                                newOuter);
                            await File.WriteAllTextAsync(extSidecar, updatedExternal, SidecarUtf8NoBom, ct);
                        }
                    }
                }

                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HashSidecarService] InjectSidecarIntoArchiveAsync failed: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> DeleteArchiveInternalHashFilesAsync(
            string sevenZipExe,
            string archivePath,
            CancellationToken ct = default,
            bool syncPostMutationContainerHashJson = true)
        {
            sevenZipExe = ResolveSevenZipExecutable(sevenZipExe);
            if (!File.Exists(sevenZipExe) || !File.Exists(archivePath) || !IsWritableArchiveFilePath(archivePath))
                return false;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = sevenZipExe,
                    Arguments = $"d \"{archivePath}\" *.hash -r",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };

                using var p = Process.Start(psi);
                if (p == null) return false;
                await p.WaitForExitAsync(ct);
                if (p.ExitCode != 0) return false;

                if (syncPostMutationContainerHashJson)
                {
                    await UpsertPostMutationArchiveHashAsync(archivePath, hasInternalSidecar: false, "", null, HashSidecarMode.SidecarBeside, ct);
                }
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HashSidecarService] DeleteArchiveInternalHashFilesAsync failed: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> MigrateValidArchiveSidecarPlacementAsync(
            string sevenZipExe,
            string target,
            HashSidecarMode targetPlacement,
            SidecarItemVerificationResult itemRes,
            Action<string, string>? log = null,
            CancellationToken ct = default,
            bool cleanDisallowedOnMigrate = false)
        {
            if (itemRes.ParsedDocument == null)
                return false;

            string shortName = Path.GetFileName(target);
            string baseTitle = Path.GetFileNameWithoutExtension(target);
            bool writable = IsWritableArchiveFilePath(target);

            if (targetPlacement == HashSidecarMode.DatabaseOnly && cleanDisallowedOnMigrate)
            {
                bool changed = false;
                foreach (var extPath in GetExistingExternalSidecarPaths(target))
                {
                    NetworkSafetyService.TryDeleteFile(extPath);
                    changed = true;
                }
                if (writable && itemRes.Location is SidecarLocation.InternalArchive or SidecarLocation.Both)
                    changed |= await DeleteArchiveInternalHashFilesAsync(sevenZipExe, target, ct);
                return changed;
            }

            if (targetPlacement == HashSidecarMode.SidecarAndArchive)
            {
                string content = SerializeParsedSidecarDocument(baseTitle, itemRes.ParsedDocument);
                bool changed = false;
                if (itemRes.Location != SidecarLocation.ExternalBeside && itemRes.Location != SidecarLocation.Both)
                {
                    await File.WriteAllTextAsync(GetStandardSidecarPath(target), content, SidecarUtf8NoBom, ct);
                    changed = true;
                }
                if (writable && itemRes.Location != SidecarLocation.InternalArchive && itemRes.Location != SidecarLocation.Both)
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "hk_hash_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);
                    try
                    {
                        string tempSidecar = Path.Combine(tempDir, $"{baseTitle}.hash");
                        await File.WriteAllTextAsync(tempSidecar, content, SidecarUtf8NoBom, ct);
                        changed |= await InjectSidecarIntoArchiveAsync(sevenZipExe, target, tempSidecar, ct);
                    }
                    finally { try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch (Exception ex) { Debug.WriteLine(ex.Message); } }
                }
                return changed;
            }

            if (targetPlacement == HashSidecarMode.PackageInsideArchive && writable && itemRes.Location != SidecarLocation.InternalArchive)
            {
                if (itemRes.Location == SidecarLocation.ExternalBeside)
                {
                    string content = SerializeParsedSidecarDocument(baseTitle, itemRes.ParsedDocument);
                    string tempDir = Path.Combine(Path.GetTempPath(), "hk_hash_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);
                    try
                    {
                        string tempSidecar = Path.Combine(tempDir, $"{baseTitle}.hash");
                        await File.WriteAllTextAsync(tempSidecar, content, SidecarUtf8NoBom, ct);
                        if (await InjectSidecarIntoArchiveAsync(sevenZipExe, target, tempSidecar, ct))
                        {
                            foreach (var extPath in GetExistingExternalSidecarPaths(target))
                                NetworkSafetyService.TryDeleteFile(extPath);
                            log?.Invoke("SUCCESS", $"[Migrated -> Inside Archive] '{shortName}' (valid .hash packaged & external sidecar removed)");
                            return true;
                        }
                    }
                    finally { try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch (Exception ex) { Debug.WriteLine(ex.Message); } }
                }
                else if (itemRes.Location == SidecarLocation.Both)
                {
                    foreach (var extPath in GetExistingExternalSidecarPaths(target))
                        NetworkSafetyService.TryDeleteFile(extPath);
                    return true;
                }
            }

            return false;
        }
    }
}
