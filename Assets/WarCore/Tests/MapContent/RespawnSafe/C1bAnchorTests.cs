using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.RespawnWorld;
using static WarCore.Tests.Wp16TestWorld;

namespace WarCore.Tests
{
    // C1b: the respawn anchor skips owned points with a living enemy inside the capture circle. Death at K, switch due at K + D.
    // Body at (20, 20). A = point 1 at (24, 20), B = point 2 at (40, 20), both radius 2 m with the spawn on the centre. Grid 2 x 2,
    // 2 m spacing: cell 0 = anchor + (-1, 0, -1). Enemy 10 stays at (78, 38); keys 11 and 12 are placed by each test.
    public sealed class C1bAnchorTests
    {
        internal const int K = 1, D = 5;
        internal const ulong Reserve = 3, Other = 11, Second = 12;
        internal static readonly SpawnAnchor[] OwnBase = Bases(new SpawnAnchor(1, P(5, 38)));

        internal static ControlPointDef[] AB() => Points(Spawn(1, 24, 20), Spawn(2, 40, 20));
        internal static MatchPack AbPack() => Pack(AB(), OwnBase, D);

        internal static UnitSpawn Mm(ulong key, long xMm, long zMm, int team) => new UnitSpawn(key, 0, team, xMm, zMm, 1, team);

        internal static bool InsideA(M6World w, ulong key) => CaptureSystem.Inside(Point(w, key), w.Recipe.Match.Points[0]);

        // Steps to K + D - 1, runs the check on the fixture, then takes the due step and returns where the reserve went.
        internal static SimVec3 Respawned(M6World w, System.Action<M6World> precondition = null, bool killPlayer = true)
        {
            if (killPlayer) KillAt(w, K, Player);
            w.Step(K + D - 1);
            Assert.That(Controlled(w), Is.EqualTo(Player));
            precondition?.Invoke(w);
            w.Step();
            Assert.That(w.Sim.Clock.Tick, Is.EqualTo(K + D));
            Assert.That(Controlled(w), Is.EqualTo(Reserve), "switched on the due tick");
            Assert.That(Switches(w), Is.EqualTo(1));
            Assert.That(w.Has<Position>(Reserve), Is.True);
            return Point(w, Reserve);
        }

        private static void EnemyInA(M6World w)
        {
            Assert.That(InsideA(w, Other), Is.True); Assert.That(w.Dead(Other), Is.False);
        }

        // (2) The recording's case: an enemy stands in the nearest owned point.
        [Test]
        public void EnemyInNearestPoint_UsesNextOwnedPoint() =>
            Assert.That(Respawned(World(Units(U(Reserve, 0, 0, reserve: true), U(Other, 24, 21, team: 2)), AbPack()), EnemyInA), Is.EqualTo(P(39, 19)));

        [Test]
        public void NoEnemy_NearestPointAsBefore() =>
            Assert.That(Respawned(World(Units(U(Reserve, 0, 0, reserve: true)), AbPack())), Is.EqualTo(P(23, 19)));

        // (3) The circle includes its boundary: exactly 2 m from the centre counts, 1 mm farther does not.
        [Test]
        public void EnemyOnTheBoundary_Contested() =>
            Assert.That(Respawned(World(Units(U(Reserve, 0, 0, reserve: true), U(Other, 24, 22, team: 2)), AbPack()), EnemyInA), Is.EqualTo(P(39, 19)));

        [Test]
        public void EnemyOneMillimetreOutside_NotContested() =>
            Assert.That(Respawned(World(Units(U(Reserve, 0, 0, reserve: true), Mm(Other, 24000, 22001, 2)), AbPack()),
                w => { Assert.That(InsideA(w, Other), Is.False); Assert.That(w.Dead(Other), Is.False); }), Is.EqualTo(P(23, 19)));

        // (3b-a) One enemy and one ally inside A: 1 : 1 gives no advantage under 2 : 1, so Capturing stays 0; A is still contested.
        [Test]
        public void EvenNumbers_CapturingZero_StillContested() =>
            Assert.That(Respawned(World(Units(U(Reserve, 0, 0, reserve: true), U(Other, 24, 21, team: 2), U(Second, 25, 21)), AbPack()), w =>
            {
                EnemyInA(w); Assert.That(InsideA(w, Second), Is.True);
                Assert.That(w.Capture.Points[0].Capturing, Is.Zero);
            }), Is.EqualTo(P(39, 19)));

        // (3b-b) Pause keeps Capturing after the enemy left at K + 2; with nobody inside, A is chosen.
        [Test]
        public void PauseLeftover_CapturingSet_NoEnemy_NotContested()
        {
            var pack = new MatchPack(AB(), OwnBase, 2, 1, false, 8000, InterruptMode.Pause, 1, 1000000, 1000000, 100000, SimFix.FromInt(35),
                2, 2, SimFix.FromInt(2), D);
            var w = World(Units(U(Reserve, 0, 0, reserve: true), U(Other, 24, 21, team: 2)), pack);
            w.Sim.RegisterGameplay("test.respawn-move", new MoveAt(w, K + 2, Other, P(60, 30)), SimSystemPhase.Requests);
            Assert.That(Respawned(w, x =>
            {
                Assert.That(x.Capture.Points[0].Capturing, Is.EqualTo(2)); Assert.That(x.Capture.Points[0].Progress, Is.GreaterThan(0));
                Assert.That(InsideA(x, Other), Is.False);
            }), Is.EqualTo(P(23, 19)));
        }

