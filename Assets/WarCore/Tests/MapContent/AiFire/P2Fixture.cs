using System;
using System.Collections.Generic;
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
    // P2 fixtures, built on P1's (P1Fixture.cs; P1's and A3's files are not touched): A2's map with its wall, grass
    // concealing to a half, the rifle with effect 0 (30-tick interval, 3 rounds, 60-tick reload, no spread), thinking
    // interval 10, seed 41. The combat pack is written out here with the same values the P1 fixture takes from A2.Combat(0)
    // (CombatPack's defaults: ally-check miss 0, ally hold 30000 milli-ticks), so the ally tests assert the runtime values.
    internal static class P2
    {
        internal const ulong KeyA = P1.KeyA, KeyB = P1.KeyB, KeyC = P1.KeyC, KeyA2 = P1.KeyA2;
        internal const int AllyMissRaw = 0;
        internal const long AllyHoldMilli = 30000;

        internal static string Ai(int reaction, int? sense) => sense == null ? P1.Ai(reaction)
            : "{ \"reactionTicks\": " + reaction + ", \"senseTicks\": " + sense.Value + " }";
        internal static string AiAll(int memory, int search, int reaction, int sense) =>
            "{ \"targetMemoryTicks\": " + memory + ", \"searchTicks\": " + search + ", \"reactionTicks\": " + reaction
            + ", \"senseTicks\": " + sense + " }";

        internal static CombatPack Combat() =>
            new CombatPack(new[] { A2.Rifle(0) }, new RwrHitResolver(), new SightLimited(new NearestVisibleEnemy(), A2.Sight), 100 * A2.S,
                friendlyFire: false, friendlyFireLocked: false, allyCheckMissRaw: AllyMissRaw, allyHoldMilli: AllyHoldMilli);

        internal static WorldRecipe Recipe(UnitSpawn[] units, string ai, params IWorldOption[] extensions)
        {
            WorldRecipe r = A3.Recipe(units, A3.Load(P1.PackJson(ai))).WithCombat(Combat());
            foreach (IWorldOption e in extensions) r = r.With(e);
            return r;
        }
        internal static M6World World(UnitSpawn[] units, string ai, bool reverse = false, params IWorldOption[] extensions) =>
            new M6World(Recipe(units, ai, extensions), WcMapWriter.Write(A2.Map(A2.DWalls)), reverse);

        // The cast: A at A3's point; A2 (same team, armed) where given; B and C west of A beyond the 20 m sight.
        internal static UnitSpawn[] Cast(SimVec3? a2At = null, SimVec3? bAt = null, SimVec3? cAt = null)
        {
            var units = new List<UnitSpawn> { P1.Shooter(KeyA, A3.AAt) };
            if (a2At != null) units.Add(P1.Shooter(KeyA2, a2At.Value));
            units.Add(P1.Target(KeyB, 2, bAt ?? A2.Cell(0, 10)));
            units.Add(P1.Target(KeyC, 3, cAt ?? A2.Cell(0, 4)));
            return units.ToArray();
        }
        internal static readonly SimVec3 A2Behind = A2.Cell(24, 10), A2Aside = A2.Cell(22, 13);

        internal static StableId Id(M6World w, ulong key) => P1.Id(w, key);
        // Whether the placed unit still exists (a destroyed target no longer resolves).
        internal static bool Present(M6World w, ulong key) => w.Sim.Entities.TryResolve(StableEntityKey.Placed(1, key).Id, out _);

        // P1.Drive with a guard for a destroyed target: the same commands on the same ticks while every unit exists.
        internal static InputFrame Drive(M6World w, long tick, P1.Move[] script)
        {
            if (tick == 1) return P1.Command(SquadCommandKind.Hold, 2);
            if (tick == 2) return P1.Command(SquadCommandKind.Hold, 3);
            foreach (P1.Move m in script) if (m.Tick == tick) return P1.Command(SquadCommandKind.MoveTo, m.Squad, m.To);
            if (Present(w, KeyB) && Ended(w, KeyB)) return P1.Command(SquadCommandKind.Hold, 2);
            if (Present(w, KeyC) && Ended(w, KeyC)) return P1.Command(SquadCommandKind.Hold, 3);
            return default;
        }
        private static bool Ended(M6World w, ulong key) => w.Has<SquadStanding>(key)
            && w.Read<SquadStanding>(key).Kind == StandingKind.Move && !w.Read<MoveIntent>(key).Active;
        internal static int Count(M6World w) => P1.Count(w);
        internal static string Systems(M6World w) => P1.Systems(w);
        internal static ulong Digest(byte[] bytes) => A3.Digest(bytes);

        // The sampling schedule under test, in the two-remainder form that never overflows a long.
        internal static bool Samples(ulong id, long tick, int k) => k == 1 || ((tick % k) + (long)(id % (ulong)k)) % k == 0;
        internal static long Phase(ulong id, int k) => (long)(id % (ulong)k);
        internal static long FirstSample(ulong id, long from, int k)
        {
            for (long tick = from; ; tick++) if (Samples(id, tick, k)) return tick;
        }
        // The horizontal cross product X * Z' - Z * X' of two vectors, in raw units (positions here stay far below 2^31 raw).
        internal static long Cross(SimVec3 a, SimVec3 b) => a.X.Raw * b.Z.Raw - a.Z.Raw * b.X.Raw;
        internal static SimVec3 Flat(SimVec3 v) => CombatSystem.Horizontal(v);

        // One shooter's per-tick record (index = tick; index 0 is the start).
        internal sealed class Shots
        {
            internal readonly List<ulong> Sel = new List<ulong>(), Target = new List<ulong>();
            internal readonly List<bool> Fire = new List<bool>();
            internal readonly List<long> Due = new List<long>();
            internal readonly List<int> Ammo = new List<int>();
            internal readonly List<SimVec3> At = new List<SimVec3>(), Origin = new List<SimVec3>(), End = new List<SimVec3>();
            internal long FirstFire(long from = 1)
            {
                for (int t = (int)from; t < Fire.Count; t++) if (Fire[t]) return t;
                return -1;
            }
            internal long LastFire(long upTo)
            {
                for (int t = (int)upTo; t >= 1; t--) if (Fire[t]) return t;
                return -1;
            }
            internal List<long> FireTicks(long upTo)
            {
                var ticks = new List<long>();
                for (int t = 1; t <= upTo && t < Fire.Count; t++) if (Fire[t]) ticks.Add(t);
                return ticks;
            }
            internal long FirstSelect(ulong target, long from = 1)
            {
                for (int t = (int)from; t < Sel.Count; t++) if (Sel[t] == target) return t;
                return -1;
            }
        }

        internal sealed class Trace
        {
            internal M6World W;
            internal readonly Shots A = new Shots(), A2 = new Shots();
            internal readonly List<ulong> Hashes = new List<ulong>();
            internal readonly List<int> Queries = new List<int>();
            internal readonly List<SimVec3> AtB = new List<SimVec3>(), AtC = new List<SimVec3>();
            internal readonly List<int> Counts = new List<int>();
            internal ulong Id(ulong key) => P2.Id(W, key).Value;
        }

        private static void Record(M6World w, ulong key, Shots s, long tick, bool present)
        {
            if (!present) { s.Sel.Add(0); s.Target.Add(0); s.Fire.Add(false); s.Due.Add(0); s.Ammo.Add(0); s.At.Add(default); s.Origin.Add(default); s.End.Add(default); return; }
            s.Sel.Add(P1.Selected(w, key));
            Combatant unit = w.Read<Combatant>(key);
            s.Due.Add(unit.DueMilli); s.Ammo.Add(unit.Ammo); s.At.Add(w.Read<Position>(key).Value);
            bool fired = false; ulong hit = 0; SimVec3 origin = default, end = default;
            if (tick > 0)
            {
                StableId id = P2.Id(w, key);
                foreach (FireRecord f in w.Combat.Fires)
                    if (f.Shooter.Equals(id)) { fired = true; hit = f.Target.Value; origin = f.Origin; end = f.End; }
            }
            s.Fire.Add(fired); s.Target.Add(hit); s.Origin.Add(origin); s.End.Add(end);
        }

        internal static Trace Run(UnitSpawn[] units, P1.Move[] script, string ai, long last, bool reverse = false, params IWorldOption[] extensions)
        {
            var r = new Trace { W = World(units, ai, reverse, extensions) };
            bool twin = Array.Exists(units, u => u.Key == KeyA2);
            bool counted = Systems(r.W).Contains("battle.ai-fire");
            for (long tick = 0; tick <= last; tick++)
            {
                if (tick > 0) r.W.Sim.Step(Drive(r.W, tick, script));
                Record(r.W, KeyA, r.A, tick, true);
                Record(r.W, KeyA2, r.A2, tick, twin);
                r.Hashes.Add(r.W.Hash);
                r.Queries.Add(r.W.Combat.QueriesThisStep);
                r.AtB.Add(Present(r.W, KeyB) ? r.W.Read<Position>(KeyB).Value : default);
                r.AtC.Add(r.W.Read<Position>(KeyC).Value);
                r.Counts.Add(counted ? Count(r.W) : -1);
            }
            return r;
        }

        // 64-bit FNV-1a over a sequence of 64-bit values.
        internal static ulong Fold(IEnumerable<ulong> values)
        {
            ulong fold = 14695981039346656037UL;
            foreach (ulong v in values) fold = (fold ^ v) * 1099511628211UL;
            return fold;
        }
        internal static ulong Fold(IEnumerable<long> values)
        {
            ulong fold = 14695981039346656037UL;
            foreach (long v in values) fold = (fold ^ unchecked((ulong)v)) * 1099511628211UL;
            return fold;
        }

        // A test extension: a Queries system (Order 25, before combat's 30 in the same phase) that deals a lethal blow to a
        // placed unit on one tick. The death settles after the phase, so it is seen from the next tick on.
        internal sealed class KillProbe : WorldFeature
        {
            private readonly long tick; private readonly ulong key;
            internal KillProbe(long tick, ulong key) { this.tick = tick; this.key = key; }
            public override string OptionId => "test.p2-kill";
            public override void RegisterSystems(WorldAssembly a) =>
                a.Sim.RegisterGameplay("test.p2-kill", new Killer(this, a.Sim, a.Recipe.Combat.MaxHealthRaw), SimSystemPhase.Queries);
            private sealed class Killer : IRetainedGameplaySystem
            {
                private readonly KillProbe owner; private readonly Simulation sim; private readonly long health;
                internal Killer(KillProbe owner, Simulation sim, long health) { this.owner = owner; this.sim = sim; this.health = health; }
                public int Order => 25;
                public string StateTypeId => "test.p2-kill";
                public int StateVersion => 1;
                public void Step(SimContext c)
                {
                    if (c.Clock.Tick != owner.tick) return;
                    StableId id = sim.Entities.StableOf(sim.Entities.Resolve(new EntityLink(StableEntityKey.Placed(1, owner.key).Id)));
                    c.Damage(sim.GameplayTarget(id), new GameplayApplyKey(id, (ulong)owner.tick, 0, id, (ulong)owner.tick, 0), 1, health);
                }
                public void SaveState(SimStateWriter writer) { }
                public void LoadState(SimStateReader reader) { }
                public void WriteState(StateHash hash) { hash.Add(StateTypeId); hash.Add(StateVersion); }
            }
        }
    }
}
