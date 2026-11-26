using System;
using System.Collections.Generic;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // RC2-3 card appendix 7a: the three boards of the standard answers (early, idle, lose), built as the draft's scene probe
    // built them, numbers unchanged. Flat ground without walls, nobody has a weapon, the match never ends.
    //   early: 120 x 40 m. A (team 1, x 20, z 20, radius 6 m), B (team 2, x 100); bases (4, 20) and (116, 20). Team 1: 40
    //          units (keys 1..40) at x 8..22, z 12..18; team 2: 12 (keys 100..111) at x 106..112, z 16..20. Speed 4 m/s,
    //          capture 300 ticks, think every 30. Once 8 team-2 units stand within 30 m of A (or 600 ticks after the first
    //          did), every team-2 unit falls 300 ticks later.
    //   idle:  early plus 12 team-1 units (keys 41..52) at z 36, x 30..52, held from the start (R0: commanded). A team-2 unit
    //          is stopped and held once it stands within 20 m of A. After the first tick on which the commander counts at
    //          least 4 enemies near A the batch is let go, and every team-2 unit falls 200 ticks after that.
    //   lose:  320 x 40 m. A (team 1, x 20), B (team 2, x 300); bases (4, 20) and (316, 20). Team 1: 4 units (keys 1..4) at
    //          x 8..14, z 12, and a batch of 10 (keys 11..20) at z 36, x 30, 34, .., 66, held from the start. Team 2: 12 (keys
    //          100..111) at x 56..62, z 2..6. Nobody falls. On the 30th tick after the first tick A belongs to team 2 the
    //          batch is let go.
    // The ai section: dispatch "commander" with 3, 200, 1, 600, 30000; with squads also the seven squad numbers.
    internal sealed class CommanderScene
    {
        internal enum Kind { Early, Idle, Lose }

        internal const string Five = "\"dispatch\": \"commander\", \"commanderMaxAttackTargets\": 3, \"commanderEnemyWeight\": 200, "
            + "\"commanderDistanceWeightPerMetre\": 1, \"commanderStickiness\": 600, \"commanderThreatRadiusMm\": 30000";
        internal const string Seven = "\"squadMinSize\": 4, \"squadMaxSize\": 9, \"squadSkirmisherPermille\": 300, \"squadRecruitRadiusMm\": 30000, "
            + "\"squadFormTicks\": 15, \"squadSpreadMinMm\": 1500, \"squadSpreadMaxMm\": 6000";

        internal readonly M6World W;
        internal readonly Commander Commander;
        internal readonly CommanderChecker Checker;
        internal readonly Kind Board;
        internal readonly int BatchFirst, BatchCount;
        // The tick the batch was let go (-1 before), the first tick A belonged to team 2 (lose; -1 before).
        internal long ReleasedAt { get; private set; } = -1;
        internal long LostAt { get; private set; } = -1;
        private readonly Fall fall;
        private readonly ComponentType<SquadStanding> standing;
        private readonly ComponentType<Position> positions;
        private readonly InterimLocomotion walk;
        private readonly HashSet<ulong> held = new HashSet<ulong>();
        private long firstNear = -1;

        internal static ContentPack Pack(string ai) => ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
        {
            ["commander-scene"] = "{ \"id\": \"commander-scene\", \"extends\": \"rwr\", \"ai\": { " + ai + " } }"
        }, FileContentSource.Rwr()), "commander-scene");

        internal CommanderScene(Kind kind, bool squads)
        {
            Board = kind;
            bool lose = kind == Kind.Lose, idle = kind == Kind.Idle;
            int bx = lose ? 300 : 100, main = lose ? 4 : 40;
            BatchFirst = lose ? 11 : 41; BatchCount = lose ? 10 : idle ? 12 : 0;
            var points = new[]
            {
                new ControlPointDef(1, SimVec3.Metres(20, 0, 20), SimFix.FromInt(6), 1),
                new ControlPointDef(2, SimVec3.Metres(bx, 0, 20), SimFix.FromInt(6), 2),
            };
            var units = new List<UnitSpawn>();
            for (int i = 0; i < main; i++) units.Add(M6World.U((ulong)i + 1, 1, 8 + i % 8 * 2, 12 + i / 8 * 2, weapon: -1, speed: 4000));
            for (int i = 0; i < 12; i++)
                units.Add(lose ? M6World.U((ulong)(100 + i), 2, 56 + i % 4 * 2, 2 + i / 4 * 2, weapon: -1, speed: 4000)
                    : M6World.U((ulong)(100 + i), 2, 106 + i % 4 * 2, 16 + i / 4 * 2, weapon: -1, speed: 4000));
            for (int i = 0; i < BatchCount; i++) units.Add(M6World.U((ulong)(BatchFirst + i), 1, lose ? 30 + 4 * i : 30 + 2 * i, 36, weapon: -1, speed: 4000));
            MatchPack match = M6World.Pack(points, ticks: 300, think: 30,
                bases: new[] { new SpawnAnchor(1, SimVec3.Metres(4, 0, 20)), new SpawnAnchor(2, SimVec3.Metres(bx + 16, 0, 20)) });
            WorldRecipe recipe = M6World.RecipeOf(units.ToArray(), match).WithRuleSet(MatchMode.TestNeverEndsId)
                .WithPack(Pack(squads ? Seven + ", " + Five : Five));
            if (BatchCount > 0) recipe = recipe.WithStandingOrders();
            W = new M6World(recipe, WcMapWriter.Write(Wp23.Make(bx + 20, 40, 0, 4 * SimFix.Scale, Array.Empty<(int, int, int, int)>(), 2 * SimFix.Scale, null)));
            fall = new Fall(W);
            W.Sim.RegisterGameplay("test.commander-scene-fall", fall, SimSystemPhase.Queries);
            Commander = W.System<Commander>();
            Checker = CommanderChecker.Attach(W);
            positions = W.Sim.Entities.Resolve<Position>();
            if (BatchCount > 0)
            {
                standing = W.Sim.Entities.Resolve<SquadStanding>();
                walk = W.System<InterimLocomotion>();
                for (int i = 0; i < BatchCount; i++) Hold((ulong)(BatchFirst + i));
            }
        }

        internal StableId Id(ulong key) => W.Sim.Entities.StableOf(W.Unit(key));
        // The point the unit with this spawn key remembers; -1 without a row.
        internal int Row(ulong key) => Commander.CommandOf(Id(key).Value);
        internal bool Alive(ulong key) => !W.Combat.IsDead(W.Unit(key));
        internal int Owner(int point) => W.Capture.Points[point].Owner;
        internal SimVec3 At(ulong key) => W.Sim.Entities.Read(positions, W.Unit(key)).Value;

        private void Hold(ulong key)
        {
            W.Sim.Entities.Set(standing, W.Unit(key), new SquadStanding(StandingKind.Hold)); held.Add(key);
        }

        private static double Metres(SimVec3 a, SimVec3 b)
        {
            double dx = (a.X.Raw - b.X.Raw) / (double)SimFix.Scale, dz = (a.Z.Raw - b.Z.Raw) / (double)SimFix.Scale;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        // One tick, then what the scene does between ticks (as the draft's probe did it, in the same order).
        internal void Step()
        {
            W.Step();
            long t = W.Sim.Clock.Tick;
            SimVec3 a = W.Recipe.Match.Points[0].Center;
            int near = 0;
            foreach (UnitSpawn spawn in W.Recipe.Units)
            {
                if (spawn.Team != 2) continue;
                EntityId e = W.Unit(spawn.Key);
                if (W.Combat.IsDead(e) || !W.Sim.Entities.TryGet(positions, e, out Position at)) continue;
                double d = Metres(at.Value, a);
                if (d <= 30) near++;
                if (Board == Kind.Idle && d <= 20 && !held.Contains(spawn.Key)) { walk.Stop(e); Hold(spawn.Key); }
            }
            if (near > 0 && firstNear < 0) firstNear = t;
            if (Board == Kind.Idle)
            {
                if (ReleasedAt < 0 && Commander.EnemiesNear(1, 0) >= 4) { Release(t); fall.At = t + 200; }
            }
            else if (Board == Kind.Lose)
            {
                if (LostAt < 0 && Owner(0) == 2) LostAt = t;
                if (LostAt > 0 && ReleasedAt < 0 && t == LostAt + 30) Release(t);
            }
            else if (fall.At < 0 && firstNear >= 0 && (near >= 8 || t - firstNear >= 600)) fall.At = t + 300;
        }

        private void Release(long t)
        {
            ReleasedAt = t;
            for (int i = 0; i < BatchCount; i++)
            {
                ulong key = (ulong)(BatchFirst + i);
                W.Sim.Entities.Remove(standing, W.Unit(key)); held.Remove(key);
            }
        }

        // Team-1 units (spawn keys) whose own row is the point.
        internal int RowsAt(int point)
        {
            int count = 0;
            foreach (UnitSpawn spawn in W.Recipe.Units)
                if (spawn.Team == 1 && Alive(spawn.Key) && Row(spawn.Key) == point) count++;
            return count;
        }

        // Every team-2 unit takes full damage at tick At (set while the world runs).
        private sealed class Fall : IRetainedGameplaySystem
        {
            private readonly M6World w;
            internal long At = -1;
            internal Fall(M6World w) { this.w = w; }
            public int Order => 26;
            public string StateTypeId => "test.commander-scene-fall";
            public int StateVersion => 1;
            public void Step(SimContext c)
            {
                if (c.Clock.Tick != At) return;
                foreach (UnitSpawn spawn in w.Recipe.Units)
                {
                    if (spawn.Team != 2) continue;
                    StableId id = w.Sim.Entities.StableOf(w.Unit(spawn.Key));
                    c.Damage(w.Sim.GameplayTarget(id), new GameplayApplyKey(id, (ulong)At, 0, id, (ulong)At, 0), 1, w.Recipe.Combat.MaxHealthRaw);
                }
            }
            public void SaveState(SimStateWriter writer) { }
            public void LoadState(SimStateReader reader) { }
            public void WriteState(StateHash hash) { }
        }
    }
}
