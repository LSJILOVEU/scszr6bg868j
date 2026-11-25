using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Nerve1.Nerve1Fixture;

namespace WarCore.Tests.Nerve1
{
    // NERVE-1 in assembled worlds: the pressure system as the nerve source (NerveFeature), the refusal without it, the script
    // source, timing, snapshots and the "nerve-fire" replay.
    public sealed class Nerve1WorldTests
    {
        private static readonly string[] Fields =
        {
            "pressureDeathRadiusMm", "pressureDeathShock", "pressureNearRadiusMm", "pressureNearShock", "pressureDecayPermille",
            "pressureBrokenAt", "pressureRecoverAt", "pressurePinnedAt", "pressureSpreadPermille", "pressureBreakSharePermille",
        };

        // AI rules with the pressure fields only (in Fields order); no engage part, so nobody is ordered anywhere.
        private static ContentPack Ai(params int[] values)
        {
            var parts = new List<string>();
            for (int i = 0; i < Fields.Length; i++) parts.Add("\"" + Fields[i] + "\": " + values[i]);
            return Load("n1-ai", Layer("n1-ai", "rwr", "\"ai\": { " + string.Join(", ", parts) + " }"));
        }

        private static readonly int[] Plain = { 3000, 2000, 3000, 1000, 12, 7000, 1000, 2000, 0, 300 };

        // The M6 guns with the given status (and clamp) and resolver.
        private static CombatPack Guns(CombatStatus status, IHitResolver resolver = null, int ceil = 0)
        {
            CombatPack g = M6World.Guns(resolver ?? new ZeroResolver());
            return new CombatPack(g.Weapons.ToArray(), g.Resolver, g.Selector, g.MaxHealthRaw, g.FriendlyFire, g.FriendlyFireLocked,
                g.AllyCheckMissRaw, g.AllyHoldMilli, status: status, spreadDragCeilPermille: ceil);
        }

        // AI-S3's field: 20 armed attackers of team 1 abreast at x = 60 m (z = 10.5 .. 29.5, keys 1..20), 10 defenders of team 2
        // round (110, 20) out of sight (keys 21..30). Nobody moves or fires on its own.
        private static M6World Field(CombatStatus status, ContentPack ai)
        {
            var spawns = new List<UnitSpawn>();
            for (int i = 0; i < 20; i++) spawns.Add(new UnitSpawn((ulong)i + 1, 0, 1, 60000, 10500 + 1000 * i, 6000, 1, 0, false));
            for (int i = 0; i < 10; i++) spawns.Add(M6World.U((ulong)(21 + i), 2, i < 5 ? 108 : 112, 16 + i % 5 * 2, weapon: 1));
            var points = new[] { new ControlPointDef(1, SimVec3.Metres(110, 0, 20), SimFix.FromInt(6), 2) };
            var bases = new[] { new SpawnAnchor(1, SimVec3.Metres(6, 0, 20)), new SpawnAnchor(2, SimVec3.Metres(150, 0, 20)) };
            WorldRecipe recipe = M6World.RecipeOf(spawns.ToArray(), M6World.Pack(points, ticks: 1000000, think: 30, bases: bases), Guns(status))
                .WithRuleSet(MatchMode.TestNeverEndsId);
            if (ai != null) recipe = recipe.WithPack(ai);
            return new M6World(recipe, WcMapWriter.Write(Wp23.Make(160, 60, 0, 4 * S, Array.Empty<(int, int, int, int)>(), 2 * S, null)));
        }

        private static StableId Id(M6World w, ulong key) => w.Sim.Entities.StableOf(w.Unit(key));
        private static int Angle(M6World w, ulong key, int weapon = Angle2000) =>
            w.Combat.ShotAngle(w.Unit(key), w.Combat.Unit(w.Unit(key)), weapon, w.Sim.Clock.Tick);
        private static FireRecord NearShot(M6World w, ulong near)
        {
            SimVec3 end = w.Read<Position>(near).Value + new SimVec3(SimFix.FromInt(1), default, default);
            return new FireRecord(Id(w, 21), default, end, end, default, 0, 2, FireOutcome.Open);
        }

