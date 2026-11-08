import React, { useState } from 'react';
import { useAppState } from '../context/AppStateContext';
import { apiClient } from '../services/apiClient';
import { ModalDialog } from './ModalDialog';

export const LoginGateDialog: React.FC = () => {
  const { authStatus, authGateOpen, refreshAuthAndBootstrap } = useAppState();
  const [password, setPassword] = useState('');
  const [confirmPwd, setConfirmPwd] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  const needsSetup = Boolean(authStatus?.requiresInitialPasswordSetup);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    if (needsSetup) {
      if (password.length < 4) {
        setError('Admin password must be at least 4 characters.');
        return;
      }
      if (password !== confirmPwd) {
        setError('Passwords do not match.');
        return;
      }
    }

    setBusy(true);
    try {
      if (needsSetup) {
        await apiClient.setupPassword(password);
      } else {
        await apiClient.login(password);
      }
      setPassword('');
      setConfirmPwd('');
      await refreshAuthAndBootstrap();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Authentication failed.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <ModalDialog
      open={authGateOpen}
      onClose={() => {}}
      closedBy="none"
      size="narrow"
      title={
        needsSetup
          ? '🔐 Housekeeping — Set Remote Admin Password'
          : '🔐 Housekeeping — Sign In'
      }
    >
      <form onSubmit={(e) => void handleSubmit(e)}>
        <p style={{ marginTop: 0, color: 'var(--text-secondary)' }}>
          {needsSetup
            ? 'Direct localhost access is auto-trusted, while LAN / Tailnet connections require an admin password. Create your admin password now to establish a 30-day session.'
            : `Enter the Admin Password for ${authStatus?.machineName || 'this Housekeeping host'} to continue.`}
        </p>

        {error && (
          <div className="hk-alert-banner err">
            <span>⚠ {error}</span>
          </div>
        )}

        <div className="hk-field" style={{ marginBottom: 12 }}>
          <label className="hk-field-label" htmlFor="hk-auth-pwd">
            Admin Password
          </label>
          <input
            id="hk-auth-pwd"
            type="password"
            className="hk-input"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete={needsSetup ? 'new-password' : 'current-password'}
            autoFocus
          />
        </div>

        {needsSetup && (
          <div className="hk-field" style={{ marginBottom: 14 }}>
            <label className="hk-field-label" htmlFor="hk-auth-confirm">
              Confirm Password
            </label>
            <input
              id="hk-auth-confirm"
              type="password"
              className="hk-input"
              value={confirmPwd}
              onChange={(e) => setConfirmPwd(e.target.value)}
              autoComplete="new-password"
            />
          </div>
        )}

        <div className="hk-row" style={{ justifyContent: 'flex-end' }}>
          <button type="submit" className="hk-btn primary" disabled={busy}>
            {busy ? 'Signing in...' : needsSetup ? 'Set Password & Sign In' : 'Sign In'}
          </button>
        </div>
      </form>
    </ModalDialog>
  );
};
