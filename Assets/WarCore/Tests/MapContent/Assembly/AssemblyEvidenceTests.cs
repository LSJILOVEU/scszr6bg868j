using System;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // F2a: the recorded worlds are only a lock if their ticks go through the code the installers hold. These facts are read from
    // the worlds themselves at tick 100, so a fixture that stops reaching its branch fails here instead of locking nothing.
    public sealed class AssemblyEvidenceTests
    {
        private static SimSession At100(string name)
        {
            (WorldRecipe recipe, byte[] map) = AssemblyWorlds.Make(name);
            SimSession session = AssemblyWorlds.Build(recipe, map, false);
            while (session.Tick < 100) session.StepOnce();
            return session;
        }

        private static bool Has<T>(SimSession session, ulong key) where T : unmanaged, IStateHash
        {
            WarCore.Sim.Sim sim = M3TestWorld.Sim(session);
            return sim.Entities.TryGet(sim.Entities.Resolve<T>(), M3TestWorld.Unit(session, key), out _);
        }

        // Unit 1 starts at its spawn point and has an order in every one of these worlds; it must have left it.
        [TestCase("M2b"), TestCase("M3"), TestCase("M4"), TestCase("Terrain"), TestCase("FacingMotionStanding")]
        public void MovementInstaller_UnitsAreOrderedAndWalkAway(string name)
        {
            SimSession session = At100(name);
            (WorldRecipe recipe, _) = AssemblyWorlds.Make(name);
            UnitSpawn spawn = recipe.Units[0];
            Assert.That(spawn.Key, Is.EqualTo(1UL));
            SimVec3 from = M3TestWorld.Point(spawn.XMm, spawn.ZMm);
            SimVec3 now = M3TestWorld.Position(session, 1);
            SimFix dx = now.X - from.X, dz = now.Z - from.Z;
            Assert.That(dx * dx + dz * dz, Is.GreaterThan(SimFix.FromInt(1)), name);
        }

        [Test]
        public void TerrainInstaller_MoversCarryProgressAndWalkOnTheSurface()
        {
            SimSession session = At100("Terrain");
            Assert.That(Has<MoveProgress>(session, 1), Is.True);
            Assert.That(Has<MoveWaypoint>(session, 1), Is.True);
            Assert.That(M3TestWorld.Position(session, 1).Y.Raw, Is.Not.EqualTo(0L), "unit 1 climbs the ridge");
        }

        [Test]
        public void FacingMotionAndStandingInstallers_LeaveTheirMarkOnTheUnits()
        {
            SimSession session = At100("FacingMotionStanding");
            Assert.That(session.TryFacing(1, out long x, out _), Is.True);
            Assert.That(x, Is.GreaterThan(0L), "unit 1 walked along +X and faces it");
            for (ulong key = 1; key <= 5; key++)
                Assert.That(Has<MotionLimits>(session, key), Is.True, "motion limits of unit " + key);
            Assert.That(M3TestWorld.Component<SquadStanding>(session, 4).Kind, Is.EqualTo(StandingKind.Hold));
            Assert.That(M3TestWorld.Component<SquadStanding>(session, 3).Kind, Is.EqualTo(StandingKind.Follow));
            Assert.That(Has<SquadStanding>(session, 1), Is.True);
        }

        [Test]
        public void BlockAiWorld_ARaisedBlockStopsTheSwing()
        {
            var world = new M6World(AssemblyWorlds.BlockAiRecipe(), AssemblyWorlds.PlainMap());
            int blocked = 0, started = 0;
            // The strike log holds one tick, so it is read after every tick.
            for (int tick = 0; tick < 100; tick++)
            {
                world.Step();
                foreach (StrikeRecord strike in world.Combat.Strikes)
                {
                    if (strike.Kind == StrikeKind.Blocked) blocked++;
                    if (strike.Kind == StrikeKind.Started) started++;
                }
            }
            Assert.That(started, Is.GreaterThan(0));
            Assert.That(blocked, Is.GreaterThan(0));
        }

        [Test]
        public void PlayerWorld_NamesItsControlledUnit()
        {
            SimSession session = At100("MpDemo");
            Assert.That(session.ControlledKey.HasValue, Is.True);
        }
    }
}
