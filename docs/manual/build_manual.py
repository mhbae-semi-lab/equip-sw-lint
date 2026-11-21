# -*- coding: utf-8 -*-
"""장비 SW 위험 코드 린트 사용자 매뉴얼(PPTX) 생성기. llm-pulse docs/manual/build_manual.py 양식(log-analyzer 판)을 이 도구에 맞게 고쳐 쓴다.

    python docs/manual/build_manual.py          # PPTX
    python docs/manual/build_manual.py --pdf    # + PDF (PowerPoint COM, Windows)

스크린샷은 docs/manual/capture.mjs 로 만든 shots/*.png (1280x800 CSS px, 배율 1.5) 와 말풍선 자리 shots/marks.json.
터미널 · HTML 보고서 화면은 make_assets.py 가 합성 예제로 만든다. 화면은 모두 합성 데이터다.
"""
import json
import os
import sys

from PIL import Image
from pptx import Presentation
from pptx.dml.color import RGBColor
from pptx.enum.shapes import MSO_SHAPE
from pptx.enum.text import MSO_ANCHOR, PP_ALIGN
from pptx.util import Inches, Pt

HERE = os.path.dirname(os.path.abspath(__file__))
SHOTS = os.path.join(HERE, "shots")
ASSETS = os.path.join(HERE, "assets")
CACHE = os.path.join(HERE, ".cache")
OUT = os.path.join(HERE, "Equip-SW-Lint-User-Manual-ko.pptx")
VERSION = "1.0.0"
DATE = "2026.11.22"
NAME = "장비 SW 위험 코드 린트"
MARKS = json.load(open(os.path.join(SHOTS, "marks.json"), encoding="utf-8"))
DIP = (1280, 800)

# ---------------------------------------------------------------- 디자인 토큰 (llm-pulse 매뉴얼과 같음, 강조색만 앱 색)
INK = RGBColor(0x1F, 0x29, 0x37)
SUB = RGBColor(0x5B, 0x65, 0x73)
BLUE = RGBColor(0xC0, 0x00, 0x00)
NAVY = RGBColor(0x14, 0x14, 0x18)
CARD = RGBColor(0xF3, 0xF5, 0xF8)
LINE = RGBColor(0xD9, 0xDE, 0xE5)
AMBER = RGBColor(0xE5, 0xA0, 0x2D)
AMBER_BG = RGBColor(0xFF, 0xF6, 0xE5)
AMBER_TX = RGBColor(0x7A, 0x4E, 0x00)
WHITE = RGBColor(0xFF, 0xFF, 0xFF)
SOFT = RGBColor(0xF2, 0xB8, 0xB8)
NOTE = RGBColor(0x1A, 0x5F, 0xD0)
FONT = "맑은 고딕"
MONO = "Consolas"

prs = Presentation()
prs.slide_width, prs.slide_height = Inches(13.333), Inches(7.5)
TOTAL = 23
state = {"n": 0}


def new_slide():
    state["n"] += 1
    return prs.slides.add_slide(prs.slide_layouts[6])


def box(s, x, y, w, h, fill=None, line=None, shape=MSO_SHAPE.RECTANGLE, radius=None, lw=0.75):
    sh = s.shapes.add_shape(shape, Inches(x), Inches(y), Inches(w), Inches(h))
    if fill is None:
        sh.fill.background()
    else:
        sh.fill.solid()
        sh.fill.fore_color.rgb = fill
    if line is None:
        sh.line.fill.background()
    else:
        sh.line.color.rgb = line
        sh.line.width = Pt(lw)
    sh.shadow.inherit = False
    if radius is not None and shape == MSO_SHAPE.ROUNDED_RECTANGLE:
        sh.adjustments[0] = radius
    return sh


def rbox(s, x, y, w, h, fill=CARD, line=None, radius=0.03, lw=0.75):
    return box(s, x, y, w, h, fill, line, MSO_SHAPE.ROUNDED_RECTANGLE, radius, lw)


def text(s, x, y, w, h, t, size=12, bold=False, color=INK, align=PP_ALIGN.LEFT, anchor=MSO_ANCHOR.TOP,
         shape=None, font=FONT, spacing=None):
    tb = shape or s.shapes.add_textbox(Inches(x), Inches(y), Inches(w), Inches(h))
    tf = tb.text_frame
    tf.word_wrap = True
    tf.margin_left = tf.margin_right = Inches(0.05)
    tf.margin_top = tf.margin_bottom = Inches(0.02)
    tf.vertical_anchor = anchor
    lines = t if isinstance(t, list) else [t]
    for i, line in enumerate(lines):
        p = tf.paragraphs[0] if i == 0 else tf.add_paragraph()
        p.alignment = align
        if spacing:
            p.line_spacing = spacing
        r = p.add_run()
        r.text = line
        r.font.size = Pt(size)
        r.font.bold = bold
        r.font.color.rgb = color
        r.font.name = font
    return tb


def num_circle(s, x, y, n, d=0.3, size=11):
    c = box(s, x, y, d, d, fill=BLUE, line=WHITE, shape=MSO_SHAPE.OVAL, lw=1.5)
    text(s, 0, 0, 0, 0, str(n), size, True, WHITE, PP_ALIGN.CENTER, MSO_ANCHOR.MIDDLE, shape=c)
    c.text_frame.margin_left = c.text_frame.margin_right = 0
    return c


def code_card(s, x, y, w, h, title, lines, size=11):
    rbox(s, x, y, w, h)
    text(s, x + 0.15, y + 0.1, w - 0.3, 0.32, title, 13, True)
    b = rbox(s, x + 0.15, y + 0.5, w - 0.3, h - 0.65, fill=NAVY, radius=0.03)
    text(s, 0, 0, 0, 0, lines, size, False, RGBColor(0xD7, 0xDA, 0xE0), shape=b, font=MONO)
    b.text_frame.margin_left = Inches(0.15)
    b.text_frame.margin_top = Inches(0.1)


# ---------------------------------------------------------------- 공통 틀
def header(s, section, title, bullets):
    pill = box(s, 10.2, 0.0, 2.7, 0.34, fill=BLUE, shape=MSO_SHAPE.ROUNDED_RECTANGLE, radius=0.3)
    text(s, 0, 0, 0, 0, section, 10, True, WHITE, PP_ALIGN.CENTER, MSO_ANCHOR.MIDDLE, shape=pill)
    text(s, 0.45, 0.32, 11.5, 0.6, f"[SW 린트] {title}", 28, True)
    box(s, 0.5, 1.0, 0.9, 0.06, fill=BLUE)
    for i, t in enumerate(bullets):
        box(s, 0.55, 1.37 + i * 0.36, 0.07, 0.07, fill=BLUE, shape=MSO_SHAPE.OVAL)
        text(s, 0.72, 1.24 + i * 0.36, 12.1, 0.32, t, 13.5)
    footer(s)


