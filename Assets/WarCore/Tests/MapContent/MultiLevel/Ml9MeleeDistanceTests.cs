using System.Linq;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Sim;
using static WarCore.Tests.Ml9World;

namespace WarCore.Tests
{
    // ML-9 acceptance 5 (melee through the floor) and 7 (the 3D distance on plate maps, each call site; the same layout without
    // plates keeps the horizontal answer).
    public sealed class Ml9MeleeDistanceTests
    {
        private static readonly CombatBody?[] Bodies = { null, Ml3World.Rwr };
        private static SimFix Metres(long mm) => SimFix.Ratio(mm, 1000);

        // A thrust: windup 1 tick, 4 m long and 3.5 m wide (reaches a unit 3 m up), engage range 4 m.
        private static MeleeSpec Thrust(long radiusMm = 4000, long halfWidthMm = 3500, int maxTargets = 1) =>
            new MeleeSpec(1, 1, 0, Metres(radiusMm), 0, Metres(4000), SimFix.FromInt(10), maxTargets, Metres(radiusMm), Metres(halfWidthMm));
        // A sector: windup 1 tick, radius 8 m, +-90 degrees, 4 m high each way.
        private static MeleeSpec Sector(int maxTargets = 1) =>
            new MeleeSpec(1, 1, 0, SimFix.FromInt(8), 0, SimFix.FromInt(4), SimFix.FromInt(10), maxTargets, SimFix.FromInt(8));

        private static Ml9World Melee(CombatBody? body, MeleeSpec spec, bool plates, params Ml3World.Unit[] units)
        {
            var w = new Ml9World(Pack(body, melee: new[] { spec }), units, plates ? Floor() : null);
            for (int i = 0; i < units.Length; i++) w.Combat.SetMelee(w.Unit(i), 0);
            return w;
        }

        private static StrikeRecord[] Run(Ml9World w, int ticks)
        {
            var all = new System.Collections.Generic.List<StrikeRecord>();
            for (int t = 0; t < ticks; t++) { w.Step(); all.AddRange(w.Combat.Strikes.ToArray()); }
            return all.ToArray();
        }

        // 5: a thrust or a sector (4 m high each way) at a unit straight above: through the floor it whiffs, through the cutout
        // it lands, without plates it lands.
        [TestCaseSource(nameof(Bodies))]
        public void TheFloorStopsMelee(CombatBody? body)
        {
            foreach (MeleeSpec spec in new[] { Thrust(), Sector() })
            foreach ((long at, bool plates, StrikeKind expected) in new[] { (10500L, true, StrikeKind.Whiffed), (12500L, true, StrikeKind.Landed), (10500L, false, StrikeKind.Landed) })
            {
                var w = Melee(body, spec, plates, U(at, 0, at, 1, -1), U(at, Top, at, 2, -1));
                w.Produce = c => { if (c.Clock.Tick == 1) w.Combat.Submit(c, FireIntent.AtTarget(w.Id(0), w.Id(1))); };
                StrikeRecord[] strikes = Run(w, 3).Where(r => r.Striker.Equals(w.Id(0))).Take(2).ToArray();
                Assert.That(strikes.Select(r => r.Kind), Is.EqualTo(new[] { StrikeKind.Started, expected }), spec.IsThrust + " " + at + " " + plates);
            }
        }

        // 7, Melee.cs reach of an explicit target: a gunless unit 3 m below its target, thrust 2 m: out of reach in 3D.
        [Test]
        public void TheReachIsCountedIn3D()
        {
            foreach (bool plates in new[] { true, false })
            {
                var w = Melee(Ml3World.Rwr, Thrust(2000, 3500), plates, U(12500, 0, 12500, 1, -1), U(12500, Top, 12500, 2, -1));
                w.Produce = c => { if (c.Clock.Tick == 1) w.Combat.Submit(c, FireIntent.AtTarget(w.Id(0), w.Id(1))); };
                Assert.That(Run(w, 1).Any(r => r.Kind == StrikeKind.Started), Is.EqualTo(!plates), plates ? "3D: 3 m > 2 m" : "horizontal: 0 m");
            }
        }

