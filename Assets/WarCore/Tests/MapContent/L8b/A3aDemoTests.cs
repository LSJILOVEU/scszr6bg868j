using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // L8b: the A3a demo is the A3a search fixture written out as production code (map, recipe, three scripted commands).
    public sealed class A3aDemoTests
    {
        [Test]
        public void A3aDemo_MapIsTheFixture()
        {
            Assert.That(A3aDemo.MapBytes(), Is.EqualTo(WcMapWriter.Write(A3a.Map())));
        }

        // Every tick 0..900 hashes as the fixture game of L8bBase_A3aFixtureTimeline, so that timeline is the demo's.
        [Test]
        public void A3aDemo_ReplaysTheFixture()
        {
            Assert.That(() => A3aDemo.Recipe(), Throws.Nothing);
            var expected = new List<ulong>();
            L8bBaseTests.Fixture(expected, null);
            Assert.That(expected.Count, Is.EqualTo(L8bBaseTests.End + 1));
            SimSession s = SimSession.Create(A3aDemo.Recipe(), A3aDemo.MapBytes());
            var actual = new List<ulong> { s.StateHash };
            for (long t = 1; t <= L8bBaseTests.End; t++) { s.StepOnce(); actual.Add(s.StateHash); }
            Assert.That(s.Tick, Is.EqualTo(L8bBaseTests.End));
            for (int t = 0; t < expected.Count; t++)
                Assert.That(actual[t], Is.EqualTo(expected[t]), "tick " + t);
        }
    }
}
