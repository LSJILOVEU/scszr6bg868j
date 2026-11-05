using System;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class M4SnapshotTests
    {
        [Test]
        public void IndependentCreationOrders_Match601Hashes_AndRowMajorTargets()
        {
            var recipe = DemoRecipes.M4();
            var a = new M4TestWorld(M2DemoMap.Build(), recipe.Units.ToArray());
            var b = new M4TestWorld(M2DemoMap.Build(), recipe.Units.ToArray(), reverse: true);
            Assert.That(a.Unit(0), Is.Not.EqualTo(b.Unit(0)), "Physical allocation must differ.");
            for (int tick = 0; tick <= 600; tick++)
            {
                if (tick != 0)
                {
                    InputFrame input = tick == 1 ? M4TestWorld.Command(1, 44000, 22000) : tick == 2 ? M4TestWorld.Command(2, 56000, 22000) : default;
                    a.Sim.Step(input); b.Sim.Step(input);
                }
                Assert.That(b.Sim.StateHashRetained(), Is.EqualTo(a.Sim.StateHashRetained()), "tick=" + tick);
            }
            for (int i = 0; i < 20; i++) Assert.That(a.Point(i), Is.EqualTo(M4CrowdTests.Target(i)));
        }

        [Test]
        public void Snapshot150_FreshReverseWorld_MatchesThrough600()
        {
            var session = SimSession.Create(DemoRecipes.M4(), DemoRecipes.M2bMapBytes());
            for (int tick = 1; tick <= 150; tick++) session.StepOnce();
            var a = M3TestWorld.Sim(session);
            var b = new M4TestWorld(M2DemoMap.Build(), DemoRecipes.M4().Units.ToArray(), reverse: true);
            Assert.That(M3TestWorld.Component<MoveIntent>(session).Active, Is.True);
            byte[] snapshot = a.SaveRetainedSnapshot();
            b.Sim.RestoreRetainedSnapshot(snapshot);
            Assert.That(b.Sim.SaveRetainedSnapshot(), Is.EqualTo(snapshot));
            for (int tick = 151; tick <= 600; tick++)
            {
                a.Step(default); b.Sim.Step(default);
                Assert.That(b.Sim.StateHashRetained(), Is.EqualTo(a.StateHashRetained()), "tick=" + tick);
            }
            for (int i = 0; i < 20; i++) Assert.That(b.Point(i), Is.EqualTo(M4CrowdTests.Target(i)));
        }

        [Test]
        public void SnapshotDuringAcceptedTangent_FreshWorld_ThenNewCommand()
        {
            var units = new[] { new UnitSpawn(1, 0, 1, 4500, 4500, 6000), new UnitSpawn(2, 0, 2, 24500, 4500, 6000) };
            var map = M3TestWorld.Boxes(32, 12);
            var a = new M4TestWorld(map, units);
            a.Order(0, 24500, 4500); a.Order(1, 4500, 4500);
            int tangentTick = 0;
            for (int tick = 1; tick <= 150 && tangentTick == 0; tick++)
            {
                var before = a.Points();
                var straight = new SimVec3[2];
                for (int i = 0; i < 2; i++)
                {
                    var w = a.Sim.Entities.Get(a.Waypoints, a.Unit(i)).Point;
                    var delta = w - before[i]; var speed = a.Intent(i).SpeedPerTick;
                    straight[i] = M4CrowdTests.Squared(w, before[i]) <= (System.Numerics.BigInteger)speed.Raw * speed.Raw
                        ? w : before[i] + delta.Normalized * speed;
                }
                a.Sim.Step(default);
                for (int i = 0; i < 2; i++)
                    if (!a.Point(i).Equals(before[i]) && !a.Point(i).Equals(straight[i])) tangentTick = tick;
            }
            Assert.That(tangentTick, Is.InRange(1, 150), "Must save an observed accepted tangent, not merely an arbitrary active tick.");
            var b = new M4TestWorld(map, units, reverse: true);
            byte[] snapshot = a.Sim.SaveRetainedSnapshot(); b.Sim.RestoreRetainedSnapshot(snapshot);
            Assert.That(b.Sim.SaveRetainedSnapshot(), Is.EqualTo(snapshot));
            for (int tick = tangentTick + 1; tick <= 600; tick++)
            {
                var input = tick == tangentTick + 5 ? M4TestWorld.Command(1, 26500, 8500) : default;
                a.Sim.Step(input); b.Sim.Step(input);
                Assert.That(b.Sim.StateHashRetained(), Is.EqualTo(a.Sim.StateHashRetained()), "tick=" + tick);
            }
            Assert.That(b.Point(0), Is.EqualTo(M3TestWorld.Point(26500, 8500)));
            Assert.That(b.Point(1), Is.EqualTo(M3TestWorld.Point(4500, 4500)));
            TestContext.Out.WriteLine("snapshot accepted tangent tick=" + tangentTick + "; new command tick=" + (tangentTick + 5));
        }

        [TestCase(false, true, 2, 2)]
        [TestCase(true, false, 2, 2)]
        [TestCase(true, true, 0, 2)]
        [TestCase(true, true, 2, 0)]
        [TestCase(true, true, 1, 2)]
        [TestCase(true, true, 2, 1)]
        public void FullSnapshot_RejectsIncompatibleModesAndSpacing(bool fromCrowd, bool toCrowd, int fromSpacing, int toSpacing)
        {
            var units = new[] { new UnitSpawn(1, 0, 1, 1500, 1500, 6000) };
            var map = M3TestWorld.Boxes(12, 12);
            var a = new M4TestWorld(map, units, crowd: fromCrowd, spacing: fromSpacing);
            var b = new M4TestWorld(map, units, crowd: toCrowd, spacing: toSpacing);
            a.Sim.Step(M4TestWorld.Command(1, 8500, 8500));
            var snapshot = a.Sim.SaveRetainedSnapshot();
            Assert.Catch<InvalidOperationException>(() => b.Sim.RestoreRetainedSnapshot(snapshot));
        }
    }
}
