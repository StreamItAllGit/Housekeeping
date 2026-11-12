// @module: Housekeeping.Web/components/TerminalResizeHandle
// @purpose: Accessible pointer-drag and keyboard-resizable separator bar for the docked Dashboard and Global Terminal panes.
// @depends-on: react, ../hooks/useUiLayoutPersistence
// @exports: TerminalResizeHandle
import React, { useRef } from 'react';
import { clampTerminalHeight } from '../hooks/useUiLayoutPersistence';

interface TerminalResizeHandleProps {
  heightPx: number;
  direction?: 'grow-down' | 'grow-up';
  onChangeHeight: (nextHeightPx: number) => void;
  onResetMedium: () => void;
}

export const TerminalResizeHandle: React.FC<TerminalResizeHandleProps> = ({
  heightPx,
  direction = 'grow-down',
  onChangeHeight,
  onResetMedium,
}) => {
  const dragStateRef = useRef<{ startY: number; startHeight: number } | null>(null);

  const handlePointerDown = (e: React.PointerEvent<HTMLDivElement>) => {
    e.preventDefault();
    const target = e.currentTarget;
    target.setPointerCapture(e.pointerId);
    dragStateRef.current = { startY: e.clientY, startHeight: heightPx };
  };

  const handlePointerMove = (e: React.PointerEvent<HTMLDivElement>) => {
    const st = dragStateRef.current;
    if (!st) return;
    const deltaY = e.clientY - st.startY;
    const rawNext =
      direction === 'grow-up' ? st.startHeight - deltaY : st.startHeight + deltaY;
    onChangeHeight(clampTerminalHeight(rawNext));
  };

  const handlePointerUp = (e: React.PointerEvent<HTMLDivElement>) => {
    if (!dragStateRef.current) return;
    dragStateRef.current = null;
    if (e.currentTarget.hasPointerCapture(e.pointerId)) {
      e.currentTarget.releasePointerCapture(e.pointerId);
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
    if (e.key === 'ArrowUp') {
      e.preventDefault();
      const delta = direction === 'grow-up' ? 24 : -24;
      onChangeHeight(clampTerminalHeight(heightPx + delta));
    } else if (e.key === 'ArrowDown') {
      e.preventDefault();
      const delta = direction === 'grow-up' ? -24 : 24;
      onChangeHeight(clampTerminalHeight(heightPx + delta));
    } else if (e.key === 'Home' || e.key === 'Enter') {
      e.preventDefault();
      onResetMedium();
    }
  };

  const maxPx =
    typeof window !== 'undefined' ? Math.max(220, window.innerHeight - 160) : 720;

  return (
    <div
      role="separator"
      aria-orientation="horizontal"
      aria-label="Resize terminal pane height (drag, ArrowUp/ArrowDown, or double-click to reset)"
      aria-valuemin={120}
      aria-valuemax={maxPx}
      aria-valuenow={Math.round(heightPx)}
      tabIndex={0}
      className="hk-terminal-resize-handle"
      title={`Terminal height: ${Math.round(heightPx)}px — drag or press ↑/↓ to resize; double-click to reset to Medium (40%)`}
      onPointerDown={handlePointerDown}
      onPointerMove={handlePointerMove}
      onPointerUp={handlePointerUp}
      onPointerCancel={handlePointerUp}
      onDoubleClick={onResetMedium}
      onKeyDown={handleKeyDown}
    >
      <span className="hk-terminal-resize-grip" aria-hidden="true" />
    </div>
  );
};
