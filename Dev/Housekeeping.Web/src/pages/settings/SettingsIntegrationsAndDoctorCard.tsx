// @module: Housekeeping.Web/pages/settings/SettingsIntegrationsAndDoctorCard
// @purpose: Extracted Greenroom (:8420) Sister-App Coexistence card and System Doctor diagnostic/backup panels for SettingsPage.
// @depends-on: react, ../../components/HybridPathPicker, ../../hints/settings, ../../services/apiClient, ../../types/models
// @exports: SettingsIntegrationsAndDoctorCard
import React from 'react';
import { HybridPathPicker } from '../../components/HybridPathPicker';
import { SETTINGS_HINTS } from '../../hints/settings';
import { apiClient } from '../../services/apiClient';
import type {
  AppConfig,
  AuthStatusResponse,
  DependencyStatusItem,
  DiskDriveMetricItem,
  GreenroomStatusDto,
  TailscalePwaStatusDto,
} from '../../types/models';

interface Props {
  mode: 'greenroom' | 'doctor';
  config: AppConfig;
  authStatus: AuthStatusResponse | null;
  greenroomStatus: GreenroomStatusDto | null;
  tailscalePwa: TailscalePwaStatusDto | null;
  deps: DependencyStatusItem[];
  disks: DiskDriveMetricItem[];
  doctorBusy: boolean;
  setDoctorBusy: (busy: boolean) => void;
  setDeps: (deps: DependencyStatusItem[]) => void;
  updateConfig: (mutator: (d: AppConfig) => void) => void;
  flushSaveNow: () => Promise<boolean>;
  refreshGreenroomStatus: () => Promise<void>;
  showToast: (msg: string) => void;
}

