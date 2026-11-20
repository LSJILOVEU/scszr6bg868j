using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml11aWorld;
using static WarCore.Tests.RespawnWorld;

namespace WarCore.Tests
{
    // ML-11a acceptance 4 (soldiers climb to a point upstairs), 5 (arrival is on the slot's layer), 6 (slots are taken per
    // layer), 8b and 8d (no order to where nobody walks), 12 (fair decisions) and 13 (determinism, snapshot, digest).
    public sealed class Ml11aClimbTests
    {
        private static List<UnitSpawn> Squad() =>
            new List<UnitSpawn> { Walker(1, 30500, 31500), Walker(2, 30500, 32500), Walker(3, 29500, 31500), Walker(4, 29500, 32500) };
        private static M6World Assault(int capture = 400) => World(Squad(), new[] { PointUp(1, 41500, 32500, 3) }, capture: capture);

        // 4. Four soldiers on the ground west of the stair, a neutral point upstairs. Each is ordered to a slot of its own on the
        // floor, walks up the stair, stops at the slot and stays; nobody's walk fails. The first is in the circle at tick 100 and
        // capture takes 400 ticks.
        [Test]
        public void SoldiersClimbToThePointUpstairs()
        {
            M6World w = Assault();
            GuardSlots slots = Slots(w);
            Assert.That((slots.Total, slots.AllTotal), Is.EqualTo((0, 15)));
            var goal = new int[5];
            int owned = -1;
            for (int t = 1; t <= 520; t++)
            {
                w.Step();
                if (owned < 0 && w.Capture.Points[0].Owner == 1) owned = t;
                for (ulong key = 1; key <= 4; key++)
                {
                    Assert.That(Failed(w, key), Is.False, "unit " + key + " tick " + t);
                    if (!Ordered(w, key, out SimVec3 target, out int layer)) continue;
                    Assert.That(layer, Is.EqualTo(1), "unit " + key + " tick " + t);
                    Assert.That(slots.TryFindAll(1, target, 0, out goal[key]), Is.True, "unit " + key + " tick " + t);
                    Assert.That(Moves(w).CanStandAt(target, 1), Is.True);
                }
            }
            Assert.That(owned, Is.EqualTo(500));
            var taken = new HashSet<int>();
            for (ulong key = 1; key <= 4; key++)
            {
                Assert.That(Ordered(w, key), Is.False, "unit " + key + " has stopped");
                Assert.That(Layer(w, key), Is.EqualTo(1), "unit " + key);
                Assert.That(At(w, key).Y.Raw, Is.EqualTo(3 * M), "unit " + key);
                Assert.That(AtSpot(At(w, key), slots.AllAt(goal[key])), Is.True, "unit " + key + " stands at its slot");
                Assert.That(slots.NearestAll(1, At(w, key)), Is.EqualTo(goal[key]), "unit " + key);
                Assert.That(taken.Add(goal[key]), Is.True, "unit " + key + " has a slot of its own");
            }
            Assert.That(goal, Is.EqualTo(new[] { 0, 11, 1, 12, 5 }));
        }

        // 5. The slot nearest to the soldier is right above its head. It has not arrived: it keeps the order, walks round to the
        // stair and stops only upstairs.
        [Test]
        public void StandingUnderASlotIsNotArriving()
        {
            M6World w = World(new[] { Walker(1, 41500, 32500) }, new[] { PointUp(1, 41500, 32500) }, capture: 100000);
            w.Step(12);
            Assert.That(Ordered(w, 1, out SimVec3 target, out int layer), Is.True);
            Assert.That((target, layer), Is.EqualTo((Up(41500, 32500), 1)));
            int arrived = -1;
            for (int t = 13; t <= 400 && arrived < 0; t++)
            {
                w.Step();
                Assert.That(Failed(w, 1), Is.False, "tick " + t);
                if (Ordered(w, 1)) Assert.That(Moves(w).TryGetOrder(Id(w, 1), out target, out layer) && layer == 1 && target.Equals(Up(41500, 32500)), Is.True, "tick " + t);
                else arrived = t;
            }
            Assert.That(arrived, Is.GreaterThan(100));
            Assert.That((Layer(w, 1), AtSpot(At(w, 1), Up(41500, 32500))), Is.EqualTo((1, true)));
        }

