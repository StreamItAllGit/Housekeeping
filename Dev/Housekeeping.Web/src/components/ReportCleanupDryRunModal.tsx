// @module: Housekeeping.Web/components/ReportCleanupDryRunModal
// @purpose: Guarded 7z d clutter removal dry-run preview modal extracted from ReportsCenterPage.tsx.
// @depends-on: react, ../types/models, ./ModalDialog
// @exports: ReportCleanupDryRunModal, formatReportBytes
import React from 'react';
import type { ArchiveCleanupDryRunResult, ComplianceReportItem } from '../types/models';
import { ModalDialog } from './ModalDialog';

export function formatReportBytes(bytes: number): string {
  if (bytes >= 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
  if (bytes >= 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
  if (bytes >= 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${bytes} B`;
}

interface ReportCleanupDryRunModalProps {
  dryRunPreview: ArchiveCleanupDryRunResult | null;
  inspectedItem: ComplianceReportItem | null;
  busyAction: boolean;
  onClose: () => void;
  onExecuteClean: (reportIds: string[], explicitPaths?: string[]) => void;
}

export const ReportCleanupDryRunModal: React.FC<ReportCleanupDryRunModalProps> = ({
  dryRunPreview,
  inspectedItem,
  busyAction,
  onClose,
  onExecuteClean,
}) => {
  return (
    <ModalDialog
      open={dryRunPreview !== null}
      onClose={onClose}
      title="🧪 Guarded 7z d Clutter Removal — Dry-Run Preview"
      size="normal"
      closedBy="any"
      footer={
        <>
          <button type="button" className="hk-btn" onClick={onClose}>
            Cancel
          </button>
          {dryRunPreview?.IsAllowed && inspectedItem && (
            <button
              type="button"
              className="hk-btn success"
              disabled={busyAction}
              onClick={() =>
                onExecuteClean(
                  [inspectedItem.ReportId],
                  dryRunPreview.FilesToDelete.map((f) => f.InternalPath)
                )
              }
            >
              🧹 Execute 7z d &amp; Refresh .hash
            </button>
          )}
        </>
      }
    >
      {dryRunPreview && (
        <div>
          {!dryRunPreview.IsAllowed ? (
            <div className="hk-alert-banner err">
              <span>🛑 Safety Guard Blocked Cleanup: {dryRunPreview.BlockReason}</span>
            </div>
          ) : (
            <div className="hk-info-banner">
              <strong>✓ Safety Guard Passed:</strong> {dryRunPreview.ProtectedFilesKept.length}{' '}
              primary ROM/descriptor file(s) remain protected inside the archive. Reclaiming{' '}
              <strong>{formatReportBytes(dryRunPreview.ReclaimedBytes)}</strong>.
            </div>
          )}

          <div className="hk-field" style={{ marginBottom: 12 }}>
            <label className="hk-field-label">Exact 7-Zip Command</label>
            <input
              type="text"
              className="hk-input mono"
              readOnly
              value={dryRunPreview.CommandPreview}
            />
          </div>

          <div className="hk-grid-2">
            <div>
              <strong style={{ color: 'var(--err)', fontSize: 12.5 }}>
                Extra Clutter to Remove ({dryRunPreview.FilesToDelete.length})
              </strong>
              <ul>
                {dryRunPreview.FilesToDelete.map((f) => (
                  <li key={f.InternalPath} className="mono" style={{ fontSize: 11.5 }}>
                    {f.InternalPath} ({formatReportBytes(f.SizeBytes)})
                  </li>
                ))}
              </ul>
            </div>
            <div>
              <strong style={{ color: 'var(--ok)', fontSize: 12.5 }}>
                Protected Game Files Kept ({dryRunPreview.ProtectedFilesKept.length})
              </strong>
              <ul>
                {dryRunPreview.ProtectedFilesKept.map((f) => (
                  <li key={f.InternalPath} className="mono" style={{ fontSize: 11.5 }}>
                    {f.InternalPath} ({formatReportBytes(f.SizeBytes)})
                  </li>
                ))}
              </ul>
            </div>
          </div>
        </div>
      )}
    </ModalDialog>
  );
};
