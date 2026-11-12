// @module: Housekeeping.Web/pages/SettingsPage
// @purpose: Global Settings workspace across 8 subtabs with Standard 09 2-line hover hints and extracted Greenroom/Doctor panels.
// @depends-on: react, ../components/HybridPathPicker, ../context/AppStateContext, ../hints/settings, ../services/apiClient, ../types/models, ./settings/SettingsIntegrationsAndDoctorCard
// @exports: SettingsPage
import React, { useEffect, useState } from 'react';
import { HybridPathPicker } from '../components/HybridPathPicker';
import { useAppState, type ColorSchemeMode, type UiDensityMode } from '../context/AppStateContext';
import { SETTINGS_HINTS } from '../hints/settings';
import { apiClient } from '../services/apiClient';
import type {
  DependencyStatusItem,
  FooterVerbosityLevel,
  RemoteConnectionType,
  RemoteNodeConfig,
  TailscalePwaStatusDto,
  VerbosityLevel,
} from '../types/models';
import { SettingsIntegrationsAndDoctorCard } from './settings/SettingsIntegrationsAndDoctorCard';

type SettingsSection =
  | 'tools'
  | 'watchdog'
  | 'network'
  | 'web'
  | 'greenroom'
  | 'notifications'
  | 'fleet'
  | 'doctor';

const ALL_LOG_TAGS = ['ERROR', 'WARN', 'SUCCESS', 'WORK', 'HEADER', 'INFO', 'NET', 'CMD', 'DRY', 'SKIP'];

