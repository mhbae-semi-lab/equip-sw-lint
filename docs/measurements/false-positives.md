# False-positive measurement on public C# code (2026-11-22)

No company code was used. Every finding below was read in its source context and marked
TP (the rule's claim holds on that code) or FP (it does not). Per-finding lists: [`triage.csv`](triage.csv)
(tuning set), [`triage-holdout.csv`](triage-holdout.csv) (held-out set).

## Repositories

| Set | Repository | License | Commit | Scope | Lines |
|---|---|---|---|---|---|
| tuning | [dotnet/iot](https://github.com/dotnet/iot) | MIT | 22edda6 (2026-11-19) | `src`, without tests/samples | 188,643 |
| tuning | [NModbus/NModbus](https://github.com/NModbus/NModbus) | MIT | baddfca (2026-06-30) | without test projects | 8,963 |
| tuning | [dotnet/MQTTnet](https://github.com/dotnet/MQTTnet) | MIT | 798a0e2 (2026-09-30) | `Source`, without tests | 42,177 |
| held out | [naudio/NAudio](https://github.com/naudio/NAudio) | MIT | 174bb49 (2026-10-25) | everything but tests (incl. WinForms demos) | 143,765 |
| held out | [ScottPlot/ScottPlot](https://github.com/ScottPlot/ScottPlot) | MIT | fb3f5aa (2026-08-15) | `src`, without tests | 91,418 |

Command (CLI, no build; vendor/NuGet dependencies are not restored, so rules skip unresolved types):

```
equip-sw-lint <repo>/src --exclude tests --exclude samples --fail-on none --sarif out.sarif
```

## Tuning set: initial rules → refined rules

The first run found four false-positive shapes. The rules were changed for exactly these shapes and re-run:

| Rule | Initial (TP / FP) | Change | Refined (TP / FP) |
|---|---|---|---|
| ESL001 | 0 / 2 | `TickCount + period` stored as int and later compared by difference is wrap-safe: stop flagging the addition, keep flagging the comparison | 0 / 0 |
| ESL002 | 10 / 0 | – | 10 / 0 |
| ESL004 | 0 / 4 | UDP listeners and `SocketAsyncEventArgs` overloads are out of scope (connectionless / callback completion) | 0 / 0 |
| ESL005 | 20 / 3 | waiting on `CancellationToken.WaitHandle` (alone or inside `WaitAny`) is the cancellation path itself | 20 / 1 |
| ESL011 | 3 / 3 | conditions using `Stopwatch.GetTimestamp()` / `Environment` time, and `_terminated` flags, are bounded | 3 / 0 |
| **All** | **33 / 12 (FP 26.7%)** | | **33 / 1 (FP 2.9%)** |

The refined numbers are measured on the code the rules were tuned on, so they overstate precision. The held-out set is the honest number.

## Held-out set (refined rules, no changes after this run)

| Rule | Findings | TP | FP | FP rate | FP shape |
|---|---|---|---|---|---|
| ESL002 | 1 | 1 | 0 | 0% | |
| ESL005 (Shadow) | 12 | 8 | 4 | 33% | `SemaphoreSlim` used as a lock; pause gate / driver event that Stop sets |
| ESL006 | 1 | 0 | 1 | 100% | build tool writing generated `recipes.json` (path keyword match) |
| ESL007 (Shadow) | 2 | 0 | 2 | 100% | caller holds `lock(Plot.Sync)` outside the method |
| ESL010 (Shadow) | 3 | 2 | 1 | 33% | thread sets the state itself first |
| **All** | **19** | **11** | **8** | **42%** | |

Consequences:
- ESL006 matches settings files by path keywords. After this run it ships as **Shadow** (heuristic rules start in Shadow); it is promoted per codebase after triage.
- ESL005, ESL007 and ESL010 stay **Shadow**. Across both sets ESL005 is 28 TP / 5 FP (15% FP), above the 10% promotion bar.
- ESL003, ESL004 (after the change), ESL008 and ESL009 had **no findings** on these five repositories: they are precise by construction (specific API + condition) but there is no real-code precision data for them yet. Their recall/precision evidence is the synthetic samples and unit tests only.

## Synthetic samples (completion metric)

`samples/Equipment/Bad` plants 16 bugs (every rule at least once); `samples/Equipment/Good` holds the fixed twins.
`CliTests.Samples_AllPlantedBugsFound_NoFalsePositivesOnFixedCode`: 16/16 found, 0 findings on Good, 0 unplanned findings.

## Analysis time

Intel Core i7-11370H, 32 GB, .NET 8 runtime, CLI from the Release build, three runs each:

| Code | Files | Lines | Total (parse + analyze) |
|---|---|---|---|
| samples/Equipment | 13 | 528 | ~0.6 s |
| dotnet/iot `src` | 1,550 | 188,643 | 12.5 – 13.2 s (parse 4.6 s, analyze 7.9 s) |
| NModbus | 122 | 8,963 | 1.0 – 1.5 s |
| MQTTnet `Source` | 434 | 42,177 | 3.3 – 5.0 s |
| NAudio | 1,122 | 143,765 | 10.6 s |
| ScottPlot `src` | 1,061 | 91,418 | 10.3 s |