def footer(s):
    box(s, 0.5, 7.05, 12.33, 0.01, fill=LINE)
    s.shapes.add_picture(os.path.join(ASSETS, "icon.png"), Inches(0.5), Inches(7.12), Inches(0.22), Inches(0.22))
    text(s, 0.78, 7.1, 6, 0.25, f"{NAME} 사용자 매뉴얼  ·  v{VERSION}  ·  배민혁", 9, False, SUB)
    text(s, 11.8, 7.1, 1.03, 0.25, f"{state['n']} / {TOTAL}", 9, True, SUB, PP_ALIGN.RIGHT)


# ---------------------------------------------------------------- 스크린샷 카드
def load_shot(name, crop=None, dip=DIP):
    """crop: CSS px 사각형 (x0,y0,x1,y1). 반환: (경로, 크기, 오프셋)"""
    path = os.path.join(SHOTS, f"{name}.png")
    im = Image.open(path)
    k = im.width / dip[0]
    if not crop:
        return path, dip, (0, 0)
    os.makedirs(CACHE, exist_ok=True)
    x0, y0, x1, y1 = crop
    out = os.path.join(CACHE, f"{name}_{x0}_{y0}_{x1}_{y1}.png")
    im.crop((round(x0 * k), round(y0 * k), round(x1 * k), round(y1 * k))).save(out, optimize=True)
    return out, (x1 - x0, y1 - y0), (x0, y0)


def M(name, *idx):
    """marks.json 에서 말풍선 자리 고르기 (숫자 대신 사각형을 직접 줄 수도 있음)"""
    out = []
    for i in idx:
        r = MARKS[name][i] if isinstance(i, int) else i
        if r:
            out.append(tuple(r))
    return out


def shot_card(s, x, y, w, h, title, name, callouts=(), crop=None, dip=DIP, d=0.24):
    rbox(s, x, y, w, h)
    text(s, x + 0.15, y + 0.1, w - 0.3, 0.32, title, 13, True)
    path, (dw, dh), (ox, oy) = load_shot(name, crop, dip)
    bw, bh = w - 0.4, h - 0.82  # 아래 0.2in 는 "합성 데이터" 표기 자리
    k = min(bw / dw, bh / dh)
    iw, ih = dw * k, dh * k
    ix, iy = x + (w - iw) / 2, y + 0.5
    box(s, ix - 0.02, iy - 0.02, iw + 0.04, ih + 0.04, fill=WHITE, line=LINE)
    s.shapes.add_picture(path, Inches(ix), Inches(iy), Inches(iw), Inches(ih))
    for n, r in enumerate(callouts, 1):
        x0, y0, x1, y1 = r
        x0, y0, x1, y1 = max(x0, ox), max(y0, oy), min(x1, ox + dw), min(y1, oy + dh)
        box(s, ix + (x0 - ox) * k, iy + (y0 - oy) * k, max(0.05, (x1 - x0) * k), max(0.05, (y1 - y0) * k), fill=None, line=BLUE,
            shape=MSO_SHAPE.ROUNDED_RECTANGLE, radius=0.08, lw=1.75)
        c = box(s, ix + (x0 - ox) * k - d / 2, iy + (y0 - oy) * k - d / 2, d, d, fill=BLUE, line=WHITE,
                shape=MSO_SHAPE.OVAL, lw=1.25)
        text(s, 0, 0, 0, 0, str(n), 10, True, WHITE, PP_ALIGN.CENTER, MSO_ANCHOR.MIDDLE, shape=c)
        c.text_frame.margin_left = c.text_frame.margin_right = 0
    text(s, x + w - 3.0, y + h - 0.27, 2.85, 0.22, "※ 합성 데이터로 찍은 화면", 8.5, False, SUB, PP_ALIGN.RIGHT)


def steps_card(s, x, y, w, h, steps, title="사용 순서", tip=None, desc_size=10):
    rbox(s, x, y, w, h)
    text(s, x + 0.15, y + 0.1, w - 0.3, 0.32, title, 13, True)
    n = len(steps)
    top = y + 0.52
    avail = h - 0.52 - (0.62 if tip else 0.15)
    pitch = min(0.8, avail / n)
    for i, (hd, ds) in enumerate(steps):
        yy = top + i * pitch
        num_circle(s, x + 0.2, yy + 0.03, i + 1)
        text(s, x + 0.62, yy - 0.02, w - 0.8, 0.28, hd, 12, True)
        text(s, x + 0.62, yy + 0.24, w - 0.8, pitch - 0.22, ds, desc_size, False, SUB)
    if tip:
        t = rbox(s, x + 0.15, y + h - 0.55, w - 0.3, 0.42, fill=AMBER_BG, line=AMBER, radius=0.15)
        text(s, 0, 0, 0, 0, "Tip  " + tip, 9.5, False, AMBER_TX, PP_ALIGN.LEFT, MSO_ANCHOR.MIDDLE, shape=t)
        t.text_frame.margin_left = Inches(0.12)


CY, CH = 2.45, 4.5      # 카드 영역
LX, LW = 0.5, 7.0       # 왼쪽(스크린샷) 카드
RX, RW = 7.65, 5.18     # 오른쪽(순서) 카드


def shot_steps(section, title, bullets, shot, callouts, steps, tip=None, card_title=None, crop=None, desc_size=10):
    s = new_slide()
    header(s, section, title, bullets)
    shot_card(s, LX, CY, LW, CH, card_title or title, shot, callouts, crop)
    steps_card(s, RX, CY, RW, CH, steps, tip=tip, desc_size=desc_size)
    return s


def code_lines(s, x0, y0, w, h):
    """표지용 그림: 코드 줄 막대 위에 위험 줄(빨강) · Shadow 줄(파랑)"""
    import random
    rnd = random.Random(20261122)
    rows = 14
    lh = h / rows
    for i in range(rows):
        indent = rnd.choice([0, 0.25, 0.5, 0.75])
        ln = rnd.uniform(0.18, 0.55) * w
        col = RGBColor(0x2A, 0x2C, 0x34)
        if i in (4, 9):
            col = BLUE
        elif i == 12:
            col = NOTE
        box(s, x0 + 0.3 + indent, y0 + i * lh + lh * 0.25, ln, lh * 0.45, fill=col)


