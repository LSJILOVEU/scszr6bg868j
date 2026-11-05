using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // The crowd pass rule in a one-metre corridor, where a blocker cannot be walked around.
    public sealed class Wp21PassTests
    {
        private static byte[] Corridor() => WcMapWriter.Write(M3TestWorld.Boxes(80, 40, (10, 0, 10, 20), (10, 21, 10, 19)));
        private static UnitSpawn S(ulong key, int team, int squad, double x) =>
            new UnitSpawn(key, 0, squad, (long)(x * 1000), 20500, 6000, team, -1);
        private static UnitSpawn Armed(ulong key, int team, double x) => new UnitSpawn(key, 0, team, (long)(x * 1000), 20500, 6000, team, 0);
        private static M6World World(UnitSpawn[] units, CrowdPassKind kind = CrowdPassKind.TeamIdle, ulong? player = null, CombatPack combat = null)
        {
            var recipe = M6World.Endless(M6World.RecipeOf(units, combat: combat)).WithCrowdPass(kind);
            if (player.HasValue) recipe = recipe.WithPlayer(player.Value);
            return new M6World(recipe, Corridor());
        }
        private static SimVec3 At(double x) => new SimVec3(SimFix.Ratio((long)(x * 1000), 1000), default, SimFix.Ratio(20500, 1000));
        private static SimVec3 Run(M6World w, ulong mover, double to, int ticks = 600)
        {
            w.System<InterimLocomotion>().Order(w.Unit(mover), At(to));
            w.Step(ticks);
            return w.Read<Position>(mover).Value;
        }
        private static bool Reached(SimVec3 p, double to) => CombatSystem.Horizontal(p - At(to)).LengthSquared.Sign == 0;
        private static bool Blocked(SimVec3 p, double blockerX) => p.X.Raw < SimFix.Ratio((long)(blockerX * 1000), 1000).Raw;

        [Test]
        public void ASameTeamMoverPassesAnIdleFriendAndTheFriendDoesNotMove()
        {
            var w = World(new[] { S(1, 1, 1, 5), S(2, 1, 1, 15) });
            var idleBefore = w.Read<Position>(2).Value;
            Assert.That(Reached(Run(w, 1, 30), 30), Is.True);
            Assert.That(w.Read<Position>(2).Value, Is.EqualTo(idleBefore));
        }
        [Test]
        public void WithoutTheRuleTheFriendBlocksTheCorridor()
        {
            var w = World(new[] { S(1, 1, 1, 5), S(2, 1, 1, 15) }, CrowdPassKind.None);
            Assert.That(Blocked(Run(w, 1, 30), 15), Is.True);
        }
        [Test]
        public void AnEnemyBlocksAndAnActiveFriendDoesNotPassAnotherActiveOne()
        {
            var w = World(new[] { S(1, 1, 1, 5), S(2, 2, 2, 15) });
            Assert.That(Blocked(Run(w, 1, 30), 15), Is.True);
        }
        [Test]
        public void TheSameSquadInAnotherTeamBlocksAndAnotherSquadInTheSameTeamPasses()
        {
            var otherTeam = World(new[] { S(1, 1, 7, 5), S(2, 2, 7, 15) });
            Assert.That(Blocked(Run(otherTeam, 1, 30), 15), Is.True);
            var otherSquad = World(new[] { S(1, 1, 7, 5), S(2, 1, 9, 15) });
            Assert.That(Reached(Run(otherSquad, 1, 30), 30), Is.True);
        }
        [Test]
        public void OnlyTheSameTeamPassesIncludingAThirdTeamButNeverNeutral()
        {
            Assert.That(Reached(Run(World(new[] { S(1, 3, 3, 5), S(2, 3, 3, 15) }), 1, 30), 30), Is.True, "3 passes 3");
            Assert.That(Blocked(Run(World(new[] { S(1, 1, 1, 5), S(2, 3, 3, 15) }), 1, 30), 15), Is.True, "1 does not pass 3");
            Assert.That(Blocked(Run(World(new[] { S(1, 3, 3, 5), S(2, 1, 1, 15) }), 1, 30), 15), Is.True, "3 does not pass 1");
            Assert.That(Blocked(Run(World(new[] { S(1, 0, 0, 5), S(2, 0, 0, 15) }), 1, 30), 15), Is.True, "neutral never passes");
        }
        [Test]
        public void TheControlledPlayerIsNeverPassedAndBusyUnitsKeepBlocking()
        {
            var players = World(new[] { S(1, 1, 1, 5), S(2, 1, 1, 15) }, player: 2);
            Assert.That(Blocked(Run(players, 1, 30), 15), Is.True, "player idle blocks");
            var busyIdle = World(new[] { S(1, 1, 1, 5), S(2, 1, 1, 15) });
            busyIdle.Set(2, new Combatant(1, -1, 0, 100000000));
            Assert.That(Blocked(Run(busyIdle, 1, 30), 15), Is.True, "idle unit with a pending shot blocks");
            var busyMover = World(new[] { S(1, 1, 1, 5), S(2, 1, 1, 15) });
            busyMover.Set(1, new Combatant(1, -1, 0, 100000000));
            Assert.That(Blocked(Run(busyMover, 1, 30), 15), Is.True, "mover with a pending shot is blocked");
            var expired = World(new[] { S(1, 1, 1, 5), S(2, 1, 1, 15) });
            expired.Set(2, new Combatant(1, -1, 0, 1000));
            expired.System<InterimLocomotion>().Order(expired.Unit(1), At(30)); expired.Step(40);
            Assert.That(Blocked(expired.Read<Position>(1).Value, 15), Is.True, "just fired: still engaged");
            expired.Step(560);
            Assert.That(Reached(expired.Read<Position>(1).Value, 30), Is.True, "engagement expires after EngagedTicks");
            var ready = World(new[] { S(1, 1, 1, 5), S(2, 1, 1, 15) });
            ready.Set(2, new Combatant(1, -1, 0, 0));
            Assert.That(Reached(Run(ready, 1, 30), 30), Is.True, "ready idle unit is passable");
        }
        [Test]
        public void AWallStillStopsAPassingMover()
        {
            var w = new M6World(M6World.Endless(M6World.RecipeOf(new[] { S(1, 1, 1, 5), S(2, 1, 1, 14) })).WithCrowdPass(CrowdPassKind.TeamIdle),
                WcMapWriter.Write(M3TestWorld.Boxes(80, 40, (10, 0, 10, 20), (10, 21, 10, 19), (18, 20, 1, 1))));
            Assert.That(Blocked(Run(w, 1, 30), 18), Is.True);
        }
        private static (int zeroRange, int shots, int friendly, string trace) FriendThroughShooter(bool friendlyFire)
        {
            var original = M6World.Guns(new ZeroResolver());
            var combat = new CombatPack(original.Weapons.ToArray(), new ZeroResolver(), original.Selector, original.MaxHealthRaw,
                friendlyFire, original.FriendlyFireLocked, original.AllyCheckMissRaw, original.AllyHoldMilli);
            var units = new[] { Armed(1, 1, 20), S(2, 1, 1, 5), S(3, 2, 2, 8) };
            int zeroRange = 0, shots = 0, friendly = 0; var trace = new System.Text.StringBuilder();
            var w = World(units, combat: combat);
            w.System<InterimLocomotion>().Order(w.Unit(2), At(26));
            for (int tick = 0; tick < 400; tick++)
            {
                w.Step();
                foreach (var f in w.Combat.Fires)
                {
                    if (f.Shooter.Equals(w.Sim.Entities.StableOf(w.Unit(1)))) shots++;
                    if (f.Outcome == FireOutcome.FriendBlocked || f.Outcome == FireOutcome.FriendHit) friendly++;
                    if (f.Distance.Raw == 0 && (f.Outcome == FireOutcome.FriendBlocked || f.Outcome == FireOutcome.FriendHit)) zeroRange++;
                    trace.Append(tick).Append(':').Append((int)f.Outcome).Append(':').Append(f.Distance.Raw).Append(':').Append(f.DamageRaw).Append(';');
                }
            }
            return (zeroRange, shots, friendly, trace.ToString());
        }
        [TestCase(false)] [TestCase(true)]
        public void AFriendWalkingThroughAShooterNeverProducesAZeroRangeFriendlyShotAndReplaysExactly(bool friendlyFire)
        {
            var first = FriendThroughShooter(friendlyFire); var second = FriendThroughShooter(friendlyFire);
            Assert.That(first.zeroRange, Is.Zero); Assert.That(first.shots, Is.GreaterThan(0));
            Assert.That(second.trace, Is.EqualTo(first.trace), "every fire record replays exactly");
            TestContext.Out.WriteLine($"FRIEND_SHOT friendlyFire={friendlyFire} shots={first.shots} friendlyOutcomes={first.friendly}");
        }
        // Ruling for rule-enabled Match worlds: bodies may overlap only where the rule lets one pass the other
        // (same team, idle, not engaged, not the player); enemy bodies never overlap, and nothing stays stacked.
        [Test]
        public void TwoHundredUnitsOverlapOnlyWithinATeamAndNothingStaysStacked()
        {
            var units = new UnitSpawn[200];
            for (int i = 0; i < 200; i++)
            {
                int slot = i % 100, team = i < 100 ? 1 : 2; long x = 10000 + slot % 10 * 750; if (team == 2) x = 80000 - x;
                units[i] = new UnitSpawn((ulong)i + 1, 0, team, x, 30000 + slot / 10 * 1500, 6000, team, -1);
            }
            var point = new ControlPointDef(1, M6World.P(40, 40), SimFix.FromInt(6));
            var pack = M6World.Pack(new[] { point }, ticks: 100000, think: 30, max: 100000, sight: SimFix.FromRaw(1));
            var w = new M6World(M6World.Endless(M6World.RecipeOf(units, pack)), WcMapWriter.Write(M3TestWorld.Boxes(80, 80)));
            const long half = SimFix.Scale * SimFix.Scale / 4; int enemyOverlaps = 0, lastOverlaps = 0;
            for (int tick = 0; tick < 900; tick++)
            {
                w.Step(); lastOverlaps = 0;
                var p = new SimVec3[200];
                for (int i = 0; i < 200; i++) p[i] = w.Read<Position>((ulong)i + 1).Value;
                for (int i = 0; i < 200; i++) for (int j = i + 1; j < 200; j++)
                {
                    long dx = p[i].X.Raw - p[j].X.Raw, dz = p[i].Z.Raw - p[j].Z.Raw;
                    if (dx * dx + dz * dz >= half) continue;
                    lastOverlaps++; if (units[i].Team != units[j].Team) enemyOverlaps++;
                }
            }
            Assert.That(enemyOverlaps, Is.Zero); Assert.That(lastOverlaps, Is.Zero);
        }
        [Test]
        public void SnapshotsRoundTripWithTheRuleAndRejectAMismatchedOne()
        {
            var units = new[] { S(1, 1, 1, 5), S(2, 1, 1, 15) };
            var a = World(units); a.System<InterimLocomotion>().Order(a.Unit(1), At(30)); a.Step(40);
            var snapshot = a.Sim.SaveRetainedSnapshot();
            var fresh = World(units); fresh.Sim.RestoreRetainedSnapshot(snapshot);
            var used = World(units); used.System<InterimLocomotion>().Order(used.Unit(2), At(18)); used.Step(7); used.Sim.RestoreRetainedSnapshot(snapshot);
            for (int i = 0; i < 80; i++) { a.Step(); fresh.Step(); used.Step(); Assert.That(fresh.Hash, Is.EqualTo(a.Hash)); Assert.That(used.Hash, Is.EqualTo(a.Hash)); }
            var reversed = new M6World(M6World.Endless(M6World.RecipeOf(units)).WithCrowdPass(CrowdPassKind.TeamIdle), Corridor(), reverse: true);
            reversed.System<InterimLocomotion>().Order(reversed.Unit(1), At(30)); reversed.Step(40);
            Assert.That(reversed.Sim.SaveRetainedSnapshot(), Is.EqualTo(snapshot), "creation order does not matter");
            var none = World(units, CrowdPassKind.None);
            Assert.Catch<InvalidOperationException>(() => none.Sim.RestoreRetainedSnapshot(snapshot), "v5 into v4");
            var noneSnapshot = none.Sim.SaveRetainedSnapshot();
            var rule = World(units);
            Assert.Catch<InvalidOperationException>(() => rule.Sim.RestoreRetainedSnapshot(noneSnapshot), "v4 into v5");
            var other = World(units);
            typeof(InterimLocomotion).GetField("pass", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(other.System<InterimLocomotion>(), new OtherRule());
            Assert.Catch<InvalidOperationException>(() => other.Sim.RestoreRetainedSnapshot(snapshot), "same version, different digest");
        }
        [Test]
        public void TheRuleDigestIsPartOfTheStateHashAndTheSavedBytes()
        {
            var units = new[] { S(1, 1, 1, 5), S(2, 1, 1, 15) };
            var a = World(units); var b = World(units);
            ulong Digest(M6World w) { var h = new StateHash(); w.System<InterimLocomotion>().WriteState(h); return h.Value; }
            Assert.That(Digest(b), Is.EqualTo(Digest(a)));
            typeof(InterimLocomotion).GetField("pass", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(b.System<InterimLocomotion>(), new OtherRule());
            Assert.That(Digest(b), Is.Not.EqualTo(Digest(a)));
            Assert.That(b.Sim.SaveRetainedSnapshot(), Is.Not.EqualTo(a.Sim.SaveRetainedSnapshot()));
        }
        [Test]
        public void AFriendMidSwingOrWithinTheWindowAfterOneKeepsBlockingAndAnIdleOneDoesNot()
        {
            var content = WarCore.Content.Rwr.RwrPack.Load("rwr-melee");
            int sword = content.MeleeOfUnitType("rwr.swordsman");
            var units = new[] { new UnitSpawn(1, 0, 1, 5000, 20500, 6000, 1, -1, false, sword), new UnitSpawn(2, 0, 1, 15000, 20500, 6000, 1, -1, false, sword) };
            var recipe = M6World.Endless(M6World.RecipeOf(units, combat: content.BuildCombat())).WithCrowdPass(CrowdPassKind.TeamIdle);
            var w = new M6World(recipe, Corridor());
            object rule = typeof(InterimLocomotion).GetField("pass", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(w.System<InterimLocomotion>());
            var may = typeof(ICrowdPass).GetMethod("MayPass");
            Func<bool> pass = () => (bool)may.Invoke(rule, new object[] { w.Unit(1), w.Unit(2) });
            w.Step(100); long now = w.Sim.Clock.Tick;
            Assert.That(pass(), Is.True, "idle swordsmen");
            w.Set(2, new MeleeState(0, startTick: now));
            Assert.That(pass(), Is.False, "mid swing");
            w.Set(2, new MeleeState(0, -1, nextStartTick: now - 10));
            Assert.That(pass(), Is.False, "inside the window after the last swing");
            w.Set(2, new MeleeState(0, -1, nextStartTick: now - 60));
            Assert.That(pass(), Is.True, "window over");
        }
        private sealed class OtherRule : ICrowdPass
        {
            public ulong Digest => 0xABCD;
            public bool MayPass(EntityId mover, EntityId idle) => true;
        }
        [Test]
        public void TheRuleNeedsCrowdAndCombatAndOnlyTheRecipeSelectsIt()
        {
            var units = new[] { S(1, 1, 1, 5) };
            var wrapped = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
                new M6World(M6World.RecipeOf(units).WithCombat(null).WithCrowdPass(CrowdPassKind.TeamIdle), Corridor()));
            Assert.That(wrapped.InnerException, Is.TypeOf<ArgumentException>());
            Assert.That(M6World.RecipeOf(units).WithCrowdPass(CrowdPassKind.None).CrowdPass, Is.EqualTo(CrowdPassKind.None));
            var recipe = M6World.RecipeOf(units);
            Assert.That(recipe.WithPlayer(1).CrowdPass, Is.EqualTo(recipe.CrowdPass));
            Assert.That(recipe.WithRuleSet("x").CrowdPass, Is.EqualTo(recipe.CrowdPass));
            Assert.That(recipe.WithMatch(M6World.Pack()).CrowdPass, Is.EqualTo(recipe.CrowdPass));
            Assert.Throws<ArgumentOutOfRangeException>(() => recipe.WithCrowdPass((CrowdPassKind)9));
        }
    }
}
