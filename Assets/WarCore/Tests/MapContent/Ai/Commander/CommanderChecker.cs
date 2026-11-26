using System;
using System.Collections.Generic;
using System.Reflection;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests
{
    // RC2-3: what the commander's rows must satisfy on every tick (card appendix 7, I1, I2, I7, I8, L2, L3), worked out from
    // scratch and reading the commander through its public members only (View; a test may hand one that breaks a rule on
    // purpose). It runs right after the commander (queries, order 42), not at the end of the tick: the tick's hits settle
    // after the queries phase, so a unit killed on that tick is dead there while the commander still gave it a row.
    // Registering it changes the world's hash and snapshot manifest. Allocates; tests only.
    internal sealed class CommanderChecker
    {
        internal const int ProbeOrder = 42;

        // The checker as a gameplay system; it keeps nothing a snapshot needs (a restore makes it remember the loaded rows).
        internal sealed class Probe : IRetainedGameplaySystem
        {
            internal readonly CommanderChecker Checker;
            internal Probe(CommanderChecker checker) { Checker = checker; }
            public int Order => ProbeOrder;
            public string StateTypeId => "test.commander-check";
            public int StateVersion => 1;
            public void Step(SimContext c) => Checker.Check(c.Clock.Tick);
            public void SaveState(SimStateWriter writer) { }
            public void LoadState(SimStateReader reader) => Checker.Remember();
            public void WriteState(StateHash hash) { }
        }

        // What the checker reads of the commander: its rows, its attack flags and its rules.
        internal class View
        {
            internal readonly Commander Commander;
            internal View(Commander commander) { Commander = commander ?? throw new ArgumentNullException(nameof(commander)); }
            internal virtual int OrderCount => Commander.OrderCount;
            internal virtual ulong OrderKeyAt(int index) => Commander.OrderKeyAt(index);
            internal virtual int OrderPointAt(int index) => Commander.OrderPointAt(index);
            internal virtual bool Attacks(int team, int point) => Commander.Attacks(team, point);
        }

        private struct Unit { internal ulong Key, Group; internal int Team, Layer; internal SimVec3 GroupAt; }
        private struct Group { internal ulong Key; internal int Team, Layer, Old, New, Size; internal SimVec3 At; internal bool LeaderRow; }

        private readonly CombatSystem combat;
        private readonly CaptureSystem capture;
        private readonly Squads squads;
        private readonly IMoveOrders moves;
        private readonly IControlTarget controlled;
        private readonly IPointReach reach;
        private readonly IUnitLayers layers;
        private readonly View view;
        private readonly EntityStage stage;
        private readonly ComponentType<Combatant> combatants;
        private readonly ComponentType<Position> positions;
        private readonly int points, teams, cap;
        private SortedDictionary<ulong, int> previous = new SortedDictionary<ulong, int>();

        internal readonly List<string> Findings = new List<string>();
        internal readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
        // Rows whose point is a point of the unit's own team (card acceptance 4: 0 over a whole match).
        internal int OwnPointRows { get; private set; }
        internal int Checks { get; private set; }

        internal CommanderChecker(CombatSystem combat, CaptureSystem capture, Squads squads, IMoveOrders moves, IControlTarget controlled,
            IPointReach reach, IUnitLayers layers, View view)
        {
            this.combat = combat; this.capture = capture; this.squads = squads; this.moves = moves; this.controlled = controlled;
            this.reach = reach; this.layers = layers; this.view = view;
            stage = new EntityStage(combat.Entities);
            combatants = combat.Entities.Resolve<Combatant>(); positions = combat.Entities.Resolve<Position>();
            points = view.Commander.PointCount; teams = view.Commander.TeamCount;
            cap = Math.Max(1, view.Commander.Rules.Quota.MaxAttackTargets);
            Remember();
        }

        // Registers a checker in the world before its first tick, built from the world's own parts: its commander (or the one
        // given, which the test registered itself) and the same IPointReach and IUnitLayers that commander got (null on a map
        // without floor plates, which is every map the commander accepts).
        internal static CommanderChecker Attach(M6World w, Commander commander = null, IPointReach reach = null, IUnitLayers layers = null, View view = null)
        {
            commander = commander ?? w.System<Commander>() ?? throw new ArgumentException("The world has no commander.", nameof(w));
            var controlled = (IControlTarget)w.Session.GetType().GetField("controlled", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(w.Session);
            var checker = new CommanderChecker(w.Combat, w.Capture, w.System<Squads>(), M7GuardTests.Adapter(w), controlled, reach, layers,
                view ?? new View(commander));
            w.Sim.RegisterGameplay("test.commander-check", new Probe(checker), SimSystemPhase.Queries);
            return checker;
        }

        internal int Count(string rule) => Counts.TryGetValue(rule, out int n) ? n : 0;
        internal bool Clean => Findings.Count == 0 && Counts.Count == 0;
        internal string First => Findings.Count > 0 ? string.Join(" | ", Findings) : "clean";

        // After a restore: the rows the loaded commander holds are the rows of the last tick.
        internal void Remember()
        {
            previous = new SortedDictionary<ulong, int>();
            for (int i = 0; i < view.OrderCount; i++) previous[view.OrderKeyAt(i)] = view.OrderPointAt(i);
        }

        private void Fail(string rule, long tick, string what)
        {
            Counts[rule] = Count(rule) + 1;
            if (Findings.Count < 20) Findings.Add(rule + " tick " + tick + ": " + what);
        }

        private bool Reaches(SimVec3 from, int layer, int point) => reach == null || reach.TryCost(point, from, layer, out _);

        internal void Check(long tick)
        {
            Checks++;
            // Who the commander dispatches this tick, and their groups.
            var units = new SortedDictionary<ulong, Unit>();
            foreach (EntityId entity in stage.Capture())
            {
                if (!AiUnits.Steered(combat, combatants, positions, moves, controlled, entity, out StableId id, out int team, out SimVec3 here)) continue;
                ulong group = id.Value; SimVec3 groupAt = here;
                if (squads != null)
                {
                    bool body = squads.SizeOf(id) > 0;
                    if (squads.TryLeader(id, out StableId leader, out SimVec3 leaderHere)) { group = leader.Value; groupAt = leaderHere; body = true; }
                    if (body && squads.RegroupOf(id) == SquadRally.Active) continue;
                }
                units[id.Value] = new Unit { Key = id.Value, Group = group, Team = team, GroupAt = groupAt, Layer = layers != null ? layers.LayerOf(entity) : 0 };
            }
            var groups = new SortedDictionary<ulong, Group>();
            foreach (Unit u in units.Values)
            {
                // Units come in key order: the first unit of a group has the smallest key.
                if (!groups.TryGetValue(u.Group, out Group g)) g = new Group { Key = u.Group, Team = u.Team, Layer = u.Layer, At = u.GroupAt, Old = -1, New = int.MinValue };
                g.Size++;
                groups[u.Group] = g;
            }
            // R4's first sentence on the last tick's rows (of the units dispatched now): the leader's row, else the row of the
            // group's unit with the smallest key that has one.
            foreach (Unit u in units.Values)
            {
                if (!previous.TryGetValue(u.Key, out int row)) continue;
                Group g = groups[u.Group];
                if (u.Key == g.Key) { g.Old = row; g.LeaderRow = true; }
                else if (!g.LeaderRow && g.Old < 0) g.Old = row;
                groups[u.Group] = g;
            }
            var now = new SortedDictionary<ulong, int>();
            // I1: one row per unit, keys strictly increasing, only units the commander dispatches, one point per group.
            for (int i = 0; i < view.OrderCount; i++)
            {
                ulong key = view.OrderKeyAt(i);
                int point = view.OrderPointAt(i);
                if (i > 0 && key <= view.OrderKeyAt(i - 1)) Fail("I1", tick, "keys not strictly increasing at row " + i);
                if (!units.ContainsKey(key)) Fail("I1", tick, "row for unit " + key + " that the commander does not dispatch");
                if (point < 0 || point >= points) { Fail("I1", tick, "row for unit " + key + " names point " + point); continue; }
                now[key] = point;
            }
            foreach (Unit u in units.Values)
            {
                int row = now.TryGetValue(u.Key, out int r) ? r : -1;
                Group g = groups[u.Group];
                if (g.New == int.MinValue) g.New = row;
                else if (g.New != row) Fail("I1", tick, "group " + g.Key + " has units with rows " + g.New + " and " + row);
                groups[u.Group] = g;
                if (row < 0) continue;
                // I2: the point of a row is a target of the unit's team this tick (so never an own point).
                if (!view.Attacks(u.Team, row)) Fail("I2", tick, "unit " + u.Key + " remembers point " + row + " that is not a target");
                if (capture.Points[row].Owner == u.Team) { OwnPointRows++; Fail("own", tick, "unit " + u.Key + " remembers point " + row + " of its own team"); }
            }
            // I7: a unit whose row leaves point p has a reason: p is no target now; its group cannot walk to p; it follows
            // another group now (its own old row is not its group's old order); or the commander no longer dispatches it.
            foreach (KeyValuePair<ulong, int> old in previous)
            {
                int p = old.Value;
                if (!units.TryGetValue(old.Key, out Unit u)) continue;
                if (now.TryGetValue(old.Key, out int row) && row == p) continue;
                Group g = groups[u.Group];
                if (g.Old != p) continue;
                if (!view.Attacks(u.Team, p) || !Reaches(g.At, g.Layer, p)) continue;
                Fail("I7", tick, "unit " + old.Key + " left point " + p + " that is still a target its group can walk to");
            }
            // I8: a group let go of its old order p while p is not its team's and it can walk to p: then p lost on score, so
            // its team attacks as many points as it may.
            foreach (Group g in groups.Values)
            {
                int p = g.Old;
                if (p < 0 || g.New == p || capture.Points[p].Owner == g.Team || !Reaches(g.At, g.Layer, p)) continue;
                int targets = 0;
                for (int q = 0; q < points; q++) if (view.Attacks(g.Team, q)) targets++;
                if (targets != cap) Fail("I8", tick, "group " + g.Key + " let go of point " + p + " while its team attacks " + targets + " of " + cap + " points");
            }
            for (int t = 1; t <= teams; t++)
            {
                bool anyTarget = false;
                for (int p = 0; p < points; p++) anyTarget |= view.Attacks(t, p);
                bool reaches = false, engaged = false;
                var groupsAt = new int[points];
                foreach (Group g in groups.Values)
                {
                    if (g.Team != t) continue;
                    for (int p = 0; p < points && !reaches; p++) reaches = view.Attacks(t, p) && Reaches(g.At, g.Layer, p);
                    if (g.New < 0) continue;
                    groupsAt[g.New]++;
                    if (view.Attacks(t, g.New)) engaged = true;
                }
                // L2: nobody idle while there is something to attack that somebody can walk to.
                if (anyTarget && reaches && !engaged) Fail("L2", tick, "team " + t + " has points to attack and groups that reach them, but nobody attacks");
                // L3: no target without a group while a group without an order can walk to it.
                for (int p = 0; p < points; p++)
                {
                    if (!view.Attacks(t, p) || groupsAt[p] > 0) continue;
                    foreach (Group g in groups.Values)
                        if (g.Team == t && g.New < 0 && Reaches(g.At, g.Layer, p))
                        { Fail("L3", tick, "team " + t + " point " + p + " is attacked by nobody, group " + g.Key + " has no order and reaches it"); break; }
                }
            }
            previous = now;
        }
    }
}
