import { t, setLang, getLang, applyStatic } from "./i18n.js";
import { parseJson, readSarif, compare, SarifError } from "./sarif.js";
import * as rs from "./ruleset.js";
import * as store from "./store.js";

const $ = (id) => document.getElementById(id);
const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);

const S = {
  rules: [], version: "",
  report: null, reportName: "", reportText: "",
  base: null, baseName: "", baseText: "",
  verdicts: {}, history: [], criteria: { ...rs.DEFAULT_CRITERIA },
  filters: { q: "", rule: "", level: "", state: "" },
  selected: null, tab: "findings",
};

const ruleOf = (id) => S.rules.find((r) => r.id === id);
const L = (o) => (o ? o[getLang()] ?? o.en : "");
const overrides = () => rs.current(S.history).overrides;
const stageOf = (id) => { const r = ruleOf(id); return r ? rs.effectiveStage(r, overrides()) : "enforce"; };
const baseSet = () => new Set(S.base?.results.map((r) => r.fp) || []);

// ---------- errors ----------
function toast(msg) {
  const el = $("toast");
  el.textContent = msg;
  el.hidden = false;
  clearTimeout(toast.h);
  toast.h = setTimeout(() => (el.hidden = true), 3500);
}
window.addEventListener("error", (e) => { store.logError(e.error || e.message); toast(t("errGeneric")); });
window.addEventListener("unhandledrejection", (e) => { store.logError(e.reason); toast(t("errGeneric")); });

// ---------- loading ----------
function parseReport(text) {
  const doc = parseJson(text);
  const rep = readSarif(doc);
  if (!rep.results.length) {
    const e = new SarifError("noEsl");
    e.ignored = rep.ignored;
    throw e;
  }
  return rep;
}

function showLoadError(e) {
  const el = $("load-error");
  if (!e) { el.hidden = true; return; }
  const key = { json: "errJson", notObject: "errNotObject", noRuns: "errNoRuns", version: "errVersion", noEsl: "errNoEsl" }[e.message];
  el.textContent = !key ? t("errRead", e.message) : key === "errJson" ? t(key, e.line, e.col) : key === "errNoEsl" ? t(key, e.ignored ?? 0) : t(key);
  el.hidden = false;
}

async function openReport(text, name, which = "report") {
  try {
    const rep = parseReport(text);
    showLoadError(null);
    if (which === "report") {
      Object.assign(S, { report: rep, reportName: name, reportText: text, selected: rep.results[0]?.fp ?? null });
      await store.put("kv", { name, text }, "report");
    } else {
      Object.assign(S, { base: rep, baseName: name, baseText: text });
      await store.put("kv", { name, text }, "baseline");
    }
    render();
    return true;
  } catch (e) {
    if (!(e instanceof SarifError)) store.logError(e);
    showLoadError(e);
    return false;
  }
}

async function readFile(file, which) {
  try { await openReport(await file.text(), file.name, which); }
  catch (e) { showLoadError(new Error(e.message)); }
}

const sampleUrl = (base) => `samples/equipment${base ? "-v1" : ""}.${getLang()}.sarif`;
async function loadSample(base = false) {
  const res = await fetch(sampleUrl(base));
  await openReport(await res.text(), sampleUrl(base).split("/").pop(), base ? "baseline" : "report");
}

// ---------- rendering ----------
function render() {
  renderStatus();
  renderFilters();
  renderFindings();
  renderDetail();
  renderCompare();
  renderShadow();
  renderHistory();
  renderRules();
  renderTabs();
  $("clear-base").hidden = !S.base;
}

function renderStatus() {
  const el = $("status");
  if (!S.report) { el.innerHTML = `<p class="hint">${esc(t("noReport"))}</p>`; return; }
  const st = S.report.stats || {};
  const files = new Set(S.report.results.map((r) => r.file)).size;
  const parts = [
    [t("statFindings"), S.report.results.length],
    [t("statFiles"), st.files ?? files],
    st.analyzeMs != null ? [t("statTime"), `${(st.parseMs || 0) + st.analyzeMs} ms`] : null,
    S.report.ignored ? [t("statIgnored"), S.report.ignored] : null,
    S.base ? [t("statBaseline"), S.baseName] : null,
  ].filter(Boolean);
  el.innerHTML = `<div class="stat-row"><div><small>${esc(S.report.tool)} ${esc(S.report.version)}</small><b class="fname">${esc(S.reportName)}</b></div>${
    parts.map(([k, v]) => `<div><small>${esc(k)}</small><b>${esc(v)}</b></div>`).join("")}</div>${
    S.report.absolute ? `<p class="hint">${esc(t("absoluteNote"))}</p>` : ""}`;
}

