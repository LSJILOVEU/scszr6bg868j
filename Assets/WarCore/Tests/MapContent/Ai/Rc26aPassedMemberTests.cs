using NUnit.Framework;
using WarCore.Battle;
using WarCore.Sim;
using static WarCore.Tests.LedGame;
using static WarCore.Tests.Rc26aStandClearTests;

namespace WarCore.Tests
{
    // RC2-6a, rule R4 while the leader walks (Battle/Ai/LedSquad.cs: leaderWalks in Step, the line before the walk loop of
    // Upkeep, Passed): a walking leader sends nobody aside. A member it walks past stands as it stands: on its place, or,
    // while the leader's body is still on that place, a step aside of it. The leader walks by its input, 0.2 m a tick
    // (Ratio(200, 1000) = 13107 raw), and the squad looks before the leader's step of the tick. The flat fields are those of
    // Rc26aStandClearTests: the leader (unit 1) at (10, 20), unit 2 in a squad of one, whose place is the anchor plus side 0's
    // step. Each test names the entries of tools/mutation/lists/rc2-6a.txt it is there to kill.
    public sealed class Rc26aPassedMemberTests
    {
        // Unit 2 starts on its place, spacing (1 m or 2 m) from the leader along (dirX, dirZ), and stands there without a walk
        // order once the squad holds.
        private static LedGame Settled(int spacingMm, int dirX, int dirZ, out SimVec3 place)
        {
            LedGame g = Field(1, spacingMm, Soldier(1, 10, 20), Mm(2, 10000 + dirX * spacingMm, 20000 + dirZ * spacingMm));
            place = Plus(g.At(1), (dirX * Raw(spacingMm), dirZ * Raw(spacingMm)));
            g.Order(SquadCommandKind.MoveTo, AnchorFor(place, 0, spacingMm == 2000 ? Steps2 : Steps1));
            Assert.That(Xz(g.At(2)), Is.EqualTo(Xz(place)), "unit 2 stands on its place");
            Assert.That(Xz(StandOf(g, 2)), Is.EqualTo(Xz(place)));
            Assert.That(HasOrder(g, 2), Is.False, "without a walk order");
            return g;
        }

        // P2 (and the start of P3 and P4). As Settled along x; the leader walks east until its body is on the place (2 m: 8
        // steps, 11.6 m, 0.4 m short of the place; 1 m: 3 steps, 10.6 m, the same), and unit 2 is never given a walk order on
        // the way. Then one tick without input: on that tick unit 2 is sent aside, to E (the leader - place points W), and it
        // then stands there, clear of the leader. aside: the east side's point.
        private static LedGame StoodOnThePlace(int spacingMm, out SimVec3 place, out SimVec3 aside)
        {
            LedGame g = Settled(spacingMm, 1, 0, out place);
            int steps = 0;
            for (; steps < 20 && !Overlap(g.At(1), place); steps++)
            {
                g.Step(Walk(1000, 0));
                Assert.That(HasOrder(g, 2), Is.False, "the leader walks, step " + steps);
                Assert.That(Xz(g.At(2)), Is.EqualTo(Xz(place)), "unit 2 stands on its place, step " + steps);
            }
            Assert.That(Overlap(g.At(1), place), Is.True, "the leader's body is on the place");
            Assert.That(steps, Is.EqualTo(spacingMm == 2000 ? 8 : 3));
            g.Wait(1);
            aside = Plus(place, spacingMm == 2000 ? Halves2[2] : Aside1[2]);
            Assert.That(Xz(Target(g, 2)), Is.EqualTo(Xz(aside)), "sent aside on the tick the leader stands");
            Assert.That(Xz(StandOf(g, 2)), Is.EqualTo(Xz(aside)));
            StandsAside(g, 2, place, "the tick the leader stands");
            for (int t = 0; t < 30 && g.Walking(2); t++) g.Wait(1);
            Assert.That(g.Walking(2), Is.False, "it stopped");
            Assert.That(Metres(g.At(2), aside), Is.LessThanOrEqualTo(0.25), "it stands aside");
            Assert.That(Overlap(g.At(2), g.At(1)), Is.False, "clear of the leader");
            Assert.That(HasOrder(g, 2), Is.False);
            return g;
        }

