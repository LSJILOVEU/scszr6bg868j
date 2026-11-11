using System.IO;
using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt066SimStateIOTests
    {
        [Test]
        public void BytesRejectsOneByteOverLimitBeforeWritingHeader()
        {
            var writer = new SimStateWriter();
            writer.Int32(0);
            var stream = (MemoryStream)typeof(SimStateWriter)
                .GetField("stream", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(writer);
            // Seed the logical length without allocating a multi-gigabyte buffer.
            typeof(MemoryStream).GetField("_length", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(stream, int.MaxValue - 4);

            Assert.Throws<SimBoundaryException>(() => writer.Bytes(new byte[] { 1 }));
            Assert.That(stream.Position, Is.EqualTo(4));
        }
    }
}
