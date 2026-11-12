// @module: Housekeeping.Web/components/CoexistenceWarningBanner
// @purpose: Displays live sister-app vault coexistence status, active .hklock counts, and 1-click alignment preset actions.
// @depends-on: react, ../context/AppStateContext, ../services/apiClient, ../types/coexistenceModels
// @exports: CoexistenceWarningBanner
import React, { useCallback, useEffect, useState } from 'react';
import { useAppState } from '../context/AppStateContext';
import { apiClient } from '../services/apiClient';
import type { CoexistencePresetId, CoexistenceStatusDto } from '../types/coexistenceModels';

interface CoexistenceWarningBannerProps {
  jobId: string;
  moduleType: 'CollectionManifest' | 'CloudSync' | 'RomLauncherGenerator';
}

const PRESET_OPTIONS_BY_MODULE: Record<
  CoexistenceWarningBannerProps['moduleType'],
  Array<{ id: CoexistencePresetId; label: string; title: string }>
> = {
  CollectionManifest: [
    {
      id: 'delegate_to_shadowlibrary',
      label: '🛡 Delegate ROM Vault Auditing to ShadowLibrary',
      title:
        'Enable SkipShadowLibraryManagedRoots so ShadowLibrary exclusively owns DAT & .hash auditing on shared roots.\nSnapshots settings.json before applying so you can undo anytime.',
    },
    {
      id: 'co_audit_zero_rewrite',
      label: '🤝 Co-Audit with Zero-Rewrite Parity',
      title:
        'Align SidecarBeside placement, post-injection .archive_hashes.json sync, and zero-rewrite # Container: preservation.\nSnapshots settings.json and stamps .coexistence.json v2 on shared vaults.',
    },
  ],
  CloudSync: [
    {
      id: 'align_cloud_sync',
      label: '☁ Align Cloud Sync Exclusions & Lock Deferral',
      title:
        'Enables distributed .hklock deferral and excludes transient .partial*, .redroom-part, and .hklock artifacts.\nPrevents rclone uploads while ShadowLibrary, Redroom, or uStation are mutating a vault.',
    },
  ],
  RomLauncherGenerator: [
    {
      id: 'align_ustation_saves_and_launchers',
      label: '🎮 Align uStation Saves & Launchers',
      title:
        'Coordinates ROM launcher & save-sync behavior with uStation (:8460) and stamps .coexistence.json v2.\nSnapshots settings.json first so pre/post launch save rules stay reversible.',
    },
  ],
};

export const CoexistenceWarningBanner: React.FC<CoexistenceWarningBannerProps> = ({
  jobId,
  moduleType,
}) => {
  const { replaceConfigFromServer, flushSaveNow, showToast } = useAppState();
  const [status, setStatus] = useState<CoexistenceStatusDto | null>(null);
  const [applying, setApplying] = useState<string>('');

  const loadStatus = useCallback(async () => {
    try {
      const res = await apiClient.getCoexistenceStatus();
      setStatus(res);
    } catch (err) {
      console.debug('[CoexistenceWarningBanner] status probe skipped:', err);
    }
  }, []);

  useEffect(() => {
    void loadStatus();
  }, [loadStatus, jobId]);

  const relevantConflicts = (status?.detectedConflicts || []).filter(
    (c) => !c.jobId || c.jobId === jobId
  );
  const onlinePeers = (status?.peers || []).filter((p) => p.isOnline);
  const presets = PRESET_OPTIONS_BY_MODULE[moduleType];

  const handleApplyPreset = async (preset: CoexistencePresetId) => {
    setApplying(preset);
    try {
      await flushSaveNow();
      const res = await apiClient.applyCoexistencePreset(preset, jobId);
      replaceConfigFromServer(res.config, res.snapshotsCount);
      await loadStatus();
      showToast(res.message || `Applied coexistence preset '${preset}'.`);
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Could not apply coexistence preset.');
    } finally {
      setApplying('');
    }
  };

  const hasBannerSignal =
    relevantConflicts.length > 0 ||
    (status?.activeHkLocksCount || 0) > 0 ||
    onlinePeers.length > 0 ||
    (status?.managedRoots.length || 0) > 0;

  if (!status) return null;

  return (
    <div
      className="hk-coexistence-banner"
      style={{
        marginBottom: 'var(--gap-grid)',
        borderColor: relevantConflicts.length > 0 ? 'var(--warn)' : 'var(--border-strong)',
      }}
    >
      <div className="hk-row" style={{ justifyContent: 'space-between', alignItems: 'flex-start' }}>
        <div style={{ flex: 1, minInlineSize: 0 }}>
          <div className="hk-row" style={{ gap: 6, marginBottom: 4 }}>
            <strong>
              {relevantConflicts.length > 0
                ? `⚠ ${relevantConflicts.length} Sister-App Coexistence Overlap(s) Detected`
                : '🤝 Fleet Vault Coexistence & Lock Coordination'}
            </strong>
            <span className="hk-pill nowrap">{status.coexistenceMode}</span>
            {status.activeHkLocksCount > 0 && (
              <span className="hk-pill warn nowrap">
                🔒 {status.activeHkLocksCount} Active .hklock
              </span>
            )}
            {onlinePeers.map((p) => (
              <span key={p.appId} className="hk-pill ok nowrap">
                ● {p.name} :{p.port}
              </span>
            ))}
          </div>

          {relevantConflicts.length > 0 ? (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 3, fontSize: 12 }}>
              {relevantConflicts.map((c) => (
                <div key={c.conflictId} style={{ color: 'var(--text-secondary)' }}>
                  • <strong>[{c.sisterAppName || c.sisterAppId}]</strong> {c.summary}{' '}
                  {c.sharedPath ? <code>({c.sharedPath})</code> : null}
                </div>
              ))}
            </div>
          ) : (
            <div style={{ fontSize: 12, color: 'var(--text-secondary)' }}>
              {hasBannerSignal
                ? 'Shared roots honor Standard 19 PID+StartTime .hklock deferral, .coexistence.json v2, and zero-rewrite sidecar parity.'
                : 'Apply a 1-click coexistence preset below to pre-align shared vaults with ShadowLibrary, Redroom, uStation, and StreamEngine.'}
            </div>
          )}
        </div>

        <button
          type="button"
          className="hk-btn sm"
          onClick={() => void loadStatus()}
          title="Re-probe sister apps (:5179/:5180/:8460/:8430/:8420) and .coexistence.json manifests"
        >
          ↻ Check Peers
        </button>
      </div>

      <div className="hk-row" style={{ marginTop: 8, gap: 6 }}>
        {presets.map((p) => (
          <button
            key={p.id}
            type="button"
            className="hk-btn sm primary"
            disabled={Boolean(applying)}
            title={p.title}
            onClick={() => void handleApplyPreset(p.id)}
          >
            {applying === p.id ? '⏳ Applying...' : p.label}
          </button>
        ))}
      </div>
    </div>
  );
};
