using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // VSPREAD-1 "vspread-range": three lanes 10 m apart on a flat 40 x 32 m map with no walls. In each lane a standing shooter
    // (team 1) at x = 5 m fires at an unarmed target (team 2) 30 m away: standing in lane 0, crouched in lane 1, prone in
    // lane 2 (asked at ticks 1 and 2, the session takes one command a tick; all settled by SettleTick). The demo layer is
    // rwr-vspread with weapon 1 made a fixed 2000 millidegree gun (no growth, no recovery) that fires every tick from a
    // 1000-round magazine and does no damage, so the targets live and every shot is one sample of the spread.
    public static class VspreadDemo
    {
        public const string Layer = "rwr-vspread", DemoLayer = "vspread-range";
        public const int Width = 40, Depth = 32, Lanes = 3;
        public const long ShooterXMm = 5000, TargetXMm = 35000, Speed = 6000, SettleTick = 30;
        private const long FirstLaneZMm = 6000, LaneStepMm = 10000;

        // The z of a lane (mm); shooters have keys 1..3, targets 4..6 (lane = key - 1 and key - 4).
        public static long LaneZMm(int lane) => FirstLaneZMm + LaneStepMm * lane;

        public static ContentPack Content() => ContentLoader.Load(new DemoSource(), DemoLayer);

        public static WorldRecipe Recipe()
        {
            ContentPack pack = Content();
            int weapon = pack.WeaponIndex("rwr.weapon-1");
            var units = new UnitSpawn[2 * Lanes];
            for (int lane = 0; lane < Lanes; lane++)
            {
                units[lane] = new UnitSpawn((ulong)lane + 1, 0, lane + 1, ShooterXMm, LaneZMm(lane), Speed, 1, weapon);
                units[lane + Lanes] = new UnitSpawn((ulong)lane + 4, 0, lane + 4, TargetXMm, LaneZMm(lane), Speed, 2, -1);
            }
            var script = new[] { BridgeCommand.Stance(1, 5, 1), BridgeCommand.Stance(2, 6, 2) };
            return new WorldRecipe(41, MoverKind.InterimCrowd, units, script).WithCombat(pack.BuildCombat());
        }

        private const string DemoJson = "{ \"id\": \"" + DemoLayer + "\", \"extends\": \"" + Layer + "\", \"combat\": { \"resolver\": \"damage\" }, "
            + "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"fireIntervalMilli\": 1000, \"magazine\": 1000, \"effectRaw\": 0, \"spreadBase\": 2000, "
            + "\"spreadGrowth\": 0, \"spreadRecoverPerSec\": 0, \"spreadMax\": 2000 } ] }";

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
            var bits = new byte[(Width * Depth + 7) / 8];
            for (int i = 0; i < bits.Length; i++) bits[i] = 255;
            var map = new WcMap(DemoLayer, "none", default, new SimVec3(SimFix.FromInt(Width), SimFix.FromInt(6), SimFix.FromInt(Depth)),
                SimFix.FromInt(Width), default, Width, Depth, new[] { new WcMapLayer("walls", 1) }, System.Array.Empty<WcMapWall>(), bits);
            return WcMapWriter.Write(map);
        }
    }
}
