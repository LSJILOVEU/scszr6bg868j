using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Sim;
using static WarCore.Tests.Ml9World;

namespace WarCore.Tests
{
    // ML-9 acceptance 3 (sight), 4 (shots), 6 (straight up and down) and 8 (the body check) on the fixture plate: the first
    // floor over x, z in [8, 16) m, top 3 m, bottom 2.5 m, a cutout at x, z in [12, 13) m.
    public sealed class Ml9SightShotTests
    {
        private static readonly CombatBody?[] Bodies = { null, Ml3World.Rwr };
        // The lift of the line tested against ground and plates: 0.5 m without a body, none with one.
        private static long Lift(CombatBody? body) => body.HasValue ? 0 : M / 2;

        private static Ml9World World(CombatBody? body, bool plates, params Ml3World.Unit[] units) =>
            new Ml9World(Pack(body), units, plates ? Floor() : null);

        [TestCaseSource(nameof(Bodies))]
        public void FloorsHideEachOther(CombatBody? body)
        {
            var w = World(body, true, U(10500, 0, 10500, 1), U(10500, Top, 10500, 2), U(14500, Top, 14500, 2), U(2000, 0, 2000, 1), U(6000, 0, 2000, 2));
            Assert.That((w.Combat.Visible(w.Unit(0), w.Unit(1)), w.Combat.Visible(w.Unit(1), w.Unit(0))), Is.EqualTo((false, false)), "straight above");
            Assert.That((w.Combat.Visible(w.Unit(0), w.Unit(2)), w.Combat.Visible(w.Unit(2), w.Unit(0))), Is.EqualTo((false, false)), "above, aside");
            Assert.That(w.Combat.Visible(w.Unit(0), w.Point(2)), Is.False, "a point on the floor");
            Assert.That(w.Combat.Sees(w.Point(0), w.Point(2)), Is.False, "ground point to floor point");
            Assert.That(w.Combat.Visible(w.Unit(1), w.Unit(2)), Is.True, "the same floor");
            Assert.That(w.Combat.Visible(w.Unit(3), w.Unit(4)), Is.True, "two on the ground off the plate");
            var flat = World(body, false, U(10500, 0, 10500, 1), U(10500, Top, 10500, 2), U(14500, Top, 14500, 2));
            Assert.That((flat.Combat.Visible(flat.Unit(0), flat.Unit(1)), flat.Combat.Visible(flat.Unit(0), flat.Unit(2))), Is.EqualTo((true, true)),
                "without plates nothing is in the way");
        }

        // A vertical line is tested against the plates alone: through the cutout it is clear, through the floor it is not; without
        // plates the old rule (a horizontal zero is clear) stands.
        [TestCaseSource(nameof(Bodies))]
        public void TheCutoutOpensTheVerticalLine(CombatBody? body)
        {
            var w = World(body, true, U(12500, 0, 12500, 1), U(12500, Top, 12500, 2), U(10500, 0, 10500, 1), U(10500, Top, 10500, 2));
            Assert.That((w.Combat.Visible(w.Unit(0), w.Unit(1)), w.Combat.Visible(w.Unit(1), w.Unit(0))), Is.EqualTo((true, true)), "through the cutout");
            Assert.That(w.Combat.Visible(w.Unit(2), w.Unit(3)), Is.False, "through the floor");
            Assert.That(w.Combat.Visible(w.Unit(2), w.Unit(2)), Is.True, "a zero-length line");
        }

