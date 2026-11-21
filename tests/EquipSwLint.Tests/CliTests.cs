using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EquipSwLint.Cli;
using Xunit;

namespace EquipSwLint.Tests;

public class CliTests
{
    internal static string RepoRoot
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "EquipSwLint.sln"))) d = d.Parent;
            return d?.FullName ?? throw new InvalidOperationException("repo root not found");
        }
    }

    private static string Samples => Path.Combine(RepoRoot, "samples", "Equipment");

    /// <summary>Completion metric: every planted bug in samples/Equipment/Bad is found, nothing in Good is flagged.</summary>
    [Fact]
    public async Task Samples_AllPlantedBugsFound_NoFalsePositivesOnFixedCode()
    {
        var expected = new HashSet<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(Samples, "Bad"), "*.cs"))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var m = Regex.Match(lines[i], @"// expect: (.+)$");
                if (!m.Success) continue;
                foreach (var id in m.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries))
                    expected.Add($"Bad/{Path.GetFileName(file)}:{i + 1}:{id}");
            }
        }
        var r = await Engine.RunAsync(new RunOptions { Path = Samples });
        var actual = r.Findings.Select(f => $"{f.File}:{f.Line}:{f.RuleId}").ToHashSet();

        Assert.Empty(r.AnalyzerErrors);
        Assert.Equal(0, r.UnresolvedTypes);
        Assert.Equal(16, expected.Count);
        Assert.Empty(expected.Except(actual));                         // recall 100%
        Assert.DoesNotContain(actual, a => a.StartsWith("Good/"));        // no findings on fixed code
        Assert.Empty(actual.Except(expected));                         // nothing unplanned
        Assert.Equal(Rules.All.Select(x => x.Id).OrderBy(x => x), expected.Select(e => e.Split(':')[2]).Distinct().OrderBy(x => x)); // every rule demonstrated
    }

    [Fact]
    public async Task Baseline_OnlyNewFindingsFailTheGate()
    {
        var dir = Directory.CreateTempSubdirectory("esl-cli-").FullName;
        try
        {
            File.Copy(Path.Combine(Samples, "Bad", "StageController.cs"), Path.Combine(dir, "StageController.cs"));
            var sarif = Path.Combine(dir, "base.sarif");
            Assert.Equal(1, await Run(dir, "--sarif", sarif));          // ESL004 warning fails the gate
            Assert.Equal(0, await Run(dir, "--baseline", sarif));        // same code against its own baseline

            // insert lines above: findings move but stay 'unchanged'
            var src = Path.Combine(dir, "StageController.cs");
            File.WriteAllText(src, "// header\n// header\n" + File.ReadAllText(src));
            Assert.Equal(0, await Run(dir, "--baseline", sarif));

            // a new unguarded read is new
            File.WriteAllText(Path.Combine(dir, "Extra.cs"), "class Extra { System.IO.Ports.SerialPort _p = new System.IO.Ports.SerialPort(\"COM9\"); string R() => _p.ReadLine(); }");
            var outp = new StringWriter();
            Assert.Equal(1, await Program.RunAsync(new[] { dir, "--baseline", sarif, "--lang", "en" }, outp));
            Assert.Contains("1 new", outp.ToString());
            Assert.Equal(0, await Run(dir, "--baseline", sarif, "--fail-on", "error"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Sarif_IsRelativeAndValid_HtmlIsWritten_KoreanMessages()
    {
        var dir = Directory.CreateTempSubdirectory("esl-out-").FullName;
        try
        {
            var sarif = Path.Combine(dir, "r.sarif");
            var html = Path.Combine(dir, "r.html");
            await Program.RunAsync(new[] { Samples, "--sarif", sarif, "--html", html, "--lang", "ko", "--fail-on", "none" }, TextWriter.Null);
            var text = File.ReadAllText(sarif);
            Assert.DoesNotContain(RepoRoot.Replace('\\', '/'), text.Replace("\\\\", "/"));
            Assert.DoesNotContain("file:///", text);
            using var doc = JsonDocument.Parse(text);
            var run = doc.RootElement.GetProperty("runs")[0];
            Assert.Equal(11, run.GetProperty("tool").GetProperty("driver").GetProperty("rules").GetArrayLength());
            var results = run.GetProperty("results");
            Assert.Equal(16, results.GetArrayLength());
            Assert.All(results.EnumerateArray(), r => Assert.Equal(16, r.GetProperty("partialFingerprints").GetProperty(Sarif.FingerprintKey).GetString()!.Length));
            Assert.Contains("24.9일", text);
            var page = File.ReadAllText(html);
            Assert.Contains("장비에서 어떻게 멈추나", page);
            Assert.Contains("<mark>", page);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task EditorConfig_SeverityPromotesOrDisablesRules()
    {
        var dir = Directory.CreateTempSubdirectory("esl-cfg-").FullName;
        try
        {
            File.Copy(Path.Combine(Samples, "Bad", "StageController.cs"), Path.Combine(dir, "StageController.cs"));
            // Shadow ESL011 promoted to error, ESL004 turned off
            File.WriteAllText(Path.Combine(dir, ".editorconfig"), "root = true\n[*.cs]\ndotnet_diagnostic.ESL011.severity = error\ndotnet_diagnostic.ESL004.severity = none\n");
            var r = await Engine.RunAsync(new RunOptions { Path = dir });
            Assert.Equal("error", Assert.Single(r.Findings).Level);
            Assert.Equal("ESL011", r.Findings[0].RuleId);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task LegacyKoreanEncoding_IsReadForSnippets()
    {
        var dir = Directory.CreateTempSubdirectory("esl-enc-").FullName;
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var src = "class Old {\n    // 스테이지 대기\n    bool Late(int deadline) => System.Environment.TickCount > deadline; // 마감\n}\n";
            File.WriteAllBytes(Path.Combine(dir, "Old.cs"), Encoding.GetEncoding(949).GetBytes(src));
            var r = await Engine.RunAsync(new RunOptions { Path = dir });
            Assert.Contains("// 마감", Assert.Single(r.Findings).LineText);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task UsageErrors_ExitCode2()
    {
        Assert.Equal(2, await Program.Main(new[] { "--nope" }));
        Assert.Equal(2, await Program.Main(new[] { Path.Combine(RepoRoot, "does-not-exist") }));
        Assert.Equal(2, await Program.Main(new[] { Samples, "--fail-on", "maybe" }));
        Assert.Equal(2, await Program.Main(new[] { Samples, "--baseline", Path.Combine(RepoRoot, "README.md") }));
    }

    /// <summary>viewer/rules.json is generated from Rules.cs: `dotnet run --project src/EquipSwLint.Cli -- rules --json`.</summary>
    [Fact]
    public void ViewerRulesJson_IsInSyncWithAnalyzer()
    {
        var committed = File.ReadAllText(Path.Combine(RepoRoot, "viewer", "rules.json")).Replace("\r\n", "\n").Trim();
        Assert.Equal(Program.RulesJson().Replace("\r\n", "\n").Trim(), committed);
    }

    private static Task<int> Run(params string[] args) => Program.RunAsync(args.Append("--lang").Append("en").ToArray(), TextWriter.Null);
}
