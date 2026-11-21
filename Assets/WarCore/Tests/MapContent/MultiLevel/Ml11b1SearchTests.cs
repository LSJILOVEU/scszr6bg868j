using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml11aWorld;
using static WarCore.Tests.Ml11b1World;

namespace WarCore.Tests
{
    // ML-11b-1 acceptance 4 to 7: the search for a remembered target on a map with floor plates. The soldier walks to the place
    // on the place's layer, has arrived only on that layer, and a place it cannot stand at or walk to is no memory for the think.
    // The enemy stands far away, out of the rifle's reach: the memory is written by the test.
    public sealed class Ml11b1SearchTests
    {
        private const string Search = "{ \"targetMemoryTicks\": 300, \"searchTicks\": 40 }";
        private static readonly SimVec3 P = Up(41500, 32500);

        private static M6World Hunt(UnitSpawn hunter, bool stair = true, byte[] map = null, bool plates = true) =>
            World(new[] { hunter, Still(B, FarX, FarZ) }, Search, Guns(8), stair, map, plates);

        private static bool Connected(M6World w, SimVec3 from, int fromLayer, SimVec3 to, int toLayer) => Field(w).Connected(from, fromLayer, to, toLayer);

        // 4. From the ground to a place on the floor (over a wall on the ground: the place is stood at and walked to on the
        // floor's layer only): the order names the place and layer 1 all the way, no walk fails, the soldier arrives on the
        // floor, stands for the search time without an order, then gives up and is sent to its point.
        [Test]
        public void ASoldierClimbsToWhereItLastSawTheEnemy()
        {
            M6World w = Hunt(Gunner(A, 33500, 31500), map: Pillar());
            Assert.That((Moves(w).CanStandAt(P, 1), Moves(w).CanStandAt(P, 0)), Is.EqualTo((true, false)));
            Assert.That((Connected(w, Flat(33500, 31500), 0, P, 1), Connected(w, Flat(33500, 31500), 0, P, 0)), Is.EqualTo((true, false)));
            Plant(w, A, B, P, 1);
            Send(w, A, 0);
            int arrived = -1, left = -1;
            for (int t = 1; t <= 250 && left < 0; t++)
            {
                w.Step();
                Assert.That(Failed(w, A), Is.False, "tick " + t);
                if (arrived < 0)
                {
                    if (Ordered(w, A)) { Assert.That(GoesTo(w, A, P, 1), Is.True, "tick " + t); continue; }
                    arrived = t;
                    Assert.That((Layer(w, A), AtSpot(At(w, A), P), At(w, A).Y), Is.EqualTo((1, true, P.Y)), "arrived on the floor");
                }
                else if (Ordered(w, A))
                {
                    left = t;
                    (_, SimVec3 target, int layer) = Order(w, A);
                    Assert.That(layer == 0 && Slots(w).TryFindAll(0, target, -1, out _), Is.True, "sent to a slot of the point on the ground");
                }
            }
            // This package's baseline: 8.4 m of walking at 0.1 m a tick to within the arrival distance, then 40 ticks of standing.
            Assert.That((arrived, left), Is.EqualTo((82, 122)));
            Assert.That(Recall(w, A).Known, Is.True, "the memory outlives the search");
        }

        // 5. Under the place is not at the place, over it neither; at it on its layer is.
        [Test]
        public void StandingUnderThePlaceIsNotArriving()
        {
            M6World under = Hunt(Gunner(A, 41500, 32500));
            Plant(under, A, B, P, 1);
            under.Step();
            Assert.That(GoesTo(under, A, P, 1), Is.True);

            M6World over = Hunt(Gunner(A, 41500, 32500, 1));
            Plant(over, A, B, Flat(41500, 32500), 0);
            over.Step();
            Assert.That(GoesTo(over, A, Flat(41500, 32500), 0), Is.True);

            M6World at = Hunt(Gunner(A, 41500, 32500, 1));
            Plant(at, A, B, P, 1);
            at.Step(5);
            Assert.That(Ordered(at, A), Is.False, "at the place: the soldier stands");
        }

        // 6a. Without the stair the place on the floor cannot be walked to: the soldier is never ordered there, no walk fails,
        // and from its first think it goes to its point instead.
        [Test]
        public void APlaceThatCannotBeWalkedToIsNotSearched()
        {
            M6World w = Hunt(Gunner(A, 33500, 31500), stair: false);
            Assert.That(Moves(w).CanStandAt(P, 1), Is.True);
            Assert.That(Connected(w, Flat(33500, 31500), 0, P, 1), Is.False);
            Plant(w, A, B, P, 1);
            Send(w, A, 0);
            for (int t = 1; t <= 300; t++)
            {
                w.Step();
                Assert.That(Failed(w, A), Is.False, "tick " + t);
                (bool ordered, SimVec3 target, int layer) = Order(w, A);
                if (t == 1) Assert.That(ordered && layer == 0 && Slots(w).TryFindAll(0, target, -1, out _), Is.True, "the point's slot");
                Assert.That(GoesTo(w, A, P, 1) || GoesTo(w, A, P, 0), Is.False, "tick " + t);
            }
        }

