using System;
using System.Collections.Generic;
using Housekeeping.Models;

namespace Housekeeping.Services.Modules
{
    public static class ManifestPresetCatalog
    {
        /// <summary>
        /// Presets tailored specifically to the user's actual AutoCompress / Media library conventions
        /// (e.g. "Dragon Ball - s01e01 - Secret of the Dragon Balls - DVD (Original).mkv",
        /// "Anchorman (2004) - DVD (Original) {edition-DVD, Unrated}.mkv").
        /// </summary>
        public static List<ManifestFieldParseRule> GetPersonalMediaPresets() => new()
        {
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "MovieTitle",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"^(?<MovieTitle>.+?)\s*\((?:19|20)\d{2}\)\s*-\s*(?:DVD|BR|UHD)",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "MovieYear",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"\((?<MovieYear>(?:19|20)\d{2})\)\s*-\s*(?:DVD|BR|UHD)",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "ShowTitle",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexNamedCapture,
                Pattern = @"^(?<ShowTitle>.+?)(?:\s*\((?:19|20)\d{2}\))?\s*-\s*[sS]\d+",
                AssignedValue = "$ShowTitle"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "Season",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexNamedCapture,
                Pattern = @"[sS](?<Season>\d{1,2})[Ee]\d{1,3}",
                AssignedValue = "$Season"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "Episode",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexNamedCapture,
                Pattern = @"[sS]\d{1,2}[Ee](?<Episode>\d{1,3})",
                AssignedValue = "$Episode"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "EpisodeTitle",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexNamedCapture,
                Pattern = @"[sS]\d+[eE]\d+\s*-\s*(?<EpisodeTitle>.+?)\s*-\s*(?:DVD|BR|UHD)",
                AssignedValue = "$EpisodeTitle"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "DiscTier",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"-\s*(DVD|BR|UHD)\s*\(",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "SourceOrRes",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"\((Original|144p|240p|360p|480p|720p|1080p|4K)\)",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "Edition",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"\{edition-(?<Edition>[^\}]+)\}",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "ProxyRes",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"\((?<ProxyRes>\d+p)_proxy\)",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "MediaLangFolder",
                InspectTarget = ManifestInspectTarget.ParentFolderName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"^(Japanese|English|Spanish|French|German)\s+(?:TV Shows|Movies)",
                AssignedValue = "$1"
            }
        };