# ================================================================ 1. 표지
def cover():
    s = new_slide()
    s.background.fill.solid()
    s.background.fill.fore_color.rgb = NAVY
    code_lines(s, 0.5, 5.25, 5.8, 1.95)
    s.shapes.add_picture(os.path.join(ASSETS, "icon.png"), Inches(0.8), Inches(0.9), Inches(0.9), Inches(0.9))
    text(s, 0.75, 2.0, 6.2, 1.0, NAME, 32, True, WHITE)
    text(s, 0.8, 3.0, 7, 0.6, "사용자 매뉴얼", 26, False, SOFT)
    box(s, 0.85, 3.75, 0.9, 0.06, fill=BLUE)
    text(s, 0.8, 3.95, 6.0, 0.9, ["장비에서 몇 주 뒤에 멈출 C# 코드를", "커밋 전에 찾는 Roslyn 분석기 · CLI · 보고서 뷰어"], 15, False, RGBColor(0xC9, 0xD3, 0xE0))
    text(s, 0.8, 4.85, 7, 0.3, f"Version {VERSION}   ·   {DATE}   ·   배민혁", 11, False, RGBColor(0x8A, 0x97, 0xA8))
    path, _, _ = load_shot("Findings")
    box(s, 6.88, 0.78, 6.0, 3.77, fill=None, line=RGBColor(0x3A, 0x3A, 0x44), lw=1.5)
    s.shapes.add_picture(path, Inches(6.9), Inches(0.8), Inches(5.96), Inches(3.725))
    p2, _, _ = load_shot("TermBuild", crop=(0, 0, 1280, 360))
    box(s, 6.88, 4.78, 6.0, 1.71, fill=None, line=RGBColor(0x3A, 0x3A, 0x44), lw=1.5)
    s.shapes.add_picture(p2, Inches(6.9), Inches(4.8), Inches(5.96), Inches(1.676))


# ================================================================ 2. 목차
def toc():
    s = new_slide()
    pill = box(s, 10.2, 0.0, 2.7, 0.34, fill=BLUE, shape=MSO_SHAPE.ROUNDED_RECTANGLE, radius=0.3)
    text(s, 0, 0, 0, 0, "목차", 10, True, WHITE, PP_ALIGN.CENTER, MSO_ANCHOR.MIDDLE, shape=pill)
    text(s, 0.45, 0.32, 11.5, 0.6, "목차", 28, True)
    box(s, 0.5, 1.0, 0.9, 0.06, fill=BLUE)
    text(s, 0.55, 1.2, 12, 0.35, "처음 쓰는 분은 1~2번, 필요한 화면만 찾는 분은 오른쪽 쪽수를 보세요.", 13.5, False, SUB)
    groups = [
        ("1", "시작하기", [("3", "이 도구가 푸는 문제"), ("4", "구성과 설치"), ("5", "NuGet 분석기 (빌드 · IDE)"), ("6", "dotnet tool (빌드 없이)"), ("7", "HTML 보고서")]),
        ("2", "보고서 뷰어", [("8", "첫 화면"), ("9", "결과 목록 · 근거 줄"), ("10", "규칙 설명 · 판정")]),
        ("3", "현장 검증 절차", [("11", "4단계 한눈에"), ("12", "재현"), ("13", "비교"), ("14", "Shadow"), ("15", "전환 · 롤백")]),
        ("4", "규칙 · 내 데이터", [("16", "규칙 11개"), ("17", "읽을 수 있는 SARIF"), ("18", "오류 메시지"), ("19", "CI 게이트 (GitHub Actions)"), ("20", "개인정보 · 저장 · LLM")]),
        ("5", "부록", [("21", "영어 · 다크 · 모바일 · 키보드"), ("22", "FAQ · 문제 해결"), ("23", "버전 정보")]),
    ]
    pos = [(0.5, 1.9, 3.95, 2.45), (4.69, 1.9, 3.95, 2.45), (8.88, 1.9, 3.95, 2.45),
           (0.5, 4.5, 6.05, 2.4), (6.78, 4.5, 6.05, 2.4)]
    for (no, name, items), (x, y, w, h) in zip(groups, pos):
        rbox(s, x, y, w, h)
        num_circle(s, x + 0.2, y + 0.2, no, 0.38, 13)
        text(s, x + 0.7, y + 0.2, w - 0.9, 0.4, name, 16, True, anchor=MSO_ANCHOR.MIDDLE)
        for i, (pg, nm) in enumerate(items):
            yy = y + 0.74 + i * 0.32
            text(s, x + 0.3, yy, 0.9, 0.3, pg, 11.5, True, BLUE)
            text(s, x + 1.2, yy, w - 1.4, 0.3, nm, 11.5, False, INK)
    footer(s)


# ================================================================ 3. 소개
def intro():
    s = new_slide()
    header(s, "시작하기", "이 도구가 푸는 문제", [
        "관점: 장비 PC 에서 몇 주씩 도는 C# 설비 SW 에서 실제로 Down 을 낸 코드 모양을 커밋 전에 찾는다",
        "범용 분석기는 문법 · API 사용법을 보고, 이 분석기는 설비 통신 · 타이머 · 영상 콜백 · 레시피 파일 · UI 스레드를 본다",
        "VSTHRD · CA · SonarQube 를 대신하지 않고 함께 쓴다 (그 분석기들이 하는 규칙은 넣지 않음)",
    ])
    cards = [
        ("시간이 지나야 드러나는 버그", "24.9일 뒤 넘어가는 32bit 틱, 시각 동기화로 건너뛰는 벽시계, 처리가 조금 느려 하루 뒤 넘치는 프레임 큐."),
        ("장비가 응답을 멈출 때", "타임아웃 없는 시리얼 · TCP 읽기, 기한 없는 센서 폴링, 신호를 놓치면 영원히 기다리는 대기가 알람 없이 시퀀스를 멈춥니다."),
        ("스레드 경계", "콜백과 UI 가 락 없이 쓰는 영상 버퍼, lock 안의 동기 Invoke(창 메시지 교착), 수신을 먼저 시작한 재시작 레이스."),
    ]
    for i, (hd, ds) in enumerate(cards):
        x = 0.5 + i * 4.19
        rbox(s, x, 2.45, 3.95, 1.95)
        num_circle(s, x + 0.2, 2.65, i + 1, 0.36)
        text(s, x + 0.68, 2.64, 3.1, 0.38, hd, 15, True, anchor=MSO_ANCHOR.MIDDLE)
        text(s, x + 0.25, 3.18, 3.5, 1.1, ds, 11, False, SUB)
    text(s, 0.5, 4.62, 6, 0.3, "코드가 판단이 되기까지", 13, True)
    flow = [
        ("코드", ["내 C# 프로젝트", "벤더 SDK 없이도"]),
        ("분석", ["NuGet 분석기 (빌드)", "dotnet tool (빌드 없이)"]),
        ("SARIF 보고서", ["근거 줄 · 멈추는 방식", "고치는 법 · 재현 테스트"]),
        ("현장 검증", ["재현 → 비교", "Shadow → 전환 · 롤백"]),
    ]
    w, gap = 2.78, 0.4
    for i, (hd, ln) in enumerate(flow):
        x = 0.5 + i * (w + gap)
        hl = i == 2
        rbox(s, x, 5.0, w, 1.75, fill=WHITE if hl else CARD, line=BLUE if hl else None, radius=0.04, lw=2)
        text(s, x + 0.1, 5.1, w - 0.2, 0.4, hd, 14, True, BLUE if hl else INK, PP_ALIGN.CENTER)
        text(s, x + 0.1, 5.6, w - 0.2, 1.0, ln, 10.5, False, SUB, PP_ALIGN.CENTER)
        if i < 3:
            box(s, x + w + 0.06, 5.72, gap - 0.12, 0.34, fill=BLUE, shape=MSO_SHAPE.RIGHT_ARROW)


