// @module: Housekeeping.Web/hints/jobQueues
// @purpose: Standard 09 2-line hover hints for JobQueuesPage schedule controls, checkboxes, and table headers.
// @depends-on: none
// @exports: JOB_QUEUES_HINTS
export const JOB_QUEUES_HINTS = {
  enableSchedule:
    'Activates automatic background scheduling for this Named Job Queue.\nRespects Game/Streaming Process Guard and chains cleanly if another run is active.',
  toggleAllJobs:
    'Enables or disables every attached job in this queue at once.\nSynchronizes each job’s Enabled flag with Defined Jobs.',
  jobRowEnabled: (name: string) =>
    `Controls whether "${name}" executes when this queue runs.\nUnchecking skips the job in queue runs while keeping its order position.`,
  thOrder:
    'Sequential execution order within this Named Job Queue (1..N).\nUse the Up/Down arrows in Actions to reorder steps.',
  thEnabledCol:
    'Toggles all attached jobs enabled or disabled in one click.\nIndividual rows can be toggled below.',
  thJobName:
    'Display name of the attached job definition.\nClick Edit in Actions to open its full configuration in Job Builder.',
  thModule:
    'Automation module engine powering this job step.\nDetermines tool dependencies and coexistence lock rules.',
  thChain:
    'Optional follow-up job triggered automatically when this step succeeds.\nConfigured via On Success → Trigger in Job Builder.',
  thLiveState:
    'Real-time execution status broadcast over SignalR.\nShows Idle, Running, Completed, Skipped, or Cancelled.',
  thActions:
    'Reorder, edit in Job Builder, or detach this job from the queue.\nDetaching removes it from this queue without deleting the job definition.',
} as const;
