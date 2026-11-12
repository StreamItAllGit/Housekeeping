// @module: Housekeeping.Web/components/TerminalPaneToolbar
// @purpose: Extracted header bar (S/M/L presets, verbosity, split modes) and per-pane filter/wrap/auto-scroll toolbar for DualPaneTerminal.
// @depends-on: react, ../types/models
// @exports: DualTerminalHeaderBar, SinglePaneToolbar
import React from 'react';
import type {
  RemoteNodeConfig,
  TerminalSizePreset,
  TerminalSplitMode,
  VerbosityLevel,
} from '../types/models';

const WRAP_HINT =
  'Wrap long CLI and encoder lines inside the terminal viewport instead of scrolling horizontally.\nDisables fixed-row virtualization while active so multi-line heights stay exact.';
const AUTOSCROLL_HINT =
  'Keep the terminal viewport pinned to the newest log line automatically.\nScrolling up more than 45px pauses auto-scroll until you scroll back to the bottom.';

interface DualTerminalHeaderBarProps {
  bufferedCount: number;
  verbosity: VerbosityLevel;
  onChangeVerbosity: (next: VerbosityLevel) => void;
  splitMode: TerminalSplitMode;
  onChangeSplitMode: (mode: TerminalSplitMode) => void;
  sizePreset: TerminalSizePreset;
  onSelectPreset: (preset: 'small' | 'medium' | 'large') => void;
}

export const DualTerminalHeaderBar: React.FC<DualTerminalHeaderBarProps> = ({
  bufferedCount,
  verbosity,
  onChangeVerbosity,
  splitMode,
  onChangeSplitMode,
  sizePreset,
  onSelectPreset,
}) => (
  <div className="hk-row" style={{ justifyContent: 'space-between', marginBottom: 8 }}>
    <div className="hk-row">
      <span style={{ fontWeight: 700, color: 'var(--accent)' }}>
        🖥 Live Pipeline &amp; Fleet Terminal
      </span>
      <span className="hk-pill nowrap">{bufferedCount} lines buffered</span>
    </div>

    <div className="hk-row">
      <div className="hk-row" style={{ gap: 3 }} role="group" aria-label="Terminal height presets">
        {(['small', 'medium', 'large'] as const).map((preset) => {
          const short = preset === 'small' ? 'S' : preset === 'medium' ? 'M' : 'L';
          const pct = preset === 'small' ? '25%' : preset === 'medium' ? '40%' : '60%';
          return (
            <button
              key={preset}
              type="button"
              className={`hk-btn sm ${sizePreset === preset ? 'primary' : ''}`}
              title={`Set terminal height to ${preset} (${pct} of viewport)`}
              onClick={() => onSelectPreset(preset)}
            >
              {short}
            </button>
          );
        })}
      </div>

      <label className="hk-field-label">Verbosity:</label>
      <select
        className="hk-select"
        style={{ width: 'auto' }}
        value={verbosity}
        onChange={(e) => onChangeVerbosity(e.target.value as VerbosityLevel)}
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
          onClick={() => onChangeSplitMode('Single')}
        >
          ▣ Single
        </button>
        <button
          type="button"
          className={`hk-btn sm hk-desktop-only ${splitMode === 'SplitVertical' ? 'primary' : ''}`}
          onClick={() => onChangeSplitMode('SplitVertical')}
        >
          ◫ Split Side
        </button>
        <button
          type="button"
          className={`hk-btn sm ${splitMode === 'SplitHorizontal' ? 'primary' : ''}`}
          onClick={() => onChangeSplitMode('SplitHorizontal')}
        >
          ⊟ Split Stack
        </button>
      </div>
    </div>
  </div>
);

interface SinglePaneToolbarProps {
  paneIndex: 1 | 2;
  nodeId: string;
  machineName: string;
  remoteNodes: RemoteNodeConfig[];
  remoteStatus: string;
  searchFilter: string;
  wordWrap: boolean;
  autoScroll: boolean;
  onChangeNodeId: (next: string) => void;
  onChangeSearchFilter: (next: string) => void;
  onChangeWordWrap: (next: boolean) => void;
  onChangeAutoScroll: (next: boolean) => void;
  onClear: () => void;
}

export const SinglePaneToolbar: React.FC<SinglePaneToolbarProps> = ({
  paneIndex,
  nodeId,
  machineName,
  remoteNodes,
  remoteStatus,
  searchFilter,
  wordWrap,
  autoScroll,
  onChangeNodeId,
  onChangeSearchFilter,
  onChangeWordWrap,
  onChangeAutoScroll,
  onClear,
}) => (
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
        <option value="local">🖥 Local ({machineName || 'This PC'})</option>
        {remoteNodes.map((n) => (
          <option key={n.Id} value={n.Id}>
            🌐 {n.Name} ({n.HostOrIp || n.RemoteAppPath})
          </option>
        ))}
      </select>
      {remoteStatus && <span style={{ color: '#94a3b8', fontSize: 11.5 }}>{remoteStatus}</span>}
    </div>

    <div className="hk-row" style={{ flex: '1 1 auto', justifyContent: 'flex-end' }}>
      <input
        type="search"
        className="hk-input hk-terminal-filter-input"
        style={{ width: 170, minHeight: 26, padding: '2px 8px', fontSize: 12 }}
        placeholder="Filter logs..."
        value={searchFilter}
        onChange={(e) => onChangeSearchFilter(e.target.value)}
        aria-label="Filter terminal output"
      />
      <label
        className="hk-checkbox-label hk-has-hint"
        style={{ fontSize: 11.5, color: '#cbd5e1' }}
        title={WRAP_HINT}
      >
        <input
          type="checkbox"
          className="hk-has-hint"
          title={WRAP_HINT}
          checked={wordWrap}
          onChange={(e) => onChangeWordWrap(e.target.checked)}
        />
        Wrap
      </label>
      <label
        className="hk-checkbox-label hk-has-hint"
        style={{ fontSize: 11.5, color: '#cbd5e1' }}
        title={AUTOSCROLL_HINT}
      >
        <input
          type="checkbox"
          className="hk-has-hint"
          title={AUTOSCROLL_HINT}
          checked={autoScroll}
          onChange={(e) => onChangeAutoScroll(e.target.checked)}
        />
        Auto-Scroll
      </label>
      {(!nodeId || nodeId === 'local') && (
        <button
          type="button"
          className="hk-btn sm"
          style={{ minHeight: 25 }}
          onClick={onClear}
        >
          🧹 Clear
        </button>
      )}
    </div>
  </div>
);
