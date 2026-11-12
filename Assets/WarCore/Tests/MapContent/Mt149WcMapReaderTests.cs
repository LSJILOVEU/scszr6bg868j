using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt149WcMapReaderTests
    {
        [Test]
        public void IdentifierAt255Bytes_RoundTrips()
        {
            string name = new string('a', 255);
            WcMap map = M2MapFixture.Copy(M2MapFixture.Model(false),
                layers: new[] { new WcMapLayer(name, 1) });
            byte[] bytes = WcMapWriter.Write(map);
            WcMap actual = null;
            Assert.DoesNotThrow(() => actual = WcMapReader.Read(bytes));
            Assert.That(actual.Layers[0].Name, Is.EqualTo(name));
        }

        [Test]
        public void All32Layers_RoundTrip()
        {
            WcMapLayer[] layers = Enumerable.Range(0, 32)
                .Select(i => new WcMapLayer("layer" + i.ToString("D2"), (byte)i)).ToArray();
            byte[] bytes = WcMapWriter.Write(M2MapFixture.Copy(M2MapFixture.Model(false), layers: layers));
            WcMap actual = null;
            Assert.DoesNotThrow(() => actual = WcMapReader.Read(bytes));
            Assert.That(actual.Layers.Count, Is.EqualTo(32));
            Assert.That(actual.LayerMask("layer31"), Is.EqualTo(0x80000000u));
        }

        [Test]
        public void WidthAt512_RoundTrips()
        {
            WcMap source = M2MapFixture.Model(false);
            var map = new WcMap(source.MapId, source.ContentId, source.Min, SimVec3.Metres(512, 4, 1),
                SimFix.FromInt(512), source.FlatHeight, 512, 1, source.Layers.ToArray(), source.Walls.ToArray(),
                Enumerable.Repeat((byte)255, 64).ToArray());
            byte[] bytes = WcMapWriter.Write(map);
            WcMap actual = null;
            Assert.DoesNotThrow(() => actual = WcMapReader.Read(bytes));
            Assert.That(actual.Width, Is.EqualTo(512u));
            Assert.That(actual.Depth, Is.EqualTo(1u));
            Assert.That(actual.IsPassable(511, 0), Is.True);
        }

        [Test]
        public void All64SurfaceNames_RoundTrip()
        {
            WcMap source = M2MapFixture.Model(false);
            string[] names = Enumerable.Range(0, 64).Select(i => "surface" + i.ToString("D2")).ToArray();
            byte[] cells = Enumerable.Repeat((byte)63, 32).ToArray();
            var map = new WcMap(source.MapId, source.ContentId, source.Min, source.Max, source.ChunkSize,
                source.FlatHeight, source.Width, source.Depth, source.Layers.ToArray(), source.Walls.ToArray(),
                source.Passability.ToArray(), null, names, cells);
            byte[] bytes = WcMapWriter.Write(map);
            WcMap actual = null;
            Assert.DoesNotThrow(() => actual = WcMapReader.Read(bytes));
            Assert.That(actual.SurfaceNames, Is.EqualTo(names));
            Assert.That(actual.SurfaceCells, Is.EqualTo(cells));
        }
    }
}
