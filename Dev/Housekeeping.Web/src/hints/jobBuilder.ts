// @module: Housekeeping.Web/hints/jobBuilder
// @purpose: Standard 09 2-line hover hints for JobBuilderPage checkboxes and toggles.
// @depends-on: none
// @exports: JOB_BUILDER_HINTS
export const JOB_BUILDER_HINTS = {
  sidebarJobEnabled: (name: string) =>
    `Enables or disables "${name}" across attached job queues.\nDisabled jobs remain editable here and can still be run manually.`,
  enabledInQueue:
    'Allows this job to execute when its parent Named Queue runs.\nUncheck to skip this job during scheduled/queue runs without detaching it.',
  runInExternalCmd:
    'Spawns child CLI tools for this job in a separate visible CMD window.\nLeave unchecked for background capture in the Dual-Pane Terminal.',
} as const;
