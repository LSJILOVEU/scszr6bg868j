using NUnit.Framework;
using WarCore.Tests.Oracle;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S3"),Category("M0c2S3Oracle")]
    public sealed class M0c2S3OracleTests
    {
        [Test] public void IndependentTimelineReferenceMatchesWorkedHalfOpenIntervals()
        {
            var model=new AbilityOracle.Model(new AbilityOracle.Definition(2,5,2,2,10,3,5),20,20);
            Assert.That(model.Admit(1),Is.EqualTo(AbilityOracle.Failure.None));
            var expected=new[]{AbilityOracle.Phase.Idle,AbilityOracle.Phase.Windup,AbilityOracle.Phase.Windup,
                AbilityOracle.Phase.Active,AbilityOracle.Phase.Active,AbilityOracle.Phase.Active,
                AbilityOracle.Phase.Active,AbilityOracle.Phase.Active,AbilityOracle.Phase.Recovery,
                AbilityOracle.Phase.Recovery,AbilityOracle.Phase.Idle};
            for(int tick=0;tick<expected.Length;tick++)Assert.That(model.At(tick),Is.EqualTo(expected[tick]),"tick "+tick);
            Assert.That(model.Resources,Is.EqualTo(new long[]{17,15}));
            Assert.That(model.ReleaseCount(2),Is.Zero);Assert.That(model.ReleaseCount(3),Is.EqualTo(1));
            Assert.That(model.PulseCount(4),Is.Zero);Assert.That(model.PulseCount(5),Is.EqualTo(1));
            Assert.That(model.PulseCount(7),Is.EqualTo(2));Assert.That(model.PulseCount(8),Is.EqualTo(2));
            Assert.That(model.EndCount(7),Is.Zero);Assert.That(model.EndCount(8),Is.EqualTo(1));
            Assert.That(model.Admit(10),Is.EqualTo(AbilityOracle.Failure.Cooldown));
            Assert.That(model.Admit(11),Is.EqualTo(AbilityOracle.Failure.None));
        }
        [Test] public void IndependentCostAndControlReferencePreservesAtomicPaymentAndReleasedEvents()
        {
            var insufficient=new AbilityOracle.Model(new AbilityOracle.Definition(0,0,0,1,0,3,5),20,4);
            Assert.That(insufficient.Admit(1),Is.EqualTo(AbilityOracle.Failure.InsufficientResource));
            Assert.That(insufficient.Resources,Is.EqualTo(new long[]{20,4}));Assert.That(insufficient.Ledger.Count,Is.Zero);
            var interrupted=new AbilityOracle.Model(new AbilityOracle.Definition(2,4,1,1,7,3),20);
            interrupted.Admit(1);interrupted.Control(2,3);
            Assert.That(interrupted.ReleaseCount(10),Is.Zero);Assert.That(interrupted.Resources[0],Is.EqualTo(17));
            Assert.That(interrupted.Admit(3),Is.EqualTo(AbilityOracle.Failure.Blocked));
            Assert.That(interrupted.Admit(5),Is.EqualTo(AbilityOracle.Failure.Cooldown));
            var released=new AbilityOracle.Model(new AbilityOracle.Definition(2,4,1,1,7,3),20);
            released.Admit(1);released.Control(3,2);
            Assert.That(released.ReleaseCount(3),Is.EqualTo(1));Assert.That(released.PulseCount(10),Is.Zero);
        }
        [Test] public void IndependentTimelineReferenceUsesWideSaturationAndZeroPhaseCollapse()
        {
            Assert.That(AbilityOracle.Future(long.MaxValue-2,int.MaxValue),Is.EqualTo(long.MaxValue-1));
            var model=new AbilityOracle.Model(new AbilityOracle.Definition(0,0,0,1,0),new long[0]);
            model.Admit(1);Assert.That(model.At(1),Is.EqualTo(AbilityOracle.Phase.Idle));
            Assert.That(model.ReleaseCount(1),Is.EqualTo(1));Assert.That(model.EndCount(1),Is.EqualTo(1));
            Assert.That(model.PulseCount(100),Is.Zero);Assert.That(model.Admit(1),Is.EqualTo(AbilityOracle.Failure.None));
        }
    }
}
