import React from 'react';
import { ModalDialog } from './ModalDialog';

interface KeyboardShortcutsModalProps {
  open: boolean;
  onClose: () => void;
}

export const KeyboardShortcutsModal: React.FC<KeyboardShortcutsModalProps> = ({ open, onClose }) => {
  const shortcutGroups = [
    {
      title: 'Navigation Rail Views',
      items: [
        { keys: ['1'], desc: 'Switch to Dashboard overview' },
        { keys: ['2'], desc: 'Switch to Job Queues & schedules' },
        { keys: ['3'], desc: 'Switch to Job Builder' },
        { keys: ['4'], desc: 'Switch to Modules Catalog' },
        { keys: ['5'], desc: 'Switch to Reports & Compliance Center' },
        { keys: ['6'], desc: 'Switch to Settings & Tools' },
        { keys: ['7'], desc: 'Switch to Run History' },
      ],
    },
    {
      title: 'Global Controls & Tools',
      items: [
        { keys: ['Ctrl', 'K'], desc: 'Open Global Omni-Search' },
        { keys: ['Ctrl', 'B'], desc: 'Toggle Left Navigation Sidebar Rail' },
        { keys: ['Ctrl', 'S'], desc: 'Flush & save configuration snapshot now' },
        { keys: ['Ctrl', '`'], desc: 'Toggle Global Terminal Drawer' },
        { keys: ['?'], desc: 'Open this keyboard shortcuts cheat-sheet' },
        { keys: ['Esc'], desc: 'Close any open modal or dialog' },
      ],
    },
  ];

  return (
    <ModalDialog
      open={open}
      onClose={onClose}
      title="⌨️ Keyboard Shortcuts Cheat-Sheet"
      size="narrow"
      closedBy="any"
    >
      <div className="hk-shortcuts-dialog">
        {shortcutGroups.map((grp) => (
          <div key={grp.title} className="hk-shortcuts-group">
            <h4 className="hk-shortcuts-title">{grp.title}</h4>
            <div className="hk-shortcuts-list">
              {grp.items.map((it) => (
                <div key={it.desc} className="hk-shortcut-row">
                  <span className="hk-shortcut-desc">{it.desc}</span>
                  <div className="hk-shortcut-keys">
                    {it.keys.map((k) => (
                      <kbd key={k} className="hk-kbd">
                        {k}
                      </kbd>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </div>
        ))}

        <div style={{ marginTop: 14, textAlign: 'right' }}>
          <button type="button" className="hk-btn primary" onClick={onClose}>
            Got it
          </button>
        </div>
      </div>
    </ModalDialog>
  );
};
