using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // BLAST-1 replay "blast-wall": a flat 40 x 24 m map with one wall 3 m high (x 16 to 17 m, z 4 to 20 m). Four team 1 units
    // stand west of it at different distances from the blast point (14, 12) m, four team 2 units east of it, all within the
    // blast's outer radius. The wall hides each group from the other, so nobody shoots. On tick 60 a scripted rwr.frag goes off
    // at the blast point: the west group is hurt by distance, the east group is untouched. Shared by the replay tool and the tests.
    public static class BlastDemo
    {
        public const string Layer = "rwr-blast", Blast = "rwr.frag";
        public const long BlastTick = 60, BlastXMm = 14000, BlastZMm = 12000;
        private const int Width = 40, Depth = 24, WallX = 16, WallZ = 4, WallDepth = 16, WallHeight = 3;

        // West of the wall (team 1): horizontal distances 1, 1.5, 3 and 4.5 m from the blast point. East (team 2): 4 to 5.2 m.
        public static readonly long[][] West = { new[] { 13000L, 12000L }, new[] { 14000L, 13500L }, new[] { 11000L, 12000L }, new[] { 14000L, 7500L } };
        public static readonly long[][] East = { new[] { 18000L, 12000L }, new[] { 18000L, 13500L }, new[] { 18000L, 10500L }, new[] { 19000L, 12000L } };

        public static WcMap Map()
        {
            var halfX = SimFix.Ratio(1, 2); var halfZ = SimFix.FromInt(WallDepth / 2);
            var walls = new[]
            {
                new WcMapWall(0, 1, "wall", "walls", new SimVec3(SimFix.FromInt(WallX) + halfX, SimFix.Ratio(WallHeight, 2), SimFix.FromInt(WallZ) + halfZ),
                    halfX, SimFix.FromInt(WallHeight), halfZ),
            };
            var bits = new byte[(Width * Depth + 7) / 8];
            for (int z = 0; z < Depth; z++)
                for (int x = 0; x < Width; x++)
                    if (!(x == WallX && z >= WallZ && z < WallZ + WallDepth))
                        bits[(z * Width + x) >> 3] |= (byte)(1 << ((z * Width + x) & 7));
            return new WcMap("blast-wall", "none", default, SimVec3.Metres(Width, 4, Depth), SimFix.FromInt(Width),
                default, Width, Depth, new[] { new WcMapLayer("walls", 1) }, walls, bits);
        }

        public static byte[] MapBytes() => WcMapWriter.Write(Map());

        public static WorldRecipe Recipe() => Recipe(WarCore.Content.Rwr.RwrPack.Load(Layer));

        public static WorldRecipe Recipe(ContentPack pack)
        {
            int west = pack.WeaponOfUnitType(WarCore.Content.Rwr.RwrPack.SoldierTeam1), east = pack.WeaponOfUnitType(WarCore.Content.Rwr.RwrPack.SoldierTeam2);
            var units = new UnitSpawn[West.Length + East.Length];
            for (int i = 0; i < West.Length; i++)
                units[i] = new UnitSpawn((ulong)i + 1, 0, 1, West[i][0], West[i][1], 3000, 1, west); // PLACEHOLDER speed.
            for (int i = 0; i < East.Length; i++)
                units[West.Length + i] = new UnitSpawn((ulong)(West.Length + i) + 1, 0, 2, East[i][0], East[i][1], 3000, 2, east);
            return new WorldRecipe(41, MoverKind.InterimCrowd, units, new[] { BridgeCommand.Blast(BlastTick, BlastXMm, BlastZMm, Blast) })
                .WithCombat(pack.BuildCombat());
        }
    }
}