function option(value, label, cur) { return `<option value="${esc(value)}"${value === cur ? " selected" : ""}>${esc(label)}</option>`; }

function renderFilters() {
  const f = S.filters;
  $("f-q").value = f.q;
  const present = new Set(S.report?.results.map((r) => r.ruleId) || []);
  $("f-rule").innerHTML = option("", t("allRules"), f.rule) + S.rules.filter((r) => present.has(r.id)).map((r) => option(r.id, `${r.id} ${L(r.title)}`, f.rule)).join("");
  $("f-level").innerHTML = option("", t("allLevels"), f.level) + option("error", t("levelError"), f.level) + option("warning", t("levelWarning"), f.level) + option("note", t("levelNote"), f.level);
  $("f-state").innerHTML = option("", t("allStates"), f.state) + option("new", t("stateNew"), f.state) + option("kept", t("stateKept"), f.state);
  $("f-state").disabled = !S.base;
}

function filtered() {
  if (!S.report) return [];
  const f = S.filters, q = f.q.trim().toLowerCase(), kept = baseSet();
  return S.report.results.filter((r) =>
    (!f.rule || r.ruleId === f.rule) && (!f.level || r.level === f.level)
    && (!f.state || !S.base || (f.state === "new") !== kept.has(r.fp))
    && (!q || `${r.file} ${r.message} ${r.ruleId}`.toLowerCase().includes(q)));
}

function levelLabel(l) { return l === "error" ? t("levelError") : l === "note" ? t("levelNote") : t("levelWarning"); }

function renderFindings() {
  const list = filtered();
  const total = S.report?.results.length || 0;
  $("f-count").textContent = S.report ? t("shown", list.length, total) : "";
  const counts = {};
  for (const r of S.report?.results || []) counts[r.ruleId] = (counts[r.ruleId] || 0) + 1;
  $("rule-chips").innerHTML = Object.keys(counts).sort().map((id) =>
    `<button type="button" class="chip${S.filters.rule === id ? " on" : ""}" data-rule="${id}" aria-pressed="${S.filters.rule === id}">${id} <b>${counts[id]}</b>${stageOf(id) === "shadow" ? ` <i>${esc(t("stageShadow"))}</i>` : ""}</button>`).join("");
  const kept = baseSet();
  $("results").innerHTML = !S.report ? "" : !list.length ? `<li class="hint pad">${esc(t("noMatch"))}</li>` : list.map((r) => {
    const v = S.verdicts[r.fp]?.v;
    const state = S.base ? (kept.has(r.fp) ? `<span class="tag">${esc(t("badgeKept"))}</span>` : `<span class="tag new">${esc(t("badgeNew"))}</span>`) : "";
    return `<li><button type="button" class="res lv-${r.level}${r.fp === S.selected ? " on" : ""}" data-fp="${r.fp}" aria-current="${r.fp === S.selected}">
      <span class="res-head"><b>${r.ruleId}</b><span class="lv">${esc(levelLabel(r.level))}</span>${state}${v ? `<span class="tag v-${v}">${esc(v === "tp" ? t("triageTp") : t("triageFp"))}</span>` : ""}</span>
      <span class="res-file">${esc(r.file)}:${r.line}</span>
      <span class="res-msg">${esc(r.message)}</span></button></li>`;
  }).join("");
  // keep the selected result visible inside the scrolling list
  const box = $("results"), on = box.querySelector(".res.on")?.parentElement;
  if (on && (on.offsetTop < box.scrollTop || on.offsetTop + on.offsetHeight > box.scrollTop + box.clientHeight)) box.scrollTop = on.offsetTop - 8;
}

function snippetHtml(r) {
  if (!r.context && r.snippet == null) return `<p class="hint">${esc(t("noSnippet"))}</p>`;
  const ctx = r.context || { start: r.line, text: r.snippet };
  return `<pre class="code" aria-label="${esc(t("evidence"))}">${ctx.text.split("\n").map((line, i) => {
    const no = ctx.start + i;
    return `<span class="ln${no === r.line ? " hit" : ""}"><i>${no}</i>${esc(line)}</span>`;
  }).join("")}</pre>`;
}

