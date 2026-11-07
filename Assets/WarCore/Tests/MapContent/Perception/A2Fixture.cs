using System;
using System.Collections.Generic;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // A2 fixtures: a flat terrain world (height field all 0) of 40 x 30 cells with two surfaces, "dirt" (index 0) and
    // "grass" (index 1). Everything that shapes behaviour is written here: seed 41, the map, the unit table, the content
    // layer (extends rwr; the surfaces and slope rule are literals), the combat pack, the match pack, the thinking interval.
    internal static class A2
    {
        internal const long S = SimFix.Scale;
        internal const int Width = 40, Depth = 30, Think = 10;
        internal static readonly string[] Names = { "dirt", "grass" };

        // The grass patch: cells x 18..23, z 22..27.
        internal static bool InPatch(int x, int z) => x >= 18 && x < 24 && z >= 22 && z < 28;

        // dirt / grass: the text appended to each surface entry (for example ", \"concealMilli\": 500"), empty for none.
        internal static string PackJson(string dirt = "", string grass = "") => "{ \"id\": \"a2-test\", \"extends\": \"rwr\", "
            + "\"surfaces\": [ { \"id\": \"dirt\"" + dirt + " }, { \"id\": \"grass\", \"speedMilli\": 800" + grass + " } ], "
            + "\"slopeRule\": { \"maxWalkableNum\": 3, \"maxWalkableDen\": 4 } }";
        internal static ContentPack Pack(string dirt = "", string grass = "") =>
            ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string> { ["a2-test"] = PackJson(dirt, grass) }, FileContentSource.Rwr()), "a2-test");

        // Cell (x, z) has its centre at (x + 0.5, z + 0.5) m.
        internal static SimVec3 Cell(int x, int z) => new SimVec3(SimFix.FromRaw(x * S + S / 2), default, SimFix.FromRaw(z * S + S / 2));

        // patch: which cells are grass (null = the grass patch above).
        internal static WcMap Map((int, int, int, int)[] boxes, Func<int, int, bool> patch = null)
        {
            patch ??= InPatch;
            return Wp23.Make(Width, Depth, 0, 4 * S, boxes ?? Array.Empty<(int, int, int, int)>(), 2 * S, (x, z) => 0, Names,
                (x, z) => patch(x, z) ? (byte)1 : (byte)0);
        }

        // One weapon: 1 s between shots (30 ticks), 3 rounds, 2 s reload, falloff 10..30 m, the given effect.
        internal static WeaponDef Rifle(long effectRaw) =>
            new WeaponDef(30000, 3, 60000, SimFix.FromInt(10), SimFix.FromInt(30), effectRaw);
        internal static readonly SimFix Sight = SimFix.FromInt(20);
        internal static CombatPack Combat(long effectRaw) =>
            new CombatPack(new[] { Rifle(effectRaw) }, new RwrHitResolver(), new SightLimited(new NearestVisibleEnemy(), Sight), 100 * S);

        internal static readonly SimVec3 PointCenter = Cell(30, 10);
        internal static MatchPack Match() =>
            new MatchPack(new[] { new ControlPointDef(1, PointCenter, SimFix.Ratio(1, 2)) },
                new[] { new SpawnAnchor(1, Cell(1, 10)), new SpawnAnchor(2, Cell(38, 10)) },
                2, 1, false, 100000, InterruptMode.Reset, 1, 90, Think, 9000, Sight, 4, 4, SimFix.Ratio(3, 2));

        internal static UnitSpawn Soldier(ulong key, int team, SimVec3 at, int weapon, long speed) =>
            new UnitSpawn(key, 0, team, at.X.Raw * 1000 / S, at.Z.Raw * 1000 / S, speed, team, weapon, false);

        internal static WorldRecipe Recipe(UnitSpawn[] units, CombatPack combat, ContentPack pack, bool standing = false)
        {
            WorldRecipe r = new WorldRecipe(41, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>())
                .WithMatch(Match()).WithCombat(combat).WithCrowdPass(CrowdPassKind.TeamIdle)
                .WithRuleSet(MatchMode.TestNeverEndsId).WithTerrainRules(pack);
            return standing ? r.WithStandingOrders() : r;
        }

        // Fixture D (duel): two armed soldiers a side, one of team 2 inside the grass patch, a wall between the lines.
        internal static readonly (int, int, int, int)[] DWalls = { (15, 13, 1, 5) };
        internal static UnitSpawn[] DUnits() => new[]
        {
            Soldier(1, 1, Cell(4, 10), 0, 3000), Soldier(2, 1, Cell(4, 16), 0, 3000),
            Soldier(3, 2, Cell(21, 24), 0, 3000), Soldier(4, 2, Cell(34, 14), 0, 3000),
        };
        internal static M6World D(ContentPack pack, bool reverse = false) =>
            new M6World(Recipe(DUnits(), Combat(6554), pack), WcMapWriter.Write(Map(DWalls)), reverse);

        // Folds the per-tick hashes of ticks 0..last into one value (FNV-1a over the 64-bit hashes); counts the fires.
        // log, when given, receives every fire record with its tick.
        internal static ulong HashRun(M6World w, int last, List<ulong> each, out int fires, List<(int, FireRecord)> log = null)
        {
            ulong fold = 14695981039346656037UL;
            fires = 0;
            for (int tick = 0; tick <= last; tick++)
            {
                if (tick > 0)
                {
                    w.Step(); fires += w.Combat.Fires.Length;
                    if (log != null) foreach (FireRecord f in w.Combat.Fires) log.Add((tick, f));
                }
                ulong h = w.Hash; each?.Add(h);
                fold = (fold ^ h) * 1099511628211UL;
            }
            return fold;
        }

        internal static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "");

        // ---- Direct selection (acceptance 2-7): a combat-only flat world, units moved by hand, a grid built here. ----

        internal static NavGrid Grid(int width, int depth)
        {
            var bits = new byte[(width * depth + 7) / 8];
            for (int i = 0; i < width * depth; i++) bits[i / 8] |= (byte)(1 << (i % 8));
            return new NavGrid(width, depth, default, default, bits);
        }

        internal static short[] Cover(int width, int depth, Func<int, int, short> k)
        {
            var cells = new short[width * depth];
            for (int z = 0; z < depth; z++)
                for (int x = 0; x < width; x++) cells[z * width + x] = k(x, z);
            return cells;
        }

        internal static ITargetSelector Limited() => new SightLimited(new NearestVisibleEnemy(), Sight);

        // Unit 0 (team 1) is the shooter; the rest are team 2. No weapon fires: the world is never stepped.
        internal static M5TestWorld Direct(int enemies)
        {
            var units = new UnitSpawn[enemies + 1];
            units[0] = M5TestWorld.Spawn(1, 1000, 1000);
            for (int i = 1; i <= enemies; i++) units[i] = M5TestWorld.Spawn(i + 1, 1000 + 2000 * i, 3000, team: 2, weapon: -1, squad: 2);
            return new M5TestWorld(units, Combat(0));
        }

        internal static void Place(M5TestWorld w, int unit, long xRaw, long zRaw)
        {
            w.Sim.Entities.Set(w.Positions, w.Unit(unit), new Position(new SimVec3(SimFix.FromRaw(xRaw), default, SimFix.FromRaw(zRaw))));
            w.Sim.CompleteSpatialBoundary();
        }

        // The selection, as (found, index of the chosen unit or -1).
        internal static (bool, int) Pick(M5TestWorld w, ITargetSelector selector, SimFix range)
        {
            bool found = selector.TrySelect(w.Combat, w.Unit(0), range, out EntityId target);
            if (!found) return (false, -1);
            for (int i = 0; i < w.Count; i++) if (w.Unit(i).Equals(target)) return (true, i);
            return (true, -2);
        }
    }
}
