// @module: Housekeeping.Web/pages/queues/QueueScheduleControls
// @purpose: Extracted Named Job Queue schedule configurator with cadence inputs, day-of-week pills, and upcoming trigger preview.
// @depends-on: react, ../../hints/jobQueues, ../../types/models
// @exports: QueueScheduleControls, DAYS_OF_WEEK
import React from 'react';
import { JOB_QUEUES_HINTS } from '../../hints/jobQueues';
import type { AppConfig, MasterScheduleMode, NamedJobQueue } from '../../types/models';

export const DAYS_OF_WEEK = [
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
  'Sunday',
];

interface QueueScheduleControlsProps {
  activeQueue: NamedJobQueue;
  upcomingRuns: string[];
  updateConfig: (mutator: (d: AppConfig) => void) => void;
}

export const QueueScheduleControls: React.FC<QueueScheduleControlsProps> = ({
  activeQueue,
  upcomingRuns,
  updateConfig,
}) => {
  return (
    <div
      style={{
        padding: 12,
        borderRadius: 'var(--radius-md)',
        background: 'var(--bg-elevated)',
        border: '1px solid var(--border-subtle)',
      }}
    >
      <div className="hk-row" style={{ justifyContent: 'space-between', marginBottom: 10 }}>
        <label className="hk-checkbox-label hk-has-hint" title={JOB_QUEUES_HINTS.enableSchedule}>
          <input
            type="checkbox"
            className="hk-has-hint"
            title={JOB_QUEUES_HINTS.enableSchedule}
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
            <span key={iso} className="hk-pill nowrap">
              {new Date(iso).toLocaleString()}
            </span>
          ))}
        </div>
      )}
    </div>
  );
};
