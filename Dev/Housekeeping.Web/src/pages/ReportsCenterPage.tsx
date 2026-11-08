import React, { useCallback, useEffect, useState } from 'react';
import { ArchiveTemplateEditorDialog } from '../components/ArchiveTemplateEditorDialog';
import { ModalDialog } from '../components/ModalDialog';
import { useAppState } from '../context/AppStateContext';
import { apiClient } from '../services/apiClient';
import type {
  ArchiveCleanupDryRunResult,
  ComplianceReportItem,
  ReportsQueryResponse,
} from '../types/models';

function formatBytes(bytes: number): string {
  if (bytes >= 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
  if (bytes >= 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
  if (bytes >= 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${bytes} B`;
}

export const ReportsCenterPage: React.FC = () => {
  const {
    config,
    authStatus,
    builtInArchiveTemplates,
    updateConfig,
    flushSaveNow,
    pipelineState,
    setPipelineState,
    setGlobalDrawerOpen,
    openComplianceCount,
    setOpenComplianceCount,
    showToast,
  } = useAppState();

  const [data, setData] = useState<ReportsQueryResponse | null>(null);
  const [consoleFilter, setConsoleFilter] = useState('All Consoles');
  const [categoryFilter, setCategoryFilter] = useState('All Categories');
  const [statusFilter, setStatusFilter] = useState('Open Only');
  const [searchText, setSearchText] = useState('');

  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [inspectedId, setInspectedId] = useState<string>('');
  const [customCleanPaths, setCustomCleanPaths] = useState<Record<string, boolean>>({});

  const [scanning, setScanning] = useState(false);
  const [busyAction, setBusyAction] = useState(false);
  const [templatesOpen, setTemplatesOpen] = useState(false);
  const [dryRunPreview, setDryRunPreview] = useState<ArchiveCleanupDryRunResult | null>(null);

  const loadReports = useCallback(async () => {
    try {
      const res = await apiClient.getReports({
        console: consoleFilter,
        category: categoryFilter,
        status: statusFilter,
        search: searchText,
      });
      setData(res);
      setOpenComplianceCount(res.kpis.openCount);
    } catch {
      // ignore
    }
  }, [categoryFilter, consoleFilter, searchText, setOpenComplianceCount, statusFilter]);

  useEffect(() => {
    void loadReports();
  }, [loadReports, openComplianceCount, pipelineState.IsRunning]);

  const items = data?.items || [];
  const inspectedItem: ComplianceReportItem | null =
    items.find((i) => i.ReportId === inspectedId) || items[0] || null;

  useEffect(() => {
    if (!inspectedItem) return;
    const init: Record<string, boolean> = {};
    for (const node of inspectedItem.EntryNodes || []) {
      init[node.InternalPath] = Boolean(node.CanClean);
    }
    setCustomCleanPaths(init);
  }, [inspectedItem?.ReportId]);

  const handleScanNow = async () => {
    setScanning(true);
    try {
      await flushSaveNow();
      const res = await apiClient.runComplianceScan();
      if (res.state) setPipelineState(res.state);
      setGlobalDrawerOpen(true);
      showToast(res.message);
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Compliance scan failed.');
    } finally {
      setScanning(false);
    }
  };

  const targetIdsForBatch =
    selectedIds.length > 0
      ? selectedIds
      : inspectedItem
        ? [inspectedItem.ReportId]
        : [];

  const handleDryRunClean = async () => {
    if (!inspectedItem) return;
    setBusyAction(true);
    try {
      const explicit = Object.entries(customCleanPaths)
        .filter(([, on]) => on)
        .map(([p]) => p);
      const preview = await apiClient.dryRunCleanArchive(
        inspectedItem.ReportId,
        explicit.length > 0 ? explicit : undefined
      );
      setDryRunPreview(preview);
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Dry-run preview failed.');
    } finally {
      setBusyAction(false);
    }
  };

  const handleExecuteCleanExtra = async (reportIds: string[], explicitPaths?: string[]) => {
    setBusyAction(true);
    try {
      const res = await apiClient.cleanExtraFiles(reportIds, explicitPaths);
      setDryRunPreview(null);
      await loadReports();
      showToast(`Cleaned ${res.cleaned} archive(s); ${res.failed} skipped/failed.`);
    } catch (err) {
      showToast(err instanceof Error ? err.message : '7z cleanup failed.');
    } finally {
      setBusyAction(false);
    }
  };

  const handleHealHashes = async () => {
    if (targetIdsForBatch.length === 0) return;
    setBusyAction(true);
    try {
      const res = await apiClient.healReportHashes(targetIdsForBatch);
      await loadReports();
      showToast(`Healed .hash sidecar for ${res.healedCount} item(s).`);
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Heal .hash failed.');
    } finally {
      setBusyAction(false);
    }
  };

  const handleSetIgnored = async (ignored: boolean) => {
    if (targetIdsForBatch.length === 0) return;
    setBusyAction(true);
    try {
      await apiClient.setReportsIgnored(targetIdsForBatch, ignored);
      await loadReports();
      showToast(ignored ? 'Marked selected report(s) as Ignored.' : 'Reopened selected report(s).');
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Update failed.');
    } finally {
      setBusyAction(false);
    }
  };

  const handleClearResolved = async () => {
    setBusyAction(true);
    try {
      const res = await apiClient.clearResolvedReports();
      await loadReports();
      showToast(`Cleared ${res.removedCount} resolved report(s).`);
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Clear resolved failed.');
    } finally {
      setBusyAction(false);
    }
  };

  const romJob = config?.DefinedJobs.find((j) => j.ModuleType === 'RomLauncherGenerator');
  const activeTemplates =
    romJob?.RomLauncherGenerator?.ArchiveTemplates ||
    config?.RomLauncherGenerator?.ArchiveTemplates ||
    builtInArchiveTemplates;

  return (
    <div>
      {/* 4 KPI Summary Cards */}
      <div className="hk-grid-4" style={{ marginBottom: 'var(--gap-grid)' }}>
        <div className="hk-kpi-card">
          <div className="hk-field-label">Open Archive &amp; Hash Issues</div>
          <div
            className="hk-kpi-value"
            style={{ color: (data?.kpis.openCount || 0) > 0 ? 'var(--warn)' : 'var(--ok)' }}
          >
            {data?.kpis.openCount ?? 0}
          </div>
        </div>
        <div className="hk-kpi-card">
          <div className="hk-field-label">Cleanable Extra Clutter (7z d)</div>
          <div className="hk-kpi-value" style={{ color: 'var(--accent)' }}>
            {data?.kpis.cleanableCount ?? 0}
          </div>
        </div>
        <div className="hk-kpi-card">
          <div className="hk-field-label">Missing Refs / Format Issues</div>
          <div
            className="hk-kpi-value"
            style={{ color: (data?.kpis.missingOrFormatCount || 0) > 0 ? 'var(--err)' : 'inherit' }}
          >
            {data?.kpis.missingOrFormatCount ?? 0}
          </div>
        </div>
        <div className="hk-kpi-card">
          <div className="hk-field-label">Resolved / Ignored</div>
          <div className="hk-kpi-value" style={{ color: 'var(--ok)' }}>
            {data?.kpis.resolvedOrIgnoredCount ?? 0}
          </div>
        </div>
      </div>

      {/* Filter & Remediation Toolbar */}
      <div className="hk-card" style={{ marginBottom: 'var(--gap-grid)' }}>
        <div className="hk-row" style={{ justifyContent: 'space-between', marginBottom: 10 }}>
          <div className="hk-row">
            <select
              className="hk-select"
              style={{ width: 'auto' }}
              value={consoleFilter}
              onChange={(e) => setConsoleFilter(e.target.value)}
              aria-label="Filter by console"
            >
              <option value="All Consoles">All Consoles</option>
              {(data?.consoles || []).map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>

            <select
              className="hk-select"
              style={{ width: 'auto' }}
              value={categoryFilter}
              onChange={(e) => setCategoryFilter(e.target.value)}
              aria-label="Filter by issue category"
            >
              <option value="All Categories">All Categories</option>
              <option value="Extra Files">Extra Files</option>
              <option value="Missing / Broken Refs">Missing / Broken Refs</option>
              <option value="Wrong Format">Wrong Format</option>
              <option value="Multi-ROM / Structure">Multi-ROM / Structure</option>
              <option value="Hash Integrity">Hash Integrity</option>
            </select>

            <select
              className="hk-select"
              style={{ width: 'auto' }}
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value)}
              aria-label="Filter by resolution status"
            >
              <option value="Open Only">Open Only</option>
              <option value="All">All Statuses</option>
              <option value="Ignored Only">Ignored Only</option>
              <option value="Resolved Only">Resolved Only</option>
            </select>

            <input
              type="search"
              className="hk-input hk-mobile-flex-input"
              style={{ width: 220 }}
              placeholder="Search game, code, template..."
              value={searchText}
              onChange={(e) => setSearchText(e.target.value)}
            />
          </div>

          <div className="hk-row">
            <button
              type="button"
              className="hk-btn sm"
              onClick={() => setTemplatesOpen(true)}
            >
              📐 Archive Templates
            </button>
            <button
              type="button"
              className="hk-btn sm primary"
              disabled={scanning}
              onClick={() => void handleScanNow()}
            >
              {scanning ? 'Scanning Archives...' : '🔍 Scan Archives Now'}
            </button>
            <a
              href="/api/v1/reports/export-csv"
              className="hk-btn sm"
              style={{ textDecoration: 'none' }}
            >
              📥 Export CSV
            </a>
            <a
              href="/api/v1/reports/export-json"
              className="hk-btn sm"
              style={{ textDecoration: 'none' }}
            >
              📥 Export JSON
            </a>
          </div>
        </div>

        {/* Batch Remediation Bar */}
        <div
          className="hk-row"
          style={{
            paddingTop: 8,
            borderTop: '1px solid var(--border-subtle)',
            justifyContent: 'space-between',
          }}
        >
          <div className="hk-row">
            <span style={{ fontSize: 12, color: 'var(--text-secondary)' }}>
              {selectedIds.length > 0
                ? `${selectedIds.length} report(s) selected`
                : inspectedItem
                  ? `Active: ${inspectedItem.TargetFileName}`
                  : 'Select a report below'}
            </span>
            <button
              type="button"
              className="hk-btn sm purple"
              disabled={!inspectedItem || inspectedItem.IsDirectory || busyAction}
              onClick={() => void handleDryRunClean()}
            >
              🧪 Preview 7z d Cleanup
            </button>
            <button
              type="button"
              className="hk-btn sm success"
              disabled={targetIdsForBatch.length === 0 || busyAction}
              onClick={() => void handleExecuteCleanExtra(targetIdsForBatch)}
            >
              🧹 Clean Extra Files (7z d)
            </button>
            <button
              type="button"
              className="hk-btn sm"
              disabled={targetIdsForBatch.length === 0 || busyAction}
              onClick={() => void handleHealHashes()}
            >
              🔧 Heal .hash Sidecar
            </button>
            {authStatus?.isDirectLoopback && inspectedItem && (
              <button
                type="button"
                className="hk-btn sm"
                onClick={() =>
                  void apiClient
                    .revealInExplorer(inspectedItem.TargetFullPath)
                    .catch((err: unknown) =>
                      showToast(err instanceof Error ? err.message : 'Unable to reveal path in Explorer.')
                    )
                }
              >
                📂 Reveal in Explorer
              </button>
            )}
          </div>

          <div className="hk-row">
            <button
              type="button"
              className="hk-btn sm"
              disabled={targetIdsForBatch.length === 0 || busyAction}
              onClick={() => void handleSetIgnored(true)}
            >
              🙈 Ignore
            </button>
            <button
              type="button"
              className="hk-btn sm"
              disabled={targetIdsForBatch.length === 0 || busyAction}
              onClick={() => void handleSetIgnored(false)}
            >
              ↺ Reopen
            </button>
            <button
              type="button"
              className="hk-btn sm"
              disabled={busyAction}
              onClick={() => void handleClearResolved()}
            >
              🧹 Clear Resolved
            </button>
          </div>
        </div>
      </div>

      {/* Master-Detail Split: Flagged Archives Table + Inner Archive File Tree Inspector */}
      <div className="hk-builder-layout reports-split">
        <div className="hk-card">
          <h3 className="hk-card-title" style={{ marginBottom: 10 }}>
            📋 Flagged Archives &amp; Folder Dumps ({items.length})
          </h3>
          <div className="table-wrapper" tabIndex={0} role="region" aria-label="Compliance reports">
            <table className="hk-table stack-mobile">
              <thead>
                <tr>
                  <th style={{ width: 42 }}>
                    <input
                      type="checkbox"
                      checked={items.length > 0 && selectedIds.length === items.length}
                      onChange={(e) =>
                        setSelectedIds(e.target.checked ? items.map((i) => i.ReportId) : [])
                      }
                      aria-label="Select all visible reports"
                    />
                  </th>
                  <th>Severity</th>
                  <th>Console</th>
                  <th>Archive / Folder</th>
                  <th>Category</th>
                  <th>Extra</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {items.length === 0 && (
                  <tr>
                    <td colSpan={7} style={{ color: 'var(--text-secondary)' }}>
                      No compliance issues match the current filter. Click &ldquo;🔍 Scan Archives Now&rdquo; to
                      run a zero-decompression header audit across configured ROM collections.
                    </td>
                  </tr>
                )}
                {items.map((rep) => {
                  const checked = selectedIds.includes(rep.ReportId);
                  const active = inspectedItem?.ReportId === rep.ReportId;
                  const sevPill =
                    rep.Severity === 'Error'
                      ? 'err'
                      : rep.Severity === 'Warning'
                        ? 'warn'
                        : 'accent';
                  return (
                    <tr
                      key={rep.ReportId}
                      className={active ? 'selected' : ''}
                      style={{ cursor: 'pointer' }}
                      onClick={() => setInspectedId(rep.ReportId)}
                    >
                      <td data-label="Select" onClick={(e) => e.stopPropagation()}>
                        <input
                          type="checkbox"
                          checked={checked}
                          onChange={(e) =>
                            setSelectedIds((prev) =>
                              e.target.checked
                                ? [...prev, rep.ReportId]
                                : prev.filter((x) => x !== rep.ReportId)
                            )
                          }
                        />
                      </td>
                      <td data-label="Severity">
                        <span className={`hk-pill ${sevPill}`}>{rep.Severity}</span>
                      </td>
                      <td data-label="Console">{rep.ConsoleName}</td>
                      <td data-label="Archive">
                        <strong>{rep.TargetFileName}</strong>
                        <div style={{ fontSize: 11, color: 'var(--text-secondary)' }}>
                          {rep.SummaryText}
                        </div>
                      </td>
                      <td data-label="Category">{rep.PrimaryCategory}</td>
                      <td data-label="Extra Clutter">
                        {rep.ExtraDeletableCount > 0 ? (
                          <span className="hk-pill warn">{rep.ExtraDeletableCount} cleanable</span>
                        ) : (
                          '—'
                        )}
                      </td>
                      <td data-label="Status">
                        <span
                          className={`hk-pill ${rep.ResolutionStatus === 'Resolved' ? 'ok' : ''}`}
                        >
                          {rep.ResolutionStatus}
                        </span>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>

        {/* Right: Inner Archive File Tree Inspector */}
        <div className="hk-card">
          {inspectedItem ? (
            <>
              <div style={{ marginBottom: 10 }}>
                <h3 className="hk-card-title">🔬 Inner Archive File Tree Inspector</h3>
                <div style={{ fontWeight: 700, marginTop: 4 }}>{inspectedItem.TargetFileName}</div>
                <div
                  className="mono"
                  style={{
                    fontSize: 11,
                    color: 'var(--text-secondary)',
                    wordBreak: 'break-all',
                  }}
                >
                  {inspectedItem.TargetFullPath}
                </div>
                <div style={{ fontSize: 11.5, marginTop: 4 }}>
                  Template: <strong>{inspectedItem.TemplateDisplayName}</strong>
                </div>
              </div>

              {/* Issues list */}
              <div style={{ display: 'flex', flexDirection: 'column', gap: 5, marginBottom: 12 }}>
                {inspectedItem.Issues.map((iss, idx) => (
                  <div
                    key={idx}
                    style={{
                      padding: '6px 9px',
                      borderRadius: 'var(--radius-sm)',
                      background: 'var(--bg-elevated)',
                      border: '1px solid var(--border-subtle)',
                      fontSize: 12,
                    }}
                  >
                    <strong>[{iss.Kind}]</strong> {iss.Message}
                  </div>
                ))}
              </div>

              {/* Entry Nodes table with per-file CanClean checkboxes */}
              <div
                className="table-wrapper"
                tabIndex={0}
                role="region"
                aria-label="Archive inner files"
                style={{ maxHeight: 380 }}
              >
                <table className="hk-table">
                  <thead>
                    <tr>
                      <th style={{ width: 48 }}>Clean</th>
                      <th>Internal Path</th>
                      <th>Role</th>
                      <th>CRC32</th>
                      <th>Size</th>
                    </tr>
                  </thead>
                  <tbody>
                    {(inspectedItem.EntryNodes || []).map((node) => (
                      <tr key={node.InternalPath}>
                        <td>
                          <input
                            type="checkbox"
                            disabled={!node.CanClean}
                            checked={Boolean(customCleanPaths[node.InternalPath])}
                            onChange={(e) =>
                              setCustomCleanPaths((prev) => ({
                                ...prev,
                                [node.InternalPath]: e.target.checked,
                              }))
                            }
                            title={
                              node.CanClean
                                ? 'Safe extra clutter eligible for 7z d removal'
                                : 'Protected primary ROM or companion descriptor (cannot be deleted)'
                            }
                          />
                        </td>
                        <td className="mono" style={{ fontSize: 11.5 }}>
                          {node.InternalPath}
                        </td>
                        <td>
                          <span
                            className={`hk-pill ${node.CanClean ? 'warn' : 'ok'}`}
                            style={{ fontSize: 10.5 }}
                          >
                            {node.RoleClassification}
                          </span>
                        </td>
                        <td className="mono" style={{ fontSize: 11 }}>
                          {node.Crc32 || '—'}
                        </td>
                        <td style={{ fontSize: 11.5, whiteSpace: 'nowrap' }}>
                          {formatBytes(node.SizeBytes)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </>
          ) : (
            <div style={{ color: 'var(--text-secondary)' }}>
              Select a compliance report row on the left to inspect its internal 7z/ZIP file tree,
              CRC32 headers, and protected vs. cleanable entries.
            </div>
          )}
        </div>
      </div>

      {/* 7z d Dry-Run Preview & Confirmation Modal */}
      <ModalDialog
        open={dryRunPreview !== null}
        onClose={() => setDryRunPreview(null)}
        title="🧪 Guarded 7z d Clutter Removal — Dry-Run Preview"
        size="normal"
        closedBy="any"
        footer={
          <>
            <button type="button" className="hk-btn" onClick={() => setDryRunPreview(null)}>
              Cancel
            </button>
            {dryRunPreview?.IsAllowed && inspectedItem && (
              <button
                type="button"
                className="hk-btn success"
                disabled={busyAction}
                onClick={() =>
                  void handleExecuteCleanExtra(
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
                <strong>{formatBytes(dryRunPreview.ReclaimedBytes)}</strong>.
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
                      {f.InternalPath} ({formatBytes(f.SizeBytes)})
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
                      {f.InternalPath} ({formatBytes(f.SizeBytes)})
                    </li>
                  ))}
                </ul>
              </div>
            </div>
          </div>
        )}
      </ModalDialog>

      {/* Archive Templates Modal */}
      <ArchiveTemplateEditorDialog
        open={templatesOpen}
        onClose={() => setTemplatesOpen(false)}
        templates={activeTemplates}
        onSaveTemplates={(updated) =>
          updateConfig((d) => {
            d.RomLauncherGenerator.ArchiveTemplates = updated;
            for (const j of d.DefinedJobs) {
              if (j.RomLauncherGenerator) {
                j.RomLauncherGenerator.ArchiveTemplates = structuredClone(updated);
              }
            }
          })
        }
      />
    </div>
  );
};
