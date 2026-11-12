// @module: Housekeeping.Web/components/ArchiveTemplateEditorDialog
// @purpose: Modal dialog for inspecting built-in archive compliance templates and editing custom cloned rulesets.
// @depends-on: react, ../context/AppStateContext, ../hints/commonDialogs, ../types/models, ./ModalDialog
// @exports: ArchiveTemplateEditorDialog
import React, { useEffect, useState } from 'react';
import { useAppState } from '../context/AppStateContext';
import { COMMON_UI_HINTS } from '../hints/commonDialogs';
import type { ArchiveComplianceTemplate } from '../types/models';
import { ModalDialog } from './ModalDialog';

interface ArchiveTemplateEditorDialogProps {
  open: boolean;
  onClose: () => void;
  templates: ArchiveComplianceTemplate[];
  onSaveTemplates: (updated: ArchiveComplianceTemplate[]) => void;
}

export const ArchiveTemplateEditorDialog: React.FC<ArchiveTemplateEditorDialogProps> = ({
  open,
  onClose,
  templates,
  onSaveTemplates,
}) => {
  const { builtInArchiveTemplates } = useAppState();
  const [localList, setLocalList] = useState<ArchiveComplianceTemplate[]>([]);
  const [selectedId, setSelectedId] = useState<string>('');

  useEffect(() => {
    if (!open) return;
    const base =
      templates && templates.length > 0
        ? structuredClone(templates)
        : structuredClone(builtInArchiveTemplates);
    setLocalList(base);
    setSelectedId(base[0]?.TemplateId || '');
  }, [builtInArchiveTemplates, open, templates]);

  const active = localList.find((t) => t.TemplateId === selectedId) || localList[0] || null;
  const isReadOnlyBuiltIn = Boolean(active?.IsBuiltIn);

  const updateActive = (mutator: (t: ArchiveComplianceTemplate) => void) => {
    if (!active || active.IsBuiltIn) return;
    setLocalList((prev) =>
      prev.map((item) => {
        if (item.TemplateId !== active.TemplateId) return item;
        const next = structuredClone(item);
        mutator(next);
        return next;
      })
    );
  };

  const handleCloneAsCustom = () => {
    const src = active || builtInArchiveTemplates[0];
    if (!src) return;
    const copy: ArchiveComplianceTemplate = {
      ...structuredClone(src),
      TemplateId: `custom-${Math.random().toString(36).slice(2, 8)}`,
      DisplayName: `${src.DisplayName} (Custom)`,
      Category: 'Custom',
      IsBuiltIn: false,
    };
    const next = [...localList, copy];
    setLocalList(next);
    setSelectedId(copy.TemplateId);
  };

  const handleDeleteCustom = () => {
    if (!active || active.IsBuiltIn) return;
    const next = localList.filter((t) => t.TemplateId !== active.TemplateId);
    setLocalList(next);
    setSelectedId(next[0]?.TemplateId || '');
  };

  const toggleSpecs: Array<{
    label: string;
    hint: string;
    getVal: (t: ArchiveComplianceTemplate) => boolean;
    setVal: (t: ArchiveComplianceTemplate, v: boolean) => void;
  }> = [
    {
      label: 'Inherit Allowed Payloads from Console Rule',
      hint: COMMON_UI_HINTS.tplInheritExts,
      getVal: (t) => t.UseConsoleRuleExtensionsAsPayloads,
      setVal: (t, v) => (t.UseConsoleRuleExtensionsAsPayloads = v),
    },
    {
      label: 'Require Outer .7z / .zip Container',
      hint: COMMON_UI_HINTS.tplRequireArchive,
      getVal: (t) => t.RequireArchivedContainer,
      setVal: (t, v) => (t.RequireArchivedContainer = v),
    },
    {
      label: 'Allow Unpacked Folder Dump Targets',
      hint: COMMON_UI_HINTS.tplAllowFolderTargets,
      getVal: (t) => t.AllowUnpackedFolderTargets,
      setVal: (t, v) => (t.AllowUnpackedFolderTargets = v),
    },
    {
      label: 'Allow Single Top-Level Wrapper Folder',
      hint: COMMON_UI_HINTS.tplAllowSingleWrapper,
      getVal: (t) => t.AllowSingleWrapperFolder,
      setVal: (t, v) => (t.AllowSingleWrapperFolder = v),
    },
    {
      label: 'Require .cue / .gdi Descriptor for Raw .bin Tracks',
      hint: COMMON_UI_HINTS.tplRequireCueGdi,
      getVal: (t) => t.RequireCueOrGdiForRawBinTracks,
      setVal: (t, v) => (t.RequireCueOrGdiForRawBinTracks = v),
    },
    {
      label: 'Validate .cue / .gdi / .m3u Referenced Tracks Exist',
      hint: COMMON_UI_HINTS.tplValidateTrackRefs,
      getVal: (t) => t.ValidateCueGdiM3uReferences,
      setVal: (t, v) => (t.ValidateCueGdiM3uReferences = v),
    },
    {
      label: 'Flag Orphan Unreferenced .bin Tracks',
      hint: COMMON_UI_HINTS.tplFlagOrphanBins,
      getVal: (t) => t.FlagOrphanTrackBins,
      setVal: (t, v) => (t.FlagOrphanTrackBins = v),
    },
    {
      label: 'Validate Multi-Disc Sequence Continuity (Disc 1..N)',
      hint: COMMON_UI_HINTS.tplMultiDiscSeq,
      getVal: (t) => t.ValidateMultiDiscSequenceContinuity,
      setVal: (t, v) => (t.ValidateMultiDiscSequenceContinuity = v),
    },
    {
      label: 'Flag Nested Archives (.zip/.7z/.rar inside Archive)',
      hint: COMMON_UI_HINTS.tplFlagNestedArchives,
      getVal: (t) => t.FlagNestedArchives,
      setVal: (t, v) => (t.FlagNestedArchives = v),
    },
    {
      label: 'Validate Folder Dump Boot Anchors (PS3_DISC.SFB / meta.xml)',
      hint: COMMON_UI_HINTS.tplBootAnchors,
      getVal: (t) => t.ValidateFolderDumpBootAnchors,
      setVal: (t, v) => (t.ValidateFolderDumpBootAnchors = v),
    },
    {
      label: 'Flag PS3_UPDATE.PUP Firmware Bloat',
      hint: COMMON_UI_HINTS.tplFlagPs3Pup,
      getVal: (t) => t.FlagPs3UpdatePupBloat,
      setVal: (t, v) => (t.FlagPs3UpdatePupBloat = v),
    },
  ];

  return (
    <ModalDialog
      open={open}
      onClose={onClose}
      title="📐 Archive Structure & Compliance Templates"
      size="wide"
      closedBy="closerequest"
      footer={
        <>
          <button type="button" className="hk-btn" onClick={onClose}>
            Cancel
          </button>
          <button
            type="button"
            className="hk-btn primary"
            onClick={() => {
              onSaveTemplates(localList);
              onClose();
            }}
          >
            ✓ Apply Templates
          </button>
        </>
      }
    >
      <div className="hk-builder-layout templates-split">
        <div className="hk-card" style={{ padding: 10 }}>
          <div className="hk-row" style={{ justifyContent: 'space-between', marginBottom: 8 }}>
            <strong>Templates ({localList.length})</strong>
            <button type="button" className="hk-btn sm primary" onClick={handleCloneAsCustom}>
              + Clone Custom
            </button>
          </div>

          <div style={{ display: 'flex', flexDirection: 'column', gap: 5 }}>
            {localList.map((tpl) => (
              <div
                key={tpl.TemplateId}
                className={`hk-job-item ${tpl.TemplateId === active?.TemplateId ? 'active' : ''}`}
                onClick={() => setSelectedId(tpl.TemplateId)}
              >
                <div style={{ flex: 1 }}>
                  <div style={{ fontWeight: 700, fontSize: 12.5 }}>{tpl.DisplayName}</div>
                  <div style={{ fontSize: 11, color: 'var(--text-secondary)' }}>
                    {tpl.IsBuiltIn ? `🔒 Built-In (${tpl.Category})` : '✏ Custom Rule'}
                  </div>
                </div>
              </div>
            ))}
          </div>
        </div>

        <div className="hk-card">
          {active ? (
            <>
              <div className="hk-row" style={{ justifyContent: 'space-between', marginBottom: 10 }}>
                <div>
                  <h3 className="hk-card-title">{active.DisplayName}</h3>
                  <div className="hk-card-subtitle">
                    {isReadOnlyBuiltIn
                      ? 'Built-in standard preset — click "+ Clone Custom" on the left to customize.'
                      : 'Custom editable archive compliance ruleset.'}
                  </div>
                </div>
                {!isReadOnlyBuiltIn && (
                  <button type="button" className="hk-btn sm danger" onClick={handleDeleteCustom}>
                    🗑 Delete Custom Template
                  </button>
                )}
              </div>

              <div className="hk-grid-2" style={{ marginBottom: 10 }}>
                <div className="hk-field">
                  <label className="hk-field-label">Display Name</label>
                  <input
                    type="text"
                    className="hk-input"
                    disabled={isReadOnlyBuiltIn}
                    value={active.DisplayName}
                    onChange={(e) => updateActive((t) => (t.DisplayName = e.target.value))}
                  />
                </div>
                <div className="hk-field">
                  <label className="hk-field-label">Template ID</label>
                  <input type="text" className="hk-input mono" disabled value={active.TemplateId} />
                </div>
              </div>

              <div className="hk-field" style={{ marginBottom: 10 }}>
                <label className="hk-field-label">Description</label>
                <input
                  type="text"
                  className="hk-input"
                  disabled={isReadOnlyBuiltIn}
                  value={active.Description}
                  onChange={(e) => updateActive((t) => (t.Description = e.target.value))}
                />
              </div>

              <div className="hk-grid-3" style={{ marginBottom: 10 }}>
                <div className="hk-field">
                  <label className="hk-field-label">Allowed Payload Exts (CSV)</label>
                  <input
                    type="text"
                    className="hk-input mono"
                    disabled={isReadOnlyBuiltIn || active.UseConsoleRuleExtensionsAsPayloads}
                    value={
                      active.UseConsoleRuleExtensionsAsPayloads
                        ? '(Uses Console Rule Extensions)'
                        : active.AllowedPayloadExtensionsCsv
                    }
                    onChange={(e) =>
                      updateActive((t) => (t.AllowedPayloadExtensionsCsv = e.target.value))
                    }
                  />
                </div>
                <div className="hk-field">
                  <label className="hk-field-label">Allowed Companion Exts (CSV)</label>
                  <input
                    type="text"
                    className="hk-input mono"
                    disabled={isReadOnlyBuiltIn}
                    value={active.AllowedCompanionExtensionsCsv}
                    onChange={(e) =>
                      updateActive((t) => (t.AllowedCompanionExtensionsCsv = e.target.value))
                    }
                  />
                </div>
                <div className="hk-field">
                  <label className="hk-field-label">Allowed Outer Containers (CSV)</label>
                  <input
                    type="text"
                    className="hk-input mono"
                    disabled={isReadOnlyBuiltIn}
                    value={active.AllowedOuterContainerExtensionsCsv}
                    onChange={(e) =>
                      updateActive((t) => (t.AllowedOuterContainerExtensionsCsv = e.target.value))
                    }
                  />
                </div>
              </div>

              <div className="hk-grid-2">
                {toggleSpecs.map((spec) => (
                  <label key={spec.label} className="hk-checkbox-label hk-has-hint" title={spec.hint}>
                    <input
                      type="checkbox"
                      className="hk-has-hint"
                      title={spec.hint}
                      disabled={isReadOnlyBuiltIn}
                      checked={spec.getVal(active)}
                      onChange={(e) => updateActive((t) => spec.setVal(t, e.target.checked))}
                    />
                    {spec.label}
                  </label>
                ))}

                <div className="hk-field">
                  <label className="hk-field-label">
                    Max Primary ROMs per Archive (0 = Unlimited)
                  </label>
                  <input
                    type="number"
                    min={0}
                    className="hk-input"
                    disabled={isReadOnlyBuiltIn}
                    value={active.MaxPrimaryRomsPerArchive}
                    onChange={(e) =>
                      updateActive((t) => (t.MaxPrimaryRomsPerArchive = Number(e.target.value) || 0))
                    }
                  />
                </div>
              </div>
            </>
          ) : (
            <p>Select a template on the left.</p>
          )}
        </div>
      </div>
    </ModalDialog>
  );
};
