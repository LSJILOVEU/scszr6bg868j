using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.E2Fixtures;

namespace WarCore.Tests
{
    // Recorded before the map gate existed: what the runtime decides on the E2 fixtures. The gate tests compare against these.
    public sealed class E2RuntimeLockTests
    {
        internal static string RuntimeAnchors(byte[] descBytes, ContentPack pack)
        {
            MapBakeResult bake = MapBake.Run(descBytes);
            TerrainTables tables = TerrainDerivation.Derive(WcMapReader.Read(bake.WcMapBytes), pack);
            SimVec3[] spawns = bake.Setup.Bases.Select(b => b.Point).ToArray();
            SimVec3[] points = bake.Setup.ControlPoints.Select(p => p.Center).ToArray();
            try { TerrainAnchors.Validate(tables, spawns, points); }
            catch (InvalidDataException e) { return e.Message.Split(' ')[0]; }
            return "ok";
        }

        [Test]
        public void UnitFootprintIsAQuarterMetre() => Assert.That(InterimLocomotion.UnitFootprint.Raw, Is.EqualTo(16384L));

        [Test]
        public void AnchorChecksOnTheTerrainFixtures()
        {
            Assert.That(RuntimeAnchors(TerrainDemo(), TerrainPack()), Is.EqualTo("ok"));
            Assert.That(RuntimeAnchors(MudBand(), TerrainPack()), Is.EqualTo("ok"), "shipped mud is slow, not blocked");
            Assert.That(RuntimeAnchors(MudBand(), TerrainPack(MudBlockedPackJson)), Is.EqualTo("TERRAIN_UNREACHABLE"));
            Assert.That(RuntimeAnchors(TerrainDemo(), TerrainPack(SteepPackJson)), Is.EqualTo("TERRAIN_ANCHOR_BLOCKED"));
            Assert.That(RuntimeAnchors(WallAnchor(), TerrainPack()), Is.EqualTo("TERRAIN_ANCHOR_BLOCKED"));
            Assert.That(RuntimeAnchors(Plateau(), TerrainPack()), Is.EqualTo("TERRAIN_UNREACHABLE"));
        }

        // Wp23SpawnTests.WallCorner_IsRoundedByTheFootprint on a flat description map with the same wall box.
        [Test]
        public void WallCornerIsRoundedOnADescriptionMap()
        {
            byte[] bytes = new E2Map { Width = 16, Depth = 8 }.Wall(10, 2, 1, 3).Bytes();
            NavGrid nav = TerrainDerivation.Derive(WcMapReader.Read(MapBake.Run(bytes).WcMapBytes), M6Pack()).Nav;
            Assert.That(nav.SegmentClear(Mm(9850, 1850), Mm(9850, 1850), InterimLocomotion.UnitFootprint), Is.False);
            Assert.That(nav.SegmentClear(Mm(9823, 1823), Mm(9823, 1823), InterimLocomotion.UnitFootprint), Is.True);
        }

        [TestCase((byte)0, true)]
        [TestCase((byte)1, false)]
        public void WorldBuilderRefusesAWallLayerOnTheMoverLayer(byte bit, bool refused)
        {
            byte[] map = MapBake.Run(new E2Map { LayerBit = bit }.Wall(5, 5, 1, 1).Bytes()).WcMapBytes;
            var recipe = new WorldRecipe(1, MoverKind.InterimPath, new UnitSpawn[0], new BridgeCommand[0]);
            if (refused)
                Assert.That(Assert.Throws<InvalidOperationException>(() => WorldBuilder.Create(recipe, map)).Message,
                    Is.EqualTo("Wall layers must not share the mover layer."));
            else
                Assert.DoesNotThrow(() => WorldBuilder.Create(recipe, map));
        }
    }
}
