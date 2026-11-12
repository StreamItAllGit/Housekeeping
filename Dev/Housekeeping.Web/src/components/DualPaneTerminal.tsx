// @module: Housekeeping.Web/components/DualPaneTerminal
// @purpose: Resizable single/dual-pane real-time log terminal with ring-buffer auto-scroll, word-wrap, and S/M/L presets.
// @depends-on: react, ../context/AppStateContext, ../hooks/useRemoteTerminalTail, ../hooks/useTerminalAutoScroll, ./TerminalCliPromptBar, ./TerminalPaneToolbar, ./TerminalResizeHandle
// @exports: DualPaneTerminal
import React, { useEffect, useMemo, useRef, useState } from 'react';
import { useAppState } from '../context/AppStateContext';
import { useRemoteTerminalTail } from '../hooks/useRemoteTerminalTail';
import { useTerminalAutoScroll } from '../hooks/useTerminalAutoScroll';
import type { TerminalLogEntry, TerminalSplitMode, VerbosityLevel } from '../types/models';
import { TerminalCliPromptBar } from './TerminalCliPromptBar';
import { DualTerminalHeaderBar, SinglePaneToolbar } from './TerminalPaneToolbar';
import { TerminalResizeHandle } from './TerminalResizeHandle';

const ROW_HEIGHT = 20;

