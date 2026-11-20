# Battle replay (headless)

Runs a whole match without Unity (about 2 s for M7, 150 vs 150) and writes a replay JSON: unit positions every N
ticks, point owners, shots, and layout metrics. `battle-replay.html` plays one or more replays from above on a
shared timeline. Game code is only referenced, never changed.

```sh
sudo apt-get install -y dotnet-sdk-8.0      # once (run apt-get update first in a fresh container)
tools/replay/run.sh                         # builds, runs base + playtest into tools/replay/out/
tools/replay/run.sh mp-playtest             # any setups listed by --list
tools/replay/run.sh base                    # one setup; TICKS=3000 EVERY=6 to change length / frame step
cd tools/replay/out && python3 -m http.server   # then open /battle-replay.html (fetch needs http, not file://)
```

Direct use: `WarCore.Replay <setup> <maxTicks> <every> <out.json> [lanesSeed]`, `--list` (setups), `--selftest`
(metric checks and a byte-identical repeat export). `lanesSeed` replaces the seed of the three-lane map's battle
(41 when left out), to compare several matches of one setup. The binary finds the Unity project root itself; any working directory works.
Setups live in `Recipes.cs`, one line each. The page reads `replay-base.json` and `replay-playtest.json` from its own
folder, or the files named in `?files=a.json,b.json`.

## Metrics

Computed on every recorded frame for each team's alive, deployed units; `metrics` in the JSON holds the mean and peak
over the match, `m` on each frame holds `[share1, share2, nn1, nn2, spread1, spread2, close1, close2, idle1, idle2,
closeNear1, closeNear2, enemy20_1, enemy20_2, idleNoEnemy1, idleNoEnemy2, pinned1, pinned2, broken1, broken2,
pinnedHidden1, pinnedHidden2, pinnedMoving1, pinnedMoving2, pinnedAtWall1, pinnedAtWall2]` (`null` = undefined).
A frame also lists the indices of the pinned (`n`) and broken (`b`) units when there are any; the page draws a thin
ring round a pinned unit and a thick yellow one round a broken unit.

| Key | Meaning | Reads |
|---|---|---|
| `maxPointShare` | Largest share of the team inside one point circle | high = whole team in one place |
| `nnSpread` | Nearest-teammate distance: interquartile range / median | 0 = lattice, higher = uneven spacing |
| `attackSpread` | Circular variance of attacker bearings around the point with most attackers | 0 = one side, 1 = all sides |
| `closeShare` | Share of the team whose nearest teammate is closer than 1.2 m | high = shoulder to shoulder |
| `idleOutside` | Share of the team whose position has not changed for 60 ticks and that stands farther than radius + slot ring from every point centre | high = units idle away from the points |
| `closeShareNearPoints` | `closeShare` among units within 2 x radius of a point centre (nearest teammate searched in the whole team); the range depends on the map only | high = shoulder to shoulder at the points |
| `enemyWithin20` | Share of the team with an enemy (alive, on the field) within 20 m | 0 = the two sides never come close |
| `idleNoEnemy` | `idleOutside` restricted to units with no enemy within 45 m | high = units idle away from the points with nobody to fight |
| `pinnedShare` | Share of the team the pressure system reports as pinned (read from the system, not from positions) | 0 without the system |
| `brokenShare` | Share of the team the pressure system reports as broken | high = a large part of the team is falling back at once |
| `pinnedHidden` | Share of the team's pinned units that no enemy (alive, on the field) within 68 m has a wall-free line to (walls only, from above) | high = pinned units are out of the enemy's sight |
| `pinnedMoving` | Share of the team's pinned units whose position differs from the previous recorded frame | walking to cover and pushing at each other both count |
| `pinnedAtWall` | Share of the team's pinned units within 2.5 m of a wall | high = pinned units stand at walls |
| `decidedTick` | Tick the match was won; `null` if not decided within `maxTicks` | |

- `pinnedHidden`, `pinnedMoving` and `pinnedAtWall` are undefined on a frame with fewer than 5 pinned units of the team.
- Undefined when fewer than 10 units are on the field (share, nn, close, idle, enemy20, idleNoEnemy, pinned, broken), fewer than 10 units near points
  (closeNear) or fewer than 5 attackers (spread).
