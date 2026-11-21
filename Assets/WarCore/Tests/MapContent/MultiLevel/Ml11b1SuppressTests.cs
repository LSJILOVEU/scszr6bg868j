using System;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml11aWorld;
using static WarCore.Tests.Ml11b1World;

namespace WarCore.Tests
{
    // ML-11b-1 acceptance 8: suppressive fire at a remembered place on a map with floor plates. The range is the combat's span
    // (3D), and a place straight above or below on another height is not "under the feet": the line of sight decides.
    // The rifle reaches 8 m; the enemy stands far away, out of its reach: the memory is written by the test.
    public sealed class Ml11b1SuppressTests
    {
        private const string Suppress = "{ \"targetMemoryTicks\": 300, \"suppressTicks\": 5 }";

        private static M6World Shooter(UnitSpawn shooter, SimVec3 place, int layer, bool plates = true)
        {
            M6World w = World(new[] { shooter, Still(B, FarX, FarZ) }, Suppress, Guns(8), plates: plates);
            Plant(w, A, B, place, layer);
            return w;
        }

        // The shots of the shooter over the next ticks, one entry per tick (null = no shot).
        private static FireRecord?[] Fires(M6World w, int ticks)
        {
            var fires = new FireRecord?[ticks];
            for (int t = 0; t < ticks; t++)
            {
                w.Step();
                foreach (FireRecord fire in w.Combat.Fires)
                {
                    Assert.That(fire.Shooter, Is.EqualTo(Id(w, A)), "only the shooter fires");
                    Assert.That(fires[t], Is.Null, "one shot a tick");
                    fires[t] = fire;
                }
                Assert.That(Ordered(w, A), Is.False, "the shooter stays where it is");
            }
            return fires;
        }

        private static int Count(FireRecord?[] fires) => Array.FindAll(fires, f => f.HasValue).Length;

        // The shot leaves the shooter's place and flies toward the place, height included (the unit vectors agree to 0.001).
        private static void Towards(FireRecord fire, SimVec3 from, SimVec3 place)
        {
            Assert.That(fire.Origin, Is.EqualTo(from));
            double[] shot = Unit(fire.End - fire.Origin), aim = Unit(place - from);
            for (int k = 0; k < 3; k++) Assert.That(shot[k], Is.EqualTo(aim[k]).Within(0.001), "axis " + k);
        }

        private static double[] Unit(SimVec3 v)
        {
            double x = v.X.Raw, y = v.Y.Raw, z = v.Z.Raw, length = Math.Sqrt(x * x + y * y + z * z);
            return new[] { x / length, y / length, z / length };
        }

        // 8a, 8b. A place on the floor seen from the ground. 7.5 m away on the plane it is 8.08 m away (out of reach): no shot.
        // 7 m away on the plane it is 7.62 m away: a shot on each of the five ticks, toward the place on the floor, then silence.
        [Test]
        public void TheRangeIsMeasuredToThePlaceOnItsFloor()
        {
            SimVec3 place = Up(40500, 30500);
            M6World far = Shooter(Gunner(A, 33000, 30500), place, 1);
            Assert.That(far.Combat.Visible(far.Unit(A), place), Is.True);
            Assert.That(Count(Fires(far, 8)), Is.EqualTo(0));

            M6World near = Shooter(Gunner(A, 33500, 30500), place, 1);
            Assert.That(near.Combat.Visible(near.Unit(A), place), Is.True);
            FireRecord?[] fires = Fires(near, 8);
            Assert.That(Count(fires), Is.EqualTo(5));
            for (int t = 0; t < 5; t++)
            {
                Assert.That(fires[t].HasValue, Is.True, "tick " + t);
                Towards(fires[t].Value, Flat(33500, 30500), place);
                Assert.That(fires[t].Value.End.Y.Raw, Is.GreaterThan(3 * M), "the shot ends over the floor");
            }
        }

        // 8c, 8d. A place straight above. Through the floor there is no line: no shot. Through the floor's hole there is: the
        // shooter fires straight up, although the place is "at its feet" on the plane.
        [Test]
        public void APlaceStraightAboveIsShotAtWhenNothingIsBetween()
        {
            M6World under = Shooter(Gunner(A, 41500, 32500), Up(41500, 32500), 1);
            Assert.That(under.Combat.Visible(under.Unit(A), Up(41500, 32500)), Is.False);
            Assert.That(Count(Fires(under, 8)), Is.EqualTo(0));

            SimVec3 hole = Up(42500, 31500);
            M6World open = Shooter(Gunner(A, 42500, 31500), hole, 1);
            Assert.That(open.Combat.Visible(open.Unit(A), hole), Is.True);
            FireRecord?[] fires = Fires(open, 8);
            Assert.That(Count(fires), Is.EqualTo(5));
            for (int t = 0; t < 5; t++)
            {
                FireRecord fire = fires[t].Value;
                Assert.That((fire.Origin, fire.End.X, fire.End.Z), Is.EqualTo((Flat(42500, 31500), hole.X, hole.Z)), "straight up, tick " + t);
                Assert.That(fire.End.Y.Raw, Is.GreaterThan(3 * M));
            }
        }

        // 8. A place at the shooter's feet on its own floor is not shot at, upstairs as on the ground; neither is the same
        // place on a map without plates (the old rule).
        [Test]
        public void APlaceAtTheFeetIsNotShotAt()
        {
            Assert.That(Count(Fires(Shooter(Gunner(A, 41500, 32500, 1), Up(41500, 32500), 1), 8)), Is.EqualTo(0));
            Assert.That(Count(Fires(Shooter(Gunner(A, 33500, 30500), Flat(33500, 30500), 0), 8)), Is.EqualTo(0));
            Assert.That(Count(Fires(Shooter(Gunner(A, 33500, 30500), Flat(33500, 30500), 0, plates: false), 8)), Is.EqualTo(0));
        }

        // 1. Without plates the range is the one on the plane, as before: the same two places are both in reach whatever height
        // the memory holds.
        [Test]
        public void WithoutPlatesTheRangeIsOnThePlane()
        {
            M6World w = Shooter(Gunner(A, 33000, 30500), Up(40500, 30500), 1, plates: false);
            Assert.That(Count(Fires(w, 8)), Is.EqualTo(5));
            M6World beyond = Shooter(Gunner(A, 32400, 30500), Flat(40500, 30500), 0, plates: false);
            Assert.That(Count(Fires(beyond, 8)), Is.EqualTo(0));
        }
    }
}
