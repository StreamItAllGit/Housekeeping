using System;
using System.Collections.Generic;
using System.Linq;

namespace Housekeeping.Models
{
    /// <summary>
    /// Comprehensive catalog and alias resolver for video game console systems.
    /// Maps casual abbreviations (e.g. 'ps2', 'snes', 'genesis') to official No-Intro and Redump DAT naming.
    /// </summary>
    public static class ConsoleSystemCatalog
    {
        public class SystemEntry
        {
            public string CanonicalName { get; set; } = "";
            public string Category { get; set; } = "Home Console"; // Handheld, Computer, Arcade
            public string PreferredDatSource { get; set; } = "Redump"; // Redump, No-Intro
            public string DefaultExtensions { get; set; } = "";
            public List<string> Aliases { get; set; } = new();
            public bool IsDiscBased { get; set; } = false;
        }

        private static readonly List<SystemEntry> _systems = new()
        {
            new SystemEntry
            {
                CanonicalName = "Sony - PlayStation 2",
                Category = "Home Console",
                PreferredDatSource = "Redump",
                IsDiscBased = true,
                DefaultExtensions = ".iso,.bin,.chd,.cue,.7z,.zip",
                Aliases = new() { "ps2", "playstation 2", "playstation2", "sony ps2", "sony playstation 2" }
            },
            new SystemEntry
            {
                CanonicalName = "Sony - PlayStation",
                Category = "Home Console",
                PreferredDatSource = "Redump",
                IsDiscBased = true,
                DefaultExtensions = ".cue,.bin,.chd,.iso,.pbp,.7z,.zip",
                Aliases = new() { "ps1", "psx", "playstation", "sony playstation", "sony ps1" }
            },
            new SystemEntry
            {
                CanonicalName = "Sony - PlayStation 3",
                Category = "Home Console",
                PreferredDatSource = "Redump",
                IsDiscBased = true,
                DefaultExtensions = ".iso,.bin,.chd,.7z,.zip",
                Aliases = new() { "ps3", "playstation 3", "playstation3", "sony ps3" }
            },
            new SystemEntry
            {
                CanonicalName = "Sony - PlayStation 4",
                Category = "Home Console",
                PreferredDatSource = "Redump",
                IsDiscBased = true,
                DefaultExtensions = ".pkg,.iso",
                Aliases = new() { "ps4", "playstation 4", "playstation4", "sony ps4" }
            },
            new SystemEntry
            {
                CanonicalName = "Sony - PlayStation Portable",
                Category = "Handheld",
                PreferredDatSource = "Redump",
                IsDiscBased = true,
                DefaultExtensions = ".iso,.cso,.chd,.pbp,.7z,.zip",
                Aliases = new() { "psp", "playstation portable", "sony psp" }
            },
            new SystemEntry
            {
                CanonicalName = "Sony - PlayStation Vita",
                Category = "Handheld",
                PreferredDatSource = "No-Intro",
                IsDiscBased = false,
                DefaultExtensions = ".vpk,.zip,.7z",
                Aliases = new() { "psv", "vita", "ps vita", "playstation vita" }
            },
            new SystemEntry
            {
                CanonicalName = "Nintendo - Super Nintendo Entertainment System",
                Category = "Home Console",
                PreferredDatSource = "No-Intro",
                IsDiscBased = false,
                DefaultExtensions = ".sfc,.smc,.7z,.zip",
                Aliases = new() { "snes", "super nintendo", "super famicom", "sfc", "super nes" }
            },
            new SystemEntry
            {
                CanonicalName = "Nintendo - Nintendo Entertainment System",
                Category = "Home Console",
                PreferredDatSource = "No-Intro",
                IsDiscBased = false,
                DefaultExtensions = ".nes,.fds,.7z,.zip",
                Aliases = new() { "nes", "famicom", "nintendo entertainment system" }
            },
            new SystemEntry
            {
                CanonicalName = "Nintendo - Nintendo 64",
                Category = "Home Console",
                PreferredDatSource = "No-Intro",
                IsDiscBased = false,
                DefaultExtensions = ".n64,.z64,.v64,.7z,.zip",
                Aliases = new() { "n64", "nintendo 64", "nintendo64" }
            },
            new SystemEntry
            {
                CanonicalName = "Nintendo - GameCube",
                Category = "Home Console",
                PreferredDatSource = "Redump",
                IsDiscBased = true,
                DefaultExtensions = ".iso,.gcm,.rvz,.ciso,.7z,.zip",
                Aliases = new() { "ngc", "gamecube", "nintendo gamecube", "gc" }
            },
            new SystemEntry
            {
                CanonicalName = "Nintendo - Wii",
                Category = "Home Console",
                PreferredDatSource = "Redump",
                IsDiscBased = true,
                DefaultExtensions = ".wbfs,.rvz,.iso,.wia,.7z,.zip",
                Aliases = new() { "wii", "nintendo wii" }
            },
            new SystemEntry
            {
                CanonicalName = "Nintendo - Wii U",
                Category = "Home Console",
                PreferredDatSource = "Redump",
                IsDiscBased = true,
                DefaultExtensions = ".wux,.wud,.rpx,.7z,.zip",
                Aliases = new() { "wiiu", "wii u", "nintendo wii u" }
            },
            new SystemEntry
            {
                CanonicalName = "Nintendo - Game Boy",
                Category = "Handheld",
                PreferredDatSource = "No-Intro",
                IsDiscBased = false,
                DefaultExtensions = ".gb,.7z,.zip",
                Aliases = new() { "gb", "game boy", "gameboy" }
            },
            new SystemEntry
            {
                CanonicalName = "Nintendo - Game Boy Color",
                Category = "Handheld",
                PreferredDatSource = "No-Intro",
                IsDiscBased = false,
                DefaultExtensions = ".gbc,.7z,.zip",
                Aliases = new() { "gbc", "game boy color", "gameboy color" }
            },
            new SystemEntry
            {
                CanonicalName = "Nintendo - Game Boy Advance",
                Category = "Handheld",
                PreferredDatSource = "No-Intro",
                IsDiscBased = false,
                DefaultExtensions = ".gba,.7z,.zip",
                Aliases = new() { "gba", "game boy advance", "gameboy advance" }
            },
            new SystemEntry
            {
                CanonicalName = "Nintendo - Nintendo DS",
                Category = "Handheld",
                PreferredDatSource = "No-Intro",
                IsDiscBased = false,
                DefaultExtensions = ".nds,.7z,.zip",
                Aliases = new() { "nds", "ds", "nintendo ds" }
            },
            new SystemEntry
            {
                CanonicalName = "Nintendo - Nintendo 3DS",
                Category = "Handheld",
                PreferredDatSource = "No-Intro",
                IsDiscBased = false,
                DefaultExtensions = ".3ds,.cia,.cci,.7z,.zip",
                Aliases = new() { "3ds", "nintendo 3ds" }
            },
            new SystemEntry
            {
                CanonicalName = "Nintendo - Nintendo Switch",
                Category = "Home Console",
                PreferredDatSource = "No-Intro",
                IsDiscBased = false,
                DefaultExtensions = ".nsp,.xci,.nsz,.7z,.zip",
                Aliases = new() { "switch", "nintendo switch", "nx" }
            },
            new SystemEntry
            {
                CanonicalName = "Sega - Mega Drive - Genesis",
                Category = "Home Console",
                PreferredDatSource = "No-Intro",
                IsDiscBased = false,
                DefaultExtensions = ".md,.gen,.bin,.smd,.7z,.zip",
                Aliases = new() { "genesis", "mega drive", "megadrive", "sega genesis", "sega mega drive" }
            },
            new SystemEntry
            {
                CanonicalName = "Sega - Master System - Mark III",
                Category = "Home Console",
                PreferredDatSource = "No-Intro",
                IsDiscBased = false,
                DefaultExtensions = ".sms,.7z,.zip",
                Aliases = new() { "sms", "master system", "sega master system" }
            },
            new SystemEntry
            {
                CanonicalName = "Sega - 32X",
                Category = "Home Console",
                PreferredDatSource = "No-Intro",
                IsDiscBased = false,
                DefaultExtensions = ".32x,.7z,.zip",
                Aliases = new() { "32x", "sega 32x", "mega 32x" }
            },
            new SystemEntry
            {
                CanonicalName = "Sega - Mega-CD - Sega CD",
                Category = "Home Console",
                PreferredDatSource = "Redump",
                IsDiscBased = true,
                DefaultExtensions = ".cue,.bin,.chd,.iso,.7z,.zip",
                Aliases = new() { "segacd", "sega cd", "megacd", "mega cd", "sega mega cd" }
            },
            new SystemEntry
            {
                CanonicalName = "Sega - Saturn",
                Category = "Home Console",
                PreferredDatSource = "Redump",
                IsDiscBased = true,
                DefaultExtensions = ".cue,.bin,.chd,.iso,.7z,.zip",
                Aliases = new() { "saturn", "sega saturn" }
            },
            new SystemEntry
            {
                CanonicalName = "Sega - Dreamcast",
                Category = "Home Console",
                PreferredDatSource = "Redump",
                IsDiscBased = true,
                DefaultExtensions = ".gdi,.cdi,.chd,.cue,.bin,.7z,.zip",
                Aliases = new() { "dreamcast", "sega dreamcast", "dc" }
            },
            new SystemEntry
            {
                CanonicalName = "Sega - Game Gear",
                Category = "Handheld",
                PreferredDatSource = "No-Intro",
                IsDiscBased = false,
                DefaultExtensions = ".gg,.7z,.zip",
                Aliases = new() { "gg", "game gear", "gamegear", "sega game gear" }
            },
            new SystemEntry
            {
                CanonicalName = "Microsoft - Xbox",
                Category = "Home Console",
                PreferredDatSource = "Redump",
                IsDiscBased = true,
                DefaultExtensions = ".iso,.xiso,.7z,.zip",
                Aliases = new() { "xbox", "original xbox", "ms xbox" }
            },
            new SystemEntry
            {
                CanonicalName = "Microsoft - Xbox 360",
                Category = "Home Console",
                PreferredDatSource = "Redump",
                IsDiscBased = true,
                DefaultExtensions = ".iso,.god,.xex,.7z,.zip",
                Aliases = new() { "xbox 360", "xbox360", "x360" }
            },
            new SystemEntry
            {
                CanonicalName = "SNK - Neo Geo",
                Category = "Arcade",
                PreferredDatSource = "No-Intro",
                IsDiscBased = false,
                DefaultExtensions = ".zip,.7z",
                Aliases = new() { "neogeo", "neo geo", "aes", "mvs" }
            },
            new SystemEntry
            {
                CanonicalName = "NEC - PC Engine - TurboGrafx 16",
                Category = "Home Console",
                PreferredDatSource = "No-Intro",
                IsDiscBased = false,
                DefaultExtensions = ".pce,.7z,.zip",
                Aliases = new() { "pce", "tg16", "turbografx", "turbografx-16", "pc engine" }
            },
            new SystemEntry
            {
                CanonicalName = "Generic Media / Videos",
                Category = "Media",
                PreferredDatSource = "None",
                IsDiscBased = false,
                DefaultExtensions = ".mkv,.mp4,.avi,.mov,.wmv,.m4v",
                Aliases = new() { "media", "video", "movies", "tv", "shows", "generic media" }
            }
        };

