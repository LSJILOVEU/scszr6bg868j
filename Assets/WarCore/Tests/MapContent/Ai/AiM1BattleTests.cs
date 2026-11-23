using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Sim;
using static WarCore.Tests.Ml11aWorld;

namespace WarCore.Tests
{
    // AI-M1 acceptance 10 to 12: whole games of the ancient cartridge, 60 against 60 (AiM1Field). The layer "ancient" is the
    // game as it was; "ancient-nerve" has the pressure, the engage part "bound", both AI-M1 fields and a fight range of 4 m
    // on. The numbers are this package's baseline (PLACEHOLDER values in ancient-nerve.json).
    public sealed class AiM1BattleTests
    {
        // Seed 41: winner, end tick, dead of team 1 and 2, the tick the first strike landed, landed strikes, and the median
        // number of ticks from that first strike to a unit's death.
        private static readonly (int, int, int, int, int, int, int) PlainFight = (1, 417, 55, 60, 183, 415, 100), NerveFight = (1, 1163, 58, 60, 183, 429, 107),
            StandFight = (1, 1115, 54, 60, 183, 411, 291);
        // Units that ever broke, those of them that came back and swung again, and the largest share (in thousandths) of the
        // living that was broken at one tick on the field, broken within one team, and pinned on the field.
        private static readonly (int, int, int, int, int) NerveNerves = (18, 13, 379, 428, 698), StandNerves = (14, 13, 185, 189, 838);
        // Idle units (standing, no swing for 30 ticks) from the first landed strike on: their share of all unit-ticks, the
        // largest share at one tick (thousandths), and the ticks at which more than half of the living were idle.
        private static readonly (int, int, int) PlainIdle = (32, 160, 0), NerveIdle = (113, 557, 8), StandIdle = (527, 826, 564);
        private const ulong PlainHash = 0xFF69C738929461F1, NerveHash = 0x844BB8CA2A81C48D, StandHash = 0xB2AB2FA9448C85A7;

        // 11. The layer loads, with the fields this package reads, and the world has the parts they turn on.
        [Test]
        public void TheNerveLayerBuilds()
        {
            AiRules ai = AiM1Field.Load(AiM1Field.Nerve).Section<AiRules>();
            Assert.That((ai.MeleeChaseRange, ai.MeleeNerve, ai.Engage), Is.EqualTo((40, true, ContentPolicies.BoundEngage)));
            Assert.That(ai.MeleeNerveFightMm, Is.EqualTo(4000));
            PressureRules rules = ai.Pressure.Value;
            Assert.That((rules.Strikes, rules.FallOnKill), Is.EqualTo((true, true)));
            Assert.That((rules.DeathRadiusMm, rules.DeathShock, rules.NearRadiusMm, rules.NearShock, rules.DecayPermille, rules.BrokenAt, rules.RecoverAt,
                rules.PinnedAt, rules.SpreadPermille, rules.BreakSharePermille), Is.EqualTo((3000, 3000, 3000, 60, 12, 7000, 1000, 2000, 500, 300)));
            AiRules plain = AiM1Field.Load(AiM1Field.Plain).Section<AiRules>();
            Assert.That((plain.MeleeChaseRange, plain.MeleeNerve, plain.Pressure.HasValue, plain.Engage), Is.EqualTo((40, false, false, ContentPolicies.HoldEngage)));

            M6World w = AiM1Field.World(AiM1Field.Nerve);
            Assert.That(w.System<Pressure>().StateVersion, Is.EqualTo(7), "no sources, a kill is a fall, strikes");
            Assert.That(Part<bool>(w, "meleeNerve"), Is.True);
            Assert.That(Part<IAiEngage>(w, "engage"), Is.InstanceOf<BoundEngage>());
            M6World old = AiM1Field.World(AiM1Field.Plain);
            Assert.That(old.System<Pressure>(), Is.Null);
            Assert.That(Part<bool>(old, "meleeNerve"), Is.False);
        }

        // 12. The game as it was: the lines meet at tick 183 and the fight is over 234 ticks later; nobody is ever pressed.
        [Test]
        public void ThePlainBattleIsTheBaseline()
        {
            AiM1Field.Report r = AiM1Field.Run(AiM1Field.Plain);
            Assert.That(r.Finished, Is.True);
            Assert.That(r.Fight, Is.EqualTo(PlainFight));
            Assert.That(r.Nerves, Is.EqualTo((0, 0, 0, 0, 0)));
            Assert.That(r.Idle, Is.EqualTo(PlainIdle));
            Assert.That(r.Hash, Is.EqualTo(PlainHash));
        }