        // Shots through the floor stop in it; the recorded end lies on the plate (with a body) or 0.5 m lower (without, where the
        // tested line is lifted by 0.5 m). Floor to floor and through the cutout the shot goes on.
        [TestCaseSource(nameof(Bodies))]
        public void ShotsStopInTheFloor(CombatBody? body)
        {
            long low = Bottom - Lift(body) - 1, high = Top - Lift(body);
            var w = World(body, true, U(10500, 0, 10500, 1), U(14500, Top, 14500, 2), U(9500, Top, 9500, 1));
            w.Produce = c => w.Combat.Submit(c, FireIntent.Aimed(w.Id(0), w.Point(1)));
            FireRecord up = w.StepFires(0).Single();
            Assert.That(up.Outcome, Is.EqualTo(FireOutcome.WallBlocked), "up");
            Assert.That(up.End.Y.Raw, Is.InRange(low, high), "up: the end at the plate");

            w = World(body, true, U(10500, 0, 10500, 1), U(14500, Top, 14500, 2), U(9500, Top, 9500, 1));
            w.Produce = c => w.Combat.Submit(c, FireIntent.Aimed(w.Id(1), w.Point(0)));
            FireRecord down = w.StepFires(1).Single();
            Assert.That(down.Outcome, Is.EqualTo(FireOutcome.WallBlocked), "down");
            Assert.That(down.End.Y.Raw, Is.InRange(low, high), "down: the end at the plate");

            w = World(body, true, U(10500, 0, 10500, 1), U(14500, Top, 14500, 2), U(9500, Top, 9500, 1));
            w.Produce = c => w.Combat.Submit(c, FireIntent.Aimed(w.Id(1), w.Point(2)));
            FireRecord level = w.StepFires(1).Single();
            Assert.That((level.Outcome, level.Target), Is.EqualTo((FireOutcome.Hit, w.Id(2))), "floor to floor");

            var flat = World(body, false, U(10500, 0, 10500, 1), U(14500, Top, 14500, 2));
            flat.Produce = c => flat.Combat.Submit(c, FireIntent.Aimed(flat.Id(0), flat.Point(1)));
            Assert.That(flat.StepFires(0).Single().Outcome, Is.EqualTo(FireOutcome.Hit), "without plates");
        }

        // Acceptance 4 on a flat plate map: no ground field at all, the plates alone (the Trace entry condition).
        [Test]
        public void WithoutGroundThePlatesStillStopShots()
        {
            var w = new Ml9World(Pack(null), new[] { U(10500, 0, 10500, 1), U(14500, Top, 14500, 2) }, Floor(), ground: null);
            w.Produce = c => w.Combat.Submit(c, FireIntent.Aimed(w.Id(0), w.Point(1)));
            Assert.That(w.StepFires(0).Single().Outcome, Is.EqualTo(FireOutcome.WallBlocked));
        }

        // With a flat ground at 0 under the plate both are tested; the plate comes first.
        [Test]
        public void WithGroundThePlateComesFirst()
        {
            var ground = new HeightField(32, 32, default, default, new int[33 * 33]);
            var w = new Ml9World(Pack(Ml3World.Rwr), new[] { U(10500, 0, 10500, 1), U(14500, Top, 14500, 2) }, Floor(), ground: ground);
            w.Produce = c => w.Combat.Submit(c, FireIntent.Aimed(w.Id(0), w.Point(1)));
            FireRecord shot = w.StepFires(0).Single();
            Assert.That(shot.Outcome, Is.EqualTo(FireOutcome.WallBlocked));
            Assert.That(shot.End.Y.Raw, Is.InRange(Bottom - 1, Top));
        }

        // The plate touching the unit at the end of the ray wins, as the ground does.
        [Test]
        public void APlateAtTheEndWins()
        {
            // A horizontal shot 2.75 m up (inside the plate's body height) at a unit whose side is at x = 8 m, where the plate begins.
            var units = new[] { U(4000, 2750 * M / 1000 - Ml3World.Rwr.Muzzle.Raw, 10500, 1), U(8250, 2750 * M / 1000 - Ml3World.Rwr.Eye.Raw, 10500, 2) };
            var w = new Ml9World(Pack(Ml3World.Rwr), units, Floor());
            w.Produce = c => w.Combat.Submit(c, FireIntent.Aimed(w.Id(0), w.Point(1)));
            FireRecord shot = w.StepFires(0).Single();
            Assert.That(shot.Outcome, Is.EqualTo(FireOutcome.WallBlocked));
            Assert.That(shot.End.X.Raw, Is.EqualTo(8 * M));
            var flat = new Ml9World(Pack(Ml3World.Rwr), units, null);
            flat.Produce = c => flat.Combat.Submit(c, FireIntent.Aimed(flat.Id(0), flat.Point(1)));
            Assert.That(flat.StepFires(0).Single().Outcome, Is.EqualTo(FireOutcome.Hit), "without the plate the unit is hit");
        }