        // P1. The leader walks straight through unit 2's place, on which unit 2 stands, and on to 1.5 m past it; once, with its
        // body on the place, the world is saved and loaded. Unit 2 is given no walk order and does not move by a raw unit.
        // Along x and along z separately, so that each half of leaderWalks is needed. Kills: rc2-6a-r4-pass-removed,
        // -pass-when-standing, -pass-not-on-its-place, -walks-on-both-axes, -walks-on-x-only (the z cases), -walks-on-z-only
        // (the x cases).
        [TestCase(2000, 1, 0)]
        [TestCase(2000, 0, 1)]
        [TestCase(1000, 1, 0)]
        [TestCase(1000, 0, 1)]
        public void TheLeaderWalkingOverAMemberOnItsPlaceLeavesItStanding(int spacingMm, int dirX, int dirZ)
        {
            LedGame g = Settled(spacingMm, dirX, dirZ, out SimVec3 place);
            long past = Raw(1500);
            int on = 0;
            bool saved = false;
            for (int t = 0; t < 40 && (g.At(1).X.Raw - place.X.Raw) * dirX + (g.At(1).Z.Raw - place.Z.Raw) * dirZ < past; t++)
            {
                g.Step(Walk(1000 * dirX, 1000 * dirZ));
                if (Overlap(g.At(1), place))
                {
                    on++;
                    if (!saved) { g.W.Sim.RestoreRetainedSnapshot(g.W.Sim.SaveRetainedSnapshot()); saved = true; }
                }
                Assert.That(HasOrder(g, 2), Is.False, "no walk order, tick " + t);
                Assert.That(Xz(g.At(2)), Is.EqualTo(Xz(place)), "it did not move, tick " + t);
            }
            Assert.That((g.At(1).X.Raw - place.X.Raw) * dirX + (g.At(1).Z.Raw - place.Z.Raw) * dirZ, Is.GreaterThanOrEqualTo(past), "the leader walked 1.5 m past the place");
            Assert.That((g.At(1).X.Raw - place.X.Raw) * dirZ + (g.At(1).Z.Raw - place.Z.Raw) * dirX, Is.EqualTo(0L), "straight through it");
            Assert.That(on, Is.GreaterThan(0), "its body was on the place");
            Assert.That(saved, Is.True);
        }

        // P2. Kills: rc2-6a-r4-walks-a-tick-late (unit 2 would be sent aside a tick after the leader stands).
        [TestCase(2000)]
        [TestCase(1000)]
        public void AMemberIsSentAsideOnTheTickTheLeaderStandsOnItsPlace(int spacingMm)
        {
            StoodOnThePlace(spacingMm, out _, out _);
        }

        // P3 (a). After P2 (2 m: unit 2 at E, within reach of (13, 20)): the leader takes a step north and one east, its body
        // still on the place (0.45 m and 0.28 m from it). From (11.8, 20.2) SE points more away than E (dot -1.17e9 against
        // -0.86e9), but unit 2 keeps E: no walk order, not moved, also once the leader stands.
        [Test]
        public void AMemberAsideStaysWhileTheLeaderShiftsOnItsPlace()
        {
            LedGame g = StoodOnThePlace(2000, out SimVec3 place, out SimVec3 aside);
            SimVec3 at = g.At(2);
            foreach (InputFrame step in new[] { Walk(0, 1000), Walk(1000, 0) })
            {
                g.Step(step);
                Assert.That(Overlap(g.At(1), place), Is.True, "the leader's body is still on the place");
                Assert.That(HasOrder(g, 2), Is.False);
                Assert.That(Xz(g.At(2)), Is.EqualTo(Xz(at)), "it did not move");
            }
            g.Wait(1);
            Assert.That(HasOrder(g, 2), Is.False, "nor once the leader stands");
            Assert.That(Xz(StandOf(g, 2)), Is.EqualTo(Xz(aside)));
        }

