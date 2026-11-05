using System;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content.Rwr;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class M5TargetTests
    {
        [Test]
        public void C8_PLACEHOLDER_CandidateOverflowIsExplicit()
        {
            var world = new M5TestWorld(new[] { M5TestWorld.Spawn(1, 0), M5TestWorld.Spawn(2, 1000, team: 2, weapon: -1),
                M5TestWorld.Spawn(3, 2000, team: 2, weapon: -1) }, candidateCapacity: 2);
            var error = Assert.Throws<InvalidOperationException>(() => world.Step());
            Assert.That(error.Message, Does.Contain("capacity exceeded"));
        }
        [Test]
        public void C4_C6_PLACEHOLDER_LethalHitSettlesSameTick_DeadShooterNeverFiresAgain()
        {
            var world = new M5TestWorld(M5TestWorld.Pair(2, armedTarget: true));
            world.SetDue(1, 2000); world.Step();
            Assert.That(world.Combat.Fires.Length, Is.EqualTo(1));
            Assert.That(world.Dead(1), Is.True);
            Assert.That(world.Sim.GameplayLife(world.Id(1)).EnteredTick, Is.EqualTo(1));
            world.Step(); Assert.That(world.Combat.Fires.Length, Is.Zero); Assert.That(world.Dead(0), Is.False);
        }
        [Test]
        public void C0_C4_C6_PLACEHOLDER_SameTickMutualKill_IsDraw()
        {
            var world = new M5TestWorld(M5TestWorld.Pair(2, armedTarget: true)); world.Step();
            Assert.That(world.Combat.Fires.Length, Is.EqualTo(2));
            for (int i = 0; i < 2; i++) { Assert.That(world.Dead(i), Is.True); Assert.That(world.Sim.GameplayLife(world.Id(i)).EnteredTick, Is.EqualTo(1)); }
            world.Step(); Assert.That(world.Combat.Fires.Length, Is.Zero);
        }
        [Test]
        public void C4_C6_PLACEHOLDER_FocusFire_AllPacketsSettle_OneDeathNotice()
        {
            var world = new M5TestWorld(new[] { M5TestWorld.Spawn(1, 0, weapon: 2), M5TestWorld.Spawn(2, 1000, 1500, weapon: 2),
                M5TestWorld.Spawn(3, 10000, team: 2, weapon: -1) });
            world.Step(); Assert.That(world.Combat.Fires.Length, Is.EqualTo(2)); Assert.That(world.Dead(2), Is.True);
            int damage = 0, life = 0;
            foreach (var e in world.Sim.Events.Read(1)) { if (e.Type.Equals(GameplayEvents.Damage)) damage++; if (e.Type.Equals(GameplayEvents.Life)) life++; }
            Assert.That(damage, Is.EqualTo(2)); Assert.That(life, Is.EqualTo(1));
        }
        [TestCase(false, 10000, 1)]
        [TestCase(true, 10000, 0)]
        [TestCase(false, 145000, 0)]
        public void C4_PLACEHOLDER_ClearWallOutOfRange(bool wall, long distance, int shots)
        {
            var map = wall ? M3TestWorld.Boxes(160, 8, (5, 0, 1, 8)) : M3TestWorld.Boxes(160, 8);
            var world = new M5TestWorld(new[] { M5TestWorld.Spawn(1, 1000, 1500, weapon: 2),
                M5TestWorld.Spawn(2, 1000 + distance, 1500, team: 2, weapon: -1) }, map: map);
            world.Step(); Assert.That(world.Combat.Fires.Length, Is.EqualTo(shots)); Assert.That(world.Dead(1), Is.EqualTo(shots == 1));
        }
        // WP-17 (was C7: friends were transparent). A teammate on the line now stops the bullet: unharmed, shot recorded as FriendBlocked.
        // The miss rate is 1 so the AI fires anyway; with friendly fire off the teammate and the enemy both keep their health.
        [Test]
        public void C7_PLACEHOLDER_FriendOnRayBlocksBulletAndIsUnharmed()
        {
            var pack = M5TestWorld.Rules(M5TestWorld.NoSpread(RwrPack.Create()), friendlyFire: false, allyCheckMissRaw: (int)SimFix.Scale);
            var world = new M5TestWorld(new[] { M5TestWorld.Spawn(1, 0, weapon: 2), M5TestWorld.Spawn(2, 5000, weapon: -1),
                M5TestWorld.Spawn(3, 10000, team: 2, weapon: -1) }, pack);
            world.Step(); Assert.That(world.Combat.Fires.Length, Is.EqualTo(1)); Assert.That(world.Combat.Fires[0].Target, Is.EqualTo(world.Id(1)));
            Assert.That(world.Combat.Fires[0].Outcome, Is.EqualTo(FireOutcome.FriendBlocked));
            Assert.That(world.Dead(1), Is.False); Assert.That(world.Dead(2), Is.False); Assert.That(world.Dead(0), Is.False);
            Assert.That(world.Sim.GameplayResource(world.Id(1), 1), Is.EqualTo(pack.MaxHealthRaw));
            Assert.That(world.Sim.GameplayResource(world.Id(2), 1), Is.EqualTo(pack.MaxHealthRaw));
        }
        [TestCase(false)]
        [TestCase(true)]
        public void C8_PLACEHOLDER_EqualDistanceUsesStableId(bool reverse)
        {
            var world = new M5TestWorld(new[] { M5TestWorld.Spawn(1, 10000), M5TestWorld.Spawn(2, 0, team: 2, weapon: -1),
                M5TestWorld.Spawn(3, 20000, team: 2, weapon: -1) }, M5TestWorld.Pack(new ZeroResolver()), reverse);
            StableId expected = world.Id(1).CompareTo(world.Id(2)) < 0 ? world.Id(1) : world.Id(2);
            world.Step(); Assert.That(world.Combat.Fires[0].Target, Is.EqualTo(expected));
        }
        [Test]
        public void C8_PLACEHOLDER_NearOccludedFallsBackToFarVisible()
        {
            var world = new M5TestWorld(new[] { M5TestWorld.Spawn(1, 2500, 2500), M5TestWorld.Spawn(2, 6500, 2500, team: 2, weapon: -1),
                M5TestWorld.Spawn(3, 2500, 12500, team: 2, weapon: -1) }, M5TestWorld.Pack(new ZeroResolver()),
                map: M3TestWorld.Boxes(24, 24, (4, 0, 1, 8)));
            world.Step(); Assert.That(world.Combat.Fires[0].Target, Is.EqualTo(world.Id(2)));
        }
        private sealed class Farthest : ITargetSelector
        {
            public ulong PolicyKey => 101;
            public bool TrySelect(CombatSystem world, EntityId shooter, SimFix range, out EntityId target)
            {
                target = default; bool found = false; SimWide farthest = default;
                SimWide limit = new SimVec3(range, default, default).LengthSquared;
                foreach (var hit in world.Candidates(shooter, range))
                {
                    if (!world.IsEnemy(shooter, hit.Entity)) continue;
                    SimWide distance = CombatSystem.Horizontal(world.Point(hit.Entity) - world.Point(shooter)).LengthSquared;
                    if (distance > limit || (found && distance <= farthest) || !world.Visible(shooter, hit.Entity)) continue;
                    target = hit.Entity; farthest = distance; found = true;
                }
                return found;
            }
        }
        [Test]
        public void C8_PLACEHOLDER_CustomSelectorChoosesFarthest()
        {
            var world = new M5TestWorld(new[] { M5TestWorld.Spawn(1, 0), M5TestWorld.Spawn(2, 10000, team: 2, weapon: -1),
                M5TestWorld.Spawn(3, 20000, 3000, team: 2, weapon: -1) }, M5TestWorld.Pack(new ZeroResolver(), new Farthest()));
            world.Step(); Assert.That(world.Combat.Fires[0].Target, Is.EqualTo(world.Id(2)));
        }
        [Test]
        public void C8_PLACEHOLDER_NeutralAndDeadAreExcluded_CoincidentEnemyVisible()
        {
            var world = new M5TestWorld(new[] { M5TestWorld.Spawn(1, 0, weapon: 2), M5TestWorld.Spawn(2, 0, team: 0, weapon: -1),
                M5TestWorld.Spawn(3, 0, team: 2, weapon: -1), M5TestWorld.Spawn(4, 10000, team: 2, weapon: -1) });
            world.Step(); Assert.That(world.Combat.Fires[0].Target, Is.EqualTo(world.Id(2))); Assert.That(world.Dead(1), Is.False);
            for (int i = 0; i < 45; i++) world.Step();
            Assert.That(world.Combat.Fires[0].Target, Is.EqualTo(world.Id(3)));
        }
    }
}
