// IndexedDB storage, schema version 1: kv (settings, open reports, rule set history) · triage (fingerprint -> verdict) · errors.
// Falls back to memory when IndexedDB is unavailable (private windows); work is then lost on reload.
const DB = "equip-sw-lint", VER = 1;
const STORES = { kv: {}, triage: {}, errors: { autoIncrement: true } };

let dbp = null;
const mem = Object.fromEntries(Object.keys(STORES).map((k) => [k, new Map()]));
let memSeq = 0;

function open() {
  if (dbp) return dbp;
  dbp = new Promise((res) => {
    let req;
    try { req = indexedDB.open(DB, VER); } catch { res(null); return; }
    req.onupgradeneeded = () => {
      const db = req.result;
      for (const [name, o] of Object.entries(STORES)) if (!db.objectStoreNames.contains(name)) db.createObjectStore(name, o);
    };
    req.onsuccess = () => res(req.result);
    req.onerror = () => res(null);
    req.onblocked = () => res(null);
  });
  return dbp;
}

const wrap = (r) => new Promise((res, rej) => { r.onsuccess = () => res(r.result); r.onerror = () => rej(r.error); });

async function tx(store, mode, fn) {
  const db = await open();
  if (!db) return fn(null);
  return fn(db.transaction(store, mode).objectStore(store));
}

export const get = (store, key) => tx(store, "readonly", (s) => (s ? wrap(s.get(key)) : mem[store].get(key)));
export const put = (store, value, key) => tx(store, "readwrite", (s) => {
  if (s) return wrap(key === undefined ? s.put(value) : s.put(value, key));
  mem[store].set(key ?? ++memSeq, value);
  return key;
});
export const del = (store, key) => tx(store, "readwrite", (s) => (s ? wrap(s.delete(key)) : mem[store].delete(key)));
export const clear = (store) => tx(store, "readwrite", (s) => (s ? wrap(s.clear()) : mem[store].clear()));
export const persistent = async () => !!(await open());

export async function entries(store) {
  return tx(store, "readonly", async (s) => {
    if (!s) return [...mem[store].entries()];
    const [keys, values] = await Promise.all([wrap(s.getAllKeys()), wrap(s.getAll())]);
    return keys.map((k, i) => [k, values[i]]);
  });
}

// keep the last 200 errors
export async function logError(e) {
  try {
    await put("errors", { t: new Date().toISOString(), msg: String(e?.message || e).slice(0, 500), stack: String(e?.stack || "").slice(0, 1500) });
    const list = await entries("errors");
    for (const [k] of list.slice(0, Math.max(0, list.length - 200))) await del("errors", k);
  } catch { /* logging must never throw */ }
}

const BACKUP_KV = ["lang", "theme", "criteria", "history", "filters", "firstRunDone"];

export async function exportBackup() {
  const kv = {};
  for (const k of BACKUP_KV) kv[k] = await get("kv", k);
  return { app: "equip-sw-lint", schema: VER, exported: new Date().toISOString(), kv, triage: Object.fromEntries(await entries("triage")) };
}

export async function importBackup(b) {
  if (!b || b.app !== "equip-sw-lint" || typeof b.kv !== "object" || typeof b.triage !== "object") throw new Error("badBackup");
  for (const k of BACKUP_KV) if (b.kv[k] !== undefined) await put("kv", b.kv[k], k);
  await clear("triage");
  for (const [fp, v] of Object.entries(b.triage)) await put("triage", v, fp);
}

export async function clearAll() { for (const s of Object.keys(STORES)) await clear(s); }
