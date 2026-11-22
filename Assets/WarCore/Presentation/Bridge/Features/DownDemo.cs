using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // DOWN-1 replay "down-crawl": a flat 30 x 16 m field with no walls and three team 2 units in a row along z 8 m, each its own
    // squad: A at x 10 m, B at x 14 m, C at x 18 m (no enemy, so nobody fires). On tick 30 a scripted rwr.frag of the "rwr-down"
    // layer goes off at A's feet: A goes down (prone, crawling), B, 4 m away, is hurt and stunned for 60 ticks, C, 8 m away, is
    // out of reach. On ticks 31, 32 and 33 (a session takes one command per tick) A, B and C are ordered 3 m north: C walks at
    // once, B once it wakes, A crawls; A bleeds out on tick 1830. Shared by the replay tool and the tests.
    public static class DownDemo
    {
        public const string Layer = "rwr-down", Blast = "rwr.frag";
        public const long BlastTick = 30, MoveTick = 31, BlastXMm = 10000, BlastZMm = 8000, MoveMm = 3000, SpeedMmPerSec = 3000;
        public const int SquadA = 2, SquadB = 3, SquadC = 4, Team = 2;
        public const ulong KeyA = 1, KeyB = 2, KeyC = 3;
        public static readonly long[] XMm = { 10000, 14000, 18000 };
        private const int Width = 30, Depth = 16;

        public static WcMap Map()
        {
            var bits = new byte[(Width * Depth + 7) / 8];
            for (int k = 0; k < Width * Depth; k++) bits[k >> 3] |= (byte)(1 << (k & 7));
            return new WcMap("down-crawl", "none", default, SimVec3.Metres(Width, 4, Depth), SimFix.FromInt(Width),
                default, Width, Depth, new[] { new WcMapLayer("walls", 1) }, new WcMapWall[0], bits);
        }

        public static byte[] MapBytes() => WcMapWriter.Write(Map());

        public static WorldRecipe Recipe() => Recipe(WarCore.Content.Rwr.RwrPack.Load(Layer));

        public static WorldRecipe Recipe(ContentPack pack)
        {
            int weapon = pack.WeaponOfUnitType(WarCore.Content.Rwr.RwrPack.SoldierTeam2);
            int[] squads = { SquadA, SquadB, SquadC };
            var units = new UnitSpawn[3];
            var script = new BridgeCommand[4];
            script[0] = BridgeCommand.Blast(BlastTick, BlastXMm, BlastZMm, Blast);
            for (int i = 0; i < 3; i++)
            {
                units[i] = new UnitSpawn((ulong)i + 1, 0, squads[i], XMm[i], BlastZMm, SpeedMmPerSec, Team, weapon);
                script[i + 1] = BridgeCommand.MoveTo(MoveTick + i, squads[i], XMm[i], BlastZMm + MoveMm);
            }
            return new WorldRecipe(41, MoverKind.InterimCrowd, units, script).WithCombat(pack.BuildCombat());
        }
    }
}
