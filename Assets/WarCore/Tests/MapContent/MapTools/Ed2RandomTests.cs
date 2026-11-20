using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using WarCore.Content.MapTools;
using static WarCore.Tests.E3Plans;
using static WarCore.Tests.Ed2Fixtures;

namespace WarCore.Tests
{
    // ED-2 acceptance 5: 200 random brushes on one map; every result bakes. The map has no walls or plates: brushes do not
    // avoid them (see TheBrushesDoNotAvoidWalls).
    public sealed class Ed2RandomTests
    {
        private static readonly string[] Kinds = { "raise", "flatten", "smooth", "ramp", "bank", "circle", "square" };
        private static readonly string[] Names = { "grass", "mud", "road" };

        // 64 x 48, rolling ground below 1/2, a terrain stamp (1 m, "mud") over points 10..30.
        private static MapDesc Start()
        {
            var e = new MapEdit(Map(64, 48, (i, j) => (int)(S / 16 * ((i * 7 + j * 13) % 9)), 0, 40 * S));
            e.AddSurfaceName("mud");
            e.Stamp(new MapStampPlan(MapNew.TerrainInstance, Corners(10, 30, 10, 30, (int)S), Cells(10, 29, 10, 29, "mud"),
                new MapStampEntry<MapWall>[0], new MapStampEntry<MapPoint>[0], new MapStampEntry<MapBase>[0], new MapStampEntry<MapUnit>[0]));
            return e.Current;
        }

        [Test]
        public void TwoHundredBrushesKeepTheMapBakeable()
        {
            var rng = new Ed1Rng(77);
            MapDesc d = Start();
            AllLegal(d);
            var drawn = new Dictionary<string, int>();
            int refused = 0;
            for (int n = 0; n < 200; n++)
            {
                string kind = Kinds[rng.Next(Kinds.Length)];
                int i = rng.Next(80) - 8, j = rng.Next(64) - 8, r = 1 + rng.Next(12);
                int amount = rng.Next((int)(6 * S)) - (int)(3 * S);
                MapHeightWrite[] heights = null;
                MapSurfaceWrite[] surfaces = null;
                string name = Names[rng.Next(Names.Length)];
                try
                {
                    switch (kind)
                    {
                        case "raise": heights = MapBrush.Raise(d, i, j, r, amount); break;
                        case "flatten": heights = MapBrush.Flatten(d, i, j, r, rng.Next((int)(4 * S))); break;
                        case "smooth": heights = MapBrush.Smooth(d, i, j, r); break;
                        case "ramp":
                        {
                            int i1 = i + rng.Next(25) - 12, j1 = j + rng.Next(25) - 12;
                            if (i1 == i && j1 == j) i1++;
                            int h0 = rng.Next((int)(3 * S)), h1 = rng.Next((int)(3 * S));
                            heights = MapBrush.Ramp(d, i, j, h0, i1, j1, h1, rng.Next(4), 3, 4);
                            break;
                        }
                        case "bank":
                        {
                            int i1 = i + rng.Next(25) - 12, j1 = j + rng.Next(25) - 12;
                            if (i1 == i && j1 == j) j1++;
                            heights = MapBrush.Bank(d, i, j, i1, j1, 1 + rng.Next(6), amount);
                            break;
                        }
                        case "circle": surfaces = MapBrush.SurfaceCircle(d, i, j, rng.Next(10), name); break;
                        default: surfaces = MapBrush.SurfaceSquare(d, i, j, rng.Next(10), name); break;
                    }
                }
                catch (ArgumentException) when (kind == "ramp")
                {
                    refused++;
                    continue;
                }
                drawn[kind] = drawn.TryGetValue(kind, out int k) ? k + 1 : 1;

                var e = new MapEdit(d);
                var written = new HashSet<(int, int)>();
                if (heights != null)
                    foreach (MapHeightWrite w in heights)
                    {
                        Assert.That(w.I >= 0 && w.J >= 0 && w.I <= 64 && w.J <= 48, Is.True, "inside " + n);
                        Assert.That(w.Raw, Is.Not.EqualTo(d.Heights[w.J][w.I]), "changed " + n);
                        Assert.That(written.Add((w.I, w.J)), Is.True, "once " + n);
                    }
                else
                    foreach (MapSurfaceWrite w in surfaces)
                    {
                        Assert.That(w.X >= 0 && w.Z >= 0 && w.X < 64 && w.Z < 48, Is.True, "inside " + n);
                        Assert.That(w.Name, Is.Not.EqualTo(d.Surfaces.Names[MapEditDraft.OwnerIndex(d.Surfaces.Rows[w.Z][w.X])]), "changed " + n);
                        Assert.That(written.Add((w.X, w.Z)), Is.True, "once " + n);
                    }
                e.Batch(b =>
                {
                    if (heights != null) foreach (MapHeightWrite w in heights) b.SetHeight(w.I, w.J, w.Raw);
                    else if (surfaces.Length > 0)
                    {
                        b.AddSurfaceName(name);
                        foreach (MapSurfaceWrite w in surfaces) b.SetSurface(w.X, w.Z, w.Name);
                    }
                });
                MapDesc next = e.Current;
                // Unwritten points and cells keep their owners.
                if (heights != null)
                {
                    for (int pj = 0; pj <= 48; pj++)
                        for (int pi = 0; pi <= 64; pi++)
                            Assert.That(next.HeightOwner[pj][pi], Is.EqualTo(written.Contains((pi, pj)) ? 'A' : d.HeightOwner[pj][pi]), "owner " + n);
                }
                else
                {
                    for (int z = 0; z < 48; z++)
                        for (int x = 0; x < 64; x++)
                            Assert.That(next.SurfaceOwner[z][x], Is.EqualTo(written.Contains((x, z)) ? 'A' : d.SurfaceOwner[z][x]), "owner " + n);
                }
                d = next;
                byte[] bytes = MapDescJson.Write(d);
                Assert.DoesNotThrow(() => MapBake.Run(bytes), "brush " + n + " " + kind);
            }
            AllLegal(d);
            foreach (string kind in Kinds) Assert.That(drawn.ContainsKey(kind), Is.True, kind);
            Assert.That(refused, Is.LessThan(drawn["ramp"] * 3), "most ramps fit");
        }

        [Test]
        public void TheBrushesDoNotAvoidWalls()
        {
            // A floor wall 2 m tall at cell (4, 4): raising the ground under it to 3 m is written, and the bake reports it.
            var e = new MapEdit(Map(8, 8, (i, j) => 0, 0, 4 * S));
            e.AddWall(E3Fixtures.WallAt(4, 4));
            MapDesc d = e.Current;
            Assert.DoesNotThrow(() => MapBake.Run(MapDescJson.Write(d)));
            MapDesc raised = Apply(d, MapBrush.Flatten(d, 4, 4, 2, (int)(3 * S)));
            var ex = Assert.Throws<InvalidDataException>(() => MapBake.Run(MapDescJson.Write(raised)));
            StringAssert.Contains("WALL_TERRAIN", ex.Message);
        }
    }
}
