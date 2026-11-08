import React, { useState } from 'react';
import { DualPaneTerminal } from '../components/DualPaneTerminal';
import { useAppState } from '../context/AppStateContext';
import { apiClient } from '../services/apiClient';
import { MODULE_METADATA, type ModuleId, type NamedJobQueue } from '../types/models';

function formatScheduleSummary(q: NamedJobQueue): string {
  if (!q.EnableSchedule) return 'Manual Only';
  if (q.ScheduleMode === 'IntervalCadence') return `Every ${q.ScheduleIntervalMinutes}m`;
  if (q.ScheduleMode === 'SpecificTimes') return `At ${q.ScheduleSpecificTimesCsv}`;
  return `Daily at ${q.ScheduleDailyTime}`;
}

export const DashboardPage: React.FC = () => {
  const {
    config,
    updateConfig,
    replaceConfigFromServer,
    flushSaveNow,
    pipelineState,
    setPipelineState,
    telemetry,
    openComplianceCount,
    setActiveTab,
    openJobInBuilder,
    showToast,
  } = useAppState();

  const [newJobModule, setNewJobModule] = useState<ModuleId>('CollectionManifest');
  const [newJobName, setNewJobName] = useState('');
  const [creatingJob, setCreatingJob] = useState(false);

  const queues = config?.NamedQueues || [];
  const activeQueueId = config?.ActiveQueueId || queues[0]?.Id || '';

  const handleRunSpecificQueue = async (queueId: string, isDryRun: boolean) => {
    const saved = await flushSaveNow();
    if (!saved) return;
    try {
      const res = await apiClient.runQueue(queueId, isDryRun, false);
      setPipelineState(res.state);
      showToast(res.message);
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Unable to run queue.');
    }
  };

  const handleRetrySwaps = async () => {
    try {
      const res = await apiClient.retryPendingSwaps();
      showToast(`Committed ${res.committed} pending swap(s); ${res.remaining} remaining.`);
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Retry swaps failed.');
    }
  };

  const handleDiscardSwaps = async () => {
    try {
      await apiClient.discardPendingSwaps();
      showToast('Cleared deferred pending swaps queue.');
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Discard swaps failed.');
    }
  };

  const handleRemovePendingRun = async (itemKey: string, displayName: string) => {
    try {
      const res = await apiClient.removePendingRun(itemKey);
      setPipelineState(res.state);
      showToast(`Removed '${displayName}' from Up Next queue.`);
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Could not remove pending run.');
    }
  };

  const handleClearPendingRuns = async () => {
    try {
      const res = await apiClient.clearPendingRuns();
      setPipelineState(res.state);
      showToast(`Cleared ${res.cleared} pending Up Next run(s).`);
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Could not clear Up Next queue.');
    }
  };

  const pendingRuns = pipelineState.PendingRuns || [];

  const handleQuickCreateJob = async (e: React.FormEvent) => {
    e.preventDefault();
    setCreatingJob(true);
    try {
      await flushSaveNow();
      const res = await apiClient.createJob(
        newJobModule,
        newJobName.trim() || undefined,
        activeQueueId
      );
      const boot = await apiClient.getConfigBootstrap();
      replaceConfigFromServer(boot.config, boot.snapshotsCount);
      setNewJobName('');
      openJobInBuilder(res.job.JobId);
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Could not create job.');
    } finally {
      setCreatingJob(false);
    }
  };

  return (
    <div>
      {/* Alert Banners: Network Wait + Pending Swaps */}
      {pipelineState.IsWaitingForNetwork && (
        <div className="hk-alert-banner err">
          <div>
            <strong>🌐 Waiting for Network / NAS Reconnection:</strong>{' '}
            {pipelineState.NetworkWaitMessage || 'Retrying network storage availability...'}
          </div>
        </div>
      )}

      {pipelineState.PendingSwapCount > 0 && (
        <div className="hk-alert-banner">
          <div>
            <strong>⏳ {pipelineState.PendingSwapCount} Deferred Pending Swap(s) Ready:</strong>{' '}
            Target file was locked during transcoding
            {pipelineState.PendingSwapSampleFile
              ? ` (e.g. ${pipelineState.PendingSwapSampleFile})`
              : ''}
            . Housekeeping sweeps automatically or you can commit immediately.
          </div>
          <div className="hk-row">
            <button type="button" className="hk-btn sm primary" onClick={() => void handleRetrySwaps()}>
              🔄 Commit Swaps Now
            </button>
            <button type="button" className="hk-btn sm danger" onClick={() => void handleDiscardSwaps()}>
              🗑 Discard
            </button>
          </div>
        </div>
      )}

      {pendingRuns.length > 0 && (
        <div className="hk-alert-banner" style={{ alignItems: 'center' }}>
          <div style={{ flex: 1, minInlineSize: 0 }}>
            <div style={{ marginBottom: 6 }}>
              <strong>⏭ Up Next ({pendingRuns.length} Queued — Max 1 Per Distinct Queue/Job):</strong>{' '}
              Will auto-launch in FIFO order as soon as the active run completes.
            </div>
            <div className="hk-row" style={{ gap: 6, flexWrap: 'wrap' }}>
              {pendingRuns.map((pr, idx) => (
                <span
                  key={pr.ItemKey}
                  className={`hk-pill ${pr.IsDryRun ? '' : 'accent'}`}
                  style={{ display: 'inline-flex', alignItems: 'center', gap: 6, padding: '3px 8px' }}
                >
                  <strong>#{idx + 1}</strong>{' '}
                  {pr.RunKind === 'SingleJob' ? `🛠 ${pr.DisplayName}` : `🗂 ${pr.DisplayName}`}
                  {pr.IsDryRun ? ' [DRY-RUN]' : ''}
                  <button
                    type="button"
                    onClick={() => void handleRemovePendingRun(pr.ItemKey, pr.DisplayName)}
                    title={`Remove '${pr.DisplayName}' from Up Next`}
                    aria-label={`Remove ${pr.DisplayName} from Up Next`}
                    style={{
                      background: 'transparent',
                      border: 'none',
                      color: 'inherit',
                      cursor: 'pointer',
                      fontWeight: 700,
                      padding: '0 2px',
                      lineHeight: 1,
                    }}
                  >
                    ✕
                  </button>
                </span>
              ))}
            </div>
          </div>
          <div className="hk-row">
            <button
              type="button"
              className="hk-btn sm danger"
              onClick={() => void handleClearPendingRuns()}
            >
              🗑 Clear Queued
            </button>
          </div>
        </div>
      )}

      {/* Top Row: Live Pipeline & Host Telemetry */}
      <div className="hk-grid-4 hk-kpi-grid" style={{ marginBottom: 'var(--gap-grid)' }}>
        <div className="hk-kpi-card">
          <div className="hk-field-label">Active Pipeline Engine</div>
          <div style={{ fontWeight: 700, fontSize: 15, marginTop: 4 }}>
            {pipelineState.IsRunning
              ? `${pipelineState.IsDryRun ? '[DRY-RUN] ' : ''}${pipelineState.ActiveQueueName || 'Single Job'} (${pipelineState.CurrentJobIndex}/${pipelineState.TotalJobs})`
              : 'Idle — Ready'}
          </div>
          <div className="hk-kpi-status-text">
            {pipelineState.IsRunning
              ? `${pipelineState.ActiveJobName || ''}: ${pipelineState.SubprocessStatusText}`
              : pipelineState.GlobalNextRunStatusText}
          </div>
          <div className="hk-progress-track" style={{ marginTop: 8 }}>
            <div
              className="hk-progress-fill"
              style={{
                width: `${pipelineState.IsRunning ? Math.min(100, Math.max(2, pipelineState.ProgressPercent)) : 0}%`,
              }}
            />
          </div>
        </div>

        <div className="hk-kpi-card">
          <div className="hk-field-label">Host CPU &amp; Memory</div>
          <div className="hk-kpi-value">
            {telemetry ? `${telemetry.CpuUsagePercent.toFixed(0)}% CPU` : '—'}
          </div>
          <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 4 }}>
            {telemetry
              ? `RAM: ${telemetry.RamUsedGb.toFixed(1)} / ${telemetry.RamTotalGb.toFixed(1)} GB (${telemetry.RamUsagePercent.toFixed(0)}%)`
              : 'Sampling system metrics...'}
          </div>
        </div>

        <div className="hk-kpi-card">
          <div className="hk-field-label">Disk I/O &amp; Network Throughput</div>
          <div className="hk-kpi-value">
            {telemetry ? `${telemetry.DiskReadWriteMbps.toFixed(1)} MB/s` : '—'}
          </div>
          <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 4 }}>
            {telemetry
              ? `Network: ${telemetry.NetworkUsageMbps.toFixed(1)} Mbps • ${telemetry.Disks.length} Mounted Drives`
              : 'Sampling I/O counters...'}
          </div>
        </div>

        <div
          className="hk-kpi-card"
          style={{ cursor: 'pointer' }}
          onClick={() => setActiveTab('reports')}
        >
          <div className="hk-field-label">Archive Compliance &amp; Integrity</div>
          <div
            className="hk-kpi-value"
            style={{ color: openComplianceCount > 0 ? 'var(--warn)' : 'var(--ok)' }}
          >
            {openComplianceCount > 0 ? `${openComplianceCount} Open` : '✓ All Clean'}
          </div>
          <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 4 }}>
            Click to inspect Archive Structure Reports &amp; 7z Clutter Cleanup →
          </div>
        </div>
      </div>

      {/* Named Job Queues Strip + Quick Create Job */}
      <div className="hk-card" style={{ marginBottom: 'var(--gap-grid)' }}>
        <div className="hk-card-header">
          <div>
            <h3 className="hk-card-title">🗂 Named Job Queues &amp; Quick Dispatch</h3>
            <p className="hk-card-subtitle">
              Select the active queue or trigger any queue on demand (scheduled runs auto-chain
              without collisions).
            </p>
          </div>

          <form className="hk-row" onSubmit={(e) => void handleQuickCreateJob(e)}>
            <select
              className="hk-select"
              style={{ width: 'auto' }}
              value={newJobModule}
              onChange={(e) => setNewJobModule(e.target.value as ModuleId)}
              aria-label="New job module type"
            >
              {(Object.keys(MODULE_METADATA) as ModuleId[]).map((m) => (
                <option key={m} value={m}>
                  {MODULE_METADATA[m].badge}
                </option>
              ))}
            </select>
            <input
              type="text"
              className="hk-input hk-mobile-flex-input"
              style={{ width: 175 }}
              placeholder="Optional job name..."
              value={newJobName}
              onChange={(e) => setNewJobName(e.target.value)}
            />
            <button type="submit" className="hk-btn sm primary" disabled={creatingJob}>
              + Quick New Job
            </button>
          </form>
        </div>

        <div className="hk-grid-3">
          {queues.map((q) => {
            const isSelected = q.Id === activeQueueId;
            const enabledCount = (q.Jobs || []).filter((j) => j.Enabled).length;
            const isThisQueueRunning =
              pipelineState.IsRunning &&
              Boolean(pipelineState.ActiveQueueId) &&
              pipelineState.ActiveQueueId?.toLowerCase() === q.Id.toLowerCase();
            const isThisQueueQueued = pendingRuns.some(
              (pr) =>
                pr.RunKind.toLowerCase() === 'queue' &&
                pr.TargetId.toLowerCase() === q.Id.toLowerCase()
            );
            const blockQueueTrigger = isThisQueueRunning || isThisQueueQueued;
            return (
              <div
                key={q.Id}
                className="hk-card"
                style={{
                  marginTop: 0,
                  background: isSelected ? 'var(--bg-selected)' : 'var(--bg-elevated)',
                  borderColor: isSelected ? 'var(--accent)' : 'var(--border-subtle)',
                }}
              >
                <div className="hk-row" style={{ justifyContent: 'space-between', marginBottom: 6 }}>
                  <strong style={{ fontSize: 13.5 }}>{q.Name}</strong>
                  <div className="hk-row" style={{ gap: 4 }}>
                    {isThisQueueQueued && <span className="hk-pill accent">⏭ Up Next</span>}
                    <span className={`hk-pill ${q.EnableSchedule ? 'accent' : ''}`}>
                      ⏰ {formatScheduleSummary(q)}
                    </span>
                  </div>
                </div>
                <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginBottom: 10 }}>
                  {enabledCount} / {(q.Jobs || []).length} job(s) enabled
                  {q.Description ? ` • ${q.Description}` : ''}
                </div>
                <div className="hk-row" style={{ justifyContent: 'space-between' }}>
                  <div className="hk-row" style={{ gap: 5 }}>
                    <button
                      type="button"
                      className="hk-btn sm success"
                      disabled={blockQueueTrigger}
                      onClick={() => void handleRunSpecificQueue(q.Id, false)}
                      title={
                        isThisQueueRunning
                          ? 'This queue is currently running'
                          : isThisQueueQueued
                            ? 'Already queued in Up Next (duplicate queueing prevented)'
                            : pipelineState.IsRunning
                              ? 'Queue this job queue once to execute right after the active run'
                              : 'Run this queue immediately'
                      }
                    >
                      {isThisQueueRunning
                        ? '⟳ Running'
                        : isThisQueueQueued
                          ? '⏳ Queued'
                          : pipelineState.IsRunning
                            ? '⏭ Queue Next'
                            : '▶ Run'}
                    </button>
                    <button
                      type="button"
                      className="hk-btn sm purple"
                      disabled={blockQueueTrigger}
                      onClick={() => void handleRunSpecificQueue(q.Id, true)}
                    >
                      {pipelineState.IsRunning && !blockQueueTrigger ? '🧪 Queue Dry' : '🧪 Dry'}
                    </button>
                  </div>
                  <div className="hk-row" style={{ gap: 5 }}>
                    {!isSelected && (
                      <button
                        type="button"
                        className="hk-btn sm"
                        onClick={() =>
                          updateConfig((d) => {
                            d.ActiveQueueId = q.Id;
                          })
                        }
                      >
                        Set Active
                      </button>
                    )}
                    <button
                      type="button"
                      className="hk-btn sm"
                      onClick={() => {
                        updateConfig((d) => {
                          d.ActiveQueueId = q.Id;
                        });
                        setActiveTab('queues');
                      }}
                    >
                      Configure →
                    </button>
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      </div>

      {/* Dual-Pane Live Terminal */}
      <div className="hk-card">
        <DualPaneTerminal />
      </div>
    </div>
  );
};
