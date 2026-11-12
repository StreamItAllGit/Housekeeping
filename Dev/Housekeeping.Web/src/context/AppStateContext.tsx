import React, { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react';
import {
  applyMetaColorScheme,
  readInitialColorScheme,
  readInitialDensity,
  readTabFromHash,
  useUiLayoutPersistence,
  type ColorSchemeMode,
  type NavTabId,
  type UiDensityMode,
} from '../hooks/useUiLayoutPersistence';
import {
  apiClient,
  ApiHttpError,
  setApiTargetNodeId,
  setOnUnauthorizedHandler,
} from '../services/apiClient';
import { HousekeepingSignalRClient } from '../services/signalrClient';
import type {
  AppConfig,
  ArchiveComplianceTemplate,
  AuthStatusResponse,
  ConsoleSystemEntry,
  GreenroomStatusDto,
  HousekeepingJob,
  PipelineRuntimeStateDto,
  SystemPerformanceStats,
  TerminalLogEntry,
  TerminalSizePreset,
  UiLayoutConfig,
} from '../types/models';

export type { ColorSchemeMode, NavTabId, UiDensityMode };
export type SaveIndicatorState = 'saved' | 'saving' | 'error';

interface AppStateContextValue {
  activeTab: NavTabId;
  setActiveTab: (tab: NavTabId) => void;
  selectedJobId: string;
  setSelectedJobId: (jobId: string) => void;
  openJobInBuilder: (jobId: string) => void;

  authStatus: AuthStatusResponse | null;
  authGateOpen: boolean;
  setAuthGateOpen: (open: boolean) => void;
  refreshAuthAndBootstrap: () => Promise<void>;

  config: AppConfig | null;
  consoleCatalog: ConsoleSystemEntry[];
  builtInArchiveTemplates: ArchiveComplianceTemplate[];
  updateConfig: (mutator: (draft: AppConfig) => void) => void;
  updateJob: (jobId: string, mutator: (job: HousekeepingJob) => void) => void;
  replaceConfigFromServer: (cfg: AppConfig, snapshotsCount?: number) => void;
  flushSaveNow: () => Promise<boolean>;
  forceSaveNow: (showFeedbackToast?: boolean) => Promise<boolean>;

  saveState: SaveIndicatorState;
  saveErrors: string[];
  snapshotsCount: number;
  setSnapshotsCount: React.Dispatch<React.SetStateAction<number>>;

  pipelineState: PipelineRuntimeStateDto;
  setPipelineState: React.Dispatch<React.SetStateAction<PipelineRuntimeStateDto>>;
  telemetry: SystemPerformanceStats | null;
  openComplianceCount: number;
  setOpenComplianceCount: React.Dispatch<React.SetStateAction<number>>;
  greenroomStatus: GreenroomStatusDto | null;
  refreshGreenroomStatus: () => Promise<void>;

  terminalEntries: TerminalLogEntry[];
  clearTerminalEntries: () => Promise<void>;
  cliWorkingDir: string;
  cliRunning: boolean;
  cliHistory: string[];
  runCliCommand: (cmd: string) => Promise<void>;
  cancelCliCommand: () => Promise<void>;

  activeTargetNodeId: string;
  setActiveTargetNodeId: (nodeId: string) => void;
  realtimeConnected: boolean;

  colorScheme: ColorSchemeMode;
  setColorScheme: (mode: ColorSchemeMode) => void;
  toggleTwoStateColorScheme: () => void;
  density: UiDensityMode;
  setDensity: (d: UiDensityMode) => void;

  uiLayout: UiLayoutConfig;
  setTerminalHeight: (heightPx: number, preset?: TerminalSizePreset) => void;
  applyTerminalPreset: (preset: 'small' | 'medium' | 'large') => void;
  toggleSidebarCollapsed: () => void;
  setTerminalWordWrap: (wrap: boolean) => void;
  drivesModalOpen: boolean;
  setDrivesModalOpen: React.Dispatch<React.SetStateAction<boolean>>;

  globalDrawerOpen: boolean;
  setGlobalDrawerOpen: React.Dispatch<React.SetStateAction<boolean>>;
  toastMessage: string | null;
  showToast: (msg: string) => void;
}

const DEFAULT_PIPELINE_STATE: PipelineRuntimeStateDto = {
  IsRunning: false,
  IsPaused: false,
  IsProcessSuspended: false,
  IsDryRun: false,
  CurrentJobIndex: 0,
  TotalJobs: 0,
  ProgressPercent: 0,
  SubprocessStatusText: 'Ready — Pipeline idle',
  FooterStatusText: 'Ready — Housekeeping Engine Online',
  IsWaitingForNetwork: false,
  NetworkWaitMessage: '',
  SessionProcessed: 0,
  SessionSkipped: 0,
  SessionErrors: 0,
  PendingSwapCount: 0,
  GlobalNextRunStatusText: 'Schedule: Disabled',
  PostRunPowerAction: 'StayAwake',
  PendingRuns: [],
  JobStates: {},
};

const AppStateContext = createContext<AppStateContextValue | null>(null);

export const AppStateProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [activeTab, setActiveTabState] = useState<NavTabId>(readTabFromHash);
  const [selectedJobId, setSelectedJobId] = useState<string>('');

  const setActiveTab = useCallback((tab: NavTabId) => {
    setActiveTabState(tab);
    try {
      const nextHash = `#${tab}`;
      if (window.location.hash !== nextHash) {
        window.history.replaceState(null, '', nextHash);
      }
    } catch (err) {
      console.debug('[AppStateContext] history.replaceState skipped:', err);
    }
  }, []);

  useEffect(() => {
    const onHashChange = () => setActiveTabState(readTabFromHash());
    window.addEventListener('hashchange', onHashChange);
    return () => window.removeEventListener('hashchange', onHashChange);
  }, []);

  const [authStatus, setAuthStatus] = useState<AuthStatusResponse | null>(null);
  const [authGateOpen, setAuthGateOpen] = useState(false);

  const [config, setConfig] = useState<AppConfig | null>(null);
  const [consoleCatalog, setConsoleCatalog] = useState<ConsoleSystemEntry[]>([]);
  const [builtInArchiveTemplates, setBuiltInArchiveTemplates] = useState<ArchiveComplianceTemplate[]>([]);

  const [saveState, setSaveState] = useState<SaveIndicatorState>('saved');
  const [saveErrors, setSaveErrors] = useState<string[]>([]);
  const [snapshotsCount, setSnapshotsCount] = useState<number>(0);

  const [pipelineState, setPipelineState] = useState<PipelineRuntimeStateDto>(DEFAULT_PIPELINE_STATE);
  const [telemetry, setTelemetry] = useState<SystemPerformanceStats | null>(null);
  const [openComplianceCount, setOpenComplianceCount] = useState<number>(0);
  const [greenroomStatus, setGreenroomStatus] = useState<GreenroomStatusDto | null>(null);

  const [terminalEntries, setTerminalEntries] = useState<TerminalLogEntry[]>([]);
  const [cliWorkingDir, setCliWorkingDir] = useState<string>('');
  const [cliRunning, setCliRunning] = useState<boolean>(false);
  const [cliHistory, setCliHistory] = useState<string[]>([]);

  const [activeTargetNodeId, setActiveTargetNodeIdState] = useState<string>('local');
  const [realtimeConnected, setRealtimeConnected] = useState<boolean>(false);

  const [colorScheme, setColorSchemeState] = useState<ColorSchemeMode>(readInitialColorScheme);
  const [density, setDensityState] = useState<UiDensityMode>(readInitialDensity);
  const [globalDrawerOpen, setGlobalDrawerOpen] = useState<boolean>(false);
  const [toastMessage, setToastMessage] = useState<string | null>(null);

  const pendingSaveTimerRef = useRef<number | null>(null);
  const latestConfigRef = useRef<AppConfig | null>(null);
  const editRevisionRef = useRef<number>(0);
  const savedRevisionRef = useRef<number>(0);
  const saveChainRef = useRef<Promise<boolean>>(Promise.resolve(true));
  const signalrRef = useRef<HousekeepingSignalRClient>(new HousekeepingSignalRClient());
  const toastTimerRef = useRef<number | null>(null);

  const appendUniqueTerminalEntries = useCallback((incoming: TerminalLogEntry[]) => {
    if (!Array.isArray(incoming) || incoming.length === 0) return;
    setTerminalEntries((prev) => {
      const cap = Math.max(500, Math.min(5000, latestConfigRef.current?.TerminalMaxLines || 1500));
      const seen = new Set<number>(prev.map((e) => e.Id));
      const additions = incoming.filter((e) => e && !seen.has(e.Id));
      if (additions.length === 0) return prev;
      const merged = prev.concat(additions);
      return merged.length > cap ? merged.slice(merged.length - cap) : merged;
    });
  }, []);

  const showToast = useCallback((msg: string) => {
    setToastMessage(msg);
    if (toastTimerRef.current) window.clearTimeout(toastTimerRef.current);
    toastTimerRef.current = window.setTimeout(() => setToastMessage(null), 3600);
  }, []);

  const setColorScheme = useCallback((mode: ColorSchemeMode) => {
    setColorSchemeState(mode);
    applyMetaColorScheme(mode);
  }, []);

  const toggleTwoStateColorScheme = useCallback(() => {
    const sysDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    const currentRenderedDark =
      colorScheme === 'dark' ? true : colorScheme === 'light' ? false : sysDark;
    const targetLiteral: 'dark' | 'light' = !currentRenderedDark ? 'dark' : 'light';
    const sysLiteral: 'dark' | 'light' = sysDark ? 'dark' : 'light';
    setColorScheme(targetLiteral === sysLiteral ? 'system' : targetLiteral);
  }, [colorScheme, setColorScheme]);

  const setDensity = useCallback((d: UiDensityMode) => {
    setDensityState(d);
    document.documentElement.dataset.density = d;
    try {
      localStorage.setItem('hk-density', d);
    } catch (err) {
      console.debug('[AppStateContext] density localStorage write skipped:', err);
    }
  }, []);

  useEffect(() => {
    const onStorage = (e: StorageEvent) => {
      if (e.key === 'color-scheme') {
        const next: ColorSchemeMode =
          e.newValue === 'dark' || e.newValue === 'light' ? e.newValue : 'system';
        setColorSchemeState(next);
        const meta = document.querySelector('meta[name="color-scheme"]');
        if (meta) meta.setAttribute('content', next === 'system' ? 'light dark' : next);
      } else if (e.key === 'hk-density') {
        if (e.newValue === 'compact' || e.newValue === 'comfortable' || e.newValue === 'touch') {
          setDensityState(e.newValue);
          document.documentElement.dataset.density = e.newValue;
        }
      }
    };
    window.addEventListener('storage', onStorage);
    return () => window.removeEventListener('storage', onStorage);
  }, []);

  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key === '`') {
        e.preventDefault();
        setGlobalDrawerOpen((prev) => !prev);
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, []);

  const refreshGreenroomStatus = useCallback(async () => {
    try {
      const st = await apiClient.getGreenroomStatus();
      setGreenroomStatus(st);
    } catch (err) {
      console.debug('[AppStateContext] Greenroom status probe non-fatal:', err);
    }
  }, []);

  const connectRealtime = useCallback(
    async (nodeId: string) => {
      await signalrRef.current.connect(nodeId, {
        onTerminalBatch: (batch) => appendUniqueTerminalEntries(batch),
        onTerminalCleared: () => setTerminalEntries([]),
        onPipelineState: (st) => setPipelineState(st),
        onSubprocessProgress: (prog) => {
          setPipelineState((prev) => ({
            ...prev,
            ProgressPercent: prog.Percent,
            SubprocessStatusText: prog.StatusText || prev.SubprocessStatusText,
          }));
        },
        onTelemetrySnapshot: (snap) => {
          if (snap.Telemetry) setTelemetry(snap.Telemetry);
          setOpenComplianceCount(snap.OpenComplianceCount ?? 0);
          setPipelineState((prev) => ({
            ...prev,
            GlobalNextRunStatusText: snap.GlobalNextRunText || prev.GlobalNextRunStatusText,
            GlobalNextRunUtc: snap.GlobalNextRunUtc,
            GlobalNextRunQueueName: snap.GlobalNextRunQueueName,
            PendingSwapCount: snap.PendingSwapCount ?? prev.PendingSwapCount,
            PendingSwapSampleFile: snap.PendingSwapSampleFile ?? prev.PendingSwapSampleFile,
            SessionProcessed:
              typeof snap.SessionProcessed === 'number' ? snap.SessionProcessed : prev.SessionProcessed,
            SessionSkipped:
              typeof snap.SessionSkipped === 'number' ? snap.SessionSkipped : prev.SessionSkipped,
            SessionErrors:
              typeof snap.SessionErrors === 'number' ? snap.SessionErrors : prev.SessionErrors,
            FooterStatusText: snap.FooterStatusText || prev.FooterStatusText,
            InterruptedRun:
              snap.InterruptedRun !== undefined ? snap.InterruptedRun : prev.InterruptedRun,
          }));
        },
        onComplianceSummary: (s) => setOpenComplianceCount(s.OpenCount ?? 0),
        onConnectionChanged: (ok) => setRealtimeConnected(ok),
      });
    },
    [appendUniqueTerminalEntries]
  );

  const loadNodeWorkspace = useCallback(
    async (nodeId: string) => {
      const [boot, pState, termHist, reports] = await Promise.all([
        apiClient.getConfigBootstrap(),
        apiClient.getPipelineState(),
        apiClient.getTerminalHistory(1200),
        apiClient.getReports(),
      ]);

      latestConfigRef.current = boot.config;
      editRevisionRef.current = 0;
      savedRevisionRef.current = 0;
      setConfig(boot.config);
      setSnapshotsCount(boot.snapshotsCount);
      setConsoleCatalog(boot.consoleCatalog || []);
      setBuiltInArchiveTemplates(boot.archiveTemplatesBuiltIn || []);
      setSaveState('saved');
      setSaveErrors([]);

      if (boot.config.DefinedJobs.length > 0) {
        setSelectedJobId((prev) =>
          prev && boot.config.DefinedJobs.some((j) => j.JobId === prev)
            ? prev
            : boot.config.DefinedJobs[0].JobId
        );
      }

      setPipelineState(pState);
      setTerminalEntries(termHist.entries || []);
      setCliWorkingDir(termHist.workingDirectory || '');
      setCliRunning(Boolean(termHist.isCommandRunning));
      setCliHistory(termHist.commandHistory || []);
      setOpenComplianceCount(reports.kpis.openCount);

      void refreshGreenroomStatus();
      void connectRealtime(nodeId);
    },
    [connectRealtime, refreshGreenroomStatus]
  );

  const refreshAuthAndBootstrap = useCallback(async () => {
    try {
      const auth = await apiClient.getAuthStatus();
      setAuthStatus(auth);
      if (!auth.isAuthenticated) {
        setAuthGateOpen(true);
        return;
      }
      setAuthGateOpen(false);
      await loadNodeWorkspace(activeTargetNodeId);
    } catch (err) {
      if (err instanceof ApiHttpError && err.status === 401) {
        setAuthGateOpen(true);
      }
    }
  }, [activeTargetNodeId, loadNodeWorkspace]);

  useEffect(() => {
    setOnUnauthorizedHandler(() => setAuthGateOpen(true));
    void refreshAuthAndBootstrap();
    return () => {
      setOnUnauthorizedHandler(null);
      void signalrRef.current.disconnect();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const enqueueConfigSave = useCallback((forceWriteEvenIfClean = false): Promise<boolean> => {
    const runNext = async (): Promise<boolean> => {
      if (!latestConfigRef.current) return true;
      if (!forceWriteEvenIfClean && editRevisionRef.current <= savedRevisionRef.current) {
        setSaveState('saved');
        return true;
      }

      while (latestConfigRef.current) {
        const revToSave = editRevisionRef.current;
        const snapshotToSave = latestConfigRef.current;
        try {
          setSaveState('saving');
          const res = await apiClient.saveConfig(snapshotToSave);
          savedRevisionRef.current = Math.max(savedRevisionRef.current, revToSave);
          setSnapshotsCount(res.snapshotsCount);
          setSaveErrors([]);

          if (editRevisionRef.current <= savedRevisionRef.current) {
            setSaveState('saved');
            return true;
          }
        } catch (err) {
          setSaveState('error');
          if (err instanceof ApiHttpError && err.validationErrors.length > 0) {
            setSaveErrors(err.validationErrors);
          } else if (err instanceof Error) {
            setSaveErrors([err.message]);
          } else {
            setSaveErrors(['Failed to save settings.']);
          }
          return false;
        }
      }

      return true;
    };

    const nextPromise = saveChainRef.current.then(runNext, runNext);
    saveChainRef.current = nextPromise;
    return nextPromise;
  }, []);

  const flushSaveNow = useCallback(async (): Promise<boolean> => {
    if (pendingSaveTimerRef.current) {
      window.clearTimeout(pendingSaveTimerRef.current);
      pendingSaveTimerRef.current = null;
    }
    if (editRevisionRef.current <= savedRevisionRef.current || !latestConfigRef.current) {
      return await saveChainRef.current;
    }
    return await enqueueConfigSave(false);
  }, [enqueueConfigSave]);

  const forceSaveNow = useCallback(
    async (showFeedbackToast = true): Promise<boolean> => {
      if (pendingSaveTimerRef.current) {
        window.clearTimeout(pendingSaveTimerRef.current);
        pendingSaveTimerRef.current = null;
      }
      if (!latestConfigRef.current) return false;
      const ok = await enqueueConfigSave(true);
      if (showFeedbackToast) {
        showToast(ok ? '💾 All settings saved to settings.json' : '⚠ Save failed — check validation errors');
      }
      return ok;
    },
    [enqueueConfigSave, showToast]
  );

  useEffect(() => {
    const onSaveShortcut = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && !e.altKey && (e.key === 's' || e.key === 'S')) {
        e.preventDefault();
        void forceSaveNow(true);
      }
    };
    const onBeforeUnload = (e: BeforeUnloadEvent) => {
      if (editRevisionRef.current > savedRevisionRef.current && latestConfigRef.current) {
        void flushSaveNow();
        e.preventDefault();
        e.returnValue = '';
      }
    };
    window.addEventListener('keydown', onSaveShortcut);
    window.addEventListener('beforeunload', onBeforeUnload);
    return () => {
      window.removeEventListener('keydown', onSaveShortcut);
      window.removeEventListener('beforeunload', onBeforeUnload);
    };
  }, [flushSaveNow, forceSaveNow]);

  const scheduleDebouncedSave = useCallback(
    (nextCfg: AppConfig) => {
      latestConfigRef.current = nextCfg;
      editRevisionRef.current += 1;
      setSaveState('saving');
      if (pendingSaveTimerRef.current) window.clearTimeout(pendingSaveTimerRef.current);
      pendingSaveTimerRef.current = window.setTimeout(() => {
        pendingSaveTimerRef.current = null;
        void enqueueConfigSave(false);
      }, 450);
    },
    [enqueueConfigSave]
  );

  const updateConfig = useCallback(
    (mutator: (draft: AppConfig) => void) => {
      const base = latestConfigRef.current;
      if (!base) return;
      const next: AppConfig = structuredClone(base);
      mutator(next);
      const byId = new Map(next.DefinedJobs.map((j) => [j.JobId, j]));
      for (const q of next.NamedQueues) {
        q.Jobs = q.Jobs.map((qj) => byId.get(qj.JobId) ?? qj);
      }
      latestConfigRef.current = next;
      setConfig(next);
      scheduleDebouncedSave(next);
    },
    [scheduleDebouncedSave]
  );

  const {
    uiLayout,
    setTerminalHeight,
    applyTerminalPreset,
    toggleSidebarCollapsed,
    setTerminalWordWrap,
    drivesModalOpen,
    setDrivesModalOpen,
  } = useUiLayoutPersistence(config, updateConfig);

  const updateJob = useCallback(
    (jobId: string, mutator: (job: HousekeepingJob) => void) => {
      updateConfig((draft) => {
        const target = draft.DefinedJobs.find((j) => j.JobId === jobId);
        if (target) mutator(target);
      });
    },
    [updateConfig]
  );

  const replaceConfigFromServer = useCallback((cfg: AppConfig, nextSnapshotsCount?: number) => {
    if (pendingSaveTimerRef.current) {
      window.clearTimeout(pendingSaveTimerRef.current);
      pendingSaveTimerRef.current = null;
    }
    latestConfigRef.current = cfg;
    savedRevisionRef.current = editRevisionRef.current;
    setConfig(cfg);
    if (typeof nextSnapshotsCount === 'number') setSnapshotsCount(nextSnapshotsCount);
    setSaveErrors([]);
    setSaveState('saved');
  }, []);

  const setActiveTargetNodeId = useCallback(
    (nodeId: string) => {
      void (async () => {
        await flushSaveNow();
        setApiTargetNodeId(nodeId);
        setActiveTargetNodeIdState(nodeId);
        try {
          await loadNodeWorkspace(nodeId);
          showToast(nodeId === 'local' ? 'Switched to Local Machine' : `Switched Target Node to '${nodeId}'`);
        } catch (err) {
          showToast(`Could not connect to target node: ${err instanceof Error ? err.message : 'Offline'}`);
        }
      })();
    },
    [flushSaveNow, loadNodeWorkspace, showToast]
  );

  const openJobInBuilder = useCallback(
    (jobId: string) => {
      setSelectedJobId(jobId);
      setActiveTab('builder');
    },
    [setActiveTab]
  );

  const clearTerminalEntries = useCallback(async () => {
    await apiClient.clearTerminal();
    setTerminalEntries([]);
  }, []);

  const runCliCommand = useCallback(
    async (cmd: string) => {
      const trimmed = cmd.trim();
      if (!trimmed) return;
      setCliRunning(true);
      setCliHistory((prev) => [trimmed, ...prev.filter((x) => x !== trimmed)].slice(0, 50));
      try {
        const res = await apiClient.execTerminalCommand(trimmed);
        if (res.workingDirectory) setCliWorkingDir(res.workingDirectory);
        if (Array.isArray(res.entries) && res.entries.length > 0) {
          appendUniqueTerminalEntries(res.entries);
        }
      } catch (err) {
        showToast(err instanceof Error ? err.message : 'CLI command failed.');
      } finally {
        setCliRunning(false);
      }
    },
    [appendUniqueTerminalEntries, showToast]
  );

  const cancelCliCommand = useCallback(async () => {
    await apiClient.cancelTerminalCommand();
    setCliRunning(false);
  }, []);

  return (
    <AppStateContext.Provider
      value={{
        activeTab,
        setActiveTab,
        selectedJobId,
        setSelectedJobId,
        openJobInBuilder,
        authStatus,
        authGateOpen,
        setAuthGateOpen,
        refreshAuthAndBootstrap,
        config,
        consoleCatalog,
        builtInArchiveTemplates,
        updateConfig,
        updateJob,
        replaceConfigFromServer,
        flushSaveNow,
        forceSaveNow,
        saveState,
        saveErrors,
        snapshotsCount,
        setSnapshotsCount,
        pipelineState,
        setPipelineState,
        telemetry,
        openComplianceCount,
        setOpenComplianceCount,
        greenroomStatus,
        refreshGreenroomStatus,
        terminalEntries,
        clearTerminalEntries,
        cliWorkingDir,
        cliRunning,
        cliHistory,
        runCliCommand,
        cancelCliCommand,
        activeTargetNodeId,
        setActiveTargetNodeId,
        realtimeConnected,
        colorScheme,
        setColorScheme,
        toggleTwoStateColorScheme,
        density,
        setDensity,
        uiLayout,
        setTerminalHeight,
        applyTerminalPreset,
        toggleSidebarCollapsed,
        setTerminalWordWrap,
        drivesModalOpen,
        setDrivesModalOpen,
        globalDrawerOpen,
        setGlobalDrawerOpen,
        toastMessage,
        showToast,
      }}
    >
      {children}
    </AppStateContext.Provider>
  );
};

export function useAppState(): AppStateContextValue {
  const ctx = useContext(AppStateContext);
  if (!ctx) throw new Error('useAppState must be used within AppStateProvider');
  return ctx;
}
