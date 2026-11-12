// @module: Housekeeping.Web/utils/formatDrive
// @purpose: Shared drive free-space formatter and health status class resolver for sidebar storage rows and drive tables.
// @depends-on: ../types/models
// @exports: formatDriveFree, resolveDriveStatusClass
import type { DiskDriveMetricItem } from '../types/models';

export function formatDriveFree(d: Pick<DiskDriveMetricItem, 'IsReady' | 'FreeGb'>): string {
  if (d.IsReady === false) return 'Offline';
  if (d.FreeGb >= 1000) {
    return `${(d.FreeGb / 1024).toFixed(1)} TB`;
  }
  return `${d.FreeGb >= 100 ? d.FreeGb.toFixed(0) : d.FreeGb.toFixed(1)} GB`;
}

export function resolveDriveStatusClass(
  d: Pick<DiskDriveMetricItem, 'IsReady' | 'FreeGb' | 'TotalGb'>
): 'ok' | 'warn' | 'err' {
  const isReady = d.IsReady !== false;
  if (!isReady) return 'err';
  const ratio = d.TotalGb > 0 ? d.FreeGb / d.TotalGb : 1;
  if (d.FreeGb < 25 || ratio < 0.05) return 'err';
  if (ratio < 0.12) return 'warn';
  return 'ok';
}
