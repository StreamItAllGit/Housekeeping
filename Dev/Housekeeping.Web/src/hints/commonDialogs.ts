// @module: Housekeeping.Web/hints/commonDialogs
// @purpose: Standard 09 2-line hover hints for shared modals, Modules Catalog, History, and Reports Center tables.
// @depends-on: none
// @exports: COMMON_UI_HINTS
export const COMMON_UI_HINTS = {
  moduleAppWideActive: (name: string) =>
    `Enables or disables the ${name} engine app-wide across all queues.\nDisabling skips all jobs of this module type until re-enabled.`,
  tplInheritExts:
    'Uses each Console Collection Rule’s Extensions CSV as the allowed primary payloads.\nUncheck on custom templates to specify an explicit payload extension list.',
  tplRequireArchive:
    'Flags loose unarchived ROM files that are not packaged inside an outer .7z or .zip.\nDisable for systems stored intentionally as unpacked files or folder dumps.',
  tplAllowFolderTargets:
    'Permits unpacked directory game dumps (e.g., RPCS3 PS3_GAME or Wii U code/content/meta).\nValidates folder boot anchors when Folder Dump Boot Anchors is enabled.',
  tplAllowSingleWrapper:
    'Allows archives whose contents sit inside one clean top-level game folder.\nNormalizes wrapper prefixes during multi-track .cue/.bin verification.',
  tplRequireCueGdi:
    'Flags raw .bin track dumps that are missing a companion .cue or .gdi descriptor.\nPrevents unbootable multi-track disc archives.',
  tplValidateTrackRefs:
    'Parses inner .cue, .gdi, and .m3u descriptors to verify every referenced file exists.\nCatches case/rename mismatches without extracting the archive.',
  tplFlagOrphanBins:
    'Flags extra .bin files inside an archive that are not referenced by any .cue/.gdi.\nHelps identify stray track clutter safely.',
  tplMultiDiscSeq:
    'Checks multi-disc sets for contiguous numbering (Disc 1 through Disc N).\nFlags missing intermediate discs in a set.',
  tplFlagNestedArchives:
    'Flags .zip, .7z, or .rar archives accidentally nested inside an outer archive.\nPrevents launch extraction failures.',
  tplBootAnchors:
    'Verifies required boot files on folder dumps (PS3_DISC.SFB, EBOOT.BIN, or meta.xml).\nCatches incomplete or mis-rooted directory rips.',
  tplFlagPs3Pup:
    'Flags redundant PS3_UPDATE/PS3UPDAT.PUP firmware installers inside PS3 dumps.\nMarks firmware bloat as safe extra clutter for guarded 7z d cleanup.',
  serverBrowseCol: (col: string) =>
    `${col} of entries inside the inspected server or UNC directory.\nDouble-click folders to navigate or click Use Path to select.`,
  snapshotCol: (col: string) =>
    `${col} for this automatic SHA-256 deduplicated settings.json snapshot.\nClick Restore to roll back configuration safely.`,
  historySelectAll:
    'Selects or deselects all visible run history rows matching your current filter.\nCSV and JSON exports will include only selected rows when checked.',
  historySelectRow:
    'Includes this execution history entry in selective CSV/JSON exports.\nLeave all unchecked to export every filtered row.',
  historyCol: (col: string) =>
    `${col} recorded for each completed, cancelled, or interrupted pipeline run.\nClick sortable headers to toggle ascending/descending order.`,
  reportsSelectAll:
    'Selects or deselects all currently filtered archive compliance reports.\nBatch Clean, Heal .hash, Ignore, and Reopen actions target selected rows.',
  reportsSelectRow:
    'Selects this compliance report for batch remediation or status updates.\nClick anywhere else on the row to inspect its inner file tree.',
  reportsCleanNode: (canClean: boolean) =>
    canClean
      ? 'Safe extra clutter eligible for guarded 7z d removal.\nUncheck to keep this file inside the archive during cleanup.'
      : 'Protected primary ROM or companion descriptor file.\nSafety guard blocks deletion of primary game payloads.',
  reportsCol: (col: string) =>
    `${col} from zero-decompression archive structure and .hash integrity auditing.\nClick any report row to inspect inner 7z/ZIP entries on the right.`,
} as const;
