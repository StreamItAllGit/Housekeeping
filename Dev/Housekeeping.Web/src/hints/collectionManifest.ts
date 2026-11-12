// @module: Housekeeping.Web/hints/collectionManifest
// @purpose: Standard 09 2-line hover hints for Module 8 (Collection Manifest & Bit Rot Auditor) controls and tables.
// @depends-on: none
// @exports: COLLECTION_MANIFEST_HINTS
export const COLLECTION_MANIFEST_HINTS = {
  unifiedMaster:
    'Combines all enabled target folders into a single consolidated master manifest file.\nUncheck to emit separate per-folder manifests inside each target location.',
  exportCsvTsv: (isTsv: boolean) =>
    `Exports the manifest table as a UTF-8 No-BOM ${isTsv ? 'Tab-Separated (.tsv)' : 'delimited (.csv)'} file.\nAutomatically switches extension to .tsv when Tab (\\t) delimiter is selected.`,
  exportJson:
    'Exports structured JSON manifest records alongside dynamic extracted columns.\nIdeal for downstream scripts, dashboards, and automated catalog pipelines.',
  exportHtml:
    'Generates a standalone searchable visual HTML catalog report.\nCan be opened directly in any browser without a web server.',
  autoSyncGreenroom:
    'Notifies Greenroom (:8420) whenever manifest rows are added or updated.\nTriggers non-blocking catalog refresh only when Greenroom integration is enabled.',
  enableBitRot:
    'Computes and verifies cryptographic hashes against .hash sidecars and manifest caches.\nFlags unexpected payload mutations while preserving valid repacked containers.',
  createMissingHashes:
    'Writes baseline .hash metadata for newly discovered files that lack sidecars.\nHonors your selected 4-mode Hash Placement Strategy (0..3).',
  autoHealContainers:
    'Automatically refreshes outer container metadata when inner ROM payloads still match.\nPreserves # Container: and CRC32+MD5+SHA1 fields without degrading hashes.',
  twoStepArchive:
    'Verifies outer archive container hashes first and falls back to inner 7z/ZIP stream inspection.\nUses .archive_hashes.json to achieve zero-extraction verification hits.',
  skipVerifiedCsv:
    'Trusts previously verified manifest rows whose file size and LastWriteTimeUtc are unchanged.\nDramatically accelerates incremental audits across multi-terabyte NAS vaults.',
  syncPostInjectionJson:
    'Maintains <Directory>/.archive_hashes.json after internal .hash sidecar injection or cleanup.\nEliminates re-hash thrash between Housekeeping and ShadowLibrary.',
  skipShadowLibraryRoots:
    'Skips folders stamped as ShadowLibrary-managed in .coexistence.json or active .hklock.\nPrevents duplicate ROM vault auditing when ShadowLibrary owns the directory.',
  targetRowEnabled:
    'Includes this target directory in manifest indexing and bit-rot verification.\nUncheck to pause scanning this location while keeping its settings intact.',
  includeSubdirs:
    'Recursively scans nested child folders beneath this target path.\nUncheck to restrict indexing strictly to top-level files in the folder.',
  ruleEnabled:
    'Activates this custom column extraction rule during manifest generation.\nEvaluated live in the C# Regex Playground below.',
  thTargetEnabled:
    'Toggles whether each target directory is scanned during Module 8 runs.\nDisabled rows remain saved for future use.',
  thLocationId:
    'Short storage identifier written to the Location column (e.g., NAS-V, Local-SSD).\nHelps distinguish identical relative paths across multiple drives.',
  thLabel:
    'Collection or library group name assigned to discovered rows (e.g., Movies, SNES).\nUsed for grouping in HTML reports and Greenroom sync.',
  thFolderPath:
    'Local directory or SMB UNC share path to index.\nSupports read-only coexistence with active sister apps via .hklock deferral.',
  thSubfolders:
    'Controls recursive directory traversal for each target folder.\nDisable when indexing flat drop directories.',
  thSystem:
    'Canonical platform/system classification or Auto-Detect.\nDrives No-Intro/Redump DAT matching when auditing ROM collections.',
  thHashTarget:
    'Selects single-file hashing vs. composite folder-unit hashing (PS3/WiiU/BDMV).\nAuto-Detect handles both archives and boot-anchored folder dumps.',
  thExtFilter:
    'Comma-separated file extensions included in the scan (or * for all files).\nCompanion files in Excluded Extensions are still ignored.',
  thDelete:
    'Removes this row from the configuration.\nChanges can be reverted via Ctrl+Z or Snapshot History.',
  thRuleOn:
    'Enables or disables this custom field parsing rule.\nDisabled rules are skipped without losing their pattern.',
  thColumnName:
    'Header name of the custom column added to CSV/TSV, JSON, and HTML outputs.\nMultiple rules targeting the same column use first-match-wins.',
  thAppliesExts:
    'Limits this extraction rule to specific file extensions (e.g., .mkv, .7z, or *).\nLeave * to evaluate against every indexed item.',
  thInspectTarget:
    'Selects which path segment is tested (File Name, Parent Folder, Relative Path, Full Path).\nChoose Name (No Ext) to avoid matching file extensions.',
  thMatchMode:
    'Selects C# Regex Named Capture, $1 group capture, substring match, or delimiter split.\nEvaluated with culture-invariant .NET regular expressions.',
  thPattern:
    'C# regular expression or substring token evaluated against the chosen target.\nInvalid regex syntax is highlighted immediately in the playground.',
  thAssignedVal:
    'Output template written when the pattern matches (supports ${Group} or $1 tokens).\nLeave blank to output the matched value directly.',
  thSplitDelim:
    'Delimiter string used when Match Mode is Split Delimiter Token (e.g., " - ").\nIgnored for Regex and TextContains modes.',
  thSplitIndex:
    'Zero-based token index selected after splitting by Delim.\nOut-of-range indices safely evaluate to an empty cell.',
} as const;
