// @module: Housekeeping.Web/components/TopBarPwaControls
// @purpose: Extracted desktop & mobile PWA install, Tailscale HTTPS upgrade, and settings navigation controls for TopCommandBar.
// @depends-on: react, ../hints/settings, ../services/apiClient, ../types/models
// @exports: TopBarPwaControls

import React, { useEffect, useState } from 'react';
import { SETTINGS_HINTS } from '../hints/settings';
import { apiClient } from '../services/apiClient';
import type { TailscalePwaStatusDto } from '../types/models';

interface BeforeInstallPromptEvent extends Event {
  prompt: () => Promise<void>;
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed'; platform: string }>;
}

interface TopBarPwaControlsProps {
  isAuthenticated: boolean;
  variant: 'desktop' | 'mobile';
  onOpenWebSettings: () => void;
}

export const TopBarPwaControls: React.FC<TopBarPwaControlsProps> = ({
  isAuthenticated,
  variant,
  onOpenWebSettings,
}) => {
  const [installPrompt, setInstallPrompt] = useState<BeforeInstallPromptEvent | null>(null);
  const [isStandalonePwa, setIsStandalonePwa] = useState<boolean>(() =>
    typeof window !== 'undefined'
      ? window.matchMedia('(display-mode: standalone)').matches
      : false
  );
  const [tailscalePwa, setTailscalePwa] = useState<TailscalePwaStatusDto | null>(null);

  const isInsecureRemoteOrigin = typeof window !== 'undefined' && !window.isSecureContext;

  useEffect(() => {
    if (typeof window === 'undefined') return;
    const onBeforeInstall = (e: Event) => {
      e.preventDefault();
      setInstallPrompt(e as BeforeInstallPromptEvent);
    };
    const onInstalled = () => {
      setInstallPrompt(null);
      setIsStandalonePwa(true);
    };
    window.addEventListener('beforeinstallprompt', onBeforeInstall);
    window.addEventListener('appinstalled', onInstalled);
    return () => {
      window.removeEventListener('beforeinstallprompt', onBeforeInstall);
      window.removeEventListener('appinstalled', onInstalled);
    };
  }, []);

  useEffect(() => {
    if (!isAuthenticated || !isInsecureRemoteOrigin) return;
    apiClient
      .getTailscalePwaStatus()
      .then((res) => setTailscalePwa(res))
      .catch((err) => {
        console.debug('[TopBarPwaControls] Tailscale PWA probe skipped:', err);
      });
  }, [isAuthenticated, isInsecureRemoteOrigin]);

  const handleInstallPwa = async () => {
    if (!installPrompt) return;
    try {
      await installPrompt.prompt();
      const choice = await installPrompt.userChoice;
      if (choice.outcome === 'accepted') setInstallPrompt(null);
    } catch (err) {
      console.debug('[TopBarPwaControls] PWA prompt dismissed:', err);
    }
  };

  if (isStandalonePwa) return null;

  const isMobile = variant === 'mobile';
  const mobileStyle: React.CSSProperties | undefined = isMobile
    ? { width: '100%', justifyContent: 'center' }
    : undefined;
  const targetHttpsUrl = tailscalePwa?.MotherHttpsPwaUrl || tailscalePwa?.HttpsPwaUrl || '';

  return (
    <>
      {installPrompt && (
        <button
          type="button"
          className="hk-btn sm primary hk-has-hint"
          style={mobileStyle}
          onClick={() => void handleInstallPwa()}
          title={SETTINGS_HINTS.pwaInstallApp}
        >
          {isMobile ? '📲 Install Housekeeping App' : '📲 Install App'}
        </button>
      )}
      {isInsecureRemoteOrigin && tailscalePwa?.ServeConfigured && targetHttpsUrl && (
        <a
          href={targetHttpsUrl}
          className="hk-btn sm primary hk-has-hint"
          style={{ textDecoration: 'none', ...mobileStyle }}
          title={SETTINGS_HINTS.pwaSwitchHttps}
        >
          {isMobile ? '🔒 Switch to Trusted HTTPS (PWA)' : '🔒 Switch to HTTPS (PWA)'}
        </a>
      )}
      {isInsecureRemoteOrigin && (!tailscalePwa?.ServeConfigured || !targetHttpsUrl) && (
        <button
          type="button"
          className="hk-btn sm hk-has-hint"
          style={mobileStyle}
          onClick={onOpenWebSettings}
          title={SETTINGS_HINTS.pwaSetupHttps}
        >
          📱 Setup HTTPS PWA
        </button>
      )}
    </>
  );
};
