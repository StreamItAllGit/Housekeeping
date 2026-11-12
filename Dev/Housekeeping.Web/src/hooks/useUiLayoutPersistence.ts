// @module: Housekeeping.Web/hooks/useUiLayoutPersistence
// @purpose: Manages resizable terminal height/presets, sidebar collapse (Ctrl+B), theme/density/hash helpers, and 300ms settings.json sync.
// @depends-on: react, ../types/models
// @exports: useUiLayoutPersistence, clampTerminalHeight, computePresetHeight, readInitialColorScheme, readInitialDensity, applyMetaColorScheme, readTabFromHash
import { useCallback, useEffect, useRef, useState } from 'react';
import type { AppConfig, TerminalSizePreset, UiLayoutConfig } from '../types/models';

export type NavTabId =
  | 'dashboard'
  | 'queues'
  | 'builder'
  | 'modules'
  | 'reports'
  | 'settings'
  | 'history';
export type ColorSchemeMode = 'system' | 'dark' | 'light';
export type UiDensityMode = 'compact' | 'comfortable' | 'touch';

const MIN_TERMINAL_PX = 120;
const HEADER_RESERVE_PX = 160;
const LS_KEY = 'hk_ui_layout_v1';
const VALID_NAV_TABS: readonly NavTabId[] = [
  'dashboard',
  'queues',
  'builder',
  'modules',
  'reports',
  'settings',
  'history',
];

export function readInitialColorScheme(): ColorSchemeMode {
  try {
    const v = localStorage.getItem('color-scheme');
    if (v === 'dark' || v === 'light') return v;
  } catch (err) {
    console.debug('[uiLayout] color-scheme read skipped:', err);
  }
  return 'system';
}

export function readInitialDensity(): UiDensityMode {
  try {
    const v = localStorage.getItem('hk-density');
    if (v === 'compact' || v === 'comfortable' || v === 'touch') return v;
    if (
      typeof window !== 'undefined' &&
      typeof window.matchMedia === 'function' &&
      window.matchMedia('(max-width: 767px)').matches
    ) {
      return 'touch';
    }
  } catch (err) {
    console.debug('[uiLayout] density read skipped:', err);
  }
  return 'compact';
}

export function applyMetaColorScheme(mode: ColorSchemeMode) {
  const meta = document.querySelector('meta[name="color-scheme"]');
  if (meta) {
    meta.setAttribute('content', mode === 'system' ? 'light dark' : mode);
  }
  try {
    if (mode === 'system') localStorage.removeItem('color-scheme');
    else localStorage.setItem('color-scheme', mode);
  } catch (err) {
    console.debug('[uiLayout] color-scheme write skipped:', err);
  }
}

