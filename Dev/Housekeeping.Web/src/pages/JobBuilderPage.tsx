// @module: Housekeeping.Web/pages/JobBuilderPage
// @purpose: Master-detail job definition workspace with single-line compact module icon badges and module editors.
// @depends-on: react, ../context/AppStateContext, ../hints/jobBuilder, ../services/apiClient, ../types/models, ../utils/jobSummary, ./editors/*
// @exports: JobBuilderPage
import React, { useMemo, useState } from 'react';
import { useAppState } from '../context/AppStateContext';
import { JOB_BUILDER_HINTS } from '../hints/jobBuilder';
import { apiClient } from '../services/apiClient';
import { MODULE_METADATA, type ModuleId } from '../types/models';
import { summarizeJob } from '../utils/jobSummary';
import { ArchiveCompressorEditor } from './editors/ArchiveCompressorEditor';
import { CloudSyncEditor } from './editors/CloudSyncEditor';
import { CollectionManifestEditor } from './editors/CollectionManifestEditor';
import { CustomScriptsEditor } from './editors/CustomScriptsEditor';
import { HumbleSyncEditor } from './editors/HumbleSyncEditor';
import { MediaTranscoderEditor } from './editors/MediaTranscoderEditor';
import { ProxyGeneratorEditor } from './editors/ProxyGeneratorEditor';
import { RomLauncherGeneratorEditor } from './editors/RomLauncherGeneratorEditor';

