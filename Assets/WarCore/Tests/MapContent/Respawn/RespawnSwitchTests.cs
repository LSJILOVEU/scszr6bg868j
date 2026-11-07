using NUnit.Framework;
using WarCore.Battle;
using WarCore.Sim;
using static WarCore.Tests.RespawnWorld;
using static WarCore.Tests.Wp16TestWorld;

namespace WarCore.Tests
{
    // Death at tick K is entered at K (damage in Queries, death in the same step's Settle); respawn reads it from tick K + 1 on.
    // Grid 2 x 2 with 2 m spacing: cell 0 sits at anchor + (-1, 0, -1).
    public sealed class RespawnSwitchTests
    {
        private const int K = 1, D = 5;
        private const ulong Reserve = 3;

        [TestCase(false)]
        [TestCase(true)]
        public void SwitchesExactlyDTicksAfterDeath_ToReserveAtCellZero(bool reverse)
        {
            var w = World(Units(U(Reserve, 0, 0, reserve: true)), Pack(Points(Spawn(1, 30, 20)), Bases(new SpawnAnchor(1, P(5, 38))), D), reverse);
            Assert.That(SystemIds(w), Does.Contain("battle.respawn"));
            Assert.That(SystemIds(w), Does.Not.Contain("control.fallback"));
            KillAt(w, K, Player);
            w.Step();
            Assert.That(w.Sim.Clock.Tick, Is.EqualTo(K)); Assert.That(w.Dead(Player), Is.True);
            Assert.That(Entered(w, Player), Is.EqualTo(K));
            for (long tick = K + 1; tick <= K + D - 1; tick++)
            {
                w.Step();
                Assert.That(w.Sim.Clock.Tick, Is.EqualTo(tick));
                Assert.That(Controlled(w), Is.EqualTo(Player), "tick " + tick);
                Assert.That(Switches(w), Is.Zero, "tick " + tick);
                Assert.That(w.Has<Position>(Reserve), Is.False, "tick " + tick);
            }
            w.Step();
            Assert.That(w.Sim.Clock.Tick, Is.EqualTo(K + D));
            Assert.That(Controlled(w), Is.EqualTo(Reserve));
            Assert.That(Switches(w), Is.EqualTo(1));
            var change = SwitchEvent(w);
            Assert.That(change.Source.Id, Is.EqualTo(StableEntityKey.Placed(1, Player).Id));
            Assert.That(change.Target.Id, Is.EqualTo(StableEntityKey.Placed(1, Reserve).Id));
            // Spawn (30, 20) + cell 0 offset (-1, 0, -1).
            Assert.That(Point(w, Reserve), Is.EqualTo(P(29, 19)));
            w.Step(); Assert.That(Switches(w), Is.Zero); Assert.That(Controlled(w), Is.EqualTo(Reserve));
        }

        // Grid 1 x 1: the only cell is the anchor itself. A living ally stands on the nearest anchor until tick K + 8 starts.
        [Test]
        public void BlockedDeploymentRetriesOnlyTheNearestAnchor_AndSwitchesOnlyOnSuccess()
        {
            const ulong blocker = 4;
            var w = World(Units(U(Reserve, 0, 0, reserve: true), U(blocker, 30, 20)),
                Pack(Points(Spawn(1, 30, 20), Spawn(2, 50, 20)), Bases(new SpawnAnchor(1, P(5, 38))), D, columns: 1, rows: 1));
            KillAt(w, K, Player);
            // Registered before the first tick: moves the blocker at tick K + 7 (order 19), after respawn (order 4) already failed there.
            w.Sim.RegisterGameplay("test.respawn-move", new MoveAt(w, K + 7, blocker, P(30, 30)), SimSystemPhase.Requests);
            w.Step(K + D - 1);
            Assert.That(Controlled(w), Is.EqualTo(Player));
            for (long tick = K + D; tick <= K + 7; tick++)
            {
                w.Step();
                Assert.That(w.Sim.Clock.Tick, Is.EqualTo(tick));
                Assert.That(Controlled(w), Is.EqualTo(Player), "tick " + tick);
                Assert.That(Switches(w), Is.Zero, "tick " + tick);
                Assert.That(w.Has<Position>(Reserve), Is.False, "never deployed to the free farther point, tick " + tick);
            }
            Assert.That(Point(w, blocker), Is.EqualTo(P(30, 30)));
            w.Step();
            Assert.That(w.Sim.Clock.Tick, Is.EqualTo(K + 8));
            Assert.That(Controlled(w), Is.EqualTo(Reserve)); Assert.That(Switches(w), Is.EqualTo(1));
            Assert.That(Point(w, Reserve), Is.EqualTo(P(30, 20)));
        }

