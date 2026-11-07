using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Control;
using WarCore.Presentation;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // Respawn fixtures: every unit, point, base and match value is written here, nothing comes from a shipped default.
    // Flat 80 x 40 m map, no walls; the AI never thinks, nobody carries a weapon unless a test says so, and hits deal no damage.
    internal static class RespawnWorld
    {
        internal const ulong Seed = 41;
        internal const ulong Player = 1, Ally = 2, Enemy = 10;

        internal static SimVec3 P(int x, int z) => SimVec3.Metres(x, 0, z);

        internal static UnitSpawn U(ulong key, int x, int z, int team = 1, bool reserve = false, int weapon = -1) =>
            new UnitSpawn(key, 0, team, x * 1000L, z * 1000L, 1, team, weapon, reserve);

        // The player at (20, 20), a deployed ally far from every anchor and a living enemy: no side runs out of living units.
        internal static List<UnitSpawn> Units(params UnitSpawn[] extra)
        {
            var units = new List<UnitSpawn> { U(Player, 20, 20), U(Ally, 20, 38), U(Enemy, 78, 38, team: 2) };
            units.AddRange(extra);
            return units;
        }

        internal static ControlPointDef Spawn(int id, int x, int z, int owner = 1) =>
            new ControlPointDef(id, P(x, z), SimFix.FromInt(2), owner, true, P(x, z));
        // An enemy point and a neutral point with no spawn, far from everything: no team ever owns all points.
        internal static ControlPointDef[] Points(params ControlPointDef[] own)
        {
            var points = new List<ControlPointDef>(own) { new ControlPointDef(90, P(76, 4), SimFix.FromInt(2), 2), new ControlPointDef(91, P(60, 4), SimFix.FromInt(2)) };
            return points.ToArray();
        }
        internal static SpawnAnchor[] Bases(params SpawnAnchor[] own)
        {
            var bases = new List<SpawnAnchor>(own) { new SpawnAnchor(2, P(78, 20)) };
            return bases.ToArray();
        }

        // Capture takes 8000 ticks and the AI thinks every 1000000, so neither acts inside any test window.
        internal static MatchPack Pack(ControlPointDef[] points, SpawnAnchor[] bases, int respawn, int columns = 2, int rows = 2,
            int interval = 1000000, int max = 100000) =>
            new MatchPack(points, bases, 2, 1, false, 8000, InterruptMode.Reset, 1, interval, 1000000, max, SimFix.FromInt(35),
                columns, rows, SimFix.FromInt(2), respawn);

        internal static WorldRecipe Recipe(IEnumerable<UnitSpawn> units, MatchPack pack, bool player = true)
        {
            var recipe = new WorldRecipe(Seed, MoverKind.InterimCrowd, new List<UnitSpawn>(units).ToArray(), System.Array.Empty<BridgeCommand>())
                .WithMatch(pack).WithCombat(M5TestWorld.Pack(new ZeroResolver())).WithCrowdPass(CrowdPassKind.TeamIdle);
            return player ? recipe.WithPlayer(Player) : recipe;
        }

        internal static M6World World(IEnumerable<UnitSpawn> units, MatchPack pack, bool reverse = false, long[] kills = null)
        {
            var w = new M6World(Recipe(units, pack), reverse: reverse);
            if (kills != null) w.Sim.RegisterGameplay("test.respawn-kills", new KillControlled(w, kills), SimSystemPhase.Queries);
            return w;
        }

        internal static List<string> SystemIds(M6World w)
        {
            var ids = new List<string>();
            foreach (object entry in (IEnumerable)typeof(Simulation).GetField("systems", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(w.Sim))
                ids.Add((string)entry.GetType().GetField("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(entry));
            return ids;
        }

        internal static long Entered(M6World w, ulong key) => w.Sim.GameplayLife(w.Sim.Entities.StableOf(w.Unit(key))).EnteredTick;
        internal static ulong Controlled(M6World w) => w.Session.ControlledKey.Value;
    }

    // Kills whatever unit is controlled at each listed tick (Queries, order 26, so it can sit beside M6Damage at 25). One instance per world.
    internal sealed class KillControlled : IRetainedGameplaySystem
    {
        private readonly M6World world;
        private readonly long[] ticks;
        internal KillControlled(M6World world, long[] ticks) { this.world = world; this.ticks = ticks; }
        public int Order => 26;
        public string StateTypeId => "test.respawn-kills";
        public int StateVersion => 1;
        public void Step(SimContext c)
        {
            if (System.Array.IndexOf(ticks, c.Clock.Tick) < 0) return;
            StableId id = world.Sim.Entities.StableOf(world.System<PlayerControl>().Current);
            c.Damage(world.Sim.GameplayTarget(id), new GameplayApplyKey(id, (ulong)c.Clock.Tick, 0, id, (ulong)c.Clock.Tick, 0), 1, world.Recipe.Combat.MaxHealthRaw);
        }
        public void SaveState(SimStateWriter w) { w.Int32(ticks.Length); foreach (long tick in ticks) w.Int64(tick); }
        public void LoadState(SimStateReader r)
        {
            SimStateReader.Require(r.Int32() == ticks.Length, "ticks");
            foreach (long tick in ticks) SimStateReader.Require(r.Int64() == tick, "tick");
        }
        public void WriteState(StateHash h) { h.Add(ticks.Length); foreach (long tick in ticks) h.Add(tick); }
    }

    // Sets one unit's position at one tick (Requests, order 19: after respawn at 4, where deployment writes positions too). One instance per world.
    internal sealed class MoveAt : IRetainedGameplaySystem
    {
        private readonly M6World world;
        private readonly long tick;
        private readonly ulong key;
        private readonly SimVec3 to;
        internal MoveAt(M6World world, long tick, ulong key, SimVec3 to) { this.world = world; this.tick = tick; this.key = key; this.to = to; }
        public int Order => 19;
        public string StateTypeId => "test.respawn-move";
        public int StateVersion => 1;
        public void Step(SimContext c)
        {
            if (c.Clock.Tick == tick) world.Sim.Entities.Set(world.Sim.Entities.Resolve<Position>(), world.Unit(key), new Position(to));
        }
        public void SaveState(SimStateWriter w) { w.Int64(tick); w.UInt64(key); }
        public void LoadState(SimStateReader r) { SimStateReader.Require(r.Int64() == tick && r.UInt64() == key, "move"); }
        public void WriteState(StateHash h) { h.Add(tick); h.Add(key); to.WriteState(h); }
    }
}