# ================================================================ 4. 구성과 설치
def install():
    s = new_slide()
    header(s, "시작하기", "구성과 설치", [
        "세 가지는 같은 규칙 · 같은 .editorconfig 키를 씀. 필요한 것만 골라 써도 됨",
        "nuget.org 게시 전에는 GitHub Releases 의 .nupkg 를 받은 폴더를 --add-source 로 지정",
        "보고서 뷰어는 웹 주소에서 바로 열거나 '앱 설치'로 설치. 계정 · 서버 없음",
    ])
    cols = [
        ("NuGet 분석기", "EquipSwLint.Analyzers", ["빌드 · Visual Studio · Rider 에서", "경고로 바로 보임", "VS 2019 16.11 / .NET 5 SDK 이후"],
         ["dotnet add package EquipSwLint.Analyzers", "dotnet build"]),
        ("dotnet tool", "equip-sw-lint", ["빌드 없이 폴더의 .cs 를 분석", "SARIF · HTML · 기준선 게이트", ".NET 8 이상 런타임"],
         ["dotnet tool install -g equip-sw-lint", "equip-sw-lint ./src --sarif r.sarif"]),
        ("보고서 뷰어", "정적 웹 페이지 (PWA)", ["SARIF 열기 · 근거 줄 · 비교", "Shadow 판정 · 규칙 세트 이력", "오프라인 · 한국어 / English"],
         ["https://mhbae-semi-lab.github.io/", "  equip-sw-lint/viewer/"]),
    ]
    w = 3.95
    for i, (hd, pkg, items, cmd) in enumerate(cols):
        x = 0.5 + i * 4.19
        rbox(s, x, CY, w, CH)
        num_circle(s, x + 0.2, CY + 0.2, i + 1, 0.36)
        text(s, x + 0.68, CY + 0.18, w - 0.8, 0.4, hd, 16, True, anchor=MSO_ANCHOR.MIDDLE)
        text(s, x + 0.25, CY + 0.7, w - 0.4, 0.3, pkg, 11, True, BLUE, font=MONO)
        for j, it in enumerate(items):
            box(s, x + 0.3, CY + 1.22 + j * 0.42, 0.07, 0.07, fill=BLUE, shape=MSO_SHAPE.OVAL)
            text(s, x + 0.45, CY + 1.08 + j * 0.42, w - 0.6, 0.36, it, 11.5)
        b = rbox(s, x + 0.2, CY + 2.75, w - 0.4, 1.45, fill=NAVY, radius=0.04)
        text(s, 0, 0, 0, 0, ["> " + c if not c.startswith("  ") and not c.startswith("http") else c for c in cmd], 9.5, False,
             RGBColor(0xD7, 0xDA, 0xE0), shape=b, font=MONO)
        b.text_frame.margin_left = Inches(0.12)
        b.text_frame.margin_top = Inches(0.12)


def tools():
    SEC = "시작하기"
    s = new_slide()
    header(s, SEC, "NuGet 분석기 (빌드 · IDE)", [
        "패키지를 추가하면 빌드 · IDE 에서 규칙이 경고(warning)로 보임. Shadow 규칙은 suggestion(점선 밑줄)",
        "메시지는 빌드 · IDE 의 UI 언어를 따름 (한국어 Visual Studio 는 한국어, -p:PreferredUILang=ko-KR)",
        "단계는 .editorconfig 의 dotnet_diagnostic.ESLxxx.severity 로 바꿈 (none · suggestion · warning · error)",
    ])
    shot_card(s, LX, CY, LW, CH, "dotnet build 출력", "TermBuild", crop=(0, 0, 1020, 350))
    code_card(s, RX, CY, RW, 2.3, ".editorconfig (프로젝트 · 솔루션 폴더)", [
        "[*.cs]", "# Shadow → 적용", "dotnet_diagnostic.ESL010.severity = warning",
        "# 끄기", "dotnet_diagnostic.ESL002.severity = none"], 10.5)
    steps_card(s, RX, CY + 2.45, RW, CH - 2.45, [
        ("SARIF 로 남기기", "dotnet build -p:ErrorLog=report.sarif%2Cversion=2.1"),
        ("경고를 오류로", "TreatWarningsAsErrors 또는 severity = error"),
    ], title="빌드에서", desc_size=10)

    shot_steps(SEC, "dotnet tool (빌드 없이)", [
        "장비 코드는 벤더 SDK DLL 이 CI 에 없어 빌드가 안 되는 경우가 많아, .cs 파일을 바로 분석하는 CLI 를 따로 둠",
        "형식을 못 찾으면 그 형식에 대한 규칙은 건너뜀 (오탐이 늘지 않음). --ref 로 DLL 폴더를 주면 더 많이 봄",
        "종료 코드: 0 통과 · 1 --fail-on 이상 새 결과 · 2 사용법 · 입력 오류",
    ], "TermCli", [], [
        ("분석", "equip-sw-lint <폴더 | 파일.cs | 프로젝트.csproj>"),
        ("보고서", "--sarif r.sarif (뷰어 · 코드 스캐닝),  --html r.html (메일 · 아티팩트)"),
        ("기준선 게이트", "--baseline 지난.sarif  --fail-on warning | error | none"),
        ("형식 찾기 · 폴더 빼기", "--ref <벤더 DLL 폴더>,  --exclude tests"),
        ("언어 · 규칙 목록", "--lang ko | en,  equip-sw-lint rules"),
    ], tip="분석 폴더의 .editorconfig 심각도를 그대로 읽습니다. 소스는 UTF-8, 아니면 CP949", card_title="PowerShell", crop=(0, 0, 1000, 610), desc_size=9.5)

    shot_steps(SEC, "HTML 보고서", [
        "--html 로 만드는 파일 하나짜리 보고서. CI 아티팩트 · 메일로 보내기 좋음",
        "규칙별로 묶고, 결과마다 근거 줄 앞뒤 2줄 · 장비에서 어떻게 멈추나 · 고치는 법을 붙임",
        "판정 · 비교 · 규칙 세트 이력은 SARIF 를 보고서 뷰어에서 열어 함",
    ], "HtmlRule", [], [
        ("요약", "파일 · 줄 · 분석 시간 · 결과 수, 규칙별 개수와 단계"),
        ("규칙 설명", "멈추는 방식 · 고치는 법 · 같은 패턴을 보는 다른 분석기"),
        ("결과", "파일:줄, 메시지, 근거 줄 강조"),
        ("기준선", "--baseline 이 있으면 새 결과 · 해결 수"),
    ], card_title="report.html (ESL004 부분)")


