using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 8, 9(a)(f)(g): the content field, the assembly and the constructor.
    public sealed class A3bContentTests
    {
        private static int Read(string ai) => A3.Load(A3.PackJson(A3.Grass500, ai)).Section<AiRules>().SuppressTicks;

        [TestCase(0)]
        [TestCase(100)]
        [TestCase(1000000)]
        public void SuppressTicks_IsRead(int value) => Assert.That(Read("{ \"suppressTicks\": " + value + " }"), Is.EqualTo(value));

        [Test]
        public void SuppressTicks_DefaultsToZero()
        {
            Assert.That(A3.Load(A3.PackJson(A3.Grass500, (int?)null)).Section<AiRules>().SuppressTicks, Is.EqualTo(0));
            Assert.That(Read("{ \"targetMemoryTicks\": 300 }"), Is.EqualTo(0));
        }

        [TestCase("-1", "out of range [0, 1000000]")]
        [TestCase("1000001", "out of range [0, 1000000]")]
        [TestCase("1.5", "must be an integer")]
        [TestCase("\"5\"", "must be an integer")]
        public void SuppressTicks_Invalid(string value, string tail)
        {
            var e = Assert.Throws<ContentException>(() => Read("{ \"suppressTicks\": " + value + " }"));
            Assert.That(e.Message, Is.EqualTo("content pack 'a2-test' layer 'a2-test' ai field 'suppressTicks': " + tail));
        }

        // The six fields of this package together, and the old five without the new one.
        [Test]
        public void SixFields_EachRead()
        {
            AiRules r = A3.Load(A3.PackJson(A3.Grass500, "{ \"failedSlotTicks\": 7, \"targetMemoryTicks\": 300, \"searchTicks\": 60, "
                + "\"reactionTicks\": 15, \"senseTicks\": 3, \"suppressTicks\": 100 }")).Section<AiRules>();
            Assert.That(r.FailedSlotTicks, Is.EqualTo(7)); Assert.That(r.TargetMemoryTicks, Is.EqualTo(300)); Assert.That(r.SearchTicks, Is.EqualTo(60));
            Assert.That(r.ReactionTicks, Is.EqualTo(15)); Assert.That(r.SenseTicks, Is.EqualTo(3)); Assert.That(r.SuppressTicks, Is.EqualTo(100));
            AiRules old = A3.Load(A3.PackJson(A3.Grass500, "{ \"failedSlotTicks\": 7, \"targetMemoryTicks\": 300, \"searchTicks\": 60, "
                + "\"reactionTicks\": 15, \"senseTicks\": 3 }")).Section<AiRules>();
            Assert.That(old.SuppressTicks, Is.EqualTo(0)); Assert.That(old.SenseTicks, Is.EqualTo(3));
        }

        // 9(a): M > 0 and D > 0 on a terrain world register exactly one battle.ai-suppress at order 18 in Requests.
        [Test]
        public void Assembly_OneSystemAtOrder18()
        {
            M6World w = A3b.World(A3b.Ai(300, 100));
            string systems = L4.Systems(w);
            Assert.That(systems.Split(';'), Has.Exactly(1).EqualTo("battle.ai-suppress|0|18"));
            Assert.That(w.System<TargetSuppress>(), Is.Not.Null);
            Assert.That(w.System<TargetSuppress>().Order, Is.EqualTo(18));
            Assert.That(w.System<TargetSuppress>().StateTypeId, Is.EqualTo("battle.ai-suppress"));
            Assert.That(w.System<TargetSuppress>().StateVersion, Is.EqualTo(1));
        }

        // 9(f): a flat world has neither the memory (13) nor the suppression (18).
        [Test]
        public void Assembly_FlatWorld_NeitherMemoryNorSuppression()
        {
            M6World w = L4.A(10, "{ \"targetMemoryTicks\": 300, \"suppressTicks\": 100 }", terrain: false);
            Assert.That(L4.Systems(w), Does.Not.Contain("battle.ai-contacts"));
            Assert.That(L4.Systems(w), Does.Not.Contain("battle.ai-suppress"));
            Assert.That(w.System<TargetSuppress>(), Is.Null);
        }

        // 9(g): the constructor checks ticks first, then the three references in order.
        [Test]
        public void Constructor_ChecksTicksThenReferences()
        {
            M6World w = A3b.World(A3b.Ai(300, 100));
            TargetMemory memory = w.System<TargetMemory>();
            IMoveOrders moves = L4.Moves(w);
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => new TargetSuppress(w.Combat, memory, moves, 0)).ParamName, Is.EqualTo("ticks"));
            Assert.That(Assert.Throws<ArgumentNullException>(() => new TargetSuppress(null, memory, moves, 1)).ParamName, Is.EqualTo("combat"));
            Assert.That(Assert.Throws<ArgumentNullException>(() => new TargetSuppress(w.Combat, null, moves, 1)).ParamName, Is.EqualTo("contacts"));
            Assert.That(Assert.Throws<ArgumentNullException>(() => new TargetSuppress(w.Combat, memory, null, 1)).ParamName, Is.EqualTo("moves"));
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => new TargetSuppress(null, memory, moves, 0)).ParamName, Is.EqualTo("ticks"));
            Assert.DoesNotThrow(() => new TargetSuppress(w.Combat, memory, moves, 1));
        }
    }
}
