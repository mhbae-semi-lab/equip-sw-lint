// Manual / report screenshots (synthetic data only). Drives headless Chrome over the DevTools protocol.
//   1) python -m http.server 8741 --bind 127.0.0.1          (repository root)
//   2) python docs/manual/make_assets.py <built demo project>  (terminal pages, optional)
//   3) node docs/manual/capture.mjs [http://127.0.0.1:8741/]
// Output: docs/manual/shots/*.png (1280 x 800 CSS px, scale 1.5) + shots/marks.json (callout rectangles).
import { spawn } from "node:child_process";
import { mkdtempSync, writeFileSync, mkdirSync, rmSync, existsSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = process.argv[2] || "http://127.0.0.1:8741/";
const APP = ROOT + "viewer/";
const HERE = dirname(fileURLToPath(import.meta.url));
const OUT = join(HERE, "shots");
const CHROME = process.env.CHROME || "C:/Program Files/Google/Chrome/Application/chrome.exe";
const PORT = 9335;
mkdirSync(OUT, { recursive: true });
const profile = mkdtempSync(join(tmpdir(), "esl-capture-"));
const proc = spawn(CHROME, ["--headless=new", `--remote-debugging-port=${PORT}`, `--user-data-dir=${profile}`, "--hide-scrollbars", "--no-first-run", "--window-size=1280,800", "about:blank"], { stdio: "ignore" });
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

let ws, seq = 0;
const pending = new Map();
async function connect() {
  for (let i = 0; i < 50; i++) {
    try {
      const list = await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json();
      const page = list.find((x) => x.type === "page");
      if (page) { ws = new WebSocket(page.webSocketDebuggerUrl); break; }
    } catch { /* starting */ }
    await sleep(200);
  }
  await new Promise((r) => ws.addEventListener("open", r, { once: true }));
  ws.addEventListener("message", (m) => {
    const d = JSON.parse(m.data);
    if (d.id && pending.has(d.id)) { const p = pending.get(d.id); pending.delete(d.id); d.error ? p.rej(new Error(d.error.message)) : p.res(d.result); }
  });
}
const send = (method, params = {}) => new Promise((res, rej) => { const id = ++seq; pending.set(id, { res, rej }); ws.send(JSON.stringify({ id, method, params })); });
async function js(expr) {
  const r = await send("Runtime.evaluate", { expression: `(async () => { ${expr} })()`, awaitPromise: true, returnByValue: true });
  if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
  return r.result.value;
}
async function view(w, h, scheme = "light", mobile = false) {
  await send("Emulation.setDeviceMetricsOverride", { width: w, height: h, deviceScaleFactor: 1.5, mobile });
  await send("Emulation.setEmulatedMedia", { features: [{ name: "prefers-color-scheme", value: scheme }, { name: "prefers-reduced-motion", value: "reduce" }] });
}
const MARKS = {};
async function shot(name, sel, offset = 66, marks = []) {
  if (sel) await js(`const el = document.querySelector(${JSON.stringify(sel)}); window.scrollTo(0, el.getBoundingClientRect().top + scrollY - ${offset});`);
  await sleep(250);
  MARKS[name] = await js(`return ${JSON.stringify(marks)}.map((m) => { const el = document.querySelector(m); if (!el) return null;
    const r = el.getBoundingClientRect(); if (r.bottom < 0 || r.top > innerHeight || !r.width) return null;
    return [r.left, r.top, r.right, Math.min(r.bottom, innerHeight)].map(Math.round); });`);
  const r = await send("Page.captureScreenshot", { format: "png" });
  writeFileSync(join(OUT, name + ".png"), Buffer.from(r.data, "base64"));
  console.log("shot", name, MARKS[name].map((x) => (x ? "o" : "-")).join(""));
}
const click = (sel) => js(`document.querySelector(${JSON.stringify(sel)}).click()`);
async function open(url, wait = 1500) { await send("Page.navigate", { url }); await sleep(wait); await js(`await document.fonts.ready`); }
async function setLang(lang) {
  await js(`const s = document.querySelector("#lang"); if (s.value !== "${lang}") { s.value = "${lang}"; s.dispatchEvent(new Event("change", { bubbles: true })); }`);
  await sleep(700);
}
async function tab(name) { await click(`#t-${name}`); await sleep(250); }
// pick the n-th finding of a rule in the list and triage it
const pick = (rule, n = 0) => js(`[...document.querySelectorAll('.res')].filter(b => b.querySelector('b').textContent === '${rule}')[${n}].click()`);
const verdict = async (v) => { await js(`document.dispatchEvent(new KeyboardEvent('keydown', { key: '${v}' }))`); await sleep(200); };
async function feed(id, name, text) {
  await js(`const dt = new DataTransfer(); dt.items.add(new File([${JSON.stringify(text)}], ${JSON.stringify(name)}, { type: "application/json" }));
    const i = document.getElementById(${JSON.stringify(id)}); i.files = dt.files; i.dispatchEvent(new Event("change"));`);
  await sleep(500);
}

try {
  await connect();
  await send("Page.enable");
  await send("Runtime.enable");
  await open(APP, 1200);
  await js(`for (const r of await navigator.serviceWorker.getRegistrations()) await r.unregister();
    await new Promise(r => { const q = indexedDB.deleteDatabase("equip-sw-lint"); q.onsuccess = q.onerror = q.onblocked = r; });`);

  // terminal / HTML report pages (made by make_assets.py)
  await view(1280, 800);
  if (existsSync(join(HERE, ".cache", "term-cli.html"))) { await open(ROOT + "docs/manual/.cache/term-cli.html", 600); await shot("TermCli"); }
  if (existsSync(join(HERE, ".cache", "term-build.html"))) { await open(ROOT + "docs/manual/.cache/term-build.html", 600); await shot("TermBuild"); }
  if (existsSync(join(HERE, ".cache", "report.html"))) {
    await open(ROOT + "docs/manual/.cache/report.html", 600); await shot("HtmlTop");
    await shot("HtmlRule", "#ESL004", 20);
  }

  // first run
  await open(APP);
  await setLang("ko");
  await shot("Home", null, 0, ["#lang", "#theme", "#open-help", "#open-settings", "#first-run", "#load-sample", ".src-tabs .file-btn", "#load-sample-base", ".format summary"]);

  // example report
  await click("#load-sample"); await sleep(600);
  await click("#dismiss-first"); await sleep(200);
  await pick("ESL004", 2); await sleep(200);
  await shot("Findings", "#tabs", 70, ["#tabs", "#f-q", "#f-rule", "#rule-chips", ".res.on", "#detail .d-msg", "#detail pre.code .hit", "#detail .triage"]);
  await js(`document.querySelector("#detail h4").scrollIntoView({ block: "start" }); window.scrollBy(0, -260);`);
  await shot("RuleDoc", null, 0, ["#detail pre.code", "#detail .triage", "#detail h4:nth-of-type(1)", "#detail h4:nth-of-type(2)", "#detail h4:nth-of-type(3)", "#detail h4:nth-of-type(4)"]);
  await shot("Status", "#drop", 70, ["#status .stat-row > div:nth-child(1)", "#status .stat-row > div:nth-child(2)", "#status .stat-row > div:nth-child(4)", ".format summary"]);

  // compare: same code, rule set v1 baseline
  await click("#load-sample-base"); await sleep(600);
  await tab("compare");
  await shot("Compare", "#tabs", 70, ["#p-compare .stat-row", "#p-compare tr.changed", "#p-compare ul.plain"]);
  await tab("findings");
  await js(`const s = document.querySelector("#f-state"); s.value = "new"; s.dispatchEvent(new Event("change"));`); await sleep(300);
  await pick("ESL010", 0);
  await shot("CompareNew", "#tabs", 70, ["#f-state", ".res .tag.new", "#f-count"]);
  await js(`const s = document.querySelector("#f-state"); s.value = ""; s.dispatchEvent(new Event("change"));`); await sleep(200);

  // shadow triage: ESL005 two findings, ESL011 one
  await pick("ESL005", 0); await verdict("t");
  await pick("ESL005", 1); await verdict("t");
  await pick("ESL011", 0); await verdict("t");
  await pick("ESL010", 0); await verdict("f");
  await tab("shadow");
  await shot("Shadow", "#tabs", 70, ["#p-shadow > p.hint", "#p-shadow .card:nth-child(1) .stat-row", "#p-shadow .card:nth-child(1) .meter", "#p-shadow .card:nth-child(1) [data-promote]", "#p-shadow .card:nth-child(1) details"]);
  // demo bar: 2 triaged, as written on the manual page
  await click("#open-settings"); await sleep(300);
  await js(`const m = document.getElementById("c-min"); m.value = "2"; m.dispatchEvent(new Event("change"));`); await sleep(200);
  await shot("Settings", null, 0, ["#c-min", "#c-fp", "#export-backup", ".file-label", "#clear-all", "#err-log"]);
  await js(`document.querySelector("#settings form").requestSubmit()`); await sleep(300);
  await shot("ShadowReady", "#tabs", 70, ["#p-shadow > p.hint", "#p-shadow .card:nth-child(1) .ok-t", "#p-shadow .card:nth-child(1) [data-promote]"]);
  await click('[data-promote="ESL005"]'); await sleep(400);
  await tab("history");
  await shot("History", "#tabs", 70, ["#p-history h3", "#ec", "#copy-ec", "#dl-ec", "#p-history tbody tr:nth-child(1)", "[data-rollback]"]);
  await click('[data-rollback="1"]'); await sleep(400);
  await shot("HistoryRollback", "#tabs", 70, ["#ec", "#p-history tbody tr:nth-child(1)"]);
  await tab("rules");
  await shot("Rules", "#tabs", 70, ["#p-rules > p", "#rule-ESL001 .badge", "#rule-ESL005 .badge"]);
  await tab("findings");

  // my data: build SARIF (line numbers only) and broken files
  if (existsSync(join(HERE, ".cache", "build.sarif"))) {
    const t = await (await fetch(ROOT + "docs/manual/.cache/build.sarif")).text();
    await feed("file", "report.sarif", t);
    await pick("ESL004", 0);
    await shot("BuildSarif", "#drop", 70, ["#status .stat-row > div:nth-child(1)", "#status .hint", "#detail .hint"]);
  }
  await feed("file", "broken.sarif", '{\n  "version": "2.1.0",\n  "runs": [\n    {,\n');
  await shot("ErrJson", "#drop", 70, ["#load-error"]);
  await feed("file", "other-tool.sarif", JSON.stringify({ version: "2.1.0", runs: [{ tool: { driver: { name: "other" } }, results: [{ ruleId: "CA2000", message: { text: "x" } }] }] }));
  await shot("ErrNoEsl", "#drop", 70, ["#load-error"]);
  await click("#load-sample"); await sleep(600);

  await click("#open-help"); await sleep(300);
  await shot("Help");
  await js(`document.querySelector("#help form").requestSubmit()`); await sleep(200);

  // English, mobile dark
  await setLang("en");
  await pick("ESL001", 0);
  await shot("EnFindings", "#tabs", 70);
  await view(390, 844, "dark", true);
  await open(APP);
  await setLang("ko");
  await pick("ESL009", 0);
  await shot("MobileDark", "#detail", 70);
  await view(1280, 800, "dark");
  await open(APP);
  await pick("ESL007", 0);
  await shot("DarkFindings", "#tabs", 70);
  writeFileSync(join(OUT, "marks.json"), JSON.stringify(MARKS, null, 1));
} finally {
  try { ws?.close(); } catch { /* */ }
  proc.kill();
  await sleep(500);
  try { rmSync(profile, { recursive: true, force: true }); } catch { /* locked files stay */ }
}
