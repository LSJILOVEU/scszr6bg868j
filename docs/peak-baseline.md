# Engagement peak baseline (P4)

Test: `PeakBaselineTests.FirstFireTickPeak` (Manual, Explicit, report-only, no timing gate).
Command: `dotnet test tools/headless/WarCore.Headless.Tests.csproj -c Release --filter "FullyQualifiedName~PeakBaselineTests"`

Scene: `M7BehaviorLockTests.Real()` (100 vs 100, seed 41, 80x112 box map). JIT warmed on a separate world.
First shot is at tick 599; end state hash after the window is `1DF9ACAC46A23B1E` (identical in all 5 runs).
Each run builds a fresh world, steps to tick 598 untimed, then times ticks 599..628 one by one.

| Quantity (ms per tick) | max of 5 | median of 5 | per run |
|---|---|---|---|
| first shot tick (599) | 1.317 | 1.269 | 1.286, 1.269, 1.317, 1.261, 1.244 |
| max over ticks 599..628 | 3.085 | 2.063 | 2.733, 2.063, 1.645, 1.731, 3.085 |

Notes:
- Absolute numbers depend on the runner; compare P5/P6 only against a run of the same test on the same runner.
- The window maximum varies more between runs than the first-tick time; use the median of 5 for comparisons.
