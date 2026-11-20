// @module: Housekeeping.Web/pages/settings/SettingsTailscalePwaCard
// @purpose: Extracted Mobile PWA, Tailscale Serve HTTPS, Mother subpath URL copy, and offline SVG QR pairing card for SettingsPage.
// @depends-on: react, ../../hints/settings, ../../services/apiClient, ../../types/models, ../../utils/subpathUrl
// @exports: SettingsTailscalePwaCard

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { SETTINGS_HINTS } from '../../hints/settings';
import { apiClient } from '../../services/apiClient';
import type { TailscalePwaStatusDto, TailscaleQrDto } from '../../types/models';
import { parseQrSvgMarkup } from '../../utils/subpathUrl';

interface SettingsTailscalePwaCardProps {
  port: number;
  tailscalePwa: TailscalePwaStatusDto | null;
  onStatusChange: (status: TailscalePwaStatusDto) => void;
  showToast: (msg: string) => void;
}

export const SettingsTailscalePwaCard: React.FC<SettingsTailscalePwaCardProps> = ({
  port,
  tailscalePwa,
  onStatusChange,
  showToast,
}) => {
  const [tailscaleBusy, setTailscaleBusy] = useState(false);
  const [qrData, setQrData] = useState<TailscaleQrDto | null>(null);
  const [qrVariant, setQrVariant] = useState<'mother' | 'direct'>('mother');

  const refreshStatusAndQr = useCallback(async () => {
    const [status, qr] = await Promise.all([apiClient.getTailscalePwaStatus(), apiClient.getTailscaleQr()]);
    onStatusChange(status);
    setQrData(qr);
  }, [onStatusChange]);

  useEffect(() => {
    let active = true;
    const probe = () => {
      void Promise.all([apiClient.getTailscalePwaStatus(), apiClient.getTailscaleQr()])
        .then(([status, qr]) => {
          if (!active) return;
          onStatusChange(status);
          setQrData(qr);
        })
        .catch((err) => console.debug('[SettingsTailscalePwaCard] Probe skipped:', err));
    };
    probe();
    const timer =
      tailscalePwa?.PendingAuthUrl && !tailscalePwa.ServeConfigured ? window.setInterval(probe, 4000) : undefined;
    return () => {
      active = false;
      if (timer) window.clearInterval(timer);
    };
  }, [onStatusChange, tailscalePwa?.PendingAuthUrl, tailscalePwa?.ServeConfigured]);

  const runAction = async (fn: () => Promise<void>, fallbackErr: string) => {
    setTailscaleBusy(true);
    try {
      await fn();
    } catch (err) {
      showToast(err instanceof Error ? err.message : fallbackErr);
    } finally {
      setTailscaleBusy(false);
    }
  };

  const copyText = (value: string, label: string) => {
    if (!navigator.clipboard?.writeText) {
      showToast('Clipboard API unavailable in non-HTTPS context. Copy URL directly.');
      return;
    }
    void navigator.clipboard
      .writeText(value)
      .then(() => showToast(`Copied ${label} to clipboard.`))
      .catch((err) => {
        console.debug('[SettingsTailscalePwaCard] clipboard write failed:', err);
        showToast('Clipboard write blocked by browser. Copy URL directly.');
      });
  };

  const motherUrl = qrData?.motherUrl || tailscalePwa?.MotherHttpsPwaUrl || '';
  const directUrl = tailscalePwa?.HttpsPwaUrl || qrData?.directUrl || '';
  const activeQrUrl = qrVariant === 'mother' ? motherUrl || directUrl : directUrl || motherUrl;
  const activeQrSvg = qrVariant === 'mother' ? qrData?.motherQrSvg : qrData?.directQrSvg;
  const parsedSvg = useMemo(() => parseQrSvgMarkup(activeQrSvg), [activeQrSvg]);
  const pillClass = tailscalePwa?.ServeConfigured ? 'ok' : tailscalePwa?.PendingAuthUrl ? 'warn' : '';
  const pillLabel = tailscalePwa?.ServeConfigured
    ? 'HTTPS Active'
    : tailscalePwa?.PendingAuthUrl
      ? 'Awaiting Tailscale Auth'
      : tailscalePwa?.TailscaleAvailable
        ? 'HTTPS Not Configured'
        : 'Checking Tailscale...';

  return (
    <div className="hk-card">
      <div className="hk-card-header">
        <div>
          <h3 className="hk-card-title">📱 Mobile PWA, Mother Subpath &amp; Tailscale Serve HTTPS</h3>
          <p className="hk-card-subtitle">
            Scan the offline QR code or enable Tailscale Serve HTTPS for full-screen Android/iOS PWA installs while keeping LAN HTTP (<code>:{port}</code>) intact.
          </p>
        </div>
        <div className="hk-row">
          <span className={`hk-pill ${pillClass}`}>{pillLabel}</span>
          <button type="button" className="hk-btn sm hk-has-hint" disabled={tailscaleBusy} title={SETTINGS_HINTS.tailscaleRefresh} onClick={() => void runAction(refreshStatusAndQr, 'Status check failed.')}>
            🔄 Refresh
          </button>
          <button
            type="button"
            className="hk-btn sm primary hk-has-hint"
            disabled={tailscaleBusy || tailscalePwa?.TailscaleAvailable === false}
            title={SETTINGS_HINTS.tailscaleEnable}
            onClick={() =>
              void runAction(async () => {
                const res = await apiClient.enableTailscalePwa();
                onStatusChange(res);
                setQrData(await apiClient.getTailscaleQr());
                showToast(res.StatusMessage);
              }, 'Failed to enable Tailscale Serve.')
            }
          >
            {tailscaleBusy ? 'Provisioning...' : tailscalePwa?.ServeConfigured ? '⚡ Re-Apply Tailscale HTTPS' : '⚡ Enable Tailscale HTTPS (PWA)'}
          </button>
          {tailscalePwa?.ServeConfigured && (
            <button
              type="button"
              className="hk-btn sm danger hk-has-hint"
              disabled={tailscaleBusy}
              title={SETTINGS_HINTS.tailscaleReset}
              onClick={() =>
                void runAction(async () => {
                  onStatusChange(await apiClient.disableTailscalePwa());
                  setQrData(await apiClient.getTailscaleQr());
                  showToast('Reset Tailscale Serve HTTPS configuration.');
                }, 'Reset failed.')
              }
            >
              Reset Serve
            </button>
          )}
        </div>
      </div>

      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 16, alignItems: 'flex-start' }}>
        <div style={{ flex: '1 1 340px', display: 'flex', flexDirection: 'column', gap: 10 }}>
          <div style={{ fontSize: 13, color: 'var(--text-secondary)' }}>
            {tailscalePwa?.StatusMessage || 'Inspecting local Tailscale daemon...'}
          </div>
          {tailscalePwa?.PendingAuthUrl && !tailscalePwa.ServeConfigured && (
            <div style={{ padding: '10px 12px', borderRadius: 8, border: '1px solid var(--warn)', background: 'rgba(245, 158, 11, 0.08)', display: 'flex', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: 10 }}>
              <div style={{ fontSize: 13 }}>
                <strong>One-Time Tailscale Authorization Required:</strong> Approve Tailscale Serve &amp; HTTPS Certificates on your tailnet to activate automatic mobile TLS.
              </div>
              <a href={tailscalePwa.PendingAuthUrl} target="_blank" rel="noreferrer" className="hk-btn sm primary" style={{ textDecoration: 'none' }}>
                🔑 Authorize Tailscale Serve &amp; HTTPS ↗
              </a>
            </div>
          )}
          {motherUrl && (
            <div className="hk-field">
              <label className="hk-field-label">Mother Subpath PWA URL (/app/housekeeping/)</label>
              <div className="hk-row">
                <input type="text" readOnly className="hk-input mono" style={{ flex: 1 }} value={motherUrl} />
                <button type="button" className="hk-btn sm hk-has-hint" title={SETTINGS_HINTS.tailscaleCopyMotherUrl} onClick={() => copyText(motherUrl, 'Mother PWA URL')}>
                  📋 Copy Mother URL
                </button>
              </div>
            </div>
          )}
          {directUrl && (
            <div className="hk-field">
              <label className="hk-field-label">Direct Housekeeping PWA URL</label>
              <div className="hk-row">
                <input type="text" readOnly className="hk-input mono" style={{ flex: 1 }} value={directUrl} />
                <button type="button" className="hk-btn sm hk-has-hint" title={SETTINGS_HINTS.tailscaleCopyDirectUrl} onClick={() => copyText(directUrl, 'Direct PWA URL')}>
                  📋 Copy URL
                </button>
                {tailscalePwa?.ServeConfigured && (
                  <a href={directUrl} target="_blank" rel="noreferrer" className="hk-btn sm success" style={{ textDecoration: 'none' }}>
                    🔒 Open HTTPS PWA ↗
                  </a>
                )}
              </div>
            </div>
          )}
        </div>

        <div style={{ flex: '0 0 auto', display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 8, padding: 12, borderRadius: 10, border: '1px solid var(--border-subtle)', background: 'var(--bg-elevated)' }}>
          <div className="hk-row" style={{ gap: 6 }}>
            <button type="button" className={`hk-btn sm hk-has-hint ${qrVariant === 'mother' ? 'primary' : ''}`} title={SETTINGS_HINTS.tailscaleQrVariant} onClick={() => setQrVariant('mother')}>
              Mother QR
            </button>
            <button type="button" className={`hk-btn sm hk-has-hint ${qrVariant === 'direct' ? 'primary' : ''}`} title={SETTINGS_HINTS.tailscaleQrVariant} onClick={() => setQrVariant('direct')}>
              Direct QR
            </button>
          </div>
          {parsedSvg ? (
            <svg viewBox={parsedSvg.viewBox} shapeRendering="crispEdges" role="img" aria-label={`QR code for ${activeQrUrl}`} style={{ width: 148, height: 148, borderRadius: 6, background: parsedSvg.lightColor }}>
              <rect width={parsedSvg.size} height={parsedSvg.size} fill={parsedSvg.lightColor} />
              <path fill={parsedSvg.darkColor} d={parsedSvg.pathData} />
            </svg>
          ) : (
            <div style={{ width: 148, height: 148, display: 'grid', placeItems: 'center', fontSize: 12 }}>Generating QR...</div>
          )}
          <span className="mono" style={{ fontSize: 10.5, maxWidth: 180, wordBreak: 'break-all', textAlign: 'center' }}>
            {activeQrUrl}
          </span>
        </div>
      </div>
    </div>
  );
};