        // 6. A point upstairs and one on the ground under it; every unit wants the centre of the point it is sent to. A soldier
        // standing upstairs on the centre slot does not hold the ground slot under it, and the other way round.
        private static M6World Stacked(UnitSpawn[] units, params (ulong Key, int Point)[] sent)
        {
            M6World w = World(units, new[] { PointUp(1, 41500, 32500), PointDown(2, 41500, 32500) }, capture: 100000);
            var dispatch = new Sent();
            foreach ((ulong key, int point) in sent) dispatch.To[Id(w, key).Value] = point;
            Install(w, "dispatch", dispatch); Install(w, "placement", new Centre());
            return w;
        }

        [Test]
        public void ASoldierUpstairsDoesNotHoldTheSlotUnderIt()
        {
            M6World w = Stacked(new[] { Walker(1, 41500, 32500, 1), Walker(2, 43500, 34500) }, (2, 1));
            w.Step(12);
            Assert.That(Ordered(w, 1), Is.False);
            Assert.That(Ordered(w, 2, out SimVec3 target, out int layer), Is.True);
            Assert.That((target, layer), Is.EqualTo((Flat(41500, 32500), 0)));
        }

        [Test]
        public void ASoldierOnTheGroundDoesNotHoldTheSlotAboveIt()
        {
            M6World w = Stacked(new[] { Walker(1, 41500, 32500), Walker(2, 43500, 32500, 1) }, (2, 0));
            w.Step(12);
            Assert.That(Ordered(w, 1), Is.False);
            Assert.That(Ordered(w, 2, out SimVec3 target, out int layer), Is.True);
            Assert.That((target, layer), Is.EqualTo((Up(41500, 32500), 1)));
        }

        // An order to the slot upstairs does not reserve the ground slot under it, and the other way round; on one layer the
        // second soldier takes another slot.
        [Test]
        public void OrdersReserveSlotsOnTheirOwnLayer()
        {
            M6World w = Stacked(new[] { Walker(1, 43500, 33500, 1), Walker(2, 44500, 34500), Walker(3, 40500, 33500, 1), Walker(4, 39500, 34500) },
                (1, 0), (2, 1), (3, 0), (4, 1));
            w.Step(12);
            var targets = new (SimVec3, int)[5];
            for (ulong key = 1; key <= 4; key++)
            {
                Assert.That(Ordered(w, key, out SimVec3 target, out int layer), Is.True, "unit " + key);
                Assert.That(layer, Is.EqualTo(key % 2 == 1 ? 1 : 0), "unit " + key);
                Assert.That(Slots(w).TryFindAll(layer, target, key % 2 == 1 ? 0 : 1, out _), Is.True, "unit " + key);
                targets[key] = (target, layer);
            }
            int upstairs = (targets[1].Item1.Equals(Up(41500, 32500)) ? 1 : 0) + (targets[3].Item1.Equals(Up(41500, 32500)) ? 1 : 0);
            int ground = (targets[2].Item1.Equals(Flat(41500, 32500)) ? 1 : 0) + (targets[4].Item1.Equals(Flat(41500, 32500)) ? 1 : 0);
            Assert.That((upstairs, ground), Is.EqualTo((1, 1)), "one soldier per layer has the centre slot");
            w.Step(30);
            for (ulong key = 1; key <= 4; key++)
                if (Ordered(w, key, out SimVec3 target, out int layer)) Assert.That((target, layer), Is.EqualTo(targets[key]), "unit " + key + " keeps its slot");
        }

