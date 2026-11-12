// @module: Housekeeping.Web/hints/romLauncher
// @purpose: Standard 09 2-line hover hints for Module 4 (ROM & PC Game Launcher Generator) controls and tables.
// @depends-on: none
// @exports: ROM_LAUNCHER_HINTS
export const ROM_LAUNCHER_HINTS = {
  autoM3u:
    'Automatically generates multi-disc .m3u playlists when Disc 1..N sets are detected.\nPoints generated emulator launchers at the unified .m3u playlist.',
  autoCompress7z:
    'Packages loose unarchived ROMs into solid .7z archives before launcher creation.\nAutomatically excludes *.hash, *.partial*, *.hklock, and .archive_hashes.json.',
  deleteOriginals:
    'Deletes loose source ROM files only after 7z compression succeeds with exit code 0.\nNever deletes originals during Dry Run or if archive verification fails.',
  fast7zInspect:
    'Reads inner ROM filenames via zero-extraction 7z l -slt header inspection.\nAvoids decompressing multi-gigabyte archives just to build launcher scripts.',
  extractOnLaunch:
    'Extracts compressed .7z archives to the Fast SSD Scratch folder when the .bat runs.\nCleans up the scratch folder automatically after the emulator exits.',
  startMenuShortcuts:
    'Creates categorized Windows Start Menu .url shortcuts per console collection.\nOrphaned shortcuts are garbage-collected when ROMs are renamed or removed.',
  localLauncherBeside:
    'Writes a companion "<Game> - Launcher.bat" directly next to each ROM archive.\nUseful when browsing NAS folders directly in Windows Explorer.',
  refreshLaunchers:
    'Rewrites existing .bat launcher scripts even if they already exist on disk.\nEnable temporarily after changing emulator paths, cores, or save-sync rules.',
  collectionEnabled:
    'Includes this console or PC collection rule during launcher generation.\nUncheck to skip scanning this system without losing its path settings.',
  prePostSyncToggle:
    'Embeds sequential rclone save/state sync commands before launch and after emulator exit.\nCoordinates with uStation (:8460) and skips active .hklock directories.',
  syncRowEnabled:
    'Includes this save/state folder pair in generated pre-launch and post-launch commands.\nDisabled rows stay saved without appearing in .bat scripts.',
  customPrePostCli:
    'Replaces auto-generated rclone commands with your custom raw CLI commands below.\nPreview panes switch to editable textareas when enabled.',
  overrideConsoleSync:
    'Uses custom per-console save/state sync rows instead of the job-level defaults.\nIdeal for emulators with isolated portable memory-card directories.',
  thOn:
    'Toggles whether this row is active during Module 4 execution.\nDisabled rows remain stored in settings.json.',
  thConsole:
    'Canonical system name used for launcher subfolders and compliance rules.\nSupports standard No-Intro / Redump platform naming.',
  thSubfolder:
    'Short folder name used inside the SSD extraction scratch directory.\nKeeps extracted temporary files isolated per console.',
  thRunner:
    'Emulator CLI switch preset matching RomLauncherGeneratorModule.CommandBuilder.\nMissing custom runner names show a warning guard until updated.',
  thEmuPath:
    'Path to the emulator executable (e.g., retroarch.exe, pcsx2-qt.exe).\nSupports local paths or shared portable emulator directories.',
  thCoreArgs:
    'Libretro core DLL name (for RetroArch) or custom launch template tokens.\nSupports {emu}, {rom}, and {romDir} placeholders in Custom runner mode.',
  thExts:
    'Comma-separated ROM or disc descriptor extensions launched by this rule.\nOrder determines priority when an archive holds multiple files (e.g., .m3u first).',
  thArchivesPath:
    'Directory containing ROM archives or game folders for this system.\nAutomatically defers if uStation or ShadowLibrary holds an active .hklock.',
  thTemplate:
    'Archive structure compliance ruleset audited in the Reports Center.\nValidates inner extensions, .cue/.gdi tracks, and wrapper folders.',
  thSyncOverride:
    'Opens per-console save and save-state cloud sync overrides.\nShows Custom badge when overriding job-level sync rows.',
  thDelete:
    'Removes this row from the table.\nUse Ctrl+Z or Snapshot History to restore if needed.',
  thSyncLabel:
    'Descriptive label for this save/state row (e.g., Saves, States, MemCards).\nPrinted in launcher sync progress output.',
  thSyncMode:
    'Selects rclone bisync (2-way), sync (1-way), or copy (non-destructive).\nbisync keeps desktop and handheld saves converged.',
  thLocalSave:
    'Local emulator save or state directory synchronized on launch/exit.\nSkipped safely if the directory does not exist yet.',
  thRemoteSave:
    'rclone remote destination (e.g., gdrive:EmuSaves/PS2).\nMust match a configured remote in rclone.conf.',
} as const;
