using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests
{
    // AI-P5a, the squad parts: a member asks the battle's dispatch and placement as its leader (the leader's id and the
    // place the leader stands at now, its own nerve) and stands at the leader's anchor plus an offset of its own.
    public sealed partial class AiP5aTests
    {
        private static ulong Tag(string tag, ulong digest) { var h = new StateHash(); h.Add(tag); h.Add(digest); return h.Value; }

        // A dispatch that answers what the test says and keeps every unit it was asked for.
        private sealed class SeenDispatch : IAiDispatch
        {
            internal int Answer;
            internal readonly List<AiUnit> Seen = new List<AiUnit>();
            public ulong Fold(ulong digest) => Tag("test.dispatch", digest);
            public int Pick(in AiUnit unit, AiField field) { Seen.Add(unit); return Answer; }
        }

        // A placement that wants every unit at one spot and keeps every unit and point it was asked for.
        private class SeenPlacement : IAiPlacement
        {
            internal SimVec3 Spot;
            internal readonly List<AiUnit> Seen = new List<AiUnit>();
            internal readonly List<int> Points = new List<int>();
            public ulong Fold(ulong digest) => Tag("test.placement", digest);
            public SimVec3 Anchor(in AiUnit unit, int point, AiField field) { Seen.Add(unit); Points.Add(point); return Spot; }
        }

        // The same with a waypoint.
        private sealed class SeenWaypoint : SeenPlacement, IAiWaypoint
        {
            internal SimVec3 Via;
            internal bool Has;
            internal readonly List<AiUnit> Asked = new List<AiUnit>();
            internal readonly List<int> AskedPoints = new List<int>();
            public bool TryWaypoint(in AiUnit unit, int point, AiField field, out SimVec3 at) { Asked.Add(unit); AskedPoints.Add(point); at = Via; return Has; }
        }

        private static SimVec3 At(M6World w, ulong key) => w.Read<Position>(key).Value;
        // The man as the scheduler would hand him to a part, standing somewhere of his own.
        private static AiUnit Thinker(M6World w, ulong key, AiNerve nerve = AiNerve.Calm) => new AiUnit(Id(w, key), 1, At(w, key), nerve);
        private static void Same(AiUnit seen, StableId id, SimVec3 here, AiNerve nerve)
        {
            Assert.That(seen.Id, Is.EqualTo(id));
            Assert.That(seen.Team, Is.EqualTo(1));
            Assert.That(seen.Here, Is.EqualTo(here));
            Assert.That(seen.Nerve, Is.EqualTo(nerve));
        }

        // 3. The dispatch is asked for a member as its leader, where the leader stands; for a leader and for a skirmisher
        // as themselves. The answer is the inner part's.
        [Test]
        public void AMemberAsksTheDispatchAsItsLeader()
        {
            M6World w = Ten(out ulong[] k);
            To(w, Form);
            var inner = new SeenDispatch { Answer = 3 };
            IAiDispatch wrap = Sq(w).Wrap(inner);
            Assert.That(wrap.Pick(Thinker(w, k[5], AiNerve.Pinned), null), Is.EqualTo(3));
            Same(inner.Seen[0], Id(w, k[0]), At(w, k[0]), AiNerve.Pinned);
            Assert.That(At(w, k[0]), Is.Not.EqualTo(At(w, k[5])));
            inner.Answer = -1;
            Assert.That(wrap.Pick(Thinker(w, k[0], AiNerve.Broken), null), Is.EqualTo(-1));
            Same(inner.Seen[1], Id(w, k[0]), At(w, k[0]), AiNerve.Broken);
            inner.Answer = 0;
            Assert.That(wrap.Pick(Thinker(w, k[9]), null), Is.Zero);
            Same(inner.Seen[2], Id(w, k[9]), At(w, k[9]), AiNerve.Calm);
            // The second squad's member asks as the second leader, not as the first.
            wrap.Pick(Thinker(w, k[2]), null);
            Same(inner.Seen[3], Id(w, k[1]), At(w, k[1]), AiNerve.Calm);
            Assert.That(inner.Seen, Has.Count.EqualTo(4));
        }

        // The layer a member is asked with is the member's own, in all three questions.
        [Test]
        public void AMemberAsksWithItsOwnLayer()
        {
            M6World w = Ten(out ulong[] k);
            To(w, Form);
            var member = new AiUnit(Id(w, k[5]), 1, At(w, k[5]), AiNerve.Calm, 2);
            var dispatch = new SeenDispatch();
            Sq(w).Wrap(dispatch).Pick(in member, null);
            var inner = new SeenWaypoint { Has = true };
            IAiPlacement wrap = Sq(w).Wrap((IAiPlacement)inner);
            wrap.Anchor(in member, 0, null);
            ((IAiWaypoint)wrap).TryWaypoint(in member, 0, null, out _);
            foreach (AiUnit seen in new[] { dispatch.Seen[0], inner.Seen[0], inner.Asked[0] })
            {
                Assert.That(seen.Id, Is.EqualTo(Id(w, k[0])));
                Assert.That(seen.Layer, Is.EqualTo(2));
            }
        }

        // 3. The leader's place is looked up on every call: when he has moved, his member asks from the new place.
        [Test]
        public void TheLeadersPlaceIsReadOnEveryCall()
        {
            M6World w = Ten(out ulong[] k);
            To(w, Form);
            var dispatch = new SeenDispatch();
            var placement = new SeenPlacement();
            IAiDispatch wrapDispatch = Sq(w).Wrap(dispatch);
            IAiPlacement wrapPlacement = Sq(w).Wrap(placement);
            wrapDispatch.Pick(Thinker(w, k[5]), null); wrapPlacement.Anchor(Thinker(w, k[5]), 0, null);
            SimVec3 moved = SimVec3.Metres(27, 0, 33);
            w.Set(k[0], new Position { Value = moved });
            wrapDispatch.Pick(Thinker(w, k[5]), null); wrapPlacement.Anchor(Thinker(w, k[5]), 0, null);
            Assert.That(dispatch.Seen[0].Here, Is.EqualTo(SimVec3.Metres(20, 0, 20)));
            Assert.That(placement.Seen[0].Here, Is.EqualTo(SimVec3.Metres(20, 0, 20)));
            Assert.That(dispatch.Seen[1].Here, Is.EqualTo(moved));
            Assert.That(placement.Seen[1].Here, Is.EqualTo(moved));
        }

        // 3. A member whose leader fell on this tick asks as itself at once: the table still names the leader (the squad
        // is dissolved on the next tick), but there is nobody to ask as.
        [Test]
        public void AMemberWhoseLeaderFellAsksAsItself()
        {
            M6World w = Ten(out ulong[] k, null, x => Kill(x, 20, ByElect(Keys(1, 10))[0]));
            To(w, 20);
            Assert.That(Band(w, k[5]), Is.EqualTo(k[0]));
            var dispatch = new SeenDispatch();
            var placement = new SeenWaypoint { Spot = SimVec3.Metres(70, 0, 30), Has = true };
            Sq(w).Wrap(dispatch).Pick(Thinker(w, k[5], AiNerve.Pinned), null);
            Same(dispatch.Seen[0], Id(w, k[5]), At(w, k[5]), AiNerve.Pinned);
            IAiPlacement wrap = Sq(w).Wrap(placement);
            Assert.That(wrap.Anchor(Thinker(w, k[5]), 2, null), Is.EqualTo(placement.Spot), "no offset without a leader");
            Same(placement.Seen[0], Id(w, k[5]), At(w, k[5]), AiNerve.Calm);
            ((IAiWaypoint)wrap).TryWaypoint(Thinker(w, k[5]), 2, null, out _);
            Same(placement.Asked[0], Id(w, k[5]), At(w, k[5]), AiNerve.Calm);
        }

        // The offset by the rule, written out: s = spreadMin + Permille(leader, Tight) * (spreadMax - spreadMin) / 1000, then
        // s = s * (500 + Permille(member, Sloppy)) / 1000; m = Mix(member ^ Offset); u from the low 32 bits and v from the
        // high 32 bits, each % (2s + 1) - s, in millimetres.
        private static (long u, long v, long s) Spread(ulong member, ulong leader, int spreadMin, int spreadMax)
        {
            long s = spreadMin + (long)AiParts.Permille(leader, TightSalt) * (spreadMax - spreadMin) / 1000;
            s = s * (500 + AiParts.Permille(member, SloppySalt)) / 1000;
            ulong m = AiParts.Mix(member ^ OffsetSalt), span = 2UL * (ulong)s + 1UL;
            return ((long)((m & 0xFFFFFFFFUL) % span) - s, (long)((m >> 32) % span) - s, s);
        }
        private static SimVec3 Raw(long uMm, long vMm) => new SimVec3(SimFix.Ratio(uMm, 1000), default, SimFix.Ratio(vMm, 1000));

        // 3. A member's anchor is the inner part's anchor for its leader plus the member's offset; a leader and a
        // skirmisher get the inner part's anchor for themselves, untouched.
        [Test]
        public void AMemberStandsAtItsLeadersAnchorPlusItsOffset()
        {
            M6World w = Ten(out ulong[] k, With(SpreadMin, 1500, SpreadMax, 6000));
            To(w, Form);
            var inner = new SeenPlacement { Spot = SimVec3.Metres(70, 0, 30) };
            IAiPlacement wrap = Sq(w).Wrap(inner);
            (long u, long v, long s) = Spread(Stable(k[5]), Stable(k[0]), 1500, 6000);
            Assert.That(s, Is.GreaterThan(0));
            Assert.That(u != 0 || v != 0, "the fixture's member has an offset");
            Assert.That(wrap.Anchor(Thinker(w, k[5], AiNerve.Broken), 2, null), Is.EqualTo(inner.Spot + Raw(u, v)));
            Same(inner.Seen[0], Id(w, k[0]), At(w, k[0]), AiNerve.Broken);
            Assert.That(inner.Points[0], Is.EqualTo(2));
            Assert.That(Sq(w).Offset(Id(w, k[5]), Id(w, k[0])), Is.EqualTo(Raw(u, v)));
            Assert.That(wrap.Anchor(Thinker(w, k[0]), 1, null), Is.EqualTo(inner.Spot));
            Same(inner.Seen[1], Id(w, k[0]), At(w, k[0]), AiNerve.Calm);
            Assert.That(wrap.Anchor(Thinker(w, k[9]), 0, null), Is.EqualTo(inner.Spot));
            Same(inner.Seen[2], Id(w, k[9]), At(w, k[9]), AiNerve.Calm);
            Assert.That(inner.Points, Is.EqualTo(new[] { 2, 1, 0 }));
        }

        // 3. The offset of 50 member keys under 30 leader keys: equal to the rule written out, inside [-s, +s] both ways,
        // and both the squad's tightness (by its leader) and the member's own sloppiness show.
        [Test]
        public void TheOffsetFollowsTheRule()
        {
            M6World w = Ten(out _, With(SpreadMin, 1500, SpreadMax, 6000));
            var widths = new HashSet<long>();
            long least = long.MaxValue, most = long.MinValue;
            bool west = false, east = false, north = false, south = false;
            for (ulong leader = 1; leader <= 30; leader++)
                for (ulong member = 31; member <= 80; member++)
                {
                    (long u, long v, long s) = Spread(Stable(member), Stable(leader), 1500, 6000);
                    Assert.That(Sq(w).Offset(new StableId(Stable(member)), new StableId(Stable(leader))), Is.EqualTo(Raw(u, v)), member + " under " + leader);
                    Assert.That(Math.Abs(u), Is.LessThanOrEqualTo(s));
                    Assert.That(Math.Abs(v), Is.LessThanOrEqualTo(s));
                    widths.Add(s); least = Math.Min(least, s); most = Math.Max(most, s);
                    west |= u < 0; east |= u > 0; north |= v < 0; south |= v > 0;
                }
            // s lies in [1500 * 500 / 1000, 6000 * 1499 / 1000] = [750, 8994] mm.
            Assert.That(least, Is.InRange(750, 2000));
            Assert.That(most, Is.InRange(6000, 8994));
            Assert.That(widths.Count, Is.GreaterThan(300));
            Assert.That(west && east && north && south);
        }

        // 3. Three offsets pinned in millimetres (keys 11, 12, 13 under leader 1; spread 1500..6000): a change of a salt,
        // of Mix or of the rule shows here.
        [Test]
        public void ThreeOffsetsArePinned()
        {
            var seen = new List<string>();
            for (ulong member = 11; member <= 13; member++)
            {
                (long u, long v, long s) = Spread(Stable(member), Stable(1), 1500, 6000);
                seen.Add(u + "/" + v + "/" + s);
            }
            Assert.That(string.Join(" ", seen), Is.EqualTo(PinnedOffsets));
        }
        private const string PinnedOffsets = "2641/-2516/3652 2328/3078/3513 -299/-84/2464";

        // 3. No spread, no offset; equal bounds leave only the member's sloppiness: s = 4000 * (500 + sloppy) / 1000.
        [Test]
        public void TheSpreadBoundsAreTheOffsetsBounds()
        {
            M6World none = Ten(out ulong[] k);
            To(none, Form);
            Assert.That(Sq(none).Offset(Id(none, k[5]), Id(none, k[0])), Is.EqualTo(default(SimVec3)));
            var inner = new SeenPlacement { Spot = SimVec3.Metres(70, 0, 30) };
            Assert.That(Sq(none).Wrap(inner).Anchor(Thinker(none, k[5]), 0, null), Is.EqualTo(inner.Spot));

            M6World even = Ten(out _, With(SpreadMin, 4000, SpreadMax, 4000));
            for (ulong member = 11; member <= 40; member++)
            {
                long s = 4000L * (500 + AiParts.Permille(Stable(member), SloppySalt)) / 1000;
                (long u, long v, long width) = Spread(Stable(member), Stable(2), 4000, 4000);
                Assert.That(width, Is.EqualTo(s));
                Assert.That(Sq(even).Offset(new StableId(Stable(member)), new StableId(Stable(2))), Is.EqualTo(Raw(u, v)));
            }
        }

        // 3. The waypoint: the inner part is asked as the leader (its id) from where the member itself stands, and its
        // answer goes back untouched (no offset). An inner part without waypoints answers no.
        [Test]
        public void AMemberAsksTheWaypointAsItsLeaderFromItsOwnPlace()
        {
            M6World w = Ten(out ulong[] k, With(SpreadMin, 1500, SpreadMax, 6000));
            To(w, Form);
            var inner = new SeenWaypoint { Via = SimVec3.Metres(55, 0, 25), Has = true };
            var wrap = (IAiWaypoint)Sq(w).Wrap((IAiPlacement)inner);
            Assert.That(wrap.TryWaypoint(Thinker(w, k[5], AiNerve.Pinned), 2, null, out SimVec3 via), Is.True);
            Assert.That(via, Is.EqualTo(inner.Via));
            Same(inner.Asked[0], Id(w, k[0]), At(w, k[5]), AiNerve.Pinned);
            Assert.That(inner.AskedPoints[0], Is.EqualTo(2));
            Assert.That(wrap.TryWaypoint(Thinker(w, k[0]), 1, null, out via), Is.True);
            Same(inner.Asked[1], Id(w, k[0]), At(w, k[0]), AiNerve.Calm);
            inner.Has = false;
            Assert.That(wrap.TryWaypoint(Thinker(w, k[9]), 0, null, out _), Is.False);
            Same(inner.Asked[2], Id(w, k[9]), At(w, k[9]), AiNerve.Calm);
            Assert.That(inner.Seen, Is.Empty, "a waypoint question is no anchor question");

            var plain = new SeenPlacement();
            IAiPlacement wrapped = Sq(w).Wrap(plain);
            Assert.That(wrapped, Is.InstanceOf<IAiWaypoint>());
            Assert.That(((IAiWaypoint)wrapped).TryWaypoint(Thinker(w, k[5]), 2, null, out via), Is.False);
            Assert.That(via, Is.EqualTo(default(SimVec3)));
            Assert.That(plain.Seen, Is.Empty);
        }

        // 5. The wrappers fold after the inner part: the dispatch wrapper adds its tag and the seven numbers in the order of
        // the fields, the placement wrapper its tag.
        [Test]
        public void TheWrappersFoldTheirTagsAndTheSevenNumbers()
        {
            int[] values = { 2, 7, 250, 31000, 12, 1500, 6000 };
            M6World w = Ten(out _, values);
            const ulong digest = 0x1234567890abcdefUL;
            var hash = new StateHash(); hash.Add("battle.ai.squads.v1"); hash.Add(Tag("test.dispatch", digest));
            foreach (int value in values) hash.Add(value);
            Assert.That(Sq(w).Wrap(new SeenDispatch()).Fold(digest), Is.EqualTo(hash.Value));
            Assert.That(Sq(w).Wrap((IAiPlacement)new SeenPlacement()).Fold(digest), Is.EqualTo(Tag("battle.ai.squad-placement.v1", Tag("test.placement", digest))));
        }

        [Test]
        public void WrapNeedsAnInnerPart()
        {
            M6World w = Ten(out _);
            Assert.Throws<ArgumentNullException>(() => Sq(w).Wrap((IAiDispatch)null));
            Assert.Throws<ArgumentNullException>(() => Sq(w).Wrap((IAiPlacement)null));
        }
    }
}
