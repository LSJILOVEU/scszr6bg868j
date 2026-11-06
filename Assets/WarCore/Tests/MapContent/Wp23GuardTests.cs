using System;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Wp23GuardTests
    {
        private const long S = Wp23.S;
        private static readonly (int, int, int, int)[] Box = { (10, 2, 1, 3) };

        private static WcMap Profile(string name)
        {
            bool heights = name == "height" || name == "both", surfaces = name == "surface" || name == "both";
            return Wp23.Make(16, 8, 0, 6 * S, Box, 4 * S, heights ? (Func<int, int, int>)((i, j) => (int)(i * (S / 16))) : null,
                surfaces ? Wp23.Names : null, surfaces ? (Func<int, int, byte>)((x, z) => (byte)(x < 8 ? 0 : 1)) : null);
        }

        private static UnitSpawn[] Units => new[] { Wp23.Unit(1, 1, 2500, 4500) };

        [TestCase("flat"), TestCase("height"), TestCase("surface"), TestCase("both")]
        public void ProfileMatrix_AcrossMoversAndPack(string profile)
        {
            WcMap map = Profile(profile);
            bool terrain = profile != "flat";
            byte[] bytes = WcMapWriter.Write(map);
            foreach (MoverKind mover in new[] { MoverKind.Interim, MoverKind.InterimPath, MoverKind.InterimCrowd })
            {
                var recipe = new WorldRecipe(41, mover, Units, Array.Empty<BridgeCommand>());
                if (!terrain)
                {
                    Assert.DoesNotThrow(() => WorldBuilder.Create(recipe, bytes), profile + " " + mover);
                    continue;
                }
                if (mover == MoverKind.Interim)
                {
                    Assert.That(Assert.Throws<NotSupportedException>(() => WorldBuilder.Create(recipe.WithTerrainRules(Wp23.Pack()), bytes)).Message,
                        Does.Contain("path or crowd"));
                    continue;
                }
                Assert.Throws<ArgumentException>(() => WorldBuilder.Create(recipe, bytes), profile + " " + mover + " without a pack");
                Assert.DoesNotThrow(() => WorldBuilder.Create(recipe.WithTerrainRules(Wp23.Pack()), bytes), profile + " " + mover);
            }
        }

        // Terrain + PlayerKey stays refused for T1c, before any derivation (so no pack is needed). Combat and match are allowed.
        [TestCase("height"), TestCase("surface"), TestCase("both")]
        public void PlayerOnTerrain_IsRefused_CombatAndMatchAreNot(string profile)
        {
            byte[] bytes = WcMapWriter.Write(Profile(profile));
            var units = new[] { M6World.U(1, 1, 3, 4, weapon: -1, speed: 4000), M6World.U(2, 2, 12, 4, weapon: -1, speed: 4000) };
            WorldRecipe battle = M6World.RecipeOf(units);
            Assert.Throws<ArgumentException>(() => WorldBuilder.Create(battle, bytes), "combat + match still needs the pack");
            Assert.DoesNotThrow(() => WorldBuilder.Create(battle.WithTerrainRules(Wp23.Pack()), bytes));
            Assert.That(Assert.Throws<NotSupportedException>(() => WorldBuilder.Create(battle.WithPlayer(1).WithTerrainRules(Wp23.Pack()), bytes)).Message, Does.Contain("T1c"));
            // Interim / Path with a player stay the older ArgumentException (checked before the map is read).
            var path = new WorldRecipe(41, MoverKind.InterimPath, Units, Array.Empty<BridgeCommand>());
            Assert.Throws<ArgumentException>(() => WorldBuilder.Create(path.WithPlayer(1), bytes));
        }

        [Test]
        public void PathAndCrowdWorlds_RefuseASpeedAboveTheStepCap_AtTheFirstMove()
        {
            byte[] bytes = WcMapWriter.Write(Profile("both"));
            // 7000 mm/s = 15291 raw per tick fits 0.25 m (16384); 8000 mm/s = 17476 does not.
            Assert.That(Wp23.Step(7000), Is.LessThanOrEqualTo(WarCore.Interim.InterimLocomotion.MaxTerrainStepRaw));
            Assert.That(Wp23.Step(8000), Is.GreaterThan(WarCore.Interim.InterimLocomotion.MaxTerrainStepRaw));
            foreach (MoverKind mover in new[] { MoverKind.InterimPath, MoverKind.InterimCrowd })
            {
                var fast = new WorldRecipe(41, mover, new[] { Wp23.Unit(1, 1, 2500, 4500, 8000) }, new[] { BridgeCommand.MoveTo(1, 1, 5500, 4500) }).WithTerrainRules(Wp23.Pack());
                SimSession s = WorldBuilder.Create(fast, bytes);
                Assert.Throws<InvalidOperationException>(() => { for (int i = 0; i < 3; i++) s.StepOnce(); }, mover.ToString());
                var ok = new WorldRecipe(41, mover, new[] { Wp23.Unit(1, 1, 2500, 4500, 7000) }, new[] { BridgeCommand.MoveTo(1, 1, 5500, 4500) }).WithTerrainRules(Wp23.Pack());
                SimSession t = WorldBuilder.Create(ok, bytes);
                Assert.DoesNotThrow(() => { for (int i = 0; i < 30; i++) t.StepOnce(); });
            }
        }

        [Test]
        public void FlatWorldsIgnoreThePack_AndSurfaceOnlyMapsKeepFlatY()
        {
            byte[] flat = WcMapWriter.Write(Profile("flat"));
            var plain = new WorldRecipe(41, MoverKind.InterimCrowd, Units, Array.Empty<BridgeCommand>());
            Assert.That(WorldBuilder.Create(plain.WithTerrainRules(Wp23.Pack()), flat).StateHash, Is.EqualTo(WorldBuilder.Create(plain, flat).StateHash),
                "a flat map is not derived, so the pack does not touch it");
            SimSession surface = WorldBuilder.Create(plain.WithTerrainRules(Wp23.Pack()), WcMapWriter.Write(Profile("surface")));
            Assert.That(Wp23.At(surface).Y.Raw, Is.EqualTo(0));
        }
    }
}
