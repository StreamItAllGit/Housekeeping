using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Models;

namespace Housekeeping.Services.Modules
{
    public static class RomLauncherGeneratorModule
    {
        private static readonly string[] ArchiveExtensions = { ".7z", ".zip", ".rar" };

        private static readonly Regex MultiDiscRegex = new(
            @"^(?<base>.+?)\s*[\(\[\-_ ]*(?:Disc|Disk|CD)\s*(?<num>\d+)[\)\]]*",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex CueFileLineRegex = new(
            @"^\s*FILE\s+(?:""(?<quoted>[^""]+)""|(?<unquoted>\S+))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex BatExtractPathRegex = new(
            @"x\s+""(?<path>[^""]+)""",
            RegexOptions.Compiled);

        private static readonly Regex UrlShortcutTargetRegex = new(
            @"URL=file:///(?<target>.+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly string[] MultiDiscCandidateExtensions =
        {
            ".cue", ".gdi", ".chd", ".rvz", ".ciso", ".iso", ".cdi", ".pbp", ".img"
        };

        private static readonly string[] RecognizedRomExtensionsFallback =
        {
            ".gbc", ".gb", ".gba", ".nes", ".fds", ".sfc", ".smc", ".z64", ".n64", ".v64",
            ".bin", ".cue", ".iso", ".img", ".chd", ".rvz", ".ciso", ".cso", ".pbp", ".gdi",
            ".cdi", ".m3u", ".md", ".gen", ".smd", ".sms", ".gg", ".pce", ".sg", ".ws", ".wsc"
        };

        private static readonly string[] SupplementalSiblingExtensions =
        {
            ".m3u", ".cue", ".gdi", ".sbi", ".sub", ".ccd", ".mds", ".mdf", ".bin", ".raw", ".img", ".iso", ".wav"
        };

        public static async Task RunAsync(ModuleExecutionContext ctx, CancellationToken ct)
        {
            var cfg = ctx.ActiveRomConfig;
            var activeCollections = cfg.Collections.Where(c => c.Enabled && !string.IsNullOrWhiteSpace(c.ArchivesPath)).ToList();

            ctx.Log("HEADER", $"=== Starting ROM & Emulator Launcher Generator ({activeCollections.Count} console collection(s)) ===");

            if (activeCollections.Count == 0)
            {
                ctx.Log("WARN", "No console collections configured or enabled. Add console rules or click 'Load Standard Console Templates' in Module Configurations -> 4. ROM Launchers.");
                return;
            }

            if (string.IsNullOrWhiteSpace(cfg.LaunchersOutputRootPath) && !cfg.CreateLocalLauncherNextToArchive)
            {
                ctx.Log("WARN", "Launchers Output Root Path is empty and 'Create Local Launcher Next to Archive' is unchecked. Please configure a Launchers Output Root Path.");
                return;
            }

            string sevenZipExe = ctx.Dependencies.ResolveSevenZip();

            var existingLaunchers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(cfg.LaunchersOutputRootPath) && Directory.Exists(cfg.LaunchersOutputRootPath))
            {
                try
                {
                    var dirInfo = new DirectoryInfo(cfg.LaunchersOutputRootPath);
                    foreach (var file in dirInfo.GetFiles("*.bat", SearchOption.AllDirectories))
                    {
                        existingLaunchers.Add(Path.GetFileNameWithoutExtension(file.FullName));
                    }
                    ctx.Log("INFO", $"Indexed {existingLaunchers.Count} existing launcher(s) in '{cfg.LaunchersOutputRootPath}'.");
                }
                catch (Exception ex)
                {
                    ctx.Log("WARN", $"Could not read existing launchers directory: {ex.Message}");
                }
            }

            int colIdx = 0;
            foreach (var col in activeCollections)
            {
                ct.ThrowIfCancellationRequested();
                await ctx.WaitIfQueuePausedAsync(ct);

                colIdx++;
                double colPct = (colIdx - 1) * 100.0 / Math.Max(1, activeCollections.Count);
                ctx.UpdateModuleProgress(colPct, $"Scanning {col.ConsoleName} ({colIdx}/{activeCollections.Count})...");

                ctx.Log("INFO", $"[{colIdx}/{activeCollections.Count}] Processing {col.ConsoleName} ({col.EmulatorRunnerType}) in: {col.ArchivesPath}");

                if (!await ctx.NetworkSafety.EnsureDirectoryReachableAsync(col.ArchivesPath, $"ROMs ({col.ConsoleName})", createIfRootExists: false, ct))
                {
                    ctx.Stats.Errors++;
                    continue;
                }

                string[] extensionsByPriority = BuildExtensionsPriorityList(col.ExtensionsCsv, cfg.AutoGenerateM3uForMultiDisc);

                // Step 1: Auto-generate .m3u playlists for loose multi-disc games if enabled
                if (cfg.AutoGenerateM3uForMultiDisc)
                {
                    GenerateM3uPlaylistsInDirectory(ctx, col.ArchivesPath);
                }

                // Step 2: Compress unarchived ROM files / game folders into .7z if enabled
                if (cfg.AutoCompressUnarchivedRomsWith7z)
                {
                    await CompressUnarchivedRomsInCollectionAsync(ctx, cfg, col, sevenZipExe, extensionsByPriority, ct);
                }

                // Step 2.5: Audit existing launcher scripts & shortcuts to verify game files still exist
                CleanupOrphanedLaunchersAndShortcuts(ctx, cfg, new List<ConsoleCollectionRule> { col });

                // Step 2.6: Create and maintain collection verification manifest (.csv), Redump/No-Intro auditing & Archive Compliance Reports
                var complianceStore = new ComplianceReportService(Path.Combine(ctx.ConfigService.DataDirectory, "compliance_reports.json"));
                if (cfg.EnableCollectionManifestCsv)
                {
                    try
                    {
                        string datDir = string.IsNullOrWhiteSpace(cfg.DatFilesDirectory) ? Path.Combine(ctx.ConfigService.DataDirectory, "dats") : cfg.DatFilesDirectory;
                        if (cfg.AutoDownloadMissingDats)
                        {
                            var datDownloader = new DatDownloadService();
                            await datDownloader.EnsureDatFilesForConsoleAsync(datDir, col.ConsoleName, (lvl, msg) => ctx.Log(lvl, msg), ct);
                        }

                        var datIndex = NoIntroRedumpDatParser.LoadIndexForConsole(datDir, col.ConsoleName);
                        await RomManifestCsvService.GenerateOrUpdateManifestAsync(
                            col.ArchivesPath,
                            col.ConsoleName,
                            cfg,
                            datIndex,
                            (lvl, msg) => ctx.Log(lvl, msg),
                            ct,
                            consoleRule: col,
                            reportStore: complianceStore);
                    }
                    catch (Exception ex)
                    {
                        ctx.Log("WARN", $"ROM Manifest Generation warning for {col.ConsoleName}: {ex.Message}");
                    }
                }
                else if (cfg.EnableArchiveTemplateComplianceScan && !ctx.IsDryRun)
                {
                    try
                    {
                        await ArchiveComplianceService.ScanConsoleCollectionAsync(
                            col,
                            cfg,
                            sevenZipExe,
                            complianceStore,
                            msg => ctx.Log("INFO", msg),
                            ct);
                    }
                    catch (Exception ex)
                    {
                        ctx.Log("WARN", $"Archive Compliance Scan warning for {col.ConsoleName}: {ex.Message}");
                    }
                }

                // Step 3: Scan archives
                var archiveDir = new DirectoryInfo(col.ArchivesPath);
                List<FileInfo> archives;
                try
                {
                    archives = archiveDir
                        .EnumerateFiles("*", SearchOption.AllDirectories)
                        .Where(f => ArchiveExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase) &&
                                    !f.Name.Contains(".partial.", StringComparison.OrdinalIgnoreCase) &&
                                    f.Length > 0)
                        .ToList();
                }
                catch (Exception ex)
                {
                    ctx.Log("ERROR", $"Error scanning '{col.ArchivesPath}': {ex.Message}");
                    ctx.Stats.Errors++;
                    continue;
                }

                if (archives.Count == 0)
                {
                    ctx.Log("INFO", $"No ROM archives found for {col.ConsoleName}.");
                    continue;
                }

                foreach (var archive in archives)
                {
                    ct.ThrowIfCancellationRequested();
                    await ctx.WaitIfQueuePausedAsync(ct);

                    string archiveBaseName = Path.GetFileNameWithoutExtension(archive.Name);
                    string launcherKey = $"{archiveBaseName} - {col.ConsoleName}";

                    if (existingLaunchers.Contains(launcherKey) && !cfg.RefreshExistingLaunchers)
                    {
                        ctx.Log("SKIP", $"[Skip] Launcher exists: {launcherKey}");
                        ctx.Stats.Skipped++;
                        continue;
                    }

                    if (ctx.IsDryRun)
                    {
                        ctx.Log("DRYRUN", $"[Dry Run] Would inspect archive & build launcher: {launcherKey}");
                        ctx.Stats.Processed++;
                        continue;
                    }

                    ctx.Log("WORK", $"Inspecting archive for {launcherKey}...");
                    var (tempGamePath, autoM3uDiscs) = await ResolveGamePathFromArchiveAsync(
                        ctx,
                        cfg,
                        sevenZipExe,
                        archive,
                        archiveBaseName,
                        extensionsByPriority,
                        col.SubFolder,
                        ct);

                    if (string.IsNullOrWhiteSpace(tempGamePath))
                    {
                        ctx.Log("ERROR", $"Could not find matching ROM ({col.ExtensionsCsv}) inside '{archive.Name}'.");
                        ctx.Stats.Errors++;
                        continue;
                    }

                    CreateLauncherFiles(ctx, cfg, sevenZipExe, archive, archiveBaseName, tempGamePath, autoM3uDiscs, col);
                    existingLaunchers.Add(launcherKey);
                    ctx.Stats.Processed++;
                }
            }

            ctx.UpdateModuleProgress(100, "ROM & Emulator Launcher Generator complete");
        }

