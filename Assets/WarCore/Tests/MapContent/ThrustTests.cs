using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // C5: a melee spec with a line half width hits along a narrow straight line instead of a sector. Unit 1 swings at the bait, unit 2,
    // 1 m ahead on +X; the line is 2 m long with half width 0.2 m; a unit collider is 0.25 m, so a target counts while its centre is
    // within 0.45 m of the line (round ends). Windup 6: the swing starts on tick 1 and lands on tick 7.
    public sealed class ThrustTests
    {
        private const long Ox = 20000, Oz = 20000;

        private static MeleeSpec Thrust(double halfWidth = 0.2, int maxTargets = 3, int windup = 6) =>
            new MeleeSpec(windup, 2, 9, SimFix.FromInt(2), 500, SimFix.FromInt(1), SimFix.FromInt(10), maxTargets, SimFix.Ratio(18, 10),
                SimFix.Ratio((long)(halfWidth * 1000), 1000));

        private static MeleeWorld World(MeleeSpec spec, params UnitSpawn[] others)
        {
            var units = new List<UnitSpawn> { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), MeleeWorld.Unit(2, 2, Ox + 1000, Oz) };
            units.AddRange(others);
            return new MeleeWorld(MeleeWorld.Pack(new[] { spec }), units.ToArray());
        }

        private static void MoveTo(MeleeWorld w, ulong key, long xMm, long zMm) =>
            w.W.Set(key, new Position(new SimVec3(SimFix.Ratio(xMm, 1000), w.W.Read<Position>(key).Value.Y, SimFix.Ratio(zMm, 1000))));

        // The bait fixes the heading (+X); the probe stands at (dx, dz) from the striker.
        [TestCase(1500, 0, true, TestName = "on the line")]
        [TestCase(1500, 400, true, TestName = "0.4 m off the line, collider still touches")]
        [TestCase(1500, -400, true, TestName = "0.4 m off the other side")]
        [TestCase(1500, 500, false, TestName = "0.5 m off, collider clear")]
        [TestCase(1500, 600, false, TestName = "0.6 m off, a sector would have hit")]
        [TestCase(-1200, 0, false, TestName = "behind")]
        [TestCase(2400, 0, true, TestName = "past the line end by 0.4 m, collider touches the round end")]
        [TestCase(2600, 0, false, TestName = "past the reach")]
        [TestCase(0, 1500, false, TestName = "beside, 90 degrees")]
        public void OnlyTheNarrowLineHits(long dx, long dz, bool hit)
        {
            var w = World(Thrust(), MeleeWorld.Unit(3, 2, Ox + dx, Oz + dz));
            w.Step(7);
            Assert.That(w.Health(2), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)), "the bait is always hit");
            Assert.That(w.Health(3), Is.EqualTo(hit ? MeleeWorld.Full - MeleeWorld.Cost(10) : MeleeWorld.Full));
        }

        [Test]
        public void TheSameGeometryUnderTheSectorSpecHitsTheWideTarget()
        {
            var sector = new MeleeSpec(6, 2, 9, SimFix.FromInt(2), 500, SimFix.FromInt(1), SimFix.FromInt(10), 3, SimFix.Ratio(18, 10));
            var w = World(sector, MeleeWorld.Unit(3, 2, Ox + 1500, Oz + 600));
            w.Step(7);
            Assert.That(w.Health(3), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)));
        }

        [Test]
        public void TheLineIsLookedUpOnTheLandingTickAlongTheHeadingLockedAtTheStart()
        {
            // A target that is on the line at the start but has left it by tick 7 is missed; one that steps onto it is hit.
            var leaves = World(Thrust(), MeleeWorld.Unit(3, 2, Ox + 1800, Oz));
            leaves.Step(3); MoveTo(leaves, 3, Ox + 1800, Oz + 1500); leaves.Step(4);
            Assert.That(leaves.Health(3), Is.EqualTo(MeleeWorld.Full), "left the line during the windup");
            var arrives = World(Thrust(), MeleeWorld.Unit(3, 2, Ox + 1800, Oz + 1500));
            arrives.Step(3); MoveTo(arrives, 3, Ox + 1800, Oz); arrives.Step(4);
            Assert.That(arrives.Health(3), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)), "stepped onto the line during the windup");
            var turned = World(Thrust(), MeleeWorld.Unit(3, 2, Ox + 1800, Oz));
            turned.Step(3); MoveTo(turned, 2, Ox, Oz + 1000); MoveTo(turned, 3, Ox, Oz + 1800); turned.Step(4);
            Assert.That(turned.Health(2), Is.EqualTo(MeleeWorld.Full), "the heading stays where the swing started");
            Assert.That(turned.Health(3), Is.EqualTo(MeleeWorld.Full), "a target on the line a re-aimed swing would draw is missed");
        }

        [Test]
        public void TheLineLengthIsTheSpecReach_WhateverTheDistanceToTheTarget()
        {
            // The bait stands 1.5 m ahead; the heading is a direction, not a length. Reach 2 m + round end 0.2 m + collider 0.25 m.
            var w = new MeleeWorld(MeleeWorld.Pack(new[] { Thrust() }), new[]
            { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), MeleeWorld.Unit(2, 2, Ox + 1500, Oz), MeleeWorld.Unit(3, 2, Ox + 2600, Oz), MeleeWorld.Unit(4, 2, Ox + 2400, Oz + 0) });
            w.Step(7);
            Assert.That(w.Health(2), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)));
            Assert.That(w.Health(3), Is.EqualTo(MeleeWorld.Full), "2.6 m is past the reach");
            Assert.That(w.Health(4), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)), "2.4 m is inside the round end");
        }

        [Test]
        public void NobodyIsHitOutsideTheLandingTick()
        {
            // The extra target is on the line only after the landing tick (tick 7) and while the next swing is still locked out.
            var w = World(Thrust(), MeleeWorld.Unit(3, 2, Ox + 1800, Oz + 1500));
            w.Step(7);
            Assert.That(w.Health(3), Is.EqualTo(MeleeWorld.Full));
            MoveTo(w, 3, Ox + 1800, Oz);
            for (int tick = 8; tick <= 17; tick++)
            {
                w.Step();
                Assert.That(w.Health(3), Is.EqualTo(MeleeWorld.Full), "active and recovery ticks never hit, tick " + tick);
            }
        }

        [Test]
        public void ThePierceIsCappedByMaxTargets_NearestFirst()
        {
            var w = World(Thrust(maxTargets: 2), MeleeWorld.Unit(3, 2, Ox + 1500, Oz), MeleeWorld.Unit(4, 2, Ox + 1900, Oz));
            w.Step(7);
            Assert.That(w.Health(2), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)));
            Assert.That(w.Health(3), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)));
            Assert.That(w.Health(4), Is.EqualTo(MeleeWorld.Full), "the third on the line is past MaxTargets");
        }

        [Test]
        public void SwingTimingComesFromTheSpecAsForASector()
        {
            var w = World(Thrust(windup: 3));
            w.Step(3); Assert.That(w.Health(2), Is.EqualTo(MeleeWorld.Full));
            w.Step(); Assert.That(w.Health(2), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)), "lands on tick 1 + 3");
        }

        [Test]
        public void ASectorSpecKeepsItsBytesAndDigest_AndTheWidthEntersTheHashOnlyWhenSet()
        {
            var sector = new MeleeSpec(6, 2, 9, SimFix.FromInt(2), 500, SimFix.FromInt(1), SimFix.FromInt(10), 3, SimFix.Ratio(18, 10));
            var explicitZero = new MeleeSpec(6, 2, 9, SimFix.FromInt(2), 500, SimFix.FromInt(1), SimFix.FromInt(10), 3, SimFix.Ratio(18, 10), default);
            ulong Hash(MeleeSpec s) { var h = new StateHash(); s.WriteState(h); return h.Value; }
            // The field list the spec hashed before the width existed.
            var old = new StateHash();
            old.Add(6); old.Add(2); old.Add(9); old.Add(sector.Radius.Raw); old.Add(500); old.Add(sector.HalfHeight.Raw); old.Add(sector.Damage.Raw); old.Add(3); old.Add(sector.EngageRange.Raw);
            Assert.That(Hash(sector), Is.EqualTo(old.Value));
            Assert.That(Hash(explicitZero), Is.EqualTo(old.Value));
            Assert.That(Hash(Thrust()), Is.Not.EqualTo(old.Value));
            Assert.That(Hash(Thrust(0.2)), Is.Not.EqualTo(Hash(Thrust(0.3))));
            Assert.That(sector.IsThrust, Is.False); Assert.That(Thrust().IsThrust, Is.True);
            Assert.That(MeleeWorld.Pack(new[] { sector }).PolicyDigest, Is.Not.EqualTo(MeleeWorld.Pack(new[] { Thrust() }).PolicyDigest));
        }

        [Test]
        public void TheWidthIsRangeChecked()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Thrust(-0.1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Thrust(17));
            Assert.DoesNotThrow(() => Thrust(16));
        }

        private static string Json(string tweak) =>
            "{ \"id\": \"t\", \"extends\": \"rwr\", \"melee\": [ { \"id\": \"m\", \"windupTicks\": 6, \"activeTicks\": 2, \"recoveryTicks\": 9, \"radius\": 2,"
            + " \"cosHalfAnglePermille\": 500, \"halfHeight\": 1, \"damage\": 34, \"maxTargets\": 3, \"engageRange\": 1.8" + tweak + " } ],"
            + " \"unitTypes\": [ { \"id\": \"u\", \"weapon\": \"\", \"melee\": \"m\" } ] }";
        private static ContentPack Load(string json) =>
            ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string> { ["t"] = json }, FileContentSource.Rwr()), "t");

        [Test]
        public void ContentCanDefineAThrust_AndAbsenceIsASector()
        {
            Assert.That(Load(Json("")).BuildCombat().Melee[0].IsThrust, Is.False);
            MeleeSpec spec = Load(Json(", \"lineHalfWidth\": 0.2")).BuildCombat().Melee[0];
            Assert.That((spec.IsThrust, spec.LineHalfWidth), Is.EqualTo((true, SimFix.Ratio(2, 10))));
            Assert.That(Assert.Throws<ContentException>(() => Load(Json(", \"lineHalfWidth\": -1"))).Message, Does.Contain("lineHalfWidth"));
            Assert.That(Assert.Throws<ContentException>(() => Load(Json(", \"lineHalfWidth\": 17"))).Message, Does.Contain("lineHalfWidth"));
            Assert.That(Assert.Throws<ContentException>(() => Load(Json(", \"lineHalfWidths\": 1"))).Message, Does.Contain("lineHalfWidths"));
        }
    }
}
