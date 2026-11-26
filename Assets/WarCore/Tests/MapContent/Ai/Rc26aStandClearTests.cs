using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.LedGame;

namespace WarCore.Tests
{
    // RC2-6a, rule R4: a member whose place the leader's body is on stands a step aside of it (Battle/Ai/LedSquad.cs: Clear,
    // Aside, Stand, Upkeep and TryPlace). Of the sides that can be stood on and are clear of the leader it keeps the one it
    // stands at or walks to, else takes the one pointing most away from the leader (the lower of two alike). The flat
    // fields are 80 m by 40 m and nobody is armed; the leader (unit 1) stands at (10, 20) and a MoveTo command puts the
    // anchor where the test wants the places. Every body is 0.25 m (16384 raw): two of them overlap closer than 0.5 m.
    public sealed partial class Rc26aStandClearTests
    {
        // The first ring of a 2 m lattice (GuardSlots.Build), sides 0 to 5 = NW, NE, E, SE, SW, W: dx is 1 m or 2 m, dz is
        // 2 m * 866025 / 1000000 = 113511 raw (truncated). Half a step halves each part, truncated toward zero; it is longer
        // than the two radii (32768^2 + 56755^2 and 65536^2 >= 32768^2), so it is the step aside.
        internal static readonly (long X, long Z)[] Steps2 = { (-65536, 113511), (65536, 113511), (131072, 0), (65536, -113511), (-65536, -113511), (-131072, 0) };
        internal static readonly (long X, long Z)[] Halves2 = { (-32768, 56755), (32768, 56755), (65536, 0), (32768, -56755), (-32768, -56755), (-65536, 0) };
        // The same for 1 m: dz = 65536 * 866025 / 1000000 = 56755. Half a step E or W, (32768, 0), is just the two radii long:
        // the step aside (the bodies touch). Half a slanting step, (16384, 28377), falls short of them by a hair (16384^2 +
        // 28377^2 = 1073689585 < 32768^2 = 1073741824): the step aside is the full step * reach / 65536 (truncated toward zero)
        // for the least reach from 32768 up that is long enough, 32769: (16384, 28378), 1073746340 long squared.
        internal static readonly (long X, long Z)[] Steps1 = { (-32768, 56755), (32768, 56755), (65536, 0), (32768, -56755), (-32768, -56755), (-65536, 0) };
        internal static readonly (long X, long Z)[] Aside1 = { (-16384, 28378), (16384, 28378), (32768, 0), (16384, -28378), (-16384, -28378), (-32768, 0) };
        // Two default bodies touch at 0.5 m.
        internal const long Touch = 2 * BodyRadius.DefaultRaw;

