using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Interim;
using WarCore.Presentation;
using static WarCore.Tests.E2Fixtures;

namespace WarCore.Tests
{
    // Acceptance 1 (shipped descriptions pass), 5 (footprint and layer agree with the runtime) and 6 (read only).
    public sealed class E2GateTests
    {
        internal static MapProblem P(string code, string id = null, int? x = null, int? z = null, long? xMm = null, long? zMm = null, int? line = null) =>
            new MapProblem(code, id, x.HasValue ? (x.Value, z.Value) : ((int, int)?)null, xMm.HasValue ? new MapMm(xMm.Value, zMm.Value) : (MapMm?)null, line);

        [Test]
        public void TerrainPackLiteralIsTheDemoPackText() => Assert.That(TerrainPackJson, Is.EqualTo(TerrainDemoMap.PackJson));

        [Test]
        public void ShippedDescriptionsHaveNoProblems()
        {
            Assert.That(MapGate.Check(File.ReadAllBytes(TerrainPath), TerrainPack()), Is.Empty, "terrain demo");
            Assert.That(MapGate.Check(File.ReadAllBytes(M6Path), M6Pack()), Is.Empty, "m6 demo");
            Assert.That(MapGate.Check(File.ReadAllBytes(M7Path), M6Pack()), Is.Empty, "m7 demo");
        }

        [Test]
        public void M7DescriptionBakesToTheM7DemoMap()
        {
            byte[] bytes = File.ReadAllBytes(M7Path);
            Assert.That(MapDescJson.Write(MapDescJson.Read(bytes)), Is.EqualTo(bytes), "canonical bytes");
            MapBakeResult bake = MapBake.Run(bytes);
            Assert.That(bake.WcMapBytes, Is.EqualTo(M7DemoMap.Bytes()));
            Assert.That(bake.PackId, Is.EqualTo("rwr-m6"));
            Assert.That(bake.Setup.Seed, Is.EqualTo(41UL));
            Assert.That(bake.Setup.CrowdPass, Is.EqualTo("teamIdle"));

            UnitSpawn[] units = DemoRecipes.M7().Units.ToArray();
            Assert.That(bake.Setup.Soldiers.Count, Is.EqualTo(300));
            Assert.That(units.Length, Is.EqualTo(300));
            int reserves = 0;
            for (int i = 0; i < units.Length; i++)
            {
                MapSoldier s = bake.Setup.Soldiers[i];
                Assert.That((s.Key, s.Team, s.Squad, s.XMm, s.ZMm, s.Reserve),
                    Is.EqualTo((units[i].Key, units[i].Team, units[i].Squad, units[i].XMm, units[i].ZMm, units[i].Reserve)), "unit " + i);
                Assert.That(s.Type, Is.EqualTo(s.Team == 1 ? RwrPack.SoldierTeam1 : RwrPack.SoldierTeam2));
                if (s.Reserve) reserves++;
            }
            Assert.That(reserves, Is.EqualTo(100));

            MatchPack match = RwrMatch.CreateM7();
            ControlPointDef[] points = bake.Setup.ControlPoints;
            Assert.That(points.Length, Is.EqualTo(5));
            Assert.That(match.Points.Length, Is.EqualTo(5));
            for (int i = 0; i < points.Length; i++)
            {
                ControlPointDef a = points[i], b = match.Points[i];
                Assert.That((a.Id, a.Center, a.Radius, a.InitialOwner, a.HasSpawn, a.Spawn),
                    Is.EqualTo((b.Id, b.Center, b.Radius, b.InitialOwner, b.HasSpawn, b.Spawn)), "point " + i);
            }
            SpawnAnchor[] bases = bake.Setup.Bases;
            Assert.That(bases.Length, Is.EqualTo(2));
            Assert.That(match.Bases.Length, Is.EqualTo(2));
            for (int i = 0; i < bases.Length; i++)
                Assert.That((bases[i].Team, bases[i].Point), Is.EqualTo((match.Bases[i].Team, match.Bases[i].Point)), "base " + i);
        }

        [Test]
        public void FootprintIsTheInterimUnitFootprint() =>
            Assert.That(MapGate.FootprintRaw, Is.EqualTo(InterimLocomotion.UnitFootprint.Raw));

        [TestCase((byte)0)]
        [TestCase((byte)1)]
        public void LayerProblemAgreesWithTheWorldBuilder(byte bit)
        {
            byte[] desc = new E2Map { LayerBit = bit }.Wall(5, 5, 1, 1).Bytes();
            byte[] map = MapBake.Run(desc).WcMapBytes;
            var recipe = new WorldRecipe(1, MoverKind.InterimPath, new UnitSpawn[0], new BridgeCommand[0]);
            if (bit == 0)
            {
                Assert.That(MapGate.Check(desc, M6Pack()), Is.EqualTo(new[] { P("GATE_LAYER", "layer walls") }));
                Assert.That(Assert.Throws<InvalidOperationException>(() => WorldBuilder.Create(recipe, map)).Message,
                    Is.EqualTo("Wall layers must not share the mover layer."));
            }
            else
            {
                Assert.That(MapGate.Check(desc, M6Pack()), Is.Empty);
                Assert.DoesNotThrow(() => WorldBuilder.Create(recipe, map));
            }
        }

        [Test]
        public void CheckIsReadOnly()
        {
            foreach (byte[] bytes in new[] { E2FixtureTests.MultiProblem().Bytes(), E2FixtureTests.Version2() })
            {
                byte[] copy = (byte[])bytes.Clone();
                IReadOnlyList<MapProblem> first = MapGate.Check(bytes, M6Pack());
                IReadOnlyList<MapProblem> second = MapGate.Check(bytes, M6Pack());
                Assert.That(first.Count, Is.Not.Zero);
                Assert.That(second, Is.EqualTo(first));
                Assert.That(bytes, Is.EqualTo(copy));
            }
        }

        [Test]
        public void PackMismatchIsAnArgumentError()
        {
            byte[] bytes = new E2Map().Bytes();
            Assert.That(MapGate.Check(bytes, M6Pack()), Is.Empty);
            Assert.Throws<ArgumentException>(() => MapGate.Check(bytes, TerrainPack()));
            Assert.Throws<ArgumentNullException>(() => MapGate.Check(null, M6Pack()));
            Assert.Throws<ArgumentNullException>(() => MapGate.Check(bytes, null));
            // Unreadable bytes have no "pack" to compare: the read problem comes back instead of the argument error.
            Assert.That(MapGate.Check(Encoding.UTF8.GetBytes("[]"), TerrainPack()), Is.EqualTo(new[] { P("MAPDESC_FORMAT", line: 1) }));
        }
    }
}
