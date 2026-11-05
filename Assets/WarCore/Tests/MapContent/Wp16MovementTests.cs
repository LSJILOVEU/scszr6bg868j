using NUnit.Framework;
using WarCore.Content;
using WarCore.Control;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Wp16TestWorld;

namespace WarCore.Tests
{
    public sealed class Wp16MovementTests
    {
        [TestCase(0, 1, 0L, 13107L)]
        [TestCase(0, -1, 0L, -13107L)]
        [TestCase(-1, 0, -13107L, 0L)]
        [TestCase(1, 0, 0L, 0L)]
        public void ExactWallContact_AllowsTangentAndExit_BlocksEntry(int x, int z, long dxRaw, long dzRaw)
        {
            var map = M3TestWorld.Boxes(80, 40, (10, 0, 1, 30));
            foreach (bool reverse in new[] { false, true })
            {
                var w = new M6World(Recipe(new[] { U(1, 9750), U(2, 70000, 35000, team: 2) }),
                    WcMapWriter.Write(map), reverse);
                Assert.That(Point(w).X.Raw, Is.EqualTo(638976L));
                Assert.That(w.Read<Collider>(1).Radius.Raw, Is.EqualTo(16384L));
                w.Sim.Step(Input(x, z));
                Assert.That(Point(w).X.Raw, Is.EqualTo(638976L + dxRaw));
                Assert.That(Point(w).Z.Raw, Is.EqualTo(655360L + dzRaw));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CircularFootprint_AllowsLegalCorner_BlocksNextPenetratingStep(bool reverse)
        {
            var map = M3TestWorld.Boxes(80, 40, (10, 10, 1, 1));
            var w = new M6World(Recipe(new[] { U(1, 9600, 9800), U(2, 70000, 35000, team: 2) }),
                WcMapWriter.Write(map), reverse);
            w.Sim.Step(Input(1, 0));
            // (9.8, 9.8), radius 0.25: the circle clears the corner although its square does not.
            Assert.That(Point(w).X.Raw, Is.EqualTo(642252L));
            Assert.That(Point(w).Z.Raw, Is.EqualTo(642252L));
            w.Sim.Step(Input(1, 0));
            Assert.That(Point(w).X.Raw, Is.EqualTo(642252L));
            Assert.That(Point(w).Z.Raw, Is.EqualTo(642252L));
        }

        [TestCase(-1, 0, 16383L, 655360L)]
        [TestCase(1, 0, 5226497L, 655360L)]
        [TestCase(0, -1, 655360L, 16383L)]
        [TestCase(0, 1, 655360L, 2605057L)]
        public void OpenMapEdge_AllowsRadiusMinusOneRaw_RejectsOneRawCloser(int x, int z, long endXRaw, long endZRaw)
        {
            var w = new M6World(Recipe());
            w.Set(1, new Position(new SimVec3(SimFix.FromRaw(endXRaw - x * 13107L), default,
                SimFix.FromRaw(endZRaw - z * 13107L))));
            w.Sim.Step(Input(x, z));
            Assert.That(Point(w).X.Raw, Is.EqualTo(endXRaw));
            Assert.That(Point(w).Z.Raw, Is.EqualTo(endZRaw));
            // Six input raw produces exactly one movement raw at speed 13107.
            w.Sim.Step(new InputFrame(new SimVec3(SimFix.FromRaw(x * 6L), default, SimFix.FromRaw(z * 6L)), default));
            Assert.That(Point(w).X.Raw, Is.EqualTo(endXRaw));
            Assert.That(Point(w).Z.Raw, Is.EqualTo(endZRaw));
        }

        [TestCase(10000L, 655360L)]
        [TestCase(10100L, 661913L)]
        public void InitialFriendlyOverlap_SeparatesEachTick_RejectsCloserSteps(long allyMm, long allyRaw)
        {
            foreach (bool reverse in new[] { false, true })
            {
                var w = new M6World(Recipe(new[] {
                    U(1), U(2, allyMm), U(3, 70000, 35000, team: 2, squad: 2)
                }), reverse: reverse);
                long previousGapRaw = allyRaw - 655360L;
                for (int step = 1; step <= 3; step++)
                {
                    w.Sim.Step(Input(-1, 0));
                    long expectedXRaw = 655360L - step * 13107L;
                    Assert.That(Point(w).X.Raw, Is.EqualTo(expectedXRaw));
                    Assert.That(Point(w).Z.Raw, Is.EqualTo(655360L));
                    Assert.That(Point(w, 2).X.Raw, Is.EqualTo(allyRaw));
                    Assert.That(Point(w, 2).Z.Raw, Is.EqualTo(655360L));
                    long gapRaw = Point(w, 2).X.Raw - Point(w).X.Raw;
                    Assert.That(gapRaw, Is.GreaterThan(previousGapRaw));
                    previousGapRaw = gapRaw;
                    w.Sim.Step(Input(1, 0));
                    Assert.That(Point(w).X.Raw, Is.EqualTo(expectedXRaw), "inward step must not undo separation");
                    Assert.That(Point(w).Z.Raw, Is.EqualTo(655360L));
                }
                Assert.That(previousGapRaw, Is.GreaterThanOrEqualTo(32768L));
            }
        }

        [TestCase(3, 4, 39321L, 52428L)]
        [TestCase(-3, -4, -39321L, -52428L)]
        [TestCase(-3, 4, -39321L, 52428L)]
        [TestCase(3, -4, 39321L, -52428L)]
        public void NormalizeThreeFour_ExactRawVector(int x, int z, long expectedXRaw, long expectedZRaw)
        {
            var direction = SimVec3.Metres(x, 0, z).Normalized;
            Assert.That(direction.X.Raw, Is.EqualTo(expectedXRaw));
            Assert.That(direction.Y.Raw, Is.EqualTo(0L));
            Assert.That(direction.Z.Raw, Is.EqualTo(expectedZRaw));
            Assert.That(direction.LengthSquared <= new SimVec3(SimFix.FromRaw(65536L), default, default).LengthSquared, Is.True);
        }

        [TestCase(196608L, 262144L, 7864L, 10485L)] // 3:4 -> (39321, 52428) normalized raw.
        [TestCase(-196608L, -262144L, -7864L, -10485L)]
        [TestCase(-196608L, 262144L, -7864L, 10485L)]
        [TestCase(196608L, -262144L, 7864L, -10485L)]
        [TestCase(65536L, 65536L, 9267L, 9267L)]
        [TestCase(32768L, 32768L, 6553L, 6553L)] // Length < 1 keeps analog magnitude.
        [TestCase(-32768L, -32768L, -6553L, -6553L)]
        [TestCase(39321L, 52428L, 7864L, 10485L)] // Quantized unit vector must not be boosted.
        public void AnalogDirection_ExactRawDisplacement_AndNoDiagonalSpeedGain(
            long xRaw, long zRaw, long dxRaw, long dzRaw)
        {
            var w = new M6World(Recipe());
            var start = Point(w);
            w.Sim.Step(new InputFrame(new SimVec3(SimFix.FromRaw(xRaw), default, SimFix.FromRaw(zRaw)), default));
            var delta = Point(w) - start;
            Assert.That(delta.X.Raw, Is.EqualTo(dxRaw));
            Assert.That(delta.Y.Raw, Is.EqualTo(0L));
            Assert.That(delta.Z.Raw, Is.EqualTo(dzRaw));
            Assert.That(delta.LengthSquared <= new SimVec3(SimFix.FromRaw(13107L), default, default).LengthSquared, Is.True);
        }

        [Test]
        public void AnalogInput_IgnoresY_ClampsDiagonal_ReleaseStops()
        {
            var w = new M6World(Recipe());
            SimVec3 start = Point(w);
            SimFix speed = w.Read<MoveIntent>(1).SpeedPerTick;
            w.Sim.Step(new InputFrame(new SimVec3(SimFix.Ratio(1, 2), SimFix.FromInt(50), default), default));
            Assert.That(Point(w), Is.EqualTo(start + new SimVec3(speed * SimFix.Ratio(1, 2), default, default)));
            SimVec3 before = Point(w);
            w.Sim.Step(Input(3, 4));
            SimVec3 moved = Point(w) - before;
            Assert.That(moved.X.Raw, Is.GreaterThan(0)); Assert.That(moved.Z.Raw, Is.GreaterThan(0));
            Assert.That(moved.LengthSquared <= new SimVec3(speed, default, default).LengthSquared, Is.True);
            Assert.That(moved.Y.Raw, Is.Zero);
            before = Point(w);
            w.Step(20);
            Assert.That(Point(w), Is.EqualTo(before)); Idle(w, 1);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WallBlocksFullStep_DiagonalSlidesWithoutIncreasingSpeed(bool reverse)
        {
            var map = M3TestWorld.Boxes(80, 40, (10, 0, 1, 30));
            var w = new M6World(Recipe(new[] { U(1, 9700), U(2, 70000, 35000, team: 2) }), WcMapWriter.Write(map), reverse);
            SimVec3 start = Point(w);
            for (int tick = 0; tick < 30; tick++) w.Sim.Step(Input(1, 0));
            Assert.That(Point(w), Is.EqualTo(start));
            SimFix speed = w.Read<MoveIntent>(1).SpeedPerTick;
            SimFix component = (SimVec3.Metres(1, 0, 1).Normalized * speed).Z;
            for (int tick = 0; tick < 60; tick++)
            {
                SimVec3 before = Point(w);
                w.Sim.Step(Input(1, 1));
                Assert.That(Point(w).X, Is.EqualTo(start.X));
                Assert.That(Point(w).Z - before.Z, Is.EqualTo(component), "slide keeps the original Z component");
                Assert.That(M3TestWorld.WallDistance(before, Point(w), map, map.Walls[0]), Is.GreaterThanOrEqualTo(0.25));
            }
        }

        [TestCase(-1, 0, 300, 10000)]
        [TestCase(1, 0, 79700, 10000)]
        [TestCase(0, -1, 10000, 300)]
        [TestCase(0, 1, 10000, 39700)]
        public void PlayerFootprintCannotLeaveMap(int x, int z, long px, long pz)
        {
            var w = new M6World(Recipe(new[] { U(1, px, pz), U(2, 70000, 35000, team: 2) }));
            SimVec3 start = Point(w);
            for (int tick = 0; tick < 10; tick++) w.Sim.Step(Input(x, z));
            Assert.That(Point(w), Is.EqualTo(start));
        }

        [TestCase(-1, 300)]
        [TestCase(1, 79700)]
        public void MapBoundaryWithoutWall_StillBlocksAndSlides(int x, long px)
        {
            // WCMAP requires blocked cells to match walls. An open map edge isolates
            // navigation footprint rejection without constructing an invalid map.
            var map = M3TestWorld.Boxes(80, 40);
            Assert.That(map.Walls.Count, Is.Zero);
            var w = new M6World(Recipe(new[] { U(1, px), U(2, 70000, 35000, team: 2) }), WcMapWriter.Write(map));
            SimVec3 start = Point(w);
            SimFix radius = w.Read<Collider>(1).Radius;
            SimFix speed = w.Read<MoveIntent>(1).SpeedPerTick;
            SimFix component = (SimVec3.Metres(x, 0, 1).Normalized * speed).Z;
            for (int tick = 0; tick < 60; tick++)
            {
                SimVec3 before = Point(w);
                w.Sim.Step(Input(x, 1));
                Assert.That(Point(w).X, Is.EqualTo(start.X));
                Assert.That(Point(w).Z - before.Z, Is.EqualTo(component), "slide keeps the original Z component");
                Assert.That(Point(w).X - radius >= map.Min.X && Point(w).X + radius < map.Max.X, Is.True);
            }
        }

        [Test]
        public void UnitBlocksDiagonal_XSlideHasPriorityOverZ()
        {
            var w = new M6World(Recipe(new[] {
                U(1, speed: 7500), U(2, 10500, 10500), U(3, 70000, 35000, team: 2)
            }));
            SimVec3 before = Point(w);
            w.Sim.Step(Input(1, 1));
            Assert.That(Point(w).X.Raw, Is.GreaterThan(before.X.Raw));
            Assert.That(Point(w).Z, Is.EqualTo(before.Z));
            M4CrowdTests.CheckPairs(M4CrowdTests.Points(w.Session, 3), 1);
        }

        [Test]
        public void AiAndSquadOrdersExcludePlayer_BeforeSlotAssignment()
        {
            var w = new M6World(Recipe(new[] { U(1), U(2, 12000), U(3, 70000, 35000, team: 2, squad: 2) }, match: Match(think: 1)));
            var command = new SquadCommand(SquadCommandKind.MoveTo, 1, SimVec3.Metres(40, 0, 10), default);
            SimFix speed = w.Read<MoveIntent>(1).SpeedPerTick;
            for (int tick = 0; tick < 20; tick++)
            {
                SimVec3 before = Point(w);
                Assert.DoesNotThrow(() => w.Sim.Step(Input(0, 1, command)));
                Assert.That(Point(w), Is.EqualTo(before + new SimVec3(default, default, speed)));
                Idle(w, 1);
            }
            SimVec3 stopped = Point(w);
            w.Sim.Step(Input(0, 0, command));
            Assert.That(Point(w), Is.EqualTo(stopped)); Idle(w, 1);

            // Quiet AI lets us inspect the exact squad slot. A mistakenly included player would shift it by 1 m.
            var quiet = new M6World(Recipe(new[] { U(1), U(2, 12000), U(3, 70000, 35000, team: 2, squad: 2) }));
            quiet.Sim.Step(Input(0, 1, command));
            Assert.That(quiet.Read<MoveIntent>(2).Target, Is.EqualTo(command.TargetPoint));
            Idle(quiet, 1);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LargerStableIdPlayerWinsContestedSpace(bool reverse)
        {
            ulong player = 1, ai = 2;
            if (StableEntityKey.Placed(1, player).Id.Value < StableEntityKey.Placed(1, ai).Id.Value)
            { player = 2; ai = 1; }
            var w = new M6World(Recipe(new[] {
                U(player, 10750, 10500, speed: 7500), U(ai, 10000, 10500, speed: 7500),
                U(3, 70000, 35000, team: 2, squad: 2)
            }, player), reverse: reverse);
            Assert.That(w.Sim.Entities.StableOf(w.Unit(player)).Value, Is.GreaterThan(w.Sim.Entities.StableOf(w.Unit(ai)).Value));
            M3TestWorld.Locomotion(w.Session).Order(w.Unit(ai), M3TestWorld.Point(10500, 10500));
            w.Sim.Step(Input(-1, 0));
            Assert.That(Point(w, player), Is.EqualTo(M3TestWorld.Point(10500, 10500)), "player must claim the contested space first");
            Assert.That(M4CrowdTests.Squared(Point(w, player), Point(w, ai)) >= (System.Numerics.BigInteger)32768 * 32768, Is.True);
        }

        [Test]
        public void TenAiAroundPlayer_600Ticks_NoPairOverlaps_NoEntityBirths()
        {
            var units = new UnitSpawn[12]; units[0] = U(1, 40000, 20000);
            // Stay outside every guard ring (at most 4.5 m) plus the 1 m arrival
            // tolerance. The old rows at Z=16/24 put keys 4 and 8 already at arrival.
            for (int i = 0; i < 10; i++)
                units[i + 1] = U((ulong)i + 2, 34000 + i % 5 * 3000, i < 5 ? 12000 : 28000);
            units[11] = U(12, 70000, 35000, team: 2, squad: 2);
            var match = M6World.Pack(new[] {
                new WarCore.Battle.ControlPointDef(1, M6World.P(40, 20), SimFix.FromInt(6)),
                new WarCore.Battle.ControlPointDef(2, M6World.P(60, 30), SimFix.FromInt(2))
            }, ticks: 8000, think: 1);
            var w = new M6World(Recipe(units, match: match));
            var initialIds = Ids(w);
            Assert.That(initialIds.Length, Is.EqualTo(units.Length));
            SimVec3[] CheckCrowd(int tick)
            {
                Assert.That(Ids(w), Is.EqualTo(initialIds), $"entity identities at tick={tick}");
                var points = new SimVec3[units.Length];
                var radii = new SimFix[units.Length];
                for (int i = 0; i < units.Length; i++)
                {
                    ulong key = units[i].Key;
                    Assert.That(w.Dead(key), Is.False, $"unit={key} died at tick={tick}");
                    points[i] = Point(w, key);
                    radii[i] = w.Read<Collider>(key).Radius;
                }
                for (int i = 0; i < units.Length; i++)
                    for (int j = i + 1; j < units.Length; j++)
                    {
                        var gap = (System.Numerics.BigInteger)(radii[i] + radii[j]).Raw;
                        Assert.That(M4CrowdTests.Squared(points[i], points[j]) >= gap * gap, Is.True,
                            $"overlap tick={tick} pair={units[i].Key}/{units[j].Key}");
                    }
                return points;
            }
            var previous = CheckCrowd(0);
            var aiMoved = new bool[10];
            int playerMoves = 0;
            for (int tick = 0; tick < 600; tick++)
            {
                SimVec3 before = Point(w);
                int phase = tick / 15 % 4;
                w.Sim.Step(Input(phase == 0 ? 1 : phase == 2 ? -1 : 0, phase == 1 ? 1 : phase == 3 ? -1 : 0));
                if (!Point(w).Equals(before)) playerMoves++;
                var current = CheckCrowd(tick + 1);
                for (int i = 0; i < aiMoved.Length; i++)
                    aiMoved[i] |= !current[i + 1].Equals(previous[i + 1]);
                previous = current;
                Idle(w, 1); Assert.That(w.Sim.Events.EventsDroppedTotal, Is.Zero);
            }
            for (int i = 0; i < aiMoved.Length; i++)
                Assert.That(aiMoved[i], Is.True, $"AI key={units[i + 1].Key} must move during the scenario");
            Assert.That(playerMoves, Is.GreaterThan(0));
        }

        private static ulong[] Ids(M6World w)
        {
            var result = new System.Collections.Generic.List<ulong>();
            foreach (EntityId unit in new EntityStage(w.Sim.Entities).Capture()) result.Add(w.Sim.Entities.StableOf(unit).Value);
            return result.ToArray();
        }

        [Test]
        public void ArmedPlayerBesideEnemy_600Ticks_NoAutomaticFire()
        {
            var w = new M6World(Recipe(new[] { U(1, weapon: 0), U(2, 13000, team: 2, squad: 2) }, match: Match(think: 1)));
            Assert.That(w.Read<WarCore.Combat.Combatant>(1).Weapon, Is.EqualTo(0));
            for (int tick = 0; tick < 600; tick++)
            {
                w.Sim.Step(new InputFrame(default, Point(w, 2), primaryAttack: false));
                Assert.That(w.Combat.Fires.Length, Is.Zero, "movement package must not generate player fire");
                Assert.That(w.Sim.Events.EventsDroppedTotal, Is.Zero); Idle(w, 1);
            }
        }
    }
}
