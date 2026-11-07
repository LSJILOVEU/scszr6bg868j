using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.E1aSamples;
using static WarCore.Tests.E1bSamples;

namespace WarCore.Tests
{
    // E1 acceptance 4: the four height-field / surface-table combinations run from a description, and the terrain layer file.
    public sealed class E1bTerrainTests
    {
        private const string TerrainPlayerGuard = "Player control on terrain maps is not supported before T1c.";

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void TerrainCombinationRunsFromTheDescription(bool heights, bool surfaces)
        {
            string text = TerrainCase(heights, surfaces);
            MapBakeResult result = Bake(text);
            ContentPack pack = RwrPack.Load(TerrainPack);
            WorldRecipe recipe = MapRecipe.From(result, pack);
            if (heights || surfaces) Assert.That(recipe.TerrainPack, Is.SameAs(pack));
            else Assert.That(recipe.TerrainPack, Is.Null);
            Assert.That(recipe.Match, Is.Not.Null);

            SimSession session = null;
            Assert.DoesNotThrow(() => session = MapDocument.Open(Utf8(text)).Start());
            var start = new Dictionary<ulong, SimVec3>();
            foreach (ulong key in new ulong[] { 1, 2 }) start[key] = M3TestWorld.Position(session, key);
            if (heights)
                foreach (ulong key in start.Keys) Assert.That(start[key].Y.Raw, Is.EqualTo(PlateauRaw), "tick 0 unit " + key);
            for (int i = 0; i < 90; i++) session.StepOnce();
            Assert.That(session.Tick, Is.EqualTo(90));
            foreach (ulong key in start.Keys)
            {
                SimVec3 now = M3TestWorld.Position(session, key);
                long dx = now.X.Raw - start[key].X.Raw, dz = now.Z.Raw - start[key].Z.Raw;
                Assert.That(dx * dx + dz * dz, Is.GreaterThanOrEqualTo(SimFix.Scale * SimFix.Scale), "unit " + key + " moved less than 1 m");
                if (heights) Assert.That(now.Y.Raw, Is.EqualTo(PlateauRaw), "tick 90 unit " + key);
            }

            if (heights || surfaces)
            {
                NotSupportedException e = Assert.Throws<NotSupportedException>(() => SimSession.Create(recipe.WithPlayer(1), result.WcMapBytes));
                Assert.That(e.Message, Is.EqualTo(TerrainPlayerGuard));
            }
        }

        // The layer file is PackJson byte for byte plus a final newline, so extends, ids and every value are locked with it.
        [Test]
        public void TerrainLayerFileIsTheDemoPackJson()
        {
            string file = File.ReadAllText(Path.Combine(ArchitectureTests.AssetRoot(), "WarCore/Content/Rwr/Data/rwr-terrain-demo.json"));
            Assert.That(file, Is.EqualTo(TerrainDemoMap.PackJson + "\n"));
        }

        [Test]
        public void TerrainLayerFileMatchesTheDemoPackJson()
        {
            ContentPack disk = RwrPack.Load(TerrainPack);
            ContentPack memory = ContentLoader.Load(new MemoryContentSource(
                new Dictionary<string, string> { [TerrainDemoMap.PackId] = TerrainDemoMap.PackJson }, FileContentSource.Rwr()), TerrainDemoMap.PackId);
            TerrainRules a = disk.Terrain, b = memory.Terrain;
            Assert.That(a, Is.Not.Null);
            Assert.That(b, Is.Not.Null);
            Assert.That(a.SurfaceCount, Is.EqualTo(3));
            Assert.That(a.SurfaceCount, Is.EqualTo(b.SurfaceCount));
            for (int i = 0; i < a.SurfaceCount; i++)
            {
                SurfaceDef x = a.Surface(i), y = b.Surface(i);
                Assert.That(x.Id, Is.EqualTo(y.Id), "surface " + i);
                Assert.That(x.Passable, Is.EqualTo(y.Passable), "surface " + i);
                Assert.That(x.SpeedMilli, Is.EqualTo(y.SpeedMilli), "surface " + i);
                Assert.That(x.Cost, Is.EqualTo(y.Cost), "surface " + i);
            }
            Assert.That(a.MaxWalkableNum, Is.EqualTo(3));
            Assert.That(a.MaxWalkableDen, Is.EqualTo(4));
            Assert.That(a.MaxWalkableNum, Is.EqualTo(b.MaxWalkableNum));
            Assert.That(a.MaxWalkableDen, Is.EqualTo(b.MaxWalkableDen));
            Assert.That(disk.Id, Is.EqualTo(memory.Id));
        }
    }
}
