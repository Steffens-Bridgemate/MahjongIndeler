// Caution! Be sure you understand the caveats before publishing an application with
// temporary caching. See https://aka.ms/blazor-asset-caching
self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));
// The page posts 'SKIP_WAITING' only when the user clicks "Update now" in the banner, so the
// new worker takes over (and the page reloads) at a moment the user chose — never mid-action.
self.addEventListener('message', event => { if (event.data === 'SKIP_WAITING') self.skipWaiting(); });

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [/\.dll$/, /\.pdb$/, /\.wasm/, /\.html$/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/];
const offlineAssetsExclude = [/^service-worker\.js$/];

async function onInstall(event) {
    console.info('Service worker: Install');

    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)));

    const cache = await caches.open(cacheName);
    for (const asset of assetsRequests) {
        try {
            await cache.add(new Request(asset.url, { cache: 'no-cache' }));
        } catch (e) {
            console.warn('Service worker: Failed to cache', asset.url, e);
        }
    }

    // Intentionally NOT calling skipWaiting() here: the new worker installs and then waits.
    // The running app keeps using the current version until the user opts in via the banner.
}

async function onActivate(event) {
    console.info('Service worker: Activate');

    // Delete old caches
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));

    // Take control of all open tabs immediately
    self.clients.claim();
}

async function onFetch(event) {
    let cachedResponse = null;
    if (event.request.method === 'GET') {
        const shouldServeIndexHtml = event.request.mode === 'navigate';
        const request = shouldServeIndexHtml ? 'index.html' : event.request;
        const cache = await caches.open(cacheName);
        cachedResponse = await cache.match(request);
    }

    return cachedResponse || fetch(event.request);
}
