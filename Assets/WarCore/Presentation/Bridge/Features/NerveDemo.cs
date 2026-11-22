using System;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // NERVE-1 replay "nerve-fire": a flat 50 x 40 m field without walls. Three shooters of team 1 stand at x 5.5 m (keys 1, 2, 3
    // at z 10.5, 20.5, 30.5 m), each 30 m in front of an unarmed target of team 2 (keys 4, 5, 6 at x 35.5 m). All carry the
    // shipped soldier weapon of the "rwr-nerve" layer; hits do no damage (see DemoLayer), so everybody lives and the shooters
    // keep firing. There is no match and no pressure system: a script is the nerve source. From PressTick on, shooter 2 is
    // pinned (x1.4) and shooter 3 broken (x1.8); shooter 1 stays calm. Nobody moves.
    public static class NerveDemo
    {
        public const string Layer = "rwr-nerve";
        public const long PressTick = 150;
        public const ulong Calm = 1, Pinned = 2, Broken = 3;
        private const int Width = 50, Depth = 40;

        public static WorldRecipe Recipe()
        {
            ContentPack pack = ContentLoader.Load(new DemoSource(), DemoLayer);
            int weapon = pack.WeaponOfUnitType(WarCore.Content.Rwr.RwrPack.SoldierTeam1);
            long speed1 = pack.SpeedOfUnitType(WarCore.Content.Rwr.RwrPack.SoldierTeam1), speed2 = pack.SpeedOfUnitType(WarCore.Content.Rwr.RwrPack.SoldierTeam2);
            var units = new UnitSpawn[6];
            for (int i = 0; i < 3; i++)
            {
                long z = 10500 + 10000 * i;
                units[i] = new UnitSpawn((ulong)i + 1, 0, 1, 5500, z, speed1, 1, weapon);
                units[i + 3] = new UnitSpawn((ulong)i + 4, 0, 2, 35500, z, speed2, 2, -1);
            }
            return new WorldRecipe(41, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>()).WithCombat(pack.BuildCombat())
                .With(Script((Pinned, PressTick, CombatSystem.NervePinned), (Broken, PressTick, CombatSystem.NerveBroken)));
        }

        // The demo's own layer, in memory: rwr-nerve with the "damage" resolver and weapons that do no damage.
        public const string DemoLayer = "nerve-fire";
        private const string DemoJson = "{ \"id\": \"" + DemoLayer + "\", \"extends\": \"" + Layer + "\", \"combat\": { \"resolver\": \"damage\" }, "
            + "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"effectRaw\": 0 }, { \"id\": \"rwr.weapon-2\", \"effectRaw\": 0 }, { \"id\": \"rwr.weapon-3\", \"effectRaw\": 0 } ] }";

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
            for (int k = 0; k < Width * Depth; k++) bits[k / 8] |= (byte)(1 << (k % 8));
            var map = new WcMap("nerve-fire", "none", default, new SimVec3(SimFix.FromInt(Width), SimFix.FromInt(6), SimFix.FromInt(Depth)),
                SimFix.FromInt(Width), default, Width, Depth, new[] { new WcMapLayer("walls", 1) }, Array.Empty<WcMapWall>(), bits);
            return WcMapWriter.Write(map);
        }

        // A world option that binds a scripted nerve source in Connect (before NerveFeature looks for the pressure system):
        // each entry gives a unit (by spawn key) a level from a tick on; every other unit, and every unit before its tick, is
        // calm. The level is a function of the unit and the simulation's tick only, so a restored world answers the same.
        internal static IWorldOption Script(params (ulong Key, long FromTick, int Level)[] plan) => new ScriptFeature(plan);

        private sealed class ScriptFeature : WorldStageFeature
        {
            private readonly (ulong Key, long FromTick, int Level)[] plan;
            internal ScriptFeature((ulong Key, long FromTick, int Level)[] plan) { this.plan = ((ulong, long, int)[])plan.Clone(); }

            public override string OptionId => "world.nerve-script";

            public override bool IsActive(WorldAssembly a) => a.Recipe.Combat?.Status != null && a.Recipe.Combat.Status.HasNerve;

            public override void Connect(WorldAssembly a)
            {
                var ids = new StableId[plan.Length];
                ReadOnlySpan<UnitSpawn> spawns = a.Recipe.Units;
                for (int p = 0; p < plan.Length; p++)
                {
                    int index = -1;
                    for (int i = 0; i < spawns.Length; i++) if (spawns[i].Key == plan[p].Key) index = i;
                    if (index < 0) throw new ArgumentException("The nerve script names unit " + plan[p].Key + ", which the recipe does not spawn.");
                    ids[p] = a.Sim.Entities.StableOf(a.Units[index]);
                }
                a.Combat.BindNerve(new Source(a.Sim, ids, plan));
            }
        }

        private sealed class Source : INerveSource
        {
            private readonly WarCore.Sim.Sim sim;
            private readonly StableId[] ids;
            private readonly (ulong Key, long FromTick, int Level)[] plan;
            internal Source(WarCore.Sim.Sim sim, StableId[] ids, (ulong Key, long FromTick, int Level)[] plan) { this.sim = sim; this.ids = ids; this.plan = plan; }

            public int NerveLevel(StableId unit)
            {
                for (int p = 0; p < plan.Length; p++)
                    if (ids[p].Equals(unit) && sim.Clock.Tick >= plan[p].FromTick) return plan[p].Level;
                return CombatSystem.NerveCalm;
            }
        }
    }
}
