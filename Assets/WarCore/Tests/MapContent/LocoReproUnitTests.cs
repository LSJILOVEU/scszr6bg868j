using System;
using System.Numerics;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class LocoReproUnitTests
    {
        [Category("Manual"), Test, Explicit("Reproduces crowd penetration; run by name.")]
        public void FastShortChord_DoesNotPassThroughAStationaryUnit()
        {
            var session = SimSession.Create(new WorldRecipe(41, MoverKind.InterimCrowd,
                new[] { new UnitSpawn(1, 0, 1, 8500, 8500, 12000), new UnitSpawn(2, 0, 2, 8700, 8960, 6000) },
                new[] { BridgeCommand.MoveTo(1, 1, 8900, 8500) }), WcMapWriter.Write(M3TestWorld.Boxes(12, 12)));
            SimVec3 before = M3TestWorld.Position(session), other = M3TestWorld.Position(session, 2);
            long radius = M3TestWorld.Component<Collider>(session).Radius.Raw
                + M3TestWorld.Component<Collider>(session, 2).Radius.Raw;
            Assert.That(M4CrowdTests.Squared(before, other), Is.GreaterThanOrEqualTo((BigInteger)radius * radius));

            session.StepOnce();

            SimVec3 after = M3TestWorld.Position(session);
            Assert.That(M3TestWorld.Position(session, 2), Is.EqualTo(other));
            Assert.That(M4CrowdTests.Squared(after, other), Is.GreaterThanOrEqualTo((BigInteger)radius * radius));
            double dx = after.X.Raw - before.X.Raw, dz = after.Z.Raw - before.Z.Raw;
            double ox = other.X.Raw - before.X.Raw, oz = other.Z.Raw - before.Z.Raw;
            double lengthSquared = dx * dx + dz * dz;
            double t = lengthSquared == 0 ? 0 : Math.Max(0, Math.Min(1, (ox * dx + oz * dz) / lengthSquared));
            double closestSquared = (ox - t * dx) * (ox - t * dx) + (oz - t * dz) * (oz - t * dz);
            Assert.That(Math.Sqrt(closestSquared) / SimFix.Scale, Is.GreaterThanOrEqualTo((double)radius / SimFix.Scale),
                "The AI step must keep the summed capsule radii clear along its whole path.");
        }
    }
}
