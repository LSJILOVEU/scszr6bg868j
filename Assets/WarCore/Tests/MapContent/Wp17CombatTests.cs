using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // WP-17: ray-based shots, aim spread, friendly-fire switch, AI teammate avoidance. Draw counts follow the
    // consumption table in docs/content-data.md: ally-check -> spread -> ray -> hit.
    public sealed class Wp17CombatTests
    {
        private static readonly SpreadSpec Placeholder = new SpreadSpec(1000, 340, 1400, 3000);

        private static WeaponDef Gun(SpreadSpec spread = default) =>
            new WeaponDef(3390, 30, 60000, SimFix.FromInt(34), SimFix.FromInt(67), 32768, spread);

        private static CombatPack Pack(IHitResolver resolver, SpreadSpec spread = default, bool friendlyFire = false, int missRaw = 0) =>
            M5TestWorld.Rules(M5TestWorld.Pack(resolver, null, new[] { Gun(spread) }), friendlyFire, missRaw);

        private static int Draws(Wp13TestWorld w, string stream)
        {
            // Replay the stream from the seed until the live world's generator state matches.
            for (int n = 0; n <= 5000; n++)
            {
                var rng = Wp13AimedTests.ReplayRandom(); var id = rng.Stream(stream);
                for (int i = 0; i < n; i++) rng.NextInt(id, 65536);
                if (Wp13TestWorld.Hash(rng) == Wp13TestWorld.Hash(w.Sim.Rng)) return n;
            }
            return -1;
        }

        // Replays only the named streams, in any order; used to prove the other streams did not move.
        private static ulong Replayed(int hit, int spread, int ally)
        {
            var rng = Wp13AimedTests.ReplayRandom();
            var h = rng.Stream("combat.hit"); var s = rng.Stream("combat.spread"); var a = rng.Stream("combat.ally-check");
            for (int i = 0; i < hit; i++) rng.NextInt(h, 65536);
            for (int i = 0; i < spread; i++) rng.NextInt(s, 65536);
            for (int i = 0; i < ally; i++) rng.NextInt(a, 65536);
            return Wp13TestWorld.Hash(rng);
        }

        // ---- spread recurrence (pure integer vectors; one second = 30000 thousandths of a tick) ----

        [Test]
        public void SpreadAngleVectors_FirstShotBurstCapStopFireAndReload()
        {
            var s = Placeholder;
            Assert.That(s.AngleAt(0, 0, 3000), Is.EqualTo(1000), "first shot uses the base angle");
            Assert.That(s.StoredAfter(1000), Is.EqualTo(1340));
            // Next shot one fire interval later: 1400 * 3390 / 30000 = 158 recovered (integer division).
            Assert.That(s.AngleAt(1340, 3000, 3000 + 3390), Is.EqualTo(1182));
            Assert.That(s.StoredAfter(2900), Is.EqualTo(3000), "capped at max");
            Assert.That(s.AngleAt(3000, 0, 0), Is.EqualTo(3000), "no time passed: stays at the cap");
            Assert.That(s.AngleAt(3000, 0, 30000), Is.EqualTo(1600), "one second without firing recovers 1.4 degrees");
            Assert.That(s.AngleAt(3000, 0, 90000), Is.EqualTo(1000), "recovery stops at the base angle");
            Assert.That(s.AngleAt(3000, 0, 60000), Is.EqualTo(1000), "a reload (2 s) recovers 2.8 degrees, but never below the base angle");
            Assert.That(new SpreadSpec(100, 340, 1400, 3000).AngleAt(3000, 0, 60000), Is.EqualTo(200), "same reload with a low base: 3000 - 2800");
            Assert.That(new SpreadSpec(0, 0, 0, 0).IsNone, Is.True);
            Assert.That(new SpreadSpec(0, 0, 0, 0).AngleAt(5, 0, 99999), Is.Zero);
        }

        [Test]
        public void SpreadSpecRejectsInconsistentValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpreadSpec(2000, 0, 0, 1000));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpreadSpec(-1, 0, 0, 1000));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpreadSpec(0, 0, 0, SpreadSpec.MaxLegalMilliDegrees + 1));
        }

        [Test]
        public void SpreadShotsStayInsideTheAngleAndBothSidesAreUsed()
        {
            var w = new Wp13TestWorld(new[] { M5TestWorld.Spawn(1, 0), M5TestWorld.Spawn(2, 60000, team: 2, weapon: -1) },
                Pack(new ZeroResolver(), Placeholder));
            w.Hold(); long stored = 0, last = 0; int left = 0, right = 0, shots = 0, hits = 0;
            for (int tick = 1; tick <= 140; tick++)
            {
                w.Step();
                foreach (var f in w.Combat.Fires)
                {
                    int angle = Placeholder.AngleAt((int)stored, last, tick * 1000L);
                    double deg = Math.Atan2((f.End - f.Origin).Z.Raw, (f.End - f.Origin).X.Raw) * 180.0 / Math.PI;
                    Assert.That(Math.Abs(deg), Is.LessThanOrEqualTo(angle / 1000.0 + 0.01), "tick=" + tick);
                    if (deg > 0.001) left++; if (deg < -0.001) right++; shots++; if (f.Outcome == FireOutcome.Hit) hits++;
                    stored = Placeholder.StoredAfter(angle); last = tick * 1000L;
                    Assert.That(w.State(0).SpreadStored, Is.EqualTo(stored)); Assert.That(w.State(0).SpreadLastMilli, Is.EqualTo(last));
                }
            }
            Assert.That(shots, Is.GreaterThanOrEqualTo(30)); Assert.That(left, Is.GreaterThan(0)); Assert.That(right, Is.GreaterThan(0));
            Assert.That(Wp13TestWorld.Hash(w.Sim.Rng), Is.EqualTo(Replayed(hits, shots, 0)), "one spread draw per shot, one hit draw per struck target");
        }

        // ---- consumption table ----

        [Test]
        public void NoSpreadWeaponDrawsNoSpreadNumberAndEnemyHitDrawsOneHitNumber()
        {
            var w = new Wp13TestWorld(new[] { M5TestWorld.Spawn(1, 0), M5TestWorld.Spawn(2, 10000, team: 2, weapon: -1) }, Pack(new LethalResolver()));
            w.Hold(); w.Step();
            Assert.That(w.Combat.Fires.Length, Is.EqualTo(1)); Assert.That(w.Combat.Fires[0].Outcome, Is.EqualTo(FireOutcome.Hit));
            Assert.That(w.EventCount(GameplayEvents.Damage), Is.EqualTo(1));
            Assert.That(Wp13TestWorld.Hash(w.Sim.Rng), Is.EqualTo(Replayed(1, 0, 0)));
        }

        [Test]
        public void SpreadWeaponDrawsOneSpreadNumberPerShotIncludingShotsThatHitNothing()
        {
            var w = new Wp13TestWorld(new[] { M5TestWorld.Spawn(1, 0), M5TestWorld.Spawn(2, 10000, 20000, team: 2, weapon: -1) }, Pack(new ZeroResolver(), Placeholder));
            w.Produce = c => w.Aim(c, 0, SimVec3.Metres(1, 0, 0)); w.Step();
            Assert.That(w.Combat.Fires[0].Outcome, Is.EqualTo(FireOutcome.Open)); Assert.That(w.EventCount(GameplayEvents.Damage), Is.Zero);
            Assert.That(w.State(0).Ammo, Is.EqualTo(29));
            Assert.That(Wp13TestWorld.Hash(w.Sim.Rng), Is.EqualTo(Replayed(0, 1, 0)));
        }

        [Test]
        public void WallFirstIsAShotWithNoHitNumberAndNoDamage()
        {
            var w = new Wp13TestWorld(new[] { M5TestWorld.Spawn(1, 20000, 2000), M5TestWorld.Spawn(2, 28000, 2000, team: 2, weapon: -1) },
                Pack(new LethalResolver(), Placeholder), map: M2DemoMap.Build());
            w.Produce = c => w.Aim(c, 0, w.Point(1)); w.Step();
            Assert.That(w.Combat.Fires[0].Outcome, Is.EqualTo(FireOutcome.WallBlocked)); Assert.That(w.Combat.Fires[0].DamageRaw, Is.Zero);
            Assert.That(w.EventCount(GameplayEvents.Damage), Is.Zero); Assert.That(w.Dead(1), Is.False);
            Assert.That(Wp13TestWorld.Hash(w.Sim.Rng), Is.EqualTo(Replayed(0, 1, 0)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FriendFirst_SwitchDecidesDamageAndHitNumber_AllyCheckIsNotDrawnForPlayerShots(bool friendlyFire)
        {
            var w = new Wp13TestWorld(new[] { M5TestWorld.Spawn(1, 0), M5TestWorld.Spawn(2, 3000, weapon: -1), M5TestWorld.Spawn(3, 9000, team: 2, weapon: -1) },
                Pack(new LethalResolver(), friendlyFire: friendlyFire));
            w.Produce = c => w.Aim(c, 0, w.Point(2)); w.Step();
            Assert.That(w.Combat.Fires[0].Outcome, Is.EqualTo(friendlyFire ? FireOutcome.FriendHit : FireOutcome.FriendBlocked));
            Assert.That(w.EventCount(GameplayEvents.Damage), Is.EqualTo(friendlyFire ? 1 : 0));
            Assert.That(w.State(0).Ammo, Is.EqualTo(29), "a blocked bullet still costs ammo");
            Assert.That(Wp13TestWorld.Hash(w.Sim.Rng), Is.EqualTo(Replayed(friendlyFire ? 1 : 0, 0, 0)));
        }

        [Test]
        public void ZeroDamageHitStillCallsDamageOnceAndDrawsOneHitNumber()
        {
            var w = new Wp13TestWorld(new[] { M5TestWorld.Spawn(1, 0), M5TestWorld.Spawn(2, 10000, team: 2, weapon: -1) }, Pack(new ZeroResolver()));
            w.Hold(); w.Step();
            Assert.That(w.Combat.Fires[0].Outcome, Is.EqualTo(FireOutcome.Hit)); Assert.That(w.Combat.Fires[0].DamageRaw, Is.Zero);
            Assert.That(w.EventCount(GameplayEvents.Damage), Is.EqualTo(1));
            Assert.That(Wp13TestWorld.Hash(w.Sim.Rng), Is.EqualTo(Replayed(1, 0, 0)));
        }

        [TestCase(1, 0)]
        [TestCase(0, 1)]
        [TestCase(1, 1)]
        public void SubMillimetreAimVectorWithSpreadStillFiresARay(int rawX, int rawZ)
        {
            var w = new Wp13TestWorld(new[] { M5TestWorld.Spawn(1, 0), M5TestWorld.Spawn(2, 60000, team: 2, weapon: -1) },
                Pack(new ZeroResolver(), Placeholder));
            w.Produce = c => w.Aim(c, 0, w.Point(0) + new SimVec3(SimFix.FromRaw(rawX), default, SimFix.FromRaw(rawZ)));
            Assert.DoesNotThrow(() => w.Step());
            Assert.That(w.Combat.Fires.Length, Is.EqualTo(1));
            Assert.That(w.Combat.Fires[0].End, Is.Not.EqualTo(w.Combat.Fires[0].Origin));
        }

        [Test]
        public void AutoShotHitsTheFirstUnitOnTheLineNotTheSelectedFarTarget()
        {
            var w = new Wp13TestWorld(new[] { M5TestWorld.Spawn(1, 0), M5TestWorld.Spawn(2, 10000, team: 2, weapon: -1),
                M5TestWorld.Spawn(3, 20000, team: 2, weapon: -1) }, M5TestWorld.Pack(new ZeroResolver(), new FarthestSelector()), controlled: false);
            w.Step();
            Assert.That(w.Combat.Fires[0].Target, Is.EqualTo(w.Id(1)), "the nearer enemy stands in the way");
        }

        // ---- AI teammate avoidance ----

        private static Wp13TestWorld AllyOnLine(int missRaw, bool friendlyFire = false, SpreadSpec spread = default, bool reverse = false) =>
            new Wp13TestWorld(new[] { M5TestWorld.Spawn(1, 0), M5TestWorld.Spawn(2, 3000, weapon: -1),
                M5TestWorld.Spawn(3, 10000, team: 2, weapon: -1) }, Pack(new LethalResolver(), spread, friendlyFire, missRaw), reverse: reverse, controlled: false);

        [Test]
        public void AiHoldsFireWhenATeammateIsOnTheLine_NoAmmoNoRecord_RechecksAfterOneSecond()
        {
            var w = AllyOnLine(missRaw: 0);
            w.Step(); // tick 1
            Assert.That(w.Combat.Fires.Length, Is.Zero); Assert.That(w.State(0).Ammo, Is.EqualTo(30));
            Assert.That(w.State(0).DueMilli, Is.EqualTo(1000 + 30000), "hold lasts 30000 thousandths of a tick = one second, from now");
            Assert.That(w.EventCount(GameplayEvents.Damage), Is.Zero);
            Assert.That(Wp13TestWorld.Hash(w.Sim.Rng), Is.EqualTo(Replayed(0, 0, 1)));
            for (int tick = 2; tick <= 30; tick++) { w.Step(); Assert.That(w.Combat.Fires.Length, Is.Zero, "tick=" + tick); }
            Assert.That(Wp13TestWorld.Hash(w.Sim.Rng), Is.EqualTo(Replayed(0, 0, 1)), "no check while holding");
            w.Step(); // tick 31 checks again
            Assert.That(Wp13TestWorld.Hash(w.Sim.Rng), Is.EqualTo(Replayed(0, 0, 2)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AiWithMissRateOneFiresAnyway_SwitchDecidesTheOutcome_AllyNumberDrawnEitherWay(bool friendlyFire)
        {
            var w = AllyOnLine(missRaw: (int)SimFix.Scale, friendlyFire: friendlyFire);
            w.Step();
            Assert.That(w.Combat.Fires.Length, Is.EqualTo(1));
            Assert.That(w.Combat.Fires[0].Outcome, Is.EqualTo(friendlyFire ? FireOutcome.FriendHit : FireOutcome.FriendBlocked));
            Assert.That(w.Dead(1), Is.EqualTo(friendlyFire)); Assert.That(w.Dead(2), Is.False);
            Assert.That(Wp13TestWorld.Hash(w.Sim.Rng), Is.EqualTo(Replayed(friendlyFire ? 1 : 0, 0, 1)), "the switch does not decide whether the check is drawn");
        }

        [Test]
        public void AiNeverDrawsTheAllyNumberWhenNoTeammateIsOnTheLine()
        {
            var w = new Wp13TestWorld(new[] { M5TestWorld.Spawn(1, 0), M5TestWorld.Spawn(2, 3000, 4000, weapon: -1),
                M5TestWorld.Spawn(3, 10000, team: 2, weapon: -1) }, Pack(new LethalResolver(), missRaw: 0), controlled: false);
            for (int tick = 1; tick <= 20; tick++) w.Step();
            Assert.That(Wp13TestWorld.Hash(w.Sim.Rng), Is.EqualTo(Replayed(Draws(w, "combat.hit"), 0, 0)));
            Assert.That(w.State(0).Ammo, Is.LessThan(30));
        }

        // The real decision rule on one fixed draw sequence: each of the first 100 checks fires exactly when its draw is
        // below the threshold, and more tolerance never means fewer shots.
        [Test]
        public void MissRateThresholdIsMonotoneOnAFixedDrawSequence_EndpointsAreExact()
        {
            const int Checks = 100;
            var rng = Wp13AimedTests.ReplayRandom(); var stream = rng.Stream("combat.ally-check");
            var draws = new int[Checks]; for (int i = 0; i < Checks; i++) draws[i] = rng.NextInt(stream, 65536);
            int previous = -1;
            foreach (int raw in new[] { 0, 16384, 32768, 49152, 65536 })
            {
                var w = AllyOnLine(missRaw: raw); int seen = 0, fired = 0;
                for (int tick = 1; seen < Checks && tick <= 20000; tick++)
                {
                    w.Step();
                    bool fire = w.Combat.Fires.Length > 0;
                    if (!fire && w.State(0).DueMilli != tick * 1000L + 30000) continue; // neither a shot nor a hold: no check this tick
                    Assert.That(fire, Is.EqualTo(draws[seen] < raw), "raw=" + raw + " check=" + seen);
                    seen++; if (fire) fired++;
                }
                Assert.That(seen, Is.EqualTo(Checks), "raw=" + raw);
                Assert.That(fired, Is.GreaterThanOrEqualTo(previous)); previous = fired;
                if (raw == 0) Assert.That(fired, Is.Zero);
                if (raw == 65536) Assert.That(fired, Is.EqualTo(Checks));
            }
        }

        // A neutral (team 0) shooter has no enemies and no teammates: its aimed shot passes through every unit.
        [Test]
        public void NeutralShooterAimedWithoutSpreadHitsNobody()
        {
            var w = new Wp13TestWorld(new[] { M5TestWorld.Spawn(1, 0, team: 0), M5TestWorld.Spawn(2, 10000, team: 2, weapon: -1) },
                Pack(new LethalResolver()));
            w.Hold(); w.Step();
            Assert.That(w.Combat.Fires[0].Outcome, Is.EqualTo(FireOutcome.Open));
            Assert.That(w.Dead(1), Is.False);
        }

        // ---- the lock: one place where a requested value meets the pack ----

        private static ContentPack Locked(bool value, bool locked) => ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
        { ["lk"] = "{ \"id\": \"lk\", \"extends\": \"rwr\", \"combat\": { \"friendlyFire\": " + (value ? "true" : "false") + ", \"friendlyFireLocked\": " + (locked ? "true" : "false") + " } }" },
            FileContentSource.Rwr()), "lk");

        [TestCase(false, true, null, false)]
        [TestCase(false, true, true, false)]
        [TestCase(false, true, false, false)]
        [TestCase(true, true, null, true)]
        [TestCase(true, true, true, true)]
        [TestCase(true, true, false, true)]
        [TestCase(false, false, null, false)]
        [TestCase(false, false, true, true)]
        [TestCase(true, false, false, false)]
        [TestCase(true, false, null, true)]
        public void FriendlyFireRequestIsIgnoredWhenLocked_OtherwiseWins(bool pack, bool locked, bool? request, bool effective)
        {
            var combat = Locked(pack, locked).BuildCombat(request);
            Assert.That(combat.FriendlyFire, Is.EqualTo(effective)); Assert.That(combat.FriendlyFireLocked, Is.EqualTo(locked));
        }

        [Test]
        public void FriendlyFireAndRatesAreInThePolicyDigestAndRwrDefaultsAreOffAndQuarter()
        {
            var rwr = RwrPack.Create();
            Assert.That(rwr.FriendlyFire, Is.False); Assert.That(rwr.FriendlyFireLocked, Is.False);
            Assert.That(rwr.AllyCheckMissRaw, Is.EqualTo(16384)); Assert.That(rwr.AllyHoldMilli, Is.EqualTo(30000));
            Assert.That(Locked(false, false).BuildCombat(true).PolicyDigest, Is.Not.EqualTo(rwr.PolicyDigest));
            Assert.That(RwrPack.Create().Weapons[0].Spread, Is.EqualTo(Placeholder));
        }

        private static ContentException Bad(string body) => Assert.Throws<ContentException>(() => ContentLoader.Load(new MemoryContentSource(
            new Dictionary<string, string> { ["bad"] = "{ \"id\": \"bad\", \"extends\": \"rwr\", " + body + " }" }, FileContentSource.Rwr()), "bad"));

        [Test]
        public void BadCombatAndSpreadValuesNameTheField()
        {
            Assert.That(Bad("\"combat\": { \"allyCheckMissRate\": 1.5 }").Message, Does.Contain("allyCheckMissRate").And.Contain("between 0 and 1"));
            Assert.That(Bad("\"combat\": { \"allyHoldMilli\": 10 }").Message, Does.Contain("allyHoldMilli"));
            Assert.That(Bad("\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"spreadBase\": 5000 } ]").Message, Does.Contain("spreadMax").And.Contain("at least spreadBase"));
            Assert.That(Bad("\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"spreadGrowth\": 99999 } ]").Message, Does.Contain("spreadGrowth"));
            Assert.That(Bad("\"weapons\": [ { \"id\": \"n.gun\", \"fireIntervalMilli\": 3000, \"magazine\": 3, \"reloadMilli\": 9000, \"falloffStart\": 1, \"falloffEnd\": 2, \"effectRaw\": 0 } ]").Message,
                Does.Contain("missing field 'spreadBase'").And.Contain("n.gun"));
        }

        [Test]
        public void ExplicitZeroSpreadInAChildLayerSwitchesTheSpreadOffForThatWeapon()
        {
            var pack = ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string> { ["flat"] = "{ \"id\": \"flat\", \"extends\": \"rwr\", \"weapons\": [ { \"id\": \"rwr.weapon-1\", " +
                "\"spreadBase\": 0, \"spreadGrowth\": 0, \"spreadRecoverPerSec\": 0, \"spreadMax\": 0 } ] }" }, FileContentSource.Rwr()), "flat").BuildCombat();
            Assert.That(pack.Weapons[0].Spread.IsNone, Is.True); Assert.That(pack.Weapons[1].Spread.IsNone, Is.False);
        }


        // ---- M6 seed 41, both friendly-fire modes: the match ends before MaxTicks and replays identically every tick ----

        [TestCase(false)]
        [TestCase(true)]
        public void M6Seed41EndsBeforeMaxTicksInBothFriendlyFireModes_RepeatAndReverseEveryTick(bool friendlyFire)
        {
            var content = ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string> { ["ff"] = "{ \"id\": \"ff\", \"extends\": \"rwr-m6\", \"combat\": { \"friendlyFire\": " +
                (friendlyFire ? "true" : "false") + " } }" }, FileContentSource.Rwr()), "ff");
            var recipe = DemoRecipes.M6(content); var map = DemoRecipes.M6MapBytes();
            var a = new M6World(recipe, map); var b = new M6World(recipe, map); var reverse = new M6World(recipe, map, true);
            Assert.That(a.Combat.Fires.Length, Is.Zero);
            int friendHits = 0, friendBlocked = 0, walls = 0;
            while (a.Match.Winner == 0 && a.Sim.Clock.Tick < 9000)
            {
                a.Step(); b.Step(); reverse.Step();
                Assert.That(b.Hash, Is.EqualTo(a.Hash), "repeat tick=" + a.Sim.Clock.Tick);
                Assert.That(reverse.Hash, Is.EqualTo(a.Hash), "reverse tick=" + a.Sim.Clock.Tick);
                foreach (var f in a.Combat.Fires)
                {
                    if (f.Outcome == FireOutcome.FriendHit) friendHits++;
                    if (f.Outcome == FireOutcome.FriendBlocked) friendBlocked++;
                    if (f.Outcome == FireOutcome.WallBlocked) walls++;
                }
            }
            TestContext.Out.WriteLine($"WP17 seed41 friendlyFire={friendlyFire} end={a.Match.EndTick} winner={a.Match.Winner} reason={a.Match.EndReason} friendHit={friendHits} friendBlocked={friendBlocked} wallBlocked={walls}");
            Assert.That(a.Match.Winner, Is.InRange(1, 2)); Assert.That(a.Match.EndTick, Is.GreaterThan(0).And.LessThan(a.Recipe.Match.MaxTicks));
            if (!friendlyFire) Assert.That(friendHits, Is.Zero);
        }


        // The first spread shot in a process builds the static trig table (one 16 KB array, TrigTables type initializer).
        // After that, shots with spread, wall hits, blocked bullets and AI holds allocate nothing.
        [Test]
        public void SpreadShotsAllocateNothingOnceTheTrigTableExists()
        {
            SimMath.Sin(SimFix.Ratio(1, 360)); // pay the one-time table cost outside the measured window
            var w = new Wp13TestWorld(new[] { M5TestWorld.Spawn(1, 0), M5TestWorld.Spawn(2, 3000, weapon: -1),
                M5TestWorld.Spawn(3, 30000, team: 2) }, Pack(new ZeroResolver(), Placeholder, missRaw: 16384), controlled: false);
            for (int tick = 0; tick < 20; tick++) w.Step();
            long before = GC.GetAllocatedBytesForCurrentThread(); int fires = 0;
            for (int tick = 0; tick < 400; tick++) { w.Step(); fires += w.Combat.Fires.Length; }
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
            Assert.That(fires, Is.GreaterThan(0));
        }

        // ---- snapshot: spread state and hold state survive restore into a reverse-created world ----

        [TestCase(20)]
        [TestCase(105)]
        public void SpreadStateSurvivesSnapshotRestoreIntoReverseWorld(int saveTick)
        {
            UnitSpawn[] units = { M5TestWorld.Spawn(1, 0), M5TestWorld.Spawn(2, 40000, team: 2) };
            var pack = Pack(new ZeroResolver(), Placeholder);
            var a = new Wp13TestWorld(units, pack, controlled: false); var b = new Wp13TestWorld(units, pack, reverse: true, controlled: false);
            for (int tick = 1; tick <= saveTick; tick++) a.Step();
            Assert.That(a.State(0).SpreadStored, Is.GreaterThan(0), "fixture must carry a non-zero spread state");
            b.Sim.RestoreRetainedSnapshot(a.Sim.SaveRetainedSnapshot());
            Assert.That(b.State(0), Is.EqualTo(a.State(0))); Assert.That(b.Sim.StateHashRetained(), Is.EqualTo(a.Sim.StateHashRetained()));
            for (int tick = saveTick + 1; tick <= 300; tick++)
            {
                a.Step(); b.Step(); Wp13TestWorld.EqualCombat(a, b);
                Assert.That(b.Sim.StateHashRetained(), Is.EqualTo(a.Sim.StateHashRetained()), "tick=" + tick);
                for (int i = 0; i < a.Combat.Fires.Length; i++) Assert.That(b.Combat.Fires[i], Is.EqualTo(a.Combat.Fires[i]));
            }
        }

        [Test]
        public void HoldStateSurvivesSnapshotRestoreIntoReverseWorld()
        {
            var a = AllyOnLine(0); var b = AllyOnLine(0, reverse: true);
            for (int tick = 1; tick <= 5; tick++) a.Step();
            Assert.That(a.State(0).DueMilli, Is.EqualTo(31000));
            b.Sim.RestoreRetainedSnapshot(a.Sim.SaveRetainedSnapshot());
            Assert.That(b.State(0), Is.EqualTo(a.State(0)));
            for (int tick = 6; tick <= 70; tick++)
            { a.Step(); b.Step(); Wp13TestWorld.EqualCombat(a, b); Assert.That(b.Sim.StateHashRetained(), Is.EqualTo(a.Sim.StateHashRetained()), "tick=" + tick); }
        }

        private sealed class FarthestSelector : ITargetSelector
        {
            public ulong PolicyKey => 0x5750313701UL;
            public bool TrySelect(CombatSystem world, EntityId shooter, SimFix range, out EntityId target)
            {
                target = default; long best = -1;
                foreach (var hit in world.Candidates(shooter, range))
                {
                    if (!world.IsEnemy(shooter, hit.Entity)) continue;
                    long d = hit.Distance.Raw; if (d > best) { best = d; target = hit.Entity; }
                }
                return best >= 0;
            }
        }
    }
}
