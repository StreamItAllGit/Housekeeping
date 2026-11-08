import React, { useEffect, useState } from 'react';
import { useAppState } from '../context/AppStateContext';
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
    const base = templates && templates.length > 0 ? structuredClone(templates) : structuredClone(builtInArchiveTemplates);
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
                <label className="hk-checkbox-label">
                  <input
                    type="checkbox"
                    disabled={isReadOnlyBuiltIn}
                    checked={active.UseConsoleRuleExtensionsAsPayloads}
                    onChange={(e) =>
                      updateActive((t) => (t.UseConsoleRuleExtensionsAsPayloads = e.target.checked))
                    }
                  />
                  Inherit Allowed Payloads from Console Rule
                </label>

                <label className="hk-checkbox-label">
                  <input
                    type="checkbox"
                    disabled={isReadOnlyBuiltIn}
                    checked={active.RequireArchivedContainer}
                    onChange={(e) =>
                      updateActive((t) => (t.RequireArchivedContainer = e.target.checked))
                    }
                  />
                  Require Outer .7z / .zip Container
                </label>

                <label className="hk-checkbox-label">
                  <input
                    type="checkbox"
                    disabled={isReadOnlyBuiltIn}
                    checked={active.AllowUnpackedFolderTargets}
                    onChange={(e) =>
                      updateActive((t) => (t.AllowUnpackedFolderTargets = e.target.checked))
                    }
                  />
                  Allow Unpacked Folder Dump Targets
                </label>

                <label className="hk-checkbox-label">
                  <input
                    type="checkbox"
                    disabled={isReadOnlyBuiltIn}
                    checked={active.AllowSingleWrapperFolder}
                    onChange={(e) =>
                      updateActive((t) => (t.AllowSingleWrapperFolder = e.target.checked))
                    }
                  />
                  Allow Single Top-Level Wrapper Folder
                </label>

                <label className="hk-checkbox-label">
                  <input
                    type="checkbox"
                    disabled={isReadOnlyBuiltIn}
                    checked={active.RequireCueOrGdiForRawBinTracks}
                    onChange={(e) =>
                      updateActive((t) => (t.RequireCueOrGdiForRawBinTracks = e.target.checked))
                    }
                  />
                  Require .cue / .gdi Descriptor for Raw .bin Tracks
                </label>

                <label className="hk-checkbox-label">
                  <input
                    type="checkbox"
                    disabled={isReadOnlyBuiltIn}
                    checked={active.ValidateCueGdiM3uReferences}
                    onChange={(e) =>
                      updateActive((t) => (t.ValidateCueGdiM3uReferences = e.target.checked))
                    }
                  />
                  Validate .cue / .gdi / .m3u Referenced Tracks Exist
                </label>

                <label className="hk-checkbox-label">
                  <input
                    type="checkbox"
                    disabled={isReadOnlyBuiltIn}
                    checked={active.FlagOrphanTrackBins}
                    onChange={(e) =>
                      updateActive((t) => (t.FlagOrphanTrackBins = e.target.checked))
                    }
                  />
                  Flag Orphan Unreferenced .bin Tracks
                </label>

                <label className="hk-checkbox-label">
                  <input
                    type="checkbox"
                    disabled={isReadOnlyBuiltIn}
                    checked={active.ValidateMultiDiscSequenceContinuity}
                    onChange={(e) =>
                      updateActive((t) => (t.ValidateMultiDiscSequenceContinuity = e.target.checked))
                    }
                  />
                  Validate Multi-Disc Sequence Continuity (Disc 1..N)
                </label>

                <label className="hk-checkbox-label">
                  <input
                    type="checkbox"
                    disabled={isReadOnlyBuiltIn}
                    checked={active.FlagNestedArchives}
                    onChange={(e) =>
                      updateActive((t) => (t.FlagNestedArchives = e.target.checked))
                    }
                  />
                  Flag Nested Archives (.zip/.7z/.rar inside Archive)
                </label>

                <label className="hk-checkbox-label">
                  <input
                    type="checkbox"
                    disabled={isReadOnlyBuiltIn}
                    checked={active.ValidateFolderDumpBootAnchors}
                    onChange={(e) =>
                      updateActive((t) => (t.ValidateFolderDumpBootAnchors = e.target.checked))
                    }
                  />
                  Validate Folder Dump Boot Anchors (PS3_DISC.SFB / meta.xml)
                </label>

                <label className="hk-checkbox-label">
                  <input
                    type="checkbox"
                    disabled={isReadOnlyBuiltIn}
                    checked={active.FlagPs3UpdatePupBloat}
                    onChange={(e) =>
                      updateActive((t) => (t.FlagPs3UpdatePupBloat = e.target.checked))
                    }
                  />
                  Flag PS3_UPDATE.PUP Firmware Bloat
                </label>

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
