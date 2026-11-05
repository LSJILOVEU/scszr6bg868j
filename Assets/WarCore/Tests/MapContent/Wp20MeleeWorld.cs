using System;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // WP-20 fixture: a plain combat world (no match rules, so nothing ends it) with melee specs, built by the real WorldBuilder.
    internal sealed class MeleeWorld
    {
        internal readonly M6World W;
        internal Action<SimContext> Intent;

        internal MeleeWorld(CombatPack pack, UnitSpawn[] units, byte[] map = null, bool reverse = false, ulong? player = null, WarCore.Battle.MatchPack match = null)
        {
            var recipe = new WorldRecipe(41, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>()).WithCombat(pack);
            // Player control needs match rules; the never-ending rule set keeps the world running while the units stand on a point.
            if (match != null) recipe = recipe.WithMatch(match).WithRuleSet(WarCore.Battle.MatchMode.TestNeverEndsId);
            if (player.HasValue) recipe = recipe.WithPlayer(player.Value);
            W = new M6World(recipe, map, reverse);
            W.Sim.RegisterGameplay("test.melee-intent", new IntentScript(this), SimSystemPhase.Queries);
        }

        internal static MeleeSpec Spec(int windup = 6, int active = 2, int recovery = 9, double radius = 2, int cos = 500, double halfHeight = 1,
            int damage = 10, int maxTargets = 3, double engage = 1.8) =>
            new MeleeSpec(windup, active, recovery, SimFix.Ratio((long)(radius * 1000), 1000), cos, SimFix.Ratio((long)(halfHeight * 1000), 1000),
                SimFix.FromInt(damage), maxTargets, SimFix.Ratio((long)(engage * 1000), 1000));

        internal static CombatPack Pack(MeleeSpec[] melee, WeaponDef[] guns = null, bool friendlyFire = false, IHitResolver resolver = null) =>
            new CombatPack(guns ?? Array.Empty<WeaponDef>(), resolver ?? new ZeroResolver(), new NearestVisibleEnemy(), 100 * SimFix.Scale,
                friendlyFire, false, 0, 30000, melee);

        // x and z in millimetres. melee = index into the pack's specs; weapon = gun index or -1.
        internal static UnitSpawn Unit(ulong key, int team, long xMm, long zMm, int melee = -1, int weapon = -1) =>
            new UnitSpawn(key, 0, team, xMm, zMm, 3000, team, weapon, false, melee);

        internal ulong Id(ulong key) => W.Sim.Entities.StableOf(W.Unit(key)).Value;
        internal long Health(ulong key) => W.Sim.GameplayResource(W.Sim.Entities.StableOf(W.Unit(key)), 1);
        internal static long Full => 100 * SimFix.Scale;
        internal static long Cost(int damage) => damage * (long)SimFix.Scale;
        internal long Tick => W.Sim.Clock.Tick;
        internal void Step(int count = 1) { for (int i = 0; i < count; i++) W.Step(); }
        internal void Step(InputFrame input) => W.Sim.Step(input);
        internal int Count(StrikeKind kind)
        {
            int n = 0;
            foreach (var s in W.Combat.Strikes) if (s.Kind == kind) n++;
            return n;
        }

        // Spends the command capacity of one tick before the melee code runs (order 26, combat runs later).
        internal void FillCommandsAt(long tick, int count, ulong victimKey) =>
            W.Sim.RegisterGameplay("test.melee-filler", new Filler(W, tick, count, victimKey), SimSystemPhase.Queries);

        private sealed class Filler : IRetainedGameplaySystem
        {
            private readonly M6World world;
            private readonly long tick;
            private readonly int count;
            private readonly ulong victim;
            internal Filler(M6World world, long tick, int count, ulong victim) { this.world = world; this.tick = tick; this.count = count; this.victim = victim; }
            public int Order => 26;
            public string StateTypeId => "test.melee-filler";
            public int StateVersion => 1;
            public void Step(SimContext c)
            {
                if (c.Clock.Tick != tick) return;
                StableId id = world.Sim.Entities.StableOf(world.Unit(victim));
                for (int i = 0; i < count; i++)
                    c.Damage(world.Sim.GameplayTarget(id), new GameplayApplyKey(id, (ulong)tick, 0, id, (ulong)tick, 9, (uint)i), 1, 1);
            }
            public void SaveState(SimStateWriter writer) { }
            public void LoadState(SimStateReader reader) { }
            public void WriteState(StateHash hash) { hash.Add(StateTypeId); }
        }

        private sealed class IntentScript : IRetainedGameplaySystem
        {
            private readonly MeleeWorld owner;
            internal IntentScript(MeleeWorld owner) { this.owner = owner; }
            public int Order => 28;
            public string StateTypeId => "test.melee-intent";
            public int StateVersion => 1;
            public void Step(SimContext c) => owner.Intent?.Invoke(c);
            public void SaveState(SimStateWriter writer) { }
            public void LoadState(SimStateReader reader) { }
            public void WriteState(StateHash hash) { hash.Add(StateTypeId); }
        }
    }
}
