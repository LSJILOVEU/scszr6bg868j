using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Wp13AimedTests
    {
        [TestCase(0, true)]
        [TestCase(200, true)]
        [TestCase(300, false)]
        public void PLACEHOLDER_HorizontalRayCapsuleWidth_NoSpread(long lateralMm, bool hit)
        {
            var spy = new Wp13SelectorSpy();
            var w = new Wp13TestWorld(new[] { M5TestWorld.Spawn(1, 0),
                M5TestWorld.Spawn(2, 10000, lateralMm, team: 2, weapon: -1) }, M5TestWorld.Pack(new ZeroResolver(), spy), spread: 0);
            w.Produce = c => w.Aim(c, 0, SimVec3.Metres(1, 123, 0)); w.Step();
            Assert.That(w.Combat.Fires.Length, Is.EqualTo(1)); var f = w.Combat.Fires[0];
            Assert.That(f.Shooter, Is.EqualTo(w.Id(0))); Assert.That(f.ShooterTeam, Is.EqualTo(1));
            Assert.That(f.Target, Is.EqualTo(hit ? w.Id(1) : default)); Assert.That(f.Origin, Is.EqualTo(w.Point(0)));
            if (hit)
            {
                var hits = new SpatialHit[2];
                var ray = SpatialQueries.Ray(w.Point(0), SimVec3.Metres(1, 0, 0), SimFix.FromInt(67), 1);
                var result = w.Sim.SpatialQuery.AllCapped(ray, hits.Length, hits);
                Assert.That(result.Written, Is.EqualTo(2));
                Assert.That(hits[1].Entity, Is.EqualTo(w.Unit(1)));
                Assert.That(f.End, Is.EqualTo(f.Origin + ray.Direction.Normalized * hits[1].Distance));
                Assert.That(f.End.Y, Is.EqualTo(f.Origin.Y));
                Assert.That(f.End.Z, Is.EqualTo(f.Origin.Z), "including the lateral-offset hit, End stays on the ray");
                Assert.That(f.End.X.Raw, Is.GreaterThan(f.Origin.X.Raw).And.LessThan(w.Point(1).X.Raw));
                Assert.That(f.End, Is.Not.EqualTo(w.Point(1)), "entry point is not the target center");
            }
            else Assert.That(f.End, Is.EqualTo(SimVec3.Metres(67, 0, 0)));
            Assert.That(f.Distance, Is.EqualTo(hit ? CombatSystem.Horizontal(w.Point(1) - w.Point(0)).Length : SimFix.FromInt(67)));
            Assert.That(f.DamageRaw, Is.Zero); Assert.That(w.State(0).Ammo, Is.EqualTo(29)); Assert.That(w.State(0).DueMilli, Is.EqualTo(4390));
            Assert.That(w.EventCount(GameplayEvents.Damage), Is.EqualTo(hit ? 1 : 0));
            Assert.That(spy.Calls, Is.Zero); Assert.That(w.Combat.QueriesThisStep, Is.Zero); Wp13IntentTests.Empty(w);
        }

        [Test]
        public void PLACEHOLDER_ZeroHorizontalDirectionIsNone_EvenWithDifferentHeight()
        {
            var w = new Wp13TestWorld(); var before = w.State(0); ulong rng = Wp13TestWorld.Hash(w.Sim.Rng);
            w.Produce = c => w.Aim(c, 0, w.Point(0) + SimVec3.Metres(0, 50, 0)); w.Step();
            Assert.That(w.Combat.Fires.Length, Is.Zero); Assert.That(w.State(0), Is.EqualTo(before));
            Assert.That(Wp13TestWorld.Hash(w.Sim.Rng), Is.EqualTo(rng)); Assert.That(w.Sim.Events.Read(1).Length, Is.Zero);
        }

        [TestCase(67000, true)]
        [TestCase(67100, true)]
        [TestCase(68000, false)]
        public void PLACEHOLDER_EntranceWithinRangeMayHitBeyondCenterRange_ZeroFalloffStillRollsAndDamages(long centerMm, bool hit)
        {
            var resolver = new RecordingResolver();
            var w = new Wp13TestWorld(M5TestWorld.Pair(distanceMm: centerMm), M5TestWorld.Pack(resolver));
            var rng = ReplayRandom(); var stream = rng.Stream("combat.hit");
            w.Hold(); w.Step(); var f = w.Combat.Fires[0];
            Assert.That(f.Target, Is.EqualTo(hit ? w.Id(1) : default));
            Assert.That(f.Distance, Is.EqualTo(hit ? SimFix.Ratio(centerMm, 1000) : SimFix.FromInt(67)));
            Assert.That(f.End, Is.EqualTo(hit ? w.Point(1) - new SimVec3(SimFix.Ratio(1, 4), default, default) : SimVec3.Metres(67, 0, 0)));
            Assert.That(f.DamageRaw, Is.Zero); Assert.That(w.EventCount(GameplayEvents.Damage), Is.EqualTo(hit ? 1 : 0));
            Assert.That(resolver.Samples.Count, Is.EqualTo(hit ? 1 : 0));
            if (hit)
            {
                Assert.That(resolver.Samples[0].falloff, Is.Zero);
                Assert.That(resolver.Samples[0].roll, Is.EqualTo(rng.NextInt(stream, 65536)));
            }
            Assert.That(Wp13TestWorld.Hash(w.Sim.Rng), Is.EqualTo(Wp13TestWorld.Hash(rng)));
        }

        // WP-17: neutrals still pass through; a teammate on the line no longer does (see FriendOnAimedLine below).
        [TestCase(false)]
        [TestCase(true)]
        public void PLACEHOLDER_NeutralPassesThrough_FirstEnemyWins(bool reverse)
        {
            var w = new Wp13TestWorld(new[] { M5TestWorld.Spawn(1, 0), M5TestWorld.Spawn(2, 1000, 5000, weapon: -1),
                M5TestWorld.Spawn(3, 2000, team: 0, weapon: -1), M5TestWorld.Spawn(4, 3000, team: 2, weapon: -1),
                M5TestWorld.Spawn(5, 4000, team: 2, weapon: -1) }, pack: M5TestWorld.Pack(new LethalResolver()), reverse: reverse);
            w.Produce = c => w.Aim(c, 0, w.Point(4)); w.Step();
            Assert.That(w.Combat.Fires[0].Target, Is.EqualTo(w.Id(3))); Assert.That(w.Dead(3), Is.True);
            Assert.That(w.Dead(1), Is.False); Assert.That(w.Dead(2), Is.False); Assert.That(w.Dead(4), Is.False);
            Assert.That(w.Sim.GameplayResource(w.Id(1), 1), Is.EqualTo(w.Pack.MaxHealthRaw));
        }

        // Friendly fire off: the teammate stops the bullet, nobody is hurt. On: the same shot kills the teammate.
        [TestCase(false)]
        [TestCase(true)]
        public void FriendOnAimedLine_BlocksOrHurtsByTheSwitch(bool friendlyFire)
        {
            var pack = M5TestWorld.Rules(M5TestWorld.Pack(new LethalResolver()), friendlyFire, allyCheckMissRaw: 0);
            var w = new Wp13TestWorld(new[] { M5TestWorld.Spawn(1, 0), M5TestWorld.Spawn(2, 1000, weapon: -1),
                M5TestWorld.Spawn(3, 3000, team: 2, weapon: -1) }, pack: pack);
            w.Produce = c => w.Aim(c, 0, w.Point(2)); w.Step();
            Assert.That(w.Combat.Fires.Length, Is.EqualTo(1)); Assert.That(w.Combat.Fires[0].Target, Is.EqualTo(w.Id(1)));
            Assert.That(w.Combat.Fires[0].Outcome, Is.EqualTo(friendlyFire ? FireOutcome.FriendHit : FireOutcome.FriendBlocked));
            Assert.That(w.Dead(1), Is.EqualTo(friendlyFire)); Assert.That(w.Dead(2), Is.False);
            Assert.That(w.EventCount(GameplayEvents.Damage), Is.EqualTo(friendlyFire ? 1 : 0));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PLACEHOLDER_EqualEntranceDistanceChoosesStableId(bool reverse)
        {
            var w = new Wp13TestWorld(new[] { M5TestWorld.Spawn(1, 0),
                M5TestWorld.Spawn(2, 10000, 125, team: 2, weapon: -1),
                M5TestWorld.Spawn(3, 10000, -125, team: 2, weapon: -1) }, reverse: reverse);
            w.Produce = c => w.Aim(c, 0, SimVec3.Metres(1, 0, 0)); w.Step();
            var hits = new SpatialHit[3]; var result = w.Sim.SpatialQuery.AllCapped(SpatialQueries.Ray(w.Point(0), SimVec3.Metres(1, 0, 0), SimFix.FromInt(67), 1), 3, hits);
            Assert.That(result.Written, Is.EqualTo(3)); Assert.That(hits[1].Distance.Raw, Is.EqualTo(hits[2].Distance.Raw));
            StableId expected = w.Id(1).CompareTo(w.Id(2)) < 0 ? w.Id(1) : w.Id(2);
            Assert.That(w.Combat.Fires[0].Target, Is.EqualTo(expected));
        }

        [TestCase(5, false)]
        [TestCase(5, true)]
        [TestCase(6, false)]
        [TestCase(6, true)]
        [TestCase(7, false)]
        [TestCase(7, true)]
        public void PLACEHOLDER_CapacityBeforeFiltering_SelfFriendNeutralCorpseEnemy_KMinus1_K_KPlus1(int count, bool reverse)
        {
            const int k = 6;
            var units = new UnitSpawn[count];
            units[0] = M5TestWorld.Spawn(1, 0);
            // WP-17: a teammate on the line now stops the bullet, so the filtered-out unit that used to be a friend is a second neutral.
            units[1] = M5TestWorld.Spawn(2, 1000, team: 0, weapon: -1);
            units[2] = M5TestWorld.Spawn(3, 2000, team: 0, weapon: -1);
            units[3] = M5TestWorld.Spawn(4, 3000, team: 2, weapon: -1);
            units[4] = M5TestWorld.Spawn(5, 10000, team: 2, weapon: -1);
            for (int i = 5; i < count; i++) units[i] = M5TestWorld.Spawn(i + 1, i * 1000, team: 0, weapon: -1);
            var w = new Wp13TestWorld(units, capacity: k, reverse: reverse);
            w.After = c => c.Damage(w.Sim.GameplayTarget(w.Id(3)),
                new GameplayApplyKey(w.Id(0), 1, 0, w.Id(0), 1, 0), 1, w.Pack.MaxHealthRaw);
            w.Step(); w.After = null; Assert.That(w.Dead(3), Is.True);
            w.Produce = c => w.Aim(c, 0, w.Point(4));
            if (count > k)
            {
                var error = Assert.Throws<InvalidOperationException>(() => w.Step());
                Assert.That(error.Message, Does.Contain("capacity exceeded"));
                Wp13IntentTests.Empty(w); // finally must also run on a truncated query
                Assert.That((bool)Wp13IntentTests.Field("cacheValid").GetValue(w.Combat), Is.False);
                Assert.That((bool)Wp13IntentTests.Field("inStep").GetValue(w.Combat), Is.False);
            }
            else
            {
                w.Step(); Assert.That(w.Combat.Fires.Length, Is.EqualTo(1));
                Assert.That(w.Combat.Fires[0].Target, Is.EqualTo(w.Id(4))); Wp13IntentTests.Empty(w);
            }
        }

        [TestCase(28000, true, false, false)]
        [TestCase(28000, true, false, true)]
        [TestCase(24250, true, true, false)]
        [TestCase(24250, true, true, true)]
        [TestCase(22000, false, false, false)]
        [TestCase(22000, false, false, true)]
        public void PLACEHOLDER_RealM2Wall_BlockedTieAndEnemyBeforeWall(long enemyMm, bool blocked, bool tie, bool reverse)
        {
            var w = new Wp13TestWorld(new[] { M5TestWorld.Spawn(1, 20000, 2000),
                M5TestWorld.Spawn(2, enemyMm, 2000, team: 2, weapon: -1) }, map: M2DemoMap.Build(), reverse: reverse);
            w.Hold(); w.Step(); var f = w.Combat.Fires[0];
            Assert.That(f.Target, Is.EqualTo(blocked ? default : w.Id(1))); Assert.That(f.Origin, Is.EqualTo(w.Point(0)));
            Assert.That(f.End, Is.EqualTo(blocked ? SimVec3.Metres(24, 0, 2) : w.Point(1) - new SimVec3(SimFix.Ratio(1, 4), default, default)));
            Assert.That(f.Distance, Is.EqualTo(SimFix.FromInt(blocked ? 4 : 2)));
            Assert.That(f.DamageRaw, Is.Zero); Assert.That(w.EventCount(GameplayEvents.Damage), Is.EqualTo(blocked ? 0 : 1));
            Assert.That(w.State(0).Ammo, Is.EqualTo(29)); Assert.That(w.State(0).DueMilli, Is.EqualTo(4390));
            if (tie)
            {
                var ray = SpatialQueries.Ray(w.Point(0), SimVec3.Metres(1, 0, 0), SimFix.FromInt(67), 1);
                var hits = new SpatialHit[2]; w.Sim.SpatialQuery.AllCapped(ray, 2, hits);
                Assert.That(w.Sim.SpatialQuery.Nearest(SpatialQueries.Ray(w.Point(0), ray.Direction, SimFix.FromInt(67), w.WallMask), out SpatialHit wall), Is.True);
                Assert.That(wall.Distance.Raw, Is.EqualTo(hits[1].Distance.Raw), "raw equality fixture");
            }
        }

        [Test]
        public void PLACEHOLDER_NoEnemyButRealWall_UsesWallEndpoint()
        {
            var w = new Wp13TestWorld(new[] { M5TestWorld.Spawn(1, 20000, 2000),
                M5TestWorld.Spawn(2, 20000, 5000, team: 2, weapon: -1) }, map: M2DemoMap.Build());
            ulong rng = Wp13TestWorld.Hash(w.Sim.Rng);
            w.Produce = c => w.Aim(c, 0, SimVec3.Metres(30, 0, 2)); w.Step();
            Assert.That(w.Combat.Fires[0].Target, Is.EqualTo(default(StableId)));
            Assert.That(w.Combat.Fires[0].End, Is.EqualTo(SimVec3.Metres(24, 0, 2)));
            Assert.That(w.Combat.Fires[0].Distance, Is.EqualTo(SimFix.FromInt(4)));
            Assert.That(Wp13TestWorld.Hash(w.Sim.Rng), Is.EqualTo(rng));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PLACEHOLDER_ContinuousM16A4_31ShotsIncludingReload_HitAndMiss(bool hit)
        {
            var w = new Wp13TestWorld(); w.Hold(hit);
            var shots = new List<long>(); var rng = ReplayRandom(); var stream = rng.Stream("combat.hit");
            for (int tick = 1; tick <= 160; tick++)
            {
                w.Step();
                if (w.Combat.Fires.Length != 0)
                {
                    shots.Add(tick); Assert.That(w.Combat.Fires.Length, Is.EqualTo(1));
                    Assert.That(w.Combat.Fires[0].Target, Is.EqualTo(hit ? w.Id(1) : default));
                    Assert.That(w.Combat.Fires[0].DamageRaw, Is.Zero);
                    if (hit) rng.NextInt(stream, 65536);
                }
                Assert.That(w.EventCount(GameplayEvents.Damage), Is.EqualTo(hit ? w.Combat.Fires.Length : 0));
                Assert.That(Wp13TestWorld.Hash(w.Sim.Rng), Is.EqualTo(Wp13TestWorld.Hash(rng)), "one hit roll; zero miss rolls");
            }
            Assert.That(shots.Count, Is.EqualTo(31)); Assert.That(shots[0], Is.EqualTo(1));
            for (int n = 1; n <= 30; n++) Assert.That(shots[n - 1] - 1, Is.EqualTo(((n - 1) * 3390 + 999) / 1000), "shot=" + n);
            Assert.That(shots[29] - 1, Is.EqualTo(99)); Assert.That(shots[30] - 1, Is.EqualTo(159));
            Assert.That(w.State(0).Ammo, Is.EqualTo(29));
        }

        internal static SimRandom ReplayRandom()
        { var rng = new SimRandom(41); rng.Stream("combat.critical"); rng.Stream("combat.hit"); rng.Stream("combat.spread"); rng.Stream("combat.ally-check"); return rng; }
    }
}
