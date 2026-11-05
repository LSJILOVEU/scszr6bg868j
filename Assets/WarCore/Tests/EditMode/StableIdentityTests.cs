using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class StableIdentityTests
    {
        [Test]
        public void ExplicitKeysSurviveReverseCreationAndRejectDefaultCreation()
        {
            var first = new EntityStore(8, IdentityMode.StableWorld);
            var second = new EntityStore(8, IdentityMode.StableWorld);
            var a = StableEntityKey.Placed(7, 11);
            var b = StableEntityKey.Placed(7, 12);
            EntityRef fa = first.Create(a);
            EntityRef fb = first.Create(b);
            EntityRef sb = second.Create(b);
            EntityRef sa = second.Create(a);
            Assert.That(fa.Slot, Is.Not.EqualTo(sa.Slot));
            Assert.That(first.StableOf(fa), Is.EqualTo(second.StableOf(sa)));
            Assert.That(first.StableOf(fb), Is.EqualTo(second.StableOf(sb)));
            Assert.Throws<SimBoundaryException>(() => first.Create());
            Assert.Throws<StableIdentityConflictException>(() => first.Create(a));
            Assert.That(first.TryResolve(a.Id, out EntityRef resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(fa));
        }

        [Test]
        public void IndependentH1VectorsFreezeDomainWidthsAndMixing()
        {
            Assert.That(StableEntityKey.Placed(0, 0).Id.Value, Is.EqualTo(0xC8B486766E9FAEAEUL));
            Assert.That(StableEntityKey.Placed(ulong.MaxValue, 0x8000000000000000UL).Id.Value,
                Is.EqualTo(0x9340AFD4448D0199UL));
        }

        [Test]
        public void LegacyCreationKeepsSlotAsIdentity()
        {
            var entities = new EntityStore();
            EntityId a = entities.Create();
            EntityId b = entities.Create();
            Assert.That(entities.StableOf(a).Value, Is.EqualTo((ulong)a.Id));
            Assert.That(entities.StableOf(b).Value, Is.EqualTo((ulong)b.Id));
            Assert.Throws<SimBoundaryException>(() => entities.Create(StableEntityKey.Placed(1, 1)));
        }
    }
}