export const SettingsPage: React.FC = () => {
  const {
    config,
    authStatus,
    updateConfig,
    flushSaveNow,
    greenroomStatus,
    refreshGreenroomStatus,
    colorScheme,
    setColorScheme,
    density,
    setDensity,
    showToast,
    telemetry,
  } = useAppState();

  const disks = telemetry?.Disks || [];

  const [section, setSection] = useState<SettingsSection>(() => {
    try {
      const requested = sessionStorage.getItem('hk-settings-subtab') as SettingsSection | null;
      if (requested) {
        sessionStorage.removeItem('hk-settings-subtab');
        return requested;
      }
    } catch (err) {
      console.debug('[SettingsPage] sessionStorage subtab read skipped:', err);
    }
    return 'tools';
  });
  const [deps, setDeps] = useState<DependencyStatusItem[]>([]);

  // Admin password change fields
  const [currPwd, setCurrPwd] = useState('');
  const [newPwd, setNewPwd] = useState('');
  const [newGuardProc, setNewGuardProc] = useState('');
  const [fleetStatus, setFleetStatus] = useState<Record<string, RemoteNodeConfig>>({});
  const [probingFleet, setProbingFleet] = useState(false);
  const [tailscalePwa, setTailscalePwa] = useState<TailscalePwaStatusDto | null>(null);
  const [tailscaleBusy, setTailscaleBusy] = useState(false);
  const [doctorBusy, setDoctorBusy] = useState(false);

  useEffect(() => {
    apiClient
      .getModulesCatalog()
      .then((res) => setDeps(res.dependencies || []))
      .catch((err) => console.debug('[SettingsPage] dependencies load failed:', err));
  }, []);

  useEffect(() => {
    if (section !== 'web' && section !== 'doctor') return;
    let active = true;
    const loadTs = () => {
      apiClient
        .getTailscalePwaStatus()
        .then((res) => {
          if (active) setTailscalePwa(res);
        })
        .catch((err) => console.debug('[SettingsPage] Tailscale status probe failed:', err));
    };
    loadTs();

    let timer: number | undefined;
    if (tailscalePwa?.PendingAuthUrl && !tailscalePwa.ServeConfigured) {
      timer = window.setInterval(loadTs, 4000);
    }
    return () => {
      active = false;
      if (timer) window.clearInterval(timer);
    };
  }, [section, tailscalePwa?.PendingAuthUrl, tailscalePwa?.ServeConfigured]);

  if (!config) {
    return <div className="hk-card">Loading Settings...</div>;
  }

  const handleChangePassword = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      const res = await apiClient.changePassword(currPwd, newPwd);
      setCurrPwd('');
      setNewPwd('');
      showToast(res.message);
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Password update failed.');
    }
  };

  const handleRotateNodeToken = async () => {
    try {
      const res = await apiClient.rotateNodeToken();
      updateConfig((d) => {
        d.WebServer.NodeApiToken = res.nodeApiToken;
      });
      showToast('Rotated Node API Token.');
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Token rotation failed.');
    }
  };

  const handleProbeFleet = async () => {
    await flushSaveNow();
    setProbingFleet(true);
    try {
      const res = await apiClient.getFleetNodes(true);
      const map: Record<string, RemoteNodeConfig> = {};
      for (const n of res.nodes) {
        map[n.Id] = n;
      }
      setFleetStatus(map);
      showToast(`Probed ${res.nodes.length} remote node(s).`);
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Fleet probe failed.');
    } finally {
      setProbingFleet(false);
    }
  };

  const subtabs: { id: SettingsSection; label: string }[] = [
    { id: 'tools', label: '🔧 Tools & HandBrake Profiles' },
    { id: 'watchdog', label: '🛡 Startup & Process Guard' },
    { id: 'network', label: '🌐 Network Safety & Locking' },
    { id: 'web', label: '🔐 Web, Tailnet & Desktop UI' },
    { id: 'greenroom', label: '🟢 Greenroom (:8420)' },
    { id: 'notifications', label: '🔔 Verbosity & Discord' },
    { id: 'fleet', label: '🖧 Remote Fleet Nodes' },
    { id: 'doctor', label: '🩺 System Doctor' },
  ];

  return (
    <div>
      <div className="hk-subtabs" role="tablist" aria-label="Settings categories">
        {subtabs.map((t) => (
          <button
            key={t.id}
            type="button"
            role="tab"
            aria-selected={section === t.id}
            className={`hk-subtab ${section === t.id ? 'active' : ''}`}
            onClick={() => setSection(t.id)}
          >
            {t.label}
          </button>
        ))}
      </div>

      {/* 1. TOOLS & HANDBRAKE PROFILES */}
      {section === 'tools' && (
        <>
          <div className="hk-card">
            <h3 className="hk-card-title">🔧 External CLI Tool Overrides</h3>
            <p className="hk-card-subtitle">
              Leave blank to auto-resolve from <code>./tools/</code> or Windows <code>PATH</code>.
            </p>

            <div className="hk-row" style={{ margin: '10px 0' }}>
              {deps.map((d) => (
                <span key={d.ToolName} className={`hk-pill ${d.IsAvailable ? 'ok' : 'warn'}`}>
                  {d.IsAvailable ? '✓' : '⚠'} {d.ToolName}: {d.ResolvedPath || d.SourceLabel}
                </span>
              ))}
            </div>

            <div className="hk-grid-2">
              <div className="hk-field">
                <label className="hk-field-label">HandBrakeCLI.exe Path</label>
                <HybridPathPicker
                  value={config.ToolPaths.HandBrakeCliPath}
                  mode="file"
                  onChange={(v) => updateConfig((d) => (d.ToolPaths.HandBrakeCliPath = v))}
                  placeholder="Auto-resolve HandBrakeCLI.exe"
                />
              </div>
              <div className="hk-field">
                <label className="hk-field-label">7z.exe (7-Zip CLI) Path</label>
                <HybridPathPicker
                  value={config.ToolPaths.SevenZipPath}
                  mode="file"
                  onChange={(v) => updateConfig((d) => (d.ToolPaths.SevenZipPath = v))}
                  placeholder="Auto-resolve 7z.exe"
                />
              </div>
              <div className="hk-field">
                <label className="hk-field-label">rclone.exe Path</label>
                <HybridPathPicker
                  value={config.ToolPaths.RclonePath}
                  mode="file"
                  onChange={(v) => updateConfig((d) => (d.ToolPaths.RclonePath = v))}
                  placeholder="Auto-resolve rclone.exe"
                />
              </div>
              <div className="hk-field">
                <label className="hk-field-label">Humble Bundle Downloader (hbd.exe) Path</label>
                <HybridPathPicker
                  value={config.ToolPaths.HumbleDownloaderPath}
                  mode="file"
                  onChange={(v) => updateConfig((d) => (d.ToolPaths.HumbleDownloaderPath = v))}
                  placeholder="Auto-resolve hbd.exe"
                />
              </div>
            </div>
          </div>

          <div className="hk-card">
            <div className="hk-card-header">
              <div>
                <h3 className="hk-card-title">🎞 HandBrake Encoding Profiles Catalog</h3>
                <p className="hk-card-subtitle">
                  Shared profiles referenced by Proxy Gen, Video Archiver, and Multi-Tier Media
                  Transcoder.
                </p>
              </div>
              <button
                type="button"
                className="hk-btn sm primary"
                onClick={() =>
                  updateConfig((d) => {
                    d.HandBrakeProfiles.push({
                      ProfileId: `Custom_${d.HandBrakeProfiles.length + 1}`,
                      Name: 'Custom HEVC Profile',
                      ContainerExtension: '.mkv',
                      VideoEncoder: 'x265',
                      EncoderPreset: 'fast',
                      QualityRf: 21,
                      MaxWidth: 1920,
                      MaxHeight: 1080,
                      AudioEncoder: 'av_aac',
                      AudioBitrateKbps: 192,
                      AudioLanguageCode: '',
                      SubtitleMode: 'none',
                      UseGuiPresetOverride: false,
                      GuiPresetName: '',
                      GuiPresetFilePath: '',
                      ExtraCliFlags: '',
                    });
                  })
                }
              >
                + Add Encoding Profile
              </button>
            </div>

            <div className="table-wrapper" tabIndex={0} role="region" aria-label="HandBrake profiles">
              <table className="hk-table">
                <thead>
                  <tr>
                    <th className="hk-has-hint" title={SETTINGS_HINTS.thHbId}>Profile ID</th>
                    <th className="hk-has-hint" title={SETTINGS_HINTS.thHbName}>Display Name</th>
                    <th className="hk-has-hint" title={SETTINGS_HINTS.thHbExt}>Ext</th>
                    <th className="hk-has-hint" title={SETTINGS_HINTS.thHbEnc}>Encoder</th>
                    <th className="hk-has-hint" title={SETTINGS_HINTS.thHbPreset}>Preset</th>
                    <th className="hk-has-hint" title={SETTINGS_HINTS.thHbRf}>RF</th>
                    <th className="hk-has-hint" title={SETTINGS_HINTS.thHbMaxWh}>Max W×H</th>
                    <th className="hk-has-hint" title={SETTINGS_HINTS.thHbAudio}>Audio</th>
                    <th className="hk-has-hint" title={SETTINGS_HINTS.thHbKbps}>Kbps</th>
                    <th className="hk-has-hint" title={SETTINGS_HINTS.thHbSubs}>Subs</th>
                    <th className="hk-has-hint" title={SETTINGS_HINTS.thHbFlags}>Extra Flags</th>
                    <th className="hk-has-hint" title={SETTINGS_HINTS.thHbDel}>Del</th>
                  </tr>
                </thead>
                <tbody>
                  {config.HandBrakeProfiles.map((p, idx) => (
                    <tr key={idx}>
                      <td>
                        <input
                          type="text"
                          className="hk-input mono"
                          style={{ minWidth: 125 }}
                          value={p.ProfileId}
                          onChange={(e) =>
                            updateConfig((d) => {
                              d.HandBrakeProfiles[idx].ProfileId = e.target.value;
                            })
                          }
                        />
                      </td>
                      <td>
                        <input
                          type="text"
                          className="hk-input"
                          style={{ minWidth: 165 }}
                          value={p.Name}
                          onChange={(e) =>
                            updateConfig((d) => {
                              d.HandBrakeProfiles[idx].Name = e.target.value;
                            })
                          }
                        />
                      </td>
                      <td>
                        <select
                          className="hk-select"
                          value={p.ContainerExtension}
                          onChange={(e) =>
                            updateConfig((d) => {
                              d.HandBrakeProfiles[idx].ContainerExtension = e.target.value;
                            })
                          }
                        >
                          <option value=".mkv">.mkv</option>
                          <option value=".mp4">.mp4</option>
                        </select>
                      </td>
                      <td>
                        <select
                          className="hk-select"
                          value={p.VideoEncoder}
                          onChange={(e) =>
                            updateConfig((d) => {
                              d.HandBrakeProfiles[idx].VideoEncoder = e.target.value;
                            })
                          }
                        >
                          <option value="x265">x265</option>
                          <option value="x265_10bit">x265_10bit</option>
                          <option value="x264">x264</option>
                          <option value="nvenc_h265">nvenc_h265</option>
                          <option value="nvenc_h264">nvenc_h264</option>
                          <option value="qsv_h265">qsv_h265</option>
                          <option value="svt_av1">svt_av1</option>
                        </select>
                      </td>
                      <td>
                        <select
                          className="hk-select"
                          value={p.EncoderPreset}
                          onChange={(e) =>
                            updateConfig((d) => {
                              d.HandBrakeProfiles[idx].EncoderPreset = e.target.value;
                            })
                          }
                        >
                          <option value="ultrafast">ultrafast</option>
                          <option value="veryfast">veryfast</option>
                          <option value="faster">faster</option>
                          <option value="fast">fast</option>
                          <option value="medium">medium</option>
                          <option value="slow">slow</option>
                        </select>
                      </td>
                      <td>
                        <input
                          type="number"
                          step="0.5"
                          className="hk-input"
                          style={{ width: 68 }}
                          value={p.QualityRf}
                          onChange={(e) =>
                            updateConfig((d) => {
                              d.HandBrakeProfiles[idx].QualityRf = Number(e.target.value) || 21;
                            })
                          }
                        />
                      </td>
                      <td>
                        <div className="hk-row" style={{ flexWrap: 'nowrap', gap: 4 }}>
                          <input
                            type="number"
                            className="hk-input"
                            style={{ width: 72 }}
                            value={p.MaxWidth}
                            onChange={(e) =>
                              updateConfig((d) => {
                                d.HandBrakeProfiles[idx].MaxWidth = Number(e.target.value) || 1920;
                              })
                            }
                          />
                          <span>×</span>
                          <input
                            type="number"
                            className="hk-input"
                            style={{ width: 70 }}
                            value={p.MaxHeight}
                            onChange={(e) =>
                              updateConfig((d) => {
                                d.HandBrakeProfiles[idx].MaxHeight = Number(e.target.value) || 1080;
                              })
                            }
                          />
                        </div>
                      </td>
                      <td>
                        <select
                          className="hk-select"
                          value={p.AudioEncoder}
                          onChange={(e) =>
                            updateConfig((d) => {
                              d.HandBrakeProfiles[idx].AudioEncoder = e.target.value;
                            })
                          }
                        >
                          <option value="av_aac">av_aac</option>
                          <option value="ac3">ac3</option>
                          <option value="eac3">eac3</option>
                          <option value="copy">copy</option>
                        </select>
                      </td>
                      <td>
                        <input
                          type="number"
                          className="hk-input"
                          style={{ width: 72 }}
                          value={p.AudioBitrateKbps}
                          onChange={(e) =>
                            updateConfig((d) => {
                              d.HandBrakeProfiles[idx].AudioBitrateKbps =
                                Number(e.target.value) || 160;
                            })
                          }
                        />
                      </td>
                      <td>
                        <select
                          className="hk-select"
                          value={p.SubtitleMode}
                          onChange={(e) =>
                            updateConfig((d) => {
                              d.HandBrakeProfiles[idx].SubtitleMode = e.target.value;
                            })
                          }
                        >
                          <option value="none">none</option>
                          <option value="first">first</option>
                          <option value="burned">burned</option>
                        </select>
                      </td>
                      <td>
                        <input
                          type="text"
                          className="hk-input mono"
                          style={{ minWidth: 120 }}
                          value={p.ExtraCliFlags}
                          onChange={(e) =>
                            updateConfig((d) => {
                              d.HandBrakeProfiles[idx].ExtraCliFlags = e.target.value;
                            })
                          }
                        />
                      </td>
                      <td>
                        <button
                          type="button"
                          className="hk-btn sm danger"
                          onClick={() =>
                            updateConfig((d) => {
                              d.HandBrakeProfiles.splice(idx, 1);
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
        </>
      )}

      {/* 2. WATCHDOG, STARTUP & PROCESS GUARD */}
      {section === 'watchdog' && (
        <div className="hk-grid-2">
          <div className="hk-card">
            <h3 className="hk-card-title">🖥 Windows Tray Daemon &amp; Task Scheduler Watchdog</h3>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 12, marginTop: 10 }}>
              <label className="hk-checkbox-label hk-has-hint" title={SETTINGS_HINTS.startWithWindows}>
                <input
                  type="checkbox"
                  className="hk-has-hint"
                  title={SETTINGS_HINTS.startWithWindows}
                  checked={config.StartWithWindows}
                  onChange={(e) => updateConfig((d) => (d.StartWithWindows = e.target.checked))}
                />
                Launch Housekeeping Tray Daemon Automatically at Windows Logon
              </label>

              <label className="hk-checkbox-label hk-has-hint" title={SETTINGS_HINTS.startMinimized}>
                <input
                  type="checkbox"
                  className="hk-has-hint"
                  title={SETTINGS_HINTS.startMinimized}
                  checked={config.StartMinimizedToTray}
                  onChange={(e) => updateConfig((d) => (d.StartMinimizedToTray = e.target.checked))}
                />
                Start Silently in System Tray (Backend Always Running)
              </label>

              <label className="hk-checkbox-label hk-has-hint" title={SETTINGS_HINTS.autoRestartInterrupted}>
                <input
                  type="checkbox"
                  className="hk-has-hint"
                  title={SETTINGS_HINTS.autoRestartInterrupted}
                  checked={Boolean(config.AutoRestartInterruptedRunsOnStartup)}
                  onChange={(e) =>
                    updateConfig((d) => (d.AutoRestartInterruptedRunsOnStartup = e.target.checked))
                  }
                />
                Auto-Restart Jobs &amp; Queues Interrupted Mid-Shutdown on Startup
              </label>

              <div
                style={{
                  padding: 12,
                  borderRadius: 'var(--radius-md)',
                  background: 'var(--bg-elevated)',
                  border: '1px solid var(--border-subtle)',
                  fontSize: 13,
                  color: 'var(--text-secondary)',
                }}
              >
                <strong>🤖 Fleet Watchdog &amp; Health Supervision:</strong> Windows service crash-recovery
                and heartbeat monitoring are centrally managed by <code>Mother v1.0</code> via <code>mother.manifest.json</code>.
              </div>
            </div>
          </div>

          <div className="hk-card">
            <h3 className="hk-card-title">🎮 Game / Streaming Process Guard</h3>
            <p className="hk-card-subtitle">
              Automatically defers background scheduled runs while emulators or OBS are active so
              CPU/GPU frame times never stutter.
            </p>
            <label className="hk-checkbox-label hk-has-hint" style={{ margin: '10px 0' }} title={SETTINGS_HINTS.enableProcessGuard}>
              <input
                type="checkbox"
                className="hk-has-hint"
                title={SETTINGS_HINTS.enableProcessGuard}
                checked={config.EnableProcessGuard}
                onChange={(e) => updateConfig((d) => (d.EnableProcessGuard = e.target.checked))}
              />
              Defer Scheduled Runs When Guarded Processes Are Running
            </label>

            <div className="hk-row" style={{ marginBottom: 10 }}>
              {(config.GuardedProcessNames || []).map((proc) => (
                <span key={proc} className="hk-pill accent">
                  {proc}
                  <button
                    type="button"
                    className="hk-btn sm ghost"
                    style={{ minHeight: 18, padding: '0 4px' }}
                    onClick={() =>
                      updateConfig((d) => {
                        d.GuardedProcessNames = d.GuardedProcessNames.filter((x) => x !== proc);
                      })
                    }
                  >
                    ✕
                  </button>
                </span>
              ))}
            </div>

            <form
              className="hk-row"
              onSubmit={(e) => {
                e.preventDefault();
                const clean = newGuardProc.trim().replace(/\.exe$/i, '');
                if (!clean) return;
                updateConfig((d) => {
                  if (!d.GuardedProcessNames.includes(clean)) {
                    d.GuardedProcessNames.push(clean);
                  }
                });
                setNewGuardProc('');
              }}
            >
              <input
                type="text"
                className="hk-input mono"
                style={{ width: 210 }}
                placeholder="Process name (e.g. obs64)"
                value={newGuardProc}
                onChange={(e) => setNewGuardProc(e.target.value)}
              />
              <button type="submit" className="hk-btn sm primary">
                + Add Process
              </button>
            </form>
          </div>
        </div>
      )}

      {/* 3. NETWORK SAFETY & DISTRIBUTED LOCKING */}
      {section === 'network' && (
        <div className="hk-card">
          <h3 className="hk-card-title">🌐 NAS Network Drop Resilience &amp; Atomic Writes</h3>
          <div className="hk-grid-2" style={{ marginTop: 12 }}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
              <label className="hk-checkbox-label hk-has-hint" title={SETTINGS_HINTS.networkDropSafety}>
                <input
                  type="checkbox"
                  className="hk-has-hint"
                  title={SETTINGS_HINTS.networkDropSafety}
                  checked={config.EnableNetworkDropSafety}
                  onChange={(e) =>
                    updateConfig((d) => (d.EnableNetworkDropSafety = e.target.checked))
                  }
                />
                Pause &amp; Auto-Retry When SMB / NAS Network Shares Drop
              </label>
              <label className="hk-checkbox-label hk-has-hint" title={SETTINGS_HINTS.atomicPartialWrites}>
                <input
                  type="checkbox"
                  className="hk-has-hint"
                  title={SETTINGS_HINTS.atomicPartialWrites}
                  checked={config.EnableAtomicPartialWrites}
                  onChange={(e) =>
                    updateConfig((d) => (d.EnableAtomicPartialWrites = e.target.checked))
                  }
                />
                Write Transcodes Atomically via <code>.partial</code> + Deferred Pending Swaps
              </label>
              <label className="hk-checkbox-label hk-has-hint" title={SETTINGS_HINTS.cleanupPartials}>
                <input
                  type="checkbox"
                  className="hk-has-hint"
                  title={SETTINGS_HINTS.cleanupPartials}
                  checked={config.CleanupPartialsOnStartup}
                  onChange={(e) =>
                    updateConfig((d) => (d.CleanupPartialsOnStartup = e.target.checked))
                  }
                />
                Clean Up Interrupted <code>.partial</code> Artifacts on Startup
              </label>
              <label className="hk-checkbox-label hk-has-hint" title={SETTINGS_HINTS.distributedLocking}>
                <input
                  type="checkbox"
                  className="hk-has-hint"
                  title={SETTINGS_HINTS.distributedLocking}
                  checked={config.EnableDistributedNetworkLocking}
                  onChange={(e) =>
                    updateConfig((d) => (d.EnableDistributedNetworkLocking = e.target.checked))
                  }
                />
                Enable Distributed Multi-Machine Lock Files (<code>.hklock</code>)
              </label>
            </div>

            <div className="hk-grid-2">
              <div className="hk-field">
                <label className="hk-field-label">Network Retry Interval (Seconds)</label>
                <input
                  type="number"
                  min={3}
                  className="hk-input"
                  value={config.NetworkRetryIntervalSeconds}
                  onChange={(e) =>
                    updateConfig(
                      (d) => (d.NetworkRetryIntervalSeconds = Math.max(3, Number(e.target.value) || 15))
                    )
                  }
                />
              </div>
              <div className="hk-field">
                <label className="hk-field-label">Max Reconnect Attempts</label>
                <input
                  type="number"
                  min={1}
                  className="hk-input"
                  value={config.NetworkMaxRetries}
                  onChange={(e) =>
                    updateConfig(
                      (d) => (d.NetworkMaxRetries = Math.max(1, Number(e.target.value) || 240))
                    )
                  }
                />
              </div>
              <div className="hk-field">
                <label className="hk-field-label">Stale Lock Timeout (Minutes)</label>
                <input
                  type="number"
                  min={5}
                  className="hk-input"
                  value={config.DistributedLockTimeoutMinutes}
                  onChange={(e) =>
                    updateConfig(
                      (d) => (d.DistributedLockTimeoutMinutes = Math.max(5, Number(e.target.value) || 90))
                    )
                  }
                />
              </div>
            </div>
          </div>
        </div>
      )}

      {/* 4. WEB, TAILNET, SECURITY & DESKTOP HOST */}
      {section === 'web' && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
        <div className="hk-grid-2">
          <div className="hk-card">
            <h3 className="hk-card-title">🌐 Web Server, Tailnet Binding &amp; Desktop Mode</h3>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 12, marginTop: 10 }}>
              <div className="hk-grid-2">
                <div className="hk-field">
                  <label className="hk-field-label">HTTP / SignalR Port (Restart to Rebind)</label>
                  <input
                    type="number"
                    min={1024}
                    max={65535}
                    className="hk-input mono"
                    value={config.WebServer.Port}
                    onChange={(e) =>
                      updateConfig((d) => (d.WebServer.Port = Number(e.target.value) || 5178))
                    }
                  />
                </div>
                <div className="hk-field">
                  <label className="hk-field-label">Tray Double-Click Default UI</label>
                  <select
                    className="hk-select"
                    value={config.WebServer.DefaultDesktopLaunchMode}
                    onChange={(e) =>
                      updateConfig((d) => (d.WebServer.DefaultDesktopLaunchMode = e.target.value))
                    }
                  >
                    <option value="Browser">🌐 Open Default Web Browser</option>
                    <option value="StandaloneWindow">🪟 Open Standalone WebView2 Window</option>
                  </select>
                </div>
              </div>

              <div className="hk-grid-2">
                <div className="hk-field">
                  <label className="hk-field-label">Color Scheme Preference (3-State)</label>
                  <select
                    className="hk-select"
                    value={colorScheme}
                    onChange={(e) => setColorScheme(e.target.value as ColorSchemeMode)}
                  >
                    <option value="system">System Default (Follow OS)</option>
                    <option value="dark">Dark (Greenroom Sister-App Slate)</option>
                    <option value="light">Light (High-Contrast Daylight)</option>
                  </select>
                </div>

                <div className="hk-field">
                  <label className="hk-field-label">UI Density</label>
                  <select
                    className="hk-select"
                    value={density}
                    onChange={(e) => setDensity(e.target.value as UiDensityMode)}
                  >
                    <option value="compact">Compact (High-Density Desktop)</option>
                    <option value="comfortable">Comfortable</option>
                    <option value="touch">Touch (Tablet / Mobile)</option>
                  </select>
                </div>
              </div>

              <label className="hk-checkbox-label hk-has-hint" title={SETTINGS_HINTS.bindLanTailnet}>
                <input
                  type="checkbox"
                  className="hk-has-hint"
                  title={SETTINGS_HINTS.bindLanTailnet}
                  checked={config.WebServer.BindLanAndTailnet}
                  onChange={(e) =>
                    updateConfig((d) => (d.WebServer.BindLanAndTailnet = e.target.checked))
                  }
                />
                Bind <code>0.0.0.0</code> for LAN &amp; Tailscale Tailnet Access (Uncheck for{' '}
                <code>127.0.0.1</code> only)
              </label>

              <label className="hk-checkbox-label hk-has-hint" title={SETTINGS_HINTS.trustTailscaleHeaders}>
                <input
                  type="checkbox"
                  className="hk-has-hint"
                  title={SETTINGS_HINTS.trustTailscaleHeaders}
                  checked={config.WebServer.TrustTailscaleServeHeaders}
                  onChange={(e) =>
                    updateConfig((d) => (d.WebServer.TrustTailscaleServeHeaders = e.target.checked))
                  }
                />
                Trust <code>Tailscale-User-Login</code> Identity Header from Loopback{' '}
                <code>tailscale serve</code>
              </label>

              <label className="hk-checkbox-label hk-has-hint" title={SETTINGS_HINTS.restrictRawShell}>
                <input
                  type="checkbox"
                  className="hk-has-hint"
                  title={SETTINGS_HINTS.restrictRawShell}
                  disabled={Boolean(authStatus?.rawShellBlockedForCaller)}
                  checked={config.WebServer.RestrictRawShellToLocalhost}
                  onChange={(e) =>
                    updateConfig(
                      (d) => (d.WebServer.RestrictRawShellToLocalhost = e.target.checked)
                    )
                  }
                />
                Hardening: Restrict Interactive Raw Shell &amp; Custom Scripts Editing to Localhost
              </label>

              {authStatus?.nativeDialogsAvailable && (
                <div className="hk-row">
                  <button
                    type="button"
                    className="hk-btn sm primary"
                    onClick={() =>
                      void apiClient
                        .openStandaloneWindow()
                        .catch((err: unknown) =>
                          showToast(err instanceof Error ? err.message : 'Unable to open standalone window.')
                        )
                    }
                  >
                    🪟 Open Standalone WebView2 Window Now
                  </button>
                </div>
              )}
            </div>
          </div>

          <div className="hk-card">
            <h3 className="hk-card-title">🔐 Remote Admin Password &amp; Fleet API Token</h3>
            <p className="hk-card-subtitle">
              Current caller: <strong>{authStatus?.identity || 'local'}</strong> via{' '}
              <code>{authStatus?.authMethod || 'Loopback'}</code>
            </p>

            <form
              onSubmit={(e) => void handleChangePassword(e)}
              style={{ display: 'flex', flexDirection: 'column', gap: 10, marginTop: 10 }}
            >
              {!authStatus?.isDirectLoopback && !authStatus?.requiresInitialPasswordSetup && (
                <div className="hk-field">
                  <label className="hk-field-label">Current Admin Password</label>
                  <input
                    type="password"
                    className="hk-input"
                    value={currPwd}
                    onChange={(e) => setCurrPwd(e.target.value)}
                  />
                </div>
              )}
              <div className="hk-field">
                <label className="hk-field-label">
                  {authStatus?.requiresInitialPasswordSetup
                    ? 'Set Initial Remote Admin Password'
                    : 'New Remote Admin Password'}
                </label>
                <div className="hk-row">
                  <input
                    type="password"
                    className="hk-input"
                    style={{ flex: 1 }}
                    value={newPwd}
                    onChange={(e) => setNewPwd(e.target.value)}
                    placeholder="At least 4 characters..."
                  />
                  <button type="submit" className="hk-btn primary" disabled={newPwd.length < 4}>
                    Save Password
                  </button>
                </div>
              </div>
            </form>

            <div
              style={{
                marginTop: 16,
                paddingTop: 12,
                borderTop: '1px solid var(--border-subtle)',
              }}
            >
              <label className="hk-field-label">
                Machine-to-Machine Fleet Node API Token (<code>X-Housekeeping-Token</code>)
              </label>
              <div className="hk-row" style={{ marginTop: 4 }}>
                <input
                  type="text"
                  className="hk-input mono"
                  style={{ flex: 1 }}
                  readOnly
                  value={config.WebServer.NodeApiToken}
                />
                <button
                  type="button"
                  className="hk-btn sm"
                  onClick={() => {
                    if (!navigator.clipboard?.writeText) {
                      showToast('Clipboard API unavailable in non-HTTPS context. Copy from input field directly.');
                      return;
                    }
                    void navigator.clipboard
                      .writeText(config.WebServer.NodeApiToken)
                      .then(() => showToast('Copied Node API Token to clipboard.'))
                      .catch((err) => {
                        console.debug('[SettingsPage] clipboard write failed:', err);
                        showToast('Clipboard write blocked by browser. Copy from input field directly.');
                      });
                  }}
                >
                  📋 Copy
                </button>
                <button
                  type="button"
                  className="hk-btn sm warn"
                  onClick={() => void handleRotateNodeToken()}
                >
                  🔄 Rotate
                </button>
              </div>
            </div>
          </div>
        </div>

        <div className="hk-card">
          <div className="hk-card-header">
            <div>
              <h3 className="hk-card-title">📱 Mobile PWA &amp; Tailscale Serve HTTPS</h3>
              <p className="hk-card-subtitle">
                Android Chrome &amp; iOS Safari require a trusted <code>https://</code> certificate
                to install Housekeeping as a standalone full-screen PWA. Tailscale Serve provisions
                an automatic Let&apos;s Encrypt TLS certificate for your Tailnet hostname while
                keeping LAN HTTP (<code>:{config.WebServer.Port}</code>) intact.
              </p>
            </div>
            <div className="hk-row">
              <span
                className={`hk-pill ${
                  tailscalePwa?.ServeConfigured
                    ? 'ok'
                    : tailscalePwa?.PendingAuthUrl
                      ? 'warn'
                      : ''
                }`}
              >
                {tailscalePwa?.ServeConfigured
                  ? 'HTTPS Active'
                  : tailscalePwa?.PendingAuthUrl
                    ? 'Awaiting Tailscale Auth'
                    : tailscalePwa?.TailscaleAvailable
                      ? 'HTTPS Not Configured'
                      : 'Checking Tailscale...'}
              </span>
              <button
                type="button"
                className="hk-btn sm"
                disabled={tailscaleBusy}
                onClick={async () => {
                  setTailscaleBusy(true);
                  try {
                    const res = await apiClient.getTailscalePwaStatus();
                    setTailscalePwa(res);
                  } catch (err) {
                    showToast(err instanceof Error ? err.message : 'Status check failed.');
                  } finally {
                    setTailscaleBusy(false);
                  }
                }}
              >
                🔄 Refresh
              </button>
              <button
                type="button"
                className="hk-btn sm primary"
                disabled={tailscaleBusy || tailscalePwa?.TailscaleAvailable === false}
                onClick={async () => {
                  setTailscaleBusy(true);
                  try {
                    const res = await apiClient.enableTailscalePwa();
                    setTailscalePwa(res);
                    showToast(res.StatusMessage);
                  } catch (err) {
                    showToast(err instanceof Error ? err.message : 'Failed to enable Tailscale Serve.');
                  } finally {
                    setTailscaleBusy(false);
                  }
                }}
              >
                {tailscaleBusy
                  ? 'Provisioning...'
                  : tailscalePwa?.ServeConfigured
                    ? '⚡ Re-Apply Tailscale HTTPS'
                    : '⚡ Enable Tailscale HTTPS (PWA)'}
              </button>
              {tailscalePwa?.ServeConfigured && (
                <button
                  type="button"
                  className="hk-btn sm danger"
                  disabled={tailscaleBusy}
                  onClick={async () => {
                    setTailscaleBusy(true);
                    try {
                      const res = await apiClient.disableTailscalePwa();
                      setTailscalePwa(res);
                      showToast('Reset Tailscale Serve HTTPS configuration.');
                    } catch (err) {
                      showToast(err instanceof Error ? err.message : 'Reset failed.');
                    } finally {
                      setTailscaleBusy(false);
                    }
                  }}
                >
                  Reset Serve
                </button>
              )}
            </div>
          </div>

          <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            <div style={{ fontSize: 13, color: 'var(--text-secondary)' }}>
              {tailscalePwa?.StatusMessage || 'Inspecting local Tailscale daemon...'}
            </div>

            {tailscalePwa?.PendingAuthUrl && !tailscalePwa.ServeConfigured && (
              <div
                style={{
                  padding: '10px 12px',
                  borderRadius: 8,
                  border: '1px solid var(--warn)',
                  background: 'rgba(245, 158, 11, 0.08)',
                  display: 'flex',
                  flexWrap: 'wrap',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  gap: 10,
                }}
              >
                <div style={{ fontSize: 13 }}>
                  <strong>One-Time Tailscale Authorization Required:</strong> Click to approve
                  Tailscale Serve &amp; HTTPS Certificates on your tailnet. Housekeeping is
                  listening in the background and will finish activating HTTPS automatically as soon
                  as you click <strong>Enable</strong>.
                </div>
                <a
                  href={tailscalePwa.PendingAuthUrl}
                  target="_blank"
                  rel="noreferrer"
                  className="hk-btn sm primary"
                  style={{ textDecoration: 'none' }}
                >
                  🔑 Authorize Tailscale Serve &amp; HTTPS ↗
                </a>
              </div>
            )}

            {tailscalePwa?.HttpsPwaUrl && (
              <div className="hk-field">
                <label className="hk-field-label">
                  Android &amp; iOS Standalone PWA Install URL (MagicDNS HTTPS)
                </label>
                <div className="hk-row">
                  <input
                    type="text"
                    readOnly
                    className="hk-input mono"
                    style={{ flex: 1 }}
                    value={tailscalePwa.HttpsPwaUrl}
                  />
                  <button
                    type="button"
                    className="hk-btn sm"
                    onClick={() => {
                      if (!navigator.clipboard?.writeText) {
                        showToast('Clipboard API unavailable in non-HTTPS context. Copy URL directly.');
                        return;
                      }
                      void navigator.clipboard
                        .writeText(tailscalePwa.HttpsPwaUrl)
                        .then(() => showToast(`Copied ${tailscalePwa.HttpsPwaUrl} to clipboard.`))
                        .catch((err) => {
                          console.debug('[SettingsPage] PWA URL clipboard write failed:', err);
                          showToast('Clipboard write blocked by browser. Copy URL directly.');
                        });
                    }}
                  >
                    📋 Copy URL
                  </button>
                  {tailscalePwa.ServeConfigured && (
                    <a
                      href={tailscalePwa.HttpsPwaUrl}
                      target="_blank"
                      rel="noreferrer"
                      className="hk-btn sm success"
                      style={{ textDecoration: 'none' }}
                    >
                      🔒 Open HTTPS PWA ↗
                    </a>
                  )}
                </div>
              </div>
            )}
          </div>
        </div>
        </div>
      )}

      {/* 5. GREENROOM (:8420) & SISTER-APP COEXISTENCE */}
      {section === 'greenroom' && (
        <SettingsIntegrationsAndDoctorCard
          mode="greenroom"
          config={config}
          authStatus={authStatus}
          greenroomStatus={greenroomStatus}
          tailscalePwa={tailscalePwa}
          deps={deps}
          disks={disks}
          doctorBusy={doctorBusy}
          setDoctorBusy={setDoctorBusy}
          setDeps={setDeps}
          updateConfig={updateConfig}
          flushSaveNow={flushSaveNow}
          refreshGreenroomStatus={refreshGreenroomStatus}
          showToast={showToast}
        />
      )}

      {/* 6. VERBOSITY & NOTIFICATIONS */}
      {section === 'notifications' && (
        <div className="hk-grid-2">
          <div className="hk-card">
            <h3 className="hk-card-title">📋 Terminal Buffer &amp; Custom Tag Matrix</h3>
            <div className="hk-grid-3" style={{ marginTop: 10, marginBottom: 12 }}>
              <div className="hk-field">
                <label className="hk-field-label">Terminal Buffer Lines</label>
                <input
                  type="number"
                  min={200}
                  max={5000}
                  className="hk-input"
                  value={config.TerminalMaxLines}
                  onChange={(e) =>
                    updateConfig((d) => (d.TerminalMaxLines = Number(e.target.value) || 1500))
                  }
                />
              </div>
              <div className="hk-field">
                <label className="hk-field-label">Terminal Verbosity</label>
                <select
                  className="hk-select"
                  value={config.TerminalVerbosity}
                  onChange={(e) =>
                    updateConfig((d) => (d.TerminalVerbosity = e.target.value as VerbosityLevel))
                  }
                >
                  <option value="Everything">Everything</option>
                  <option value="ProgressAndStatus">Progress &amp; Status</option>
                  <option value="Quiet">Quiet</option>
                  <option value="AlertsOnly">Alerts Only</option>
                  <option value="Custom">Custom Tags</option>
                </select>
              </div>
              <div className="hk-field">
                <label className="hk-field-label">Footer Bar Verbosity</label>
                <select
                  className="hk-select"
                  value={config.FooterVerbosity}
                  onChange={(e) =>
                    updateConfig(
                      (d) => (d.FooterVerbosity = e.target.value as FooterVerbosityLevel)
                    )
                  }
                >
                  <option value="ProgressAndStatus">Progress &amp; Status</option>
                  <option value="Everything">Everything</option>
                  <option value="Quiet">Quiet</option>
                  <option value="AlertsOnly">Alerts Only</option>
                  <option value="Custom">Custom Tags</option>
                </select>
              </div>
            </div>

            <div className="hk-field">
              <label className="hk-field-label">Custom Terminal Tags (Active when Verbosity = Custom)</label>
              <div className="hk-row">
                {ALL_LOG_TAGS.map((tag) => {
                  const active = config.CustomVerbosity.TerminalTags.includes(tag);
                  const tagHint = SETTINGS_HINTS.customTagToggle(tag);
                  return (
                    <label key={tag} className="hk-checkbox-label hk-has-hint" title={tagHint}>
                      <input
                        type="checkbox"
                        className="hk-has-hint"
                        title={tagHint}
                        checked={active}
                        onChange={(e) =>
                          updateConfig((d) => {
                            if (e.target.checked) {
                              if (!d.CustomVerbosity.TerminalTags.includes(tag)) {
                                d.CustomVerbosity.TerminalTags.push(tag);
                              }
                            } else {
                              d.CustomVerbosity.TerminalTags =
                                d.CustomVerbosity.TerminalTags.filter((x) => x !== tag);
                            }
                          })
                        }
                      />
                      {tag}
                    </label>
                  );
                })}
              </div>
            </div>
          </div>

          <div className="hk-card">
            <h3 className="hk-card-title">🔔 Tray Balloon &amp; Discord Webhook Alerts</h3>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 10, marginTop: 10 }}>
              <label className="hk-checkbox-label hk-has-hint" title={SETTINGS_HINTS.enableTrayToasts}>
                <input
                  type="checkbox"
                  className="hk-has-hint"
                  title={SETTINGS_HINTS.enableTrayToasts}
                  checked={config.EnableToastNotifications}
                  onChange={(e) =>
                    updateConfig((d) => (d.EnableToastNotifications = e.target.checked))
                  }
                />
                Enable Windows Tray Balloon Notifications
              </label>
              <label className="hk-checkbox-label hk-has-hint" title={SETTINGS_HINTS.enableDiscordWebhook}>
                <input
                  type="checkbox"
                  className="hk-has-hint"
                  title={SETTINGS_HINTS.enableDiscordWebhook}
                  checked={config.EnableDiscordWebhook}
                  onChange={(e) => updateConfig((d) => (d.EnableDiscordWebhook = e.target.checked))}
                />
                Enable Discord Rich Embed Webhook Notifications
              </label>

              <div className="hk-field">
                <label className="hk-field-label">Discord Webhook URL</label>
                <input
                  type="password"
                  className="hk-input mono"
                  value={config.DiscordWebhookUrl}
                  onChange={(e) => updateConfig((d) => (d.DiscordWebhookUrl = e.target.value))}
                  placeholder="https://discord.com/api/webhooks/..."
                />
              </div>

              <div className="hk-grid-2">
                <label className="hk-checkbox-label hk-has-hint" title={SETTINGS_HINTS.discordQueueStart}>
                  <input
                    type="checkbox"
                    className="hk-has-hint"
                    title={SETTINGS_HINTS.discordQueueStart}
                    checked={config.DiscordNotifyQueueStart}
                    onChange={(e) =>
                      updateConfig((d) => (d.DiscordNotifyQueueStart = e.target.checked))
                    }
                  />
                  Notify Queue Start
                </label>
                <label className="hk-checkbox-label hk-has-hint" title={SETTINGS_HINTS.discordQueueFinish}>
                  <input
                    type="checkbox"
                    className="hk-has-hint"
                    title={SETTINGS_HINTS.discordQueueFinish}
                    checked={config.DiscordNotifyQueueFinish}
                    onChange={(e) =>
                      updateConfig((d) => (d.DiscordNotifyQueueFinish = e.target.checked))
                    }
                  />
                  Notify Queue Finish
                </label>
                <label className="hk-checkbox-label hk-has-hint" title={SETTINGS_HINTS.discordJobFinish}>
                  <input
                    type="checkbox"
                    className="hk-has-hint"
                    title={SETTINGS_HINTS.discordJobFinish}
                    checked={config.DiscordNotifyJobFinish}
                    onChange={(e) =>
                      updateConfig((d) => (d.DiscordNotifyJobFinish = e.target.checked))
                    }
                  />
                  Notify Individual Job Finish
                </label>
                <label className="hk-checkbox-label hk-has-hint" title={SETTINGS_HINTS.discordErrors}>
                  <input
                    type="checkbox"
                    className="hk-has-hint"
                    title={SETTINGS_HINTS.discordErrors}
                    checked={config.DiscordNotifyErrors}
                    onChange={(e) =>
                      updateConfig((d) => (d.DiscordNotifyErrors = e.target.checked))
                    }
                  />
                  Notify Pipeline Errors
                </label>
              </div>

              <div className="hk-row">
                <button
                  type="button"
                  className="hk-btn sm primary"
                  onClick={async () => {
                    await flushSaveNow();
                    try {
                      const res = await apiClient.testDiscord();
                      showToast(res.message);
                    } catch (err) {
                      showToast(err instanceof Error ? err.message : 'Discord test failed.');
                    }
                  }}
                >
                  📨 Send Test Discord Embed
                </button>
                <button
                  type="button"
                  className="hk-btn sm"
                  onClick={async () => {
                    const res = await apiClient.testToast();
                    showToast(res.message);
                  }}
                >
                  🔔 Test Notification
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* 7. REMOTE FLEET NODES */}
      {section === 'fleet' && (
        <div className="hk-card">
          <div className="hk-card-header">
            <div>
              <h3 className="hk-card-title">🖧 Remote LAN &amp; Tailnet Housekeeping Nodes</h3>
              <p className="hk-card-subtitle">
                Nodes configured here appear in the Top-Bar Target Node Switcher and Dual-Pane
                Terminal. Supports Housekeeping HTTP API (<code>:5178</code>) + SMB fallback.
              </p>
            </div>
            <div className="hk-row">
              <button
                type="button"
                className="hk-btn sm"
                disabled={probingFleet}
                onClick={() => void handleProbeFleet()}
              >
                {probingFleet ? 'Probing...' : '📡 Probe Fleet Status'}
              </button>
              <button
                type="button"
                className="hk-btn sm primary"
                onClick={() =>
                  updateConfig((d) => {
                    d.RemoteNodes.push({
                      Id: `node-${Math.random().toString(36).slice(2, 8)}`,
                      Name: `Remote Node ${d.RemoteNodes.length + 1}`,
                      HostOrIp: '100.64.0.10',
                      HttpApiPort: 5178,
                      ApiToken: '',
                      PreferHttpApi: true,
                      RemoteAppPath: '\\\\100.64.0.10\\Housekeeping',
                      ConnectionType: 'WindowsUncShare',
                      Port: 445,
                      Username: '',
                    });
                  })
                }
              >
                + Add Remote Node
              </button>
            </div>
          </div>

          <div className="table-wrapper" tabIndex={0} role="region" aria-label="Remote fleet nodes">
            <table className="hk-table">
              <thead>
                <tr>
                  <th className="hk-has-hint" title={SETTINGS_HINTS.thNodeCol('Node Name')}>Name</th>
                  <th className="hk-has-hint" title={SETTINGS_HINTS.thNodeCol('Tailscale / LAN Host')}>Tailscale / LAN Host or IP</th>
                  <th className="hk-has-hint" title={SETTINGS_HINTS.thNodeCol('HTTP Port')}>HTTP Port</th>
                  <th className="hk-has-hint" title={SETTINGS_HINTS.thNodeCol('Node API Token')}>Node API Token</th>
                  <th className="hk-has-hint" title={SETTINGS_HINTS.thNodeCol('Prefer HTTP')}>Prefer HTTP</th>
                  <th className="hk-has-hint" title={SETTINGS_HINTS.thNodeCol('SMB Share Path')}>SMB Share Path (Fallback)</th>
                  <th className="hk-has-hint" title={SETTINGS_HINTS.thNodeCol('Probe Status')}>Status</th>
                  <th className="hk-has-hint" title={SETTINGS_HINTS.thNodeCol('Node Actions')}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {config.RemoteNodes.length === 0 && (
                  <tr>
                    <td colSpan={8} style={{ color: 'var(--text-secondary)' }}>
                      No remote nodes configured yet. Add another PC running Housekeeping on your
                      LAN or Tailscale tailnet to control it from the Top Bar.
                    </td>
                  </tr>
                )}
                {config.RemoteNodes.map((n, idx) => {
                  const probed = fleetStatus[n.Id];
                  return (
                    <tr key={n.Id}>
                      <td>
                        <input
                          type="text"
                          className="hk-input"
                          value={n.Name}
                          onChange={(e) =>
                            updateConfig((d) => {
                              d.RemoteNodes[idx].Name = e.target.value;
                            })
                          }
                        />
                      </td>
                      <td>
                        <input
                          type="text"
                          className="hk-input mono"
                          value={n.HostOrIp}
                          onChange={(e) =>
                            updateConfig((d) => {
                              d.RemoteNodes[idx].HostOrIp = e.target.value;
                            })
                          }
                          placeholder="100.x.y.z or server.ts.net"
                        />
                      </td>
                      <td>
                        <input
                          type="number"
                          className="hk-input mono"
                          style={{ width: 82 }}
                          value={n.HttpApiPort || 5178}
                          onChange={(e) =>
                            updateConfig((d) => {
                              d.RemoteNodes[idx].HttpApiPort = Number(e.target.value) || 5178;
                            })
                          }
                        />
                      </td>
                      <td>
                        <input
                          type="password"
                          className="hk-input mono"
                          value={n.ApiToken}
                          onChange={(e) =>
                            updateConfig((d) => {
                              d.RemoteNodes[idx].ApiToken = e.target.value;
                            })
                          }
                          placeholder="Paste remote X-Housekeeping-Token"
                        />
                      </td>
                      <td style={{ textAlign: 'center' }}>
                        <input
                          type="checkbox"
                          className="hk-has-hint"
                          title={SETTINGS_HINTS.preferHttpApi}
                          checked={n.PreferHttpApi}
                          onChange={(e) =>
                            updateConfig((d) => {
                              d.RemoteNodes[idx].PreferHttpApi = e.target.checked;
                            })
                          }
                        />
                      </td>
                      <td>
                        <input
                          type="text"
                          className="hk-input mono"
                          value={n.RemoteAppPath}
                          onChange={(e) =>
                            updateConfig((d) => {
                              d.RemoteNodes[idx].RemoteAppPath = e.target.value;
                              d.RemoteNodes[idx].ConnectionType =
                                'WindowsUncShare' as RemoteConnectionType;
                            })
                          }
                        />
                      </td>
                      <td>
                        <span className={`hk-pill ${probed?.IsReachable ? 'ok' : ''}`}>
                          {probed?.StatusMessage || 'Unprobed'}
                        </span>
                      </td>
                      <td>
                        <div className="hk-row" style={{ gap: 4 }}>
                          <button
                            type="button"
                            className="hk-btn sm success"
                            onClick={async () => {
                              await flushSaveNow();
                              try {
                                await apiClient.sendNodeCommand(n.Id, 'StartQueue');
                                showToast(`Dispatched StartQueue to '${n.Name}'.`);
                              } catch (err) {
                                showToast(err instanceof Error ? err.message : 'Command failed.');
                              }
                            }}
                          >
                            ▶
                          </button>
                          <button
                            type="button"
                            className="hk-btn sm"
                            onClick={async () => {
                              await flushSaveNow();
                              try {
                                await apiClient.pushNodeSettings(n.Id);
                                showToast(`Pushed settings to '${n.Name}'.`);
                              } catch (err) {
                                showToast(err instanceof Error ? err.message : 'Push failed.');
                              }
                            }}
                            title="Push settings.json via SMB"
                          >
                            📤 Settings
                          </button>
                          <button
                            type="button"
                            className="hk-btn sm danger"
                            onClick={() =>
                              updateConfig((d) => {
                                d.RemoteNodes.splice(idx, 1);
                              })
                            }
                          >
                            ✕
                          </button>
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* 8. SYSTEM DOCTOR */}
      {section === 'doctor' && (
        <SettingsIntegrationsAndDoctorCard
          mode="doctor"
          config={config}
          authStatus={authStatus}
          greenroomStatus={greenroomStatus}
          tailscalePwa={tailscalePwa}
          deps={deps}
          disks={disks}
          doctorBusy={doctorBusy}
          setDoctorBusy={setDoctorBusy}
          setDeps={setDeps}
          updateConfig={updateConfig}
          flushSaveNow={flushSaveNow}
          refreshGreenroomStatus={refreshGreenroomStatus}
          showToast={showToast}
        />
      )}
    </div>
  );
};