- The slot ring of `idleOutside` is the setup's `ai.slotRingMm` (the `RingMm` of its `Recipes.cs` entry), 0 by default.
- An attacker is a unit whose nearest point is not owned by its team and lies within 25 m of that point's centre.
- `nnSpread` uses quartiles rather than std / mean: on M7 a few stragglers far from the main body pushed std / mean
  above 1 while the main body stood on the lattice.
- Thresholds belong to the task that uses them; do not tune game values to pass a metric.

## Reference values (main, 63f6110 for the first four; AI-P1 setups on its branch)

| Setup | End | Hash | share 1/2 | nn 1/2 | spread 1/2 | close 1/2 | idle 1/2 | closeNear 1/2 |
|---|---|---|---|---|---|---|---|---|
| base | team 2 wins at tick 2050 | 349ABBEB3EBBED0D | 0.754 / 0.685 | 0.311 / 0.328 | 0.606 / 0.343 | 0.936 / 0.889 | 0.015 / 0.028 | 0.943 / 0.896 |
| playtest | undecided at 2400 | 1B5A952895358459 | 0.726 / 0.634 | 0.408 / 0.278 | 0.513 / 0.250 | 0.874 / 0.870 | 0.085 / 0.106 | 0.884 / 0.880 |
| mp-playtest | undecided at 2400 | 6BA883C992F6F598 | 0.661 / 0.641 | 0.470 / 0.249 | 0.509 / 0.258 | 0.833 / 0.884 | 0.128 / 0.095 | 0.867 / 0.897 |
| lanes | team 1 wins at tick 2245 | 0392AC1519D79015 | 0.315 / 0.213 | 0.474 / 0.571 | 0.180 / 0.143 | 0.784 / 0.771 | 0.022 / 0.019 | 0.832 / 0.824 |
| playtest-p1 | undecided at 2400 | 5BB665BAB3789C19 | 0.472 / 0.382 | 0.414 / 0.545 | 0.494 / 0.719 | 0.211 / 0.270 | 0.013 / 0.020 | 0.207 / 0.279 |
| playtest-p1w | undecided at 2400 | 2C0F3A77CF416390 | 0.184 / 0.197 | 0.805 / 0.655 | 0.614 / 0.467 | 0.305 / 0.201 | 0.076 / 0.025 | 0.277 / 0.196 |
| lanes-p1 | team 1 wins at tick 2319 | 270F5A03ED5B7491 | 0.234 / 0.146 | 0.897 / 0.784 | 0.428 / 0.433 | 0.352 / 0.445 | 0.003 / 0.004 | 0.223 / 0.230 |
| lanes-p1w | undecided at 2400 | E5CB1C7783233FD6 | 0.102 / 0.067 | 1.327 / 0.817 | 0.376 / 0.402 | 0.230 / 0.400 | 0.004 / 0.003 | 0.134 / 0.165 |

Means over the match. At tick 750 (25 s) team 1 in `base` has 81 of 108 units in one circle (share 0.75).

AI-S1 (engage part, values PLACEHOLDER: `boundMoveTicks` 60, `boundFireTicks` 90, `boundSharePermille` 700,
`boundMinRangeMm` 12000; the setups add only these to `playtest` and `lanes`):

| Setup | End | Hash | enemy20 1/2 | idleNoEnemy 1/2 | idle 1/2 | closeNear 1/2 |
|---|---|---|---|---|---|---|
| playtest | undecided at 2400 | 1B5A952895358459 | 0 / 0 | 0.009 / 0.016 | 0.085 / 0.106 | 0.884 / 0.880 |
| playtest-s1 | undecided at 2400 | 1348046939DEA9F2 | 0.042 / 0.039 | 0.007 / 0.015 | 0.078 / 0.149 | 0.837 / 0.882 |
| lanes | team 1 wins at tick 2245 | 0392AC1519D79015 | 0 / 0 | 0.018 / 0.019 | 0.022 / 0.019 | 0.832 / 0.824 |
| lanes-s1 | team 1 wins at tick 1845 | ECBA9D4FE6B0524C | 0 / 0 | 0.013 / 0.019 | 0.017 / 0.019 | 0.811 / 0.850 |

AI-S3 (pressure system, values PLACEHOLDER: death and near radius 3000 mm, death shock 2000, near shock 1000, decay
12 thousandths a tick, broken at 7000, recover at 1000, pinned at 2000, spread 500, break share 300; the setups add
only these to `playtest-s1` and `lanes-s1`; 3600 ticks, every 3):

