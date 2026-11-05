using System;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // WP-20: the swing protocol. Unit 1 (team 1) carries melee spec 0; the targets carry nothing, so only unit 1 ever acts.
    public sealed class Wp20MeleeTests
    {
        private const long Ox = 20000, Oz = 20000;

        private static MeleeWorld Duel(MeleeSpec spec, long dx = 1000, long dz = 0, byte[] map = null) =>
            new MeleeWorld(MeleeWorld.Pack(new[] { spec }), new[]
            { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), MeleeWorld.Unit(2, 2, Ox + dx, Oz + dz) }, map);

        [Test]
        public void SwingLandsOnceOnTheWindupTickAndTheBusyWindowBlocksTheNext()
        {
            var w = Duel(MeleeWorld.Spec()); // windup 6, active 2, recovery 9: start 1, land 7, next start 18
            w.Step(6);
            Assert.That(w.Health(2), Is.EqualTo(MeleeWorld.Full), "the tick before the landing tick");
            Assert.That(w.Count(StrikeKind.Landed), Is.Zero);
            w.Step();
            Assert.That(w.Tick, Is.EqualTo(7)); Assert.That(w.Count(StrikeKind.Landed), Is.EqualTo(1));
            Assert.That(w.Health(2), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)));
            for (int tick = 8; tick <= 17; tick++)
            {
                w.Step();
                Assert.That(w.Health(2), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)), "active and recovery ticks never hit again, tick " + tick);
                Assert.That(w.Count(StrikeKind.Started), Is.Zero, "no new swing before the busy window ends, tick " + tick);
            }
            w.Step(); // tick 18
            Assert.That(w.Count(StrikeKind.Started), Is.EqualTo(1));
            Assert.That(w.Health(2), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)), "the start tick itself never lands");
            w.Step(5); Assert.That(w.Health(2), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)), "tick 23");
            w.Step(); Assert.That(w.Health(2), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(20)));
        }

        [Test]
        public void ShortestTimetable_WindupOne_ActiveOne_RecoveryZero()
        {
            var w = Duel(MeleeWorld.Spec(1, 1, 0));
            var expected = new[] { 0, 1, 1, 2, 2, 3 }; // swings start on 1, 3, 5 and land on 2, 4, 6
            for (int tick = 1; tick <= 6; tick++)
            {
                w.Step();
                Assert.That(w.Health(2), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10 * expected[tick - 1])), "tick " + tick);
            }
        }

        // The striker stands at the origin and the bait 1 m ahead (+X) fixes the heading. The probe is the unit under test.
        // Sector semantics are the engine's: a collider that touches the sector counts, so the centre may sit just outside.
        [TestCase(1500, 0, true, TestName = "ahead inside")]
        [TestCase(-1100, 0, false, TestName = "behind")]
        [TestCase(0, 1900, false, TestName = "side, 90 degrees")]
        [TestCase(727, 1312, true, TestName = "centre 61 degrees, collider still touches the 60 degree edge")]
        [TestCase(388, 1449, false, TestName = "centre 75 degrees, whole collider outside")]
        [TestCase(1000, 1000, true, TestName = "45 degrees inside")]
        [TestCase(2100, 0, true, TestName = "centre past the radius, near edge inside")]
        [TestCase(2400, 0, false, TestName = "whole collider past the radius")]
        public void SectorEdges(long dx, long dz, bool hit)
        {
            var w = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec(maxTargets: 4) }), new[]
            { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), MeleeWorld.Unit(2, 2, Ox + 1000, Oz), MeleeWorld.Unit(3, 2, Ox + dx, Oz + dz) });
            w.Step(7);
            Assert.That(w.Health(2), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)), "the bait is always hit");
            Assert.That(w.Health(3), Is.EqualTo(hit ? MeleeWorld.Full - MeleeWorld.Cost(10) : MeleeWorld.Full));
        }

        [Test]
        public void ConeAngleIsRawFixedPoint_ZeroCosineIsAFullHalfPlane()
        {
            var narrow = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec(cos: 1000, maxTargets: 4) }), new[]
            { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), MeleeWorld.Unit(2, 2, Ox + 1000, Oz), MeleeWorld.Unit(3, 2, Ox + 1000, Oz + 600) });
            narrow.Step(7);
            Assert.That(narrow.Health(3), Is.EqualTo(MeleeWorld.Full), "cosine 1000 is a line, a unit beside it is outside");
            var half = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec(cos: 0, maxTargets: 4) }), new[]
            { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), MeleeWorld.Unit(2, 2, Ox + 1000, Oz), MeleeWorld.Unit(3, 2, Ox, Oz + 1500) });
            half.Step(7);
            Assert.That(half.Health(3), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)), "cosine 0 reaches 90 degrees to the side");
        }

        [Test]
        public void AWallBetweenStrikerAndTargetStopsTheSwing()
        {
            // Wall cells x 21..22, z 17..23: it stands between the striker (x 20) and the probe (x 22.4), not around the bait (z 20, x 20.9).
            byte[] map = WcMapWriter.Write(M3TestWorld.Boxes(80, 40, (21, 14, 1, 4), (21, 22, 1, 4)));
            var open = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec() }), new[]
            { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), MeleeWorld.Unit(2, 2, Ox + 1000, Oz), MeleeWorld.Unit(3, 2, Ox + 1500, Oz + 1500) }, map);
            open.Step(7);
            Assert.That(open.Health(3), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)), "the gap in the wall leaves the line open");
            byte[] blocked = WcMapWriter.Write(M3TestWorld.Boxes(80, 40, (21, 14, 1, 8)));
            var closed = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec() }), new[]
            { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), MeleeWorld.Unit(2, 2, Ox + 1000, Oz + 4000), MeleeWorld.Unit(3, 2, Ox + 1800, Oz + 600) }, blocked);
            // Auto needs the bait to be visible and within reach to start at all; use an Aimed swing at the walled side instead.
            closed.Intent = c => { if (c.Clock.Tick == 1) closed.W.Combat.Submit(c, FireIntent.Aimed(new StableId(closed.Id(1)), SimVec3.Metres(30, 0, 20))); };
            closed.Step(7);
            Assert.That(closed.Health(3), Is.EqualTo(MeleeWorld.Full), "the wall is in the way");
            Assert.That(closed.Count(StrikeKind.Whiffed), Is.EqualTo(1));
        }

        [Test]
        public void ThreeTargetsEachTakeOneHit_ThenOnlyTheNearestTwoWhenCappedAtTwo()
        {
            UnitSpawn[] Units() => new[]
            {
                MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), MeleeWorld.Unit(2, 2, Ox + 1500, Oz + 100), MeleeWorld.Unit(3, 2, Ox + 1000, Oz - 100),
                MeleeWorld.Unit(4, 2, Ox + 1200, Oz + 500),
            };
            var all = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec(maxTargets: 3) }), Units());
            all.Step(7);
            foreach (ulong key in new ulong[] { 2, 3, 4 }) Assert.That(all.Health(key), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)), "unit " + key);
            Assert.That(all.Count(StrikeKind.Landed), Is.EqualTo(3));
            var two = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec(maxTargets: 2) }), Units());
            two.Step(7);
            Assert.That(two.Health(3), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)), "nearest");
            Assert.That(two.Health(4), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)), "second nearest");
            Assert.That(two.Health(2), Is.EqualTo(MeleeWorld.Full), "the farthest is simply not reached; this is the rule, not a capacity loss");
            var order = new System.Collections.Generic.List<ulong>();
            foreach (var s in two.W.Combat.Strikes) if (s.Kind == StrikeKind.Landed) order.Add(s.Target.Value);
            Assert.That(order, Is.EqualTo(new[] { two.Id(3), two.Id(4) }), "applied nearest first");
        }

        [Test]
        public void EqualDistanceTargetsAreOrderedByStableId()
        {
            // +-0.25 m are exact binary fractions, so the two distances are equal to the last raw bit.
            var w = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec(maxTargets: 1) }), new[]
            { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), MeleeWorld.Unit(5, 2, Ox + 1000, Oz + 250), MeleeWorld.Unit(3, 2, Ox + 1000, Oz - 250) });
            w.Step(7);
            // Equal distance: the smaller StableId (not the smaller key) is struck first.
            ulong first = new StableId(w.Id(3)).CompareTo(new StableId(w.Id(5))) < 0 ? 3UL : 5UL, second = first == 3 ? 5UL : 3UL;
            Assert.That(w.Health(first), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)));
            Assert.That(w.Health(second), Is.EqualTo(MeleeWorld.Full));
        }

        [Test]
        public void FriendlyFireSwitch_OffTeammateIsNeitherHurtNorInTheWay_OnTeammateIsHurt()
        {
            UnitSpawn[] Units() => new[]
            {
                MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), MeleeWorld.Unit(2, 1, Ox + 700, Oz), MeleeWorld.Unit(3, 2, Ox + 1500, Oz),
            };
            var off = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec() }), Units());
            off.Step(7);
            Assert.That(off.Health(2), Is.EqualTo(MeleeWorld.Full), "teammate not hurt");
            Assert.That(off.Health(3), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)), "and does not shield the enemy behind it");
            var on = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec() }, friendlyFire: true), Units());
            on.Step(7);
            Assert.That(on.Health(2), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)), "friendly fire on: the teammate takes the blow");
            Assert.That(on.Health(3), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)));
        }

        [Test]
        public void HeadingAndOriginAreLockedAtTheStart()
        {
            var w = Duel(MeleeWorld.Spec());
            w.Step(2);
            // The target steps out of the sector behind the striker; the swing still lands where it was aimed.
            w.W.Set(2, new Position(new SimVec3(SimFix.FromInt(0) + SimFix.Ratio(Ox - 1500, 1000), w.W.Read<Position>(2).Value.Y, SimFix.Ratio(Oz, 1000))));
            w.Step(5);
            Assert.That(w.Health(2), Is.EqualTo(MeleeWorld.Full), "target left the sector: the swing finds nobody");
            Assert.That(w.Count(StrikeKind.Whiffed), Is.EqualTo(1));
            // The striker walks 1.5 m away after starting (past its reach from there); the target stays: the swing is still cast from the locked origin and hits.
            var m = Duel(MeleeWorld.Spec());
            m.Step(2);
            m.W.Set(1, new Position(new SimVec3(SimFix.Ratio(Ox - 1500, 1000), m.W.Read<Position>(1).Value.Y, SimFix.Ratio(Oz, 1000))));
            m.Step(5);
            Assert.That(m.Health(2), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)));
        }

        [Test]
        public void ARunOutCommandCapacityIsAHardErrorNotASilentDrop()
        {
            // Two units, one hit per action: the tick has room for two damage commands. A test system spends both before the swing lands;
            // the Sim itself refuses the third command, so a swing can never lose a hit without anyone noticing.
            var w = Duel(MeleeWorld.Spec(maxTargets: 1));
            w.FillCommandsAt(7, 2, 2);
            w.Step(6);
            var ex = Assert.Throws<SimBoundaryException>(() => w.Step());
            Assert.That(ex.Message, Does.Contain("M0C_RELEASE_CAPACITY"));
        }

        // Engine semantics inherited by the swing: a collider touching the sector counts, edges included. Quarter-metre unit colliders,
        // target placed to the raw unit (1/65536 m) relative to the striker; the aim is +X.
        [TestCase(0, 0, true, TestName = "radius plus collider, exactly")]
        [TestCase(1, 0, false, TestName = "radius plus collider, one raw unit further")]
        [TestCase(0, 1, false, TestName = "half plane (cos 0): centre on the far side by one raw unit")]
        public void SectorBoundaryIsExactToOneRawUnit(long radialOffset, long planeSide, bool hit)
        {
            const long Q = 65536 / 4;
            bool plane = planeSide != 0;
            var w = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec(cos: plane ? 0 : 500) }), new[]
            { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), MeleeWorld.Unit(2, 2, Ox + 5000, Oz + 5000) });
            w.Intent = c => { if (c.Clock.Tick == 1) w.W.Combat.Submit(c, FireIntent.Aimed(new StableId(w.Id(1)), SimVec3.Metres(30, 0, 20))); };
            SimVec3 p = w.W.Read<Position>(1).Value;
            long dx = plane ? -Q - planeSide : 2 * 65536 + Q + radialOffset, dz = plane ? 65536 : 0;
            w.W.Set(2, new Position(new SimVec3(SimFix.FromRaw(p.X.Raw + dx), p.Y, SimFix.FromRaw(p.Z.Raw + dz))));
            w.Step(7);
            Assert.That(w.Health(2), Is.EqualTo(hit ? MeleeWorld.Full - MeleeWorld.Cost(10) : MeleeWorld.Full));
        }

        [Test]
        public void ATargetThatWalksIntoTheSectorBeforeTheLandingTickIsHit()
        {
            // The sector is looked up again on the landing tick (not fixed at the start): the probe stands behind the striker when
            // the swing starts, and steps in front of it during the windup.
            var w = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec(maxTargets: 4) }), new[]
            { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), MeleeWorld.Unit(2, 2, Ox + 1000, Oz), MeleeWorld.Unit(3, 2, Ox - 1100, Oz) });
            w.Step(3);
            w.W.Set(3, new Position(new SimVec3(SimFix.Ratio(Ox + 1500, 1000), w.W.Read<Position>(3).Value.Y, SimFix.Ratio(Oz, 1000))));
            w.Step(4);
            Assert.That(w.Health(3), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)));
        }

        [Test]
        public void StrikerKilledDuringTheWindupNeverLands()
        {
            var w = Duel(MeleeWorld.Spec());
            w.W.Sim.RegisterGameplay("test.death", new M6Damage(w.W, 3, 1), SimSystemPhase.Queries);
            w.Step(10);
            Assert.That(w.W.Dead(1), Is.True);
            Assert.That(w.Count(StrikeKind.Landed), Is.Zero);
            Assert.That(w.Health(2), Is.EqualTo(MeleeWorld.Full));
            MeleeState state = w.W.Read<MeleeState>(1);
            Assert.That(state.Pending, Is.False, "the dead striker's swing is cancelled, not left pending in the saved state");
            Assert.That(state.Origin, Is.EqualTo(default(SimVec3))); Assert.That(state.DirX, Is.Zero); Assert.That(state.DirZ, Is.Zero);
        }

        [Test]
        public void SameTickSwingsBothLand_TheKillsSettleAfterTheQueries()
        {
            var specs = new[] { MeleeWorld.Spec(windup: 3, damage: 100) };
            var w = new MeleeWorld(MeleeWorld.Pack(specs), new[] { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), MeleeWorld.Unit(2, 2, Ox + 1000, Oz, melee: 0) });
            w.Step(4);
            Assert.That(w.W.Dead(1), Is.True); Assert.That(w.W.Dead(2), Is.True, "both swings landed before either death settled");
        }

        [Test]
        public void HeldInputDoesNotRestartASwingInFlight_AndTheNextOneStartsWhenTheBusyWindowEnds()
        {
            var w = Duel(MeleeWorld.Spec(), dx: 3000);
            w.Intent = c => w.W.Combat.Submit(c, FireIntent.Aimed(new StableId(w.Id(1)), SimVec3.Metres(30, 0, 20)));
            var starts = new System.Collections.Generic.List<long>();
            for (int tick = 1; tick <= 40; tick++) { w.Step(); foreach (var s in w.W.Combat.Strikes) if (s.Kind == StrikeKind.Started) starts.Add(s.Tick); }
            Assert.That(starts, Is.EqualTo(new long[] { 1, 18, 35 }), "one swing per 17-tick busy window");
        }

        [Test]
        public void ManyStrikersInOneTickMatchTheTotalOfSingleSwings()
        {
            var units = new System.Collections.Generic.List<UnitSpawn>();
            for (int i = 0; i < 5; i++) units.Add(MeleeWorld.Unit((ulong)i + 1, 1, Ox, Oz - 1000 + i * 500, melee: 0));
            for (int i = 0; i < 3; i++) units.Add(MeleeWorld.Unit((ulong)i + 10, 2, Ox + 1200, Oz - 400 + i * 400));
            var w = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec(maxTargets: 3) }), units.ToArray());
            w.Step(7);
            Assert.That(w.Count(StrikeKind.Landed), Is.EqualTo(15), "five strikers, three targets each, none dropped");
            foreach (ulong key in new ulong[] { 10, 11, 12 }) Assert.That(w.Health(key), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(50)), "unit " + key);
            Assert.That(w.W.Combat.Strikes.Length, Is.EqualTo(15));
        }

        [Test]
        public void CapacityScalesWithTheLargestSwing_AndNotAtAllWithoutMelee()
        {
            var none = MeleeWorld.Pack(null);
            Assert.That(none.MaxHitsPerAction, Is.EqualTo(1)); Assert.That(none.HasMelee, Is.False);
            var pack = MeleeWorld.Pack(new[] { MeleeWorld.Spec(maxTargets: 2), MeleeWorld.Spec(maxTargets: 7) });
            Assert.That(pack.MaxHitsPerAction, Is.EqualTo(7));
            Assert.That(pack.BuildConfig(10, 8).EventHardLimit, Is.EqualTo(8 * (7 + 2)));
            Assert.That(none.BuildConfig(10, 8).EventHardLimit, Is.EqualTo(3 * 8), "the no-melee formula is the old one");
        }

        [Test]
        public void MeleeSpecRejectsEveryIllegalValue()
        {
            SimFix m(int v) => SimFix.FromInt(v);
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeSpec(0, 1, 0, m(2), 500, m(1), m(10), 3, m(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeSpec(601, 1, 0, m(2), 500, m(1), m(10), 3, m(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeSpec(1, 0, 0, m(2), 500, m(1), m(10), 3, m(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeSpec(1, 1, -1, m(2), 500, m(1), m(10), 3, m(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeSpec(1, 1, 0, m(0), 500, m(1), m(10), 3, m(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeSpec(1, 1, 0, m(17), 500, m(1), m(10), 3, m(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeSpec(1, 1, 0, m(2), -1, m(1), m(10), 3, m(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeSpec(1, 1, 0, m(2), 1001, m(1), m(10), 3, m(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeSpec(1, 1, 0, m(2), 500, m(0), m(10), 3, m(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeSpec(1, 1, 0, m(2), 500, m(1), SimFix.FromRaw(-1), 3, m(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeSpec(1, 1, 0, m(2), 500, m(1), m(1_000_001), 3, m(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeSpec(1, 1, 0, m(2), 500, m(1), m(10), 0, m(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeSpec(1, 1, 0, m(2), 500, m(1), m(10), 17, m(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeSpec(1, 1, 0, m(2), 500, m(1), m(10), 3, m(0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeSpec(1, 1, 0, m(2), 500, m(1), m(10), 3, m(3)), "engage range beyond the radius");
            Assert.DoesNotThrow(() => new MeleeSpec(1, 1, 0, m(2), 0, m(1), m(0), 1, m(2)));
        }
    }
}
