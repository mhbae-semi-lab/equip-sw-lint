using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace EquipSwLint.Cli;

/// <summary>Single-file HTML report for CI artifacts and e-mail. For triage, baseline compare and Shadow promotion use the viewer.</summary>
public static class Html
{
    public static void Write(RunResult r, string path)
    {
        var ko = r.Lang == "ko";
        string T(string en, string k) => ko ? k : en;
        static string E(string s) => WebUtility.HtmlEncode(s);
        var sb = new StringBuilder();
        sb.Append($"<!doctype html><html lang=\"{(ko ? "ko" : "en")}\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append($"<title>equip-sw-lint {T("report", "보고서")}</title><style>");
        sb.Append(":root{color-scheme:light dark;--bg:#fff;--fg:#1b1f24;--mut:#57606a;--line:#d0d7de;--hl:#fff8c5;--warn:#9a6700;--note:#0969da;--err:#cf222e}");
        sb.Append("@media(prefers-color-scheme:dark){:root{--bg:#0d1117;--fg:#e6edf3;--mut:#8d96a0;--line:#30363d;--hl:#3b2e00;--warn:#d29922;--note:#4493f8;--err:#f85149}}");
        sb.Append("body{margin:0 auto;max-width:1100px;padding:16px;background:var(--bg);color:var(--fg);font:15px/1.5 system-ui,'Malgun Gothic',sans-serif}");
        sb.Append("table{border-collapse:collapse;width:100%}td,th{border-bottom:1px solid var(--line);padding:4px 8px;text-align:left}");
        sb.Append("pre{overflow-x:auto;border:1px solid var(--line);padding:8px;font:13px/1.45 Consolas,monospace}mark{background:var(--hl);color:inherit;display:block}");
        sb.Append(".lv-warning{color:var(--warn)}.lv-note{color:var(--note)}.lv-error{color:var(--err)}.mut{color:var(--mut)}h2{margin-top:2em;border-bottom:1px solid var(--line)}");
        sb.Append("</style></head><body>");
        sb.Append($"<h1>equip-sw-lint {E(Sarif.Version)}</h1>");
        sb.Append($"<p class=\"mut\">{T("Files", "파일")} {r.Files} · {T("lines", "줄")} {r.Lines:N0} · {T("analysis", "분석")} {r.ParseMs + r.AnalyzeMs} ms · {T("findings", "결과")} {r.Findings.Count}");
        if (r.Findings.Any(f => f.BaselineState != null))
            sb.Append($" · {T("new", "새 결과")} {r.Findings.Count(f => f.BaselineState == "new")} · {T("fixed since baseline", "기준선 이후 해결")} {r.AbsentFromBaseline}");
        sb.Append("</p>");
        if (r.UnresolvedTypes > 0)
            sb.Append($"<p class=\"mut\">{T($"{r.UnresolvedTypes} type references were not resolved (vendor SDK DLLs?). Rules on those types are skipped; pass --ref <folder>.", $"형식 참조 {r.UnresolvedTypes}개를 찾지 못했다(벤더 SDK DLL?). 그 형식에 대한 규칙은 건너뛴다. --ref <폴더> 로 넘긴다.")}</p>");

        sb.Append($"<table><thead><tr><th>{T("Rule", "규칙")}</th><th>{T("Stage", "단계")}</th><th>{T("Count", "개수")}</th></tr></thead><tbody>");
        foreach (var rule in Rules.All)
        {
            var n = r.Findings.Count(f => f.RuleId == rule.Id);
            sb.Append($"<tr><td><a href=\"#{rule.Id}\">{rule.Id}</a> {E(ko ? rule.TitleKo : rule.TitleEn)}</td><td>{(rule.Stage == "shadow" ? "Shadow" : T("Enforce", "적용"))}</td><td>{n}</td></tr>");
        }
        sb.Append("</tbody></table>");

        foreach (var g in r.Findings.GroupBy(f => f.RuleId))
        {
            var rule = Rules.Find(g.Key);
            sb.Append($"<h2 id=\"{g.Key}\">{g.Key} {E(rule == null ? "" : ko ? rule.TitleKo : rule.TitleEn)}</h2>");
            if (rule != null)
            {
                sb.Append($"<p><b>{T("How it stops equipment", "장비에서 어떻게 멈추나")}</b> {E(ko ? rule.WhyKo : rule.WhyEn)}</p>");
                sb.Append($"<p><b>{T("Fix", "고치는 법")}</b> {E(ko ? rule.FixKo : rule.FixEn)}</p>");
                if (rule.Overlap.Length > 0) sb.Append($"<p class=\"mut\">{T("Also covered by", "다른 분석기")}: {E(rule.Overlap)}</p>");
            }
            foreach (var f in g)
            {
                sb.Append($"<h3 class=\"lv-{f.Level}\">{E(r.Shown(f))}:{f.Line} <span class=\"mut\">{f.Level}{(f.BaselineState == "new" ? " · " + T("new", "새 결과") : "")}</span></h3>");
                sb.Append($"<p>{E(f.Message)}</p><pre>");
                var lines = f.Context.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    var no = f.ContextStart + i;
                    var text = E($"{no,5}  {lines[i]}");
                    sb.Append(no == f.Line ? $"<mark>{text}</mark>" : text + "\n");
                }
                sb.Append("</pre>");
            }
        }
        sb.Append($"<p class=\"mut\">{T("Open the SARIF file in the viewer for filters, baseline compare and Shadow rule triage.", "필터 · 기준선 비교 · Shadow 규칙 판정은 SARIF 파일을 보고서 뷰어에서 연다.")}</p>");
        sb.Append("</body></html>");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }
}
