using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml11aWorld;

namespace WarCore.Tests
{
    // ML-11a acceptance 6 and 12: the scheduler's books within one tick. A slot a soldier upstairs leaves, stops at or gives up
    // is released and taken on the floor's table, so the soldier that thinks after it in the same tick reads the right books.
    // The house with the point upstairs (radius 3 m, slots 7..21 of the one index space) and a point on the ground east of it
    // (slots 0..6). Soldiers start at slots of the floor: 8 (41, 33.366), 10 (42.5, 32.5), 12 (40.5, 32.5), 14 (43.5, 32.5),
    // 18 (40.5, 30.767), 21 (43.5, 30.767); slot 7 is the centre, 13 is (43, 33.366), 17 is (41.5, 30.767).
    public sealed class Ml11aBooksTests
    {
        // A placement that wants a unit where the test says, else at the point's centre.
        private sealed class Wants : IAiPlacement
        {
            internal readonly Dictionary<ulong, SimVec3> At = new Dictionary<ulong, SimVec3>();
            public ulong Fold(ulong digest) => digest;
            public SimVec3 Anchor(in AiUnit unit, int point, AiField field) => At.TryGetValue(unit.Id.Value, out SimVec3 at) ? at : field.Center(point);
        }

        private static readonly Dictionary<int, (long X, long Z)> Spots = new Dictionary<int, (long, long)>
        {
            [8] = (41000, 33366), [10] = (42500, 32500), [12] = (40500, 32500), [14] = (43500, 32500), [18] = (40500, 30767), [21] = (43500, 30767)
        };

        private sealed class Books
        {
            internal readonly M6World W;
            private readonly Sent sent = new Sent();
            private readonly Wants wants = new Wants();
            private readonly ulong[] keys;

            // Soldier n (from 1) is the n-th unit the scheduler reads in a tick: it reads them by stable id, so the spawn keys are
            // sorted by it first. Soldier n starts upstairs at the n-th slot given; every unit thinks every `think` ticks.
            internal Books(int think, bool fair, byte[] map, params int[] starts)
            {
                keys = new ulong[starts.Length];
                var units = new UnitSpawn[starts.Length];
                for (int k = 0; k < starts.Length; k++) { keys[k] = (ulong)k + 1; units[k] = Walker(keys[k], 30500 + 1000 * k, 20500); }
                M6World probe = World(units, new[] { PointDown(2, 51500, 32500, 1) });
                Array.Sort(keys, (a, b) => Id(probe, a).Value.CompareTo(Id(probe, b).Value));
                for (int k = 0; k < starts.Length; k++) units[k] = Walker(keys[k], Spots[starts[k]].X, Spots[starts[k]].Z, 1);
                var points = new[] { PointUp(1, 41500, 32500, 3), PointDown(2, 51500, 32500, 1) };
                W = World(units, points, think: think, capture: 100000, terrain: fair ? Rules("{ \"fairDecisions\": true }") : null, map: map);
                Install(W, "dispatch", sent); Install(W, "placement", wants);
                Assert.That((Slots(W).Total, Slots(W).AllTotal), Is.EqualTo((7, 22)));
                for (int k = 0; k < starts.Length; k++)
                {
                    Assert.That(Slots(W).NearestAll(1, Ml11aWorld.At(W, keys[k])), Is.EqualTo(starts[k]), "soldier " + (k + 1) + " stands at its slot");
                    if (k > 0) Assert.That(Id(W, keys[k - 1]).Value, Is.LessThan(Id(W, keys[k]).Value), "soldier " + k + " is read before soldier " + (k + 1));
                }
            }
            internal SimVec3 Slot(int slot) => Slots(W).AllAt(slot);
            // The dispatch sends the soldier to the point; the placement wants it at the slot (or at the point's centre).
            internal void Send(int soldier, int point, int slot = -1)
            {
                ulong id = Id(W, keys[soldier - 1]).Value;
                sent.To[id] = point;
                if (slot >= 0) wants.At[id] = Slot(slot);
            }
            // An order the soldier already has when the tick begins.
            internal void Walk(int soldier, int slot)
            {
                Moves(W).Order(Id(W, keys[soldier - 1]), Slot(slot), Slots(W).AllLayerOf(slot));
                Assert.That(Goal(soldier), Is.EqualTo(slot), "soldier " + soldier + " has the order");
            }
            // The slot the soldier is ordered to (its number tells the layer); -1 without an order, -2 when the order is to no slot.
            internal int Goal(int soldier)
            {
                if (!Ordered(W, keys[soldier - 1], out SimVec3 target, out int layer)) return -1;
                return Slots(W).TryFindAll(layer, target, -1, out int slot) ? slot : -2;
            }
            internal int[] Goals()
            {
                var goals = new int[keys.Length];
                for (int k = 0; k < keys.Length; k++) goals[k] = Goal(k + 1);
                return goals;
            }
        }