function ruleDoc(rule, compact = false) {
  return `<h4>${esc(t("whyStops"))}</h4><p>${esc(L(rule.why))}</p>
    <h4>${esc(t("fix"))}</h4><p>${esc(L(rule.fix))}</p>
    <h4>${esc(t("overlap"))}</h4><p class="${rule.overlap ? "" : "hint"}">${esc(rule.overlap || t("overlapNone"))}</p>
    ${compact ? "" : `<h4>${esc(t("repro"))}</h4><p><code>${esc(t("reproCmd", rule.repro))}</code></p>`}`;
}

function renderDetail() {
  const el = $("detail");
  const r = S.report?.results.find((x) => x.fp === S.selected);
  if (!r) { el.innerHTML = S.report ? `<p class="hint">${esc(t("selectHint"))}</p>` : ""; return; }
  const rule = ruleOf(r.ruleId);
  const v = S.verdicts[r.fp]?.v;
  const stage = stageOf(r.ruleId);
  el.innerHTML = `<div class="d-head"><span class="rid">${r.ruleId}</span><span class="badge st-${stage}">${esc(stage === "shadow" ? t("stageShadow") : stage === "off" ? t("stageOff") : t("stageEnforce"))}</span>
      <h3>${esc(rule ? L(rule.title) : r.ruleId)}</h3></div>
    <p class="d-file">${esc(r.file)}:${r.line}${r.col ? ":" + r.col : ""}</p>
    <p class="d-msg">${esc(r.message)}</p>
    ${snippetHtml(r)}
    <div class="triage" role="group" aria-label="${esc(t("triage"))}">
      <span>${esc(t("triage"))}</span>
      <button type="button" class="btn sm ${v === "tp" ? "" : "ghost"}" data-verdict="tp" aria-pressed="${v === "tp"}">${esc(t("triageTp"))} <kbd>T</kbd></button>
      <button type="button" class="btn sm ${v === "fp" ? "" : "ghost"}" data-verdict="fp" aria-pressed="${v === "fp"}">${esc(t("triageFp"))} <kbd>F</kbd></button>
      ${v ? `<button type="button" class="link" data-verdict="">${esc(t("triageClear"))}</button>` : ""}
    </div>
    ${rule ? ruleDoc(rule) : ""}`;
}

function renderCompare() {
  const el = $("p-compare");
  if (!S.report || !S.base) { el.innerHTML = `<p class="hint">${esc(t("cmpNeed"))}</p>`; return; }
  const c = compare(S.report, S.base);
  const rows = Object.keys(c.perRule).sort().map((id) => {
    const { before, after } = c.perRule[id];
    const d = after - before;
    return `<tr class="${d ? "changed" : ""}"><td class="mono">${id}</td><td>${esc(L(ruleOf(id)?.title))}</td><td class="num">${before}</td><td class="num">${after}</td><td class="num"><b>${d > 0 ? "+" + d : d}</b></td></tr>`;
  }).join("");
  const list = (items, cls) => items.length ? `<ul class="plain">${items.map((r) => `<li class="${cls}"><span class="mono">${r.ruleId}</span> ${esc(r.file)}:${r.line} <span class="hint">${esc(r.message.slice(0, 110))}</span></li>`).join("")}</ul>` : "";
  el.innerHTML = `<p class="hint">${esc(S.baseName)} → ${esc(S.reportName)}</p>
    <div class="stat-row"><div><small>${esc(t("cmpAdded"))}</small><b class="c-alarm">${c.added.length}</b></div><div><small>${esc(t("cmpKept"))}</small><b>${c.kept.length}</b></div><div><small>${esc(t("cmpFixed"))}</small><b class="c-ok">${c.fixed.length}</b></div></div>
    <div class="tbl-wrap"><table class="t"><thead><tr><th>${esc(t("cmpRule"))}</th><th></th><th>${esc(t("cmpBefore"))}</th><th>${esc(t("cmpAfter"))}</th><th>${esc(t("cmpChange"))}</th></tr></thead><tbody>${rows}</tbody></table></div>
    ${c.added.length ? `<h3>${esc(t("cmpAdded"))}</h3>${list(c.added, "added")}` : ""}
    ${c.fixed.length ? `<h3>${esc(t("cmpFixed"))}</h3>${list(c.fixed, "fixed")}` : ""}`;
}

