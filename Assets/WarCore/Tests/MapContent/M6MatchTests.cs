using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class M6MatchTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void Bridge_EndFrameStaysFixedAfterRepeatedAdvance(bool endWithAdvance)
        {
            var w = new M6World(M6World.RecipeOf(new[] {
                M6World.U(1, 1, 5, speed: 6000), M6World.U(2, 2, 70)
            }, M6World.Pack(max: 2)));
            var session = w.Session;
            session.Enqueue(BridgeCommand.MoveTo(1, 1, 20000, 20000));
            Assert.That(session.Advance(40000), Is.EqualTo(1));
            var before = session.Current.Entities[0];
            if (endWithAdvance) Assert.That(session.Advance(40000), Is.EqualTo(1));
            else session.StepOnce();
            var final = session.Current;
            Assert.That(final.Entities[0].X, Is.Not.EqualTo(before.X), "The unit must move on the final tick.");
            Assert.That(session.Winner, Is.EqualTo(-1));
            Assert.That(session.Tick, Is.EqualTo(2));
            Assert.That(session.EndTick, Is.EqualTo(2));
            var entities = final.Entities.ToArray();
            void CheckFrozen()
            {
                Assert.That(session.Current, Is.SameAs(final));
                Assert.That(session.Previous, Is.SameAs(final));
                Assert.That(session.Current.Entities.ToArray(), Is.EqualTo(entities));
                Assert.That(session.Previous.Entities.ToArray(), Is.EqualTo(entities));
                Assert.That(FrameView.Interpolate(session.Previous, session.Current, session.Alpha).Entities.ToArray(), Is.EqualTo(entities));
                Assert.That(session.Alpha, Is.Zero);
                Assert.That(session.Tick, Is.EqualTo(2));
                Assert.That(session.EndTick, Is.EqualTo(2));
            }
            CheckFrozen();
            foreach (long elapsed in new long[] { 0, 1, 16667, 40000, 999999, 1000000, long.MaxValue })
            {
                Assert.That(session.Advance(elapsed), Is.Zero);
                CheckFrozen();
            }
            session.StepOnce();
            CheckFrozen();
            Assert.Throws<ArgumentOutOfRangeException>(() => session.Advance(-1));
        }

        [TestCase(1, 1, MatchEndReason.AllPoints)]
        [TestCase(2, 1, MatchEndReason.Exhausted)]
        [TestCase(3, -1, MatchEndReason.Draw)]
        [TestCase(4, 2, MatchEndReason.Time)]
        [TestCase(5, 1, MatchEndReason.Time)]
        [TestCase(6, -1, MatchEndReason.Draw)]
        public void D6_PLACEHOLDER_AllEndingsAndLatch(int scenario, int winner, MatchEndReason reason)
        {
            var points = new[] { new ControlPointDef(1, M6World.P(30), SimFix.FromInt(1), scenario == 1 ? 1 : scenario == 4 ? 2 : 0),
                new ControlPointDef(2, M6World.P(50), SimFix.FromInt(1), scenario == 1 ? 1 : 0) };
            UnitSpawn[] units = scenario == 2 ? new[] { M6World.U(1, 1) } : scenario == 3 ? new[] { M6World.U(1, 0) } :
                scenario == 5 ? new[] { M6World.U(1, 1), M6World.U(2, 2), M6World.U(3, 1, reserve: true) } : new[] { M6World.U(1, 1), M6World.U(2, 2) };
            var w = new M6World(M6World.RecipeOf(units, M6World.Pack(points, max: 2)));
            // At most two ticks; the match may already be over after the first (Sim refuses a step after the end).
            for (int i = 0; i < 2 && !w.Sim.IsFinished; i++) w.Step();
            long end = w.Match.EndTick;
            Assert.That(w.Match.Winner, Is.EqualTo(winner)); Assert.That(w.Match.EndReason, Is.EqualTo(reason));
            foreach (var spawn in units) w.Set(spawn.Key, new WarCore.Combat.Combatant(3 - Math.Max(1, spawn.Team), -1, 0, 0));
            // Latch: the match is over, so Sim refuses every further step (ten times, as the old test stepped ten ticks),
            // and the result stays exactly as it was.
            for (int i = 0; i < 10; i++)
            {
                Assert.Throws<InvalidOperationException>(() => w.Step());
                Assert.That(w.Match.Winner, Is.EqualTo(winner)); Assert.That(w.Match.EndTick, Is.EqualTo(end)); Assert.That(w.Match.EndReason, Is.EqualTo(reason));
                Assert.That(w.Sim.Clock.Tick, Is.EqualTo(end));
            }
            // Sim no longer lets the judge run again, so the latch itself is checked on the judge: ten more evaluations on later
            // ticks, over a world whose units have all changed sides, leave the result untouched.
            for (int i = 1; i <= 10; i++)
            {
                w.Mode.Evaluate(end + i);
                Assert.That(w.Match.Winner, Is.EqualTo(winner)); Assert.That(w.Match.EndTick, Is.EqualTo(end)); Assert.That(w.Match.EndReason, Is.EqualTo(reason));
            }
        }
        [Test]
        public void D6_PLACEHOLDER_SameTickMutualDeathIsDrawOnNextSample()
        {
            var w = new M6World(M6World.RecipeOf(new[] { M6World.U(1, 1, 5), M6World.U(2, 2, 70) }));
            w.Sim.RegisterGameplay("test.death", new M6Damage(w, 1, 1, 2), SimSystemPhase.Queries);
            w.Step(); Assert.That(w.Match.Winner, Is.Zero); w.Step();
            Assert.That(w.Match.Winner, Is.EqualTo(-1)); Assert.That(w.Match.EndTick, Is.EqualTo(2));
        }
        [Test]
        public void Config_PLACEHOLDER_ValidatesAndFreezesAllInputs()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => M6World.Pack(num: 1, den: 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => M6World.Pack(den: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => M6World.Pack(ticks: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => M6World.Pack(interval: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => M6World.Pack(think: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => M6World.Pack(max: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => M6World.Pack(mode: InterruptMode.Decay, decay: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ControlPointDef(1, default, default));
            Assert.Throws<ArgumentException>(() => M6World.Pack(bases: Array.Empty<SpawnAnchor>()));
            Assert.Throws<ArgumentOutOfRangeException>(() => M6World.Pack(columns: 0));
            var points = new[] { new ControlPointDef(4, M6World.P(20), SimFix.FromInt(2)) };
            var bases = new[] { new SpawnAnchor(1, M6World.P(5)) };
            var pack = M6World.Pack(points, bases: bases); ulong hash = pack.PolicyDigest;
            points[0] = new ControlPointDef(5, M6World.P(40), SimFix.FromInt(3)); bases[0] = new SpawnAnchor(2, M6World.P(70));
            Assert.That(pack.Points[0].Id, Is.EqualTo(4)); Assert.That(pack.Bases[0].Team, Is.EqualTo(1)); Assert.That(pack.PolicyDigest, Is.EqualTo(hash));
        }
        [Test]
        public void Bridge_PLACEHOLDER_CopyOrderFrameAndSessionEnd()
        {
            var raw = new WorldRecipe(41, MoverKind.InterimCrowd, new[] { M6World.U(1, 1), M6World.U(2, 2, 70) }, Array.Empty<BridgeCommand>());
            var combat = RwrPack.CreateM6(); var pack = M6World.Pack();
            var a = raw.WithCombat(combat).WithMatch(pack); var b = raw.WithMatch(pack).WithCombat(combat);
            Assert.That(a.Combat, Is.SameAs(combat)); Assert.That(b.Match, Is.SameAs(pack)); Assert.That(raw.Combat, Is.Null);
            var x = new M6World(a); var y = new M6World(b);
            x.Session.StepOnce(); y.Session.StepOnce(); Assert.That(x.Hash, Is.EqualTo(y.Hash));
            var frame = x.Session.Current; var mix = FrameView.Interpolate(x.Session.Previous, frame, 0.5f);
            Assert.That(mix.Points.ToArray(), Is.EqualTo(frame.Points.ToArray())); Assert.That(mix.Match, Is.EqualTo(frame.Match));
            x.Session.StepOnce(); x.Session.StepOnce(); Assert.That(x.Session.Winner, Is.EqualTo(1)); Assert.That(x.Session.EndTick, Is.EqualTo(3));
            Assert.That(x.Session.Current.Match.AliveOne, Is.EqualTo(1));
            Assert.That(x.Session.Current.Match.EndReason, Is.EqualTo((int)MatchEndReason.AllPoints));
            x.Session.StepOnce(); Assert.That(x.Session.Tick, Is.EqualTo(3));
            // A direct Sim.Step after the end is refused (the Session guard above is the soft path).
            Assert.Throws<InvalidOperationException>(() => x.Step()); Assert.That(x.Sim.Clock.Tick, Is.EqualTo(3));
            Assert.That(frame.Points[0].Progress, Is.EqualTo(1f / 3));
        }
    }
}