# ================================================================ 뷰어
def viewer():
    SEC = "보고서 뷰어"
    shot_steps(SEC, "첫 화면", [
        "오른쪽 위: 언어(한국어 · English) · 테마(밝게 · 어둡게) · 도움말 · 설정",
        "처음 열면 안내 띠가 보임. 내 파일 없이 '예시 보고서'로 모든 기능을 둘러볼 수 있음",
        "새로고침해도 마지막 보고서 · 기준선 · 판정 · 탭이 다시 열림 (이 브라우저 IndexedDB)",
    ], "Home", M("Home", 5, 6, 7, 4, 8), [
        ("예시 보고서", "합성 장비 코드에 심은 버그 16개를 규칙 11개가 찾은 결과"),
        ("SARIF 열기", "파일을 고르거나 페이지에 끌어다 놓습니다"),
        ("예시 기준선", "같은 코드를 규칙 세트 v1 로 돌린 결과 (비교용)"),
        ("처음 안내", "[닫기]로 다시 보지 않기"),
        ("SARIF 만드는 법", "CLI · dotnet build 명령"),
    ], card_title="첫 화면")

    shot_steps(SEC, "결과 목록 · 근거 줄", [
        "왼쪽 목록: 규칙 · 수준(경고 / Shadow) · 파일:줄 · 메시지. 왼쪽 띠 색이 수준",
        "오른쪽: 메시지와 근거 줄(앞뒤 2줄, 해당 줄 강조), 그 아래 규칙 설명",
        "규칙 칩을 누르면 그 규칙만, 다시 누르면 전체",
    ], "Findings", M("Findings", 1, 3, 4, 5, 6), [
        ("검색", "파일 · 메시지 · 규칙 Id 로 거릅니다 (수준 · 기준선 상태 필터도)"),
        ("규칙 칩", "규칙별 개수. 'Shadow' 표시는 빌드를 막지 않는 규칙"),
        ("고른 결과", "↑ ↓ 로 이동"),
        ("메시지", "무엇이 · 왜 · 어떻게 고치나"),
        ("근거 줄", "CLI SARIF 에만 들어 있습니다"),
    ], card_title="결과 탭", desc_size=9.5)

    shot_steps(SEC, "규칙 설명 · 판정", [
        "모든 결과에 '장비에서 어떻게 멈추나 · 고치는 법 · 같은 패턴을 보는 다른 분석기 · 재현 테스트'가 붙음",
        "판정(맞음 / 오탐)은 Shadow 규칙의 오탐률 계산에 쓰이고, 결과 지문으로 저장돼 다시 열어도 남음",
        "단축키: 결과를 고른 뒤 T(맞음) · F(오탐)",
    ], "RuleDoc", M("RuleDoc", 0, 1, 2, 3), [
        ("근거 줄", "강조한 줄이 결과 위치"),
        ("판정", "맞음 (진짜 위험) · 오탐 · 판정 지우기"),
        ("장비에서 어떻게 멈추나", "현장에서 보이는 증상까지"),
        ("고치는 법", "바꿀 API · 패턴"),
    ], tip="재현 테스트는 저장소에서 dotnet test --filter Repro_ESL004 처럼 돌립니다", card_title="상세")


def flow_overview():
    s = new_slide()
    header(s, "현장 검증 절차", "4단계 한눈에", [
        "규칙을 바꾸기 전에 같은 코드로 확인하고, 새 규칙은 옆에서 먼저 돌려 보고, 언제든 되돌린다",
        "뷰어의 탭 '비교 · Shadow · 전환 · 규칙 세트 이력'이 이 순서를 따름 (← → 로 탭 이동)",
        "규칙 세트 = .editorconfig 한 덩어리. NuGet 분석기와 CLI 가 같은 키를 읽음",
    ])
    st = [
        ("재현", "버그를 심은 합성 코드로", ["예시 보고서 (16개 · 11규칙)", "규칙별 재현 테스트 11개", "고친 짝 코드에선 0건"]),
        ("비교", "같은 코드, 전 / 후", ["기준선 SARIF 열기", "새로 생김 · 그대로 · 사라짐", "CLI --baseline 게이트"]),
        ("Shadow", "새 규칙은 참고로만", ["suggestion 으로 시작", "결과마다 맞음 / 오탐", "규칙별 오탐률"]),
        ("전환 · 롤백", "기준을 넘으면 전환", ["판정 10건 · 오탐 10% 이하", ".editorconfig 버전 쌓기", "이전 버전으로 한 번에"]),
    ]
    w, gap = 2.85, 0.31
    for i, (hd, sub, items) in enumerate(st):
        x = 0.5 + i * (w + gap)
        rbox(s, x, 2.6, w, 3.9, fill=WHITE, line=BLUE if i == 0 else LINE, radius=0.04, lw=1.5)
        num_circle(s, x + 0.2, 2.8, i + 1, 0.42, 14)
        text(s, x + 0.75, 2.8, w - 0.9, 0.42, hd, 17, True, anchor=MSO_ANCHOR.MIDDLE)
        text(s, x + 0.2, 3.4, w - 0.4, 0.6, sub, 11.5, True, SUB)
        for j, it in enumerate(items):
            box(s, x + 0.25, 4.22 + j * 0.55, 0.07, 0.07, fill=BLUE, shape=MSO_SHAPE.OVAL)
            text(s, x + 0.4, 4.08 + j * 0.55, w - 0.55, 0.5, it, 11)
        if i < 3:
            box(s, x + w + 0.03, 4.3, gap - 0.06, 0.3, fill=BLUE, shape=MSO_SHAPE.RIGHT_ARROW)


