// @module: Housekeeping.Web/services/coexistenceAndSystemApi
// @purpose: Extracted API helpers for 3-tier stop control, sister-app coexistence, fleet nodes, Greenroom, Tailscale PWA, and System Doctor.
// @depends-on: ../types/models
// @exports: createCoexistenceAndSystemApi, RequestJsonFn
import type {
  AppConfig,
  CoexistenceStatusDto,
  DependencyStatusItem,
  GreenroomStatusDto,
  ModuleId,
  PipelineRuntimeStateDto,
  RemoteNodeConfig,
  RunHistoryEntry,
  TailscalePwaStatusDto,
} from '../types/models';

export type RequestJsonFn = <T>(
  path: string,
  options?: RequestInit & { bypassNodeProxy?: boolean }
) => Promise<T>;

export function createCoexistenceAndSystemApi(requestJson: RequestJsonFn) {
  return {
    // 3-Tier Stop Controls (Stop Current Job vs Stop Entire Queue)
    stopCurrentJob: () =>
      requestJson<{ ok: boolean; stoppedJob: boolean; state: PipelineRuntimeStateDto }>(
        '/pipeline/stop-job',
        { method: 'POST' }
      ),
    stopEntireQueue: () =>
      requestJson<{ ok: boolean; stoppedQueue?: boolean; state: PipelineRuntimeStateDto }>(
        '/pipeline/stop-queue',
        { method: 'POST' }
      ),

    // Sister-App Coexistence Bridge
    getCoexistenceStatus: () =>
      requestJson<CoexistenceStatusDto>('/coexistence/status', { bypassNodeProxy: true }),
    applyCoexistencePreset: (preset: string, jobId?: string, enableAutoAlign?: boolean) =>
      requestJson<{
        ok: boolean;
        preset: string;
        message: string;
        config: AppConfig;
        snapshotsCount: number;
      }>('/coexistence/apply-recommendations', {
        method: 'POST',
        body: JSON.stringify({ preset, jobId: jobId || null, enableAutoAlign }),
        bypassNodeProxy: true,
      }),

    // Greenroom Integration
    getGreenroomStatus: () => requestJson<GreenroomStatusDto>('/greenroom/status'),
    syncGreenroomNow: (SourceModule: ModuleId = 'RomLauncherGenerator') =>
      requestJson<{ Triggered: boolean; MethodUsed: string; TasksRequested: string[]; Summary: string }>(
        '/greenroom/sync-now',
        { method: 'POST', body: JSON.stringify({ SourceModule }) }
      ),

    // Remote Fleet Nodes
    getFleetNodes: (probe = false) =>
      requestJson<{ localMachineName: string; localVersion: string; nodes: RemoteNodeConfig[] }>(
        `/nodes?probe=${probe}`,
        { bypassNodeProxy: true }
      ),
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

    // History, Watchdog & Notifications
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
      requestJson<{ ok: boolean; backupPath: string; fileName: string; format: string }>(
        '/system/backup/now',
        { method: 'POST', bypassNodeProxy: true }
      ),
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
}
