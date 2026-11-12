// @module: Housekeeping.Web/hooks/useTerminalAutoScroll
// @purpose: Saturation-proof ring-buffer auto-scroll hook using lastEntryKey, double-rAF layout latching, 45px proximity re-latch, and ResizeObserver.
// @depends-on: react
// @exports: useTerminalAutoScroll
import { useCallback, useEffect, useRef, type RefObject } from 'react';

interface UseTerminalAutoScrollOptions {
  viewportRef: RefObject<HTMLDivElement | null>;
  contentRef: RefObject<HTMLDivElement | null>;
  bottomSentinelRef: RefObject<HTMLDivElement | null>;
  autoScroll: boolean;
  setAutoScroll: (next: boolean) => void;
  entryCount: number;
  lastEntryKey: string | number;
  wordWrap: boolean;
}

const BOTTOM_PROXIMITY_PX = 45;

export function useTerminalAutoScroll({
  viewportRef,
  contentRef,
  bottomSentinelRef,
  autoScroll,
  setAutoScroll,
  entryCount,
  lastEntryKey,
  wordWrap,
}: UseTerminalAutoScrollOptions) {
  const programmaticScrollRef = useRef(false);

  const scrollToBottomImmediate = useCallback(() => {
    const el = viewportRef.current;
    if (!el) return;
    programmaticScrollRef.current = true;
    el.scrollTop = el.scrollHeight;
    bottomSentinelRef.current?.scrollIntoView({ block: 'end' });
    requestAnimationFrame(() => {
      programmaticScrollRef.current = false;
    });
  }, [bottomSentinelRef, viewportRef]);

  // Double-rAF scroll latch keyed on BOTH length AND lastEntryKey + wordWrap so 1,500-line ring-buffer saturation never stalls
  useEffect(() => {
    if (!autoScroll) return;
    let raf2 = 0;
    const raf1 = requestAnimationFrame(() => {
      scrollToBottomImmediate();
      raf2 = requestAnimationFrame(() => {
        scrollToBottomImmediate();
      });
    });
    return () => {
      cancelAnimationFrame(raf1);
      if (raf2) cancelAnimationFrame(raf2);
    };
  }, [autoScroll, entryCount, lastEntryKey, wordWrap, scrollToBottomImmediate]);

  // Keep pinned to bottom when container or wrapped content resizes
  useEffect(() => {
    const viewport = viewportRef.current;
    if (!viewport) return;
    const ro = new ResizeObserver(() => {
      if (autoScroll) {
        scrollToBottomImmediate();
      }
    });
    ro.observe(viewport);
    if (contentRef.current) {
      ro.observe(contentRef.current);
    }
    return () => ro.disconnect();
  }, [autoScroll, contentRef, scrollToBottomImmediate, viewportRef]);

  const handleViewportScroll = useCallback(
    (scrollTop: number, scrollHeight: number, clientHeight: number) => {
      if (programmaticScrollRef.current) return;
      const distanceFromBottom = scrollHeight - scrollTop - clientHeight;
      const nearBottom = distanceFromBottom <= BOTTOM_PROXIMITY_PX;
      if (!nearBottom && autoScroll) {
        setAutoScroll(false);
      } else if (nearBottom && !autoScroll) {
        setAutoScroll(true);
      }
    },
    [autoScroll, setAutoScroll]
  );

  return { handleViewportScroll, scrollToBottomImmediate };
}
