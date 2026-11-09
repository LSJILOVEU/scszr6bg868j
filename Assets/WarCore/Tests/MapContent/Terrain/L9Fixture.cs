using System;
using System.Collections.Generic;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // L9 fixtures: small height-field worlds with no match and no AI orders, so units stand still and only automatic fire
    // (or the aimed-intent extension below) shoots. Everything that shapes behaviour is written here: seed 41, the maps, the
    // unit tables, the content layer (a literal, no "extends"), the combat pack and the weapon.
    internal static class L9
    {
        internal const long S = SimFix.Scale;
        internal static readonly string[] Names = { "dirt", "grass" };

        // Used only through WithTerrainRules (surfaces and slope rule); the combat pack does not come from content.
        internal const string PackJson = "{ \"id\": \"l9-test\", "
            + "\"combat\": { \"maxHealth\": 100, \"resolver\": \"rwr.hit\", \"selector\": \"nearest-visible\", \"friendlyFire\": false, \"friendlyFireLocked\": false, \"allyCheckMissRate\": 0.25, \"allyHoldMilli\": 30000 }, "
            + "\"surfaces\": [ { \"id\": \"dirt\", \"passable\": true, \"speedMilli\": 1000, \"cost\": 1, \"concealMilli\": 1000 }, { \"id\": \"grass\", \"passable\": true, \"speedMilli\": 800, \"cost\": 1, \"concealMilli\": 1000 } ], "
            + "\"slopeRule\": { \"maxWalkableNum\": 2, \"maxWalkableDen\": 1 }, "
            + "\"ai\": { \"failedSlotTicks\": 0, \"targetMemoryTicks\": 0, \"searchTicks\": 0, \"reactionTicks\": 0, \"senseTicks\": 1 } }";
        internal static ContentPack Pack() =>
            ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string> { ["l9-test"] = PackJson }), "l9-test");

        // One weapon: 30 ticks between shots, 30 rounds, falloff 10..30 m, effect 6554; spread in thousandths of a degree.
        internal static WeaponDef Rifle(int spread) => new WeaponDef(30000, 30, 60000, SimFix.FromInt(10), SimFix.FromInt(30), 6554,
            spread == 0 ? default : new SpreadSpec(spread, 0, 0, spread));
        // ZeroResolver by default: no damage, so nobody dies and the shot count only depends on the fire interval.
        internal static CombatPack Combat(int spread, IHitResolver resolver = null) =>
            new CombatPack(new[] { Rifle(spread) }, resolver ?? new ZeroResolver(), new NearestVisibleEnemy(), 100 * S);

        internal static UnitSpawn Unit(ulong key, int team, long xMm, long zMm, int weapon) =>
            new UnitSpawn(key, 0, team, xMm, zMm, 3000, team, weapon, false);

        internal static readonly (int, int, int, int)[] NoWalls = Array.Empty<(int, int, int, int)>();

        // The ridge: grid points 6..18 rise to 3 m at x = 12 (slope 0.5). The flat twin has the same size and an all-zero field.
        internal static int Ridge(int i, int j) => i >= 6 && i <= 18 ? (int)((6 - Abs(i - 12)) * S / 2) : 0;
        internal static byte[] RidgeMap(bool ridge, (int, int, int, int)[] walls = null) =>
            WcMapWriter.Write(Wp23.Make(24, 8, 0, 6 * S, walls ?? NoWalls, 2 * S, ridge ? (Func<int, int, int>)Ridge : (i, j) => 0, Names, (x, z) => 0));

        // Acceptance 3(d): one 2 m point column at i = 4 on an 8 x 4 field.
        internal static byte[] ObliqueMap() =>
            WcMapWriter.Write(Wp23.Make(8, 4, 0, 6 * S, NoWalls, 2 * S, (i, j) => i == 4 ? (int)(2 * S) : 0, Names, (x, z) => 0));

        // Acceptance 3(e): a bump h(10) = 0.625 m, h(11) = -1 m (slopes 0.625 and 1.625); the flat twin is all zero.
        internal static byte[] BumpMap(bool bump) =>
            WcMapWriter.Write(Wp23.Make(24, 8, -2 * S, 6 * S, NoWalls, 2 * S,
                (i, j) => !bump ? 0 : i == 10 ? 40960 : i == 11 ? -65536 : 0, Names, (x, z) => 0));

        // Acceptance 10: a 26 x 4 ramp, h(i, j) = i * 59419 raw (slope 0.906662).
        internal static byte[] RampMap() =>
            WcMapWriter.Write(Wp23.Make(26, 4, 0, 30 * S, NoWalls, 2 * S, (i, j) => i * 59419, Names, (x, z) => 0));

        // Acceptance 6(a): an all-zero field and a 2 m wall (cell x 12, z 6) beside the line from unit 2 to unit 4 (z = 5.5), so
        // the eye line stays clear and only shots turned by spread run into it. (A wall on a height field must be at least 1 m
        // above its highest vertex, WcMapValidation.cs:102, so a low wall under the eye line is not possible.)
        internal static readonly (int, int, int, int)[] DuelWalls = { (12, 6, 1, 1) };
        internal static byte[] DuelMap() =>
            WcMapWriter.Write(Wp23.Make(24, 8, 0, 6 * S, DuelWalls, 2 * S, (i, j) => 0, Names, (x, z) => 0));
        internal static UnitSpawn[] DuelUnits() => new[]
        {
            Unit(1, 1, 3500, 2500, 0), Unit(2, 1, 3500, 5500, 0), Unit(3, 2, 13500, 2500, 0), Unit(4, 2, 13500, 5500, 0),
        };

        // Acceptance 6(b): a 3 m plateau over grid points 4..20 with 1.5 m ramp points at 2, 3, 21 and 22 (appendix A), and one
        // ground wall in the corner, away from the plateau. Both teams stand on the plateau (x in 6..18), so every eye line stays above it.
        internal static int Plateau(int i, int j) =>
            i >= 4 && i <= 20 ? (int)(3 * S) : i == 2 || i == 3 || i == 21 || i == 22 ? (int)(3 * S / 2) : 0;
        internal static readonly (int, int, int, int)[] PlateauWalls = { (0, 0, 1, 1) };
        internal static byte[] PlateauMap() =>
            WcMapWriter.Write(Wp23.Make(24, 8, 0, 6 * S, PlateauWalls, 2 * S, Plateau, Names, (x, z) => 0));
        internal static UnitSpawn[] PlateauUnits() => new[]
        {
            Unit(1, 1, 7500, 2500, 0), Unit(2, 1, 7500, 5500, 0), Unit(3, 2, 16500, 2500, 0), Unit(4, 2, 16500, 5500, 0),
        };

        internal static M6World World(byte[] map, UnitSpawn[] units, int spread = 0, IWorldOption extra = null, bool reverse = false,
            IHitResolver resolver = null)
        {
            WorldRecipe r = new WorldRecipe(41, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>()).WithCombat(Combat(spread, resolver))
                .WithCrowdPass(CrowdPassKind.TeamIdle).WithTerrainRules(Pack());
            if (extra != null) r = r.With(extra);
            return new M6World(r, map, reverse);
        }

        internal static SimVec3 Mm(long xMm, long yRaw, long zMm) =>
            new SimVec3(SimFix.FromRaw(xMm * S / 1000), SimFix.FromRaw(yRaw), SimFix.FromRaw(zMm * S / 1000));
        internal static SimVec3 Raw(long x, long y, long z) => new SimVec3(SimFix.FromRaw(x), SimFix.FromRaw(y), SimFix.FromRaw(z));

        // Steps ticks 1..last; per tick folds the state hash (FNV-1a over 64-bit values) and logs every fire with its tick.
        internal static ulong Run(M6World w, int last, List<(int, FireRecord)> log)
        {
            ulong fold = 14695981039346656037UL;
            fold = (fold ^ w.Hash) * 1099511628211UL;
            for (int tick = 1; tick <= last; tick++)
            {
                w.Step();
                foreach (FireRecord f in w.Combat.Fires) log.Add((tick, f));
                fold = (fold ^ w.Hash) * 1099511628211UL;
            }
            return fold;
        }

        // FNV-1a over every field of every fire record that the acceptance names, in order.
        internal static ulong FoldFires(List<(int, FireRecord)> log)
        {
            ulong fold = 14695981039346656037UL;
            void Add(ulong v) { fold = (fold ^ v) * 1099511628211UL; }
            foreach (var (tick, f) in log)
            {
                Add((ulong)tick); Add(f.Shooter.Value); Add(f.Target.Value);
                Add((ulong)f.Origin.X.Raw); Add((ulong)f.Origin.Y.Raw); Add((ulong)f.Origin.Z.Raw);
                Add((ulong)f.End.X.Raw); Add((ulong)f.End.Y.Raw); Add((ulong)f.End.Z.Raw);
                Add((ulong)f.Distance.Raw); Add((ulong)f.Outcome);
            }
            return fold;
        }

        private static long Abs(long v) => v < 0 ? -v : v;

        // A test extension: a Requests system (Order 25) that submits an aimed intent for unit `key` on ticks from .. to.
        internal sealed class Aim : WorldFeature
        {
            private readonly ulong key; private readonly long from, to; private readonly SimVec3 point;
            internal Aim(ulong key, long from, long to, SimVec3 point) { this.key = key; this.from = from; this.to = to; this.point = point; }
            public override string OptionId => "test.l9-aim";
            public override void RegisterSystems(WorldAssembly a) =>
                a.Sim.RegisterGameplay("test.l9-aim", new Submitter(this, a.Sim, a.Combat), SimSystemPhase.Requests);
            private sealed class Submitter : IRetainedGameplaySystem
            {
                private readonly Aim owner; private readonly Simulation sim; private readonly CombatSystem combat;
                internal Submitter(Aim owner, Simulation sim, CombatSystem combat) { this.owner = owner; this.sim = sim; this.combat = combat; }
                public int Order => 25;
                public string StateTypeId => "test.l9-aim";
                public int StateVersion => 1;
                public void Step(SimContext c)
                {
                    long tick = c.Clock.Tick;
                    if (tick < owner.from || tick > owner.to) return;
                    StableId a = sim.Entities.StableOf(sim.Entities.Resolve(new EntityLink(StableEntityKey.Placed(1, owner.key).Id)));
                    combat.Submit(c, FireIntent.Aimed(a, owner.point));
                }
                public void SaveState(SimStateWriter writer) { }
                public void LoadState(SimStateReader reader) { }
                public void WriteState(StateHash hash) { hash.Add(StateTypeId); hash.Add(StateVersion); }
            }
        }
    }
}
