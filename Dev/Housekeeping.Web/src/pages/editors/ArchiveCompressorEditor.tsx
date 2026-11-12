// @module: Housekeeping.Web/pages/editors/ArchiveCompressorEditor
// @purpose: Module 3 (Video Archive Compressor) editor with ProfileSelect dropdown and 2-line hover hints.
// @depends-on: react, ../../components/HybridPathPicker, ../../components/ProfileSelect, ../../context/AppStateContext, ../../hints/archiveCompressor, ../../types/models
// @exports: ArchiveCompressorEditor
import React from 'react';
import { HybridPathPicker } from '../../components/HybridPathPicker';
import { ProfileSelect } from '../../components/ProfileSelect';
import { useAppState } from '../../context/AppStateContext';
import { ARCHIVE_COMPRESSOR_HINTS } from '../../hints/archiveCompressor';
import type { ArchiveCompressorConfig } from '../../types/models';

interface Props {
  cfg: ArchiveCompressorConfig;
  onChange: (mutator: (d: ArchiveCompressorConfig) => void) => void;
}

export const ArchiveCompressorEditor: React.FC<Props> = ({ cfg, onChange }) => {
  const { config } = useAppState();
  const profileIds = (config?.HandBrakeProfiles || []).map((p) => p.ProfileId);

  return (
    <>
      <div className="hk-info-banner">
        <strong>📦 What This Module Does: Video Archive Compressor</strong>
        <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 2 }}>
          Compresses completed working video directories into space-efficient long-term HEVC/AV1
          archives while preserving subfolder hierarchy and honoring StreamEngine <code>.hklock</code> locks.
        </div>
      </div>

      <div className="hk-card">
        <h3 className="hk-card-title">⚙ Compression Profile &amp; Filter Rules</h3>
        <div className="hk-grid-4" style={{ marginTop: 10 }}>
          <div className="hk-field">
            <label className="hk-field-label">HandBrake Profile</label>
            <ProfileSelect
              value={cfg.HandBrakeProfileId}
              options={profileIds}
              ariaLabel="Archive Compressor HandBrake profile"
              onChange={(v) => onChange((d) => (d.HandBrakeProfileId = v))}
            />
          </div>
          <div className="hk-field">
            <label className="hk-field-label">Output Container</label>
            <select
              className="hk-select"
              value={cfg.OutputExtension}
              onChange={(e) => onChange((d) => (d.OutputExtension = e.target.value))}
            >
              <option value=".mkv">.mkv (Matroska)</option>
              <option value=".mp4">.mp4 (MPEG-4)</option>
            </select>
          </div>
          <div className="hk-field">
            <label className="hk-field-label">Skip Files Containing Tag</label>
            <input
              type="text"
              className="hk-input mono"
              value={cfg.SkipFilesContainingTag}
              onChange={(e) => onChange((d) => (d.SkipFilesContainingTag = e.target.value))}
              placeholder="_proxy)"
            />
          </div>
          <div className="hk-field">
            <label className="hk-field-label">Input Extensions (CSV)</label>
            <input
              type="text"
              className="hk-input mono"
              value={(cfg.InputExtensions || []).join(', ')}
              onChange={(e) =>
                onChange((d) => {
                  d.InputExtensions = e.target.value
                    .split(',')
                    .map((s) => s.trim())
                    .filter(Boolean);
                })
              }
            />
          </div>
        </div>
      </div>

      <div className="hk-card">
        <div className="hk-card-header">
          <div>
            <h3 className="hk-card-title">🗂 Source → Archive Folder Mappings</h3>
            <p className="hk-card-subtitle">
              Map active capture/working directories to long-term archive destinations.
            </p>
          </div>
          <button
            type="button"
            className="hk-btn sm primary"
            onClick={() =>
              onChange((d) => {
                d.Mappings.push({
                  Enabled: true,
                  Name: `Archive Pair ${d.Mappings.length + 1}`,
                  SourcePath: '',
                  ArchiveOutputPath: '',
                });
              })
            }
          >
            + Add Mapping
          </button>
        </div>

        <div className="table-wrapper" tabIndex={0} role="region" aria-label="Archive folder mappings">
          <table className="hk-table stack-mobile">
            <thead>
              <tr>
                <th className="hk-has-hint" style={{ width: 65 }} title={ARCHIVE_COMPRESSOR_HINTS.thEnabled}>
                  Enabled
                </th>
                <th className="hk-has-hint" style={{ width: 170 }} title={ARCHIVE_COMPRESSOR_HINTS.thLabel}>
                  Label
                </th>
                <th className="hk-has-hint" title={ARCHIVE_COMPRESSOR_HINTS.thSourceFolder}>
                  Source Folder
                </th>
                <th className="hk-has-hint" title={ARCHIVE_COMPRESSOR_HINTS.thDestFolder}>
                  Archive Destination Folder
                </th>
                <th className="hk-has-hint" style={{ width: 85 }} title={ARCHIVE_COMPRESSOR_HINTS.thAction}>
                  Action
                </th>
              </tr>
            </thead>
            <tbody>
              {(cfg.Mappings || []).length === 0 && (
                <tr>
                  <td colSpan={5} style={{ color: 'var(--text-secondary)' }}>
                    No archive mappings configured yet.
                  </td>
                </tr>
              )}
              {(cfg.Mappings || []).map((m, idx) => (
                <tr key={idx}>
                  <td data-label="Enabled">
                    <input
                      type="checkbox"
                      className="hk-has-hint"
                      title={ARCHIVE_COMPRESSOR_HINTS.mappingEnabled}
                      checked={m.Enabled}
                      onChange={(e) =>
                        onChange((d) => {
                          d.Mappings[idx].Enabled = e.target.checked;
                        })
                      }
                    />
                  </td>
                  <td data-label="Label">
                    <input
                      type="text"
                      className="hk-input"
                      value={m.Name}
                      onChange={(e) =>
                        onChange((d) => {
                          d.Mappings[idx].Name = e.target.value;
                        })
                      }
                    />
                  </td>
                  <td data-label="Source Folder">
                    <HybridPathPicker
                      value={m.SourcePath}
                      compactButton
                      onChange={(next) =>
                        onChange((d) => {
                          d.Mappings[idx].SourcePath = next;
                        })
                      }
                    />
                  </td>
                  <td data-label="Archive Destination">
                    <HybridPathPicker
                      value={m.ArchiveOutputPath}
                      compactButton
                      onChange={(next) =>
                        onChange((d) => {
                          d.Mappings[idx].ArchiveOutputPath = next;
                        })
                      }
                    />
                  </td>
                  <td data-label="Action">
                    <button
                      type="button"
                      className="hk-btn sm danger"
                      onClick={() => onChange((d) => d.Mappings.splice(idx, 1))}
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
