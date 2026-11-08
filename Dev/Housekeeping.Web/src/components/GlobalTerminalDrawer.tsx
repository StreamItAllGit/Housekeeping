import React from 'react';
import { useAppState } from '../context/AppStateContext';
import type { FooterVerbosityLevel } from '../types/models';
import { DualPaneTerminal } from './DualPaneTerminal';

export const GlobalTerminalDrawer: React.FC = () => {
  const {
    activeTab,
    config,
    updateConfig,
    pipelineState,
    realtimeConnected,
    globalDrawerOpen,
    setGlobalDrawerOpen,
  } = useAppState();

  const showDrawer = activeTab !== 'dashboard' && globalDrawerOpen;

  return (
    <>
      {showDrawer && (
        <section className="hk-global-drawer" aria-label="Global Live Terminal Drawer">
          <div
            className="hk-row"
            style={{ justifyContent: 'space-between', marginBottom: 6, flex: '0 0 auto' }}
          >
            <strong style={{ fontSize: 12.5, color: 'var(--accent)' }}>
              ⌨ Live Terminal Drawer
              <span className="hk-desktop-only"> (Press Ctrl+` to toggle)</span>
            </strong>
            <button
              type="button"
              className="hk-btn sm"
              onClick={() => setGlobalDrawerOpen(false)}
            >
              ▼ Collapse
            </button>
          </div>
          <div
            style={{
              display: 'flex',
              flexDirection: 'column',
              flex: '1 1 0',
              minBlockSize: 0,
              overflow: 'hidden',
            }}
          >
            <DualPaneTerminal compactDrawerMode />
          </div>
        </section>
      )}

      <footer className="hk-footer-bar">
        <div
          className="hk-row"
          style={{
            flex: '1 1 auto',
            minWidth: 0,
            flexWrap: 'nowrap',
            cursor: activeTab !== 'dashboard' ? 'pointer' : 'default',
          }}
          onClick={() => {
            if (activeTab !== 'dashboard') {
              setGlobalDrawerOpen((prev) => !prev);
            }
          }}
          title={
            activeTab !== 'dashboard'
              ? 'Click or press Ctrl+` to expand live Terminal Drawer'
              : undefined
          }
        >
          <span
            className={`hk-drive-dot ${realtimeConnected ? '' : 'warn'}`}
            style={{ flexShrink: 0 }}
            title={realtimeConnected ? 'SignalR Real-Time Connected' : 'Reconnecting...'}
          />
          <span className="hk-footer-status-text">
            {pipelineState.IsRunning
              ? `${pipelineState.ActiveJobName || 'Running'} — ${pipelineState.SubprocessStatusText}`
              : pipelineState.FooterStatusText}
          </span>
          {pipelineState.IsRunning && (
            <>
              <div className="hk-desktop-only" style={{ width: 110, flexShrink: 0 }}>
                <div className="hk-progress-track">
                  <div
                    className="hk-progress-fill"
                    style={{ width: `${Math.min(100, Math.max(0, pipelineState.ProgressPercent))}%` }}
                  />
                </div>
              </div>
              <span
                className="hk-mobile-only hk-pill accent"
                style={{ fontSize: 10.5, padding: '1px 6px', flexShrink: 0 }}
              >
                {Math.min(100, Math.max(0, pipelineState.ProgressPercent)).toFixed(0)}%
              </span>
            </>
          )}
        </div>

        {pipelineState.IsRunning && (
          <div className="hk-mobile-only-block" style={{ width: '100%' }}>
            <div className="hk-progress-track" style={{ height: 5 }}>
              <div
                className="hk-progress-fill"
                style={{ width: `${Math.min(100, Math.max(2, pipelineState.ProgressPercent))}%` }}
              />
            </div>
          </div>
        )}

        <div className="hk-row hk-footer-controls-row">
          {config && (
            <label className="hk-row" style={{ gap: 5, fontSize: 11.5, flexWrap: 'nowrap' }}>
              <span className="hk-desktop-only" style={{ color: 'var(--text-secondary)' }}>
                Verbosity:
              </span>
              <select
                className="hk-select"
                style={{ width: 'auto', minHeight: 24, padding: '1px 7px', fontSize: 11.5 }}
                value={config.FooterVerbosity || 'ProgressAndStatus'}
                aria-label="Footer verbosity level"
                onChange={(e) =>
                  updateConfig((d) => {
                    d.FooterVerbosity = e.target.value as FooterVerbosityLevel;
                  })
                }
              >
                <option value="ProgressAndStatus">Progress &amp; Status</option>
                <option value="Everything">Everything</option>
                <option value="Quiet">Quiet</option>
                <option value="AlertsOnly">Alerts Only</option>
                <option value="Custom">Custom</option>
              </select>
            </label>
          )}

          <span className="hk-desktop-only" style={{ color: 'var(--text-secondary)', whiteSpace: 'nowrap' }}>
            Session Totals — Processed:{' '}
            <strong style={{ color: 'var(--ok)' }}>{pipelineState.SessionProcessed}</strong> | Skipped:{' '}
            <strong>{pipelineState.SessionSkipped}</strong> | Errors:{' '}
            <strong style={{ color: pipelineState.SessionErrors > 0 ? 'var(--err)' : 'inherit' }}>
              {pipelineState.SessionErrors}
            </strong>
          </span>

          <span
            className="hk-mobile-only"
            style={{
              gap: 6,
              alignItems: 'center',
              color: 'var(--text-secondary)',
              whiteSpace: 'nowrap',
              fontSize: 11.5,
            }}
          >
            <span>
              Done: <strong style={{ color: 'var(--ok)' }}>{pipelineState.SessionProcessed}</strong>
            </span>
            <span>•</span>
            <span>
              Skip: <strong>{pipelineState.SessionSkipped}</strong>
            </span>
            <span>•</span>
            <span>
              Err:{' '}
              <strong style={{ color: pipelineState.SessionErrors > 0 ? 'var(--err)' : 'inherit' }}>
                {pipelineState.SessionErrors}
              </strong>
            </span>
          </span>

          {activeTab !== 'dashboard' && (
            <button
              type="button"
              className={`hk-btn sm ${globalDrawerOpen ? 'primary' : ''}`}
              style={{ flexShrink: 0 }}
              onClick={() => setGlobalDrawerOpen((prev) => !prev)}
            >
              {globalDrawerOpen ? (
                <>
                  ▼ Hide<span className="hk-desktop-only"> Terminal</span>
                </>
              ) : (
                <>
                  ▲ Terminal<span className="hk-desktop-only"> (Ctrl+`)</span>
                </>
              )}
            </button>
          )}
        </div>
      </footer>
    </>
  );
};
