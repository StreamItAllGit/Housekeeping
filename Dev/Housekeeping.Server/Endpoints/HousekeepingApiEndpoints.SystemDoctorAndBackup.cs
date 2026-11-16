// @module: Housekeeping.Server.Endpoints.HousekeepingApiEndpoints.SystemDoctorAndBackup
// @purpose: System Doctor diagnostics/bundle export (--log-dir safe), clean state-only backups (sibling backups\), and graceful HTTP shutdown.
// @depends-on: System, System.Diagnostics, System.IO, System.IO.Compression, System.Linq, System.Text, System.Text.Json, Microsoft.AspNetCore.Builder, Microsoft.AspNetCore.Http
// @exports: Housekeeping.Server.Endpoints.HousekeepingApiEndpoints (partial: MapSystemDoctorAndBackupEndpoints)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Server.Hosting;
using Housekeeping.Server.Security;
using Housekeeping.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Housekeeping.Server.Endpoints
{
    public static partial class HousekeepingApiEndpoints
    {
        private static readonly string[] BackupEligibleStateFiles =
        {
            "settings.json",
            "history.json",
            "pending_swaps.json",
            "compliance_reports.json"
        };

        private static void MapSystemDoctorAndBackupEndpoints(this WebApplication app, RouteGroupBuilder api)
        {
            api.MapPost("/system/backup/now", (HousekeepingRuntimeContainer rt) =>
            {
                try
                {
                    string dataDir = rt.ConfigService.DataDirectory;
                    string backupDir = Path.GetDirectoryName(rt.ConfigService.SnapshotsDir)
                                       ?? Path.Combine(rt.ConfigService.InstallRootDirectory, "backups");
                    Directory.CreateDirectory(backupDir);

                    string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                    rt.ConfigService.Save();

                    var existingFiles = BackupEligibleStateFiles
                        .Select(name => Path.Combine(dataDir, name))
                        .Where(File.Exists)
                        .ToList();

                    string szExe = rt.Dependencies.ResolveSevenZip();
                    string arc7z = Path.Combine(backupDir, $"housekeeping_backup_{stamp}.7z");
                    bool created7z = false;

                    if (existingFiles.Count > 0 && !string.IsNullOrWhiteSpace(szExe) && File.Exists(szExe))
                    {
                        string quotedArgs = string.Join(" ", existingFiles.Select(f => $"\"{f}\""));
                        var psi = new ProcessStartInfo
                        {
                            FileName = szExe,
                            Arguments = $"a -t7z -mx=9 \"{arc7z}\" {quotedArgs}",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using var proc = Process.Start(psi);
                        proc?.WaitForExit(10000);
                        created7z = File.Exists(arc7z);
                    }

                    string finalPath = created7z ? arc7z : Path.Combine(backupDir, $"housekeeping_backup_{stamp}.zip");
                    if (!created7z)
                    {
                        if (File.Exists(finalPath)) File.Delete(finalPath);
                        using var zipStream = new FileStream(finalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false);
                        foreach (string stateFile in existingFiles)
                        {
                            var entry = archive.CreateEntry(Path.GetFileName(stateFile), CompressionLevel.Optimal);
                            using var entryStream = entry.Open();
                            using var srcStream = new FileStream(stateFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            srcStream.CopyTo(entryStream);
                        }
                    }

                    rt.Orchestrator.Log("SUCCESS", $"[System Doctor] Created state backup: {Path.GetFileName(finalPath)}");
                    return Results.Ok(new
                    {
                        ok = true,
                        backupPath = finalPath,
                        fileName = Path.GetFileName(finalPath),
                        includedFiles = existingFiles.Select(Path.GetFileName).ToArray(),
                        format = created7z ? "7z" : "zip"
                    });
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { ok = false, error = ex.Message });
                }
            });

            api.MapGet("/system/doctor/diagnostics", async (HousekeepingRuntimeContainer rt, CancellationToken ct) =>
            {
                var deps = rt.Dependencies.CheckAllDependencies();
                var coexistence = await rt.Coexistence.GetStatusAsync(ct);
                var drives = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => new
                {
                    name = d.Name,
                    driveType = d.DriveType.ToString(),
                    volumeLabel = d.VolumeLabel,
                    totalGb = Math.Round((double)d.TotalSize / (1024 * 1024 * 1024), 1),
                    freeGb = Math.Round((double)d.AvailableFreeSpace / (1024 * 1024 * 1024), 1),
                    isReady = d.IsReady
                }).ToList();

                return Results.Ok(new
                {
                    appVersion = RemoteNodeDeploymentService.GetLocalAppVersion(),
                    machineName = Environment.MachineName,
                    osVersion = Environment.OSVersion.ToString(),
                    isIsolated = rt.ConfigService.IsIsolatedOverride,
                    activeLogsDir = rt.ConfigService.ActiveLogsDir,
                    dataDirectory = rt.ConfigService.DataDirectory,
                    dependencies = deps,
                    coexistence,
                    drives
                });
            });

            var exportDoctorBundle = async (HousekeepingRuntimeContainer rt, CancellationToken ct) =>
            {
                try
                {
                    string logsDir = rt.ConfigService.ActiveLogsDir;
                    Directory.CreateDirectory(logsDir);
                    string nowIso = DateTime.UtcNow.ToString("o");
                    string diagName = $"Housekeeping_Diagnostics_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
                    string diagPath = Path.Combine(logsDir, diagName);
                    var coexistence = await rt.Coexistence.GetStatusAsync(ct);

                    var report = new
                    {
                        appName = "Housekeeping",
                        version = RemoteNodeDeploymentService.GetLocalAppVersion(),
                        machine = Environment.MachineName,
                        os = Environment.OSVersion.ToString(),
                        createdAtUtc = nowIso,
                        timestampUtc = nowIso,
                        isIsolated = rt.ConfigService.IsIsolatedOverride,
                        activeLogsDir = logsDir,
                        bundlePath = diagPath,
                        dependencies = rt.Dependencies.CheckAllDependencies(),
                        coexistence,
                        drives = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => new
                        {
                            name = d.Name,
                            volumeLabel = d.VolumeLabel,
                            totalGb = Math.Round((double)d.TotalSize / (1024 * 1024 * 1024), 1),
                            freeGb = Math.Round((double)d.AvailableFreeSpace / (1024 * 1024 * 1024), 1)
                        }).ToList(),
                        recentLogs = rt.LogBuffer.GetRecentEntries(150).Select(e => e.FormattedLine).ToList()
                    };

                    string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                    ConfigService.AtomicWriteAllText(diagPath, json);
                    rt.Orchestrator.Log("SUCCESS", $"[System Doctor] Exported diagnostic bundle: {diagName}");
                    return Results.File(Encoding.UTF8.GetBytes(json), "application/json", diagName);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { ok = false, error = ex.Message });
                }
            };

            api.MapPost("/system/doctor/export-bundle", exportDoctorBundle);
            api.MapGet("/system/doctor/export-bundle", exportDoctorBundle);
            app.MapGet("/api/system/doctor/export-bundle", exportDoctorBundle);
            app.MapPost("/api/system/doctor/export-bundle", exportDoctorBundle);

            var postSystemShutdown = (HttpContext ctx, IHostApplicationLifetime lifetime, TieredAuthService auth, HousekeepingRuntimeContainer rt) =>
            {
                var eval = ctx.Items[TieredAuthMiddleware.AuthResultItemKey] as AuthEvaluationResult ?? auth.EvaluateRequest(ctx);
                if (!eval.IsDirectLoopback && !eval.IsAuthenticated)
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }

                rt.Orchestrator.Log("INFO", "[System] Received HTTP graceful shutdown signal via /api/v1/system/shutdown.");
                if (rt.Orchestrator.IsRunning)
                {
                    rt.Orchestrator.InitiateGracefulShutdownAbort("HTTP Shutdown API");
                }

                _ = Task.Run(async () =>
                {
                    await Task.Delay(250);
                    try { lifetime.StopApplication(); }
                    catch (Exception ex) { Debug.WriteLine($"[HousekeepingApiEndpoints] StopApplication error ignored: {ex.Message}"); }
                });

                return Results.Ok(new
                {
                    ok = true,
                    status = "shutting_down",
                    timestampUtc = DateTime.UtcNow
                });
            };

            api.MapPost("/system/shutdown", postSystemShutdown);
            app.MapPost("/api/system/shutdown", postSystemShutdown);
        }
    }
}
