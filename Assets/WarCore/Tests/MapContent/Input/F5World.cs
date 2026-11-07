using System;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // F5 fixture: seed, units, control points, think interval, sight and every CombatPack argument are literals here.
    // Remaining MatchPack arguments come from the test helper M6World.Pack (test code, not production defaults).
    // Squad 1 = player (key 1) + ally (key 2); squad 2 = leader (key 4); team 2 = one far enemy (key 3).
    internal static class F5World
    {
        internal const ulong Seed = 41;

        private sealed class FixedHit : IHitResolver
        {
            public ulong PolicyKey => 0x4635000000000001;
            public long DamageRaw(in WeaponDef weapon, long falloffRaw, int rollRaw, long maxHealthRaw) => maxHealthRaw / 20;
        }

        private static UnitSpawn U(ulong key, long x, long z, int team, int squad) =>
            new UnitSpawn(key, 0, squad, x, z, 6000, team, -1, false);

        internal static WorldRecipe Recipe(bool standing = false)
        {
            var units = new[] {
                U(1, 10000, 10000, 1, 1), U(2, 12000, 10000, 1, 1),
                U(4, 16000, 12000, 1, 2), U(3, 70000, 35000, 2, 3)
            };
            var match = M6World.Pack(new[] {
                    new ControlPointDef(1, M6World.P(30, 30), SimFix.FromInt(2)),
                    new ControlPointDef(2, M6World.P(60, 30), SimFix.FromInt(2))
                }, ticks: 8000, think: 1000000, interval: 9000, max: 9000, sight: SimFix.FromInt(40));
            var combat = new CombatPack(Array.Empty<WeaponDef>(), new FixedHit(), new NearestVisibleEnemy(), 100 * SimFix.Scale,
                friendlyFire: false, friendlyFireLocked: false, allyCheckMissRaw: 0, allyHoldMilli: 30000, melee: null, block: null);
            WorldRecipe recipe = M6World.RecipeOf(units, match, combat, Seed).WithPlayer(1);
            return standing ? recipe.WithStandingOrders() : recipe;
        }

        internal static M6World World(bool standing = false, bool reverse = false) => new M6World(Recipe(standing), null, reverse);
    }
}
