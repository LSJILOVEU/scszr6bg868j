using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests.Diagnostics
{
    public sealed class DdaBenchmarkContractTests
    {
        internal const string GeneratorVersion = "real-bench-legacy-v1";

        [Test]
        public void ExistingIntegerHashOverloadsWriteEightLittleEndianBytes()
        {
            var signed = new StateHash();
            signed.Add(-1);
            var unsigned = new StateHash();
            unsigned.Add(uint.MaxValue);
            var expectedSigned = new StateHash();
            var expectedUnsigned = new StateHash();
            for (int i = 0; i < 8; i++)
            {
                expectedSigned.Add((byte)255);
                expectedUnsigned.Add((byte)(i < 4 ? 255 : 0));
            }
            Assert.That(signed.Value, Is.EqualTo(expectedSigned.Value));
            Assert.That(unsigned.Value, Is.EqualTo(expectedUnsigned.Value));
        }

        [TestCase(false, false, 0x491B1C9946E26B1FUL, 0xFB81BFBCE69B2838UL)]
        [TestCase(false, true, 0x5DC4600B9373608FUL, 0x0EC85D4460BDDB87UL)]
        [TestCase(true, false, 0x66B31FC5150CDCE2UL, 0xF6661E6B2C611C4BUL)]
        [TestCase(true, true, 0x4C78518D2D8D8131UL, 0xC947AF32AD139F24UL)]
        public void FrozenRawInputsAndEveryOrderedOutputMatchPreDda(bool dense, bool peak, ulong input, ulong output)
        {
            var scene = new RealBenchScene(dense, peak);
            ulong actualInput = InputSignature(scene);
            ulong actualOutput = OutputSignature(scene);
            TestContext.WriteLine("D1 contract {0}: generator={1}; seed=0x{2:X}; queries={3}; E={4}; input=0x{5:X16}; output=0x{6:X16}.",
                RealSimBench.Label(dense, peak), GeneratorVersion, RealBenchScene.Seed, scene.Queries.Length, scene.Affected, actualInput, actualOutput);
            Assert.That(actualInput, Is.EqualTo(input));
            Assert.That(actualOutput, Is.EqualTo(output));
        }

        internal static ulong InputSignature(RealBenchScene scene)
        {
            // The frozen v1 input signature predates layers and must keep its original byte stream.
            var hash = new StateHash(ignoreColliderLayers: true);
            hash.Add(GeneratorVersion);
            hash.Add(RealBenchScene.Seed);
            hash.Add(scene.Dense);
            hash.Add(scene.Peak);
            hash.Add(3 * SimFix.Scale);
            hash.Add(scene.Bodies.Length);
            foreach (SpatialBody body in scene.Bodies)
            {
                body.Entity.WriteState(hash);
                body.Position.WriteState(hash);
                body.Collider.WriteState(hash);
            }
            hash.Add(scene.Queries.Length);
            foreach (RealQuery input in scene.Queries)
            {
                hash.Add((int)input.Role);
                hash.Add(input.Query.Kind);
                input.Query.Origin.WriteState(hash);
                input.Query.Direction.WriteState(hash);
                hash.Add(input.Query.Radius.Raw);
                hash.Add(input.Query.Kind == 2 ? 600 : 0);
                hash.Add(input.Query.Kind == 2 ? 2 * SimFix.Scale : 0);
            }
            return hash.Value;
        }

        internal static ulong OutputSignature(RealBenchScene scene)
        {
            var hash = new StateHash();
            hash.Add("real-bench-output-v1");
            hash.Add(scene.Queries.Length);
            foreach (RealQuery input in scene.Queries)
            {
                int count = input.Query.Run(scene.Grid, scene.Entities, scene.Rays);
                hash.Add(count);
                for (int i = 0; i < count; i++)
                {
                    EntityId entity = input.Query.Kind == 0 ? scene.Rays[i].Entity : scene.Entities[i];
                    entity.WriteState(hash);
                    if (input.Query.Kind == 0)
                        hash.Add(scene.Rays[i].Distance.Raw);
                }
            }
            return hash.Value;
        }
    }
}
