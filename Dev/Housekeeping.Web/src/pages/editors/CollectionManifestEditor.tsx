import React, { useEffect, useState } from 'react';
import { ComboSelect } from '../../components/ComboSelect';
import { HybridPathPicker } from '../../components/HybridPathPicker';
import { useAppState } from '../../context/AppStateContext';
import { apiClient } from '../../services/apiClient';
import type {
  AuditStructureMode,
  CollectionManifestConfig,
  HashSidecarMode,
  ManifestInspectTarget,
  ManifestMatchMode,
  RegexPlaygroundResultDto,
  SidecarVerifyMode,
} from '../../types/models';

interface Props {
  jobId: string;
  cfg: CollectionManifestConfig;
  onChange: (mutator: (d: CollectionManifestConfig) => void) => void;
}

export const CollectionManifestEditor: React.FC<Props> = ({ jobId, cfg, onChange }) => {
  const {
    config,
    consoleCatalog,
    flushSaveNow,
    pipelineState,
    setPipelineState,
    setGlobalDrawerOpen,
    showToast,
  } = useAppState();

  const [verifyMode, setVerifyMode] = useState<SidecarVerifyMode>('FastHeaderAndHeal');
  const [verifying, setVerifying] = useState(false);
  const [selectedPreset, setSelectedPreset] = useState('');
  const [samplePath, setSamplePath] = useState(
    'V:\\Media\\Movies\\The Matrix (1999) - BR (1080p).mkv'
  );
  const [regexResult, setRegexResult] = useState<RegexPlaygroundResultDto | null>(null);

  const catalog = config?.ManifestDropdownCatalog;
  const locationOptions = catalog?.LocationIds || ['Local-SSD', 'NAS-V', 'NAS-W', 'NAS-X', 'NAS-Y', 'NAS-Z'];
  const labelOptions = catalog?.Labels || ['Media', 'Movies', 'TV Shows', 'Anime', 'Master Set'];
  const platformOptions = [
    ...(catalog?.Platforms || ['Auto-Detect']),
    ...consoleCatalog.map((c) => c.CanonicalName),
  ].filter((v, i, arr) => arr.indexOf(v) === i);
  const extFilterOptions = catalog?.ExtensionsFilters || [
    'mkv, mp4, avi',
    '.7z, .zip, .chd, .iso, .rvz',
    '* (All Files)',
  ];

  // Live C#-backed .NET Regex Playground evaluation
  useEffect(() => {
    let cancelled = false;
    const t = window.setTimeout(() => {
      apiClient
        .testManifestRegex(samplePath, cfg.FieldParseRules || [])
        .then((res) => {
          if (!cancelled) setRegexResult(res);
        })
        .catch(() => {});
    }, 280);
    return () => {
      cancelled = true;
      window.clearTimeout(t);
    };
  }, [samplePath, cfg.FieldParseRules]);

  const handleLoadPresetBundle = async (bundleKey: string) => {
    if (!bundleKey) return;
    setSelectedPreset('');
    try {
      const rules = await apiClient.getManifestPresetBundle(bundleKey);
      onChange((d) => {
        for (const r of rules) {
          d.FieldParseRules.push(r);
        }
      });
      showToast(`Added ${rules.length} parsing rule(s) from preset '${bundleKey}'.`);
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Failed loading preset bundle.');
    }
  };

  const isVerifierRunning =
    pipelineState.IsRunning && pipelineState.ActiveQueueName === '.hash Integrity Verifier';

  const handleRunVerifier = async () => {
    setVerifying(true);
    try {
      const ok = await flushSaveNow();
      if (!ok) {
        showToast('Fix configuration validation errors before running .hash verifier.');
        return;
      }
      const res = await apiClient.runHashVerifier(jobId, 'CollectionManifest', verifyMode);
      if (res.state) setPipelineState(res.state);
      setGlobalDrawerOpen(true);
      showToast(res.message);
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Verifier failed.');
    } finally {
      setVerifying(false);
    }
  };

  return (
    <>
      <div className="hk-info-banner">
        <strong>📋 What This Module Does: Media Manifest &amp; Bit Rot Auditor</strong>
        <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 2 }}>
          Recursively scans directories across local drives and network NAS paths. Catalogs files,
          parses metadata (clean titles, years, seasons, episodes, regions, resolutions), applies
          custom C# regex/text extraction rules to populate dynamic CSV/JSON/HTML columns, detects
          bit rot against .hash sidecars with ShadowLibrary parity and auto-healing, computes
          composite hashes for folder dumps, mirrors manifests to secondary backup storage, and
          notifies Greenroom (<code>:8420</code>) on manifest updates.
        </div>
      </div>

      {/* 1. General & Output Formats */}
      <div className="hk-card">
        <h3 className="hk-card-title">⚙ General &amp; Output Formats</h3>

        <div className="hk-row" style={{ gap: 16, marginTop: 10, marginBottom: 12 }}>
          <label className="hk-checkbox-label">
            <input
              type="checkbox"
              checked={cfg.UnifiedMasterManifest}
              onChange={(e) => onChange((d) => (d.UnifiedMasterManifest = e.target.checked))}
            />
            Unified Master Manifest
          </label>

          <div className="hk-row" style={{ gap: 6 }}>
            <span className="hk-field-label">Manifest Base Name:</span>
            <input
              type="text"
              className="hk-input"
              style={{ width: 220 }}
              value={cfg.UnifiedManifestName}
              onChange={(e) => onChange((d) => (d.UnifiedManifestName = e.target.value))}
              placeholder="Plex Catalog Master"
            />
          </div>

          <div className="hk-row" style={{ gap: 6 }}>
            <span className="hk-field-label">CSV Delimiter:</span>
            <select
              className="hk-select"
              style={{ width: 'auto' }}
              value={cfg.CsvDelimiter || ','}
              onChange={(e) => onChange((d) => (d.CsvDelimiter = e.target.value))}
            >
              <option value=",">Comma (,)</option>
              <option value=";">Semicolon (;)</option>
              <option value="&#9;">Tab (\t)</option>
              <option value="|">Pipe (|)</option>
            </select>
          </div>

          <label className="hk-checkbox-label">
            <input
              type="checkbox"
              checked={cfg.ExportCsv}
              onChange={(e) => onChange((d) => (d.ExportCsv = e.target.checked))}
            />
            CSV (.csv)
          </label>
          <label className="hk-checkbox-label">
            <input
              type="checkbox"
              checked={cfg.ExportJson}
              onChange={(e) => onChange((d) => (d.ExportJson = e.target.checked))}
            />
            JSON (.json)
          </label>
          <label className="hk-checkbox-label">
            <input
              type="checkbox"
              checked={cfg.ExportHtmlReport}
              onChange={(e) => onChange((d) => (d.ExportHtmlReport = e.target.checked))}
            />
            Visual HTML (.html)
          </label>
          <label className="hk-checkbox-label" style={{ color: 'var(--greenroom)' }}>
            <input
              type="checkbox"
              checked={cfg.AutoSyncGreenroomOnManifestChange}
              onChange={(e) =>
                onChange((d) => (d.AutoSyncGreenroomOnManifestChange = e.target.checked))
              }
            />
            🟢 Auto-Sync Greenroom (:8420) on Change
          </label>
        </div>

        <div className="hk-grid-2">
          <div className="hk-field">
            <label className="hk-field-label">Primary Output Folder</label>
            <HybridPathPicker
              value={cfg.UnifiedManifestOutputFolder}
              onChange={(next) => onChange((d) => (d.UnifiedManifestOutputFolder = next))}
              placeholder=".\manifests"
            />
          </div>
          <div className="hk-field">
            <label className="hk-field-label">Secondary Mirror Backup Folder</label>
            <HybridPathPicker
              value={cfg.SecondaryMirrorFolder}
              onChange={(next) => onChange((d) => (d.SecondaryMirrorFolder = next))}
              placeholder="Optional cloud / NAS backup folder"
            />
          </div>
        </div>
      </div>

      {/* 2. Integrity & Bit Rot Engine */}
      <div className="hk-card">
        <h3 className="hk-card-title">🔐 Integrity &amp; Bit Rot Engine</h3>

        <div className="hk-row" style={{ gap: 16, marginTop: 10, marginBottom: 10 }}>
          <label className="hk-checkbox-label">
            <input
              type="checkbox"
              checked={cfg.EnableBitRotDetection ?? cfg.EnableFileHashing}
              onChange={(e) =>
                onChange((d) => {
                  d.EnableBitRotDetection = e.target.checked;
                  d.EnableFileHashing = e.target.checked;
                })
              }
            />
            Enable Bit Rot Detection &amp; Warnings
          </label>

          <label className="hk-checkbox-label">
            <input
              type="checkbox"
              checked={cfg.CreateMissingHashFiles ?? cfg.GenerateHashSidecars}
              onChange={(e) =>
                onChange((d) => {
                  d.CreateMissingHashFiles = e.target.checked;
                  d.GenerateHashSidecars = e.target.checked;
                })
              }
            />
            Create Missing .hash Files (Baseline)
          </label>

          <label className="hk-checkbox-label">
            <input
              type="checkbox"
              checked={cfg.EnableBitRotAutoHeal ?? false}
              onChange={(e) => onChange((d) => (d.EnableBitRotAutoHeal = e.target.checked))}
            />
            Auto-Heal Repacked Containers
          </label>

          <label className="hk-checkbox-label">
            <input
              type="checkbox"
              checked={cfg.CheckInnerArchiveAndContainer ?? cfg.DeepInspectAndHashInnerRoms}
              onChange={(e) =>
                onChange((d) => {
                  d.CheckInnerArchiveAndContainer = e.target.checked;
                  d.DeepInspectAndHashInnerRoms = e.target.checked;
                })
              }
            />
            Two-Step Archive Inspection (Outer + Inner Fallback)
          </label>

          <label className="hk-checkbox-label">
            <input
              type="checkbox"
              checked={cfg.SkipExistingVerifiedHashes}
              onChange={(e) => onChange((d) => (d.SkipExistingVerifiedHashes = e.target.checked))}
            />
            Skip Existing Verified Hashes in CSV
          </label>
        </div>

        <div className="hk-grid-3" style={{ marginBottom: 10 }}>
          <div className="hk-field">
            <label className="hk-field-label">Hash Placement Strategy</label>
            <select
              className="hk-select"
              value={cfg.HashFilePlacement || 'SidecarBeside'}
              onChange={(e) =>
                onChange((d) => (d.HashFilePlacement = e.target.value as HashSidecarMode))
              }
            >
              <option value="SidecarBeside">Sidecar Beside File/Archive (.hash)</option>
              <option value="PackageInsideArchive">Package Inside .7z Archive (.hash)</option>
              <option value="None">Disabled (Do not write .hash files)</option>
            </select>
          </div>

          <div className="hk-field">
            <label className="hk-field-label">Algorithm</label>
            <select
              className="hk-select"
              value={cfg.HashAlgorithm || 'SHA-1'}
              onChange={(e) => onChange((d) => (d.HashAlgorithm = e.target.value))}
            >
              <option value="SHA-1">SHA-1 (Default / ShadowLibrary Parity)</option>
              <option value="CRC32">CRC32</option>
              <option value="MD5">MD5</option>
              <option value="SHA-256">SHA-256</option>
            </select>
          </div>

          <div className="hk-field">
            <label className="hk-field-label">File vs Folder Hashing Logic</label>
            <select
              className="hk-select"
              value={cfg.StructureMode}
              onChange={(e) =>
                onChange((d) => (d.StructureMode = e.target.value as AuditStructureMode))
              }
            >
              <option value="Auto">
                ⚡ Auto-Detect (Single Files + PS3/WiiU/BDMV Folder Units)
              </option>
              <option value="FilesOnly">Files Only (Every File Hashed Individually)</option>
              <option value="FolderDumpsOnly">Folder Dumps Only (Subfolders as Atomic Units)</option>
            </select>
          </div>
        </div>

        <div className="hk-grid-1" style={{ marginBottom: 10 }}>
          <div className="hk-field">
            <label className="hk-field-label">Excluded Companion Extensions (CSV)</label>
            <input
              type="text"
              className="hk-input mono"
              value={cfg.ExcludedExtensions}
              onChange={(e) => onChange((d) => (d.ExcludedExtensions = e.target.value))}
              placeholder=".nfo, .txt, .sfv, .jpg, .png"
            />
          </div>
        </div>

        <div
          className="hk-row"
          style={{
            padding: '8px 12px',
            borderRadius: 'var(--radius-md)',
            background: 'var(--bg-elevated)',
            border: '1px solid var(--border-subtle)',
            justifyContent: 'space-between',
          }}
        >
          <div className="hk-row">
            <strong style={{ fontSize: 12.5, color: 'var(--accent)' }}>
              🔍 .hash Integrity &amp; Legacy Outer Archive Verifier:
            </strong>
            <select
              className="hk-select"
              style={{ width: 'auto' }}
              value={verifyMode}
              onChange={(e) => setVerifyMode(e.target.value as SidecarVerifyMode)}
            >
              <option value="FastHeaderAndHeal">⚡ Fast 7z Header &amp; Legacy Audit + Heal</option>
              <option value="DeepCryptographicAndHeal">🔬 Deep Full Stream Re-Hash + Heal</option>
              <option value="DryRunReportOnly">👁 Audit Only (No File Changes)</option>
            </select>
            {isVerifierRunning && (
              <span className="hk-pill accent">
                ⟳ {pipelineState.SubprocessStatusText || 'Verifying .hash sidecars...'} (
                {pipelineState.ProgressPercent.toFixed(0)}%)
              </span>
            )}
          </div>
          <div className="hk-row">
            {isVerifierRunning && (
              <button
                type="button"
                className="hk-btn sm danger"
                onClick={() =>
                  void apiClient.stopPipeline().then((r) => {
                    setPipelineState(r.state);
                    showToast('Stopping .hash verifier...');
                  })
                }
              >
                ⏹ Stop Verifier
              </button>
            )}
            <button
              type="button"
              className="hk-btn sm primary"
              disabled={verifying || pipelineState.IsRunning}
              onClick={() => void handleRunVerifier()}
            >
              {verifying || isVerifierRunning
                ? '⟳ Verifying & Healing .hash...'
                : '🔍 Verify & Heal .hash Files'}
            </button>
          </div>
        </div>
      </div>

      {/* 3. Target Directories & Locations (Zero Vertical Text Clipping!) */}
      <div className="hk-card">
        <div className="hk-card-header">
          <div>
            <h3 className="hk-card-title">📁 Target Directories &amp; Locations</h3>
            <p className="hk-card-subtitle">
              Add local drives, mapped NAS letters (<code>V:\Media</code>–<code>Z:\Media</code>), or
              UNC shares (<code>\\NAS\Media</code>).
            </p>
          </div>
          <button
            type="button"
            className="hk-btn sm primary"
            onClick={() =>
              onChange((d) => {
                d.TargetFolders.push({
                  Enabled: true,
                  LocationId: `NAS-${d.TargetFolders.length + 1}`,
                  Label: 'Media',
                  FolderPath: '',
                  IncludeSubdirectories: true,
                  MaxSubdirectoryDepth: 0,
                  SystemSelection: 'Auto-Detect',
                  StructureMode: 'Auto',
                  TreatSubfoldersAsGameUnits: false,
                  ExtensionsFilter: 'mkv, mp4, avi',
                  ExcludedFolderNames: 'sample,extras,.deleted,$RECYCLE.BIN,@eaDir',
                  MinimumFileSizeBytes: 0,
                });
              })
            }
          >
            + Add Target Folder
          </button>
        </div>

        <div
          className="table-wrapper"
          tabIndex={0}
          role="region"
          aria-label="Target directories and locations"
        >
          <table className="hk-table stack-mobile">
            <thead>
              <tr>
                <th style={{ width: 52 }}>Enabled</th>
                <th style={{ width: 125 }}>Location ID</th>
                <th style={{ width: 145 }}>Label / Collection</th>
                <th>Target Folder Path (Local or UNC: \\NAS)</th>
                <th style={{ width: 70 }}>Subfolders</th>
                <th style={{ width: 165 }}>Category / System</th>
                <th style={{ width: 155 }}>Hash Target</th>
                <th style={{ width: 160 }}>Ext Filter</th>
                <th style={{ width: 65 }}>Del</th>
              </tr>
            </thead>
            <tbody>
              {(cfg.TargetFolders || []).length === 0 && (
                <tr>
                  <td colSpan={9} style={{ color: 'var(--text-secondary)' }}>
                    No target folders added yet. Click &ldquo;+ Add Target Folder&rdquo; above.
                  </td>
                </tr>
              )}
              {(cfg.TargetFolders || []).map((tf, idx) => (
                <tr key={idx}>
                  <td data-label="Enabled">
                    <input
                      type="checkbox"
                      checked={tf.Enabled}
                      onChange={(e) =>
                        onChange((d) => {
                          d.TargetFolders[idx].Enabled = e.target.checked;
                        })
                      }
                    />
                  </td>
                  <td data-label="Location ID">
                    <ComboSelect
                      value={tf.LocationId}
                      options={locationOptions}
                      onChange={(v) =>
                        onChange((d) => {
                          d.TargetFolders[idx].LocationId = v;
                        })
                      }
                    />
                  </td>
                  <td data-label="Label">
                    <ComboSelect
                      value={tf.Label}
                      options={labelOptions}
                      onChange={(v) =>
                        onChange((d) => {
                          d.TargetFolders[idx].Label = v;
                        })
                      }
                    />
                  </td>
                  <td data-label="Folder Path" style={{ minWidth: 230 }}>
                    <HybridPathPicker
                      value={tf.FolderPath}
                      compactButton
                      onChange={(next) =>
                        onChange((d) => {
                          d.TargetFolders[idx].FolderPath = next;
                        })
                      }
                    />
                  </td>
                  <td data-label="Subfolders" style={{ textAlign: 'center' }}>
                    <input
                      type="checkbox"
                      checked={tf.IncludeSubdirectories}
                      onChange={(e) =>
                        onChange((d) => {
                          d.TargetFolders[idx].IncludeSubdirectories = e.target.checked;
                        })
                      }
                    />
                  </td>
                  <td data-label="Category / System">
                    <ComboSelect
                      value={tf.SystemSelection}
                      options={platformOptions}
                      onChange={(v) =>
                        onChange((d) => {
                          d.TargetFolders[idx].SystemSelection = v;
                        })
                      }
                    />
                  </td>
                  <td data-label="Hash Target">
                    <select
                      className="hk-select"
                      value={tf.StructureMode || 'Auto'}
                      onChange={(e) =>
                        onChange((d) => {
                          const mode = e.target.value as AuditStructureMode;
                          d.TargetFolders[idx].StructureMode = mode;
                          d.TargetFolders[idx].TreatSubfoldersAsGameUnits =
                            mode === 'FolderDumpsOnly';
                        })
                      }
                    >
                      <option value="Auto">⚡ Auto-Detect</option>
                      <option value="FilesOnly">Files Only</option>
                      <option value="FolderDumpsOnly">Folder Units</option>
                    </select>
                  </td>
                  <td data-label="Ext Filter">
                    <ComboSelect
                      value={tf.ExtensionsFilter}
                      mono
                      options={extFilterOptions}
                      onChange={(v) =>
                        onChange((d) => {
                          d.TargetFolders[idx].ExtensionsFilter = v;
                        })
                      }
                      placeholder="mkv, mp4, avi"
                    />
                  </td>
                  <td data-label="Delete">
                    <button
                      type="button"
                      className="hk-btn sm danger"
                      onClick={() =>
                        onChange((d) => {
                          d.TargetFolders.splice(idx, 1);
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

      {/* 4. Custom Text Parsing & CSV Field Extraction + Live C# Regex Playground */}
      <div className="hk-card hk-deferred-section">
        <div className="hk-card-header">
          <div>
            <h3 className="hk-card-title">🧬 Custom Text Parsing &amp; CSV Field Extraction</h3>
            <p className="hk-card-subtitle">
              Extract custom columns from filenames, parent folders, or relative paths into CSV,
              JSON, and HTML reports.
            </p>
          </div>
          <div className="hk-row">
            <select
              className="hk-select"
              style={{ width: 'auto' }}
              value={selectedPreset}
              onChange={(e) => void handleLoadPresetBundle(e.target.value)}
              aria-label="Load preset extraction bundle"
            >
              <option value="">⚡ Select Preset Bundle to Add...</option>
              <option value="PersonalMedia">🎬 Personal Plex / Movies &amp; TV (Title, Year, Show, Season, Episode)</option>
              <option value="PersonalRom">🎮 Retro ROM Collection (Region, Revision, Disc, Language)</option>
              <option value="GeneralMedia">📺 Video Source, Codec &amp; Resolution Tags</option>
              <option value="GeneralAudio">🎵 Music &amp; Audio Metadata (Artist, Album, Track)</option>
              <option value="GeneralFiles">📁 General File Organization &amp; Dates</option>
            </select>
            <button
              type="button"
              className="hk-btn sm primary"
              onClick={() =>
                onChange((d) => {
                  d.FieldParseRules.push({
                    Id: Math.random().toString(36).slice(2, 10),
                    Enabled: true,
                    ColumnName: 'CustomField',
                    FileExtensionFilter: '*',
                    InspectTarget: 'FileName',
                    MatchMode: 'RegexNamedCapture',
                    Pattern: '(?<CustomField>.+)',
                    AssignedValue: '${CustomField}',
                    SplitDelimiter: ' - ',
                    SplitIndex: 0,
                  });
                })
              }
            >
              + Add Rule
            </button>
            {(cfg.FieldParseRules || []).length > 0 && (
              <button
                type="button"
                className="hk-btn sm danger"
                onClick={() =>
                  onChange((d) => {
                    d.FieldParseRules = [];
                  })
                }
              >
                🗑 Clear All
              </button>
            )}
          </div>
        </div>

        <div className="table-wrapper" tabIndex={0} role="region" aria-label="Custom field extraction rules">
          <table className="hk-table">
            <thead>
              <tr>
                <th style={{ width: 48 }}>On</th>
                <th style={{ width: 145 }}>CSV Column Name</th>
                <th style={{ width: 130 }}>Applies To (Exts)</th>
                <th style={{ width: 155 }}>Inspect In</th>
                <th style={{ width: 175 }}>Match Mode</th>
                <th>Match Pattern / C# Regex</th>
                <th style={{ width: 135 }}>Assigned Value</th>
                <th style={{ width: 75 }}>Delim</th>
                <th style={{ width: 65 }}>Index</th>
                <th style={{ width: 55 }}>Del</th>
              </tr>
            </thead>
            <tbody>
              {(cfg.FieldParseRules || []).length === 0 && (
                <tr>
                  <td colSpan={10} style={{ color: 'var(--text-secondary)' }}>
                    No custom extraction rules defined. Choose a Preset Bundle above or click &ldquo;+ Add Rule&rdquo;.
                  </td>
                </tr>
              )}
              {(cfg.FieldParseRules || []).map((rule, idx) => (
                <tr key={rule.Id || idx}>
                  <td>
                    <input
                      type="checkbox"
                      checked={rule.Enabled}
                      onChange={(e) =>
                        onChange((d) => {
                          d.FieldParseRules[idx].Enabled = e.target.checked;
                        })
                      }
                    />
                  </td>
                  <td>
                    <input
                      type="text"
                      className="hk-input"
                      value={rule.ColumnName}
                      onChange={(e) =>
                        onChange((d) => {
                          d.FieldParseRules[idx].ColumnName = e.target.value;
                        })
                      }
                    />
                  </td>
                  <td>
                    <input
                      type="text"
                      className="hk-input mono"
                      value={rule.FileExtensionFilter}
                      onChange={(e) =>
                        onChange((d) => {
                          d.FieldParseRules[idx].FileExtensionFilter = e.target.value;
                        })
                      }
                    />
                  </td>
                  <td>
                    <select
                      className="hk-select"
                      value={rule.InspectTarget}
                      onChange={(e) =>
                        onChange((d) => {
                          d.FieldParseRules[idx].InspectTarget =
                            e.target.value as ManifestInspectTarget;
                        })
                      }
                    >
                      <option value="FileName">File Name</option>
                      <option value="FileNameWithoutExtension">Name (No Ext)</option>
                      <option value="ParentFolderName">Parent Folder</option>
                      <option value="RelativePath">Relative Path</option>
                      <option value="FullPath">Full Path</option>
                    </select>
                  </td>
                  <td>
                    <select
                      className="hk-select"
                      value={rule.MatchMode}
                      onChange={(e) =>
                        onChange((d) => {
                          d.FieldParseRules[idx].MatchMode = e.target.value as ManifestMatchMode;
                        })
                      }
                    >
                      <option value="RegexNamedCapture">Regex Named Capture</option>
                      <option value="RegexPattern">Regex Pattern ($1)</option>
                      <option value="TextContains">Text Contains</option>
                      <option value="SplitToken">Split Delimiter Token</option>
                    </select>
                  </td>
                  <td style={{ minWidth: 250 }}>
                    <input
                      type="text"
                      className="hk-input mono"
                      value={rule.Pattern}
                      onChange={(e) =>
                        onChange((d) => {
                          d.FieldParseRules[idx].Pattern = e.target.value;
                        })
                      }
                    />
                  </td>
                  <td>
                    <input
                      type="text"
                      className="hk-input mono"
                      value={rule.AssignedValue}
                      onChange={(e) =>
                        onChange((d) => {
                          d.FieldParseRules[idx].AssignedValue = e.target.value;
                        })
                      }
                    />
                  </td>
                  <td>
                    <input
                      type="text"
                      className="hk-input mono"
                      value={rule.SplitDelimiter}
                      onChange={(e) =>
                        onChange((d) => {
                          d.FieldParseRules[idx].SplitDelimiter = e.target.value;
                        })
                      }
                    />
                  </td>
                  <td>
                    <input
                      type="number"
                      className="hk-input"
                      value={rule.SplitIndex}
                      onChange={(e) =>
                        onChange((d) => {
                          d.FieldParseRules[idx].SplitIndex = Number(e.target.value) || 0;
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
                          d.FieldParseRules.splice(idx, 1);
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

        {/* Live C#-Backed .NET Regex Rule Test Playground */}
        <div
          style={{
            marginTop: 12,
            padding: 12,
            borderRadius: 'var(--radius-md)',
            background: 'var(--bg-elevated)',
            border: '1px solid var(--border-subtle)',
          }}
        >
          <div className="hk-row" style={{ marginBottom: 8 }}>
            <strong style={{ fontSize: 12.5, color: 'var(--accent)' }}>
              🧪 Live .NET Regex Playground (Evaluated by C# Backend):
            </strong>
            <input
              type="text"
              className="hk-input mono"
              style={{ flex: 1 }}
              value={samplePath}
              onChange={(e) => setSamplePath(e.target.value)}
              placeholder="Enter sample file path to test your rules live..."
            />
          </div>

          {regexResult?.ValidationErrors && regexResult.ValidationErrors.length > 0 && (
            <div className="hk-alert-banner err" style={{ marginBottom: 8 }}>
              <span>⚠ {regexResult.ValidationErrors.join(' • ')}</span>
            </div>
          )}

          <div className="hk-row">
            {regexResult && Object.keys(regexResult.ExtractedFields || {}).length > 0 ? (
              Object.entries(regexResult.ExtractedFields).map(([col, val]) => (
                <span key={col} className="hk-pill ok">
                  <strong>{col}:</strong> {val || '(empty)'}
                </span>
              ))
            ) : (
              <span style={{ fontSize: 12, color: 'var(--text-secondary)' }}>
                Extracted columns for the sample path will appear here in real time.
              </span>
            )}
          </div>
        </div>
      </div>
    </>
  );
};
