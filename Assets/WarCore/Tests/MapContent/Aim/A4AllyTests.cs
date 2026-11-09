using NUnit.Framework;
using WarCore.Combat;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // A4 acceptance 9 (dispatch item 2 (a): A3b merged first, so A4 makes its public AllyBlocks 3D). Plateau map, shooter team 1
    // at x = 3.5 m (Y 0), target team 2 at 20.5 m (Y 2 m), z = 4.5 m; two separate worlds.
    public sealed class A4AllyTests
    {
        private static readonly SimFix Range = SimFix.FromInt(30);

        // Teammate at 11.5 m: the 3D line passes 0.941 m above its centre (radius 0.5 m), the horizontal line would hit it.
        [Test]
        public void A4Ally_FarTeammateUnderTheRisingLine_DoesNotBlock()
        {
            var (w, aim) = A4.IntentWorld(A4.PlateauMap(),
                new[] { A4.Unit(1, 1, 3500, 0), A4.Unit(3, 1, 11500, -1), A4.Unit(2, 2, 20500, -1) }, suppress: true);
            SimVec3 target = w.Read<Position>(2).Value;
            aim.Point = target;
            Assert.That(target.Y.Raw, Is.EqualTo(2 * A4.S));
            Assert.That(w.Combat.AllyBlocks(w.Unit(1), target, Range), Is.False);
            w.Step();
            Assert.That(w.Combat.Fires.Length, Is.EqualTo(1));
            Assert.That(w.Combat.Fires[0].Outcome, Is.EqualTo(FireOutcome.Hit));
            Assert.That(w.Combat.Fires[0].Target, Is.EqualTo(Wp18TestWorld.Id(2)));
        }

        // Teammate at 5.5 m: the 3D line is only 0.235 m above its centre there, so it blocks and the suppress intent is declined.
        [Test]
        public void A4Ally_NearTeammateOnTheLine_BlocksAndTheIntentIsDeclined()
        {
            var (w, aim) = A4.IntentWorld(A4.PlateauMap(),
                new[] { A4.Unit(1, 1, 3500, 0), A4.Unit(3, 1, 5500, -1), A4.Unit(2, 2, 20500, -1) }, suppress: true);
            SimVec3 target = w.Read<Position>(2).Value;
            aim.Point = target;
            Assert.That(w.Combat.AllyBlocks(w.Unit(1), target, Range), Is.True);
            Combatant before = w.Read<Combatant>(1);
            ulong rng = A4.Rng(w);
            w.Step();
            Assert.That(w.Combat.Fires.Length, Is.EqualTo(0));
            Combatant after = w.Read<Combatant>(1);
            Assert.That(after.Ammo, Is.EqualTo(before.Ammo));
            Assert.That(after.DueMilli, Is.EqualTo(before.DueMilli));
            Assert.That(after.SpreadStored, Is.EqualTo(before.SpreadStored));
            Assert.That(after.SpreadLastMilli, Is.EqualTo(before.SpreadLastMilli));
            Assert.That(A4.Rng(w), Is.EqualTo(rng));
        }
    }
}
