import React, { useEffect, useState } from 'react';
import { useAppState } from '../context/AppStateContext';
import { apiClient } from '../services/apiClient';
import { MODULE_METADATA, type MasterScheduleMode } from '../types/models';

const DAYS_OF_WEEK = [
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
  'Sunday',
];

export const JobQueuesPage: React.FC = () => {
  const {
    config,
    updateConfig,
    flushSaveNow,
    pipelineState,
    setPipelineState,
    openJobInBuilder,
    showToast,
  } = useAppState();

  const [selectedQueueId, setSelectedQueueId] = useState<string>('');
  const [addExistingJobId, setAddExistingJobId] = useState<string>('');
  const [upcomingRuns, setUpcomingRuns] = useState<string[]>([]);
  const [mobileQueuesOpen, setMobileQueuesOpen] = useState(false);

  const queues = config?.NamedQueues || [];
  const activeQueue =
    queues.find((q) => q.Id === selectedQueueId) ||
    queues.find((q) => q.Id === config?.ActiveQueueId) ||
    queues[0] ||
    null;

  useEffect(() => {
    if (activeQueue && selectedQueueId !== activeQueue.Id) {
      setSelectedQueueId(activeQueue.Id);
    }
  }, [activeQueue, selectedQueueId]);

  useEffect(() => {
    if (!activeQueue) return;
    let cancelled = false;
    const t = window.setTimeout(() => {
      apiClient
        .getQueueUpcoming(activeQueue.Id)
        .then((res) => {
          if (!cancelled) setUpcomingRuns(res.upcomingUtc || []);
        })
        .catch(() => {
          if (!cancelled) setUpcomingRuns([]);
        });
    }, 350);
    return () => {
      cancelled = true;
      window.clearTimeout(t);
    };
  }, [
    activeQueue?.Id,
    activeQueue?.EnableSchedule,
    activeQueue?.ScheduleMode,
    activeQueue?.ScheduleDailyTime,
    activeQueue?.ScheduleIntervalMinutes,
    activeQueue?.ScheduleSpecificTimesCsv,
    activeQueue?.ScheduleDays,
  ]);

  if (!config || !activeQueue) {
    return <div className="hk-card">Loading job queues...</div>;
  }

  const availableJobsToAttach = config.DefinedJobs.filter(
    (dj) => !activeQueue.Jobs.some((qj) => qj.JobId === dj.JobId)
  );
  const pendingRuns = pipelineState.PendingRuns || [];
  const isSelectedQueueRunning =
    pipelineState.IsRunning &&
    Boolean(pipelineState.ActiveQueueId) &&
    pipelineState.ActiveQueueId?.toLowerCase() === activeQueue.Id.toLowerCase();
  const isSelectedQueueQueued = pendingRuns.some(
    (pr) =>
      pr.RunKind.toLowerCase() === 'queue' &&
      pr.TargetId.toLowerCase() === activeQueue.Id.toLowerCase()
  );
  const blockSelectedQueueTrigger = isSelectedQueueRunning || isSelectedQueueQueued;

  const handleCreateQueue = () => {
    const id = `queue-${Math.random().toString(36).slice(2, 9)}`;
    updateConfig((d) => {
      d.NamedQueues.push({
        Id: id,
        Name: `Queue ${d.NamedQueues.length + 1}`,
        Description: '',
        EnableSchedule: false,
        ScheduleMode: 'DailyTime',
        ScheduleDailyTime: '03:00',
        ScheduleIntervalMinutes: 60,
        ScheduleSpecificTimesCsv: '03:00, 15:00',
        ScheduleDays: [...DAYS_OF_WEEK],
        Jobs: [],
      });
      d.ActiveQueueId = id;
    });
    setSelectedQueueId(id);
  };

  const handleDeleteQueue = () => {
    if (queues.length <= 1) {
      showToast('At least one Named Job Queue must remain.');
      return;
    }
    const deletedName = activeQueue.Name;
    updateConfig((d) => {
      d.NamedQueues = d.NamedQueues.filter((q) => q.Id !== activeQueue.Id);
      d.ActiveQueueId = d.NamedQueues[0]?.Id || '';
    });
    showToast(`Deleted queue '${deletedName}'.`);
  };

  const handleMoveJob = (idx: number, dir: -1 | 1) => {
    const targetIdx = idx + dir;
    if (targetIdx < 0 || targetIdx >= activeQueue.Jobs.length) return;
    updateConfig((d) => {
      const q = d.NamedQueues.find((x) => x.Id === activeQueue.Id);
      if (!q) return;
      const [item] = q.Jobs.splice(idx, 1);
      q.Jobs.splice(targetIdx, 0, item);
    });
  };

  const handleRunThisQueue = async (dryRun: boolean) => {
    const ok = await flushSaveNow();
    if (!ok) return;
    try {
      const res = await apiClient.runQueue(activeQueue.Id, dryRun, false);
      setPipelineState(res.state);
      showToast(res.message);
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Unable to start queue.');
    }
  };

  return (
    <div className="hk-builder-layout">
      {/* Mobile Quick Queue Switcher Bar (<= 768px only) */}
      <div className="hk-mobile-only hk-mobile-switcher-bar">
        <span style={{ fontSize: 13 }} aria-hidden="true">
          🗂
        </span>
        <select
          className="hk-select"
          value={activeQueue.Id}
          onChange={(e) => {
            const id = e.target.value;
            setSelectedQueueId(id);
            updateConfig((d) => {
              d.ActiveQueueId = id;
            });
          }}
          aria-label="Quick switch active job queue"
        >
          {queues.map((q) => (
            <option key={q.Id} value={q.Id}>
              {q.Name} ({q.Jobs.filter((j) => j.Enabled).length}/{q.Jobs.length} enabled)
            </option>
          ))}
        </select>
        <button
          type="button"
          className="hk-btn sm primary"
          style={{ flexShrink: 0 }}
          onClick={handleCreateQueue}
          title="Create new named queue"
        >
          + New
        </button>
        <button
          type="button"
          className={`hk-btn sm ${mobileQueuesOpen ? 'primary' : ''}`}
          style={{ flexShrink: 0 }}
          onClick={() => setMobileQueuesOpen((prev) => !prev)}
        >
          {mobileQueuesOpen ? 'Hide' : `List (${queues.length})`}
        </button>
      </div>

      {/* Left Sidebar: Named Queues (permanently visible on desktop; toggled on mobile) */}
      <div
        className={`hk-card hk-mobile-collapsible-sidebar ${mobileQueuesOpen ? '' : 'collapsed'}`}
      >
        <div className="hk-row" style={{ justifyContent: 'space-between', marginBottom: 10 }}>
          <strong>Named Queues ({queues.length})</strong>
          <button type="button" className="hk-btn sm primary" onClick={handleCreateQueue}>
            + New Queue
          </button>
        </div>

        <div className="hk-job-list">
          {queues.map((q) => {
            const isDefaultActive = q.Id === config.ActiveQueueId;
            return (
              <div
                key={q.Id}
                className={`hk-job-item ${q.Id === activeQueue.Id ? 'active' : ''}`}
                onClick={() => {
                  setSelectedQueueId(q.Id);
                  updateConfig((d) => {
                    d.ActiveQueueId = q.Id;
                  });
                  setMobileQueuesOpen(false);
                }}
              >
                <div style={{ flex: 1 }}>
                  <div className="hk-row" style={{ justifyContent: 'space-between' }}>
                    <strong>{q.Name}</strong>
                    {isDefaultActive && <span className="hk-pill accent">Active</span>}
                  </div>
                  <div style={{ fontSize: 11.5, color: 'var(--text-secondary)', marginTop: 2 }}>
                    {q.Jobs.filter((j) => j.Enabled).length}/{q.Jobs.length} enabled •{' '}
                    {q.EnableSchedule ? `⏰ ${q.ScheduleMode}` : 'Manual'}
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      </div>

      {/* Right Pane: Queue Metadata, Schedule Configurator & Ordered Pipeline */}
      <div>
        <div className="hk-card">
          <div className="hk-card-header">
            <div className="hk-row" style={{ flex: 1 }}>
              <div className="hk-field" style={{ flex: '1 1 200px', minInlineSize: 0 }}>
                <label className="hk-field-label">Queue Name</label>
                <input
                  type="text"
                  className="hk-input"
                  value={activeQueue.Name}
                  onChange={(e) =>
                    updateConfig((d) => {
                      const q = d.NamedQueues.find((x) => x.Id === activeQueue.Id);
                      if (q) q.Name = e.target.value;
                    })
                  }
                />
              </div>
              <div className="hk-field" style={{ flex: '2 1 220px', minInlineSize: 0 }}>
                <label className="hk-field-label">Description</label>
                <input
                  type="text"
                  className="hk-input"
                  value={activeQueue.Description}
                  onChange={(e) =>
                    updateConfig((d) => {
                      const q = d.NamedQueues.find((x) => x.Id === activeQueue.Id);
                      if (q) q.Description = e.target.value;
                    })
                  }
                  placeholder="Purpose of this pipeline..."
                />
              </div>
            </div>

            <div className="hk-row">
              <button
                type="button"
                className="hk-btn success"
                disabled={blockSelectedQueueTrigger}
                onClick={() => void handleRunThisQueue(false)}
              >
                {isSelectedQueueRunning
                  ? '⟳ Running...'
                  : isSelectedQueueQueued
                    ? '⏳ Queued Next'
                    : pipelineState.IsRunning
                      ? '⏭ Queue This Next'
                      : '▶ Run This Queue'}
              </button>
              <button
                type="button"
                className="hk-btn purple"
                disabled={blockSelectedQueueTrigger}
                onClick={() => void handleRunThisQueue(true)}
              >
                {pipelineState.IsRunning && !blockSelectedQueueTrigger
                  ? '🧪 Queue Dry Run'
                  : '🧪 Dry Run'}
              </button>
              <button
                type="button"
                className="hk-btn danger"
                disabled={queues.length <= 1}
                onClick={handleDeleteQueue}
              >
                🗑 Delete Queue
              </button>
            </div>
          </div>

          {/* Independent Queue Schedule Configurator */}
          <div
            style={{
              padding: 12,
              borderRadius: 'var(--radius-md)',
              background: 'var(--bg-elevated)',
              border: '1px solid var(--border-subtle)',
            }}
          >
            <div className="hk-row" style={{ justifyContent: 'space-between', marginBottom: 10 }}>
              <label className="hk-checkbox-label">
                <input
                  type="checkbox"
                  checked={activeQueue.EnableSchedule}
                  onChange={(e) =>
                    updateConfig((d) => {
                      const q = d.NamedQueues.find((x) => x.Id === activeQueue.Id);
                      if (q) q.EnableSchedule = e.target.checked;
                    })
                  }
                />
                <strong>⏰ Enable Automatic Schedule for &ldquo;{activeQueue.Name}&rdquo;</strong>
              </label>

              {activeQueue.LastRunUtc && (
                <span style={{ fontSize: 11.5, color: 'var(--text-secondary)' }}>
                  Last Scheduled Run: {new Date(activeQueue.LastRunUtc).toLocaleString()}
                </span>
              )}
            </div>

            <div className="hk-grid-3" style={{ marginBottom: 10 }}>
              <div className="hk-field">
                <label className="hk-field-label">Schedule Cadence Mode</label>
                <select
                  className="hk-select"
                  disabled={!activeQueue.EnableSchedule}
                  value={activeQueue.ScheduleMode}
                  onChange={(e) =>
                    updateConfig((d) => {
                      const q = d.NamedQueues.find((x) => x.Id === activeQueue.Id);
                      if (q) q.ScheduleMode = e.target.value as MasterScheduleMode;
                    })
                  }
                >
                  <option value="DailyTime">Daily at Specific Time (HH:mm)</option>
                  <option value="IntervalCadence">Recurring Interval (Minutes)</option>
                  <option value="SpecificTimes">Multiple Daily Times (CSV)</option>
                </select>
              </div>

              {activeQueue.ScheduleMode === 'DailyTime' && (
                <div className="hk-field">
                  <label className="hk-field-label">Daily Time (24h HH:mm)</label>
                  <input
                    type="time"
                    className="hk-input mono"
                    disabled={!activeQueue.EnableSchedule}
                    value={activeQueue.ScheduleDailyTime}
                    onChange={(e) =>
                      updateConfig((d) => {
                        const q = d.NamedQueues.find((x) => x.Id === activeQueue.Id);
                        if (q) q.ScheduleDailyTime = e.target.value;
                      })
                    }
                  />
                </div>
              )}

              {activeQueue.ScheduleMode === 'IntervalCadence' && (
                <div className="hk-field">
                  <label className="hk-field-label">Interval (Minutes)</label>
                  <input
                    type="number"
                    min={5}
                    className="hk-input"
                    disabled={!activeQueue.EnableSchedule}
                    value={activeQueue.ScheduleIntervalMinutes}
                    onChange={(e) =>
                      updateConfig((d) => {
                        const q = d.NamedQueues.find((x) => x.Id === activeQueue.Id);
                        if (q) q.ScheduleIntervalMinutes = Math.max(5, Number(e.target.value) || 60);
                      })
                    }
                  />
                </div>
              )}

              {activeQueue.ScheduleMode === 'SpecificTimes' && (
                <div className="hk-field">
                  <label className="hk-field-label">Times CSV (e.g. 03:00, 12:00, 18:00)</label>
                  <input
                    type="text"
                    className="hk-input mono"
                    disabled={!activeQueue.EnableSchedule}
                    value={activeQueue.ScheduleSpecificTimesCsv}
                    onChange={(e) =>
                      updateConfig((d) => {
                        const q = d.NamedQueues.find((x) => x.Id === activeQueue.Id);
                        if (q) q.ScheduleSpecificTimesCsv = e.target.value;
                      })
                    }
                  />
                </div>
              )}

              <div className="hk-field">
                <label className="hk-field-label">Active Days of Week</label>
                <div className="hk-row" style={{ gap: 4 }}>
                  {DAYS_OF_WEEK.map((day) => {
                    const checked = (activeQueue.ScheduleDays || []).includes(day);
                    return (
                      <button
                        key={day}
                        type="button"
                        disabled={!activeQueue.EnableSchedule}
                        className={`hk-btn sm ${checked ? 'primary' : ''}`}
                        onClick={() =>
                          updateConfig((d) => {
                            const q = d.NamedQueues.find((x) => x.Id === activeQueue.Id);
                            if (!q) return;
                            if (q.ScheduleDays.includes(day)) {
                              q.ScheduleDays = q.ScheduleDays.filter((x) => x !== day);
                            } else {
                              q.ScheduleDays.push(day);
                            }
                          })
                        }
                      >
                        {day.slice(0, 3)}
                      </button>
                    );
                  })}
                </div>
              </div>
            </div>

            {activeQueue.EnableSchedule && upcomingRuns.length > 0 && (
              <div className="hk-row" style={{ fontSize: 11.5, color: 'var(--text-secondary)' }}>
                <strong>Upcoming 5 Triggers:</strong>
                {upcomingRuns.map((iso) => (
                  <span key={iso} className="hk-pill">
                    {new Date(iso).toLocaleString()}
                  </span>
                ))}
              </div>
            )}
          </div>
        </div>

        {/* Ordered Jobs in Active Queue */}
        <div className="hk-card">
          <div className="hk-card-header">
            <div>
              <h3 className="hk-card-title">
                🔗 Ordered Execution Pipeline ({activeQueue.Jobs.length} Jobs)
              </h3>
              <p className="hk-card-subtitle">
                Jobs run top-to-bottom. Any job with an <code>OnSuccessTriggerJobId</code> chain
                fires its follow-up job automatically after succeeding.
              </p>
            </div>

            <div className="hk-row">
              <select
                className="hk-select"
                style={{ width: 'auto' }}
                value={addExistingJobId}
                onChange={(e) => setAddExistingJobId(e.target.value)}
                aria-label="Select existing defined job to attach"
              >
                <option value="">Attach Defined Job to Queue...</option>
                {availableJobsToAttach.map((j) => (
                  <option key={j.JobId} value={j.JobId}>
                    {j.Name} ({MODULE_METADATA[j.ModuleType]?.short || j.ModuleType})
                  </option>
                ))}
              </select>
              <button
                type="button"
                className="hk-btn sm primary"
                disabled={!addExistingJobId}
                onClick={() => {
                  const jobToAdd = config.DefinedJobs.find((j) => j.JobId === addExistingJobId);
                  if (!jobToAdd) return;
                  updateConfig((d) => {
                    const q = d.NamedQueues.find((x) => x.Id === activeQueue.Id);
                    if (q && !q.Jobs.some((x) => x.JobId === jobToAdd.JobId)) {
                      q.Jobs.push(structuredClone(jobToAdd));
                    }
                  });
                  setAddExistingJobId('');
                }}
              >
                + Attach to Queue
              </button>
              <button
                type="button"
                className="hk-btn sm"
                onClick={() => {
                  const blob = new Blob([JSON.stringify(activeQueue, null, 2)], { type: 'application/json' });
                  const url = URL.createObjectURL(blob);
                  const a = document.createElement('a');
                  a.href = url;
                  a.download = `Housekeeping_Queue_${activeQueue.Name.replace(/[^a-zA-Z0-9_-]/g, '_')}.json`;
                  document.body.appendChild(a);
                  a.click();
                  document.body.removeChild(a);
                  URL.revokeObjectURL(url);
                }}
                title="Export active queue definition and attached jobs as JSON"
              >
                📥 Export Queue JSON
              </button>
            </div>
          </div>

          <div className="table-wrapper" tabIndex={0} role="region" aria-label="Jobs in queue">
            <table className="hk-table stack-mobile">
              <thead>
                <tr>
                  <th style={{ width: 55 }}>Order</th>
                  <th style={{ width: 65, textAlign: 'center' }}>
                    <input
                      type="checkbox"
                      checked={activeQueue.Jobs.length > 0 && activeQueue.Jobs.every((j) => j.Enabled)}
                      onChange={(e) => {
                        const targetEnabled = e.target.checked;
                        updateConfig((d) => {
                          const q = d.NamedQueues.find((x) => x.Id === activeQueue.Id);
                          if (q) {
                            for (const j of q.Jobs) {
                              j.Enabled = targetEnabled;
                              const dj = d.DefinedJobs.find((x) => x.JobId === j.JobId);
                              if (dj) dj.Enabled = targetEnabled;
                            }
                          }
                        });
                      }}
                      title="Toggle all jobs enabled/disabled in this queue"
                      aria-label="Toggle all jobs enabled"
                    />
                  </th>
                  <th>Job Name</th>
                  <th>Module</th>
                  <th>Post-Job Chain</th>
                  <th>Live State</th>
                  <th style={{ width: 220 }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {activeQueue.Jobs.length === 0 && (
                  <tr>
                    <td colSpan={7} style={{ color: 'var(--text-secondary)' }}>
                      This queue has no jobs attached yet. Attach an existing job above or create one
                      in Job Builder.
                    </td>
                  </tr>
                )}
                {activeQueue.Jobs.map((job, idx) => {
                  const chained = job.OnSuccessTriggerJobId
                    ? config.DefinedJobs.find((x) => x.JobId === job.OnSuccessTriggerJobId)
                    : null;
                  const live = pipelineState.JobStates?.[job.JobId];
                  return (
                    <tr key={job.JobId}>
                      <td data-label="Order">
                        <strong>#{idx + 1}</strong>
                      </td>
                      <td data-label="Enabled">
                        <input
                          type="checkbox"
                          checked={job.Enabled}
                          onChange={(e) =>
                            updateConfig((d) => {
                              const dj = d.DefinedJobs.find((x) => x.JobId === job.JobId);
                              if (dj) dj.Enabled = e.target.checked;
                              const q = d.NamedQueues.find((x) => x.Id === activeQueue.Id);
                              const qj = q?.Jobs.find((x) => x.JobId === job.JobId);
                              if (qj) qj.Enabled = e.target.checked;
                            })
                          }
                        />
                      </td>
                      <td data-label="Job Name">
                        <strong>{job.Name}</strong>
                      </td>
                      <td data-label="Module">
                        <span className="hk-pill accent">
                          {MODULE_METADATA[job.ModuleType]?.badge || job.ModuleType}
                        </span>
                      </td>
                      <td data-label="Post-Job Chain">
                        {chained ? (
                          <span className="hk-pill ok">↳ {chained.Name}</span>
                        ) : (
                          <span style={{ color: 'var(--text-muted)', fontSize: 12 }}>—</span>
                        )}
                      </td>
                      <td data-label="Live State">
                        <span className="hk-pill">{live?.State || 'Idle'}</span>
                      </td>
                      <td data-label="Actions">
                        <div className="hk-row" style={{ gap: 4 }}>
                          <button
                            type="button"
                            className="hk-btn sm"
                            disabled={idx === 0}
                            onClick={() => handleMoveJob(idx, -1)}
                          >
                            ▲
                          </button>
                          <button
                            type="button"
                            className="hk-btn sm"
                            disabled={idx === activeQueue.Jobs.length - 1}
                            onClick={() => handleMoveJob(idx, 1)}
                          >
                            ▼
                          </button>
                          <button
                            type="button"
                            className="hk-btn sm primary"
                            onClick={() => openJobInBuilder(job.JobId)}
                          >
                            Edit
                          </button>
                          <button
                            type="button"
                            className="hk-btn sm danger"
                            onClick={() =>
                              updateConfig((d) => {
                                const q = d.NamedQueues.find((x) => x.Id === activeQueue.Id);
                                if (q) q.Jobs = q.Jobs.filter((x) => x.JobId !== job.JobId);
                              })
                            }
                            title="Remove from this queue (keeps job in Defined Jobs)"
                          >
                            Detach
                          </button>
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>
  );
};
