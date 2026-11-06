using System;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Wp18TestWorld;

namespace WarCore.Tests
{
    // When a unit's facing is written: a step turns it toward the step, standing still and not-moving keep it, and an
    // aimed shot by the controlled unit turns it toward the aim point. Only worlds whose recipe opens facing are touched.
    public sealed class FacingWriteTests
    {
        private const long S = Wp23.S;
        private static readonly MoverKind[] Movers = { MoverKind.Interim, MoverKind.InterimPath, MoverKind.InterimCrowd };

        private static SimSession Walk(MoverKind mover, long toXMm, long toZMm, bool facing = true, WcMap map = null)
        {
            var units = new[] { Wp23.Unit(1, 1, 20500, 12500, 4000) };
            var recipe = new WorldRecipe(41, mover, units, new[] { BridgeCommand.MoveTo(1, 1, toXMm, toZMm) });
            if (facing) recipe = recipe.WithFacing();
            if (map != null && map.HasTerrain) recipe = recipe.WithTerrainRules(Wp23.Pack());
            return SimSession.Create(recipe, WcMapWriter.Write(map ?? Wp23.Make(40, 24, 0, 6 * S, null, 4 * S)));
        }

        private static (long x, long z) Face(SimSession s, ulong key = 1)
        {
            Assert.That(s.TryFacing(key, out long x, out long z), Is.True);
            return (x, z);
        }

        // Four directions, three movers: the facing is exactly the unit axis the unit walks along.
        [Test]
        public void AStepTurnsTheUnitTowardTheStep([ValueSource(nameof(Movers))] MoverKind mover,
            [Values(0, 1, 2, 3)] int direction)
        {
            long[,] to = { { 30500, 12500 }, { 10500, 12500 }, { 20500, 20500 }, { 20500, 4500 } };
            (long, long)[] expected = { (S, 0), (-S, 0), (0, S), (0, -S) };
            SimSession s = Walk(mover, to[direction, 0], to[direction, 1]);
            Assert.That(Face(s), Is.EqualTo((0L, S)), "the default before any step");
            for (int i = 0; i < 20; i++) s.StepOnce();
            Assert.That(Face(s), Is.EqualTo(expected[direction]));
        }

        [TestCase(MoverKind.InterimPath), TestCase(MoverKind.InterimCrowd)]
        public void ADiagonalStepFacesTheDiagonal(MoverKind mover)
        {
            SimSession s = Walk(mover, 28500, 20500);
            for (int i = 0; i < 20; i++) s.StepOnce();
            (long x, long z) = Face(s);
            Assert.That(x, Is.EqualTo(46341).Within(2));
            Assert.That(z, Is.EqualTo(46341).Within(2));
        }

        [TestCase(MoverKind.Interim), TestCase(MoverKind.InterimPath), TestCase(MoverKind.InterimCrowd)]
        public void ArrivingAndStandingStillKeepTheFacing(MoverKind mover)
        {
            SimSession s = Walk(mover, 20500, 20500);
            for (int i = 0; i < 400; i++) s.StepOnce();
            Assert.That(M3TestWorld.Component<MoveIntent>(s).Active, Is.False, "the unit has arrived");
            Assert.That(Face(s), Is.EqualTo((0L, S)));
            Assert.That(Face(Walk(mover, 10500, 12500)), Is.EqualTo((0L, S)), "no step yet");
            SimSession w = Walk(mover, 10500, 12500);
            for (int i = 0; i < 400; i++) w.StepOnce();
            Assert.That(Face(w), Is.EqualTo((-S, 0L)));
            for (int i = 0; i < 100; i++) w.StepOnce();
            Assert.That(Face(w), Is.EqualTo((-S, 0L)), "a unit at rest keeps its facing");
        }

        [TestCase(MoverKind.Interim), TestCase(MoverKind.InterimPath), TestCase(MoverKind.InterimCrowd)]
        public void AnOrderToTheUnitsOwnPoint_NeverWritesAZeroDirection(MoverKind mover)
        {
            SimSession s = Walk(mover, 20500, 12500);
            for (int i = 0; i < 30; i++) s.StepOnce();
            Assert.That(Face(s), Is.EqualTo((0L, S)));
        }

        [TestCase(MoverKind.InterimPath), TestCase(MoverKind.InterimCrowd)]
        public void OnASlope_TheFacingIsTheHorizontalDirection(MoverKind mover)
        {
            WcMap hill = Wp23.Make(40, 24, 0, 6 * S, null, 4 * S, (i, j) => i * (int)(S / 16));
            SimSession s = Walk(mover, 30500, 12500, map: hill);
            for (int i = 0; i < 20; i++) s.StepOnce();
            Assert.That(Face(s), Is.EqualTo((S, 0L)));
        }

