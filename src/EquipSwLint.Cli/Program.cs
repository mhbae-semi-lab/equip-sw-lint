using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace EquipSwLint.Cli;

public static class Program
{
    private const string Usage = """
        equip-sw-lint <folder|file.cs|project.csproj> [options]
        equip-sw-lint rules [--json] [--lang ko|en]

          --sarif <file>      write SARIF 2.1.0 (open it in the viewer or upload to code scanning)
          --html <file>       write a single-file HTML report
          --baseline <sarif>  results already in that SARIF are 'unchanged'; only new ones fail the gate
          --fail-on <level>   warning (default) | error | none   (Shadow rules report as note and never fail)
          --ref <folder>      extra assemblies to resolve types (vendor SDK DLLs); repeatable
          --exclude <name>    skip folders with this name (tests, vendored code); repeatable
          --lang <ko|en>      message language (default: OS UI language)

        exit code: 0 ok, 1 new findings at or above --fail-on, 2 usage or input error
        """;

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            return await RunAsync(args, Console.Out);
        }
        catch (UsageException e)
        {
            Console.Error.WriteLine(e.Message);
            return 2;
        }
    }

    public static async Task<int> RunAsync(string[] args, TextWriter output)
    {
        var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko" ? "ko" : "en";
        string? path = null, sarif = null, html = null, baseline = null;
        var failOn = "warning";
        var refs = new List<string>();
        var exclude = new List<string>();
        var json = false;
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new UsageException($"{args[i]} needs a value\n\n{Usage}");
            switch (args[i])
            {
                case "--sarif": sarif = Next(); break;
                case "--html": html = Next(); break;
                case "--baseline": baseline = Next(); break;
                case "--fail-on": failOn = Next().ToLowerInvariant(); break;
                case "--ref": refs.Add(Next()); break;
                case "--exclude": exclude.Add(Next()); break;
                case "--lang": lang = Next() == "ko" ? "ko" : "en"; break;
                case "--json": json = true; break;
                case "-h": case "--help": output.WriteLine(Usage); return 0;
                case "--version": output.WriteLine(Sarif.Version); return 0;
                default:
                    if (args[i].StartsWith("--")) throw new UsageException($"Unknown option {args[i]}\n\n{Usage}");
                    if (path != null) throw new UsageException($"Only one path is allowed\n\n{Usage}");
                    path = args[i];
                    break;
            }
        }
        if (failOn != "warning" && failOn != "error" && failOn != "none") throw new UsageException($"--fail-on must be warning, error or none\n\n{Usage}");
        if (path == "rules")
        {
            output.WriteLine(json ? RulesJson() : string.Join("\n", Rules.All.Select(r => $"{r.Id}  {(r.Stage == "shadow" ? "shadow " : "enforce")}  {(lang == "ko" ? r.TitleKo : r.TitleEn)}")));
            return 0;
        }
        if (path == null) throw new UsageException(Usage);

        var ko = lang == "ko";
        var r = await Engine.RunAsync(new RunOptions { Path = path, RefDirs = refs, BaselineSarif = baseline, Lang = lang, Exclude = exclude });
        foreach (var f in r.Findings)
            output.WriteLine($"{f.File}({f.Line},{f.Column}): {f.Level} {f.RuleId}: {f.Message}{(f.BaselineState == "unchanged" ? (ko ? " [기준선]" : " [baseline]") : "")}");
        output.WriteLine();
        output.WriteLine(ko
            ? $"파일 {r.Files}개 · {r.Lines:N0}줄 · 분석 {r.ParseMs + r.AnalyzeMs} ms · 결과 {r.Findings.Count}개"
            : $"{r.Files} files · {r.Lines:N0} lines · {r.ParseMs + r.AnalyzeMs} ms · {r.Findings.Count} findings");
        foreach (var g in r.Findings.GroupBy(f => f.RuleId).OrderBy(g => g.Key))
            output.WriteLine($"  {g.Key}  {g.Count()}");
        if (r.UnresolvedTypes > 0)
            output.WriteLine(ko ? $"형식 참조 {r.UnresolvedTypes}개를 찾지 못함 (벤더 SDK DLL 은 --ref 로)" : $"{r.UnresolvedTypes} unresolved type references (pass vendor SDK DLLs with --ref)");
        foreach (var e in r.AnalyzerErrors) output.WriteLine("analyzer error: " + e);
        if (sarif != null) Sarif.Write(r, sarif);
        if (html != null) Html.Write(r, html);

        var gate = r.Findings.Where(f => f.BaselineState != "unchanged").Count(f =>
            failOn == "warning" ? f.Level is "warning" or "error" : failOn == "error" && f.Level == "error");
        if (baseline != null)
            output.WriteLine(ko ? $"기준선 대비 새 결과 {r.Findings.Count(f => f.BaselineState == "new")}개, 해결 {r.AbsentFromBaseline}개"
                                : $"vs baseline: {r.Findings.Count(f => f.BaselineState == "new")} new, {r.AbsentFromBaseline} fixed");
        if (gate > 0) output.WriteLine(ko ? $"게이트 실패: --fail-on {failOn} 이상 새 결과 {gate}개" : $"Gate failed: {gate} new findings at --fail-on {failOn}");
        return r.AnalyzerErrors.Count > 0 ? 2 : gate > 0 ? 1 : 0;
    }

    /// <summary>Rule metadata for the viewer (viewer/rules.json is generated from this and checked by a test).</summary>
    public static string RulesJson()
    {
        var list = Rules.All.Select(r => new
        {
            id = r.Id, stage = r.Stage, category = r.Category, overlap = r.Overlap, repro = r.Repro,
            title = new { en = r.TitleEn, ko = r.TitleKo },
            why = new { en = r.WhyEn, ko = r.WhyKo },
            fix = new { en = r.FixEn, ko = r.FixKo },
        });
        return JsonSerializer.Serialize(new { version = Sarif.Version, rules = list },
            new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}
