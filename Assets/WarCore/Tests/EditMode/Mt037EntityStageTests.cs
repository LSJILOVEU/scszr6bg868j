using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture]
    public sealed class Mt037EntityStageTests
    {
        [Test]
        public void ConstructorRejectsNullStore()
        {
            Assert.Throws<ArgumentNullException>(() => new EntityStage(null));
        }
    }
}