        // 7, Melee.cs reach of an aimed point (a unit with a gun swings only within the engage range): a point 1 m aside and 3 m up.
        [Test]
        public void AnAimedSwingCountsIn3D()
        {
            foreach (bool plates in new[] { true, false })
            {
                var w = Melee(Ml3World.Rwr, Thrust(2000, 500), plates, U(4000, 0, 4000, 1), U(20000, 0, 20000, 2, -1));
                SimVec3 point = Raw(5 * M, Top, 4 * M);
                w.Produce = c => { if (c.Clock.Tick == 1) w.Combat.Submit(c, FireIntent.Aimed(w.Id(0), point)); };
                Assert.That(Run(w, 1).Any(r => r.Kind == StrikeKind.Started), Is.EqualTo(!plates), plates ? "3D: 3.16 m > 2 m" : "horizontal: 1 m");
            }
        }

        // The layout for the target choices: the striker on open ground at (2, 12) m; A on the first floor at (8.5, 12) m
        // (horizontal 6.5 m, 3D 7.16 m); B on the ground at (2, 5) m (7 m either way). Both are in sight of the striker.
        private static Ml3World.Unit[] Choice(int strikerWeapon) =>
            new[] { U(2000, 0, 12000, 1, strikerWeapon), U(8500, Top, 12000, 2, -1), U(2000, 0, 5000, 2, -1) };

        // 7, Melee.cs TryNearestEnemy: an automatic gunless striker thrusts towards the nearest enemy. The thrust is 8 m long and
        // 3.5 m wide: towards A (+X) it misses B, towards B (-Z) it misses A, so the one hit shows the heading chosen.
        [TestCaseSource(nameof(Bodies))]
        public void TheNearestEnemyToStrikeIsNearestIn3D(CombatBody? body)
        {
            foreach (bool plates in new[] { true, false })
            {
                var w = Melee(body, Thrust(8000, 3500), plates, Choice(-1));
                StrikeRecord landed = Run(w, 3).Single(r => r.Striker.Equals(w.Id(0)) && r.Kind == StrikeKind.Landed);
                Assert.That(landed.Target, Is.EqualTo(w.Id(plates ? 2 : 1)), plates ? "B, 3D nearer" : "A, horizontally nearer");
            }
        }

        // 7, Melee.cs ordering of the hit units: one sector swing towards (5, 9) m reaches A and B; one target is taken.
        [TestCaseSource(nameof(Bodies))]
        public void TheSwingTakesTheNearestIn3D(CombatBody? body)
        {
            foreach (bool plates in new[] { true, false })
            {
                var w = Melee(body, Sector(), plates, Choice(0));
                w.Produce = c => { if (c.Clock.Tick == 1) w.Combat.Submit(c, FireIntent.Aimed(w.Id(0), Raw(5 * M, 0, 9 * M))); };
                StrikeRecord landed = Run(w, 3).Single(r => r.Striker.Equals(w.Id(0)) && r.Kind == StrikeKind.Landed);
                Assert.That(landed.Target, Is.EqualTo(w.Id(plates ? 2 : 1)), plates ? "B" : "A");
            }
        }

