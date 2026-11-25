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
| Shipped body (`parts/body`, which `rwr-body` and `rwr-field` list) | height 1.75, eye 1.5, muzzle 1.5 | `Content/Rwr/Data/parts` | W04, `Body1GeometryTests` |
| Body rule | 2r <= height <= headroom; r <= eye <= height - r; 0 <= muzzle <= height | `CombatSection` | W04 |
| No body (older layers) | collider 1 m, eye 0.5 m, shot from the foot | `WorldAssembly.UnitHeight`, `CombatSystem` | `Body1GeometryTests.OldLayers_AOneMetreWallStillHidesTheTarget` |
| Drawn body | with a body: the simulated top per stance level (1.75 / 1.0 / 0.5 m shipped), radius r; without a body: 1 m (`AimPick.BodyHeight`) | `Presentation/Bridge/Features/BodySizes.cs`, `Presentation/Camera/DrawnParts.cs` | `Scale2aSizesTests`, `Scale2aViewsTests` |
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
| Window sill, opening top | content `scale` (`windowSillMm`, `windowTopMm`); without it 0.9 m, 2.0 m | `ScaleRules`, `MapPrefabs.DefaultSillRaw`, `DefaultOpeningRaw` | W06, `Ml3WindowTests`, `Scale2bBuildTests.SC9_WindowBounds` |
| Window gate | sill >= 2r; sill <= min(eye, muzzle) - r; opening top >= max(eye, muzzle) + r | `MapPrefabs.Window` | W06, `Ml3WindowTests.SillAndOpeningAgainstEyeAndMuzzle` |
| Grounded wall on terrain | at least 1 m above its highest ground point | `WcMapValidation` | `Wp23WallTests.ExactlyOneMetreAbove_IsLegal_TwoRawLess_IsNot_OnTheFarCorner` |

## Floors and stairs

| Quantity | Value | Where | Test |
|---|---|---|---|
| Storey height | content `scale.storeyMm` (demo maps 3 m; least 2.5 m, headroom + thinnest plate) | `ScaleRules`, `PlateDemoMap`, `PlateAssaultMap` (a test fixture), `showcase-house.map.json`, `WcMapPlate` | W07, `Scale2bBuildTests.SC10_Storeys` |
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
| Demo stairs | 3 m over 4 cells (3/4) | `PlateDemoMap`, `PlateAssaultMap` (a test fixture), `showcase-house.map.json` | W08 |
| Slope speed (content `movement.slope`) | probe 0.5 m ahead along the heading, slope = rise * 1000 / 0.5 m, the last band strictly exceeded; `rwr-slope`: up > 100: 900, > 300: 700, > 500: 500; down > 400: 800; the dash takes no slope | `SlopeTable`, `InterimLocomotion.StepLine` | `Slope1TableTests`, `Slope1PlateTests` |

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

## Content section `scale`

An optional object section of a content layer (SCALE-2b): the theme's standard parts in whole millimetres. The shipped layer
`rwr-scale` writes it; a layer without it gets the defaults below (`ScaleRules.Default`, checked against no body) and loads
byte for byte as before. Tests: `Assets/WarCore/Tests/MapContent/Scale/Scale2b*.cs`.

```json
"scale": { "storeyMm": 3000, "plateMm": 500,
  "lowWallMm": 1000, "chestWallMm": 1400, "highWallMm": 2500,
  "windowSillMm": 900, "windowTopMm": 2000, "railingMm": 1000,
  "stairRiseMm": 175, "stairRunMm": 280,
  "laneMm": 3250, "sidewalkMm": 2000, "alleyMm": 3000 }
```

- All thirteen fields are required after merging (a child layer may change one); each is an integer 1 to 100000; an
  unknown field is refused. A layer that writes the section needs a combat body.
- Millimetres become raw with `SimFix.Ratio(mm, 1000)` (truncated). Storey, the three walls and the railing are used as
  wall heights, which must be even: an odd raw value grows 1 raw. Plate, sill, window top and the street widths are not
  padded (the window prefab pads sill and top itself). Every relation below is checked on the padded values.
- `scale` is accepted but not in the "allowed" list of the unknown-key message until MIX-1, so a misspelled section key
  gets no hint.
- The section holds recommended sizes, not legal ones: the map gate never reads it, so a hand-made wall of any legal height
  stays legal. The checks use the gate's and the prefabs' own constants, so a part they accept builds.
- Readers: the editor's two windows (`MapDocument.WithWindow`, `WithWindowOn`), `MapDocument.Scale()` and
  `MapDocument.StandardBuilding(...)` (storey, plate, a one-cell door and `ScaleRules.StairCells(floors)` stair cells).
  `WithFloors` keeps a building's stair length, so a standard building going from one floor to two or from two to three is
  made again with `StandardBuilding`. Railing and street widths have no reader yet (candidate pool STREET-1). On a layer
  without the section whose walkable slope is gentler than the default step (0.625), a standard building's stair is too
  steep and `WithBuilding` refuses it; such a layer writes its own `scale`.

