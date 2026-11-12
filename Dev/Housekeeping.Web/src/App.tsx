import React, { useEffect, useState } from 'react';
import { DrivesTableModal } from './components/DrivesTableModal';
import { GlobalTerminalDrawer } from './components/GlobalTerminalDrawer';
import { KeyboardShortcutsModal } from './components/KeyboardShortcutsModal';
import { LoginGateDialog } from './components/LoginGateDialog';
import { MobileBottomNavBar } from './components/MobileBottomNavBar';
import { OmniSearchModal } from './components/OmniSearchModal';
import { SidebarNavRail } from './components/SidebarNavRail';
import { TopCommandBar } from './components/TopCommandBar';
import { AppStateProvider, useAppState } from './context/AppStateContext';
import { DashboardPage } from './pages/DashboardPage';
import { HistoryPage } from './pages/HistoryPage';
import { JobBuilderPage } from './pages/JobBuilderPage';
import { JobQueuesPage } from './pages/JobQueuesPage';
import { ModulesCatalogPage } from './pages/ModulesCatalogPage';
import { ReportsCenterPage } from './pages/ReportsCenterPage';
import { SettingsPage } from './pages/SettingsPage';
import { apiClient } from './services/apiClient';

const InterruptedRunRecoveryBanner: React.FC = () => {
  const {
    activeTab,
    config,
    updateConfig,
    pipelineState,
    setPipelineState,
    setGlobalDrawerOpen,
    showToast,
  } = useAppState();
  const [busy, setBusy] = useState(false);

  const cp = pipelineState.InterruptedRun;
  if (!cp || pipelineState.IsRunning) return null;

  const isMultiStepPartialQueue =
    cp.RunKind === 'Queue' &&
    Array.isArray(cp.CompletedJobIds) &&
    cp.CompletedJobIds.length > 0 &&
    Array.isArray(cp.InitialJobIds) &&
    cp.InitialJobIds.length > cp.CompletedJobIds.length;

  const titleLabel =
    cp.QueueName || cp.ActiveJobName || cp.RunKind || 'Pipeline Run';

  const handleResume = async (restartFullQueue: boolean) => {
    setBusy(true);
    try {
      const res = await apiClient.resumeInterruptedRun(restartFullQueue);
      setPipelineState(res.state);
      if (activeTab !== 'dashboard') {
        setGlobalDrawerOpen(true);
      }
      showToast(`▶ ${res.message}`);
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Could not resume interrupted run.');
    } finally {
      setBusy(false);
    }
  };

  const handleDismiss = async () => {
    setBusy(true);
    try {
      const res = await apiClient.dismissInterruptedRun();
      setPipelineState(res.state);
      showToast('Cleared interrupted run recovery checkpoint.');
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Failed to dismiss checkpoint.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <section
      className="hk-alert-banner"
      role="region"
      aria-label="Interrupted mid-shutdown run recovery"
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: 3, minWidth: 240, flex: '1 1 320px' }}>
        <div className="hk-row" style={{ gap: 8 }}>
          <strong>⚡ Interrupted Mid-Shutdown Run Detected:</strong>
          <span className="hk-pill warn">{titleLabel}</span>
          {cp.IsDryRun && <span className="hk-pill">Dry Run</span>}
          {cp.CompletedJobIds?.length > 0 && cp.InitialJobIds?.length > 0 && (
            <span className="hk-pill ok">
              {cp.CompletedJobIds.length} / {cp.InitialJobIds.length} job(s) completed prior to shutdown
            </span>
          )}
          {cp.PendingChainedQueueIds?.length > 0 && (
            <span className="hk-pill accent">
              +{cp.PendingChainedQueueIds.length} queued schedule(s) waiting
            </span>
          )}
        </div>
        <span style={{ fontSize: 'var(--font-small)', color: 'var(--text-secondary)' }}>
          {cp.ActiveJobName ? `Active step: ${cp.ActiveJobName} • ` : ''}
          {typeof cp.LastProgressPercent === 'number' && cp.LastProgressPercent > 0
            ? `${cp.LastProgressPercent.toFixed(0)}% • `
            : ''}
          {cp.LastStatusText || 'Interrupted while Housekeeping was shutting down.'}
        </span>
      </div>

      <div className="hk-row">
        {config && (
          <label
            className="hk-checkbox-label hk-has-hint"
            style={{ fontSize: 'var(--font-small)', marginRight: 6 }}
            title={'Automatically resume interrupted jobs or queues whenever Housekeeping starts up.\nSkips jobs already completed prior to the graceful shutdown checkpoint.'}
          >
            <input
              type="checkbox"
              className="hk-has-hint"
              title={'Automatically resume interrupted jobs or queues whenever Housekeeping starts up.\nSkips jobs already completed prior to the graceful shutdown checkpoint.'}
              checked={Boolean(config.AutoRestartInterruptedRunsOnStartup)}
              onChange={(e) =>
                updateConfig((d) => {
                  d.AutoRestartInterruptedRunsOnStartup = e.target.checked;
                })
              }
            />
            Auto-restart on startup
          </label>
        )}

        <button
          type="button"
          className="hk-btn sm success"
          disabled={busy}
          onClick={() => void handleResume(false)}
        >
          {isMultiStepPartialQueue ? '▶ Resume Remaining Jobs' : '▶ Restart Interrupted Run'}
        </button>

        {isMultiStepPartialQueue && (
          <button
            type="button"
            className="hk-btn sm primary"
            disabled={busy}
            onClick={() => void handleResume(true)}
          >
            ↺ Restart Entire Queue
          </button>
        )}

        <button
          type="button"
          className="hk-btn sm"
          disabled={busy}
          onClick={() => void handleDismiss()}
        >
          ✕ Dismiss
        </button>
      </div>
    </section>
  );
};