        // 12. The same battle with nerves: it ends with the same winner inside the time limit, 18 units break and 13 of them
        // come back and swing again; at most 379 of 1000 living units are broken at one tick (428 within one team). 113 of
        // 1000 unit-ticks are idle, and more than half of the living are idle at 8 ticks.
        [Test]
        public void TheNerveBattleIsTheBaseline()
        {
            AiM1Field.Report r = AiM1Field.Run(AiM1Field.Nerve);
            Assert.That(r.Finished, Is.True);
            Assert.That(r.Fight, Is.EqualTo(NerveFight));
            Assert.That(r.Nerves, Is.EqualTo(NerveNerves));
            Assert.That(r.Idle, Is.EqualTo(NerveIdle));
            Assert.That(r.Hash, Is.EqualTo(NerveHash));
            Assert.That(r.Broke, Is.GreaterThanOrEqualTo(1), "12 (a)");
            Assert.That(r.Returned, Is.GreaterThanOrEqualTo(1), "12 (a)");
            Assert.That(r.PeakBrokenPermille, Is.LessThanOrEqualTo(500), "12 (b)");
            Assert.That(r.PeakTeamBrokenPermille, Is.LessThanOrEqualTo(500), "12 (b), within one team");
        }

        // 12. The nerve layer with a fight range of 0: a pinned unit stands whatever the distance. The fight lasts as long
        // but 527 of 1000 unit-ticks are idle and more than half of the living are idle at 564 ticks.
        [Test]
        public void WithoutAFightRangePinnedUnitsStand()
        {
            AiM1Field.Report r = AiM1Field.Run(AiM1Field.Nerve, fight: 0);
            Assert.That(r.Finished, Is.True);
            Assert.That(r.Fight, Is.EqualTo(StandFight));
            Assert.That(r.Nerves, Is.EqualTo(StandNerves));
            Assert.That(r.Idle, Is.EqualTo(StandIdle));
            Assert.That(r.Hash, Is.EqualTo(StandHash));
        }

        // 12. Nothing in these battles draws on the seed: who can break and how brave a unit is come from its id. Another
        // seed gives the battle of seed 41 with another hash.
        [TestCase(AiM1Field.Plain, 42UL)]
        [TestCase(AiM1Field.Plain, 43UL)]
        [TestCase(AiM1Field.Plain, 44UL)]
        [TestCase(AiM1Field.Plain, 45UL)]
        [TestCase(AiM1Field.Nerve, 43UL)]
        [TestCase(AiM1Field.Nerve, 45UL)]
        public void TheSeedDoesNotChangeTheseBattles(string layer, ulong seed)
        {
            bool nerve = layer == AiM1Field.Nerve;
            AiM1Field.Report r = AiM1Field.Run(layer, seed);
            Assert.That(r.Fight, Is.EqualTo(nerve ? NerveFight : PlainFight));
            Assert.That(r.Nerves, Is.EqualTo(nerve ? NerveNerves : (0, 0, 0, 0, 0)));
            Assert.That(r.Idle, Is.EqualTo(nerve ? NerveIdle : PlainIdle));
            Assert.That(r.Hash, Is.Not.EqualTo(nerve ? NerveHash : PlainHash));
        }

        // 10. The same seed gives the same hash at every tick, and a snapshot taken while units are pinned and broken (tick
        // 300) and one taken late (tick 700) both resume to the same hashes up to the end.
        [Test]
        public void TheNerveBattleIsDeterministicAndResumes()
        {
            M6World a = AiM1Field.World(AiM1Field.Nerve), b = AiM1Field.World(AiM1Field.Nerve), early = null, late = null;
            Pressure pressure = a.System<Pressure>();
            while (!a.Mode.IsFinished)
            {
                a.Step(); b.Step();
                long tick = a.Sim.Clock.Tick;
                Assert.That(b.Hash, Is.EqualTo(a.Hash), "tick " + tick);
                if (early != null) { early.Step(); Assert.That(early.Hash, Is.EqualTo(a.Hash), "resumed from 300, tick " + tick); }
                if (late != null) { late.Step(); Assert.That(late.Hash, Is.EqualTo(a.Hash), "resumed from 700, tick " + tick); }
                if (tick != 300 && tick != 700) continue;
                M6World next = AiM1Field.World(AiM1Field.Nerve);
                next.Sim.RestoreRetainedSnapshot(a.Sim.SaveRetainedSnapshot());
                Assert.That(next.Hash, Is.EqualTo(a.Hash), "restored at tick " + tick);
                if (tick == 300)
                {
                    early = next;
                    int pinned = 0, broken = 0;
                    foreach (var unit in a.Recipe.Units)
                    {
                        if (a.Dead(unit.Key)) continue;
                        AiNerve nerve = pressure.Level(Id(a, unit.Key));
                        if (nerve == AiNerve.Pinned) pinned++; else if (nerve == AiNerve.Broken) broken++;
                    }
                    Assert.That((pinned, broken), Is.EqualTo((36, 14)), "the snapshot carries pinned and broken units");
                }
                else late = next;
            }
            Assert.That((a.Mode.Winner, a.Sim.Clock.Tick, early != null, late != null), Is.EqualTo((1, 1163L, true, true)));
        }
    }
}
