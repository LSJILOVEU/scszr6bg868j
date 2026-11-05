using System;
using System.Collections;
using System.Reflection;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    internal sealed class M6World
    {
        internal readonly SimSession Session;
        internal readonly Simulation Sim;
        internal readonly CombatSystem Combat;
        internal readonly CaptureSystem Capture;
        internal readonly ReinforceSystem Reinforce;
        internal readonly MatchSystem Match;
        internal readonly WorldRecipe Recipe;
        internal MatchMode Mode => (MatchMode)typeof(Simulation).GetField("mode", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Sim);
        internal M6World(WorldRecipe recipe, byte[] map = null, bool reverse = false)
        {
            Recipe = recipe;
            Session = (SimSession)typeof(WorldBuilder).GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { recipe, map ?? WcMapWriter.Write(M3TestWorld.Boxes(80, 40)), reverse });
            Sim = M3TestWorld.Sim(Session); Combat = System<CombatSystem>(); Capture = System<CaptureSystem>();
            Reinforce = System<ReinforceSystem>(); Match = System<MatchSystem>();
        }
        internal T System<T>() where T : class
        {
            foreach (object entry in (IEnumerable)typeof(Simulation).GetField("systems", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Sim))
            {
                object system = entry.GetType().GetField("System", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(entry);
                if (system is T value) return value;
            }
            return null;
        }
        internal EntityId Unit(ulong key) => Sim.Entities.Resolve(new EntityLink(StableEntityKey.Placed(1, key).Id));
        internal T Read<T>(ulong key) where T : unmanaged, IStateHash => Sim.Entities.Read(Sim.Entities.Resolve<T>(), Unit(key));
        internal bool Has<T>(ulong key) where T : unmanaged, IStateHash => Sim.Entities.TryGet(Sim.Entities.Resolve<T>(), Unit(key), out _);
        internal void Set<T>(ulong key, T value) where T : unmanaged, IStateHash => Sim.Entities.Set(Sim.Entities.Resolve<T>(), Unit(key), value);
        internal bool Dead(ulong key) => Combat.IsDead(Unit(key));
        internal void Step(int count = 1) { for (int i = 0; i < count; i++) Sim.Step(default); }
        internal ulong Hash => Sim.StateHashRetained();
        internal static SimVec3 P(int x, int z = 20) => SimVec3.Metres(x, 0, z);
        internal static UnitSpawn U(ulong key, int team, int x = 20, int z = 20, bool reserve = false, int weapon = -1, long speed = 1) =>
            new UnitSpawn(key, 0, team, x * 1000, z * 1000, speed, team, weapon, reserve);
        // A0: a world whose match must never end, for tests that keep stepping past what the real rules would call the end.
        internal static WorldRecipe Endless(WorldRecipe recipe) => recipe.WithRuleSet(MatchMode.TestNeverEndsId);
        internal static MatchPack Pack(ControlPointDef[] points = null, int num = 2, int den = 1, bool strict = false,
            int ticks = 3, InterruptMode mode = InterruptMode.Reset, int decay = 1, int interval = 90, int think = 1000000,
            int max = 9000, SpawnAnchor[] bases = null, int columns = 4, int rows = 4, SimFix? spacing = null, SimFix? sight = null) =>
            new MatchPack(points ?? new[] { new ControlPointDef(7, P(20), SimFix.FromInt(6)) },
                bases ?? new[] { new SpawnAnchor(1, P(6)), new SpawnAnchor(2, P(74)) },
                num, den, strict, ticks, mode, decay, interval, think, max, sight ?? RwrMatch.Sight, columns, rows, spacing ?? SimFix.Ratio(3, 2));
        internal static WorldRecipe RecipeOf(UnitSpawn[] units, MatchPack match = null, CombatPack combat = null, ulong seed = 41) =>
            new WorldRecipe(seed, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>()).WithMatch(match ?? Pack()).WithCombat(combat ?? RwrPack.CreateM6()).WithCrowdPass(CrowdPassKind.TeamIdle);
        internal static CombatPack Guns(IHitResolver resolver, ITargetSelector selector = null)
        {
            var original = RwrPack.CreateM6();
            return new CombatPack(original.Weapons.ToArray(), resolver, selector ?? original.Selector, original.MaxHealthRaw,
                original.FriendlyFire, original.FriendlyFireLocked, original.AllyCheckMissRaw, original.AllyHoldMilli);
        }
    }

    internal sealed class M6Damage : IRetainedGameplaySystem
    {
        private readonly M6World world;
        private readonly long tick;
        private readonly ulong[] keys;
        internal M6Damage(M6World world, long tick, params ulong[] keys) { this.world = world; this.tick = tick; this.keys = keys; }
        public int Order => 25;
        public string StateTypeId => "test.m6-damage";
        public int StateVersion => 1;
        public void Step(SimContext c)
        {
            if (c.Clock.Tick != tick) return;
            foreach (ulong key in keys)
            {
                StableId id = world.Sim.Entities.StableOf(world.Unit(key));
                c.Damage(world.Sim.GameplayTarget(id), new GameplayApplyKey(id, (ulong)tick, 0, id, (ulong)tick, 0), 1, world.Recipe.Combat.MaxHealthRaw);
            }
        }
        public void SaveState(SimStateWriter w) { w.Int64(tick); foreach (var key in keys) w.UInt64(key); }
        public void LoadState(SimStateReader r) { SimStateReader.Require(r.Int64() == tick, "tick"); foreach (var key in keys) SimStateReader.Require(r.UInt64() == key, "key"); }
        public void WriteState(StateHash h) { h.Add(tick); foreach (var key in keys) h.Add(key); }
    }
}
