// @module: Housekeeping.Web/main
// @purpose: Bootstraps React root and registers subpath-aware PWA service worker in secure contexts.
// @depends-on: react, react-dom/client, ./App, ./utils/subpathUrl, ./styles/theme.css
// @exports: (entrypoint)
import React from 'react';
import ReactDOM from 'react-dom/client';
import { App } from './App';
import { resolveAppUrl } from './utils/subpathUrl';
import './styles/theme.css';
import './styles/sidebarAndModals.css';

if (typeof window !== 'undefined' && window.isSecureContext && 'serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    const swUrl = resolveAppUrl('/sw.js');
    const swScope = resolveAppUrl('/');
    navigator.serviceWorker
      .register(swUrl, { scope: swScope })
      .catch((err: unknown) => {
        console.debug('[Housekeeping SW] Registration skipped:', err);
      });
  });
}

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);
