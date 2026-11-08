import React, { useEffect, useState } from 'react';
import { useAppState, type NavTabId } from '../context/AppStateContext';

interface SidebarNavRailProps {
  onOpenSearch: () => void;
  onOpenShortcuts: () => void;
}

export const SidebarNavRail: React.FC<SidebarNavRailProps> = ({ onOpenSearch, onOpenShortcuts }) => {
  const {
    activeTab,
    setActiveTab,
    authStatus,
    telemetry,
    openComplianceCount,
  } = useAppState();

  const disks = telemetry?.Disks || [];

  const [collapsed, setCollapsed] = useState<boolean>(() => {
    try {
      return localStorage.getItem('hk_sidebar_collapsed') === 'true';
    } catch {
      return false;
    }
  });

  const toggleCollapsed = () => {
    setCollapsed((prev) => {
      const next = !prev;
      try {
        localStorage.setItem('hk_sidebar_collapsed', String(next));
      } catch {
        // ignore
      }
      return next;
    });
  };

  const navItems: { id: NavTabId; label: string; icon: string; keyNum: string; badge?: number }[] = [
    { id: 'dashboard', label: 'Dashboard', icon: '📊', keyNum: '1' },
    { id: 'queues', label: 'Job Queues', icon: '🗂', keyNum: '2' },
    { id: 'builder', label: 'Job Builder', icon: '🛠', keyNum: '3' },
    { id: 'modules', label: 'Modules', icon: '🧩', keyNum: '4' },
    { id: 'reports', label: 'Reports', icon: '🛡', keyNum: '5', badge: openComplianceCount },
    { id: 'settings', label: 'Settings', icon: '⚙', keyNum: '6' },
    { id: 'history', label: 'History', icon: '📜', keyNum: '7' },
  ];

  // Hotkey navigation: 1..7 switches view when not in an input/textarea
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
  }, [setActiveTab]);

  const versionStr = authStatus?.version || 'v1.0.0-rc.11-hf.1';

  return (
    <aside
      className={`hk-sidebar-rail ${collapsed ? 'collapsed' : ''}`}
      aria-label="Primary sidebar navigation"
    >
      {/* Brand Header */}
      <div className="hk-sidebar-header">
        <img src="/app_icon.png" alt="Housekeeping" className="hk-sidebar-logo" />
        {!collapsed && (
          <div className="hk-sidebar-brand-text">
            <span className="hk-sidebar-title">Housekeeping</span>
            <span className="hk-sidebar-version">{versionStr}</span>
          </div>
        )}
      </div>

      {/* Primary Navigation Rail */}
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
              <span className="hk-sidebar-icon" aria-hidden="true">{item.icon}</span>
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

      {/* Sidebar Quick Utility Buttons */}
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

      {/* Pinned Drive Free-Space Footer */}
      {!collapsed && disks.length > 0 && (
        <div className="hk-sidebar-drives" aria-label="Mounted storage free space">
          <div className="hk-sidebar-drives-title">
            <span>💾 MOUNTED STORAGE</span>
            <span className="hk-text-muted">{disks.length}</span>
          </div>
          <div className="hk-sidebar-drives-list">
            {disks.slice(0, 10).map((d) => {
              const isReady = d.IsReady !== false;
              const ratio = isReady && d.TotalGb > 0 ? d.FreeGb / d.TotalGb : 1;
              const statusClass = !isReady
                ? 'err'
                : d.FreeGb < 25 || ratio < 0.05
                  ? 'err'
                  : ratio < 0.12
                    ? 'warn'
                    : 'ok';
              return (
                <div
                  key={d.DriveLetter}
                  className={`hk-sidebar-drive-item ${statusClass}`}
                  title={`${d.IsNetwork ? '🌐 Network' : '💾 Local'} ${d.DriveLetter} ${d.VolumeLabel ? `(${d.VolumeLabel})` : ''} • ${d.FreeGb.toFixed(1)} GB free of ${d.TotalGb.toFixed(1)} GB`}
                >
                  <span className="hk-drive-dot" />
                  <span className="hk-drive-name">{d.DriveLetter}</span>
                  <span className="hk-drive-space">
                    {isReady ? `${d.FreeGb.toFixed(0)} GB` : 'Offline'}
                  </span>
                </div>
              );
            })}
          </div>
        </div>
      )}

      {/* Collapse / Expand Toggle Button */}
      <div className="hk-sidebar-footer">
        <button
          type="button"
          className="hk-sidebar-toggle-btn"
          onClick={toggleCollapsed}
          title={collapsed ? 'Expand Sidebar Rail (220px)' : 'Collapse to Icon Rail (56px)'}
          aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
        >
          {collapsed ? '▶' : '◀ Collapse'}
        </button>
      </div>
    </aside>
  );
};
