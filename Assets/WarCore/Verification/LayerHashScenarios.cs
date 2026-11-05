using WarCore.Sim;
using WarCore.Verification.Fixtures;
using Simulation = WarCore.Sim.Sim;
using Fixture = WarCore.Verification.Fixtures.Fixtures;

namespace WarCore.Verification
{
    internal static class LayerHashScenarios
    {
        internal static void Run(Report report)
        {
            Simulation baseline = LayerHashFixture.Create(123);
            for (int tick = 1; tick <= 510; tick++)
                baseline.Step(Fixture.Input(tick));
            byte[] snapshot = baseline.SaveSnapshot();
            string saved = Pair(baseline);
            report.Line("layerWhole.save510", saved);
            var included = new ulong[490];
            var ignored = new ulong[490];
            var sequence = new StableDigest();
            for (int tick = 511; tick <= 1000; tick++)
            {
                baseline.Step(Fixture.Input(tick));
                included[tick - 511] = LayerHashFixture.Hash(baseline, false);
                ignored[tick - 511] = LayerHashFixture.Hash(baseline, true);
                sequence.Row(tick + ":" + Pair(baseline));
            }
            HashScenarios.Require(LayerHashFixture.Hash(baseline, false) == LayerHashFixture.IncludedHash, "Collider match v2 hash mismatch.");
            HashScenarios.Require(LayerHashFixture.Hash(baseline, true) == LayerHashFixture.IgnoredHash, "Collider match v1 hash mismatch.");
            HashScenarios.Require(LayerHashFixture.IncludedHash != LayerHashFixture.IgnoredHash, "Layer bytes must change the collider match hash.");
            report.Line("layerWhole.final", Pair(baseline));
            report.Digest("layerWhole.sequence", sequence);
            foreach (bool fresh in new[] { false, true })
            {
                Simulation restored = fresh ? LayerHashFixture.Create(999) : baseline;
                restored.RestoreSnapshot(snapshot);
                HashScenarios.Require(restored.Clock.Tick == 510 && Pair(restored) == saved, "Collider match restore mismatch.");
                var resumed = new StableDigest();
                for (int tick = 511; tick <= 1000; tick++)
                {
                    restored.Step(Fixture.Input(tick));
                    HashScenarios.Require(LayerHashFixture.Hash(restored, false) == included[tick - 511], "Collider match continuation mismatch at " + tick);
                    HashScenarios.Require(LayerHashFixture.Hash(restored, true) == ignored[tick - 511], "Collider match v1 continuation mismatch at " + tick);
                    resumed.Row(tick + ":" + Pair(restored));
                }
                string key = fresh ? "layerWhole.fresh" : "layerWhole.inplace";
                report.Line(key + ".final", Pair(restored));
                report.Digest(key + ".sequence", resumed);
            }
            CheckLayerEncoding(0x80000000u);
            CheckLayerEncoding(uint.MaxValue);
        }

        private static string Pair(Simulation sim) => "hash=" + StableDigest.Hex64(LayerHashFixture.Hash(sim, false))
            + ";ignored=" + StableDigest.Hex64(LayerHashFixture.Hash(sim, true));

        private static void CheckLayerEncoding(uint layer)
        {
            var collider = new Collider(default, default, layer);
            var serializer = new ColliderSerializer();
            var writer = new SimStateWriter();
            serializer.Write(writer, in collider);
            byte[] bytes = writer.ToArray();
            HashScenarios.Require(bytes.Length == 29 && bytes[28] == 0, "Collider v3 encoding width and static bit.");
            for (int i = 0; i < 8; i++)
                HashScenarios.Require(bytes[20 + i] == unchecked((byte)((ulong)layer >> (8 * i))), "Collider zero extension.");
            var reader = new SimStateReader(bytes);
            HashScenarios.Require(serializer.Read(reader).Layer == layer, "Collider layer round trip.");
            reader.End();
            var expected = new StateHash();
            for (int i = 0; i < 24; i++)
                expected.Add((byte)0);
            for (int i = 0; i < 8; i++)
                expected.Add(bytes[20 + i]);
            var actual = new StateHash(ignoreColliderLayers: false);
            collider.WriteState(actual);
            HashScenarios.Require(actual.Value == expected.Value, "Collider v2 compatibility hash zero extension.");
            expected.Add(false);
            var current = new StateHash();
            collider.WriteState(current);
            HashScenarios.Require(current.Value == expected.Value, "Collider v3 static byte.");
        }
    }
}