| Setup | End | Hash | pinned 1/2 | pinned peak 1/2 | broken 1/2 | broken peak 1/2 | idleNoEnemy 1/2 | idle 1/2 | enemy20 1/2 |
|---|---|---|---|---|---|---|---|---|---|
| playtest-s1 | undecided at 3600 | 9D77B681FA745C17 | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0 | 0.009 / 0.015 | 0.067 / 0.149 | 0.028 / 0.039 |
| playtest-s3 | undecided at 3600 | DE8842816C51B043 | 0.173 / 0.165 | 0.867 / 0.615 | 0.056 / 0.065 | 0.313 / 0.450 | 0.005 / 0.011 | 0.074 / 0.089 | 0.013 / 0.010 |
| lanes-s1 | team 1 wins at tick 1845 | ECBA9D4FE6B0524C | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0 | 0.013 / 0.019 | 0.017 / 0.019 | 0 / 0 |
| lanes-s3 | team 1 wins at tick 2676 | 33330F89BD530D84 | 0.157 / 0.113 | 0.614 / 0.657 | 0.045 / 0.077 | 0.303 / 0.556 | 0.014 / 0.013 | 0.067 / 0.082 | 0 / 0 |

The broken peak of team 2 in `lanes-s3` (0.556) is ticks 1179-1206: the team is down to 18-19 units on the field and
10 of them are broken. The most units of one team broken at once: 15 of 84 on the field (team 1, `lanes-s3`, tick
963), 12 for team 2 there, 7 and 9 in `playtest-s3`.

AI-S4 (cover, values PLACEHOLDER: `coverRadiusMm` 9000, `coverStepMm` 1000, `coverDepthMm` 2000; the setups add only
these to `playtest-s3` and `lanes-s3`; 3600 ticks, every 3):

| Setup | End | Hash | pinnedHidden 1/2 | pinnedMoving 1/2 | pinnedAtWall 1/2 | idleNoEnemy 1/2 |
|---|---|---|---|---|---|---|
| playtest-s3 | undecided at 3600 | DE8842816C51B043 | 0 / 0 | 0.058 / 0.019 | 0 / 0 | 0.005 / 0.011 |
| playtest-s4 | undecided at 3600 | EEA41851F348FCD0 | 0 / 0 | 0.058 / 0.019 | 0 / 0 | 0.005 / 0.011 |
| lanes-s3 | team 1 wins at tick 2676 | 33330F89BD530D84 | 0.212 / 0.001 | 0.105 / 0.252 | 0.003 / 0.206 | 0.014 / 0.013 |
| lanes-s4 | undecided at 3600 | AA0D6ED6EFED997F | 0.211 / 0.149 | 0.246 / 0.411 | 0.552 / 0.322 | 0.009 / 0.033 |

`playtest-s4` differs from `playtest-s3` in the hash only: no pinned unit on that map has a wall within reach. Over
the seeds 41-46 of the three-lane map the means are, before and after: `pinnedHidden` 0.108 / 0.004 and 0.260 / 0.185,
`pinnedMoving` 0.127 / 0.215 and 0.216 / 0.388; three matches of six are undecided at 3600 either way (42, 44, 45
before; 41, 42, 44 after).

AI-S2 (each shooter's own reaction, burst and pause, `traitSpreadPermille` 300, PLACEHOLDER; the setup adds only this
field to `playtest`). The replay cannot show who fired when; the per-shooter timing is held by `AiS2Tests`.

| Setup | End | Hash | share 1/2 | nn 1/2 | close 1/2 | idle 1/2 | closeNear 1/2 | idleNoEnemy 1/2 |
|---|---|---|---|---|---|---|---|---|
| playtest | undecided at 2400 | 1B5A952895358459 | 0.726 / 0.634 | 0.408 / 0.278 | 0.874 / 0.870 | 0.085 / 0.106 | 0.884 / 0.880 | 0.009 / 0.016 |
| playtest-s2 | undecided at 2400 | 7531D3A423B34B4C | 0.692 / 0.657 | 0.591 / 0.268 | 0.823 / 0.874 | 0.092 / 0.093 | 0.834 / 0.886 | 0.009 / 0.017 |
