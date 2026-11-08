import React, { useCallback, useEffect, useState } from 'react';
import { apiClient } from '../services/apiClient';
import type { FsBrowseResponse, FsDriveItem } from '../types/models';
import { ModalDialog } from './ModalDialog';

interface ServerFolderPickerDialogProps {
  open: boolean;
  onClose: () => void;
  initialPath?: string;
  includeFiles?: boolean;
  title?: string;
  onSelect: (selectedPath: string) => void;
}

export const ServerFolderPickerDialog: React.FC<ServerFolderPickerDialogProps> = ({
  open,
  onClose,
  initialPath,
  includeFiles = false,
  title = 'Select Server Folder or Network UNC Path',
  onSelect,
}) => {
  const [drives, setDrives] = useState<FsDriveItem[]>([]);
  const [pathInput, setPathInput] = useState<string>(initialPath || 'C:\\');
  const [browseData, setBrowseData] = useState<FsBrowseResponse | null>(null);
  const [selectedFile, setSelectedFile] = useState<string>('');
  const [filterText, setFilterText] = useState<string>('');
  const [loading, setLoading] = useState<boolean>(false);
  const [error, setError] = useState<string>('');

  const loadDirectory = useCallback(
    async (targetPath: string) => {
      if (!targetPath.trim()) return;
      setLoading(true);
      setError('');
      setSelectedFile('');
      try {
        const res = await apiClient.browseDirectory(targetPath.trim(), includeFiles);
        setBrowseData(res);
        setPathInput(res.currentPath);
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Unable to open folder.');
      } finally {
        setLoading(false);
      }
    },
    [includeFiles]
  );

  useEffect(() => {
    if (!open) return;
    void (async () => {
      try {
        const drv = await apiClient.listDrives();
        setDrives(drv);
        const start =
          initialPath && initialPath.trim().length >= 2
            ? initialPath.trim()
            : drv.find((d) => d.isReady)?.name || 'C:\\';
        setPathInput(start);
        await loadDirectory(start);
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Could not enumerate server drives.');
      }
    })();
  }, [open, initialPath, loadDirectory]);

  const filteredDirs = (browseData?.directories || []).filter(
    (d) => !filterText || d.name.toLowerCase().includes(filterText.toLowerCase())
  );
  const filteredFiles = (browseData?.files || []).filter(
    (f) => !filterText || f.name.toLowerCase().includes(filterText.toLowerCase())
  );

  const finalSelection = includeFiles && selectedFile ? selectedFile : pathInput.trim();

  return (
    <ModalDialog
      open={open}
      onClose={onClose}
      title={title}
      size="wide"
      closedBy="any"
      footer={
        <>
          <button type="button" className="hk-btn" onClick={onClose}>
            Cancel
          </button>
          <button
            type="button"
            className="hk-btn primary"
            disabled={!finalSelection}
            onClick={() => {
              onSelect(finalSelection);
              onClose();
            }}
          >
            ✓ Use Path
          </button>
        </>
      }
    >
      <div className="hk-row" style={{ marginBottom: 10 }}>
        {drives.map((d) => (
          <button
            key={d.driveLetter}
            type="button"
            className="hk-btn sm"
            disabled={!d.isReady}
            onClick={() => void loadDirectory(d.name)}
            title={`${d.volumeLabel || d.driveType} — ${d.freeGb} GB free`}
          >
            💾 {d.driveLetter} ({d.freeGb >= 1000 ? `${(d.freeGb / 1024).toFixed(1)} TB` : `${d.freeGb.toFixed(0)} GB`})
          </button>
        ))}
      </div>

      <form
        className="hk-row"
        style={{ marginBottom: 10 }}
        onSubmit={(e) => {
          e.preventDefault();
          void loadDirectory(pathInput);
        }}
      >
        <input
          type="text"
          className="hk-input mono"
          style={{ flex: '1 1 320px' }}
          value={pathInput}
          onChange={(e) => setPathInput(e.target.value)}
          placeholder="Enter drive path or UNC share (e.g. V:\Media or \\NAS\Share)"
          autoFocus
        />
        <button type="submit" className="hk-btn">
          Go
        </button>
        {browseData?.parentPath && (
          <button
            type="button"
            className="hk-btn"
            onClick={() => browseData.parentPath && void loadDirectory(browseData.parentPath)}
          >
            ⬆ Up
          </button>
        )}
        <input
          type="search"
          className="hk-input"
          style={{ maxWidth: 210 }}
          value={filterText}
          onChange={(e) => setFilterText(e.target.value)}
          placeholder="Filter items..."
        />
      </form>

      {error && (
        <div className="hk-alert-banner err" style={{ marginBottom: 10 }}>
          <span>⚠ {error}</span>
          <span style={{ fontSize: 12 }}>
            You can still click <strong>✓ Use Path</strong> to save a manual or currently offline UNC path.
          </span>
        </div>
      )}

      <div
        className="table-wrapper"
        tabIndex={0}
        role="region"
        aria-label="Server directory contents"
        style={{ maxHeight: 360 }}
      >
        <table className="hk-table">
          <thead>
            <tr>
              <th>Name</th>
              <th>Type</th>
              <th>Details</th>
            </tr>
          </thead>
          <tbody>
            {loading && (
              <tr>
                <td colSpan={3}>Loading directory contents...</td>
              </tr>
            )}
            {!loading &&
              filteredDirs.map((dir) => (
                <tr
                  key={dir.fullPath}
                  style={{ cursor: 'pointer' }}
                  onClick={() => void loadDirectory(dir.fullPath)}
                >
                  <td>📁 {dir.name}</td>
                  <td>Folder</td>
                  <td style={{ color: 'var(--text-secondary)', fontSize: 11.5 }}>
                    {new Date(dir.lastModifiedUtc).toLocaleDateString()}
                  </td>
                </tr>
              ))}
            {!loading &&
              includeFiles &&
              filteredFiles.map((file) => (
                <tr
                  key={file.fullPath}
                  className={selectedFile === file.fullPath ? 'selected' : ''}
                  style={{ cursor: 'pointer' }}
                  onClick={() => setSelectedFile(file.fullPath)}
                  onDoubleClick={() => {
                    onSelect(file.fullPath);
                    onClose();
                  }}
                >
                  <td>📄 {file.name}</td>
                  <td>{file.extension || 'File'}</td>
                  <td style={{ color: 'var(--text-secondary)', fontSize: 11.5 }}>
                    {(file.sizeBytes / 1024).toFixed(1)} KB
                  </td>
                </tr>
              ))}
            {!loading && filteredDirs.length === 0 && filteredFiles.length === 0 && (
              <tr>
                <td colSpan={3} style={{ color: 'var(--text-secondary)' }}>
                  No matching entries in this directory.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </ModalDialog>
  );
};
