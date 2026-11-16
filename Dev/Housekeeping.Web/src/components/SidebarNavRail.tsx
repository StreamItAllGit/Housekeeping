import React, { useEffect } from 'react';
import { useAppState, type NavTabId } from '../context/AppStateContext';
import { formatDriveFree, resolveDriveStatusClass } from '../utils/formatDrive';

interface SidebarNavRailProps {
  onOpenSearch: () => void;
  onOpenShortcuts: () => void;
}

export const SidebarNavRail: React.FC<SidebarNavRailProps> = ({
  onOpenSearch,
  onOpenShortcuts,
}) => {
  const {
    activeTab,
    setActiveTab,
    authStatus,
    telemetry,
    openComplianceCount,
    uiLayout,
    toggleSidebarCollapsed,
    setDrivesModalOpen,
  } = useAppState();

  const disks = telemetry?.Disks || [];
  const collapsed = Boolean(uiLayout.SidebarCollapsed);

  const navItems: { id: NavTabId; label: string; icon: string; keyNum: string; badge?: number }[] = [
    { id: 'dashboard', label: 'Dashboard', icon: '📊', keyNum: '1' },
    { id: 'queues', label: 'Job Queues', icon: '🗂', keyNum: '2' },
    { id: 'builder', label: 'Job Builder', icon: '🛠', keyNum: '3' },
    { id: 'modules', label: 'Modules', icon: '🧩', keyNum: '4' },
    { id: 'reports', label: 'Reports', icon: '🛡', keyNum: '5', badge: openComplianceCount },
    { id: 'settings', label: 'Settings', icon: '⚙', keyNum: '6' },
    { id: 'history', label: 'History', icon: '📜', keyNum: '7' },
  ];

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null;
      const tag = target?.tagName?.toLowerCase();
      if (tag === 'input' || tag === 'textarea' || tag === 'select' || target?.isContentEditable) {
        return;
      }
      if (e.ctrlKey || e.altKey || e.metaKey) return;

      const hit = navItems.find((it) => it.keyNum === e.key);
      if (hit) {
        e.preventDefault();
        setActiveTab(hit.id);
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [setActiveTab]);

  const rawVersion = authStatus?.version || '0.1.12-hf.1';
  const versionStr = rawVersion.startsWith('v') ? rawVersion : `v${rawVersion}`;

  return (
    <aside
      className={`hk-sidebar-rail ${collapsed ? 'collapsed' : ''}`}
      aria-label="Primary sidebar navigation"
    >
      <div className="hk-sidebar-header">
        <img src="/app_icon.png" alt="Housekeeping" className="hk-sidebar-logo" />
        {!collapsed && (
          <div className="hk-sidebar-brand-text">
            <span className="hk-sidebar-title">Housekeeping</span>
            <span className="hk-sidebar-version">{versionStr}</span>
          </div>
        )}
      </div>

      <nav className="hk-sidebar-nav" aria-label="Main Views">
        {navItems.map((item) => {
          const isActive = activeTab === item.id;
          return (
            <button
              key={item.id}
              type="button"
              className={`hk-sidebar-item ${isActive ? 'active' : ''}`}
              onClick={() => setActiveTab(item.id)}
              title={`${item.label} (${item.keyNum})`}
              aria-current={isActive ? 'page' : undefined}
            >
              <span className="hk-sidebar-icon" aria-hidden="true">
                {item.icon}
              </span>
              {!collapsed && (
                <>
                  <span className="hk-sidebar-label">{item.label}</span>
                  {typeof item.badge === 'number' && item.badge > 0 && (
                    <span className="hk-badge-count">{item.badge}</span>
                  )}
                  <span className="hk-sidebar-shortcut">{item.keyNum}</span>
                </>
              )}
            </button>
          );
        })}
      </nav>

      <div className="hk-sidebar-utils">
        <button
          type="button"
          className="hk-sidebar-util-btn"
          onClick={onOpenSearch}
          title="Global Omni-Search (Ctrl+K)"
        >
          <span className="hk-sidebar-icon">🔍</span>
          {!collapsed && <span>Search</span>}
          {!collapsed && <kbd className="hk-kbd">Ctrl+K</kbd>}
        </button>
        <button
          type="button"
          className="hk-sidebar-util-btn"
          onClick={onOpenShortcuts}
          title="Keyboard Shortcuts Cheat-Sheet (?)"
        >
          <span className="hk-sidebar-icon">⌨️</span>
          {!collapsed && <span>Shortcuts</span>}
          {!collapsed && <kbd className="hk-kbd">?</kbd>}
        </button>
      </div>

      {!collapsed && disks.length > 0 && (
        <div className="hk-sidebar-drives" aria-label="Mounted storage free space">
          <button
            type="button"
            className="hk-sidebar-drives-title"
            onClick={() => setDrivesModalOpen(true)}
            title="Click to inspect all mounted local and NAS drives"
          >
            <span>💾 MOUNTED STORAGE</span>
            <span className="hk-text-muted">{disks.length}</span>
          </button>
          <div className="hk-sidebar-drives-list">
            {disks.map((d) => {
              const statusClass = resolveDriveStatusClass(d);
              const shareSuffix = d.RemotePath
                ? ` (${d.VolumeLabel ? `${d.VolumeLabel} — ` : ''}${d.RemotePath})`
                : d.VolumeLabel
                  ? ` (${d.VolumeLabel})`
                  : '';
              return (
                <button
                  key={d.DriveLetter}
                  type="button"
                  className={`hk-sidebar-drive-item ${statusClass}`}
                  onClick={() => setDrivesModalOpen(true)}
                  title={`${d.IsNetwork ? '🌐 Network' : '💾 Local'} ${d.DriveLetter}${shareSuffix} • ${d.FreeGb.toFixed(1)} GB free of ${d.TotalGb.toFixed(1)} GB — click for details`}
                >
                  <span className="hk-drive-dot" />
                  <span className="hk-drive-name">{d.DriveLetter}</span>
                  <span className="hk-drive-space">{formatDriveFree(d)}</span>
                </button>
              );
            })}
          </div>
        </div>
      )}

      <div className="hk-sidebar-footer">
        <button
          type="button"
          className="hk-sidebar-toggle-btn"
          onClick={toggleSidebarCollapsed}
          title={
            collapsed
              ? 'Expand Sidebar Rail (220px) — Ctrl+B'
              : 'Collapse to Icon Rail (56px) — Ctrl+B'
          }
          aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
        >
          {collapsed ? '▶' : '◀ Collapse'}
        </button>
      </div>
    </aside>
  );
};
