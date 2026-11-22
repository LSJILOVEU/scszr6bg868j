using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // STANCE-1 replay "stance-wall": a flat 40 x 16 m field with a 1 m wall (x 24..25 m, z 3..13 m). Team 2 (squad 2, four men)
    // stands right behind it at x 25.5 m, team 1 (squad 1, four men) 20 m away at x 5.5 m; both carry the shipped soldier
    // weapons of the "rwr-stance" layer (hits wound, see DemoLayer). Squad 2 crouches at tick 60 and stands up at tick 240.
    // Nobody moves.
    public static class StanceDemo
    {
        public const string Layer = "rwr-stance";
        public const long CrouchTick = 60, StandTick = 240;
        private const int Width = 40, Depth = 16;
        private const long Speed = 6000;
        private const long WallX0 = 24000, WallX1 = 25000, WallZ0 = 3000, WallZ1 = 13000, WallTopRaw = SimFix.Scale;

        public static WorldRecipe Recipe()
        {
            ContentPack pack = ContentLoader.Load(new DemoSource(), DemoLayer);
            int weapon1 = pack.WeaponOfUnitType(WarCore.Content.Rwr.RwrPack.SoldierTeam1), weapon2 = pack.WeaponOfUnitType(WarCore.Content.Rwr.RwrPack.SoldierTeam2);
            var units = new UnitSpawn[8];
            for (int i = 0; i < 4; i++)
            {
                long z = 5000 + 2000 * i;
                units[i] = new UnitSpawn((ulong)i + 1, 0, 1, 5500, z, Speed, 1, weapon1);
                units[i + 4] = new UnitSpawn((ulong)i + 5, 0, 2, 25500, z, Speed, 2, weapon2);
            }
            var script = new[] { BridgeCommand.Stance(CrouchTick, 2, 1), BridgeCommand.Stance(StandTick, 2, 0) };
            return new WorldRecipe(41, MoverKind.InterimCrowd, units, script).WithCombat(pack.BuildCombat());
        }

        // The demo's own layer, in memory: rwr-stance with the "damage" resolver (a hit takes the weapon's effect, half a point)
        // and 40 health, so that both sides live through the crouch and still trade hits after standing up (the shipped
        // resolver kills with one hit).
        public const string DemoLayer = "stance-wall";
        private const string DemoJson = "{ \"id\": \"" + DemoLayer + "\", \"extends\": \"" + Layer + "\", \"combat\": { \"maxHealth\": 40, \"resolver\": \"damage\" } }";

        private sealed class DemoSource : IContentSource
        {
            private readonly IContentSource files = FileContentSource.Rwr();
            public bool TryRead(string layerId, out string json, out string location)
            {
                if (layerId != DemoLayer) return files.TryRead(layerId, out json, out location);
                json = DemoJson; location = "demo:" + DemoLayer; return true;
            }
        }

        public static byte[] MapBytes()
        {
            var wall = new WcMapWall(0, 1, "wall", "walls",
                new SimVec3(SimFix.Ratio(WallX0 + WallX1, 2000), SimFix.FromRaw(WallTopRaw / 2), SimFix.Ratio(WallZ0 + WallZ1, 2000)),
                SimFix.Ratio(WallX1 - WallX0, 2000), SimFix.FromRaw(WallTopRaw), SimFix.Ratio(WallZ1 - WallZ0, 2000));
            var bits = new byte[(Width * Depth + 7) / 8];
            for (int z = 0; z < Depth; z++)
                for (int x = 0; x < Width; x++)
                {
                    bool blocked = x * 1000 < WallX1 && (x + 1) * 1000 > WallX0 && z * 1000 < WallZ1 && (z + 1) * 1000 > WallZ0;
                    if (!blocked) { int k = z * Width + x; bits[k / 8] |= (byte)(1 << (k % 8)); }
                }
            var map = new WcMap("stance-wall", "none", default, new SimVec3(SimFix.FromInt(Width), SimFix.FromInt(6), SimFix.FromInt(Depth)),
                SimFix.FromInt(Width), default, Width, Depth, new[] { new WcMapLayer("walls", 1) }, new[] { wall }, bits);
            return WcMapWriter.Write(map);
        }
    }
}