export const JobBuilderPage: React.FC = () => {
  const {
    config,
    selectedJobId,
    setSelectedJobId,
    updateConfig,
    updateJob,
    replaceConfigFromServer,
    flushSaveNow,
    forceSaveNow,
    saveState,
    pipelineState,
    setPipelineState,
    showToast,
  } = useAppState();

  const [newModuleChoice, setNewModuleChoice] = useState<string>('');
  const [mobileListOpen, setMobileListOpen] = useState(false);

  const filterModule = config?.JobBuilderFilterModule || 'All';
  const sortMode = config?.JobBuilderSortMode || 'NameAsc';
  const viewMode = config?.JobBuilderViewMode || 'Compact';

  const visibleJobs = useMemo(() => {
    const list = [...(config?.DefinedJobs || [])].filter(
      (j) => filterModule === 'All' || j.ModuleType === filterModule
    );
    if (sortMode === 'NameAsc') {
      list.sort((a, b) => a.Name.localeCompare(b.Name));
    } else if (sortMode === 'Module') {
      list.sort(
        (a, b) => a.ModuleType.localeCompare(b.ModuleType) || a.Name.localeCompare(b.Name)
      );
    }
    return list;
  }, [config?.DefinedJobs, filterModule, sortMode]);

  const activeJob =
    config?.DefinedJobs.find((j) => j.JobId === selectedJobId) ||
    visibleJobs[0] ||
    config?.DefinedJobs[0] ||
    null;

  if (!config) {
    return <div className="hk-card">Loading Job Builder...</div>;
  }

  const pendingRuns = pipelineState.PendingRuns || [];
  const isActiveJobRunningSingle =
    Boolean(activeJob) &&
    pipelineState.IsRunning &&
    !pipelineState.ActiveQueueId &&
    pipelineState.ActiveJobId?.toLowerCase() === activeJob?.JobId.toLowerCase();
  const isActiveJobQueued =
    Boolean(activeJob) &&
    pendingRuns.some(
      (pr) =>
        pr.RunKind.toLowerCase() === 'singlejob' &&
        pr.TargetId.toLowerCase() === activeJob?.JobId.toLowerCase()
    );
  const blockActiveJobTrigger = isActiveJobRunningSingle || isActiveJobQueued;

  const refreshBootstrap = async (selectId?: string) => {
    const boot = await apiClient.getConfigBootstrap();
    replaceConfigFromServer(boot.config, boot.snapshotsCount);
    if (selectId !== undefined) setSelectedJobId(selectId);
    return boot;
  };

  const handleCreateJob = async (mod: ModuleId) => {
    setNewModuleChoice('');
    await flushSaveNow();
    try {
      const res = await apiClient.createJob(mod, undefined, config.ActiveQueueId);
      await refreshBootstrap(res.job.JobId);
      showToast(`Created job '${res.job.Name}'.`);
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Failed creating job.');
    }
  };

  const handleCloneActive = async () => {
    if (!activeJob) return;
    await flushSaveNow();
    try {
      const res = await apiClient.cloneJob(activeJob.JobId);
      await refreshBootstrap(res.job.JobId);
      showToast(`Cloned '${activeJob.Name}' -> '${res.job.Name}'.`);
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Clone failed.');
    }
  };

  const handleDeleteActive = async () => {
    if (!activeJob) return;
    await flushSaveNow();
    try {
      await apiClient.deleteJob(activeJob.JobId);
      const boot = await refreshBootstrap();
      setSelectedJobId(boot.config.DefinedJobs[0]?.JobId || '');
      showToast(`Deleted job '${activeJob.Name}'.`);
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Delete failed.');
    }
  };

  const handleAddStarterJobs = async () => {
    await flushSaveNow();
    try {
      const res = await apiClient.populateStarterJobs();
      await refreshBootstrap();
      showToast(
        res.addedCount > 0
          ? `Added ${res.addedCount} starter module job(s).`
          : 'All 8 modules already have at least one job.'
      );
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Could not add starter jobs.');
    }
  };

  const handleMoveDefinedJob = (dir: -1 | 1) => {
    if (!activeJob) return;
    updateConfig((d) => {
      if (d.JobBuilderSortMode !== 'Custom') d.JobBuilderSortMode = 'Custom';
      const idx = d.DefinedJobs.findIndex((j) => j.JobId === activeJob.JobId);
      const targetIdx = idx + dir;
      if (idx < 0 || targetIdx < 0 || targetIdx >= d.DefinedJobs.length) return;
      const [moved] = d.DefinedJobs.splice(idx, 1);
      d.DefinedJobs.splice(targetIdx, 0, moved);
    });
  };

  const handleRunActiveJob = async (dryRun: boolean) => {
    if (!activeJob) return;
    const ok = await flushSaveNow();
    if (!ok) {
      showToast('Fix validation errors before running job.');
      return;
    }
    try {
      const res = await apiClient.runJob(activeJob.JobId, dryRun, false);
      setPipelineState(res.state);
      showToast(res.message);
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Unable to start job.');
    }
  };

  return (
    <div className="hk-builder-layout">
      <div className="hk-mobile-only hk-mobile-switcher-bar">
        <span style={{ fontSize: 13 }} aria-hidden="true">🛠</span>
        <select
          className="hk-select"
          value={activeJob?.JobId || ''}
          onChange={(e) => setSelectedJobId(e.target.value)}
          aria-label="Quick switch active job"
        >
          {config.DefinedJobs.map((j) => (
            <option key={j.JobId} value={j.JobId}>
              {j.Enabled ? '✓ ' : '○ '}
              {j.Name} ({MODULE_METADATA[j.ModuleType]?.short || j.ModuleType})
            </option>
          ))}
        </select>
        <button
          type="button"
          className={`hk-btn sm ${mobileListOpen ? 'primary' : ''}`}
          style={{ flexShrink: 0 }}
          onClick={() => setMobileListOpen((prev) => !prev)}
        >
          📂 {mobileListOpen ? 'Hide List' : `Manage (${config.DefinedJobs.length})`}
        </button>
      </div>

      <aside className={`hk-card hk-mobile-collapsible-sidebar ${mobileListOpen ? '' : 'collapsed'}`}>
        <div className="hk-row" style={{ justifyContent: 'space-between', marginBottom: 8 }}>
          <strong>
            Defined Jobs <span className="hk-pill">{config.DefinedJobs.length}</span>
          </strong>
          <select
            className="hk-select"
            style={{ width: 'auto', fontWeight: 700 }}
            value={newModuleChoice}
            onChange={(e) => {
              if (e.target.value) void handleCreateJob(e.target.value as ModuleId);
            }}
            aria-label="Create new job by module"
          >
            <option value="">+ New Job ▾</option>
            {(Object.keys(MODULE_METADATA) as ModuleId[]).map((m) => (
              <option key={m} value={m}>
                {MODULE_METADATA[m].badge}
              </option>
            ))}
          </select>
        </div>

        <div className="hk-grid-2" style={{ gap: 6, marginBottom: 8 }}>
          <select
            className="hk-select"
            value={filterModule}
            onChange={(e) => updateConfig((d) => (d.JobBuilderFilterModule = e.target.value))}
            aria-label="Filter jobs by module"
          >
            <option value="All">All Modules</option>
            {(Object.keys(MODULE_METADATA) as ModuleId[]).map((m) => (
              <option key={m} value={m}>
                {MODULE_METADATA[m].short}
              </option>
            ))}
          </select>

          <select
            className="hk-select"
            value={sortMode}
            onChange={(e) => updateConfig((d) => (d.JobBuilderSortMode = e.target.value))}
            aria-label="Sort jobs"
          >
            <option value="NameAsc">Name (A → Z)</option>
            <option value="Module">By Module</option>
            <option value="Custom">Manual Order</option>
          </select>
        </div>

        <div className="hk-row" style={{ justifyContent: 'center', gap: 14, marginBottom: 8 }}>
          <label className="hk-checkbox-label" style={{ fontSize: 12 }}>
            <input
              type="radio"
              name="hk-builder-view"
              checked={viewMode === 'Compact'}
              onChange={() => updateConfig((d) => (d.JobBuilderViewMode = 'Compact'))}
            />
            Compact
          </label>
          <label className="hk-checkbox-label" style={{ fontSize: 12 }}>
            <input
              type="radio"
              name="hk-builder-view"
              checked={viewMode === 'Detailed'}
              onChange={() => updateConfig((d) => (d.JobBuilderViewMode = 'Detailed'))}
            />
            Detailed
          </label>
        </div>

        <div className="hk-job-list">
          {visibleJobs.map((j) => {
            const meta = MODULE_METADATA[j.ModuleType];
            const enableHint = JOB_BUILDER_HINTS.sidebarJobEnabled(j.Name);
            return (
              <div
                key={j.JobId}
                className={`hk-job-item ${activeJob?.JobId === j.JobId ? 'active' : ''}`}
                onClick={() => {
                  setSelectedJobId(j.JobId);
                  setMobileListOpen(false);
                }}
              >
                <input
                  type="checkbox"
                  className="hk-has-hint"
                  title={enableHint}
                  checked={j.Enabled}
                  onClick={(e) => e.stopPropagation()}
                  onChange={(e) => updateJob(j.JobId, (draft) => (draft.Enabled = e.target.checked))}
                  aria-label={`Enable ${j.Name}`}
                />
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div
                    className="hk-row"
                    style={{ justifyContent: 'space-between', gap: 6, flexWrap: 'nowrap' }}
                  >
                    <strong
                      style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
                      title={j.Name}
                    >
                      {j.Name}
                    </strong>
                    {viewMode === 'Compact' ? (
                      <span
                        className="hk-pill accent icon-only nowrap"
                        title={`${meta?.label || j.ModuleType}`}
                      >
                        {meta?.icon || '⚙'}
                      </span>
                    ) : (
                      <span
                        className="hk-pill accent nowrap"
                        style={{ fontSize: 10.5, padding: '1px 6px' }}
                        title={meta?.label || j.ModuleType}
                      >
                        {meta?.badge || j.ModuleType}
                      </span>
                    )}
                  </div>
                  {viewMode === 'Detailed' && (
                    <div style={{ fontSize: 11, color: 'var(--text-secondary)', marginTop: 3 }}>
                      {summarizeJob(j)}
                    </div>
                  )}
                </div>
              </div>
            );
          })}
        </div>

        <div style={{ marginTop: 10, display: 'flex', flexDirection: 'column', gap: 6 }}>
          <div className="hk-row" style={{ justifyContent: 'space-between' }}>
            <button type="button" className="hk-btn sm" onClick={() => handleMoveDefinedJob(-1)}>
              ▲ Up
            </button>
            <button type="button" className="hk-btn sm" onClick={() => handleMoveDefinedJob(1)}>
              ▼ Down
            </button>
            <button
              type="button"
              className="hk-btn sm"
              disabled={!activeJob}
              onClick={() => void handleCloneActive()}
            >
              ⧉ Clone
            </button>
            <button
              type="button"
              className="hk-btn sm danger"
              disabled={!activeJob}
              onClick={() => void handleDeleteActive()}
            >
              ✕ Delete
            </button>
          </div>
          <button
            type="button"
            className="hk-btn sm"
            style={{ width: '100%' }}
            onClick={() => void handleAddStarterJobs()}
          >
            ✨ + Add All 8 Starter Module Jobs
          </button>
        </div>
      </aside>

      <div>
        {activeJob ? (
          <>
            <div className="hk-card" style={{ marginBottom: 'var(--gap-grid)' }}>
              <div className="hk-row" style={{ justifyContent: 'space-between' }}>
                <div className="hk-row" style={{ gap: 12, flex: 1 }}>
                  <div className="hk-row" style={{ gap: 6, flex: '1 1 240px', minInlineSize: 0 }}>
                    <strong>Editing Job Name:</strong>
                    <input
                      type="text"
                      className="hk-input hk-mobile-flex-input"
                      style={{ width: 220, fontWeight: 700 }}
                      value={activeJob.Name}
                      onChange={(e) => updateJob(activeJob.JobId, (d) => (d.Name = e.target.value))}
                    />
                  </div>

                  <span className="hk-pill accent nowrap">
                    {MODULE_METADATA[activeJob.ModuleType]?.badge || activeJob.ModuleType}
                  </span>

                  <label className="hk-checkbox-label hk-has-hint" title={JOB_BUILDER_HINTS.enabledInQueue}>
                    <input
                      type="checkbox"
                      className="hk-has-hint"
                      title={JOB_BUILDER_HINTS.enabledInQueue}
                      checked={activeJob.Enabled}
                      onChange={(e) => updateJob(activeJob.JobId, (d) => (d.Enabled = e.target.checked))}
                    />
                    Enabled In Queue
                  </label>

                  <label className="hk-checkbox-label hk-has-hint" title={JOB_BUILDER_HINTS.runInExternalCmd}>
                    <input
                      type="checkbox"
                      className="hk-has-hint"
                      title={JOB_BUILDER_HINTS.runInExternalCmd}
                      checked={activeJob.RunInExternalCmd}
                      onChange={(e) =>
                        updateJob(activeJob.JobId, (d) => (d.RunInExternalCmd = e.target.checked))
                      }
                    />
                    Run in Separate CMD Window
                  </label>

                  <div className="hk-row" style={{ gap: 6 }}>
                    <span className="hk-field-label">On Success → Trigger:</span>
                    <select
                      className="hk-select"
                      style={{ width: 'auto' }}
                      value={activeJob.OnSuccessTriggerJobId || ''}
                      onChange={(e) =>
                        updateJob(activeJob.JobId, (d) => (d.OnSuccessTriggerJobId = e.target.value))
                      }
                      aria-label="Post-job chain trigger on success"
                    >
                      <option value="">(None)</option>
                      {config.DefinedJobs.filter((j) => j.JobId !== activeJob.JobId).map((j) => (
                        <option key={j.JobId} value={j.JobId}>
                          ↳ {j.Name} ({MODULE_METADATA[j.ModuleType]?.short || j.ModuleType})
                        </option>
                      ))}
                    </select>
                  </div>
                </div>

                <div className="hk-row">
                  <button
                    type="button"
                    className={`hk-btn ${saveState === 'saving' ? 'primary' : ''}`}
                    onClick={() => void forceSaveNow(true)}
                    title="Force save all settings to settings.json immediately (Ctrl+S)"
                  >
                    💾 Save<span className="hk-desktop-only"> (Ctrl+S)</span>
                  </button>
                  <button
                    type="button"
                    className="hk-btn primary"
                    disabled={blockActiveJobTrigger}
                    onClick={() => void handleRunActiveJob(false)}
                  >
                    {isActiveJobRunningSingle
                      ? '⟳ Running...'
                      : isActiveJobQueued
                        ? '⏳ Queued Next'
                        : pipelineState.IsRunning
                          ? '⏭ Queue Job Next'
                          : '▶ Run This Job'}
                  </button>
                  <button
                    type="button"
                    className="hk-btn purple"
                    disabled={blockActiveJobTrigger}
                    onClick={() => void handleRunActiveJob(true)}
                  >
                    {pipelineState.IsRunning && !blockActiveJobTrigger
                      ? '🧪 Queue Dry-Run'
                      : '🧪 Dry-Run Job'}
                  </button>
                </div>
              </div>
            </div>

            {activeJob.ModuleType === 'ProxyGenerator' && (
              <ProxyGeneratorEditor
                cfg={activeJob.ProxyGenerator || config.ProxyGenerator}
                onChange={(mut) =>
                  updateJob(activeJob.JobId, (j) => {
                    if (!j.ProxyGenerator) j.ProxyGenerator = structuredClone(config.ProxyGenerator);
                    mut(j.ProxyGenerator);
                  })
                }
              />
            )}

            {activeJob.ModuleType === 'ArchiveCompressor' && (
              <ArchiveCompressorEditor
                cfg={activeJob.ArchiveCompressor || config.ArchiveCompressor}
                onChange={(mut) =>
                  updateJob(activeJob.JobId, (j) => {
                    if (!j.ArchiveCompressor)
                      j.ArchiveCompressor = structuredClone(config.ArchiveCompressor);
                    mut(j.ArchiveCompressor);
                  })
                }
              />
            )}

            {activeJob.ModuleType === 'MediaTranscoder' && (
              <MediaTranscoderEditor
                cfg={activeJob.MediaTranscoder || config.MediaTranscoder}
                onChange={(mut) =>
                  updateJob(activeJob.JobId, (j) => {
                    if (!j.MediaTranscoder) j.MediaTranscoder = structuredClone(config.MediaTranscoder);
                    mut(j.MediaTranscoder);
                  })
                }
              />
            )}

            {activeJob.ModuleType === 'RomLauncherGenerator' && (
              <RomLauncherGeneratorEditor
                jobId={activeJob.JobId}
                cfg={activeJob.RomLauncherGenerator || config.RomLauncherGenerator}
                onChange={(mut) =>
                  updateJob(activeJob.JobId, (j) => {
                    if (!j.RomLauncherGenerator)
                      j.RomLauncherGenerator = structuredClone(config.RomLauncherGenerator);
                    mut(j.RomLauncherGenerator);
                  })
                }
              />
            )}

            {activeJob.ModuleType === 'CloudSync' && (
              <CloudSyncEditor
                jobId={activeJob.JobId}
                cfg={activeJob.CloudSync || config.CloudSync}
                onChange={(mut) =>
                  updateJob(activeJob.JobId, (j) => {
                    if (!j.CloudSync) j.CloudSync = structuredClone(config.CloudSync);
                    mut(j.CloudSync);
                  })
                }
              />
            )}

            {activeJob.ModuleType === 'HumbleSync' && (
              <HumbleSyncEditor
                cfg={activeJob.HumbleSync || config.HumbleSync}
                onChange={(mut) =>
                  updateJob(activeJob.JobId, (j) => {
                    if (!j.HumbleSync) j.HumbleSync = structuredClone(config.HumbleSync);
                    mut(j.HumbleSync);
                  })
                }
              />
            )}

            {activeJob.ModuleType === 'CustomScripts' && (
              <CustomScriptsEditor
                cfg={activeJob.CustomScripts || config.CustomScripts}
                onChange={(mut) =>
                  updateJob(activeJob.JobId, (j) => {
                    if (!j.CustomScripts) j.CustomScripts = structuredClone(config.CustomScripts);
                    mut(j.CustomScripts);
                  })
                }
              />
            )}

            {activeJob.ModuleType === 'CollectionManifest' && (
              <CollectionManifestEditor
                jobId={activeJob.JobId}
                cfg={activeJob.CollectionManifest || config.CollectionManifest}
                onChange={(mut) =>
                  updateJob(activeJob.JobId, (j) => {
                    if (!j.CollectionManifest)
                      j.CollectionManifest = structuredClone(config.CollectionManifest);
                    mut(j.CollectionManifest);
                  })
                }
              />
            )}
          </>
        ) : (
          <div className="hk-card">
            <h3 className="hk-card-title">No Jobs Defined Yet</h3>
            <p className="hk-card-subtitle">
              Click <strong>+ New Job ▾</strong> or{' '}
              <strong>✨ + Add All 8 Starter Module Jobs</strong> in the left sidebar to begin.
            </p>
          </div>
        )}
      </div>
    </div>
  );
};
