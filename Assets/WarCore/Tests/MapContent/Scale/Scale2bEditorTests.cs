using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using static WarCore.Tests.Scale2bFixtures;

namespace WarCore.Tests
{
    // SCALE-2b acceptance SC1 (editor part), SC13, SC15, SC16 and SC18: traversal alignment, the editor reading the section,
    // the gate not reading it, and the editor's read-out.
    public sealed class Scale2bEditorTests
    {
        private const long Top = 3 * M;

        private static string Kind(string id, string shape, long riseMin, long riseMax, long fallMin = 0, long fallMax = 163840, long gapMin = 65536) =>
            "{ \"id\": \"" + id + "\", \"shape\": \"" + shape + "\", \"riseMin\": " + riseMin + ", \"riseMax\": " + riseMax + ", \"fallMin\": " + fallMin
            + ", \"fallMax\": " + fallMax + ", \"gapMin\": " + gapMin + ", \"ticksBase\": 24, \"ticksPerMetreUp\": 0, \"ticksPerMetreDown\": 0, \"pathCost\": 0 }";

        private static string Traversal(params string[] kinds) => "\"traversal\": { \"kinds\": [ " + string.Join(", ", kinds) + " ] }";

        private static string Parent(params string[] kinds) => Layer("scale2b-trv", "rwr-stance", Traversal(kinds));

        private static void TrvAccepts(string parent, params (string Field, int Mm)[] changes) =>
            Assert.That(Loads(Layer("scale2b-test", "scale2b-trv", ScaleJson(changes)), parent).SectionOrNull<ScaleRules>(), Is.Not.Null);

        private static void TrvRefusesOnly(string field, string parent, params (string Field, int Mm)[] changes) =>
            Assert.That(Fields(Refused(Layer("scale2b-test", "scale2b-trv", ScaleJson(changes)), parent)), Is.EqualTo(new[] { field }));

        // SC13: with an over kind, a lone low wall on flat ground must match one (FirstMatch with rise = fall = height and no
        // gap) and a lone high wall must match none; edge kinds alone do not count.
        [Test]
        public void SC13_TraversalAlignment()
        {
            string upTo1m = Parent(Kind("vault", "over", 0, 65536));
            TrvAccepts(upTo1m, ("lowWallMm", 1000));
            TrvRefusesOnly("lowWallMm", upTo1m, ("lowWallMm", 1001));

            string upTo175 = Parent(Kind("vault", "over", 0, 114688));
            TrvRefusesOnly("highWallMm", upTo175, ("highWallMm", 1750));
            TrvAccepts(upTo175, ("highWallMm", 1751));
            Assert.That(Loads(Layer("scale2b-test", "scale2b-trv", ScaleJson(("highWallMm", 1751))), upTo175).SectionOrNull<ScaleRules>().HighWallRaw,
                Is.EqualTo(114754));

            string shortFall = Parent(Kind("vault", "over", 0, 65536, 0, 39321));
            TrvRefusesOnly("lowWallMm", shortFall, ("lowWallMm", 1000));

            string twoOvers = Parent(Kind("hop", "over", 0, 39321), Kind("vault", "over", 39322, 78642));
            TrvAccepts(twoOvers, ("lowWallMm", 1000));

            string edgeOnly = Parent(Kind("step", "edge", 0, 39321));
            TrvAccepts(edgeOnly);
        }

        // SC1: on old layers the editor's windows equal the prefabs' default parameters.
        [TestCase("rwr-field")]
        [TestCase("rwr-stance")]
        public void SC1_EditorWindowsOnOldLayersUseThePrefabDefaults(string layer)
        {
            CombatBody body = Shipped(layer).Body.Value;
            MapDocument doc = Tall(layer);
            MapDocument window = Ed6aFixtures.NoThrow(() => doc.WithWindow(5, 2, true, 8, 3, 2, Top));
            Assert.That(window.Save(), Is.EqualTo(Ed6aFixtures.StampedBytes(doc, d =>
                MapPrefabs.Window(d, "window-1", new MapXZ(5, 2), true, 8, 3, 2, Top, "walls", "wall", body))));
            MapDocument on = Ed6aFixtures.NoThrow(() => doc.WithWindowOn(5, 2, true, 8, 3, 2, Top, 2 * Top));
            Assert.That(on.Save(), Is.EqualTo(Ed6aFixtures.StampedBytes(doc, d =>
                MapPrefabs.WindowOn(d, "window-1", new MapXZ(5, 2), true, 8, 3, 2, Top, 2 * Top, "walls", "wall", body))));
        }

