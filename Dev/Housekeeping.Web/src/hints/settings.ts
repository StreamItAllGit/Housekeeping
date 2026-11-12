// @module: Housekeeping.Web/hints/settings
// @purpose: Standard 09 2-line hover hints for SettingsPage checkboxes, toggles, and table headers.
// @depends-on: none
// @exports: SETTINGS_HINTS
export const SETTINGS_HINTS = {
  startWithWindows:
    'Registers Housekeeping in Windows logon startup so schedules stay active.\nUpdates startup registration cleanly when settings are saved.',
  startMinimized:
    'Starts the Desktop Tray Daemon silently in the notification area without popping a browser.\nDouble-click the tray icon anytime to open the dashboard.',
  autoRestartInterrupted:
    'Automatically resumes runs recorded in interrupted_run_checkpoint.json on startup.\nOnly triggers after graceful shutdowns or unexpected host restarts, never manual stops.',
  enableProcessGuard:
    'Defers scheduled background runs while any guarded game, emulator, or OBS process is active.\nManual user-triggered runs still execute immediately.',
  networkDropSafety:
    'Pauses active jobs and polls until dropped SMB/NAS shares come back online.\nPrevents false missing-file failures during router or NAS reboots.',
  atomicPartialWrites:
    'Writes transcodes to .partial working files and commits via exclusive FileShare.None pre-probes.\nQueues a non-destructive Pending Swap if the target media file is currently playing.',
  cleanupPartials:
    'Sweeps abandoned .partial encode artifacts left behind by hard power loss on startup.\nNever touches active files protected by a live .hklock sentinel.',
  distributedLocking:
    'Writes Standard 19 PID+processStartTimeUtc .hklock sentinels during directory mutations.\nPrevents collisions across multi-PC Housekeeping nodes and sister apps.',
  bindLanTailnet:
    'Binds Kestrel to 0.0.0.0 so LAN devices and Tailscale tailnet peers can connect.\nUncheck to bind strictly to 127.0.0.1 loopback only.',
  trustTailscaleHeaders:
    'Accepts Tailscale-User-Login identity headers arriving over local loopback tailscale serve.\nAllows seamless authenticated mobile PWA access over MagicDNS HTTPS.',
  restrictRawShell:
    'Blocks remote non-loopback sessions from editing Custom Scripts or invoking raw shell tools.\nStrongly recommended whenever LAN/Tailnet binding is enabled.',
  greenroomEnabled:
    'Enables HTTP webhook notifications to your local Greenroom (:8420) companion service.\nAllows automatic library catalog refreshes after manifest runs.',
  greenroomRomAutoSync:
    'Triggers Greenroom collect_library + verify_hashes when ROM manifests change.\nNon-blocking if Greenroom is currently offline.',
  greenroomManifestAutoSync:
    'Triggers Greenroom collect_library + collect_plex after Module 8 manifest updates.\nKeeps media and game catalogs synchronized automatically.',
  autoAlignSisterApps:
    'Automatically aligns shared vault exclusions and zero-rewrite sidecar rules with sister apps.\nDefault Off so manual settings are never mutated unless explicitly enabled.',
  customTagToggle: (tag: string) =>
    `Includes [${tag}] log lines when Terminal or Footer Verbosity is set to Custom Tags.\nUncheck to hide [${tag}] entries from the Custom filter view.`,
  enableTrayToasts:
    'Shows Windows system tray balloon notifications on queue completion or errors.\nApplies when running under the Desktop Tray Host.',
  enableDiscordWebhook:
    'Sends rich embed summaries to your Discord webhook URL on configured pipeline events.\nWebhook URL is masked in the UI and excluded from diagnostic bundles.',
  discordQueueStart:
    'Posts a Discord embed whenever a scheduled or manual job queue begins.\nRequires Enable Discord Rich Embed Webhook Notifications.',
  discordQueueFinish:
    'Posts a Discord summary embed with processed/error counts when a queue completes.\nRequires Enable Discord Rich Embed Webhook Notifications.',
  discordJobFinish:
    'Posts an individual Discord update after each job inside a queue finishes.\nLeave unchecked if you prefer one summary per queue.',
  discordErrors:
    'Posts high-visibility Discord alerts whenever a pipeline job encounters errors.\nRecommended for unattended NAS and overnight transcode schedules.',
  preferHttpApi:
    'Communicates with this remote node over HTTP API (:5178) first before falling back to SMB.\nProvides live SignalR terminal streaming and instant queue triggers.',
  thHbId: 'Unique identifier referenced by Module 1, 2, and 3 dropdowns.\nRenaming updates new selections; stale references show a warning badge.',
  thHbName: 'Descriptive human-readable label for this HandBrake profile.\nShown in documentation and profile catalogs.',
  thHbExt: 'Output media container format (.mkv or .mp4).\n.mkv is recommended for multi-track subtitles and chapters.',
  thHbEnc: 'HandBrake video codec engine (CPU x265/x264/AV1 or GPU NVENC/QSV).\nHardware encoders require compatible GPU drivers on the worker node.',
  thHbPreset: 'Encoder speed-vs-efficiency preset passed to HandBrakeCLI.\nSlower presets yield smaller files at the cost of encode time.',
  thHbRf: 'Constant Quality RF target (lower numbers = higher visual quality & larger files).\nTypical HEVC sweet spot is RF 19–23.',
  thHbMaxWh: 'Maximum output bounding box (Width × Height) in pixels.\nPreserves aspect ratio without upscaling smaller sources.',
  thHbAudio: 'Audio codec passed to HandBrakeCLI (AAC, AC3, EAC3, or passthrough copy).\ncopy preserves original bitstream audio without re-encoding.',
  thHbKbps: 'Target audio bitrate in kbps when re-encoding audio tracks.\nIgnored when Audio is set to copy.',
  thHbSubs: 'Subtitle inclusion mode (none, first track, or burned-in).\nExternal .srt companion files are preserved separately.',
  thHbFlags: 'Optional raw HandBrakeCLI flags appended to this profile.\nLeave blank for standard preset behavior.',
  thHbDel: 'Deletes this HandBrake profile from settings.json.\nJobs referencing a deleted profile will show a Missing warning guard.',
  thNodeCol: (col: string) =>
    `${col} configuration for this remote Housekeeping worker node.\nUsed by Fleet HTTP API (:5178) and Clean-Release-Root SMB sync.`,
  thDoctorTool: (col: string) =>
    `${col} reported by the runtime dependency resolver.\nOverrides can be configured under Tools & HandBrake Profiles.`,
  thDoctorDrive: (col: string) =>
    `${col} sampled from mounted local and SMB storage volumes.\nWarns automatically when free space drops below 12% or 25 GB.`,
} as const;
