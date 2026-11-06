using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Wp18TestWorld;

namespace WarCore.Tests
{
    // Fixtures for the auto-fire seam. Every number is written here, none comes from the code under test.
    internal static class AutoFireFixtures
    {
        // Run lengths and the window in which the scripted shooters hold their trigger.
        internal const int AiTicks = 600, PlayerTicks = 120, IntentTicks = 120, PairTicks = 90;
        internal const long WindowFirst = 30, WindowLast = 89;

        // All eighty M6 soldiers are driven by the default fire decision; returns the total number of shots over AiTicks.
        internal static int AiFires(System.Action<CombatSystem> configure = null)
        {
            var w = new M6World(DemoRecipes.M6(), DemoRecipes.M6MapBytes());
            configure?.Invoke(w.Combat);
            int fires = 0;
            for (int tick = 1; tick <= AiTicks; tick++) { w.Step(); fires += w.Combat.Fires.Length; }
            return fires;
        }

        // The player holds the primary attack on ticks 30..89 aimed at an empty patch of ground; the only other unit has no weapon,
        // so nothing can hit the player and the survival window is the same whatever fires on its own.
        internal static int PlayerFires(System.Action<CombatSystem> configure = null)
        {
            var w = new M6World(Recipe());
            configure?.Invoke(w.Combat);
            w.Session.SetPlayerInput(WindowFirst, Aim(true, 10000, 30000));
            w.Session.SetPlayerInput(WindowLast + 1, Aim(false, 10000, 30000));
            int shots = 0;
            for (int tick = 1; tick <= PlayerTicks; tick++) { w.Session.StepOnce(); shots += Shots(w); }
            return shots;
        }

        // One armed, uncontrolled unit with no enemy anywhere; on ticks 30..89 it is given an explicit aimed intent at a fixed point.
        // Returns the shots inside the window and (out) the shots over the whole run.
        internal static int IntentFires(out int total, System.Action<CombatSystem> configure = null)
        {
            var w = new Wp13TestWorld(new[] { M5TestWorld.Spawn(1, 0) }, controlled: false);
            configure?.Invoke(w.Combat);
            w.Produce = c =>
            {
                if (c.Clock.Tick >= WindowFirst && c.Clock.Tick <= WindowLast) w.Aim(c, 0, SimVec3.Metres(0, 0, 10));
            };
            int inside = 0, all = 0;
            w.After = c =>
            {
                all += w.Combat.Fires.Length;
                if (c.Clock.Tick >= WindowFirst && c.Clock.Tick <= WindowLast) inside += w.Combat.Fires.Length;
            };
            for (int tick = 1; tick <= IntentTicks; tick++) w.Step();
            total = all;
            return inside;
        }

        // Two hostile armed shooters, 10 m apart on open ground, no spread, no melee, no player, no intents, damage always zero.
        // Weapon: one shot every 3 ticks, a magazine that outlasts the run, so there are cooldown ticks to observe.
        internal static Wp13TestWorld Pair(bool controlled = false, IHitResolver resolver = null, int secondWeapon = 0)
        {
            var weapon = new WeaponDef(3000, 100, 9000, default, SimFix.FromInt(67), 0);
            var units = new[] { M5TestWorld.Spawn(1, 0, weapon: 0), M5TestWorld.Spawn(2, 10000, team: 2, weapon: secondWeapon, squad: 2) };
            return new Wp13TestWorld(units, M5TestWorld.Pack(resolver ?? new ZeroResolver(), weapons: new[] { weapon }), controlled: controlled);
        }
    }

    // Forwards to another implementation and writes down every call. TryAim checks the cooldown guard on every call.
    internal sealed class RecordingAutoFire : IAutoFire
    {
        private readonly IAutoFire inner;
        internal readonly Dictionary<StableId, int> SensesThisTick = new Dictionary<StableId, int>();
        internal readonly Dictionary<StableId, int> SensesTotal = new Dictionary<StableId, int>();
        internal int Senses, Aims, CooldownSenses;
        internal RecordingAutoFire(IAutoFire inner) { this.inner = inner; }

        internal void NewTick() => SensesThisTick.Clear();
        internal int ThisTick(StableId id) => SensesThisTick.TryGetValue(id, out int n) ? n : 0;
        internal int Total(StableId id) => SensesTotal.TryGetValue(id, out int n) ? n : 0;

        public void Sense(CombatSystem combat, EntityId shooter, in Combatant unit, in Position position, long tick)
        {
            StableId id = combat.Entities.StableOf(shooter);
            SensesThisTick[id] = ThisTick(id) + 1; SensesTotal[id] = Total(id) + 1;
            Senses++;
            if (unit.Weapon >= 0 && unit.DueMilli > tick * 1000) CooldownSenses++;
            inner.Sense(combat, shooter, in unit, in position, tick);
        }

        public AutoFireAction TryAim(CombatSystem combat, EntityId shooter, in Combatant unit, in Position position,
            in WeaponDef weapon, long tick, out SimVec3 direction)
        {
            Aims++;
            Assert.That(unit.DueMilli, Is.LessThanOrEqualTo(tick * 1000), "TryAim is only asked once the weapon is ready");
            return inner.TryAim(combat, shooter, in unit, in position, in weapon, tick, out direction);
        }
    }

    // Never fires and never senses.
    internal sealed class SilentAutoFire : IAutoFire
    {
        internal int Aims;
        public void Sense(CombatSystem combat, EntityId shooter, in Combatant unit, in Position position, long tick) { }
        public AutoFireAction TryAim(CombatSystem combat, EntityId shooter, in Combatant unit, in Position position,
            in WeaponDef weapon, long tick, out SimVec3 direction)
        { Aims++; direction = default; return AutoFireAction.Pass; }
    }

    // Fires every time it is asked, toward +X, or holds fire every time. For checking who may be asked at all.
    internal sealed class ConstantAutoFire : IAutoFire
    {
        private readonly AutoFireAction action;
        internal readonly List<StableId> Offered = new List<StableId>();
        internal ConstantAutoFire(AutoFireAction action) { this.action = action; }
        public void Sense(CombatSystem combat, EntityId shooter, in Combatant unit, in Position position, long tick) { }
        public AutoFireAction TryAim(CombatSystem combat, EntityId shooter, in Combatant unit, in Position position,
            in WeaponDef weapon, long tick, out SimVec3 direction)
        { Offered.Add(combat.Entities.StableOf(shooter)); direction = new SimVec3(SimFix.FromInt(1), default, default); return action; }
    }
}
