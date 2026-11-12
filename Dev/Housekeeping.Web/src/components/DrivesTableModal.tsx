// @module: Housekeeping.Web/components/DrivesTableModal
// @purpose: Shared accessible modal dialog listing all mounted local and SMB/NAS drives with sticky headers and hover hints.
// @depends-on: react, ./ModalDialog, ../types/models, ../utils/formatDrive
// @exports: DrivesTableModal
import React from 'react';
import type { DiskDriveMetricItem } from '../types/models';
import { formatDriveFree } from '../utils/formatDrive';
import { ModalDialog } from './ModalDialog';

interface DrivesTableModalProps {
  open: boolean;
  onClose: () => void;
  disks: DiskDriveMetricItem[];
}

export const DrivesTableModal: React.FC<DrivesTableModalProps> = ({ open, onClose, disks }) => (
  <ModalDialog
    open={open}
    onClose={onClose}
    title="💾 Mounted Local & NAS Drives"
    size="normal"
    closedBy="any"
  >
    <div className="table-wrapper" tabIndex={0} role="region" aria-label="All mounted drives">
      <table className="hk-table stack-mobile">
        <thead>
          <tr>
            <th
              className="hk-has-hint"
              title={'Windows drive letter or mount root.\nClick any sidebar drive row to inspect full share details.'}
            >
              Drive
            </th>
            <th
              className="hk-has-hint"
              title={'Storage transport classification.\nDistinguishes local NVMe/SATA volumes from mapped SMB/NAS shares.'}
            >
              Type
            </th>
            <th
              className="hk-has-hint"
              title={'Filesystem volume label and UNC network share path.\nHelps verify NAS vault targets before running heavy jobs.'}
            >
              Volume / Share
            </th>
            <th
              className="hk-has-hint"
              title={'Real-time mount reachability.\nOffline network shares trigger Housekeeping network-drop protection.'}
            >
              Status
            </th>
            <th
              className="hk-has-hint"
              title={'Available writable capacity on the volume.\nWarns below 12% free and flags critical below 25 GB or 5%.'}
            >
              Free Space
            </th>
            <th
              className="hk-has-hint"
              title={'Total formatted capacity reported by the volume.\nDisplayed in gigabytes or terabytes for capacity planning.'}
            >
              Total Capacity
            </th>
          </tr>
        </thead>
        <tbody>
          {disks.map((d) => {
            const isReady = d.IsReady !== false;
            return (
              <tr key={d.DriveLetter}>
                <td data-label="Drive">
                  <strong>{d.DriveLetter}</strong>
                </td>
                <td data-label="Type">
                  {d.IsNetwork ? '🌐 Network / NAS' : d.DriveType || 'Local'}
                </td>
                <td
                  data-label="Volume / Share"
                  style={{
                    fontFamily: d.RemotePath ? 'var(--font-mono)' : undefined,
                    fontSize: 12,
                  }}
                >
                  {d.RemotePath
                    ? d.VolumeLabel
                      ? `${d.VolumeLabel} (${d.RemotePath})`
                      : d.RemotePath
                    : d.VolumeLabel || '—'}
                </td>
                <td data-label="Status">
                  <span className={`hk-pill nowrap ${isReady ? 'ok' : 'err'}`}>
                    {isReady ? 'Online' : 'Offline (!)'}
                  </span>
                </td>
                <td data-label="Free Space">{isReady ? d.FreeText || formatDriveFree(d) : '—'}</td>
                <td data-label="Total Capacity">
                  {isReady
                    ? d.TotalGb >= 1000
                      ? `${(d.TotalGb / 1024).toFixed(2)} TB`
                      : `${d.TotalGb.toFixed(1)} GB`
                    : '—'}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  </ModalDialog>
);
