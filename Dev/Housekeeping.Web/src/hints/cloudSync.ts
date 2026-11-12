// @module: Housekeeping.Web/hints/cloudSync
// @purpose: Standard 09 2-line hover hints for Module 5 (Cloud Folder Sync via rclone) controls and table headers.
// @depends-on: none
// @exports: CLOUD_SYNC_HINTS
export const CLOUD_SYNC_HINTS = {
  pairEnabled:
    'Enables rclone synchronization for this local-to-remote folder pair.\nAutomatically defers if an active .hklock sentinel exists in the folder tree.',
  thOn:
    'Toggles whether this rclone sync pair runs during Cloud Sync execution.\nDisabled pairs remain saved for future runs.',
  thLabel:
    'Friendly label for this sync pair in terminal logs and notifications.\nUsed to identify individual remote transfers.',
  thMode:
    'Selects rclone bisync (2-way), sync (1-way mirror), or copy (additive only).\nbisync requires a one-time --resync initialization on new pairs.',
  thLocalFolder:
    'Local drive or SMB folder synchronized with the cloud remote.\nAutomatically excludes *.hklock, *.partial*, and *.redroom-part.',
  thRemoteTarget:
    'rclone remote and path specification (e.g., gdrive:Backups).\nClick Refresh rclone Remotes to populate configured remotes.',
  thFlags:
    'Additional rclone CLI flags appended to this pair (e.g., --resilient --verbose).\nTransient lock/partial exclusions are always injected automatically.',
  thDelete:
    'Removes this sync pair from the job configuration.\nDoes not delete local or remote cloud files.',
} as const;