        // Cell 0 of the 2 x 2 grid, (29, 19), is taken by a living ally; cell 1, (31, 19), is free and is used on the due tick.
        [Test]
        public void CellZeroBlocked_UsesCellOneOnTheDueTick()
        {
            const ulong blocker = 4;
            var w = World(Units(U(Reserve, 0, 0, reserve: true), U(blocker, 29, 19)), Pack(Points(Spawn(1, 30, 20)), Bases(new SpawnAnchor(1, P(5, 38))), D));
            KillAt(w, K, Player);
            w.Step(K + D - 1);
            Assert.That(Controlled(w), Is.EqualTo(Player)); Assert.That(w.Has<Position>(Reserve), Is.False);
            w.Step();
            Assert.That(w.Sim.Clock.Tick, Is.EqualTo(K + D));
            Assert.That(Controlled(w), Is.EqualTo(Reserve)); Assert.That(Switches(w), Is.EqualTo(1));
            Assert.That(Point(w, Reserve), Is.EqualTo(P(31, 19)));
            Assert.That(Point(w, blocker), Is.EqualTo(P(29, 19)));
        }

        private static SimVec3 RespawnAt(ControlPointDef[] points, SpawnAnchor[] bases)
        {
            var w = World(Units(U(Reserve, 0, 0, reserve: true)), Pack(points, bases, D));
            KillAt(w, K, Player);
            w.Step(K + D);
            Assert.That(Controlled(w), Is.EqualTo(Reserve)); Assert.That(Switches(w), Is.EqualTo(1));
            return Point(w, Reserve);
        }

        // The body lies at (20, 20). The lower-index point is the farther one, so "first" and "nearest" disagree.
        [Test]
        public void Anchor_NearestOwnedSpawnPoint_EvenWhenListedSecond() =>
            Assert.That(RespawnAt(Points(Spawn(1, 40, 20), Spawn(2, 10, 20)), Bases(new SpawnAnchor(1, P(5, 38)))), Is.EqualTo(P(9, 19)));

        [Test]
        public void Anchor_EqualDistance_LowerIndexWins() =>
            Assert.That(RespawnAt(Points(Spawn(1, 30, 20), Spawn(2, 10, 20)), Bases(new SpawnAnchor(1, P(5, 38)))), Is.EqualTo(P(29, 19)));

        [Test]
        public void Anchor_NoOwnedPoint_NearestTeamBase_EqualDistance_LowerIndexWins() =>
            Assert.That(RespawnAt(Points(), Bases(new SpawnAnchor(1, P(20, 30)), new SpawnAnchor(1, P(20, 10)), new SpawnAnchor(1, P(5, 38)))),
                Is.EqualTo(P(19, 29)));

        [Test]
        public void Anchor_NearestPointBelongsToEnemy_UsesOwnPoint() =>
            Assert.That(RespawnAt(Points(Spawn(1, 22, 20, owner: 2), Spawn(2, 40, 20)), Bases(new SpawnAnchor(1, P(5, 38)))), Is.EqualTo(P(39, 19)));

        // The owner is read live: the neutral point at (25, 20) is captured by an ally standing in it (3 capture ticks) before the
        // player dies at tick 10, so the respawn uses it, not the point the team owned from the start at (40, 20).
        [Test]
        public void Anchor_UsesAPointCapturedDuringTheMatch()
        {
            const long death = 10;
            const ulong taker = 4;
            var w = World(Units(U(Reserve, 0, 0, reserve: true), U(taker, 26, 21)),
                Pack(Points(Spawn(1, 25, 20, owner: 0), Spawn(2, 40, 20)), Bases(new SpawnAnchor(1, P(5, 38))), D, captureTicks: 3));
            KillAt(w, death, Player);
            w.Step((int)death);
            Assert.That(w.Capture.Points[0].Owner, Is.EqualTo(1), "captured before the death");
            Assert.That(w.Dead(Player), Is.True);
            w.Step(D);
            Assert.That(w.Sim.Clock.Tick, Is.EqualTo(death + D));
            Assert.That(Controlled(w), Is.EqualTo(Reserve)); Assert.That(Switches(w), Is.EqualTo(1));
            Assert.That(Point(w, Reserve), Is.EqualTo(P(24, 19)));
        }

