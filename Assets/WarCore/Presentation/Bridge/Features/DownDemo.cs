using WarCore.Combat;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // DOWN-1 replay "down-crawl": a flat 30 x 16 m field with no walls and three team 2 units in a row along its middle line,
    // each its own squad (no enemy, so nobody fires). On tick 30 a scripted rwr.frag of the "rwr-down" layer goes off at A's feet
    // (x 10 m): A goes down (prone, crawling); B stands midway between the frag's inner and outer radius (4 m with the shipped
    // 2 and 6 m), so it is hurt and stunned for 60 ticks; C stands at inner + outer (8 m), out of reach. The spacing is read
    // from the layer's frag, so the row follows its radii. On ticks 31, 32 and 33 (a session takes one command per tick) A, B and
    // C are ordered 3 m north: C walks at once, B once it wakes, A crawls; A bleeds out on tick 1830. The field, the blast point,
    // the walk and the speed are this replay's own layout, defined only here. Shared by the replay tool and the tests.
    public static class DownDemo
    {
        public const string Layer = "rwr-down", Blast = "rwr.frag";
        public const long BlastTick = 30, MoveTick = 31, BlastXMm = 10000, BlastZMm = 8000, MoveMm = 3000, SpeedMmPerSec = 3000;
        public const int SquadA = 2, SquadB = 3, SquadC = 4, Team = 2;
        public const ulong KeyA = 1, KeyB = 2, KeyC = 3;
        private const int Width = 30, Depth = (int)(2 * BlastZMm / 1000);

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
            CombatPack combat = pack.BuildCombat();
            BlastSpec frag = combat.Blasts.Kinds[combat.Blasts.IndexOf(Blast)];
            long step = (frag.InnerMm + frag.OuterMm) / 2;
            int[] squads = { SquadA, SquadB, SquadC };
            var units = new UnitSpawn[3];
            var script = new BridgeCommand[4];
            script[0] = BridgeCommand.Blast(BlastTick, BlastXMm, BlastZMm, Blast);
            for (int i = 0; i < 3; i++)
            {
                long xMm = BlastXMm + i * step;
                units[i] = new UnitSpawn((ulong)i + 1, 0, squads[i], xMm, BlastZMm, SpeedMmPerSec, Team, weapon);
                script[i + 1] = BridgeCommand.MoveTo(MoveTick + i, squads[i], xMm, BlastZMm + MoveMm);
            }
            return new WorldRecipe(41, MoverKind.InterimCrowd, units, script).WithCombat(combat);
        }
    }
}
