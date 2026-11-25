using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.LedGame;

namespace WarCore.Tests
{
    // RC2-1: what the led squad saves and hashes, field for field, and the saved states it refuses.
    public sealed class Rc21LedStateTests
    {
        private const int Size = 8, Radius = 30000;

        private static LedGame Game() => Small(Size, Radius, new[] { Soldier(1, 10, 20), Soldier(2, 13, 20), Soldier(3, 7, 20), Soldier(4, 40, 20) });

        private static byte[] Payload(int size, int radius, long spacing, ulong leader, int mode, long x, long y, long z, int layer, params (ulong Key, int Spot, bool Refused)[] members)
        {
            var writer = new SimStateWriter();
            writer.Int32(size); writer.Int32(radius); writer.Int64(spacing);
            writer.UInt64(leader); writer.Int32(mode);
            writer.Int64(x); writer.Int64(y); writer.Int64(z); writer.Int32(layer);
            writer.Int32(members.Length);
            foreach ((ulong key, int spot, bool refused) in members) { writer.UInt64(key); writer.Int32(spot); writer.Bool(refused); }
            return writer.ToArray();
        }

        private static ulong Hash(int size, int radius, long spacing, ulong leader, int mode, long x, long y, long z, int layer, params (ulong Key, int Spot, bool Refused)[] members)
        {
            var hash = new StateHash();
            hash.Add("battle.led-squad"); hash.Add(1);
            hash.Add(size); hash.Add(radius); hash.Add(spacing);
            hash.Add(leader); hash.Add(mode);
            hash.Add(x); hash.Add(y); hash.Add(z); hash.Add(layer);
            hash.Add(members.Length);
            foreach ((ulong key, int spot, bool refused) in members) { hash.Add(key); hash.Add(spot); hash.Add(refused); }
            return hash.Value;
        }

        private static byte[] Saved(LedSquad led) { var writer = new SimStateWriter(); led.SaveState(writer); return writer.ToArray(); }

        private static ulong Hashed(LedSquad led) { var hash = new StateHash(); led.WriteState(hash); return hash.Value; }

        [Test]
        public void ASquadIsSavedAndHashedFieldForField()
        {
            LedGame g = Game();
            Assert.That((g.Led.StateTypeId, g.Led.StateVersion), Is.EqualTo(("battle.led-squad", 1)));
            Assert.That(Saved(g.Led), Is.EqualTo(Payload(Size, Radius, g.SpacingRaw, 0, 0, 0, 0, 0, 0)), "before any squad");
            Assert.That(Hashed(g.Led), Is.EqualTo(Hash(Size, Radius, g.SpacingRaw, 0, 0, 0, 0, 0, 0)));
            g.Order(SquadCommandKind.Follow);
            SimVec3 anchor = g.Led.Anchor;
            var members = new (ulong Key, int Spot, bool Refused)[g.Led.MemberCount];
            for (int i = 0; i < members.Length; i++) members[i] = (g.Led.MemberAt(i), g.Led.SpotOf(new StableId(g.Led.MemberAt(i))), false);
            Assert.That(members.Length, Is.EqualTo(3));
            Assert.That(Saved(g.Led), Is.EqualTo(Payload(Size, Radius, g.SpacingRaw, g.Led.Leader, 1, anchor.X.Raw, anchor.Y.Raw, anchor.Z.Raw, 0, members)));
            Assert.That(Hashed(g.Led), Is.EqualTo(Hash(Size, Radius, g.SpacingRaw, g.Led.Leader, 1, anchor.X.Raw, anchor.Y.Raw, anchor.Z.Raw, 0, members)));
        }

        // A state in which every field has a value of its own, loaded into a squad that had three members on their places.
        [Test]
        public void ALoadedStateReplacesTheSquadAndIsSavedAndHashedAsItWasWritten()
        {
            LedGame g = Game();
            g.Order(SquadCommandKind.Follow);
            foreach (ulong key in g.Members()) Assert.That(g.Led.TryPlace(g.Id(key), out _), Is.True, "member " + key);
            byte[] payload = Payload(Size, Radius, g.SpacingRaw, 77, 2, 11, 22, 33, 3, (5UL, 4, true), (9UL, 0, false));
            g.Led.LoadState(new SimStateReader(payload));
            Assert.That((g.Led.Leader, g.Led.Mode, g.Led.AnchorLayer, g.Led.MemberCount), Is.EqualTo((77UL, LedMode.Hold, 3, 2)));
            Assert.That((g.Led.Anchor.X.Raw, g.Led.Anchor.Y.Raw, g.Led.Anchor.Z.Raw), Is.EqualTo((11L, 22L, 33L)));
            Assert.That((g.Led.MemberAt(0), g.Led.MemberAt(1)), Is.EqualTo((5UL, 9UL)));
            Assert.That((g.Led.SpotOf(new StableId(5)), g.Led.SpotOf(new StableId(9)), g.Led.SpotOf(new StableId(7))), Is.EqualTo((4, 0, -1)));
            Assert.That(g.Led.TryPlace(new StableId(5), out _), Is.False, "the places of a loaded squad are worked out on its next step");
            Assert.That(Saved(g.Led), Is.EqualTo(payload));
            Assert.That(Hashed(g.Led), Is.EqualTo(Hash(Size, Radius, g.SpacingRaw, 77, 2, 11, 22, 33, 3, (5UL, 4, true), (9UL, 0, false))));

            g.Led.LoadState(new SimStateReader(Payload(Size, Radius, g.SpacingRaw, 0, 0, 0, 0, 0, 0)));
            Assert.That((g.Led.Leader, g.Led.Mode, g.Led.MemberCount), Is.EqualTo((0UL, LedMode.None, 0)), "a state without a squad empties it");
        }

