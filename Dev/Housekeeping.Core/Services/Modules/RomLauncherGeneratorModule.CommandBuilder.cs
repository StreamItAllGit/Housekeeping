// @module: Housekeeping.Services.Modules.RomLauncherGeneratorModule.CommandBuilder
// @purpose: Launcher .bat/.url script synthesis, orphaned launcher garbage collection, and 24-emulator command builder for Module 4.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, System.Linq, Housekeeping.Models
// @exports: Housekeeping.Services.Modules.RomLauncherGeneratorModule (partial: CreateLauncherFiles, CleanupOrphanedLaunchersAndShortcuts, BuildEmulatorLaunchCommand)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Housekeeping.Models;

namespace Housekeeping.Services.Modules
{
    public static partial class RomLauncherGeneratorModule
    {
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
                lines.AddRange(preSyncCmds);

            if (cfg.ExtractArchiveOnLaunch)
            {
                lines.Add($"\"{sevenZipExe}\" x \"{archive.FullName}\" -y -o\"{targetScratchDir}\"");
                if (autoM3uRelativeDiscs.Count >= 2)
                {
                    string? discFolder = Path.GetDirectoryName(autoM3uRelativeDiscs[0]);
                    for (int i = 0; i < autoM3uRelativeDiscs.Count; i++)
                    {
                        string op = i == 0 ? ">" : ">>";
                        string discEntry = !string.IsNullOrWhiteSpace(discFolder)
                            ? Path.GetFileName(autoM3uRelativeDiscs[i])
                            : autoM3uRelativeDiscs[i];
                        lines.Add($"echo {discEntry}{op}\"{tempGamePath}\"");
                    }
                }
            }

            lines.Add(launchCmd);

            if (cfg.ExtractArchiveOnLaunch)
                lines.Add($"rmdir \"{cfg.TempExtractionScratchPath}\" /q /s");

            if (cfg.EnablePrePostLaunchSyncCommands && postSyncCmds.Count > 0)
                lines.AddRange(postSyncCmds);

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
                    string appExe = Environment.ProcessPath ?? typeof(RomLauncherGeneratorModule).Assembly.Location;
                    File.WriteAllLines(urlPath, new[]
                    {
                        "[InternetShortcut]",
                        "URL=file:///" + primaryBatFullPath,
                        "IconIndex=0",
                        "IconFile=" + appExe.Replace('\\', '/')
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

                if (!string.IsNullOrWhiteSpace(cfg.LaunchersOutputRootPath))
                {
                    string consoleLauncherDir = Path.Combine(cfg.LaunchersOutputRootPath, col.ConsoleName);
                    if (Directory.Exists(consoleLauncherDir))
                    {
                        foreach (var bat in Directory.GetFiles(consoleLauncherDir, "*.bat", SearchOption.TopDirectoryOnly))
                        {
                            bool isOrphan = false;
                            try
                            {
                                string content = File.ReadAllText(bat);
                                var match = BatExtractPathRegex.Match(content);
                                if (match.Success)
                                {
                                    isOrphan = !File.Exists(match.Groups["path"].Value);
                                }
                                else
                                {
                                    string batName = Path.GetFileNameWithoutExtension(bat);
                                    string suffix = $" - {col.ConsoleName}";
                                    string baseName = batName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                                        ? batName[..^suffix.Length]
                                        : batName;
                                    isOrphan = !ArchiveExtensions.Any(ext => File.Exists(Path.Combine(col.ArchivesPath, baseName + ext)));
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[RomLauncherGeneratorModule] Inspect launcher skipped ({bat}): {ex.Message}");
                                isOrphan = false;
                            }

                            if (!isOrphan) continue;
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
                                    }
                                }
                            }
                            catch (Exception ex) { ctx.Log("WARN", $"Could not delete outdated launcher '{bat}': {ex.Message}"); }
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(cfg.StartMenuShortcutsPath))
                {
                    string shortcutDir = Path.Combine(cfg.StartMenuShortcutsPath, col.ConsoleName);
                    if (Directory.Exists(shortcutDir))
                    {
                        foreach (var url in Directory.GetFiles(shortcutDir, "*.url", SearchOption.TopDirectoryOnly))
                        {
                            try
                            {
                                var m = UrlShortcutTargetRegex.Match(File.ReadAllText(url));
                                if (m.Success && !File.Exists(m.Groups["target"].Value.Trim().Replace('/', '\\')))
                                {
                                    File.Delete(url);
                                    deleted++;
                                    ctx.Log("WARN", $"[Garbage Collection] Removed dead Start Menu shortcut: {Path.GetFileName(url)}");
                                }
                            }
                            catch (Exception ex) { Debug.WriteLine($"[RomLauncherGeneratorModule] Dead shortcut check skipped ({url}): {ex.Message}"); }
                        }
                    }
                }

                if (cfg.CreateLocalLauncherNextToArchive && Directory.Exists(col.ArchivesPath))
                {
                    try
                    {
                        foreach (var lbat in Directory.GetFiles(col.ArchivesPath, "* - Launcher.bat", SearchOption.AllDirectories))
                        {
                            string dir = Path.GetDirectoryName(lbat) ?? "";
                            string batName = Path.GetFileName(lbat);
                            string baseName = batName[..^" - Launcher.bat".Length];
                            if (!ArchiveExtensions.Any(ext => File.Exists(Path.Combine(dir, baseName + ext))))
                            {
                                File.Delete(lbat);
                                deleted++;
                                ctx.Log("WARN", $"[Garbage Collection] Removed outdated local launcher: {batName}");
                            }
                        }
                    }
                    catch (Exception ex) { Debug.WriteLine($"[RomLauncherGeneratorModule] Local launcher audit skipped: {ex.Message}"); }
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
                "citra" or "azahar" or "lime3ds" => $"start /w \"\" \"{emu}\" -f \"{romPath}\"",
                "cemu" => $"start /w \"\" \"{emu}\" -f -g \"{romPath}\"",
                "ryujinx" => $"start /w \"\" \"{emu}\" --fullscreen \"{romPath}\"",
                "yuzu" => $"start /w \"\" \"{emu}\" -f -g \"{romPath}\"",
                "vita3k" => $"start /w \"\" \"{emu}\" -F -r \"{romPath}\"",
                "xemu" => $"start /w \"\" \"{emu}\" -full-screen -dvd_path \"{romPath}\"",
                "xenia" => $"start /w \"\" \"{emu}\" --fullscreen \"{romPath}\"",
                "mame" => $"start /w \"\" \"{emu}\" \"{Path.GetFileNameWithoutExtension(romPath)}\" -rompath \"{romDir}\"",
                "scummvm" => $"start /w \"\" \"{emu}\" -f -p \"{romDir}\" --auto-detect",
                "dosbox" => $"start /w \"\" \"{emu}\" \"{romPath}\" -fullscreen -exit",
                "pcnative" => $"start /w \"\" \"{romPath}\" {coreOrArgs}".TrimEnd(),
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
