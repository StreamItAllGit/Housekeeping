import React, { useEffect, useRef, useState } from 'react';
import { useAppState, type NavTabId, type UiDensityMode } from '../context/AppStateContext';
import { apiClient } from '../services/apiClient';
import type { TailscalePwaStatusDto } from '../types/models';
import { ModalDialog } from './ModalDialog';
import { SnapshotHistoryDialog } from './SnapshotHistoryDialog';

interface BeforeInstallPromptEvent extends Event {
  prompt: () => Promise<void>;
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed'; platform: string }>;
}

export const TopCommandBar: React.FC = () => {
  const {
    activeTab,
    setActiveTab,
    authStatus,
    config,
    replaceConfigFromServer,
    flushSaveNow,
    forceSaveNow,
    saveState,
    saveErrors,
    snapshotsCount,
    pipelineState,
    setPipelineState,
    telemetry,
    openComplianceCount,
    activeTargetNodeId,
    setActiveTargetNodeId,
    colorScheme,
    toggleTwoStateColorScheme,
    density,
    setDensity,
    showToast,
  } = useAppState();

  const [snapshotsOpen, setSnapshotsOpen] = useState(false);
  const [drivesModalOpen, setDrivesModalOpen] = useState(false);
  const [exportModalOpen, setExportModalOpen] = useState(false);
  const [mobileControlsOpen, setMobileControlsOpen] = useState(false);
  const [installPrompt, setInstallPrompt] = useState<BeforeInstallPromptEvent | null>(null);
  const [isStandalonePwa, setIsStandalonePwa] = useState<boolean>(() =>
    typeof window !== 'undefined'
      ? window.matchMedia('(display-mode: standalone)').matches
      : false
  );
  const [tailscalePwa, setTailscalePwa] = useState<TailscalePwaStatusDto | null>(null);
  const fileImportRef = useRef<HTMLInputElement | null>(null);

  const isInsecureRemoteOrigin =
    typeof window !== 'undefined' && !window.isSecureContext;

  useEffect(() => {
    if (typeof window === 'undefined') return;

    const onBeforeInstall = (e: Event) => {
      e.preventDefault();
      setInstallPrompt(e as BeforeInstallPromptEvent);
    };
    const onInstalled = () => {
      setInstallPrompt(null);
      setIsStandalonePwa(true);
    };

    window.addEventListener('beforeinstallprompt', onBeforeInstall);
    window.addEventListener('appinstalled', onInstalled);
    return () => {
      window.removeEventListener('beforeinstallprompt', onBeforeInstall);
      window.removeEventListener('appinstalled', onInstalled);
    };
  }, []);

  useEffect(() => {
    if (!authStatus?.isAuthenticated || !isInsecureRemoteOrigin) return;
    apiClient
      .getTailscalePwaStatus()
      .then((res) => setTailscalePwa(res))
      .catch(() => {});
  }, [authStatus?.isAuthenticated, isInsecureRemoteOrigin]);

  const handleInstallPwa = async () => {
    if (!installPrompt) return;
    try {
      await installPrompt.prompt();
      const choice = await installPrompt.userChoice;
      if (choice.outcome === 'accepted') {
        setInstallPrompt(null);
      }
    } catch {
      // ignore
    }
  };

  const disks = telemetry?.Disks || [];
  const remoteNodes = config?.RemoteNodes || [];
  const activeTopQueueId = config?.ActiveQueueId || config?.NamedQueues?.[0]?.Id || '';
  const pendingRuns = pipelineState.PendingRuns || [];
  const isActiveQueueRunning =
    pipelineState.IsRunning &&
    Boolean(pipelineState.ActiveQueueId) &&
    Boolean(activeTopQueueId) &&
    pipelineState.ActiveQueueId?.toLowerCase() === activeTopQueueId.toLowerCase();
  const isActiveQueueQueued =
    Boolean(activeTopQueueId) &&
    pendingRuns.some(
      (pr) =>
        pr.RunKind.toLowerCase() === 'queue' &&
        pr.TargetId.toLowerCase() === activeTopQueueId.toLowerCase()
    );
  const blockActiveQueueTrigger = isActiveQueueRunning || isActiveQueueQueued;

  const handleRunQueue = async (isDryRun: boolean) => {
    const saved = await flushSaveNow();
    if (!saved) {
      showToast('Resolve configuration validation errors before starting a run.');
      return;
    }
    try {
      const res = await apiClient.runQueue(config?.ActiveQueueId, isDryRun, false);
      setPipelineState(res.state);
      showToast(res.message);
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Unable to start queue.');
    }
  };

  const handlePause = async () => {
    try {
      const res = await apiClient.togglePause();
      setPipelineState(res.state);
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Pause failed.');
    }
  };

  const handleSuspend = async () => {
    try {
      const res = await apiClient.toggleSuspend();
      setPipelineState(res.state);
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Suspend failed.');
    }
  };

  const handleStop = async () => {
    try {
      const res = await apiClient.stopPipeline();
      setPipelineState(res.state);
      showToast('Stop signal sent to active pipeline.');
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Stop failed.');
    }
  };

  const handleImportFile = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!file) return;
    try {
      const text = await file.text();
      const res = await apiClient.importProfile(text);
      replaceConfigFromServer(res.config);
      showToast(`Imported profile '${file.name}'`);
    } catch (err) {
      showToast(`Import failed: ${err instanceof Error ? err.message : 'Invalid profile'}`);
    }
  };

  const navItems: { id: NavTabId; label: string; icon: string; badge?: number }[] = [
    { id: 'dashboard', label: 'Dashboard', icon: '📊' },
    { id: 'queues', label: 'Job Queues', icon: '🗂' },
    { id: 'builder', label: 'Job Builder', icon: '🛠' },
    { id: 'modules', label: 'Modules', icon: '🧩' },
    { id: 'reports', label: 'Reports', icon: '🛡', badge: openComplianceCount },
    { id: 'settings', label: 'Settings', icon: '⚙' },
    { id: 'history', label: 'History', icon: '📜' },
  ];

  const isDarkRendered =
    colorScheme === 'dark' ||
    (colorScheme === 'system' &&
      typeof window !== 'undefined' &&
      window.matchMedia('(prefers-color-scheme: dark)').matches);

  const compactMobilePromptDir = authStatus?.version || '1.0.0-rc.11-hf.1';

  return (
    <header className="hk-topbar">
      {/* Shared Hidden File Input for Desktop & Mobile Profile Import */}
      <input
        ref={fileImportRef}
        type="file"
        accept=".json"
        style={{ display: 'none' }}
        onChange={(e) => void handleImportFile(e)}
      />

      {/* Desktop Top Command Bar (> 768px — 100% preserved) */}
      <div className="hk-topbar-row hk-desktop-only">
        {/* Left: Brand + Version + Target Node Switcher + Auto-Save + Snapshots */}
        <div className="hk-row">
          <div className="hk-brand">
            <img src="/app_icon.png" alt="Housekeeping" />
            <div>
              <div className="hk-row" style={{ gap: 6 }}>
                <span className="hk-brand-title">Housekeeping</span>
                <span className="hk-version-pill">
                  v{compactMobilePromptDir}
                </span>
              </div>
              <div style={{ fontSize: 11, color: 'var(--text-secondary)' }}>
                {pipelineState.GlobalNextRunStatusText || 'Schedule: Disabled'}
              </div>
            </div>
          </div>

          {/* Target Node Switcher (Decision 5) */}
          <select
            className="hk-select"
            style={{ width: 'auto', fontWeight: 600 }}
            value={activeTargetNodeId}
            onChange={(e) => setActiveTargetNodeId(e.target.value)}
            aria-label="Target Machine Node Switcher"
            title="Switch active Target Node (Local Machine or Remote LAN/Tailnet Node)"
          >
            <option value="local">
              🖥 Local ({authStatus?.machineName || 'Host'})
            </option>
            {remoteNodes.map((n) => (
              <option key={n.Id} value={n.Id}>
                🌐 {n.Name} ({n.HostOrIp || n.RemoteAppPath})
              </option>
            ))}
          </select>

          {/* Auto-Save Status Pill + Force Save (Ctrl+S) + 20-Snapshot History (Decision 8) */}
          {saveState === 'saved' && (
            <span className="hk-pill ok" title="All changes saved to settings.json (Ctrl+S to force save)">
              ✓ Saved
            </span>
          )}
          {saveState === 'saving' && (
            <span className="hk-pill accent">⟳ Saving...</span>
          )}
          {saveState === 'error' && (
            <span
              className="hk-pill err"
              title={saveErrors.join('\n')}
            >
              ⚠ Validation Error ({saveErrors[0] || 'Check inputs'})
            </span>
          )}

          <button
            type="button"
            className={`hk-btn sm ${saveState === 'saving' ? 'primary' : ''}`}
            onClick={() => void forceSaveNow(true)}
            title="Force save all settings immediately (Ctrl+S)"
          >
            💾 Save
          </button>

          <button
            type="button"
            className="hk-btn sm"
            onClick={() => setSnapshotsOpen(true)}
            title="View & restore from the 20 most recent settings.json snapshots"
          >
            ↺ History ({snapshotsCount})
          </button>

          <button
            type="button"
            className="hk-btn sm"
            onClick={() => fileImportRef.current?.click()}
          >
            📥 Import
          </button>
          <button
            type="button"
            className="hk-btn sm"
            onClick={() => setExportModalOpen(true)}
          >
            📤 Export
          </button>
        </div>

        {/* Center: Live Local + Mapped Network/NAS Drive Free-Space Strip */}
        {disks.length > 0 && (
          <div className="hk-drive-strip desktop-only" aria-label="Mounted local and network drive free space">
            {disks.slice(0, 16).map((d) => {
              const isReady = d.IsReady !== false;
              const isNet = Boolean(d.IsNetwork);
              const ratio = isReady && d.TotalGb > 0 ? d.FreeGb / d.TotalGb : 1;
              const dotClass = !isReady
                ? 'err'
                : d.FreeGb < 25 || ratio < 0.05
                  ? 'err'
                  : ratio < 0.12
                    ? 'warn'
                    : '';
              const shareInfo = d.RemotePath
                ? ` (${d.VolumeLabel ? `${d.VolumeLabel} — ` : ''}${d.RemotePath})`
                : d.VolumeLabel
                  ? ` (${d.VolumeLabel})`
                  : '';
              const tooltip = isReady
                ? `${isNet ? '🌐 NAS/Network ' : '💾 Local '}${d.DriveLetter}${shareInfo} • ${d.FreeGb.toFixed(1)} GB free of ${d.TotalGb.toFixed(1)} GB — click for all drives`
                : `${isNet ? '🌐 NAS/Network ' : '💾 '}${d.DriveLetter}${shareInfo} • Offline / Unreachable — click for all drives`;

              return (
                <button
                  key={d.DriveLetter}
                  type="button"
                  className={`hk-drive-pill ${isNet ? 'network' : ''} ${!isReady ? 'offline' : ''}`}
                  title={tooltip}
                  onClick={() => setDrivesModalOpen(true)}
                >
                  <span className={`hk-drive-dot ${dotClass}`} />
                  {!isReady
                    ? `${d.DriveLetter} !`
                    : `${d.DriveLetter} ${
                        d.FreeGb >= 1000
                          ? `${(d.FreeGb / 1024).toFixed(1)} TB`
                          : `${d.FreeGb >= 100 ? d.FreeGb.toFixed(0) : d.FreeGb.toFixed(1)} GB`
                      }`}
                </button>
              );
            })}
            {disks.length > 16 && (
              <button
                type="button"
                className="hk-btn sm"
                onClick={() => setDrivesModalOpen(true)}
              >
                +{disks.length - 16} Drives
              </button>
            )}
          </div>
        )}

        {/* Right: PWA Install + Density + Theme + Post-Run Power + Run / Dry-Run / Pause / Suspend / Stop */}
        <div className="hk-row">
          {!isStandalonePwa && installPrompt && (
            <button
              type="button"
              className="hk-btn sm primary"
              onClick={() => void handleInstallPwa()}
              title="Install Housekeeping as a standalone desktop or Android PWA"
            >
              📲 Install App
            </button>
          )}
          {!isStandalonePwa && isInsecureRemoteOrigin && tailscalePwa?.ServeConfigured && tailscalePwa.HttpsPwaUrl && (
            <a
              href={tailscalePwa.HttpsPwaUrl}
              className="hk-btn sm primary"
              style={{ textDecoration: 'none' }}
              title="Android/iOS require HTTPS to install a PWA — tap to switch to your trusted Tailscale HTTPS URL"
            >
              🔒 Switch to HTTPS (PWA)
            </a>
          )}
          {!isStandalonePwa && isInsecureRemoteOrigin && (!tailscalePwa?.ServeConfigured || !tailscalePwa.HttpsPwaUrl) && (
            <button
              type="button"
              className="hk-btn sm"
              onClick={() => {
                try {
                  sessionStorage.setItem('hk-settings-subtab', 'web');
                } catch {
                  // ignore
                }
                setActiveTab('settings');
              }}
              title="Configure Tailscale Serve HTTPS so Android can install Housekeeping as a standalone PWA"
            >
              📱 Setup HTTPS PWA
            </button>
          )}

          <select
            className="hk-select"
            style={{ width: 'auto' }}
            value={density}
            onChange={(e) => setDensity(e.target.value as UiDensityMode)}
            aria-label="UI Density"
            title="Switch UI Density (Compact, Comfortable, Touch)"
          >
            <option value="compact">Compact</option>
            <option value="comfortable">Comfortable</option>
            <option value="touch">Touch</option>
          </select>

          <button
            type="button"
            className="hk-btn sm"
            onClick={toggleTwoStateColorScheme}
            aria-label={isDarkRendered ? 'Switch to light theme' : 'Switch to dark theme'}
            title={
              colorScheme === 'system'
                ? `Following system (${isDarkRendered ? 'Dark' : 'Light'}) — click to override`
                : `Pinned to ${colorScheme} — click to toggle`
            }
          >
            {isDarkRendered ? '☀ Light' : '🌙 Dark'}
          </button>


          <button
            type="button"
            className="hk-btn success"
            disabled={blockActiveQueueTrigger}
            onClick={() => void handleRunQueue(false)}
          >
            {isActiveQueueRunning
              ? '⟳ Running'
              : isActiveQueueQueued
                ? '⏳ Queued Next'
                : pipelineState.IsRunning
                  ? '⏭ Queue Next'
                  : '▶ Run Enabled'}
          </button>
          <button
            type="button"
            className="hk-btn purple"
            disabled={blockActiveQueueTrigger}
            onClick={() => void handleRunQueue(true)}
          >
            {pipelineState.IsRunning && !blockActiveQueueTrigger ? '🧪 Queue Dry' : '🧪 Dry Run'}
          </button>
          <button
            type="button"
            className={`hk-btn ${pipelineState.IsPaused ? 'warn' : ''}`}
            disabled={!pipelineState.IsRunning}
            onClick={() => void handlePause()}
          >
            {pipelineState.IsPaused ? '▶ Resume Queue' : '⏸ Pause Queue'}
          </button>
          <button
            type="button"
            className={`hk-btn ${pipelineState.IsProcessSuspended ? 'warn' : ''}`}
            disabled={!pipelineState.IsRunning}
            onClick={() => void handleSuspend()}
            title="Freeze or unfreeze the active HandBrake / 7z / rclone child process threads cleanly"
          >
            {pipelineState.IsProcessSuspended ? '🔥 Unsuspend' : '❄ Suspend Process'}
          </button>
          <button
            type="button"
            className="hk-btn danger"
            disabled={!pipelineState.IsRunning}
            onClick={() => void handleStop()}
          >
            ⏹ Stop
          </button>
        </div>
      </div>

      {/* Mobile Browser & Standalone PWA Compact Header (<= 768px) */}
      <div className="hk-mobile-only hk-mobile-topbar-main">
        <div className="hk-mobile-brand">
          <img src="/app_icon.png" alt="Housekeeping" />
          <div style={{ minInlineSize: 0, flex: '1 1 auto' }}>
            <div className="hk-row" style={{ gap: 5, flexWrap: 'nowrap' }}>
              <span className="hk-brand-title" style={{ fontSize: 14 }}>
                Housekeeping
              </span>
              <span className="hk-version-pill" style={{ fontSize: 10, padding: '1px 5px' }}>
                v{compactMobilePromptDir}
              </span>
            </div>
            <div className="hk-mobile-brand-schedule">
              {pipelineState.GlobalNextRunStatusText || 'Schedule: Disabled'}
            </div>
          </div>
        </div>

        <div className="hk-mobile-actions">
          <button
            type="button"
            className={`hk-btn sm ${
              saveState === 'error' ? 'danger' : saveState === 'saving' ? 'primary' : ''
            }`}
            style={{ padding: '0 8px' }}
            onClick={() => void forceSaveNow(true)}
            aria-label="Save configuration"
            title={
              saveState === 'error'
                ? `Validation error: ${saveErrors[0] || 'Check inputs'}`
                : 'Save settings immediately'
            }
          >
            {saveState === 'saving' ? '⟳' : saveState === 'error' ? '⚠' : '💾'}
          </button>

          {!pipelineState.IsRunning ? (
            <button
              type="button"
              className="hk-btn sm success"
              style={{ padding: '0 9px' }}
              onClick={() => void handleRunQueue(false)}
              title="Run active enabled queue"
            >
              ▶ Run
            </button>
          ) : (
            <>
              <button
                type="button"
                className={`hk-btn sm ${pipelineState.IsPaused ? 'warn' : ''}`}
                style={{ padding: '0 8px' }}
                onClick={() => void handlePause()}
                aria-label={pipelineState.IsPaused ? 'Resume Queue' : 'Pause Queue'}
              >
                {pipelineState.IsPaused ? '▶' : '⏸'}
              </button>
              <button
                type="button"
                className="hk-btn sm danger"
                style={{ padding: '0 8px' }}
                onClick={() => void handleStop()}
                aria-label="Stop active pipeline"
              >
                ⏹
              </button>
            </>
          )}

          <button
            type="button"
            className={`hk-btn sm ${mobileControlsOpen ? 'primary' : ''}`}
            style={{ padding: '0 9px' }}
            aria-expanded={mobileControlsOpen}
            aria-label="Toggle mobile controls menu"
            onClick={() => setMobileControlsOpen((prev) => !prev)}
          >
            {mobileControlsOpen ? '✕' : '☰'}
          </button>
        </div>
      </div>

      {/* Expandable Mobile Controls Drawer (<= 768px) */}
      {mobileControlsOpen && (
        <div className="hk-mobile-only-block">
          <div className="hk-mobile-controls-sheet">
            <div className="hk-row" style={{ justifyContent: 'space-between', gap: 6, flexWrap: 'nowrap' }}>
              <select
                className="hk-select"
                style={{ flex: '1 1 auto', minInlineSize: 0, fontWeight: 600 }}
                value={activeTargetNodeId}
                onChange={(e) => setActiveTargetNodeId(e.target.value)}
                aria-label="Mobile Target Machine Node Switcher"
              >
                <option value="local">
                  🖥 Local ({authStatus?.machineName || 'Host'})
                </option>
                {remoteNodes.map((n) => (
                  <option key={n.Id} value={n.Id}>
                    🌐 {n.Name} ({n.HostOrIp || n.RemoteAppPath})
                  </option>
                ))}
              </select>

              {saveState === 'saved' && <span className="hk-pill ok">✓ Saved</span>}
              {saveState === 'saving' && <span className="hk-pill accent">⟳ Saving...</span>}
              {saveState === 'error' && (
                <span className="hk-pill err" title={saveErrors.join('\n')}>
                  ⚠ Error
                </span>
              )}
            </div>

            {!isStandalonePwa && installPrompt && (
              <button
                type="button"
                className="hk-btn sm primary"
                style={{ width: '100%', justifyContent: 'center' }}
                onClick={() => void handleInstallPwa()}
              >
                📲 Install Housekeeping App
              </button>
            )}
            {!isStandalonePwa && isInsecureRemoteOrigin && tailscalePwa?.ServeConfigured && tailscalePwa.HttpsPwaUrl && (
              <a
                href={tailscalePwa.HttpsPwaUrl}
                className="hk-btn sm primary"
                style={{ textDecoration: 'none', width: '100%', justifyContent: 'center' }}
              >
                🔒 Switch to Trusted HTTPS (PWA)
              </a>
            )}
            {!isStandalonePwa && isInsecureRemoteOrigin && (!tailscalePwa?.ServeConfigured || !tailscalePwa.HttpsPwaUrl) && (
              <button
                type="button"
                className="hk-btn sm"
                style={{ width: '100%', justifyContent: 'center' }}
                onClick={() => {
                  try {
                    sessionStorage.setItem('hk-settings-subtab', 'web');
                  } catch {
                    // ignore
                  }
                  setMobileControlsOpen(false);
                  setActiveTab('settings');
                }}
              >
                📱 Setup HTTPS PWA
              </button>
            )}

            <div className="hk-mobile-sheet-grid">
              <button
                type="button"
                className="hk-btn sm success"
                disabled={blockActiveQueueTrigger}
                onClick={() => {
                  setMobileControlsOpen(false);
                  void handleRunQueue(false);
                }}
              >
                {isActiveQueueRunning
                  ? '⟳ Running'
                  : isActiveQueueQueued
                    ? '⏳ Queued Next'
                    : pipelineState.IsRunning
                      ? '⏭ Queue Next'
                      : '▶ Run Enabled'}
              </button>
              <button
                type="button"
                className="hk-btn sm purple"
                disabled={blockActiveQueueTrigger}
                onClick={() => {
                  setMobileControlsOpen(false);
                  void handleRunQueue(true);
                }}
              >
                {pipelineState.IsRunning && !blockActiveQueueTrigger ? '🧪 Queue Dry' : '🧪 Dry Run'}
              </button>

              <button
                type="button"
                className={`hk-btn sm ${pipelineState.IsPaused ? 'warn' : ''}`}
                disabled={!pipelineState.IsRunning}
                onClick={() => void handlePause()}
              >
                {pipelineState.IsPaused ? '▶ Resume Queue' : '⏸ Pause Queue'}
              </button>
              <button
                type="button"
                className={`hk-btn sm ${pipelineState.IsProcessSuspended ? 'warn' : ''}`}
                disabled={!pipelineState.IsRunning}
                onClick={() => void handleSuspend()}
              >
                {pipelineState.IsProcessSuspended ? '🔥 Unsuspend' : '❄ Suspend'}
              </button>

              <button
                type="button"
                className="hk-btn sm danger"
                disabled={!pipelineState.IsRunning}
                onClick={() => void handleStop()}
              >
                ⏹ Stop Pipeline
              </button>
              <button
                type="button"
                className="hk-btn sm"
                onClick={() => {
                  setMobileControlsOpen(false);
                  setDrivesModalOpen(true);
                }}
              >
                💾 Drives ({disks.length})
              </button>

              <button
                type="button"
                className="hk-btn sm"
                onClick={() => {
                  setMobileControlsOpen(false);
                  setSnapshotsOpen(true);
                }}
              >
                ↺ History ({snapshotsCount})
              </button>

              <button
                type="button"
                className="hk-btn sm"
                onClick={() => {
                  setMobileControlsOpen(false);
                  fileImportRef.current?.click();
                }}
              >
                📥 Import
              </button>
              <button
                type="button"
                className="hk-btn sm"
                onClick={() => {
                  setMobileControlsOpen(false);
                  setExportModalOpen(true);
                }}
              >
                📤 Export
              </button>

              <select
                className="hk-select"
                value={density}
                onChange={(e) => setDensity(e.target.value as UiDensityMode)}
                aria-label="Mobile UI Density"
              >
                <option value="compact">Density: Compact</option>
                <option value="comfortable">Density: Comfort</option>
                <option value="touch">Density: Touch</option>
              </select>
              <button
                type="button"
                className="hk-btn sm"
                onClick={toggleTwoStateColorScheme}
              >
                {isDarkRendered ? '☀ Light Theme' : '🌙 Dark Theme'}
              </button>
            </div>

          </div>
        </div>
      )}

      {/* Mobile-only Navigation Tabs (Desktop driven by SidebarNavRail) */}
      <nav className="hk-nav-tabs hk-mobile-only" aria-label="Mobile sections">
        {navItems.map((tab) => (
          <button
            key={tab.id}
            type="button"
            className={`hk-nav-tab ${activeTab === tab.id ? 'active' : ''}`}
            onClick={() => {
              setMobileControlsOpen(false);
              setActiveTab(tab.id);
            }}
          >
            <span>{tab.icon}</span>
            <span>{tab.label}</span>
            {typeof tab.badge === 'number' && tab.badge > 0 && (
              <span className="hk-badge-count">{tab.badge}</span>
            )}
          </button>
        ))}
      </nav>

      <SnapshotHistoryDialog open={snapshotsOpen} onClose={() => setSnapshotsOpen(false)} />

      {/* Export Profile Modal */}
      <ModalDialog
        open={exportModalOpen}
        onClose={() => setExportModalOpen(false)}
        title="📤 Export Housekeeping Configuration Profile"
        size="narrow"
        closedBy="any"
      >
        <p style={{ marginTop: 0, color: 'var(--text-secondary)' }}>
          Choose whether to export a shareable profile (strips Humble cookies, Discord webhooks, and
          API tokens) or a full backup containing all credentials.
        </p>
        <div className="hk-row" style={{ justifyContent: 'flex-end' }}>
          <a
            href="/api/v1/profiles/export?sanitize=true"
            className="hk-btn primary"
            style={{ textDecoration: 'none' }}
            onClick={() => setExportModalOpen(false)}
          >
            🛡 Export Shareable (Sanitized)
          </a>
          <a
            href="/api/v1/profiles/export?sanitize=false"
            className="hk-btn"
            style={{ textDecoration: 'none' }}
            onClick={() => setExportModalOpen(false)}
          >
            🔐 Export Full Backup
          </a>
        </div>
      </ModalDialog>

      {/* Full Drives Drawer Modal */}
      <ModalDialog
        open={drivesModalOpen}
        onClose={() => setDrivesModalOpen(false)}
        title="💾 Mounted Local & NAS Drives"
        size="normal"
        closedBy="any"
      >
        <div className="table-wrapper" tabIndex={0} role="region" aria-label="All mounted drives">
          <table className="hk-table stack-mobile">
            <thead>
              <tr>
                <th>Drive</th>
                <th>Type</th>
                <th>Volume / Share</th>
                <th>Status</th>
                <th>Free Space</th>
                <th>Total Capacity</th>
              </tr>
            </thead>
            <tbody>
              {disks.map((d) => {
                const isReady = d.IsReady !== false;
                return (
                  <tr key={d.DriveLetter}>
                    <td data-label="Drive"><strong>{d.DriveLetter}</strong></td>
                    <td data-label="Type">{d.IsNetwork ? '🌐 Network / NAS' : d.DriveType || 'Local'}</td>
                    <td data-label="Volume / Share" style={{ fontFamily: d.RemotePath ? 'var(--font-mono)' : undefined, fontSize: 12 }}>
                      {d.RemotePath
                        ? d.VolumeLabel
                          ? `${d.VolumeLabel} (${d.RemotePath})`
                          : d.RemotePath
                        : d.VolumeLabel || '—'}
                    </td>
                    <td data-label="Status">
                      <span className={`hk-pill ${isReady ? 'ok' : 'err'}`}>
                        {isReady ? 'Online' : 'Offline (!)'}
                      </span>
                    </td>
                    <td data-label="Free Space">{isReady ? d.FreeText : '—'}</td>
                    <td data-label="Total Capacity">{isReady ? `${d.TotalGb.toFixed(1)} GB` : '—'}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </ModalDialog>
    </header>
  );
};
