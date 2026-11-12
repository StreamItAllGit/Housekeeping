// @module: Housekeeping.Web/pages/editors/manifest/CollectionManifestExportCard
// @purpose: Extracted General & Output Formats (.csv/.tsv delimiter parity) and 4-Mode Hash Placement / Integrity card for Module 8.
// @depends-on: react, ../../../components/HybridPathPicker, ../../../hints/collectionManifest, ../../../types/models
// @exports: CollectionManifestExportCard
import React from 'react';
import { HybridPathPicker } from '../../../components/HybridPathPicker';
import { COLLECTION_MANIFEST_HINTS } from '../../../hints/collectionManifest';
import type {
  AuditStructureMode,
  CollectionManifestConfig,
  HashSidecarMode,
  PipelineRuntimeStateDto,
  SidecarVerifyMode,
} from '../../../types/models';

interface Props {
  cfg: CollectionManifestConfig;
  verifyMode: SidecarVerifyMode;
  setVerifyMode: (m: SidecarVerifyMode) => void;
  verifying: boolean;
  isVerifierRunning: boolean;
  pipelineState: PipelineRuntimeStateDto;
  onRunVerifier: () => void;
  onStopVerifier: () => void;
  onChange: (mutator: (d: CollectionManifestConfig) => void) => void;
}

