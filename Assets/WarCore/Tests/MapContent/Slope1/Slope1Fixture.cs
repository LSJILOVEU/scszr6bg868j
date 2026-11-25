using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // SLOPE-1 fixtures: test layers over rwr-field (or rwr-stance), small height-field maps, AI and player sessions, and
    // one-step measurements. Every expected number in the tests is written out by hand from the formulas in the comments.
    internal static class Slope1
    {
        internal const long S = SimFix.Scale;
        // 4000 mm/s at 30 Hz: 4000 * 65536 / 30000 = 8738.13 -> 8738 raw per tick.
        internal const long Base4000 = 8738;
        // Rises per metre (raw): 39322 (600.006 permille), 26214 (399.99 permille), 6554 (100.01 permille).
        internal const int Rise600 = 39322, Rise400 = 26214;

        // The rwr-slope numbers: up > 100: 900, > 300: 700, > 500: 500; down > 400: 800.
        internal const string SlopeJson = "\"slope\": { \"up\": [ { \"abovePermille\": 100, \"speedPermille\": 900 }, "
            + "{ \"abovePermille\": 300, \"speedPermille\": 700 }, { \"abovePermille\": 500, \"speedPermille\": 500 } ], "
            + "\"down\": [ { \"abovePermille\": 400, \"speedPermille\": 800 } ] }";
        internal const string MovementJson = "\"movement\": { " + SlopeJson + " }";

        internal static string Stats(int floor = 0, int ceil = 0)
        {
            var parts = new List<string>();
            if (floor != 0) parts.Add("\"dragFloorPermille\": " + floor);
            if (ceil != 0) parts.Add("\"dragCeilPermille\": " + ceil);
            return "\"stats\": { \"speed\": { " + string.Join(", ", parts) + " } }";
        }

        // A layer `id` extending `parent` with the given extra top-level members (JSON text without braces), or none.
        internal static ContentPack Layer(string id, string extra, string parent = "rwr-field")
        {
            string json = "{ \"id\": \"" + id + "\", \"extends\": \"" + parent + "\"" + (string.IsNullOrEmpty(extra) ? "" : ", " + extra) + " }";
            return ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string> { [id] = json }, FileContentSource.Rwr()), id);
        }

        internal static ContentPack Sloped(string id = "slope-test") => Layer(id, MovementJson);
        internal static ContentPack Plain(string id = "slope-test") => Layer(id, null);

        // A map `width` x `depth` with heights (raw per grid point) and every cell on surface `surface` (no surface table when null).
        internal static WcMap Map(int width, int depth, Func<int, int, int> height, string surface = null) =>
            Wp23.Make(width, depth, 0, 20 * S, null, 4 * S, height, surface == null ? null : new[] { surface },
                surface == null ? (Func<int, int, byte>)null : (x, z) => 0);

        // A plane rising `rise` raw per metre along +X (heights 0 at x = 0).
        internal static WcMap RampX(int width, int depth, int rise, string surface = null) => Map(width, depth, (i, j) => rise * i, surface);

        internal static UnitSpawn Unit(ulong key, long xMm, long zMm, long speed = 4000, int squad = 0) =>
            new UnitSpawn(key, 0, squad == 0 ? (int)key : squad, xMm, zMm, speed);

        // An AI world: crowd or path mover, reading the pack.
        internal static SimSession Ai(WcMap map, ContentPack pack, UnitSpawn[] units, BridgeCommand[] script = null,
            MoverKind mover = MoverKind.InterimCrowd) =>
            SimSession.Create(new WorldRecipe(41, mover, units, script ?? Array.Empty<BridgeCommand>()).WithPack(pack), WcMapWriter.Write(map));

        // A player world: key 1 is the player (team 1), key 99 a still enemy in the far corner so the quiet match is not over
        // (crowd mover, the pack's own combat, nobody armed).
        internal static SimSession Player(WcMap map, ContentPack pack, long xMm, long zMm, long speed = 4000, UnitSpawn[] extra = null) =>
            SimSession.Create(PlayerRecipe(map, pack, xMm, zMm, speed, extra), WcMapWriter.Write(map));

        internal static WorldRecipe PlayerRecipe(WcMap map, ContentPack pack, long xMm, long zMm, long speed = 4000, UnitSpawn[] extra = null)
        {
            var units = new List<UnitSpawn> { new UnitSpawn(1, 0, 1, xMm, zMm, speed, 1),
                new UnitSpawn(99, 0, 2, map.Width * 1000L - 500, map.Depth * 1000L - 500, speed, 2) };
            if (extra != null) units.AddRange(extra);
            return new WorldRecipe(41, MoverKind.InterimCrowd, units.ToArray(), Array.Empty<BridgeCommand>())
                .WithCombat(pack.BuildCombat()).WithMatch(L10Fixture.QuietMatch()).WithPack(pack).WithPlayer(1);
        }

        internal static SimVec3 At(SimSession s, ulong key = 1) => M3TestWorld.Position(s, key);
        internal static InterimLocomotion Loco(SimSession s) => M3TestWorld.Locomotion(s);
        internal static EntityId Id(SimSession s, ulong key = 1) => M3TestWorld.Unit(s, key);
        internal static ulong Hash(SimSession s) => M3TestWorld.Sim(s).StateHashRetained();

        // Steps until key moves (at most 20 ticks) and returns that first displacement.
        internal static SimVec3 FirstMove(SimSession s, ulong key = 1)
        {
            for (int t = 0; t < 20; t++)
            {
                SimVec3 before = At(s, key);
                s.StepOnce();
                SimVec3 d = At(s, key) - before;
                if (d.X.Raw != 0 || d.Z.Raw != 0) return d;
            }
            Assert.Fail("unit " + key + " never moved");
            return default;
        }

        // The player's displacement over one tick with the input held.
        internal static SimVec3 PlayerStep(SimSession s, int x, int z)
        {
            s.SetPlayerInput(s.Tick + 1, new PlayerInput(x, z));
            SimVec3 before = At(s);
            s.StepOnce();
            return At(s) - before;
        }

        internal static SimVec3 Dir(int x, int z) => new SimVec3(SimFix.FromInt(x), default, SimFix.FromInt(z));

        internal static long Scaled(long baseRaw, params int[] factors)
        {
            long value = baseRaw;
            long den = 1;
            foreach (int f in factors) { value *= f; den *= 1000; }
            return value / den;
        }

        // A drag with a fixed value, for the registration point.
        internal sealed class FixedDrag : IStepDrag
        {
            private readonly int value;
            internal int Calls;
            internal FixedDrag(int value) { this.value = value; }
            public int Permille(EntityId unit) { Calls++; return value; }
        }
    }
}
