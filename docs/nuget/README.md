# equip-sw-lint

Roslyn analyzers for C# software that runs for weeks on equipment PCs (inspection, motion, vision, host links).
They flag the code shapes behind real field stops that general analyzers do not cover.

| Id | Stage | Finds |
|---|---|---|
| ESL001 | enforce | `Environment.TickCount` / `GetTickCount()` compared, widened or divided: wrong after 24.9 days of uptime |
| ESL002 | enforce | intervals and deadlines from `DateTime.Now/UtcNow` (clock sync breaks them; overlaps SonarQube S6561) |
| ESL003 | enforce | periodic thread-pool timer whose callback blocks and has no reentrancy guard |
| ESL004 | enforce | serial/TCP reads with no timeout, async reads with no token |
| ESL005 | Shadow | `WaitOne()`, `Join()`, `Monitor.Wait`, `WaitForExit()` without a timeout |
| ESL006 | Shadow | settings/recipe files overwritten in place (no temp file + replace) |
| ESL007 | Shadow | buffer written by a device callback and read by UI code with no lock |
| ESL008 | enforce | queue fed by a device/timer callback with no size limit |
| ESL009 | enforce | `Control.Invoke` / `Dispatcher.Invoke` / `SendMessage` while holding a lock |
| ESL010 | Shadow | field initialized after receiving already started (port opened, thread started) |
| ESL011 | Shadow | `while (!ready) Sleep(..)` polling loop with no deadline |

Shadow rules report as suggestions and never break the build. Promote one with `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.ESL010.severity = warning
```

- `EquipSwLint.Analyzers` (NuGet): runs in the build and the IDE. Works with Visual Studio 2019 16.11 / .NET 5 SDK and later.
- `equip-sw-lint` (dotnet tool): analyzes a folder without building it, writes SARIF and HTML, `--baseline` gate.
- Report viewer (static web page): evidence line, how each pattern stops equipment, baseline compare, Shadow triage.

Messages are in English or Korean (follows the build/IDE UI language). MIT license.
Source, rule details and measurements: https://github.com/mhbae-semi-lab/equip-sw-lint
