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
    public sealed class AiS3Tests
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

        // ---- The scheduler ----

        // Keys 7..13 without 10: the attackers within 3 m of key 10 while they stand abreast.
        private static readonly ulong[] Pressed = { 7, 8, 9, 11, 12, 13 };
        // Every attacker but key 10: who a fall of key 10 presses under a radius that covers the field. Once the attackers
        // walk they are no longer abreast, so the scheduler tests use that radius.
        private static readonly ulong[] Rest = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 };
        private const int Wide = 100000;

        // A broken unit that sees nobody goes to its team's point behind it, stands there, and goes forward again once
        // it has recovered. Key 10, which nothing presses, walks on.
        [Test]
        public void Broken_FallsBackToTheNearestOwnPointAndReturns()
        {
            // 4 thousandths a tick: 10000 is above 1000 for more than 570 ticks.
            M6World w = Field(Inject(1000, BoundAi, Wide, 4));
            Pressure p = w.System<Pressure>();
            Assert.That(p, Is.Not.Null);
            w.Step(5);
            p.Advance(new[] { Fall(w, 10) });
            foreach (ulong key in Rest) Assert.That(p.Level(Id(w, key)), Is.EqualTo(AiNerve.Broken), "key " + key);
            Assert.That(p.Level(Id(w, 10)), Is.EqualTo(AiNerve.Calm));
            w.Step(Think);
            for (ulong key = 1; key <= Attackers; key++)
            {
                MoveIntent order = w.Read<MoveIntent>(key);
                Assert.That(order.Active, Is.True, "key " + key);
                Assert.That(Horizontal2(order.Target, key == 10 ? Theirs : Own), Is.LessThanOrEqualTo(Metres2(6)), "key " + key);
            }
            // 20 m at 6 m/s is 100 ticks, and nineteen units need a little longer to sort out their slots.
            w.Step(270);
            foreach (ulong key in Rest)
            {
                Assert.That(p.Level(Id(w, key)), Is.EqualTo(AiNerve.Broken), "key " + key);
                // A unit stops within the arrival distance of its slot, so one at the rim may stand just outside the circle.
                Assert.That(Horizontal2(At(w, key), Own), Is.LessThanOrEqualTo(Metres2(7)), "key " + key);
                Assert.That(w.Read<MoveIntent>(key).Active, Is.False, "key " + key);
            }
            SimVec3 stood = At(w, 8);
            w.Step(20);
            Assert.That(At(w, 8), Is.EqualTo(stood));
            // Recovered (after tick 580 or so): forward again.
            w.Step(300 + 2 * Think);
            foreach (ulong key in Rest)
            {
                Assert.That(p.Level(Id(w, key)), Is.EqualTo(AiNerve.Calm), "key " + key);
                MoveIntent order = w.Read<MoveIntent>(key);
                Assert.That(order.Active, Is.True, "key " + key);
                Assert.That(Horizontal2(order.Target, Theirs), Is.LessThanOrEqualTo(Metres2(6)), "key " + key);
            }
        }

        // Without a point of its own a broken unit goes to its base; without a base either it stands.
        [TestCase(true)]
        [TestCase(false)]
        public void Broken_WithoutAPointGoesToTheBaseOrStands(bool ownBase)
        {
            M6World w = Field(Inject(1000, BoundAi, Wide), ownPoint: false, ownBase: ownBase);
            Pressure p = w.System<Pressure>();
            w.Step(Think + 5);
            foreach (ulong key in Rest) Assert.That(w.Read<MoveIntent>(key).Active, Is.True, "walking to the enemy point first, key " + key);
            p.Advance(new[] { Fall(w, 10) });
            w.Step(Think);
            SimVec3 home = SimVec3.Metres(6, 0, 20);
            foreach (ulong key in Rest)
            {
                MoveIntent order = w.Read<MoveIntent>(key);
                if (!ownBase) { Assert.That(order.Active, Is.False, "key " + key); continue; }
                Assert.That(order.Active, Is.True, "key " + key);
                Assert.That((order.Target.X.Raw, order.Target.Z.Raw), Is.EqualTo((home.X.Raw, home.Z.Raw)), "key " + key);
            }
            Assert.That(w.Read<MoveIntent>(10).Active, Is.True);
            Assert.That(Horizontal2(w.Read<MoveIntent>(10).Target, Theirs), Is.LessThanOrEqualTo(Metres2(6)));
        }

        // A pinned unit that sees nobody stops where it is until the pressure is off; it does not fall back.
        [Test]
        public void Pinned_StandsWhileItLasts()
        {
            M6World w = Field(Inject(0, BoundAi, Wide));
            Pressure p = w.System<Pressure>();
            w.Step(Think + 5);
            p.Advance(new[] { Fall(w, 10) });
            foreach (ulong key in Rest) Assert.That(p.Level(Id(w, key)), Is.EqualTo(AiNerve.Pinned), "key " + key);
            w.Step(Think);
            var stood = new Dictionary<ulong, SimVec3>();
            foreach (ulong key in Rest)
            {
                Assert.That(w.Read<MoveIntent>(key).Active, Is.False, "key " + key);
                stood[key] = At(w, key);
            }
            Assert.That(w.Read<MoveIntent>(10).Active, Is.True);
            // 10000 falls below 2000 after 134 ticks; 36 ticks are gone.
            w.Step(60);
            foreach (ulong key in Rest)
            {
                Assert.That(p.Level(Id(w, key)), Is.EqualTo(AiNerve.Pinned), "key " + key);
                Assert.That(At(w, key), Is.EqualTo(stood[key]), "key " + key);
            }
            // Calm from tick 135 after the fall; every unit thinks once in the 30 ticks after that.
            w.Step(50 + Think);
            foreach (ulong key in Rest)
            {
                Assert.That(p.Level(Id(w, key)), Is.EqualTo(AiNerve.Calm), "key " + key);
                Assert.That(w.Read<MoveIntent>(key).Active, Is.True, "key " + key);
                Assert.That(Horizontal2(w.Read<MoveIntent>(key).Target, Theirs), Is.LessThanOrEqualTo(Metres2(6)), "key " + key);
            }
        }

        // In sight of the enemy every unit advances in its move window (share 1000); a pinned one gets no order at all.
        [Test]
        public void Pinned_InSightDoesNotAdvance()
        {
            string all = "\"engage\": \"bound\", \"boundMoveTicks\": 60, \"boundFireTicks\": 30, \"boundSharePermille\": 1000, \"boundMinRangeMm\": 12000";
            // A fall anywhere presses the whole team here, so key 10 walking off does not let its neighbours go.
            M6World w = Field(Inject(0, all, 100000), attackersX: 80), calm = Field(Inject(0, all, 100000), attackersX: 80);
            Pressure p = w.System<Pressure>();
            int moved = 0, calmMoved = 0;
            for (int t = 0; t < 180; t++)
            {
                // Keep keys 7..13 pinned: a fall beside them every tick.
                p.Advance(new[] { Fall(w, 10) });
                w.Step(); calm.Step();
                foreach (ulong key in Pressed)
                {
                    Assert.That(p.Level(Id(w, key)), Is.EqualTo(AiNerve.Pinned), "key " + key);
                    if (w.Read<MoveIntent>(key).Active) moved++;
                    if (calm.Read<MoveIntent>(key).Active) calmMoved++;
                }
            }
            Assert.That(moved, Is.EqualTo(0));
            Assert.That(calmMoved, Is.GreaterThan(0), "the same units advance when nothing presses them");
            Assert.That(At(w, 8), Is.EqualTo(At(Field(null, attackersX: 80), 8)), "a pinned unit never left its spawn point");
        }

        // In sight of the enemy a broken unit turns round and goes to its own point, and while it is pressed it stays
        // there. It remembers where the enemy was, but a unit falling back neither suppresses nor searches: with every
        // unit on the field broken, both tables stay empty.
        [Test]
        public void Broken_InSightFallsBackAndStays()
        {
            string all = "\"targetMemoryTicks\": 300, \"searchTicks\": 90, \"suppressTicks\": 90, "
                + "\"engage\": \"bound\", \"boundMoveTicks\": 60, \"boundFireTicks\": 30, \"boundSharePermille\": 1000, \"boundMinRangeMm\": 12000";
            M6World w = Field(Inject(1000, all, Wide), attackersX: 80);
            Pressure p = w.System<Pressure>();
            TargetMemory memory = w.System<TargetMemory>(); TargetSearch search = w.System<TargetSearch>(); TargetSuppress suppress = w.System<TargetSuppress>();
            Assert.That(memory, Is.Not.Null); Assert.That(search, Is.Not.Null); Assert.That(suppress, Is.Not.Null);
            // Two fall on each side every tick, so each unit has a fallen teammate that is not itself.
            void Press()
            {
                p.Advance(new[] { Fall(w, 9), Fall(w, 10), Fall(w, Attackers + 1, shooterTeam: 1), Fall(w, Attackers + 2, shooterTeam: 1) });
                w.Step();
                Assert.That((Count(search), Count(suppress)), Is.EqualTo((0, 0)), "tick " + w.Sim.Clock.Tick);
            }
            for (int t = 0; t < 2 * Think; t++) Press();
            Assert.That(Count(memory), Is.GreaterThanOrEqualTo(Attackers), "every attacker saw an enemy and remembers it");
            for (ulong key = 1; key <= Attackers + Defenders; key++) Assert.That(p.Level(Id(w, key)), Is.EqualTo(AiNerve.Broken), "key " + key);
            for (ulong key = 1; key <= Attackers; key++)
            {
                MoveIntent order = w.Read<MoveIntent>(key);
                Assert.That(order.Active, Is.True, "key " + key);
                Assert.That(Horizontal2(order.Target, Own), Is.LessThanOrEqualTo(Metres2(6)), "key " + key);
            }
            // 40 m at 6 m/s is 200 ticks, plus the time twenty units need to sort out their slots.
            for (int t = 0; t < 400; t++) Press();
            var stood = new Dictionary<ulong, SimVec3>();
            for (ulong key = 1; key <= Attackers; key++)
            {
                Assert.That(Horizontal2(At(w, key), Own), Is.LessThanOrEqualTo(Metres2(7)), "key " + key);
                Assert.That(w.Read<MoveIntent>(key).Active, Is.False, "key " + key);
                stood[key] = At(w, key);
            }
            for (int t = 0; t < 2 * Think; t++) Press();
            for (ulong key = 1; key <= Attackers; key++) Assert.That(At(w, key), Is.EqualTo(stood[key]), "key " + key);
        }

        // A defender in its own point is already where a broken unit goes: it takes a slot there and does not leave.
        [Test]
        public void Broken_DefenderStaysInItsPoint()
        {
            M6World w = Field(Inject(1000, BoundAi, 100000), ownPoint: false, defenderSpeed: 6000);
            Pressure p = w.System<Pressure>();
            w.Step(Think);
            var stood = new Dictionary<ulong, SimVec3>();
            for (int t = 0; t < 150; t++)
            {
                p.Advance(new[] { Fall(w, Attackers + 1, shooterTeam: 1), Fall(w, Attackers + 6, shooterTeam: 1) });
                w.Step();
                for (ulong key = Attackers + 1; key <= Attackers + Defenders; key++)
                {
                    Assert.That(p.Level(Id(w, key)), Is.EqualTo(AiNerve.Broken), "key " + key);
                    Assert.That(Horizontal2(At(w, key), Theirs), Is.LessThanOrEqualTo(Metres2(6)), "key " + key + " tick " + t);
                    // The first 60 ticks may go into reaching a slot; after that nobody moves.
                    if (t == 60) stood[key] = At(w, key);
                    if (t > 60) Assert.That(At(w, key), Is.EqualTo(stood[key]), "key " + key + " tick " + t);
                }
            }
        }

        // ---- Battles on the control layout ----

        // "hold" never looks at the nerve: with the pressure system on, every unit is where it is without it, tick by tick.
        [Test]
        public void Battle_HoldWithPressureMovesNobodyElsewhere()
        {
            M6World on = Battle(41, null, Json(Values)), off = new M6World(FairEFixture.Recipe("control", 41, null), FairEFixture.Map("control"));
            Pressure p = on.System<Pressure>();
            Assert.That(p, Is.Not.Null);
            Assert.That(off.System<Pressure>(), Is.Null);
            int pressed = 0;
            for (int tick = 0; tick < 900; tick++)
            {
                on.Step(); off.Step();
                for (ulong key = 1; key <= 300; key++)
                {
                    Assert.That(on.Dead(key), Is.EqualTo(off.Dead(key)), "key " + key + " tick " + tick);
                    if (on.Has<Position>(key)) Assert.That(At(on, key), Is.EqualTo(At(off, key)), "key " + key + " tick " + tick);
                }
                pressed = Math.Max(pressed, Entries(p));
            }
            Assert.That(pressed, Is.GreaterThan(0), "the battle must have pressed somebody");
        }

        [Test]
        public void Battle_PressureChangesTheBoundBattle()
        {
            M6World on = Battle(41, null, S3), off = Battle(41, null, "{ " + BoundAi + " }");
            on.Step(1200); off.Step(1200);
            bool differs = false;
            for (ulong key = 1; key <= 300 && !differs; key++)
                differs = on.Dead(key) != off.Dead(key) || (on.Has<Position>(key) && !At(on, key).Equals(At(off, key)));
            Assert.That(differs, Is.True);
        }

        private const string Combo1 = "{ " + BoundAi + ", " + PressureAi + ", \"holdSharePermille\": 300, \"attackSpreadPoints\": 3 }";
        private const string Combo2 = "{ " + BoundAi + ", " + PressureAi + ", \"slotSpacingMm\": 2000, \"slotRingMm\": 6000, \"placement\": \"scatter\", \"scatterMm\": 6000 }";
        private static int BrokenUnits(M6World w, Pressure p)
        {
            int count = 0;
            for (ulong key = 1; key <= 300; key++) if (w.Has<Position>(key) && !w.Dead(key) && p.Level(Id(w, key)) == AiNerve.Broken) count++;
            return count;
        }

        // Same seed twice, tick by tick; a snapshot taken while units are broken restores to the same battle.
        [TestCase(null, S3)]
        [TestCase(false, S3)]
        [TestCase(true, S3)]
        [TestCase(null, Combo1)]
        [TestCase(true, Combo1)]
        [TestCase(null, Combo2)]
        [TestCase(true, Combo2)]
        public void Battle_RepeatAndRestoreWhileBroken(bool? fair, string ai)
        {
            M6World first = Battle(41, fair, ai), repeat = Battle(41, fair, ai);
            Pressure p = first.System<Pressure>();
            Assert.That(repeat.Hash, Is.EqualTo(first.Hash));
            int stepped = 0;
            while (BrokenUnits(first, p) < 2)
            {
                Assert.That(stepped, Is.LessThan(3000), "nobody broke");
                first.Step(); repeat.Step(); stepped++;
                Assert.That(repeat.Hash, Is.EqualTo(first.Hash), "tick " + first.Sim.Clock.Tick);
            }
            byte[] snapshot = first.Sim.SaveRetainedSnapshot();
            M6World restored = Battle(41, fair, ai);
            restored.Sim.RestoreRetainedSnapshot(snapshot);
            Assert.That(restored.Hash, Is.EqualTo(first.Hash));
            Assert.That(BrokenUnits(restored, restored.System<Pressure>()), Is.EqualTo(BrokenUnits(first, p)));
            for (int tick = 0; tick < 600 && !first.Sim.IsFinished; tick++)
            {
                first.Step(); restored.Step();
                Assert.That(restored.Hash, Is.EqualTo(first.Hash), "tick " + first.Sim.Clock.Tick);
            }
        }

        // ---- Saved state ----

        // Each of the ten numbers is part of the saved state: a battle with one changed refuses the other's snapshot.
        [Test]
        public void State_EveryRuleIsSavedAndChecked()
        {
            int[][] variants =
            {
                Values, With(0, 2999), With(1, 1999), With(2, 2999), With(3, 999), With(4, 13), With(5, 7001), With(6, 999), With(7, 2001), With(8, 499), With(9, 299),
            };
            var worlds = new M6World[variants.Length]; var saved = new byte[variants.Length][]; var own = new byte[variants.Length][];
            for (int i = 0; i < variants.Length; i++)
            {
                worlds[i] = Battle(41, null, Json(variants[i], BoundAi));
                worlds[i].Step();
                saved[i] = worlds[i].Sim.SaveRetainedSnapshot();
                var writer = new SimStateWriter(); worlds[i].System<Pressure>().SaveState(writer); own[i] = writer.ToArray();
            }
            // The system's own hash holds each rule (the battle hash below also sees the content pack).
            var ownHashes = new HashSet<ulong>();
            foreach (int[] v in variants)
            {
                var hash = new StateHash();
                new Pressure(worlds[0].Combat, new PressureRules(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9])).WriteState(hash);
                ownHashes.Add(hash.Value);
            }
            Assert.That(ownHashes.Count, Is.EqualTo(variants.Length), "each rule is part of the system's own hash");
            var hashes = new HashSet<ulong>();
            foreach (M6World world in worlds) hashes.Add(world.Hash);
            Assert.That(hashes.Count, Is.EqualTo(variants.Length), "each rule is part of the state hash");
            for (int i = 0; i < variants.Length; i++)
                for (int j = 0; j < variants.Length; j++)
                {
                    if (i == j) { Assert.DoesNotThrow(() => worlds[j].System<Pressure>().LoadState(new SimStateReader(own[i]))); continue; }
                    var e = Assert.Catch(() => worlds[j].System<Pressure>().LoadState(new SimStateReader(own[i])));
                    Assert.That(e.Message, Does.Contain("AI pressure rules differ."), i + " into " + j);
                    Assert.Catch(() => worlds[j].Sim.RestoreRetainedSnapshot(saved[i]));
                }
            // A battle without the system and one with it refuse each other.
            M6World plain = Battle(41, null, "{ " + BoundAi + " }");
            plain.Step();
            Assert.Catch(() => plain.Sim.RestoreRetainedSnapshot(saved[0]));
            Assert.Catch(() => worlds[0].Sim.RestoreRetainedSnapshot(plain.Sim.SaveRetainedSnapshot()));
        }

        private static byte[] Saved(int[] rules, params (ulong key, int value, bool broken)[] entries)
        {
            var writer = new SimStateWriter();
            foreach (int rule in rules) writer.Int32(rule);
            writer.Int32(entries.Length);
            foreach (var (key, value, broken) in entries) { writer.UInt64(key); writer.Int32(value); writer.Bool(broken); }
            return writer.ToArray();
        }

        [Test]
        public void State_LoadRejectsWhatStepCannotLeave()
        {
            M6World w = Field(null);
            int[] rules = { 3000, 2000, 3000, 1000, 12, 7000, 1000, 2000, 0, 300 };
            var p = new Pressure(w.Combat, new PressureRules(3000, 2000, 3000, 1000, 12, 7000, 1000, 2000, 0, 300));
            // A key that can break and one that cannot.
            ulong breaks = 1, steady = 1;
            while ((int)(Finalize(breaks ^ BreakSalt) % 1000UL) >= 300) breaks++;
            while ((int)(Finalize(steady ^ BreakSalt) % 1000UL) < 300) steady++;
            ulong low = Math.Min(breaks, steady), high = Math.Max(breaks, steady);
            void Bad(string message, params (ulong key, int value, bool broken)[] entries)
            {
                var e = Assert.Catch(() => p.LoadState(new SimStateReader(Saved(rules, entries))));
                Assert.That(e.Message, Does.Contain(message));
            }
            void Good(params (ulong key, int value, bool broken)[] entries) =>
                Assert.DoesNotThrow(() => p.LoadState(new SimStateReader(Saved(rules, entries))));
            Good();
            Good((low, 1, false), (high, Pressure.Max, high == breaks));
            Assert.That(p.Value(new StableId(low)), Is.EqualTo(1));
            Assert.That(p.Level(new StableId(high)), Is.EqualTo(high == breaks ? AiNerve.Broken : AiNerve.Pinned));
            Bad("not in strictly increasing order", (high, 5, false), (low, 5, false));
            Bad("not in strictly increasing order", (low, 5, false), (low, 5, false));
            Bad("out of range", (low, 0, false));
            Bad("out of range", (low, Pressure.Max + 1, false));
            // Broken above the recover line only; 1001 is the lowest.
            Good((breaks, 1001, true));
            Bad("broken at or below the recover line", (breaks, 1000, true));
            // At the broken line a unit that can break is broken; one that cannot never is.
            Good((breaks, 6999, false)); Good((breaks, 7000, true)); Good((steady, 7000, false));
            Bad("broken flag disagree", (breaks, 7000, false));
            Bad("broken flag disagree", (steady, 7000, true));
            Bad("broken flag disagree", (steady, 5000, true));
            // A save and a load give the same hash.
            Good((low, 1500, false), (high, 9000, high == breaks));
            var hash = new StateHash(); p.WriteState(hash);
            var writer = new SimStateWriter(); p.SaveState(writer);
            var q = new Pressure(w.Combat, new PressureRules(3000, 2000, 3000, 1000, 12, 7000, 1000, 2000, 0, 300));
            q.LoadState(new SimStateReader(writer.ToArray()));
            var again = new StateHash(); q.WriteState(again);
            Assert.That(again.Value, Is.EqualTo(hash.Value));
            // The broken flag is part of the hash: the same number, broken and not.
            Good((breaks, 5000, true));
            var flagged = new StateHash(); p.WriteState(flagged);
            Good((breaks, 5000, false));
            var plain = new StateHash(); p.WriteState(plain);
            Assert.That(flagged.Value, Is.Not.EqualTo(plain.Value));
            var empty = new StateHash(); new Pressure(w.Combat, new PressureRules(3000, 2000, 3000, 1000, 12, 7000, 1000, 2000, 0, 300)).WriteState(empty);
            Assert.That(empty.Value, Is.Not.EqualTo(hash.Value));
        }

        // ---- Content ----

        [Test]
        public void Content_FieldsAreReadAllOrNone()
        {
            Assert.That(Pack("{}").Section<AiRules>().Pressure, Is.Null);
            Assert.That(Pack(null).Section<AiRules>().Pressure, Is.Null);
            PressureRules? read = Pack(Json(Values)).Section<AiRules>().Pressure;
            Assert.That(read, Is.Not.Null);
            PressureRules r = read.Value;
            Assert.That(new[] { r.DeathRadiusMm, r.DeathShock, r.NearRadiusMm, r.NearShock, r.DecayPermille, r.BrokenAt, r.RecoverAt, r.PinnedAt, r.SpreadPermille, r.BreakSharePermille },
                Is.EqualTo(Values));
            // The system is in the battle only when the fields are written; with hold or with bound.
            Assert.That(Field(null).System<Pressure>(), Is.Null);
            Assert.That(Field("{ " + BoundAi + " }").System<Pressure>(), Is.Null);
            Assert.That(Field(Json(Values)).System<Pressure>(), Is.Not.Null);
            Assert.That(Field(S3).System<Pressure>(), Is.Not.Null);
            // Range edges are accepted.
            Assert.That(Pack(Json(new[] { 0, 0, 0, 0, 1, 1, 0, 1, 0, 0 })).Section<AiRules>().Pressure, Is.Not.Null);
            Assert.That(Pack(Json(new[] { 512000, 10000, 512000, 10000, 1000, 10000, 9999, 10000, 1000, 1000 })).Section<AiRules>().Pressure, Is.Not.Null);
        }

        [Test]
        public void Content_AnyFieldWrittenNeedsAllTen()
        {
            for (int skip = 0; skip < Fields.Length; skip++)
            {
                var e = Assert.Throws<ContentException>(() => Pack(Json(Values, null, skip)));
                Assert.That(e.Message, Is.EqualTo("content pack 's3-test' layer 's3-test' ai is missing field '" + Fields[skip] + "'"));
            }
            // One field alone: the first of the others is reported missing.
            for (int only = 0; only < Fields.Length; only++)
            {
                var e = Assert.Throws<ContentException>(() => Pack("{ \"" + Fields[only] + "\": " + Values[only] + " }"));
                Assert.That(e.Message, Is.EqualTo("content pack 's3-test' layer 's3-test' ai is missing field '" + Fields[only == 0 ? 1 : 0] + "'"));
            }
        }

        [TestCase(0, -1)] [TestCase(0, 512001)]
        [TestCase(1, -1)] [TestCase(1, 10001)]
        [TestCase(2, -1)] [TestCase(2, 512001)]
        [TestCase(3, -1)] [TestCase(3, 10001)]
        [TestCase(4, 0)] [TestCase(4, 1001)]
        [TestCase(5, 0)] [TestCase(5, 10001)]
        [TestCase(6, -1)] [TestCase(6, 10001)]
        [TestCase(7, 0)] [TestCase(7, 10001)]
        [TestCase(8, -1)] [TestCase(8, 1001)]
        [TestCase(9, -1)] [TestCase(9, 1001)]
        public void Content_OutOfRangeNamesTheField(int index, int value)
        {
            var e = Assert.Throws<ContentException>(() => Pack(Json(With(index, value))));
            Assert.That(e.Message, Does.StartWith("content pack 's3-test' layer 's3-test' ai field '" + Fields[index] + "': "));
        }

        [TestCase(7000)]
        [TestCase(7001)]
        public void Content_RecoverMustBeBelowBroken(int recoverAt)
        {
            var e = Assert.Throws<ContentException>(() => Pack(Json(With(6, recoverAt))));
            Assert.That(e.Message, Is.EqualTo("content pack 's3-test' layer 's3-test' ai field 'pressureRecoverAt': must be below pressureBrokenAt (7000)"));
            Assert.That(Pack(Json(With(6, 6999))).Section<AiRules>().Pressure.Value.RecoverAt, Is.EqualTo(6999));
        }

        // The view tells who is pinned and who is broken; without the system everybody reads 0.
        [Test]
        public void View_CarriesTheNerve()
        {
            M6World w = Field(Inject(300));
            Pressure p = w.System<Pressure>();
            p.Advance(new[] { Fall(w, 10) });
            w.Session.StepOnce();
            int pinned = 0, broken = 0;
            for (ulong key = 1; key <= Attackers + Defenders; key++)
            {
                Assert.That(w.Session.Current.TryGet(key, out ViewEntity seen), Is.True, "key " + key);
                Assert.That(seen.Nerve, Is.EqualTo((int)p.Level(Id(w, key))), "key " + key);
                if (seen.Nerve == 1) pinned++; else if (seen.Nerve == 2) broken++;
            }
            Assert.That(pinned + broken, Is.EqualTo(Pressed.Length));
            M6World plain = Field(null);
            plain.Session.StepOnce();
            for (ulong key = 1; key <= Attackers + Defenders; key++)
            {
                Assert.That(plain.Session.Current.TryGet(key, out ViewEntity seen), Is.True);
                Assert.That(seen.Nerve, Is.EqualTo(0));
            }
        }

#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
        // Shots land near the attackers every tick: entries come and go, and a step must not allocate.
        [Test]
        public void Battle_StepAllocatesZero()
        {
            string ai = Json(new[] { 3000, 2000, 3000, 1000, 12, 7000, 1000, 2000, 500, 300 }, BoundAi);
            M6World warm = Field(ai, attackersX: 80);
            warm.Step(300);
            M6World w = Field(ai, attackersX: 80);
            w.Step(300);
            Assert.That(Entries(w.System<Pressure>()), Is.GreaterThan(0), "somebody must be pressed");
            AllocationMeter.Stabilize();
            long total = 0;
            for (int i = 0; i < 150; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                w.Sim.Step(default);
                total += GC.GetAllocatedBytesForCurrentThread() - before;
            }
            Assert.That(total, Is.EqualTo(0L));
        }
#endif
    }
}
