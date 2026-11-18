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
    // AI-S3: the pressure system (ai.pressure*) keeps one number per unit that rises with enemy shots landing near it and
    // teammates brought down beside it and decays by a share of itself. It hands the engage part a level (AiUnit.Nerve):
    // "bound" stands when Pinned and falls back to the team's nearest point when Broken; "hold" ignores it.
    public sealed partial class AiS3Tests
    {
        private const string BoundAi = "\"engage\": \"bound\", \"boundMoveTicks\": 60, \"boundFireTicks\": 90, \"boundSharePermille\": 700, \"boundMinRangeMm\": 12000";
        private const string PressureAi = "\"pressureDeathRadiusMm\": 3000, \"pressureDeathShock\": 2000, \"pressureNearRadiusMm\": 3000, \"pressureNearShock\": 1000, "
            + "\"pressureDecayPermille\": 12, \"pressureBrokenAt\": 7000, \"pressureRecoverAt\": 1000, \"pressurePinnedAt\": 2000, "
            + "\"pressureSpreadPermille\": 500, \"pressureBreakSharePermille\": 300";
        private const string S3 = "{ " + BoundAi + ", " + PressureAi + " }";
        // The system's constants, written out (Pressure.CourageSalt, BreakSalt).
        private const ulong CourageSalt = 0x510e527fade682d1UL, BreakSalt = 0x9b05688c2b3e6c1fUL;
        private const int Think = 30;
        private static readonly string[] Fields =
        {
            "pressureDeathRadiusMm", "pressureDeathShock", "pressureNearRadiusMm", "pressureNearShock", "pressureDecayPermille",
            "pressureBrokenAt", "pressureRecoverAt", "pressurePinnedAt", "pressureSpreadPermille", "pressureBreakSharePermille",
        };
        private static readonly int[] Values = { 3000, 2000, 3000, 1000, 12, 7000, 1000, 2000, 500, 300 };

        private static string Json(int[] values, string head = null, int skip = -1)
        {
            var parts = new List<string>();
            if (head != null) parts.Add(head);
            for (int i = 0; i < Fields.Length; i++) if (i != skip) parts.Add("\"" + Fields[i] + "\": " + values[i]);
            return "{ " + string.Join(", ", parts) + " }";
        }
        private static int[] With(int index, int value) { int[] copy = (int[])Values.Clone(); copy[index] = value; return copy; }

        private static ContentPack Pack(string ai) => ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
        {
            ["s3-test"] = "{ \"id\": \"s3-test\", \"extends\": \"rwr\"" + (ai == null ? "" : ", \"ai\": " + ai) + " }"
        }, FileContentSource.Rwr()), "s3-test");

        private static byte[] Flat() => WcMapWriter.Write(Wp23.Make(160, 60, 0, 4 * SimFix.Scale, Array.Empty<(int, int, int, int)>(), 2 * SimFix.Scale, null));
        private static readonly SimVec3 Own = SimVec3.Metres(40, 0, 20), Theirs = SimVec3.Metres(110, 0, 20);

        // The fixture: 20 armed attackers of team 1 abreast at x = 60 (z = 10.5 .. 29.5, one metre apart, keys 1..20) and 10
        // armed defenders of team 2 in their point at (110, 20) (keys 21..30), 50 m away and out of sight. Team 1 owns a point
        // at (40, 20) behind the attackers unless ownPoint is false. Shots do no damage, nobody dies, no point flips. The
        // defenders crawl at 1 mm/s unless told otherwise, so they stay where they are whatever they decide.
        private const int Attackers = 20, Defenders = 10;
        private static M6World Field(string ai, bool ownPoint = true, bool ownBase = true, int attackersX = 60, long defenderSpeed = 1)
        {
            var spawns = new List<UnitSpawn>();
            for (int i = 0; i < Attackers; i++) spawns.Add(new UnitSpawn((ulong)i + 1, 0, 1, attackersX * 1000, 10500 + 1000 * i, 6000, 1, 0, false));
            for (int i = 0; i < Defenders; i++) spawns.Add(M6World.U((ulong)(Attackers + i + 1), 2, i < 5 ? 108 : 112, 16 + i % 5 * 2, weapon: 1, speed: defenderSpeed));
            var points = new List<ControlPointDef> { new ControlPointDef(1, Theirs, SimFix.FromInt(6), 2) };
            if (ownPoint) points.Add(new ControlPointDef(2, Own, SimFix.FromInt(6), 1));
            SpawnAnchor[] bases = ownBase ? new[] { new SpawnAnchor(1, SimVec3.Metres(6, 0, 20)), new SpawnAnchor(2, SimVec3.Metres(150, 0, 20)) }
                : new[] { new SpawnAnchor(2, SimVec3.Metres(150, 0, 20)) };
            WorldRecipe recipe = M6World.RecipeOf(spawns.ToArray(), M6World.Pack(points.ToArray(), ticks: 1000000, think: Think, bases: bases), M6World.Guns(new ZeroResolver()))
                .WithRuleSet(MatchMode.TestNeverEndsId);
            if (ai != null) recipe = recipe.WithAiRules(Pack(ai));
            return new M6World(recipe, Flat());
        }
        // Rules under which only an injected fall presses anybody: real shots add nothing, one fall beside a unit takes it to
        // the top, and the number halves in about two seconds. breakShare 1000 lets every unit break, 0 none.
        private static string Inject(int breakShare, string engage = BoundAi, int radius = 3000, int decay = 12) =>
            Json(new[] { radius, Pressure.Max, 3000, 0, decay, 7000, 1000, 2000, 0, breakShare }, engage);

        private static M6World Battle(ulong seed, bool? fair, string ai) =>
            new M6World(FairEFixture.Recipe("control", seed, fair).WithAiRules(Pack(ai)), FairEFixture.Map("control"));

        private static StableId Id(M6World w, ulong key) => w.Sim.Entities.StableOf(w.Unit(key));
        private static SimVec3 At(M6World w, ulong key) => w.Read<Position>(key).Value;
        private static long Horizontal2(SimVec3 a, SimVec3 b) { long dx = a.X.Raw - b.X.Raw, dz = a.Z.Raw - b.Z.Raw; return dx * dx + dz * dz; }
        private static long Metres2(int m) => (long)m * SimFix.Scale * m * SimFix.Scale;
        private static SimVec3 Offset(SimVec3 from, long xMm, long zMm = 0) => new SimVec3(from.X + SimFix.Ratio(xMm, 1000), from.Y, from.Z + SimFix.Ratio(zMm, 1000));
        private static bool Thinks(ulong stable, long tick) => (tick + (long)(stable % Think)) % Think == 0;

        // A shot of the given team that ends at a point and hurts nobody.
        private static FireRecord Shot(M6World w, int team, SimVec3 end) =>
            new FireRecord(Id(w, team == 1 ? 1UL : Attackers + 1UL), default, end, end, default, 0, team, FireOutcome.Open);
        // A shot that brings the unit down (as the record says; the unit itself is left alone).
        private static FireRecord Fall(M6World w, ulong victim, int shooterTeam = 2) =>
            new FireRecord(Id(w, shooterTeam == 1 ? 1UL : Attackers + 1UL), Id(w, victim), At(w, victim), At(w, victim), default, 1, shooterTeam);
        private static PressureRules Rules(int deathRadius = 3000, int deathShock = 2000, int nearRadius = 3000, int nearShock = 1000, int decay = 12,
            int brokenAt = 7000, int recoverAt = 1000, int pinnedAt = 2000, int spread = 0, int breakShare = 1000) =>
            new PressureRules(deathRadius, deathShock, nearRadius, nearShock, decay, brokenAt, recoverAt, pinnedAt, spread, breakShare);
        private static int Count(object system) => (int)system.GetType().GetProperty("Count", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(system);
        private static int Entries(Pressure pressure) => (int)typeof(Pressure).GetProperty("Count", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(pressure);

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
        private static int Decayed(int value, int permille) => value - Math.Max(1, (int)((long)value * permille / 1000));

        // ---- The number ----

        [Test]
        public void Near_EnemyShotWithinTheRadiusOnceATick()
        {
            M6World w = Field(null);
            var p = new Pressure(w.Combat, Rules());
            SimVec3 here = At(w, 10);
            StableId me = Id(w, 10), other = Id(w, 1);
            // Exactly on the radius counts; one millimetre beyond does not.
            p.Advance(new[] { Shot(w, 2, Offset(here, 3001)) });
            Assert.That(p.Value(me), Is.EqualTo(0));
            p.Advance(new[] { Shot(w, 2, Offset(here, 3000)) });
            Assert.That(p.Value(me), Is.EqualTo(1000));
            // Height does not count.
            var q = new Pressure(w.Combat, Rules());
            SimVec3 high = Offset(here, 0, 2999); high = new SimVec3(high.X, high.Y + SimFix.FromInt(50), high.Z);
            q.Advance(new[] { Shot(w, 2, high) });
            Assert.That(q.Value(me), Is.EqualTo(1000));
            // Two shots in one tick press once; the unit at z = 10.5, 9 m away, feels nothing.
            var r = new Pressure(w.Combat, Rules());
            r.Advance(new[] { Shot(w, 2, here), Shot(w, 2, Offset(here, 100)) });
            Assert.That(r.Value(me), Is.EqualTo(1000));
            Assert.That(r.Value(other), Is.EqualTo(0));
            // A shot of the unit's own team does not press it; it presses an enemy standing there.
            var s = new Pressure(w.Combat, Rules());
            s.Advance(new[] { Shot(w, 1, here), Shot(w, 1, At(w, 21)) });
            Assert.That(s.Value(me), Is.EqualTo(0));
            Assert.That(s.Value(Id(w, 21)), Is.EqualTo(1000));
            // The next tick adds on top of what decay left.
            r.Advance(new[] { Shot(w, 2, here) });
            Assert.That(r.Value(me), Is.EqualTo(Decayed(1000, 12) + 1000));
        }

        [Test]
        public void Death_TeammateWithinTheRadiusOncePerFallen()
        {
            M6World w = Field(null);
            SimVec3 victim = At(w, 10);
            // Attackers stand one metre apart: keys 7..13 are within 3 m of key 10, key 6 and 14 are 4 m away.
            var p = new Pressure(w.Combat, Rules(nearShock: 0));
            p.Advance(new[] { Fall(w, 10) });
            for (ulong key = 1; key <= Attackers; key++)
                Assert.That(p.Value(Id(w, key)), Is.EqualTo(key >= 7 && key <= 13 && key != 10 ? 2000 : 0), "key " + key);
            for (ulong key = Attackers + 1; key <= Attackers + Defenders; key++) Assert.That(p.Value(Id(w, key)), Is.EqualTo(0), "key " + key);
            // The radius is inclusive to the millimetre (key 13 is exactly 3000 mm away).
            var edge = new Pressure(w.Combat, Rules(deathRadius: 2999, nearShock: 0));
            edge.Advance(new[] { Fall(w, 10) });
            Assert.That(edge.Value(Id(w, 13)), Is.EqualTo(0));
            Assert.That(edge.Value(Id(w, 12)), Is.EqualTo(2000));
            // Two fall in one tick: a unit beside both rises twice. The same unit named by two records counts once.
            var two = new Pressure(w.Combat, Rules(nearShock: 0));
            two.Advance(new[] { Fall(w, 10), Fall(w, 12), Fall(w, 10) });
            Assert.That(two.Value(Id(w, 11)), Is.EqualTo(4000));
            Assert.That(two.Value(Id(w, 10)), Is.EqualTo(2000), "the fallen unit feels the other fall only");
            Assert.That(two.Value(Id(w, 8)), Is.EqualTo(2000));
            // An enemy falling presses its own side, not the attackers; a record without damage is not a fall.
            var foe = new Pressure(w.Combat, Rules(nearShock: 0, deathRadius: 100000));
            foe.Advance(new[] { Fall(w, 21, shooterTeam: 1), new FireRecord(Id(w, 21), Id(w, 5), victim, victim, default, 0, 2) });
            for (ulong key = 1; key <= Attackers; key++) Assert.That(foe.Value(Id(w, key)), Is.EqualTo(0), "key " + key);
            Assert.That(foe.Value(Id(w, 22)), Is.EqualTo(2000));
            // The shot that brings a neighbour down is also a shot landing near: both count.
            var both = new Pressure(w.Combat, Rules());
            both.Advance(new[] { Fall(w, 10) });
            Assert.That(both.Value(Id(w, 11)), Is.EqualTo(3000));
        }

        [Test]
        public void Decay_TakesAShareAndDrainsToNothing()
        {
            M6World w = Field(null);
            var p = new Pressure(w.Combat, Rules(deathShock: Pressure.Max, nearShock: 0));
            StableId me = Id(w, 11);
            p.Advance(new[] { Fall(w, 10) });
            Assert.That(p.Value(me), Is.EqualTo(Pressure.Max));
            // Another fall cannot lift it above the top.
            p.Advance(new[] { Fall(w, 10) });
            Assert.That(p.Value(me), Is.EqualTo(Pressure.Max));
            int expected = Pressure.Max, ticks = 0;
            while (expected > 0)
            {
                p.Advance(default);
                expected = Decayed(expected, 12); ticks++;
                Assert.That(p.Value(me), Is.EqualTo(expected), "tick " + ticks);
            }
            // 12 thousandths a tick: half in under 60 ticks; below 84 the loss is 1 a tick, so it reaches zero.
            Assert.That(ticks, Is.InRange(400, 600));
            Assert.That(Entries(p), Is.EqualTo(0));
            Assert.That(Decayed(Decayed(10000, 12), 12), Is.EqualTo(9762));
        }

        [Test]
        public void Courage_ScalesTheRiseByKey()
        {
            foreach (ulong key in new ulong[] { 1, 2, 77, 0xDEADBEEF })
            {
                int permille = (int)(Finalize(key ^ CourageSalt) % 1000UL);
                Assert.That(Pressure.Courage(key, 500), Is.EqualTo(1000 + (permille - 500) * 500 / 500), "key " + key);
                Assert.That(Pressure.Courage(key, 300), Is.EqualTo(1000 + (permille - 500) * 300 / 500), "key " + key);
                Assert.That(Pressure.Courage(key, 0), Is.EqualTo(1000));
                Assert.That(Pressure.Breaks(key, 300), Is.EqualTo((int)(Finalize(key ^ BreakSalt) % 1000UL) < 300), "key " + key);
                Assert.That(Pressure.Breaks(key, 0), Is.False);
                Assert.That(Pressure.Breaks(key, 1000), Is.True);
            }
            M6World w = Field(null);
            var p = new Pressure(w.Combat, Rules(spread: 500));
            var flat = new Pressure(w.Combat, Rules(spread: 0));
            p.Advance(new[] { Fall(w, 10) }); flat.Advance(new[] { Fall(w, 10) });
            var seen = new HashSet<int>();
            foreach (ulong key in new ulong[] { 8, 9, 11, 12 })
            {
                ulong stable = Id(w, key).Value;
                int scale = 1000 + ((int)(Finalize(stable ^ CourageSalt) % 1000UL) - 500) * 500 / 500;
                Assert.That(scale, Is.InRange(500, 1499));
                Assert.That(p.Value(Id(w, key)), Is.EqualTo(3000 * scale / 1000), "key " + key);
                Assert.That(flat.Value(Id(w, key)), Is.EqualTo(3000), "key " + key);
                seen.Add(scale);
            }
            Assert.That(seen.Count, Is.GreaterThan(1), "the four units must not all share one scale");
        }

        // ---- The level ----

        [Test]
        public void Level_PinnedAndBrokenAtTheLines()
        {
            M6World w = Field(null);
            StableId me = Id(w, 11);
            (int shock, AiNerve level)[] cases =
            {
                (1999, AiNerve.Calm), (2000, AiNerve.Pinned), (6999, AiNerve.Pinned), (7000, AiNerve.Broken),
            };
            foreach (var (shock, level) in cases)
            {
                var p = new Pressure(w.Combat, Rules(deathShock: shock, nearShock: 0));
                p.Advance(new[] { Fall(w, 10) });
                Assert.That((p.Value(me), p.Level(me)), Is.EqualTo((shock, level)));
                Assert.That(p.Level(Id(w, 1)), Is.EqualTo(AiNerve.Calm));
            }
        }

        // Broken holds until the number is back at the recover line, one tick at a time.
        [Test]
        public void Level_BrokenLastsDownToTheRecoverLine()
        {
            M6World w = Field(null);
            StableId me = Id(w, 11);
            var p = new Pressure(w.Combat, Rules(deathShock: 7000, nearShock: 0, decay: 1));
            p.Advance(new[] { Fall(w, 10) });
            int value = 7000; bool sawAbove = false, sawAt = false;
            while (value > 0)
            {
                p.Advance(default);
                value = Decayed(value, 1);
                AiNerve expected = value > 1000 ? AiNerve.Broken : AiNerve.Calm;
                Assert.That((p.Value(me), p.Level(me)), Is.EqualTo((value, expected)), "value " + value);
                sawAbove |= value == 1001; sawAt |= value == 1000;
            }
            Assert.That(sawAbove && sawAt, Is.True, "the walk must pass 1001 and 1000");
            // Out of Broken, the same number below the broken line is Pinned or Calm again, and breaks again only at the line.
            var again = new Pressure(w.Combat, Rules(deathShock: 3500, nearShock: 0, decay: 1));
            again.Advance(new[] { Fall(w, 10) });
            Assert.That(again.Level(me), Is.EqualTo(AiNerve.Pinned));
            again.Advance(new[] { Fall(w, 10) });
            Assert.That((again.Value(me), again.Level(me)), Is.EqualTo((Decayed(3500, 1) + 3500, AiNerve.Pinned)));
            again.Advance(new[] { Fall(w, 10) });
            Assert.That(again.Level(me), Is.EqualTo(AiNerve.Broken));
        }

        [Test]
        public void Level_OnlyTheBreakShareBreaks()
        {
            M6World w = Field(null);
            var p = new Pressure(w.Combat, Rules(deathShock: Pressure.Max, nearShock: 0, breakShare: 300, deathRadius: 100000));
            p.Advance(new[] { Fall(w, 10), Fall(w, 11) });
            int broken = 0, pinned = 0;
            for (ulong key = 1; key <= Attackers; key++)
            {
                StableId id = Id(w, key);
                bool breaks = (int)(Finalize(id.Value ^ BreakSalt) % 1000UL) < 300;
                Assert.That(p.Value(id), Is.EqualTo(Pressure.Max), "key " + key);
                Assert.That(p.Level(id), Is.EqualTo(breaks ? AiNerve.Broken : AiNerve.Pinned), "key " + key);
                if (breaks) broken++; else pinned++;
            }
            Assert.That(broken, Is.GreaterThan(0));
            Assert.That(pinned, Is.GreaterThan(0));
        }

        [Test]
        public void Rules_AreChecked()
        {
            Assert.DoesNotThrow(() => Rules());
            Assert.Throws<ArgumentOutOfRangeException>(() => Rules(deathRadius: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Rules(deathShock: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Rules(deathShock: Pressure.Max + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Rules(nearRadius: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Rules(nearShock: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Rules(nearShock: Pressure.Max + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Rules(decay: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Rules(decay: 1001));
            Assert.Throws<ArgumentOutOfRangeException>(() => Rules(brokenAt: 0, recoverAt: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Rules(brokenAt: Pressure.Max + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Rules(recoverAt: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Rules(recoverAt: 7000));
            Assert.Throws<ArgumentOutOfRangeException>(() => Rules(pinnedAt: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Rules(pinnedAt: Pressure.Max + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Rules(spread: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Rules(spread: 1001));
            Assert.Throws<ArgumentOutOfRangeException>(() => Rules(breakShare: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Rules(breakShare: 1001));
            Assert.DoesNotThrow(() => Rules(recoverAt: 6999));
            Assert.Throws<ArgumentException>(() => new Pressure(Field(null).Combat, default));
        }

        // ---- The parts ----

        [Test]
        public void Parts_AnswerTheNerve()
        {
            M6World w = Field(null);
            AiField field = (AiField)typeof(SimpleAiSystem).GetField("field", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(w.System<SimpleAiSystem>());
            SimVec3 here = SimVec3.Metres(0, 0, 0), enemy = SimVec3.Metres(30, 0, 0);
            var hold = new HoldEngage();
            // The share, window and range are checked by AiS1Tests; here only the nerve matters, so take a unit that advances when calm.
            var bound = new BoundEngage(60, 30, 1000, 12000);
            ulong key = 1;
            while (bound.Decide(new AiUnit(new StableId(key), 1, here), enemy, field) != AiEngage.Advance) key++;
            foreach (AiNerve nerve in new[] { AiNerve.Calm, AiNerve.Pinned, AiNerve.Broken })
            {
                var unit = new AiUnit(new StableId(key), 1, here, nerve);
                Assert.That(unit.Nerve, Is.EqualTo(nerve));
                Assert.That(hold.Decide(in unit, enemy, field), Is.EqualTo(AiEngage.Hold), nerve.ToString());
                Assert.That(hold.Unseen(in unit, field), Is.EqualTo(AiEngage.Advance), nerve.ToString());
            }
            var calm = new AiUnit(new StableId(key), 1, here);
            var pinned = new AiUnit(new StableId(key), 1, here, AiNerve.Pinned);
            var broken = new AiUnit(new StableId(key), 1, here, AiNerve.Broken);
            Assert.That(calm.Nerve, Is.EqualTo(AiNerve.Calm));
            Assert.That(bound.Decide(in calm, enemy, field), Is.EqualTo(AiEngage.Advance));
            Assert.That(bound.Decide(in pinned, enemy, field), Is.EqualTo(AiEngage.Hold));
            Assert.That(bound.Decide(in broken, enemy, field), Is.EqualTo(AiEngage.Fallback));
            Assert.That(bound.Unseen(in calm, field), Is.EqualTo(AiEngage.Advance));
            Assert.That(bound.Unseen(in pinned, field), Is.EqualTo(AiEngage.Hold));
            Assert.That(bound.Unseen(in broken, field), Is.EqualTo(AiEngage.Fallback));
            // The pressure system does not touch the bound part's digest.
            Assert.That(ContentPolicies.Parts(Pack(S3).Section<AiRules>(), default, Think).Engage.Fold(5),
                Is.EqualTo(ContentPolicies.Parts(Pack("{ " + BoundAi + " }").Section<AiRules>(), default, Think).Engage.Fold(5)));
        }
    }
}
