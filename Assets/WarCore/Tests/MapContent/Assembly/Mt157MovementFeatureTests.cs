using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt157MovementFeatureTests
    {
        [Test]
        public void UnequalMapOriginAxes_PlaceWallInItsLocalCell()
        {
            WcMap source = M2MapFixture.Model();
            var map = new WcMap(source.MapId, source.ContentId, SimVec3.Metres(2, 0, 5),
                SimVec3.Metres(10, 4, 9), source.ChunkSize, source.FlatHeight, source.Width, source.Depth,
                source.Layers.ToArray(), source.Walls.ToArray(), source.Passability.ToArray());
            var recipe = new WorldRecipe(41, MoverKind.Interim, Array.Empty<UnitSpawn>(), Array.Empty<BridgeCommand>());
            SimSession session = SimSession.Create(recipe, WcMapWriter.Write(map));

            Assert.That(session.BlockedCell(4, 1), Is.True);
        }

        [Test]
        public void InitialUnitRadius_AllowsThreeEighthsMetreWallClearance()
        {
            var recipe = new WorldRecipe(41, MoverKind.Interim,
                new[] { new UnitSpawn(1, 0, 1, 3000, 2000, 60000) },
                new[] { BridgeCommand.MoveTo(1, 1, 3625, 2000) });
            SimSession session = SimSession.Create(recipe, WcMapWriter.Write(M2MapFixture.Model()));

            session.StepOnce();

            Assert.That(session.Current.TryGet(1, out ViewEntity unit), Is.True);
            Assert.That(unit.X, Is.EqualTo(3.625f));
        }
    }
}
