using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace EquipSwLint.Cli;

public sealed class Finding
{
    public required string RuleId { get; init; }
    public required string Level { get; init; } // error | warning | note
    public required string Message { get; init; }
    public required string File { get; init; } // relative, '/' separated
    public int Line { get; init; }
    public int Column { get; init; }
    public int EndLine { get; init; }
    public int EndColumn { get; init; }
    public string LineText { get; init; } = "";
    public int ContextStart { get; init; }
    public string Context { get; init; } = "";
    public string Fingerprint { get; set; } = "";
    public string? BaselineState { get; set; } // new | unchanged (null without baseline)
}

public sealed class RunResult
{
    public List<Finding> Findings { get; } = new();
    public int Files { get; set; }
    public int Lines { get; set; }
    public long ParseMs { get; set; }
    public long AnalyzeMs { get; set; }
    public Dictionary<string, double> RuleMs { get; } = new();
    public int UnresolvedTypes { get; set; }
    public List<string> AnalyzerErrors { get; } = new();
    public int AbsentFromBaseline { get; set; }
    public string Lang { get; set; } = "en";
    /// <summary>Full path of the analyzed folder; finding paths and fingerprints are relative to it.</summary>
    public string Root { get; set; } = "";
    /// <summary>Prepended to paths in SARIF/HTML/console only (--repo-root), so code scanning links to source. Fingerprints ignore it.</summary>
    public string PathPrefix { get; set; } = "";
    public string Shown(Finding f) => PathPrefix + f.File;
}

public sealed class RunOptions
{
    public required string Path { get; init; }
    public List<string> RefDirs { get; init; } = new();
    public string? BaselineSarif { get; init; }
    public List<string> Exclude { get; init; } = new(); // folder names to skip (tests, vendored code)
    public string Lang { get; init; } = "en";
}

public static class Engine
{
    private static readonly string[] SkipDirs = { "bin", "obj", ".git", ".vs", "node_modules", "packages", "TestResults" };

    public static async Task<RunResult> RunAsync(RunOptions o)
    {
        var result = new RunResult { Lang = o.Lang };
        var full = System.IO.Path.GetFullPath(o.Path);
        string root;
        List<string> files;
        if (File.Exists(full) && full.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            root = System.IO.Path.GetDirectoryName(full)!;
            files = new List<string> { full };
        }
        else
        {
            root = File.Exists(full) ? System.IO.Path.GetDirectoryName(full)! : full; // .csproj / .sln: analyze its folder
            if (!Directory.Exists(root)) throw new UsageException($"Path not found: {o.Path}");
            files = EnumerateSources(root, o.Exclude).ToList();
        }

        result.Root = root;
        var sw = Stopwatch.StartNew();
        var parse = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = files.Select(f =>
        {
            var text = SourceText.From(ReadSource(f), Encoding.UTF8);
            return CSharpSyntaxTree.ParseText(text, parse, f);
        }).ToList();
        result.Files = trees.Count;
        result.Lines = trees.Sum(t => t.GetText().Lines.Count);
        result.ParseMs = sw.ElapsedMilliseconds;

        var severities = ReadSeverities(root);
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, concurrentBuild: true)
            .WithSpecificDiagnosticOptions(severities);
        var compilation = CSharpCompilation.Create("equip-sw-lint-analysis", trees, References(o.RefDirs), options);
        result.UnresolvedTypes = compilation.GetDiagnostics().Count(d => d.Id == "CS0246" || d.Id == "CS0234");

