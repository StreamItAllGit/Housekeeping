// @module: Housekeeping.Web/hooks/useRemoteTerminalTail
// @purpose: Polls remote fleet node log tail lines with adaptive online/offline backoff and AbortController cleanup.
// @depends-on: react, ../services/apiClient
// @exports: useRemoteTerminalTail
import { useEffect, useState } from 'react';
import { apiClient } from '../services/apiClient';

export function useRemoteTerminalTail(nodeId: string): {
  remoteLines: string[];
  remoteStatus: string;
} {
  const [remoteLines, setRemoteLines] = useState<string[]>([]);
  const [remoteStatus, setRemoteStatus] = useState<string>('');

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
      if (!cancelled) nextTimer = window.setTimeout(() => void pollOnce(), delayMs);
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

  return { remoteLines, remoteStatus };
}
