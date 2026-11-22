using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Control;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // RESPAWN-AI-2 fixture: flat 80 x 40 m map, two teams. Team 1: three on the field (keys 1, 2, 3) and four reserves
    // (11..14), base B1 at (6, 20). Team 2: three on the field (4, 5, 6) and four reserves (21..24), base B2 at (74, 20). Point
    // P (id 5, index 0) at (40, 20), radius 4 m, spawn on its centre, owned by team 1; a neutral point with no spawn far away.
    // A 2 x 2 grid 2 m apart. Capture takes 8000 ticks and the AI thinks every 1000000, so nobody moves and P keeps its owner
    // inside every test window. Nobody carries a weapon; hits deal no damage. With a player, key 1 is the player.
    // Team 1's entry list is [B1, P]. Only the pre-RESPAWN-AI-2 API is used here, so the lock can be recorded on the old code.
    internal static class RespawnAi2World
    {
        internal const int Interval = 30;
        internal const ulong Player = RespawnWorld.Player;
        internal static readonly SimVec3 B1 = SimVec3.Metres(6, 0, 20), B2 = SimVec3.Metres(74, 0, 20), PCentre = SimVec3.Metres(40, 0, 20);

        internal static UnitSpawn U(ulong key, long xMm, long zMm, int team, bool reserve = false) =>
            new UnitSpawn(key, 0, team, xMm, zMm, 1, team, -1, reserve);

        // reserves: how many reserves each team has (keys 11.. and 21..); extra: units added to the base set.
        internal static List<UnitSpawn> Units(int reserves = 4, params UnitSpawn[] extra)
        {
            var units = new List<UnitSpawn>
            {
                U(1, 10000, 36000, 1), U(2, 14000, 36000, 1), U(3, 18000, 36000, 1),
                U(4, 70000, 36000, 2), U(5, 66000, 36000, 2), U(6, 62000, 36000, 2),
            };
            for (int i = 0; i < reserves; i++) units.Add(U(11 + (ulong)i, 2000, 2000, 1, reserve: true));
            for (int i = 0; i < reserves; i++) units.Add(U(21 + (ulong)i, 78000, 2000, 2, reserve: true));
            units.AddRange(extra);
            return units;
        }

        // Four units of a team on the four grid cells round an anchor: nobody can deploy there.
        internal static UnitSpawn[] Blockers(ulong firstKey, SimVec3 anchor, int team = 1)
        {
            long x = anchor.X.Raw * 1000 / SimFix.Scale, z = anchor.Z.Raw * 1000 / SimFix.Scale;
            return new[] { U(firstKey, x - 1000, z - 1000, team), U(firstKey + 1, x + 1000, z - 1000, team), U(firstKey + 2, x - 1000, z + 1000, team), U(firstKey + 3, x + 1000, z + 1000, team) };
        }

        internal static ControlPointDef P(int owner = 1) => new ControlPointDef(5, PCentre, SimFix.FromInt(4), owner, true, PCentre);
        internal static ControlPointDef Far() => new ControlPointDef(9, SimVec3.Metres(60, 0, 4), SimFix.FromInt(2));
        internal static SpawnAnchor[] Bases(bool teamOneBase = true) => teamOneBase
            ? new[] { new SpawnAnchor(1, B1), new SpawnAnchor(2, B2) }
            : new[] { new SpawnAnchor(2, B2) };

        // The pack through the constructor the old code has (no RESPAWN-AI-2 field).
        internal static MatchPack OldPack(int respawn = 0) =>
            new MatchPack(new[] { P(), Far() }, Bases(), 2, 1, false, 8000, InterruptMode.Reset, 1, Interval, 1000000, 100000,
                SimFix.FromInt(35), 2, 2, SimFix.FromInt(2), respawn);

        internal static M6World World(IEnumerable<UnitSpawn> units, MatchPack pack, bool player = false)
        {
            M6World w = null;
            Assert.DoesNotThrow(() => w = new M6World(M6World.Endless(RespawnWorld.Recipe(units, pack, player))));
            return w;
        }

        // Kills the listed units at one tick (the death is entered at that tick).
        internal static void Kill(M6World w, long tick, params ulong[] keys) => Kills(w, Array.ConvertAll(keys, key => (tick, key)));
        // Several deaths, each (tick, key); once per world.
        internal static void Kills(M6World w, params (long tick, ulong key)[] kills) =>
            w.Sim.RegisterGameplay("test.kill-schedule", new KillSchedule(w, kills), SimSystemPhase.Queries);
        // At each listed tick kills the first unit of the team (in recipe order) that stands on the field alive; once per world.
        internal static void KillFielded(M6World w, int team, params long[] ticks) =>
            w.Sim.RegisterGameplay("test.kill-fielded", new KillFieldedUnit(w, team, ticks), SimSystemPhase.Queries);
        internal static void KillPlayer(M6World w, params long[] ticks) =>
            w.Sim.RegisterGameplay("test.respawn-kills", new KillControlled(w, ticks), SimSystemPhase.Queries);
        internal static void Move(M6World w, long tick, ulong key, SimVec3 to) =>
            w.Sim.RegisterGameplay("test.move", new MoveAt(w, tick, key, to), SimSystemPhase.Requests);

        // Units of a team that stand on the field and are not dead; units of a team that are alive and wait in reserve.
        internal static int Fielded(M6World w, int team)
        {
            int count = 0;
            foreach (UnitSpawn spawn in w.Recipe.Units)
                if (spawn.Team == team && w.Has<Position>(spawn.Key) && !w.Dead(spawn.Key)) count++;
            return count;
        }
        internal static int Reserves(M6World w, int team)
        {
            int count = 0;
            foreach (UnitSpawn spawn in w.Recipe.Units)
                if (spawn.Team == team && !w.Has<Position>(spawn.Key) && !w.Dead(spawn.Key)) count++;
            return count;
        }
        internal static int DeadOf(M6World w, int team)
        {
            int count = 0;
            foreach (UnitSpawn spawn in w.Recipe.Units) if (spawn.Team == team && w.Dead(spawn.Key)) count++;
            return count;
        }

        internal static ulong KeyOf(M6World w, StableId unit) => RespawnAi1World.KeyOf(w, unit);
        internal static bool Near(SimVec3 at, SimVec3 anchor) => RespawnAi1World.Near(at, anchor);
        internal static byte[] Saved(ReinforceSystem system) => RespawnAi1World.Saved(system);
        internal static ulong HashOf(ReinforceSystem system) => RespawnAi1World.HashOf(system);
        internal static ulong Run(M6World w, int ticks) => RespawnAi1World.Run(w, ticks);

        // ---- Map W and its content layer (a map file start) ----
        // Map W: 96 x 8 m, flat. Team 1: three on the field (keys 1, 2, 3) round x = 4..8 m; team 2: three (4, 5, 6) round
        // x = 88..92 m; no reserve in the map, so the highest key M is 6. A base each (1.5 and 94.5 m); one point P (id 1) in
        // the middle with a spawn, neutral. Every soldier is a "w.line" (no weapon, no melee).
        internal const ulong HighestKey = 6;
        internal const string PointW = "{ \"id\": 1, \"at\": [48500, 4500], \"radiusMm\": 3000, \"initialOwner\": 0, \"spawn\": [48500, 4500] }";
        internal const string Base1W = "{ \"id\": 1, \"team\": 1, \"at\": [1500, 4500] }", Base2W = "{ \"id\": 2, \"team\": 2, \"at\": [94500, 4500] }";
        internal static string SoldiersW(bool teamOne = true, bool teamTwo = true)
        {
            var units = new List<string>();
            if (teamOne) for (ulong k = 1; k <= 3; k++) units.Add(E1bSamples.Unit(k, "w.line", 1, 1, 2500 + 2000 * (long)k, 6500, false));
            if (teamTwo) for (ulong k = 4; k <= 6; k++) units.Add(E1bSamples.Unit(k, "w.line", 2, 2, 85500 + 2000 * (long)(k - 3), 6500, false));
            return string.Join(", ", units);
        }
        internal const string PackId = "respawn-ai-2";
        // nextUnit: the description's next unit key, above every unit key in it.
        internal static MapBakeResult MapW(string points = PointW, string bases = Base1W + ", " + Base2W, string soldiers = null, ulong nextUnit = 7) =>
            E1aSamples.Bake(E1bSamples.Desc(PackId, null, null, points, bases, soldiers ?? SoldiersW(), 41, "teamIdle", 2, 3, nextUnit));

        // The layer of map W: rwr-melee with two unit types of its own and quiet match rules (the AI never thinks, capture
        // takes 8000 ticks, a 2 x 2 grid 2 m apart, reinforcement every 30 ticks). "w.reservist" differs from "w.line" in
        // every field a spawn carries. match: more fields of the match section, written after the quiet ones; top: more
        // top-level sections.
        internal static string LayerW(string match = "", string top = "") =>
            "{ \"id\": \"" + PackId + "\", \"extends\": \"rwr-melee\", "
            + "\"block\": [ { \"id\": \"shield\", \"cosHalfAnglePermille\": 500, \"reductionPermille\": 750, \"aiRaisePermille\": 1000 } ], "
            + "\"unitTypes\": [ { \"id\": \"w.line\", \"weapon\": \"rwr.weapon-1\", \"speed\": 3000 }, "
            + "{ \"id\": \"w.reservist\", \"weapon\": \"rwr.weapon-2\", \"melee\": \"rwr.sword-1\", \"speed\": 4200, \"archetype\": 7, \"block\": \"shield\", "
            + "\"dashDistance\": 3000, \"dashTicks\": 6, \"dashCooldown\": 60 } ], "
            + "\"match\": { \"captureTicks\": 8000, \"thinkInterval\": 1000000, \"maxTicks\": 100000, \"intervalTicks\": " + Interval
            + ", \"gridColumns\": 2, \"gridRows\": 2, \"gridSpacing\": 2" + (match.Length == 0 ? "" : ", " + match) + " }"
            + (top.Length == 0 ? "" : ", " + top) + " }";
        internal static ContentPack LoadW(string match = "", string top = "") =>
            ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string> { [PackId] = LayerW(match, top) }, FileContentSource.Rwr()), PackId);

        internal static M6World WorldW(MapBakeResult map, ContentPack pack, bool useUnitTypes = false)
        {
            M6World w = null;
            Assert.DoesNotThrow(() => w = new M6World(MapRecipe.From(map, pack, useUnitTypes), map.WcMapBytes));
            return w;
        }
    }

    // Kills the listed units at their ticks (Queries, order 27: beside M6Damage at 25 and KillControlled at 26). One instance per world.
    internal sealed class KillSchedule : IRetainedGameplaySystem
    {
        private readonly M6World world;
        private readonly (long tick, ulong key)[] kills;
        internal KillSchedule(M6World world, (long tick, ulong key)[] kills) { this.world = world; this.kills = kills; }
        public int Order => 27;
        public string StateTypeId => "test.kill-schedule";
        public int StateVersion => 1;
        public void Step(SimContext c)
        {
            foreach ((long tick, ulong key) in kills)
            {
                if (c.Clock.Tick != tick) continue;
                StableId id = world.Sim.Entities.StableOf(world.Unit(key));
                c.Damage(world.Sim.GameplayTarget(id), new GameplayApplyKey(id, (ulong)tick, 0, id, (ulong)tick, 0), 1, world.Recipe.Combat.MaxHealthRaw);
            }
        }
        public void SaveState(SimStateWriter w) { w.Int32(kills.Length); foreach (var kill in kills) { w.Int64(kill.tick); w.UInt64(kill.key); } }
        public void LoadState(SimStateReader r)
        {
            SimStateReader.Require(r.Int32() == kills.Length, "kills");
            foreach (var kill in kills) SimStateReader.Require(r.Int64() == kill.tick && r.UInt64() == kill.key, "kill");
        }
        public void WriteState(StateHash h) { h.Add(kills.Length); foreach (var kill in kills) { h.Add(kill.tick); h.Add(kill.key); } }
    }

    // At each listed tick kills the first unit of the team, in recipe order, that stands on the field alive (Queries, order 28).
    internal sealed class KillFieldedUnit : IRetainedGameplaySystem
    {
        private readonly M6World world;
        private readonly int team;
        private readonly long[] ticks;
        internal KillFieldedUnit(M6World world, int team, long[] ticks) { this.world = world; this.team = team; this.ticks = ticks; }
        public int Order => 28;
        public string StateTypeId => "test.kill-fielded";
        public int StateVersion => 1;
        public void Step(SimContext c)
        {
            if (Array.IndexOf(ticks, c.Clock.Tick) < 0) return;
            foreach (UnitSpawn spawn in world.Recipe.Units)
            {
                if (spawn.Team != team || !world.Has<Position>(spawn.Key) || world.Dead(spawn.Key)) continue;
                StableId id = world.Sim.Entities.StableOf(world.Unit(spawn.Key));
                c.Damage(world.Sim.GameplayTarget(id), new GameplayApplyKey(id, (ulong)c.Clock.Tick, 0, id, (ulong)c.Clock.Tick, 0), 1, world.Recipe.Combat.MaxHealthRaw);
                return;
            }
        }
        public void SaveState(SimStateWriter w) { w.Int32(team); w.Int32(ticks.Length); foreach (long tick in ticks) w.Int64(tick); }
        public void LoadState(SimStateReader r)
        {
            SimStateReader.Require(r.Int32() == team && r.Int32() == ticks.Length, "ticks");
            foreach (long tick in ticks) SimStateReader.Require(r.Int64() == tick, "tick");
        }
        public void WriteState(StateHash h) { h.Add(team); h.Add(ticks.Length); foreach (long tick in ticks) h.Add(tick); }
    }
}
