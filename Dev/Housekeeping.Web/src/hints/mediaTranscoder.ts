// @module: Housekeeping.Web/hints/mediaTranscoder
// @purpose: Standard 09 2-line hover hints for Module 1 (Multi-Tier Media Transcoder) checkboxes and table headers.
// @depends-on: none
// @exports: MEDIA_TRANSCODER_HINTS
export const MEDIA_TRANSCODER_HINTS = {
  reEncodeNonHevc:
    'Re-encodes existing output tiers when their video codec is not HEVC (x265).\nRequires MediaInfo codec inspection unless Fast Network Mode is enabled.',
  remoteSkipMediaInfo:
    'Skips MediaInfo stream inspection when an output tier file already exists on SMB/NAS.\nDramatically accelerates large network scans when existing tiers are already HEVC.',
  tierToggle: (label: string) =>
    `Enables or disables ${label} output generation across all source quality tiers.\nUnchecking skips ${label} encodes globally without altering your tier profile assignments.`,
  tierRowEnabled:
    'Enables transcoding for master files matching this source quality tier.\nUncheck to temporarily bypass this source tier during pipeline runs.',
  customFolderEnabled:
    'Includes this custom directory in Multi-Tier Media Transcoder discovery.\nUncheck to skip scanning this path without deleting the folder rule.',
  editionRuleEnabled:
    'Enables alternate-language edition extraction and transcoding for matching audio tracks.\nWrites output files preserving {edition-*} tags and external .srt subtitles.',
  thTierOn:
    'Controls whether each source quality tier row is evaluated during scans.\nDisabled rows never spawn HandBrakeCLI jobs.',
  thTierName:
    'Human-readable label for this source quality tier (e.g., DVD, Blu-Ray, 4K UHD).\nUsed in live terminal logs and execution summaries.',
  thSourceTags:
    'Comma-separated filename tokens matched when Detection Mode uses Filename Tag Matching.\nCase-insensitive substring check against master filenames.',
  thMaxHeight:
    'Maximum vertical pixel height matched when Detection Mode uses MediaInfo Resolution Scan.\nEvaluated from lowest height tier upward to classify sources.',
  thResolutionCol: (label: string) =>
    `HandBrake encoding profile used when generating ${label} outputs for this source tier.\nSelect (Disabled) to skip ${label} output for sources in this tier.`,
  thDelete:
    'Removes this row from the job configuration.\nPress Ctrl+Z or use Snapshot History to undo accidental deletions.',
  thCustomPath:
    'Local drive folder or UNC share scanned recursively for master video files.\nAutomatically defers if an active .hklock or .redroom-part sentinel is present.',
  thIsoLang:
    'Three-letter ISO 639-2 audio language code matched in source tracks (e.g., JPN, ENG).\nPassed to HandBrakeCLI audio track selection.',
  thLangPrefix:
    'Subfolder or edition naming prefix applied to alternate-language outputs.\nPreserves Plex/Jellyfin {edition-*} naming conventions.',
  thResTag:
    'Resolution suffix tag appended to generated language edition filenames (e.g., 480p).\nPrevents filename collisions with primary multi-tier outputs.',
  thLangProfile:
    'HandBrake encoding profile applied to this alternate-language edition.\nMissing profiles display a warning option until reassigned.',
} as const;