        [Test]
        public void AStateThatCannotBeIsRefused()
        {
            LedGame g = Game();
            long s = g.SpacingRaw;
            Assert.That(() => g.Led.LoadState(new SimStateReader(Payload(Size, Radius, s, 77, 2, 11, 22, 33, 3, (5UL, 4, true), (9UL, 0, false)))), Throws.Nothing);
            var tooMany = new (ulong Key, int Spot, bool Refused)[Size + 1];
            for (int i = 0; i < tooMany.Length; i++) tooMany[i] = ((ulong)i + 1, i % Size, false);
            var refused = new (string Why, string Says, byte[] Payload)[]
            {
                ("another size", "rules differ", Payload(Size + 1, Radius, s, 77, 2, 11, 22, 33, 3)),
                ("another radius", "rules differ", Payload(Size, Radius + 1, s, 77, 2, 11, 22, 33, 3)),
                ("another spacing", "rules differ", Payload(Size, Radius, s + 1, 77, 2, 11, 22, 33, 3)),
                ("a mode below the first", "mode is out of range", Payload(Size, Radius, s, 77, -1, 11, 22, 33, 3)),
                ("a mode beyond the last", "mode is out of range", Payload(Size, Radius, s, 77, 3, 11, 22, 33, 3)),
                ("a leader without a mode", "a leader without a mode", Payload(Size, Radius, s, 77, 0, 0, 0, 0, 0)),
                ("a mode without a leader", "a leader without a mode", Payload(Size, Radius, s, 0, 1, 0, 0, 0, 0)),
                ("a layer below the ground", "anchor is out of range", Payload(Size, Radius, s, 77, 2, 11, 22, 33, -1)),
                ("an anchor without a leader, x", "anchor is out of range", Payload(Size, Radius, s, 0, 0, 1, 0, 0, 0)),
                ("an anchor without a leader, y", "anchor is out of range", Payload(Size, Radius, s, 0, 0, 0, 1, 0, 0)),
                ("an anchor without a leader, z", "anchor is out of range", Payload(Size, Radius, s, 0, 0, 0, 0, 1, 0)),
                ("an anchor layer without a leader", "anchor is out of range", Payload(Size, Radius, s, 0, 0, 0, 0, 0, 1)),
                ("more members than the size", "count exceeds", Payload(Size, Radius, s, 77, 2, 11, 22, 33, 3, tooMany)),
                ("members without a leader", "members without a leader", Payload(Size, Radius, s, 0, 0, 0, 0, 0, 0, (5UL, 4, false))),
                ("a member without a key", "strictly increasing", Payload(Size, Radius, s, 77, 2, 11, 22, 33, 3, (0UL, 4, false))),
                ("the leader as a member", "strictly increasing", Payload(Size, Radius, s, 77, 2, 11, 22, 33, 3, (77UL, 4, false))),
                ("members out of order", "strictly increasing", Payload(Size, Radius, s, 77, 2, 11, 22, 33, 3, (9UL, 4, false), (5UL, 0, false))),
                ("a member twice", "strictly increasing", Payload(Size, Radius, s, 77, 2, 11, 22, 33, 3, (5UL, 4, false), (5UL, 0, false))),
                ("a spot below the first", "spot is out of range", Payload(Size, Radius, s, 77, 2, 11, 22, 33, 3, (5UL, -1, false))),
                ("a spot beyond the last", "spot is out of range", Payload(Size, Radius, s, 77, 2, 11, 22, 33, 3, (5UL, Size, false))),
                ("two members on a spot", "share a spot", Payload(Size, Radius, s, 77, 2, 11, 22, 33, 3, (5UL, 4, false), (9UL, 4, false))),
            };
            foreach ((string why, string says, byte[] payload) in refused)
                Assert.That(() => g.Led.LoadState(new SimStateReader(payload)), Throws.TypeOf<SimSnapshotException>().With.Message.Contains(says), why);
        }
    }
}
