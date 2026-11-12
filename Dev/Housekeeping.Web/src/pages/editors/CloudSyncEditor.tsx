// @module: Housekeeping.Web/pages/editors/CloudSyncEditor
// @purpose: Module 5 (Cloud Folder Sync via rclone) editor with CoexistenceWarningBanner and 2-line hover hints.
// @depends-on: react, ../../components/CoexistenceWarningBanner, ../../components/ComboSelect, ../../components/HybridPathPicker, ../../context/AppStateContext, ../../hints/cloudSync, ../../services/apiClient
// @exports: CloudSyncEditor
import React, { useEffect, useState } from 'react';
import { CoexistenceWarningBanner } from '../../components/CoexistenceWarningBanner';
import { ComboSelect } from '../../components/ComboSelect';
import { HybridPathPicker } from '../../components/HybridPathPicker';
import { useAppState } from '../../context/AppStateContext';
import { CLOUD_SYNC_HINTS } from '../../hints/cloudSync';
import { apiClient } from '../../services/apiClient';
import type { CloudSyncConfig } from '../../types/models';

interface Props {
  jobId: string;
  cfg: CloudSyncConfig;
  onChange: (mutator: (d: CloudSyncConfig) => void) => void;
}

export const CloudSyncEditor: React.FC<Props> = ({ jobId, cfg, onChange }) => {
  const { flushSaveNow, pipelineState, setPipelineState, showToast } = useAppState();
  const [remotes, setRemotes] = useState<string[]>([]);
  const [refreshing, setRefreshing] = useState(false);

  useEffect(() => {
    apiClient
      .getModulesCatalog()
      .then((res) => setRemotes(res.rcloneRemotes || []))
      .catch((err) => console.debug('[CloudSyncEditor] rclone catalog probe skipped:', err));
  }, []);

  const handleRefreshRemotes = async () => {
    setRefreshing(true);
    try {
      const res = await apiClient.refreshRcloneRemotes();
      setRemotes(res.rcloneRemotes || []);
      showToast(`Detected ${res.rcloneRemotes.length} configured rclone remote(s).`);
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Could not query rclone remotes.');
    } finally {
      setRefreshing(false);
    }
  };

  const handleRunResync = async () => {
    const ok = await flushSaveNow();
    if (!ok) return;
    try {
      const res = await apiClient.runJob(jobId, false, true);
      setPipelineState(res.state);
      showToast('Started Cloud Sync job with one-time --resync flag.');
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Could not start --resync run.');
    }
  };

  return (
    <>
      <div className="hk-info-banner">
        <strong>☁ What This Module Does: Cloud Folder Sync (rclone)</strong>
        <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 2 }}>
          Synchronizes local directories with cloud storage providers (Google Drive, OneDrive, S3,
          Dropbox, NAS SFTP) via <code>rclone</code> with automatic network drop recovery and
          Standard 19 <code>.hklock</code> sister-app deferral.
        </div>
      </div>

      <CoexistenceWarningBanner jobId={jobId} moduleType="CloudSync" />

      <div className="hk-card">
        <div className="hk-card-header">
          <div>
            <h3 className="hk-card-title">☁ rclone Sync Folder Pairs</h3>
            <p className="hk-card-subtitle">
              Detected rclone remotes:{' '}
              {remotes.length > 0 ? remotes.join(', ') : '(click Refresh Remotes to query rclone)'}
            </p>
          </div>
          <div className="hk-row">
            <button
              type="button"
              className="hk-btn sm"
              disabled={refreshing}
              onClick={() => void handleRefreshRemotes()}
            >
              {refreshing ? 'Refreshing...' : '🔄 Refresh rclone Remotes'}
            </button>
            <button
              type="button"
              className="hk-btn sm warn"
              disabled={pipelineState.IsRunning}
              onClick={() => void handleRunResync()}
              title="Run bisync once with --resync to initialize or heal bisync state"
            >
              ⚡ Run Once with --resync
            </button>
            <button
              type="button"
              className="hk-btn sm primary"
              onClick={() =>
                onChange((d) => {
                  d.Jobs.push({
                    Enabled: true,
                    JobName: `Sync Pair ${d.Jobs.length + 1}`,
                    SyncMode: 'bisync',
                    LocalPath: '',
                    RemoteTarget: remotes[0] ? `${remotes[0]}Backups` : 'gdrive:Backups',
                    Flags: '--create-empty-src-dirs --resilient --verbose',
                  });
                })
              }
            >
              + Add Sync Pair
            </button>
          </div>
        </div>

        <div className="table-wrapper" tabIndex={0} role="region" aria-label="Cloud sync folder pairs">
          <table className="hk-table stack-mobile">
            <thead>
              <tr>
                <th className="hk-has-hint" style={{ width: 55 }} title={CLOUD_SYNC_HINTS.thOn}>On</th>
                <th className="hk-has-hint" style={{ width: 150 }} title={CLOUD_SYNC_HINTS.thLabel}>Label</th>
                <th className="hk-has-hint" style={{ width: 115 }} title={CLOUD_SYNC_HINTS.thMode}>Mode</th>
                <th className="hk-has-hint" title={CLOUD_SYNC_HINTS.thLocalFolder}>Local Folder</th>
                <th className="hk-has-hint" title={CLOUD_SYNC_HINTS.thRemoteTarget}>Remote Target (remote:path)</th>
                <th className="hk-has-hint" title={CLOUD_SYNC_HINTS.thFlags}>Extra Flags</th>
                <th className="hk-has-hint" style={{ width: 70 }} title={CLOUD_SYNC_HINTS.thDelete}>Del</th>
              </tr>
            </thead>
            <tbody>
              {(cfg.Jobs || []).length === 0 && (
                <tr>
                  <td colSpan={7} style={{ color: 'var(--text-secondary)' }}>
                    No cloud sync pairs configured yet.
                  </td>
                </tr>
              )}
              {(cfg.Jobs || []).map((item, idx) => (
                <tr key={idx}>
                  <td data-label="Enabled">
                    <input
                      type="checkbox"
                      className="hk-has-hint"
                      title={CLOUD_SYNC_HINTS.pairEnabled}
                      checked={item.Enabled}
                      onChange={(e) =>
                        onChange((d) => {
                          d.Jobs[idx].Enabled = e.target.checked;
                        })
                      }
                    />
                  </td>
                  <td data-label="Label">
                    <input
                      type="text"
                      className="hk-input"
                      value={item.JobName}
                      onChange={(e) =>
                        onChange((d) => {
                          d.Jobs[idx].JobName = e.target.value;
                        })
                      }
                    />
                  </td>
                  <td data-label="Mode">
                    <select
                      className="hk-select"
                      value={item.SyncMode}
                      onChange={(e) =>
                        onChange((d) => {
                          d.Jobs[idx].SyncMode = e.target.value;
                        })
                      }
                    >
                      <option value="bisync">bisync (2-way)</option>
                      <option value="sync">sync (1-way)</option>
                      <option value="copy">copy (no delete)</option>
                    </select>
                  </td>
                  <td data-label="Local Folder">
                    <HybridPathPicker
                      value={item.LocalPath}
                      compactButton
                      onChange={(next) =>
                        onChange((d) => {
                          d.Jobs[idx].LocalPath = next;
                        })
                      }
                    />
                  </td>
                  <td data-label="Remote Target">
                    <ComboSelect
                      value={item.RemoteTarget}
                      mono
                      options={remotes.map((r) => (r.endsWith(':') ? r : `${r}:`))}
                      onChange={(v) =>
                        onChange((d) => {
                          d.Jobs[idx].RemoteTarget = v;
                        })
                      }
                      placeholder="gdrive:Saves"
                    />
                  </td>
                  <td data-label="Extra Flags">
                    <input
                      type="text"
                      className="hk-input mono"
                      value={item.Flags}
                      onChange={(e) =>
                        onChange((d) => {
                          d.Jobs[idx].Flags = e.target.value;
                        })
                      }
                    />
                  </td>
                  <td data-label="Action">
                    <button
                      type="button"
                      className="hk-btn sm danger"
                      onClick={() => onChange((d) => d.Jobs.splice(idx, 1))}
                    >
                      ✕
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </>
  );
};