        private static ContentPack Pack(int size, int spacingMm) => ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
        {
            ["rc26a-clear"] = "{ \"id\": \"rc26a-clear\", \"extends\": \"rwr\", \"ai\": { \"slotSpacingMm\": " + spacingMm + ", \"ledSize\": " + size
                + ", \"ledRecruitRadiusMm\": 30000 } }"
        }, FileContentSource.Rwr()), "rc26a-clear");

        // A flat field under a pack with a led squad of that size and lattice spacing; the first unit is the player's. A squad
        // of one has spot 0 only, so its member's place is the anchor plus side 0's step.
        internal static LedGame Field(int size, int spacingMm, params UnitSpawn[] units) =>
            new LedGame(new M6World(M6World.RecipeOf(units).WithRuleSet(MatchMode.TestNeverEndsId).WithPack(Pack(size, spacingMm)).WithPlayer(units[0].Key),
                WcMapWriter.Write(M3TestWorld.Boxes(80, 40))));

        // The leader at (10, 20) and unit 2 in a squad of one with a 2 m lattice.
        private static LedGame Pair(int xMm, int zMm, int speed = 6000) => Field(1, 2000, Soldier(1, 10, 20), Mm(2, xMm, zMm, speed));

        // A soldier of team 1 at millimetres.
        internal static UnitSpawn Mm(ulong key, int xMm, int zMm, int speed = 6000) => new UnitSpawn(key, 0, 1, xMm, zMm, speed, 1);

        internal static SimVec3 Plus(SimVec3 p, (long X, long Z) d) => new SimVec3(SimFix.FromRaw(p.X.Raw + d.X), p.Y, SimFix.FromRaw(p.Z.Raw + d.Z));
        internal static SimVec3 Minus(SimVec3 p, (long X, long Z) d) => Plus(p, (-d.X, -d.Z));
        internal static long Raw(int mm) => SimFix.Ratio(mm, 1000).Raw;
        // The anchor that puts the place of that spot at the point (a 2 m lattice unless said otherwise).
        internal static SimVec3 AnchorFor(SimVec3 place, int spot, (long X, long Z)[] steps = null) => Minus(place, (steps ?? Steps2)[spot]);
        internal static (long, long) Xz(SimVec3 p) => (p.X.Raw, p.Z.Raw);
        internal static long Squared(SimVec3 a, SimVec3 b)
        {
            long x = a.X.Raw - b.X.Raw, z = a.Z.Raw - b.Z.Raw;
            return x * x + z * z;
        }
        internal static bool Overlap(SimVec3 a, SimVec3 b) => Squared(a, b) < Touch * Touch;

        // Where the member is to stand now, by the squad's own answer.
        internal static SimVec3 StandOf(LedGame g, ulong key)
        {
            Assert.That(g.Led.TryPlace(g.Id(key), out SimVec3 at), Is.True, "member " + key + " has somewhere to stand");
            return at;
        }

        // The point the member's walk order goes to (it must have one) on that layer.
        internal static SimVec3 Target(LedGame g, ulong key, int layer = 0)
        {
            Assert.That(g.Moves.TryGetOrder(g.Id(key), out SimVec3 target, out int on), Is.True, "member " + key + " has a walk order");
            Assert.That(on, Is.EqualTo(layer), "member " + key + " is sent to layer " + layer);
            return target;
        }

        internal static bool HasOrder(LedGame g, ulong key) => g.Moves.TryGetOrder(g.Id(key), out _, out _);

        // Steps the leader with that input until the squad has seen it on the place (on: true) or off it (false); the squad
        // looks before the leader's step of the tick. At most 20 ticks.
        private static bool LeaderWalksUntil(LedGame g, InputFrame input, SimVec3 place, bool on)
        {
            for (int t = 0; t < 20; t++)
            {
                SimVec3 seen = g.At(g.Player);
                g.Step(input);
                if (Overlap(seen, place) == on) return true;
            }
            return false;
        }

        // The world's move orders behind a stand-in: nobody can stand within 0.1 m of a blocked point; one unit's walk orders
        // (all of them, those to Point, or those to anywhere else) are refused the world's way (the unit is left without an
        // order); every walk order that unit is given is written down.
        private sealed class StandIn : IMoveOrders
        {
            private const long Near = 6554;
            private readonly IMoveOrders inner;
            internal readonly List<SimVec3> Blocked = new List<SimVec3>();
            internal readonly List<SimVec3> Given = new List<SimVec3>();
            internal ulong Unit;
            internal SimVec3 Point;
            internal bool RefuseAll, RefusePoint, RefuseElsewhere;
            internal StandIn(IMoveOrders inner) { this.inner = inner; }
            private bool Open(SimVec3 at)
            {
                foreach (SimVec3 b in Blocked)
                    if (Math.Abs(at.X.Raw - b.X.Raw) <= Near && Math.Abs(at.Z.Raw - b.Z.Raw) <= Near) return false;
                return true;
            }
            public bool CanStandAt(SimVec3 at) => Open(at) && inner.CanStandAt(at);
            public bool CanStandAt(SimVec3 at, int layer) => Open(at) && inner.CanStandAt(at, layer);
            public bool TryStandAt(SimVec3 at, int layer, out SimVec3 grounded)
            {
                grounded = at;
                return Open(at) && inner.TryStandAt(at, layer, out grounded);
            }
            public bool IsCommanded(StableId unit) => inner.IsCommanded(unit);
            public void Order(StableId unit, SimVec3 target) => inner.Order(unit, target);
            public void Order(StableId unit, SimVec3 target, int layer)
            {
                if (unit.Value == Unit)
                {
                    Given.Add(target);
                    bool there = target.X.Raw == Point.X.Raw && target.Z.Raw == Point.Z.Raw;
                    if (RefuseAll || (there ? RefusePoint : RefuseElsewhere)) { inner.Stop(unit); return; }
                }
                inner.Order(unit, target, layer);
            }
            public void Stop(StableId unit) => inner.Stop(unit);
            public bool TryGetOrder(StableId unit, out SimVec3 target) => inner.TryGetOrder(unit, out target);
            public bool TryGetOrder(StableId unit, out SimVec3 target, out int layer) => inner.TryGetOrder(unit, out target, out layer);
            public bool HasFailed(StableId unit) => inner.HasFailed(unit);
            public bool TryRouteCost(SimVec3 from, SimVec3 to, long limitCost, out long cost) => inner.TryRouteCost(from, to, limitCost, out cost);
        }

        private static StandIn Install(LedGame g, ulong key)
        {
            var orders = new StandIn(g.Moves) { Unit = g.Id(key).Value };
            g.Moves = orders;
            return orders;
        }

        // T1. Anchor (8, 20) + spot 2's step (2, 0) = (10, 20), the leader's own point. Unit 2 stands 4 m east of it, 3 m from
        // the nearest side point, without a walk order. With the leader on the place every side points away alike (dot 0), so
        // side 0, NW: (10, 20) + (-32768, 56755) raw. Unit 3 takes spot 5, (6, 20), a metre from it, and walks there.
        [Test]
        public void ALeaderOnAPlaceSendsThatMemberHalfAStepToTheFirstSideAndNobodyElse()
        {
            LedGame g = Field(8, 2000, Soldier(1, 10, 20), Soldier(2, 14, 20), Soldier(3, 5, 20));
            SimVec3[] offsets = LedSquad.Formation(8, g.SpacingRaw);
            for (int side = 0; side < 6; side++) Assert.That(Xz(offsets[side]), Is.EqualTo(Steps2[side]), "the first ring of the lattice, side " + side);
            SimVec3 leader = g.At(1), anchor = AnchorFor(leader, 2);
            g.Order(SquadCommandKind.MoveTo, anchor);
            Assert.That((g.Led.SpotOf(g.Id(2)), g.Led.SpotOf(g.Id(3))), Is.EqualTo((2, 5)), "each takes the spot whose place is nearest to it");
            Assert.That(Xz(StandOf(g, 2)), Is.EqualTo((leader.X.Raw - 32768, leader.Z.Raw + 56755)));
            Assert.That(Xz(Target(g, 2)), Is.EqualTo((leader.X.Raw - 32768, leader.Z.Raw + 56755)), "walked there");
            SimVec3 own = Plus(anchor, Steps2[5]);
            Assert.That(Xz(StandOf(g, 3)), Is.EqualTo(Xz(own)), "unit 3 stands on its own place");
            Assert.That(Xz(Target(g, 3)), Is.EqualTo(Xz(own)));
        }

        // T2. The same, but unit 2 stands on the east side's point, (10, 20) + (65536, 0) = (11, 20), clear of the leader: it
        // keeps that side, gets no walk order and is still there 30 ticks on.
        [Test]
        public void AMemberAlreadyOnASidePointStaysThere()
        {
            LedGame g = Pair(11000, 20000);
            StandIn orders = Install(g, 2);
            SimVec3 leader = g.At(1), east = Plus(leader, Halves2[2]);
            Assert.That(Xz(g.At(2)), Is.EqualTo(Xz(east)));
            g.Order(SquadCommandKind.MoveTo, AnchorFor(leader, 0));
            Assert.That(Xz(StandOf(g, 2)), Is.EqualTo(Xz(east)));
            Assert.That(HasOrder(g, 2), Is.False, "it is where it is to stand");
            g.Wait(30);
            Assert.That(Xz(StandOf(g, 2)), Is.EqualTo(Xz(east)));
            Assert.That(Xz(g.At(2)), Is.EqualTo(Xz(east)), "it did not move");
            Assert.That(orders.Given.Count, Is.EqualTo(0), "it was given no walk order");
        }

        // T3. Unit 2's place is gapMm from the leader towards the NW (gapMm / 2 west, gapMm * 0.866 north): closer than the two
        // radii (0.5 m), farther than the leader's own (0.25 m). The leader - place points SE, so of the dot products with
        // the steps side 0's is the least (-1 m * gapMm), and its point is gapMm + 1 m from the leader. Unit 2 stands 3 m east
        // of its place, without a walk order.
        [TestCase(400)]
        [TestCase(499)]
        public void ALeaderCloserToThePlaceThanTheTwoRadiiIsStoodAsideOf(int gapMm)
        {
            LedGame g = Pair(13000 - gapMm / 2, 20000 + gapMm * 866 / 1000);
            Assert.That((g.W.Read<Collider>(1).Radius.Raw, g.W.Read<Collider>(2).Radius.Raw), Is.EqualTo((BodyRadius.DefaultRaw, BodyRadius.DefaultRaw)), "two bodies of 0.25 m");
            SimVec3 leader = g.At(1);
            SimVec3 place = Plus(leader, (-SimFix.Ratio(gapMm, 2000).Raw, SimFix.Ratio(gapMm * 866025L, 1000000000L).Raw));
            Assert.That(Overlap(leader, place), Is.True, "closer than the two radii");
            Assert.That(Squared(leader, place), Is.GreaterThan(BodyRadius.DefaultRaw * BodyRadius.DefaultRaw), "farther than the leader's radius");
            g.Order(SquadCommandKind.MoveTo, AnchorFor(place, 0));
            SimVec3 aside = Plus(place, Halves2[0]);
            Assert.That(Xz(StandOf(g, 2)), Is.EqualTo(Xz(aside)));
            Assert.That(Xz(Target(g, 2)), Is.EqualTo(Xz(aside)), "walked there");
        }

        // T4. Unit 2's place is gapMm due east of the leader. At 500 mm, exactly the two radii (32768 raw), the bodies touch
        // and do not overlap: unit 2 stands on its place; farther, too.
        [TestCase(500)]
        [TestCase(501)]
        [TestCase(1500)]
        public void ALeaderAtTheTwoRadiiOrFartherFromThePlaceIsNotStoodAsideOf(int gapMm)
        {
            LedGame g = Pair(13000 + gapMm, 20000);
            SimVec3 leader = g.At(1), place = Plus(leader, (Raw(gapMm), 0));
            Assert.That(Overlap(leader, place), Is.False);
            g.Order(SquadCommandKind.MoveTo, AnchorFor(place, 0));
            Assert.That(Xz(StandOf(g, 2)), Is.EqualTo(Xz(place)));
            Assert.That(Xz(Target(g, 2)), Is.EqualTo(Xz(place)), "walked to its place");
        }

        // T5. The leader stands (dx, dz) mm from unit 2's place; unit 2 is 3 m east of the place, without a walk order. The
        // side taken is the one whose step has the least dot product with (dx, dz), the lower of two alike (raw, 2 m steps):
        // (-150, 260) mm: SE (32768 * -9830 + -56755 * 17039 = -1.29e9) is the least; NW is the most (the first version's side);
        // (300, 0): W; (-300, 0): E; (0, 300): SE and SW alike, SE; (0, -300): NW and NE alike, NW. Every side point is at
        // least 0.7 m from the leader.
        [TestCase(-150, 260, 3)]
        [TestCase(300, 0, 5)]
        [TestCase(-300, 0, 2)]
        [TestCase(0, 300, 3)]
        [TestCase(0, -300, 0)]
        public void TheMemberStandsAsideToTheSidePointingMostAwayFromTheLeader(int dxMm, int dzMm, int side)
        {
            LedGame g = Pair(13000 - dxMm, 20000 - dzMm);
            SimVec3 leader = g.At(1), place = Minus(leader, (Raw(dxMm), Raw(dzMm)));
            Assert.That(Overlap(leader, place), Is.True);
            g.Order(SquadCommandKind.MoveTo, AnchorFor(place, 0));
            SimVec3 aside = Plus(place, Halves2[side]);
            Assert.That(Xz(StandOf(g, 2)), Is.EqualTo(Xz(aside)));
            Assert.That(Xz(Target(g, 2)), Is.EqualTo(Xz(aside)), "walked there");
        }

        // T6. As T1 (a squad of one): unit 2 walks to NW, (9.5, 20.866); the leader walks south at 0.2 m a tick. While the
        // squad saw the leader within 0.5 m of the place unit 2 is to stand at NW, from then on on its place, and it is sent
        // back there on the first tick; it ends on its place.
        [Test]
        public void OnceTheLeaderIsOffThePlaceTheMemberGoesBackToIt()
        {
            LedGame g = Pair(14000, 20000);
            SimVec3 place = g.At(1), nw = Plus(place, Halves2[0]);
            g.Order(SquadCommandKind.MoveTo, AnchorFor(place, 0));
            Assert.That(Xz(StandOf(g, 2)), Is.EqualTo(Xz(nw)));
            bool off = false;
            int on = 0;
            for (int t = 0; t < 15 && !off; t++)
            {
                SimVec3 seen = g.At(1);
                g.Step(Walk(0, -1000));
                off = !Overlap(seen, place);
                if (!off) on++;
                Assert.That(Xz(StandOf(g, 2)), Is.EqualTo(Xz(off ? place : nw)), "tick " + t);
            }
            Assert.That(off, Is.True, "the leader walked off the place");
            Assert.That(on, Is.GreaterThan(0), "after standing on it for a while");
            Assert.That(Xz(Target(g, 2)), Is.EqualTo(Xz(place)), "sent back to its place at once");
            g.Wait(60);
            Assert.That(Xz(StandOf(g, 2)), Is.EqualTo(Xz(place)));
            Assert.That(g.Walking(2), Is.False);
            Assert.That(Metres(g.At(2), place), Is.LessThanOrEqualTo(0.25), "it stands on its place");
        }

        // T7 setup: as T1, then the leader takes two steps north (0.4 m) and stands, still on the place. From the leader at
        // (0, 0.4) m off the place SE and SW point most away (dot -56755 * 26214 each), and SE is the lower; NW, which unit 2
        // walks to, is 0.68 m from the leader and still clear.
        private static LedGame Shifted(out SimVec3 nw)
        {
            LedGame g = Pair(14000, 20000);
            SimVec3 place = g.At(1);
            nw = Plus(place, Halves2[0]);
            g.Order(SquadCommandKind.MoveTo, AnchorFor(place, 0));
            Assert.That(Xz(Target(g, 2)), Is.EqualTo(Xz(nw)));
            for (int t = 0; t < 3; t++)
            {
                g.Step(t < 2 ? Walk(0, 1000) : default);
                Assert.That(Xz(StandOf(g, 2)), Is.EqualTo(Xz(nw)), "tick " + t);
                Assert.That(Xz(Target(g, 2)), Is.EqualTo(Xz(nw)), "tick " + t);
            }
            Assert.That(Overlap(g.At(1), place), Is.True, "the leader is still on the place");
            Assert.That(g.At(1).Z.Raw - place.Z.Raw, Is.GreaterThan(0L), "north of it");
            return g;
        }

        // T7. A member on its way to a side keeps it while the leader shifts about, and stands there.
        [Test]
        public void AMemberOnItsWayToASideKeepsItWhileTheLeaderShifts()
        {
            LedGame g = Shifted(out SimVec3 nw);
            for (int t = 0; t < 60 && g.Walking(2); t++)
            {
                g.Wait(1);
                Assert.That(Xz(StandOf(g, 2)), Is.EqualTo(Xz(nw)), "tick " + t);
            }
            Assert.That(g.Walking(2), Is.False);
            Assert.That(Metres(g.At(2), nw), Is.LessThanOrEqualTo(0.25), "it stands at NW");
            Assert.That(Xz(StandOf(g, 2)), Is.EqualTo(Xz(nw)));
        }

        // T8. A 1 m lattice: the steps aside E and W are half a step, the two radii (Aside1). Unit 2 stands on the east side's
        // point (10.5, 20), the leader on the place: it keeps east. The leader takes one walk step east (Ratio(200, 1000) =
        // 13107 raw; on that walking tick unit 2 stays, RC2-6a Passed): now 13107 raw from the place and 32768 - 13107 = 19661
        // raw from the east point, so east is no side any more; W points most away and is 45875 raw (0.7 m) from the leader.
        [Test]
        public void OnAOneMetreLatticeASideTheLeaderComesOntoIsLeftForTheSideMostAway()
        {
            LedGame g = Field(1, 1000, Soldier(1, 10, 20), Mm(2, 10500, 20000));
            SimVec3[] offsets = LedSquad.Formation(1, g.SpacingRaw);
            for (int side = 0; side < 6; side++) Assert.That(Xz(offsets[side]), Is.EqualTo(Steps1[side]), "the first ring of a 1 m lattice, side " + side);
            SimVec3 place = g.At(1), east = Plus(place, Aside1[2]), west = Plus(place, Aside1[5]);
            Assert.That(Metres(g.At(2), east), Is.LessThanOrEqualTo(0.25));
            g.Order(SquadCommandKind.MoveTo, AnchorFor(place, 0, Steps1));
            Assert.That(Xz(StandOf(g, 2)), Is.EqualTo(Xz(east)));
            Assert.That(HasOrder(g, 2), Is.False);
            g.Step(Walk(1000, 0));
            SimVec3 seen = g.At(1);
            Assert.That((seen.X.Raw - place.X.Raw, seen.Z.Raw - place.Z.Raw), Is.EqualTo((Raw(200), 0L)), "the leader stepped east");
            Assert.That(Overlap(seen, place) && Overlap(seen, east), Is.True, "on the place and on the east point");
            g.Wait(1);
            Assert.That(Xz(StandOf(g, 2)), Is.EqualTo(Xz(west)));
            Assert.That(Xz(Target(g, 2)), Is.EqualTo(Xz(west)), "sent west");
        }

        // T9. The leader stands 300 mm east of the place: W points most away, but nobody can stand there; NW and SW come next,
        // alike (-32768 * 19661 each): NW.
        [Test]
        public void WhenTheSideMostAwayCannotBeStoodOnTheNextMostAwayIsTaken()
        {
            LedGame g = Pair(12700, 20000);
            SimVec3 leader = g.At(1), place = Minus(leader, (Raw(300), 0));
            StandIn orders = Install(g, 2);
            orders.Blocked.Add(Plus(place, Halves2[5]));
            g.Order(SquadCommandKind.MoveTo, AnchorFor(place, 0));
            SimVec3 nw = Plus(place, Halves2[0]);
            Assert.That(Xz(StandOf(g, 2)), Is.EqualTo(Xz(nw)));
            Assert.That(Xz(Target(g, 2)), Is.EqualTo(Xz(nw)), "walked there");
        }

        // T10. A 1 m lattice, the leader on the place, unit 2 3 m east of it: NW, (-16384, 28378) raw off the place, the
        // shortest step along NW that just reaches the two radii: half a step, (-16384, 28377), falls short by a hair
        // (1073689585 < 32768^2 = 1073741824); the next reach, 32769, gives 16384^2 + 28378^2 = 1073746340 >= 1073741824.
        [Test]
        public void OnAOneMetreLatticeTheStepAsideJustReachesTheTwoRadii()
        {
            LedGame g = Field(1, 1000, Soldier(1, 10, 20), Soldier(2, 13, 20));
            SimVec3 place = g.At(1);
            g.Order(SquadCommandKind.MoveTo, AnchorFor(place, 0, Steps1));
            SimVec3 stand = StandOf(g, 2);
            Assert.That(Xz(stand), Is.EqualTo((place.X.Raw - 16384, place.Z.Raw + 28378)));
            Assert.That(Xz(Target(g, 2)), Is.EqualTo(Xz(stand)), "walked there");
            Assert.That(Squared(stand, g.At(1)), Is.GreaterThanOrEqualTo(Touch * Touch), "clear of the leader");
        }

        // T10, the six sides: unit 2 stands at the side's point (to the millimetre; its body clear of the leader on the place):
        // every side can be used, so it keeps it and is not walked. The millimetres are the side's step (Aside1) rounded to a
        // whole millimetre away from the place: 16384 and 32768 raw are 250 and 500 mm exactly; 28378 raw is 433.01 mm, so
        // 434 (433 mm, 28377 raw, would put the body on the leader's).
        [TestCase(0, -250, 434)]
        [TestCase(1, 250, 434)]
        [TestCase(2, 500, 0)]
        [TestCase(3, 250, -434)]
        [TestCase(4, -250, -434)]
        [TestCase(5, -500, 0)]
        public void OnAOneMetreLatticeEverySideCanBeStoodAt(int side, int xMm, int zMm)
        {
            LedGame g = Field(1, 1000, Soldier(1, 10, 20), Mm(2, 10000 + xMm, 20000 + zMm));
            SimVec3 place = g.At(1), point = Plus(place, Aside1[side]);
            Assert.That(Metres(g.At(2), point), Is.LessThanOrEqualTo(0.25));
            Assert.That(Overlap(g.At(2), place), Is.False);
            g.Order(SquadCommandKind.MoveTo, AnchorFor(place, 0, Steps1));
            Assert.That(Xz(StandOf(g, 2)), Is.EqualTo(Xz(point)));
            Assert.That(HasOrder(g, 2), Is.False, "it stays");
        }

        // The step aside is searched along a side of the lattice (LedSquad.Aside), so a side must never be a point. At a
        // spacing of one raw unit the rounding would put places on the origin, and there the lattice is not made of complete
        // rings, which Formation refuses; from two raw units up, every spacing it takes keeps all its places off the origin.
        [Test]
        public void NoFormationHasAPlaceOnTheOrigin()
        {
            int made = 0;
            for (int size = 1; size <= LedRules.MaxSize; size++)
            {
                Assert.That(() => LedSquad.Formation(size, 1), Throws.InvalidOperationException, "size " + size + " at one raw unit");
                for (long spacing = 2; spacing <= 64; spacing++)
                {
                    SimVec3[] offsets;
                    try { offsets = LedSquad.Formation(size, spacing); }
                    catch (InvalidOperationException) { continue; }
                    made++;
                    foreach (SimVec3 offset in offsets)
                        Assert.That(offset.X.Raw != 0 || offset.Z.Raw != 0, Is.True, "size " + size + " spacing " + spacing);
                }
            }
            Assert.That(made, Is.GreaterThan(LedRules.MaxSize * 63 / 2), "most spacings from two raw units up make a formation");
        }
    }
}