function renderShadow() {
  const el = $("p-shadow");
  const shadowRules = S.rules.filter((r) => rs.effectiveStage(r, overrides()) === "shadow");
  const head = `<p>${esc(t("shadowIntro"))}</p><p class="hint">${esc(t("criteria", S.criteria.minTriaged, Math.round(S.criteria.maxFpRate * 100)))}</p>`;
  if (!shadowRules.length) { el.innerHTML = head + `<p class="hint">${esc(t("shadowNone"))}</p>`; return; }
  const results = S.report?.results || [];
  const verdicts = Object.fromEntries(Object.entries(S.verdicts).map(([k, v]) => [k, v.v]));
  el.innerHTML = head + `<div class="cards">${shadowRules.map((rule) => {
    const st = rs.shadowStats(results, verdicts, rule.id);
    const ok = rs.canPromote(st, S.criteria);
    const need = Math.max(0, S.criteria.minTriaged - st.triaged);
    const status = ok ? t("shReady") : need ? t("shNotEnough", need) : t("shTooMany");
    const pct = Math.min(100, Math.round((st.triaged / S.criteria.minTriaged) * 100));
    const open = results.filter((r) => r.ruleId === rule.id && !verdicts[r.fp]);
    return `<article class="card">
      <h3><span class="rid">${rule.id}</span> ${esc(L(rule.title))}</h3>
      <div class="stat-row tight"><div><small>${esc(t("shTotal"))}</small><b>${st.total}</b></div><div><small>${esc(t("shTriaged"))}</small><b>${st.triaged} <i class="hint">(${st.tp} / ${st.fp})</i></b></div><div><small>${esc(t("shFpRate"))}</small><b>${st.fpRate == null ? "–" : Math.round(st.fpRate * 100) + "%"}</b></div></div>
      <div class="meter" role="progressbar" aria-valuenow="${st.triaged}" aria-valuemin="0" aria-valuemax="${S.criteria.minTriaged}" aria-label="${esc(t("shTriaged"))}"><span style="width:${pct}%"></span></div>
      <p class="${ok ? "ok-t" : "hint"}">${esc(status)}</p>
      <div class="row"><button type="button" class="btn sm" data-promote="${rule.id}" ${ok ? "" : "disabled"}>${esc(t("promote"))}</button>
        <button type="button" class="btn ghost sm" data-disable="${rule.id}">${esc(t("disable"))}</button></div>
      ${open.length ? `<details><summary>${esc(t("untriaged"))} (${open.length})</summary><ul class="plain">${open.map((r) =>
        `<li><button type="button" class="link" data-goto="${r.fp}">${esc(r.file)}:${r.line}</button></li>`).join("")}</ul></details>` : ""}
    </article>`;
  }).join("")}</div>`;
}

