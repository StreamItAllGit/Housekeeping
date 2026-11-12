import React, { useCallback, useEffect, useState } from 'react';
import { useAppState } from '../context/AppStateContext';
import { COMMON_UI_HINTS } from '../hints/commonDialogs';
import { apiClient } from '../services/apiClient';
import type { DependencyStatusItem, ModuleCatalogItemDto, ModuleId } from '../types/models';

export const ModulesCatalogPage: React.FC = () => {
  const {
    config,
    replaceConfigFromServer,
    flushSaveNow,
    openJobInBuilder,
    showToast,
  } = useAppState();

  const [modules, setModules] = useState<ModuleCatalogItemDto[]>([]);
  const [deps, setDeps] = useState<DependencyStatusItem[]>([]);
  const [selectedKey, setSelectedKey] = useState<ModuleId>('CollectionManifest');

  const loadCatalog = useCallback(async () => {
    try {
      const res = await apiClient.getModulesCatalog();
      setModules(res.modules || []);
      setDeps(res.dependencies || []);
    } catch (err) {
      console.debug('[ModulesCatalogPage] loadCatalog failed:', err);
    }
  }, []);

  useEffect(() => {
    void loadCatalog();
  }, [loadCatalog]);

  const activeMod = modules.find((m) => m.Id === selectedKey) || modules[0] || null;

  const handleToggleModule = async (mod: ModuleCatalogItemDto, enabled: boolean) => {
    await flushSaveNow();
    try {
      await apiClient.toggleModuleAppWide(mod.Id, enabled);
      const boot = await apiClient.getConfigBootstrap();
      replaceConfigFromServer(boot.config, boot.snapshotsCount);
      await loadCatalog();
      showToast(`${mod.Name} is now ${enabled ? 'Enabled' : 'Disabled'} app-wide.`);
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Toggle failed.');
    }
  };

  const handleCreateJobForModule = async (modId: ModuleId) => {
    await flushSaveNow();
    try {
      const res = await apiClient.createJob(modId, undefined, config?.ActiveQueueId);
      const boot = await apiClient.getConfigBootstrap();
      replaceConfigFromServer(boot.config, boot.snapshotsCount);
      openJobInBuilder(res.job.JobId);
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Create job failed.');
    }
  };

  return (
    <div>
      {/* CLI Dependency Readiness Strip */}
      <div className="hk-card" style={{ marginBottom: 'var(--gap-grid)' }}>
        <div className="hk-row" style={{ justifyContent: 'space-between' }}>
          <strong>🔧 External Tool &amp; Engine Readiness</strong>
          <div className="hk-row">
            {deps.map((d) => (
              <span
                key={d.ToolName}
                className={`hk-pill ${d.IsAvailable ? 'ok' : 'warn'}`}
                title={d.ResolvedPath || d.Description}
              >
                {d.IsAvailable ? '✓' : '⚠'} {d.ToolName} ({d.SourceLabel})
              </span>
            ))}
          </div>
        </div>
      </div>

      {/* 8 Module Cards + Selected Module Deep Documentation */}
      <div className="hk-builder-layout modules-split">
        <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
          {modules.map((m) => (
            <div
              key={m.Id}
              className="hk-card"
              style={{
                marginTop: 0,
                cursor: 'pointer',
                background: activeMod?.Id === m.Id ? 'var(--bg-selected)' : 'var(--bg-surface)',
                borderColor: activeMod?.Id === m.Id ? 'var(--accent)' : 'var(--border-subtle)',
              }}
              onClick={() => setSelectedKey(m.Id)}
            >
              <div className="hk-row" style={{ justifyContent: 'space-between', marginBottom: 4 }}>
                <div className="hk-row" style={{ gap: 7 }}>
                  <span style={{ fontSize: 18 }}>{m.Icon}</span>
                  <strong>{m.Name}</strong>
                  <span className="hk-pill">{m.DefinedJobCount} job(s)</span>
                </div>

                <label
                  className="hk-checkbox-label hk-has-hint"
                  title={COMMON_UI_HINTS.moduleAppWideActive(m.Name)}
                  onClick={(e) => e.stopPropagation()}
                  style={{ fontSize: 11.5 }}
                >
                  <input
                    type="checkbox"
                    className="hk-has-hint"
                    title={COMMON_UI_HINTS.moduleAppWideActive(m.Name)}
                    checked={m.IsAppWideEnabled}
                    onChange={(e) => void handleToggleModule(m, e.target.checked)}
                  />
                  Active
                </label>
              </div>
              <div style={{ fontSize: 12, color: 'var(--text-secondary)' }}>{m.ShortSummary}</div>
              <div className="hk-row" style={{ marginTop: 6, justifyContent: 'space-between' }}>
                <span style={{ fontSize: 10.5, fontWeight: 700, color: 'var(--accent)' }}>
                  {m.Category}
                </span>
                <span className={`hk-pill ${m.IsToolReady ? 'ok' : 'warn'}`} title={m.ToolStatusText}>
                  {m.ToolStatusText}
                </span>
              </div>
            </div>
          ))}
        </div>

        {/* Right: Detailed Capabilities, Setup Guide & Best Practices */}
        {activeMod && (
          <div className="hk-card">
            <div className="hk-card-header">
              <div>
                <div style={{ fontSize: 11, fontWeight: 700, color: 'var(--accent)' }}>
                  {activeMod.Category}
                </div>
                <h2 style={{ margin: '2px 0 4px', fontSize: 20 }}>
                  {activeMod.Icon} {activeMod.Name}
                </h2>
                <div style={{ color: 'var(--text-secondary)' }}>{activeMod.LongDescription}</div>
              </div>

              <div className="hk-row">
                <button
                  type="button"
                  className="hk-btn primary"
                  onClick={() => void handleCreateJobForModule(activeMod.Id)}
                >
                  + Add Job for {activeMod.Name}
                </button>
                {config?.DefinedJobs.some((j) => j.ModuleType === activeMod.Id) && (
                  <button
                    type="button"
                    className="hk-btn"
                    onClick={() => {
                      const first = config.DefinedJobs.find((j) => j.ModuleType === activeMod.Id);
                      if (first) openJobInBuilder(first.JobId);
                    }}
                  >
                    Open Existing Job →
                  </button>
                )}
              </div>
            </div>

            <div className="hk-grid-2" style={{ marginTop: 14 }}>
              <div className="hk-card" style={{ background: 'var(--bg-elevated)' }}>
                <h3 className="hk-card-title">✨ Core Capabilities</h3>
                <pre
                  style={{
                    whiteSpace: 'pre-wrap',
                    overflowWrap: 'break-word',
                    fontFamily: 'inherit',
                    margin: '8px 0 0',
                    lineHeight: 1.55,
                  }}
                >
                  {activeMod.Capabilities}
                </pre>
              </div>

              <div className="hk-card" style={{ background: 'var(--bg-elevated)' }}>
                <h3 className="hk-card-title">🧭 Quick Setup Guide</h3>
                <pre
                  style={{
                    whiteSpace: 'pre-wrap',
                    overflowWrap: 'break-word',
                    fontFamily: 'inherit',
                    margin: '8px 0 0',
                    lineHeight: 1.55,
                  }}
                >
                  {activeMod.SetupGuide}
                </pre>
              </div>
            </div>

            <div className="hk-card" style={{ marginTop: 12, background: 'var(--bg-elevated)' }}>
              <h3 className="hk-card-title">💡 Best Practices &amp; Required Tools</h3>
              <p style={{ margin: '6px 0', fontSize: 12.5 }}>
                <strong>Required CLI / Native Dependencies:</strong>{' '}
                <code>{activeMod.RequiredTools}</code>
              </p>
              <pre
                style={{
                  whiteSpace: 'pre-wrap',
                  overflowWrap: 'break-word',
                  fontFamily: 'inherit',
                  margin: '6px 0 0',
                  lineHeight: 1.55,
                }}
              >
                {activeMod.BestPractices}
              </pre>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};
