// node --test viewer/test
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { parseJson, readSarif, compare, hash16, SarifError } from "../src/sarif.js";
import * as rs from "../src/ruleset.js";
import { STR } from "../src/i18n.js";

const here = (p) => new URL(p, import.meta.url);
const load = (name) => readSarif(parseJson(readFileSync(here(`../samples/${name}`), "utf8")));
const rules = JSON.parse(readFileSync(here("../rules.json"), "utf8")).rules;

test("CLI sample: 16 findings, relative paths, snippets, stable fingerprints", () => {
  const s = load("equipment.ko.sarif");
  assert.equal(s.tool, "equip-sw-lint");
  assert.equal(s.results.length, 16);
  assert.equal(s.absolute, false);
  assert.ok(s.results.every((r) => r.file.startsWith("Bad/") && r.snippet && r.context && /^[0-9a-f]{16}$/.test(r.fp)));
  assert.equal(new Set(s.results.map((r) => r.ruleId)).size, 11);
  assert.equal(new Set(s.results.map((r) => r.fp)).size, 16);
});

test("rule set v1 -> v2 on the same code: 6 added, 10 kept, 0 fixed", () => {
  const c = compare(load("equipment.en.sarif"), load("equipment-v1.en.sarif"));
  assert.equal(c.added.length, 6);
  assert.equal(c.kept.length, 10);
  assert.equal(c.fixed.length, 0);
  assert.deepEqual(c.perRule.ESL005, { before: 0, after: 2 });
  assert.deepEqual(c.perRule.ESL001, { before: 2, after: 2 });
});

test("dotnet build SARIF: absolute URIs become relative, other tools are ignored, ids are hashed", () => {
  const doc = {
    version: "2.1.0",
    runs: [{
      tool: { driver: { name: "Microsoft (R) Visual C# Compiler", rules: [{ id: "ESL011", defaultConfiguration: { level: "note" } }] } },
      results: [
        { ruleId: "CS0168", level: "warning", message: { text: "unused" }, locations: [] },
        { ruleId: "ESL011", message: { text: "Polling loop waits for 'x'" }, locations: [{ physicalLocation: { artifactLocation: { uri: "file:///C:/Users/someone/src/Tool/Motion/Axis.cs" }, region: { startLine: 40, startColumn: 9 } } }] },
        { ruleId: "ESL001", level: "warning", message: { text: "tick" }, locations: [{ physicalLocation: { artifactLocation: { uri: "file:///C:/Users/someone/src/Tool/Io.cs" }, region: { startLine: 3 } } }] },
      ],
    }],
  };
  const s = readSarif(doc);
  assert.equal(s.ignored, 1);
  assert.equal(s.absolute, true);
  assert.deepEqual(s.results.map((r) => r.file), ["Io.cs", "Motion/Axis.cs"]);
  assert.equal(s.results[1].level, "note"); // from the rule's default level
  assert.equal(s.results[1].snippet, null);
  assert.equal(s.results[0].fp, hash16("ESL001|C:/Users/someone/src/Tool/Io.cs|tick|0"));
});

test("broken input reports line and column", () => {
  assert.throws(() => parseJson('{\n  "version": "2.1.0",\n  "runs": [\n    {,\n'), (e) => e instanceof SarifError && e.line === 4 && e.col > 0);
  assert.throws(() => readSarif({ version: "2.1.0" }), (e) => e.message === "noRuns");
  assert.throws(() => readSarif({ version: "1.0.0", runs: [] }), (e) => e.message === "version");
});

test("Shadow triage, promotion criteria, rollback keeps history", () => {
  const s = load("equipment.en.sarif");
  const esl005 = s.results.filter((r) => r.ruleId === "ESL005");
  const verdicts = { [esl005[0].fp]: "tp", [esl005[1].fp]: "fp" };
  const st = rs.shadowStats(s.results, verdicts, "ESL005");
  assert.deepEqual(st, { total: 2, tp: 1, fp: 1, triaged: 2, fpRate: 0.5 });
  assert.equal(rs.canPromote(st), false);
  assert.equal(rs.canPromote({ triaged: 12, fpRate: 1 / 12 }), true);
  assert.equal(rs.canPromote({ triaged: 12, fpRate: 2 / 12 }), false);

  let h = rs.initialHistory("2026-11-22T00:00:00Z");
  const r10 = rules.find((r) => r.id === "ESL010");
  assert.equal(rs.effectiveStage(r10, rs.current(h).overrides), "shadow");
  h = rs.setSeverity(h, "ESL010", "warning", "promote", "2026-11-23T00:00:00Z");
  assert.equal(rs.effectiveStage(r10, rs.current(h).overrides), "enforce");
  assert.match(rs.editorconfig(rs.current(h)), /dotnet_diagnostic\.ESL010\.severity = warning/);
  h = rs.rollback(h, 1, "2026-11-24T00:00:00Z");
  assert.equal(h.length, 3);
  assert.equal(rs.effectiveStage(r10, rs.current(h).overrides), "shadow");
  assert.deepEqual(rs.diff(h[1].overrides, h[2].overrides), [{ id: "ESL010", from: "warning", to: null }]);
});

test("every UI string exists in Korean and English", () => {
  const ko = Object.keys(STR.ko).sort(), en = Object.keys(STR.en).sort();
  assert.deepEqual(ko, en);
  for (const k of ko) assert.ok(STR.ko[k] && STR.en[k], k);
});
