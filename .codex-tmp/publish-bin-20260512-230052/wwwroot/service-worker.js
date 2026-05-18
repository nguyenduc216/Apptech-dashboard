const STATIC_CACHE = 'apptech-static-v1';
const STATIC_ASSETS = [
    '/dang-nhap',
    '/manifest.webmanifest',
    '/css/site.css',
    '/js/dashboard.js',
    '/images/login-smart-home-bg.jpg',
    '/images/apptech-logo-192.png',
    '/images/apptech-logo-512.png'
];

self.addEventListener('install', (event) => {
    event.waitUntil(
        caches.open(STATIC_CACHE)
            .then((cache) => cache.addAll(STATIC_ASSETS))
            .catch(() => Promise.resolve())
    );

    self.skipWaiting();
});

self.addEventListener('activate', (event) => {
    event.waitUntil(
        caches.keys().then((keys) => Promise.all(
            keys
                .filter((key) => key !== STATIC_CACHE)
                .map((key) => caches.delete(key))
        ))
    );

    self.clients.claim();
});

self.addEventListener('fetch', (event) => {
    const { request } = event;
    if (request.method !== 'GET') {
        return;
    }

    const requestUrl = new URL(request.url);
    if (requestUrl.origin !== self.location.origin) {
        return;
    }

    if (request.mode === 'navigate') {
        event.respondWith(
            fetch(request).catch(() => caches.match('/dang-nhap'))
        );
        return;
    }

    event.respondWith(
        caches.match(request).then((cachedResponse) => {
            if (cachedResponse) {
                return cachedResponse;
            }

            return fetch(request).then((networkResponse) => {
                if (!networkResponse || networkResponse.status !== 200 || networkResponse.type !== 'basic') {
                    return networkResponse;
                }

                const responseClone = networkResponse.clone();
                caches.open(STATIC_CACHE).then((cache) => {
                    cache.put(request, responseClone);
                });

                return networkResponse;
            });
        })
    );
});
