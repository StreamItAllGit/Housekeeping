// @module: Housekeeping.Web/utils/historyExport
// @purpose: Duration formatting and CSV/JSON browser download helpers extracted from HistoryPage.tsx.
// @depends-on: ../types/models
// @exports: formatRunDuration, getRunDurationMs, exportHistoryToCsv, exportHistoryToJson
import type { RunHistoryEntry } from '../types/models';

export function formatRunDuration(startIso: string, endIso: string): string {
  const ms = Math.max(0, new Date(endIso).getTime() - new Date(startIso).getTime());
  const totalSec = Math.floor(ms / 1000);
  const h = Math.floor(totalSec / 3600);
  const m = Math.floor((totalSec % 3600) / 60);
  const s = totalSec % 60;
  return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
}

export function getRunDurationMs(startIso: string, endIso: string): number {
  return Math.max(0, new Date(endIso).getTime() - new Date(startIso).getTime());
}

function downloadBlob(content: string, filename: string, mimeType: string) {
  const blob = new Blob([content], { type: mimeType });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}

export function exportHistoryToCsv(items: RunHistoryEntry[]): void {
  const headers = [
    'StartedAt',
    'CompletedAt',
    'Duration',
    'TriggerSource',
    'ModulesSummary',
    'ItemsProcessed',
    'ItemsSkipped',
    'ErrorsCount',
    'Status',
    'LogFilePath',
  ];
  const rows = items.map((e) => [
    `"${e.StartedAt}"`,
    `"${e.CompletedAt}"`,
    `"${formatRunDuration(e.StartedAt, e.CompletedAt)}"`,
    `"${e.TriggerSource.replace(/"/g, '""')}"`,
    `"${e.ModulesSummary.replace(/"/g, '""')}"`,
    e.ItemsProcessed,
    e.ItemsSkipped,
    e.ErrorsCount,
    `"${e.Status.replace(/"/g, '""')}"`,
    `"${(e.LogFilePath || '').replace(/"/g, '""')}"`,
  ]);
  const csvContent = [headers.join(','), ...rows.map((r) => r.join(','))].join('\r\n');
  const dateStr = new Date().toISOString().slice(0, 10).replace(/-/g, '');
  downloadBlob(csvContent, `Housekeeping_History_${dateStr}.csv`, 'text/csv');
}

export function exportHistoryToJson(items: RunHistoryEntry[]): void {
  const jsonContent = JSON.stringify(items, null, 2);
  const dateStr = new Date().toISOString().slice(0, 10).replace(/-/g, '');
  downloadBlob(jsonContent, `Housekeeping_History_${dateStr}.json`, 'application/json');
}
