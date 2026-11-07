using System;
using System.Security.Cryptography;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Fixtures of the M6 inertia tests. Every parameter that changes behaviour is written here: seed 41, a flat 40 x 24 m map
    // without walls, one AI unit (key 1, squad 1) at (5 m, 12 m) with 6000 mm/s (13107 raw per tick), ordered at tick 1.
    internal static class InertiaWorlds
    {
        internal const long S = SimFix.Scale;
        internal const long Speed = 6000;
        internal const long StartXMm = 5000, StartZMm = 12000;

        internal static byte[] FlatMap() => WcMapWriter.Write(Wp23.Make(40, 24, 0, 6 * S, null, 4 * S));

        internal static WorldRecipe Line(long toXMm, long toZMm, MoverKind mover = MoverKind.Interim) =>
            new WorldRecipe(41, mover, new[] { new UnitSpawn(1, 0, 1, StartXMm, StartZMm, Speed) },
                new[] { BridgeCommand.MoveTo(1, 1, toXMm, toZMm) });

        internal static SimSession Build(WorldRecipe recipe, byte[] map, bool reverse = false) => WorldBuilder.Build(recipe, map, reverse);

        internal static string Sha(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }

        internal static string SnapshotSha(SimSession s) => Sha(M3TestWorld.Sim(s).SaveRetainedSnapshot());
    }
}
