using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Sim;

namespace WarCore.Tests
{
    // AI-P5a, the saved state of battle.ai-squads: the seven numbers, then leader -> size and member -> leader, both in
    // key order. Loading checks everything it reads.
    public sealed partial class AiP5aTests
    {
        private static byte[] Save(M6World w) { var writer = new SimStateWriter(); Sq(w).SaveState(writer); return writer.ToArray(); }
        private static ulong HashOf(M6World w) { var hash = new StateHash(); Sq(w).WriteState(hash); return hash.Value; }

        // The payload written out by hand. The tables are given as they are to be written (the caller sorts them, or not).
        private static byte[] Payload(int[] values, (ulong leader, int size)[] squads, (ulong member, ulong leader)[] members)
        {
            var writer = new SimStateWriter();
            foreach (int value in values) writer.Int32(value);
            writer.Int32(squads.Length);
            foreach ((ulong leader, int size) in squads) { writer.UInt64(leader); writer.Int32(size); }
            writer.Int32(members.Length);
            foreach ((ulong member, ulong leader) in members) { writer.UInt64(member); writer.UInt64(leader); }
            return writer.ToArray();
        }

        // The tables of the ten after the first form tick, in key order: k0 leads k5..k8 (size 4), k1 leads k2 (size 4).
        private static void TenTables(ulong[] k, out (ulong leader, int size)[] squads, out (ulong member, ulong leader)[] members)
        {
            squads = new[] { (Stable(k[0]), 4), (Stable(k[1]), 4) };
            members = new[]
            {
                (Stable(k[5]), Stable(k[0])), (Stable(k[6]), Stable(k[0])), (Stable(k[7]), Stable(k[0])), (Stable(k[8]), Stable(k[0])), (Stable(k[2]), Stable(k[1])),
            };
            Array.Sort(squads, (a, b) => a.leader.CompareTo(b.leader));
            Array.Sort(members, (a, b) => a.member.CompareTo(b.member));
        }

        // 4. What is saved and what is hashed, byte for byte.
        [Test]
        public void State_TheSevenNumbersAndBothTablesInKeyOrder()
        {
            int[] values = With(SpreadMin, 1500, SpreadMax, 6000);
            M6World w = Ten(out ulong[] k, values);
            Assert.That(Save(w), Is.EqualTo(Payload(values, new (ulong, int)[0], new (ulong, ulong)[0])));
            int[] uneven = With(Min, 2, Max, 6);
            Assert.That(Save(Ten(out _, uneven)), Is.EqualTo(Payload(uneven, new (ulong, int)[0], new (ulong, ulong)[0])), "each of the seven in its place");
            To(w, Form);
            TenTables(k, out var squads, out var members);
            Assert.That(Save(w), Is.EqualTo(Payload(values, squads, members)));

            var hash = new StateHash(); hash.Add("battle.ai-squads"); hash.Add(1);
            foreach (int value in values) hash.Add(value);
            hash.Add(squads.Length);
            foreach ((ulong leader, int size) in squads) { hash.Add(leader); hash.Add(size); }
            hash.Add(members.Length);
            foreach ((ulong member, ulong leader) in members) { hash.Add(member); hash.Add(leader); }
            Assert.That(HashOf(w), Is.EqualTo(hash.Value));
            Assert.That(Sq(w).StateTypeId, Is.EqualTo("battle.ai-squads"));
            Assert.That(Sq(w).StateVersion, Is.EqualTo(1));
        }

        // 4. Load reads back what Save wrote, into a world that had other tables, and replaces them.
        [Test]
        public void State_LoadReplacesTheTables()
        {
            M6World formed = Ten(out ulong[] k);
            To(formed, Form);
            M6World fresh = Ten(out _);
            Sq(fresh).LoadState(new SimStateReader(Save(formed)));
            Assert.That(Bands(fresh, k), Is.EqualTo(Bands(formed, k)));
            Assert.That(Save(fresh), Is.EqualTo(Save(formed)));
            Assert.That(HashOf(fresh), Is.EqualTo(HashOf(formed)));
            Assert.That(Bodies(fresh, 1), Is.EqualTo(7));
            // And back to nothing.
            Sq(formed).LoadState(new SimStateReader(Save(Ten(out _))));
            Assert.That(Sq(formed).SquadCount, Is.Zero);
            Assert.That(Sq(formed).MemberCount, Is.Zero);
            Assert.That(Bands(formed, k), Is.EqualTo(Rep(0, 10)));
        }