function matchesVerbosity(tag: string, verbosity: VerbosityLevel, customTags: string[]): boolean {
  const upper = (tag || 'INFO').toUpperCase();
  if (upper === 'CLI') return true;
  switch (verbosity) {
    case 'AlertsOnly':
      return upper === 'ERROR' || upper === 'WARN';
    case 'Quiet':
      return upper === 'ERROR' || upper === 'WARN' || upper === 'SUCCESS' || upper === 'HEADER';
    case 'ProgressAndStatus':
      return ['ERROR', 'WARN', 'SUCCESS', 'HEADER', 'WORK', 'NET', 'DRY', 'CMD'].includes(upper);
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
  explicitHeightPx?: number;
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
  explicitHeightPx,
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
    uiLayout,
    setTerminalWordWrap,
  } = useAppState();

  const [searchFilter, setSearchFilter] = useState('');
  const [autoScroll, setAutoScroll] = useState(true);
  const [scrollTop, setScrollTop] = useState(0);
  const [viewportHeight, setViewportHeight] = useState(300);

  const wordWrap = Boolean(uiLayout.TerminalWordWrap);
  const viewportRef = useRef<HTMLDivElement | null>(null);
  const contentRef = useRef<HTMLDivElement | null>(null);
  const bottomSentinelRef = useRef<HTMLDivElement | null>(null);
  const { remoteLines, remoteStatus } = useRemoteTerminalTail(nodeId);

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
    return localEntries.filter(
      (e) =>
        matchesVerbosity(e.Tag, verbosity, customTags) &&
        (!q || e.FormattedLine.toLowerCase().includes(q))
    );
  }, [customTags, localEntries, nodeId, remoteLines, searchFilter, verbosity]);

  const lastEntry = filteredEntries[filteredEntries.length - 1];
  const lastEntryKey = lastEntry ? `${lastEntry.Id}:${lastEntry.FormattedLine.length}` : 'empty';

  const { handleViewportScroll } = useTerminalAutoScroll({
    viewportRef,
    contentRef,
    bottomSentinelRef,
    autoScroll,
    setAutoScroll,
    entryCount: filteredEntries.length,
    lastEntryKey,
    wordWrap,
  });

  useEffect(() => {
    const el = viewportRef.current;
    if (!el) return;
    const update = () => setViewportHeight(el.clientHeight || 300);
    update();
    const ro = new ResizeObserver(update);
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  const useVirtual = !wordWrap && filteredEntries.length > 120;
  const totalHeight = filteredEntries.length * ROW_HEIGHT;
  const startIdx = useVirtual ? Math.max(0, Math.floor(scrollTop / ROW_HEIGHT) - 10) : 0;
  const visibleCount = useVirtual
    ? Math.ceil(viewportHeight / ROW_HEIGHT) + 24
    : filteredEntries.length;
  const visibleSlice = filteredEntries.slice(startIdx, Math.min(filteredEntries.length, startIdx + visibleCount));

  return (
    <div
      className={`hk-terminal-surface ${sizingVariant}`}
      style={explicitHeightPx ? { blockSize: `${explicitHeightPx}px` } : undefined}
    >
      <SinglePaneToolbar
        paneIndex={paneIndex}
        nodeId={nodeId}
        machineName={authStatus?.machineName || 'This PC'}
        remoteNodes={config?.RemoteNodes || []}
        remoteStatus={remoteStatus}
        searchFilter={searchFilter}
        wordWrap={wordWrap}
        autoScroll={autoScroll}
        onChangeNodeId={onChangeNodeId}
        onChangeSearchFilter={setSearchFilter}
        onChangeWordWrap={setTerminalWordWrap}
        onChangeAutoScroll={setAutoScroll}
        onClear={() => void clearTerminalEntries()}
      />

      <div
        ref={viewportRef}
        className="hk-terminal-viewport"
        onScroll={(e) => {
          const t = e.currentTarget;
          setScrollTop(t.scrollTop);
          handleViewportScroll(t.scrollTop, t.scrollHeight, t.clientHeight);
        }}
      >
        {filteredEntries.length === 0 ? (
          <div style={{ color: '#64748b', padding: '8px 2px' }}>
            Housekeeping live stream ready — output from HandBrakeCLI, 7z, rclone, DAT verifier, and
            interactive CLI appears here in real time.
          </div>
        ) : (
          <div
            ref={contentRef}
            style={useVirtual ? { height: totalHeight, position: 'relative' } : undefined}
          >
            <div style={useVirtual ? { transform: `translateY(${startIdx * ROW_HEIGHT}px)` } : undefined}>
              {visibleSlice.map((entry) => (
                <div key={entry.Id} className={`hk-terminal-line ${wordWrap ? 'wrap' : ''}`}>
                  {entry.TimestampText && (
                    <span style={{ color: '#64748b', flexShrink: 0 }}>[{entry.TimestampText}]</span>
                  )}
                  <span className={`hk-log-tag ${entry.Tag || 'INFO'}`}>{entry.Tag || 'INFO'}</span>
                  <span className="hk-terminal-msg">{entry.Message}</span>
                </div>
              ))}
            </div>
            <div ref={bottomSentinelRef} aria-hidden="true" style={{ height: 1 }} />
          </div>
        )}
      </div>

      {showCli && (
        <TerminalCliPromptBar
          isLocalPane={!nodeId || nodeId === 'local'}
          rawBlocked={Boolean(authStatus?.rawShellBlockedForCaller)}
          cliWorkingDir={cliWorkingDir}
          cliRunning={cliRunning}
          cliHistory={cliHistory}
          onRunCommand={runCliCommand}
          onCancelCommand={cancelCliCommand}
          onSwitchToLocal={() => onChangeNodeId('local')}
        />
      )}
    </div>
  );
};

interface DualPaneTerminalProps {
  compactDrawerMode?: boolean;
}

export const DualPaneTerminal: React.FC<DualPaneTerminalProps> = ({
  compactDrawerMode = false,
}) => {
  const {
    config,
    updateConfig,
    terminalEntries,
    uiLayout,
    setTerminalHeight,
    applyTerminalPreset,
  } = useAppState();

  const splitMode: TerminalSplitMode = compactDrawerMode ? 'Single' : config?.SplitMode || 'Single';
  const pane1Node = config?.ActiveNodeIdPane1 || 'local';
  const pane2Node = config?.ActiveNodeIdPane2 || 'local';
  const verbosity = config?.TerminalVerbosity || 'Everything';
  const customTags = config?.CustomVerbosity?.TerminalTags || [];

  const paneSizingVariant: 'dashboard-single' | 'dashboard-stacked' | 'drawer-pane' =
    compactDrawerMode
      ? 'drawer-pane'
      : splitMode === 'SplitHorizontal'
        ? 'dashboard-stacked'
        : 'dashboard-single';

  const explicitPaneHeight = compactDrawerMode
    ? undefined
    : splitMode === 'SplitHorizontal'
      ? Math.max(150, Math.round(uiLayout.TerminalHeightPx / 2))
      : uiLayout.TerminalHeightPx;

  const pane1IsLocal = !pane1Node || pane1Node === 'local';
  const pane2IsLocal = !pane2Node || pane2Node === 'local';
  const showCliOnPane1 = splitMode === 'Single' || pane1IsLocal || !pane2IsLocal;
  const showCliOnPane2 = splitMode !== 'Single' && !pane1IsLocal && pane2IsLocal;

  return (
    <div
      style={
        compactDrawerMode
          ? { display: 'flex', flexDirection: 'column', flex: '1 1 0', minBlockSize: 0 }
          : undefined
      }
    >
      {!compactDrawerMode && (
        <DualTerminalHeaderBar
          bufferedCount={terminalEntries.length}
          verbosity={verbosity}
          onChangeVerbosity={(next) => updateConfig((d) => (d.TerminalVerbosity = next))}
          splitMode={splitMode}
          onChangeSplitMode={(mode) => updateConfig((d) => (d.SplitMode = mode))}
          sizePreset={uiLayout.TerminalSizePreset}
          onSelectPreset={applyTerminalPreset}
        />
      )}

      <div
        className={
          compactDrawerMode
            ? undefined
            : `hk-terminal-grid ${splitMode === 'SplitVertical' ? 'split-side' : ''}`
        }
        style={
          compactDrawerMode
            ? { display: 'flex', flexDirection: 'column', flex: '1 1 0', minBlockSize: 0 }
            : undefined
        }
      >
        <SingleTerminalPane
          paneIndex={1}
          nodeId={pane1Node}
          onChangeNodeId={(next) => updateConfig((d) => (d.ActiveNodeIdPane1 = next))}
          localEntries={terminalEntries}
          verbosity={verbosity}
          customTags={customTags}
          sizingVariant={paneSizingVariant}
          explicitHeightPx={explicitPaneHeight}
          showCli={showCliOnPane1}
        />

        {splitMode !== 'Single' && (
          <SingleTerminalPane
            paneIndex={2}
            nodeId={pane2Node}
            onChangeNodeId={(next) => updateConfig((d) => (d.ActiveNodeIdPane2 = next))}
            localEntries={terminalEntries}
            verbosity={verbosity}
            customTags={customTags}
            sizingVariant={paneSizingVariant}
            explicitHeightPx={explicitPaneHeight}
            showCli={showCliOnPane2}
          />
        )}
      </div>

      {!compactDrawerMode && (
        <TerminalResizeHandle
          heightPx={uiLayout.TerminalHeightPx}
          direction="grow-down"
          onChangeHeight={(h) => setTerminalHeight(h, 'custom')}
          onResetMedium={() => applyTerminalPreset('medium')}
        />
      )}
    </div>
  );
};
