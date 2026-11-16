using System;
using NUnit.Framework;
using WarCore.Battle;

namespace WarCore.Tests
{
    public sealed class Mt007MatchModeTests
    {
        [Test]
        public void ConstructorRejectsNullPack()
        {
            Assert.Throws<ArgumentNullException>(() => new MatchMode(null));
        }
    }
}