        // 6b, 6c. A place at the floor's edge where a soldier's footprint does not fit is not searched although it can be
        // walked to; half a metre in it is.
        [Test]
        public void APlaceNobodyCanStandAtIsNotSearched()
        {
            SimVec3 edge = Up(40100, 30500), inside = Up(40500, 30500);
            M6World w = Hunt(Gunner(A, 33500, 31500));
            Assert.That((Moves(w).CanStandAt(edge, 1), Moves(w).CanStandAt(inside, 1)), Is.EqualTo((false, true)));
            Assert.That(Connected(w, Flat(33500, 31500), 0, edge, 1), Is.True);
            Plant(w, A, B, edge, 1);
            w.Step(3);
            Assert.That(Ordered(w, A), Is.False);
            Send(w, A, 0);
            w.Step();
            Assert.That(Order(w, A).Layer == 0 && Slots(w).TryFindAll(0, Order(w, A).Target, -1, out _), Is.True, "on to the point");

            M6World control = Hunt(Gunner(A, 33500, 31500));
            Plant(control, A, B, inside, 1);
            control.Step();
            Assert.That(GoesTo(control, A, inside, 1), Is.True);
        }

        // 6d. A target remembered in the air (layer 65535) or on a layer the map does not have is not searched.
        [TestCase(65535)]
        [TestCase(3)]
        public void APlaceOnNoLayerIsNotSearched(int layer)
        {
            M6World w = Hunt(Gunner(A, 33500, 31500));
            Plant(w, A, B, P, layer);
            Assert.DoesNotThrow(() => w.Step(3));
            Assert.That(Ordered(w, A), Is.False);
            Assert.That(Recall(w, A).Layer, Is.EqualTo(layer));
        }

        // 6e. On a map with plates the rule is one for every layer: a place on the ground 0.1 m from a wall is not searched
        // either. On a map without plates nothing is asked, as before: the soldier is ordered to the same place.
        [Test]
        public void OnlyAMapWithPlatesAsksWhetherThePlaceCanBeStoodAt()
        {
            SimVec3 place = Flat(40900, 32500);
            M6World w = Hunt(Gunner(A, 33500, 32500), map: Pillar());
            Assert.That(Moves(w).CanStandAt(place, 0), Is.False);
            Assert.That(Connected(w, Flat(33500, 32500), 0, place, 0), Is.True);
            Plant(w, A, B, place, 0);
            w.Step(3);
            Assert.That(Ordered(w, A), Is.False);

            M6World flat = Hunt(Gunner(A, 33500, 32500), map: WallOnly(), plates: false);
            Assert.That(Part<IUnitLayers>(flat, "layers"), Is.Null);
            Assert.That(Moves(flat).CanStandAt(place, 0), Is.False);
            Plant(flat, A, B, place, 0);
            flat.Step();
            Assert.That(GoesTo(flat, A, place, 0), Is.True);
        }

        // 6. The soldier's own layer is read: from the floor over the wall (the cell under it is walked on by nobody) it comes
        // down to a place on the ground.
        [Test]
        public void ASoldierUpstairsComesDownToSearch()
        {
            SimVec3 place = Flat(33500, 31500);
            M6World w = Hunt(Gunner(A, 41500, 32500, 1), map: Pillar());
            Assert.That((Connected(w, P, 1, place, 0), Connected(w, P, 0, place, 0)), Is.EqualTo((true, false)));
            Plant(w, A, B, place, 0);
            w.Step();
            Assert.That(GoesTo(w, A, place, 0), Is.True);
        }

        // 7. An order to the same X and Z on another layer is not "already on the way": it is replaced.
        [Test]
        public void AnOrderToTheSamePlaceOnAnotherLayerIsReplaced()
        {
            SimVec3 place = Up(41500, 33500), below = Flat(41500, 33500);
            M6World w = Hunt(Gunner(A, 33500, 31500));
            Moves(w).Order(Id(w, A), below, 0);
            Assert.That(Order(w, A), Is.EqualTo((true, below, 0)));
            Plant(w, A, B, place, 1);
            w.Step();
            Assert.That(GoesTo(w, A, place, 1), Is.True);

            // The same order on the place's own layer stays as it is (a new order would start the walk again).
            M6World same = Hunt(Gunner(A, 33500, 31500));
            Plant(same, A, B, place, 1);
            same.Step();
            SimVec3 after = At(same, A);
            same.Step();
            Assert.That(GoesTo(same, A, place, 1), Is.True);
            Assert.That(At(same, A), Is.Not.EqualTo(after), "the soldier keeps walking");
        }
    }
}
