// @module: Housekeeping.Web/pages/editors/RomLauncherGeneratorEditor
// @purpose: Module 4 (ROM, Emulator & PC Game Launcher Generator) editor with strict ProfileSelect runner dropdown, coexistence banner, and consolidated compliance card.
// @depends-on: react, ../../components/ArchiveTemplateEditorDialog, ../../components/CoexistenceWarningBanner, ../../components/ComboSelect, ../../components/HybridPathPicker, ../../components/ModalDialog, ../../components/ProfileSelect, ../../constants/emulatorRunners, ../../context/AppStateContext, ../../hints/romLauncher, ../../services/apiClient, ../../types/models
// @exports: RomLauncherGeneratorEditor
import React, { useEffect, useState } from 'react';
import { ArchiveTemplateEditorDialog } from '../../components/ArchiveTemplateEditorDialog';
import { CoexistenceWarningBanner } from '../../components/CoexistenceWarningBanner';
import { ComboSelect } from '../../components/ComboSelect';
import { HybridPathPicker } from '../../components/HybridPathPicker';
import { ModalDialog } from '../../components/ModalDialog';
import { ProfileSelect } from '../../components/ProfileSelect';
import { RUNNER_TYPES } from '../../constants/emulatorRunners';
import { useAppState } from '../../context/AppStateContext';
import { ROM_LAUNCHER_HINTS } from '../../hints/romLauncher';
import { apiClient } from '../../services/apiClient';
import type { ConsoleCollectionRule, RomLauncherGeneratorConfig } from '../../types/models';

interface Props {
  jobId: string;
  cfg: RomLauncherGeneratorConfig;
  onChange: (mutator: (d: RomLauncherGeneratorConfig) => void) => void;
}

