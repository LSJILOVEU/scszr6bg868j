using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // BASE-0 V18-V20: the action bits of the input frame. Without Sim.EnableInputActions a frame with bits is refused and every
    // byte is the original one; with it each saved and hashed input ends with four bytes, after the layer numbers.
    public sealed class Base0InputActionsTests
    {
        private static readonly SimVec3 Point = SimVec3.Metres(3, 1, 4);

        [Test]
        public void V18_Helpers()
        {
            Assert.That(InputActions.StanceRequest(0), Is.EqualTo(-1));
            Assert.That(InputActions.WithStance(0, 0), Is.EqualTo(1u));
            Assert.That(InputActions.StanceRequest(1), Is.EqualTo(0));
            Assert.That(InputActions.WithStance(0, 7), Is.EqualTo(8u));
            Assert.That(InputActions.StanceRequest(8), Is.EqualTo(7));
            Assert.That(InputActions.WithStance(InputActions.Reload, 2), Is.EqualTo(0x13u));
            Assert.That(InputActions.WithStance(0x18, 2), Is.EqualTo(0x13u), "replaces the old stance");
            Assert.That(InputActions.StanceRequest(0x70), Is.EqualTo(-1), "the key bits are not a stance");
            Assert.That(InputActions.IsValid(0x78), Is.True, "three keys and stance 8");
            Assert.That(InputActions.IsValid(0), Is.True);
            Assert.That(InputActions.IsValid(0x7F), Is.False, "stance bits 15");
            Assert.That(InputActions.IsValid(0x80), Is.False);
            Assert.That(InputActions.IsValid(0x100), Is.False);
            Assert.That(InputActions.IsValid(0xF00), Is.False);
            Assert.That(InputActions.IsValid(0x80000000u), Is.False);
            Assert.That(InputActions.IsValid(9), Is.False, "stance bits 9");
            Assert.That(InputActions.IsValid(8), Is.True);
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => InputActions.WithStance(0, 8)).ParamName, Is.EqualTo("level"));
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => InputActions.WithStance(0, -1)).ParamName, Is.EqualTo("level"));
            Assert.That((InputActions.StanceMask, InputActions.Reload, InputActions.Interact, InputActions.GiveUp, InputActions.ValidMask),
                Is.EqualTo((0xFu, 0x10u, 0x20u, 0x40u, 0x7Fu)));
        }

        [Test]
        public void V18_TheFrameRefusesInvalidBits()
        {
            Assert.That(new InputFrame(default, default).Actions, Is.EqualTo(0u));
            Assert.That(new InputFrame(default, default, actions: 0x78).Actions, Is.EqualTo(0x78u));
            foreach (uint bad in new uint[] { 0x7F, 0x80, 0x100, 9 })
                Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => new InputFrame(default, default, actions: bad)).ParamName,
                    Is.EqualTo("actions"), bad.ToString("X"));
        }

        private static Simulation World(bool actions, bool layers = false)
        {
            Simulation sim = Simulation.CreateRetainedWorld(7);
            if (layers) sim.EnableInputLayers();
            if (actions) sim.EnableInputActions();
            return sim;
        }

        private static byte[] Save(StableInputFrame frame, bool layers, bool actions)
        {
            var writer = new SimStateWriter();
            frame.Save(writer, layers, actions);
            return writer.ToArray();
        }

        private static byte[] Concat(byte[] head, params int[] tail)
        {
            var writer = new SimStateWriter();
            foreach (int value in tail)
                writer.Int32(value);
            return head.Concat(writer.ToArray()).ToArray();
        }

        // Matrix row 1 and the hash of a world without the switch: a locked 300-tick baseline keeps every hash.
        [Test]
        public void V19_Row1_AnExistingWorldKeepsItsLockedHashes()
        {
            ulong[] hashes = F5BaselineLockTests.Run(out _);
            Assert.That(hashes, Is.EqualTo(F5BaselineData.Hashes));
        }

        // Row 2: a world without the switch and zero bits writes the original bytes.
        [Test]
        public void V19_Row2_WithoutTheSwitchZeroBitsKeepTheOriginalBytes()
        {
            var frame = new StableInputFrame(new InputFrame(SimVec3.Metres(1, 0, 0), Point, true, squad: new SquadCommand(SquadCommandKind.Hold, 2, Point, default)));
            Assert.That(Save(frame, false, false), Is.EqualTo(Save(frame, false, true).Take(Save(frame, false, true).Length - 4).ToArray()));
            var plain = new SimStateWriter();
            frame.Save(plain, false);
            Assert.That(Save(frame, false, false), Is.EqualTo(plain.ToArray()), "the old two-argument call");
            Simulation a = World(false), b = World(false);
            for (int tick = 0; tick < 6; tick++)
            {
                a.Step(new InputFrame(default, Point, tick == 3));
                b.StepStable(new StableInputFrame(new InputFrame(default, Point, tick == 3, actions: 0)));
            }
            Assert.That(b.SaveRetainedSnapshot(), Is.EqualTo(a.SaveRetainedSnapshot()));
            Assert.That(b.StateHashRetained(), Is.EqualTo(a.StateHashRetained()));
        }

        // Row 3: without the switch non-zero bits are refused at both entry points, in a retained and in a legacy world.
        [Test]
        public void V19_Row3_WithoutTheSwitchBitsAreRefused()
        {
            var frame = new InputFrame(default, default, actions: InputActions.Reload);
            Simulation retained = World(false);
            var error = Assert.Throws<SimBoundaryException>(() => retained.Step(frame));
            Assert.That(error.Message, Is.EqualTo("This world's input carries no action bits."));
            Assert.Throws<SimBoundaryException>(() => retained.StepStable(new StableInputFrame(frame)));
            Assert.Throws<SimBoundaryException>(() => retained.StepStable(new StableInputFrame(new InputFrame(default, default, actions: 1))));
            Assert.That(retained.Clock.Tick, Is.EqualTo(0), "nothing stepped");
            Simulation layered = World(false, true);
            Assert.Throws<SimBoundaryException>(() => layered.Step(new InputFrame(default, default, aimLayer: 1, actions: InputActions.GiveUp)), "layers are not actions");

            var legacy = new Simulation(7, identityMode: IdentityMode.Legacy);
            Assert.That(Assert.Throws<SimBoundaryException>(() => legacy.Step(frame)).Message, Is.EqualTo("This world's input carries no action bits."));
            Assert.That(legacy.Clock.Tick, Is.EqualTo(0));
            Assert.Throws<SimBoundaryException>(() => legacy.EnableInputActions(), "only a stable world carries action bits");
            Assert.DoesNotThrow(() => legacy.Step(new InputFrame(default, default)));
        }

        // Row 4: with the switch and without layers the snapshot has exactly one more field: four bytes of value, which the stable
        // writer stores like every Int32 as eight (the same width the aim layer adds).
        [Test]
        public void V19_Row4_TheSwitchAddsOneField()
        {
            Simulation plain = World(false), switched = World(true), layered = World(false, true);
            for (int tick = 0; tick < 5; tick++)
            {
                plain.Step(new InputFrame(default, Point, tick == 2));
                switched.Step(new InputFrame(default, Point, tick == 2));
                layered.Step(new InputFrame(default, Point, tick == 2));
            }
            var field = new SimStateWriter(Simulation.CreateRetainedWorld(1).Entities, materialize: true);
            field.Int32(0);
            int width = field.ToArray().Length;
            Assert.That(width, Is.EqualTo(8), "a stable Int32");
            Assert.That(switched.SaveRetainedSnapshot().Length, Is.EqualTo(plain.SaveRetainedSnapshot().Length + width));
            Assert.That(layered.SaveRetainedSnapshot().Length, Is.EqualTo(plain.SaveRetainedSnapshot().Length + width), "the aim layer adds as much");
            var frame = new StableInputFrame(new InputFrame(default, Point, actions: 0x35));
            Assert.That(Save(frame, false, true), Is.EqualTo(Concat(Save(frame, false, false), 0x35)));
        }

        // Row 5: both switches: the layer numbers first, the action bits last.
        [Test]
        public void V19_Row5_LayersThenActions()
        {
            var squad = new SquadCommand(SquadCommandKind.MoveTo, 9, Point, default, 3);
            var frame = new StableInputFrame(new InputFrame(SimVec3.Metres(1, 0, 0), Point, true, squad: squad, aimLayer: 2, actions: 0x35));
            Assert.That(Save(frame, true, true), Is.EqualTo(Concat(Save(frame, false, false), 3, 2, 0x35)));
            Assert.That(Save(frame, true, true), Is.EqualTo(Concat(Save(frame, true, false), 0x35)));
            var aimOnly = new StableInputFrame(new InputFrame(default, Point, aimLayer: 5, actions: 0x10));
            Assert.That(Save(aimOnly, true, true), Is.EqualTo(Concat(Save(aimOnly, false, false), 5, 0x10)));

            StableInputFrame back = StableInputFrame.Read(new SimStateReader(Save(frame, true, true)), true, true);
            Assert.That((back.Payload.Squad.Value.TargetLayer, back.Payload.AimLayer, back.Payload.Actions), Is.EqualTo((3, 2, 0x35u)));
            back = StableInputFrame.Read(new SimStateReader(Save(frame, false, true)), false, true);
            Assert.That((back.Payload.Squad.Value.TargetLayer, back.Payload.AimLayer, back.Payload.Actions), Is.EqualTo((0, 0, 0x35u)));
            back = StableInputFrame.Read(new SimStateReader(Save(frame, true, false)), true, false);
            Assert.That(back.Payload.Actions, Is.EqualTo(0u));

            foreach (int bad in new[] { 0x80, 0x7F, 9, -1 })
                Assert.Throws<SimSnapshotException>(() => StableInputFrame.Read(new SimStateReader(Concat(Save(aimOnly, true, false), bad)), true, true), bad.ToString());
        }

        [Test]
        public void V19_BitsSurviveASnapshot()
        {
            foreach (bool layers in new[] { false, true })
            {
                Simulation a = World(true, layers);
                a.Step(new InputFrame(default, Point, actions: 0x35));
                byte[] snapshot = a.SaveRetainedSnapshot();
                Simulation b = World(true, layers);
                b.RestoreRetainedSnapshot(snapshot);
                Assert.That(b.SaveRetainedSnapshot(), Is.EqualTo(snapshot));
                Assert.That(b.StateHashRetained(), Is.EqualTo(a.StateHashRetained()));
                Simulation zero = World(true, layers);
                zero.Step(new InputFrame(default, Point));
                Assert.That(zero.SaveRetainedSnapshot(), Is.Not.EqualTo(snapshot), "the bits are in the snapshot");
                a.Step(new InputFrame(default, Point, actions: 0x12));
                b.Step(new InputFrame(default, Point, actions: 0x12));
                Assert.That(b.StateHashRetained(), Is.EqualTo(a.StateHashRetained()));
            }
        }

        // Row 6: a snapshot of a world with the switch does not load into one without it, nor the other way round.
        [TestCase(true)]
        [TestCase(false)]
        public void V19_Row6_MismatchedSnapshotsFailToLoad(bool savedWithSwitch)
        {
            foreach (bool layers in new[] { false, true })
                foreach (int ticks in new[] { 0, 1, 7 })
                {
                    Simulation source = World(savedWithSwitch, layers);
                    for (int tick = 0; tick < ticks; tick++)
                        source.Step(new InputFrame(default, Point, tick % 2 == 0));
                    byte[] snapshot = source.SaveRetainedSnapshot();
                    Simulation target = World(!savedWithSwitch, layers);
                    Assert.Throws<SimSnapshotException>(() => target.RestoreRetainedSnapshot(snapshot), "layers " + layers + " ticks " + ticks);
                }
        }

        // Row 7: the switch is set before warmup.
        [Test]
        public void V19_Row7_TheSwitchIsSetBeforeWarmup()
        {
            Simulation sim = World(true);
            Assert.DoesNotThrow(() => sim.EnableInputActions(), "twice is harmless");
            Assert.DoesNotThrow(() => sim.Step(new InputFrame(default, default, actions: 0x78)));
            Assert.DoesNotThrow(() => sim.StepStable(new StableInputFrame(new InputFrame(default, default, actions: 0x40))));
            Simulation late = Simulation.CreateRetainedWorld(7);
            late.CompleteWarmup();
            Assert.Throws<InvalidOperationException>(() => late.EnableInputActions());
            Simulation stepped = Simulation.CreateRetainedWorld(7);
            stepped.Step(default);
            Assert.Throws<InvalidOperationException>(() => stepped.EnableInputActions());
        }

        // The bits are part of their tick's hash: only that tick differs.
        [TestCase(false)]
        [TestCase(true)]
        public void V19_BitsChangeTheirTicksHash(bool layers)
        {
            Simulation a = World(true, layers), b = World(true, layers);
            var ha = new ulong[8];
            var hb = new ulong[8];
            for (int tick = 0; tick < 8; tick++)
            {
                a.Step(new InputFrame(default, Point));
                b.Step(new InputFrame(default, Point, actions: tick == 4 ? InputActions.Reload : 0));
                ha[tick] = a.StateHashRetained();
                hb[tick] = b.StateHashRetained();
            }
            Assert.That(ha.Take(4), Is.EqualTo(hb.Take(4)), "before");
            Assert.That(ha[4], Is.Not.EqualTo(hb[4]), "the tick with the bits");
            Assert.That(ha.Skip(5), Is.EqualTo(hb.Skip(5)), "after: the frame is only the current tick's");
        }

        // The switch changes every hash even with zero bits (four more bytes), and only in the world that has it.
        [Test]
        public void V19_TheSwitchItselfIsInTheHash()
        {
            Simulation plain = World(false), switched = World(true);
            plain.Step(default);
            switched.Step(default);
            Assert.That(switched.StateHashRetained(), Is.Not.EqualTo(plain.StateHashRetained()));
        }

        private sealed class Reader : ISimSystem, IStableStateSerializer
        {
            internal uint Actions;
            internal int AimLayer = -1, TargetLayer = -1, Steps;
            public int Order => 1;
            public string StateTypeId => "test.base0-input-reader";
            public int StateVersion => 1;
            public void Step(SimContext context)
            {
                Steps++;
                Actions = context.Input.Actions;
                AimLayer = context.Input.AimLayer;
                TargetLayer = context.Input.Squad.HasValue ? context.Input.Squad.Value.TargetLayer : -1;
            }
            public void WriteState(StateHash hash) { }
            public void SaveState(SimStateWriter writer) { }
            public void LoadState(SimStateReader reader) { }
        }

        // V20: StepStable rebuilds the frame (Bind) and keeps the bits next to the squad command and the layers.
        [Test]
        public void V20_BindKeepsTheBits()
        {
            Simulation sim = World(true, true);
            var reader = new Reader();
            sim.Register("test.base0-input-reader", reader);
            var squad = new SquadCommand(SquadCommandKind.MoveTo, 9, Point, default, 3);
            sim.StepStable(new StableInputFrame(new InputFrame(default, Point, squad: squad, aimLayer: 2, actions: 0x35)));
            Assert.That((reader.Steps, reader.Actions, reader.AimLayer, reader.TargetLayer), Is.EqualTo((1, 0x35u, 2, 3)));
            sim.StepStable(new StableInputFrame(new InputFrame(default, Point, actions: InputActions.WithStance(InputActions.GiveUp, 7))));
            Assert.That((reader.Steps, reader.Actions, reader.AimLayer, reader.TargetLayer), Is.EqualTo((2, 0x48u, 0, -1)));
            sim.Step(new InputFrame(default, Point, actions: 0x20));
            Assert.That((reader.Steps, reader.Actions), Is.EqualTo((3, 0x20u)));
        }
    }
}