export function readTabFromHash(): NavTabId {
  try {
    const raw = window.location.hash.replace(/^#\/?/, '').trim().toLowerCase() as NavTabId;
    if ((VALID_NAV_TABS as readonly string[]).includes(raw)) return raw;
  } catch (err) {
    console.debug('[uiLayout] hash read skipped:', err);
  }
  return 'dashboard';
}

export function clampTerminalHeight(heightPx: number): number {
  const maxBody =
    typeof window !== 'undefined'
      ? Math.max(220, window.innerHeight - HEADER_RESERVE_PX)
      : 720;
  return Math.round(Math.min(maxBody, Math.max(MIN_TERMINAL_PX, heightPx)));
}

export function computePresetHeight(preset: Exclude<TerminalSizePreset, 'custom'>): number {
  const vh = typeof window !== 'undefined' ? window.innerHeight : 900;
  const ratio = preset === 'small' ? 0.25 : preset === 'large' ? 0.6 : 0.4;
  return clampTerminalHeight(vh * ratio);
}

function readInitialLayout(): UiLayoutConfig {
  const fallback: UiLayoutConfig = {
    TerminalHeightPx: 360,
    TerminalSizePreset: 'medium',
    TerminalOpen: true,
    TerminalWordWrap: typeof window !== 'undefined' && window.innerWidth <= 768,
    SidebarCollapsed: false,
  };
  try {
    const raw = localStorage.getItem(LS_KEY);
    if (raw) {
      const parsed = JSON.parse(raw) as Partial<UiLayoutConfig>;
      return {
        TerminalHeightPx: clampTerminalHeight(Number(parsed.TerminalHeightPx) || 360),
        TerminalSizePreset: parsed.TerminalSizePreset || 'medium',
        TerminalOpen: parsed.TerminalOpen !== false,
        TerminalWordWrap: Boolean(parsed.TerminalWordWrap ?? fallback.TerminalWordWrap),
        SidebarCollapsed: Boolean(parsed.SidebarCollapsed),
      };
    }
    if (localStorage.getItem('hk_sidebar_collapsed') === 'true') {
      fallback.SidebarCollapsed = true;
    }
  } catch (err) {
    console.debug('[uiLayout] localStorage read skipped:', err);
  }
  return fallback;
}

export function useUiLayoutPersistence(
  config: AppConfig | null,
  updateConfig: (mutator: (draft: AppConfig) => void) => void
) {
  const [uiLayout, setUiLayout] = useState<UiLayoutConfig>(readInitialLayout);
  const [drivesModalOpen, setDrivesModalOpen] = useState(false);
  const hydratedConfigRef = useRef(false);
  const persistTimerRef = useRef<number | null>(null);

  useEffect(() => {
    if (!config?.UiLayout || hydratedConfigRef.current) return;
    hydratedConfigRef.current = true;
    const next: UiLayoutConfig = {
      TerminalHeightPx: clampTerminalHeight(config.UiLayout.TerminalHeightPx || 360),
      TerminalSizePreset: config.UiLayout.TerminalSizePreset || 'medium',
      TerminalOpen: config.UiLayout.TerminalOpen !== false,
      TerminalWordWrap: Boolean(config.UiLayout.TerminalWordWrap),
      SidebarCollapsed: Boolean(config.UiLayout.SidebarCollapsed),
    };
    setUiLayout(next);
    try {
      localStorage.setItem(LS_KEY, JSON.stringify(next));
    } catch (err) {
      console.debug('[uiLayout] localStorage sync skipped:', err);
    }
  }, [config?.UiLayout]);

  const commitUiLayout = useCallback(
    (updater: (prev: UiLayoutConfig) => UiLayoutConfig) => {
      setUiLayout((prev) => {
        const next = updater(prev);
        try {
          localStorage.setItem(LS_KEY, JSON.stringify(next));
          localStorage.setItem('hk_sidebar_collapsed', String(next.SidebarCollapsed));
        } catch (err) {
          console.debug('[uiLayout] localStorage write skipped:', err);
        }
        if (persistTimerRef.current) window.clearTimeout(persistTimerRef.current);
        persistTimerRef.current = window.setTimeout(() => {
          persistTimerRef.current = null;
          updateConfig((draft) => {
            draft.UiLayout = { ...next };
          });
        }, 300);
        return next;
      });
    },
    [updateConfig]
  );

  const setTerminalHeight = useCallback(
    (heightPx: number, preset: TerminalSizePreset = 'custom') => {
      commitUiLayout((prev) => ({
        ...prev,
        TerminalHeightPx: clampTerminalHeight(heightPx),
        TerminalSizePreset: preset,
      }));
    },
    [commitUiLayout]
  );

  const applyTerminalPreset = useCallback(
    (preset: Exclude<TerminalSizePreset, 'custom'>) => {
      setTerminalHeight(computePresetHeight(preset), preset);
    },
    [setTerminalHeight]
  );

  const toggleSidebarCollapsed = useCallback(() => {
    commitUiLayout((prev) => ({ ...prev, SidebarCollapsed: !prev.SidebarCollapsed }));
  }, [commitUiLayout]);

  const setTerminalWordWrap = useCallback(
    (wrap: boolean) => {
      commitUiLayout((prev) => ({ ...prev, TerminalWordWrap: wrap }));
    },
    [commitUiLayout]
  );

  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && !e.altKey && e.key.toLowerCase() === 'b') {
        e.preventDefault();
        toggleSidebarCollapsed();
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [toggleSidebarCollapsed]);

  return {
    uiLayout,
    setTerminalHeight,
    applyTerminalPreset,
    toggleSidebarCollapsed,
    setTerminalWordWrap,
    drivesModalOpen,
    setDrivesModalOpen,
  };
}
