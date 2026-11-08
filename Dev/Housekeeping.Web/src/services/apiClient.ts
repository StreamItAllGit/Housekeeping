import type {
  AppConfig,
  ArchiveCleanupDryRunResult,
  AuthStatusResponse,
  ConfigBootstrapResponse,
  ConfigSnapshotInfo,
  ConsoleCollectionRule,
  DependencyStatusItem,
  FsBrowseResponse,
  FsDriveItem,
  GreenroomStatusDto,
  HousekeepingJob,
  ManifestFieldParseRule,
  ModuleCatalogItemDto,
  ModuleId,
  PendingPipelineRunDto,
  PipelineRuntimeStateDto,
  RegexPlaygroundResultDto,
  RemoteNodeConfig,
  ReportsQueryResponse,
  RomLauncherGeneratorConfig,
  RunHistoryEntry,
  SidecarVerifyMode,
  TailscalePwaStatusDto,
  TerminalLogEntry,
} from '../types/models';

export class ApiHttpError extends Error {
  status: number;
  validationErrors: string[];
  requiresSetup: boolean;

  constructor(message: string, status: number, validationErrors: string[] = [], requiresSetup = false) {
    super(message);
    this.status = status;
    this.validationErrors = validationErrors;
    this.requiresSetup = requiresSetup;
  }
}

let currentTargetNodeId = 'local';
let onUnauthorizedCallback: (() => void) | null = null;

export function setApiTargetNodeId(nodeId: string) {
  currentTargetNodeId = nodeId || 'local';
}

export function setOnUnauthorizedHandler(fn: (() => void) | null) {
  onUnauthorizedCallback = fn;
}

async function requestJson<T>(
  path: string,
  options: RequestInit & { bypassNodeProxy?: boolean } = {}
): Promise<T> {
  const headers = new Headers(options.headers || {});
  if (options.body && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }
  if (options.bypassNodeProxy) {
    headers.set('X-Bypass-Node-Proxy', '1');
  } else if (currentTargetNodeId && currentTargetNodeId !== 'local') {
    headers.set('X-Target-Node-Id', currentTargetNodeId);
  }

  const resp = await fetch(`/api/v1${path}`, {
    ...options,
    headers,
    credentials: 'same-origin',
  });

  if (resp.status === 401) {
    let requiresSetup = false;
    let msg = 'Authentication required.';
    try {
      const data = await resp.json();
      requiresSetup = Boolean(data?.requiresSetup);
      msg = data?.message || data?.error || msg;
    } catch {
      // ignore
    }
    if (!path.startsWith('/auth/login')) {
      onUnauthorizedCallback?.();
    }
    throw new ApiHttpError(msg, 401, [], requiresSetup);
  }

  if (!resp.ok) {
    let msg = `Request failed (${resp.status})`;
    let errs: string[] = [];
    try {
      const data = await resp.json();
      if (Array.isArray(data?.errors)) {
        errs = data.errors.map(String);
        msg = errs.join(' • ') || data?.error || msg;
      } else {
        msg = data?.message || data?.error || msg;
      }
    } catch {
      // ignore
    }
    throw new ApiHttpError(msg, resp.status, errs);
  }

  return (await resp.json()) as T;
}