        // 7, Block.cs TryMeleeThreat: a swing winding up on the ground does not threaten a unit 3 m above (radius 2 m) on a plate map.
        [Test]
        public void AThreatIsCountedIn3D()
        {
            var spec = new MeleeSpec(5, 1, 0, SimFix.FromInt(2), 0, SimFix.FromInt(4), SimFix.FromInt(10), 1, SimFix.FromInt(2));
            foreach (bool plates in new[] { true, false })
            {
                var w = Melee(Ml3World.Rwr, spec, plates, U(10500, 0, 10500, 1, -1), U(11000, 0, 10500, 2, -1), U(11000, Top, 10500, 2, -1));
                w.Produce = c => { if (c.Clock.Tick == 1) w.Combat.Submit(c, FireIntent.AtTarget(w.Id(0), w.Id(1))); };
                Assert.That(Run(w, 1).Any(r => r.Kind == StrikeKind.Started && r.Striker.Equals(w.Id(0))), Is.True);
                Assert.That(w.Combat.TryMeleeThreat(w.Unit(2), out _, out _), Is.EqualTo(!plates), plates ? "3D 3.04 m > 2 m" : "horizontal 0.5 m");
                Assert.That(w.Combat.TryMeleeThreat(w.Unit(1), out _, out _), Is.True, "the unit beside it");
            }
        }

        // 7, Resolve.cs: an explicit target 59.95 m away horizontally and 3 m up is beyond the 60 m range in 3D.
        [Test]
        public void TheTargetRangeIsCountedIn3D()
        {
            foreach (bool plates in new[] { true, false })
            {
                var w = new Ml9World(Pack(Ml3World.Rwr), new[] { U(2000, 0, 2000, 1), U(61950, Top, 2000, 2, -1) }, plates ? Floor(96, 32) : null);
                w.Produce = c => w.Combat.Submit(c, FireIntent.AtTarget(w.Id(0), w.Id(1)));
                Assert.That(w.StepFires().Length, Is.EqualTo(plates ? 0 : 1), plates ? "3D 60.02 m" : "horizontal 59.95 m");
            }
        }

        // 7, Resolve.cs: the recorded distance of a hit (the falloff distance) and of a miss is 3D on a plate map.
        [Test]
        public void RecordedDistancesAre3D()
        {
            foreach (bool plates in new[] { true, false })
            {
                var w = new Ml9World(Pack(Ml3World.Rwr), Choice(0), plates ? Floor() : null);
                w.Produce = c => w.Combat.Submit(c, FireIntent.Aimed(w.Id(0), w.Point(1)));
                FireRecord hit = w.StepFires().Single();
                SimVec3 d = w.Point(1) - w.Point(0);
                Assert.That((hit.Outcome, hit.Distance), Is.EqualTo((FireOutcome.Hit, plates ? d.Length : CombatSystem.Horizontal(d).Length)), "hit");
            }
            var m = new Ml9World(Pack(Ml3World.Rwr), new[] { U(10500, 0, 10500, 1), U(14500, Top, 14500, 2, -1) }, Floor());
            m.Produce = c => m.Combat.Submit(c, FireIntent.Aimed(m.Id(0), m.Point(1)));
            FireRecord miss = m.StepFires().Single();
            Assert.That((miss.Outcome, miss.Distance), Is.EqualTo((FireOutcome.WallBlocked, (miss.End - miss.Origin).Length)), "miss");
            Assert.That(miss.Distance, Is.Not.EqualTo(CombatSystem.Horizontal(miss.End - miss.Origin).Length));
        }

        // 7, NearestVisibleEnemy and ConcealedSight (which must agree): the target is the 3D nearest on a plate map.
        [TestCaseSource(nameof(Bodies))]
        public void TheSelectorsPickTheNearestIn3D(CombatBody? body)
        {
            var cover = new short[32 * 32];
            for (int i = 0; i < cover.Length; i++) cover[i] = 1000;
            foreach (ITargetSelector selector in new ITargetSelector[] { new NearestVisibleEnemy(), new ConcealedSight(new NearestVisibleEnemy(), A2.Grid(32, 32), cover) })
                foreach (bool plates in new[] { true, false })
                {
                    var w = new Ml9World(Pack(body, selector: selector), Choice(0), plates ? Floor() : null);
                    Assert.That(selector.TrySelect(w.Combat, w.Unit(0), SimFix.FromInt(30), out EntityId target), Is.True);
                    Assert.That(target, Is.EqualTo(w.Unit(plates ? 2 : 1)), selector.GetType().Name + " " + plates);
                }
        }
    }
}
