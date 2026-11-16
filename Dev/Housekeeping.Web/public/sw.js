const CACHE_NAME = 'housekeeping-shell-v0.1.12-hf.1';
const PRECACHE_URLS = [
  '/',
  '/manifest.webmanifest',
  '/favicon.ico',
  '/app_icon.png',
  '/apple-touch-icon.png',
  '/icon-192.png',
  '/icon-512.png',
  '/icon-maskable-192.png',
  '/icon-maskable-512.png',
];

const OFFLINE_FALLBACK_HTML = `<!doctype html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <meta name="theme-color" content="#090e17" />
  <title>Housekeeping — Offline</title>
  <style>
    body {
      margin: 0;
      min-height: 100dvh;
      display: grid;
      place-items: center;
      padding: 24px;
      background: #090e17;
      color: #e6edf7;
      font-family: system-ui, -apple-system, Segoe UI, Roboto, sans-serif;
    }
    .card {
      max-width: 420px;
      background: #101929;
      border: 1px solid #22324d;
      border-radius: 14px;
      padding: 24px;
      text-align: center;
      box-shadow: 0 18px 40px rgba(0, 0, 0, 0.45);
    }
    img { width: 64px; height: 64px; margin-bottom: 12px; }
    h1 { font-size: 18px; margin: 0 0 8px; }
    p { font-size: 14px; line-height: 1.5; color: #9ab0ce; margin: 0 0 18px; }
    button {
      background: #2563eb;
      color: #fff;
      border: none;
      border-radius: 8px;
      padding: 10px 18px;
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
    }
  </style>
</head>
<body>
  <div class="card">
    <img src="/icon-192.png" alt="Housekeeping" />
    <h1>Housekeeping Host Unreachable</h1>
    <p>Verify that your Housekeeping PC is powered on and Tailscale or LAN connectivity is active.</p>
    <button type="button" onclick="location.reload()">Retry Connection</button>
  </div>
</body>
</html>`;

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches
      .open(CACHE_NAME)
      .then((cache) => cache.addAll(PRECACHE_URLS).catch(() => undefined))
      .then(() => self.skipWaiting())
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) =>
        Promise.all(
          keys
            .filter((k) => k.startsWith('housekeeping-shell-') && k !== CACHE_NAME)
            .map((k) => caches.delete(k))
        )
      )
      .then(() => self.clients.claim())
  );
});

self.addEventListener('message', (event) => {
  if (event.data && event.data.type === 'SKIP_WAITING') {
    self.skipWaiting();
  }
});

self.addEventListener('fetch', (event) => {
  const { request } = event;
  if (request.method !== 'GET') {
    return;
  }

  const url = new URL(request.url);
  if (url.origin !== self.location.origin) {
    return;
  }

  // Never intercept or cache live API endpoints or SignalR real-time hubs
  if (
    url.pathname.startsWith('/api/') ||
    url.pathname.startsWith('/hubs/')
  ) {
    return;
  }

  event.respondWith(
    (async () => {
      try {
        const networkResponse = await fetch(request);
        if (networkResponse && networkResponse.ok && networkResponse.type === 'basic') {
          const cache = await caches.open(CACHE_NAME);
          cache.put(request, networkResponse.clone()).catch(() => undefined);
        }
        return networkResponse;
      } catch {
        const cached = await caches.match(request);
        if (cached) {
          return cached;
        }
        if (request.mode === 'navigate') {
          const rootShell = await caches.match('/');
          if (rootShell) {
            return rootShell;
          }
          return new Response(OFFLINE_FALLBACK_HTML, {
            status: 200,
            headers: { 'Content-Type': 'text/html; charset=utf-8' },
          });
        }
        return Response.error();
      }
    })()
  );
});