        // SC15: a layer whose scale sets a 1 m sill: both editor windows put the sill top 65536 raw above the window's floor;
        // a layer without a body still throws the same exception and message.
        [Test]
        public void SC15_EditorWindowsReadTheScale()
        {
            using (var cartridge = new Scale2bSillCartridge())
            {
                MapDocument doc = Tall(Scale2bSillCartridge.Layer);
                MapDocument window = Ed6aFixtures.NoThrow(() => doc.WithWindow(5, 2, true, 8, 3, 2, Top));
                MapWallInfo sill = window.Walls()[1];
                Assert.That(sill.HasBase, Is.False);
                Assert.That(sill.HeightRaw, Is.EqualTo(65536));
                Assert.That(window.Save(), Is.EqualTo(Ed6aFixtures.StampedBytes(doc, d =>
                    MapPrefabs.Window(d, "window-1", new MapXZ(5, 2), true, 8, 3, 2, Top, "walls", "wall", cartridge.Body, 65536, 2 * M))));

                MapDocument on = Ed6aFixtures.NoThrow(() => doc.WithWindowOn(5, 2, true, 8, 3, 2, Top, 2 * Top));
                MapWallInfo sillOn = on.Walls()[1];
                Assert.That(sillOn.BaseRaw, Is.EqualTo(Top));
                Assert.That(sillOn.HeightRaw, Is.EqualTo(65536));
            }
            foreach (Func<MapDocument, MapDocument> place in new Func<MapDocument, MapDocument>[]
                { d => d.WithWindow(5, 2, true, 8, 3, 2, Top), d => d.WithWindowOn(5, 2, true, 8, 3, 2, Top, 2 * Top) })
            {
                MapDocument doc = Tall("rwr-m6");
                byte[] before = doc.Save();
                InvalidOperationException e = Assert.Throws<InvalidOperationException>(() => place(doc));
                Assert.That(e.Message, Does.Contain("no combat body"));
                Assert.That(e.Message, Does.Contain("rwr-m6"));
                Assert.That(doc.Save(), Is.EqualTo(before));
            }
        }

        // SC16: the gate never reads the section: a 0.7 m wall gets the same problems on rwr-scale and rwr-stance.
        [Test]
        public void SC16_GateDoesNotReadTheScale()
        {
            byte[] Bytes(string pack) => MapDescJson.Write(Desc(pack, 20, 10, null, new MapWall(1, "wall", "walls", new MapXZ(4, 4), new MapXZ(6, 1), 45876)));
            IReadOnlyList<MapProblem> withScale = null, without = null;
            Assert.DoesNotThrow(() => withScale = MapGate.Check(Bytes("rwr-scale"), Shipped("rwr-scale")));
            Assert.DoesNotThrow(() => without = MapGate.Check(Bytes("rwr-stance"), Shipped("rwr-stance")));
            Assert.That(withScale, Is.EqualTo(without));
            Assert.That(withScale, Is.Empty, "a wall of no standard height is still legal");
        }

        // SC18: MapDocument.Scale passes the layer's raw values on (Default on an old layer), with a plain public surface.
        [Test]
        public void SC18_EditorReadOut()
        {
            ScaleRules s = Shipped("rwr-scale").SectionOrNull<ScaleRules>();
            Same(Ed6aFixtures.NoThrow(() => Tall("rwr-scale").Scale()), s);
            Same(Ed6aFixtures.NoThrow(() => Tall("rwr-field").Scale()), ScaleRules.Default);
            ScaleRules odd = Rules("rwr-stance", Scale2bSillCartridge.OddScale);
            long[] raws = { odd.StoreyRaw, odd.PlateRaw, odd.LowWallRaw, odd.ChestWallRaw, odd.HighWallRaw, odd.WindowSillRaw, odd.WindowTopRaw,
                odd.RailingRaw, odd.LaneRaw, odd.SidewalkRaw, odd.AlleyRaw };
            Assert.That(raws.Distinct().Count(), Is.EqualTo(raws.Length), "every raw value differs, so a field passed on in the wrong place shows");
            using (new Scale2bSillCartridge())
            {
                MapDocument doc = Tall(Scale2bSillCartridge.OddLayer);
                Same(Ed6aFixtures.NoThrow(() => doc.Scale()), odd);
                MapBuildingInfo b = Ed6aFixtures.NoThrow(() => doc.StandardBuilding(2, 2, 12, 8, 2, "s", 2, true, 4, 4));
                Assert.That((b.StoreyRaw, b.ThickRaw, b.StairLength), Is.EqualTo((odd.StoreyRaw, odd.PlateRaw, odd.StairCells(2))));
            }

            List<string> problems = E1bDocumentTests.SurfaceViolations(typeof(MapScaleInfo), typeof(MapDocument).Assembly, out int members);
            Assert.That(problems, Is.Empty);
            Assert.That(members, Is.GreaterThan(1));
        }

        // Every field, compared one by one.
        private static void Same(MapScaleInfo info, ScaleRules s)
        {
            Assert.That(info.StoreyRaw, Is.EqualTo(s.StoreyRaw), "storey");
            Assert.That(info.PlateRaw, Is.EqualTo(s.PlateRaw), "plate");
            Assert.That(info.LowWallRaw, Is.EqualTo(s.LowWallRaw), "low wall");
            Assert.That(info.ChestWallRaw, Is.EqualTo(s.ChestWallRaw), "chest wall");
            Assert.That(info.HighWallRaw, Is.EqualTo(s.HighWallRaw), "high wall");
            Assert.That(info.WindowSillRaw, Is.EqualTo(s.WindowSillRaw), "sill");
            Assert.That(info.WindowTopRaw, Is.EqualTo(s.WindowTopRaw), "top");
            Assert.That(info.RailingRaw, Is.EqualTo(s.RailingRaw), "railing");
            Assert.That(info.LaneRaw, Is.EqualTo(s.LaneRaw), "lane");
            Assert.That(info.SidewalkRaw, Is.EqualTo(s.SidewalkRaw), "sidewalk");
            Assert.That(info.AlleyRaw, Is.EqualTo(s.AlleyRaw), "alley");
            Assert.That(info.StairRiseMm, Is.EqualTo(s.StairRiseMm), "rise");
            Assert.That(info.StairRunMm, Is.EqualTo(s.StairRunMm), "run");
        }
    }
}