const AppWorkspace: React.FC = () => {
  const { activeTab, toastMessage, telemetry, drivesModalOpen, setDrivesModalOpen } = useAppState();
  const [searchOpen, setSearchOpen] = useState(false);
  const [shortcutsOpen, setShortcutsOpen] = useState(false);

  // Global hotkeys: Ctrl+K for search, ? for shortcuts
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null;
      const tag = target?.tagName?.toLowerCase();
      const inInput = tag === 'input' || tag === 'textarea' || tag === 'select' || target?.isContentEditable;

      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        setSearchOpen((prev) => !prev);
        return;
      }

      if (e.key === '?' && !inInput && !e.ctrlKey && !e.altKey && !e.metaKey) {
        e.preventDefault();
        setShortcutsOpen((prev) => !prev);
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, []);

  return (
    <div className="hk-shell">
      <SidebarNavRail
        onOpenSearch={() => setSearchOpen(true)}
        onOpenShortcuts={() => setShortcutsOpen(true)}
      />
      <div className="hk-body-area">
        <TopCommandBar />
        <main className="hk-main">
          <InterruptedRunRecoveryBanner />
          {activeTab === 'dashboard' && <DashboardPage />}
          {activeTab === 'queues' && <JobQueuesPage />}
          {activeTab === 'builder' && <JobBuilderPage />}
          {activeTab === 'modules' && <ModulesCatalogPage />}
          {activeTab === 'reports' && <ReportsCenterPage />}
          {activeTab === 'settings' && <SettingsPage />}
          {activeTab === 'history' && <HistoryPage />}
        </main>
        <GlobalTerminalDrawer />
      </div>
      <MobileBottomNavBar />
      <LoginGateDialog />
      <OmniSearchModal open={searchOpen} onClose={() => setSearchOpen(false)} />
      <KeyboardShortcutsModal open={shortcutsOpen} onClose={() => setShortcutsOpen(false)} />
      <DrivesTableModal
        open={drivesModalOpen}
        onClose={() => setDrivesModalOpen(false)}
        disks={telemetry?.Disks || []}
      />

      {toastMessage && (
        <div role="status" aria-live="polite" className="hk-toast">
          {toastMessage}
        </div>
      )}
    </div>
  );
};

export const App: React.FC = () => (
  <AppStateProvider>
    <AppWorkspace />
  </AppStateProvider>
);
