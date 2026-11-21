// SARIF 2.1.0 reading for the viewer. Accepts the CLI output (relative paths, snippets, fingerprints)
// and `dotnet build -p:ErrorLog=out.sarif%2Cversion=2.1` output (absolute file URIs, no snippets).
export const FP_KEY = "equipSwLint/v1";

export class SarifError extends Error {
  constructor(message, line = 0, col = 0) { super(message); this.line = line; this.col = col; }
}

/** Parse JSON with a line/column for the error, the way people find it in an editor. */
export function parseJson(text) {
  try {
    return JSON.parse(text);
  } catch (e) {
    const msg = String(e.message);
    let line = 0, col = 0;
    const pos = /position (\d+)/.exec(msg);
    const lc = /line (\d+) column (\d+)/.exec(msg);
    if (lc) { line = +lc[1]; col = +lc[2]; }
    else if (pos) {
      const before = text.slice(0, +pos[1]);
      line = before.split("\n").length;
      col = +pos[1] - before.lastIndexOf("\n");
    }
    throw new SarifError("json", line, col);
  }
}

/** FNV-1a 64-bit as 16 hex chars: stable id for results that carry no fingerprint. */
export function hash16(s) {
  let h = 0xcbf29ce484222325n;
  const p = 0x100000001b3n, m = 0xffffffffffffffffn;
  for (const b of new TextEncoder().encode(s)) h = ((h ^ BigInt(b)) * p) & m;
  return h.toString(16).padStart(16, "0");
}

function uriToPath(uri) {
  let p = String(uri || "");
  try { p = decodeURIComponent(p); } catch { /* keep raw */ }
  return p.replace(/^file:\/\/\/?/, "").replace(/\\/g, "/");
}

/**
 * Normalize a SARIF document. Only ESL rules are kept; other tools' and compiler results are counted in `ignored`.
 * @returns {{tool:string, version:string, results:object[], ignored:number, stats:object|null, absolute:boolean}}
 */
export function readSarif(doc) {
  if (!doc || typeof doc !== "object") throw new SarifError("notObject");
  if (!Array.isArray(doc.runs)) throw new SarifError("noRuns");
  if (doc.version && !String(doc.version).startsWith("2.")) throw new SarifError("version");
  const out = { tool: "", version: "", results: [], ignored: 0, stats: null, absolute: false };
  const seen = new Map();
  for (const run of doc.runs) {
    const driver = run?.tool?.driver || {};
    out.tool ||= driver.name || "";
    out.version ||= driver.semanticVersion || driver.version || "";
    out.stats ||= run?.invocations?.[0]?.properties || null;
    const levels = new Map((driver.rules || []).map((r) => [r.id, r.defaultConfiguration?.level]));
    for (const r of run.results || []) {
      const ruleId = r.ruleId || r.rule?.id || "";
      if (!/^ESL\d{3}$/.test(ruleId)) { out.ignored++; continue; }
      const loc = r.locations?.[0]?.physicalLocation || {};
      const art = loc.artifactLocation || {};
      const reg = loc.region || {};
      const ctx = loc.contextRegion;
      const file = uriToPath(art.uri);
      if (!art.uriBaseId && /^[a-z]:\/|^\//i.test(file)) out.absolute = true;
      const message = r.message?.text || "";
      const key = `${ruleId}|${file}|${message}`;
      const occ = seen.get(key) || 0;
      seen.set(key, occ + 1);
      out.results.push({
        ruleId,
        level: r.level || levels.get(ruleId) || "warning",
        message,
        file,
        line: reg.startLine || 0,
        col: reg.startColumn || 0,
        snippet: reg.snippet?.text ?? null,
        context: ctx?.snippet?.text != null ? { start: ctx.startLine || reg.startLine || 1, text: ctx.snippet.text } : null,
        fp: r.partialFingerprints?.[FP_KEY] || hash16(`${key}|${occ}`),
        baselineState: r.baselineState || null,
      });
    }
  }
  if (out.absolute) stripCommonDir(out.results);
  out.results.sort((a, b) => a.file.localeCompare(b.file) || a.line - b.line || a.ruleId.localeCompare(b.ruleId));
  return out;
}

/** dotnet build SARIF has absolute paths: show them relative to the deepest common folder. */
function stripCommonDir(results) {
  if (!results.length) return;
  let common = results[0].file.split("/").slice(0, -1);
  for (const r of results) {
    const parts = r.file.split("/").slice(0, -1);
    let i = 0;
    while (i < common.length && i < parts.length && common[i].toLowerCase() === parts[i].toLowerCase()) i++;
    common = common.slice(0, i);
  }
  const n = common.join("/").length;
  if (n) for (const r of results) r.file = r.file.slice(n + 1);
}

/** Same code or same rule set, before (baseline) vs after (current). */
export function compare(current, baseline) {
  const before = new Set(baseline.results.map((r) => r.fp));
  const after = new Set(current.results.map((r) => r.fp));
  const added = current.results.filter((r) => !before.has(r.fp));
  const kept = current.results.filter((r) => before.has(r.fp));
  const fixed = baseline.results.filter((r) => !after.has(r.fp));
  const perRule = {};
  for (const r of baseline.results) (perRule[r.ruleId] ||= { before: 0, after: 0 }).before++;
  for (const r of current.results) (perRule[r.ruleId] ||= { before: 0, after: 0 }).after++;
  return { added, kept, fixed, perRule };
}