        [Test]
        public void N3_TheLevelLineIsThePressureSystemsOwn()
        {
            M6World below = Field(Nerve(), Ai(3000, 2000, 3000, 1999, 12, 7000, 1000, 2000, 0, 300));
            M6World at = Field(Nerve(), Ai(3000, 2000, 3000, 2000, 12, 7000, 1000, 2000, 0, 300));
            Assert.That(below.Combat.HasNerveSource, Is.True, "NerveFeature bound the pressure system");
            PressureOf(below).Advance(new[] { NearShot(below, 10) });
            PressureOf(at).Advance(new[] { NearShot(at, 10) });
            Assert.That(PressureOf(below).Value(Id(below, 10)), Is.EqualTo(1999));
            Assert.That(PressureOf(at).Value(Id(at, 10)), Is.EqualTo(2000));
            Assert.That(Angle(below, 10), Is.EqualTo(2000), "PinnedAt - 1: calm");
            Assert.That(Angle(at, 10), Is.EqualTo(2800), "PinnedAt: pinned");
            Assert.That(Angle(at, 1), Is.EqualTo(2000), "a unit with no entry is calm");
        }

        [Test]
        public void N15_ABrokenUnitOfThePressureSystemShootsWithTheBrokenRow()
        {
            M6World w = Field(Nerve(), Ai(3000, Pressure.Max, 3000, 0, 12, 7000, 1000, 2000, 0, 1000));
            Pressure p = PressureOf(w);
            p.Advance(new[] { new FireRecord(Id(w, 21), Id(w, 10), w.Read<Position>(10).Value, w.Read<Position>(10).Value, default, 1, 2) });
            Assert.That(p.Level(Id(w, 11)), Is.EqualTo(AiNerve.Broken));
            Assert.That(Angle(w, 11), Is.EqualTo(3600));
            Assert.That(w.Combat.NerveLevelOf(w.Unit(11)), Is.EqualTo(CombatSystem.NerveBroken));
            Assert.That(Angle(w, 1), Is.EqualTo(2000), "19 m away: calm");
        }

        // Two armed units 20 m apart (key 1 of team 1 at x 20 m, key 2 of team 2 at x 40 m) that shoot each other.
        private static M6World Duel(CombatStatus status, ContentPack ai, IHitResolver resolver = null, int ceil = 0)
        {
            var units = new[] { M6World.U(1, 1, 20, 20, weapon: 0), M6World.U(2, 2, 40, 20, weapon: 0) };
            WorldRecipe recipe = M6World.RecipeOf(units, M6World.Pack(), Guns(status, resolver, ceil)).WithRuleSet(MatchMode.TestNeverEndsId);
            if (ai != null) recipe = recipe.WithPack(ai);
            return new M6World(recipe);
        }

        [Test]
        public void N4_AShotOfTickTReadsTheLevelAtTheEndOfTickTMinusOne()
        {
            M6World w = Duel(Nerve(), Ai(Plain));
            Pressure p = PressureOf(w);
            for (int i = 0; i < 300; i++)
            {
                AiNerve before = p.Level(Id(w, 1));
                int angle = Angle(w, 1);
                w.Step();
                if (p.Level(Id(w, 1)) == AiNerve.Calm) continue;
                long t = w.Sim.Clock.Tick;
                Assert.That(before, Is.EqualTo(AiNerve.Calm), "the pressure system crossed the line in tick " + t);
                Assert.That(p.Level(Id(w, 1)), Is.EqualTo(AiNerve.Pinned));
                // ShotAngle is a pure read: between steps it answers what the next tick's shot uses.
                Assert.That(angle, Is.EqualTo(2000), "read after tick " + (t - 1) + ": the calm number");
                Assert.That(Angle(w, 1), Is.EqualTo(2800), "read after tick " + t + ": the pinned number");
                Assert.That(t, Is.GreaterThan(1));
                return;
            }
            Assert.Fail("nobody was pinned");
        }

        private static SimSession Plainfield(ContentPack pack, IWorldOption option = null)
        {
            var units = new[] { Aim1.Aim1Worlds.Unit(1, 1, 1, 5000, 10000), Aim1.Aim1Worlds.Unit(2, 2, 2, 35000, 10000, weapon: -1) };
            WorldRecipe recipe = new WorldRecipe(41, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>()).WithCombat(pack.BuildCombat());
            if (option != null) recipe = recipe.With(option);
            return WorldBuilder.Build(recipe, Aim1.Aim1Worlds.Map(), false);
        }

        private static ContentPack StatusLayer(string rows) =>
            Load("n1-s", Layer("n1-s", "rwr-field", Harmless + ", \"combat\": { \"resolver\": \"damage\", \"status\": { " + rows + " } }"));

