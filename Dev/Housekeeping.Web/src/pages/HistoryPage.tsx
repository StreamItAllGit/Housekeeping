// @module: Housekeeping.Web/pages/HistoryPage
// @purpose: Pipeline execution history table with multi-column sorting, inline log viewer, and CSV/JSON export.
// @depends-on: react, ../components/ModalDialog, ../context/AppStateContext, ../hints/commonDialogs, ../services/apiClient, ../types/models, ../utils/historyExport
// @exports: HistoryPage
import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { ModalDialog } from '../components/ModalDialog';
import { useAppState } from '../context/AppStateContext';
import { COMMON_UI_HINTS } from '../hints/commonDialogs';
import { apiClient } from '../services/apiClient';
import type { RunHistoryEntry } from '../types/models';
import {
  exportHistoryToCsv,
  exportHistoryToJson,
  formatRunDuration,
  getRunDurationMs,
} from '../utils/historyExport';

type SortField = 'Started' | 'Duration' | 'Trigger' | 'Summary' | 'Processed' | 'Errors' | 'Status';
type SortOrder = 'asc' | 'desc';
type StatusFilter = 'ALL' | 'SUCCESS' | 'ERROR' | 'CANCELLED';

export const HistoryPage: React.FC = () => {
  const { showToast } = useAppState();
  const [entries, setEntries] = useState<RunHistoryEntry[]>([]);
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('ALL');
  const [sortField, setSortField] = useState<SortField>('Started');
  const [sortOrder, setSortOrder] = useState<SortOrder>('desc');
  const [selectedKeys, setSelectedKeys] = useState<Set<string>>(new Set());
  const [logModalTitle, setLogModalTitle] = useState('');
  const [logContent, setLogContent] = useState<string | null>(null);

  const loadHistory = useCallback(async () => {
    try {
      const list = await apiClient.getHistory();
      setEntries(list || []);
    } catch (err) {
      console.debug('[HistoryPage] loadHistory failed:', err);
    }
  }, []);

  useEffect(() => {
    void loadHistory();
  }, [loadHistory]);

  const getItemKey = (entry: RunHistoryEntry, originalIdx: number) =>
    `${entry.StartedAt}_${originalIdx}`;

  // Summary counts for filter pills
  const counts = useMemo(() => {
    let success = 0;
    let error = 0;
    let cancelled = 0;
    for (const e of entries) {
      const s = e.Status.toLowerCase();
      if (s === 'completed') success++;
      else if (s.includes('error') || s === 'failed') error++;
      else if (s.includes('cancel') || s.includes('interrupt')) cancelled++;
    }
    return { all: entries.length, success, error, cancelled };
  }, [entries]);

  const processedEntries = useMemo(() => {
    let result = entries.map((entry, originalIdx) => ({ entry, originalIdx }));

    // 1. Status Filter
    if (statusFilter !== 'ALL') {
      result = result.filter(({ entry }) => {
        const s = entry.Status.toLowerCase();
        if (statusFilter === 'SUCCESS') return s === 'completed';
        if (statusFilter === 'ERROR') return s.includes('error') || s === 'failed';
        if (statusFilter === 'CANCELLED')
          return s.includes('cancel') || s.includes('interrupt');
        return true;
      });
    }

    // 2. Search Filter
    if (search.trim()) {
      const q = search.toLowerCase();
      result = result.filter(
        ({ entry }) =>
          entry.ModulesSummary.toLowerCase().includes(q) ||
          entry.Status.toLowerCase().includes(q) ||
          entry.TriggerSource.toLowerCase().includes(q)
      );
    }

    // 3. Sorting
    result.sort((a, b) => {
      let cmp = 0;
      switch (sortField) {
        case 'Started':
          cmp = new Date(a.entry.StartedAt).getTime() - new Date(b.entry.StartedAt).getTime();
          break;
        case 'Duration':
          cmp =
            getRunDurationMs(a.entry.StartedAt, a.entry.CompletedAt) -
            getRunDurationMs(b.entry.StartedAt, b.entry.CompletedAt);
          break;
        case 'Trigger':
          cmp = a.entry.TriggerSource.localeCompare(b.entry.TriggerSource);
          break;
        case 'Summary':
          cmp = a.entry.ModulesSummary.localeCompare(b.entry.ModulesSummary);
          break;
        case 'Processed':
          cmp = a.entry.ItemsProcessed - b.entry.ItemsProcessed;
          break;
        case 'Errors':
          cmp = a.entry.ErrorsCount - b.entry.ErrorsCount;
          break;
        case 'Status':
          cmp = a.entry.Status.localeCompare(b.entry.Status);
          break;
      }
      return sortOrder === 'asc' ? cmp : -cmp;
    });

    return result;
  }, [entries, statusFilter, search, sortField, sortOrder]);

  const allVisibleKeys = useMemo(
    () => processedEntries.map(({ entry, originalIdx }) => getItemKey(entry, originalIdx)),
    [processedEntries]
  );

  const isAllSelected =
    allVisibleKeys.length > 0 && allVisibleKeys.every((k) => selectedKeys.has(k));

  const toggleSelectAll = () => {
    if (isAllSelected) {
      setSelectedKeys(new Set());
    } else {
      setSelectedKeys(new Set(allVisibleKeys));
    }
  };

  const invertSelection = () => {
    const next = new Set<string>();
    for (const k of allVisibleKeys) {
      if (!selectedKeys.has(k)) {
        next.add(k);
      }
    }
    setSelectedKeys(next);
  };

  const toggleRow = (key: string) => {
    setSelectedKeys((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  };

  const getExportEntries = (): RunHistoryEntry[] => {
    if (selectedKeys.size > 0) {
      return processedEntries
        .filter(({ entry, originalIdx }) => selectedKeys.has(getItemKey(entry, originalIdx)))
        .map((x) => x.entry);
    }
    return processedEntries.map((x) => x.entry);
  };

  const handleSort = (field: SortField) => {
    if (sortField === field) {
      setSortOrder((prev) => (prev === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortField(field);
      setSortOrder('desc');
    }
  };

  const renderSortArrow = (field: SortField) => {
    if (sortField !== field) return <span style={{ opacity: 0.35, marginLeft: 4 }}>⇅</span>;
    return <span style={{ marginLeft: 4, color: 'var(--amber)' }}>{sortOrder === 'asc' ? '▲' : '▼'}</span>;
  };

  const handleOpenLog = async (idx: number) => {
    try {
      const res = await apiClient.getHistoryLog(idx);
      setLogModalTitle(`${res.ModulesSummary} — ${new Date(res.StartedAt).toLocaleString()}`);
      setLogContent(res.content || '(Empty log file)');
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Log file not available on disk.');
    }
  };

  const handleClearHistory = async () => {
    await apiClient.clearHistory();
    setEntries([]);
    setSelectedKeys(new Set());
    showToast('Cleared run history.');
  };

  return (
    <div className="hk-card">
      <div className="hk-card-header">
        <div>
          <h3 className="hk-card-title">📜 Pipeline Execution History ({entries.length})</h3>
          <p className="hk-card-subtitle">
            View completed, cancelled, and scheduled pipeline runs with inline remote-accessible log
            inspection, multi-column sorting, and data export.
          </p>
        </div>

        <div className="hk-row" style={{ flexWrap: 'wrap', gap: 8 }}>
          <input
            type="search"
            className="hk-input hk-mobile-flex-input"
            style={{ width: 200 }}
            placeholder="Filter runs..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
          <button type="button" className="hk-btn sm" onClick={() => void loadHistory()}>
            🔄 Refresh
          </button>
          {entries.length > 0 && (
            <button
              type="button"
              className="hk-btn sm danger"
              onClick={() => void handleClearHistory()}
            >
              🗑 Clear History
            </button>
          )}
        </div>
      </div>

      {/* Filter Pills & Export Bar */}
      <div
        className="hk-row"
        style={{
          padding: '8px 0 12px 0',
          justifyContent: 'space-between',
          flexWrap: 'wrap',
          gap: 8,
          borderBottom: '1px solid var(--border-subtle)',
          marginBottom: 12,
        }}
      >
        {/* Status Filter Pills */}
        <div className="hk-row" style={{ gap: 6, flexWrap: 'wrap' }}>
          <span style={{ fontSize: 12, color: 'var(--text-secondary)', marginRight: 4 }}>Filter:</span>
          <button
            type="button"
            className={`hk-pill ${statusFilter === 'ALL' ? 'accent' : ''}`}
            style={{ cursor: 'pointer' }}
            onClick={() => setStatusFilter('ALL')}
          >
            All ({counts.all})
          </button>
          <button
            type="button"
            className={`hk-pill ${statusFilter === 'SUCCESS' ? 'ok' : ''}`}
            style={{ cursor: 'pointer' }}
            onClick={() => setStatusFilter('SUCCESS')}
          >
            Completed ({counts.success})
          </button>
          <button
            type="button"
            className={`hk-pill ${statusFilter === 'ERROR' ? 'err' : ''}`}
            style={{ cursor: 'pointer' }}
            onClick={() => setStatusFilter('ERROR')}
          >
            Error ({counts.error})
          </button>
          <button
            type="button"
            className={`hk-pill ${statusFilter === 'CANCELLED' ? 'warn' : ''}`}
            style={{ cursor: 'pointer' }}
            onClick={() => setStatusFilter('CANCELLED')}
          >
            Cancelled ({counts.cancelled})
          </button>
        </div>

        {/* Selection & Export Actions */}
        <div className="hk-row" style={{ gap: 8, flexWrap: 'wrap' }}>
          {selectedKeys.size > 0 && (
            <span style={{ fontSize: 12, color: 'var(--amber)', fontWeight: 600 }}>
              {selectedKeys.size} selected
            </span>
          )}
          <button
            type="button"
            className="hk-btn sm"
            onClick={toggleSelectAll}
            title={isAllSelected ? 'Deselect all visible rows' : 'Select all visible rows'}
          >
            {isAllSelected ? '☐ Deselect All' : '☑ Select All'}
          </button>
          <button
            type="button"
            className="hk-btn sm"
            onClick={invertSelection}
            title="Invert row selection"
          >
            ⇄ Invert
          </button>
          <button
            type="button"
            className="hk-btn sm primary"
            onClick={() => exportHistoryToCsv(getExportEntries())}
            title={
              selectedKeys.size > 0
                ? `Export ${selectedKeys.size} selected entries to CSV`
                : `Export all ${processedEntries.length} filtered entries to CSV`
            }
          >
            📥 Export CSV
          </button>
          <button
            type="button"
            className="hk-btn sm primary"
            onClick={() => exportHistoryToJson(getExportEntries())}
            title={
              selectedKeys.size > 0
                ? `Export ${selectedKeys.size} selected entries to JSON`
                : `Export all ${processedEntries.length} filtered entries to JSON`
            }
          >
            📥 Export JSON
          </button>
        </div>
      </div>

      <div className="table-wrapper" tabIndex={0} role="region" aria-label="Run history">
        <table className="hk-table stack-mobile">
          <thead>
            <tr>
              <th className="hk-has-hint" style={{ width: 40, textAlign: 'center' }} title={COMMON_UI_HINTS.historySelectAll}>
                <input
                  type="checkbox"
                  className="hk-has-hint"
                  title={COMMON_UI_HINTS.historySelectAll}
                  checked={isAllSelected}
                  onChange={toggleSelectAll}
                  aria-label="Select all rows"
                />
              </th>
              <th
                className="hk-has-hint"
                onClick={() => handleSort('Started')}
                style={{ cursor: 'pointer', userSelect: 'none' }}
                title={COMMON_UI_HINTS.historyCol('Start Timestamp')}
              >
                Started {renderSortArrow('Started')}
              </th>
              <th
                className="hk-has-hint"
                onClick={() => handleSort('Duration')}
                style={{ cursor: 'pointer', userSelect: 'none' }}
                title={COMMON_UI_HINTS.historyCol('Run Duration')}
              >
                Duration {renderSortArrow('Duration')}
              </th>
              <th
                className="hk-has-hint"
                onClick={() => handleSort('Trigger')}
                style={{ cursor: 'pointer', userSelect: 'none' }}
                title={COMMON_UI_HINTS.historyCol('Trigger Source')}
              >
                Trigger {renderSortArrow('Trigger')}
              </th>
              <th
                className="hk-has-hint"
                onClick={() => handleSort('Summary')}
                style={{ cursor: 'pointer', userSelect: 'none' }}
                title={COMMON_UI_HINTS.historyCol('Queue / Job Summary')}
              >
                Queue / Job Summary {renderSortArrow('Summary')}
              </th>
              <th
                className="hk-has-hint"
                onClick={() => handleSort('Processed')}
                style={{ cursor: 'pointer', userSelect: 'none' }}
                title={COMMON_UI_HINTS.historyCol('Items Processed')}
              >
                Processed {renderSortArrow('Processed')}
              </th>
              <th className="hk-has-hint" style={{ width: 80 }} title={COMMON_UI_HINTS.historyCol('Items Skipped')}>
                Skipped
              </th>
              <th
                className="hk-has-hint"
                onClick={() => handleSort('Errors')}
                style={{ cursor: 'pointer', userSelect: 'none' }}
                title={COMMON_UI_HINTS.historyCol('Error Count')}
              >
                Errors {renderSortArrow('Errors')}
              </th>
              <th
                className="hk-has-hint"
                onClick={() => handleSort('Status')}
                style={{ cursor: 'pointer', userSelect: 'none' }}
                title={COMMON_UI_HINTS.historyCol('Terminal Run Status')}
              >
                Status {renderSortArrow('Status')}
              </th>
              <th className="hk-has-hint" style={{ width: 100 }} title={COMMON_UI_HINTS.historyCol('Run Log Viewer')}>
                Log
              </th>
            </tr>
          </thead>
          <tbody>
            {processedEntries.length === 0 && (
              <tr>
                <td colSpan={10} style={{ color: 'var(--text-secondary)', textAlign: 'center', padding: '24px 12px' }}>
                  {entries.length === 0
                    ? 'No execution history entries recorded yet.'
                    : 'No execution history matching current filter/search.'}
                </td>
              </tr>
            )}
            {processedEntries.map(({ entry, originalIdx }) => {
              const itemKey = getItemKey(entry, originalIdx);
              const isSelected = selectedKeys.has(itemKey);
              const statusLower = entry.Status.toLowerCase();
              const statusClass =
                statusLower === 'completed'
                  ? 'ok'
                  : statusLower.includes('error') || statusLower === 'failed'
                    ? 'err'
                    : 'warn';
              return (
                <tr
                  key={itemKey}
                  style={isSelected ? { backgroundColor: 'var(--bg-active-row, rgba(245, 158, 11, 0.08))' } : undefined}
                >
                  <td data-label="Select" style={{ textAlign: 'center' }}>
                    <input
                      type="checkbox"
                      className="hk-has-hint"
                      title={COMMON_UI_HINTS.historySelectRow}
                      checked={isSelected}
                      onChange={() => toggleRow(itemKey)}
                      aria-label={`Select run ${entry.ModulesSummary}`}
                    />
                  </td>
                  <td data-label="Started">{new Date(entry.StartedAt).toLocaleString()}</td>
                  <td data-label="Duration" className="mono">
                    {formatRunDuration(entry.StartedAt, entry.CompletedAt)}
                  </td>
                  <td data-label="Trigger">
                    <span className="hk-pill nowrap">{entry.TriggerSource}</span>
                  </td>
                  <td data-label="Summary">
                    <strong>{entry.ModulesSummary}</strong>
                  </td>
                  <td data-label="Processed">{entry.ItemsProcessed}</td>
                  <td data-label="Skipped">{entry.ItemsSkipped}</td>
                  <td
                    data-label="Errors"
                    style={{ color: entry.ErrorsCount > 0 ? 'var(--err)' : undefined, fontWeight: entry.ErrorsCount > 0 ? 600 : 'normal' }}
                  >
                    {entry.ErrorsCount}
                  </td>
                  <td data-label="Status">
                    <span className={`hk-pill nowrap ${statusClass}`}>{entry.Status}</span>
                  </td>
                  <td data-label="Log">
                    <button
                      type="button"
                      className="hk-btn sm"
                      disabled={!entry.LogFilePath}
                      onClick={() => void handleOpenLog(originalIdx)}
                    >
                      📄 View Log
                    </button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      <ModalDialog
        open={logContent !== null}
        onClose={() => setLogContent(null)}
        title={`📄 Run Log — ${logModalTitle}`}
        size="wide"
        closedBy="any"
        footer={
          <button type="button" className="hk-btn" onClick={() => setLogContent(null)}>
            Close
          </button>
        }
      >
        <div
          className="hk-terminal-surface"
          style={{ padding: 12, maxHeight: '65vh', overflow: 'auto', whiteSpace: 'pre-wrap' }}
        >
          {logContent}
        </div>
      </ModalDialog>
    </div>
  );
};