        // 4. Other tables, another hash: a member less, another size, a member under the other leader.
        [Test]
        public void State_OtherTablesHashDifferently()
        {
            M6World w = Ten(out ulong[] k, With(Min, 2));
            To(w, Form);
            ulong formed = HashOf(w);
            int[] values = With(Min, 2);
            TenTables(k, out var squads, out var members);
            // The sizes of the two leaders under 2..4 come from their keys.
            for (int i = 0; i < squads.Length; i++) squads[i].size = Sq(w).SizeOf(new StableId(squads[i].leader));
            Sq(w).LoadState(new SimStateReader(Payload(values, squads, members)));
            Assert.That(HashOf(w), Is.EqualTo(formed), "the tables written out by hand are the tables");

            var seen = new HashSet<ulong> { formed };
            var fewer = new List<(ulong, ulong)>(members);
            fewer.RemoveAt(Array.FindIndex(members, m => m.member == Stable(k[8])));
            Sq(w).LoadState(new SimStateReader(Payload(values, squads, fewer.ToArray())));
            Assert.That(seen.Add(HashOf(w)), "a member less");

            var resized = ((ulong leader, int size)[])squads.Clone();
            int first = Array.FindIndex(resized, s => s.leader == Stable(k[1]));
            resized[first].size = resized[first].size == 4 ? 3 : 4;
            Sq(w).LoadState(new SimStateReader(Payload(values, resized, members)));
            Assert.That(seen.Add(HashOf(w)), "another size");

            var moved = ((ulong member, ulong leader)[])fewer.ToArray().Clone();
            int mover = Array.FindIndex(moved, m => m.member == Stable(k[7]));
            moved[mover].leader = Stable(k[1]);
            Sq(w).LoadState(new SimStateReader(Payload(values, squads, moved)));
            Assert.That(seen.Add(HashOf(w)), "a member under the other leader");
        }

        private static void Refused(M6World w, byte[] payload, string reason)
        {
            var e = Assert.Catch<InvalidOperationException>(() => Sq(w).LoadState(new SimStateReader(payload)), reason);
            Assert.That(e.Message, Is.EqualTo(reason));
        }

        // 4. Load refuses, with the reason: tables out of key order (or a key twice), a member whose leader leads no squad,
        // a member that leads a squad, a size outside min..max, a squad with no member or with more than its size.
        [Test]
        public void State_LoadRefusesTablesThatCannotBe()
        {
            int[] values = With(Min, 2);
            M6World w = Ten(out ulong[] k, values);
            ulong a = Stable(k[0]), b = Stable(k[1]);
            ulong low = Math.Min(a, b), high = Math.Max(a, b);
            var others = new List<ulong>();
            for (int i = 2; i < 10; i++) others.Add(Stable(k[i]));
            others.Sort();
            ulong m0 = others[0], m1 = others[1], m2 = others[2], m3 = others[3], m4 = others[4];
            (ulong, int)[] one = { (low, 2) }, two = { (low, 2), (high, 2) };

            Assert.DoesNotThrow(() => Sq(w).LoadState(new SimStateReader(Payload(values, two, new[] { (m0, low), (m1, high), (m2, low) }))));
            Refused(w, Payload(values, new[] { (high, 2), (low, 2) }, new[] { (m0, low), (m1, high) }), "AI squad leaders are not in strictly increasing order.");
            Refused(w, Payload(values, new[] { (low, 2), (low, 2) }, new[] { (m0, low) }), "AI squad leaders are not in strictly increasing order.");
            Refused(w, Payload(values, one, new[] { (m1, low), (m0, low) }), "AI squad members are not in strictly increasing order.");
            Refused(w, Payload(values, one, new[] { (m0, low), (m0, low) }), "AI squad members are not in strictly increasing order.");
            Refused(w, Payload(values, one, new[] { (m0, low), (m1, high) }), "AI squad member follows a unit that leads no squad.");
            Refused(w, Payload(values, one, new[] { (m0, low), (m1, m0) }), "AI squad member follows a unit that leads no squad.");
            Refused(w, Payload(values, two, new[] { (low, high), (m0, low) }.OrderBy()), "AI squad member leads a squad.");
            Refused(w, Payload(values, new[] { (low, 1) }, new[] { (m0, low) }), "AI squad size is out of range.");
            Refused(w, Payload(values, new[] { (low, 5) }, new[] { (m0, low) }), "AI squad size is out of range.");
            Assert.DoesNotThrow(() => Sq(w).LoadState(new SimStateReader(Payload(values, new[] { (low, 4) }, new[] { (m0, low), (m1, low), (m2, low), (m3, low) }))));
            Refused(w, Payload(values, two, new[] { (m0, low), (m1, low) }), "AI squad has no member or more than its size.");
            Refused(w, Payload(values, one, new (ulong, ulong)[0]), "AI squad has no member or more than its size.");
            Refused(w, Payload(values, one, new[] { (m0, low), (m1, low), (m2, low) }), "AI squad has no member or more than its size.");
            Refused(w, Payload(values, new[] { (low, 4) }, new[] { (m0, low), (m1, low), (m2, low), (m3, low), (m4, low) }), "AI squad has no member or more than its size.");
            // More entries than the battle has room for units.
            var writer = new SimStateWriter();
            foreach (int value in values) writer.Int32(value);
            writer.Int32(w.Sim.Entities.Capacity + 1);
            Assert.Catch<InvalidOperationException>(() => Sq(w).LoadState(new SimStateReader(writer.ToArray())));
        }

