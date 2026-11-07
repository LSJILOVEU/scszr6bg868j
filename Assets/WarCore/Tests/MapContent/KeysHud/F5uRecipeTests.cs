using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // F5u (5): MpBlockDemo is MpDemo plus one block spec carried by the player only.
    public sealed class F5uRecipeTests
    {
        private static void SameUnitsButPlayerBlock(WorldRecipe source, WorldRecipe changed, int playerSpec)
        {
            Assert.That(changed.Units.Length, Is.EqualTo(source.Units.Length));
            int differing = 0;
            for (int i = 0; i < source.Units.Length; i++)
            {
                UnitSpawn a = source.Units[i], b = changed.Units[i];
                Assert.That(b.Key, Is.EqualTo(a.Key), $"unit {i}");
                Assert.That(b.Archetype, Is.EqualTo(a.Archetype));
                Assert.That(b.Squad, Is.EqualTo(a.Squad));
                Assert.That(b.Team, Is.EqualTo(a.Team));
                Assert.That(b.Weapon, Is.EqualTo(a.Weapon));
                Assert.That(b.Reserve, Is.EqualTo(a.Reserve));
                Assert.That(b.Melee, Is.EqualTo(a.Melee));
                Assert.That(b.XMm, Is.EqualTo(a.XMm));
                Assert.That(b.ZMm, Is.EqualTo(a.ZMm));
                Assert.That(b.SpeedMmPerSec, Is.EqualTo(a.SpeedMmPerSec));
                Assert.That(a.Block, Is.EqualTo(-1), $"source unit {a.Key}");
                if (a.Key == source.PlayerKey.Value) { Assert.That(b.Block, Is.EqualTo(playerSpec)); differing++; }
                else Assert.That(b.Block, Is.EqualTo(-1), $"unit {a.Key}");
            }
            Assert.That(differing, Is.EqualTo(1));
        }

        private static void SamePackPlusBlock(CombatPack a, CombatPack b)
        {
            Assert.That(b.Weapons.ToArray(), Is.EqualTo(a.Weapons.ToArray()));
            // MpDemo loads its content on every call, so policies are compared by type and key, not by instance.
            Assert.That(b.Resolver.GetType(), Is.EqualTo(a.Resolver.GetType()));
            Assert.That(b.Resolver.PolicyKey, Is.EqualTo(a.Resolver.PolicyKey));
            Assert.That(b.Selector.GetType(), Is.EqualTo(a.Selector.GetType()));
            Assert.That(b.Selector.PolicyKey, Is.EqualTo(a.Selector.PolicyKey));
            Assert.That(b.MaxHealthRaw, Is.EqualTo(a.MaxHealthRaw));
            Assert.That(b.FriendlyFire, Is.EqualTo(a.FriendlyFire));
            Assert.That(b.FriendlyFireLocked, Is.EqualTo(a.FriendlyFireLocked));
            Assert.That(b.AllyCheckMissRaw, Is.EqualTo(a.AllyCheckMissRaw));
            Assert.That(b.AllyHoldMilli, Is.EqualTo(a.AllyHoldMilli));
            Assert.That(b.Melee.ToArray(), Is.EqualTo(a.Melee.ToArray()));
            Assert.That(b.MaxHitsPerAction, Is.EqualTo(a.MaxHitsPerAction));
        }

        private static void SameRecipeFields(WorldRecipe a, WorldRecipe b)
        {
            Assert.That(b.Seed, Is.EqualTo(a.Seed));
            Assert.That(b.Mover, Is.EqualTo(a.Mover));
            Assert.That(b.Match.PolicyDigest, Is.EqualTo(a.Match.PolicyDigest));
            Assert.That(b.PlayerKey, Is.EqualTo(a.PlayerKey));
            Assert.That(b.CrowdPass, Is.EqualTo(a.CrowdPass));
            Assert.That(b.RuleSetId, Is.EqualTo(a.RuleSetId));
            Assert.That(b.TerrainPack, Is.SameAs(a.TerrainPack));
            Assert.That(b.Script.ToArray(), Is.EqualTo(a.Script.ToArray()));
            Assert.That(b.Facing, Is.EqualTo(a.Facing));
            Assert.That(b.Motion, Is.EqualTo(a.Motion));
            Assert.That(b.Standing, Is.EqualTo(a.Standing));
        }

        [Test]
        public void F5uMpBlockDemo_IsMpDemoWithThePlayerBlockOnly()
        {
            WorldRecipe mp = DemoRecipes.MpDemo(), block = DemoRecipes.MpBlockDemo();
            Assert.That(mp.PlayerKey, Is.EqualTo((ulong?)1));
            SameUnitsButPlayerBlock(mp, block, 0);
            SameRecipeFields(mp, block);
            Assert.That(mp.Combat.Block.Length, Is.EqualTo(0));
            Assert.That(block.Combat.Block.Length, Is.EqualTo(1));
            BlockSpec spec = block.Combat.Block[0];
            Assert.That((spec.CosHalfAnglePermille, spec.ReductionPermille, spec.AiRaisePermille), Is.EqualTo((500, 1000, 0)));
            SamePackPlusBlock(mp.Combat, block.Combat);
            Assert.That(block.BlockAi, Is.False);
            Assert.That(block.Facing, Is.False);
        }

        // MpDemo's pack leaves FriendlyFire, FriendlyFireLocked and AllyHoldMilli at their defaults, so the copy is also checked
        // on a pack where every field differs from its default and a block spec already exists.
        [Test]
        public void F5uWithPlayerBlock_CopiesEveryPackFieldOfANonDefaultPack()
        {
            WorldRecipe mp = DemoRecipes.MpDemo();
            CombatPack old = mp.Combat;
            var melee = new MeleeSpec(3, 2, 4, SimFix.Ratio(1500, 1000), 500, SimFix.Ratio(1000, 1000), SimFix.Ratio(20, 1), 2,
                SimFix.Ratio(1200, 1000));
            var custom = new CombatPack(old.Weapons.ToArray(), old.Resolver, old.Selector, old.MaxHealthRaw + 7, true, true, 123, 45000,
                new[] { melee }, new[] { new BlockSpec(100, 200, 300) });
            WorldRecipe source = mp.WithCombat(custom);
            WorldRecipe changed = DemoRecipes.WithPlayerBlock(source, new BlockSpec(500, 1000));
            SameUnitsButPlayerBlock(source, changed, 1);
            SameRecipeFields(source, changed);
            SamePackPlusBlock(custom, changed.Combat);
            Assert.That(changed.Combat.Block.Length, Is.EqualTo(2));
            Assert.That((changed.Combat.Block[0].CosHalfAnglePermille, changed.Combat.Block[0].ReductionPermille, changed.Combat.Block[0].AiRaisePermille),
                Is.EqualTo((100, 200, 300)));
            Assert.That((changed.Combat.Block[1].CosHalfAnglePermille, changed.Combat.Block[1].ReductionPermille), Is.EqualTo((500, 1000)));
            Assert.That(changed.Combat.MaxHitsPerAction, Is.EqualTo(2));
        }
    }
}