        var analyzers = typeof(Rules).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t))
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
        var cwa = compilation.WithAnalyzers(analyzers, new CompilationWithAnalyzersOptions(
            new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            onAnalyzerException: (ex, a, d) => { lock (result.AnalyzerErrors) result.AnalyzerErrors.Add($"{a.GetType().Name}: {ex.GetType().Name}: {ex.Message}"); },
            concurrentAnalysis: true, logAnalyzerExecutionTime: true, reportSuppressedDiagnostics: false));
        sw.Restart();
        var diags = await cwa.GetAnalyzerDiagnosticsAsync();
        result.AnalyzeMs = sw.ElapsedMilliseconds;
        foreach (var a in analyzers)
        {
            var info = await cwa.GetAnalyzerTelemetryInfoAsync(a, default);
            foreach (var d in a.SupportedDiagnostics)
                result.RuleMs[d.Id] = info.ExecutionTime.TotalMilliseconds / a.SupportedDiagnostics.Length;
        }

        var culture = new CultureInfo(o.Lang == "ko" ? "ko-KR" : "en-US");
        foreach (var d in diags)
        {
            if (d.Id == "AD0001") { result.AnalyzerErrors.Add(d.GetMessage(CultureInfo.InvariantCulture)); continue; }
            if (d.Severity == DiagnosticSeverity.Hidden || !d.Location.IsInSource) continue;
            var span = d.Location.GetLineSpan();
            var text = d.Location.SourceTree!.GetText();
            int line = span.StartLinePosition.Line;
            int c0 = Math.Max(0, line - 2), c1 = Math.Min(text.Lines.Count - 1, line + 2);
            result.Findings.Add(new Finding
            {
                RuleId = d.Id,
                Level = d.Severity switch { DiagnosticSeverity.Error => "error", DiagnosticSeverity.Warning => "warning", _ => "note" },
                Message = d.GetMessage(culture),
                File = System.IO.Path.GetRelativePath(root, span.Path).Replace('\\', '/'),
                Line = line + 1,
                Column = span.StartLinePosition.Character + 1,
                EndLine = span.EndLinePosition.Line + 1,
                EndColumn = span.EndLinePosition.Character + 1,
                LineText = text.Lines[line].ToString(),
                ContextStart = c0 + 1,
                Context = string.Join("\n", Enumerable.Range(c0, c1 - c0 + 1).Select(i => text.Lines[i].ToString())),
            });
        }
        result.Findings.Sort((a, b) => string.CompareOrdinal(a.File, b.File) != 0 ? string.CompareOrdinal(a.File, b.File)
            : a.Line != b.Line ? a.Line.CompareTo(b.Line) : string.CompareOrdinal(a.RuleId, b.RuleId));
        AssignFingerprints(result.Findings);
        if (o.BaselineSarif != null) ApplyBaseline(result, o.BaselineSarif);
        return result;
    }

    public static IEnumerable<string> EnumerateSources(string root, IReadOnlyCollection<string>? exclude = null)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            foreach (var f in Directory.EnumerateFiles(dir, "*.cs")) yield return f;
            foreach (var d in Directory.EnumerateDirectories(dir))
            {
                var name = System.IO.Path.GetFileName(d);
                if (!SkipDirs.Contains(name, StringComparer.OrdinalIgnoreCase) && exclude?.Contains(name, StringComparer.OrdinalIgnoreCase) != true) stack.Push(d);
            }
        }
    }

    /// <summary>UTF-8 first; legacy equipment sources are often CP949 (Korean ANSI), fall back to it so snippets stay readable.</summary>
    public static string ReadSource(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿'); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(949).GetString(bytes);
        }
    }

    /// <summary>dotnet_diagnostic.ESLxxx.severity from .editorconfig/.globalconfig in the analyzed folder (same keys the NuGet analyzer uses).</summary>
    public static Dictionary<string, ReportDiagnostic> ReadSeverities(string root)
    {
        var map = new Dictionary<string, ReportDiagnostic>();
        foreach (var name in new[] { ".globalconfig", ".editorconfig" })
        {
            var p = System.IO.Path.Combine(root, name);
            if (!File.Exists(p)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(p), @"^\s*dotnet_diagnostic\.(ESL\d{3})\.severity\s*=\s*(\w+)", RegexOptions.Multiline))
                map[m.Groups[1].Value] = m.Groups[2].Value.ToLowerInvariant() switch
                {
                    "none" => ReportDiagnostic.Suppress,
                    "silent" => ReportDiagnostic.Hidden,
                    "suggestion" => ReportDiagnostic.Info,
                    "warning" => ReportDiagnostic.Warn,
                    "error" => ReportDiagnostic.Error,
                    _ => ReportDiagnostic.Default
                };
        }
        return map;
    }

    /// <summary>Runtime + Windows Desktop (WinForms/WPF) + System.IO.Ports + user folders, first file name wins.</summary>
    public static List<MetadataReference> References(IEnumerable<string> extra)
    {
        var runtime = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var dirs = new List<string> { runtime };
        var desktopRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(runtime, "..", "..", "Microsoft.WindowsDesktop.App"));
        if (Directory.Exists(desktopRoot))
        {
            var major = Environment.Version.Major + ".";
            var best = Directory.GetDirectories(desktopRoot).Select(System.IO.Path.GetFileName)
                .Where(v => v!.StartsWith(major)).OrderByDescending(v => Version.TryParse(v, out var x) ? x : new Version()).FirstOrDefault();
            if (best != null) dirs.Add(System.IO.Path.Combine(desktopRoot, best));
        }
        var files = new List<string>();
        foreach (var d in dirs) files.AddRange(Directory.GetFiles(d, "*.dll"));
        files.Add(typeof(System.IO.Ports.SerialPort).Assembly.Location);
        foreach (var d in extra)
        {
            if (!Directory.Exists(d)) throw new UsageException($"--ref folder not found: {d}");
            files.AddRange(Directory.GetFiles(d, "*.dll", SearchOption.AllDirectories));
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var refs = new List<MetadataReference>();
        foreach (var f in files)
        {
            if (!seen.Add(System.IO.Path.GetFileName(f)) || !IsManaged(f)) continue;
            refs.Add(MetadataReference.CreateFromFile(f));
        }
        return refs;
    }

    private static bool IsManaged(string file)
    {
        try
        {
            using var s = File.OpenRead(file);
            using var pe = new PEReader(s);
            return pe.HasMetadata && pe.GetMetadataReader().IsAssembly;
        }
        catch (BadImageFormatException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    /// <summary>Line-move tolerant id: rule + file + normalized line text + occurrence, so a baseline survives unrelated edits.</summary>
    public static void AssignFingerprints(List<Finding> findings)
    {
        var seen = new Dictionary<string, int>();
        foreach (var f in findings)
        {
            var key = f.RuleId + "|" + f.File + "|" + Regex.Replace(f.LineText, @"\s+", " ").Trim();
            seen[key] = seen.TryGetValue(key, out var n) ? n + 1 : 0;
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key + "|" + seen[key]));
            f.Fingerprint = Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
        }
    }

    private static void ApplyBaseline(RunResult r, string sarifPath)
    {
        var old = Sarif.ReadFingerprints(sarifPath);
        foreach (var f in r.Findings) f.BaselineState = old.Contains(f.Fingerprint) ? "unchanged" : "new";
        r.AbsentFromBaseline = old.Count(fp => r.Findings.All(f => f.Fingerprint != fp));
    }
}

public sealed class UsageException(string message) : Exception(message);
