using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // SLOPE-1 replay "slope-race": a 24 x 15 m height field (16:10, the replay page's view) with three 3 m lanes along X, kept apart by 3 m slopes that blend one
    // lane's ground into the next. Lane 1 (z 0..3 m) is flat at 4.8 m; lane 2 (z 6..9 m) rises 26214 raw per metre (about 400
    // permille) along +X; lane 3 (z 12..15 m) rises 39322 raw per metre (about 600 permille) along +X. One unit per lane (squads
    // 1..3, all team 1, nobody fights), all at 3000 mm/s under the "rwr-slope" layer, lane k ordered at tick k (a session takes
    // one scripted command per tick) along its lane's centre line over 18 m: lanes 1 and 2 from x 2.5 m to 20.5 m (flat, uphill), lane 3 from x 20.5 m to 2.5 m (downhill). Uphill above
    // 300 permille is 700, downhill above 400 permille is 800: the flat unit arrives first, the downhill one second, the uphill
    // one last.
    public static class SlopeDemo
    {
        public const string Layer = "rwr-slope";
        public const int Width = 24, Depth = 15;
        public const long Speed = 3000, LowXMm = 2500, HighXMm = 20500;
        public const int FlatRaw = 314573, UpRisePerMetreRaw = 26214, SteepRisePerMetreRaw = 39322;
        public static readonly long[] LaneZMm = { 1500, 7500, 13500 };
        // Lane 3 walks toward -X, down its slope.
        public static long StartXMm(int lane) => lane == 2 ? HighXMm : LowXMm;
        public static long GoalXMm(int lane) => lane == 2 ? LowXMm : HighXMm;
        public static long OrderTick(int lane) => lane + 1;

        public static WorldRecipe Recipe()
        {
            ContentPack pack = WarCore.Content.Rwr.RwrPack.Load(Layer);
            int weapon = pack.WeaponOfUnitType(WarCore.Content.Rwr.RwrPack.SoldierTeam1);
            var units = new UnitSpawn[LaneZMm.Length];
            var script = new BridgeCommand[LaneZMm.Length];
            for (int i = 0; i < units.Length; i++)
            {
                units[i] = new UnitSpawn((ulong)i + 1, 0, i + 1, StartXMm(i), LaneZMm[i], Speed, 1, weapon);
                script[i] = BridgeCommand.MoveTo(OrderTick(i), i + 1, GoalXMm(i), LaneZMm[i]);
            }
            return new WorldRecipe(41, MoverKind.InterimCrowd, units, script).WithCombat(pack.BuildCombat())
                .WithTerrainRules(pack).WithPack(pack);
        }

        // Ground height at grid point (i, j): rows 0..3 lane 1, 6..9 lane 2, 12..15 lane 3, the rows between blend linearly
        // (at most 314592 raw over 3 m, about 1.6 / 1, inside the height field's 2 / 1).
        public static int HeightAt(int i, int j)
        {
            int flat = FlatRaw, up = UpRisePerMetreRaw * i, steep = SteepRisePerMetreRaw * i;
            if (j <= 3) return flat;
            if (j < 6) return flat + (up - flat) * (j - 3) / 3;
            if (j <= 9) return up;
            if (j < 12) return up + (steep - up) * (j - 9) / 3;
            return steep;
        }

        public static byte[] MapBytes()
        {
            var bits = new byte[(Width * Depth + 7) / 8];
            for (int k = 0; k < Width * Depth; k++) bits[k / 8] |= (byte)(1 << (k % 8));
            var points = new int[(Width + 1) * (Depth + 1)];
            for (int j = 0; j <= Depth; j++)
                for (int i = 0; i <= Width; i++) points[j * (Width + 1) + i] = HeightAt(i, j);
            var map = new WcMap("slope-race", "none", default, new SimVec3(SimFix.FromInt(Width), SimFix.FromInt(16), SimFix.FromInt(Depth)),
                SimFix.FromInt(Width), default, Width, Depth, new[] { new WcMapLayer("walls", 1) }, new WcMapWall[0], bits, points, null, null);
            return WcMapWriter.Write(map);
        }
    }
}