        // M6World builds through reflection, which wraps what the builder throws.
        private static void RefusedBuild(TestDelegate build, string what)
        {
            Exception e = null;
            try { build(); } catch (Exception thrown) { e = thrown; }
            while (e is System.Reflection.TargetInvocationException && e.InnerException != null) e = e.InnerException;
            Assert.That(e, Is.InstanceOf<InvalidOperationException>(), what);
            Assert.That(e.Message, Does.Contain("combat.status"), what);
            Assert.That(e.Message, Does.Contain("pressure"), what);
        }

        [Test]
        public void N8_PressureRowsNeedThePressureSystem()
        {
            const string Pin = "\"pinned\": { \"spreadPermille\": 1400 }", Brk = "\"broken\": { \"spreadPermille\": 1800 }";
            const string Wnd = "\"wounded\": { \"belowPermille\": 500, \"spreadPermille\": 1250 }";
            RefusedBuild(() => Plainfield(StatusLayer(Pin)), "only pinned, no match");
            RefusedBuild(() => Plainfield(StatusLayer(Brk)), "only broken, no match");
            RefusedBuild(() => Field(Pinned(), null), "only pinned, a match without pressure fields");
            RefusedBuild(() => Field(Broken(), null), "only broken, a match without pressure fields");

            SimSession wounded = null;
            Assert.DoesNotThrow(() => wounded = Plainfield(StatusLayer(Wnd)), "only wounded: no pressure needed");
            SimSession plain = Plainfield(Load("n1-p", Layer("n1-p", "rwr-field", Harmless + ", \"combat\": { \"resolver\": \"damage\" }")));
            Assert.That(Systems(Aim1.Aim1Worlds.SimOf(wounded)), Is.EqualTo(Systems(Aim1.Aim1Worlds.SimOf(plain))));
            Assert.That(Components(Aim1.Aim1Worlds.SimOf(wounded)), Is.EqualTo(Components(Aim1.Aim1Worlds.SimOf(plain))));

            SimSession scripted = null;
            Assert.DoesNotThrow(() => scripted = Plainfield(StatusLayer(Pin + ", " + Brk), NerveDemo.Script((1, 0, CombatSystem.NerveBroken))),
                "a script bound in Connect: NerveFeature skips the pressure system");
            CombatSystem c = Aim1.Aim1Worlds.CombatOf(scripted);
            Assert.That(c.ShotAngle(Aim1.Aim1Worlds.Id(scripted, 1), Aim1.Aim1Worlds.State(scripted, 1), 2000, scripted.Tick), Is.EqualTo(3600));
            Assert.That(c.NerveLevelOf(Aim1.Aim1Worlds.Id(scripted, 2)), Is.EqualTo(CombatSystem.NerveCalm));
        }

        // A hit takes a twelfth of the maximum: a unit is below half after seven hits and dies at twelve.
        private sealed class Twelfth : IHitResolver
        {
            public ulong PolicyKey => 0x4E45525645310001;
            public long DamageRaw(in WeaponDef weapon, long falloffRaw, int rollRaw, long maxHealthRaw) => maxHealthRaw / 12;
        }

        [Test]
        public void N9_ASnapshotOfAPinnedWoundedUnitRestoresTheSameShots()
        {
            M6World first = Duel(All(), Ai(Plain), new Twelfth(), 3000);
            Pressure p = PressureOf(first);
            ulong key = 0;
            for (int i = 0; i < 600 && key == 0; i++)
            {
                first.Step();
                for (ulong k = 1; k <= 2; k++)
                    if (!first.Dead(k) && p.Level(Id(first, k)) != AiNerve.Calm && first.Combat.IsWounded(first.Unit(k))) { key = k; break; }
            }
            Assert.That(key, Is.Not.Zero, "nobody was pressed and wounded at once");
            int angle = Angle(first, key);
            Assert.That(angle, Is.GreaterThan(2000 * 1250 / 1000), "pressed and wounded");
            byte[] snapshot = first.Sim.SaveRetainedSnapshot();

            M6World restored = Duel(All(), Ai(Plain), new Twelfth(), 3000);
            restored.Sim.RestoreRetainedSnapshot(snapshot);
            Assert.That(restored.Hash, Is.EqualTo(first.Hash));
            Assert.That(Angle(restored, key), Is.EqualTo(angle));
            Assert.That(restored.Combat.HasNerveSource, Is.True);
            for (int i = 0; i < 60; i++)
            {
                first.Step(); restored.Step();
                Assert.That(restored.Hash, Is.EqualTo(first.Hash), "tick " + first.Sim.Clock.Tick);
            }

            M6World without = Duel(null, Ai(Plain), new Twelfth());
            Assert.That(Components(first.Sim), Is.EqualTo(Components(without.Sim)), "no new component");
            Assert.That(Systems(first.Sim), Is.EqualTo(Systems(without.Sim)), "no new system");

            M6World otherStatus = Duel(All(1401), Ai(Plain), new Twelfth(), 3000), otherClamp = Duel(All(), Ai(Plain), new Twelfth(), 2999);
            Assert.That(() => otherStatus.Sim.RestoreRetainedSnapshot(snapshot), Throws.InstanceOf<InvalidOperationException>());
            Assert.That(() => otherClamp.Sim.RestoreRetainedSnapshot(snapshot), Throws.InstanceOf<InvalidOperationException>());
        }