        // (3c) Height is ignored: an enemy lifted to y = 5 m above A still contests it.
        [Test]
        public void EnemyAboveTheCircle_StillContested()
        {
            var w = World(Units(U(Reserve, 0, 0, reserve: true), U(Other, 60, 30, team: 2)), AbPack());
            w.Sim.RegisterGameplay("test.respawn-move", new MoveAt(w, K + 1, Other, SimVec3.Metres(24, 5, 21)), SimSystemPhase.Requests);
            Assert.That(Respawned(w, x => { Assert.That(Point(x, Other).Y, Is.EqualTo(SimFix.FromInt(5))); EnemyInA(x); }), Is.EqualTo(P(39, 19)));
        }

        // (4) Units that do not contest: a dead enemy (its body keeps a Position), an ally, an enemy reserve, a team 0 unit.
        [Test]
        public void DeadEnemyInPoint_NotContested()
        {
            var w = World(Units(U(Reserve, 0, 0, reserve: true), U(Other, 24, 21, team: 2)), AbPack());
            KillAt(w, K, Player, Other);
            Assert.That(Respawned(w, x =>
            {
                Assert.That(x.Dead(Other), Is.True); Assert.That(x.Has<Position>(Other), Is.True); Assert.That(InsideA(x, Other), Is.True);
            }, killPlayer: false), Is.EqualTo(P(23, 19)));
        }

        [Test]
        public void AllyInPoint_NotContested() =>
            Assert.That(Respawned(World(Units(U(Reserve, 0, 0, reserve: true), U(Other, 24, 21)), AbPack()),
                w => Assert.That(InsideA(w, Other), Is.True)), Is.EqualTo(P(23, 19)));

        [Test]
        public void EnemyReserve_NotContested() =>
            Assert.That(Respawned(World(Units(U(Reserve, 0, 0, reserve: true), U(Other, 24, 21, team: 2, reserve: true)), AbPack()),
                w => Assert.That(w.Has<Position>(Other), Is.False)), Is.EqualTo(P(23, 19)));

        [Test]
        public void TeamZeroUnitInPoint_NotContested() =>
            Assert.That(Respawned(World(Units(U(Reserve, 0, 0, reserve: true), U(Other, 24, 21, team: 0)), AbPack()),
                w => { Assert.That(InsideA(w, Other), Is.True); Assert.That(w.Dead(Other), Is.False); }), Is.EqualTo(P(23, 19)));

        // (5) Every owned spawn point contested: the nearest team base, ties to the lower index; bases are never contested.
        [Test]
        public void AllPointsContested_UsesTheBase() =>
            Assert.That(Respawned(World(Units(U(Reserve, 0, 0, reserve: true), U(Other, 24, 21, team: 2), U(Second, 40, 21, team: 2)), AbPack()),
                w => { EnemyInA(w); Assert.That(CaptureSystem.Inside(Point(w, Second), w.Recipe.Match.Points[1]), Is.True); }), Is.EqualTo(P(4, 37)));

        [Test]
        public void AllPointsContested_EqualDistanceBases_LowerIndexWins() =>
            Assert.That(Respawned(World(Units(U(Reserve, 0, 0, reserve: true), U(Other, 24, 21, team: 2), U(Second, 40, 21, team: 2)),
                Pack(AB(), Bases(new SpawnAnchor(1, P(20, 30)), new SpawnAnchor(1, P(20, 10))), D)), EnemyInA), Is.EqualTo(P(19, 29)));

        // (6) Only A, contested, and no team base: no switch until the enemy leaves at K + 7 (order 19, after respawn).
        [Test]
        public void ContestedAndNoBase_WaitsUntilThePointClears()
        {
            var w = World(Units(U(Reserve, 0, 0, reserve: true), U(Other, 24, 21, team: 2)), Pack(Points(Spawn(1, 24, 20)), Bases(), D));
            w.Sim.RegisterGameplay("test.respawn-move", new MoveAt(w, K + 7, Other, P(60, 30)), SimSystemPhase.Requests);
            KillAt(w, K, Player);
            w.Step(K + D - 1);
            for (long tick = K + D; tick <= K + 7; tick++)
            {
                w.Step();
                Assert.That(w.Sim.Clock.Tick, Is.EqualTo(tick));
                Assert.That(Controlled(w), Is.EqualTo(Player), "tick " + tick); Assert.That(Switches(w), Is.Zero, "tick " + tick);
                Assert.That(w.Has<Position>(Reserve), Is.False, "tick " + tick);
            }
            w.Step();
            Assert.That(w.Sim.Clock.Tick, Is.EqualTo(K + 8));
            Assert.That(Controlled(w), Is.EqualTo(Reserve)); Assert.That(Switches(w), Is.EqualTo(1));
            Assert.That(Point(w, Reserve), Is.EqualTo(P(23, 19)));
        }

