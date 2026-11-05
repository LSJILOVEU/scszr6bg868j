using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    internal static class Wp18TestWorld
    {
        // Real builder, real map/capsules, no movement orders or capture win during the test.
        internal static WorldRecipe Recipe(CombatPack pack = null, UnitSpawn[] units = null) =>
            M6World.RecipeOf(units ?? new[] {
                Wp16TestWorld.U(1, weapon: 0, speed: 1),
                Wp16TestWorld.U(2, 20000, team: 2, squad: 2, speed: 1)
            }, Wp16TestWorld.Match(think: int.MaxValue), pack ?? M5TestWorld.Pack(new ZeroResolver())).WithPlayer(1);

        internal static StableId Id(ulong key) => StableEntityKey.Placed(1, key).Id;
        internal static PlayerInput Aim(bool primary = true, long x = 20000, long z = 10000) =>
            new PlayerInput(0, 0, x, z, primary);
        internal static int Shots(M6World world, ulong key = 1)
        {
            int count = 0;
            foreach (var fire in world.Combat.Fires) if (fire.Shooter.Equals(Id(key))) count++;
            return count;
        }
        internal static UnitStatus Status(M6World world, ulong key = 1)
        {
            Assert.That(world.Session.TryUnitStatus(key, out UnitStatus status), Is.True);
            return status;
        }
    }
}
