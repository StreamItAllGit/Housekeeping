// @module: Housekeeping.Web/components/MobileBottomNavBar
// @purpose: Docked bottom primary section navigation bar for mobile and standalone PWA viewports (<= 820px) per Standard 09 §4.5.5.
// @depends-on: react, ../context/AppStateContext
// @exports: MobileBottomNavBar
import React from 'react';
import { useAppState, type NavTabId } from '../context/AppStateContext';

interface MobileNavItem {
  id: NavTabId;
  label: string;
  title: string;
  icon: string;
  badge?: number;
}

export const MobileBottomNavBar: React.FC = () => {
  const { activeTab, setActiveTab, openComplianceCount } = useAppState();

  const navItems: MobileNavItem[] = [
    { id: 'dashboard', label: 'Dashboard', title: 'Dashboard', icon: '📊' },
    { id: 'queues', label: 'Queues', title: 'Job Queues', icon: '🗂' },
    { id: 'builder', label: 'Builder', title: 'Job Builder', icon: '🛠' },
    { id: 'modules', label: 'Modules', title: 'Modules Catalog', icon: '🧩' },
    { id: 'reports', label: 'Reports', title: 'Reports Center', icon: '🛡', badge: openComplianceCount },
    { id: 'settings', label: 'Settings', title: 'Settings', icon: '⚙' },
    { id: 'history', label: 'History', title: 'Run History', icon: '📜' },
  ];

  return (
    <nav className="hk-mobile-bottom-nav hk-mobile-only" aria-label="Primary mobile navigation">
      {navItems.map((tab) => {
        const isActive = activeTab === tab.id;
        return (
          <button
            key={tab.id}
            type="button"
            className={`hk-mobile-nav-btn ${isActive ? 'active' : ''}`}
            onClick={() => setActiveTab(tab.id)}
            title={tab.title}
            aria-current={isActive ? 'page' : undefined}
          >
            <span className="hk-mobile-nav-icon" aria-hidden="true">
              {tab.icon}
              {typeof tab.badge === 'number' && tab.badge > 0 && (
                <span className="hk-badge-count">{tab.badge}</span>
              )}
            </span>
            <span className="hk-mobile-nav-label">{tab.label}</span>
          </button>
        );
      })}
    </nav>
  );
};
