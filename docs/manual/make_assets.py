# -*- coding: utf-8 -*-
"""Terminal and report pages for the manual screenshots, all from the synthetic samples.

    python docs/manual/make_assets.py <folder of a built demo project>   (optional, for the dotnet build screen)

Writes docs/manual/.cache/{term-cli.html, term-build.html, report.html, build.sarif}. Local paths are replaced
by C:\\src\\Equipment so no user folder appears on screen.
"""
import html
import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
CACHE = os.path.join(HERE, ".cache")
CLI = os.path.join(ROOT, "src", "EquipSwLint.Cli", "bin", "Release", "net8.0", "equip-sw-lint.dll")
os.makedirs(CACHE, exist_ok=True)

TERM = """<!doctype html><meta charset="utf-8"><title>{title}</title>
<style>body{{margin:0;background:#0f1115;color:#d7dae0;font:14px/1.55 Consolas,"D2Coding",monospace}}
.bar{{background:#1c1f26;color:#9aa3b2;padding:6px 14px;font:13px "Malgun Gothic",sans-serif}}pre{{margin:0;padding:12px 16px;white-space:pre-wrap}}
.p{{color:#7fd483}}.w{{color:#f0b24a}}.n{{color:#7fb0ff}}.e{{color:#ff6b6b}}.m{{color:#8a93a3}}</style>
<div class="bar">{title}</div><pre>{body}</pre>"""


def color(line):
    s = html.escape(line)
    s = re.sub(r"(warning ESL\d+)", r'<span class="w">\1</span>', s)
    s = re.sub(r"(note ESL\d+)", r'<span class="n">\1</span>', s)
    s = re.sub(r"(게이트 실패.*)", r'<span class="e">\1</span>', s)
    return s


def run(args, cwd=ROOT):
    return subprocess.run(args, cwd=cwd, capture_output=True, text=True, encoding="utf-8", errors="replace").stdout


# 1. CLI on the samples
cli = run(["dotnet", CLI, "samples/Equipment", "--lang", "ko", "--html", os.path.join(CACHE, "report.html")])
lines = ['<span class="p">PS C:\\src\\Equipment&gt;</span> equip-sw-lint . --sarif report.sarif --html report.html']
for ln in cli.splitlines():
    # keep the long messages short on screen
    m = re.match(r"^(.*?: (?:warning|note) ESL\d+: )(.*)$", ln)
    lines.append(color(m.group(1)) + html.escape(m.group(2)[:58] + "…") if m else color(ln))
open(os.path.join(CACHE, "term-cli.html"), "w", encoding="utf-8").write(TERM.format(title="Windows PowerShell — equip-sw-lint", body="\n".join(lines)))

# 2. dotnet build with the NuGet analyzer (optional demo project)
if len(sys.argv) > 1:
    proj = sys.argv[1]
    env = dict(os.environ, DOTNET_CLI_UI_LANGUAGE="ko")
    out = subprocess.run(["dotnet", "build", "-c", "Release", "--nologo", "--no-incremental", "-p:PreferredUILang=ko-KR",
                          "-p:ErrorLog=out.sarif%2Cversion=2.1"], cwd=proj, capture_output=True, text=True, encoding="utf-8",
                         errors="replace", env=env).stdout
    seen, keep = set(), []
    for ln in out.splitlines():
        if "ESL0" not in ln:
            continue
        ln = ln.replace(proj.replace("/", "\\") + "\\", "")
        ln = re.sub(r"\s*\[[^\]]*\.csproj\]$", "", ln)
        ln = re.sub(r"\s*\(https://[^)]*\)", "", ln)
        if ln in seen:
            continue
        seen.add(ln)
        m = re.match(r"^(.*?: warning ESL\d+: )(.*)$", ln.strip())
        keep.append(color(m.group(1)) + html.escape(m.group(2)[:60] + "…") if m else color(ln))
    body = ['<span class="p">PS C:\\src\\Equipment&gt;</span> dotnet add package EquipSwLint.Analyzers',
            '<span class="p">PS C:\\src\\Equipment&gt;</span> dotnet build -p:ErrorLog=report.sarif%2Cversion=2.1', ""] + keep + [
            "", '<span class="m">    경고 ' + str(len(keep)) + '개 · 오류 0개   (Shadow 규칙은 suggestion 이라 IDE · SARIF 에만 표시)</span>']
    open(os.path.join(CACHE, "term-build.html"), "w", encoding="utf-8").write(TERM.format(title="Windows PowerShell — dotnet build", body="\n".join(body)))
    # neutral paths in the build SARIF
    d = json.load(open(os.path.join(proj, "out.sarif"), encoding="utf-8"))
    prefix = "file:///" + proj.replace("\\", "/").rstrip("/") + "/"
    text = json.dumps(d, ensure_ascii=False)
    text = text.replace(prefix, "file:///C:/src/Equipment/").replace(prefix.replace("file:///", "file:///").lower(), "file:///C:/src/Equipment/")
    text = re.sub(r"file:///[^\"]*?/pkgtest/net8/", "file:///C:/src/Equipment/", text)
    assert "Users/" not in text and "AppData" not in text, "local path left in build.sarif"
    open(os.path.join(CACHE, "build.sarif"), "w", encoding="utf-8").write(text)

for f in os.listdir(CACHE):
    p = os.path.join(CACHE, f)
    if f.endswith((".html", ".sarif")):
        t = open(p, encoding="utf-8").read()
        assert "Users/" not in t and "Users\\" not in t and "AppData" not in t, f"local path in {f}"
print("assets in", CACHE)
