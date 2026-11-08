import React, { useState } from 'react';
import { HybridPathPicker } from '../../components/HybridPathPicker';
import type { HumbleSyncConfig } from '../../types/models';

interface Props {
  cfg: HumbleSyncConfig;
  onChange: (mutator: (d: HumbleSyncConfig) => void) => void;
}

export const HumbleSyncEditor: React.FC<Props> = ({ cfg, onChange }) => {
  const [revealCookie, setRevealCookie] = useState(false);

  return (
    <>
      <div className="hk-info-banner">
        <strong>📚 What This Module Does: Humble Bundle Library Sync</strong>
        <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 2 }}>
          Automatically downloads and incrementally updates purchased Humble Bundle eBooks,
          audiobooks, comics, and DRM-free games via <code>humble-bundle-downloader (hbd)</code>.
        </div>
      </div>

      <div className="hk-card">
        <h3 className="hk-card-title">🔐 Authentication &amp; Library Destination</h3>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 12, marginTop: 10 }}>
          <div className="hk-field">
            <label className="hk-field-label">
              Humble Session Cookie (<code>_simpleauth_sess</code>)
            </label>
            <div className="hk-row">
              <input
                type={revealCookie ? 'text' : 'password'}
                className="hk-input mono"
                style={{ flex: 1 }}
                value={cfg.SessionCookie}
                onChange={(e) => onChange((d) => (d.SessionCookie = e.target.value))}
                placeholder="Paste _simpleauth_sess cookie value from browser DevTools..."
              />
              <button
                type="button"
                className="hk-btn sm"
                onClick={() => setRevealCookie((prev) => !prev)}
              >
                {revealCookie ? '🙈 Hide' : '👁 Reveal'}
              </button>
            </div>
          </div>

          <div className="hk-grid-2">
            <div className="hk-field">
              <label className="hk-field-label">Local Library Download Folder</label>
              <HybridPathPicker
                value={cfg.LibraryPath}
                onChange={(next) => onChange((d) => (d.LibraryPath = next))}
              />
            </div>

            <div className="hk-field">
              <label className="hk-field-label">Additional CLI Flags</label>
              <input
                type="text"
                className="hk-input mono"
                value={cfg.AdditionalArgs}
                onChange={(e) => onChange((d) => (d.AdditionalArgs = e.target.value))}
                placeholder="--progress"
              />
            </div>
          </div>
        </div>
      </div>
    </>
  );
};