def verify():
    SEC = "현장 검증 절차"
    shot_steps(SEC, "① 재현", [
        "예시 보고서 = samples/Equipment/Bad 에 심은 버그 16개(규칙 11개 모두)를 분석한 결과. 고친 짝 Good 은 0건",
        "규칙마다 실제로 멈추는 것을 보여주는 재현 테스트가 있음 (예: 응답 없는 TCP 장비 읽기가 1초 넘게 막힘)",
        "내 코드의 과거 Down 지점은 그 파일을 분석한 SARIF 를 저장해 두고 재현 세트로 씀",
    ], "Status", M("Status", 0, 1, 2, 3), [
        ("열린 보고서", "도구 · 버전 · 파일 이름"),
        ("결과 수", "규칙 결과만 셉니다 (다른 분석기 결과는 숨김)"),
        ("분석 시간", "CLI SARIF 에만 들어 있습니다"),
        ("SARIF 만드는 법", "CLI · dotnet build 명령"),
    ], tip="재현 테스트: dotnet test --filter \"FullyQualifiedName~ReproTests\" (11개, 약 10초)", card_title="예시 보고서")

    shot_steps(SEC, "② 비교", [
        "기준선 SARIF 를 열면 결과 지문으로 '새로 생김 · 그대로 · 사라짐'을 나눔. 코드 줄이 움직여도 같은 결과로 봄",
        "예시 기준선은 같은 코드를 규칙 세트 v1(Shadow 규칙 · ESL002 끔)으로 돌린 것 → 6건 새로 생김",
        "결과 탭의 '새 결과만' 필터로 새로 생긴 것만 보기. CI 에서는 equip-sw-lint --baseline 으로 같은 판단",
    ], "Compare", M("Compare", 0, 1, 2), [
        ("요약", "새로 생김 · 그대로 · 사라짐 (고쳐짐 · 규칙 끔)"),
        ("바뀐 규칙", "기준선 → 현재 개수가 바뀐 행을 강조"),
        ("목록", "새로 생긴 결과와 사라진 결과"),
    ], tip="코드를 고친 전/후도 같은 방법으로 비교합니다 (같은 폴더에서 다시 분석)", card_title="비교 탭")

    shot_steps(SEC, "③ Shadow", [
        "Shadow 규칙(ESL005 · 006 · 007 · 010 · 011)은 suggestion 으로 알리고 빌드를 막지 않음",
        "결과마다 맞음 / 오탐을 판정하면 규칙별 판정 수 · 오탐률이 쌓임 (결과 지문 기준, 백업 가능)",
        "전환 기준(기본): 판정 10건 이상 · 오탐률 10% 이하. 설정에서 바꿈",
    ], "Shadow", M("Shadow", 0, 1, 2, 3), [
        ("전환 기준", "판정 수 · 최대 오탐률"),
        ("규칙별 판정", "결과 · 판정(맞음 / 오탐) · 오탐률"),
        ("진행 막대", "기준 판정 수까지"),
        ("전환 버튼", "기준을 넘어야 켜집니다"),
    ], tip="'판정 안 한 결과'를 펼쳐 누르면 결과 탭의 그 결과로 갑니다", card_title="Shadow · 전환 탭")

    shot_steps(SEC, "④ 전환 · 롤백", [
        "기준을 넘은 규칙의 [적용으로 전환]을 누르면 규칙 세트가 새 버전이 되고 .editorconfig 가 바뀜",
        "바뀐 .editorconfig 를 복사 · 내려받아 저장소에 넣으면 NuGet 분석기 · CLI 가 같은 단계로 동작",
        "[이 버전으로 되돌리기]는 그 버전 설정을 새 버전으로 쌓음. 이력은 지워지지 않음",
    ], "History", M("History", 0, 1, 2, 4, 5), [
        ("현재 규칙 세트", "버전 번호"),
        (".editorconfig", "지금 단계를 그대로 옮길 블록"),
        ("복사 · 내려받기", "저장소의 .editorconfig 에 넣습니다"),
        ("변경 이력", "버전 · 시각 · 바뀐 규칙 · 메모"),
        ("되돌리기", "롤백도 새 버전으로 남습니다"),
    ], tip="화면의 예시는 기준을 판정 2건으로 낮춰 찍었습니다 (설정 → Shadow 전환 기준)", card_title="규칙 세트 이력 탭", desc_size=9.5)


def rules_table():
    s = new_slide()
    header(s, "규칙 · 내 데이터", "규칙 11개", [
        "적용 = warning (빌드 · CI 게이트에 걸림), Shadow = suggestion (참고, 판정 뒤 전환)",
        "ESL002 · ESL007 은 다른 분석기와 겹침 (SonarQube S6561, PVS-Studio V3223 · Infer#). 나머지는 조사한 분석기에서 찾지 못함 (2026-11)",
        "async void · .Result 교착 · Thread.Abort 는 VSTHRD · CA 규칙이 하므로 넣지 않음",
    ])
    rows = [
        ("ESL001", "적용", "TickCount · GetTickCount 비교 · 형 넓힘 · 나눗셈", "24.9일 뒤 타임아웃 오동작"),
        ("ESL002", "적용", "DateTime.Now/UtcNow 로 잰 간격 · 기한", "시각 동기화로 음수 · 큰 값"),
        ("ESL003", "적용", "막히는 주기 타이머 콜백에 재진입 막기 없음", "콜백 겹침, 포트 경합"),
        ("ESL004", "적용", "타임아웃 없는 시리얼 · TCP 읽기, 토큰 없는 비동기 읽기", "장비 무응답 때 알람 없이 멈춤"),
        ("ESL005", "Shadow", "타임아웃 없는 WaitOne · Join · Monitor.Wait · WaitForExit", "신호를 놓치면 멈춤 · 종료 안 됨"),
        ("ESL006", "Shadow", "설정 · 레시피 파일 제자리 덮어쓰기", "전원 차단 뒤 파일 깨짐"),
        ("ESL007", "Shadow", "콜백이 쓰고 UI 가 락 없이 읽는 버퍼", "섞인 프레임 · IndexOutOfRange"),
        ("ESL008", "적용", "콜백이 채우는 큐에 크기 제한 없음", "하루 뒤 메모리 초과"),
        ("ESL009", "적용", "lock 안의 동기 Control.Invoke · Dispatcher.Invoke · SendMessage", "창 메시지 교착"),
        ("ESL010", "Shadow", "수신 · 스레드를 시작한 뒤 처리기가 쓰는 필드 초기화", "재시작 때 첫 메시지 유실"),
        ("ESL011", "Shadow", "기한 없는 Sleep 폴링 루프", "센서 이상 때 시퀀스 멈춤"),
    ]
    x0, y0, rh = 0.5, 2.5, 0.38
    cols = [(0.0, 1.0, "Id"), (1.0, 0.9, "단계"), (1.9, 6.4, "찾는 것"), (8.3, 4.03, "장비에서")]
    rbox(s, x0, y0 - 0.05, 12.33, rh * (len(rows) + 1) + 0.1, fill=CARD)
    for cx, cw, ct in cols:
        text(s, x0 + cx + 0.1, y0, cw, rh, ct, 11, True, SUB, anchor=MSO_ANCHOR.MIDDLE)
    for i, r in enumerate(rows):
        yy = y0 + rh * (i + 1)
        box(s, x0 + 0.1, yy, 12.13, 0.01, fill=LINE)
        for j, (cx, cw, _) in enumerate(cols):
            col = BLUE if j == 0 else (NOTE if r[1] == "Shadow" and j == 1 else INK)
            text(s, x0 + cx + 0.1, yy, cw, rh, r[j], 11, j in (0, 1), col, anchor=MSO_ANCHOR.MIDDLE, font=MONO if j == 0 else FONT)


