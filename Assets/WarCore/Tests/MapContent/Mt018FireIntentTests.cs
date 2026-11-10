using NUnit.Framework;
using WarCore.Combat;

namespace WarCore.Tests
{
    public sealed class Mt018FireIntentTests
    {
        [Test]
        public void AimedKind_IsOne()
        {
            Assert.That((byte)FireIntent.Aimed(default, default).Kind, Is.EqualTo((byte)1));
        }

        [Test]
        public void AtTargetKind_IsTwo()
        {
            Assert.That((byte)FireIntent.AtTarget(default, default).Kind, Is.EqualTo((byte)2));
        }
    }
}
