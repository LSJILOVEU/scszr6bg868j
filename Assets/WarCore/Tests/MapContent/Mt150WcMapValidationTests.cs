using System.IO;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt150WcMapValidationTests
    {
        private const long S = SimFix.Scale;

        [Test]
        public void MaximumLengthMapId_RoundTrips()
        {
            WcMap source = M2MapFixture.Model(false);
            string id = new string('m', 255);
            var map = new WcMap(id, source.ContentId, source.Min, source.Max, source.ChunkSize,
                source.FlatHeight, source.Width, source.Depth, source.Layers.ToArray(),
                source.Walls.ToArray(), source.Passability.ToArray());
            WcMap actual = null;
            Assert.DoesNotThrow(() => actual = WcMapReader.Read(WcMapWriter.Write(map)));
            Assert.That(actual.MapId, Is.EqualTo(id));
        }

        [Test]
        public void MinimumWorldCoordinate_RoundTrips()
        {
            WcMap map = Wp23.Make(1, 1, -16384 * S, -16380 * S, null, 0);
            WcMap actual = null;
            Assert.DoesNotThrow(() => actual = WcMapReader.Read(WcMapWriter.Write(map)));
            Assert.That(actual.Min.Y.Raw, Is.EqualTo(-16384 * S));
        }

        [Test]
        public void MaximumMapWidth_RoundTrips()
        {
            WcMap map = Wp23.Make(512, 1, 0, 4 * S, null, 0);
            WcMap actual = null;
            Assert.DoesNotThrow(() => actual = WcMapReader.Read(WcMapWriter.Write(map)));
            Assert.That(actual.Width, Is.EqualTo(512));
            Assert.That(actual.IsPassable(511, 0), Is.True);
        }

        [Test]
        public void FullLayerTable_RoundTrips()
        {
            WcMapLayer[] layers = Enumerable.Range(0, 32)
                .Select(i => new WcMapLayer("layer" + i, (byte)i)).ToArray();
            WcMap map = M2MapFixture.Copy(M2MapFixture.Model(false), layers: layers);
            WcMap actual = null;
            Assert.DoesNotThrow(() => actual = WcMapReader.Read(WcMapWriter.Write(map)));
            Assert.That(actual.Layers.Count, Is.EqualTo(32));
            foreach (WcMapLayer layer in layers)
                Assert.That(actual.LayerMask(layer.Name), Is.EqualTo(1u << layer.Bit));
        }

        [Test]
        public void FarEdgeTerrainVertex_RejectsInsufficientWallClearance()
        {
            WcMap map = Wp23.Make(1, 1, 0, 4 * S, new[] { (0, 0, 1, 1) },
                2 * S - 2, (x, z) => z * (int)S);
            Assert.That(Assert.Throws<InvalidDataException>(() => WcMapWriter.Write(map)).Message,
                Is.EqualTo("WCMAP_WALL_TERRAIN"));
        }

        [Test]
        public void ExactWallClearance_RoundTrips()
        {
            WcMap map = Wp23.Make(1, 1, 0, 4 * S, new[] { (0, 0, 1, 1) },
                2 * S, (x, z) => z * (int)S);
            WcMap actual = null;
            Assert.DoesNotThrow(() => actual = WcMapReader.Read(WcMapWriter.Write(map)));
            Assert.That(actual.Walls[0].Height.Raw, Is.EqualTo(2 * S));
            Assert.That(actual.VertexRaw(0, 1), Is.EqualTo(S));
        }
    }
}