export const SettingsIntegrationsAndDoctorCard: React.FC<Props> = ({
  mode,
  config,
  authStatus,
  greenroomStatus,
  tailscalePwa,
  deps,
  disks,
  doctorBusy,
  setDoctorBusy,
  setDeps,
  updateConfig,
  flushSaveNow,
  refreshGreenroomStatus,
  showToast,
}) => {
  if (mode === 'greenroom') {
    return (
      <div className="hk-card">
        <div className="hk-card-header">
          <div>
            <h3 className="hk-card-title" style={{ color: 'var(--greenroom)' }}>
              🟢 Greenroom (:8420) &amp; Sister-App Coexistence
            </h3>
            <p className="hk-card-subtitle">
              Triggers Greenroom catalog refreshes on manifest updates and coordinates shared vaults
              with ShadowLibrary, Redroom, uStation, and StreamEngine.
            </p>
          </div>
          <div className="hk-row">
            <span className={`hk-pill nowrap ${greenroomStatus?.CompanionOnline ? 'ok' : 'warn'}`}>
              {greenroomStatus?.StatusMessage || 'Checking :8420...'}
            </span>
            <button type="button" className="hk-btn sm" onClick={() => void refreshGreenroomStatus()}>
              🔄 Probe :8420
            </button>
            <button
              type="button"
              className="hk-btn sm success"
              onClick={async () => {
                await flushSaveNow();
                const res = await apiClient.syncGreenroomNow('CollectionManifest');
                await refreshGreenroomStatus();
                showToast(res.Summary);
              }}
            >
              ⚡ Trigger Greenroom Sync Now
            </button>
          </div>
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
          <label className="hk-checkbox-label hk-has-hint" title={SETTINGS_HINTS.autoAlignSisterApps}>
            <input
              type="checkbox"
              className="hk-has-hint"
              title={SETTINGS_HINTS.autoAlignSisterApps}
              checked={Boolean(config.AutoAlignWithSisterApps)}
              onChange={(e) => updateConfig((d) => (d.AutoAlignWithSisterApps = e.target.checked))}
            />
            <strong>🤝 Auto-align shared vault settings with sister apps</strong>
          </label>
          <label className="hk-checkbox-label hk-has-hint" title={SETTINGS_HINTS.greenroomEnabled}>
            <input
              type="checkbox"
              className="hk-has-hint"
              title={SETTINGS_HINTS.greenroomEnabled}
              checked={config.GreenroomIntegration.Enabled}
              onChange={(e) =>
                updateConfig((d) => (d.GreenroomIntegration.Enabled = e.target.checked))
              }
            />
            Enable Greenroom ecosystem integration
          </label>
          <label className="hk-checkbox-label hk-has-hint" title={SETTINGS_HINTS.greenroomRomAutoSync}>
            <input
              type="checkbox"
              className="hk-has-hint"
              title={SETTINGS_HINTS.greenroomRomAutoSync}
              checked={config.GreenroomIntegration.AutoSyncAfterRomManifestChanges}
              onChange={(e) =>
                updateConfig(
                  (d) => (d.GreenroomIntegration.AutoSyncAfterRomManifestChanges = e.target.checked)
                )
              }
            />
            Auto-trigger <code>collect_library</code> + <code>verify_hashes</code> after ROM manifest changes
          </label>
          <label
            className="hk-checkbox-label hk-has-hint"
            title={SETTINGS_HINTS.greenroomManifestAutoSync}
          >
            <input
              type="checkbox"
              className="hk-has-hint"
              title={SETTINGS_HINTS.greenroomManifestAutoSync}
              checked={config.GreenroomIntegration.AutoSyncAfterCollectionManifestChanges}
              onChange={(e) =>
                updateConfig(
                  (d) =>
                    (d.GreenroomIntegration.AutoSyncAfterCollectionManifestChanges = e.target.checked)
                )
              }
            />
            Auto-trigger <code>collect_library</code> + <code>collect_plex</code> after collection manifest changes
          </label>

          <div className="hk-grid-3" style={{ marginTop: 6 }}>
            <div className="hk-field">
              <label className="hk-field-label">Companion Base URL</label>
              <input
                type="text"
                className="hk-input mono"
                value={config.GreenroomIntegration.CompanionBaseUrl}
                onChange={(e) =>
                  updateConfig((d) => (d.GreenroomIntegration.CompanionBaseUrl = e.target.value))
                }
              />
            </div>
            <div className="hk-field">
              <label className="hk-field-label">Companion Port</label>
              <input
                type="number"
                className="hk-input mono"
                value={config.GreenroomIntegration.CompanionPort}
                onChange={(e) =>
                  updateConfig(
                    (d) => (d.GreenroomIntegration.CompanionPort = Number(e.target.value) || 8420)
                  )
                }
              />
            </div>
            <div className="hk-field">
              <label className="hk-field-label">Greenroom Root Folder</label>
              <HybridPathPicker
                value={config.GreenroomIntegration.GreenroomRootPath}
                onChange={(next) =>
                  updateConfig((d) => (d.GreenroomIntegration.GreenroomRootPath = next))
                }
              />
            </div>
          </div>
          {greenroomStatus && (
            <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 4 }}>
              Last Auto-Sync: <strong>{greenroomStatus.LastAutoSyncSummary}</strong>
            </div>
          )}
        </div>
      </div>
    );
  }

  const toolCols = ['Executable / Tool', 'Purpose', 'Status', 'Resolution Source', 'Resolved Path'];
  const driveCols = ['Drive', 'Volume Label', 'Type', 'Capacity', 'Available Free', 'Free Space %', 'Status'];

  return (
    <div className="hk-doctor-panel" style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div className="hk-card">
        <h3 className="hk-card-title">🩺 System Diagnostics &amp; Health Status</h3>
        <div className="hk-grid-4" style={{ marginTop: 12 }}>
          <div className="hk-kpi-card">
            <span className="hk-kpi-label">APP VERSION</span>
            <span className="hk-kpi-value ok">{authStatus?.version || '0.1.12-hf.1'}</span>
            <span className="hk-kpi-sub">Standard 03 / PATCH+1</span>
          </div>
          <div className="hk-kpi-card">
            <span className="hk-kpi-label">HOST MACHINE</span>
            <span className="hk-kpi-value" style={{ fontSize: 15 }}>{authStatus?.machineName || 'Localhost'}</span>
            <span className="hk-kpi-sub">Archetype: Companion Daemon</span>
          </div>
          <div className="hk-kpi-card">
            <span className="hk-kpi-label">API &amp; WEB SERVER</span>
            <span className="hk-kpi-value ok">:{config.WebServer?.Port || 5178}</span>
            <span className="hk-kpi-sub">Loopback &amp; LAN</span>
          </div>
          <div className="hk-kpi-card">
            <span className="hk-kpi-label">TAILSCALE SERVE</span>
            <span className={`hk-kpi-value ${tailscalePwa?.ServeConfigured ? 'ok' : 'warn'}`}>
              {tailscalePwa?.ServeConfigured ? 'Active' : tailscalePwa?.TailscaleAvailable ? 'Standby' : 'Not Installed'}
            </span>
            <span className="hk-kpi-sub">{tailscalePwa?.DnsName || 'No HTTPS proxy'}</span>
          </div>
        </div>
      </div>

      <div className="hk-card">
        <div className="hk-row" style={{ justifyContent: 'space-between', marginBottom: 8 }}>
          <h3 className="hk-card-title">🔧 Core Executable Dependencies</h3>
          <button
            type="button"
            className="hk-btn sm"
            onClick={() => {
              apiClient
                .getModulesCatalog()
                .then((res) => {
                  setDeps(res.dependencies || []);
                  showToast('Refreshed tool dependencies.');
                })
                .catch((err) => {
                  console.debug('[SettingsDoctor] refresh tools failed:', err);
                  showToast('Failed to refresh dependencies.');
                });
            }}
          >
            ↻ Refresh Tools
          </button>
        </div>
        <div className="table-wrapper" tabIndex={0} role="region" aria-label="Core executable dependencies">
          <table className="hk-table">
            <thead>
              <tr>
                {toolCols.map((c) => (
                  <th key={c} className="hk-has-hint" title={SETTINGS_HINTS.thDoctorTool(c)}>
                    {c}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {deps.map((d) => (
                <tr key={d.ToolName}>
                  <td><strong>{d.ToolName}</strong></td>
                  <td style={{ fontSize: 11.5, color: 'var(--text-secondary)' }}>{d.Description}</td>
                  <td><span className={`hk-pill ${d.IsAvailable ? 'ok' : 'err'}`}>{d.IsAvailable ? '✓ Ready' : '✕ Missing'}</span></td>
                  <td><span className="hk-pill">{d.SourceLabel || 'Auto'}</span></td>
                  <td><code style={{ fontSize: 11, wordBreak: 'break-all' }}>{d.ResolvedPath || 'Not found'}</code></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <div className="hk-card">
        <h3 className="hk-card-title">💾 Storage Volumes &amp; Mount Reachability</h3>
        <div className="table-wrapper" tabIndex={0} role="region" aria-label="Mounted storage volumes" style={{ marginTop: 8 }}>
          <table className="hk-table">
            <thead>
              <tr>
                {driveCols.map((c) => (
                  <th key={c} className="hk-has-hint" title={SETTINGS_HINTS.thDoctorDrive(c)}>
                    {c}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {disks.map((d) => {
                const isReady = d.IsReady !== false;
                const ratio = isReady && d.TotalGb > 0 ? (d.FreeGb / d.TotalGb) * 100 : 100;
                const sc = !isReady ? 'err' : d.FreeGb < 25 || ratio < 5 ? 'err' : ratio < 12 ? 'warn' : 'ok';
                return (
                  <tr key={d.DriveLetter}>
                    <td><strong>{d.DriveLetter}</strong></td>
                    <td>{d.VolumeLabel || '—'}</td>
                    <td>{d.IsNetwork ? '🌐 Network (SMB)' : '💾 Local Disk'}</td>
                    <td>{d.TotalGb.toFixed(1)} GB</td>
                    <td><strong>{d.FreeGb.toFixed(1)} GB</strong></td>
                    <td className="mono">{ratio.toFixed(0)}%</td>
                    <td><span className={`hk-pill ${sc}`}>{isReady ? (sc === 'ok' ? 'Healthy' : 'Low Space') : 'Offline'}</span></td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </div>

      <div className="hk-card">
        <h3 className="hk-card-title">🧰 Maintenance &amp; System Operations</h3>
        <div className="hk-row" style={{ marginTop: 10, gap: 10 }}>
          <button
            type="button"
            className="hk-btn primary"
            disabled={doctorBusy}
            onClick={async () => {
              setDoctorBusy(true);
              try {
                const res = await apiClient.triggerBackupNow();
                showToast(`📦 Backup archive created: ${res.fileName} (${res.format})`);
              } catch (err) {
                showToast(err instanceof Error ? err.message : 'Backup failed.');
              } finally {
                setDoctorBusy(false);
              }
            }}
          >
            {doctorBusy ? '⏳ Creating Backup...' : '📦 Backup Config & State Now (.7z)'}
          </button>
          <button
            type="button"
            className="hk-btn success"
            disabled={doctorBusy}
            onClick={async () => {
              setDoctorBusy(true);
              try {
                await apiClient.exportDiagnosticBundle();
                showToast('🩺 Exported system diagnostic bundle to ActiveLogsDir.');
              } catch (err) {
                showToast(err instanceof Error ? err.message : 'Diagnostic export failed.');
              } finally {
                setDoctorBusy(false);
              }
            }}
          >
            🩺 Export Diagnostic Bundle (JSON)
          </button>
          <button
            type="button"
            className="hk-btn"
            onClick={async () => {
              try {
                await flushSaveNow();
                showToast('💾 Saved active configuration to disk.');
              } catch (err) {
                showToast(err instanceof Error ? err.message : 'Save failed.');
              }
            }}
          >
            💾 Save Settings.json
          </button>
        </div>
      </div>
    </div>
  );
};
