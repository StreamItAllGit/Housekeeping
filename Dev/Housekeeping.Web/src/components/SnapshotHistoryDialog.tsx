import React, { useEffect, useState } from 'react';
import { useAppState } from '../context/AppStateContext';
import { COMMON_UI_HINTS } from '../hints/commonDialogs';
import { apiClient } from '../services/apiClient';
import type { ConfigSnapshotInfo } from '../types/models';
import { ModalDialog } from './ModalDialog';

interface SnapshotHistoryDialogProps {
  open: boolean;
  onClose: () => void;
}

export const SnapshotHistoryDialog: React.FC<SnapshotHistoryDialogProps> = ({ open, onClose }) => {
  const { replaceConfigFromServer, showToast } = useAppState();
  const [snapshots, setSnapshots] = useState<ConfigSnapshotInfo[]>([]);
  const [loading, setLoading] = useState(false);
  const [restoringId, setRestoringId] = useState('');

  useEffect(() => {
    if (!open) return;
    setLoading(true);
    apiClient
      .listSnapshots()
      .then((list) => setSnapshots(list))
      .catch((err) => {
        console.debug('[SnapshotHistoryDialog] listSnapshots failed:', err);
        setSnapshots([]);
      })
      .finally(() => setLoading(false));
  }, [open]);

  const handleRestore = async (snap: ConfigSnapshotInfo) => {
    setRestoringId(snap.SnapshotId);
    try {
      const res = await apiClient.restoreSnapshot(snap.SnapshotId);
      replaceConfigFromServer(res.config, res.snapshots.length);
      setSnapshots(res.snapshots);
      showToast(`Restored settings snapshot '${snap.SnapshotId}'`);
      onClose();
    } catch (err) {
      showToast(`Restore failed: ${err instanceof Error ? err.message : 'Error'}`);
    } finally {
      setRestoringId('');
    }
  };

  return (
    <ModalDialog
      open={open}
      onClose={onClose}
      title="↺ Rolling Settings History (20 Automatic Snapshots)"
      size="normal"
      closedBy="any"
      footer={
        <button type="button" className="hk-btn" onClick={onClose}>
          Close
        </button>
      }
    >
      <p style={{ marginTop: 0, color: 'var(--text-secondary)' }}>
        Housekeeping automatically snapshots <code>settings.json</code> whenever edits change your
        configuration (deduplicated by SHA-256, keeping the 20 most recent versions).
      </p>

      <div className="table-wrapper" tabIndex={0} role="region" aria-label="Configuration snapshots">
        <table className="hk-table">
          <thead>
            <tr>
              <th className="hk-has-hint" title={COMMON_UI_HINTS.snapshotCol('Snapshot Timestamp')}>Timestamp</th>
              <th className="hk-has-hint" title={COMMON_UI_HINTS.snapshotCol('Defined Job Count')}>Jobs</th>
              <th className="hk-has-hint" title={COMMON_UI_HINTS.snapshotCol('Named Queue Count')}>Queues</th>
              <th className="hk-has-hint" title={COMMON_UI_HINTS.snapshotCol('Serialized File Size')}>Size</th>
              <th className="hk-has-hint" title={COMMON_UI_HINTS.snapshotCol('Rollback Action')}>Action</th>
            </tr>
          </thead>
          <tbody>
            {loading && (
              <tr>
                <td colSpan={5}>Loading snapshots...</td>
              </tr>
            )}
            {!loading && snapshots.length === 0 && (
              <tr>
                <td colSpan={5} style={{ color: 'var(--text-secondary)' }}>
                  No prior snapshots recorded yet. Make any configuration edit to record snapshots automatically.
                </td>
              </tr>
            )}
            {!loading &&
              snapshots.map((s) => (
                <tr key={s.SnapshotId}>
                  <td>{new Date(s.CreatedAtUtc).toLocaleString()}</td>
                  <td>{s.DefinedJobCount}</td>
                  <td>{s.QueueCount}</td>
                  <td>{(s.SizeBytes / 1024).toFixed(1)} KB</td>
                  <td>
                    <button
                      type="button"
                      className="hk-btn sm primary"
                      disabled={restoringId === s.SnapshotId}
                      onClick={() => void handleRestore(s)}
                    >
                      {restoringId === s.SnapshotId ? 'Restoring...' : '↺ Restore'}
                    </button>
                  </td>
                </tr>
              ))}
          </tbody>
        </table>
      </div>
    </ModalDialog>
  );
};
