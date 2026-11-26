using System;
using System.Collections.Generic;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // A small battle with the commander for the RC2-3 rule tests: flat ground without walls (width x 40 m), the given points,
    // units and bases, an ai section (default: dispatch "commander" with 3, 200, 1, 600, 30000), a match that never ends,
    // and the checker attached. What happens to the world comes from a script system in the queries phase at order 38
    // (after movement, before the capture at 40 and the commander at 41): units fall (the damage settles at the end of the
    // tick, so the commander still sees them standing on that tick) or the player takes another body. A unit moved to a
    // place stands there when the tick begins.
    internal sealed class CommanderBoard
    {
        internal readonly M6World W;
        internal readonly Commander Commander;
        internal readonly CommanderChecker Checker;
        private readonly Script script;
        private readonly ComponentType<Position> positions;
        private readonly List<(long Tick, ulong Key, int X, int Z)> moves = new List<(long, ulong, int, int)>();

        internal static string Ai(int targets = 3, int enemy = 200, int distance = 1, int stick = 600, int threat = 30000, string extra = null) =>
            (extra != null ? extra + ", " : "") + "\"dispatch\": \"commander\", \"commanderMaxAttackTargets\": " + targets + ", \"commanderEnemyWeight\": " + enemy
            + ", \"commanderDistanceWeightPerMetre\": " + distance + ", \"commanderStickiness\": " + stick + ", \"commanderThreatRadiusMm\": " + threat;

        internal static ControlPointDef Point(int id, int x, int owner, int z = 20) => new ControlPointDef(id, SimVec3.Metres(x, 0, z), SimFix.FromInt(6), owner);
        internal static SpawnAnchor Base(int team, int x, int z = 20) => new SpawnAnchor(team, SimVec3.Metres(x, 0, z));
        // An unarmed unit walking 4 m/s.
        internal static UnitSpawn Man(ulong key, int team, int x, int z = 20, bool reserve = false) => M6World.U(key, team, x, z, reserve, -1, 4000);

        internal CommanderBoard(ControlPointDef[] points, UnitSpawn[] units, SpawnAnchor[] bases, int width, string ai = null, int capture = 300,
            int think = 30, bool standingOrders = false, ulong player = 0, int interval = 90, bool check = true, IPointReach reach = null, CommanderRules? own = null)
        {
            WorldRecipe recipe = M6World.RecipeOf(units, M6World.Pack(points, ticks: capture, think: think, interval: interval, bases: bases))
                .WithRuleSet(MatchMode.TestNeverEndsId).WithPack(CommanderScene.Pack(ai ?? Ai()));
            if (standingOrders) recipe = recipe.WithStandingOrders();
            if (player != 0) recipe = recipe.WithPlayer(player);
            W = new M6World(recipe, WcMapWriter.Write(Wp23.Make(width, 40, 0, 4 * SimFix.Scale, Array.Empty<(int, int, int, int)>(), 2 * SimFix.Scale, null)));
            positions = W.Sim.Entities.Resolve<Position>();
            script = new Script(this);
            W.Sim.RegisterGameplay("test.commander-board-script", script, SimSystemPhase.Queries);
            Commander = W.System<Commander>();
            if (own != null)
            {
                // A commander the test registers itself, with its own IPointReach (the world's own dispatch stays "nearest").
                Commander = new Commander(W.Combat, recipe.Match, W.Capture, M7GuardTests.Adapter(W), Controlled, W.System<Squads>(), own.Value, reach);
                W.Sim.RegisterGameplay("battle.ai-commander", Commander, SimSystemPhase.Queries);
            }
            if (check) Checker = CommanderChecker.Attach(W, Commander, reach);
        }

        internal IControlTarget Controlled =>
            (IControlTarget)W.Session.GetType().GetField("controlled", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(W.Session);
        internal Squads Squads => W.System<Squads>();
        internal long Tick => W.Sim.Clock.Tick;
        internal StableId Id(ulong key) => W.Sim.Entities.StableOf(W.Unit(key));
        internal int Row(ulong key) => Commander.CommandOf(Id(key).Value);
        internal int[] Rows(params ulong[] keys) { var rows = new int[keys.Length]; for (int i = 0; i < keys.Length; i++) rows[i] = Row(keys[i]); return rows; }
        // The spawn key of the unit a unit is dispatched by (Commander.GroupOf); 0 when that unit is no unit of the recipe.
        internal ulong Group(ulong key)
        {
            ulong group = Commander.GroupOf(Id(key));
            foreach (UnitSpawn unit in W.Recipe.Units) if (Id(unit.Key).Value == group) return unit.Key;
            return 0;
        }
        internal bool Alive(ulong key) => !W.Combat.IsDead(W.Unit(key));
        internal int Owner(int point) => W.Capture.Points[point].Owner;
        // On the field and standing (a reserve that has not come in yet is not).
        internal bool Present(ulong key)
        {
            if (!W.Sim.Entities.TryResolve(new StableId(StableEntityKey.Placed(1, key).Id.Value), out EntityRef found)) return false;
            return W.Sim.Entities.TryGet(positions, (EntityId)found, out _) && !W.Combat.IsDead((EntityId)found);
        }
        internal void Step(int count = 1) { for (int i = 0; i < count; i++) { Place(); W.Step(); } }
        internal void To(long tick) { while (Tick < tick) { Place(); W.Step(); } }
        // One tick with a command to the player's led squad.
        internal void Order(SquadCommandKind kind) { Place(); W.Sim.Step(new InputFrame(default, default, squad: new SquadCommand(kind, SquadCommand.Led, default, default, 0))); }

        // Script entries: on that tick, in the queries phase before the capture and the commander.
        internal void Fall(long tick, params ulong[] keys) => script.Add(tick, c =>
        {
            foreach (ulong key in keys)
            {
                StableId id = Id(key);
                c.Damage(W.Sim.GameplayTarget(id), new GameplayApplyKey(id, (ulong)tick, 0, id, (ulong)tick, 0), 1, W.Recipe.Combat.MaxHealthRaw);
            }
        });
        // A unit stands at a place when the tick begins (positions are written between ticks, as M6CaptureTests does).
        internal void Move(long tick, ulong key, int x, int z) => moves.Add((tick, key, x, z));
        internal void Take(long tick, ulong key) => script.Add(tick, c => Controlled.Switch(c, W.Unit(key)));

        // A standing hold order (the unit counts as commanded: R0) on, or taken off, between ticks.
        internal void Hold(ulong key, bool on = true)
        {
            ComponentType<SquadStanding> standing = W.Sim.Entities.Resolve<SquadStanding>();
            if (on) W.Sim.Entities.Set(standing, W.Unit(key), new SquadStanding(StandingKind.Hold));
            else W.Sim.Entities.Remove(standing, W.Unit(key));
        }

        // Loads the commander with these rows (spawn key, point) and no attack flags, as if the last tick had left them; the
        // checker remembers them as the last tick's rows.
        internal void Give(params (ulong Key, int Point)[] rows)
        {
            var sorted = new List<(ulong Id, int Point)>();
            foreach ((ulong key, int point) in rows) sorted.Add((Id(key).Value, point));
            sorted.Sort((a, b) => a.Id.CompareTo(b.Id));
            var writer = new SimStateWriter();
            CommanderRules r = Commander.Rules;
            writer.Int32(r.Quota.MaxAttackTargets); writer.Int32(r.Quota.EnemyWeight); writer.Int32(r.Quota.DistanceWeightPerMetre);
            writer.Int32(r.Quota.Stickiness); writer.Int32(r.ThreatRadiusMm);
            int flags = Commander.TeamCount * Commander.PointCount;
            writer.Int32(flags);
            for (int i = 0; i < flags; i++) writer.Bool(false);
            writer.Int32(sorted.Count);
            foreach ((ulong id, int point) in sorted) { writer.UInt64(id); writer.Int32(point); }
            Commander.LoadState(new SimStateReader(writer.ToArray()));
            Checker?.Remember();
        }

        private void Place()
        {
            foreach ((long tick, ulong key, int x, int z) in moves)
                if (tick == Tick + 1) W.Sim.Entities.Set(positions, W.Unit(key), new Position(SimVec3.Metres(x, 0, z)));
        }

        private sealed class Script : IRetainedGameplaySystem
        {
            private readonly CommanderBoard board;
            private readonly List<(long Tick, Action<SimContext> Act)> entries = new List<(long, Action<SimContext>)>();
            internal Script(CommanderBoard board) { this.board = board; }
            internal void Add(long tick, Action<SimContext> act) => entries.Add((tick, act));
            public int Order => 38;
            public string StateTypeId => "test.commander-board-script";
            public int StateVersion => 1;
            public void Step(SimContext c)
            {
                foreach ((long tick, Action<SimContext> act) in entries) if (tick == c.Clock.Tick) act(c);
            }
            public void SaveState(SimStateWriter writer) { }
            public void LoadState(SimStateReader reader) { }
            public void WriteState(StateHash hash) { }
        }
    }
}
