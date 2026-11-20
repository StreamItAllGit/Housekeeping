import React, { useEffect, useRef, useState } from 'react';
import { useAppState, type UiDensityMode } from '../context/AppStateContext';
import { apiClient } from '../services/apiClient';
import { resolveApiV1Url } from '../utils/subpathUrl';
import { ModalDialog } from './ModalDialog';
import { SnapshotHistoryDialog } from './SnapshotHistoryDialog';
import { TopBarPwaControls } from './TopBarPwaControls';

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
    activeTargetNodeId,
    setActiveTargetNodeId,
    colorScheme,
    toggleTwoStateColorScheme,
    density,
    setDensity,
    setDrivesModalOpen,
    showToast,
  } = useAppState();

  const [snapshotsOpen, setSnapshotsOpen] = useState(false);
  const [exportModalOpen, setExportModalOpen] = useState(false);
  const [mobileControlsOpen, setMobileControlsOpen] = useState(false);
  const fileImportRef = useRef<HTMLInputElement | null>(null);

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

  const handleStopJob = async () => {
    try {
      const res = await apiClient.stopCurrentJob();
      setPipelineState(res.state);
      showToast('Cancelled current job — advancing queue.');
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Stop Job failed.');
    }
  };

  const handleStopQueue = async () => {
    try {
      const res = await apiClient.stopEntireQueue();
      setPipelineState(res.state);
      showToast('Stopped entire active queue and cleared Up Next.');
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Stop Queue failed.');
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

  useEffect(() => {
    setMobileControlsOpen(false);
  }, [activeTab]);

  const isDarkRendered =
    colorScheme === 'dark' ||
    (colorScheme === 'system' &&
      typeof window !== 'undefined' &&
      window.matchMedia('(prefers-color-scheme: dark)').matches);

  const versionNumber = (authStatus?.version || '0.2.0').replace(/^v/i, '');

  const openWebSettingsSubtab = () => {
    try {
      sessionStorage.setItem('hk-settings-subtab', 'web');
    } catch (err) {
      console.debug('[TopCommandBar] sessionStorage write skipped:', err);
    }
    setActiveTab('settings');
  };

  return (
    <header className="hk-topbar">
      <input
        ref={fileImportRef}
        type="file"
        accept=".json"
        style={{ display: 'none' }}
        onChange={(e) => void handleImportFile(e)}
      />

      {/* Desktop Top Command Bar (> 768px — consolidated without duplicate drive pills) */}
      <div className="hk-topbar-row hk-desktop-only">
        <div className="hk-row">
          <div className="hk-brand">
            <img src="./app_icon.png" alt="Housekeeping" />
            <div>
              <div className="hk-row" style={{ gap: 6 }}>
                <span className="hk-brand-title">Housekeeping</span>
                <span className="hk-version-pill">v{versionNumber}</span>
              </div>
              <div style={{ fontSize: 11, color: 'var(--text-secondary)' }}>
                {pipelineState.GlobalNextRunStatusText || 'Schedule: Disabled'}
              </div>
            </div>
          </div>

          <select
            className="hk-select"
            style={{ width: 'auto', fontWeight: 600 }}
            value={activeTargetNodeId}
            onChange={(e) => setActiveTargetNodeId(e.target.value)}
            aria-label="Target Machine Node Switcher"
            title="Switch active Target Node (Local Machine or Remote LAN/Tailnet Node)"
          >
            <option value="local">🖥 Local ({authStatus?.machineName || 'Host'})</option>
            {remoteNodes.map((n) => (
              <option key={n.Id} value={n.Id}>
                🌐 {n.Name} ({n.HostOrIp || n.RemoteAppPath})
              </option>
            ))}
          </select>

          {saveState === 'saved' && (
            <span className="hk-pill ok nowrap" title="All changes saved to settings.json (Ctrl+S to force save)">
              ✓ Saved
            </span>
          )}
          {saveState === 'saving' && <span className="hk-pill accent nowrap">⟳ Saving...</span>}
          {saveState === 'error' && (
            <span className="hk-pill err nowrap" title={saveErrors.join('\n')}>
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

          <button type="button" className="hk-btn sm" onClick={() => fileImportRef.current?.click()}>
            📥 Import
          </button>
          <button type="button" className="hk-btn sm" onClick={() => setExportModalOpen(true)}>
            📤 Export
          </button>
        </div>

        <div className="hk-row">
          <TopBarPwaControls
            isAuthenticated={Boolean(authStatus?.isAuthenticated)}
            variant="desktop"
            onOpenWebSettings={openWebSettingsSubtab}
          />

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
            className="hk-btn warn"
            disabled={!pipelineState.IsRunning}
            onClick={() => void handleStopJob()}
            title="Cancel only the current job and advance to the next job in the queue"
          >
            ⏹ Stop Job
          </button>
          <button
            type="button"
            className="hk-btn danger"
            disabled={!pipelineState.IsRunning}
            onClick={() => void handleStopQueue()}
            title="Stop the entire active queue and clear pending Up Next runs"
          >
            ⏹ Stop Queue
          </button>
        </div>
      </div>

      {/* Mobile Browser & Standalone PWA Compact Header (<= 768px) */}
      <div className="hk-mobile-only hk-mobile-topbar-main">
        <div className="hk-mobile-brand">
          <img src="./app_icon.png" alt="Housekeeping" />
          <div style={{ minInlineSize: 0, flex: '1 1 auto' }}>
            <div className="hk-row" style={{ gap: 5, flexWrap: 'nowrap' }}>
              <span className="hk-brand-title" style={{ fontSize: 14 }}>
                Housekeeping
              </span>
              <span className="hk-version-pill" style={{ fontSize: 10, padding: '1px 5px' }}>
                v{versionNumber}
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
                className="hk-btn sm warn"
                style={{ padding: '0 7px' }}
                onClick={() => void handleStopJob()}
                aria-label="Stop current job"
                title="Stop current job"
              >
                ⏹J
              </button>
              <button
                type="button"
                className="hk-btn sm danger"
                style={{ padding: '0 7px' }}
                onClick={() => void handleStopQueue()}
                aria-label="Stop entire queue"
                title="Stop entire queue"
              >
                ⏹Q
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
                <option value="local">🖥 Local ({authStatus?.machineName || 'Host'})</option>
                {remoteNodes.map((n) => (
                  <option key={n.Id} value={n.Id}>
                    🌐 {n.Name} ({n.HostOrIp || n.RemoteAppPath})
                  </option>
                ))}
              </select>

              {saveState === 'saved' && <span className="hk-pill ok nowrap">✓ Saved</span>}
              {saveState === 'saving' && <span className="hk-pill accent nowrap">⟳ Saving...</span>}
              {saveState === 'error' && (
                <span className="hk-pill err nowrap" title={saveErrors.join('\n')}>
                  ⚠ Error
                </span>
              )}
            </div>

            <TopBarPwaControls
              isAuthenticated={Boolean(authStatus?.isAuthenticated)}
              variant="mobile"
              onOpenWebSettings={() => {
                setMobileControlsOpen(false);
                openWebSettingsSubtab();
              }}
            />

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
                className="hk-btn sm warn"
                disabled={!pipelineState.IsRunning}
                onClick={() => void handleStopJob()}
              >
                ⏹ Stop Job
              </button>
              <button
                type="button"
                className="hk-btn sm danger"
                disabled={!pipelineState.IsRunning}
                onClick={() => void handleStopQueue()}
              >
                ⏹ Stop Queue
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
              <button type="button" className="hk-btn sm" onClick={toggleTwoStateColorScheme}>
                {isDarkRendered ? '☀ Light Theme' : '🌙 Dark Theme'}
              </button>
            </div>
          </div>
        </div>
      )}

      <SnapshotHistoryDialog open={snapshotsOpen} onClose={() => setSnapshotsOpen(false)} />

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
            href={resolveApiV1Url('/profiles/export?sanitize=true')}
            className="hk-btn primary"
            style={{ textDecoration: 'none' }}
            onClick={() => setExportModalOpen(false)}
          >
            🛡 Export Shareable (Sanitized)
          </a>
          <a
            href={resolveApiV1Url('/profiles/export?sanitize=false')}
            className="hk-btn"
            style={{ textDecoration: 'none' }}
            onClick={() => setExportModalOpen(false)}
          >
            🔐 Export Full Backup
          </a>
        </div>
      </ModalDialog>
    </header>
  );
};