def data():
    SEC = "규칙 · 내 데이터"
    shot_steps(SEC, "읽을 수 있는 SARIF", [
        "CLI SARIF: 상대 경로 · 근거 줄 앞뒤 · 결과 지문 · 분석 시간이 들어 있음 (권장)",
        "dotnet build SARIF (ErrorLog, version=2.1): 절대 경로를 공통 폴더 기준으로 줄이고, 코드 조각 없이 줄 번호만 보임",
        "다른 분석기(CA · CS 등) 결과가 섞여 있어도 ESL 결과만 보여주고 나머지 개수는 '숨김'으로 표시",
    ], "BuildSarif", M("BuildSarif", 0, 1, 2), [
        ("파일", "열린 SARIF 이름"),
        ("경로 안내", "dotnet build SARIF 라 경로를 줄였다는 표시"),
        ("근거 줄 없음", "CLI 로 만들면 앞뒤 줄이 함께 들어갑니다"),
    ], tip="SARIF 는 IDE · GitHub 코드 스캐닝도 읽는 표준 형식(2.1.0)입니다", card_title="dotnet build SARIF 를 연 화면")

    s = new_slide()
    header(s, SEC, "오류 메시지", [
        "잘못된 파일을 열어도 지금 보던 보고서는 그대로. 무엇이 왜 안 되는지 빨간 띠로 알림",
        "JSON 이 깨졌으면 몇 번째 줄 몇 번째 글자 근처인지, SARIF 가 아니면 무엇이 빠졌는지",
        "CLI: 경로 · 옵션 · 기준선 파일 오류는 종료 코드 2 와 한 줄 설명",
    ])
    for i, (title, name) in enumerate([("깨진 JSON", "ErrJson"), ("ESL 결과가 없는 SARIF", "ErrNoEsl")]):
        e = MARKS[name][0]
        shot_card(s, 0.5, CY + i * 2.35, 6.05, 2.15, title, name, M(name, 0), crop=(e[0] - 20, e[1] - 70, e[0] + 760, e[3] + 40))
    rbox(s, 6.78, CY, 6.05, CH)
    text(s, 6.93, CY + 0.1, 5.8, 0.32, "메시지와 할 일", 13, True)
    msgs = [
        ("JSON 형식이 아닙니다: N번째 줄 M번째 글자 근처", "파일이 잘렸거나 다른 형식입니다. 다시 만드세요."),
        ("runs 배열이 없습니다", "SARIF 2.1.0 파일인지 확인하세요."),
        ("SARIF 2.x 만 읽습니다", "dotnet build 에는 ErrorLog=out.sarif%2Cversion=2.1"),
        ("ESL 규칙 결과가 없습니다 (다른 결과 N개 숨김)", "분석기가 켜져 있는지, severity = none 이 아닌지"),
        ("CLI: Path not found · Unknown option", "경로 · 옵션 철자 (종료 코드 2)"),
        ("CLI: N unresolved type references", "벤더 SDK 형식을 못 찾음 → --ref <DLL 폴더>"),
    ]
    for i, (m, a) in enumerate(msgs):
        yy = CY + 0.55 + i * 0.64
        text(s, 7.0, yy, 5.7, 0.3, m, 10.5, True, BLUE)
        text(s, 7.0, yy + 0.28, 5.7, 0.32, a, 10, False, SUB)

    s = new_slide()
    header(s, SEC, "CI 게이트 (GitHub Actions)", [
        "기존 결과는 기준선 SARIF 로 묶고, 새로 생긴 결과만 작업을 실패시킴 (도입 첫날부터 빨간 빌드가 되지 않음)",
        "보고서(SARIF · HTML)는 아티팩트로 올리고, 공개 저장소 · GHAS 는 코드 스캐닝 알림으로도 볼 수 있음",
        "예제: 저장소의 .github/workflows/lint-example.yml",
    ])
    code_card(s, 0.5, CY, 7.4, CH, ".github/workflows/equip-sw-lint.yml", [
        "jobs:",
        "  lint:",
        "    runs-on: windows-latest",
        "    steps:",
        "      - uses: actions/checkout@v4",
        "      - uses: actions/setup-dotnet@v4",
        "        with: { dotnet-version: 8.0.x }",
        "      - run: dotnet tool install -g equip-sw-lint",
        "      - run: >",
        "          equip-sw-lint ./src",
        "          --baseline .equip-sw-lint/baseline.sarif",
        "          --fail-on warning",
        "          --sarif r.sarif --html r.html",
        "      - uses: actions/upload-artifact@v4",
        "        if: always()",
        "        with: { name: lint, path: \"r.*\" }",
    ], 10)
    steps_card(s, 8.1, CY, 4.73, CH, [
        ("기준선 만들기", "equip-sw-lint ./src --fail-on none --sarif .equip-sw-lint/baseline.sarif 를 커밋"),
        ("PR 마다", "새 결과가 warning 이상이면 종료 코드 1 → 작업 실패"),
        ("고치면", "해결된 결과는 '해결 N개'로 셈. 기준선은 가끔 다시 만들기"),
        ("Shadow 전환", "뷰어에서 바꾼 .editorconfig 를 커밋하면 CI 도 같은 단계"),
    ], title="운영", desc_size=10)

    shot_steps(SEC, "개인정보 · 저장 · LLM", [
        "분석은 내 PC · 내 CI 안에서만. 보고서 뷰어도 고른 파일을 브라우저 안에서만 읽고 서버로 보내지 않음",
        "LLM 을 쓰지 않음 (규칙 문서 · 재현 테스트로 설명). 외부 오류 수집도 하지 않음",
        "판정 · 규칙 세트 이력 · 설정 · 열어 둔 보고서는 이 브라우저(IndexedDB)에만. JSON 백업 · 복원 · 전체 삭제",
    ], "Settings", M("Settings", 0, 1, 2, 3, 4, 5), [
        ("최소 판정 수", "Shadow 전환 기준 (기본 10)"),
        ("최대 오탐률", "기본 10%"),
        ("백업 내보내기", "판정 · 이력 · 설정 (SARIF 본문 제외)"),
        ("백업 가져오기", "다른 PC · 브라우저로 옮기기"),
        ("모든 데이터 지우기", "이 브라우저에서 삭제"),
        ("오류 기록", "이 기기에만 저장, 최근 200건"),
    ], tip="사생활 보호 창처럼 저장소를 못 쓰면 화면 아래에 '새로고침하면 사라짐'을 알립니다", card_title="설정", desc_size=9.5)


