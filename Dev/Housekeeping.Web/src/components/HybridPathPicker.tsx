import React, { useState } from 'react';
import { useAppState } from '../context/AppStateContext';
import { apiClient } from '../services/apiClient';
import { ServerFolderPickerDialog } from './ServerFolderPickerDialog';

interface HybridPathPickerProps {
  value: string;
  onChange: (nextPath: string) => void;
  placeholder?: string;
  mode?: 'folder' | 'file';
  fileFilter?: string;
  title?: string;
  compactButton?: boolean;
}

/**
 * Hybrid Dual-Mode Path Picker (Decision 4):
 * - On localhost with Desktop Tray Host active: 1-click Native Windows OS Dialog + optional Server/UNC Explorer Modal.
 * - Over LAN / Tailnet / Mobile: opens the Server Drive & UNC Explorer Modal directly.
 */
export const HybridPathPicker: React.FC<HybridPathPickerProps> = ({
  value,
  onChange,
  placeholder,
  mode = 'folder',
  fileFilter,
  title,
  compactButton = false,
}) => {
  const { authStatus, activeTargetNodeId } = useAppState();
  const [modalOpen, setModalOpen] = useState(false);
  const [busy, setBusy] = useState(false);

  const canUseNativeOsDialog =
    Boolean(authStatus?.nativeDialogsAvailable) && activeTargetNodeId === 'local';

  const handlePrimaryBrowse = async () => {
    if (!canUseNativeOsDialog) {
      setModalOpen(true);
      return;
    }
    setBusy(true);
    try {
      const res =
        mode === 'file'
          ? await apiClient.nativePickFile(value, fileFilter, title)
          : await apiClient.nativePickFolder(value, title);
      if (!res.cancelled && res.path) {
        onChange(res.path);
      }
    } catch (err) {
      console.debug('[HybridPathPicker] native OS picker unavailable, falling back to modal:', err);
      setModalOpen(true);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="hk-path-picker">
      <input
        type="text"
        className="hk-input mono"
        value={value ?? ''}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder || (mode === 'file' ? 'C:\\path\\to\\file.exe' : 'V:\\Media or \\\\NAS\\Share')}
        aria-label={title || placeholder || 'Path'}
      />
      <button
        type="button"
        className="hk-btn sm"
        disabled={busy}
        onClick={() => void handlePrimaryBrowse()}
        title={
          canUseNativeOsDialog
            ? 'Open Native Windows OS Picker'
            : 'Browse Server Drives & UNC Shares'
        }
      >
        {compactButton ? '📁' : '📁 Browse...'}
      </button>
      {canUseNativeOsDialog && (
        <button
          type="button"
          className="hk-btn sm"
          onClick={() => setModalOpen(true)}
          title="Open Built-In Server Drive / UNC Explorer Modal"
        >
          🌐
        </button>
      )}
      <ServerFolderPickerDialog
        open={modalOpen}
        onClose={() => setModalOpen(false)}
        initialPath={value}
        includeFiles={mode === 'file'}
        title={title || (mode === 'file' ? 'Select File on Server' : 'Select Folder on Server')}
        onSelect={onChange}
      />
    </div>
  );
};
