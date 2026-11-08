import React, { useEffect, useMemo, useRef, useState } from 'react';
import { useAppState } from '../context/AppStateContext';
import { apiClient } from '../services/apiClient';
import type { TerminalLogEntry, TerminalSplitMode, VerbosityLevel } from '../types/models';

const ROW_HEIGHT = 20;

function matchesVerbosity(
  tag: string,
  verbosity: VerbosityLevel,
  customTags: string[]
): boolean {
  const upper = (tag || 'INFO').toUpperCase();
  if (upper === 'CLI') {
    return true;
  }
  switch (verbosity) {
    case 'AlertsOnly':
      return upper === 'ERROR' || upper === 'WARN';
    case 'Quiet':
      return upper === 'ERROR' || upper === 'WARN' || upper === 'SUCCESS' || upper === 'HEADER';
    case 'ProgressAndStatus':
      return (
        upper === 'ERROR' ||
        upper === 'WARN' ||
        upper === 'SUCCESS' ||
        upper === 'HEADER' ||
        upper === 'WORK' ||
        upper === 'NET' ||
        upper === 'DRY' ||
        upper === 'CMD'
      );
    case 'Custom':
      return customTags.some((t) => t.toUpperCase() === upper);
    case 'Everything':
    default:
      return true;
  }
}

interface SingleTerminalPaneProps {
  paneIndex: 1 | 2;
  nodeId: string;
  onChangeNodeId: (next: string) => void;
  localEntries: TerminalLogEntry[];
  verbosity: VerbosityLevel;
  customTags: string[];
  sizingVariant: 'dashboard-single' | 'dashboard-stacked' | 'drawer-pane';
  showCli?: boolean;
}