        // P3 (b). After P2 on a 1 m lattice (unit 2 at E, (11.5, 20), half a step): the leader walks on east; the squad sees it
        // at 11.2 m, its body on the place (13106 raw off) and on the east point (19662 raw off), so E is no side any more (the
        // second version sent unit 2 to W here). Up to and on that tick: no walk order, not moved. Kills:
        // rc2-6a-r4-pass-without-sides.
        [Test]
        public void AMemberAsideStaysWhileTheWalkingLeaderIsOnItsPlaceAndItsSide()
        {
            LedGame g = StoodOnThePlace(1000, out SimVec3 place, out SimVec3 aside);
            SimVec3 at = g.At(2);
            bool both = false;
            for (int t = 0; t < 10 && !both; t++)
            {
                SimVec3 seen = g.At(1);
                g.Step(Walk(1000, 0));
                both = Overlap(seen, place) && Overlap(seen, aside);
                Assert.That(HasOrder(g, 2), Is.False, "tick " + t);
                Assert.That(Xz(g.At(2)), Is.EqualTo(Xz(at)), "it did not move, tick " + t);
            }
            Assert.That(both, Is.True, "the squad saw the leader on the place and on the east point");
        }

        // P4. After P2 the leader walks off without stopping: west, away from unit 2 (2 m), or east, out across the east point
        // and through unit 2's body (1 m). On the first tick the squad sees it off the place, unit 2 is sent to its place
        // (the input is not 0) and it ends there. Kills: rc2-6a-r4-pass-while-aside (both), rc2-6a-r4-pass-clear-of-the-member
        // (1 m: the leader's body is on unit 2's).
        [TestCase(2000, -1000)]
        [TestCase(1000, 1000)]
        public void AMemberAsideGoesBackOnceThePlaceIsFreeAlthoughTheLeaderWalks(int spacingMm, int xMilli)
        {
            LedGame g = StoodOnThePlace(spacingMm, out SimVec3 place, out _);
            SimVec3 at = g.At(2);
            bool off = false;
            for (int t = 0; t < 10 && !off; t++)
            {
                SimVec3 seen = g.At(1);
                g.Step(Walk(xMilli, 0));
                off = !Overlap(seen, place);
                if (off)
                {
                    Assert.That(Xz(Target(g, 2)), Is.EqualTo(Xz(place)), "sent to its place at once");
                    Assert.That(Overlap(seen, at), Is.EqualTo(xMilli > 0), xMilli > 0 ? "the leader is on unit 2's body" : "the leader is clear of unit 2");
                }
                else
                {
                    Assert.That(HasOrder(g, 2), Is.False, "tick " + t);
                    Assert.That(Xz(g.At(2)), Is.EqualTo(Xz(at)), "tick " + t);
                }
            }
            Assert.That(off, Is.True, "the leader walked off the place");
            for (int t = 0; t < 30 && g.Walking(2); t++) g.Step(Walk(xMilli, 0));
            Assert.That(g.Walking(2), Is.False);
            Assert.That(Metres(g.At(2), place), Is.LessThanOrEqualTo(0.25), "it stands on its place");
            Assert.That(Xz(StandOf(g, 2)), Is.EqualTo(Xz(place)));
        }

