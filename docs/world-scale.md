# World scale

The sizes the base fixes, where each lives, and the test that holds it. Metres; 1 m = 65536 raw (`SimFix.Scale`).
`W01`..`W15` are tests in `Assets/WarCore/Tests/MapContent/Scale/WorldScaleTests.cs`; each reads both sides of its
relation from the code or the shipped data. "none" means no test holds the value.

## Cell, map, time

| Quantity | Value | Where | Test |
|---|---|---|---|
| Map cell | 1 m | `WcMap.CellSize` | W01 |
| Height point step | 1 m | `HeightField` | W01 |
| Largest map side | 512 m | `HeightField.MaxSide` | W12, `Mt150WcMapValidationTests.MaximumMapWidth_RoundTrips` |
| World coordinate limit | 16384 m | `SimVec3.WorldLimitMetres` | W12 |
| Height limit | 8192 m (2^29 raw) | `HeightField.HeightLimitRaw` | W12 |
| Steps per second | 30 | `SimClock.TickRate` | W02 |
| Spatial index cell | 2 m | `WorldBuilder` (literal) | W15 |

## Body

| Quantity | Value | Where | Test |
|---|---|---|---|
| Soldier radius r | 0.25 m | `CombatSection.UnitRadiusRaw`; literals in `MovementFeature`, `MatchFeature`; `WallSpan.BelowRaw`; `NavGrid.MaxClearRadiusRaw`; `InterimLocomotion.UnitFootprint` | W03 |
| Shipped body (`rwr-body`, `rwr-field`) | height 1.75, eye 1.5, muzzle 1.5 | `Content/Rwr/Data` | W04, `Body1GeometryTests` |
| Body rule | 2r <= height <= headroom; r <= eye <= height - r; 0 <= muzzle <= height | `CombatSection` | W04 |
| No body (older layers) | collider 1 m, eye 0.5 m, shot from the foot | `WorldAssembly.UnitHeight`, `CombatSystem` | `Body1GeometryTests.OldLayers_AOneMetreWallStillHidesTheTarget` |
| Drawn body | 1 m tall | `Presentation/Camera/AimPick.cs` | none |
| Sight over a wall | wall top below the eye is seen over | `CombatSystem` | `Body1GeometryTests.Body_WallsUnderTheEyeAreSeenOver` |
| Melee reach | start distance <= reach; reach > 2r | content `melee` | W13 |
| Weapon falloff end | within the diagonal of the largest map | content `weapons` | W14 |

## Walls, doors, windows

| Quantity | Value | Where | Test |
|---|---|---|---|
| Headroom | 2 m | `WallSpan.ClearanceRaw` | W04, W05, `Ml1WallSpanTests.LintelAtTheHeadroomIsOpenAndOneRawLowerBlocks` |
| Wall probe below the walk band | 0.25 m | `WallSpan.BelowRaw` | W03, `Ml1WallSpanTests.BandBottomIsTheProbeSphereInclusive` |
| Wall thickness | whole cells, at least 1 m | `WcMapValidation` | none |
| Door lintel underside | headroom above the highest floor corner | `MapPrefabs.Door` | W05 |
| Window sill, opening top (defaults) | 0.9 m, 2.0 m | `MapPrefabs.DefaultSillRaw`, `DefaultOpeningRaw` | W06, `Ml3WindowTests` |
| Window gate | sill >= 2r; sill <= min(eye, muzzle) - r; opening top >= max(eye, muzzle) + r | `MapPrefabs.Window` | W06, `Ml3WindowTests.SillAndOpeningAgainstEyeAndMuzzle` |
| Grounded wall on terrain | at least 1 m above its highest ground point | `WcMapValidation` | `Wp23WallTests.ExactlyOneMetreAbove_IsLegal_TwoRawLess_IsNot_OnTheFarCorner` |

## Floors and stairs

