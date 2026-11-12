// @module: Housekeeping.Web/pages/editors/CollectionManifestEditor
// @purpose: Module 8 (Collection Manifest & Bit Rot Auditor) editor with 4-mode .hash placement, .tsv/.csv parity, coexistence banner, and live C# Regex Playground.
// @depends-on: react, ../../components/CoexistenceWarningBanner, ../../components/ComboSelect, ../../components/HybridPathPicker, ../../context/AppStateContext, ../../hints/collectionManifest, ../../services/apiClient, ../../types/models, ./manifest/CollectionManifestExportCard
// @exports: CollectionManifestEditor
import React, { useEffect, useState } from 'react';
import { CoexistenceWarningBanner } from '../../components/CoexistenceWarningBanner';
import { ComboSelect } from '../../components/ComboSelect';
import { HybridPathPicker } from '../../components/HybridPathPicker';
import { useAppState } from '../../context/AppStateContext';
import { COLLECTION_MANIFEST_HINTS } from '../../hints/collectionManifest';
import { apiClient } from '../../services/apiClient';
import type {
  AuditStructureMode,
  CollectionManifestConfig,
  ManifestInspectTarget,
  ManifestMatchMode,
  RegexPlaygroundResultDto,
  SidecarVerifyMode,
} from '../../types/models';
import { CollectionManifestExportCard } from './manifest/CollectionManifestExportCard';

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
  const locationOptions = catalog?.LocationIds || [
    'Local-SSD',
    'NAS-V',
    'NAS-W',
    'NAS-X',
    'NAS-Y',
    'NAS-Z',
  ];
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

  useEffect(() => {
    let cancelled = false;
    const t = window.setTimeout(() => {
      apiClient
        .testManifestRegex(samplePath, cfg.FieldParseRules || [])
        .then((res) => {
          if (!cancelled) setRegexResult(res);
        })
        .catch((err) => console.debug('[CollectionManifest] regex playground failed:', err));
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
        for (const r of rules) d.FieldParseRules.push(r);
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
      <CoexistenceWarningBanner jobId={jobId} moduleType="CollectionManifest" />

      <div className="hk-info-banner">
        <strong>📋 What This Module Does: Media &amp; ROM Manifest + Bit Rot Auditor</strong>
        <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 2 }}>
          Recursively indexes local drives and NAS shares, extracts dynamic columns via C# regex
          rules into <code>.csv</code> / <code>.tsv</code> / <code>.json</code> / <code>.html</code>
          , verifies 4-mode <code>.hash</code> sidecars and <code>.archive_hashes.json</code> with
          zero-rewrite ShadowLibrary parity, and notifies Greenroom (<code>:8420</code>) on updates.
        </div>
      </div>

      <CollectionManifestExportCard
        cfg={cfg}
        verifyMode={verifyMode}
        setVerifyMode={setVerifyMode}
        verifying={verifying}
        isVerifierRunning={isVerifierRunning}
        pipelineState={pipelineState}
        onRunVerifier={() => void handleRunVerifier()}
        onStopVerifier={() =>
          void apiClient.stopCurrentJob().then((r) => {
            setPipelineState(r.state);
            showToast('Stopping .hash verifier (preserving pending jobs)...');
          })
        }
        onChange={onChange}
      />

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
                <th className="hk-has-hint" style={{ width: 52 }} title={COLLECTION_MANIFEST_HINTS.thTargetEnabled}>
                  Enabled
                </th>
                <th className="hk-has-hint" style={{ width: 125 }} title={COLLECTION_MANIFEST_HINTS.thLocationId}>
                  Location ID
                </th>
                <th className="hk-has-hint" style={{ width: 145 }} title={COLLECTION_MANIFEST_HINTS.thLabel}>
                  Label / Collection
                </th>
                <th className="hk-has-hint" title={COLLECTION_MANIFEST_HINTS.thFolderPath}>
                  Target Folder Path (Local or UNC: \\NAS)
                </th>
                <th className="hk-has-hint" style={{ width: 70 }} title={COLLECTION_MANIFEST_HINTS.thSubfolders}>
                  Subfolders
                </th>
                <th className="hk-has-hint" style={{ width: 165 }} title={COLLECTION_MANIFEST_HINTS.thSystem}>
                  Category / System
                </th>
                <th className="hk-has-hint" style={{ width: 155 }} title={COLLECTION_MANIFEST_HINTS.thHashTarget}>
                  Hash Target
                </th>
                <th className="hk-has-hint" style={{ width: 160 }} title={COLLECTION_MANIFEST_HINTS.thExtFilter}>
                  Ext Filter
                </th>
                <th className="hk-has-hint" style={{ width: 65 }} title={COLLECTION_MANIFEST_HINTS.thDelete}>
                  Del
                </th>
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
                      className="hk-has-hint"
                      title={COLLECTION_MANIFEST_HINTS.targetRowEnabled}
                      checked={tf.Enabled}
                      onChange={(e) => onChange((d) => (d.TargetFolders[idx].Enabled = e.target.checked))}
                    />
                  </td>
                  <td data-label="Location ID">
                    <ComboSelect
                      value={tf.LocationId}
                      options={locationOptions}
                      onChange={(v) => onChange((d) => (d.TargetFolders[idx].LocationId = v))}
                    />
                  </td>
                  <td data-label="Label">
                    <ComboSelect
                      value={tf.Label}
                      options={labelOptions}
                      onChange={(v) => onChange((d) => (d.TargetFolders[idx].Label = v))}
                    />
                  </td>
                  <td data-label="Folder Path" style={{ minWidth: 230 }}>
                    <HybridPathPicker
                      value={tf.FolderPath}
                      compactButton
                      onChange={(next) => onChange((d) => (d.TargetFolders[idx].FolderPath = next))}
                    />
                  </td>
                  <td data-label="Subfolders" style={{ textAlign: 'center' }}>
                    <input
                      type="checkbox"
                      className="hk-has-hint"
                      title={COLLECTION_MANIFEST_HINTS.includeSubdirs}
                      checked={tf.IncludeSubdirectories}
                      onChange={(e) =>
                        onChange((d) => (d.TargetFolders[idx].IncludeSubdirectories = e.target.checked))
                      }
                    />
                  </td>
                  <td data-label="Category / System">
                    <ComboSelect
                      value={tf.SystemSelection}
                      options={platformOptions}
                      onChange={(v) => onChange((d) => (d.TargetFolders[idx].SystemSelection = v))}
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
                          d.TargetFolders[idx].TreatSubfoldersAsGameUnits = mode === 'FolderDumpsOnly';
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
                      onChange={(v) => onChange((d) => (d.TargetFolders[idx].ExtensionsFilter = v))}
                      placeholder="mkv, mp4, avi"
                    />
                  </td>
                  <td data-label="Delete">
                    <button
                      type="button"
                      className="hk-btn sm danger"
                      onClick={() => onChange((d) => d.TargetFolders.splice(idx, 1))}
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

      <div className="hk-card hk-deferred-section">
        <div className="hk-card-header">
          <div>
            <h3 className="hk-card-title">🧬 Custom Text Parsing &amp; CSV Field Extraction</h3>
            <p className="hk-card-subtitle">
              Extract custom columns from filenames, parent folders, or relative paths into CSV/TSV,
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
                onClick={() => onChange((d) => (d.FieldParseRules = []))}
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
                <th className="hk-has-hint" style={{ width: 48 }} title={COLLECTION_MANIFEST_HINTS.thRuleOn}>
                  On
                </th>
                <th className="hk-has-hint" style={{ width: 145 }} title={COLLECTION_MANIFEST_HINTS.thColumnName}>
                  CSV Column Name
                </th>
                <th className="hk-has-hint" style={{ width: 130 }} title={COLLECTION_MANIFEST_HINTS.thAppliesExts}>
                  Applies To (Exts)
                </th>
                <th className="hk-has-hint" style={{ width: 155 }} title={COLLECTION_MANIFEST_HINTS.thInspectTarget}>
                  Inspect In
                </th>
                <th className="hk-has-hint" style={{ width: 175 }} title={COLLECTION_MANIFEST_HINTS.thMatchMode}>
                  Match Mode
                </th>
                <th className="hk-has-hint" title={COLLECTION_MANIFEST_HINTS.thPattern}>
                  Match Pattern / C# Regex
                </th>
                <th className="hk-has-hint" style={{ width: 135 }} title={COLLECTION_MANIFEST_HINTS.thAssignedVal}>
                  Assigned Value
                </th>
                <th className="hk-has-hint" style={{ width: 75 }} title={COLLECTION_MANIFEST_HINTS.thSplitDelim}>
                  Delim
                </th>
                <th className="hk-has-hint" style={{ width: 65 }} title={COLLECTION_MANIFEST_HINTS.thSplitIndex}>
                  Index
                </th>
                <th className="hk-has-hint" style={{ width: 55 }} title={COLLECTION_MANIFEST_HINTS.thDelete}>
                  Del
                </th>
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
                      className="hk-has-hint"
                      title={COLLECTION_MANIFEST_HINTS.ruleEnabled}
                      checked={rule.Enabled}
                      onChange={(e) => onChange((d) => (d.FieldParseRules[idx].Enabled = e.target.checked))}
                    />
                  </td>
                  <td>
                    <input
                      type="text"
                      className="hk-input"
                      value={rule.ColumnName}
                      onChange={(e) => onChange((d) => (d.FieldParseRules[idx].ColumnName = e.target.value))}
                    />
                  </td>
                  <td>
                    <input
                      type="text"
                      className="hk-input mono"
                      value={rule.FileExtensionFilter}
                      onChange={(e) =>
                        onChange((d) => (d.FieldParseRules[idx].FileExtensionFilter = e.target.value))
                      }
                    />
                  </td>
                  <td>
                    <select
                      className="hk-select"
                      value={rule.InspectTarget}
                      onChange={(e) =>
                        onChange(
                          (d) =>
                            (d.FieldParseRules[idx].InspectTarget =
                              e.target.value as ManifestInspectTarget)
                        )
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
                        onChange(
                          (d) =>
                            (d.FieldParseRules[idx].MatchMode = e.target.value as ManifestMatchMode)
                        )
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
                      onChange={(e) => onChange((d) => (d.FieldParseRules[idx].Pattern = e.target.value))}
                    />
                  </td>
                  <td>
                    <input
                      type="text"
                      className="hk-input mono"
                      value={rule.AssignedValue}
                      onChange={(e) =>
                        onChange((d) => (d.FieldParseRules[idx].AssignedValue = e.target.value))
                      }
                    />
                  </td>
                  <td>
                    <input
                      type="text"
                      className="hk-input mono"
                      value={rule.SplitDelimiter}
                      onChange={(e) =>
                        onChange((d) => (d.FieldParseRules[idx].SplitDelimiter = e.target.value))
                      }
                    />
                  </td>
                  <td>
                    <input
                      type="number"
                      className="hk-input"
                      value={rule.SplitIndex}
                      onChange={(e) =>
                        onChange((d) => (d.FieldParseRules[idx].SplitIndex = Number(e.target.value) || 0))
                      }
                    />
                  </td>
                  <td>
                    <button
                      type="button"
                      className="hk-btn sm danger"
                      onClick={() => onChange((d) => d.FieldParseRules.splice(idx, 1))}
                    >
                      ✕
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

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
                <span key={col} className="hk-pill ok nowrap">
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