        // A soldier upstairs gives its walk up (nowhere to go). The slot it stands at is taken on the floor's table: the second
        // soldier, which wants that slot, gets the nearest free one (slots 7, 9 and 12 are equally near; the lowest). The slot it
        // walked to is released on the floor's table: the third soldier gets it.
        [Test]
        public void AStopIsBookedOnTheFloor()
        {
            var b = new Books(1, false, null, 8, 14, 21);
            b.Walk(1, 18); b.Send(2, 0, 8); b.Send(3, 0, 18);
            b.W.Step();
            Assert.That(b.Goals(), Is.EqualTo(new[] { -1, 7, 18 }));
        }

        // The first soldier leaves the slot it stands at for another: the third gets the slot it left. The second, walking to a
        // slot of the floor, is sent down to the point on the ground: the fourth gets the slot it no longer walks to.
        [Test]
        public void ANewOrderIsBookedOnTheFloor()
        {
            var b = new Books(1, false, null, 8, 14, 10, 12);
            b.Walk(2, 21);
            b.Send(1, 0, 18); b.Send(2, 1); b.Send(3, 0, 8); b.Send(4, 0, 21);
            b.W.Step();
            Assert.That(b.Goals(), Is.EqualTo(new[] { 18, 0, 8, 21 }));
        }

        // Fair decisions, nobody thinks. The second soldier walks to a slot the first stands on, so it stays where it is, on
        // slot 8 of the floor; the third walks to slot 8, so it stops too.
        [Test]
        public void FairDecisionsKeepASoldierThatStaysOnItsFloorSlot()
        {
            var b = new Books(1000000, true, null, 18, 8, 14);
            b.Walk(2, 18); b.Walk(3, 8);
            b.W.Step();
            Assert.That(b.Goals(), Is.EqualTo(new[] { -1, -1, -1 }));
        }

        // Fair decisions, everybody thinks in the same tick. The first two soldiers (walking to slots on the ground) both ask
        // for slot 17: one gets it. The loser stays on the slot it stands at, so the soldier that asks for that slot gets
        // nothing; the winner leaves its slot, so the soldier that asks for it gets it.
        [Test]
        public void FairDecisionsNameSlotsOnTheFloor()
        {
            var b = new Books(1, true, null, 8, 10, 14, 12);
            b.Walk(1, 0); b.Walk(2, 3);
            b.Send(1, 0, 17); b.Send(2, 0, 17); b.Send(3, 0, 8); b.Send(4, 0, 10);
            b.W.Step();
            int[] goals = b.Goals();
            bool first = goals[0] == 17;
            Assert.That(goals, Is.EqualTo(first ? new[] { 17, -1, 8, -1 } : new[] { -1, 17, -1, 10 }));
        }

        // Fair decisions. A soldier that gives its walk up stays on slot 8 of the floor: the soldier that asks for slot 8 in
        // the same tick gets nothing.
        [Test]
        public void FairDecisionsBookAStopOnTheFloor()
        {
            var b = new Books(1, true, null, 8, 14);
            b.Walk(1, 18); b.Send(2, 0, 8);
            b.W.Step();
            Assert.That(b.Goals(), Is.EqualTo(new[] { -1, -1 }));
        }

        // A wall on the ground under the floor's cell (41, 32): nobody stands on the ground there, the floor above is free. A
        // soldier walking to the centre slot upstairs keeps it when the placement changes its mind.
        [Test]
        public void AFloorSlotIsKeptWhateverStandsUnderIt()
        {
            byte[] map = Ml7Worlds.Bytes(Ml4Maps.Make(0, 80, 40, null,
                new[] { Ml4Maps.Flat(1, 40, 30, 4, 4, 3 * M), Ml4Maps.Slope(2, 36, 31, 4, 2, "x", 0, 3 * M) }, new[] { Ml4Maps.Cut(1, 1, 42, 31, 1, 1) },
                new[] { Ml4Maps.Wall(1, 41, 32, 1, 1, 2 * M) }));
            var b = new Books(1, false, map, 14);
            Assert.That((Moves(b.W).CanStandAt(b.Slot(7), 1), Moves(b.W).CanStandAt(b.Slot(7), 0)), Is.EqualTo((true, false)));
            Assert.That(Moves(b.W).CanStandAt(b.Slot(13), 1), Is.True);
            b.Send(1, 0);
            b.W.Step();
            Assert.That(b.Goal(1), Is.EqualTo(7));
            b.Send(1, 0, 13);
            b.W.Step(5);
            Assert.That(b.Goal(1), Is.EqualTo(7));
        }
    }
}
