// Minimal PWA service worker — caches the app shell, never auth routes.
const CACHE = 'geniusonline-v1';
const SHELL = ['/', '/manifest.webmanifest', '/app.css'];

self.addEventListener('install', (e) => {
    e.waitUntil(caches.open(CACHE).then((c) => c.addAll(SHELL)).catch(() => {}));
    self.skipWaiting();
});

self.addEventListener('activate', (e) => {
    e.waitUntil(
        caches.keys().then((keys) =>
            Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k))))
    );
    self.clients.claim();
});

self.addEventListener('fetch', (e) => {
    const url = new URL(e.request.url);
    if (url.pathname.startsWith('/login') ||
        url.pathname.startsWith('/logout') ||
        url.pathname.startsWith('/hubs') ||
        url.pathname.startsWith('/_blazor')) {
        return; // never cache auth or live channels
    }
    if (e.request.method !== 'GET') return;
    e.respondWith(
        caches.match(e.request).then((cached) => cached || fetch(e.request).catch(() => cached))
    );
});
