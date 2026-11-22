using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Stance.Stance1Worlds;
using static WarCore.Tests.VSpread1.VSpread1Worlds;

namespace WarCore.Tests.VSpread1
{
    // VSPREAD-1 V5 (two numbers from combat.spread per shot, horizontal first) and V13 (a projectile flies the same tilted line).
    public sealed class VSpread1ShotTests
    {
        private static UnitSpawn[] Pair() => new[] { Soldier(1, 1, 10500, 8000, 1), Soldier(2, 2, 20500, 8000, 2, -1) };

        // Shots and combat.spread draws of a world in which unit 1 fires automatically at unit 2 for n steps.
        private static (int Shots, int Draws) Count(CombatPack pack, int n = 12)
        {
            SimSession s = World(pack, Pair(), Map());
            int shots = Fires(s, n).Count;
            return (shots, SpreadDraws(s));
        }

        [Test]
        public void V5_DrawsPerShot()
        {
            (int shots, int draws) = Count(VPack(Shipped()));
            Assert.That(shots, Is.GreaterThan(5));
            Assert.That(draws, Is.EqualTo(2 * shots), "horizontal and vertical angles above 0");
            (shots, draws) = Count(VPack(Shipped(VerticalAim.Eye)));
            Assert.That(draws, Is.EqualTo(2 * shots), "aim eye draws the same");
            // A 999 millidegree gun with a 1 permille vertical spread: the vertical angle is 0, one number a shot.
            (shots, draws) = Count(VPack(new VerticalSpread(1, new[] { 1, 1 }), new WeaponDef(1000, 1000, 60000, default, SimFix.FromInt(67), 0,
                new SpreadSpec(999, 0, 0, 999))));
            Assert.That(shots, Is.GreaterThan(5));
            Assert.That(draws, Is.EqualTo(shots), "vertical angle 0");
            (shots, draws) = Count(VPack(Shipped(), NoSpread()), 70);
            Assert.That(shots, Is.GreaterThan(1));
            Assert.That(draws, Is.EqualTo(0), "a weapon without spread");
            (shots, draws) = Count(VPack(null));
            Assert.That(draws, Is.EqualTo(shots), "off: one number a shot");
        }

        // On a map with plates a shot straight up through the cutout has no horizontal part: it is not tilted, but both numbers
        // are drawn (whether to draw depends on the angle alone).
        [Test]
        public void V5_AVerticalLineStillDrawsTwo()
        {
            CombatPack pack = VPack(new VerticalSpread(1000), stanced: false);
            var w = new Ml9World(pack, new[] { Ml9World.U(12500, 0, 12500, 1), Ml9World.U(12500, Ml9World.Top, 12500, 2, -1) }, Ml9World.Floor());
            w.Produce = c => w.Combat.Submit(c, FireIntent.Aimed(w.Id(0), w.Point(1)));
            FireRecord[] fires = w.StepFires(0);
            Assert.That(fires.Length, Is.EqualTo(1));
            Assert.That((fires[0].End - fires[0].Origin).X.Raw, Is.EqualTo(0L));
            Assert.That((fires[0].End - fires[0].Origin).Z.Raw, Is.EqualTo(0L));
            Assert.That(SpreadDraws(w.Sim), Is.EqualTo(2));
        }

        // The player (unit 1) fires at a ground point far from any enemy in the Requests phase of tick 1; that shot is the first
        // use of combat.spread. Expected: the horizontal turn by the first number, then the tilt by the second.
        private static (FireRecord Fire, SimVec3 Expected, SimSession S) PlayerShot(CombatPack pack)
        {
            SimSession s = World(pack, Pair(), Map(), open: Player(1));
            CombatSystem combat = VSpread1Worlds.CombatOf(s);
            EntityId a = VSpread1Worlds.Unit(s, 1);
            StableId shooter = VSpread1Worlds.SimOf(s).Entities.StableOf(a);
            SimVec3 point = new SimVec3(SimFix.FromInt(30), default, SimFix.FromInt(2));
            Run(s, 1, SimSystemPhase.Requests, 16, c => combat.Submit(c, FireIntent.Aimed(shooter, point)));
            SimVec3 origin = combat.MuzzlePoint(a);
            SimVec3 direction = point + new SimVec3(default, SimFix.FromRaw(98304), default) - origin;
            int[] r = SpreadRolls(2);
            int shot = combat.ShotAngle(a, combat.Unit(a), 2000, 1);
            Assert.That(shot, Is.EqualTo(2000));
            SimVec3 e = VerticalSpread.Tilt(Rotate(direction, shot, r[0]), combat.VerticalAngle(a, shot), r[1]);
            List<FireRecord> fires = Fires(s, 1);
            Assert.That(fires.Count, Is.EqualTo(pack.HasProjectiles ? 0 : 1), "a projectile lands later");
            return (fires.Count == 0 ? default : fires[0], e, s);
        }

        [Test]
        public void V5_HorizontalFirstThenVertical()
        {
            CombatPack pack = VPack(Shipped());
            var (f, e, s) = PlayerShot(pack);
            Assert.That(f.Outcome, Is.EqualTo(FireOutcome.Open));
            SimVec3 end = f.Origin + e.Normalized * pack.Weapons[0].FalloffEnd;
            Assert.That((f.End.X.Raw, f.End.Y.Raw, f.End.Z.Raw), Is.EqualTo((end.X.Raw, end.Y.Raw, end.Z.Raw)));
            Assert.That(SpreadDraws(s), Is.EqualTo(2));
            Assert.That(f.End.Y.Raw, Is.Not.EqualTo(f.Origin.Y.Raw), "the shot left the level line");
        }

        // V13: the projectile's step is the same tilted direction, two numbers drawn.
        [Test]
        public void V13_AProjectileFliesTheTiltedLine()
        {
            CombatPack pack = VPack(Shipped(), speed: 30000);
            var (_, e, s) = PlayerShot(pack);
            ProjectileSystem projectiles = VSpread1Worlds.CombatOf(s).Projectiles;
            Assert.That(projectiles.Count, Is.EqualTo(1));
            SimVec3 step = e.Normalized * SimFix.FromRaw(pack.Projectiles[0].StepRaw);
            SimVec3 actual = projectiles.InFlight(0).Step;
            Assert.That((actual.X.Raw, actual.Y.Raw, actual.Z.Raw), Is.EqualTo((step.X.Raw, step.Y.Raw, step.Z.Raw)));
            Assert.That(SpreadDraws(s), Is.EqualTo(2));
        }
    }
}
