using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    public sealed class Mt162ViewFireTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void ConstructorPreservesLethalFlag(bool lethal)
        {
            var fire = new ViewFire(1f, 2f, 3f, 4f, 1, lethal);
            Assert.That(fire.Lethal, Is.EqualTo(lethal));
        }
    }
}
