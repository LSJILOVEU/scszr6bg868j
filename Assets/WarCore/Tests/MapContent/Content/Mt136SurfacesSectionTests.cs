using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content;

namespace WarCore.Tests
{
    public sealed class Mt136SurfacesSectionTests
    {
        [Test]
        public void Accepts255ByteSurfaceId()
        {
            string id = new string('a', 255);
            var source = new MemoryContentSource(new Dictionary<string, string>
            {
                ["surface-limit"] = "{ \"id\": \"surface-limit\", \"extends\": \"rwr\", \"surfaces\": [ { \"id\": \"" + id + "\" } ] }"
            }, FileContentSource.Rwr());
            ContentPack pack = null;
            Assert.DoesNotThrow(() => pack = ContentLoader.Load(source, "surface-limit"));
            Assert.That(pack.Terrain.TryFind(id, out SurfaceDef surface), Is.True);
            Assert.That(surface.Id, Is.EqualTo(id));
        }

        [Test]
        public void MissingConcealMilliDefaultsTo1000()
        {
            ContentPack pack = A2.Pack();
            Assert.That(pack.Terrain.TryFind("dirt", out SurfaceDef surface), Is.True);
            Assert.That(surface.ConcealMilli, Is.EqualTo(1000));
        }
    }
}