        [TestCase(MoverKind.Interim), TestCase(MoverKind.InterimPath), TestCase(MoverKind.InterimCrowd)]
        public void OpeningFacingDoesNotChangeMovement(MoverKind mover)
        {
            SimSession on = Walk(mover, 28500, 20500), off = Walk(mover, 28500, 20500, facing: false);
            for (int tick = 0; tick < 150; tick++)
            {
                on.StepOnce(); off.StepOnce();
                Assert.That(Wp23.At(on), Is.EqualTo(Wp23.At(off)), "tick " + tick);
            }
        }

        [Test]
        public void SnapshotsTakenWhileTurningResumeToTheSameHashes()
        {
            SimSession whole = Walk(MoverKind.InterimCrowd, 28500, 20500), a = Walk(MoverKind.InterimCrowd, 28500, 20500);
            for (int i = 0; i < 25; i++) { whole.StepOnce(); a.StepOnce(); }
            SimSession b = Walk(MoverKind.InterimCrowd, 28500, 20500);
            M3TestWorld.Sim(b).RestoreRetainedSnapshot(M3TestWorld.Sim(a).SaveRetainedSnapshot());
            Assert.That(Face(b), Is.EqualTo(Face(whole)));
            for (int i = 0; i < 80; i++)
            {
                whole.StepOnce(); b.StepOnce();
                Assert.That(b.StateHash, Is.EqualTo(whole.StateHash), "step " + (26 + i));
            }
            Assert.That(Face(b), Is.EqualTo(Face(whole)));
        }

        // The controlled unit stands at (10, 10) m.
        [TestCase(30000L, 10000L, S, 0L), TestCase(2000L, 10000L, -S, 0L), TestCase(10000L, 30000L, 0L, S), TestCase(10000L, 2000L, 0L, -S)]
        public void AnAimedShotTurnsTheControlledUnitTowardTheAim(long aimX, long aimZ, long expectedX, long expectedZ)
        {
            var w = new M6World(Recipe().WithFacing());
            Assert.That(Face(w.Session), Is.EqualTo((0L, S)));
            w.Session.SetPlayerInput(1, Aim(false, aimX, aimZ)); w.Session.StepOnce();
            Assert.That(Face(w.Session), Is.EqualTo((0L, S)), "aiming without firing leaves it");
            w.Session.SetPlayerInput(2, Aim(true, aimX, aimZ)); w.Session.StepOnce();
            Assert.That(Face(w.Session), Is.EqualTo((expectedX, expectedZ)));
        }

        [TestCase(1000, 0, S, 0L), TestCase(-1000, 0, -S, 0L), TestCase(0, 1000, 0L, S), TestCase(0, -1000, 0L, -S)]
        public void ControlledMovementTurnsTheUnitAlongTheInput(int moveX, int moveZ, long expectedX, long expectedZ)
        {
            var w = new M6World(Recipe().WithFacing());
            w.Session.SetPlayerInput(1, new PlayerInput(moveX, moveZ));
            for (int i = 0; i < 5; i++) w.Session.StepOnce();
            Assert.That(Face(w.Session), Is.EqualTo((expectedX, expectedZ)));
        }

        [Test]
        public void ADiagonalAim_AndAnAimOnTheUnit()
        {
            var w = new M6World(Recipe().WithFacing());
            w.Session.SetPlayerInput(1, Aim(true, 20000, 20000)); w.Session.StepOnce();
            (long x, long z) = Face(w.Session);
            Assert.That(x, Is.EqualTo(46341).Within(2)); Assert.That(z, Is.EqualTo(46341).Within(2));
            w.Session.SetPlayerInput(3, Aim(true, 10000, 10000)); w.Session.StepOnce(); w.Session.StepOnce();
            Assert.That(Face(w.Session), Is.EqualTo((x, z)), "an aim point on top of the unit keeps its facing");
        }

        [Test]
        public void AFacingOffWorldIsUntouchedByPlayerFire()
        {
            var on = new M6World(Recipe().WithFacing()); var off = new M6World(Recipe());
            on.Session.SetPlayerInput(1, Aim(true, 30000, 10000)); off.Session.SetPlayerInput(1, Aim(true, 30000, 10000));
            for (int i = 0; i < 5; i++) { on.Session.StepOnce(); off.Session.StepOnce(); }
            Assert.That(off.Session.TryFacing(1, out _, out _), Is.False);
            Assert.That(on.Read<WarCore.Combat.Combatant>(1).Ammo, Is.EqualTo(off.Read<WarCore.Combat.Combatant>(1).Ammo));
        }
    }
}