const pad = (n) => String(n).padStart(2, "0");
function localTime(iso) {
  const d = new Date(iso);
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

function noteText(note) {
  if (note === "default") return t("noteDefault");
  if (note === "promote") return t("notePromote");
  if (note === "demote") return t("noteDemote");
  if (note === "disable") return t("noteDisable");
  const m = /^rollback:v(\d+)$/.exec(note || "");
  return m ? t("noteRollback", m[1]) : note || "";
}

function renderHistory() {
  const el = $("p-history");
  const cur = rs.current(S.history);
  const rows = [...S.history].reverse().map((h, i, arr) => {
    const prev = S.history.find((x) => x.v === h.v - 1);
    const ch = prev ? rs.diff(prev.overrides, h.overrides).map((d) => `${d.id}: ${d.from || "default"} → ${d.to || "default"}`).join(", ") : "";
    return `<tr class="${h.v === cur.v ? "changed" : ""}"><td class="mono">v${h.v}</td><td class="nw">${esc(localTime(h.t))}</td><td class="mono">${esc(ch)}</td><td>${esc(noteText(h.note))}</td>
      <td>${h.v === cur.v ? "" : `<button type="button" class="rowbtn" data-rollback="${h.v}">${esc(t("rollback"))}</button>`}</td></tr>`;
  }).join("");
  const enforced = S.rules.filter((r) => r.stage === "shadow" && rs.effectiveStage(r, cur.overrides) === "enforce");
  el.innerHTML = `<p>${esc(t("histIntro"))}</p>
    <h3>${esc(t("histCurrent"))} v${cur.v}</h3>
    <pre class="cmd" id="ec">${esc(rs.editorconfig(cur, localTime(cur.t).slice(0, 10)))}</pre>
    <div class="row"><button type="button" class="btn ghost sm" id="copy-ec">${esc(t("copy"))}</button><button type="button" class="btn ghost sm" id="dl-ec">${esc(t("downloadEc"))}</button></div>
    ${enforced.map((r) => `<p class="hint">${r.id}: <button type="button" class="link" data-demote="${r.id}">${esc(t("demote"))}</button></p>`).join("")}
    <div class="tbl-wrap"><table class="t"><thead><tr><th>${esc(t("histVersion"))}</th><th>${esc(t("histDate"))}</th><th>${esc(t("histChange"))}</th><th>${esc(t("histNote"))}</th><th></th></tr></thead><tbody>${rows}</tbody></table></div>`;
}

function renderRules() {
  $("p-rules").innerHTML = `<p>${esc(t("rulesIntro"))}</p><div class="cards">${S.rules.map((r) => {
    const stage = rs.effectiveStage(r, overrides());
    return `<article class="card" id="rule-${r.id}" tabindex="-1"><h3><span class="rid">${r.id}</span> ${esc(L(r.title))} <span class="badge st-${stage}">${esc(stage === "shadow" ? t("stageShadow") : stage === "off" ? t("stageOff") : t("stageEnforce"))}</span></h3>${ruleDoc(r)}</article>`;
  }).join("")}</div>`;
}

function renderTabs() {
  for (const b of document.querySelectorAll("[role=tab]")) {
    const on = b.dataset.tab === S.tab;
    b.setAttribute("aria-selected", on);
    b.tabIndex = on ? 0 : -1;
    $(b.getAttribute("aria-controls")).hidden = !on;
  }
}

// ---------- actions ----------
async function setVerdict(fp, v) {
  const r = S.report?.results.find((x) => x.fp === fp);
  if (!r) return;
  if (v) { S.verdicts[fp] = { v, ruleId: r.ruleId, t: new Date().toISOString() }; await store.put("triage", S.verdicts[fp], fp); }
  else { delete S.verdicts[fp]; await store.del("triage", fp); }
  renderFindings(); renderDetail(); renderShadow();
}

async function saveHistory(h, msg) {
  S.history = h;
  await store.put("kv", h, "history");
  render();
  if (msg) toast(msg);
}

function select(fp, focusList = false) {
  S.selected = fp;
  renderFindings(); renderDetail();
  if (focusList) document.querySelector(`.res[data-fp="${fp}"]`)?.focus();
}

function download(name, text, type = "application/json") {
  const a = document.createElement("a");
  a.href = URL.createObjectURL(new Blob([text], { type }));
  a.download = name;
  a.click();
  setTimeout(() => URL.revokeObjectURL(a.href), 1000);
}

function setTab(tab, focus = false) {
  S.tab = tab;
  store.put("kv", tab, "tab");
  renderTabs();
  if (focus) $(`t-${tab}`).focus();
}

function wire() {
  $("lang").addEventListener("change", async (e) => {
    setLang(e.target.value);
    await store.put("kv", getLang(), "lang");
    applyStatic();
    // the example report exists in both languages
    if (/^equipment(-v1)?\.(ko|en)\.sarif$/.test(S.reportName)) await loadSample(false);
    if (/^equipment(-v1)?\.(ko|en)\.sarif$/.test(S.baseName)) await loadSample(true);
    render();
  });
  $("theme").addEventListener("click", async () => {
    const dark = document.documentElement.dataset.theme === "dark" || (!document.documentElement.dataset.theme && matchMedia("(prefers-color-scheme: dark)").matches);
    document.documentElement.dataset.theme = dark ? "light" : "dark";
    await store.put("kv", document.documentElement.dataset.theme, "theme");
  });
  $("open-help").addEventListener("click", () => $("help").showModal());
  $("open-settings").addEventListener("click", openSettings);
  $("dismiss-first").addEventListener("click", async () => { $("first-run").hidden = true; await store.put("kv", true, "firstRunDone"); });
  $("load-sample").addEventListener("click", () => loadSample(false));
  $("load-sample-base").addEventListener("click", () => loadSample(true));
  $("clear-base").addEventListener("click", async () => { Object.assign(S, { base: null, baseName: "", baseText: "" }); S.filters.state = ""; await store.del("kv", "baseline"); render(); });
  $("download-sample").addEventListener("click", async () => download(sampleUrl(false).split("/").pop(), await (await fetch(sampleUrl(false))).text()));
  $("file").addEventListener("change", (e) => { if (e.target.files[0]) readFile(e.target.files[0], "report"); e.target.value = ""; });
  $("base-file").addEventListener("change", (e) => { if (e.target.files[0]) readFile(e.target.files[0], "baseline"); e.target.value = ""; });

  // drag and drop anywhere
  let depth = 0;
  document.addEventListener("dragenter", (e) => { e.preventDefault(); depth++; document.body.classList.add("dragging"); });
  document.addEventListener("dragleave", () => { if (--depth <= 0) { depth = 0; document.body.classList.remove("dragging"); } });
  document.addEventListener("dragover", (e) => e.preventDefault());
  document.addEventListener("drop", (e) => {
    e.preventDefault(); depth = 0; document.body.classList.remove("dragging");
    const f = e.dataTransfer?.files?.[0];
    if (f) readFile(f, "report");
  });

  // filters
  const saveFilters = () => store.put("kv", S.filters, "filters");
  $("f-q").addEventListener("input", (e) => { S.filters.q = e.target.value; renderFindings(); saveFilters(); });
  for (const [id, key] of [["f-rule", "rule"], ["f-level", "level"], ["f-state", "state"]])
    $(id).addEventListener("change", (e) => { S.filters[key] = e.target.value; renderFilters(); renderFindings(); saveFilters(); });
  $("rule-chips").addEventListener("click", (e) => {
    const b = e.target.closest("[data-rule]");
    if (!b) return;
    S.filters.rule = S.filters.rule === b.dataset.rule ? "" : b.dataset.rule;
    renderFilters(); renderFindings(); saveFilters();
  });

  // results list: click + arrow keys
  $("results").addEventListener("click", (e) => { const b = e.target.closest("[data-fp]"); if (b) select(b.dataset.fp); });
  $("results").addEventListener("keydown", (e) => {
    if (e.key !== "ArrowDown" && e.key !== "ArrowUp") return;
    const list = filtered();
    const i = list.findIndex((r) => r.fp === S.selected);
    const next = list[Math.max(0, Math.min(list.length - 1, i + (e.key === "ArrowDown" ? 1 : -1)))];
    if (next) { e.preventDefault(); select(next.fp, true); }
  });
  document.addEventListener("keydown", (e) => {
    if (S.tab !== "findings" || !S.selected || e.ctrlKey || e.metaKey || e.altKey) return;
    if (/^(INPUT|SELECT|TEXTAREA)$/.test(document.activeElement?.tagName)) return;
    if (e.key === "t" || e.key === "T") setVerdict(S.selected, "tp");
    else if (e.key === "f" || e.key === "F") setVerdict(S.selected, "fp");
  });
  $("detail").addEventListener("click", (e) => { const b = e.target.closest("[data-verdict]"); if (b) setVerdict(S.selected, b.dataset.verdict); });

  // tabs: click + arrows
  $("tabs").addEventListener("click", (e) => { const b = e.target.closest("[role=tab]"); if (b) setTab(b.dataset.tab); });
  $("tabs").addEventListener("keydown", (e) => {
    if (e.key !== "ArrowRight" && e.key !== "ArrowLeft") return;
    const tabs = [...document.querySelectorAll("[role=tab]")].map((b) => b.dataset.tab);
    const i = tabs.indexOf(S.tab);
    setTab(tabs[(i + (e.key === "ArrowRight" ? 1 : tabs.length - 1)) % tabs.length], true);
  });

  // shadow / history actions
  document.addEventListener("click", async (e) => {
    const p = e.target.closest("[data-promote]");
    if (p) return saveHistory(rs.setSeverity(S.history, p.dataset.promote, "warning", "promote"), t("promoted", p.dataset.promote, rs.current(S.history).v + 1));
    const d = e.target.closest("[data-disable]");
    if (d) return saveHistory(rs.setSeverity(S.history, d.dataset.disable, "none", "disable"));
    const dm = e.target.closest("[data-demote]");
    if (dm) return saveHistory(rs.setSeverity(S.history, dm.dataset.demote, "suggestion", "demote"));
    const rb = e.target.closest("[data-rollback]");
    if (rb) return saveHistory(rs.rollback(S.history, +rb.dataset.rollback), t("rolledBack", rb.dataset.rollback, rs.current(S.history).v + 1));
    const g = e.target.closest("[data-goto]");
    if (g) { S.filters = { q: "", rule: "", level: "", state: "" }; setTab("findings"); renderFilters(); select(g.dataset.goto, true); return; }
    if (e.target.id === "copy-ec") { await navigator.clipboard?.writeText($("ec").textContent).catch(() => {}); toast(t("copied")); }
    if (e.target.id === "dl-ec") download(".editorconfig", $("ec").textContent, "text/plain");
  });

  // settings
  $("c-min").addEventListener("change", saveCriteria);
  $("c-fp").addEventListener("change", saveCriteria);
  $("export-backup").addEventListener("click", async () => download(`equip-sw-lint-backup-${new Date().toISOString().slice(0, 10)}.json`, JSON.stringify(await store.exportBackup(), null, 2)));
  $("import-backup").addEventListener("change", async (e) => {
    const f = e.target.files[0];
    e.target.value = "";
    if (!f) return;
    try { await store.importBackup(JSON.parse(await f.text())); await restore(); toast(t("imported")); }
    catch { toast(t("badBackup")); }
  });
  $("clear-all").addEventListener("click", async () => {
    if (!confirm(t("clearConfirm"))) return;
    await store.clearAll();
    location.hash = "";
    location.reload();
  });

  window.addEventListener("hashchange", routeHash);
}

async function saveCriteria() {
  const min = Math.max(1, Math.min(1000, Math.round(+$("c-min").value || rs.DEFAULT_CRITERIA.minTriaged)));
  const fp = Math.max(0, Math.min(100, +$("c-fp").value || 0)) / 100;
  S.criteria = { minTriaged: min, maxFpRate: fp };
  await store.put("kv", S.criteria, "criteria");
  renderShadow();
}

async function openSettings() {
  $("c-min").value = S.criteria.minTriaged;
  $("c-fp").value = Math.round(S.criteria.maxFpRate * 100);
  const errs = await store.entries("errors");
  $("err-log").innerHTML = errs.length ? `<ul class="plain">${errs.slice(-20).reverse().map(([, e]) => `<li><span class="mono">${esc(e.t)}</span> ${esc(e.msg)}</li>`).join("")}</ul>` : `<p class="hint">${esc(t("errLogEmpty"))}</p>`;
  $("settings").showModal();
}

function routeHash() {
  const m = /^#rule\/(ESL\d{3})$/.exec(location.hash);
  if (!m) return;
  setTab("rules");
  const card = $(`rule-${m[1]}`);
  card?.scrollIntoView({ block: "start" });
  card?.focus();
}

async function restore() {
  setLang((await store.get("kv", "lang")) || (navigator.language?.startsWith("ko") ? "ko" : "en"));
  $("lang").value = getLang();
  const theme = await store.get("kv", "theme");
  if (theme) document.documentElement.dataset.theme = theme;
  S.criteria = (await store.get("kv", "criteria")) || { ...rs.DEFAULT_CRITERIA };
  S.history = (await store.get("kv", "history")) || rs.initialHistory();
  S.filters = { ...S.filters, ...((await store.get("kv", "filters")) || {}) };
  S.tab = (await store.get("kv", "tab")) || "findings";
  S.verdicts = Object.fromEntries(await store.entries("triage"));
  applyStatic();
  const rep = await store.get("kv", "report");
  const base = await store.get("kv", "baseline");
  if (rep) { try { Object.assign(S, { report: parseReport(rep.text), reportName: rep.name, reportText: rep.text }); S.selected ??= S.report.results[0]?.fp; } catch { /* stale */ } }
  if (base) { try { Object.assign(S, { base: parseReport(base.text), baseName: base.name, baseText: base.text }); } catch { /* stale */ } }
  $("first-run").hidden = !!(await store.get("kv", "firstRunDone"));
  $("storage-note").textContent = (await store.persistent()) ? "" : t("memOnly");
  render();
}

async function main() {
  const meta = await (await fetch("rules.json")).json();
  S.rules = meta.rules;
  S.version = meta.version;
  $("ver").textContent = meta.version;
  wire();
  await restore();
  routeHash();
  if ("serviceWorker" in navigator && location.protocol !== "file:")
    navigator.serviceWorker.register("sw.js").catch((e) => store.logError(e));
}

main();
