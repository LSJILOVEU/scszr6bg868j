using System;
using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture, Category("M0c2Load")]
    public sealed class M0c2LoadTests
    {
        [TestCase(false,false),TestCase(true,false),TestCase(false,true),TestCase(true,true)]
        public void ChurnRoundTripAndContinuation(bool dense, bool references)
        {
            var a = new Round4ChurnScene(dense,references,placed:true,gameplay:new GameplaySchema(0));
            var b = new Round4ChurnScene(dense,references,placed:true,gameplay:new GameplaySchema(0));
            for (int tick = 0; tick < 45; tick++)
            {
                if (tick == 0 || tick == 1 || tick == 19 || tick == 32 || tick == 40)
                {
                    byte[] snapshot = a.Sim.SaveRetainedSnapshot();
                    b.Sim.RestoreRetainedSnapshot(snapshot,true);
                    Assert.That(b.Sim.StateHashRetained(),Is.EqualTo(a.Sim.StateHashRetained()),"round trip tick="+tick);
                    Assert.That(b.Sim.SaveRetainedSnapshot(),Is.EqualTo(snapshot),"canonical bytes tick="+tick);
                }
                a.Sim.Step(default); b.Sim.Step(default);
                Assert.That(b.Sim.StateHashRetained(),Is.EqualTo(a.Sim.StateHashRetained()),"continuation tick="+(tick+1));
            }
        }
        [TestCase(0),TestCase(1),TestCase(2),TestCase(3),TestCase(4),TestCase(5),TestCase(6),TestCase(7),
         TestCase(8),TestCase(9),TestCase(10),TestCase(11),TestCase(12)]
        public void FullRoundTripAndContinuation(int profile)
        {
            var a = new M0c2FullFixture(profile);
            var b = new M0c2FullFixture(profile);
            for(int tick=0;tick<6;tick++)
            {
                if(tick==0 || tick==3)
                {
                    byte[] snapshot=a.Sim.SaveRetainedSnapshot();
                    b.Sim.RestoreRetainedSnapshot(snapshot,true);
                    Assert.That(b.Sim.SaveRetainedSnapshot(),Is.EqualTo(snapshot));
                    Assert.That(b.Sim.StateHashRetained(),Is.EqualTo(a.Sim.StateHashRetained()));
                }
                a.Sim.Step(default); b.Sim.Step(default);
                Assert.That(b.Sim.StateHashRetained(),Is.EqualTo(a.Sim.StateHashRetained()),"profile="+profile+" tick="+tick);
            }
        }
        [Test]
        public void FullHiddenStateChangesHash()
        {
            var fixture=new M0c2FullFixture(12); fixture.Sim.Step(default);
            foreach(string name in new[]{"Calls","TotalHits","Checksum","LastQueries","LastRays","LastAffected","Truncated"})
            {
                var field=typeof(M0c2FullFixture).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);
                object value=field.GetValue(fixture); ulong before=fixture.Sim.StateHashRetained();
                field.SetValue(fixture,value is int?(object)((int)value+1):value is long?(object)((long)value+1):(object)((ulong)value+1));
                Assert.That(fixture.Sim.StateHashRetained(),Is.Not.EqualTo(before),name);
                field.SetValue(fixture,value);
            }
        }
        [Test, Category("M0c2FixtureMutation")]
        public void ChurnHiddenStateChangesHash()
        {
            var scene = new Round4ChurnScene(true,true,placed:true,gameplay:new GameplaySchema(0));
            scene.Sim.Step(default);
            foreach(string name in new[]{"cursor","Created","TraversalChecksum","Notices","LastAffected","LastRays","LastQueries"})
            {
            ulong before = scene.Sim.StateHashRetained();
            object target = name == "Notices" ? (object)scene.References : scene;
            var field = target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);
            object value = field.GetValue(target);
            field.SetValue(target, value is int ? (object)((int)value+(name=="LastAffected"?-1:1)) : value is long ? (object)((long)value+1) : (object)((ulong)value+1));
            Assert.That(scene.Sim.StateHashRetained(),Is.Not.EqualTo(before),name+" is hidden from the full hash");
            field.SetValue(target,value);
            }
        }
    }
}