const COMPLIANCE_TOGGLE_HINT =
  'Runs zero-decompression archive structure compliance checks during Module 4 execution.\nFlagged clutter and descriptor issues appear in the Reports Center.';

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
        .catch((err) => console.debug('[RomLauncher] sync preview failed:', err));
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
        if (!existingNames.has(item.ConsoleName.toLowerCase())) d.Collections.push(item);
      }
    });
    showToast('Added starter Console Collection presets.');
  };

  const overrideCol =
    overrideConsoleIdx !== null && cfg.Collections[overrideConsoleIdx]
      ? cfg.Collections[overrideConsoleIdx]
      : null;

  const launcherToggles: Array<{
    label: string;
    hint: string;
    checked: boolean;
    onToggle: (v: boolean) => void;
  }> = [
    {
      label: 'Auto-Generate .m3u for Multi-Disc',
      hint: ROM_LAUNCHER_HINTS.autoM3u,
      checked: cfg.AutoGenerateM3uForMultiDisc,
      onToggle: (v) => onChange((d) => (d.AutoGenerateM3uForMultiDisc = v)),
    },
    {
      label: 'Auto-Compress Loose ROMs to .7z',
      hint: ROM_LAUNCHER_HINTS.autoCompress7z,
      checked: cfg.AutoCompressUnarchivedRomsWith7z,
      onToggle: (v) => onChange((d) => (d.AutoCompressUnarchivedRomsWith7z = v)),
    },
    {
      label: 'Delete Loose Originals After 7z',
      hint: ROM_LAUNCHER_HINTS.deleteOriginals,
      checked: cfg.DeleteUncompressedOriginalsAfter7z,
      onToggle: (v) => onChange((d) => (d.DeleteUncompressedOriginalsAfter7z = v)),
    },
    {
      label: 'Fast 7z Header List Inspection',
      hint: ROM_LAUNCHER_HINTS.fast7zInspect,
      checked: cfg.UseFast7zListInspection,
      onToggle: (v) => onChange((d) => (d.UseFast7zListInspection = v)),
    },
    {
      label: 'Extract .7z to SSD Scratch on Launch',
      hint: ROM_LAUNCHER_HINTS.extractOnLaunch,
      checked: cfg.ExtractArchiveOnLaunch,
      onToggle: (v) => onChange((d) => (d.ExtractArchiveOnLaunch = v)),
    },
    {
      label: 'Create Windows Start Menu Shortcuts',
      hint: ROM_LAUNCHER_HINTS.startMenuShortcuts,
      checked: cfg.CreateStartMenuShortcuts,
      onToggle: (v) => onChange((d) => (d.CreateStartMenuShortcuts = v)),
    },
    {
      label: 'Create Launcher Beside Archive',
      hint: ROM_LAUNCHER_HINTS.localLauncherBeside,
      checked: cfg.CreateLocalLauncherNextToArchive,
      onToggle: (v) => onChange((d) => (d.CreateLocalLauncherNextToArchive = v)),
    },
    {
      label: 'Force Overwrite Existing Launchers',
      hint: ROM_LAUNCHER_HINTS.refreshLaunchers,
      checked: cfg.RefreshExistingLaunchers,
      onToggle: (v) => onChange((d) => (d.RefreshExistingLaunchers = v)),
    },
  ];

  return (
    <>
      <CoexistenceWarningBanner jobId={jobId} moduleType="RomLauncherGenerator" />

      <div className="hk-info-banner">
        <strong>🎮 What This Module Does: ROM, Emulator &amp; PC Game Launcher Generator</strong>
        <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 2 }}>
          Builds multi-disc <code>.m3u</code> playlists, compresses loose ROMs to solid{' '}
          <code>.7z</code> (excluding sidecars/sentinels), generates one-click launchers with
          multi-row cloud save/state sync, and coordinates cleanly with uStation (<code>:8460</code>)
          and ShadowLibrary.
        </div>
      </div>

      <div className="hk-card">
        <h3 className="hk-card-title">🚀 Launcher Automation &amp; Output Directories</h3>
        <div className="hk-grid-4" style={{ marginTop: 10, marginBottom: 12 }}>
          {launcherToggles.map((item) => (
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

      {/* Consolidated Archive Compliance & Module 8 / ShadowLibrary Pointer Card (T13 / Standard 17 §7) */}
      <div className="hk-card">
        <div className="hk-card-header">
          <div>
            <h3 className="hk-card-title">🛡 Archive Structure Compliance &amp; Vault Manifests</h3>
            <p className="hk-card-subtitle">
              Zero-decompression archive structure compliance runs here and in Reports Center.
              Full 4-mode <code>.hash</code> sidecars, No-Intro/Redump DAT auditing, and{' '}
              <code>.csv</code>/<code>.tsv</code> manifests are unified in{' '}
              <strong>Module 8 (Collection Manifest)</strong> &amp; <strong>ShadowLibrary</strong>.
            </p>
          </div>
          <div className="hk-row">
            <label className="hk-checkbox-label hk-has-hint" title={COMPLIANCE_TOGGLE_HINT}>
              <input
                type="checkbox"
                className="hk-has-hint"
                title={COMPLIANCE_TOGGLE_HINT}
                checked={cfg.EnableArchiveTemplateComplianceScan}
                onChange={(e) =>
                  onChange((d) => (d.EnableArchiveTemplateComplianceScan = e.target.checked))
                }
              />
              Scan Compliance on Run
            </label>
            <button
              type="button"
              className="hk-btn sm"
              onClick={() => setTemplatesModalOpen(true)}
            >
              📐 Manage Archive Templates ({templates.length})
            </button>
            {isComplianceScanRunning && (
              <button
                type="button"
                className="hk-btn sm danger"
                onClick={() =>
                  void apiClient.stopCurrentJob().then((r) => {
                    setPipelineState(r.state);
                    showToast('Stopping compliance scan...');
                  })
                }
              >
                ⏹ Stop Scan
              </button>
            )}
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
      </div>

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
            <button type="button" className="hk-btn sm" onClick={handleAddStandardTemplates}>
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
                <th className="hk-has-hint" title={ROM_LAUNCHER_HINTS.thOn}>On</th>
                <th className="hk-has-hint" title={ROM_LAUNCHER_HINTS.thConsole}>Console / System</th>
                <th className="hk-has-hint" title={ROM_LAUNCHER_HINTS.thSubfolder}>Subfolder</th>
                <th className="hk-has-hint" title={ROM_LAUNCHER_HINTS.thRunner}>Runner</th>
                <th className="hk-has-hint" title={ROM_LAUNCHER_HINTS.thEmuPath}>Emulator / Exe Path</th>
                <th className="hk-has-hint" title={ROM_LAUNCHER_HINTS.thCoreArgs}>Core / Args</th>
                <th className="hk-has-hint" title={ROM_LAUNCHER_HINTS.thExts}>Extensions (CSV)</th>
                <th className="hk-has-hint" title={ROM_LAUNCHER_HINTS.thArchivesPath}>ROMs / Games Folder</th>
                <th className="hk-has-hint" title={ROM_LAUNCHER_HINTS.thTemplate}>Archive Template</th>
                <th className="hk-has-hint" title={ROM_LAUNCHER_HINTS.thSyncOverride}>Sync</th>
                <th className="hk-has-hint" title={ROM_LAUNCHER_HINTS.thDelete}>Del</th>
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
                      className="hk-has-hint"
                      title={ROM_LAUNCHER_HINTS.collectionEnabled}
                      checked={col.Enabled}
                      onChange={(e) => onChange((d) => (d.Collections[idx].Enabled = e.target.checked))}
                    />
                  </td>
                  <td data-label="Console / System" style={{ minWidth: 175 }}>
                    <ComboSelect
                      value={col.ConsoleName}
                      options={consoleNameOptions}
                      onChange={(v) => onChange((d) => (d.Collections[idx].ConsoleName = v))}
                      placeholder="Sony - PlayStation 2"
                    />
                  </td>
                  <td data-label="Subfolder" style={{ minWidth: 95 }}>
                    <input
                      type="text"
                      className="hk-input mono"
                      value={col.SubFolder}
                      onChange={(e) => onChange((d) => (d.Collections[idx].SubFolder = e.target.value))}
                    />
                  </td>
                  <td data-label="Emulator Runner" style={{ minWidth: 130 }}>
                    <ProfileSelect
                      value={col.EmulatorRunnerType}
                      options={RUNNER_TYPES}
                      onChange={(v) => onChange((d) => (d.Collections[idx].EmulatorRunnerType = v))}
                      ariaLabel="Emulator runner preset"
                    />
                  </td>
                  <td data-label="Emulator / Exe Path" style={{ minWidth: 190 }}>
                    <HybridPathPicker
                      value={col.EmulatorExecutablePath}
                      mode="file"
                      compactButton
                      onChange={(next) =>
                        onChange((d) => (d.Collections[idx].EmulatorExecutablePath = next))
                      }
                    />
                  </td>
                  <td data-label="Core / Args" style={{ minWidth: 145 }}>
                    <input
                      type="text"
                      className="hk-input mono"
                      value={col.CoreOrCustomArgs}
                      onChange={(e) =>
                        onChange((d) => (d.Collections[idx].CoreOrCustomArgs = e.target.value))
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
                        onChange((d) => (d.Collections[idx].ExtensionsCsv = e.target.value))
                      }
                    />
                  </td>
                  <td data-label="ROMs / Games Folder" style={{ minWidth: 210 }}>
                    <HybridPathPicker
                      value={col.ArchivesPath}
                      compactButton
                      onChange={(next) => onChange((d) => (d.Collections[idx].ArchivesPath = next))}
                    />
                  </td>
                  <td data-label="Archive Template" style={{ minWidth: 175 }}>
                    <select
                      className="hk-select"
                      value={col.ComplianceTemplateId || 'auto-community'}
                      onChange={(e) =>
                        onChange((d) => (d.Collections[idx].ComplianceTemplateId = e.target.value))
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
                      onClick={() => onChange((d) => d.Collections.splice(idx, 1))}
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
            <h3 className="hk-card-title">
              ☁ Multi-Row Pre-Launch &amp; Post-Launch Save / State Sync
            </h3>
            <p className="hk-card-subtitle">
              Every enabled row below runs sequentially before launch and after exit inside generated{' '}
              <code>.bat</code> launchers.
            </p>
          </div>
          <div className="hk-row">
            <label className="hk-checkbox-label hk-has-hint" title={ROM_LAUNCHER_HINTS.prePostSyncToggle}>
              <input
                type="checkbox"
                className="hk-has-hint"
                title={ROM_LAUNCHER_HINTS.prePostSyncToggle}
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
                <th className="hk-has-hint" style={{ width: 55 }} title={ROM_LAUNCHER_HINTS.thOn}>On</th>
                <th className="hk-has-hint" style={{ width: 140 }} title={ROM_LAUNCHER_HINTS.thSyncLabel}>Label</th>
                <th className="hk-has-hint" style={{ width: 130 }} title={ROM_LAUNCHER_HINTS.thSyncMode}>Sync Mode</th>
                <th className="hk-has-hint" title={ROM_LAUNCHER_HINTS.thLocalSave}>Local Save / State Folder</th>
                <th className="hk-has-hint" title={ROM_LAUNCHER_HINTS.thRemoteSave}>Cloud Remote Target</th>
                <th className="hk-has-hint" style={{ width: 65 }} title={ROM_LAUNCHER_HINTS.thDelete}>Del</th>
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
                      className="hk-has-hint"
                      title={ROM_LAUNCHER_HINTS.syncRowEnabled}
                      checked={row.Enabled}
                      onChange={(e) => onChange((d) => (d.DefaultSyncPaths[idx].Enabled = e.target.checked))}
                    />
                  </td>
                  <td data-label="Label">
                    <input
                      type="text"
                      className="hk-input"
                      value={row.Label}
                      onChange={(e) => onChange((d) => (d.DefaultSyncPaths[idx].Label = e.target.value))}
                    />
                  </td>
                  <td data-label="Mode">
                    <select
                      className="hk-select"
                      value={row.SyncMode}
                      onChange={(e) => onChange((d) => (d.DefaultSyncPaths[idx].SyncMode = e.target.value))}
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
                      onChange={(next) => onChange((d) => (d.DefaultSyncPaths[idx].LocalPath = next))}
                    />
                  </td>
                  <td data-label="Cloud Remote">
                    <input
                      type="text"
                      className="hk-input mono"
                      value={row.RemoteTarget}
                      onChange={(e) =>
                        onChange((d) => (d.DefaultSyncPaths[idx].RemoteTarget = e.target.value))
                      }
                      placeholder="gdrive:RetroArch/saves"
                    />
                  </td>
                  <td data-label="Delete">
                    <button
                      type="button"
                      className="hk-btn sm danger"
                      onClick={() => onChange((d) => d.DefaultSyncPaths.splice(idx, 1))}
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
          <label
            className="hk-checkbox-label hk-has-hint"
            style={{ marginBottom: 8 }}
            title={ROM_LAUNCHER_HINTS.customPrePostCli}
          >
            <input
              type="checkbox"
              className="hk-has-hint"
              title={ROM_LAUNCHER_HINTS.customPrePostCli}
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

      <ArchiveTemplateEditorDialog
        open={templatesModalOpen}
        onClose={() => setTemplatesModalOpen(false)}
        templates={templates}
        onSaveTemplates={(updated) => onChange((d) => (d.ArchiveTemplates = updated))}
      />

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
              <label className="hk-checkbox-label hk-has-hint" title={ROM_LAUNCHER_HINTS.overrideConsoleSync}>
                <input
                  type="checkbox"
                  className="hk-has-hint"
                  title={ROM_LAUNCHER_HINTS.overrideConsoleSync}
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
                    <th className="hk-has-hint" title={ROM_LAUNCHER_HINTS.thOn}>On</th>
                    <th className="hk-has-hint" title={ROM_LAUNCHER_HINTS.thSyncLabel}>Label</th>
                    <th className="hk-has-hint" title={ROM_LAUNCHER_HINTS.thSyncMode}>Mode</th>
                    <th className="hk-has-hint" title={ROM_LAUNCHER_HINTS.thLocalSave}>Local Folder</th>
                    <th className="hk-has-hint" title={ROM_LAUNCHER_HINTS.thRemoteSave}>Cloud Remote Target</th>
                    <th className="hk-has-hint" title={ROM_LAUNCHER_HINTS.thDelete}>Del</th>
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
                          className="hk-has-hint"
                          title={ROM_LAUNCHER_HINTS.syncRowEnabled}
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
                              d.Collections[overrideConsoleIdx].CustomSyncPaths[sIdx].LocalPath = next;
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