        // 6: straight up and down. An aimed point or a target straight above, at another height on a plate map, is shot along the
        // vertical (through the cutout it hits, through the floor it stops); at the same height the old rules stand.
        [TestCaseSource(nameof(Bodies))]
        public void StraightUpAndDown(CombatBody? body)
        {
            var w = World(body, true, U(12500, 0, 12500, 1), U(12500, Top, 12500, 2));
            w.Produce = c => w.Combat.Submit(c, FireIntent.Aimed(w.Id(0), w.Point(1)));
            FireRecord hole = w.StepFires(0).Single();
            Assert.That((hole.Outcome, hole.End.X.Raw, hole.End.Z.Raw), Is.EqualTo((FireOutcome.Hit, 12500 * M / 1000, 12500 * M / 1000)), "aimed, through the cutout");

            w = World(body, true, U(10500, 0, 10500, 1), U(10500, Top, 10500, 2));
            w.Produce = c => w.Combat.Submit(c, FireIntent.Aimed(w.Id(0), w.Point(1)));
            Assert.That(w.StepFires(0).Single().Outcome, Is.EqualTo(FireOutcome.WallBlocked), "aimed, into the floor");

            // Down: a point straight below through the cutout is hit, through the floor the shot stops in it.
            w = World(body, true, U(12500, 0, 12500, 2), U(12500, Top, 12500, 1));
            w.Produce = c => w.Combat.Submit(c, FireIntent.Aimed(w.Id(1), w.Point(0)));
            FireRecord down = w.StepFires(1).Single();
            Assert.That((down.Outcome, down.End.X.Raw, down.End.Z.Raw), Is.EqualTo((FireOutcome.Hit, 12500 * M / 1000, 12500 * M / 1000)), "aimed down, through the cutout");
            w = World(body, true, U(10500, 0, 10500, 2), U(10500, Top, 10500, 1));
            w.Produce = c => w.Combat.Submit(c, FireIntent.Aimed(w.Id(1), w.Point(0)));
            FireRecord floor = w.StepFires(1).Single();
            Assert.That((floor.Outcome, floor.End.X.Raw, floor.End.Z.Raw), Is.EqualTo((FireOutcome.WallBlocked, 10500 * M / 1000, 10500 * M / 1000)), "aimed down, into the floor");

            // Eye above the muzzle: aiming at its own spot gives a short vertical line, which only another height may fire along.
            w = new Ml9World(Pack(Ml3World.Body(114688, 104858, 91750)), new[] { U(10500, 0, 10500, 1), U(4000, 0, 4000, 2) }, Floor());
            w.Produce = c => w.Combat.Submit(c, FireIntent.Aimed(w.Id(0), w.Point(0)));
            Assert.That(w.StepFires(0), Is.Empty, "aimed at its own spot: no shot, as before");

            var flat = World(body, false, U(12500, 0, 12500, 1), U(12500, Top, 12500, 2));
            flat.Produce = c => flat.Combat.Submit(c, FireIntent.Aimed(flat.Id(0), flat.Point(1)));
            Assert.That(flat.StepFires(0), Is.Empty, "without plates a point straight above gives no shot, as before");
        }