### Wall height base (relations checked when a layer loads)

r = the body's radius (`combat.body.radius`, default 0.25 m); E, M, H = the standing body's eye, muzzle and height; L = the highest eye or muzzle of any stance
(without stances L + r is 2r); slope = the layer's walkable slope (its terrain rules, else the height field's 2/1). Low,
chest and high walls are judged against each pack's own body; the base fixes no metre value for them.

| Field | Relation (raw, bounds inclusive unless strict) | Meaning |
|---|---|---|
| `lowWallMm` | L + r <= low <= min(E, M) - r | every lower stance hides behind it; standing sees and shoots over it |
| `chestWallMm` | low < chest < min(E, M) | standing just sees over it |
| `highWallMm` | high >= H | standing does not see over it |
| `windowSillMm` | 2r <= sill and sill + 1 <= min(E, M) - r | the prefab's gate, with 1 raw left for its odd-height padding |
| `windowTopMm` | max(E, M) + r <= top <= storey - plate | the prefab's gate; the lintel fits under the floor above |
| `plateMm` | plate >= 0.5 m | `WcMapPlate.MinThickRaw` |
| `storeyMm` | storey - plate >= headroom 2 m | `WallSpan.ClearanceRaw` |
| `stairRiseMm`, `stairRunMm` | rise / run <= slope and <= 5/3 | the effective plate slope (compared in millimetres) |
| `railingMm` | 2r <= railing <= min(E, M) - r | standing sees over it |
| `laneMm`, `sidewalkMm`, `alleyMm` | each >= 1000 mm | at least one cell |
| with a traversal kind of shape `over` | a lone low wall on flat ground matches one; a lone high wall matches none | the low wall can be vaulted, the high wall cannot |

All failing rows go into one error, in this order. The chest wall is not checked against traversal.

## Real-world reference sizes

Reference only: none of these is in the base yet. Approximate, metres.

| Thing | About |
|---|---|
| Carbine / rifle / sniper rifle / pistol | 0.84 / 1.0 / 1.1-1.2 / 0.2 |
| Helmet adds / backpack depth | about 0.05 / 0.21 (0.12 x body height, the drawn-parts ratio) |
| Lane / sidewalk / alley | 3.0-3.5 / 1.5-3 / 1.5-4 |
| A street, building to building | old town 10-15, main street 20-40 |
| Car | 4.5 long, 1.8 wide, 1.45 high |
| Off-road vehicle | 4.6 x 2.2 x 1.8 |
| Military truck | 7-8 x 2.5 x 3 |
| Armoured car | 7.7 x 2.9 x 2.4 |
| Tank | hull 6.9 (9.5 with the gun) x 3.6 x 2.3 |
| Door / sill / window top / railing | 0.8-0.9 wide, 2.0-2.1 high / about 0.9 / about 2.1 / 1.0-1.1 |
| Storey / floor slab / stair step | homes 2.8-3.0, shops 3.5-4 / about 0.2 / rise 0.175, run 0.28 (about 32 degrees) |
| Wall thickness | outer 0.2-0.4, inner 0.1-0.15, old city walls 1-3 |

## Deliberately not real

| Item | Base | Real | Reason | Status |
|---|---|---|---|---|
| Door width | 1 cell (1 m) | 0.8-0.9 | the grid's smallest cell (`MapPrefabs`) | kept on purpose (follows from the grid) |
| Floor plate | at least 0.5 m | about 0.2 | the 0.25 m downhill probe sphere must stay inside the plate (`MapGate.PlateSlopeNum`) | kept on purpose |
| Move speed | 6 m/s | walk 1.4, run 3-4, sprint 6-7 | RWR-style pace | open (no recorded decision) |
| Grenade | full damage 2 m, none at 6 m | about 5 lethal, 15 wounding | screen scale | open |
| Rifle range | falloff 34-67 m | effective 300-500 | tied to how wide one screen is; compressed about 6 times | open (one compression ratio to be set) |
| Width of one screen | about 36 m each side by default | - | sets how far a range looks | open |
| Wall thickness | at least 1 m (whole cells) | 0.2-0.4 | the 1 m grid | open |
| Standing eye | 1.5 | about 1.63 | same height as the muzzle | open |
| Crouching eye | 0.75 | kneeling about 1.05 | hides behind a 1 m wall | open |
| Prone | in the simulation a short upright capsule 0.5 m high; drawn lying flat | a body about 1.9 long | bodies are vertical capsules; only the middle part can be hit | open (candidate pool: prone as a long shape) |

## Words for "step"

Four lengths share the word: the seam tolerance between layers 0.3 m (`LayeredNav.StepRaw`), the sweep step 0.5 m
(`InterimLocomotion.MaxEffectiveStepRaw`), the terrain step 0.25 m per tick (`InterimLocomotion.MaxTerrainStepRaw`), and
the 1.414 m largest neighbour difference of generated terrain. The stair step of `scale` (`stairRiseMm`, `stairRunMm`) is
a fifth, measured in millimetres.

