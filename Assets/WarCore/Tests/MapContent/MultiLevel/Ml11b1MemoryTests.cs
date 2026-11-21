using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml11aWorld;
using static WarCore.Tests.Ml11b1World;

namespace WarCore.Tests
{
    // ML-11b-1 acceptance 1 and 3: on a map with floor plates the target memory keeps the layer the target stood on when it was
    // seen, in a state of version 2; without plates the state is the old one, byte for byte.
    public sealed class Ml11b1MemoryTests
    {
        private const string Remember = "{ \"targetMemoryTicks\": 300 }";

        private static M6World Sighting(UnitSpawn observer, UnitSpawn target) => World(new[] { observer, target }, Remember, Guns(20));

        // The layer is the target's, not the observer's and not read from the height: a soldier on the ground sees an enemy at
        // the floor's edge, a soldier on the floor sees an enemy on the ground, a soldier on the ground sees one on the ground.
        [Test]
        public void TheMemoryKeepsTheLayerTheTargetStoodOn()
        {
            M6World up = Sighting(Gunner(A, 36500, 30500), Still(B, 40500, 30500, 1));
            Assert.That(Recall(up, A).Known, Is.False);
            up.Step();
            Assert.That(Recall(up, A), Is.EqualTo((true, Up(40500, 30500), 1, 1L)));
            Assert.That(At(up, B), Is.EqualTo(Up(40500, 30500)));

            M6World down = Sighting(Gunner(A, 40500, 30500, 1), Still(B, 36500, 30500));
            down.Step();
            Assert.That(Recall(down, A), Is.EqualTo((true, Flat(36500, 30500), 0, 1L)));

            M6World level = Sighting(Gunner(A, 36500, 30500), Still(B, 31500, 30500));
            level.Step();
            Assert.That(Recall(level, A), Is.EqualTo((true, Flat(31500, 30500), 0, 1L)));

            // The five-value recall is the old one.
            Assert.That(Memory(up).TryRecall(Id(up, A), 1, out StableId target, out SimVec3 at, out long seen), Is.True);
            Assert.That((target, at, seen), Is.EqualTo((Id(up, B), Up(40500, 30500), 1L)));
            Assert.That(Memory(up).TryRecall(Id(up, B), 1, out _, out _, out int none, out _), Is.False);
            Assert.That(none, Is.EqualTo(0));
        }

        private static byte[] Saved(TargetMemory memory)
        {
            var writer = new SimStateWriter();
            memory.SaveState(writer);
            return writer.ToArray();
        }

        private static ulong Hashed(TargetMemory memory)
        {
            var hash = new StateHash();
            memory.WriteState(hash);
            return hash.Value;
        }

        // The state written out by hand. Without layers: version 1 and the old bytes. With layers: version 2 and one more
        // number after each entry's tick, in the bytes and in the hash.
        [Test]
        public void TheStateCarriesTheLayerOnlyWithLayers()
        {
            M6World w = Sighting(Gunner(A, 36500, 30500), Still(B, 40500, 30500, 1));
            StableId a = Id(w, A), b = Id(w, B);
            SimVec3 at = Up(40500, 30500);
            var plain = new TargetMemory(w.Combat, 60);
            var layered = new TargetMemory(w.Combat, 60, new Layers(null) { Of = { [w.Unit(B)] = 1 } });
            Saw(plain, a, w.Unit(B), 7); Saw(layered, a, w.Unit(B), 7);
            Assert.That((plain.StateVersion, layered.StateVersion), Is.EqualTo((1, 2)));
            Assert.That(Recall(plain, a, 7), Is.EqualTo((true, at, 0, 7L)));
            Assert.That(Recall(layered, a, 7), Is.EqualTo((true, at, 1, 7L)));

            var old = new SimStateWriter();
            old.Int32(60); old.Int32(1); old.UInt64(a.Value); old.UInt64(b.Value);
            old.Int64(at.X.Raw); old.Int64(at.Y.Raw); old.Int64(at.Z.Raw); old.Int64(7);
            Assert.That(Saved(plain), Is.EqualTo(old.ToArray()));
            old.Int32(1);
            Assert.That(Saved(layered), Is.EqualTo(old.ToArray()));

            StateHash Head(int version)
            {
                var hash = new StateHash();
                hash.Add("battle.ai-contacts"); hash.Add(version); hash.Add(60); hash.Add(1);
                hash.Add(a.Value); hash.Add(b.Value); hash.Add(at.X.Raw); hash.Add(at.Y.Raw); hash.Add(at.Z.Raw); hash.Add(7L);
                return hash;
            }
            Assert.That(Hashed(plain), Is.EqualTo(Head(1).Value));
            StateHash two = Head(2); two.Add(1);
            Assert.That(Hashed(layered), Is.EqualTo(two.Value));

            // Each reads its own bytes back, and not the other's.
            var again = new TargetMemory(w.Combat, 60, new Layers(null));
            again.LoadState(new SimStateReader(Saved(layered)));
            Assert.That(Recall(again, a, 7), Is.EqualTo((true, at, 1, 7L)));
            Assert.That(Hashed(again), Is.EqualTo(Hashed(layered)));
            var flat = new TargetMemory(w.Combat, 60);
            flat.LoadState(new SimStateReader(Saved(plain)));
            Assert.That(Recall(flat, a, 7), Is.EqualTo((true, at, 0, 7L)));
            Assert.That(Saved(flat), Is.EqualTo(Saved(plain)));
            Assert.That(() => new TargetMemory(w.Combat, 60, new Layers(null)).LoadState(new SimStateReader(Saved(plain))), Throws.Exception);
        }

