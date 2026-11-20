using System;
using System.Collections.Generic;
using WarCore.Content.MapTools;
using static WarCore.Tests.E3Fixtures;
using static WarCore.Tests.E3Plans;

namespace WarCore.Tests
{
    // A seeded integer generator for the ED-1 tests (no System.Random, so the sequences never change with the runtime).
    internal sealed class Ed1Rng
    {
        private ulong s;
        internal Ed1Rng(ulong seed) { s = seed * 0x9E3779B97F4A7C15UL + 1; }
        internal int Next(int n)
        {
            s = s * 6364136223846793005UL + 1442695040888963407UL;
            return (int)((s >> 33) % (ulong)n);
        }
    }

    // One recorded edit: its kind and the call with every value fixed.
    internal sealed class Ed1Op
    {
        internal readonly string Kind;
        internal readonly Action<MapEdit> Apply;
        internal Ed1Op(string kind, Action<MapEdit> apply) { Kind = kind; Apply = apply; }
    }

    internal static class Ed1Fixtures
    {
        internal const long M = Ml4Maps.M;

        internal static byte[] Bytes(MapDesc desc) => MapDescJson.Write(desc);

        // The E3 8 x 6 map (heights 0, all grass, table grass, mud, road) with three hand walls; plus two hand plates (ids 1
        // and 2), hand cutout 1 in plate 1, and stamp "door" (heights, "mud" cells, two walls, one plate).
        internal static MapDesc Start()
        {
            var e = new MapEdit(Read(ThreeWalls()));
            e.AddPlate(Ml4Maps.Flat(0, 0, 0, 4, 3, 3 * M));
            e.AddPlate(Ml4Maps.Flat(0, 4, 3, 4, 3, 3 * M));
            e.AddCutout(Ml4Maps.Cut(0, 1, 1, 1, 1, 1));
            e.Stamp(Door());
            return e.Current;
        }

        internal static MapStampPlan Door() => new MapStampPlan("door", Corners(5, 7, 0, 2, 500), Cells(5, 6, 0, 1, "mud"),
            new[] { new MapStampEntry<MapWall>(1, WallAt(5, 2)), new MapStampEntry<MapWall>(2, WallAt(7, 2)) },
            new MapStampEntry<MapPoint>[0], new MapStampEntry<MapBase>[0], new MapStampEntry<MapUnit>[0],
            new[] { new MapStampEntry<MapPlate>(1, Ml4Maps.Flat(99, 5, 0, 3, 2, 2 * M)) }, new MapStampEntry<MapCutout>[0]);

        // The kinds Good draws from; the equivalence test checks each one was drawn.
        internal static readonly string[] GoodKinds =
        {
            "height", "surface", "name", "wall", "unit", "point", "base", "plate", "cutout", "replaceWall", "replacePlate",
            "removeWall", "removeCutoutOrPlate", "release", "stamp",
        };

        private static readonly string[] NamePool = { "aaa", "dirt", "sand", "zzz", "grass" };

        // 1 to 30 edits drawn for seed against a probe that does them one by one, so each edit names entries that exist at
        // its turn. When seed % 8 == 3 one edit at a drawn position is one that throws; the list ends at the first edit the
        // probe refused.
        internal static List<Ed1Op> Sequence(ulong seed, MapDesc start)
        {
            var rng = new Ed1Rng(seed);
            var probe = new MapEdit(start);
            int length = 1 + rng.Next(30);
            int bad = seed % 8 == 3 ? rng.Next(length) : -1;
            var ops = new List<Ed1Op>();
            for (int k = 0; k < length; k++)
            {
                Ed1Op op = k == bad ? Bad(rng) : Good(rng, probe.Current);
                ops.Add(op);
                try { op.Apply(probe); }
                catch (Exception) { break; }
            }
            return ops;
        }

        private static Ed1Op Bad(Ed1Rng rng)
        {
            switch (rng.Next(4))
            {
                case 0: return new Ed1Op("bad", e => e.Remove("wall", 999999));
                case 1: return new Ed1Op("bad", e => e.SetSurface(0, 0, "nope"));
                case 2: return new Ed1Op("bad", e => e.AddCutout(Ml4Maps.Cut(0, 999999, 0, 0, 1, 1)));
                default: return new Ed1Op("bad", e => e.SetHeight(W + 1, 0, 0));
            }
        }