        // 8b. Without the stair the only point is out of reach: nobody is ordered anywhere, nobody moves.
        [Test]
        public void NobodyIsSentToAPointOutOfReach()
        {
            M6World w = World(Squad(), new[] { PointUp(1, 41500, 32500, 3) }, stair: false);
            var start = new SimVec3[5];
            for (ulong key = 1; key <= 4; key++) start[key] = At(w, key);
            for (int t = 0; t < 300; t++)
            {
                w.Step();
                for (ulong key = 1; key <= 4; key++)
                    Assert.That((Ordered(w, key), Failed(w, key), At(w, key)), Is.EqualTo((false, false, start[key])), "unit " + key + " tick " + t);
            }
        }

        // 8d. A soldier on a floor without a stair, every point and the base on the ground. Whether it holds, spreads or attacks,
        // it is never ordered; the soldiers on the ground are.
        [TestCase(1000, 1)]
        [TestCase(0, 2)]
        [TestCase(0, 1)]
        public void ASoldierCutOffIsNeverOrdered(int hold, int spread)
        {
            var units = new[] { Walker(1, 41500, 32500, 1), Walker(2, 30500, 20500), Walker(3, 31500, 20500), Walker(4, 32500, 20500) };
            var points = new[] { PointDown(1, 20500, 20500, 1, 1), PointDown(2, 50500, 20500), PointDown(3, 60500, 30500) };
            M6World w = World(units, points, stair: false, capture: 100000);
            Install(w, "dispatch", new NearestDispatch(hold, spread));
            SimVec3 start = At(w, 1);
            int sent = 0;
            for (int t = 0; t < 300; t++)
            {
                w.Step();
                Assert.That((Ordered(w, 1), Failed(w, 1), At(w, 1)), Is.EqualTo((false, false, start)), "tick " + t);
                if (t == 12) for (ulong key = 2; key <= 4; key++) sent += Ordered(w, key) ? 1 : 0;
            }
            Assert.That(sent, Is.EqualTo(3));
        }

        // 12. Fair decisions: two soldiers upstairs want the same slot in the same tick; one gets it, the order carries the layer.
        private static M6World Fair(UnitSpawn[] units)
        {
            M6World w = World(units, new[] { PointUp(1, 41500, 32500, 2) }, capture: 100000, terrain: Rules("{ \"fairDecisions\": true }"));
            Install(w, "placement", new Centre());
            return w;
        }

        [Test]
        public void FairDecisionsGiveAPlateSlotToOne()
        {
            ulong[] Run(out (SimVec3, int)[] orders)
            {
                M6World w = Fair(new[] { Walker(1, 43500, 30500, 1), Walker(2, 40500, 33500, 1), Walker(3, 43500, 33500, 1) });
                Assert.That(Ai(w).StateVersion, Is.EqualTo(4));
                var hashes = new List<ulong>();
                var first = new (SimVec3, int)[3];
                var seen = new bool[3];
                for (int t = 0; t < 12; t++)
                {
                    w.Step(); hashes.Add(w.Hash);
                    for (ulong key = 1; key <= 3; key++)
                    {
                        if (seen[key - 1] || !Ordered(w, key, out SimVec3 target, out int layer)) continue;
                        Assert.That(Slots(w).TryFindAll(1, target, 0, out _), Is.True, "unit " + key);
                        seen[key - 1] = true; first[key - 1] = (target, layer);
                    }
                }
                Assert.That(seen, Is.All.True, "each soldier's first order");
                orders = first;
                for (int t = 0; t < 120; t++) { w.Step(); hashes.Add(w.Hash); }
                for (ulong key = 1; key <= 3; key++) Assert.That((Ordered(w, key), Layer(w, key)), Is.EqualTo((false, 1)), "unit " + key);
                return hashes.ToArray();
            }
            ulong[] hashes = Run(out (SimVec3, int)[] orders);
            Assert.That(new HashSet<(SimVec3, int)>(orders).Count, Is.EqualTo(3), "three slots");
            int centre = 0;
            foreach ((SimVec3 target, int layer) in orders) { Assert.That(layer, Is.EqualTo(1)); centre += target.Equals(Up(41500, 32500)) ? 1 : 0; }
            Assert.That(centre, Is.EqualTo(1));
            Assert.That(Run(out _), Is.EqualTo(hashes));
        }

