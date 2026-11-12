// @module: Housekeeping.Web/pages/editors/MediaTranscoderEditor
// @purpose: Module 1 configuration editor using SourceQualityTierMatrix, ProfileSelect dropdowns, and Standard 09 2-line hover hints.
// @depends-on: react, ../../components/HybridPathPicker, ../../components/ProfileSelect, ../../context/AppStateContext, ../../hints/mediaTranscoder, ./transcoder/SourceQualityTierMatrix
// @exports: MediaTranscoderEditor
import React from 'react';
import { HybridPathPicker } from '../../components/HybridPathPicker';
import { ProfileSelect } from '../../components/ProfileSelect';
import { useAppState } from '../../context/AppStateContext';
import { MEDIA_TRANSCODER_HINTS } from '../../hints/mediaTranscoder';
import type { MediaTranscoderConfig, SourceDetectionMode } from '../../types/models';
import { SourceQualityTierMatrix, TIER_RES_KEYS } from './transcoder/SourceQualityTierMatrix';

interface Props {
  cfg: MediaTranscoderConfig;
  onChange: (mutator: (d: MediaTranscoderConfig) => void) => void;
}

export const MediaTranscoderEditor: React.FC<Props> = ({ cfg, onChange }) => {
  const { config } = useAppState();
  const profileOptions = [
    '(Disabled)',
    ...(config?.HandBrakeProfiles || []).map((p) => p.ProfileId),
  ];

  return (
    <>
      <div className="hk-info-banner">
        <strong>📺 What This Module Does: Multi-Tier Media Transcoder</strong>
        <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 2 }}>
          Scans media drives and custom folders for master video files, generating optimized
          multi-resolution HEVC streaming tiers (144p–4K) and optional alternate-language editions
          with atomic <code>.partial</code> writes, safe exclusive SMB pre-probes, and Pending Swaps.
        </div>
      </div>

      <div className="hk-card">
        <h3 className="hk-card-title">⚙ Detection, Drive Scan &amp; Global Tier Switches</h3>
        <div className="hk-grid-4" style={{ marginTop: 10, marginBottom: 10 }}>
          <div className="hk-field">
            <label className="hk-field-label">Source Detection Mode</label>
            <select
              className="hk-select"
              value={cfg.DetectionMode}
              onChange={(e) =>
                onChange((d) => (d.DetectionMode = e.target.value as SourceDetectionMode))
              }
            >
              <option value="FilenameTagMatching">Filename Tag Matching (DVD / BR / UHD)</option>
              <option value="MediaInfoResolutionScan">MediaInfo.dll Stream Resolution Scan</option>
            </select>
          </div>
          <div className="hk-field">
            <label className="hk-field-label">Master File Tag</label>
            <input
              type="text"
              className="hk-input mono"
              value={cfg.MasterFileTag}
              onChange={(e) => onChange((d) => (d.MasterFileTag = e.target.value))}
              placeholder="(Original)"
            />
          </div>
          <div className="hk-field">
            <label className="hk-field-label">Drives to Scan (CSV, e.g. V, W, X, Y, Z)</label>
            <input
              type="text"
              className="hk-input mono"
              value={(cfg.DrivesToScan || []).join(', ')}
              onChange={(e) =>
                onChange((d) => {
                  d.DrivesToScan = e.target.value
                    .split(',')
                    .map((s) => s.trim().replace(/:$/, ''))
                    .filter(Boolean);
                })
              }
              placeholder="V, W, X, Y, Z"
            />
          </div>
          <div className="hk-field">
            <label className="hk-field-label">Drive Relative Subfolder</label>
            <input
              type="text"
              className="hk-input mono"
              value={cfg.DriveSubfolderRelativePath}
              onChange={(e) => onChange((d) => (d.DriveSubfolderRelativePath = e.target.value))}
              placeholder="Media"
            />
          </div>
        </div>

        <div className="hk-row" style={{ justifyContent: 'space-between', marginBottom: 10 }}>
          <div className="hk-row" style={{ gap: 16 }}>
            <label
              className="hk-checkbox-label hk-has-hint"
              title={MEDIA_TRANSCODER_HINTS.reEncodeNonHevc}
            >
              <input
                type="checkbox"
                className="hk-has-hint"
                title={MEDIA_TRANSCODER_HINTS.reEncodeNonHevc}
                checked={cfg.ReEncodeNonHevcOutputs}
                onChange={(e) => onChange((d) => (d.ReEncodeNonHevcOutputs = e.target.checked))}
              />
              Re-Encode Existing Non-HEVC Outputs
            </label>
            <label
              className="hk-checkbox-label hk-has-hint"
              title={MEDIA_TRANSCODER_HINTS.remoteSkipMediaInfo}
            >
              <input
                type="checkbox"
                className="hk-has-hint"
                title={MEDIA_TRANSCODER_HINTS.remoteSkipMediaInfo}
                checked={cfg.RemoteModeSkipMediaInfo}
                onChange={(e) => onChange((d) => (d.RemoteModeSkipMediaInfo = e.target.checked))}
              />
              Fast Network Mode (Skip MediaInfo Codec Probe on Existing Files)
            </label>
          </div>

          <div className="hk-row">
            <span className="hk-field-label">Extensions:</span>
            <input
              type="text"
              className="hk-input mono"
              style={{ width: 170 }}
              value={(cfg.Extensions || []).join(', ')}
              onChange={(e) =>
                onChange((d) => {
                  d.Extensions = e.target.value
                    .split(',')
                    .map((s) => s.trim())
                    .filter(Boolean);
                })
              }
            />
          </div>
        </div>

        <div className="hk-row" style={{ gap: 14, paddingTop: 6, borderTop: '1px solid var(--border-subtle)' }}>
          <strong style={{ fontSize: 12 }}>Active Output Tiers:</strong>
          {TIER_RES_KEYS.map((t) => (
            <label
              key={t.flag}
              className="hk-checkbox-label hk-has-hint"
              title={MEDIA_TRANSCODER_HINTS.tierToggle(t.label)}
            >
              <input
                type="checkbox"
                className="hk-has-hint"
                title={MEDIA_TRANSCODER_HINTS.tierToggle(t.label)}
                checked={Boolean(cfg[t.flag])}
                onChange={(e) =>
                  onChange((d) => {
                    d[t.flag] = e.target.checked;
                  })
                }
              />
              {t.label}
            </label>
          ))}
        </div>
      </div>

      <SourceQualityTierMatrix cfg={cfg} profileOptions={profileOptions} onChange={onChange} />

      <div className="hk-grid-2">
        <div className="hk-card">
          <div className="hk-card-header">
            <h3 className="hk-card-title">📁 Custom Scan Folders</h3>
            <button
              type="button"
              className="hk-btn sm primary"
              onClick={() => onChange((d) => d.CustomFoldersToScan.push({ Enabled: true, Path: '' }))}
            >
              + Add Folder
            </button>
          </div>
          <div className="table-wrapper" tabIndex={0} role="region" aria-label="Custom scan folders">
            <table className="hk-table">
              <thead>
                <tr>
                  <th className="hk-has-hint" style={{ width: 50 }} title={MEDIA_TRANSCODER_HINTS.thTierOn}>
                    On
                  </th>
                  <th className="hk-has-hint" title={MEDIA_TRANSCODER_HINTS.thCustomPath}>
                    Path
                  </th>
                  <th className="hk-has-hint" style={{ width: 60 }} title={MEDIA_TRANSCODER_HINTS.thDelete}>
                    Del
                  </th>
                </tr>
              </thead>
              <tbody>
                {(cfg.CustomFoldersToScan || []).length === 0 && (
                  <tr>
                    <td colSpan={3} style={{ color: 'var(--text-secondary)' }}>
                      Optional extra directories beyond drive scan roots.
                    </td>
                  </tr>
                )}
                {(cfg.CustomFoldersToScan || []).map((f, idx) => (
                  <tr key={idx}>
                    <td>
                      <input
                        type="checkbox"
                        className="hk-has-hint"
                        title={MEDIA_TRANSCODER_HINTS.customFolderEnabled}
                        checked={f.Enabled}
                        onChange={(e) =>
                          onChange((d) => {
                            d.CustomFoldersToScan[idx].Enabled = e.target.checked;
                          })
                        }
                      />
                    </td>
                    <td>
                      <HybridPathPicker
                        value={f.Path}
                        compactButton
                        onChange={(next) =>
                          onChange((d) => {
                            d.CustomFoldersToScan[idx].Path = next;
                          })
                        }
                      />
                    </td>
                    <td>
                      <button
                        type="button"
                        className="hk-btn sm danger"
                        onClick={() => onChange((d) => d.CustomFoldersToScan.splice(idx, 1))}
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

        <div className="hk-card">
          <div className="hk-card-header">
            <h3 className="hk-card-title">🌐 Alternate Language Edition Rules</h3>
            <button
              type="button"
              className="hk-btn sm primary"
              onClick={() =>
                onChange((d) => {
                  d.LanguageEditionRules.push({
                    Enabled: true,
                    LanguageCode: 'JPN',
                    LanguageFolderPrefix: 'Japanese',
                    TargetResolutionTag: '480p',
                    HandBrakeProfileId: 'Lang_Edition_480p',
                  });
                })
              }
            >
              + Add Edition Rule
            </button>
          </div>
          <div className="table-wrapper" tabIndex={0} role="region" aria-label="Language edition rules">
            <table className="hk-table">
              <thead>
                <tr>
                  <th className="hk-has-hint" title={MEDIA_TRANSCODER_HINTS.thTierOn}>On</th>
                  <th className="hk-has-hint" title={MEDIA_TRANSCODER_HINTS.thIsoLang}>ISO Lang</th>
                  <th className="hk-has-hint" title={MEDIA_TRANSCODER_HINTS.thLangPrefix}>Folder Prefix</th>
                  <th className="hk-has-hint" title={MEDIA_TRANSCODER_HINTS.thResTag}>Res Tag</th>
                  <th className="hk-has-hint" title={MEDIA_TRANSCODER_HINTS.thLangProfile}>Profile</th>
                  <th className="hk-has-hint" title={MEDIA_TRANSCODER_HINTS.thDelete}>Del</th>
                </tr>
              </thead>
              <tbody>
                {(cfg.LanguageEditionRules || []).map((r, idx) => (
                  <tr key={idx}>
                    <td>
                      <input
                        type="checkbox"
                        className="hk-has-hint"
                        title={MEDIA_TRANSCODER_HINTS.editionRuleEnabled}
                        checked={r.Enabled}
                        onChange={(e) =>
                          onChange((d) => {
                            d.LanguageEditionRules[idx].Enabled = e.target.checked;
                          })
                        }
                      />
                    </td>
                    <td>
                      <input
                        type="text"
                        className="hk-input mono"
                        style={{ width: 68 }}
                        value={r.LanguageCode}
                        onChange={(e) =>
                          onChange((d) => {
                            d.LanguageEditionRules[idx].LanguageCode = e.target.value;
                          })
                        }
                      />
                    </td>
                    <td>
                      <input
                        type="text"
                        className="hk-input"
                        value={r.LanguageFolderPrefix}
                        onChange={(e) =>
                          onChange((d) => {
                            d.LanguageEditionRules[idx].LanguageFolderPrefix = e.target.value;
                          })
                        }
                      />
                    </td>
                    <td>
                      <input
                        type="text"
                        className="hk-input mono"
                        style={{ width: 72 }}
                        value={r.TargetResolutionTag}
                        onChange={(e) =>
                          onChange((d) => {
                            d.LanguageEditionRules[idx].TargetResolutionTag = e.target.value;
                          })
                        }
                      />
                    </td>
                    <td>
                      <ProfileSelect
                        value={r.HandBrakeProfileId}
                        options={profileOptions}
                        ariaLabel={`${r.LanguageCode} HandBrake profile`}
                        onChange={(v) =>
                          onChange((d) => {
                            d.LanguageEditionRules[idx].HandBrakeProfileId = v;
                          })
                        }
                      />
                    </td>
                    <td>
                      <button
                        type="button"
                        className="hk-btn sm danger"
                        onClick={() => onChange((d) => d.LanguageEditionRules.splice(idx, 1))}
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
      </div>
    </>
  );
};