        // The worlds: a map with plates registers version 2, a map without plates version 1 (and has no layers).
        [Test]
        public void OnlyAWorldWithPlatesHasTheNewVersion()
        {
            M6World plates = Sighting(Gunner(A, 36500, 30500), Still(B, 40500, 30500, 1));
            Assert.That(Memory(plates).StateVersion, Is.EqualTo(2));
            M6World flat = World(new[] { Gunner(A, 36500, 30500), Still(B, 31500, 30500) }, Remember, Guns(20), plates: false);
            Assert.That(Part<IUnitLayers>(flat, "layers"), Is.Null);
            Assert.That(Memory(flat).StateVersion, Is.EqualTo(1));
            flat.Step();
            Assert.That(Recall(flat, A), Is.EqualTo((true, Flat(31500, 30500), 0, 1L)));
            var old = new SimStateWriter();
            old.Int32(300); old.Int32(1); old.UInt64(Id(flat, A).Value); old.UInt64(Id(flat, B).Value);
            old.Int64(Flat(31500, 30500).X.Raw); old.Int64(0); old.Int64(Flat(31500, 30500).Z.Raw); old.Int64(1);
            Assert.That(Saved(Memory(flat)), Is.EqualTo(old.ToArray()));
        }

        // A snapshot taken after the sighting resumes with the same memory and the same hashes.
        [Test]
        public void ASnapshotBringsTheLayerBack()
        {
            M6World a = Sighting(Gunner(A, 36500, 30500), Still(B, 40500, 30500, 1));
            a.Step(3);
            byte[] snapshot = a.Sim.SaveRetainedSnapshot();
            M6World b = Sighting(Gunner(A, 36500, 30500), Still(B, 40500, 30500, 1));
            b.Sim.RestoreRetainedSnapshot(snapshot);
            Assert.That(Recall(b, A), Is.EqualTo(Recall(a, A)));
            Assert.That(Recall(b, A).Layer, Is.EqualTo(1));
            Assert.That(b.Hash, Is.EqualTo(a.Hash));
            for (int t = 0; t < 20; t++) { a.Step(); b.Step(); Assert.That(b.Hash, Is.EqualTo(a.Hash), "tick " + t); }
        }

        private static SimStateReader Written(M6World w, int layer)
        {
            var writer = new SimStateWriter();
            writer.Int32(300); writer.Int32(1); writer.UInt64(Id(w, A).Value); writer.UInt64(Id(w, B).Value);
            writer.Int64(1); writer.Int64(2); writer.Int64(3); writer.Int64(5); writer.Int32(layer);
            return new SimStateReader(writer.ToArray());
        }

        // Hand-written state: a layer below the ground is refused; the ground, a layer the map does not have and the layer of a
        // unit in the air are read as they are.
        [Test]
        public void ALayerBelowTheGroundIsRefused()
        {
            M6World w = Sighting(Gunner(A, 36500, 30500), Still(B, FarX, FarZ));
            var error = Assert.Throws<SimSnapshotException>(() => Memory(w).LoadState(Written(w, -1)));
            Assert.That(error.Message, Is.EqualTo("AI target memory layer is negative."));
            foreach (int layer in new[] { 0, 1, 9, 65535 })
            {
                Assert.That(() => Memory(w).LoadState(Written(w, layer)), Throws.Nothing, "layer " + layer);
                Assert.That(Recall(Memory(w), Id(w, A), 5).Layer, Is.EqualTo(layer));
            }
        }

        // A target in the air is remembered in the air.
        [Test]
        public void ATargetInTheAirIsRememberedInTheAir()
        {
            M6World w = Sighting(Gunner(A, 36500, 30500), Still(B, 40500, 30500, 1));
            var memory = new TargetMemory(w.Combat, 60, new Layers(Part<IUnitLayers>(w, "layers")) { Of = { [w.Unit(B)] = 65535 } });
            Saw(memory, Id(w, A), w.Unit(B), 3);
            Assert.That(Recall(memory, Id(w, A), 3), Is.EqualTo((true, Up(40500, 30500), 65535, 3L)));
            Saw(memory, Id(w, B), w.Unit(A), 3);
            Assert.That(Recall(memory, Id(w, B), 3), Is.EqualTo((true, Flat(36500, 30500), 0, 3L)), "the world's own layers for the rest");
        }
    }
}
