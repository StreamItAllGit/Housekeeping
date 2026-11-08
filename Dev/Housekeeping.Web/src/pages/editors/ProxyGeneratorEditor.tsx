import React from 'react';
import { ComboSelect } from '../../components/ComboSelect';
import { HybridPathPicker } from '../../components/HybridPathPicker';
import { useAppState } from '../../context/AppStateContext';
import type { ProxyGeneratorConfig } from '../../types/models';

interface Props {
  cfg: ProxyGeneratorConfig;
  onChange: (mutator: (d: ProxyGeneratorConfig) => void) => void;
}

export const ProxyGeneratorEditor: React.FC<Props> = ({ cfg, onChange }) => {
  const { config } = useAppState();
  const profileIds = (config?.HandBrakeProfiles || []).map((p) => p.ProfileId);

  return (
    <>
      <div className="hk-info-banner">
        <strong>🎬 What This Module Does: Proxy Media Generator</strong>
        <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 2 }}>
          Scans working footage folders and generates fast, low-bitrate editing proxies right beside
          source files (for example <code>Clip01 (360p_proxy).mp4</code>) while automatically skipping
          existing proxies.
        </div>
      </div>

      <div className="hk-card">
        <h3 className="hk-card-title">⚙ Encoding &amp; Naming Settings</h3>
        <div className="hk-grid-3" style={{ marginTop: 10 }}>
          <div className="hk-field">
            <label className="hk-field-label">HandBrake Encoding Profile</label>
            <ComboSelect
              value={cfg.HandBrakeProfileId}
              options={profileIds}
              onChange={(v) => onChange((d) => (d.HandBrakeProfileId = v))}
              placeholder="Proxy_360p"
            />
          </div>
          <div className="hk-field">
            <label className="hk-field-label">Proxy Filename Suffix Tag</label>
            <input
              type="text"
              className="hk-input mono"
              value={cfg.ProxySuffixTag}
              onChange={(e) => onChange((d) => (d.ProxySuffixTag = e.target.value))}
              placeholder="360p_proxy"
            />
          </div>
          <div className="hk-field">
            <label className="hk-field-label">Source Extensions (CSV)</label>
            <input
              type="text"
              className="hk-input mono"
              value={(cfg.Extensions || []).join(', ')}
              onChange={(e) =>
                onChange((d) => {
                  d.Extensions = e.target.value
                    .split(',')
                    .map((s) => s.trim())
                    .filter(Boolean);
                })
              }
              placeholder=".mp4, .mkv, .mov"
            />
          </div>
        </div>
      </div>

      <div className="hk-card">
        <div className="hk-card-header">
          <div>
            <h3 className="hk-card-title">📁 Target Footage Folders</h3>
            <p className="hk-card-subtitle">
              Directories scanned recursively for raw master video clips.
            </p>
          </div>
          <button
            type="button"
            className="hk-btn sm primary"
            onClick={() =>
              onChange((d) => {
                d.Folders.push({ Enabled: true, Path: '' });
              })
            }
          >
            + Add Folder
          </button>
        </div>

        <div className="table-wrapper" tabIndex={0} role="region" aria-label="Proxy target folders">
          <table className="hk-table stack-mobile">
            <thead>
              <tr>
                <th style={{ width: 70 }}>Enabled</th>
                <th>Folder Path (Local or UNC)</th>
                <th style={{ width: 90 }}>Action</th>
              </tr>
            </thead>
            <tbody>
              {(cfg.Folders || []).length === 0 && (
                <tr>
                  <td colSpan={3} style={{ color: 'var(--text-secondary)' }}>
                    No target folders added yet. Click &ldquo;+ Add Folder&rdquo; to begin.
                  </td>
                </tr>
              )}
              {(cfg.Folders || []).map((row, idx) => (
                <tr key={idx}>
                  <td data-label="Enabled">
                    <input
                      type="checkbox"
                      checked={row.Enabled}
                      onChange={(e) =>
                        onChange((d) => {
                          d.Folders[idx].Enabled = e.target.checked;
                        })
                      }
                      aria-label={`Enable folder ${idx + 1}`}
                    />
                  </td>
                  <td data-label="Folder Path">
                    <HybridPathPicker
                      value={row.Path}
                      onChange={(next) =>
                        onChange((d) => {
                          d.Folders[idx].Path = next;
                        })
                      }
                    />
                  </td>
                  <td data-label="Action">
                    <button
                      type="button"
                      className="hk-btn sm danger"
                      onClick={() =>
                        onChange((d) => {
                          d.Folders.splice(idx, 1);
                        })
                      }
                    >
                      Remove
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
