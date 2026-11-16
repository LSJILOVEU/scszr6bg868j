using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt105TerrainTablesTests
    {
        [Test]
        public void Digest_PreservesVersionOneEncoding()
        {
            var nav = new NavGrid(2, 1, SimFix.FromInt(0), SimFix.FromInt(0), new byte[] { 3 });
            var tables = new TerrainTables(null, nav, new byte[] { 1, 1 }, new ushort[] { 1000, 1000 },
                new string[0], new byte[0], new byte[0]);
            Assert.That(tables.Digest, Is.EqualTo(0xD270D349570D4DFEUL));
        }
    }
}