        // The tangent of the largest horizontal angle between a shooter's line to its target and its shots, in millionths.
        private static long MaxDeviation(List<FireRecord> shots, SimVec3 aimEnd)
        {
            long most = 0;
            foreach (FireRecord f in shots)
            {
                long ax = aimEnd.X.Raw - f.Origin.X.Raw, az = aimEnd.Z.Raw - f.Origin.Z.Raw;
                long bx = f.End.X.Raw - f.Origin.X.Raw, bz = f.End.Z.Raw - f.Origin.Z.Raw;
                long cross = Math.Abs(ax * bz - az * bx), dot = ax * bx + az * bz;
                Assert.That(dot, Is.GreaterThan(0));
                most = Math.Max(most, cross * 1000000 / dot);
            }
            return most;
        }

        internal const int ReplayShots = 200;
        // Recorded the first time the layer ran (NERVE-1): the largest deviation, tangent in millionths, of the first 200 shots
        // after PressTick of the calm, the pinned and the broken shooter.
        internal const long RecordedCalm = 49131, RecordedPinned = 69042, RecordedBroken = 89977;

        [Test]
        public void N10_TheReplayPressedShootersScatterWider()
        {
            SimSession s = WorldBuilder.Build(NerveDemo.Recipe(), NerveDemo.MapBytes(), false);
            CombatSystem c = Aim1.Aim1Worlds.CombatOf(s);
            var shots = new[] { new List<FireRecord>(), new List<FireRecord>(), new List<FireRecord>() };
            StableId[] ids = { Aim1.Aim1Worlds.SimOf(s).Entities.StableOf(Aim1.Aim1Worlds.Id(s, NerveDemo.Calm)),
                Aim1.Aim1Worlds.SimOf(s).Entities.StableOf(Aim1.Aim1Worlds.Id(s, NerveDemo.Pinned)),
                Aim1.Aim1Worlds.SimOf(s).Entities.StableOf(Aim1.Aim1Worlds.Id(s, NerveDemo.Broken)) };
            for (int t = 0; t < 5000 && (shots[0].Count < ReplayShots || shots[1].Count < ReplayShots || shots[2].Count < ReplayShots); t++)
            {
                s.StepOnce();
                if (s.Tick < NerveDemo.PressTick) continue;
                foreach (FireRecord f in c.Fires)
                    for (int k = 0; k < 3; k++)
                        if (f.Shooter.Equals(ids[k]) && shots[k].Count < ReplayShots) shots[k].Add(f);
            }
            long[] most = new long[3];
            for (int k = 0; k < 3; k++)
            {
                Assert.That(shots[k].Count, Is.EqualTo(ReplayShots), "shooter " + (k + 1));
                most[k] = MaxDeviation(shots[k], c.ShotEnd(Aim1.Aim1Worlds.Id(s, (ulong)k + 4)));
            }
            Assert.That(most[1], Is.GreaterThan(most[0]), "pinned wider than calm");
            Assert.That(most[2], Is.GreaterThan(most[0]), "broken wider than calm");
            Assert.That(most[0], Is.EqualTo(RecordedCalm), most[0] + ", " + most[1] + ", " + most[2]);
            Assert.That(most[1], Is.EqualTo(RecordedPinned));
            Assert.That(most[2], Is.EqualTo(RecordedBroken));
        }
    }
}
