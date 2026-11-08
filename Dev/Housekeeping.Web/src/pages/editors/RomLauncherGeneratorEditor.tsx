import React, { useEffect, useState } from 'react';
import { ArchiveTemplateEditorDialog } from '../../components/ArchiveTemplateEditorDialog';
import { ComboSelect } from '../../components/ComboSelect';
import { HybridPathPicker } from '../../components/HybridPathPicker';
import { ModalDialog } from '../../components/ModalDialog';
import { useAppState } from '../../context/AppStateContext';
import { apiClient } from '../../services/apiClient';
import type {
  AuditStructureMode,
  ConsoleCollectionRule,
  RomLauncherGeneratorConfig,
} from '../../types/models';

interface Props {
  jobId: string;
  cfg: RomLauncherGeneratorConfig;
  onChange: (mutator: (d: RomLauncherGeneratorConfig) => void) => void;
}

const EMULATOR_RUNNERS = [
  'RetroArch',
  'PCSX2',
  'DuckStation',
  'RPCS3',
  'PPSSPP',
  'Dolphin',
  'Flycast',
  'Redream',
  'Project64',
  'melonDS',
  'Citra',
  'Cemu',
  'Xemu',
  'Xenia',
  'DirectExe',
  'Custom',
];

export const RomLauncherGeneratorEditor: React.FC<Props> = ({ jobId, cfg, onChange }) => {
  const {
    consoleCatalog,
    builtInArchiveTemplates,
    flushSaveNow,
    pipelineState,
    setPipelineState,
    setGlobalDrawerOpen,
    setActiveTab,
    showToast,
  } = useAppState();

  const [templatesModalOpen, setTemplatesModalOpen] = useState(false);
  const [overrideConsoleIdx, setOverrideConsoleIdx] = useState<number | null>(null);
  const [scanningCompliance, setScanningCompliance] = useState(false);
  const [prePreview, setPrePreview] = useState<string>('');
  const [postPreview, setPostPreview] = useState<string>('');

  const templates =
    cfg.ArchiveTemplates && cfg.ArchiveTemplates.length > 0
      ? cfg.ArchiveTemplates
      : builtInArchiveTemplates;

  const consoleNameOptions = consoleCatalog.map((c) => c.CanonicalName);

  // Live Multi-Line Pre-Launch & Post-Launch Sync Command Preview
  useEffect(() => {
    let cancelled = false;
    const t = window.setTimeout(() => {
      apiClient
        .previewRomSync(cfg, null)
        .then((res) => {
          if (!cancelled) {
            setPrePreview(res.preLaunchPreviewText || '(No active pre-launch sync commands)');
            setPostPreview(res.postLaunchPreviewText || '(No active post-launch sync commands)');
          }
        })
        .catch(() => {});
    }, 300);
    return () => {
      cancelled = true;
      window.clearTimeout(t);
    };
  }, [
    cfg.DefaultSyncPaths,
    cfg.EnablePrePostLaunchSyncCommands,
    cfg.UseCustomPrePostLaunchCommands,
    cfg.PreLaunchCommand,
    cfg.PostLaunchCommand,
    cfg.SaveSyncLocalFolder,
    cfg.SaveSyncCloudRemote,
    cfg.SaveSyncMode,
  ]);

  const isComplianceScanRunning =
    pipelineState.IsRunning && pipelineState.ActiveQueueName === 'Archive Compliance Scan';

  const handleScanComplianceNow = async () => {
    setScanningCompliance(true);
    try {
      const ok = await flushSaveNow();
      if (!ok) {
        showToast('Fix configuration validation errors before running compliance scan.');
        return;
      }
      const res = await apiClient.runComplianceScan(jobId);
      if (res.state) setPipelineState(res.state);
      setGlobalDrawerOpen(true);
      showToast(res.message);
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Compliance scan failed.');
    } finally {
      setScanningCompliance(false);
    }
  };

  const handleAddStandardTemplates = () => {
    const defaults: ConsoleCollectionRule[] = [
      {
        Enabled: true,
        ConsoleName: 'Nintendo - Super Nintendo Entertainment System',
        SubFolder: 'SNES',
        EmulatorRunnerType: 'RetroArch',
        EmulatorExecutablePath: 'retroarch.exe',
        CoreOrCustomArgs: 'snes9x_libretro.dll',
        ExtensionsCsv: '.sfc, .smc',
        ArchivesPath: 'C:\\ROMs\\Nintendo\\SNES',
        OverrideJobSyncPaths: false,
        ComplianceTemplateId: 'auto-community',
        CustomSyncPaths: [],
      },
      {
        Enabled: true,
        ConsoleName: 'Nintendo - Game Boy Advance',
        SubFolder: 'GBA',
        EmulatorRunnerType: 'RetroArch',
        EmulatorExecutablePath: 'retroarch.exe',
        CoreOrCustomArgs: 'mgba_libretro.dll',
        ExtensionsCsv: '.gba',
        ArchivesPath: 'C:\\ROMs\\Nintendo\\GBA',
        OverrideJobSyncPaths: false,
        ComplianceTemplateId: 'auto-community',
        CustomSyncPaths: [],
      },
      {
        Enabled: true,
        ConsoleName: 'Sony - PlayStation',
        SubFolder: 'PS1',
        EmulatorRunnerType: 'DuckStation',
        EmulatorExecutablePath: 'duckstation-qt-x64-ReleaseLTCG.exe',
        CoreOrCustomArgs: '',
        ExtensionsCsv: '.m3u, .chd, .cue, .pbp, .iso, .bin',
        ArchivesPath: 'C:\\ROMs\\Sony\\PlayStation',
        OverrideJobSyncPaths: false,
        ComplianceTemplateId: 'auto-community',
        CustomSyncPaths: [],
      },
      {
        Enabled: true,
        ConsoleName: 'Sony - PlayStation 2',
        SubFolder: 'PS2',
        EmulatorRunnerType: 'PCSX2',
        EmulatorExecutablePath: 'pcsx2-qt.exe',
        CoreOrCustomArgs: '',
        ExtensionsCsv: '.m3u, .chd, .iso, .ciso, .bin, .cue',
        ArchivesPath: 'C:\\ROMs\\Sony\\PlayStation 2',
        OverrideJobSyncPaths: false,
        ComplianceTemplateId: 'auto-community',
        CustomSyncPaths: [],
      },
      {
        Enabled: true,
        ConsoleName: 'Sony - PlayStation 3',
        SubFolder: 'PS3',
        EmulatorRunnerType: 'RPCS3',
        EmulatorExecutablePath: 'rpcs3.exe',
        CoreOrCustomArgs: '',
        ExtensionsCsv: 'eboot.bin, .iso',
        ArchivesPath: 'C:\\ROMs\\Sony\\PlayStation 3',
        OverrideJobSyncPaths: false,
        ComplianceTemplateId: 'official-folder-dump',
        CustomSyncPaths: [],
      },
      {
        Enabled: true,
        ConsoleName: 'Nintendo - GameCube',
        SubFolder: 'GameCube',
        EmulatorRunnerType: 'Dolphin',
        EmulatorExecutablePath: 'Dolphin.exe',
        CoreOrCustomArgs: '',
        ExtensionsCsv: '.m3u, .rvz, .iso, .gcm',
        ArchivesPath: 'C:\\ROMs\\Nintendo\\GameCube',
        OverrideJobSyncPaths: false,
        ComplianceTemplateId: 'community-modern-disc',
        CustomSyncPaths: [],
      },
    ];
    onChange((d) => {
      const existingNames = new Set(d.Collections.map((c) => c.ConsoleName.toLowerCase()));
      for (const item of defaults) {
        if (!existingNames.has(item.ConsoleName.toLowerCase())) {
          d.Collections.push(item);
        }
      }
    });
    showToast('Added starter Console Collection presets.');
  };

  const overrideCol =
    overrideConsoleIdx !== null && cfg.Collections[overrideConsoleIdx]
      ? cfg.Collections[overrideConsoleIdx]
      : null;

  return (
    <>
      <div className="hk-info-banner">
        <strong>🎮 What This Module Does: ROM, Emulator &amp; PC Game Launcher Generator</strong>
        <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 2 }}>
          Builds multi-disc <code>.m3u</code> playlists, compresses loose ROMs to solid{' '}
          <code>.7z</code>, generates one-click launchers, writes 9-column{' '}
          <code>CRC32 + SHA-1</code> manifests verified against No-Intro &amp; Redump DATs, audits
          archive compliance without decompression, syncs cloud saves/states across all rows, and
          notifies Greenroom (<code>:8420</code>) when manifests change.
        </div>
      </div>

      {/* 1. Launcher Generation & Output Paths */}
      <div className="hk-card">
        <h3 className="hk-card-title">🚀 Launcher Automation &amp; Output Directories</h3>
        <div className="hk-grid-4" style={{ marginTop: 10, marginBottom: 12 }}>
          <label className="hk-checkbox-label">
            <input
              type="checkbox"
              checked={cfg.AutoGenerateM3uForMultiDisc}
              onChange={(e) => onChange((d) => (d.AutoGenerateM3uForMultiDisc = e.target.checked))}
            />
            Auto-Generate .m3u for Multi-Disc
          </label>
          <label className="hk-checkbox-label">
            <input
              type="checkbox"
              checked={cfg.AutoCompressUnarchivedRomsWith7z}
              onChange={(e) =>
                onChange((d) => (d.AutoCompressUnarchivedRomsWith7z = e.target.checked))
              }
            />
            Auto-Compress Loose ROMs to .7z
          </label>
          <label className="hk-checkbox-label">
            <input
              type="checkbox"
              checked={cfg.DeleteUncompressedOriginalsAfter7z}
              onChange={(e) =>
                onChange((d) => (d.DeleteUncompressedOriginalsAfter7z = e.target.checked))
              }
            />
            Delete Loose Originals After 7z
          </label>
          <label className="hk-checkbox-label">
            <input
              type="checkbox"
              checked={cfg.UseFast7zListInspection}
              onChange={(e) => onChange((d) => (d.UseFast7zListInspection = e.target.checked))}
            />
            Fast 7z Header List Inspection
          </label>
          <label className="hk-checkbox-label">
            <input
              type="checkbox"
              checked={cfg.ExtractArchiveOnLaunch}
              onChange={(e) => onChange((d) => (d.ExtractArchiveOnLaunch = e.target.checked))}
            />
            Extract .7z to SSD Scratch on Launch
          </label>
          <label className="hk-checkbox-label">
            <input
              type="checkbox"
              checked={cfg.CreateStartMenuShortcuts}
              onChange={(e) => onChange((d) => (d.CreateStartMenuShortcuts = e.target.checked))}
            />
            Create Windows Start Menu Shortcuts
          </label>
          <label className="hk-checkbox-label">
            <input
              type="checkbox"
              checked={cfg.CreateLocalLauncherNextToArchive}
              onChange={(e) =>
                onChange((d) => (d.CreateLocalLauncherNextToArchive = e.target.checked))
              }
            />
            Create Launcher Beside Archive
          </label>
          <label className="hk-checkbox-label">
            <input
              type="checkbox"
              checked={cfg.RefreshExistingLaunchers}
              onChange={(e) => onChange((d) => (d.RefreshExistingLaunchers = e.target.checked))}
            />
            Force Overwrite Existing Launchers
          </label>
        </div>

        <div className="hk-grid-3">
          <div className="hk-field">
            <label className="hk-field-label">Launchers Output Root Folder</label>
            <HybridPathPicker
              value={cfg.LaunchersOutputRootPath}
              onChange={(next) => onChange((d) => (d.LaunchersOutputRootPath = next))}
            />
          </div>
          <div className="hk-field">
            <label className="hk-field-label">Fast SSD Extraction Scratch Folder</label>
            <HybridPathPicker
              value={cfg.TempExtractionScratchPath}
              onChange={(next) => onChange((d) => (d.TempExtractionScratchPath = next))}
            />
          </div>
          <div className="hk-field">
            <label className="hk-field-label">Start Menu Shortcuts Folder</label>
            <HybridPathPicker
              value={cfg.StartMenuShortcutsPath}
              onChange={(next) => onChange((d) => (d.StartMenuShortcutsPath = next))}
            />
          </div>
        </div>
      </div>

      {/* 2. 9-Column CRC32+SHA-1 Manifest, Fast Header CRC32, Compliance & Greenroom Sync */}
      <div className="hk-card">
        <div className="hk-card-header">
          <div>
            <h3 className="hk-card-title">
              🛡 9-Column CRC32+SHA-1 Manifest, DAT Auditing, Compliance &amp; Greenroom Sync
            </h3>
            <p className="hk-card-subtitle">
              Zero-decompression 7z/ZIP header CRC32 comparison, composite multi-file{' '}
              <code>.hash</code> sidecars, and archive template compliance scanning.
            </p>
          </div>
          <div className="hk-row">
            <button
              type="button"
              className="hk-btn sm"
              onClick={() => setTemplatesModalOpen(true)}
            >
              📐 Manage Archive Templates ({templates.length})
            </button>
            <button
              type="button"
              className="hk-btn sm primary"
              disabled={scanningCompliance || pipelineState.IsRunning}
              onClick={() => void handleScanComplianceNow()}
            >
              {scanningCompliance || isComplianceScanRunning
                ? '⟳ Scanning Compliance...'
                : '🔍 Scan Compliance Now'}
            </button>
            <button
              type="button"
              className="hk-btn sm"
              onClick={() => setActiveTab('reports')}
            >
              🛡 Open Reports Center
            </button>
          </div>
        </div>

        <div className="hk-grid-4" style={{ marginBottom: 10 }}>
          <label className="hk-checkbox-label">
            <input
              type="checkbox"
              checked={cfg.EnableCollectionManifestCsv}
              onChange={(e) => onChange((d) => (d.EnableCollectionManifestCsv = e.target.checked))}
            />
            Generate 9-Col collection_manifest.csv
          </label>
          <label className="hk-checkbox-label">
            <input
              type="checkbox"
              checked={cfg.UseFastArchiveHeaderCrc32Audit}
              onChange={(e) =>
                onChange((d) => (d.UseFastArchiveHeaderCrc32Audit = e.target.checked))
              }
            />
            ⚡ Fast 7z/ZIP Header CRC32 Audit
          </label>
          <label className="hk-checkbox-label">
            <input
              type="checkbox"
              checked={cfg.DeepInspectAndHashInnerRoms}
              onChange={(e) => onChange((d) => (d.DeepInspectAndHashInnerRoms = e.target.checked))}
            />
            Deep Inspect &amp; Hash Inner ROMs
          </label>
          <label className="hk-checkbox-label">
            <input
              type="checkbox"
              checked={cfg.TrustExistingHashFiles}
              onChange={(e) => onChange((d) => (d.TrustExistingHashFiles = e.target.checked))}
            />
            Trust Validated .hash Sidecars
          </label>
          <label className="hk-checkbox-label">
            <input
              type="checkbox"
              checked={cfg.AutoDownloadMissingDats}
              onChange={(e) => onChange((d) => (d.AutoDownloadMissingDats = e.target.checked))}
            />
            Auto-Download Missing No-Intro DATs
          </label>
          <label className="hk-checkbox-label">
            <input
              type="checkbox"
              checked={cfg.ForceRehashExistingManifestEntries}
              onChange={(e) =>
                onChange((d) => (d.ForceRehashExistingManifestEntries = e.target.checked))
              }
            />
            Force Re-Hash Existing CSV Rows
          </label>
          <label className="hk-checkbox-label">
            <input
              type="checkbox"
              checked={cfg.EnableArchiveTemplateComplianceScan}
              onChange={(e) =>
                onChange((d) => (d.EnableArchiveTemplateComplianceScan = e.target.checked))
              }
            />
            Enable Archive Template Compliance Scan
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

        <div className="hk-grid-3" style={{ marginBottom: 10 }}>
          <div className="hk-field">
            <label className="hk-field-label">Manifest CSV Filename</label>
            <input
              type="text"
              className="hk-input mono"
              value={cfg.ManifestCsvFileName}
              onChange={(e) => onChange((d) => (d.ManifestCsvFileName = e.target.value))}
            />
          </div>
          <div className="hk-field">
            <label className="hk-field-label">No-Intro / Redump DATs Folder</label>
            <HybridPathPicker
              value={cfg.DatFilesDirectory}
              compactButton
              onChange={(next) => onChange((d) => (d.DatFilesDirectory = next))}
            />
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
              <option value="Auto">⚡ Auto-Detect (ROM Files + PS3/WiiU Folders)</option>
              <option value="FilesOnly">Files Only</option>
              <option value="FolderDumpsOnly">Folder Dumps Only (Composite SHA-1)</option>
            </select>
          </div>
        </div>

        <div className="hk-grid-2" style={{ marginBottom: 10 }}>
          <div className="hk-row">
            <label className="hk-checkbox-label">
              <input
                type="checkbox"
                checked={cfg.EnableSecondaryManifestStorage}
                onChange={(e) =>
                  onChange((d) => (d.EnableSecondaryManifestStorage = e.target.checked))
                }
              />
              Mirror Manifest CSV to Secondary Backup Folder:
            </label>
          </div>
          <HybridPathPicker
            value={cfg.SecondaryManifestDirectory}
            onChange={(next) => onChange((d) => (d.SecondaryManifestDirectory = next))}
            placeholder="Optional secondary backup directory (e.g. Google Drive / NAS)"
          />
        </div>
      </div>

      {/* 3. Console & PC Game Collections Table */}
      <div className="hk-card">
        <div className="hk-card-header">
          <div>
            <h3 className="hk-card-title">🎮 Console &amp; PC Game Collections</h3>
            <p className="hk-card-subtitle">
              Configure emulator runners, PC game executables (<code>DirectExe</code>), ROM folders,
              and per-console Archive Compliance Templates.
            </p>
          </div>
          <div className="hk-row">
            <button
              type="button"
              className="hk-btn sm"
              onClick={handleAddStandardTemplates}
            >
              ⚡ Add Starter Console Presets
            </button>
            <button
              type="button"
              className="hk-btn sm primary"
              onClick={() =>
                onChange((d) => {
                  d.Collections.push({
                    Enabled: true,
                    ConsoleName: 'Sony - PlayStation 2',
                    SubFolder: 'PS2',
                    EmulatorRunnerType: 'PCSX2',
                    EmulatorExecutablePath: 'pcsx2-qt.exe',
                    CoreOrCustomArgs: '',
                    ExtensionsCsv: '.m3u, .chd, .iso, .bin, .cue',
                    ArchivesPath: '',
                    OverrideJobSyncPaths: false,
                    ComplianceTemplateId: 'auto-community',
                    CustomSyncPaths: [],
                  });
                })
              }
            >
              + Add Console / PC Rule
            </button>
          </div>
        </div>

        <div className="table-wrapper" tabIndex={0} role="region" aria-label="Console collections">
          <table className="hk-table stack-mobile">
            <thead>
              <tr>
                <th>On</th>
                <th>Console / System</th>
                <th>Subfolder</th>
                <th>Runner</th>
                <th>Emulator / Exe Path</th>
                <th>Core / Args</th>
                <th>Extensions (CSV)</th>
                <th>ROMs / Games Folder</th>
                <th>Archive Template</th>
                <th>Sync</th>
                <th>Del</th>
              </tr>
            </thead>
            <tbody>
              {(cfg.Collections || []).length === 0 && (
                <tr>
                  <td colSpan={11} style={{ color: 'var(--text-secondary)' }}>
                    No console or PC game rules configured yet. Click &ldquo;+ Add Console / PC Rule&rdquo; or
                    &ldquo;⚡ Add Starter Console Presets&rdquo;.
                  </td>
                </tr>
              )}
              {(cfg.Collections || []).map((col, idx) => (
                <tr key={idx}>
                  <td data-label="Enabled">
                    <input
                      type="checkbox"
                      checked={col.Enabled}
                      onChange={(e) =>
                        onChange((d) => {
                          d.Collections[idx].Enabled = e.target.checked;
                        })
                      }
                    />
                  </td>
                  <td data-label="Console / System" style={{ minWidth: 175 }}>
                    <ComboSelect
                      value={col.ConsoleName}
                      options={consoleNameOptions}
                      onChange={(v) =>
                        onChange((d) => {
                          d.Collections[idx].ConsoleName = v;
                        })
                      }
                      placeholder="Sony - PlayStation 2"
                    />
                  </td>
                  <td data-label="Subfolder" style={{ minWidth: 95 }}>
                    <input
                      type="text"
                      className="hk-input mono"
                      value={col.SubFolder}
                      onChange={(e) =>
                        onChange((d) => {
                          d.Collections[idx].SubFolder = e.target.value;
                        })
                      }
                    />
                  </td>
                  <td data-label="Emulator Runner" style={{ minWidth: 120 }}>
                    <ComboSelect
                      value={col.EmulatorRunnerType}
                      options={EMULATOR_RUNNERS}
                      onChange={(v) =>
                        onChange((d) => {
                          d.Collections[idx].EmulatorRunnerType = v;
                        })
                      }
                    />
                  </td>
                  <td data-label="Emulator / Exe Path" style={{ minWidth: 190 }}>
                    <HybridPathPicker
                      value={col.EmulatorExecutablePath}
                      mode="file"
                      compactButton
                      onChange={(next) =>
                        onChange((d) => {
                          d.Collections[idx].EmulatorExecutablePath = next;
                        })
                      }
                    />
                  </td>
                  <td data-label="Core / Args" style={{ minWidth: 145 }}>
                    <input
                      type="text"
                      className="hk-input mono"
                      value={col.CoreOrCustomArgs}
                      onChange={(e) =>
                        onChange((d) => {
                          d.Collections[idx].CoreOrCustomArgs = e.target.value;
                        })
                      }
                      placeholder="core.dll or args"
                    />
                  </td>
                  <td data-label="Extensions (CSV)" style={{ minWidth: 150 }}>
                    <input
                      type="text"
                      className="hk-input mono"
                      value={col.ExtensionsCsv}
                      onChange={(e) =>
                        onChange((d) => {
                          d.Collections[idx].ExtensionsCsv = e.target.value;
                        })
                      }
                    />
                  </td>
                  <td data-label="ROMs / Games Folder" style={{ minWidth: 210 }}>
                    <HybridPathPicker
                      value={col.ArchivesPath}
                      compactButton
                      onChange={(next) =>
                        onChange((d) => {
                          d.Collections[idx].ArchivesPath = next;
                        })
                      }
                    />
                  </td>
                  <td data-label="Archive Template" style={{ minWidth: 175 }}>
                    <select
                      className="hk-select"
                      value={col.ComplianceTemplateId || 'auto-community'}
                      onChange={(e) =>
                        onChange((d) => {
                          d.Collections[idx].ComplianceTemplateId = e.target.value;
                        })
                      }
                    >
                      {templates.map((t) => (
                        <option key={t.TemplateId} value={t.TemplateId}>
                          {t.DisplayName}
                        </option>
                      ))}
                    </select>
                  </td>
                  <td data-label="Save / State Sync">
                    <button
                      type="button"
                      className={`hk-btn sm ${col.OverrideJobSyncPaths ? 'primary' : ''}`}
                      onClick={() => setOverrideConsoleIdx(idx)}
                      title="Configure per-console custom save/state sync paths"
                    >
                      ⚙ {col.OverrideJobSyncPaths ? `Custom (${col.CustomSyncPaths.length})` : 'Default'}
                    </button>
                  </td>
                  <td data-label="Delete Rule">
                    <button
                      type="button"
                      className="hk-btn sm danger"
                      onClick={() =>
                        onChange((d) => {
                          d.Collections.splice(idx, 1);
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

      {/* 4. Multi-Row Pre/Post-Launch Cloud Save & State Sync (Fixes single-line preview bug!) */}
      <div className="hk-card hk-deferred-section">
        <div className="hk-card-header">
          <div>
            <h3 className="hk-card-title">
              ☁ Multi-Row Pre-Launch &amp; Post-Launch Save / State Sync
            </h3>
            <p className="hk-card-subtitle">
              Every enabled row below runs sequentially before launch and after exit inside generated{' '}
              <code>.bat</code> launchers.
            </p>
          </div>
          <div className="hk-row">
            <label className="hk-checkbox-label">
              <input
                type="checkbox"
                checked={cfg.EnablePrePostLaunchSyncCommands}
                onChange={(e) =>
                  onChange((d) => (d.EnablePrePostLaunchSyncCommands = e.target.checked))
                }
              />
              Enable Pre/Post-Launch Sync in Generated Launchers
            </label>
            <button
              type="button"
              className="hk-btn sm primary"
              onClick={() =>
                onChange((d) => {
                  d.DefaultSyncPaths.push({
                    Enabled: true,
                    Label: d.DefaultSyncPaths.length === 0 ? 'Saves' : 'States',
                    LocalPath: '',
                    RemoteTarget: 'gdrive:EmuSaves',
                    SyncMode: 'bisync',
                  });
                })
              }
            >
              + Add Sync Row
            </button>
          </div>
        </div>

        <div className="table-wrapper" tabIndex={0} role="region" aria-label="Default save sync rows">
          <table className="hk-table stack-mobile">
            <thead>
              <tr>
                <th style={{ width: 55 }}>On</th>
                <th style={{ width: 140 }}>Label</th>
                <th style={{ width: 130 }}>Sync Mode</th>
                <th>Local Save / State Folder</th>
                <th>Cloud Remote Target</th>
                <th style={{ width: 65 }}>Del</th>
              </tr>
            </thead>
            <tbody>
              {(cfg.DefaultSyncPaths || []).length === 0 && (
                <tr>
                  <td colSpan={6} style={{ color: 'var(--text-secondary)' }}>
                    Add rows for Saves, Save States, Memory Cards, or Screenshots — all enabled rows
                    execute in sequence.
                  </td>
                </tr>
              )}
              {(cfg.DefaultSyncPaths || []).map((row, idx) => (
                <tr key={idx}>
                  <td data-label="Enabled">
                    <input
                      type="checkbox"
                      checked={row.Enabled}
                      onChange={(e) =>
                        onChange((d) => {
                          d.DefaultSyncPaths[idx].Enabled = e.target.checked;
                        })
                      }
                    />
                  </td>
                  <td data-label="Label">
                    <input
                      type="text"
                      className="hk-input"
                      value={row.Label}
                      onChange={(e) =>
                        onChange((d) => {
                          d.DefaultSyncPaths[idx].Label = e.target.value;
                        })
                      }
                    />
                  </td>
                  <td data-label="Mode">
                    <select
                      className="hk-select"
                      value={row.SyncMode}
                      onChange={(e) =>
                        onChange((d) => {
                          d.DefaultSyncPaths[idx].SyncMode = e.target.value;
                        })
                      }
                    >
                      <option value="bisync">bisync (2-way)</option>
                      <option value="sync">sync (pull/push)</option>
                      <option value="copy">copy</option>
                    </select>
                  </td>
                  <td data-label="Local Folder">
                    <HybridPathPicker
                      value={row.LocalPath}
                      compactButton
                      onChange={(next) =>
                        onChange((d) => {
                          d.DefaultSyncPaths[idx].LocalPath = next;
                        })
                      }
                    />
                  </td>
                  <td data-label="Cloud Remote">
                    <input
                      type="text"
                      className="hk-input mono"
                      value={row.RemoteTarget}
                      onChange={(e) =>
                        onChange((d) => {
                          d.DefaultSyncPaths[idx].RemoteTarget = e.target.value;
                        })
                      }
                      placeholder="gdrive:RetroArch/saves"
                    />
                  </td>
                  <td data-label="Delete">
                    <button
                      type="button"
                      className="hk-btn sm danger"
                      onClick={() =>
                        onChange((d) => {
                          d.DefaultSyncPaths.splice(idx, 1);
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

        <div style={{ marginTop: 12 }}>
          <label className="hk-checkbox-label" style={{ marginBottom: 8 }}>
            <input
              type="checkbox"
              checked={cfg.UseCustomPrePostLaunchCommands}
              onChange={(e) =>
                onChange((d) => (d.UseCustomPrePostLaunchCommands = e.target.checked))
              }
            />
            Override Auto-Generated Multi-Row rclone Commands with Custom CLI Commands
          </label>

          <div className="hk-grid-2">
            <div className="hk-field">
              <label className="hk-field-label">
                {cfg.UseCustomPrePostLaunchCommands
                  ? 'Custom Pre-Launch CLI Command'
                  : 'All Resolved Pre-Launch Commands (Multi-Line Preview)'}
              </label>
              <textarea
                className="hk-textarea mono"
                readOnly={!cfg.UseCustomPrePostLaunchCommands}
                value={cfg.UseCustomPrePostLaunchCommands ? cfg.PreLaunchCommand : prePreview}
                onChange={(e) => onChange((d) => (d.PreLaunchCommand = e.target.value))}
              />
            </div>

            <div className="hk-field">
              <label className="hk-field-label">
                {cfg.UseCustomPrePostLaunchCommands
                  ? 'Custom Post-Launch CLI Command'
                  : 'All Resolved Post-Launch Commands (Multi-Line Preview)'}
              </label>
              <textarea
                className="hk-textarea mono"
                readOnly={!cfg.UseCustomPrePostLaunchCommands}
                value={cfg.UseCustomPrePostLaunchCommands ? cfg.PostLaunchCommand : postPreview}
                onChange={(e) => onChange((d) => (d.PostLaunchCommand = e.target.value))}
              />
            </div>
          </div>
        </div>
      </div>

      {/* Archive Templates Modal */}
      <ArchiveTemplateEditorDialog
        open={templatesModalOpen}
        onClose={() => setTemplatesModalOpen(false)}
        templates={templates}
        onSaveTemplates={(updated) =>
          onChange((d) => {
            d.ArchiveTemplates = updated;
          })
        }
      />

      {/* Per-Console Sync Paths Override Modal */}
      <ModalDialog
        open={overrideCol !== null}
        onClose={() => setOverrideConsoleIdx(null)}
        title={`⚙ Save/State Sync Override — ${overrideCol?.ConsoleName || ''}`}
        size="wide"
        closedBy="any"
        footer={
          <button
            type="button"
            className="hk-btn primary"
            onClick={() => setOverrideConsoleIdx(null)}
          >
            Done
          </button>
        }
      >
        {overrideCol && overrideConsoleIdx !== null && (
          <>
            <div className="hk-row" style={{ justifyContent: 'space-between', marginBottom: 12 }}>
              <label className="hk-checkbox-label">
                <input
                  type="checkbox"
                  checked={overrideCol.OverrideJobSyncPaths}
                  onChange={(e) =>
                    onChange((d) => {
                      d.Collections[overrideConsoleIdx].OverrideJobSyncPaths = e.target.checked;
                    })
                  }
                />
                Override Job Default Sync Rows for {overrideCol.ConsoleName}
              </label>

              <button
                type="button"
                className="hk-btn sm primary"
                onClick={() =>
                  onChange((d) => {
                    d.Collections[overrideConsoleIdx].OverrideJobSyncPaths = true;
                    d.Collections[overrideConsoleIdx].CustomSyncPaths.push({
                      Enabled: true,
                      Label: 'Console Saves',
                      LocalPath: '',
                      RemoteTarget: '',
                      SyncMode: 'bisync',
                    });
                  })
                }
              >
                + Add Console Sync Row
              </button>
            </div>

            <div className="table-wrapper" tabIndex={0} role="region" aria-label="Console custom sync rows">
              <table className="hk-table stack-mobile">
                <thead>
                  <tr>
                    <th>On</th>
                    <th>Label</th>
                    <th>Mode</th>
                    <th>Local Folder</th>
                    <th>Cloud Remote Target</th>
                    <th>Del</th>
                  </tr>
                </thead>
                <tbody>
                  {overrideCol.CustomSyncPaths.length === 0 && (
                    <tr>
                      <td colSpan={6} style={{ color: 'var(--text-secondary)' }}>
                        No custom rows yet. Add one above to override job-level defaults.
                      </td>
                    </tr>
                  )}
                  {overrideCol.CustomSyncPaths.map((sp, sIdx) => (
                    <tr key={sIdx}>
                      <td data-label="Enabled">
                        <input
                          type="checkbox"
                          checked={sp.Enabled}
                          onChange={(e) =>
                            onChange((d) => {
                              d.Collections[overrideConsoleIdx].CustomSyncPaths[sIdx].Enabled =
                                e.target.checked;
                            })
                          }
                        />
                      </td>
                      <td data-label="Label">
                        <input
                          type="text"
                          className="hk-input"
                          value={sp.Label}
                          onChange={(e) =>
                            onChange((d) => {
                              d.Collections[overrideConsoleIdx].CustomSyncPaths[sIdx].Label =
                                e.target.value;
                            })
                          }
                        />
                      </td>
                      <td data-label="Mode">
                        <select
                          className="hk-select"
                          value={sp.SyncMode}
                          onChange={(e) =>
                            onChange((d) => {
                              d.Collections[overrideConsoleIdx].CustomSyncPaths[sIdx].SyncMode =
                                e.target.value;
                            })
                          }
                        >
                          <option value="bisync">bisync</option>
                          <option value="sync">sync</option>
                          <option value="copy">copy</option>
                        </select>
                      </td>
                      <td data-label="Local Folder">
                        <HybridPathPicker
                          value={sp.LocalPath}
                          compactButton
                          onChange={(next) =>
                            onChange((d) => {
                              d.Collections[overrideConsoleIdx].CustomSyncPaths[sIdx].LocalPath =
                                next;
                            })
                          }
                        />
                      </td>
                      <td data-label="Cloud Remote Target">
                        <input
                          type="text"
                          className="hk-input mono"
                          value={sp.RemoteTarget}
                          onChange={(e) =>
                            onChange((d) => {
                              d.Collections[overrideConsoleIdx].CustomSyncPaths[sIdx].RemoteTarget =
                                e.target.value;
                            })
                          }
                        />
                      </td>
                      <td data-label="Delete">
                        <button
                          type="button"
                          className="hk-btn sm danger"
                          onClick={() =>
                            onChange((d) => {
                              d.Collections[overrideConsoleIdx].CustomSyncPaths.splice(sIdx, 1);
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
          </>
        )}
      </ModalDialog>
    </>
  );
};