export const CollectionManifestExportCard: React.FC<Props> = ({
  cfg,
  verifyMode,
  setVerifyMode,
  verifying,
  isVerifierRunning,
  pipelineState,
  onRunVerifier,
  onStopVerifier,
  onChange,
}) => {
  const delimValue =
    cfg.CsvDelimiter === '\\t' || cfg.CsvDelimiter === 'Tab' ? '\t' : cfg.CsvDelimiter || ',';
  const isTsv = delimValue === '\t';
  const normalizedPlacement: HashSidecarMode =
    !cfg.HashFilePlacement || cfg.HashFilePlacement === 'None'
      ? cfg.HashFilePlacement === 'None'
        ? 'DatabaseOnly'
        : 'SidecarBeside'
      : cfg.HashFilePlacement;

  const integrityToggles: Array<{
    label: string;
    hint: string;
    checked: boolean;
    onToggle: (next: boolean) => void;
  }> = [
    {
      label: 'Enable Bit Rot Detection',
      hint: COLLECTION_MANIFEST_HINTS.enableBitRot,
      checked: Boolean(cfg.EnableBitRotDetection ?? cfg.EnableFileHashing),
      onToggle: (v) =>
        onChange((d) => {
          d.EnableBitRotDetection = v;
          d.EnableFileHashing = v;
        }),
    },
    {
      label: 'Create Missing .hash Files',
      hint: COLLECTION_MANIFEST_HINTS.createMissingHashes,
      checked: Boolean(cfg.CreateMissingHashFiles ?? cfg.GenerateHashSidecars),
      onToggle: (v) =>
        onChange((d) => {
          d.CreateMissingHashFiles = v;
          d.GenerateHashSidecars = v;
        }),
    },
    {
      label: 'Auto-Heal Repacked Containers',
      hint: COLLECTION_MANIFEST_HINTS.autoHealContainers,
      checked: Boolean(cfg.EnableBitRotAutoHeal ?? false),
      onToggle: (v) => onChange((d) => (d.EnableBitRotAutoHeal = v)),
    },
    {
      label: 'Two-Step Archive Inspection',
      hint: COLLECTION_MANIFEST_HINTS.twoStepArchive,
      checked: Boolean(cfg.CheckInnerArchiveAndContainer ?? cfg.DeepInspectAndHashInnerRoms),
      onToggle: (v) =>
        onChange((d) => {
          d.CheckInnerArchiveAndContainer = v;
          d.DeepInspectAndHashInnerRoms = v;
        }),
    },
    {
      label: 'Skip Verified CSV Hashes',
      hint: COLLECTION_MANIFEST_HINTS.skipVerifiedCsv,
      checked: Boolean(cfg.SkipExistingVerifiedHashes),
      onToggle: (v) => onChange((d) => (d.SkipExistingVerifiedHashes = v)),
    },
    {
      label: 'Sync .archive_hashes.json',
      hint: COLLECTION_MANIFEST_HINTS.syncPostInjectionJson,
      checked: Boolean(cfg.SyncPostInjectionContainerHashJson ?? true),
      onToggle: (v) => onChange((d) => (d.SyncPostInjectionContainerHashJson = v)),
    },
    {
      label: 'Skip ShadowLibrary Roots',
      hint: COLLECTION_MANIFEST_HINTS.skipShadowLibraryRoots,
      checked: Boolean(cfg.SkipShadowLibraryManagedRoots ?? false),
      onToggle: (v) => onChange((d) => (d.SkipShadowLibraryManagedRoots = v)),
    },
  ];

  const csvHint = COLLECTION_MANIFEST_HINTS.exportCsvTsv(isTsv);

  return (
    <>
      <div className="hk-card">
        <h3 className="hk-card-title">⚙ General &amp; Output Formats</h3>
        <div className="hk-row" style={{ gap: 16, marginTop: 10, marginBottom: 12 }}>
          <label className="hk-checkbox-label hk-has-hint" title={COLLECTION_MANIFEST_HINTS.unifiedMaster}>
            <input
              type="checkbox"
              className="hk-has-hint"
              title={COLLECTION_MANIFEST_HINTS.unifiedMaster}
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
              style={{ width: 210 }}
              value={cfg.UnifiedManifestName}
              onChange={(e) => onChange((d) => (d.UnifiedManifestName = e.target.value))}
              placeholder="Plex Catalog Master"
            />
          </div>

          <label className="hk-checkbox-label hk-has-hint" title={csvHint}>
            <input
              type="checkbox"
              className="hk-has-hint"
              title={csvHint}
              checked={cfg.ExportCsv}
              onChange={(e) => onChange((d) => (d.ExportCsv = e.target.checked))}
            />
            {isTsv ? 'TSV (.tsv)' : 'CSV (.csv)'}
          </label>

          <div className="hk-row" style={{ gap: 6 }}>
            <span className="hk-field-label">Delimiter:</span>
            <select
              className="hk-select"
              style={{ width: 'auto' }}
              disabled={!cfg.ExportCsv}
              value={delimValue}
              onChange={(e) => onChange((d) => (d.CsvDelimiter = e.target.value))}
              aria-label="Manifest CSV or TSV delimiter"
            >
              <option value=",">Comma (,)</option>
              <option value="&#9;">Tab (\t)</option>
              <option value=";">Semicolon (;)</option>
              <option value="|">Pipe (|)</option>
            </select>
          </div>

          <label className="hk-checkbox-label hk-has-hint" title={COLLECTION_MANIFEST_HINTS.exportJson}>
            <input
              type="checkbox"
              className="hk-has-hint"
              title={COLLECTION_MANIFEST_HINTS.exportJson}
              checked={cfg.ExportJson}
              onChange={(e) => onChange((d) => (d.ExportJson = e.target.checked))}
            />
            JSON (.json)
          </label>

          <label className="hk-checkbox-label hk-has-hint" title={COLLECTION_MANIFEST_HINTS.exportHtml}>
            <input
              type="checkbox"
              className="hk-has-hint"
              title={COLLECTION_MANIFEST_HINTS.exportHtml}
              checked={cfg.ExportHtmlReport}
              onChange={(e) => onChange((d) => (d.ExportHtmlReport = e.target.checked))}
            />
            Visual HTML (.html)
          </label>

          <label
            className="hk-checkbox-label hk-has-hint"
            style={{ color: 'var(--greenroom)' }}
            title={COLLECTION_MANIFEST_HINTS.autoSyncGreenroom}
          >
            <input
              type="checkbox"
              className="hk-has-hint"
              title={COLLECTION_MANIFEST_HINTS.autoSyncGreenroom}
              checked={cfg.AutoSyncGreenroomOnManifestChange}
              onChange={(e) => onChange((d) => (d.AutoSyncGreenroomOnManifestChange = e.target.checked))}
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

      <div className="hk-card">
        <h3 className="hk-card-title">🔐 Integrity, 4-Mode .hash Placement &amp; Coexistence</h3>
        <div className="hk-row" style={{ gap: 14, marginTop: 10, marginBottom: 10 }}>
          {integrityToggles.map((item) => (
            <label key={item.label} className="hk-checkbox-label hk-has-hint" title={item.hint}>
              <input
                type="checkbox"
                className="hk-has-hint"
                title={item.hint}
                checked={item.checked}
                onChange={(e) => item.onToggle(e.target.checked)}
              />
              {item.label}
            </label>
          ))}
        </div>

        <div className="hk-grid-3" style={{ marginBottom: 10 }}>
          <div className="hk-field">
            <label className="hk-field-label">4-Mode Hash Placement Strategy (Standard 17 §7.2)</label>
            <select
              className="hk-select"
              value={normalizedPlacement}
              onChange={(e) => onChange((d) => (d.HashFilePlacement = e.target.value as HashSidecarMode))}
            >
              <option value="DatabaseOnly">0 — Database / Manifest Only (No .hash on disk)</option>
              <option value="SidecarBeside">1 — Sidecar Beside File/Archive (.hash)</option>
              <option value="PackageInsideArchive">2 — Package Inside .7z/.zip Archive (.hash)</option>
              <option value="SidecarAndArchive">3 — Dual: Sidecar Beside + Inside Archive</option>
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
              onChange={(e) => onChange((d) => (d.StructureMode = e.target.value as AuditStructureMode))}
            >
              <option value="Auto">⚡ Auto-Detect (Files + PS3/WiiU/BDMV Folder Units)</option>
              <option value="FilesOnly">Files Only (Every File Hashed Individually)</option>
              <option value="FolderDumpsOnly">Folder Dumps Only (Subfolders as Atomic Units)</option>
            </select>
          </div>
        </div>

        <div className="hk-field" style={{ marginBottom: 10 }}>
          <label className="hk-field-label">Excluded Companion Extensions (CSV)</label>
          <input
            type="text"
            className="hk-input mono"
            value={cfg.ExcludedExtensions}
            onChange={(e) => onChange((d) => (d.ExcludedExtensions = e.target.value))}
            placeholder=".nfo, .txt, .sfv, .jpg, .png"
          />
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
              <span className="hk-pill accent nowrap">
                ⟳ {pipelineState.SubprocessStatusText || 'Verifying...'} (
                {pipelineState.ProgressPercent.toFixed(0)}%)
              </span>
            )}
          </div>
          <div className="hk-row">
            {isVerifierRunning && (
              <button type="button" className="hk-btn sm danger" onClick={onStopVerifier}>
                ⏹ Stop Verifier
              </button>
            )}
            <button
              type="button"
              className="hk-btn sm primary"
              disabled={verifying || pipelineState.IsRunning}
              onClick={onRunVerifier}
            >
              {verifying || isVerifierRunning ? '⟳ Verifying...' : '🔍 Verify & Heal .hash Files'}
            </button>
          </div>
        </div>
      </div>
    </>
  );
};