| Quantity | Value | Where | Test |
|---|---|---|---|
| Storey height | no constant; demo maps use 3 m; least that stacks is headroom + thinnest plate (2.5 m) | `PlateDemoMap`, `PlateAssaultMap`, `WcMapPlate` | W07 |
| Thinnest plate | 0.5 m | `WcMapPlate.MinThickRaw` | W07 |
| Plate stack gap | at least the headroom | `PlateSpan` | `Ml4RuleTests.StackGapIsTwoMetresInclusive` |
| Plates per cell | 64 | `PlateSpan` | `Ml4RuleTests.SixtyFourPlatesPerCell` |
| Seam tolerance between layers | 0.3 m rounded down (19660 raw), below the thinnest plate | `LayeredNav.StepRaw` | W09, `Ml6VectorTests` |

## Slopes

| Quantity | Value | Where | Test |
|---|---|---|---|
| Height field legal slope | 2/1 | `HeightField.MaxSlopeNumerator`, `MaxSlopeDenominator` | `Ml4RuleTests.PlateSlopeTwoToOne` |
| Plate slope in the gate | min(layer walkable slope, 5/3) | `MapGate.PlateSlopeNum`, `PlateSlopeDen` | W08 |
| Layer walkable slope | per layer `slopeRule`; `rwr-terrain-demo` 3/4; layers without one use the height field's 2/1 | `SlopeRuleSection` | W08 |
| Demo stairs | 3 m over 4 cells (3/4) | `PlateDemoMap`, `PlateAssaultMap` | W08 |

## Speed

| Quantity | Value | Where | Test |
|---|---|---|---|
| RWR soldier speed | 6 m/s | `MapRecipe` | W10 |
| Ancient soldier speed | 3 m/s | `Content/Ancient/Data` | W10 |
| Content speed limit | 300 m/s | `ContentPack.MaxUnitSpeedMmPerSec` | none |
| Step cap, worlds with terrain or plates | 0.25 m per step (7.5 m/s) | `InterimLocomotion.MaxTerrainStepRaw` | W10, `L10CapTests`, `Wp23GuardTests` |
| Step cap after the surface multiplier | 0.5 m per step | `InterimLocomotion.MaxEffectiveStepRaw` | W11 |
| Surface speed multiplier | up to 2x | `SurfaceDef.MaxSpeedMilli` | W11 |
| Dash | at most 100 m, 10 m per step | `UnitDashes`, `Dash` | none |

## Guard

`Tests/EditMode/SizeLiteralGuardTests.cs` fails on any new size, distance, time or speed literal in project code
(rules in `SizeLiteralScanner.cs`) unless `tools/size-guard/ledger.tsv` lists it as canon, ui or math with a note.
Old copies are listed as debt; `debt-max` may only go down.

## Known mismatches

Recorded only, nothing changed here. Each goes to SCALE-2 or the candidate pool.

1. The drawn body is 1 m tall (`AimPick.BodyHeight`); shipped bodies are 1.75 m and an old body is a capsule centred on the foot.
2. Silhouettes, see-through zones and layer cutting use that 1 m as head height; the simulated eye is 1.5 m (body) or 0.5 m (no body).
3. The soldier radius is written in three places and means the same in three more; only W03 ties them together.
4. The comment on the window prefab still computes with a 1 m sill; the default is 0.9 m and has no formula.
5. The pressure range value in the code was not checked.
6. Plate slope has three limits (height field 2/1, gate 5/3, layer rule such as 3/4); a stair legal under one can be refused by another.
7. Speed limits differ: content 300 m/s, terrain and plate worlds 7.5 m/s (an error at the first move), dash 10 m per step.
8. "Step" names four things: the 0.3 m seam, the 0.5 m sweep step, the 0.25 m terrain step, and the 1.414 m neighbour difference of generated terrain.
9. Storey height exists only as demo data; the code enforces a stack gap and a thickness, so 2.5 m is the least.
10. There is no low or high wall class (wall kind is a free string); a grounded wall on terrain is at least 1 m, lower ones need a raised base.
11. Sight is cut at 1.4 m and 1.6 m for a 1.5 m eye; a planned 1.2 m crossing limit would leave 1.2 to 1.4 m walls crossable but see-through and 1.4 to 1.6 m walls opaque but too high.
12. Body height is capped at 2 m by content but at 32768 m by the `CombatBody` constructor; melee half height is 1 m in the shipped data while a 1.75 m body is centred at 0.875 m and an old body at the foot.
13. Some older comments assume a body 0.5 m either side of its position; with 1.75 m bodies that is out of date.