## Guard

`Tests/EditMode/SizeLiteralGuardTests.cs` fails on a new literal in project code that matches the size, distance, time
or speed shapes listed at the top of `SizeLiteralScanner.cs` (a bare integer passed as an argument is not one of them),
unless `tools/size-guard/ledger.tsv` lists it as canon, ui or math with a note.
Old copies are listed as debt. The test checks that `debt-max` equals the debt total; review checks that it never rises.

## Known mismatches

Status after SCALE-2: each item says what changed or where it went.

1. Worlds with a body now draw and pick it at the simulated size (`BodySizes`). Old layers without a body still draw 1 m (`AimPick.BodyHeight`): their collider is a 1 m capsule centred on the foot, so there is no simulated top to follow, and their pictures and goldens stay unchanged.
2. With a body, silhouettes and layer cutting use the simulated top and see-through zones the simulated eye. Without a body they still use 1 m as head height while the simulated eye is 0.5 m, unchanged for the same reason as 1.
3. The soldier radius is written in three places and means the same in three more; only W03 ties them together. Status: HC-1 made it one pack key, `combat.body.radius` (default `BodyRadius.DefaultRaw`), and the `scale` relations read it.
4. The comment on the window prefab computed with a 1 m sill; the default is 0.9 m and has no formula. Status: fixed (the comment says 0.9 m; the arithmetic did not change).
5. The pressure range value in the code was not checked. Status: `ai.pressureDeathRadiusMm` and `pressureNearRadiusMm`: only `rwr-showcase` sets them, 3000 / 3000; code not changed.
6. Plate slope has three limits (height field 2/1, gate 5/3, layer rule such as 3/4); a stair legal under one can be refused by another. Status: the effective slope is min(layer, 5/3), and 2/1 is the height field's bound when a layer has no rule; the `scale` stair check is that formula.
7. Speed limits differ: content 300 m/s, terrain and plate worlds 7.5 m/s (an error at the first move), dash 10 m per step. Status: not changed, candidate pool (speed pipeline).
8. "Step" names four things: the 0.3 m seam, the 0.5 m sweep step, the 0.25 m terrain step, and the 1.414 m neighbour difference of generated terrain. Status: listed under "Words for step".
9. Storey height exists only as demo data; the code enforces a stack gap and a thickness, so 2.5 m is the least. Status: `scale.storeyMm` and `MapDocument.StandardBuilding`.
10. There is no low or high wall class (wall kind is a free string); a grounded wall on terrain is at least 1 m, lower ones need a raised base. Status: `scale` wall classes (the wall height base above); grounded walls on terrain below 1 m still need a raised base.
11. Sight is cut at 1.4 m and 1.6 m for a 1.5 m eye; a planned 1.2 m crossing limit would leave 1.2 to 1.4 m walls crossable but see-through and 1.4 to 1.6 m walls opaque but too high. Status: the three wall classes are named in `scale`, and a layer with an `over` traversal kind must let the low wall be vaulted and not the high one. The chest wall belongs to the traversal line. Finding for it: wall heights are even raw, and the test layer's 1.2 m limit is 78643 raw (odd), so a 1.2 m wall (78644 raw) cannot be vaulted; an even limit avoids that.
12. Body height is capped at 2 m by content but at 32768 m by the `CombatBody` constructor; melee half height is 1 m in the shipped data while a 1.75 m body is centred at 0.875 m and an old body at the foot. Status: candidate pool BODY-CAP.
13. Some older comments assume a body 0.5 m either side of its position; with 1.75 m bodies that is out of date. Status: the 0.5 m comments in `Combat/` (`CombatBody.cs:9`, `CombatSystem.cs`, `CombatSystem.Sight.cs`, `CombatSystem.Resolve.cs`, `CombatSystem.Melee.cs:153`) say they describe the legacy geometry without a body, which is right, so they stay. The drawn-body comments in `Presentation/Camera/` (`AimPick.cs`, `LayerCut.cs`, `SeeZone.cs`, `Silhouettes.cs`) now read the drawn sizes and keep 1 m only for worlds without sizes (SCALE-2a). No other out-of-date comment was found.

## Body probe

`Tests/MapContent/Scale/BodyProbeTests.cs` scales the shipped `rwr-stance` body and stances to a 3 m giant and a 0.5 m
tiny unit (every height times target / reference, rounded down) and runs the probes of `BodyProbes.cs`: each measures
one thing that grows on the body (radius copies, collider and stance heights, eye, muzzle, drawn sizes, pick reach, see
height, body-relative play distances) and expects the reference value scaled, or for the headroom checks the world's
value unchanged. World sizes (doors, storeys, sills, walls, streets) are not scaled. A probe that crashes counts as a gap.
What does not follow is listed in `tools/size-guard/body-gaps.tsv`, keyed by probe and body; `GapsMatchTheLedger`
fails on a new gap and on a listed one that now follows. A change that fixes one deletes its row and lowers `gaps-max`
in the same pull request; a probe that reads a constant gets its measuring point changed there too.