        // 6: the automatic fire at a visible target straight above (through the cutout) goes along the vertical; X and Z of the
        // end stay the shooter's (no spread on a vertical line). Without plates it goes along +X, as before.
        [TestCaseSource(nameof(Bodies))]
        public void TheAutomaticShotGoesStraightUp(CombatBody? body)
        {
            var spread = new WeaponDef(30000, 30, 60000, SimFix.FromInt(30), SimFix.FromInt(60), 10 * M, new SpreadSpec(5000, 0, 0, 5000));
            var pack = new CombatPack(new[] { spread }, new ZeroResolver(), new NearestVisibleEnemy(), 100 * M, body: body);
            var w = new Ml9World(pack, new[] { U(12500, 0, 12500, 1), U(12500, Top, 12500, 2, -1) }, Floor());
            FireRecord up = w.StepFires(0).Single();
            Assert.That((up.Outcome, up.Target), Is.EqualTo((FireOutcome.Hit, w.Id(1))));
            Assert.That((up.End.X.Raw, up.End.Z.Raw), Is.EqualTo((12500 * M / 1000, 12500 * M / 1000)), "straight up, spread or not");

            var flat = new Ml9World(pack, new[] { U(12500, 0, 12500, 1), U(12500, Top, 12500, 2, -1) }, null);
            FireRecord plus = flat.StepFires(0).Single();
            Assert.That(plus.End.X.Raw, Is.GreaterThan(plus.Origin.X.Raw), "without plates: along +X (turned by the spread)");

            // The same spot and height on a plate map, eye above the muzzle (a short vertical line): along +X, as before.
            var rifle = new CombatPack(new[] { Ml3World.Rifle() }, new ZeroResolver(), new NearestVisibleEnemy(), 100 * M, body: Ml3World.Body(114688, 104858, 91750));
            var same = new Ml9World(rifle, new[] { U(4500, 0, 4500, 1), U(4500, 0, 4500, 2, -1) }, Floor());
            FireRecord level = same.StepFires(0).Single();
            Assert.That(level.End.Y, Is.EqualTo(level.Origin.Y), "the same height on a plate map: a level shot, as before");
        }

        // 6: the AI's teammate check towards a point straight above follows the same rule.
        [Test]
        public void TheTeammateCheckLooksStraightUp()
        {
            var w = World(Ml3World.Rwr, true, U(12500, 0, 12500, 1), U(12500, Top, 12500, 1), U(4000, 0, 4000, 2));
            SimVec3 above = Raw(12500 * M / 1000, Top + M, 12500 * M / 1000);
            Assert.That(w.Combat.AllyBlocks(w.Unit(0), above, SimFix.FromInt(30)), Is.True, "the teammate above, through the cutout");
            var flat = World(Ml3World.Rwr, false, U(12500, 0, 12500, 1), U(12500, Top, 12500, 1), U(4000, 0, 4000, 2));
            Assert.That(flat.Combat.AllyBlocks(flat.Unit(0), above, SimFix.FromInt(30)), Is.False, "without plates: no line");
            Assert.That(w.Combat.AllyBlocks(w.Unit(0), Raw(12500 * M / 1000, 0, 12500 * M / 1000), SimFix.FromInt(30)), Is.False,
                "its own spot: no line");
        }

        // 8: on a plate map the body's eye and muzzle must be at least 1 raw above the foot.
        [Test]
        public void ThePlatesNeedAnEyeAndMuzzleAboveTheFoot()
        {
            const string Message = "A map with floor plates needs a combat body whose eye and muzzle are at least 1 raw above the foot.";
            var units = new[] { U(4000, 0, 4000, 1) };
            Assert.That(Assert.Throws<InvalidOperationException>(() => new Ml9World(Pack(Ml3World.Body(114688, 98304, 0)), units, Floor())).Message, Is.EqualTo(Message));
            Assert.That(Assert.Throws<InvalidOperationException>(() => new Ml9World(Pack(Ml3World.Body(114688, 0, 98304)), units, Floor())).Message, Is.EqualTo(Message));
            Assert.DoesNotThrow(() => new Ml9World(Pack(Ml3World.Body(114688, 1, 1)), units, Floor()));
            Assert.DoesNotThrow(() => new Ml9World(Pack(Ml3World.Body(114688, 98304, 0)), units, null));
            Assert.DoesNotThrow(() => new Ml9World(Pack(null), units, Floor()));
        }
    }
}
