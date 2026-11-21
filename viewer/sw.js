// Service worker: caches the app so it opens offline. Bump VERSION when files change.
const VERSION = "equip-sw-lint-viewer-v1.0.0-2";
const FILES = [
  "./", "index.html", "app.css", "manifest.webmanifest", "rules.json", "icons/icon.svg", "icons/icon-192.png", "icons/icon-512.png",
  "src/app.js", "src/sarif.js", "src/ruleset.js", "src/i18n.js", "src/store.js",
  "samples/equipment.ko.sarif", "samples/equipment.en.sarif", "samples/equipment-v1.ko.sarif", "samples/equipment-v1.en.sarif",
  "vendor/pretendard/pretendard.css", "vendor/pretendard/Pretendard-Regular.subset.woff2", "vendor/pretendard/Pretendard-SemiBold.subset.woff2",
  "vendor/pretendard/Pretendard-Bold.subset.woff2", "vendor/pretendard/Pretendard-ExtraBold.subset.woff2",
];

self.addEventListener("install", (e) => { e.waitUntil(caches.open(VERSION).then((c) => c.addAll(FILES.map((f) => new Request(f, { cache: "reload" })))).then(() => self.skipWaiting())); });
self.addEventListener("activate", (e) => {
  e.waitUntil(caches.keys().then((keys) => Promise.all(keys.filter((k) => k !== VERSION).map((k) => caches.delete(k)))).then(() => self.clients.claim()));
});
self.addEventListener("fetch", (e) => {
  const url = new URL(e.request.url);
  if (e.request.method !== "GET" || url.origin !== location.origin) return;
  e.respondWith(
    caches.match(e.request, { ignoreSearch: true }).then((hit) => hit ||
      fetch(e.request).catch(() => (e.request.mode === "navigate" ? caches.match("index.html") : Response.error()))),
  );
});
