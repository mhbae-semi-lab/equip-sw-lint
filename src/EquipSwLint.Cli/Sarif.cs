using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EquipSwLint.Cli;

/// <summary>SARIF 2.1.0 writer/reader. Paths are relative to SRCROOT so reports carry no local folder or user name.</summary>
public static class Sarif
{
    public const string FingerprintKey = "equipSwLint/v1";
    public static string Version => typeof(Sarif).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static void Write(RunResult r, string path)
    {
        var ko = r.Lang == "ko";
        using var stream = File.Create(path);
        using var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        w.WriteStartObject();
        w.WriteString("$schema", "https://json.schemastore.org/sarif-2.1.0.json");
        w.WriteString("version", "2.1.0");
        w.WriteStartArray("runs");
        w.WriteStartObject();

        w.WriteStartObject("tool");
        w.WriteStartObject("driver");
        w.WriteString("name", "equip-sw-lint");
        w.WriteString("semanticVersion", Version);
        w.WriteString("informationUri", "https://github.com/mhbae-semi-lab/equip-sw-lint");
        w.WriteStartArray("rules");
        foreach (var rule in Rules.All)
        {
            w.WriteStartObject();
            w.WriteString("id", rule.Id);
            w.WriteStartObject("shortDescription"); w.WriteString("text", ko ? rule.TitleKo : rule.TitleEn); w.WriteEndObject();
            w.WriteStartObject("fullDescription"); w.WriteString("text", ko ? rule.WhyKo : rule.WhyEn); w.WriteEndObject();
            w.WriteStartObject("help"); w.WriteString("text", ko ? rule.FixKo : rule.FixEn); w.WriteEndObject();
            w.WriteString("helpUri", rule.Descriptor.HelpLinkUri);
            w.WriteStartObject("defaultConfiguration"); w.WriteString("level", rule.Stage == "shadow" ? "note" : "warning"); w.WriteEndObject();
            w.WriteStartObject("properties");
            w.WriteString("stage", rule.Stage);
            w.WriteString("category", rule.Category);
            w.WriteString("overlap", rule.Overlap);
            w.WriteString("repro", rule.Repro);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
        w.WriteEndObject();

        w.WriteStartArray("results");
        var index = Rules.All.Select((x, i) => (x.Id, i)).ToDictionary(x => x.Id, x => x.i);
        foreach (var f in r.Findings)
        {
            w.WriteStartObject();
            w.WriteString("ruleId", f.RuleId);
            if (index.TryGetValue(f.RuleId, out var ri)) w.WriteNumber("ruleIndex", ri);
            w.WriteString("level", f.Level);
            w.WriteStartObject("message"); w.WriteString("text", f.Message); w.WriteEndObject();
            w.WriteStartArray("locations");
            w.WriteStartObject();
            w.WriteStartObject("physicalLocation");
            w.WriteStartObject("artifactLocation"); w.WriteString("uri", r.Shown(f)); w.WriteString("uriBaseId", "SRCROOT"); w.WriteEndObject();
            w.WriteStartObject("region");
            w.WriteNumber("startLine", f.Line); w.WriteNumber("startColumn", f.Column);
            w.WriteNumber("endLine", f.EndLine); w.WriteNumber("endColumn", f.EndColumn);
            w.WriteStartObject("snippet"); w.WriteString("text", f.LineText); w.WriteEndObject();
            w.WriteEndObject();
            w.WriteStartObject("contextRegion");
            w.WriteNumber("startLine", f.ContextStart);
            w.WriteNumber("endLine", f.ContextStart + f.Context.Split('\n').Length - 1);
            w.WriteStartObject("snippet"); w.WriteString("text", f.Context); w.WriteEndObject();
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteStartObject("partialFingerprints"); w.WriteString(FingerprintKey, f.Fingerprint); w.WriteEndObject();
            if (f.BaselineState != null) w.WriteString("baselineState", f.BaselineState);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("invocations");
        w.WriteStartObject();
        w.WriteBoolean("executionSuccessful", r.AnalyzerErrors.Count == 0);
        w.WriteStartObject("properties");
        w.WriteNumber("files", r.Files);
        w.WriteNumber("lines", r.Lines);
        w.WriteNumber("parseMs", r.ParseMs);
        w.WriteNumber("analyzeMs", r.AnalyzeMs);
        w.WriteNumber("unresolvedTypes", r.UnresolvedTypes);
        w.WriteNumber("absentFromBaseline", r.AbsentFromBaseline);
        w.WriteString("lang", r.Lang);
        w.WriteEndObject();
        w.WriteEndObject();
        w.WriteEndArray();

        w.WriteEndObject();
        w.WriteEndArray();
        w.WriteEndObject();
    }

    /// <summary>Fingerprints of a previous run (ours, or any SARIF with our partialFingerprints key).</summary>
    public static HashSet<string> ReadFingerprints(string path)
    {
        if (!File.Exists(path)) throw new UsageException($"Baseline not found: {path}");
        var set = new HashSet<string>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            foreach (var run in doc.RootElement.GetProperty("runs").EnumerateArray())
            {
                if (!run.TryGetProperty("results", out var results)) continue;
                foreach (var res in results.EnumerateArray())
                    if (res.TryGetProperty("partialFingerprints", out var fp) && fp.TryGetProperty(FingerprintKey, out var v) && v.GetString() is string s)
                        set.Add(s);
            }
        }
        catch (Exception e) when (e is JsonException || e is KeyNotFoundException || e is InvalidOperationException)
        {
            throw new UsageException($"Baseline is not a SARIF file ({path}): {e.Message}");
        }
        return set;
    }
}