        // P5. The house: the leader walks in to (16, 9) on the ground floor; the squad is sent upstairs to the anchor that puts
        // one member's place straight above that point (layer 2). The leader walks north on the ground; the member is moved,
        // without a walk order, to the ground right under its place (another layer than the anchor's). On the next tick, the
        // leader walking on, it is sent to its place on layer 2: a member on another layer is not standing on its place.
        // Kills: rc2-6a-r4-pass-on-another-layer.
        [Test]
        public void AMemberUnderItsPlaceOnAnotherLayerIsSentThereWhileTheLeaderWalks()
        {
            LedGame g = House(out IUnitLayers layers);
            WalkThrough(g, (12.5, 12.0), (16.0, 12.0), (16.0, 9.0));
            g.Wait(1);
            SimVec3 ground = g.At(g.Player);
            ulong member = MemberFor(g, ground, 2, out SimVec3 anchor, out SimVec3 place);
            g.Order(SquadCommandKind.MoveTo, anchor, 2);
            Assert.That((g.Led.Mode, g.Led.AnchorLayer), Is.EqualTo((LedMode.Hold, 2)));
            g.Step(Walk(0, 1000), 5);
            Assert.That(layers.LayerOf(g.W.Unit(g.Player)), Is.EqualTo(0), "the leader walks on the ground");
            g.Moves.Stop(g.Id(member));
            g.W.Set(member, new Position(new SimVec3(place.X, ground.Y, place.Z)));
            Assert.That(HasOrder(g, member), Is.False);
            Assert.That(layers.LayerOf(g.W.Unit(member)), Is.EqualTo(0), "the member is on the ground");
            Assert.That(Xz(g.At(member)), Is.EqualTo(Xz(place)), "right under its place");
            g.Step(Walk(0, 1000));
            Assert.That(Xz(Target(g, member, 2)), Is.EqualTo(Xz(place)), "sent to its place");
        }

        // P8. A member walking to its place while the leader walks is stopped on the tick it is within reach, as while the
        // leader stands: unit 2 walks 3 m south to its place (16, 20) in two worlds, the leader walking west in one and
        // standing in the other; tick for tick it is where it is in the other and walks when it walks there, and it ends
        // within a quarter metre of its place (not on it: a walking member is not passed). Kills: rc2-6a-r4-pass-walking.
        [Test]
        public void AMemberWalkingToItsPlaceStopsThereTheSameWhetherTheLeaderWalksOrStands()
        {
            LedGame walks = Settled(2000, 3, 0, out SimVec3 place), stands = Settled(2000, 3, 0, out _);
            foreach (LedGame g in new[] { walks, stands })
            {
                g.W.Set(2UL, new Position(Plus(place, (0, Raw(3000)))));
                g.Wait(1);
                Assert.That(Xz(Target(g, 2)), Is.EqualTo(Xz(place)), "walks to its place");
            }
            for (int t = 0; t < 30; t++)
            {
                walks.Step(Walk(-1000, 0)); stands.Wait(1);
                Assert.That(Xz(walks.At(2)), Is.EqualTo(Xz(stands.At(2))), "tick " + t);
                Assert.That(walks.Walking(2), Is.EqualTo(stands.Walking(2)), "tick " + t);
            }
            Assert.That(walks.Walking(2), Is.False);
            Assert.That(Metres(walks.At(2), place), Is.LessThanOrEqualTo(0.25), "within a quarter metre of its place");
            Assert.That(Xz(walks.At(2)), Is.Not.EqualTo(Xz(place)), "stopped short of the point itself");
        }

        // P9. A member whose unit is gone from the world (not only fallen) is answered as standing on its place without a walk
        // order, without asking the move orders about it (they cannot find it). Kills: rc2-6a-r4-answer-asks-a-gone-member.
        [Test]
        public void AMemberWhoseUnitIsGoneIsAnsweredWithItsPlace()
        {
            LedGame g = Settled(2000, 3, 0, out SimVec3 place);
            StableId id = g.Id(2);
            g.W.Sim.Entities.Destroy(g.W.Unit(2));
            bool answered = false;
            SimVec3 stand = default;
            Assert.That(() => answered = g.Led.TryPlace(id, out stand), Throws.Nothing);
            Assert.That(answered, Is.True);
            Assert.That(Xz(stand), Is.EqualTo(Xz(place)));
        }
    }
}