        private static Ed1Op Good(Ed1Rng rng, MapDesc d)
        {
            int x = rng.Next(W), z = rng.Next(D);
            switch (rng.Next(16))
            {
                case 0:
                case 1:
                {
                    int i = rng.Next(W + 1), j = rng.Next(D + 1), raw = rng.Next(3001);
                    return new Ed1Op("height", e => e.SetHeight(i, j, raw));
                }
                case 2:
                {
                    string name = d.Surfaces.Names[rng.Next(d.Surfaces.Names.Count)];
                    return new Ed1Op("surface", e => e.SetSurface(x, z, name));
                }
                case 3:
                {
                    string name = NamePool[rng.Next(NamePool.Length)];
                    return new Ed1Op("name", e => e.AddSurfaceName(name));
                }
                case 4: return new Ed1Op("wall", e => e.AddWall(WallAt(x, z)));
                case 5:
                {
                    long ux = rng.Next(W * 1000), uz = rng.Next(D * 1000);
                    return new Ed1Op("unit", e => e.AddUnit(UnitAt(ux, uz)));
                }
                case 6: return new Ed1Op("point", e => e.AddPoint(PointAt(x * 1000 + 500, z * 1000 + 500)));
                case 7: return new Ed1Op("base", e => e.AddBase(BaseAt(x * 1000 + 500, z * 1000 + 500)));
                case 8:
                {
                    int px = rng.Next(W - 2), pz = rng.Next(D - 2), w = 1 + rng.Next(2), dd = 1 + rng.Next(2);
                    long y = (1 + rng.Next(4)) * M;
                    return new Ed1Op("plate", e => e.AddPlate(Ml4Maps.Flat(0, px, pz, w, dd, y)));
                }
                case 9:
                {
                    if (d.Plates.Count == 0) return new Ed1Op("height", e => e.SetHeight(0, 0, 1));
                    ulong plate = d.Plates[rng.Next(d.Plates.Count)].Id;
                    return new Ed1Op("cutout", e => e.AddCutout(Ml4Maps.Cut(0, plate, x, z, 1, 1)));
                }
                case 10:
                {
                    if (d.Walls.Count == 0) return new Ed1Op("wall", e => e.AddWall(WallAt(x, z)));
                    ulong id = d.Walls[rng.Next(d.Walls.Count)].Id;
                    return new Ed1Op("replaceWall", e => e.ReplaceWall(WallAt(x, z, id)));
                }
                case 11:
                {
                    if (d.Plates.Count == 0) return new Ed1Op("plate", e => e.AddPlate(Ml4Maps.Flat(0, 0, 0, 1, 1, M)));
                    ulong id = d.Plates[rng.Next(d.Plates.Count)].Id;
                    int px = rng.Next(W - 2), pz = rng.Next(D - 2);
                    return new Ed1Op("replacePlate", e => e.ReplacePlate(Ml4Maps.Flat(id, px, pz, 2, 2, 2 * M)));
                }
                case 12:
                {
                    if (d.Walls.Count == 0) return new Ed1Op("wall", e => e.AddWall(WallAt(x, z)));
                    ulong id = d.Walls[rng.Next(d.Walls.Count)].Id;
                    return new Ed1Op("removeWall", e => e.Remove("wall", id));
                }
                case 13:
                {
                    if (d.Cutouts.Count > 0)
                    {
                        ulong cut = d.Cutouts[rng.Next(d.Cutouts.Count)].Id;
                        return new Ed1Op("removeCutoutOrPlate", e => e.Remove("cutout", cut));
                    }
                    if (d.Plates.Count == 0) return new Ed1Op("plate", e => e.AddPlate(Ml4Maps.Flat(0, 0, 0, 1, 1, M)));
                    ulong plate = d.Plates[rng.Next(d.Plates.Count)].Id;
                    return new Ed1Op("removeCutoutOrPlate", e => e.Remove("plate", plate));
                }
                case 14:
                {
                    int x1 = x + rng.Next(W - x), z1 = z + rng.Next(D - z);
                    return new Ed1Op("release", e => e.ReleaseHand(x, z, x1, z1));
                }
                default:
                {
                    int which = rng.Next(3);
                    return new Ed1Op("stamp", e => e.Stamp(which == 0 ? S1() : which == 1 ? S2() : Door()));
                }
            }
        }

        // The edits one by one: the first exception, or null when all succeeded.
        internal static Exception OneByOne(MapEdit edit, List<Ed1Op> ops)
        {
            foreach (Ed1Op op in ops)
            {
                try { op.Apply(edit); }
                catch (Exception e) { return e; }
            }
            return null;
        }
    }
}
