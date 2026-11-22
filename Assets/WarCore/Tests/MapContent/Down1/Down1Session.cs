using System;
using System.Collections.Generic;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Stance.Stance1Worlds;

namespace WarCore.Tests.Down1
{
    // DOWN-1 session fixtures: a world built by the real WorldBuilder (as the STANCE-1 fixtures, the 40 x 16 m wall-free map,
    // seed 41) with scripted in-step actions: Down and Stun in the Queries phase at order 26, anything else at an order given.
    internal sealed class Down1Session
    {
        internal const long M = SimFix.Scale;
        internal const int DownOrder = 26;

        internal static CombatPack DownPack() => RwrPack.Load("rwr-down").BuildCombat();

        // rwr-field (no stances) with combat.downed: lethal, 1800 ticks, crawl 150.
        internal static CombatPack NoStancePack() => Down1ContentTests.Load(
            Down1ContentTests.Downed(stance: null, finishAfter: "0", stun: null, targetable: null), "rwr-field").BuildCombat();

        internal readonly SimSession S;
        private readonly Dictionary<int, List<Action<SimContext>>> scripts = new Dictionary<int, List<Action<SimContext>>>();

        internal Down1Session(SimSession session) { S = session; }

        internal Down1Session(CombatPack pack, UnitSpawn[] units, BridgeCommand[] script = null, Func<WorldRecipe, WorldRecipe> open = null,
            MoverKind mover = MoverKind.InterimCrowd) : this(World(pack, units, Map(), script, open, mover)) { }

        internal WarCore.Sim.Sim Sim => SimOf(S);
        internal CombatSystem Combat => CombatOf(S);
        internal EntityId Unit(ulong key) => Stance.Stance1Worlds.Unit(S, key);
        internal SimVec3 Pos(ulong key) => M3TestWorld.Position(S, key);
        internal long Tick => S.Tick;
        internal void Step() => S.StepOnce();
        internal void StepTo(long tick) { while (S.Tick < tick) S.StepOnce(); }
        internal bool Downed(ulong key) => Combat.IsDowned(Unit(key));
        internal bool Stunned(ulong key) => Combat.IsStunned(Unit(key));
        internal bool Dead(ulong key) => Combat.IsDead(Unit(key));

        // Runs action during the step of every tick, in a phase at an order (one system per order; register before the first step).
        internal void Every(SimSystemPhase phase, int order, Action<SimContext> action)
        {
            if (!scripts.TryGetValue(order, out List<Action<SimContext>> list))
            {
                list = scripts[order] = new List<Action<SimContext>>();
                Sim.RegisterGameplay("down1.session-" + order, new Script(list, order), phase);
            }
            list.Add(action);
        }

        internal void At(long tick, SimSystemPhase phase, int order, Action<SimContext> action) =>
            Every(phase, order, c => { if (c.Clock.Tick == tick) action(c); });

        internal void DownAt(long tick, ulong key) => At(tick, SimSystemPhase.Queries, DownOrder, c => Combat.Down(c, Unit(key)));
        internal void StunAt(long tick, ulong key, int ticks) => At(tick, SimSystemPhase.Queries, DownOrder, c => Combat.Stun(c, Unit(key), ticks));

        // The horizontal length of a move, rounded to a raw unit.
        internal static long Length(SimVec3 d) => (long)Math.Round(Math.Sqrt((double)d.X.Raw * d.X.Raw + (double)d.Z.Raw * d.Z.Raw));

        // The horizontal length of each of the next n steps of unit key.
        internal List<long> Steps(ulong key, int n, Action<Down1Session> between = null)
        {
            var list = new List<long>();
            for (int i = 0; i < n; i++)
            {
                between?.Invoke(this);
                SimVec3 a = Pos(key);
                Step();
                list.Add(Length(Pos(key) - a));
            }
            return list;
        }

        // The events of type id published on the tick just run.
        internal int Events(int typeId)
        {
            int n = 0;
            long tick = Sim.Clock.Tick;
            int count = Sim.Events.Read(tick).Length;
            for (int e = 0; e < count; e++)
                if (Sim.ReadStableEvent(tick, e).Type.Value == typeId) n++;
            return n;
        }

        private sealed class Script : IRetainedGameplaySystem
        {
            private readonly List<Action<SimContext>> actions;
            internal Script(List<Action<SimContext>> actions, int order) { this.actions = actions; Order = order; }
            public int Order { get; }
            public string StateTypeId => "down1.session-" + Order;
            public int StateVersion => 1;
            public void Step(SimContext c) { foreach (Action<SimContext> a in actions) a(c); }
            public void SaveState(SimStateWriter w) { }
            public void LoadState(SimStateReader r) { }
            public void WriteState(StateHash h) { h.Add(StateTypeId); }
        }
    }
}
