using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Housekeeping.Services
{
    public class DatDownloadService
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        public async Task<bool> EnsureDatFilesForConsoleAsync(
            string datDir,
            string consoleName,
            Action<string, string> log,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(consoleName))
                return false;

            if (!Directory.Exists(datDir))
            {
                Directory.CreateDirectory(datDir);
            }

            // Check if DAT already exists locally
            var existingIndex = NoIntroRedumpDatParser.LoadIndexForConsole(datDir, consoleName);
            if (existingIndex.BySha1.Count > 0 || existingIndex.ByFileName.Count > 0)
            {
                return true;
            }

            log("INFO", $"No local DAT file found for '{consoleName}'. Checking online DAT repositories...");

            var sys = Models.ConsoleSystemCatalog.ResolveSystem(consoleName);
            // URL mappings for popular retro systems from official / community DAT feeds
            string? downloadUrl = ResolveOnlineDatUrl(consoleName);
            if (string.IsNullOrEmpty(downloadUrl))
            {
                log("INFO", $"No automated online DAT download endpoint registered for '{consoleName}'. Local or manual DAT can be placed in '{datDir}'.");
                return false;
            }

            try
            {
                log("WORK", $"Downloading latest DAT definition for {consoleName}...");
                HttpResponseMessage? response = null;
                try
                {
                    response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                }
                catch (Exception ex) when (downloadUrl.StartsWith("https://redump.org/", StringComparison.OrdinalIgnoreCase))
                {
                    // Fall back to HTTP if HTTPS port 443 is refused by redump.org
                    string fallbackHttp = "http://" + downloadUrl.Substring("https://".Length);
                    log("INFO", $"HTTPS failed for Redump ({ex.Message}), attempting HTTP fallback...");
                    response = await Http.GetAsync(fallbackHttp, HttpCompletionOption.ResponseHeadersRead, ct);
                }
                if (!response.IsSuccessStatusCode)
                {
                    log("WARN", $"Could not download DAT from {downloadUrl} (HTTP {(int)response.StatusCode} {response.ReasonPhrase}).");
                    return false;
                }

                string tempFile = Path.Combine(datDir, $"{NoIntroRedumpDatParser.NormalizeConsoleSearchKey(consoleName)}_download.tmp");
                await using (var fs = File.Create(tempFile))
                {
                    await response.Content.CopyToAsync(fs, ct);
                }

                // If zipped, unpack
                bool unpacked = false;
                try
                {
                    using (var archive = ZipFile.OpenRead(tempFile))
                    {
                        foreach (var entry in archive.Entries)
                        {
                            if (entry.FullName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) ||
                                entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                            {
                                string destFile = Path.Combine(datDir, entry.Name);
                                entry.ExtractToFile(destFile, overwrite: true);
                                unpacked = true;
                            }
                        }
                    }
                }
                catch
                {
                    // Not a zip — verify that the response is actually a valid XML/ClrMamePro DAT and not an HTML error/rate-limit page
                    if (IsValidDatPayload(tempFile))
                    {
                        string safeConsoleFile = string.Concat(consoleName.Split(Path.GetInvalidFileNameChars())).Trim();
                        if (string.IsNullOrEmpty(safeConsoleFile))
                            safeConsoleFile = NoIntroRedumpDatParser.NormalizeConsoleSearchKey(consoleName);

                        string targetFileName = (sys != null && !string.IsNullOrWhiteSpace(sys.CanonicalName))
                            ? $"{sys.CanonicalName}.dat"
                            : $"{safeConsoleFile}.dat";

                        string destFile = Path.Combine(datDir, targetFileName);
                        if (File.Exists(destFile)) File.Delete(destFile);
                        File.Move(tempFile, destFile);
                        unpacked = true;
                    }
                    else
                    {
                        log("WARN", $"Downloaded response for '{consoleName}' was not a valid DAT or ZIP archive (likely an HTML redirect or rate-limit page). Skipping online DAT.");
                    }
                }
                finally
                {
                    if (File.Exists(tempFile))
                        File.Delete(tempFile);
                }

                if (unpacked)
                {
                    log("SUCCESS", $"Latest DAT definition for '{consoleName}' downloaded and indexed successfully.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                log("WARN", $"Automated DAT download for '{consoleName}' skipped: {ex.Message}");
            }

            return false;
        }

        public static bool IsValidDatPayload(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                char[] buf = new char[2048];
                int read = sr.Read(buf, 0, buf.Length);
                if (read <= 0) return false;
                string head = new string(buf, 0, read);

                if (head.Contains("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
                    head.Contains("<html", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return head.Contains("<datafile", StringComparison.OrdinalIgnoreCase) ||
                       head.Contains("clrmamepro", StringComparison.OrdinalIgnoreCase) ||
                       head.Contains("<header>", StringComparison.OrdinalIgnoreCase) ||
                       head.Contains("<?xml", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static string? ResolveOnlineDatUrl(string consoleName)
        {
            string key = NoIntroRedumpDatParser.NormalizeConsoleSearchKey(consoleName);
            var sys = Models.ConsoleSystemCatalog.ResolveSystem(consoleName);

            // Redump packs & daily feeds (HTTPS) for disc systems
            if (key.Contains("psx") || key.Contains("playstation1") || key.Contains("ps1") || sys?.CanonicalName == "Sony - PlayStation")
                return "https://redump.org/datfile/psx/";
            if (key.Contains("ps2") || key.Contains("playstation2") || sys?.CanonicalName == "Sony - PlayStation 2")
                return "https://redump.org/datfile/ps2/";
            if (key.Contains("ps3") || key.Contains("playstation3") || sys?.CanonicalName == "Sony - PlayStation 3")
                return "https://redump.org/datfile/ps3/";
            if (key.Contains("saturn") || sys?.CanonicalName == "Sega - Saturn")
                return "https://redump.org/datfile/ss/";
            if (key.Contains("dreamcast") || key.Contains("dc") || sys?.CanonicalName == "Sega - Dreamcast")
                return "https://redump.org/datfile/dc/";
            if (key.Contains("gamecube") || key.Contains("gc") || sys?.CanonicalName == "Nintendo - GameCube")
                return "https://redump.org/datfile/gc/";
            if (key.Contains("wii") || sys?.CanonicalName == "Nintendo - Wii")
                return "https://redump.org/datfile/wii/";
            if (key.Contains("psp") || sys?.CanonicalName == "Sony - PlayStation Portable")
                return "https://redump.org/datfile/psp/";
            if (key.Contains("segacd") || key.Contains("megacd") || sys?.CanonicalName == "Sega - Mega-CD - Sega CD")
                return "https://redump.org/datfile/mcd/";
            if (key.Contains("xbox") || sys?.CanonicalName == "Microsoft - Xbox")
                return "https://redump.org/datfile/xbox/";

            // No-Intro cartridge systems via libretro-database upstream metadata feed
            if (sys != null && (sys.PreferredDatSource.Equals("No-Intro", StringComparison.OrdinalIgnoreCase) || !sys.IsDiscBased))
            {
                return $"https://raw.githubusercontent.com/libretro/libretro-database/master/metadat/no-intro/{Uri.EscapeDataString(sys.CanonicalName + ".dat")}";
            }

            // Fallback alias matching for No-Intro cartridge systems
            if (key.Contains("nes") || key.Contains("famicom"))
                return "https://raw.githubusercontent.com/libretro/libretro-database/master/metadat/no-intro/Nintendo%20-%20Nintendo%20Entertainment%20System.dat";
            if (key.Contains("snes") || key.Contains("supernintendo") || key.Contains("superfamicom"))
                return "https://raw.githubusercontent.com/libretro/libretro-database/master/metadat/no-intro/Nintendo%20-%20Super%20Nintendo%20Entertainment%20System.dat";
            if (key.Contains("n64") || key.Contains("nintendo64"))
                return "https://raw.githubusercontent.com/libretro/libretro-database/master/metadat/no-intro/Nintendo%20-%20Nintendo%2064.dat";
            if (key.Contains("gba") || key.Contains("gameboyadvance"))
                return "https://raw.githubusercontent.com/libretro/libretro-database/master/metadat/no-intro/Nintendo%20-%20Game%20Boy%20Advance.dat";
            if (key.Contains("gbc") || key.Contains("gameboycolor"))
                return "https://raw.githubusercontent.com/libretro/libretro-database/master/metadat/no-intro/Nintendo%20-%20Game%20Boy%20Color.dat";
            if (key.Contains("gb") || key.Contains("gameboy"))
                return "https://raw.githubusercontent.com/libretro/libretro-database/master/metadat/no-intro/Nintendo%20-%20Game%20Boy.dat";
            if (key.Contains("nds") || key.Contains("nintendods") || key == "ds")
                return "https://raw.githubusercontent.com/libretro/libretro-database/master/metadat/no-intro/Nintendo%20-%20Nintendo%20DS.dat";
            if (key.Contains("genesis") || key.Contains("megadrive"))
                return "https://raw.githubusercontent.com/libretro/libretro-database/master/metadat/no-intro/Sega%20-%20Mega%20Drive%20-%20Genesis.dat";
            if (key.Contains("gamegear") || key == "gg")
                return "https://raw.githubusercontent.com/libretro/libretro-database/master/metadat/no-intro/Sega%20-%20Game%20Gear.dat";
            if (key.Contains("32x"))
                return "https://raw.githubusercontent.com/libretro/libretro-database/master/metadat/no-intro/Sega%20-%2032X.dat";
            if (key.Contains("mastersystem") || key == "sms")
                return "https://raw.githubusercontent.com/libretro/libretro-database/master/metadat/no-intro/Sega%20-%20Master%20System%20-%20Mark%20III.dat";
            if (key.Contains("atari2600") || key == "2600")
                return "https://raw.githubusercontent.com/libretro/libretro-database/master/metadat/no-intro/Atari%20-%202600.dat";
            if (key.Contains("atari7800") || key == "7800")
                return "https://raw.githubusercontent.com/libretro/libretro-database/master/metadat/no-intro/Atari%20-%207800.dat";
            if (key.Contains("lynx"))
                return "https://raw.githubusercontent.com/libretro/libretro-database/master/metadat/no-intro/Atari%20-%20Lynx.dat";
            if (key.Contains("turbografx") || key.Contains("pcengine") || key.Contains("tg16"))
                return "https://raw.githubusercontent.com/libretro/libretro-database/master/metadat/no-intro/NEC%20-%20PC%20Engine%20-%20TurboGrafx%2016.dat";
            if (key.Contains("neogeopocket") || key.Contains("ngpc"))
                return "https://raw.githubusercontent.com/libretro/libretro-database/master/metadat/no-intro/SNK%20-%20Neo%20Geo%20Pocket%20Color.dat";
            if (key.Contains("wonderswan"))
                return "https://raw.githubusercontent.com/libretro/libretro-database/master/metadat/no-intro/Bandai%20-%20WonderSwan%20Color.dat";

            return null;
        }
    }
}
