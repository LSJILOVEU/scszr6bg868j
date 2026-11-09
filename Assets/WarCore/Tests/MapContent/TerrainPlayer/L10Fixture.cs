using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // L10 frozen fixtures. Every behaviour parameter is a literal here or comes from the frozen L6 fixture (map with its E1a
    // sha check, content layer l6-test); nothing reads DemoRecipes, Wp23.Pack or the shipped terrain layer.
    internal static class L10Fixture
    {
        internal const long S = SimFix.Scale;
        internal const long Grass6000 = 13107;   // 6000 mm/s at 30 Hz: 6000 * 65536 / 30000, floored
        // The L6 layer with grass at the largest multiplier (2000); every other value as in l6-test.
        internal static string FastJson => L6.PackJson.Replace("\"id\": \"l6-test\"", "\"id\": \"l10-fast\"")
            .Replace("{ \"id\": \"grass\", \"passable\": true, \"speedMilli\": 1000,", "{ \"id\": \"grass\", \"passable\": true, \"speedMilli\": 2000,");

        private sealed class NoHit : IHitResolver
        {
            public ulong PolicyKey => 0x4C31300000000001;
            public long DamageRaw(in WeaponDef weapon, long falloffRaw, int rollRaw, long maxHealthRaw) => 0;
        }

        internal static UnitSpawn U(ulong key, long xMm, long zMm, int team = 1, long speed = 6000, int weapon = -1, bool reserve = false) =>
            new UnitSpawn(key, 0, team, xMm, zMm, speed, team, weapon, reserve);

        // No weapons, no melee: nobody fires, nobody dies.
        internal static CombatPack Off() => new CombatPack(Array.Empty<WeaponDef>(), new NoHit(), new NearestVisibleEnemy(), 100 * S,
            friendlyFire: false, friendlyFireLocked: false, allyCheckMissRaw: 0, allyHoldMilli: 30000, melee: null, block: null);

        // A match that never thinks, never reinforces and never ends inside a test (point far away, time limit 9000).
        internal static MatchPack QuietMatch(SpawnAnchor[] bases = null, int respawn = 0) => new MatchPack(
            new[] { new ControlPointDef(1, SimVec3.Metres(24, 0, 2), SimFix.FromInt(1)) },
            bases ?? new[] { new SpawnAnchor(1, SimVec3.Metres(3, 0, 16)), new SpawnAnchor(2, SimVec3.Metres(44, 0, 16)) },
            2, 1, false, 8000, InterruptMode.Reset, 1, 9000, 1000000, 9000, SimFix.FromInt(35), 4, 4, SimFix.Ratio(3, 2), respawn);

        // The demo terrain (L6 bytes) with the L6 terrain rules; the player is key 1, a still enemy (key 2) stands far away.
        internal static M6World Demo(long xMm, long zMm, long speed = 6000, bool facing = false, ContentPack rules = null,
            byte[] map = null, UnitSpawn[] extra = null, CombatPack combat = null, int weapon = -1)
        {
            // The still enemy: far corner of the demo map, or of the 16 x 16 test maps.
            UnitSpawn enemy = map == null || map.Length == L6.MapBytes().Length ? U(2, 44500, 28500, team: 2) : U(2, 14500, 1500, team: 2);
            var units = new[] { U(1, xMm, zMm, speed: speed, weapon: weapon), enemy }.Concat(extra ?? Array.Empty<UnitSpawn>()).ToArray();
            WorldRecipe recipe = M6World.RecipeOf(units, QuietMatch(), combat ?? Off()).WithTerrainRules(rules ?? L6.Pack()).WithPlayer(1);
            if (facing) recipe = recipe.WithFacing();
            return new M6World(recipe, map ?? L6.MapBytes());
        }

        internal static ContentPack Fast() => L6.Load("l10-fast", FastJson);

        // A 16 x 16 map rising along Z: H(i, j) = j half-metres (slope 1/2), no surface table. Two 8 m wall boxes (above every
        // ground point under them, WALL_TERRAIN): a 1 m strip at x = 10, z in [4, 12), and a single cell (5, 10).
        internal static byte[] ZSlope() => WcMapWriter.Write(Wp23.Make(16, 16, 0, 10 * S, new[] { (10, 4, 1, 8), (5, 10, 1, 1) }, 8 * S,
            (i, j) => (int)(j * S / 2)));

        // The demo heights without a surface table, and a flat map with the demo surface table only.
        internal static byte[] HeightsOnly() => WcMapWriter.Write(Wp23.Make(48, 32, 0, 10 * S, null, 4 * S,
            (i, j) => (int)(DemoUnits(i, j) * S / 2)));
        internal static byte[] SurfacesOnly() => WcMapWriter.Write(Wp23.Make(48, 32, 0, 10 * S, null, 4 * S, null,
            new[] { "grass", "mud", "road" },
            (x, z) => (byte)(z >= 14 && z < 18 ? 2 : x >= 36 && x < 40 && z >= 4 && z < 12 ? 1 : 0)));

        // A flat 48 x 32 map: no height field, no surface table (a non-terrain map, so the flat branch steps the player).
        internal static byte[] Flat() => WcMapWriter.Write(Wp23.Make(48, 32, 0, 10 * S, null, 4 * S));

        // The demo map's height units (half-metres), copied as a literal rule (TerrainDemoMap.cs:38-42 at the base).
        private static int DemoUnits(int i, int j) =>
            (i <= 10 ? 0 : i <= 22 ? i - 10 : i <= 34 ? 34 - i : 0) + (j >= 23 && i >= 14 && i <= 30 ? 2 : 0);

        internal static SimVec3 At(M6World w, ulong key = 1) => w.Read<Position>(key).Value;

        // The player's point lies on the ground of the session's height field, to the raw.
        internal static bool OnGround(M6World w, SimVec3 p) =>
            w.Session.Terrain.Ground.TryHeightAt(p.X, p.Z, out SimFix y) && y.Raw == p.Y.Raw;

        // Steps with one held input and returns every point after each tick.
        internal static SimVec3[] Walk(M6World w, int x, int z, int ticks)
        {
            w.Session.SetPlayerInput(w.Session.Tick + 1, new PlayerInput(x, z));
            var points = new SimVec3[ticks];
            for (int t = 0; t < ticks; t++) { w.Session.StepOnce(); points[t] = At(w); }
            return points;
        }

        // The demo match frozen in L6, with key 1 as the player and respawn 300 (copied here instead of DemoRecipes.WithRespawn).
        internal static WorldRecipe Twin()
        {
            WorldRecipe source = L6.Recipe().WithPlayer(1);
            MatchPack m = source.Match;
            return source.WithMatch(new MatchPack(m.Points.ToArray(), m.Bases.ToArray(), m.Num, m.Den, m.Strict, m.CaptureTicks,
                m.Interrupt, m.DecayPerTick, m.IntervalTicks, m.ThinkInterval, m.MaxTicks, m.Sight, m.GridColumns, m.GridRows,
                m.GridSpacing, 300));
        }

        internal static void Queue(SimSession s, (long AtTick, PlayerInput Input)[] script)
        {
            foreach (var item in script) s.SetPlayerInput(item.AtTick, item.Input);
        }
    }
}
