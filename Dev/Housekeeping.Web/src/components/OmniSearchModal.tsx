import React, { useEffect, useMemo, useRef, useState } from 'react';
import { useAppState, type NavTabId } from '../context/AppStateContext';
import { ModalDialog } from './ModalDialog';

interface OmniSearchModalProps {
  open: boolean;
  onClose: () => void;
}

interface SearchItem {
  id: string;
  category: 'Navigation' | 'Queue' | 'Settings' | 'Storage';
  title: string;
  subtitle: string;
  action: () => void;
}

export const OmniSearchModal: React.FC<OmniSearchModalProps> = ({ open, onClose }) => {
  const { setActiveTab, config, telemetry } = useAppState();
  const disks = telemetry?.Disks || [];
  const [query, setQuery] = useState('');
  const [selectedIndex, setSelectedIndex] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (open) {
      setQuery('');
      setSelectedIndex(0);
      window.setTimeout(() => inputRef.current?.focus(), 50);
    }
  }, [open]);

  const items: SearchItem[] = useMemo(() => {
    const list: SearchItem[] = [
      {
        id: 'nav-dashboard',
        category: 'Navigation',
        title: 'Dashboard Overview',
        subtitle: 'Live KPIs, Active Job Telemetry, and Storage Bar',
        action: () => { setActiveTab('dashboard'); onClose(); },
      },
      {
        id: 'nav-queues',
        category: 'Navigation',
        title: 'Job Queues & Schedules',
        subtitle: 'Manage and trigger automated maintenance queue sequences',
        action: () => { setActiveTab('queues'); onClose(); },
      },
      {
        id: 'nav-builder',
        category: 'Navigation',
        title: 'Job Builder & Pipeline',
        subtitle: 'Configure discrete tasks, HandBrake profiles, and sync jobs',
        action: () => { setActiveTab('builder'); onClose(); },
      },
      {
        id: 'nav-modules',
        category: 'Navigation',
        title: 'Modules Catalog',
        subtitle: 'Browse all available automation modules and scripts',
        action: () => { setActiveTab('modules'); onClose(); },
      },
      {
        id: 'nav-reports',
        category: 'Navigation',
        title: 'Reports & Compliance Center',
        subtitle: 'Audit logs, soft-delete quarantine, and compliance findings',
        action: () => { setActiveTab('reports'); onClose(); },
      },
      {
        id: 'nav-settings',
        category: 'Navigation',
        title: 'Settings & Tools',
        subtitle: 'Configure paths, credentials, watchdog, and System Doctor',
        action: () => { setActiveTab('settings'); onClose(); },
      },
      {
        id: 'nav-history',
        category: 'Navigation',
        title: 'Execution Run History',
        subtitle: 'Review historical logs and timing metrics for previous runs',
        action: () => { setActiveTab('history'); onClose(); },
      },
    ];

    // Add Queues
    if (Array.isArray(config?.NamedQueues)) {
      for (const q of config.NamedQueues) {
        list.push({
          id: `queue-${q.Id}`,
          category: 'Queue',
          title: q.Name || q.Id,
          subtitle: `Queue (${q.Jobs?.length || 0} jobs) • Schedule: ${q.EnableSchedule ? q.ScheduleMode : 'Manual'}`,
          action: () => {
            setActiveTab('queues');
            onClose();
          },
        });
      }
    }

    // Add Settings sub-tabs
    list.push(
      {
        id: 'set-tools',
        category: 'Settings',
        title: 'Settings: External Tools & HandBrake',
        subtitle: 'Configure ffmpeg, 7z.exe, and video encoding presets',
        action: () => {
          sessionStorage.setItem('hk-settings-subtab', 'tools');
          setActiveTab('settings');
          onClose();
        },
      },
      {
        id: 'set-watchdog',
        category: 'Settings',
        title: 'Settings: Watchdog & Process Guard',
        subtitle: 'Lock files, PID monitoring, and crash protection',
        action: () => {
          sessionStorage.setItem('hk-settings-subtab', 'watchdog');
          setActiveTab('settings');
          onClose();
        },
      },
      {
        id: 'set-network',
        category: 'Settings',
        title: 'Settings: Network Safety & rclone Locking',
        subtitle: 'Configure rclone bisync locking and SMB share timeouts',
        action: () => {
          sessionStorage.setItem('hk-settings-subtab', 'network');
          setActiveTab('settings');
          onClose();
        },
      },
      {
        id: 'set-doctor',
        category: 'Settings',
        title: 'Settings: 🩺 System Doctor',
        subtitle: 'Run diagnostic health checks and create support bundles',
        action: () => {
          sessionStorage.setItem('hk-settings-subtab', 'doctor');
          setActiveTab('settings');
          onClose();
        },
      }
    );

    // Add Storage Drives
    for (const d of disks) {
      list.push({
        id: `drive-${d.DriveLetter}`,
        category: 'Storage',
        title: `Drive ${d.DriveLetter} ${d.VolumeLabel ? `(${d.VolumeLabel})` : ''}`,
        subtitle: `${d.FreeGb.toFixed(1)} GB free of ${d.TotalGb.toFixed(1)} GB (${d.IsNetwork ? 'Network share' : 'Local disk'})`,
        action: () => {
          setActiveTab('dashboard');
          onClose();
        },
      });
    }

    return list;
  }, [config, disks, setActiveTab, onClose]);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return items.slice(0, 10);
    return items.filter(
      (it) => it.title.toLowerCase().includes(q) || it.subtitle.toLowerCase().includes(q)
    );
  }, [items, query]);

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setSelectedIndex((prev) => (prev + 1) % Math.max(1, filtered.length));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setSelectedIndex((prev) => (prev - 1 + filtered.length) % Math.max(1, filtered.length));
    } else if (e.key === 'Enter') {
      e.preventDefault();
      if (filtered[selectedIndex]) {
        filtered[selectedIndex].action();
      }
    }
  };

  return (
    <ModalDialog
      open={open}
      onClose={onClose}
      title="🔍 Global Omni-Search"
      size="narrow"
      closedBy="any"
    >
      <div className="hk-omni-search" onKeyDown={handleKeyDown}>
        <input
          ref={inputRef}
          type="search"
          className="hk-input"
          placeholder="Search navigation, queues, settings, drives... (Esc to close)"
          value={query}
          onChange={(e) => {
            setQuery(e.target.value);
            setSelectedIndex(0);
          }}
          aria-label="Omni search input"
          autoComplete="off"
          spellCheck={false}
        />

        <div className="hk-omni-results" role="listbox">
          {filtered.length === 0 ? (
            <div className="hk-omni-empty">No matching views or resources found.</div>
          ) : (
            filtered.map((it, idx) => (
              <div
                key={it.id}
                role="option"
                aria-selected={idx === selectedIndex}
                className={`hk-omni-item ${idx === selectedIndex ? 'selected' : ''}`}
                onClick={it.action}
                onMouseEnter={() => setSelectedIndex(idx)}
              >
                <div className="hk-omni-item-header">
                  <span className="hk-pill sm">{it.category}</span>
                  <strong>{it.title}</strong>
                </div>
                <div className="hk-omni-item-sub">{it.subtitle}</div>
              </div>
            ))
          )}
        </div>

        <div className="hk-omni-footer">
          <span>Navigate with <kbd>↑</kbd> <kbd>↓</kbd></span>
          <span>Select with <kbd>Enter</kbd></span>
          <span>Close with <kbd>Esc</kbd></span>
        </div>
      </div>
    </ModalDialog>
  );
};
