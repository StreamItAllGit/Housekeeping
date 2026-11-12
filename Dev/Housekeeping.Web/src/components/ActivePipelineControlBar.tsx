// @module: Housekeeping.Web/components/ActivePipelineControlBar
// @purpose: Extracted Dashboard active-run control bar, network-wait/pending-swap alerts, and Up Next pending runs banner.
// @depends-on: react, ../types/models
// @exports: ActivePipelineControlBar
import React from 'react';
import type { PipelineRuntimeStateDto } from '../types/models';

interface ActivePipelineControlBarProps {
  pipelineState: PipelineRuntimeStateDto;
  onStopCurrentJob: () => void;
  onStopEntireQueue: () => void;
  onRetrySwaps: () => void;
  onDiscardSwaps: () => void;
  onRemovePendingRun: (itemKey: string, displayName: string) => void;
  onClearPendingRuns: () => void;
}

export const ActivePipelineControlBar: React.FC<ActivePipelineControlBarProps> = ({
  pipelineState,
  onStopCurrentJob,
  onStopEntireQueue,
  onRetrySwaps,
  onDiscardSwaps,
  onRemovePendingRun,
  onClearPendingRuns,
}) => {
  const pendingRuns = pipelineState.PendingRuns || [];

  return (
    <>
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
            {pipelineState.PendingSwapSampleFile ? ` (e.g. ${pipelineState.PendingSwapSampleFile})` : ''}.
            Housekeeping sweeps automatically or you can commit immediately.
          </div>
          <div className="hk-row">
            <button type="button" className="hk-btn sm primary" onClick={onRetrySwaps}>
              🔄 Commit Swaps Now
            </button>
            <button type="button" className="hk-btn sm danger" onClick={onDiscardSwaps}>
              🗑 Discard
            </button>
          </div>
        </div>
      )}

      {pipelineState.IsRunning && (
        <div className="hk-alert-banner" style={{ borderColor: 'var(--accent)', background: 'var(--accent-soft)' }}>
          <div>
            <strong>
              ⟳ Active Run:{' '}
              {pipelineState.ActiveQueueName || pipelineState.ActiveJobName || 'Pipeline'}
            </strong>{' '}
            ({pipelineState.CurrentJobIndex}/{pipelineState.TotalJobs}) —{' '}
            {pipelineState.SubprocessStatusText}
          </div>
          <div className="hk-row" style={{ gap: 6 }}>
            <button
              type="button"
              className="hk-btn sm warn"
              onClick={onStopCurrentJob}
              title="Abort only the active job and immediately advance to the next job in the queue"
            >
              ⏹ Stop Job
            </button>
            <button
              type="button"
              className="hk-btn sm danger"
              onClick={onStopEntireQueue}
              title="Stop the entire active queue and clear pending Up Next runs"
            >
              ⏹ Stop Queue
            </button>
          </div>
        </div>
      )}

      {pendingRuns.length > 0 && (
        <div className="hk-alert-banner" style={{ alignItems: 'center' }}>
          <div style={{ flex: 1, minInlineSize: 0 }}>
            <div style={{ marginBottom: 6 }}>
              <strong>
                ⏭ Up Next ({pendingRuns.length} Queued — Max 1 Per Distinct Queue/Job):
              </strong>{' '}
              Will auto-launch in FIFO order as soon as the active run completes.
            </div>
            <div className="hk-row" style={{ gap: 6, flexWrap: 'wrap' }}>
              {pendingRuns.map((pr, idx) => (
                <span
                  key={pr.ItemKey}
                  className={`hk-pill nowrap ${pr.IsDryRun ? '' : 'accent'}`}
                  style={{ display: 'inline-flex', alignItems: 'center', gap: 6, padding: '3px 8px' }}
                >
                  <strong>#{idx + 1}</strong>{' '}
                  {pr.RunKind === 'SingleJob' ? `🛠 ${pr.DisplayName}` : `🗂 ${pr.DisplayName}`}
                  {pr.IsDryRun ? ' [DRY-RUN]' : ''}
                  <button
                    type="button"
                    onClick={() => onRemovePendingRun(pr.ItemKey, pr.DisplayName)}
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
            <button type="button" className="hk-btn sm danger" onClick={onClearPendingRuns}>
              🗑 Clear Queued
            </button>
          </div>
        </div>
      )}
    </>
  );
};
