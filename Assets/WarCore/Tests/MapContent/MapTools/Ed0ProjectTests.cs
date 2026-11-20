using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // ED-0 acceptance 2, 3, 4 and 7 on the real project: the two shipped cartridges, the seven maps started through the
    // catalog against the old RwrPack path, an ancient map, and the no-RWR rule for the map facade.
    public sealed class Ed0ProjectTests
    {
        private Ed0Dirs dirs;

        // Fresh variables (none set) for every test, so a value left by another test never reaches the project scan.
        [SetUp] public void Make() => dirs = new Ed0Dirs();
        [TearDown] public void Clean() => dirs.Dispose();

        private static readonly string[] RwrLayers = { "rwr", "rwr-ai-playtest", "rwr-m6", "rwr-m7-playtest", "rwr-melee", "rwr-terrain-demo" };

        [Test]
        public void TheShippedCartridges()
        {
            Assert.That(MapCatalog.Cartridges(), Is.EqualTo(new[] { "Ancient", "Rwr" }));
            Assert.That(MapCatalog.Layers("Rwr"), Is.EqualTo(RwrLayers));
            Assert.That(MapCatalog.Layers("Ancient"), Is.EqualTo(new[] { "ancient" }));
            Assert.That(MapCatalog.DefaultLayer("Rwr"), Is.EqualTo("rwr-terrain-demo"));
            Assert.That(MapCatalog.DefaultLayer("Ancient"), Is.EqualTo("ancient"));
            Assert.That(MapCatalog.SurfaceNames("rwr-terrain-demo"), Is.EqualTo(new[] { "grass", "mud", "road" }));
            Assert.That(MapCatalog.UnitTypeNames("ancient"), Is.EqualTo(new[] { "ancient.swordsman", "ancient.guard" }));
            foreach (string layer in RwrLayers)
            {
                ContentPack pack = RwrPack.Load(layer);
                Assert.That(MapCatalog.LayerHasMatch(layer), Is.EqualTo(pack.HasMatch), layer);
                Assert.That(MapCatalog.LayerHasTerrain(layer), Is.EqualTo(pack.Terrain != null), layer);
            }
            Assert.That(RwrPack.Load("rwr").HasMatch, Is.True);
        }

        [Test]
        public void TheDocumentNamesItsLayersSurfacesAndUnitTypes()
        {
            MapDocument doc = MapDocument.Open(File.ReadAllBytes("Assets/WarCore/Maps/terrain-demo.map.json"));
            Assert.That(doc.SurfaceNames(), Is.EqualTo(new[] { "grass", "mud", "road" }));
            ContentPack pack = RwrPack.Load(doc.PackId);
            var types = new string[pack.UnitTypeCount];
            for (int i = 0; i < types.Length; i++) types[i] = pack.UnitTypeId(i);
            Assert.That(doc.UnitTypeNames(), Is.EqualTo(types));
            MapDocument m6 = MapDocument.Open(File.ReadAllBytes("Assets/WarCore/Maps/m6-demo.map.json"));
            IReadOnlyList<string> m6Surfaces = null;
            Assert.DoesNotThrow(() => m6Surfaces = m6.SurfaceNames());
            Assert.That(m6Surfaces, Is.Empty);
        }

        [TestCase("door-demo")]
        [TestCase("door-terrain-demo")]
        [TestCase("lanes-playtest")]
        [TestCase("m6-demo")]
        [TestCase("m7-demo")]
        [TestCase("plate-demo")]
        [TestCase("terrain-demo")]
        public void EveryShippedMapStartsAsThroughRwrPack(string map)
        {
            byte[] bytes = File.ReadAllBytes("Assets/WarCore/Maps/" + map + ".map.json");
            Func<SimSession> viaCatalog = () => MapDocument.Open(bytes).Start();
            Func<SimSession> viaRwr = () =>
            {
                MapBakeResult r = MapBake.Run(MapDocument.Open(bytes).Save());
                return SimSession.Create(MapRecipe.From(r, RwrPack.Load(r.PackId)), r.WcMapBytes);
            };
            Assert.That(Run(viaCatalog), Is.EqualTo(Run(viaRwr)), map);
        }

        // "<hash after 300 ticks>" or "<exception type>: <message>", so a map both paths refuse is compared too.
        private static string Run(Func<SimSession> start)
        {
            try
            {
                SimSession s = start();
                for (int i = 0; i < 300; i++) s.StepOnce();
                return s.Tick + " " + s.StateHash.ToString("X16");
            }
            catch (Exception e) { return e.GetType().Name + ": " + e.Message; }
        }

        // A small flat map on the ancient cartridge (not in Maps/): it starts, and its first 600 ticks repeat. The flat map
        // does not apply the pack's AI section (design K-E3), so the match is not expected to end here. AncientHash600 is a
        // behaviour baseline, not a determinism lock: a package that makes this map use the pack's AI may record it again.
        internal static byte[] AncientMap()
        {
            var units = new StringBuilder();
            for (int k = 1; k <= 12; k++)
            {
                int team = k <= 6 ? 1 : 2, n = (k - 1) % 6;
                long x = team == 1 ? 2000 + 2000 * (n % 3) : 58000 - 2000 * (n % 3), z = n < 3 ? 29000 : 35000;
                if (units.Length > 0) units.Append(",\n");
                units.Append("    { \"key\": " + k + ", \"type\": \"" + (n % 2 == 0 ? "ancient.swordsman" : "ancient.guard") + "\", \"team\": " + team
                    + ", \"squad\": " + team + ", \"at\": [" + x + ", " + z + "], \"reserve\": false }");
            }
            string text = "{\n  \"format\": \"warcore.map\",\n  \"version\": 1,\n  \"id\": \"ancient-ed0\",\n  \"content\": \"none\",\n"
                + "  \"pack\": \"ancient\",\n  \"size\": [60, 40],\n  \"origin\": [0, 0],\n  \"floorRaw\": 0,\n  \"ceilingRaw\": 262144,\n"
                + "  \"layers\": [ { \"name\": \"walls\", \"bit\": 1 } ],\n  \"walls\": [],\n"
                + "  \"points\": [ { \"id\": 1, \"at\": [30000, 32000], \"radiusMm\": 6000, \"initialOwner\": 0 } ],\n"
                + "  \"bases\": [ { \"id\": 1, \"team\": 1, \"at\": [4000, 32000] }, { \"id\": 2, \"team\": 2, \"at\": [56000, 32000] } ],\n"
                + "  \"units\": [\n" + units + "\n  ],\n  \"run\": { \"seed\": 41, \"crowdPass\": \"teamIdle\" },\n  \"owners\": [\"hand\"],\n"
                + "  \"stamps\": [],\n  \"removed\": [],\n  \"next\": { \"wall\": 1, \"point\": 2, \"base\": 3, \"unit\": 13 }\n}\n";
            return MapDocument.Open(Encoding.UTF8.GetBytes(text)).Save();
        }

        private const ulong AncientHash600 = 0x8B96639EF5D95C32UL;

        [Test]
        public void AnAncientMapStartsAndRepeats()
        {
            byte[] bytes = AncientMap();
            ulong first = Run600(bytes), second = Run600(bytes);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(first, Is.EqualTo(AncientHash600), first.ToString("X16"));
        }

        private static ulong Run600(byte[] bytes)
        {
            SimSession s = null;
            Assert.DoesNotThrow(() => s = MapDocument.Open(bytes).Start());
            for (int i = 0; i < 600; i++) s.StepOnce();
            Assert.That(s.Tick, Is.EqualTo(600));
            return s.StateHash;
        }

        // Acceptance 7: the map facade (and the Unity map folders, once they exist) never names the RWR content.
        [Test]
        public void TheMapFacadeNeverNamesRwr()
        {
            string root = ArchitectureTests.AssetRoot();
            int scanned = 0;
            foreach (string folder in new[] { "WarCore/Presentation/Maps", "WarCore/Unity/Map", "WarCore/Unity/Editor/Map" })
            {
                string dir = Path.Combine(root, folder);
                if (!Directory.Exists(dir)) continue;
                foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    string source = File.ReadAllText(file);
                    Assert.That(source, Does.Not.Contain("WarCore.Content.Rwr"), file);
                    Assert.That(source, Does.Not.Contain("RwrPack"), file);
                    scanned++;
                }
            }
            Assert.That(scanned, Is.GreaterThanOrEqualTo(3));
        }
    }
}
