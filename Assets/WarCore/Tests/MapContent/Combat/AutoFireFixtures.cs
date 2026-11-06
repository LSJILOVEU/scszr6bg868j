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
        internal static Wp13TestWorld Pair()
        {
            var weapon = new WeaponDef(3000, 100, 9000, default, SimFix.FromInt(67), 0);
            var units = new[] { M5TestWorld.Spawn(1, 0, weapon: 0), M5TestWorld.Spawn(2, 10000, team: 2, weapon: 0, squad: 2) };
            return new Wp13TestWorld(units, M5TestWorld.Pack(new ZeroResolver(), weapons: new[] { weapon }), controlled: false);
        }
    }
}
