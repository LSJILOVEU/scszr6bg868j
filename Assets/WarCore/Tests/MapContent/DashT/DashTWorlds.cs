using System;
using System.Collections.Generic;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using WarCore.Tests.Dash;

namespace WarCore.Tests.DashT
{
    // DASH-T fixtures. Terrain worlds: 24 x 8 cells of 1 m with a height field (flat at 0 unless a test gives heights) and a
    // surface table fast (x 2), grass (x 1), mud (x 0.5) and rock (not walkable); slope rule 3/4. Units walk at 6000 mm/s
    // (0.2 m per tick, under the terrain cap of 0.25 m) with radius 0.25 m. The standard DASH-T dash covers 4 m in 6 ticks
    // (cooldown 60): 4000 mm = 262144 raw.
    internal static class DashTWorlds
    {
        internal const long S = SimFix.Scale;
        internal const byte Fast = 0, Grass = 1, Mud = 2, Rock = 3;
        internal const string PackJson = "{ \"id\": \"rwr-terrain-demo\", \"extends\": \"rwr\", \"surfaces\": [ { \"id\": \"grass\" }, "
            + "{ \"id\": \"mud\", \"speedMilli\": 500, \"cost\": 3 }, { \"id\": \"fast\", \"speedMilli\": 2000 }, { \"id\": \"rock\", \"passable\": false } ], "
            + "\"slopeRule\": { \"maxWalkableNum\": 3, \"maxWalkableDen\": 4 } }";
        // The map names its surfaces in ordinal order.
        internal static readonly string[] Names = { "fast", "grass", "mud", "rock" };

        internal static ContentPack Pack() => Wp23.Pack(PackJson);

        internal static UnitSpawn Dasher(ulong key, long xMm, long zMm, long distanceMm = 4000, int ticks = 6, int cooldown = 60) =>
            new UnitSpawn(key, 0, (int)key, xMm, zMm, DashWorlds.Speed, 1).WithDash(distanceMm, ticks, cooldown);

        internal static UnitSpawn Plain(ulong key, long xMm, long zMm) => new UnitSpawn(key, 0, (int)key, xMm, zMm, DashWorlds.Speed, 1);

        internal static WcMap Map(Func<int, int, byte> surface = null, Func<int, int, int> height = null, int width = 24, int depth = 8,
            params (int x, int z, int w, int d)[] walls) =>
            Wp23.Make(width, depth, 0, 6 * S, walls, 4 * S, height ?? ((i, j) => 0), Names, surface ?? ((x, z) => Grass));

        internal static SimSession World(WcMap map, UnitSpawn[] units, BridgeCommand[] script = null) =>
            SimSession.Create(new WorldRecipe(41, MoverKind.InterimCrowd, units, script ?? Array.Empty<BridgeCommand>()).WithPack(Pack()),
                WcMapWriter.Write(map));

        // The ML-8 demo house (PlateDemoMap) with these units and no AI.
        internal static SimSession House(UnitSpawn[] units, BridgeCommand[] script = null) =>
            SimSession.Create(new WorldRecipe(41, MoverKind.InterimCrowd, units, script ?? Array.Empty<BridgeCommand>()).WithPlatesPreview(),
                PlateDemoMap.Bytes());

        internal static int Layer(SimSession s, ulong key = 1) => M3TestWorld.Component<VerticalState>(s, key).Layer;

        // Each tick's change of x (raw) over n ticks.
        internal static long[] Deltas(List<SimVec3> track, Func<SimVec3, long> axis)
        {
            var d = new long[track.Count - 1];
            for (int i = 1; i < track.Count; i++) d[i - 1] = axis(track[i]) - axis(track[i - 1]);
            return d;
        }
    }
}
