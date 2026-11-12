// @module: Housekeeping.Web/utils/jobSummary
// @purpose: Summarizes per-module HousekeepingJob configuration for the Detailed view in JobBuilderPage.
// @depends-on: ../types/models
// @exports: summarizeJob
import type { HousekeepingJob } from '../types/models';

export function summarizeJob(job: HousekeepingJob): string {
  switch (job.ModuleType) {
    case 'ProxyGenerator':
      return `${job.ProxyGenerator?.Folders?.filter((f) => f.Enabled).length ?? 0} folder(s) • ${job.ProxyGenerator?.HandBrakeProfileId || 'Proxy_360p'}`;
    case 'ArchiveCompressor':
      return `${job.ArchiveCompressor?.Mappings?.filter((m) => m.Enabled).length ?? 0} pair(s) • ${job.ArchiveCompressor?.HandBrakeProfileId || 'Archive_LQ'}`;
    case 'MediaTranscoder':
      return `${(job.MediaTranscoder?.DrivesToScan?.length ?? 0) + (job.MediaTranscoder?.CustomFoldersToScan?.filter((f) => f.Enabled).length ?? 0)} root(s) • ${job.MediaTranscoder?.MasterFileTag || '(Original)'}`;
    case 'RomLauncherGenerator':
      return `${job.RomLauncherGenerator?.Collections?.filter((c) => c.Enabled).length ?? 0} console(s) • Sync: ${job.RomLauncherGenerator?.EnablePrePostLaunchSyncCommands ? 'On' : 'Off'}`;
    case 'CloudSync':
      return `${job.CloudSync?.Jobs?.filter((j) => j.Enabled).length ?? 0} rclone sync pair(s)`;
    case 'HumbleSync':
      return job.HumbleSync?.LibraryPath || 'Library path unset';
    case 'CustomScripts':
      return `${job.CustomScripts?.Tasks?.filter((t) => t.Enabled).length ?? 0} script task(s)`;
    case 'CollectionManifest':
      return `${job.CollectionManifest?.TargetFolders?.filter((f) => f.Enabled).length ?? 0} folder(s) • Rules: ${job.CollectionManifest?.FieldParseRules?.filter((r) => r.Enabled).length ?? 0}`;
    default:
      return '';
  }
}
