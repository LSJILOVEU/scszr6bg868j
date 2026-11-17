# Battle replay (headless)

Runs a whole match without Unity (about 2 s for M7, 150 vs 150) and writes a replay JSON: unit positions every N
ticks, point owners, shots, and four layout metrics. `battle-replay.html` plays one or more replays from above on a
shared timeline. Game code is only referenced, never changed.

```sh
sudo apt-get install -y dotnet-sdk-8.0      # once (run apt-get update first in a fresh container)
tools/replay/run.sh                         # builds, runs base + playtest into tools/replay/out/
tools/replay/run.sh base                    # one setup; TICKS=3000 EVERY=6 to change length / frame step
cd tools/replay/out && python3 -m http.server   # then open /battle-replay.html (fetch needs http, not file://)
```

Direct use: `WarCore.Replay <setup> <maxTicks> <every> <out.json>`, `--list` (setups), `--selftest` (metric checks
and a byte-identical repeat export). The binary finds the Unity project root itself; any working directory works.
Setups live in `Recipes.cs`, one line each. The page reads `replay-base.json` and `replay-playtest.json` from its own
folder, or the files named in `?files=a.json,b.json`.

## Metrics

Computed on every recorded frame for each team's alive, deployed units; `metrics` in the JSON holds the mean and peak
over the match, `m` on each frame holds `[share1, share2, nn1, nn2, spread1, spread2]` (`null` = undefined).

| Key | Meaning | Reads |
|---|---|---|
| `maxPointShare` | Largest share of the team inside one point circle | high = whole team in one place |
| `nnSpread` | Nearest-teammate distance: interquartile range / median | 0 = lattice, higher = uneven spacing |
| `attackSpread` | Circular variance of attacker bearings around the point with most attackers | 0 = one side, 1 = all sides |
| `decidedTick` | Tick the match was won; `null` if not decided within `maxTicks` | |

- Undefined when fewer than 10 units are on the field (share, nn) or fewer than 5 attackers (spread).
- An attacker is a unit whose nearest point is not owned by its team and lies within 25 m of that point's centre.
- `nnSpread` uses quartiles rather than std / mean: on M7 a few stragglers far from the main body pushed std / mean
  above 1 while the main body stood on the lattice.
- Thresholds belong to the task that uses them; do not tune game values to pass a metric.

## Reference values (main, d70fc7e)

| Setup | End | Hash | share mean 1/2 | nn mean 1/2 | spread mean 1/2 |
|---|---|---|---|---|---|
| base | team 2 wins at tick 2050 | 349ABBEB3EBBED0D | 0.754 / 0.685 | 0.311 / 0.328 | 0.606 / 0.343 |
| playtest | undecided at 2400 | 77B5F399C8596E86 | 0.538 / 0.553 | 0.356 / 0.362 | 0.590 / 0.284 |

At tick 750 (25 s) team 1 in `base` has 81 of 108 units in one circle (share 0.75).
