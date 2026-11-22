# Changelog

## 1.0.1 — 2026-11-22

### 추가
- CLI `--repo-root <폴더>`: SARIF · HTML · 콘솔의 파일 경로를 그 폴더(보통 저장소 루트) 기준으로 씁니다. GitHub code scanning 알림이 소스 줄로 바로 연결됩니다. 결과 지문 · 기준선은 영향을 받지 않습니다.

### 고침
- GitHub Actions 기준선 게이트 예제: code scanning 업로드 권한(`security-events: write`)을 주고 upload-sarif 를 v4 로 올림

## 1.0.0 — 2026-11-22

첫 버전. 장비 SW 위험 코드 규칙 11개, NuGet 분석기, 빌드 없는 CLI, 보고서 뷰어.

### 추가
- 규칙 11개: ESL001 TickCount 32bit 랩 · ESL002 벽시계 간격 · ESL003 타이머 콜백 재진입 · ESL004 통신 읽기 타임아웃 · ESL005 무기한 대기 · ESL006 비원자적 설정 파일 쓰기 · ESL007 락 없는 공유 버퍼 · ESL008 무제한 콜백 큐 · ESL009 lock 안 동기 Invoke · ESL010 시작 레이스 · ESL011 기한 없는 폴링
- 단계: 적용 6개(warning), Shadow 5개(suggestion, `.editorconfig` 로 전환)
- 메시지 한국어 · 영어 (빌드 · IDE UI 언어를 따름)
- `EquipSwLint.Analyzers` NuGet 분석기 (Roslyn 3.11 기준, .NET 5 SDK 이후)
- `equip-sw-lint` dotnet tool: 빌드 없이 폴더 분석, SARIF 2.1.0 · HTML, `--baseline` 게이트(줄 이동에 견디는 지문), `--ref` · `--exclude`, CP949 소스 읽기
- 보고서 뷰어 (정적 PWA): SARIF 열기 · 필터 · 근거 줄, 기준선 · 규칙 세트 전/후 비교, Shadow 판정 · 전환 · 롤백 · 규칙 세트 이력, 한/영, 다크 모드, 오프라인, IndexedDB 백업
- 합성 장비 예제 (심은 버그 16개 + 고친 짝), 규칙별 재현 테스트 11개, xUnit 44개, 뷰어 node 테스트 6개
- GitHub Actions: CI, 기준선 게이트 예제, 태그 릴리스(초안)

### 측정에서 고친 것
- for/foreach 루프에서 ESL011 분석기가 예외로 멈추던 것
- ESL001: 나중에 차이로 비교하는 int 기한 덧셈은 안전하므로 알리지 않음
- ESL004: UDP 수신 대기 · SocketAsyncEventArgs 는 범위에서 뺌
- ESL005 · ESL011: CancellationToken 핸들 대기, Stopwatch · `_terminated` 로 끝나는 조건은 기한이 있는 것으로 봄
- ESL006 을 Shadow 로 시작 (경로 이름 추정이라 조정에 안 쓴 공개 코드에서 오탐 1건)