        // 12. A soldier walking to a plate slot a friend stands on is stopped by the decision of the tick (it kept the slot on
        // the slot's layer).
        [Test]
        public void FairDecisionsStopAWalkToAPlateSlotAFriendStandsOn()
        {
            M6World w = Fair(new[] { Walker(1, 41500, 32500, 1), Walker(2, 43500, 30500, 1) });
            var nobody = new Sent();
            Install(w, "dispatch", nobody);
            Moves(w).Order(Id(w, 2), Up(41500, 32500), 1);
            Assert.That(Ordered(w, 2), Is.True);
            w.Step();
            Assert.That(Ordered(w, 2), Is.False);
        }

        // 13. The same seed gives the same hashes; a snapshot at tick 150 resumes to the same hashes.
        [Test]
        public void TheAssaultIsDeterministicAndResumes()
        {
            M6World a = Assault(), b = Assault();
            for (int t = 0; t < 150; t++) { a.Step(); b.Step(); Assert.That(b.Hash, Is.EqualTo(a.Hash), "tick " + t); }
            byte[] snapshot = a.Sim.SaveRetainedSnapshot();
            M6World c = Assault();
            c.Sim.RestoreRetainedSnapshot(snapshot);
            for (int t = 150; t < 520; t++) { a.Step(); c.Step(); Assert.That(c.Hash, Is.EqualTo(a.Hash), "tick " + t); }
            Assert.That(c.Capture.Points[0].Owner, Is.EqualTo(1));
        }

        // 13. With plates the digest ends with the layers tag; without plates it is the match pack's digest, as before.
        [Test]
        public void TheDigestNamesTheLayers()
        {
            M6World w = Assault();
            var hash = new StateHash(); hash.Add("battle.ai.layers.v1"); hash.Add(w.Recipe.Match.PolicyDigest);
            Assert.That(Digest(w), Is.EqualTo(hash.Value));
            MatchPack pack = M6World.Pack(new[] { PointDown(1, 41500, 32500) }, interval: 1000000, think: 10, bases: Bases(new SpawnAnchor(1, P(5, 30))));
            var flat = new M6World(M6World.Endless(Recipe(Squad(), pack, false)), Ml9bMatchTests.Arena(false));
            Assert.That(Digest(flat), Is.EqualTo(pack.PolicyDigest));
            Assert.That(Part<IUnitLayers>(flat, "layers"), Is.Null);
        }

        // 13. The plates' floor, the units' layers and the walking costs come together or not at all.
        [Test]
        public void TheLayerPartsComeTogether()
        {
            M6World w = Assault();
            var layers = Part<IUnitLayers>(w, "layers");
            var floor = (ISlotFloor)layers;
            var reach = (IPointReach)typeof(AiField).GetField("reach", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(Field(w));
            SimpleAiSystem Make(ISlotFloor f, IUnitLayers l, IPointReach r) =>
                new SimpleAiSystem(w.Combat, w.Recipe.Combat, w.Recipe.Match, w.Capture, Moves(w), slotFloor: f, layers: l, reach: r);
            Assert.DoesNotThrow(() => Make(floor, layers, reach));
            Assert.Throws<ArgumentException>(() => Make(null, layers, reach));
            Assert.Throws<ArgumentException>(() => Make(floor, null, reach));
            Assert.Throws<ArgumentException>(() => Make(floor, layers, null));
            Assert.Throws<ArgumentException>(() => Make(floor, null, null));
            Assert.Throws<ArgumentException>(() => Make(null, layers, null));
            Assert.Throws<ArgumentException>(() => Make(null, null, reach));
        }
    }
}
