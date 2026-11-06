using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // The motion seam (step length from a model) and the motion-limits component. Only the instant model exists, so a world
    // behaves as before; a stand-in model proves every mover asks the seam, and the component is absent unless opened.
    public sealed class MotionModelTests
    {
        private const long S = Wp23.S, Speed = 4000;
        private static readonly UnitSpawn[] Units = { Wp23.Unit(1, 1, 20500, 12500, Speed), Wp23.Unit(2, 2, 3500, 3500, 6000) };

        private static SimSession World(MoverKind mover, bool motion = false, bool hill = false, bool order = true)
        {
            var script = order ? new[] { BridgeCommand.MoveTo(1, 1, 30500, 12500) } : new BridgeCommand[0];
            var recipe = new WorldRecipe(41, mover, Units, script);
            if (motion) recipe = recipe.WithMotion();
            WcMap map = hill ? Wp23.Make(40, 24, 0, 6 * S, null, 4 * S, (i, j) => i * (int)(S / 16)) : Wp23.Make(40, 24, 0, 6 * S, null, 4 * S);
            if (hill) recipe = recipe.WithTerrainRules(Wp23.Pack());
            return SimSession.Create(recipe, WcMapWriter.Write(map));
        }

        private sealed class Scaled : IMotionModel
        {
            public readonly List<(EntityId unit, long wanted)> Calls = new List<(EntityId, long)>();
            private readonly long numerator, denominator;
            public Scaled(long numerator, long denominator) { this.numerator = numerator; this.denominator = denominator; }
            public SimFix StepSpeed(EntityId unit, SimFix wanted)
            {
                Calls.Add((unit, wanted.Raw));
                return SimFix.FromRaw(wanted.Raw * numerator / denominator);
            }
        }

        private static readonly (MoverKind mover, bool hill)[] Worlds =
        {
            (MoverKind.Interim, false), (MoverKind.InterimPath, false), (MoverKind.InterimCrowd, false),
            (MoverKind.InterimPath, true), (MoverKind.InterimCrowd, true),
        };

        private static long Travelled(SimSession s, int ticks)
        {
            long start = Wp23.At(s).X.Raw;
            for (int i = 0; i < ticks; i++) s.StepOnce();
            return Wp23.At(s).X.Raw - start;
        }

        [Test]
        public void EveryMoverAsksTheModel_WithTheUnitsOwnSpeed([ValueSource(nameof(Worlds))] (MoverKind mover, bool hill) w)
        {
            SimSession s = World(w.mover, hill: w.hill);
            var model = new Scaled(1, 1); M3TestWorld.Locomotion(s).BindMotion(model);
            for (int i = 0; i < 20; i++) s.StepOnce();
            Assert.That(model.Calls, Is.Not.Empty);
            Assert.That(model.Calls.ConvertAll(c => c.unit), Is.All.EqualTo(M3TestWorld.Unit(s, 1)), "only the ordered unit moves");
            Assert.That(model.Calls.ConvertAll(c => c.wanted), Is.All.EqualTo(Wp23.Step(Speed)));
        }

        [Test]
        public void AModelThatHalvesTheSpeed_HalvesTheDistance([ValueSource(nameof(Worlds))] (MoverKind mover, bool hill) w)
        {
            long full = Travelled(World(w.mover, hill: w.hill), 20);
            SimSession half = World(w.mover, hill: w.hill); M3TestWorld.Locomotion(half).BindMotion(new Scaled(1, 2));
            long slow = Travelled(half, 20);
            Assert.That(full, Is.GreaterThan(0));
            if (!w.hill) Assert.That(slow, Is.EqualTo(full / 2));
            else Assert.That(System.Math.Abs(slow * 2 - full), Is.LessThanOrEqualTo(40), "terrain rounds the scaled step");
        }

        [Test]
        public void TheInstantModel_IsTheDefaultAndChangesNothing([ValueSource(nameof(Worlds))] (MoverKind mover, bool hill) w)
        {
            SimSession plain = World(w.mover, hill: w.hill), bound = World(w.mover, hill: w.hill);
            M3TestWorld.Locomotion(bound).BindMotion(new InstantMotion());
            for (int i = 0; i < 40; i++)
            {
                plain.StepOnce(); bound.StepOnce();
                Assert.That(bound.StateHash, Is.EqualTo(plain.StateHash), "tick " + (i + 1));
            }
            Assert.That(Wp23.At(plain).X.Raw, Is.GreaterThan(SimFix.FromInt(20).Raw));
        }

        [Test]
        public void WithoutTheRecipeFlag_NoLimitsExist([Values(MoverKind.Interim, MoverKind.InterimCrowd)] MoverKind mover)
        {
            SimSession s = World(mover);
            Assert.Throws<System.InvalidOperationException>(() => M3TestWorld.Sim(s).Entities.Resolve<MotionLimits>());
            Assert.That(Encoding.ASCII.GetString(M3TestWorld.Sim(s).SaveRetainedSnapshot()), Does.Not.Contain(MotionLimits.ComponentId));
        }

        [Test]
        public void WithTheFlag_EachUnitStartsWithItsOwnSpeedAndNoOtherLimit()
        {
            SimSession s = World(MoverKind.InterimCrowd, motion: true);
            Assert.That(M3TestWorld.Component<MotionLimits>(s, 1), Is.EqualTo(new MotionLimits(SimFix.FromRaw(Wp23.Step(4000)), default, default, default)));
            Assert.That(M3TestWorld.Component<MotionLimits>(s, 2), Is.EqualTo(new MotionLimits(SimFix.FromRaw(Wp23.Step(6000)), default, default, default)));
            Assert.That(Encoding.ASCII.GetString(M3TestWorld.Sim(s).SaveRetainedSnapshot()), Does.Contain(MotionLimits.ComponentId));
        }

        [Test]
        public void OpeningTheComponent_DoesNotChangeAnyPosition([Values(MoverKind.Interim, MoverKind.InterimPath, MoverKind.InterimCrowd)] MoverKind mover)
        {
            SimSession plain = World(mover), opened = World(mover, motion: true);
            for (int i = 0; i < 150; i++)
            {
                plain.StepOnce(); opened.StepOnce();
                Assert.That((Wp23.At(opened, 1), Wp23.At(opened, 2)), Is.EqualTo((Wp23.At(plain, 1), Wp23.At(plain, 2))), "tick " + (i + 1));
            }
        }

        [Test]
        public void TheLimitsAreInTheHashAndSurviveASnapshot()
        {
            SimSession a = World(MoverKind.InterimCrowd, motion: true), b = World(MoverKind.InterimCrowd, motion: true);
            Assert.That(b.StateHash, Is.EqualTo(a.StateHash));
            Simulation(b).Entities.Set(Simulation(b).Entities.Resolve<MotionLimits>(), M3TestWorld.Unit(b, 2),
                new MotionLimits(SimFix.FromInt(1), SimFix.FromInt(2), SimFix.FromInt(3), SimFix.FromInt(4)));
            Assert.That(b.StateHash, Is.Not.EqualTo(a.StateHash));
            for (int field = 0; field < 4; field++)
            {
                SimFix one = SimFix.FromInt(7);
                SimSession d = World(MoverKind.InterimCrowd, motion: true);
                Simulation(d).Entities.Set(Simulation(d).Entities.Resolve<MotionLimits>(), M3TestWorld.Unit(d, 2),
                    new MotionLimits(field == 0 ? one : default, field == 1 ? one : default, field == 2 ? one : default, field == 3 ? one : default));
                Assert.That(d.StateHash, Is.Not.EqualTo(a.StateHash), "field " + field);
            }
            byte[] saved = Simulation(b).SaveRetainedSnapshot();
            SimSession c = World(MoverKind.InterimCrowd, motion: true);
            Simulation(c).RestoreRetainedSnapshot(saved);
            Assert.That(M3TestWorld.Component<MotionLimits>(c, 2), Is.EqualTo(new MotionLimits(SimFix.FromInt(1), SimFix.FromInt(2), SimFix.FromInt(3), SimFix.FromInt(4))));
            Assert.That(c.StateHash, Is.EqualTo(b.StateHash));
        }

        [Test]
        public void EveryFieldReachesTheLegacyStateHash()
        {
            ulong Hash(MotionLimits m) { var h = new StateHash(); m.WriteState(h); return h.Value; }
            SimFix one = SimFix.FromInt(7), zero = default;
            var seen = new HashSet<ulong> { Hash(new MotionLimits(zero, zero, zero, zero)) };
            Assert.That(seen.Add(Hash(new MotionLimits(one, zero, zero, zero))), Is.True, "MaxSpeed");
            Assert.That(seen.Add(Hash(new MotionLimits(zero, one, zero, zero))), Is.True, "Accel");
            Assert.That(seen.Add(Hash(new MotionLimits(zero, zero, one, zero))), Is.True, "Decel");
            Assert.That(seen.Add(Hash(new MotionLimits(zero, zero, zero, one))), Is.True, "MaxTurn");
        }

        [Test]
        public void NegativeLimitsAreRefused()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new MotionLimits(SimFix.FromRaw(-1), default, default, default));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new MotionLimits(default, default, default, SimFix.FromRaw(-1)));
        }

        // A unit in the way makes the crowd mover sidestep; the sidestep length also comes from the model.
        [Test]
        public void TheCrowdSidestepUsesTheModelsSpeedToo()
        {
            (long x, long z) Sidestep(IMotionModel model)
            {
                var units = new[] { Wp23.Unit(1, 1, 20500, 12500, Speed), Wp23.Unit(2, 2, 21000, 12500, Speed) };
                var recipe = new WorldRecipe(41, MoverKind.InterimCrowd, units, new[] { BridgeCommand.MoveTo(1, 1, 30500, 12500) });
                SimSession s = SimSession.Create(recipe, WcMapWriter.Write(Wp23.Make(40, 24, 0, 6 * S, null, 4 * S)));
                if (model != null) M3TestWorld.Locomotion(s).BindMotion(model);
                s.StepOnce();
                SimVec3 at = Wp23.At(s);
                return (at.X.Raw - SimFix.Ratio(20500, 1000).Raw, at.Z.Raw - SimFix.Ratio(12500, 1000).Raw);
            }
            var full = Sidestep(null); var half = Sidestep(new Scaled(1, 2));
            TestContext.Out.WriteLine($"full={full} half={half}");
            Assert.That(full.z, Is.Not.Zero, "the fixture must make the unit sidestep");
            Assert.That(half.z, Is.EqualTo(full.z / 2));
        }

        private static WarCore.Sim.Sim Simulation(SimSession s) => M3TestWorld.Sim(s);
    }
}
