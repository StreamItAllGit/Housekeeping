// @module: Housekeeping.Web/pages/DashboardPage
// @purpose: Main Dashboard overview with KPI telemetry cards, ActivePipelineControlBar, Named Job Queue quick dispatch, and DualPaneTerminal.
// @depends-on: react, ../components/ActivePipelineControlBar, ../components/DualPaneTerminal, ../context/AppStateContext, ../services/apiClient, ../types/models
// @exports: DashboardPage
import React, { useState } from 'react';
import { ActivePipelineControlBar } from '../components/ActivePipelineControlBar';
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
  const pendingRuns = pipelineState.PendingRuns || [];

  const runAction = async (fn: () => Promise<void>, fallbackErr: string) => {
    try {
      await fn();
    } catch (err) {
      showToast(err instanceof Error ? err.message : fallbackErr);
    }
  };

  const handleRunSpecificQueue = (queueId: string, isDryRun: boolean) =>
    runAction(async () => {
      if (!(await flushSaveNow())) return;
      const res = await apiClient.runQueue(queueId, isDryRun, false);
      setPipelineState(res.state);
      showToast(res.message);
    }, 'Unable to run queue.');

  const handleQuickCreateJob = async (e: React.FormEvent) => {
    e.preventDefault();
    setCreatingJob(true);
    try {
      await flushSaveNow();
      const res = await apiClient.createJob(newJobModule, newJobName.trim() || undefined, activeQueueId);
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
      <ActivePipelineControlBar
        pipelineState={pipelineState}
        onStopCurrentJob={() =>
          void runAction(async () => {
            const res = await apiClient.stopCurrentJob();
            setPipelineState(res.state);
            showToast('Cancelled current job — advancing queue.');
          }, 'Stop Job failed.')
        }
        onStopEntireQueue={() =>
          void runAction(async () => {
            const res = await apiClient.stopEntireQueue();
            setPipelineState(res.state);
            showToast('Stopped entire active queue and cleared Up Next.');
          }, 'Stop Queue failed.')
        }
        onRetrySwaps={() =>
          void runAction(async () => {
            const res = await apiClient.retryPendingSwaps();
            showToast(`Committed ${res.committed} pending swap(s); ${res.remaining} remaining.`);
          }, 'Retry swaps failed.')
        }
        onDiscardSwaps={() =>
          void runAction(async () => {
            await apiClient.discardPendingSwaps();
            showToast('Cleared deferred pending swaps queue.');
          }, 'Discard swaps failed.')
        }
        onRemovePendingRun={(key, name) =>
          void runAction(async () => {
            const res = await apiClient.removePendingRun(key);
            setPipelineState(res.state);
            showToast(`Removed '${name}' from Up Next queue.`);
          }, 'Could not remove pending run.')
        }
        onClearPendingRuns={() =>
          void runAction(async () => {
            const res = await apiClient.clearPendingRuns();
            setPipelineState(res.state);
            showToast(`Cleared ${res.cleared} pending Up Next run(s).`);
          }, 'Could not clear Up Next queue.')
        }
      />

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
          <div className="hk-kpi-value">{telemetry ? `${telemetry.CpuUsagePercent.toFixed(0)}% CPU` : '—'}</div>
          <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 4 }}>
            {telemetry
              ? `RAM: ${telemetry.RamUsedGb.toFixed(1)} / ${telemetry.RamTotalGb.toFixed(1)} GB (${telemetry.RamUsagePercent.toFixed(0)}%)`
              : 'Sampling system metrics...'}
          </div>
        </div>

        <div className="hk-kpi-card">
          <div className="hk-field-label">Disk I/O &amp; Network Throughput</div>
          <div className="hk-kpi-value">{telemetry ? `${telemetry.DiskReadWriteMbps.toFixed(1)} MB/s` : '—'}</div>
          <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 4 }}>
            {telemetry
              ? `Network: ${telemetry.NetworkUsageMbps.toFixed(1)} Mbps • ${telemetry.Disks.length} Mounted Drives`
              : 'Sampling I/O counters...'}
          </div>
        </div>

        <div className="hk-kpi-card" style={{ cursor: 'pointer' }} onClick={() => setActiveTab('reports')}>
          <div className="hk-field-label">Archive Compliance &amp; Integrity</div>
          <div className="hk-kpi-value" style={{ color: openComplianceCount > 0 ? 'var(--warn)' : 'var(--ok)' }}>
            {openComplianceCount > 0 ? `${openComplianceCount} Open` : '✓ All Clean'}
          </div>
          <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 4 }}>
            Click to inspect Archive Structure Reports &amp; 7z Clutter Cleanup →
          </div>
        </div>
      </div>

      <div className="hk-card" style={{ marginBottom: 'var(--gap-grid)' }}>
        <div className="hk-card-header">
          <div>
            <h3 className="hk-card-title">🗂 Named Job Queues &amp; Quick Dispatch</h3>
            <p className="hk-card-subtitle">
              Select the active queue or trigger any queue on demand (scheduled runs auto-chain without collisions).
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
            const isRunning =
              pipelineState.IsRunning &&
              Boolean(pipelineState.ActiveQueueId) &&
              pipelineState.ActiveQueueId?.toLowerCase() === q.Id.toLowerCase();
            const isQueued = pendingRuns.some(
              (pr) => pr.RunKind.toLowerCase() === 'queue' && pr.TargetId.toLowerCase() === q.Id.toLowerCase()
            );
            const blocked = isRunning || isQueued;
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
                    {isQueued && <span className="hk-pill accent nowrap">⏭ Up Next</span>}
                    <span className={`hk-pill nowrap ${q.EnableSchedule ? 'accent' : ''}`}>
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
                      disabled={blocked}
                      onClick={() => void handleRunSpecificQueue(q.Id, false)}
                    >
                      {isRunning ? '⟳ Running' : isQueued ? '⏳ Queued' : pipelineState.IsRunning ? '⏭ Queue Next' : '▶ Run'}
                    </button>
                    <button
                      type="button"
                      className="hk-btn sm purple"
                      disabled={blocked}
                      onClick={() => void handleRunSpecificQueue(q.Id, true)}
                    >
                      {pipelineState.IsRunning && !blocked ? '🧪 Queue Dry' : '🧪 Dry'}
                    </button>
                  </div>
                  <div className="hk-row" style={{ gap: 5 }}>
                    {!isSelected && (
                      <button
                        type="button"
                        className="hk-btn sm"
                        onClick={() => updateConfig((d) => (d.ActiveQueueId = q.Id))}
                      >
                        Set Active
                      </button>
                    )}
                    <button
                      type="button"
                      className="hk-btn sm"
                      onClick={() => {
                        updateConfig((d) => (d.ActiveQueueId = q.Id));
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

      <div className="hk-card">
        <DualPaneTerminal />
      </div>
    </div>
  );
};
