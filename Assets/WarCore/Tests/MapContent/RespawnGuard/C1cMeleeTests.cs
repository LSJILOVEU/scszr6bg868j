using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Sim;
using static WarCore.Tests.RespawnWorld;

namespace WarCore.Tests
{
    // C1c (3): melee strikes on the unit switched to are zeroed inside the window, and the guard is checked before any block.
    // Enemy 11 at (21.5, 19) swings from tick 6 every 6 ticks (windup 3): strikes land on 9, 15, 21, 27.
    public sealed class C1cMeleeTests
    {
        private static InputFrame BlockWest => new InputFrame(default, SimVec3.Metres(0, 0, 19), block: true);

        private static M6World World(int guard) => C1c.World(C1c.MeleeUnits(), C1c.Pack(guard), C1c.MeleePack());

        private static void Premise(M6World w)
        {
            SimVec3 enemy = new SimVec3(SimFix.Ratio(43, 2), default, SimFix.FromInt(19));
            Assert.That(CaptureSystem.Inside(enemy, C1c.PointA()[0]), Is.False, "the enemy stands outside point A");
            Assert.That(Wp16TestWorld.Point(w, C1c.Reserve), Is.EqualTo(C1c.Spot), "deployed on the spot, not blocked by the enemy");
            Assert.That(CombatSystem.Horizontal(enemy - C1c.Spot).Length.Raw, Is.LessThan(SimFix.Ratio(18, 10).Raw), "within engage range");
        }

        // Strike kinds and damage recorded on the target per tick, for ticks first..last.
        private static Dictionary<long, string> Strikes(M6World w, long first, long last, bool block)
        {
            var seen = new Dictionary<long, string>();
            ulong target = 0;
            for (long tick = w.Sim.Clock.Tick + 1; tick <= last; tick++)
            {
                if (block) w.Sim.Step(BlockWest); else w.Step();
                if (tick == C1c.Switch) { Premise(w); target = w.Sim.Entities.StableOf(w.Unit(C1c.Reserve)).Value; if (block) Assert.That(w.Combat.IsHolding(w.Unit(C1c.Reserve)), Is.True); }
                if (tick < first) continue;
                foreach (StrikeRecord s in w.Combat.Strikes)
                    if (s.Target.Value == target && (s.Kind == StrikeKind.Landed || s.Kind == StrikeKind.Blocked))
                        seen[tick] = s.Kind + " " + s.DamageRaw / SimFix.Scale + (w.Dead(C1c.Reserve) ? " dead" : " alive");
            }
            return seen;
        }

        [Test]
        public void C1cMelee_GuardZeroesStrikesInTheWindow()
        {
            var seen = Strikes(World(10), C1c.Switch, 21, false);
            Assert.That(seen, Is.EqualTo(new Dictionary<long, string> { { 9, "Landed 0 alive" }, { 15, "Landed 0 alive" }, { 21, "Landed 100 dead" } }));
        }

        [Test]
        public void C1cMelee_NoGuard_TheFirstStrikeKills()
        {
            var seen = Strikes(World(0), C1c.Switch, 9, false);
            Assert.That(seen, Is.EqualTo(new Dictionary<long, string> { { 9, "Landed 100 dead" } }));
        }

        // A held, frontal, full block: inside the window the guard decides first (Landed 0), after it the block (Blocked).
        [Test]
        public void C1cMelee_GuardIsCheckedBeforeTheBlock()
        {
            var seen = Strikes(World(10), C1c.Switch, 27, true);
            Assert.That(seen, Is.EqualTo(new Dictionary<long, string> {
                { 9, "Landed 0 alive" }, { 15, "Landed 0 alive" }, { 21, "Blocked 0 alive" }, { 27, "Blocked 0 alive" } }));
        }

        [Test]
        public void C1cMelee_NoGuard_TheHeldBlockStopsEveryStrike()
        {
            var seen = Strikes(World(0), C1c.Switch, 21, true);
            Assert.That(seen, Is.EqualTo(new Dictionary<long, string> { { 9, "Blocked 0 alive" }, { 15, "Blocked 0 alive" }, { 21, "Blocked 0 alive" } }));
        }
    }
}
