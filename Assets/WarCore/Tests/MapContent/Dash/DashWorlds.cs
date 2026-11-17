using System;
using System.Collections.Generic;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests.Dash
{
    // Fixtures of the M7a dash tests. Seed 41, crowd mover, a flat 80 x 24 m map (walls 4 m high, cells of 1 m), every unit at
    // 6000 mm/s (13107 raw = 0.2 m per tick) with radius 0.25 m. The standard dash covers 20 m in 10 ticks, 2 m per tick: ten
    // times the unit's own step. Its cooldown is 60 ticks.
    internal static class DashWorlds
    {
        internal const long S = SimFix.Scale;
        internal const long Speed = 6000;
        internal const long DistanceMm = 20000;
        internal const int Ticks = 10, Cooldown = 60;

        internal static readonly SimVec3 PlusX = SimVec3.Metres(1, 0, 0), MinusX = SimVec3.Metres(-1, 0, 0), PlusZ = SimVec3.Metres(0, 0, 1);

        internal static UnitSpawn Dasher(ulong key, long xMm, long zMm, int team = 1, long distanceMm = DistanceMm, int ticks = Ticks,
            int cooldown = Cooldown) => new UnitSpawn(key, 0, (int)key, xMm, zMm, Speed, team).WithDash(distanceMm, ticks, cooldown);

        internal static UnitSpawn Plain(ulong key, long xMm, long zMm, int team = 2) => new UnitSpawn(key, 0, (int)key, xMm, zMm, Speed, team);

        internal static byte[] Map(params (int x, int z, int w, int d)[] walls) =>
            WcMapWriter.Write(Wp23.Make(80, 24, 0, 6 * S, walls, 4 * S));

        internal static SimSession World(UnitSpawn[] units, byte[] map, BridgeCommand[] script = null, Func<WorldRecipe, WorldRecipe> open = null)
        {
            var recipe = new WorldRecipe(41, MoverKind.InterimCrowd, units, script ?? Array.Empty<BridgeCommand>());
            return WorldBuilder.Build(open == null ? recipe : open(recipe), map, false);
        }

        internal static InterimLocomotion Loco(SimSession s) => M3TestWorld.Locomotion(s);
        internal static bool Dash(SimSession s, SimVec3 direction, ulong key = 1) => Loco(s).TryDash(M3TestWorld.Unit(s, key), direction);
        internal static DashState State(SimSession s, ulong key = 1) => M3TestWorld.Component<DashState>(s, key);
        internal static long Tick(SimSession s) => M3TestWorld.Sim(s).Clock.Tick;

        // Positions of unit key after each of n steps, the start first.
        internal static List<SimVec3> Track(SimSession s, int n, ulong key = 1)
        {
            var track = new List<SimVec3> { M3TestWorld.Position(s, key) };
            for (int i = 0; i < n; i++) { s.StepOnce(); track.Add(M3TestWorld.Position(s, key)); }
            return track;
        }

        // Distance, in metres, from point p to the segment a-b on the horizontal plane (double, independent of the sim's math).
        internal static double SegmentDistance(SimVec3 a, SimVec3 b, SimVec3 p)
        {
            double ax = a.X.Raw / (double)S, az = a.Z.Raw / (double)S, bx = b.X.Raw / (double)S, bz = b.Z.Raw / (double)S;
            double px = p.X.Raw / (double)S, pz = p.Z.Raw / (double)S;
            double dx = bx - ax, dz = bz - az, len = dx * dx + dz * dz;
            double t = len == 0 ? 0 : Math.Max(0, Math.Min(1, ((px - ax) * dx + (pz - az) * dz) / len));
            double cx = ax + t * dx - px, cz = az + t * dz - pz;
            return Math.Sqrt(cx * cx + cz * cz);
        }
    }
}