def appendix():
    s = new_slide()
    header(s, "부록", "영어 · 다크 · 모바일 · 키보드", [
        "오른쪽 위에서 한국어 · English 를 바꾸면 화면 문구 · 규칙 설명이 바뀌고, 예시 보고서는 그 언어 SARIF 로 다시 열림",
        "테마 버튼으로 밝게 · 어둡게 (처음엔 기기 설정). 휴대폰 폭에서는 목록과 상세가 위아래로",
        "CLI · 분석기 메시지 언어는 --lang 또는 빌드 · IDE UI 언어",
    ])
    path, _, _ = load_shot("EnFindings")
    rbox(s, 0.5, CY, 6.6, CH)
    text(s, 0.65, CY + 0.1, 6, 0.32, "English", 13, True)
    s.shapes.add_picture(path, Inches(0.7), Inches(CY + 0.5), Inches(6.2), Inches(3.875))
    p2, _, _ = load_shot("MobileDark", dip=(390, 844))
    rbox(s, 7.3, CY, 2.3, CH)
    text(s, 7.45, CY + 0.1, 2.0, 0.32, "모바일 · 다크", 13, True)
    s.shapes.add_picture(p2, Inches(7.55), Inches(CY + 0.5), Inches(1.8), Inches(1.8 * 1266 / 585))
    rbox(s, 9.8, CY, 3.03, CH)
    text(s, 9.95, CY + 0.1, 2.8, 0.32, "단축키", 13, True)
    keys = [("← →", "탭 이동"), ("↑ ↓", "결과 이동"), ("T", "판정: 맞음"), ("F", "판정: 오탐"), ("Tab", "다음 버튼 · 입력"), ("Esc", "대화상자 닫기")]
    for i, (k, d) in enumerate(keys):
        yy = CY + 0.6 + i * 0.58
        chip = rbox(s, 9.95, yy, 1.0, 0.38, fill=INK, radius=0.2)
        text(s, 0, 0, 0, 0, k, 10, True, WHITE, PP_ALIGN.CENTER, MSO_ANCHOR.MIDDLE, shape=chip, font=MONO)
        text(s, 11.05, yy, 1.75, 0.38, d, 10.5, False, INK, anchor=MSO_ANCHOR.MIDDLE)


def faq():
    s = new_slide()
    header(s, "부록", "FAQ · 문제 해결", [
        "대부분은 .editorconfig 단계 조정과 CLI 의 --ref · --exclude 로 해결. 뷰어 도움말 창에도 같은 내용",
        "오탐이면 그 결과를 '오탐'으로 판정하고, 줄 단위로 끄려면 #pragma warning disable ESLxxx",
        "공개 C# 저장소 측정 결과와 알려진 오탐 모양은 저장소 docs/measurements 에 있음",
    ])
    qa = [
        ("빌드에 경고가 안 나와요", "Shadow 규칙은 suggestion 이라 IDE · SARIF 에만 보입니다. 적용 규칙도 안 보이면 .editorconfig 에서 severity = none 이 아닌지 보세요."),
        ("형식을 못 찾는다고 나와요 (CLI)", "벤더 SDK DLL 폴더를 --ref 로 주세요. 못 찾은 형식의 규칙은 건너뛰므로 결과가 줄 뿐 오탐이 늘지는 않습니다."),
        ("테스트 · 생성 코드는 빼고 싶어요", "CLI 는 --exclude 폴더이름, 빌드는 .editorconfig 의 [**/Tests/**.cs] 구역에서 severity = none."),
        ("ESL004 인데 실제로는 타임아웃이 있어요", "타임아웃을 다른 형식(래퍼 클래스)에서 정하면 보이지 않습니다. 같은 형식에서 ReadTimeout 을 정하거나 그 줄을 억제하세요."),
        ("ESL007 인데 호출하는 쪽이 lock 을 잡아요", "알려진 한계입니다 (호출자 lock 은 보지 않음). 오탐으로 판정하세요. 이 규칙이 Shadow 인 이유입니다."),
        ("한글 주석이 깨져 보여요", "CLI 는 UTF-8 이 아니면 CP949 로 읽습니다. 다른 인코딩이면 UTF-8 로 저장하세요."),
        ("기준선이 매번 새 결과로 잡혀요", "같은 폴더를 기준으로 분석해야 경로가 맞습니다. 줄 내용이 바뀐 결과는 새 결과로 봅니다."),
        ("Visual Studio 2017 에서 되나요?", "아니요. Roslyn 3.11 (VS 2019 16.11 · .NET 5 SDK) 이후가 필요합니다. 오래된 IDE 는 CLI 를 쓰세요."),
    ]
    cw, ch = 6.05, 1.02
    for i, (q, a) in enumerate(qa):
        col, row = i % 2, i // 2
        x = 0.5 + col * (cw + 0.23)
        y = CY + row * (ch + 0.13)
        rbox(s, x, y, cw, ch)
        text(s, x + 0.15, y + 0.08, 0.4, 0.32, "Q", 13, True, BLUE)
        text(s, x + 0.5, y + 0.08, cw - 0.65, 0.32, q, 12, True)
        text(s, x + 0.5, y + 0.42, cw - 0.65, 0.56, a, 9.5, False, SUB)


def back():
    s = new_slide()
    s.background.fill.solid()
    s.background.fill.fore_color.rgb = NAVY
    code_lines(s, 3.6, 2.0, 6.2, 1.9)
    s.shapes.add_picture(os.path.join(ASSETS, "icon.png"), Inches(6.2), Inches(0.9), Inches(0.9), Inches(0.9))
    text(s, 0, 4.4, 13.333, 0.6, NAME, 30, True, WHITE, PP_ALIGN.CENTER)
    text(s, 0, 5.05, 13.333, 0.4, "장시간 운영 규칙 11개 · 빌드 없는 CLI · 기준선 게이트 · Shadow 전환", 13, False, RGBColor(0xC9, 0xD3, 0xE0), PP_ALIGN.CENTER)
    text(s, 0, 5.7, 13.333, 0.6, [f"v{VERSION}   ·   만든 사람 배민혁   ·   MIT License   ·   © 2026",
                                  "포함: Pretendard (SIL OFL 1.1) · 예제 코드와 화면은 모두 합성 데이터 · 오탐 측정은 MIT 공개 저장소"],
         10, False, RGBColor(0x8A, 0x97, 0xA8), PP_ALIGN.CENTER)


def main():
    cover()            # 1
    toc()              # 2
    intro()            # 3
    install()          # 4
    tools()            # 5–7
    viewer()           # 8–10
    flow_overview()    # 11
    verify()           # 12–15
    rules_table()      # 16
    data()             # 17–20
    appendix()         # 21
    faq()              # 22
    back()             # 23
    assert state["n"] == TOTAL, state["n"]
    prs.save(OUT)
    print("saved", OUT)
    if "--pdf" in sys.argv:
        pdf = OUT[:-5] + ".pdf"
        ps = (f"$p = New-Object -ComObject PowerPoint.Application; $d = $p.Presentations.Open('{OUT}', $true, $false, $false); "
              f"$d.SaveAs('{pdf}', 32); $d.Close(); $p.Quit()")
        import subprocess
        subprocess.run(["powershell", "-NoProfile", "-Command", ps], check=True)
        print("saved", pdf)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