        public static string[] BuildExtensionsPriorityList(string extensionsCsv, bool ensureM3uFirst)
        {
            var list = extensionsCsv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            if (ensureM3uFirst && !list.Any(e => e.Equals(".m3u", StringComparison.OrdinalIgnoreCase)))
            {
                list.Insert(0, ".m3u");
            }
            else if (ensureM3uFirst && list.Count > 1 && !list[0].Equals(".m3u", StringComparison.OrdinalIgnoreCase))
            {
                list.RemoveAll(e => e.Equals(".m3u", StringComparison.OrdinalIgnoreCase));
                list.Insert(0, ".m3u");
            }

            return list.ToArray();
        }

        // ==================== STEP 1: AUTOMATIC .M3U MULTI-DISC GENERATOR ==================== //

        public static int GenerateM3uPlaylistsInDirectory(ModuleExecutionContext? ctx, string rootDirPath)
        {
            int createdCount = 0;
            try
            {
                var rootDir = new DirectoryInfo(rootDirPath);
                if (!rootDir.Exists) return 0;

                var allDirs = new List<DirectoryInfo> { rootDir };
                allDirs.AddRange(rootDir.GetDirectories("*", SearchOption.AllDirectories));

                foreach (var dir in allDirs)
                {
                    var candidateFiles = dir.GetFiles("*", SearchOption.TopDirectoryOnly)
                        .Where(f => MultiDiscCandidateExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
                        .ToList();

                    if (candidateFiles.Count < 2)
                        continue;

                    var groups = new Dictionary<string, List<(int discNum, FileInfo file)>>(StringComparer.OrdinalIgnoreCase);

                    foreach (var file in candidateFiles)
                    {
                        string nameNoExt = Path.GetFileNameWithoutExtension(file.Name);
                        var m = MultiDiscRegex.Match(nameNoExt);
                        if (!m.Success) continue;

                        string baseTitle = m.Groups["base"].Value.Trim();
                        if (!int.TryParse(m.Groups["num"].Value, out int discNum))
                            continue;

                        if (!groups.TryGetValue(baseTitle, out var list))
                        {
                            list = new List<(int, FileInfo)>();
                            groups[baseTitle] = list;
                        }
                        list.Add((discNum, file));
                    }

                    foreach (var kvp in groups)
                    {
                        string baseTitle = kvp.Key;
                        // Deduplicate per disc number by preferring .cue / .gdi / .chd over .iso / .img
                        var orderedDiscs = kvp.Value
                            .GroupBy(x => x.discNum)
                            .OrderBy(g => g.Key)
                            .Select(g => g.OrderBy(item => Array.IndexOf(MultiDiscCandidateExtensions, item.file.Extension.ToLowerInvariant())).First().file)
                            .ToList();

                        if (orderedDiscs.Count < 2)
                            continue;

                        string m3uPath = Path.Combine(dir.FullName, $"{baseTitle}.m3u");
                        if (File.Exists(m3uPath))
                            continue;

                        if (ctx is { IsDryRun: true })
                        {
                            ctx.Log("DRYRUN", $"[Dry Run] Would generate multi-disc playlist: {m3uPath} ({orderedDiscs.Count} discs)");
                            createdCount++;
                            continue;
                        }

                        var lines = orderedDiscs.Select(f => f.Name).ToArray();
                        File.WriteAllLines(m3uPath, lines);
                        createdCount++;
                        ctx?.Log("SUCCESS", $"Generated multi-disc .m3u playlist: {m3uPath} ({orderedDiscs.Count} discs)");
                    }
                }
            }
            catch (Exception ex)
            {
                ctx?.Log("WARN", $"Error checking multi-disc .m3u files in '{rootDirPath}': {ex.Message}");
            }

            return createdCount;
        }

        // ==================== STEP 2: 7-ZIP AUTO-ARCHIVING OF UNCOMPRESSED ROMS/FOLDERS ==================== //

        private static async Task CompressUnarchivedRomsInCollectionAsync(
            ModuleExecutionContext ctx,
            RomLauncherGeneratorConfig cfg,
            ConsoleCollectionRule col,
            string sevenZipExe,
            string[] extensionsByPriority,
            CancellationToken ct)
        {
            var rootDir = new DirectoryInfo(col.ArchivesPath);

            // 2A. Compress uncompressed game subfolders that contain ROMs
            foreach (var subDir in rootDir.GetDirectories("*", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                await ctx.WaitIfQueuePausedAsync(ct);

                // Skip single-letter alphabetical organizational folders ("#", "A".."Z") and compress inside them instead
                if (subDir.Name.Length == 1 && (char.IsLetter(subDir.Name[0]) || subDir.Name == "#"))
                {
                    await CompressInsideDirectoryAsync(ctx, cfg, subDir, sevenZipExe, extensionsByPriority, ct);
                    continue;
                }

                await CompressSubfolderAsArchiveAsync(ctx, cfg, subDir, sevenZipExe, extensionsByPriority, ct);
            }

            // 2B. Compress loose uncompressed ROM files directly in the root collection folder
            await CompressLooseFilesInDirectoryAsync(ctx, cfg, rootDir, sevenZipExe, extensionsByPriority, ct);
        }

        private static async Task CompressInsideDirectoryAsync(
            ModuleExecutionContext ctx,
            RomLauncherGeneratorConfig cfg,
            DirectoryInfo parentDir,
            string sevenZipExe,
            string[] extensionsByPriority,
            CancellationToken ct)
        {
            foreach (var subDir in parentDir.GetDirectories("*", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                await ctx.WaitIfQueuePausedAsync(ct);
                await CompressSubfolderAsArchiveAsync(ctx, cfg, subDir, sevenZipExe, extensionsByPriority, ct);
            }

            await CompressLooseFilesInDirectoryAsync(ctx, cfg, parentDir, sevenZipExe, extensionsByPriority, ct);
        }

        private static async Task CompressSubfolderAsArchiveAsync(
            ModuleExecutionContext ctx,
            RomLauncherGeneratorConfig cfg,
            DirectoryInfo gameFolder,
            string sevenZipExe,
            string[] extensionsByPriority,
            CancellationToken ct)
        {
            var allFiles = gameFolder.GetFiles("*", SearchOption.AllDirectories)
                .Where(f => !f.Extension.Equals(".hash", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            bool hasRomInside = extensionsByPriority.Any(ext =>
                allFiles.Any(f => f.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));

            if (!hasRomInside)
                return;

            string parentPath = gameFolder.Parent?.FullName ?? "";
            string final7z = Path.Combine(parentPath, $"{gameFolder.Name}.7z");

            if (NetworkSafetyService.PurgeZeroByteFileIfExists(final7z))
            {
                ctx.Log("WARN", $"  -> Purged 0-byte invalid archive stub before compressing: {Path.GetFileName(final7z)}");
            }

            if (File.Exists(final7z))
                return;

            if (ctx.IsDryRun)
            {
                ctx.Log("DRYRUN", $"[Dry Run] Would compress ROM folder '{gameFolder.Name}' (preserving parent folder in .7z) -> '{final7z}'");
                ctx.Stats.Processed++;
                return;
            }

            string working7z = ctx.NetworkSafety.GetSafeEncodeOutputPath(final7z);
            ctx.Log("WORK", $"Compressing ROM folder to 7z: {gameFolder.Name} -> {Path.GetFileName(final7z)}");

            // For multi-file games, preserve the parent folder inside the archive: 7z a archive.7z "FolderName"
            // For single-file folders, pack folder contents directly: 7z a archive.7z "FolderName\*"
            string sourceArg = allFiles.Length > 1
                ? $"\"{gameFolder.Name}\""
                : $"\"{Path.Combine(gameFolder.FullName, "*")}\"";

            string cmd = $"\"{sevenZipExe}\" a \"{working7z}\" {sourceArg} -xr!*.hash -y";
            var res = await ctx.ProcessRunner.RunCommandAsync(
                cmd,
                useExternalCmdWindow: ctx.ResolveUseExternalCmd(cfg),
                workingDirectory: parentPath,
                belowNormalPriority: true,
                logOutputToTerminal: true,
                ct);

            if (ct.IsCancellationRequested || res.ExitCode == -999)
            {
                ctx.NetworkSafety.DiscardPartialEncode(working7z, final7z);
                ctx.Log("WARN", $"7z compression cancelled by user for folder '{gameFolder.Name}'");
            }
            else if (res.ExitCode == 0 && ctx.NetworkSafety.CommitPartialEncode(working7z, final7z))
            {
                ctx.Log("SUCCESS", $"Archived ROM folder: {Path.GetFileName(final7z)}");
                if (cfg.DeleteUncompressedOriginalsAfter7z)
                {
                    try
                    {
                        gameFolder.Delete(recursive: true);
                        ctx.Log("INFO", $"Deleted original uncompressed folder: {gameFolder.Name}");
                    }
                    catch (Exception ex)
                    {
                        ctx.Log("WARN", $"Could not delete original folder '{gameFolder.Name}': {ex.Message}");
                    }
                }
            }
            else
            {
                ctx.NetworkSafety.DiscardPartialEncode(working7z, final7z);
                ctx.Log("ERROR", $"7z failed compressing folder '{gameFolder.Name}' (Exit Code: {res.ExitCode})");
                ctx.Stats.Errors++;
            }
        }

        private static async Task CompressLooseFilesInDirectoryAsync(
            ModuleExecutionContext ctx,
            RomLauncherGeneratorConfig cfg,
            DirectoryInfo dir,
            string sevenZipExe,
            string[] extensionsByPriority,
            CancellationToken ct)
        {
            // Order candidate files by extension priority (.m3u first, then .cue/.gdi, then .bin/.iso)
            var looseFiles = dir.GetFiles("*", SearchOption.TopDirectoryOnly)
                .Where(f => !ArchiveExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase) &&
                            !f.Extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) &&
                            !f.Extension.Equals(".url", StringComparison.OrdinalIgnoreCase) &&
                            !f.Name.Contains(".partial.", StringComparison.OrdinalIgnoreCase) &&
                            extensionsByPriority.Any(ext => f.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(f =>
                {
                    int idx = Array.FindIndex(extensionsByPriority, ext => f.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
                    return idx >= 0 ? idx : int.MaxValue;
                })
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (looseFiles.Count == 0)
                return;

            var consumedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in looseFiles)
            {
                if (consumedFiles.Contains(file.FullName))
                    continue;

                ct.ThrowIfCancellationRequested();
                await ctx.WaitIfQueuePausedAsync(ct);

                var filesToBundle = ResolveSupplementalFilesForBundle(dir, file);
                string archiveTitle = Path.GetFileNameWithoutExtension(file.Name);

                string final7z = Path.Combine(dir.FullName, $"{archiveTitle}.7z");

                if (NetworkSafetyService.PurgeZeroByteFileIfExists(final7z))
                {
                    ctx.Log("WARN", $"  -> Purged 0-byte invalid archive stub before compressing: {Path.GetFileName(final7z)}");
                }

                if (File.Exists(final7z))
                {
                    foreach (var b in filesToBundle) consumedFiles.Add(b.FullName);
                    continue;
                }

                if (ctx.IsDryRun)
                {
                    ctx.Log("DRYRUN", $"[Dry Run] Would compress {filesToBundle.Count} file(s) ({string.Join(", ", filesToBundle.Select(x => x.Name))}) -> '{final7z}'");
                    foreach (var b in filesToBundle) consumedFiles.Add(b.FullName);
                    continue;
                }

                string working7z = ctx.NetworkSafety.GetSafeEncodeOutputPath(final7z);
                string cmd;
                string? tempSubdirToDelete = null;

                if (filesToBundle.Count > 1)
                {
                    // Multi-file game: keep parent folder "Game Title (USA)" in the archive
                    string gameSubfolder = Path.Combine(dir.FullName, archiveTitle);
                    bool createdTempFolder = false;
                    if (!Directory.Exists(gameSubfolder))
                    {
                        Directory.CreateDirectory(gameSubfolder);
                        createdTempFolder = true;
                    }

                    foreach (var f in filesToBundle)
                    {
                        string targetInSub = Path.Combine(gameSubfolder, f.Name);
                        if (!File.Exists(targetInSub))
                        {
                            File.Move(f.FullName, targetInSub);
                        }
                    }

                    cmd = $"\"{sevenZipExe}\" a \"{working7z}\" \"{archiveTitle}\" -xr!*.hash -y";
                    if (cfg.DeleteUncompressedOriginalsAfter7z || createdTempFolder)
                    {
                        tempSubdirToDelete = gameSubfolder;
                    }
                }
                else
                {
                    // Single file game: pack directly at root
                    cmd = $"\"{sevenZipExe}\" a \"{working7z}\" \"{filesToBundle[0].Name}\" -xr!*.hash -y";
                }

                ctx.Log("WORK", $"Compressing '{file.Name}' ({filesToBundle.Count} file(s)) -> {Path.GetFileName(final7z)}");
                var res = await ctx.ProcessRunner.RunCommandAsync(
                    cmd,
                    useExternalCmdWindow: ctx.ResolveUseExternalCmd(cfg),
                    workingDirectory: dir.FullName,
                    belowNormalPriority: true,
                    logOutputToTerminal: true,
                    ct);

                if (ct.IsCancellationRequested || res.ExitCode == -999)
                {
                    ctx.NetworkSafety.DiscardPartialEncode(working7z, final7z);
                    ctx.Log("WARN", $"7z compression cancelled for '{file.Name}'");
                }
                else if (res.ExitCode == 0 && ctx.NetworkSafety.CommitPartialEncode(working7z, final7z))
                {
                    ctx.Log("SUCCESS", $"Archived ROM bundle ({filesToBundle.Count} file(s)): {Path.GetFileName(final7z)}");
                    foreach (var b in filesToBundle) consumedFiles.Add(b.FullName);

                    if (cfg.DeleteUncompressedOriginalsAfter7z)
                    {
                        if (tempSubdirToDelete != null && Directory.Exists(tempSubdirToDelete))
                        {
                            try { Directory.Delete(tempSubdirToDelete, recursive: true); } catch (System.Exception __suppressedEx2) { System.Diagnostics.Debug.WriteLine($"[RomLauncherGeneratorModule] Non-fatal error ignored: {__suppressedEx2.Message}"); }
                        }
                        else
                        {
                            foreach (var b in filesToBundle)
                            {
                                try { b.Delete(); } catch (System.Exception __suppressedEx3) { System.Diagnostics.Debug.WriteLine($"[RomLauncherGeneratorModule] Non-fatal error ignored: {__suppressedEx3.Message}"); }
                            }
                        }
                    }
                }
                else
                {
                    ctx.NetworkSafety.DiscardPartialEncode(working7z, final7z);
                    ctx.Log("ERROR", $"7z failed compressing '{file.Name}' (Exit Code: {res.ExitCode})");
                    ctx.Stats.Errors++;
                }
            }
        }

        public static List<FileInfo> ResolveSupplementalFilesForBundle(DirectoryInfo dir, FileInfo primaryFile)
        {
            var result = new List<FileInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddFileIfValid(FileInfo? f)
            {
                if (f == null || !f.Exists) return;
                if (ArchiveExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase)) return;
                if (f.Extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
                    f.Extension.Equals(".url", StringComparison.OrdinalIgnoreCase)) return;
                if (f.Name.Contains(".partial.", StringComparison.OrdinalIgnoreCase)) return;

                if (seen.Add(f.FullName))
                    result.Add(f);
            }

            AddFileIfValid(primaryFile);
            string basePrefix = Path.GetFileNameWithoutExtension(primaryFile.Name);

            // 1. If primary file is an .m3u playlist, parse every disc entry inside it
            if (primaryFile.Extension.Equals(".m3u", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    foreach (string rawLine in File.ReadAllLines(primaryFile.FullName))
                    {
                        string line = rawLine.Trim();
                        if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                        string referencedPath = Path.Combine(dir.FullName, line);
                        if (File.Exists(referencedPath))
                        {
                            var discFile = new FileInfo(referencedPath);
                            AddFileIfValid(discFile);

                            // Also resolve any tracks or sibling files for each disc in the .m3u (.cue -> .bin, .sbi, etc.)
                            foreach (var sub in ResolveCueOrGdiReferencedFiles(dir, discFile))
                                AddFileIfValid(sub);
                        }
                    }
                }
                catch (System.Exception __suppressedEx8) { System.Diagnostics.Debug.WriteLine($"[RomLauncherGeneratorModule] Non-fatal error ignored: {__suppressedEx8.Message}"); }
            }

            // 2. If primary file is a .cue or .gdi, parse its referenced track files
            foreach (var trackFile in ResolveCueOrGdiReferencedFiles(dir, primaryFile))
            {
                AddFileIfValid(trackFile);
            }

            // 3. Include any sibling files in the directory starting with the same base name (e.g., .m3u, .cue, .bin, .sbi, .sub, .ccd)
            foreach (var sibling in dir.GetFiles($"{basePrefix}*", SearchOption.TopDirectoryOnly))
            {
                if (SupplementalSiblingExtensions.Contains(sibling.Extension, StringComparer.OrdinalIgnoreCase))
                {
                    AddFileIfValid(sibling);
                    foreach (var trackFile in ResolveCueOrGdiReferencedFiles(dir, sibling))
                        AddFileIfValid(trackFile);
                }
            }

            return result;
        }

        private static List<FileInfo> ResolveCueOrGdiReferencedFiles(DirectoryInfo dir, FileInfo cueOrGdiFile)
        {
            var tracks = new List<FileInfo>();
            string discPrefix = Path.GetFileNameWithoutExtension(cueOrGdiFile.Name);

            // Include matching prefix siblings for this specific disc (e.g. "Game (Disc 1) (Track 1).bin", "Game (Disc 1).sbi", "Game (Disc 1).sub")
            foreach (var sib in dir.GetFiles($"{discPrefix}*", SearchOption.TopDirectoryOnly))
            {
                if (SupplementalSiblingExtensions.Contains(sib.Extension, StringComparer.OrdinalIgnoreCase))
                    tracks.Add(sib);
            }

            if (cueOrGdiFile.Extension.Equals(".cue", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    foreach (string line in File.ReadAllLines(cueOrGdiFile.FullName))
                    {
                        var m = CueFileLineRegex.Match(line);
                        if (!m.Success) continue;

                        string fileName = m.Groups["quoted"].Success ? m.Groups["quoted"].Value : m.Groups["unquoted"].Value;
                        if (string.IsNullOrWhiteSpace(fileName)) continue;

                        string fullTrackPath = Path.Combine(dir.FullName, fileName);
                        if (File.Exists(fullTrackPath))
                            tracks.Add(new FileInfo(fullTrackPath));
                    }
                }
                catch (System.Exception __suppressedEx9) { System.Diagnostics.Debug.WriteLine($"[RomLauncherGeneratorModule] Non-fatal error ignored: {__suppressedEx9.Message}"); }
            }
            else if (cueOrGdiFile.Extension.Equals(".gdi", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var lines = File.ReadAllLines(cueOrGdiFile.FullName);
                    foreach (string line in lines.Skip(1))
                    {
                        // GDI track lines contain quoted or unquoted filenames before the final offset number
                        var tokens = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (tokens.Length >= 5)
                        {
                            string candidate = line.Contains('"')
                                ? line.Substring(line.IndexOf('"') + 1, line.LastIndexOf('"') - line.IndexOf('"') - 1)
                                : tokens[4];

                            string fullTrackPath = Path.Combine(dir.FullName, candidate);
                            if (File.Exists(fullTrackPath))
                                tracks.Add(new FileInfo(fullTrackPath));
                        }
                    }
                }
                catch (System.Exception __suppressedEx10) { System.Diagnostics.Debug.WriteLine($"[RomLauncherGeneratorModule] Non-fatal error ignored: {__suppressedEx10.Message}"); }
            }

            return tracks;
        }

        // ==================== STEP 3: ARCHIVE INSPECTION & LAUNCHER CREATION ==================== //

        private static async Task<(string? tempGamePath, List<string> autoM3uRelativeDiscs)> ResolveGamePathFromArchiveAsync(
            ModuleExecutionContext ctx,
            RomLauncherGeneratorConfig cfg,
            string sevenZipExe,
            FileInfo archive,
            string archiveBaseName,
            string[] extensionsByPriority,
            string subFolder,
            CancellationToken ct)
        {
            var autoM3uDiscs = new List<string>();
            if (!cfg.ExtractArchiveOnLaunch)
            {
                return (archive.FullName, autoM3uDiscs);
            }

            string targetScratchDir = Path.Combine(cfg.TempExtractionScratchPath, subFolder);

            if (cfg.UseFast7zListInspection)
            {
                string listCmd = $"\"{sevenZipExe}\" l -slt \"{archive.FullName}\"";
                var listRes = await ctx.ProcessRunner.RunCommandAsync(
                    listCmd,
                    useExternalCmdWindow: false,
                    workingDirectory: archive.DirectoryName,
                    belowNormalPriority: true,
                    logOutputToTerminal: false,
                    ct);

                if (listRes.ExitCode == 0 && listRes.StdOutLines.Count > 0)
                {
                    var internalPaths = new List<string>();
                    bool inEntries = false;
                    string? currentEntryPath = null;
                    bool currentIsDir = false;

                    foreach (var line in listRes.StdOutLines)
                    {
                        if (line.StartsWith("----------"))
                        {
                            inEntries = true;
                            continue;
                        }
                        if (!inEntries)
                            continue;

                        if (line.StartsWith("Path = ", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrWhiteSpace(currentEntryPath) && !currentIsDir)
                                internalPaths.Add(currentEntryPath);

                            currentEntryPath = line.Substring("Path = ".Length).Trim().Replace('/', '\\');
                            currentIsDir = false;
                        }
                        else if (currentEntryPath != null)
                        {
                            if (line.StartsWith("Folder = ", StringComparison.OrdinalIgnoreCase))
                            {
                                string fVal = line.Substring("Folder = ".Length).Trim();
                                if (fVal.Equals("+") || fVal.Equals("1"))
                                    currentIsDir = true;
                            }
                            else if (line.StartsWith("Attributes = ", StringComparison.OrdinalIgnoreCase))
                            {
                                string aVal = line.Substring("Attributes = ".Length).Trim();
                                if (aVal.Contains("D", StringComparison.OrdinalIgnoreCase))
                                    currentIsDir = true;
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(currentEntryPath) && !currentIsDir)
                        internalPaths.Add(currentEntryPath);

                    // Check if archive has multiple discs without an .m3u inside
                    if (cfg.AutoGenerateM3uForMultiDisc &&
                        !internalPaths.Any(p => p.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase)))
                    {
                        var discMatches = internalPaths
                            .Where(p => MultiDiscCandidateExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase) &&
                                        MultiDiscRegex.IsMatch(Path.GetFileNameWithoutExtension(p)))
                            .OrderBy(p => p)
                            .ToList();

                        if (discMatches.Count >= 2)
                        {
                            autoM3uDiscs = discMatches;
                            string firstDisc = discMatches[0];
                            string? internalSubdir = Path.GetDirectoryName(firstDisc);
                            string m3uRel = string.IsNullOrWhiteSpace(internalSubdir)
                                ? $"{archiveBaseName}.m3u"
                                : Path.Combine(internalSubdir, $"{archiveBaseName}.m3u");
                            return (Path.Combine(targetScratchDir, m3uRel), autoM3uDiscs);
                        }
                    }

                    foreach (string ext in extensionsByPriority)
                    {
                        var match = internalPaths.FirstOrDefault(p => p.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            return (Path.Combine(targetScratchDir, match), autoM3uDiscs);
                        }
                    }

                    // Secondary fallback in archive listing: pick primary payload if priority extension missed
                    foreach (string fallbackExt in RecognizedRomExtensionsFallback)
                    {
                        var match = internalPaths.FirstOrDefault(p => p.EndsWith(fallbackExt, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            return (Path.Combine(targetScratchDir, match), autoM3uDiscs);
                        }
                    }
                    var primaryPayload = internalPaths.FirstOrDefault(p =>
                        !p.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) &&
                        !p.EndsWith(".nfo", StringComparison.OrdinalIgnoreCase) &&
                        !p.EndsWith(".hash", StringComparison.OrdinalIgnoreCase) &&
                        !p.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) &&
                        !p.EndsWith(".srm", StringComparison.OrdinalIgnoreCase));

                    if (primaryPayload != null)
                    {
                        return (Path.Combine(targetScratchDir, primaryPayload), autoM3uDiscs);
                    }
                }
            }

            // Fallback: Extract archive to temp folder, find file, then clean up
            ctx.Log("INFO", $"Extracting '{archive.Name}' to temp scratch for ROM path lookup...");
            string extractCmd = $"\"{sevenZipExe}\" x \"{archive.FullName}\" -y -o\"{targetScratchDir}\"";
            await ctx.ProcessRunner.RunCommandAsync(
                extractCmd,
                useExternalCmdWindow: ctx.ResolveUseExternalCmd(cfg),
                workingDirectory: archive.DirectoryName,
                belowNormalPriority: true,
                logOutputToTerminal: true,
                ct);

            string? foundPath = null;
            if (Directory.Exists(targetScratchDir))
            {
                var tempDir = new DirectoryInfo(targetScratchDir);
                foreach (string ext in extensionsByPriority)
                {
                    var found = tempDir.GetFiles("*" + ext, SearchOption.AllDirectories).FirstOrDefault();
                    if (found != null)
                    {
                        foundPath = found.FullName;
                        break;
                    }
                }

                // If extensions missed, check fallback ROM extensions first
                if (string.IsNullOrEmpty(foundPath))
                {
                    foreach (string fallbackExt in RecognizedRomExtensionsFallback)
                    {
                        var found = tempDir.GetFiles("*" + fallbackExt, SearchOption.AllDirectories).FirstOrDefault();
                        if (found != null)
                        {
                            foundPath = found.FullName;
                            break;
                        }
                    }
                }

                // If still missed, pick primary game file
                if (string.IsNullOrEmpty(foundPath))
                {
                    var fallback = tempDir.GetFiles("*", SearchOption.AllDirectories)
                        .Where(f => !f.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) &&
                                    !f.Name.EndsWith(".nfo", StringComparison.OrdinalIgnoreCase) &&
                                    !f.Name.EndsWith(".hash", StringComparison.OrdinalIgnoreCase) &&
                                    !f.Name.EndsWith(".sav", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(f => f.Length)
                        .FirstOrDefault();

                    if (fallback != null)
                    {
                        foundPath = fallback.FullName;
                    }
                }
            }

            // Clean up scratch temp directory natively without failing on shell built-ins
            try
            {
                if (Directory.Exists(targetScratchDir))
                {
                    Directory.Delete(targetScratchDir, recursive: true);
                }
            }
            catch (System.Exception __suppressedEx11) { System.Diagnostics.Debug.WriteLine($"[RomLauncherGeneratorModule] Non-fatal error ignored: {__suppressedEx11.Message}"); }

            return (foundPath, autoM3uDiscs);
        }

        private static void CreateLauncherFiles(
            ModuleExecutionContext ctx,
            RomLauncherGeneratorConfig cfg,
            string sevenZipExe,
            FileInfo archive,
            string archiveBaseName,
            string tempGamePath,
            List<string> autoM3uRelativeDiscs,
            ConsoleCollectionRule col)
        {
            string targetScratchDir = Path.Combine(cfg.TempExtractionScratchPath, col.SubFolder);
            string launchCmd = BuildEmulatorLaunchCommand(col, tempGamePath);

            var lines = new List<string>();
            string rcloneExe = ctx.Dependencies.ResolveRclone();
            var (preSyncCmds, postSyncCmds) = cfg.ResolveAllSyncCommands(rcloneExe, col);

            if (cfg.EnablePrePostLaunchSyncCommands && preSyncCmds.Count > 0)
            {
                foreach (var cmd in preSyncCmds)
                    lines.Add(cmd);
            }

            if (cfg.ExtractArchiveOnLaunch)
            {
                lines.Add($"\"{sevenZipExe}\" x \"{archive.FullName}\" -y -o\"{targetScratchDir}\"");

                if (autoM3uRelativeDiscs.Count >= 2)
                {
                    string m3uFile = tempGamePath;
                    string? discFolder = Path.GetDirectoryName(autoM3uRelativeDiscs[0]);
                    for (int i = 0; i < autoM3uRelativeDiscs.Count; i++)
                    {
                        string op = i == 0 ? ">" : ">>";
                        string discEntry = !string.IsNullOrWhiteSpace(discFolder)
                            ? Path.GetFileName(autoM3uRelativeDiscs[i])
                            : autoM3uRelativeDiscs[i];
                        lines.Add($"echo {discEntry}{op}\"{m3uFile}\"");
                    }
                }
            }

            lines.Add(launchCmd);

            if (cfg.ExtractArchiveOnLaunch)
            {
                lines.Add($"rmdir \"{cfg.TempExtractionScratchPath}\" /q /s");
            }

            if (cfg.EnablePrePostLaunchSyncCommands && postSyncCmds.Count > 0)
            {
                foreach (var cmd in postSyncCmds)
                    lines.Add(cmd);
            }

            string batBaseName = $"{archiveBaseName} - {col.ConsoleName}";
            string? primaryBatFullPath = null;

            if (!string.IsNullOrWhiteSpace(cfg.LaunchersOutputRootPath))
            {
                string consoleLauncherDir = Path.Combine(cfg.LaunchersOutputRootPath, col.ConsoleName);
                Directory.CreateDirectory(consoleLauncherDir);
                primaryBatFullPath = Path.Combine(consoleLauncherDir, batBaseName + ".bat");
                File.WriteAllLines(primaryBatFullPath, lines);
                ctx.Log("SUCCESS", $"Created launcher: {primaryBatFullPath}");
            }

            if (cfg.CreateLocalLauncherNextToArchive && archive.DirectoryName != null)
            {
                string localBatPath = Path.Combine(archive.DirectoryName, $"{archiveBaseName} - Launcher.bat");
                File.WriteAllLines(localBatPath, lines);
                primaryBatFullPath ??= localBatPath;
                ctx.Log("SUCCESS", $"Created local launcher: {localBatPath}");
            }

            if (cfg.CreateStartMenuShortcuts && !string.IsNullOrWhiteSpace(cfg.StartMenuShortcutsPath) && primaryBatFullPath != null)
            {
                try
                {
                    string shortcutDir = Path.Combine(cfg.StartMenuShortcutsPath, col.ConsoleName);
                    Directory.CreateDirectory(shortcutDir);

                    string urlPath = Path.Combine(shortcutDir, batBaseName + ".url");
                    string appExe = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                    string iconPath = appExe.Replace('\\', '/');

                    File.WriteAllLines(urlPath, new[]
                    {
                        "[InternetShortcut]",
                        "URL=file:///" + primaryBatFullPath,
                        "IconIndex=0",
                        "IconFile=" + iconPath
                    });
                }
                catch (Exception ex)
                {
                    ctx.Log("WARN", $"Could not create Start Menu shortcut for '{batBaseName}': {ex.Message}");
                }
            }
        }

        public static int CleanupOrphanedLaunchersAndShortcuts(
            ModuleExecutionContext ctx,
            RomLauncherGeneratorConfig cfg,
            List<ConsoleCollectionRule> collections)
        {
            int deleted = 0;

            foreach (var col in collections)
            {
                if (string.IsNullOrWhiteSpace(col.ArchivesPath) || !Directory.Exists(col.ArchivesPath))
                    continue;

                // 1. Audit launchers in LaunchersOutputRootPath / ConsoleName
                if (!string.IsNullOrWhiteSpace(cfg.LaunchersOutputRootPath))
                {
                    string consoleLauncherDir = Path.Combine(cfg.LaunchersOutputRootPath, col.ConsoleName);
                    if (Directory.Exists(consoleLauncherDir))
                    {
                        var batFiles = Directory.GetFiles(consoleLauncherDir, "*.bat", SearchOption.TopDirectoryOnly);
                        foreach (var bat in batFiles)
                        {
                            bool isOrphan = false;
                            try
                            {
                                string content = File.ReadAllText(bat);
                                var match = BatExtractPathRegex.Match(content);
                                if (match.Success)
                                {
                                    string archivePath = match.Groups["path"].Value;
                                    if (!File.Exists(archivePath))
                                    {
                                        isOrphan = true;
                                    }
                                }
                                else
                                {
                                    string batName = Path.GetFileNameWithoutExtension(bat);
                                    string suffix = $" - {col.ConsoleName}";
                                    string baseName = batName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                                        ? batName.Substring(0, batName.Length - suffix.Length)
                                        : batName;

                                    bool existsInCol = ArchiveExtensions.Any(ext =>
                                        File.Exists(Path.Combine(col.ArchivesPath, baseName + ext)));

                                    if (!existsInCol)
                                    {
                                        isOrphan = true;
                                    }
                                }
                            }
                            catch
                            {
                                isOrphan = false;
                            }

                            if (isOrphan)
                            {
                                try
                                {
                                    File.Delete(bat);
                                    deleted++;
                                    ctx.Log("WARN", $"[Garbage Collection] Removed outdated launcher (ROM missing/renamed): {Path.GetFileName(bat)}");

                                    if (!string.IsNullOrWhiteSpace(cfg.StartMenuShortcutsPath))
                                    {
                                        string urlPath = Path.Combine(cfg.StartMenuShortcutsPath, col.ConsoleName, Path.GetFileNameWithoutExtension(bat) + ".url");
                                        if (File.Exists(urlPath))
                                        {
                                            File.Delete(urlPath);
                                            deleted++;
                                            ctx.Log("WARN", $"[Garbage Collection] Removed outdated Start Menu shortcut: {Path.GetFileName(urlPath)}");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    ctx.Log("WARN", $"Could not delete outdated launcher '{bat}': {ex.Message}");
                                }
                            }
                        }
                    }
                }

                // 2. Audit Start Menu shortcuts for this console
                if (!string.IsNullOrWhiteSpace(cfg.StartMenuShortcutsPath))
                {
                    string shortcutDir = Path.Combine(cfg.StartMenuShortcutsPath, col.ConsoleName);
                    if (Directory.Exists(shortcutDir))
                    {
                        var urls = Directory.GetFiles(shortcutDir, "*.url", SearchOption.TopDirectoryOnly);
                        foreach (var url in urls)
                        {
                            bool isDead = false;
                            try
                            {
                                string text = File.ReadAllText(url);
                                var m = UrlShortcutTargetRegex.Match(text);
                                if (m.Success)
                                {
                                    string target = m.Groups["target"].Value.Trim().Replace('/', '\\');
                                    if (!File.Exists(target))
                                    {
                                        isDead = true;
                                    }
                                }
                            }
                            catch (System.Exception __suppressedEx12) { System.Diagnostics.Debug.WriteLine($"[RomLauncherGeneratorModule] Non-fatal error ignored: {__suppressedEx12.Message}"); }

                            if (isDead)
                            {
                                try
                                {
                                    File.Delete(url);
                                    deleted++;
                                    ctx.Log("WARN", $"[Garbage Collection] Removed dead Start Menu shortcut: {Path.GetFileName(url)}");
                                }
                                catch (System.Exception __suppressedEx13) { System.Diagnostics.Debug.WriteLine($"[RomLauncherGeneratorModule] Non-fatal error ignored: {__suppressedEx13.Message}"); }
                            }
                        }
                    }
                }

                // 3. Audit local launchers next to archives if enabled
                if (cfg.CreateLocalLauncherNextToArchive && Directory.Exists(col.ArchivesPath))
                {
                    try
                    {
                        var localBats = Directory.GetFiles(col.ArchivesPath, "* - Launcher.bat", SearchOption.AllDirectories);
                        foreach (var lbat in localBats)
                        {
                            string dir = Path.GetDirectoryName(lbat) ?? "";
                            string batName = Path.GetFileName(lbat);
                            string baseName = batName.Substring(0, batName.Length - " - Launcher.bat".Length);

                            bool hasArchive = ArchiveExtensions.Any(ext => File.Exists(Path.Combine(dir, baseName + ext)));
                            if (!hasArchive)
                            {
                                File.Delete(lbat);
                                deleted++;
                                ctx.Log("WARN", $"[Garbage Collection] Removed outdated local launcher: {batName}");
                            }
                        }
                    }
                    catch (System.Exception __suppressedEx14) { System.Diagnostics.Debug.WriteLine($"[RomLauncherGeneratorModule] Non-fatal error ignored: {__suppressedEx14.Message}"); }
                }
            }

            return deleted;
        }

        public static string BuildEmulatorLaunchCommand(ConsoleCollectionRule col, string romPath)
        {
            string emu = string.IsNullOrWhiteSpace(col.EmulatorExecutablePath) ? "retroarch.exe" : col.EmulatorExecutablePath.Trim();
            string coreOrArgs = (col.CoreOrCustomArgs ?? "").Trim();
            string romDir = Path.GetDirectoryName(romPath) ?? "";

            return col.EmulatorRunnerType.Trim().ToLowerInvariant() switch
            {
                "retroarch" => $"start /w \"\" \"{emu}\" -f -L \"{coreOrArgs}\" \"{romPath}\"",
                "pcsx2" => $"start /w \"\" \"{emu}\" -fullscreen -batch -- \"{romPath}\"",
                "duckstation" => $"start /w \"\" \"{emu}\" -fullscreen -batch -- \"{romPath}\"",
                "rpcs3" => $"\"{emu}\" --no-gui --fullscreen \"{romPath}\"",
                "ppsspp" => $"start /w \"\" \"{emu}\" --fullscreen \"{romPath}\"",
                "dolphin" => $"start /w \"\" \"{emu}\" -b -e \"{romPath}\"",
                "flycast" => $"start /w \"\" \"{emu}\" \"{romPath}\"",
                "redream" => $"start /w \"\" \"{emu}\" \"{romPath}\"",
                "project64" or "rmg" => $"start /w \"\" \"{emu}\" \"{romPath}\"",
                "melonds" or "desmume" => $"start /w \"\" \"{emu}\" -f \"{romPath}\"",
                "citra" or "azahar" => $"start /w \"\" \"{emu}\" -f \"{romPath}\"",
                "cemu" => $"start /w \"\" \"{emu}\" -f -g \"{romPath}\"",
                "xemu" => $"start /w \"\" \"{emu}\" -full-screen -dvd_path \"{romPath}\"",
                "xenia" => $"start /w \"\" \"{emu}\" --fullscreen \"{romPath}\"",
                "custom" when !string.IsNullOrWhiteSpace(coreOrArgs) =>
                    coreOrArgs
                        .Replace("{emu}", $"\"{emu}\"", StringComparison.OrdinalIgnoreCase)
                        .Replace("{core}", $"\"{coreOrArgs}\"", StringComparison.OrdinalIgnoreCase)
                        .Replace("{rom}", $"\"{romPath}\"", StringComparison.OrdinalIgnoreCase)
                        .Replace("{romDir}", $"\"{romDir}\"", StringComparison.OrdinalIgnoreCase),
                _ => $"start /w \"\" \"{emu}\" \"{romPath}\""
            };
        }
    }
}