const SingleTerminalPane: React.FC<SingleTerminalPaneProps> = ({
  paneIndex,
  nodeId,
  onChangeNodeId,
  localEntries,
  verbosity,
  customTags,
  sizingVariant,
  showCli = true,
}) => {
  const {
    config,
    authStatus,
    clearTerminalEntries,
    cliWorkingDir,
    cliRunning,
    cliHistory,
    runCliCommand,
    cancelCliCommand,
  } = useAppState();

  const [searchFilter, setSearchFilter] = useState('');
  const [wordWrap, setWordWrap] = useState<boolean>(
    () => typeof window !== 'undefined' && window.innerWidth <= 768
  );
  const [autoScroll, setAutoScroll] = useState(true);
  const [scrollTop, setScrollTop] = useState(0);
  const [viewportHeight, setViewportHeight] = useState(300);

  const [remoteLines, setRemoteLines] = useState<string[]>([]);
  const [remoteStatus, setRemoteStatus] = useState<string>('');

  const [cmdInput, setCmdInput] = useState('');
  const [histCursor, setHistCursor] = useState(-1);

  const viewportRef = useRef<HTMLDivElement | null>(null);

  // Poll remote node tail sequentially (never overlapping) so offline hosts never starve browser sockets
  useEffect(() => {
    if (!nodeId || nodeId === 'local') {
      setRemoteLines([]);
      setRemoteStatus('');
      return;
    }

    let cancelled = false;
    let nextTimer: number | null = null;
    let activeAbort: AbortController | null = null;

    const scheduleNext = (delayMs: number) => {
      if (!cancelled) {
        nextTimer = window.setTimeout(() => void pollOnce(), delayMs);
      }
    };

    const pollOnce = async () => {
      if (cancelled) return;
      const ac = new AbortController();
      activeAbort = ac;
      const abortTimer = window.setTimeout(() => ac.abort(), 4500);
      let isOnline = false;
      try {
        const res = await apiClient.getRemoteTerminalTail(nodeId, 350, ac.signal);
        if (!cancelled) {
          setRemoteLines(res.lines || []);
          setRemoteStatus(res.status || '');
          isOnline = res.online !== false && !(res.status || '').startsWith('Offline');
        }
      } catch (err) {
        if (!cancelled) {
          setRemoteStatus(
            ac.signal.aborted
              ? 'Remote node poll timed out'
              : err instanceof Error
                ? err.message
                : 'Remote node unreachable'
          );
        }
      } finally {
        window.clearTimeout(abortTimer);
        if (activeAbort === ac) activeAbort = null;
        scheduleNext(isOnline ? 3000 : 10000);
      }
    };

    void pollOnce();
    return () => {
      cancelled = true;
      if (nextTimer !== null) window.clearTimeout(nextTimer);
      activeAbort?.abort();
    };
  }, [nodeId]);

  const filteredEntries = useMemo(() => {
    if (nodeId && nodeId !== 'local') {
      const q = searchFilter.trim().toLowerCase();
      return remoteLines
        .filter((l) => !q || l.toLowerCase().includes(q))
        .map((line, i) => ({
          Id: i + 1,
          TimestampUtc: '',
          TimestampText: '',
          Tag: 'REMOTE',
          Message: line,
          FormattedLine: line,
        }));
    }

    const q = searchFilter.trim().toLowerCase();
    return localEntries.filter((e) => {
      if (!matchesVerbosity(e.Tag, verbosity, customTags)) return false;
      if (q && !e.FormattedLine.toLowerCase().includes(q)) return false;
      return true;
    });
  }, [customTags, localEntries, nodeId, remoteLines, searchFilter, verbosity]);

  // Measure viewport height
  useEffect(() => {
    const el = viewportRef.current;
    if (!el) return;
    const update = () => setViewportHeight(el.clientHeight || 300);
    update();
    const ro = new ResizeObserver(update);
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  // Auto-scroll to bottom on new lines when locked
  useEffect(() => {
    if (!autoScroll) return;
    const el = viewportRef.current;
    if (el) {
      el.scrollTop = el.scrollHeight;
    }
  }, [autoScroll, filteredEntries.length]);

  // Virtualize when wordWrap is off so 5,000+ log lines render at 60fps
  const useVirtual = !wordWrap && filteredEntries.length > 120;
  const totalHeight = filteredEntries.length * ROW_HEIGHT;
  const startIdx = useVirtual ? Math.max(0, Math.floor(scrollTop / ROW_HEIGHT) - 10) : 0;
  const visibleCount = useVirtual ? Math.ceil(viewportHeight / ROW_HEIGHT) + 24 : filteredEntries.length;
  const endIdx = Math.min(filteredEntries.length, startIdx + visibleCount);
  const visibleSlice = filteredEntries.slice(startIdx, endIdx);

  const handleCliKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
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

  const handleCliSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!cmdInput.trim() || cliRunning) return;
    const toRun = cmdInput;
    setCmdInput('');
    setHistCursor(-1);
    await runCliCommand(toRun);
  };

  const remoteNodes = config?.RemoteNodes || [];
  const rawBlocked = Boolean(authStatus?.rawShellBlockedForCaller);

  const isLocalPane = !nodeId || nodeId === 'local';
  const compactCliPrompt = useMemo(() => {
    if (!cliWorkingDir) return 'PS>';
    const trimmed = cliWorkingDir.replace(/[\\/]+$/, '');
    const parts = trimmed.split(/[\\/]/).filter(Boolean);
    if (parts.length <= 1) return `${trimmed}\\>`;
    const leaf = parts[parts.length - 1];
    return `…\\${leaf.length > 16 ? `${leaf.slice(0, 15)}…` : leaf}>`;
  }, [cliWorkingDir]);

  return (
    <div className={`hk-terminal-surface ${sizingVariant}`}>
      <div className="hk-terminal-toolbar">
        <div className="hk-row">
          <strong style={{ color: '#38bdf8' }}>Pane {paneIndex}</strong>
          <select
            className="hk-select"
            style={{ width: 'auto', minHeight: 26, padding: '2px 8px', fontSize: 12 }}
            value={nodeId || 'local'}
            onChange={(e) => onChangeNodeId(e.target.value)}
            aria-label={`Terminal Pane ${paneIndex} Target Node`}
          >
            <option value="local">🖥 Local ({authStatus?.machineName || 'This PC'})</option>
            {remoteNodes.map((n) => (
              <option key={n.Id} value={n.Id}>
                🌐 {n.Name} ({n.HostOrIp || n.RemoteAppPath})
              </option>
            ))}
          </select>
          {remoteStatus && (
            <span style={{ color: '#94a3b8', fontSize: 11.5 }}>{remoteStatus}</span>
          )}
        </div>

        <div className="hk-row" style={{ flex: '1 1 auto', justifyContent: 'flex-end' }}>
          <input
            type="search"
            className="hk-input hk-terminal-filter-input"
            style={{ width: 170, minHeight: 26, padding: '2px 8px', fontSize: 12 }}
            placeholder="Filter logs..."
            value={searchFilter}
            onChange={(e) => setSearchFilter(e.target.value)}
            aria-label="Filter terminal output"
          />
          <label className="hk-checkbox-label" style={{ fontSize: 11.5, color: '#cbd5e1' }}>
            <input
              type="checkbox"
              checked={wordWrap}
              onChange={(e) => setWordWrap(e.target.checked)}
            />
            Wrap
          </label>
          <label className="hk-checkbox-label" style={{ fontSize: 11.5, color: '#cbd5e1' }}>
            <input
              type="checkbox"
              checked={autoScroll}
              onChange={(e) => setAutoScroll(e.target.checked)}
            />
            Auto-Scroll
          </label>
          {(!nodeId || nodeId === 'local') && (
            <button
              type="button"
              className="hk-btn sm"
              style={{ minHeight: 25 }}
              onClick={() => void clearTerminalEntries()}
            >
              🧹 Clear
            </button>
          )}
        </div>
      </div>

      <div
        ref={viewportRef}
        className="hk-terminal-viewport"
        onScroll={(e) => setScrollTop(e.currentTarget.scrollTop)}
      >
        {filteredEntries.length === 0 ? (
          <div style={{ color: '#64748b', padding: '8px 2px' }}>
            Housekeeping live stream ready — output from HandBrakeCLI, 7z, rclone, DAT verifier, and interactive CLI appears here in real time.
          </div>
        ) : (
          <div
            style={
              useVirtual
                ? { height: totalHeight, position: 'relative' }
                : undefined
            }
          >
            <div
              style={
                useVirtual
                  ? { transform: `translateY(${startIdx * ROW_HEIGHT}px)` }
                  : undefined
              }
            >
              {visibleSlice.map((entry) => (
                <div
                  key={entry.Id}
                  className={`hk-terminal-line ${wordWrap ? 'wrap' : ''}`}
                >
                  {entry.TimestampText && (
                    <span style={{ color: '#64748b', flexShrink: 0 }}>
                      [{entry.TimestampText}]
                    </span>
                  )}
                  <span className={`hk-log-tag ${entry.Tag || 'INFO'}`}>
                    {entry.Tag || 'INFO'}
                  </span>
                  <span>{entry.Message}</span>
                </div>
              ))}
            </div>
          </div>
        )}
      </div>

      {showCli &&
        (isLocalPane ? (
          <form className="hk-terminal-cli" onSubmit={(e) => void handleCliSubmit(e)}>
            <span
              className="hk-desktop-only"
              style={{ color: '#34d399', fontSize: 12, whiteSpace: 'nowrap' }}
            >
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
              onKeyDown={handleCliKeyDown}
              placeholder={
                rawBlocked
                  ? 'Localhost only (security policy)'
                  : 'Run CLI command (dir, cd, 7z, rclone)...'
              }
              aria-label="Interactive CLI command prompt"
            />
            {cliRunning ? (
              <button
                type="button"
                className="hk-btn sm danger"
                onClick={() => void cancelCliCommand()}
              >
                ⏹ Stop Cmd
              </button>
            ) : (
              <button
                type="submit"
                className="hk-btn sm primary"
                disabled={rawBlocked || !cmdInput.trim()}
              >
                Run ↵
              </button>
            )}
          </form>
        ) : (
          <div className="hk-terminal-cli" style={{ justifyContent: 'space-between' }}>
            <span style={{ color: '#94a3b8', fontSize: 12 }}>
              🌐 Viewing remote node log stream (read-only tail) — switch pane to Local for interactive CLI commands.
            </span>
            <button
              type="button"
              className="hk-btn sm primary"
              onClick={() => onChangeNodeId('local')}
            >
              🖥 Switch to Local CLI
            </button>
          </div>
        ))}
    </div>
  );
};