        // 4. Each of the seven numbers is compared with the battle's own.
        [TestCase(Min, 3)]
        [TestCase(Max, 5)]
        [TestCase(Skirmishers, 301)]
        [TestCase(Radius, 30001)]
        [TestCase(FormTicks, 16)]
        [TestCase(SpreadMin, 1)]
        [TestCase(SpreadMax, 1)]
        public void State_LoadRefusesOtherRules(int index, int value)
        {
            int[] saved = With(SpreadMax, 2), other = With(SpreadMax, 2);
            other[index] = value;
            M6World formed = Ten(out _, saved);
            To(formed, Form);
            byte[] payload = Save(formed);
            Assert.DoesNotThrow(() => Sq(Ten(out _, saved)).LoadState(new SimStateReader(payload)));
            Refused(Ten(out _, other), payload, "AI squad rules differ.");
        }

        // 4. Saved on the tick a squad was dissolved (its leader fell the tick before) while the other stands, and restored
        // into a new world: the same tables, and the same hash on each of the next 300 ticks as the battle that went on.
        [Test]
        public void State_RestoreOnTheTickASquadDissolves()
        {
            Action<M6World, ulong[]> deaths = (x, keys) => Kill(x, 20, keys[0]);
            M6World first = TwoSquadsAndOne(out ulong[] k, deaths);
            To(first, 21);
            Assert.That(Sq(first).SquadCount, Is.EqualTo(1));
            Assert.That(Bands(first, k[0], k[2], k[3]), Is.EqualTo(Rep(0, 3)));
            byte[] snapshot = first.Sim.SaveRetainedSnapshot();
            M6World restored = TwoSquadsAndOne(out _, deaths);
            Assert.That(restored.Hash, Is.Not.EqualTo(first.Hash));
            restored.Sim.RestoreRetainedSnapshot(snapshot);
            Assert.That(restored.Hash, Is.EqualTo(first.Hash));
            Assert.That(Bands(restored, k), Is.EqualTo(Bands(first, k)));
            for (int tick = 0; tick < 300; tick++)
            {
                first.Step(); restored.Step();
                Assert.That(restored.Hash, Is.EqualTo(first.Hash), "tick " + first.Sim.Clock.Tick);
            }
            Assert.That(Bands(restored, k), Is.EqualTo(Bands(first, k)));
            Assert.That(Sq(restored).SquadCount, Is.EqualTo(2), "the dissolved squad formed again");

            // The squad tables are part of the world's hash: the same world with one squad's tables emptied hashes differently.
            ulong before = restored.Hash;
            Sq(restored).LoadState(new SimStateReader(Payload(With(Skirmishers, 0, Radius, 25000), new (ulong, int)[0], new (ulong, ulong)[0])));
            Assert.That(restored.Hash, Is.Not.EqualTo(before));
        }
    }

    internal static class AiP5aSorting
    {
        // Members in key order, as the payload wants them.
        internal static (ulong member, ulong leader)[] OrderBy(this (ulong member, ulong leader)[] members)
        {
            var sorted = ((ulong member, ulong leader)[])members.Clone();
            Array.Sort(sorted, (x, y) => x.member.CompareTo(y.member));
            return sorted;
        }
    }
}