        /// <summary>
        /// Presets tailored specifically to the user's actual RetroLaunch / Libretro / Master Set & RomHacks
        /// (e.g. "A.S.P. - Air Strike Patrol (USA).7z", "Pokemon Crystal - Legacy (1.3.1).zip",
        /// "Master Set\Nintendo\SNES\A\Aaahh!!! Real Monsters (USA) - Launcher.bat").
        /// </summary>
        public static List<ManifestFieldParseRule> GetPersonalRomPresets() => new()
        {
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "GameTitle",
                InspectTarget = ManifestInspectTarget.FileNameWithoutExtension,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"^(?<GameTitle>.+?)(?:\s*\([^\)]+\)|\s*\[[^\]]+\]|\s*-\s*Launcher)*$",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "Region",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"\((USA|Europe|Japan|World|Australia|Germany|France|Spain|Italy|Canada|Korea|China|Brazil|Sweden|Netherlands|En,Ja|En,Fr,De|En,Fr|U|J|E)\)",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "DiscNumber",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"\((Disc\s*\d+|2 Discs|Disc\s*[A-Z]|CD\s*\d+|Side\s*[A-Z])\)",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "Revision",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"\((Rev\s*[\w\d]+|v\d+(?:\.\d+)*|PRG\d+|Beta(?:\s*\d+)?|Proto|Sample)\)",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "DumpTag",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"\[(?<DumpTag>\!|\!CR|CDI|GDI|YZB|DCCM|DCRES|b\d*|h\d*|o\d*|t\d*|f\d*)\]",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "IsLauncher",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.TextContains,
                Pattern = "- Launcher",
                AssignedValue = "True"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "RomHackOriginalGame",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"^(?<OriginalGame>.+?)\s*-\s*(?<HackName>.+?)\s*\((?<HackVersion>[\d\.]+)\)",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "RomHackName",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"^.+?\s*-\s*(?<HackName>.+?)\s*\((?<HackVersion>[\d\.]+)\)",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "RomHackVersion",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"^.+?\s*-\s*.+?\s*\((?<HackVersion>[\d\.]+)\)",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "PlatformFromFolder",
                InspectTarget = ManifestInspectTarget.RelativePath,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"(?:Nintendo|Sega|Sony|Atari|Bandai|Coleco|Commodore|Microsoft|NEC-PCEngine|SNK)[\\/](?<Platform>[^\\/]+)",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "CompanyFromFolder",
                InspectTarget = ManifestInspectTarget.RelativePath,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"Master Set[\\/](?<Company>[^\\/]+)",
                AssignedValue = "$1"
            }
        };

        /// <summary>
        /// Super general standard Scene / P2P media release conventions
        /// (e.g. "Breaking.Bad.S01E01.Pilot.1080p.BluRay.x265-d3g.mkv").
        /// </summary>
        public static List<ManifestFieldParseRule> GetGeneralMediaPresets() => new()
        {
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "SceneTitle",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"^(?<SceneTitle>[A-Za-z0-9\._]+?)[.\-_](?:S\d+|19\d{2}|20\d{2})",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "Year",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"\b(?<Year>(?:19|20)\d{2})\b",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "Season",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"[Ss](?<Season>\d{1,2})",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "Episode",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"[Ee](?<Episode>\d{1,3})",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "Resolution",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"\b(?<Resolution>2160p|4[Kk]|1080p|1080i|720p|576p|480p)\b",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "Source",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"\b(?<Source>UHD[\.\s\-]?BluRay|BluRay|Blu-Ray|BDRip|BRRip|WEB-DL|WEBRip|HDTV|DVDRip|DVD|Remux)\b",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "VideoCodec",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"\b(?<VideoCodec>x265|h265|HEVC|x264|h264|AVC|AV1|XviD)\b",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "AudioCodec",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"\b(?<AudioCodec>DTS-HD[\.\s\-]?MA|DTS-HD|TrueHD|Atmos|DTS|DD\+[\.\s\-]?Atmos|EAC3|AC3|AAC|FLAC)\b",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "ReleaseGroup",
                InspectTarget = ManifestInspectTarget.FileNameWithoutExtension,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"-(?<ReleaseGroup>[A-Za-z0-9]+)$",
                AssignedValue = "$1"
            }
        };

        /// <summary>
        /// Super general file, document, log, and date metadata
        /// (e.g. "Report_2026-10-29_v2.4.pdf").
        /// </summary>
        public static List<ManifestFieldParseRule> GetGeneralFilesPresets() => new()
        {
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "Date",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"\b(?<Date>(?:19|20)\d{2}[-_.]\d{2}[-_.]\d{2})\b",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "Year",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"\b(?<Year>(?:19|20)\d{2})\b",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "Version",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"\b[vV]?(?<Version>\d+(?:\.\d+)+)\b",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "ParentCategory",
                InspectTarget = ManifestInspectTarget.ParentFolderName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"^(?<Category>.+)$",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "FileExtension",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"\.([a-zA-Z0-9]+)$",
                AssignedValue = "$1"
            }
        };

        /// <summary>
        /// General music and audio track metadata
        /// (e.g. "01 - Pink Floyd - Time (320kbps).mp3").
        /// </summary>
        public static List<ManifestFieldParseRule> GetGeneralAudioPresets() => new()
        {
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "TrackNumber",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"^(?<TrackNumber>\d{1,3})[\s\.\-_]",
                AssignedValue = "$1"
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "Artist",
                InspectTarget = ManifestInspectTarget.FileNameWithoutExtension,
                MatchMode = ManifestMatchMode.SplitToken,
                SplitDelimiter = " - ",
                SplitIndex = 0,
                AssignedValue = ""
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "AlbumOrTitle",
                InspectTarget = ManifestInspectTarget.FileNameWithoutExtension,
                MatchMode = ManifestMatchMode.SplitToken,
                SplitDelimiter = " - ",
                SplitIndex = 1,
                AssignedValue = ""
            },
            new ManifestFieldParseRule
            {
                Enabled = true,
                ColumnName = "AudioBitrate",
                InspectTarget = ManifestInspectTarget.FileName,
                MatchMode = ManifestMatchMode.RegexPattern,
                Pattern = @"\b(?<Bitrate>\d{2,3}k|320kbps|V0|Lossless|FLAC)\b",
                AssignedValue = "$1"
            }
        };
    }
}
