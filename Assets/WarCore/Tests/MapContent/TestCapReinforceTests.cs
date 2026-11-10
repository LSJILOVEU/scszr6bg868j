using NUnit.Framework;
using WarCore.Battle;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class TestCapReinforceTests
    {
        [Test]
        public void DeadReserveDoesNotDeployAtDueTick()
        {
            var w = new M6World(M6World.Endless(M6World.RecipeOf(
                new[] { M6World.U(1, 1, reserve: true) },
                M6World.Pack(interval: 3, columns: 1, rows: 1))));
            w.Sim.RegisterGameplay("test.death", new M6Damage(w, 1, 1), SimSystemPhase.Queries);

            w.Step(2);
            Assert.That(w.Dead(1), Is.True);
            Assert.That(w.Sim.Clock.Tick, Is.EqualTo(2));
            Assert.That(w.Reinforce.Attempts(1), Is.EqualTo(0));
            w.Step();
            Assert.That(w.Sim.Clock.Tick, Is.EqualTo(3));
            Assert.That(w.Reinforce.Attempts(1), Is.EqualTo(1));
            Assert.That(w.Reinforce.Successes(1), Is.EqualTo(0));
            Assert.That(w.Reinforce.Deployed.Length, Is.EqualTo(0));
            Assert.That(w.Reinforce.NextTick(1), Is.EqualTo(6));
            Assert.That(w.Has<Position>(1), Is.False);
        }

        [Test]
        public void DeadReserveDoesNotBlockLivingReserve()
        {
            ulong dead = StableEntityKey.Placed(1, 1).Id.Value < StableEntityKey.Placed(1, 2).Id.Value ? 1UL : 2UL;
            ulong living = 3UL - dead;
            var w = new M6World(M6World.Endless(M6World.RecipeOf(
                new[] { M6World.U(1, 1, reserve: true), M6World.U(2, 1, reserve: true) },
                M6World.Pack(interval: 3, columns: 1, rows: 1))));
            w.Sim.RegisterGameplay("test.death", new M6Damage(w, 1, dead), SimSystemPhase.Queries);

            w.Step(2);
            Assert.That(w.Dead(dead), Is.True);
            Assert.That(w.Reinforce.Attempts(1), Is.EqualTo(0));
            w.Step();
            Assert.That(w.Sim.Clock.Tick, Is.EqualTo(3));
            Assert.That(w.Reinforce.Attempts(1), Is.EqualTo(1));
            Assert.That(w.Reinforce.Successes(1), Is.EqualTo(1));
            Assert.That(w.Reinforce.Deployed.Length, Is.EqualTo(1));
            var deployed = w.Reinforce.Deployed[0];
            Assert.That(deployed.Tick, Is.EqualTo(3));
            Assert.That(deployed.Team, Is.EqualTo(1));
            Assert.That(deployed.Unit.Value, Is.EqualTo(StableEntityKey.Placed(1, living).Id.Value));
            Assert.That(deployed.Point.X.Raw, Is.EqualTo(6 * 65536));
            Assert.That(deployed.Point.Z.Raw, Is.EqualTo(20 * 65536));
            Assert.That(w.Reinforce.NextTick(1), Is.EqualTo(6));
            Assert.That(w.Has<Position>(dead), Is.False);
            Assert.That(w.Has<Position>(living), Is.True);
        }

        [Test]
        public void OwnedPointWithoutSpawnDoesNotProvideAnchor()
        {
            var point = new ControlPointDef(7, M6World.P(40), SimFix.FromInt(6), 1, false, M6World.P(40));
            var pack = M6World.Pack(new[] { point }, interval: 1, columns: 1, rows: 1,
                bases: new[] { new SpawnAnchor(2, M6World.P(74)) });
            var w = new M6World(M6World.Endless(M6World.RecipeOf(
                new[] { M6World.U(1, 1, reserve: true) }, pack)));

            w.Step();
            Assert.That(w.Sim.Clock.Tick, Is.EqualTo(1));
            Assert.That(w.Capture.Points[0].Owner, Is.EqualTo(1));
            Assert.That(w.Reinforce.Attempts(1), Is.EqualTo(1));
            Assert.That(w.Reinforce.Successes(1), Is.EqualTo(0));
            Assert.That(w.Reinforce.Deployed.Length, Is.EqualTo(0));
            Assert.That(w.Reinforce.NextTick(1), Is.EqualTo(2));
            Assert.That(w.Has<Position>(1), Is.False);
        }
    }
}
