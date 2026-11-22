# 장비 SW 위험 코드 린트 (equip-sw-lint)

장비 PC 에서 몇 주씩 도는 C# 설비 SW 에서 **실제로 Down 을 낸 코드 모양**을 커밋 전에 찾는 Roslyn 분석기입니다.
빌드에 붙이는 NuGet 분석기, 빌드 없이 폴더를 분석하는 `dotnet tool` CLI, 결과(SARIF)를 근거 줄 · 장비에서 멈추는 방식 · 고치는 법과 함께 보는 브라우저 보고서 뷰어로 되어 있습니다.

- 보고서 뷰어: https://mhbae-semi-lab.github.io/equip-sw-lint/viewer/ (설치 없음, 예시 보고서 버튼)
- 패키지 (nuget.org): [EquipSwLint.Analyzers](https://www.nuget.org/packages/EquipSwLint.Analyzers) (분석기), [equip-sw-lint](https://www.nuget.org/packages/equip-sw-lint) (dotnet tool)

[English summary](#english)

## 1. 관점

범용 분석기는 코드를 문법 · API 사용법으로 봅니다. 장비 SW 의 Down 은 **시간이 지나야** 드러나는 것이 많습니다. PC 가동 24.9일 뒤 넘어가는 32bit 틱, 호스트와 시각을 맞출 때 건너뛰는 벽시계, 장비가 응답을 멈췄을 때 영원히 기다리는 시리얼 읽기, 촬영 콜백과 화면이 락 없이 같이 쓰는 영상 버퍼, 재시작 때 처리 루프보다 먼저 온 첫 메시지 같은 것들입니다. 이 분석기는 **설비 통신 · 타이머 · 영상 콜백 · 레시피 파일 · UI 스레드**를 기본 개념으로 두고 그 모양을 찾습니다.

## 2. 해결

규칙 11개 (Shadow 는 빌드를 막지 않고 참고로만 알리는 단계):

| Id | 단계 | 찾는 것 | 장비에서 어떻게 멈추나 |
|---|---|---|---|
| ESL001 | 적용 | `Environment.TickCount` · `GetTickCount()` 비교 · 형 넓힘 · 나눗셈 | 24.9일 뒤 타임아웃이 바로 나거나 영영 안 남 |
| ESL002 | 적용 | `DateTime.Now/UtcNow` 로 잰 간격 · 기한 | 시각 동기화로 경과 시간이 음수 · 큰 값 |
| ESL003 | 적용 | 막히는 일을 하는 주기 타이머 콜백에 재진입 막기 없음 | 콜백이 겹쳐 같은 포트에 두 스레드가 말을 검 |
| ESL004 | 적용 | 타임아웃 없는 시리얼 · TCP 읽기, 토큰 없는 비동기 읽기 | 케이블 · 컨트롤러 이상 때 알람 없이 멈춤 |
| ESL005 | Shadow | 타임아웃 없는 `WaitOne()` · `Join()` · `Monitor.Wait` · `WaitForExit()` | 신호 하나를 놓치면 시퀀스 · 종료가 멈춤 |
| ESL006 | Shadow | 설정 · 레시피 파일 제자리 덮어쓰기 | 쓰는 중 전원 차단 → 다음 시작에서 못 읽음 |
| ESL007 | Shadow | 콜백 스레드가 쓰고 UI 가 락 없이 읽는 버퍼 | 두 프레임이 섞인 화면, IndexOutOfRange |
| ESL008 | 적용 | 콜백이 채우는 큐에 크기 제한 없음 | 처리가 조금 느리면 하루 뒤 메모리 초과 |
| ESL009 | 적용 | lock 안의 동기 `Control.Invoke` · `Dispatcher.Invoke` · `SendMessage` | 창 메시지 교착 (화면 얼어붙음) |
| ESL010 | Shadow | 수신 · 스레드를 시작한 뒤에 처리기가 쓰는 필드 초기화 | 재시작 때 가끔 첫 메시지 유실 · NullReference |
| ESL011 | Shadow | 기한 없는 `while (!ready) Sleep(..)` 폴링 | 센서 · IO 이상 때 시퀀스가 알람 없이 멈춤 |

- 규칙마다 **합성 재현 코드**(`samples/Equipment/Bad`)와 고친 짝(`Good`), **실제로 멈추는 것을 보여주는 재현 테스트**(`ReproTests.Repro_ESLxxx_*`)가 있습니다. 예: 타이머 콜백이 겹치는 것을 실제 `System.Threading.Timer` 로 재고, 응답 없는 TCP 장비에 대한 읽기가 1초 넘게 막히는 것을 루프백 소켓으로 보여줍니다.
- 기존 경고는 **기준선**(이전 SARIF)으로 묶고 새 결과만 빌드를 막습니다 (`--baseline`). 코드 줄이 움직여도 같은 결과로 봅니다.
- 현장 검증 절차: **재현**(예시 보고서 · 재현 테스트) → **비교**(같은 코드의 규칙 세트 전/후, 기준선) → **Shadow**(새 규칙은 suggestion 으로 시작, 결과마다 맞음/오탐 판정) → **전환 · 롤백**(오탐률이 기준 아래면 warning 으로 전환, `.editorconfig` 규칙 세트를 버전으로 쌓고 되돌림).

## 3. 상용 비교 · 차별점 (조사 2026-11-22)

규칙 원본(sonar-dotnet rspec, Meziantou docs, dotnet/sdk NetAnalyzers, vs-threading, Roslynator, AsyncFixer, github/codeql C# 쿼리)을 받아 키워드로 대조하고, PVS-Studio · Klocwork C# 진단 목록과 웹 문서를 확인했습니다. Coverity 의 C# 체커 목록은 공개 문서에서 끝까지 확인하지 못했습니다.

| 규칙 | 같은 패턴을 하는 제품 |
|---|---|
| ESL001 TickCount 32bit | 찾지 못함 (PVS-Studio V3217 은 일반 산술 오버플로) |
| ESL002 DateTime.Now 간격 | **SonarQube S6561** "Avoid using DateTime.Now for benchmarking or timing operations" |
| ESL003 타이머 재진입 | 찾지 못함 |
| ESL004 통신 읽기 타임아웃 | 찾지 못함 |
| ESL005 무기한 대기 | 찾지 못함 (`Task.Wait`/`.Result` 교착은 VSTHRD002 · S4462 · MA0045 가 함, 이 규칙에서 제외) |
| ESL006 비원자적 파일 쓰기 | 찾지 못함 (S5445 · Datadog 은 임시 파일 보안) |
| ESL007 락 없는 공유 버퍼 | **PVS-Studio V3223, Infer# RacerD, Amazon Q Thread Safety Violation** (일반 데이터 레이스) |
| ESL008 무제한 큐 | 찾지 못함 |
| ESL009 lock 안 동기 Invoke | Klocwork CS.BANNED.INVOKE 는 동기 `Control.Invoke` 전체를 금지 (lock 여부는 보지 않음) |
| ESL010 시작 레이스 | 찾지 못함 (PVS V3128 은 생성자 안 필드 사용 순서) |
| ESL011 기한 없는 폴링 | 찾지 못함 (PVS V3120 · V3032 는 조건이 안 바뀌는 루프 · non-volatile 대기) |
| 기준선 · Shadow 단계 | Qodana baseline, SonarQube 새 코드 기준, Semgrep 규칙 모드(Monitor / Comment / Block) |
| LLM 코드 리뷰 | CodeRabbit · Copilot 코드 리뷰는 레이스 · 누수를 찾는다고 하지만 규칙 단위로 보장하지 않음 |

출처: [sonar-dotnet rspec](https://github.com/SonarSource/sonar-dotnet) · [S6561](https://rules.sonarsource.com/csharp/RSPEC-6561) · [Meziantou.Analyzer](https://github.com/meziantou/Meziantou.Analyzer) · [.NET 분석기 (CA)](https://github.com/dotnet/sdk/blob/main/src/Microsoft.CodeAnalysis.NetAnalyzers/src/Microsoft.CodeAnalysis.NetAnalyzers.md) · [VSTHRD](https://github.com/microsoft/vs-threading/tree/main/doc/analyzers) · [Roslynator](https://github.com/dotnet/roslynator) · [AsyncFixer](https://github.com/semihokur/AsyncFixer) · [CodeQL C# 동시성](https://github.com/github/codeql/tree/main/csharp/ql/src/Concurrency) · [PVS-Studio 진단 목록](https://pvs-studio.com/en/docs/warnings/) · [Klocwork C# 체커](https://help.klocwork.com/2024/en-us/concepts/csharpcheckerreference.htm) · [CS.BANNED.INVOKE](https://help.klocwork.com/2024/en-us/reference/cs.banned.invoke.htm) · [Infer#](https://github.com/microsoft/infersharp) · [Amazon Q C# 탐지기](https://docs.aws.amazon.com/amazonq/detector-library/csharp/severity/medium) · [Qodana baseline](https://www.jetbrains.com/help/qodana/baseline.html) · [Semgrep 규칙 모드](https://semgrep.dev/docs/semgrep-code/policies) · [CodeRabbit](https://www.coderabbit.ai/)

**이 분석기가 다른 점** (조사한 분석기 중에서 찾지 못함, 2026-11 기준)

1. **장시간 운영에서만 드러나는 장비 SW 규칙** 9개(ESL001 · 003 · 004 · 005 · 006 · 008 · 009 · 010 · 011)를 규칙으로 냅니다. ESL009 는 Klocwork 처럼 동기 Invoke 전체를 막지 않고 lock 안의 호출만 봅니다. ESL002 · ESL007 은 다른 분석기와 겹치며, 장비 SW 에서 자주 나와서 함께 넣었습니다.
2. 규칙마다 **합성 재현 코드와 "이 코드가 장비에서 어떻게 멈추는가" 설명, 실제로 멈추는 것을 보여주는 재현 테스트**를 같이 둡니다.

비교 대상 분석기(VSTHRD, CA, SonarQube 등)를 대신하지 않습니다. 함께 쓰는 것을 전제로, 그 분석기들이 이미 하는 async void · `.Result` 교착 · Thread.Abort 같은 규칙은 넣지 않았습니다.

## 4. 설치 · 사용

### NuGet 분석기 (빌드 · IDE)

```
dotnet add package EquipSwLint.Analyzers
dotnet build -p:ErrorLog=report.sarif%2Cversion=2.1
```

- Visual Studio 2019 16.11 / .NET 5 SDK 이후의 컴파일러에서 동작합니다 (Roslyn 3.11 기준으로 빌드, .NET 5.0.408 SDK 로 확인).
- 메시지는 빌드 · IDE 의 UI 언어를 따라 한국어 또는 영어로 나옵니다 (`-p:PreferredUILang=ko-KR`).
- 단계 바꾸기 (`.editorconfig`):
  ```ini
  [*.cs]
  dotnet_diagnostic.ESL010.severity = warning   # Shadow → 적용
  dotnet_diagnostic.ESL002.severity = none      # 끄기
  ```

### dotnet tool (빌드 없이)

장비 코드는 벤더 SDK DLL 이 CI 에 없어 빌드가 안 되는 경우가 많아, `.cs` 파일을 바로 분석하는 CLI 를 따로 둡니다.

```
dotnet tool install -g equip-sw-lint
equip-sw-lint ./src --sarif report.sarif --html report.html
equip-sw-lint ./src --baseline last.sarif --fail-on warning      # 새 결과만 실패 (종료 코드 1)
equip-sw-lint ./src --ref "C:\Vendor\SDK\bin" --exclude tests     # 벤더 DLL 로 형식 찾기, 폴더 빼기
equip-sw-lint rules                                               # 규칙 목록
```

- 오래된 NuGet 설정이 `https://www.nuget.org/api/v2/` 만 쓰면 패키지를 못 찾을 수 있습니다. 그때는 `--add-source https://api.nuget.org/v3/index.json` 을 붙입니다.
- 분석 폴더의 `.editorconfig` · `.globalconfig` 에서 `dotnet_diagnostic.ESLxxx.severity` 를 읽습니다.
- 소스 인코딩: UTF-8, 아니면 CP949(한국어 ANSI)로 읽습니다.
- 종료 코드: 0 통과, 1 `--fail-on` 이상 새 결과 있음, 2 사용법 · 입력 오류.
- GitHub Actions 예: [`.github/workflows/lint-example.yml`](.github/workflows/lint-example.yml) (기준선 게이트 + SARIF · HTML 아티팩트).

### 보고서 뷰어

- 웹 주소에서 열거나 브라우저의 "앱 설치"로 설치합니다. 한 번 열면 오프라인에서도 열립니다.
- 로컬 실행 (빌드 없음): 저장소 루트에서 `python -m http.server 8741 --bind 127.0.0.1` → `http://127.0.0.1:8741/viewer/`
- CLI SARIF(근거 줄 앞뒤 포함)와 `dotnet build` SARIF(줄 번호만)를 모두 엽니다. 잘못된 파일은 몇 번째 줄 몇 번째 글자인지 보여줍니다.
- 판정 · 규칙 세트 이력 · 설정은 이 브라우저(IndexedDB)에만 저장되고 JSON 으로 백업 · 복원합니다. 서버로 보내지 않습니다.
- 사용자 매뉴얼: [docs/manual/Equip-SW-Lint-User-Manual-ko.pdf](docs/manual/Equip-SW-Lint-User-Manual-ko.pdf)

### 개발

```
dotnet build EquipSwLint.sln -c Release          # 경고를 오류로
dotnet test EquipSwLint.sln -c Release           # 분석기 · 샘플 · 재현 · CLI 테스트
node --test viewer/test/viewer.test.js           # 뷰어 테스트 (Node 20+)
dotnet run --project src/EquipSwLint.Cli -- rules --json > viewer/rules.json   # 규칙 문서 갱신 (테스트가 일치 확인)
dotnet pack src/EquipSwLint.Analyzers -c Release -o artifacts
dotnet pack src/EquipSwLint.Cli -c Release -o artifacts
```

예시 SARIF 다시 만들기는 [viewer/samples/README.md](viewer/samples/README.md) 에 있습니다.

## 5. 측정값

| 항목 | 결과 |
|---|---|
| 합성 예제의 심은 버그 16개 (11개 규칙 모두) | 16/16 탐지, 고친 코드(`Good`) 오탐 0, 계획 밖 결과 0 |
| 테스트 | xUnit 44개 (분석기 25 · 재현 11 · CLI 7 · 규칙 1), 뷰어 node 6개 |
| 공개 C# 저장소 오탐 (조정용 3곳: dotnet/iot · NModbus · MQTTnet, 24만 줄) | 처음 규칙 45건 중 오탐 12 (26.7%) → 오탐 모양 4가지를 고친 뒤 34건 중 1 (2.9%, 같은 코드라 낙관적) |
| 공개 C# 저장소 오탐 (조정에 안 쓴 2곳: NAudio · ScottPlot, 24만 줄) | 19건 중 오탐 8 (42%). Shadow 규칙(005 · 007 · 010)과 경로 추정 규칙(006)에서 나옴 → 006 을 Shadow 로 시작 |
| 분석 시간 (i7-11370H, CLI) | 18.9만 줄 12.5 ~ 13.2초, 9천 줄 1 ~ 1.5초, 예제 0.6초 |

자세한 표 · 결과별 판정: [docs/measurements/false-positives.md](docs/measurements/false-positives.md)

## 6. 한계

- 규칙은 정적 추정입니다. 스레드 문맥(콜백 · UI)은 이벤트 이름 · API · 같은 형식 안의 호출 한 단계로 판단하고, 다른 형식이나 호출자가 잡은 lock 은 보지 못합니다 (ScottPlot 오탐 사례).
- ESL006 은 경로 이름(`.json` · `recipe` · `config` 등)으로 설정 파일을 추정합니다. 그래서 Shadow 로 시작합니다.
- ESL003 · ESL004 · ESL008 · ESL009 는 공개 저장소 5곳에서 결과가 0건이라 실제 코드 정밀도 자료가 없습니다. 합성 예제 · 단위 테스트만 근거입니다.
- CLI 는 빌드하지 않으므로 프로젝트의 전처리 기호 · 참조를 모릅니다. 못 찾은 형식에 대한 규칙은 건너뜁니다 (`--ref` 로 DLL 폴더 지정). CLI 는 분석 폴더 바로 아래의 `.editorconfig` 전역 심각도만 읽습니다.
- 자동 코드 수정(code fix)은 넣지 않았습니다. 고치는 값(타임아웃 · 용량)은 장비마다 달라 안전한 기본값이 없습니다.
- Visual Studio 2019 IDE 자체에서 띄워 보지는 않았고, 같은 컴파일러 버전(.NET 5.0.408 SDK, Roslyn 3.11)으로 빌드해 확인했습니다.
- LLM 은 쓰지 않습니다.

## 7. 만든 방법

배민혁이 설계하고, 구현 · 테스트 · 문서 작성에 AI 코딩 도구(Claude Code)를 썼습니다. 규칙 예제와 화면은 모두 새로 쓴 합성 코드이며 실제 장비 코드와 관련이 없습니다. 오탐 측정은 MIT 라이선스 공개 저장소로만 했습니다.

라이선스: MIT. 포함한 서드파티: Pretendard (SIL OFL 1.1).

---

## English

**equip-sw-lint** is a set of Roslyn analyzers for C# software that runs for weeks on equipment PCs. It flags code shapes behind real field stops: 32-bit tick wrap after 24.9 days, wall-clock intervals, overlapping timer callbacks, serial/TCP reads with no timeout, untimed waits, in-place recipe writes, buffers shared by device callbacks and UI without a lock, unbounded callback queues, synchronous Invoke under a lock, fields initialized after receiving started, and polling loops with no deadline.

- NuGet analyzer for build/IDE (Roslyn 3.11+, VS 2019 16.11 / .NET 5 SDK and later), `dotnet tool` CLI that analyzes a folder **without building** (SARIF 2.1.0 + HTML, baseline gate), and a static, offline-capable report viewer (evidence lines, how each pattern stops equipment, baseline compare, Shadow-rule triage, versioned `.editorconfig` rule sets). English and Korean.
- Every rule ships with synthetic buggy/fixed code and a test that reproduces the failure at run time.
- Not found among the analyzers surveyed (Nov 2026): nine of the eleven rules. ESL002 overlaps SonarQube S6561; ESL007 overlaps general data-race checkers (PVS-Studio V3223, Infer#). Meant to run alongside VSTHRD/CA/Sonar, not to replace them.
- Measured on public MIT code: 26.7% → 2.9% false positives on the three tuning repositories, 42% on two held-out repositories (mostly Shadow rules). Details in `docs/measurements`.
- Built by 배민혁 with an AI coding assistant (Claude Code). MIT License.
