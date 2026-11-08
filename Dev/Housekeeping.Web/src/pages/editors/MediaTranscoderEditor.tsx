import React from 'react';
import { ComboSelect } from '../../components/ComboSelect';
import { HybridPathPicker } from '../../components/HybridPathPicker';
import { useAppState } from '../../context/AppStateContext';
import type { MediaTranscoderConfig, SourceDetectionMode } from '../../types/models';

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

  const tierResKeys: {
    flag: keyof Pick<
      MediaTranscoderConfig,
      'Enable144p' | 'Enable240p' | 'Enable360p' | 'Enable480p' | 'Enable720p' | 'Enable1080p' | 'Enable4K'
    >;
    label: string;
    prop:
      | 'Profile144p'
      | 'Profile240p'
      | 'Profile360p'
      | 'Profile480p'
      | 'Profile720p'
      | 'Profile1080p'
      | 'Profile4K';
  }[] = [
    { flag: 'Enable144p', label: '144p', prop: 'Profile144p' },
    { flag: 'Enable240p', label: '240p', prop: 'Profile240p' },
    { flag: 'Enable360p', label: '360p', prop: 'Profile360p' },
    { flag: 'Enable480p', label: '480p', prop: 'Profile480p' },
    { flag: 'Enable720p', label: '720p', prop: 'Profile720p' },
    { flag: 'Enable1080p', label: '1080p', prop: 'Profile1080p' },
    { flag: 'Enable4K', label: '4K UHD', prop: 'Profile4K' },
  ];

  return (
    <>
      <div className="hk-info-banner">
        <strong>📺 What This Module Does: Multi-Tier Media Transcoder</strong>
        <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 2 }}>
          Scans media drives and custom folders for master video files, generating optimized
          multi-resolution HEVC streaming tiers (144p–4K) and optional alternate-language editions
          with atomic <code>.partial</code> writes and Pending Swaps.
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
            <label className="hk-checkbox-label">
              <input
                type="checkbox"
                checked={cfg.ReEncodeNonHevcOutputs}
                onChange={(e) => onChange((d) => (d.ReEncodeNonHevcOutputs = e.target.checked))}
              />
              Re-Encode Existing Non-HEVC Outputs
            </label>
            <label className="hk-checkbox-label">
              <input
                type="checkbox"
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
          {tierResKeys.map((t) => (
            <label key={t.flag} className="hk-checkbox-label">
              <input
                type="checkbox"
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

      {/* Source Quality Tiers Matrix */}
      <div className="hk-card">
        <div className="hk-card-header">
          <div>
            <h3 className="hk-card-title">📊 Source Quality Tier → Output Profile Matrix</h3>
            <p className="hk-card-subtitle">
              Map each source quality tier (DVD, Blu-Ray, 4K UHD) to HandBrake profiles per target resolution.
            </p>
          </div>
          <button
            type="button"
            className="hk-btn sm primary"
            onClick={() =>
              onChange((d) => {
                d.SourceTiers.push({
                  Enabled: true,
                  TierName: 'Custom Tier',
                  SourceTagsCsv: 'WEB',
                  MaxSourceHeightPx: 1080,
                  Profile144p: 'Tier_144p',
                  Profile240p: 'Tier_240p',
                  Profile360p: 'Tier_360p',
                  Profile480p: 'Tier_480p',
                  Profile720p: 'Tier_720p',
                  Profile1080p: 'Tier_1080p',
                  Profile4K: '(Disabled)',
                });
              })
            }
          >
            + Add Source Tier
          </button>
        </div>

        <div className="table-wrapper" tabIndex={0} role="region" aria-label="Source quality tiers">
          <table className="hk-table">
            <thead>
              <tr>
                <th>On</th>
                <th>Tier Name</th>
                <th>Filename Tags</th>
                <th>Max H (px)</th>
                {tierResKeys.map((t) => (
                  <th key={t.label}>{t.label}</th>
                ))}
                <th>Del</th>
              </tr>
            </thead>
            <tbody>
              {(cfg.SourceTiers || []).map((tier, idx) => (
                <tr key={idx}>
                  <td>
                    <input
                      type="checkbox"
                      checked={tier.Enabled}
                      onChange={(e) =>
                        onChange((d) => {
                          d.SourceTiers[idx].Enabled = e.target.checked;
                        })
                      }
                    />
                  </td>
                  <td>
                    <input
                      type="text"
                      className="hk-input"
                      style={{ minWidth: 130 }}
                      value={tier.TierName}
                      onChange={(e) =>
                        onChange((d) => {
                          d.SourceTiers[idx].TierName = e.target.value;
                        })
                      }
                    />
                  </td>
                  <td>
                    <input
                      type="text"
                      className="hk-input mono"
                      style={{ minWidth: 120 }}
                      value={tier.SourceTagsCsv}
                      onChange={(e) =>
                        onChange((d) => {
                          d.SourceTiers[idx].SourceTagsCsv = e.target.value;
                        })
                      }
                    />
                  </td>
                  <td>
                    <input
                      type="number"
                      className="hk-input"
                      style={{ width: 82 }}
                      value={tier.MaxSourceHeightPx}
                      onChange={(e) =>
                        onChange((d) => {
                          d.SourceTiers[idx].MaxSourceHeightPx = Number(e.target.value) || 1080;
                        })
                      }
                    />
                  </td>
                  {tierResKeys.map((t) => (
                    <td key={t.prop} style={{ minWidth: 138 }}>
                      <ComboSelect
                        value={tier[t.prop]}
                        options={profileOptions}
                        onChange={(v) =>
                          onChange((d) => {
                            d.SourceTiers[idx][t.prop] = v;
                          })
                        }
                      />
                    </td>
                  ))}
                  <td>
                    <button
                      type="button"
                      className="hk-btn sm danger"
                      onClick={() =>
                        onChange((d) => {
                          d.SourceTiers.splice(idx, 1);
                        })
                      }
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

      {/* Custom Folders + Language Edition Rules */}
      <div className="hk-grid-2">
        <div className="hk-card">
          <div className="hk-card-header">
            <h3 className="hk-card-title">📁 Custom Scan Folders</h3>
            <button
              type="button"
              className="hk-btn sm primary"
              onClick={() =>
                onChange((d) => {
                  d.CustomFoldersToScan.push({ Enabled: true, Path: '' });
                })
              }
            >
              + Add Folder
            </button>
          </div>
          <div className="table-wrapper" tabIndex={0} role="region" aria-label="Custom scan folders">
            <table className="hk-table">
              <thead>
                <tr>
                  <th style={{ width: 50 }}>On</th>
                  <th>Path</th>
                  <th style={{ width: 60 }}>Del</th>
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
                        onClick={() =>
                          onChange((d) => {
                            d.CustomFoldersToScan.splice(idx, 1);
                          })
                        }
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
                  <th>On</th>
                  <th>ISO Lang</th>
                  <th>Folder Prefix</th>
                  <th>Res Tag</th>
                  <th>Profile</th>
                  <th>Del</th>
                </tr>
              </thead>
              <tbody>
                {(cfg.LanguageEditionRules || []).map((r, idx) => (
                  <tr key={idx}>
                    <td>
                      <input
                        type="checkbox"
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
                      <ComboSelect
                        value={r.HandBrakeProfileId}
                        options={profileOptions}
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
                        onClick={() =>
                          onChange((d) => {
                            d.LanguageEditionRules.splice(idx, 1);
                          })
                        }
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
