using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class M6RunTests
    {
        internal static byte[] NarrowMap() => WcMapWriter.Write(M3TestWorld.Boxes(80, 40,
            (28, 0, 1, 19), (28, 21, 1, 19),
            (51, 0, 1, 19), (51, 21, 1, 19)));

        [Test]
        public void LegacyM5Tick100HashAndEndTick141Fixed()
        {
            var session = SimSession.Create(DemoRecipes.M5(), DemoRecipes.M2bMapBytes());
            int end = 0;
            for (int tick = 1; tick <= 141; tick++)
            {
                session.StepOnce();
                if (tick == 100) Assert.That(session.StateHash, Is.EqualTo(0x8C98B0A3D3C85A24UL));
                int one = 0, two = 0;
                foreach (var unit in session.Current.Entities)
                    if (unit.Alive) { if (unit.Team == 1) one++; if (unit.Team == 2) two++; }
                if (end == 0 && (one == 0 || two == 0)) end = tick;
            }
            Assert.That(end, Is.EqualTo(141)); // WP-17: was 132 before ray blocking and spread
        }

        [Test]
        public void Acceptance_PLACEHOLDER_Seed41_ReplayReverseCreationEveryTick()
        {
            var recipe = DemoRecipes.M6(); var map = DemoRecipes.M6MapBytes();
            var a = new M6World(recipe, map); var b = new M6World(recipe, map); var reverse = new M6World(recipe, map, true);
            Assert.That(a.Unit(1), Is.Not.EqualTo(reverse.Unit(1)));
            Assert.That(a.Hash, Is.EqualTo(reverse.Hash));
            ulong tick600 = 0;
            while (a.Match.Winner == 0 && a.Sim.Clock.Tick < 9000)
            {
                a.Step(); b.Step(); reverse.Step();
                Assert.That(b.Hash, Is.EqualTo(a.Hash), "repeat tick=" + a.Sim.Clock.Tick);
                Assert.That(reverse.Hash, Is.EqualTo(a.Hash), "reverse tick=" + a.Sim.Clock.Tick);
                Assert.That(reverse.Reinforce.Deployed.ToArray(), Is.EqualTo(a.Reinforce.Deployed.ToArray()));
                Assert.That(reverse.Capture.Changes.ToArray(), Is.EqualTo(a.Capture.Changes.ToArray()));
                if (a.Sim.Clock.Tick == 600) { tick600 = a.Hash; TestContext.Out.WriteLine($"M6 tick600={a.Hash:X16}"); }
            }
            TestContext.Out.WriteLine($"M6 seed41 end={a.Match.EndTick} winner={a.Match.Winner} reason={a.Match.EndReason} hash={a.Hash:X16}");
            Assert.That(a.Match.Winner, Is.InRange(1, 2)); Assert.That(a.Match.EndTick, Is.InRange(1, 9000));
            // Pinned recorded outcome, re-recorded by WP-21 (PLACEHOLDER numbers): a behaviour change in any M6 system moves one of these.
            Assert.That(tick600, Is.EqualTo(0x18B64BDC0A0113ABUL), "tick 600 state hash");
            Assert.That(a.Match.EndTick, Is.EqualTo(1172)); Assert.That(a.Match.Winner, Is.EqualTo(1));
            Assert.That(a.Match.EndReason, Is.EqualTo(WarCore.Battle.MatchEndReason.AllPoints));
            Assert.That(a.Hash, Is.EqualTo(0x1CCED776D43DD0D4UL), "end state hash");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Acceptance_PLACEHOLDER_TwentySeedsAndStuckWindows(bool narrow)
        {
            byte[] map = narrow ? NarrowMap() : DemoRecipes.M6MapBytes();
            int decided = 0, draws = 0, total = 0, max = 0; var reasons = new int[5];
            for (ulong seed = 41; seed <= 60; seed++)
            {
                var demo = DemoRecipes.M6();
                var recipe = new WorldRecipe(seed, demo.Mover, demo.Units.ToArray(), demo.Script.ToArray()).WithCombat(demo.Combat).WithMatch(demo.Match).WithCrowdPass(demo.CrowdPass);
                var w = new M6World(recipe, map); var metric = new StuckMeter(w);
                metric.Sample();
                while (w.Match.Winner == 0 && w.Sim.Clock.Tick < 9000) { w.Step(); metric.Sample(); }
                int stuck = metric.StuckCount; total += stuck; max = Math.Max(max, stuck);
                if (w.Match.Winner == 1 || w.Match.Winner == 2) decided++; if (w.Match.Winner == -1) draws++;
                reasons[(int)w.Match.EndReason]++;
                TestContext.Out.WriteLine($"RUN narrow={narrow} seed={seed} end={w.Match.EndTick} winner={w.Match.Winner} reason={w.Match.EndReason} stuck={stuck} longest={metric.Longest.Max()} perUnit={string.Join(",", metric.Longest)}");
                Assert.That(w.Match.Winner, Is.Not.Zero); Assert.That(w.Match.EndTick, Is.InRange(1, 9000));
                // The 2 m comparison map only counts; on the M6 map every one of the 20 seeds must have no stuck unit.
                if (!narrow) Assert.That(stuck, Is.Zero, "stuck units, seed " + seed + "; report and stop, do not tune parameters.");
            }
            TestContext.Out.WriteLine($"SUMMARY narrow={narrow} decided={decided} draws={draws} reasons={string.Join(",", reasons)} stuckSum={total} stuckMax={max}");
        }

        // Independent distance-only observation: all enemies within sight count, even through a wall.
        private sealed class StuckMeter
        {
            private readonly M6World world;
            private readonly GuardSlots guardSlots;
            private readonly UnitSpawn[] units;
            private readonly SimVec3[,] history;
            private readonly SimVec3[] target;
            private readonly int[] continuous, lastProgress;
            private readonly SimWide[] best;
            private readonly bool[] active, stuck;
            internal readonly int[] Longest;
            internal int StuckCount => stuck.Count(v => v);
            internal StuckMeter(M6World w)
            {
                world = w; units = w.Recipe.Units.ToArray(); int n = units.Length; guardSlots = new GuardSlots(w.Recipe.Match.Points);
                history = new SimVec3[n, 301]; target = new SimVec3[n]; continuous = new int[n];
                lastProgress = new int[n]; best = new SimWide[n]; active = new bool[n]; stuck = new bool[n]; Longest = new int[n];
            }
            internal void Sample()
            {
                int tick = (int)world.Sim.Clock.Tick;
                for (int i = 0; i < units.Length; i++)
                {
                    ulong key = units[i].Key;
                    bool valid = world.Has<Position>(key) && !world.Dead(key) && world.Has<MoveIntent>(key) && world.Read<MoveIntent>(key).Active;
                    if (!valid) { active[i] = false; continuous[i] = 0; continue; }
                    var p = world.Read<Position>(key).Value; var goal = world.Read<MoveIntent>(key).Target;
                    // The progress metric retains its original point-center definition.
                    // Only the command target changed to a guard position; stuck windows still compare that exact target.
                    var center = goal;
                    for (int pi = 0; pi < world.Recipe.Match.Points.Length; pi++)
                        if (guardSlots.TryFind(goal, pi, out _)) { center = world.Recipe.Match.Points[pi].Center; break; }
                    var distance = CombatSystem.Horizontal(p - center).LengthSquared;
                    if (!active[i] || !target[i].Equals(goal))
                    { best[i] = distance; lastProgress[i] = tick; continuous[i] = 0; }
                    else if (distance < best[i]) { best[i] = distance; lastProgress[i] = tick; }
                    Longest[i] = Math.Max(Longest[i], tick - lastProgress[i]); active[i] = true; target[i] = goal;
                    bool enemy = false;
                    for (int j = 0; j < units.Length && !enemy; j++)
                        if (units[j].Team != 0 && units[j].Team != units[i].Team && world.Has<Position>(units[j].Key) && !world.Dead(units[j].Key))
                            enemy = CombatSystem.Horizontal(p - world.Read<Position>(units[j].Key).Value).LengthSquared <=
                                new SimVec3(world.Recipe.Match.Sight, default, default).LengthSquared;
                    if (enemy) continuous[i] = 0; else continuous[i]++;
                    history[i, tick % 301] = p;
                    if (continuous[i] >= 301 && CombatSystem.Horizontal(p - history[i, (tick - 300) % 301]).LengthSquared <
                        new SimVec3(SimFix.FromInt(1), default, default).LengthSquared)
                    {
                        if (!stuck[i] && world.Recipe.Seed == 41)
                        {
                            var start = history[i, (tick - 300) % 301];
                            TestContext.Out.WriteLine($"STUCK seed=41 key={key} window={tick - 300}..{tick} fromRaw={start.X.Raw},{start.Z.Raw} toRaw={p.X.Raw},{p.Z.Raw} goalRaw={goal.X.Raw},{goal.Z.Raw} displacement={(p - start).Length.Raw / 65536.0:F6}m continuous={continuous[i]}");
                        }
                        stuck[i] = true;
                    }
                }
            }
        }
    }
}