interface DualPaneTerminalProps {
  compactDrawerMode?: boolean;
}

export const DualPaneTerminal: React.FC<DualPaneTerminalProps> = ({
  compactDrawerMode = false,
}) => {
  const { config, updateConfig, terminalEntries } = useAppState();

  const splitMode: TerminalSplitMode = compactDrawerMode
    ? 'Single'
    : config?.SplitMode || 'Single';
  const pane1Node = config?.ActiveNodeIdPane1 || 'local';
  const pane2Node = config?.ActiveNodeIdPane2 || 'local';
  const verbosity = config?.TerminalVerbosity || 'Everything';
  const customTags = config?.CustomVerbosity?.TerminalTags || [];

  const setSplitMode = (mode: TerminalSplitMode) => {
    updateConfig((d) => {
      d.SplitMode = mode;
    });
  };

  const effectiveSplitMode: TerminalSplitMode = compactDrawerMode ? 'Single' : splitMode;
  const paneSizingVariant: 'dashboard-single' | 'dashboard-stacked' | 'drawer-pane' =
    compactDrawerMode
      ? 'drawer-pane'
      : effectiveSplitMode === 'SplitHorizontal'
        ? 'dashboard-stacked'
        : 'dashboard-single';

  const pane1IsLocal = !pane1Node || pane1Node === 'local';
  const pane2IsLocal = !pane2Node || pane2Node === 'local';
  const showCliOnPane1 =
    effectiveSplitMode === 'Single' || pane1IsLocal || !pane2IsLocal;
  const showCliOnPane2 =
    effectiveSplitMode !== 'Single' && !pane1IsLocal && pane2IsLocal;

  return (
    <div
      style={
        compactDrawerMode
          ? { display: 'flex', flexDirection: 'column', flex: '1 1 0', minBlockSize: 0 }
          : undefined
      }
    >
      {!compactDrawerMode && (
        <div className="hk-row" style={{ justifyContent: 'space-between', marginBottom: 8 }}>
          <div className="hk-row">
            <span style={{ fontWeight: 700, color: 'var(--accent)' }}>
              🖥 Live Pipeline &amp; Fleet Terminal
            </span>
            <span className="hk-pill">{terminalEntries.length} lines buffered</span>
          </div>

          <div className="hk-row">
            <label className="hk-field-label">Verbosity:</label>
            <select
              className="hk-select"
              style={{ width: 'auto' }}
              value={verbosity}
              onChange={(e) =>
                updateConfig((d) => {
                  d.TerminalVerbosity = e.target.value as VerbosityLevel;
                })
              }
              aria-label="Terminal verbosity"
            >
              <option value="Everything">Everything</option>
              <option value="ProgressAndStatus">Progress &amp; Status</option>
              <option value="Quiet">Quiet (Headers + Results)</option>
              <option value="AlertsOnly">Alerts Only (Warn/Error)</option>
              <option value="Custom">Custom Tags</option>
            </select>

            <div className="hk-row" style={{ gap: 4 }}>
              <button
                type="button"
                className={`hk-btn sm ${splitMode === 'Single' ? 'primary' : ''}`}
                onClick={() => setSplitMode('Single')}
              >
                ▣ Single
              </button>
              <button
                type="button"
                className={`hk-btn sm hk-desktop-only ${splitMode === 'SplitVertical' ? 'primary' : ''}`}
                onClick={() => setSplitMode('SplitVertical')}
              >
                ◫ Split Side
              </button>
              <button
                type="button"
                className={`hk-btn sm ${splitMode === 'SplitHorizontal' ? 'primary' : ''}`}
                onClick={() => setSplitMode('SplitHorizontal')}
              >
                ⊟ Split Stack
              </button>
            </div>
          </div>
        </div>
      )}

      <div
        className={
          compactDrawerMode
            ? undefined
            : `hk-terminal-grid ${effectiveSplitMode === 'SplitVertical' ? 'split-side' : ''}`
        }
        style={
          compactDrawerMode
            ? {
                display: 'flex',
                flexDirection: 'column',
                flex: '1 1 0',
                minBlockSize: 0,
              }
            : undefined
        }
      >
        <SingleTerminalPane
          paneIndex={1}
          nodeId={pane1Node}
          onChangeNodeId={(next) =>
            updateConfig((d) => {
              d.ActiveNodeIdPane1 = next;
            })
          }
          localEntries={terminalEntries}
          verbosity={verbosity}
          customTags={customTags}
          sizingVariant={paneSizingVariant}
          showCli={showCliOnPane1}
        />

        {effectiveSplitMode !== 'Single' && (
          <SingleTerminalPane
            paneIndex={2}
            nodeId={pane2Node}
            onChangeNodeId={(next) =>
              updateConfig((d) => {
                d.ActiveNodeIdPane2 = next;
              })
            }
            localEntries={terminalEntries}
            verbosity={verbosity}
            customTags={customTags}
            sizingVariant={paneSizingVariant}
            showCli={showCliOnPane2}
          />
        )}
      </div>
    </div>
  );
};
