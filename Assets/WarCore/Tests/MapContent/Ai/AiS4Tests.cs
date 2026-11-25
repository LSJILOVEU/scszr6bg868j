using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // AI-S4: cover (ai.cover*). A pinned unit under engage "bound" walks to the nearest spot that has a wall close in front
    // of it, toward where the fire that pinned it came from (kept by the pressure system), and stands there.
    public sealed partial class AiS4Tests
    {
        private const string BoundAi = "\"engage\": \"bound\", \"boundMoveTicks\": 60, \"boundFireTicks\": 90, \"boundSharePermille\": 700, \"boundMinRangeMm\": 12000";
        // Only a shot that ends on a unit presses it (its neighbours stand a metre away); one such shot pins it, and nobody
        // can break. The number is back under the pinned line about 100 ticks after the last shot (decay 4), 14 with decay 30.
        private static string PressAi(int decay = 4) => "\"pressureDeathRadiusMm\": 500, \"pressureDeathShock\": 3000, \"pressureNearRadiusMm\": 500, "
            + "\"pressureNearShock\": 3000, \"pressureDecayPermille\": " + decay + ", \"pressureBrokenAt\": 7000, \"pressureRecoverAt\": 1000, "
            + "\"pressurePinnedAt\": 2000, \"pressureSpreadPermille\": 0, \"pressureBreakSharePermille\": 0";
        private static string CoverAi(int radius = 9000, int step = 1000, int depth = 2000) =>
            "\"coverRadiusMm\": " + radius + ", \"coverStepMm\": " + step + ", \"coverDepthMm\": " + depth;
        private static string S3(int decay = 4, string engage = BoundAi) => "{ " + engage + ", " + PressAi(decay) + " }";
        private static string S4(int radius = 9000, int step = 1000, int depth = 2000, int decay = 4, string more = null) =>
            "{ " + (more == null ? "" : more + ", ") + BoundAi + ", " + PressAi(decay) + ", " + CoverAi(radius, step, depth) + " }";
        // Cover's constants, written out (Cover.SaltX, SaltZ).
        private const ulong SaltX = 0x1f83d9abfb41bd6bUL, SaltZ = 0x5be0cd19137e2179UL;
        private const int Think = 30;

        private static ContentPack Pack(string ai) => ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
        {
            ["s4-test"] = "{ \"id\": \"s4-test\", \"extends\": \"rwr\"" + (ai == null ? "" : ", \"ai\": " + ai) + " }"
        }, FileContentSource.Rwr()), "s4-test");

        // One wall 1 m thick and 6 m long (x 64..65, z 17..23, 2 m high) east of the attackers, and one behind them
        // (x 54..55, z 5..12) that can only be reached by walking through it.
        private static readonly (int, int, int, int)[] Walls = { (64, 17, 1, 6), (54, 5, 1, 7) };
        private static byte[] Map((int, int, int, int)[] boxes) => WcMapWriter.Write(Wp23.Make(160, 60, 0, 4 * SimFix.Scale, boxes, 2 * SimFix.Scale, null));
        private static readonly SimVec3 Own = SimVec3.Metres(40, 0, 20), Theirs = SimVec3.Metres(110, 0, 20);
        // Where the test's fire comes from: the defenders' point, and a point north of the field that sees every attacker.
        private static readonly SimVec3 East = SimVec3.Metres(110, 0, 20), North = SimVec3.Metres(62, 0, 58);

        // The S3 fixture with walls: 20 armed attackers of team 1 abreast at x (z = 10.5 .. 29.5, keys 1..20), 10 armed
        // defenders of team 2 in their point at (110, 20) (keys 21..30) that crawl. Shots do no damage and no point flips.
        private const int Attackers = 20, Defenders = 10;
        private static M6World Yard(string ai, (int, int, int, int)[] boxes = null, int attackersX = 60)
        {
            var spawns = new List<UnitSpawn>();
            for (int i = 0; i < Attackers; i++) spawns.Add(new UnitSpawn((ulong)i + 1, 0, 1, attackersX * 1000, 10500 + 1000 * i, 6000, 1, 0, false));
            for (int i = 0; i < Defenders; i++) spawns.Add(M6World.U((ulong)(Attackers + i + 1), 2, i < 5 ? 108 : 112, 16 + i % 5 * 2, weapon: 1, speed: 1));
            var points = new[] { new ControlPointDef(1, Theirs, SimFix.FromInt(6), 2), new ControlPointDef(2, Own, SimFix.FromInt(6), 1) };
            var bases = new[] { new SpawnAnchor(1, SimVec3.Metres(6, 0, 20)), new SpawnAnchor(2, SimVec3.Metres(150, 0, 20)) };
            WorldRecipe recipe = M6World.RecipeOf(spawns.ToArray(), M6World.Pack(points, ticks: 1000000, think: Think, bases: bases), M6World.Guns(new ZeroResolver()))
                .WithRuleSet(MatchMode.TestNeverEndsId);
            if (ai != null) recipe = recipe.WithPack(Pack(ai));
            return new M6World(recipe, Map(boxes ?? Array.Empty<(int, int, int, int)>()));
        }

        private static StableId Id(M6World w, ulong key) => w.Sim.Entities.StableOf(w.Unit(key));
        private static SimVec3 At(M6World w, ulong key) => w.Read<Position>(key).Value;
        private static SimVec3 Mm(long x, long z) => new SimVec3(SimFix.Ratio(x, 1000), default, SimFix.Ratio(z, 1000));
        private static long Horizontal2(SimVec3 a, SimVec3 b) { long dx = a.X.Raw - b.X.Raw, dz = a.Z.Raw - b.Z.Raw; return dx * dx + dz * dz; }
        private static long Raw2(long mm) { long raw = SimFix.Ratio(mm, 1000).Raw; return raw * raw; }
        private static bool Thinks(ulong stable, long tick) => (tick + (long)(stable % Think)) % Think == 0;
        private static T Private<T>(object owner, string field) => (T)owner.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(owner);
        private static AiField FieldOf(M6World w) => Private<AiField>(w.System<SimpleAiSystem>(), "field");
        private static IMoveOrders MovesOf(M6World w) => Private<IMoveOrders>(w.System<SimpleAiSystem>(), "moves");

        // An enemy (team 2) shot fired from a point that ends at a point and hurts nobody.
        private static FireRecord Near(M6World w, SimVec3 from, SimVec3 end) =>
            new FireRecord(Id(w, Attackers + 1UL), default, from, end, default, 0, 2, FireOutcome.Open);
        // An enemy shot fired from a point that brings the unit down (as the record says; the unit itself is left alone).
        private static FireRecord Fall(M6World w, ulong victim, SimVec3 from) =>
            new FireRecord(Id(w, Attackers + 1UL), Id(w, victim), from, At(w, victim), default, 1, 2);
        private static PressureRules Rules(int nearShock = 3000, int deathShock = 3000, int decay = 1) =>
            new PressureRules(500, deathShock, 500, nearShock, decay, 7000, 1000, 2000, 0, 0);
        private static SimVec3 From(Pressure p, StableId id)
        {
            Assert.That(p.TryPressedFrom(id, out SimVec3 from), Is.True);
            return from;
        }

        // The splitmix64 finalizer, written out independently of AiParts.Mix.
        private static ulong Finalize(ulong x)
        {
            unchecked
            {
                x = (x ^ (x >> 30)) * 0xbf58476d1ce4e5b9UL;
                x = (x ^ (x >> 27)) * 0x94d049bb133111ebUL;
                return x ^ (x >> 31);
            }
        }

        // ---- The three combat ports ----

        [Test]
        public void Ports_WallWithinLooksOnlyAsFarAsItsReach()
        {
            CombatSystem c = Yard(null, Walls).Combat;
            SimFix two = SimFix.FromInt(2);
            // The wall's west face is at x = 64. From 1.9 m and from 2.1 m west of it, looking east.
            Assert.That(c.WallWithin(Mm(62100, 20000), East, two), Is.True);
            Assert.That(c.WallWithin(Mm(61900, 20000), East, two), Is.False);
            Assert.That(c.WallWithin(Mm(61900, 20000), East, SimFix.Ratio(22, 10)), Is.True);
            // Looking away from the wall, or past its end (z = 23).
            Assert.That(c.WallWithin(Mm(63000, 20000), Mm(20000, 20000), two), Is.False);
            Assert.That(c.WallWithin(Mm(63000, 23500), Mm(110000, 23500), two), Is.False);
            Assert.That(c.WallWithin(Mm(63000, 22500), Mm(110000, 22500), two), Is.True);
            // The look stops at the point it looks toward: a wall behind that point does not count.
            Assert.That(c.WallWithin(Mm(62500, 20000), Mm(63500, 20000), two), Is.False);
            Assert.That(c.WallWithin(Mm(62500, 20000), Mm(64500, 20000), two), Is.True);
            // From the far side the same wall is in front as well; a point looks at itself through nothing.
            Assert.That(c.WallWithin(Mm(66000, 20000), Mm(20000, 20000), two), Is.True);
            Assert.That(c.WallWithin(Mm(63000, 20000), Mm(63000, 20000), two), Is.False);
            // Without a wall anywhere.
            Assert.That(Yard(null).Combat.WallWithin(Mm(62100, 20000), East, two), Is.False);
        }

        [Test]
        public void Ports_SeesIsTheSightLineBetweenTwoPoints()
        {
            CombatSystem c = Yard(null, Walls).Combat;
            Assert.That(c.Sees(Mm(60000, 20000), Mm(70000, 20000)), Is.False, "through the wall");
            Assert.That(c.Sees(Mm(70000, 20000), Mm(60000, 20000)), Is.False, "and back");
            Assert.That(c.Sees(Mm(60000, 20000), Mm(63500, 20000)), Is.True, "up to the wall");
            Assert.That(c.Sees(Mm(60000, 24000), Mm(70000, 24000)), Is.True, "past its end");
            Assert.That(c.Sees(Mm(60000, 16000), Mm(63500, 22000)), Is.True, "to a spot behind it from the side");
            Assert.That(c.Sees(Mm(60000, 20000), Mm(60000, 20000)), Is.True, "a point sees itself");
        }

        // Attackers stand at x = 60, 1 m apart (key 10 at z 19.5, key 11 at z 20.5); a body is 0.25 m in radius.
        [Test]
        public void Ports_FriendOnWalkIsATeammateTheBodyWouldTouch()
        {
            M6World w = Yard(null);
            CombatSystem c = w.Combat;
            EntityId ten = w.Unit(10), five = w.Unit(5), three = w.Unit(3);
            Assert.That(c.FriendOnWalk(ten, Mm(58000, 15000), Mm(59000, 15000)), Is.False, "nobody near");
            Assert.That(c.FriendOnWalk(ten, Mm(57000, 20500), Mm(59600, 20500)), Is.True, "ends 0.4 m from key 11");
            Assert.That(c.FriendOnWalk(ten, Mm(57000, 20500), Mm(59400, 20500)), Is.False, "ends 0.6 m from it");
            Assert.That(c.FriendOnWalk(ten, Mm(59600, 20500), Mm(57000, 20500)), Is.True, "starts 0.4 m from it");
            // Across the row at z 19.9: 0.4 m past key 10, 0.6 m past key 11.
            Assert.That(c.FriendOnWalk(ten, Mm(59000, 19900), Mm(61000, 19900)), Is.False, "the unit itself is not in its own way");
            Assert.That(c.FriendOnWalk(five, Mm(59000, 19900), Mm(61000, 19900)), Is.True, "key 10 is in key 5's way");
            Assert.That(c.FriendOnWalk(five, Mm(59000, 20000), Mm(59400, 20000)), Is.False, "the walk ends before the row");
            // A walk of no length is the spot alone.
            Assert.That(c.FriendOnWalk(ten, Mm(60300, 12500), Mm(60300, 12500)), Is.True, "key 3 stands there");
            Assert.That(c.FriendOnWalk(three, Mm(60300, 12500), Mm(60300, 12500)), Is.False);
            // Only teammates: the defenders (key 21 at 108, 16; key 22 at 108, 18) are in each other's way, not in an attacker's.
            Assert.That(c.FriendOnWalk(ten, Mm(107000, 16000), Mm(109000, 16000)), Is.False);
            Assert.That(c.FriendOnWalk(w.Unit(22), Mm(107000, 16000), Mm(109000, 16000)), Is.True);
        }

        // ---- Where the fire came from ----

        [Test]
        public void Source_IsWhereTheShotThatPinnedCameFrom()
        {
            M6World w = Yard(null);
            var p = new Pressure(w.Combat, Rules(), true);
            StableId a = Id(w, 5), b = Id(w, 10);
            Assert.That(p.TryPressedFrom(a, out SimVec3 none), Is.False);
            Assert.That(none, Is.EqualTo(default(SimVec3)));
            // Two shots end on unit 5 in one tick: the first record counts.
            p.Advance(new[] { Near(w, East, At(w, 5)), Near(w, North, At(w, 5)) });
            Assert.That(From(p, a), Is.EqualTo(East));
            Assert.That(p.TryPressedFrom(b, out _), Is.False, "nobody shot at unit 10");
            // A shot of its own team presses nobody and leaves no source.
            p.Advance(new[] { new FireRecord(a, default, North, At(w, 10), default, 0, 1, FireOutcome.Open) });
            Assert.That(p.TryPressedFrom(b, out _), Is.False);
            // Without sources the same fire leaves nothing to read.
            var plain = new Pressure(w.Combat, Rules());
            plain.Advance(new[] { Near(w, East, At(w, 5)) });
            Assert.That(plain.Level(a), Is.EqualTo(AiNerve.Pinned));
            Assert.That(plain.TryPressedFrom(a, out SimVec3 nothing), Is.False);
            Assert.That(nothing, Is.EqualTo(default(SimVec3)));
        }

        // A teammate brought down beside the unit gives the source only in a tick without a near shot.
        [Test]
        public void Source_ANearShotComesBeforeAFall()
        {
            M6World w = Yard(null);
            // The fall radius covers the neighbours (1 m apart); the near radius only the unit itself.
            var rules = new PressureRules(1500, 3000, 500, 3000, 1, 7000, 1000, 2000, 0, 0);
            var p = new Pressure(w.Combat, rules, true);
            SimVec3 west = SimVec3.Metres(10, 0, 20);
            // Units 4 and 6 fall (from the north, then from the west); unit 5 between them also has a shot from the east on it.
            p.Advance(new[] { Fall(w, 4, North), Fall(w, 6, west), Near(w, East, At(w, 5)) });
            Assert.That(From(p, Id(w, 5)), Is.EqualTo(East), "the near shot, though the falls come first in the records");
            Assert.That(From(p, Id(w, 3)), Is.EqualTo(North), "beside unit 4 only");
            Assert.That(From(p, Id(w, 7)), Is.EqualTo(west), "beside unit 6 only");
            // Two falls beside one unit and no near shot: the first fallen in the records.
            var q = new Pressure(w.Combat, rules, true);
            q.Advance(new[] { Fall(w, 6, west), Fall(w, 4, North) });
            Assert.That(From(q, Id(w, 5)), Is.EqualTo(west));
            // The same unit falling twice in a tick counts once, with the first record's origin.
            var r = new Pressure(w.Combat, rules, true);
            r.Advance(new[] { Fall(w, 4, North), Fall(w, 4, west) });
            Assert.That(From(r, Id(w, 5)), Is.EqualTo(North));
            Assert.That(r.Value(Id(w, 5)), Is.EqualTo(3000));
        }

        // The source changes only while the number stands below the pinned line (2000) after the tick's decay.
        [TestCase(2002, true)]    // 2002 loses 2 in the next tick: 2000, still at the line, the source is kept
        [TestCase(2001, false)]   // 2001 loses 2: 1999, below the line, the next shot gives a new source
        public void Source_IsKeptFromThePinningShotUntilCalm(int shock, bool kept)
        {
            M6World w = Yard(null);
            var p = new Pressure(w.Combat, Rules(nearShock: shock), true);
            StableId a = Id(w, 5);
            p.Advance(new[] { Near(w, East, At(w, 5)) });
            Assert.That(p.Value(a), Is.EqualTo(shock));
            Assert.That(From(p, a), Is.EqualTo(East));
            p.Advance(new[] { Near(w, North, At(w, 5)) });
            Assert.That(p.Value(a), Is.EqualTo(shock - 2 + shock));
            Assert.That(From(p, a), Is.EqualTo(kept ? East : North));
            // While it stays pinned nothing changes the source, however long the fire lasts.
            for (int i = 0; i < 50; i++) p.Advance(new[] { Near(w, SimVec3.Metres(10, 0, 20), At(w, 5)) });
            Assert.That(From(p, a), Is.EqualTo(kept ? East : North));
            // No fire: the entry and its source stay until the number is gone.
            p.Advance(ReadOnlySpan<FireRecord>.Empty);
            Assert.That(From(p, a), Is.EqualTo(kept ? East : North));
        }

        // ---- The parts ----

        [Test]
        public void Parts_CoverNumbersGoTogether()
        {
            Assert.DoesNotThrow(() => new AiParts());
            Assert.DoesNotThrow(() => new AiParts(coverRadiusRaw: 1, coverStepRaw: 1, coverDepthRaw: 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AiParts(coverRadiusRaw: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AiParts(coverRadiusRaw: 1, coverStepRaw: 0, coverDepthRaw: 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AiParts(coverRadiusRaw: 1, coverStepRaw: 1, coverDepthRaw: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AiParts(coverRadiusRaw: 1, coverStepRaw: -1, coverDepthRaw: 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AiParts(coverRadiusRaw: 1, coverStepRaw: 1, coverDepthRaw: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AiParts(coverStepRaw: 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AiParts(coverDepthRaw: 1));
            var parts = new AiParts(coverRadiusRaw: 3, coverStepRaw: 2, coverDepthRaw: 1);
            Assert.That((parts.CoverRadiusRaw, parts.CoverStepRaw, parts.CoverDepthRaw), Is.EqualTo((3L, 2L, 1L)));
        }

        // The scheduler takes cover rules only together with a pressure system that keeps sources.
        [Test]
        public void Parts_CoverNeedsThePressureSources()
        {
            M6World w = Yard(null);
            var cover = new AiParts(coverRadiusRaw: 9 * SimFix.Scale, coverStepRaw: SimFix.Scale, coverDepthRaw: 2 * SimFix.Scale);
            SimpleAiSystem Make(AiParts parts, Pressure pressure) =>
                new SimpleAiSystem(w.Combat, w.Recipe.Combat, w.Recipe.Match, w.Capture, MovesOf(w), parts: parts, pressure: pressure);
            Assert.Throws<ArgumentException>(() => Make(cover, null));
            Assert.Throws<ArgumentException>(() => Make(cover, new Pressure(w.Combat, Rules())));
            Assert.DoesNotThrow(() => Make(cover, new Pressure(w.Combat, Rules(), true)));
            Assert.DoesNotThrow(() => Make(new AiParts(), null));
        }
    }
}
