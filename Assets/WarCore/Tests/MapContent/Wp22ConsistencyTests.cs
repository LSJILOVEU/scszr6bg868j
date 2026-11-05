using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Hard requirement: visible terrain, collision/ground, navigation and anchors come from one source and agree.
    // The checker compares each PROJECTION of the file (viewer, ground query, derived tables, anchors) with the file itself,
    // so it flags a projection that drifted but not a map that was changed consistently everywhere.
    public sealed class Wp22ConsistencyTests
    {
        private const long S = SimFix.Scale;

        private sealed class Projections
        {
            internal Func<int, int, long> View, Ground;
            internal Func<int, bool> Walkable;
            internal Func<int, int> Speed, Cost, Surface;
            internal Func<int, SimFix> AnchorY;
        }

        private sealed class Rig
        {
            internal WcMap File; internal ContentPack Pack; internal ViewMap View; internal TerrainTables Tables; internal SimVec3[] Anchors;

            internal static Rig Build(WcMap source, SimVec3[] anchors = null)
            {
                byte[] bytes = WcMapWriter.Write(source);
                var rig = new Rig { File = WcMapReader.Read(bytes), View = ViewMap.Parse(bytes), Anchors = anchors ?? TerrainDemoMap.Spawns.Concat(TerrainDemoMap.ControlPoints).ToArray() };
                rig.Pack = ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string> { [TerrainDemoMap.PackId] = TerrainDemoMap.PackJson }, FileContentSource.Rwr()), TerrainDemoMap.PackId);
                rig.Tables = TerrainDerivation.Derive(rig.File, rig.Pack);
                return rig;
            }

            internal Projections Real()
            {
                HeightField g = Tables.Ground;
                return new Projections
                {
                    View = (i, j) => View.VertexRaw(i, j),
                    Ground = (i, j) => i < g.Width && j < g.Depth
                        ? (g.TryHeightAt(SimFix.FromRaw(File.Min.X.Raw + i * S), SimFix.FromRaw(File.Min.Z.Raw + j * S), out SimFix h) ? h.Raw : long.MinValue)
                        : g.VertexRaw(i, j),
                    Walkable = key => Tables.Nav.IsPassable(key % Tables.Nav.Width, key / Tables.Nav.Width),
                    Speed = key => Tables.SpeedMilliAt(key),
                    Cost = key => Tables.CostAt(key),
                    Surface = key => Tables.SurfaceIndexAt(key),
                    AnchorY = a => Anchors[a].Y,
                };
            }
        }

        private static List<string> Check(Rig rig, Projections p)
        {
            var problems = new List<string>();
            int w = (int)rig.File.Width, d = (int)rig.File.Depth;
            for (int j = 0; j <= d; j++)
                for (int i = 0; i <= w; i++)
                {
                    long file = rig.File.VertexRaw(i, j);
                    if (p.View(i, j) != file) problems.Add("view height " + i + "," + j);
                    if (p.Ground(i, j) != file) problems.Add("ground height " + i + "," + j);
                }
            for (int key = 0; key < w * d; key++)
            {
                int x = key % w, z = key / w;
                SurfaceDef def = Def(rig, key);
                bool expected = rig.File.IsPassable(x, z) && def.Passable
                    && Wp22Oracle.CellWithin(rig.File.VertexRaw(x, z), rig.File.VertexRaw(x + 1, z), rig.File.VertexRaw(x, z + 1), rig.File.VertexRaw(x + 1, z + 1),
                        rig.Pack.Terrain.MaxWalkableNum, rig.Pack.Terrain.MaxWalkableDen);
                if (p.Walkable(key) != expected) problems.Add("walkable " + x + "," + z);
                if (p.Speed(key) != (expected ? def.SpeedMilli : 0)) problems.Add("speed " + x + "," + z);
                if (p.Cost(key) != (expected ? def.Cost : 0)) problems.Add("cost " + x + "," + z);
                if (p.Surface(key) != rig.File.SurfaceCells[key]) problems.Add("surface " + x + "," + z);
            }
            for (int a = 0; a < rig.Anchors.Length; a++)
            {
                SimVec3 at = rig.Anchors[a];
                rig.File.TryCell(at.X, at.Z, out int cx, out int cz);
                if (!p.Walkable(cz * w + cx)) problems.Add("anchor " + a + " cell not walkable");
                // Y must be what the ground query says at the anchor, and that must be the file's surface there.
                rig.Tables.Ground.TryHeightAt(at.X, at.Z, out SimFix ground);
                if (p.AnchorY(a) != ground) problems.Add("anchor " + a + " height");
            }
            return problems;
        }

        private static SurfaceDef Def(Rig rig, int key)
        {
            rig.Pack.Terrain.TryFind(rig.File.SurfaceNames[rig.File.SurfaceCells[key]], out SurfaceDef def);
            return def;
        }

        [Test]
        public void Demo_AllProjectionsAgree()
        {
            Rig rig = Rig.Build(TerrainDemoMap.Build());
            Assert.That(Check(rig, rig.Real()), Is.Empty);
        }

        [Test]
        public void FrozenFixture_BytesAndHandComputedAnchors()
        {
            byte[] bytes = WcMapWriter.Write(TerrainDemoMap.Build());
            // Frozen: any change to the demo (heights, surfaces, wall, layout) must be a deliberate edit of these values.
            Assert.That(Wp22Envelope.Sha256Hex(bytes), Is.EqualTo(FrozenFileSha256), "whole file");
            Assert.That(ViewMap.Parse(bytes).FileHash, Is.EqualTo(FrozenStoredHash), "stored trailing hash");
            WcMap map = WcMapReader.Read(bytes);
            for (int j = 0; j <= 32; j++)
                for (int i = 0; i <= 48; i++)
                    Assert.That(map.VertexRaw(i, j), Is.EqualTo(TerrainDemoMap.HeightUnits(i, j) * TerrainDemoMap.Unit));
            // The crest anchor's Y is a hand-computed constant (5.75 m), not read back from the code under test.
            Assert.That(TerrainDemoMap.ControlPoints[0].Y.Raw, Is.EqualTo(376832));
            Assert.That(TerrainDemoMap.ControlPoints[0].Y.Raw, Is.EqualTo(23 * S / 4));
        }

        internal const string FrozenFileSha256 = "58B2207FC0B492F32AC7AF901859350018B79DF2A9B0F0294436E45361D6B882";
        internal const string FrozenStoredHash = "7285227CF97F29135AB9EAB5698539657182D10D965D88CADFD27F3FBA8C33FD";

        // Each corruption touches ONE projection; the file is untouched. The checker must report it.
        [Test]
        public void SingleProjectionCorruptions_AreAllCaught()
        {
            Rig rig = Rig.Build(TerrainDemoMap.Build());
            Projections real = rig.Real();
            int key = 16 * 48 + 22;

            Projections p = rig.Real(); Func<int, int, long> view = real.View;
            p.View = (i, j) => i == 10 && j == 10 ? view(i, j) + 1 : view(i, j);
            Assert.That(Check(rig, p), Is.EqualTo(new[] { "view height 10,10" }));

            p = rig.Real(); Func<int, bool> walk = real.Walkable;
            p.Walkable = k => k == 5 * 48 + 5 ? !walk(k) : walk(k);
            Assert.That(Check(rig, p), Is.EqualTo(new[] { "walkable 5,5" }));

            p = rig.Real(); Func<int, SimFix> y = real.AnchorY;
            p.AnchorY = a => a == 2 ? SimFix.FromRaw(y(a).Raw + 1) : y(a);
            Assert.That(Check(rig, p), Is.EqualTo(new[] { "anchor 2 height" }));

            p = rig.Real(); Func<int, int> speed = real.Speed;
            p.Speed = k => k == key ? speed(k) + 1 : speed(k);
            Assert.That(Check(rig, p), Is.EqualTo(new[] { "speed 22,16" }));

            // A surface table re-sorted without remapping the cell indices: mud and road swapped in the projection only.
            p = rig.Real(); Func<int, int> surface = real.Surface;
            p.Surface = k => surface(k) == 1 ? 2 : surface(k) == 2 ? 1 : surface(k);
            Assert.That(Check(rig, p).Count, Is.GreaterThan(0));
            Assert.That(Check(rig, p).All(s => s.StartsWith("surface ")), Is.True);

            p = rig.Real(); Func<int, int, long> ground = real.Ground;
            p.Ground = (i, j) => i == 3 && j == 4 ? ground(i, j) - 1 : ground(i, j);
            Assert.That(Check(rig, p), Is.EqualTo(new[] { "ground height 3,4" }));
        }

        [Test]
        public void ChangingTheFileConsistentlyEverywhere_IsNotAFalseAlarm()
        {
            WcMap map = TerrainDemoMap.Build();
            int[] points = map.HeightPoints.ToArray();
            points[5 * 49 + 5] += 1;                                        // far from every anchor
            var changed = new WcMap(map.MapId, map.ContentId, map.Min, map.Max, map.ChunkSize, map.FlatHeight, map.Width, map.Depth,
                map.Layers.ToArray(), map.Walls.ToArray(), map.Passability.ToArray(), points, map.SurfaceNames.ToArray(), map.SurfaceCells.ToArray());
            Rig rig = Rig.Build(changed);
            Assert.That(Check(rig, rig.Real()), Is.Empty);
        }

        [Test]
        public void AnchorsAreOnWalkableCells_AndTheirYIsTheGroundHeight()
        {
            Rig rig = Rig.Build(TerrainDemoMap.Build());
            foreach (SimVec3 a in rig.Anchors)
            {
                Assert.That(rig.Tables.Ground.TryHeightAt(a.X, a.Z, out SimFix ground), Is.True);
                Assert.That(a.Y, Is.EqualTo(ground));
                Assert.That(rig.Tables.Nav.TryCell(a, out int key), Is.True);
                Assert.That(rig.Tables.Nav.IsPassable(key % 48, key / 48), Is.True);
            }
        }
    }
}