export const apiClient = {
  // Auth
  getAuthStatus: () => requestJson<AuthStatusResponse>('/auth/status', { bypassNodeProxy: true }),
  setupPassword: (Password: string) =>
    requestJson<{ ok: boolean; message: string }>('/auth/setup-password', {
      method: 'POST',
      body: JSON.stringify({ Password }),
      bypassNodeProxy: true,
    }),
  login: (Password: string) =>
    requestJson<{ ok: boolean }>('/auth/login', {
      method: 'POST',
      body: JSON.stringify({ Password }),
      bypassNodeProxy: true,
    }),
  logout: () =>
    requestJson<{ ok: boolean }>('/auth/logout', { method: 'POST', bypassNodeProxy: true }),
  changePassword: (CurrentPassword: string, NewPassword: string) =>
    requestJson<{ ok: boolean; message: string }>('/auth/change-password', {
      method: 'POST',
      body: JSON.stringify({ CurrentPassword, NewPassword }),
      bypassNodeProxy: true,
    }),
  rotateNodeToken: () =>
    requestJson<{ nodeApiToken: string }>('/auth/rotate-node-token', {
      method: 'POST',
      bypassNodeProxy: true,
    }),

  // Config & Snapshots
  getConfigBootstrap: () => requestJson<ConfigBootstrapResponse>('/config'),
  saveConfig: (cfg: AppConfig) =>
    requestJson<{ ok: boolean; savedAtUtc: string; snapshotsCount: number }>('/config', {
      method: 'PUT',
      body: JSON.stringify(cfg),
    }),
  listSnapshots: () => requestJson<ConfigSnapshotInfo[]>('/config/snapshots'),
  restoreSnapshot: (snapshotId: string) =>
    requestJson<{ ok: boolean; config: AppConfig; snapshots: ConfigSnapshotInfo[] }>(
      `/config/snapshots/${encodeURIComponent(snapshotId)}/restore`,
      { method: 'POST' }
    ),
  importProfile: (Json: string) =>
    requestJson<{ ok: boolean; config: AppConfig }>('/profiles/import', {
      method: 'POST',
      body: JSON.stringify({ Json }),
    }),

  // Jobs, Queues, Sync Preview & Regex Playground
  createJob: (ModuleType: ModuleId, Name?: string, QueueId?: string) =>
    requestJson<{ ok: boolean; job: HousekeepingJob }>('/jobs/create', {
      method: 'POST',
      body: JSON.stringify({ ModuleType, Name, QueueId }),
    }),
  cloneJob: (jobId: string) =>
    requestJson<{ ok: boolean; job: HousekeepingJob }>(`/jobs/${encodeURIComponent(jobId)}/clone`, {
      method: 'POST',
    }),
  deleteJob: (jobId: string) =>
    requestJson<{ ok: boolean; removed: number }>(`/jobs/${encodeURIComponent(jobId)}`, {
      method: 'DELETE',
    }),
  populateStarterJobs: () =>
    requestJson<{ ok: boolean; addedCount: number }>('/jobs/starter', { method: 'POST' }),
  previewRomSync: (RomConfig: RomLauncherGeneratorConfig, ConsoleOverride?: ConsoleCollectionRule | null) =>
    requestJson<{
      preLaunchCommands: string[];
      postLaunchCommands: string[];
      preLaunchPreviewText: string;
      postLaunchPreviewText: string;
    }>('/rom/sync-preview', {
      method: 'POST',
      body: JSON.stringify({ RomConfig, ConsoleOverride: ConsoleOverride ?? null }),
    }),
  testManifestRegex: (SampleInput: string, Rules: ManifestFieldParseRule[]) =>
    requestJson<RegexPlaygroundResultDto>('/manifest/test-regex', {
      method: 'POST',
      body: JSON.stringify({ SampleInput, Rules }),
    }),
  getManifestPresetBundle: (bundleKey: string) =>
    requestJson<ManifestFieldParseRule[]>(`/manifest/presets/${encodeURIComponent(bundleKey)}`),
  getQueueUpcoming: (queueId: string) =>
    requestJson<{ queueId: string; enableSchedule: boolean; upcomingUtc: string[] }>(
      `/queues/${encodeURIComponent(queueId)}/upcoming`
    ),

  // Modules & Dependencies
  getModulesCatalog: () =>
    requestJson<{
      modules: ModuleCatalogItemDto[];
      dependencies: DependencyStatusItem[];
      rcloneRemotes: string[];
    }>('/modules'),
  toggleModuleAppWide: (moduleId: ModuleId, enabled: boolean) =>
    requestJson<{ ok: boolean; module: string; enabled: boolean }>(
      `/modules/${encodeURIComponent(moduleId)}/toggle?enabled=${enabled}`,
      { method: 'POST' }
    ),
  refreshRcloneRemotes: () =>
    requestJson<{ rcloneRemotes: string[] }>('/modules/rclone-remotes/refresh', { method: 'POST' }),

  // Pipeline Orchestration & Pending Swaps
  getPipelineState: () => requestJson<PipelineRuntimeStateDto>('/pipeline/state'),
  runQueue: (QueueId?: string, IsDryRun = false, ForceCloudResync = false, EnqueueIfBusy = true) =>
    requestJson<{
      ok: boolean;
      started?: boolean;
      queued?: boolean;
      message: string;
      state: PipelineRuntimeStateDto;
    }>('/pipeline/run-queue', {
      method: 'POST',
      body: JSON.stringify({ QueueId, IsDryRun, ForceCloudResync, EnqueueIfBusy }),
    }),
  runJob: (jobId: string, IsDryRun = false, ForceCloudResync = false, EnqueueIfBusy = true) =>
    requestJson<{
      ok: boolean;
      started?: boolean;
      queued?: boolean;
      message: string;
      state: PipelineRuntimeStateDto;
    }>(`/pipeline/run-job/${encodeURIComponent(jobId)}`, {
      method: 'POST',
      body: JSON.stringify({ IsDryRun, ForceCloudResync, EnqueueIfBusy }),
    }),
  getPendingRuns: () => requestJson<PendingPipelineRunDto[]>('/pipeline/pending-runs'),
  removePendingRun: (itemKey: string) =>
    requestJson<{ ok: boolean; removed: boolean; state: PipelineRuntimeStateDto }>(
      `/pipeline/pending-runs/${encodeURIComponent(itemKey)}`,
      { method: 'DELETE' }
    ),
  clearPendingRuns: () =>
    requestJson<{ ok: boolean; cleared: number; state: PipelineRuntimeStateDto }>(
      '/pipeline/pending-runs/clear',
      { method: 'POST' }
    ),
  togglePause: () =>
    requestJson<{ ok: boolean; isPaused: boolean; state: PipelineRuntimeStateDto }>('/pipeline/pause', {
      method: 'POST',
    }),
  toggleSuspend: () =>
    requestJson<{ ok: boolean; isSuspended: boolean; state: PipelineRuntimeStateDto }>('/pipeline/suspend', {
      method: 'POST',
    }),
  stopPipeline: () =>
    requestJson<{ ok: boolean; state: PipelineRuntimeStateDto }>('/pipeline/stop', { method: 'POST' }),
  resumeInterruptedRun: (RestartFullQueue = false) =>
    requestJson<{ ok: boolean; message: string; state: PipelineRuntimeStateDto }>(
      '/pipeline/interrupted/resume',
      {
        method: 'POST',
        body: JSON.stringify({ RestartFullQueue }),
      }
    ),
  dismissInterruptedRun: () =>
    requestJson<{ ok: boolean; state: PipelineRuntimeStateDto }>('/pipeline/interrupted/dismiss', {
      method: 'POST',
    }),
  setPostRunPowerAction: (Action: string) =>
    requestJson<{ ok: boolean; postRunPowerAction: string }>('/pipeline/post-power', {
      method: 'POST',
      body: JSON.stringify({ Action }),
    }),
  retryPendingSwaps: () =>
    requestJson<{ ok: boolean; committed: number; remaining: number }>('/pipeline/pending-swaps/retry', {
      method: 'POST',
    }),
  discardPendingSwaps: () =>
    requestJson<{ ok: boolean; remaining: number }>('/pipeline/pending-swaps/discard', {
      method: 'POST',
    }),

  // Terminal & CLI
  getTerminalHistory: (limit = 1500) =>
    requestJson<{
      entries: TerminalLogEntry[];
      workingDirectory: string;
      isCommandRunning: boolean;
      commandHistory: string[];
      footerLine: string;
    }>(`/terminal/history?limit=${limit}`),
  clearTerminal: () => requestJson<{ ok: boolean }>('/terminal/clear', { method: 'POST' }),
  execTerminalCommand: (Command: string) =>
    requestJson<{
      ok: boolean;
      exitCode: number;
      workingDirectory: string;
      entries?: TerminalLogEntry[];
    }>('/terminal/exec', {
      method: 'POST',
      body: JSON.stringify({ Command }),
    }),
  cancelTerminalCommand: () => requestJson<{ ok: boolean }>('/terminal/cancel', { method: 'POST' }),
  getRemoteTerminalTail: (nodeId: string, limit = 300, signal?: AbortSignal) =>
    requestJson<{ nodeId: string; nodeName: string; online?: boolean; status: string; lines: string[] }>(
      `/terminal/remote-tail/${encodeURIComponent(nodeId)}?limit=${limit}`,
      { bypassNodeProxy: true, signal }
    ),

  // Compliance Reports & .hash Verifier
  getReports: (params?: { console?: string; category?: string; status?: string; search?: string }) => {
    const qs = new URLSearchParams();
    if (params?.console) qs.set('console', params.console);
    if (params?.category) qs.set('category', params.category);
    if (params?.status) qs.set('status', params.status);
    if (params?.search) qs.set('search', params.search);
    const suffix = qs.toString() ? `?${qs.toString()}` : '';
    return requestJson<ReportsQueryResponse>(`/reports${suffix}`);
  },
  runComplianceScan: (jobId?: string) =>
    requestJson<{
      ok: boolean;
      started?: boolean;
      flaggedCount: number;
      message: string;
      state?: PipelineRuntimeStateDto;
    }>(`/reports/scan${jobId ? `?jobId=${encodeURIComponent(jobId)}` : ''}`, { method: 'POST' }),
  dryRunCleanArchive: (reportId: string, ExplicitInternalPaths?: string[]) =>
    requestJson<ArchiveCleanupDryRunResult>(
      `/reports/${encodeURIComponent(reportId)}/dry-run-clean`,
      {
        method: 'POST',
        body: JSON.stringify({ ReportIds: [reportId], ExplicitInternalPaths: ExplicitInternalPaths ?? null }),
      }
    ),
  cleanExtraFiles: (ReportIds: string[], ExplicitInternalPaths?: string[]) =>
    requestJson<{ ok: boolean; cleaned: number; failed: number; messages: string[] }>(
      '/reports/clean-extra',
      {
        method: 'POST',
        body: JSON.stringify({ ReportIds, ExplicitInternalPaths: ExplicitInternalPaths ?? null }),
      }
    ),
  healReportHashes: (ReportIds: string[]) =>
    requestJson<{ ok: boolean; healedCount: number }>('/reports/heal-hash', {
      method: 'POST',
      body: JSON.stringify({ ReportIds, Ignored: false }),
    }),
  setReportsIgnored: (ReportIds: string[], Ignored: boolean) =>
    requestJson<{ ok: boolean }>('/reports/ignore', {
      method: 'POST',
      body: JSON.stringify({ ReportIds, Ignored }),
    }),
  clearResolvedReports: () =>
    requestJson<{ ok: boolean; removedCount: number }>('/reports/clear-resolved', { method: 'POST' }),
  runHashVerifier: (JobId: string | undefined, SourceModule: ModuleId, VerifyMode: SidecarVerifyMode) =>
    requestJson<{
      ok: boolean;
      started?: boolean;
      message: string;
      state?: PipelineRuntimeStateDto;
    }>('/hash-verifier/run', {
      method: 'POST',
      body: JSON.stringify({ JobId, SourceModule, VerifyMode }),
    }),

  // Hybrid Filesystem & Desktop Host
  listDrives: () => requestJson<FsDriveItem[]>('/filesystem/drives'),
  browseDirectory: (path: string, includeFiles = false) =>
    requestJson<FsBrowseResponse>(
      `/filesystem/browse?path=${encodeURIComponent(path)}&includeFiles=${includeFiles}`
    ),
  nativePickFolder: (InitialPath?: string, Title?: string) =>
    requestJson<{ cancelled: boolean; path?: string | null }>('/filesystem/native-pick-folder', {
      method: 'POST',
      body: JSON.stringify({ InitialPath, Title }),
      bypassNodeProxy: true,
    }),
  nativePickFile: (InitialPath?: string, Filter?: string, Title?: string) =>
    requestJson<{ cancelled: boolean; path?: string | null }>('/filesystem/native-pick-file', {
      method: 'POST',
      body: JSON.stringify({ InitialPath, Filter, Title }),
      bypassNodeProxy: true,
    }),
  revealInExplorer: (Path: string) =>
    requestJson<{ ok: boolean }>('/filesystem/reveal-in-explorer', {
      method: 'POST',
      body: JSON.stringify({ Path }),
      bypassNodeProxy: true,
    }),
  openStandaloneWindow: () =>
    requestJson<{ ok: boolean }>('/desktop/open-standalone', {
      method: 'POST',
      bypassNodeProxy: true,
    }),

  // Greenroom Integration
  getGreenroomStatus: () => requestJson<GreenroomStatusDto>('/greenroom/status'),
  syncGreenroomNow: (SourceModule: ModuleId = 'RomLauncherGenerator') =>
    requestJson<{ Triggered: boolean; MethodUsed: string; TasksRequested: string[]; Summary: string }>(
      '/greenroom/sync-now',
      {
        method: 'POST',
        body: JSON.stringify({ SourceModule }),
      }
    ),

  // Remote Fleet Nodes
  getFleetNodes: (probe = false) =>
    requestJson<{
      localMachineName: string;
      localVersion: string;
      nodes: RemoteNodeConfig[];
    }>(`/nodes?probe=${probe}`, { bypassNodeProxy: true }),
  pushNodeSettings: (nodeId: string) =>
    requestJson<{ ok: boolean }>(`/nodes/${encodeURIComponent(nodeId)}/push-settings`, {
      method: 'POST',
      bypassNodeProxy: true,
    }),
  pushNodeApp: (nodeId: string) =>
    requestJson<{ ok: boolean }>(`/nodes/${encodeURIComponent(nodeId)}/push-app`, {
      method: 'POST',
      bypassNodeProxy: true,
    }),
  sendNodeCommand: (nodeId: string, Action: string, QueueId?: string, JobId?: string) =>
    requestJson<{ ok: boolean; transport: string }>(`/nodes/${encodeURIComponent(nodeId)}/command`, {
      method: 'POST',
      body: JSON.stringify({ Action, QueueId, JobId }),
      bypassNodeProxy: true,
    }),

  // History & Settings Helpers
  getHistory: () => requestJson<RunHistoryEntry[]>('/history'),
  clearHistory: () => requestJson<{ ok: boolean }>('/history', { method: 'DELETE' }),
  getHistoryLog: (index: number) =>
    requestJson<{
      ModulesSummary: string;
      StartedAt: string;
      CompletedAt: string;
      Status: string;
      LogFilePath: string;
      content: string;
    }>(`/history/${index}/log`),
  getWatchdogStatus: () =>
    requestJson<{ isRegistered: boolean; taskName: string }>('/settings/watchdog'),
  setWatchdog: (Enable: boolean, IntervalMinutes: number) =>
    requestJson<{ ok: boolean; isRegistered: boolean; message: string }>('/settings/watchdog', {
      method: 'POST',
      body: JSON.stringify({ Enable, IntervalMinutes }),
    }),
  testDiscord: () =>
    requestJson<{ ok: boolean; message: string }>('/settings/test-discord', { method: 'POST' }),
  testToast: () =>
    requestJson<{ ok: boolean; message: string }>('/settings/test-toast', { method: 'POST' }),

  // Tailscale Serve HTTPS & Mobile PWA
  getTailscalePwaStatus: () =>
    requestJson<TailscalePwaStatusDto>('/system/tailscale-pwa', { bypassNodeProxy: true }),
  enableTailscalePwa: () =>
    requestJson<TailscalePwaStatusDto>('/system/tailscale-pwa/enable', {
      method: 'POST',
      bypassNodeProxy: true,
    }),
  disableTailscalePwa: () =>
    requestJson<TailscalePwaStatusDto>('/system/tailscale-pwa/disable', {
      method: 'POST',
      bypassNodeProxy: true,
    }),

  // System Doctor & Maintenance Operations
  triggerBackupNow: () =>
    requestJson<{ ok: boolean; backupPath: string; fileName: string; format: string }>('/system/backup/now', {
      method: 'POST',
      bypassNodeProxy: true,
    }),
  getDoctorDiagnostics: () =>
    requestJson<{
      appVersion: string;
      machineName: string;
      osVersion: string;
      isTestMode: boolean;
      isSandboxed: boolean;
      dependencies: DependencyStatusItem[];
      drives: Array<{
        name: string;
        driveType: string;
        volumeLabel: string;
        totalGb: number;
        freeGb: number;
        isReady: boolean;
      }>;
    }>('/system/doctor/diagnostics', { bypassNodeProxy: true }),
  exportDiagnosticBundle: async () => {
    const resp = await fetch('/api/v1/system/doctor/export-bundle', {
      method: 'POST',
      credentials: 'same-origin',
    });
    if (!resp.ok) throw new Error('Failed to export diagnostic bundle');
    const blob = await resp.blob();
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `Housekeeping_Diagnostics_${new Date().toISOString().replace(/[:.]/g, '-')}.json`;
    a.click();
    URL.revokeObjectURL(url);
  },
};
