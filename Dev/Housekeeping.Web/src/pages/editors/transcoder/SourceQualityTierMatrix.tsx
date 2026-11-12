// @module: Housekeeping.Web/pages/editors/transcoder/SourceQualityTierMatrix
// @purpose: Extracted Source Quality Tier -> HandBrake Profile matrix using ProfileSelect dropdowns and 2-line column hints.
// @depends-on: react, ../../../components/ProfileSelect, ../../../hints/mediaTranscoder, ../../../types/models
// @exports: SourceQualityTierMatrix, TIER_RES_KEYS
import React from 'react';
import { ProfileSelect } from '../../../components/ProfileSelect';
import { MEDIA_TRANSCODER_HINTS } from '../../../hints/mediaTranscoder';
import type { MediaTranscoderConfig } from '../../../types/models';

export const TIER_RES_KEYS: Array<{
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
}> = [
  { flag: 'Enable144p', label: '144p', prop: 'Profile144p' },
  { flag: 'Enable240p', label: '240p', prop: 'Profile240p' },
  { flag: 'Enable360p', label: '360p', prop: 'Profile360p' },
  { flag: 'Enable480p', label: '480p', prop: 'Profile480p' },
  { flag: 'Enable720p', label: '720p', prop: 'Profile720p' },
  { flag: 'Enable1080p', label: '1080p', prop: 'Profile1080p' },
  { flag: 'Enable4K', label: '4K UHD', prop: 'Profile4K' },
];

interface SourceQualityTierMatrixProps {
  cfg: MediaTranscoderConfig;
  profileOptions: string[];
  onChange: (mutator: (d: MediaTranscoderConfig) => void) => void;
}

export const SourceQualityTierMatrix: React.FC<SourceQualityTierMatrixProps> = ({
  cfg,
  profileOptions,
  onChange,
}) => {
  return (
    <div className="hk-card">
      <div className="hk-card-header">
        <div>
          <h3 className="hk-card-title">📊 Source Quality Tier → Output Profile Matrix</h3>
          <p className="hk-card-subtitle">
            Map each source quality tier (DVD, Blu-Ray, 4K UHD) to HandBrake profiles per target
            resolution.
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
              <th className="hk-has-hint" title={MEDIA_TRANSCODER_HINTS.thTierOn}>
                On
              </th>
              <th className="hk-has-hint" title={MEDIA_TRANSCODER_HINTS.thTierName}>
                Tier Name
              </th>
              <th className="hk-has-hint" title={MEDIA_TRANSCODER_HINTS.thSourceTags}>
                Filename Tags
              </th>
              <th className="hk-has-hint" title={MEDIA_TRANSCODER_HINTS.thMaxHeight}>
                Max H (px)
              </th>
              {TIER_RES_KEYS.map((t) => (
                <th
                  key={t.label}
                  className="hk-has-hint"
                  title={MEDIA_TRANSCODER_HINTS.thResolutionCol(t.label)}
                >
                  {t.label}
                </th>
              ))}
              <th className="hk-has-hint" title={MEDIA_TRANSCODER_HINTS.thDelete}>
                Del
              </th>
            </tr>
          </thead>
          <tbody>
            {(cfg.SourceTiers || []).map((tier, idx) => (
              <tr key={idx}>
                <td>
                  <input
                    type="checkbox"
                    className="hk-has-hint"
                    title={MEDIA_TRANSCODER_HINTS.tierRowEnabled}
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
                {TIER_RES_KEYS.map((t) => (
                  <td key={t.prop} style={{ minWidth: 138 }}>
                    <ProfileSelect
                      value={tier[t.prop]}
                      options={profileOptions}
                      ariaLabel={`${tier.TierName} ${t.label} HandBrake profile`}
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
  );
};