        public static IReadOnlyList<SystemEntry> AllSystems => _systems;

        /// <summary>
        /// Attempts to resolve any casual name, folder name, or alias to an official canonical system entry.
        /// </summary>
        public static SystemEntry? ResolveSystem(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            string clean = input.Trim().ToLowerInvariant();

            // Direct canonical match
            var direct = _systems.FirstOrDefault(s => s.CanonicalName.Equals(input, StringComparison.OrdinalIgnoreCase));
            if (direct != null) return direct;

            // Direct alias match
            var aliasMatch = _systems.FirstOrDefault(s => s.Aliases.Any(a => a.Equals(clean, StringComparison.OrdinalIgnoreCase)));
            if (aliasMatch != null) return aliasMatch;

            // Substring or token matching in input (e.g. "D:\ROMs\Sony PlayStation 2 (USA)" matches PS2)
            foreach (var system in _systems)
            {
                foreach (var alias in system.Aliases)
                {
                    if (clean.Contains(alias, StringComparison.OrdinalIgnoreCase))
                    {
                        return system;
                    }
                }
            }

            return null;
        }

        public static List<string> GetDropdownOptions()
        {
            var list = new List<string> { "Auto-Detect" };
            list.AddRange(_systems.Select(s => s.CanonicalName));
            return list;
        }
    }
}
