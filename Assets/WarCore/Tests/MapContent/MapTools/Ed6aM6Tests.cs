using System.IO;
using System.Text;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using static WarCore.Tests.Ed6aFixtures;

namespace WarCore.Tests
{
    // Acceptance 2 (E0 E6 3 and E7 3): the M6 demo rebuilt from an empty map through the entry facade alone.
    public sealed class Ed6aM6Tests
    {
        // 4 walls, 3 points, 2 bases and 8 squads of 10 (per team one deployed squad and three reserve squads around its base).
        internal static MapDocument RebuildM6()
        {
            MapDocument doc = MapDocument.New(M6Settings());
            foreach (int x in new[] { 28, 51 })
                foreach (int z in new[] { 0, 24 })
                    doc = doc.WithWallAdded(new MapWallInfo("wall", "walls", x, z, 1, 16, 131072, false, 0));
            for (int i = 0; i < 3; i++)
            {
                long x = 16000 + 24000 * i;
                doc = doc.WithPointAdded(new MapPointInfo(x, 20000, 6000, 0, true, x, 20000, false, 0, false, 0));
            }
            doc = doc.WithBaseAdded(new MapBaseInfo(1, 6000, 20000, false, 0));
            doc = doc.WithBaseAdded(new MapBaseInfo(2, 74000, 20000, false, 0));
            for (int team = 1; team <= 2; team++)
            {
                long x = team == 1 ? 6000 : 74000;
                string type = team == 1 ? RwrPack.SoldierTeam1 : RwrPack.SoldierTeam2;
                for (int squad = 0; squad < 4; squad++) doc = doc.WithSquad(type, team, team, x, 20000, 10, squad > 0);
            }
            return doc;
        }

        [Test]
        public void RebuiltM6BakesToTheM6DemoMapBytes()
        {
            MapBakeResult result = MapBake.Run(RebuildM6().Save());
            Assert.That(result.WcMapBytes, Is.EqualTo(M6DemoMap.Bytes()));
            Assert.That(result.PackId, Is.EqualTo(RwrPack.M6Layer));
        }

        [Test]
        public void RebuiltM6BuildsTheRecordedMatchPolicy()
        {
            MapSetup setup = MapBake.Run(RebuildM6().Save()).Setup;
            MatchPack match = RwrPack.Load(RwrPack.M6Layer).BuildMatch(setup.ControlPoints, setup.Bases);
            Assert.That(match.PolicyDigest, Is.EqualTo(0x526DC7AB0741E51AUL));
            Assert.That(match.PolicyDigest, Is.EqualTo(E1aRecordTests.M6PolicyDigest));
        }

        [Test]
        public void RebuiltM6UnitsMatchTheRecordingAndTheDescription()
        {
            MapSetup setup = MapBake.Run(RebuildM6().Save()).Setup;
            ContentPack pack = RwrPack.Load(RwrPack.M6Layer);
            var text = new StringBuilder();
            foreach (MapSoldier s in setup.Soldiers)
                text.Append(E1aRecordTests.UnitLine(s.Key, s.XMm, s.ZMm, s.Team, s.Squad, s.Reserve, pack.WeaponOfUnitType(s.Type)));
            Assert.That(E1aRecordTests.Sha(Encoding.UTF8.GetBytes(text.ToString())), Is.EqualTo(E1aRecordTests.M6UnitsSha));

            MapDesc expected = MapDescJson.Read(File.ReadAllBytes(M6Path));
            MapDesc rebuilt = Desc(RebuildM6());
            Assert.That(rebuilt.Units.Count, Is.EqualTo(80));
            for (int i = 0; i < 80; i++)
            {
                MapUnit a = rebuilt.Units[i], b = expected.Units[i];
                Assert.That(a.Key, Is.EqualTo(b.Key), "unit " + i);
                Assert.That(a.Squad, Is.EqualTo(b.Squad), "unit " + i);
                Assert.That(a.Reserve, Is.EqualTo(b.Reserve), "unit " + i);
                Assert.That(a.Team, Is.EqualTo(b.Team), "unit " + i);
                Assert.That(a.Type, Is.EqualTo(b.Type), "unit " + i);
                Assert.That(a.At, Is.EqualTo(b.At), "unit " + i);
            }
        }
    }
}
