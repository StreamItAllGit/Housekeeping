// @module: Housekeeping.Web/components/TerminalCliPromptBar
// @purpose: Interactive local PowerShell/CLI prompt bar with command history navigation and remote-node switch banner.
// @depends-on: react
// @exports: TerminalCliPromptBar
import React, { useMemo, useState } from 'react';

interface TerminalCliPromptBarProps {
  isLocalPane: boolean;
  rawBlocked: boolean;
  cliWorkingDir: string;
  cliRunning: boolean;
  cliHistory: string[];
  onRunCommand: (cmd: string) => Promise<void>;
  onCancelCommand: () => Promise<void>;
  onSwitchToLocal: () => void;
}

export const TerminalCliPromptBar: React.FC<TerminalCliPromptBarProps> = ({
  isLocalPane,
  rawBlocked,
  cliWorkingDir,
  cliRunning,
  cliHistory,
  onRunCommand,
  onCancelCommand,
  onSwitchToLocal,
}) => {
  const [cmdInput, setCmdInput] = useState('');
  const [histCursor, setHistCursor] = useState(-1);

  const compactCliPrompt = useMemo(() => {
    if (!cliWorkingDir) return 'PS>';
    const trimmed = cliWorkingDir.replace(/[\\/]+$/, '');
    const parts = trimmed.split(/[\\/]/).filter(Boolean);
    if (parts.length <= 1) return `${trimmed}\\>`;
    const leaf = parts[parts.length - 1];
    return `…\\${leaf.length > 16 ? `${leaf.slice(0, 15)}…` : leaf}>`;
  }, [cliWorkingDir]);

  const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowUp') {
      e.preventDefault();
      if (cliHistory.length === 0) return;
      const nextIdx = Math.min(cliHistory.length - 1, histCursor + 1);
      setHistCursor(nextIdx);
      setCmdInput(cliHistory[nextIdx] || '');
    } else if (e.key === 'ArrowDown') {
      e.preventDefault();
      if (histCursor <= 0) {
        setHistCursor(-1);
        setCmdInput('');
      } else {
        const nextIdx = histCursor - 1;
        setHistCursor(nextIdx);
        setCmdInput(cliHistory[nextIdx] || '');
      }
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!cmdInput.trim() || cliRunning) return;
    const toRun = cmdInput;
    setCmdInput('');
    setHistCursor(-1);
    await onRunCommand(toRun);
  };

  if (!isLocalPane) {
    return (
      <div className="hk-terminal-cli" style={{ justifyContent: 'space-between' }}>
        <span style={{ color: '#94a3b8', fontSize: 12 }}>
          🌐 Viewing remote node log stream (read-only tail) — switch pane to Local for interactive CLI commands.
        </span>
        <button type="button" className="hk-btn sm primary" onClick={onSwitchToLocal}>
          🖥 Switch to Local CLI
        </button>
      </div>
    );
  }

  return (
    <form className="hk-terminal-cli" onSubmit={(e) => void handleSubmit(e)}>
      <span className="hk-desktop-only" style={{ color: '#34d399', fontSize: 12, whiteSpace: 'nowrap' }}>
        {cliWorkingDir ? `${cliWorkingDir}>` : 'PS>'}
      </span>
      <span
        className="hk-mobile-only"
        style={{ color: '#34d399', fontSize: 11.5, whiteSpace: 'nowrap', flexShrink: 0 }}
        title={cliWorkingDir || 'PowerShell'}
      >
        {compactCliPrompt}
      </span>
      <input
        type="text"
        value={cmdInput}
        disabled={rawBlocked || cliRunning}
        onChange={(e) => setCmdInput(e.target.value)}
        onKeyDown={handleKeyDown}
        placeholder={
          rawBlocked
            ? 'Localhost only (security policy)'
            : 'Run CLI command (dir, cd, 7z, rclone)...'
        }
        aria-label="Interactive CLI command prompt"
      />
      {cliRunning ? (
        <button type="button" className="hk-btn sm danger" onClick={() => void onCancelCommand()}>
          ⏹ Stop Cmd
        </button>
      ) : (
        <button type="submit" className="hk-btn sm primary" disabled={rawBlocked || !cmdInput.trim()}>
          Run ↵
        </button>
      )}
    </form>
  );
};
