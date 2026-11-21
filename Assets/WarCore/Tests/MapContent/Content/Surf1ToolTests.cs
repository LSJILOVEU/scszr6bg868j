using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using static WarCore.Tests.E2GateTests;

namespace WarCore.Tests
{
    // SURF-1: the map gate and the editor catalog see the new layer. Maps are built in code through the E2 facade; no map file is added.
    public sealed class Surf1ToolTests
    {
        private static string[] Rows(int width, int depth, Func<int, char> letter) =>
            Enumerable.Range(0, depth).Select(z => new string(Enumerable.Range(0, width).Select(letter).ToArray())).ToArray();

        // 20 x 10 cells: bush on the left (A), water-deep on the right (C), grass between (B).
        private static byte[] Map(string pack) => new E2Map
        {
            Pack = pack,
            Surfaces = new MapSurfaces(new[] { "bush", "grass", "water-deep" }, Rows(20, 10, x => x < 6 ? 'A' : x < 14 ? 'B' : 'C')),
        }.Bytes();

        // S6
        [Test]
        public void Gate_AcceptsBushAndDeepWaterOnTheNewLayer()
        {
            Assert.That(MapGate.Check(Map("rwr-surfaces"), RwrPack.Load("rwr-surfaces")).ToArray(), Is.Empty);
        }

        [Test]
        public void Gate_RefusesTheSameMapOnTheOldLayer()
        {
            MapProblem[] problems = MapGate.Check(Map("rwr-terrain-demo"), RwrPack.Load("rwr-terrain-demo")).ToArray();
            Assert.That(problems, Is.EqualTo(new[] { P("GATE_REF", "surface bush"), P("GATE_REF", "surface water-deep") }));
            Assert.That(problems[0].ToString(), Does.Contain("surface bush"));
        }

        // S7: the editor facade reads the names from the data. Fresh variables, so no value left by another test is read.
        [Test]
        public void Catalog_ListsTenSurfaceNamesForTheNewLayerAndThreeForTheOld()
        {
            using (new Ed0Dirs())
            {
                Assert.That(MapCatalog.SurfaceNames("rwr-surfaces"), Is.EqualTo(Surf1RulesTests.Names));
                Assert.That(MapCatalog.SurfaceNames("rwr-terrain-demo"), Is.EqualTo(new[] { "grass", "mud", "road" }));
                Assert.That(MapCatalog.DefaultLayer("Rwr"), Is.EqualTo("rwr-terrain-demo"), "the editor default layer stays the old terrain layer");
            }
        }
    }
}