        // A nearer owned point without a spawn (its Spawn field set to (22, 20) all the same) is never an anchor.
        [Test]
        public void Anchor_OwnedPointWithoutSpawnIsSkipped() =>
            Assert.That(RespawnAt(Points(new ControlPointDef(1, P(22, 20), SimFix.FromInt(2), 1, false, P(22, 20)), Spawn(2, 30, 20)),
                Bases(new SpawnAnchor(1, P(5, 38)))), Is.EqualTo(P(29, 19)));

        [Test]
        public void Anchor_NoOwnedPointAndNoBase_NeverSwitches()
        {
            var w = World(Units(U(Reserve, 0, 0, reserve: true)), Pack(Points(), Bases(), D));
            KillAt(w, K, Player);
            w.Step(K + D - 1);
            for (int i = 0; i < 60; i++)
            {
                w.Step();
                Assert.That(Controlled(w), Is.EqualTo(Player)); Assert.That(Switches(w), Is.Zero);
                Assert.That(w.Has<Position>(Reserve), Is.False);
            }
            Assert.That(w.Sim.Clock.Tick, Is.EqualTo(K + D + 59));
        }

        // No reserve on the player's team; the deployed ally stands 30 m from the anchor, where it would not block a deployment.
        [Test]
        public void EmptyPool_NeverSwitches_ToALivingDeployedAlly()
        {
            var w = World(Units(U(Reserve, 0, 0, team: 2, reserve: true)), Pack(Points(Spawn(1, 20, 8)), Bases(new SpawnAnchor(1, P(5, 38))), D));
            KillAt(w, K, Player);
            w.Step();
            for (int i = 0; i < 300; i++)
            {
                Assert.DoesNotThrow(() => w.Step());
                Assert.That(Controlled(w), Is.EqualTo(Player)); Assert.That(Switches(w), Is.Zero);
            }
            Assert.That(Point(w, Ally), Is.EqualTo(P(20, 38)));
            Assert.That(w.Match.Winner, Is.EqualTo(0));
            Assert.That(w.Sim.Clock.Tick, Is.EqualTo(K + 300));
        }

        // Reinforcement and respawn fall due on the same tick: respawn (order 4) takes the first reserve in Capture() order,
        // reinforcement (order 8) the second. Capture() runs in stable-id order; for keys 3 and 4 that is FirstReserve first.
        [TestCase(false)]
        [TestCase(true)]
        public void SameTickAsReinforcement_RespawnTakesFirstReserve(bool reverse)
        {
            const ulong other = 4;
            var w = World(Units(U(Reserve, 0, 0, reserve: true), U(other, 0, 0, reserve: true)),
                Pack(Points(Spawn(1, 30, 20)), Bases(new SpawnAnchor(1, P(5, 38))), D, interval: K + D), reverse);
            Assert.That(w.Reinforce.NextTick(1), Is.EqualTo(K + D));
            KillAt(w, K, Player);
            w.Step(K + D - 1);
            Assert.That(w.Has<Position>(FirstReserve), Is.False); Assert.That(w.Has<Position>(SecondReserve), Is.False);
            w.Step();
            Assert.That(Controlled(w), Is.EqualTo(FirstReserve)); Assert.That(Switches(w), Is.EqualTo(1));
            Assert.That(w.Reinforce.Deployed.Length, Is.EqualTo(1));
            Assert.That(w.Reinforce.Deployed[0].Unit, Is.EqualTo(StableEntityKey.Placed(1, SecondReserve).Id));
            Assert.That(w.Has<Position>(SecondReserve), Is.True);
        }

        // Stable ids: key 3 = 459506500295203436, key 4 = 15958299891356236636.
        internal const ulong FirstReserve = 3, SecondReserve = 4;

        [Test]
        public void NoPlayer_NothingRegistered()
        {
            var units = Units(U(Reserve, 0, 0, reserve: true));
            M6World w = null;
            Assert.DoesNotThrow(() => w = new M6World(Recipe(units, Pack(Points(Spawn(1, 30, 20)), Bases(new SpawnAnchor(1, P(5, 38))), 300), player: false)));
            Assert.That(SystemIds(w), Does.Not.Contain("battle.respawn"));
            Assert.That(SystemIds(w), Does.Not.Contain("control.fallback"));
            Assert.DoesNotThrow(() => w.Step(10));
        }
    }
}
