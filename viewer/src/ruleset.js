// Rule set versions, Shadow statistics and promotion. A version holds the full override map
// (rule id -> .editorconfig severity), so rollback is "append a copy of an older version": history is never lost.
export const DEFAULT_CRITERIA = { minTriaged: 10, maxFpRate: 0.1 };
export const SEVERITIES = ["none", "suggestion", "warning", "error"];

export function initialHistory(now = new Date().toISOString()) {
  return [{ v: 1, t: now, overrides: {}, note: "default" }];
}

export const current = (history) => history[history.length - 1];

/** shadow | enforce | off, from the rule's shipped stage and the current overrides. */
export function effectiveStage(rule, overrides) {
  const o = overrides?.[rule.id];
  if (o === "none") return "off";
  if (o === "warning" || o === "error") return "enforce";
  if (o === "suggestion") return "shadow";
  return rule.stage === "shadow" ? "shadow" : "enforce";
}

/** Triage of one rule's findings in the open report. verdicts: fingerprint -> "tp" | "fp". */
export function shadowStats(results, verdicts, ruleId) {
  const mine = results.filter((r) => r.ruleId === ruleId);
  let tp = 0, fp = 0;
  for (const r of mine) {
    const v = verdicts[r.fp];
    if (v === "tp") tp++;
    else if (v === "fp") fp++;
  }
  const triaged = tp + fp;
  return { total: mine.length, tp, fp, triaged, fpRate: triaged ? fp / triaged : null };
}

export function canPromote(stats, criteria = DEFAULT_CRITERIA) {
  return stats.triaged >= criteria.minTriaged && stats.fpRate !== null && stats.fpRate <= criteria.maxFpRate;
}

function append(history, overrides, note, now) {
  return [...history, { v: current(history).v + 1, t: now, overrides, note }];
}

export function setSeverity(history, ruleId, severity, note, now = new Date().toISOString()) {
  if (!SEVERITIES.includes(severity)) throw new Error("bad severity " + severity);
  return append(history, { ...current(history).overrides, [ruleId]: severity }, note, now);
}

export function rollback(history, toVersion, now = new Date().toISOString()) {
  const target = history.find((h) => h.v === toVersion);
  if (!target) throw new Error("no version " + toVersion);
  return append(history, { ...target.overrides }, `rollback:v${toVersion}`, now);
}

/** Changed keys between two override maps, for the history table. */
export function diff(a, b) {
  const keys = new Set([...Object.keys(a || {}), ...Object.keys(b || {})]);
  return [...keys].sort().filter((k) => (a || {})[k] !== (b || {})[k]).map((k) => ({ id: k, from: (a || {})[k] || null, to: (b || {})[k] || null }));
}

/** The same keys the NuGet analyzer and the CLI read. */
export function editorconfig(entry, date = entry.t.slice(0, 10)) {
  const lines = [`# equip-sw-lint rule set v${entry.v} (${date})`, "[*.cs]"];
  for (const id of Object.keys(entry.overrides).sort()) lines.push(`dotnet_diagnostic.${id}.severity = ${entry.overrides[id]}`);
  if (lines.length === 2) lines.push("# (defaults: enforce rules = warning, Shadow rules = suggestion)");
  return lines.join("\n") + "\n";
}
