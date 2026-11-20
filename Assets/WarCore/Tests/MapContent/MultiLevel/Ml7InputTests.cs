using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // ML-7 acceptance 4 and V-f 4: layer numbers on the Sim's input frame. Without EnableInputLayers a frame with a layer is
    // refused at both entry points and the bytes are the original ones; with it the squad command's TargetLayer follows its
    // link and the AimLayer comes last.
    public sealed class Ml7InputTests
    {
        private static readonly SimVec3 Point = SimVec3.Metres(3, 1, 4);
        private static SquadCommand Move(int layer) => new SquadCommand(SquadCommandKind.MoveTo, 9, Point, default, layer);

        private static Simulation Layered()
        {
            Simulation sim = Simulation.CreateRetainedWorld(7);
            sim.EnableInputLayers();
            return sim;
        }

        [Test]
        public void LayersAreBounded()
        {
            Assert.That(Move(4096).TargetLayer, Is.EqualTo(4096));
            Assert.That(new InputFrame(default, default, aimLayer: 4096).AimLayer, Is.EqualTo(4096));
            Assert.Throws<ArgumentOutOfRangeException>(() => Move(4097));
            Assert.Throws<ArgumentOutOfRangeException>(() => Move(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new InputFrame(default, default, aimLayer: 4097));
            Assert.Throws<ArgumentOutOfRangeException>(() => new InputFrame(default, default, aimLayer: -1));
            Assert.That((Move(0).TargetLayer, new InputFrame(default, default).AimLayer), Is.EqualTo((0, 0)));
        }

        [Test]
        public void WithoutTheSwitchLayersAreRefused()
        {
            var aim = new InputFrame(default, default, aimLayer: 1);
            var squad = new InputFrame(default, default, squad: Move(1));
            Simulation retained = Simulation.CreateRetainedWorld(7);
            Assert.Throws<SimBoundaryException>(() => retained.Step(aim));
            Assert.Throws<SimBoundaryException>(() => retained.Step(squad));
            Assert.Throws<SimBoundaryException>(() => retained.StepStable(new StableInputFrame(aim)));
            Assert.Throws<SimBoundaryException>(() => retained.StepStable(new StableInputFrame(squad)));
            Assert.That(retained.Clock.Tick, Is.EqualTo(0), "nothing stepped");
            Assert.DoesNotThrow(() => retained.Step(new InputFrame(default, default, squad: Move(0))));

            var legacy = new Simulation(7, identityMode: IdentityMode.Legacy);
            Assert.Throws<SimBoundaryException>(() => legacy.Step(aim));
            Assert.Throws<SimBoundaryException>(() => legacy.EnableInputLayers(), "only a stable world carries layers");
        }

        [Test]
        public void TheSwitchIsSetBeforeWarmup()
        {
            Simulation sim = Layered();
            Assert.DoesNotThrow(() => sim.EnableInputLayers(), "twice is harmless");
            Assert.DoesNotThrow(() => sim.Step(new InputFrame(default, default, squad: Move(3), aimLayer: 2)));
            Assert.DoesNotThrow(() => sim.StepStable(new StableInputFrame(new InputFrame(default, default, aimLayer: 2))));
            Simulation late = Simulation.CreateRetainedWorld(7);
            late.CompleteWarmup();
            Assert.Throws<InvalidOperationException>(() => late.EnableInputLayers());
        }

        private static byte[] Save(StableInputFrame frame, bool layers)
        {
            var writer = new SimStateWriter();
            frame.Save(writer, layers);
            return writer.ToArray();
        }

        [Test]
        public void LayeredBytesAppendTheTargetLayerThenTheAimLayer()
        {
            var withSquad = new StableInputFrame(new InputFrame(SimVec3.Metres(1, 0, 0), Point, true, squad: Move(3), aimLayer: 2));
            var expected = new SimStateWriter();
            withSquad.Save(expected, false);
            expected.Int32(3); expected.Int32(2);
            Assert.That(Save(withSquad, true), Is.EqualTo(expected.ToArray()));

            var aimOnly = new StableInputFrame(new InputFrame(default, Point, aimLayer: 5));
            expected = new SimStateWriter();
            aimOnly.Save(expected, false);
            expected.Int32(5);
            Assert.That(Save(aimOnly, true), Is.EqualTo(expected.ToArray()));

            StableInputFrame back = StableInputFrame.Read(new SimStateReader(Save(withSquad, true)), true);
            Assert.That((back.Payload.Squad.Value.TargetLayer, back.Payload.AimLayer, back.Payload.Squad.Value.TargetPoint), Is.EqualTo((3, 2, Point)));
            back = StableInputFrame.Read(new SimStateReader(Save(aimOnly, true)), true);
            Assert.That((back.Payload.Squad.HasValue, back.Payload.AimLayer), Is.EqualTo((false, 5)));

            var plain = new StableInputFrame(new InputFrame(default, Point, squad: Move(0)));
            back = StableInputFrame.Read(new SimStateReader(Save(plain, false)), false);
            Assert.That((back.Payload.Squad.Value.TargetLayer, back.Payload.AimLayer), Is.EqualTo((0, 0)));
            Assert.That(Save(plain, false), Is.EqualTo(Save(plain, false)), "the same input, the same bytes");

            var bad = new SimStateWriter();
            aimOnly.Save(bad, false);
            bad.Int32(4097);
            Assert.Throws<SimSnapshotException>(() => StableInputFrame.Read(new SimStateReader(bad.ToArray()), true));
        }

        [Test]
        public void TheLayersSurviveASnapshot()
        {
            Simulation a = Layered();
            a.Step(new InputFrame(default, Point, squad: Move(3), aimLayer: 2));
            byte[] snapshot = a.SaveRetainedSnapshot();
            Simulation b = Layered();
            b.RestoreRetainedSnapshot(snapshot);
            Assert.That(b.SaveRetainedSnapshot(), Is.EqualTo(snapshot));
            Assert.That(b.StateHashRetained(), Is.EqualTo(a.StateHashRetained()));
        }

        // V-f 4: a layer is part of the tick's hash: only that tick differs.
        [TestCase(true)]
        [TestCase(false)]
        public void ALayerChangesItsTicksHash(bool squad)
        {
            Simulation a = Layered(), b = Layered();
            var ha = new ulong[8];
            var hb = new ulong[8];
            for (int tick = 0; tick < 8; tick++)
            {
                InputFrame Frame(int layer) => tick != 4 ? default : squad
                    ? new InputFrame(default, Point, squad: Move(layer))
                    : new InputFrame(default, Point, aimLayer: layer);
                a.Step(Frame(0)); b.Step(Frame(1));
                ha[tick] = a.StateHashRetained(); hb[tick] = b.StateHashRetained();
            }
            Assert.That(ha.Take(4), Is.EqualTo(hb.Take(4)), "before");
            Assert.That(ha[4], Is.Not.EqualTo(hb[4]), "the tick with the layer");
            Assert.That(ha.Skip(5), Is.EqualTo(hb.Skip(5)), "after: the frame is only the current tick's");
        }
    }
}
