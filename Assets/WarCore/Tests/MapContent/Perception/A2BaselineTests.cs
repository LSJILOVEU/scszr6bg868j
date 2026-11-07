using NUnit.Framework;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 1, recorded on the code before A2: the fixture pack's canonical terrain-rule bytes, the terrain digest of
    // fixture D and the 600-tick run of fixture D. Later commits compare the default and explicit-1000 packs to these.
    public sealed class A2BaselineTests
    {
        internal const string CanonicalHex = "030000000400000002000000040000006469727401E803000001000000050000006772617373012003000001000000";
        internal const ulong TablesDigest = 6726583312463469901UL;
        internal const ulong DRun600 = 8154670557817580121UL;
        internal const int DFires600 = 12;

        [Test]
        public void Recorded_CanonicalBytes()
        {
            Assert.That(A2.Hex(A2.Pack().Terrain.Canonical()), Is.EqualTo(CanonicalHex));
        }

        [Test]
        public void Recorded_TablesDigest()
        {
            Assert.That(TerrainDerivation.Derive(A2.Map(A2.DWalls), A2.Pack()).Digest, Is.EqualTo(TablesDigest));
        }

        [Test]
        public void Recorded_FixtureD_600Ticks()
        {
            M6World w = A2.D(A2.Pack());
            Assert.That(A2.HashRun(w, 600, null, out int fires), Is.EqualTo(DRun600));
            Assert.That(fires, Is.EqualTo(DFires600));
        }
    }
}
