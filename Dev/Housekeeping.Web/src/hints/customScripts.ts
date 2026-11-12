// @module: Housekeeping.Web/hints/customScripts
// @purpose: Standard 09 2-line hover hints for Module 7 (Custom Scripts & CLI Tasks) controls and table headers.
// @depends-on: none
// @exports: CUSTOM_SCRIPTS_HINTS
export const CUSTOM_SCRIPTS_HINTS = {
  taskEnabled:
    'Enables this script or CLI command step when the job executes.\nDisabled steps are logged as skipped and preserve pipeline order.',
  externalCmd:
    'Launches this command in a visible standalone Windows CMD console window.\nLeave unchecked to stream stdout/stderr directly into the Dual-Pane Terminal.',
  thOn:
    'Controls whether each script task runs during pipeline execution.\nUncheck to bypass a step temporarily.',
  thTaskName:
    'Friendly step name displayed in live progress and run history logs.\nHelps distinguish multiple scripts inside one job.',
  thScript:
    'Path to the .bat, .cmd, .ps1, .py, or .exe binary to execute.\nEditable only from localhost when Raw Shell Hardening is active.',
  thArgs:
    'Command-line flags and arguments passed to the script or executable.\nQuote paths containing spaces as needed.',
  thWorkDir:
    'Working directory set before spawning the child process.\nDefaults to the script parent directory when left blank.',
  thReqPath:
    'Optional drive or UNC path verified before running this task.\nPauses for network recovery if Network Drop Safety is enabled.',
  thExtCmd:
    'Runs the task in an interactive external CMD window instead of headless capture.\nOnly applies on local interactive Windows sessions.',
  thDelete:
    'Removes this task step from the job pipeline.\nUse Ctrl+Z or Snapshot History to undo.',
} as const;
