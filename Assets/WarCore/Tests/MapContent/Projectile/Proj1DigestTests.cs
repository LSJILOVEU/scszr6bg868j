using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;

namespace WarCore.Tests.Projectile
{
    // PROJ-1 A1 (unchanged half): a pack without a projectile weapon keeps the policy digest it had before this package.
    // The values were recorded on main before any projectile code existed.
    public sealed class Proj1DigestTests
    {
        private const string AncientData = "Assets/WarCore/Content/Ancient/Data";

        internal static ContentPack Shipped(string id) =>
            id == "ancient" ? ContentLoader.Load(new FileContentSource(AncientData), id) : RwrPack.Load(id);

        [TestCase("rwr", 10081263646446793677UL)]
        [TestCase("rwr-m6", 10614849506318239698UL)]
        [TestCase("ancient", 12694807483423786222UL)]
        public void ShippedPackDigestIsUnchanged(string id, ulong expected) =>
            Assert.That(Shipped(id).BuildCombat().PolicyDigest, Is.EqualTo(expected), id);
    }
}
