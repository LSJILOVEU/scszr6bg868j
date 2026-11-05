using System;
using System.Reflection;
using NUnit.Framework;
using WarCore.Presentation;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    internal static class M1TestWorld
    {
        internal static WorldRecipe Probe(ulong seed = 1) => new WorldRecipe(seed, MoverKind.Interim,
            new[] { new UnitSpawn(1, 0, 1, 0, 0, 3000) }, Array.Empty<BridgeCommand>());

        internal static WorldRecipe Recipe(bool script = true) => new WorldRecipe(41, MoverKind.Interim,
            new[]
            {
                new UnitSpawn(73, 0, 2, -5000, 2000, 1700),
                new UnitSpawn(1, 0, 1, 0, 0, 3000),
                new UnitSpawn(19, 0, 1, 1000, -2000, 3000)
            }, script ? new[]
            {
                BridgeCommand.MoveTo(1, 1, 10000, 0),
                BridgeCommand.MoveTo(6, 2, -10001, 7333),
                BridgeCommand.MoveTo(7, 1, 10000, 0),
                BridgeCommand.MoveTo(150, 1, 10000, 10000),
                BridgeCommand.MoveTo(599, 2, 0, 0),
                BridgeCommand.MoveTo(600, 1, -1000, -1000)
            } : Array.Empty<BridgeCommand>());

        // Test-only inspection keeps raw coordinates and simulation handles out of the bridge API.
        internal static Simulation SimulationOf(SimSession session)
        {
            FieldInfo field = typeof(SimSession).GetField("sim", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (Simulation)field.GetValue(session);
        }

        internal static SimVec3 PositionOf(SimSession session, ulong key = 1)
        {
            EntityStore entities = SimulationOf(session).Entities;
            Assert.That(entities.TryResolve(StableEntityKey.Placed(1, key).Id, out EntityRef unit), Is.True);
            Assert.That(entities.TryGet(entities.Resolve<Position>(), unit, out Position position), Is.True);
            return position.Value;
        }

        internal static bool IsAt(SimSession session, long xMm, long zMm)
            => PositionOf(session).Equals(new SimVec3(SimFix.Ratio(xMm, 1000), default, SimFix.Ratio(zMm, 1000)));

        internal static long XMm(SimSession session) => PositionOf(session).X.Raw * 1000 / SimFix.Scale;
        internal static long ZMm(SimSession session) => PositionOf(session).Z.Raw * 1000 / SimFix.Scale;
    }
}