        // (6b) The pool stays shared: while respawn waits, reinforcement (order 8) takes the reserve at K + 6; no switch follows.
        [Test]
        public void ContestedAndNoBase_ReinforcementTakesTheReserve_NoSwitch()
        {
            var w = World(Units(U(Reserve, 0, 0, reserve: true), U(Other, 24, 21, team: 2)), Pack(Points(Spawn(1, 24, 20)), Bases(), D, interval: K + 6));
            Assert.That(w.Reinforce.NextTick(1), Is.EqualTo(K + 6));
            KillAt(w, K, Player);
            w.Step(K + 6);
            Assert.That(w.Has<Position>(Reserve), Is.True);
            Assert.That(w.Reinforce.Deployed.Length, Is.EqualTo(1));
            Assert.That(w.Reinforce.Deployed[0].Unit, Is.EqualTo(StableEntityKey.Placed(1, Reserve).Id));
            Assert.That(Controlled(w), Is.EqualTo(Player));
            for (long tick = K + 7; tick <= K + 60; tick++)
            {
                w.Step();
                Assert.That(Controlled(w), Is.EqualTo(Player), "tick " + tick); Assert.That(Switches(w), Is.Zero, "tick " + tick);
            }
            Assert.That(w.Sim.Clock.Tick, Is.EqualTo(K + 60));
        }

        // (7) 1 x 1 grid. An ally blocks A's only cell, so the due tick fails (C1: no jump to B). The enemy enters A at K + 6
        // (order 19, after respawn); at K + 7 the anchor is chosen again and B is used.
        [Test]
        public void BlockedRetry_PointBecomesContested_SwitchesToTheNextPoint()
        {
            var w = World(Units(U(Reserve, 0, 0, reserve: true), U(Second, 24, 20), U(Other, 60, 30, team: 2)), Pack(AB(), OwnBase, D, columns: 1, rows: 1));
            w.Sim.RegisterGameplay("test.respawn-move", new MoveAt(w, K + 6, Other, P(24, 21)), SimSystemPhase.Requests);
            KillAt(w, K, Player);
            w.Step(K + D - 1);
            for (long tick = K + D; tick <= K + 6; tick++)
            {
                w.Step();
                Assert.That(Controlled(w), Is.EqualTo(Player), "tick " + tick); Assert.That(Switches(w), Is.Zero, "tick " + tick);
                Assert.That(w.Has<Position>(Reserve), Is.False, "tick " + tick);
            }
            EnemyInA(w);
            w.Step();
            Assert.That(w.Sim.Clock.Tick, Is.EqualTo(K + 7));
            Assert.That(Controlled(w), Is.EqualTo(Reserve)); Assert.That(Switches(w), Is.EqualTo(1));
            Assert.That(Point(w, Reserve), Is.EqualTo(P(40, 20)));
        }

        // (8) The player on team 2: team 1 units are the enemy.
        [TestCase(1, 39)]
        [TestCase(2, 23)]
        public void PlayerOnTeamTwo_TeamOneContests(int standerTeam, int x)
        {
            var units = new List<UnitSpawn>
            {
                U(Player, 20, 20, team: 2), U(Ally, 20, 38, team: 2), U(Enemy, 78, 38, team: 1),
                U(Reserve, 0, 0, team: 2, reserve: true), U(Other, 24, 21, team: standerTeam)
            };
            var pack = Pack(Points(Spawn(1, 24, 20, owner: 2), Spawn(2, 40, 20, owner: 2)), Bases(new SpawnAnchor(2, P(5, 38))), D);
            Assert.That(Respawned(World(units, pack), w => Assert.That(InsideA(w, Other), Is.True)), Is.EqualTo(P(x, 19)));
        }

        // (9) The circle is around the centre, not the spawn: A's centre is (24, 20), its spawn (24, 30).
        internal static ControlPointDef[] Offset() =>
            Points(new ControlPointDef(1, P(24, 20), SimFix.FromInt(2), 1, true, P(24, 30)), Spawn(2, 40, 20));

        [Test]
        public void EnemyAtTheCentre_FarFromTheSpawn_Contested() =>
            Assert.That(Respawned(World(Units(U(Reserve, 0, 0, reserve: true), U(Other, 24, 21, team: 2)), Pack(Offset(), OwnBase, D)), EnemyInA),
                Is.EqualTo(P(39, 19)));

        [Test]
        public void EnemyOnTheSpawn_FarFromTheCentre_NotContested() =>
            Assert.That(Respawned(World(Units(U(Reserve, 0, 0, reserve: true), U(Other, 24, 30, team: 2)), Pack(Offset(), OwnBase, D)),
                w => Assert.That(InsideA(w, Other), Is.False)), Is.EqualTo(P(23, 29)));
    }
}
