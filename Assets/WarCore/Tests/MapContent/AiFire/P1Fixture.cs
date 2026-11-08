using System;
using System.Collections.Generic;
using System.Reflection;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // P1 fixtures, built on A3's memory fixture through its helpers (A3Fixture.cs; A3's files are not touched): A2's map with
    // its wall, grass concealing to a half (Lc = 10 m, L = 20 m), the rifle with effect 0 (30-tick interval, 3 rounds, 60-tick
    // reload), thinking interval 10, seed 41. Observer A (key 1, team 1, armed) stands on the only control point, which team 1
    // owns, so its AI never moves it. Targets (team 2, unarmed) move only on squad commands. Here the targets start west of A,
    // beyond the 20 m sight, and walk in along z = 10; nothing else moves.
    internal static class P1
    {
        internal const ulong KeyA = A3.KeyA, KeyB = A3.KeyB, KeyC = A3.KeyC, KeyA2 = 4;
        internal static readonly SimFix Range = SimFix.FromInt(30);   // the rifle's FalloffEnd (A2Fixture.cs Rifle)

        // The content layer: A3's (grass 500) with the ai object when given, else without the ai key.
        internal static string PackJson(string ai) => ai == null ? A3.PackJson(A3.Grass500, (int?)null) : A3.PackJson(A3.Grass500, ai);
        internal static string Ai(int reaction) => "{ \"reactionTicks\": " + reaction + " }";

        internal static UnitSpawn Target(ulong key, int squad, SimVec3 at, long speed = 3000) =>
            new UnitSpawn(key, 0, squad, at.X.Raw * 1000 / A2.S, at.Z.Raw * 1000 / A2.S, speed, 2, -1, false);
        internal static UnitSpawn Shooter(ulong key, SimVec3 at) => A2.Soldier(key, 1, at, 0, 3000);

        internal static WorldRecipe Recipe(UnitSpawn[] units, string ai, params IWorldOption[] extensions)
        {
            WorldRecipe r = A3.Recipe(units, A3.Load(PackJson(ai)));
            foreach (IWorldOption e in extensions) r = r.With(e);
            return r;
        }
        internal static M6World World(UnitSpawn[] units, string ai, bool reverse = false, params IWorldOption[] extensions) =>
            new M6World(Recipe(units, ai, extensions), WcMapWriter.Write(A2.Map(A2.DWalls)), reverse);

        internal static InputFrame Command(SquadCommandKind kind, int squad, SimVec3 at = default) =>
            new InputFrame(default, default, squad: new SquadCommand(kind, squad, at, default));

        // A script: (tick, squad, kind, destination). Ticks 1 and 2 hold squads 2 and 3; afterwards a squad whose move just
        // ended is held again (InterimOrders ends a move standing on arrival), so every target stays under squad command.
        internal readonly struct Move
        {
            internal readonly long Tick; internal readonly int Squad; internal readonly SimVec3 To;
            internal Move(long tick, int squad, SimVec3 to) { Tick = tick; Squad = squad; To = to; }
        }
        internal static InputFrame Drive(M6World w, long tick, Move[] script)
        {
            if (tick == 1) return Command(SquadCommandKind.Hold, 2);
            if (tick == 2) return Command(SquadCommandKind.Hold, 3);
            foreach (Move m in script) if (m.Tick == tick) return Command(SquadCommandKind.MoveTo, m.Squad, m.To);
            if (Ended(w, KeyB)) return Command(SquadCommandKind.Hold, 2);
            if (Ended(w, KeyC)) return Command(SquadCommandKind.Hold, 3);
            return default;
        }
        private static bool Ended(M6World w, ulong key) => w.Has<SquadStanding>(key)
            && w.Read<SquadStanding>(key).Kind == StandingKind.Move && !w.Read<MoveIntent>(key).Active;

        internal static StableId Id(M6World w, ulong key) => w.Sim.Entities.StableOf(w.Unit(key));

        // What the shooter's selector picks now (outside the combat step; Sight.cs takes the conservative path there).
        internal static ulong Selected(M6World w, ulong shooter)
        {
            if (!w.Combat.Pack.Selector.TrySelect(w.Combat, w.Unit(shooter), Range, out EntityId target)) return 0;
            return w.Sim.Entities.StableOf(target).Value;
        }

        // The ticks of the shooter's fire records this tick.
        internal static bool Fired(M6World w, ulong shooter)
        {
            StableId id = Id(w, shooter);
            foreach (FireRecord f in w.Combat.Fires) if (f.Shooter.Equals(id)) return true;
            return false;
        }

        // One run, recorded tick by tick (index = tick; index 0 is the start): what each shooter's selector picks after the
        // step, whether it fired, and its weapon state. Shooters are A and, when present, A2.
        internal sealed class Trace
        {
            internal M6World W;
            internal readonly List<ulong> SelA = new List<ulong>(), SelA2 = new List<ulong>(), Hashes = new List<ulong>();
            internal readonly List<bool> FireA = new List<bool>(), FireA2 = new List<bool>();
            internal readonly List<long> DueA = new List<long>();
            internal readonly List<int> AmmoA = new List<int>();
            internal readonly List<ulong> TargetA = new List<ulong>();   // the target of A's fire record this tick, 0 for none
            internal ulong Id(ulong key) => P1.Id(W, key).Value;
            internal long FirstSelect(ulong shooter, ulong target, long from = 1)
            {
                List<ulong> sel = shooter == KeyA ? SelA : SelA2;
                for (int t = (int)from; t < sel.Count; t++) if (sel[t] == Id(target)) return t;
                return -1;
            }
            internal long FirstFire(ulong shooter, long from = 1)
            {
                List<bool> fire = shooter == KeyA ? FireA : FireA2;
                for (int t = (int)from; t < fire.Count; t++) if (fire[t]) return t;
                return -1;
            }
        }

        internal static Trace Run(UnitSpawn[] units, Move[] script, string ai, long last, bool reverse = false, params IWorldOption[] extensions)
        {
            var r = new Trace { W = World(units, ai, reverse, extensions) };
            bool twin = Array.Exists(units, u => u.Key == KeyA2);
            for (long tick = 0; tick <= last; tick++)
            {
                if (tick > 0) r.W.Sim.Step(Drive(r.W, tick, script));
                r.SelA.Add(Selected(r.W, KeyA)); r.FireA.Add(tick > 0 && Fired(r.W, KeyA));
                r.SelA2.Add(twin ? Selected(r.W, KeyA2) : 0); r.FireA2.Add(tick > 0 && twin && Fired(r.W, KeyA2));
                Combatant a = r.W.Read<Combatant>(KeyA);
                r.DueA.Add(a.DueMilli); r.AmmoA.Add(a.Ammo); r.Hashes.Add(r.W.Hash);
                ulong hit = 0;
                if (tick > 0) foreach (FireRecord f in r.W.Combat.Fires) if (f.Shooter.Equals(Id(r.W, KeyA))) hit = f.Target.Value;
                r.TargetA.Add(hit);
            }
            return r;
        }

        // The standard cast: A at A3's point, B and C west of A beyond the 20 m sight (B 22.0 m, C 22.8 m away).
        internal static UnitSpawn[] Cast(SimVec3? cAt = null, SimVec3? bAt = null) => new[]
        {
            Shooter(KeyA, A3.AAt), Target(KeyB, 2, bAt ?? A2.Cell(0, 10)), Target(KeyC, 3, cAt ?? A2.Cell(0, 4)),
        };
        // B walks east along z = 10 from tick 20; A can first select it on tick 40 (B 19.9 m away).
        internal static readonly Move[] Walk = { new Move(20, 2, A2.Cell(8, 10)) };
        internal const long T = 40;

        // The registered reaction state's entry count (internal; the Battle assembly has no InternalsVisibleTo).
        internal static int Count(M6World w) => Count(w.System<ReactionFire>());
        internal static int Count(ReactionFire fire) =>
            (int)typeof(ReactionFire).GetProperty("Count", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(fire);

        // A test extension: a Requests system (Order 25) that submits an aimed intent for A on ticks from .. to.
        internal sealed class IntentProbe : WorldFeature
        {
            private readonly long from, to; private readonly SimVec3 point;
            internal IntentProbe(long from, long to, SimVec3 point) { this.from = from; this.to = to; this.point = point; }
            public override string OptionId => "test.p1-intent";
            public override void RegisterSystems(WorldAssembly a) =>
                a.Sim.RegisterGameplay("test.p1-intent", new Submitter(this, a.Sim, a.Combat), SimSystemPhase.Requests);
            private sealed class Submitter : IRetainedGameplaySystem
            {
                private readonly IntentProbe owner; private readonly Simulation sim; private readonly CombatSystem combat;
                internal Submitter(IntentProbe owner, Simulation sim, CombatSystem combat) { this.owner = owner; this.sim = sim; this.combat = combat; }
                public int Order => 25;
                public string StateTypeId => "test.p1-intent";
                public int StateVersion => 1;
                public void Step(SimContext c)
                {
                    long tick = c.Clock.Tick;
                    if (tick < owner.from || tick > owner.to) return;
                    StableId a = sim.Entities.StableOf(sim.Entities.Resolve(new EntityLink(StableEntityKey.Placed(1, KeyA).Id)));
                    combat.Submit(c, FireIntent.Aimed(a, owner.point));
                }
                public void SaveState(SimStateWriter writer) { }
                public void LoadState(SimStateReader reader) { }
                public void WriteState(StateHash hash) { hash.Add(StateTypeId); hash.Add(StateVersion); }
            }
        }

        // A test extension: a Requests system (Order 25) that destroys a placed unit on one tick.
        internal sealed class DestroyProbe : WorldFeature
        {
            private readonly long tick; private readonly ulong key;
            internal DestroyProbe(long tick, ulong key) { this.tick = tick; this.key = key; }
            public override string OptionId => "test.p1-destroy";
            public override void RegisterSystems(WorldAssembly a) =>
                a.Sim.RegisterGameplay("test.p1-destroy", new Destroyer(this, a.Sim), SimSystemPhase.Requests);
            private sealed class Destroyer : IRetainedGameplaySystem
            {
                private readonly DestroyProbe owner; private readonly Simulation sim;
                internal Destroyer(DestroyProbe owner, Simulation sim) { this.owner = owner; this.sim = sim; }
                public int Order => 25;
                public string StateTypeId => "test.p1-destroy";
                public int StateVersion => 1;
                public void Step(SimContext c)
                {
                    if (c.Clock.Tick == owner.tick)
                        sim.Entities.Destroy(sim.Entities.Resolve(new EntityLink(StableEntityKey.Placed(1, owner.key).Id)));
                }
                public void SaveState(SimStateWriter writer) { }
                public void LoadState(SimStateReader reader) { }
                public void WriteState(StateHash hash) { hash.Add(StateTypeId); hash.Add(StateVersion); }
            }
        }

        // 64-bit FNV-1a over the bytes.
        internal static ulong Digest(byte[] bytes) => A3.Digest(bytes);
        internal static string Systems(M6World w) => L4.Systems(w);
    }
}
