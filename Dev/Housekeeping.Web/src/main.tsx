// @module: Housekeeping.Web/main
// @purpose: Bootstraps React root and registers optional PWA service worker in secure contexts.
// @depends-on: react, react-dom/client, ./App, ./styles/theme.css
// @exports: (entrypoint)
import React from 'react';
import ReactDOM from 'react-dom/client';
import { App } from './App';
import './styles/theme.css';
import './styles/sidebarAndModals.css';

if (typeof window !== 'undefined' && window.isSecureContext && 'serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    navigator.serviceWorker
      .register('/sw.js', { scope: '/' })
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
