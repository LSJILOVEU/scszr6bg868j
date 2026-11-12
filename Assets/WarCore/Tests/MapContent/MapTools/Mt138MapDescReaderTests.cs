using System.Globalization;
using System.IO;
using NUnit.Framework;
using WarCore.Content.MapTools;
using static WarCore.Tests.E1aSamples;

namespace WarCore.Tests
{
    public sealed class Mt138MapDescReaderTests
    {
        [Test]
        public void SignedHeightExtremesArePreserved()
        {
            string text = Edit(Edit(Sample, "\"floorRaw\": -65536", "\"floorRaw\": -9223372036854775808"),
                "\"ceilingRaw\": 262144", "\"ceilingRaw\": 9223372036854775807");
            MapDesc desc = null;
            Assert.DoesNotThrow(() => desc = MapDescJson.Read(Utf8(text)));
            Assert.That(desc.FloorRaw, Is.EqualTo(long.MinValue));
            Assert.That(desc.CeilingRaw, Is.EqualTo(long.MaxValue));
        }

        [Test]
        public void NegativeSeedIsRejected()
        {
            string text = Edit(Sample, "\"seed\": 41", "\"seed\": -41");
            var error = Assert.Throws<InvalidDataException>(() => MapDescJson.Read(Utf8(text)));
            Assert.That(error.Message, Is.EqualTo("MAPDESC_KEY line 71"));
        }

        [Test]
        public void SurrogatePairAtEndOfIdIsPreserved()
        {
            string text = Edit(Sample, "\"id\": \"sample\"", "\"id\": \"sample\\uD83D\\uDE00\"");
            MapDesc desc = null;
            Assert.DoesNotThrow(() => desc = MapDescJson.Read(Utf8(text)));
            Assert.That(desc.Id, Is.EqualTo("sample\uD83D\uDE00"));
        }

        [Test]
        public void SixtyFourSurfaceNamesAreAccepted()
        {
            var names = new string[64];
            for (int i = 0; i < names.Length; i++) names[i] = "s" + i.ToString("D2", CultureInfo.InvariantCulture);
            string text = Edit(Sample, "[\"grass\", \"mud\"]", "[\"" + string.Join("\", \"", names) + "\"]");
            text = Edit(text, "\"AB\"", "\"A/\"");
            MapDesc desc = null;
            Assert.DoesNotThrow(() => desc = MapDescJson.Read(Utf8(text)));
            Assert.That(desc.Surfaces.Names, Is.EqualTo(names));
            Assert.That(desc.Surfaces.Rows, Is.EqualTo(new[] { "A/" }));
        }

        [Test]
        public void DuplicateSurfaceNamesAreRejected()
        {
            string text = Edit(Sample, "[\"grass\", \"mud\"]", "[\"grass\", \"grass\"]");
            var error = Assert.Throws<InvalidDataException>(() => MapDescJson.Read(Utf8(text)));
            Assert.That(error.Message, Is.EqualTo("MAPDESC_GRID line 22"));
        }

        [Test]
        public void SixtyFourOwnersAreAccepted()
        {
            var owners = new string[64];
            owners[0] = "hand";
            for (int i = 1; i < owners.Length; i++) owners[i] = "o" + i.ToString("D2", CultureInfo.InvariantCulture);
            string text = Edit(Sample, "[\"hand\", \"stamp:s1\"]", "[\"" + string.Join("\", \"", owners) + "\"]");
            text = Edit(text, "\".B\"", "\"./\"");
            MapDesc desc = null;
            Assert.DoesNotThrow(() => desc = MapDescJson.Read(Utf8(text)));
            Assert.That(desc.Owners, Is.EqualTo(owners));
            Assert.That(desc.SurfaceOwner, Is.EqualTo(new[] { "./" }));
        }
    }
}
