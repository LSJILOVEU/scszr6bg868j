using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Sim;

namespace WarCore.Tests
{
    // ML-4 acceptance 6: section 12 of a version 3 .wcmap, field by field, the decode-time checks and the check order.
    public sealed class Ml4WcMapTests
    {
        private const long M = Ml4Maps.M;

        // 6 x 4 flat: plate 1 at 3 m on [1, 4) x [1, 3), plate 2 at 6 m on [2, 5) x [1, 3).
        private static WcMap Base() => Ml4Maps.Valid(Ml4Maps.Bake(Ml4Maps.Make(6, 4, Ml4Maps.Flat(1, 1, 1, 3, 2, 3 * M), Ml4Maps.Flat(2, 2, 1, 3, 2, 6 * M))));

        internal static WcMap With(WcMap m, WcMapPlate[] plates, byte[] passability = null, int[] heights = null, string[] names = null, byte[] cells = null) =>
            new WcMap(m.MapId, m.ContentId, m.Min, m.Max, m.ChunkSize, m.FlatHeight, m.Width, m.Depth, m.Layers.ToArray(), m.Walls.ToArray(),
                passability ?? m.Passability.ToArray(), heights ?? (m.HasHeightField ? m.HeightPoints.ToArray() : null),
                names ?? (m.HasSurfaceTable ? m.SurfaceNames.ToArray() : null), cells ?? (m.HasSurfaceTable ? m.SurfaceCells.ToArray() : null), plates);

        internal static WcMapPlate Copy(WcMapPlate p, ulong? id = null, int? x = null, int? z = null, int? w = null, int? d = null, PlateAxis? axis = null,
            int? from = null, int? to = null, int? thick = null, byte? surface = null, byte[] solid = null, byte[] walk = null) =>
            new WcMapPlate(id ?? p.Id, x ?? p.X, z ?? p.Z, w ?? p.Width, d ?? p.Depth, axis ?? p.Axis, from ?? p.FromRaw, to ?? p.ToRaw,
                thick ?? p.ThickRaw, surface ?? p.Surface, solid ?? p.Solid.ToArray(), walk ?? p.Walkable.ToArray());

        internal static string Code(WcMap map) => Assert.Throws<InvalidDataException>(() => WcMapValidation.Validate(map)).Message;

        private static string FirstWith(Func<WcMapPlate, WcMapPlate> change)
        {
            WcMap m = Base();
            return Code(With(m, new[] { change(m.Plates[0]), m.Plates[1] }));
        }

        [Test]
        public void OrderAndIds()
        {
            WcMap m = Base();
            Assert.That(FirstWith(p => Copy(p, id: 0)), Is.EqualTo("WCMAP_PLATE_ORDER"));
            Assert.That(Code(With(m, new[] { m.Plates[1], m.Plates[0] })), Is.EqualTo("WCMAP_PLATE_ORDER"));
            Assert.That(FirstWith(p => Copy(p, id: 2)), Is.EqualTo("WCMAP_PLATE_ORDER"), "equal ids");
        }

        [Test]
        public void GeometryFields()
        {
            Assert.That(FirstWith(p => Copy(p, w: 0, solid: new byte[0], walk: new byte[0])), Is.EqualTo("WCMAP_PLATE_GEOMETRY"));
            Assert.That(FirstWith(p => Copy(p, d: 0, solid: new byte[0], walk: new byte[0])), Is.EqualTo("WCMAP_PLATE_GEOMETRY"));
            Assert.That(FirstWith(p => Copy(p, x: 4)), Is.EqualTo("WCMAP_PLATE_GEOMETRY"), "x + w = 7 > 6");
            Assert.That(FirstWith(p => Copy(p, z: 3)), Is.EqualTo("WCMAP_PLATE_GEOMETRY"), "z + d = 5 > 4");
            Assert.That(FirstWith(p => Copy(p, axis: (PlateAxis)3)), Is.EqualTo("WCMAP_PLATE_GEOMETRY"));
            Assert.That(FirstWith(p => Copy(p, to: p.FromRaw + 1)), Is.EqualTo("WCMAP_PLATE_GEOMETRY"), "flat from != to");
            Assert.That(FirstWith(p => Copy(p, axis: PlateAxis.X)), Is.EqualTo("WCMAP_PLATE_GEOMETRY"), "slope from = to");
            Assert.That(FirstWith(p => Copy(p, thick: 32767)), Is.EqualTo("WCMAP_PLATE_GEOMETRY"));
            Assert.That(FirstWith(p => Copy(p, thick: (int)HeightField.HeightLimitRaw)), Is.EqualTo("WCMAP_PLATE_GEOMETRY"));
            WcMap m = Base();
            Assert.DoesNotThrow(() => WcMapValidation.Validate(With(m, new[] { Copy(m.Plates[0], thick: 32768), m.Plates[1] })));
            Assert.DoesNotThrow(() => WcMapValidation.Validate(With(m, new[] { m.Plates[0], Copy(m.Plates[1], x: 3) })), "x + w = 6 is the edge");
        }

        // Out-of-range heights are BOUNDS before any HeightField is built from them.
        [Test]
        public void HeightsOutsideTheMapAreBounds()
        {
            Assert.That(FirstWith(p => Copy(p, from: -1, to: -1)), Is.EqualTo("WCMAP_BOUNDS"));
            Assert.That(FirstWith(p => Copy(p, axis: PlateAxis.X, to: (int)(10 * M) + 1)), Is.EqualTo("WCMAP_BOUNDS"));
            Assert.That(FirstWith(p => Copy(p, from: int.MaxValue, to: int.MaxValue)), Is.EqualTo("WCMAP_BOUNDS"));
            Assert.That(FirstWith(p => Copy(p, from: int.MinValue, to: int.MinValue)), Is.EqualTo("WCMAP_BOUNDS"));
            WcMap m = Base();
            Assert.DoesNotThrow(() => WcMapValidation.Validate(With(m, new[] { m.Plates[0], Copy(m.Plates[1], from: (int)(10 * M), to: (int)(10 * M)) })), "= Max.Y");
        }

        [Test]
        public void SurfaceIndexNeedsATable()
        {
            Assert.That(FirstWith(p => Copy(p, surface: 0)), Is.EqualTo("WCMAP_PLATE_SURFACE"), "no surface table");
            WcMap m = Base();
            var names = new[] { "grass", "road" };
            var cells = new byte[24];
            Assert.That(Code(With(m, new[] { Copy(m.Plates[0], surface: 2), m.Plates[1] }, names: names, cells: cells)), Is.EqualTo("WCMAP_PLATE_SURFACE"));
            Assert.DoesNotThrow(() => WcMapValidation.Validate(With(m, new[] { Copy(m.Plates[0], surface: 1), m.Plates[1] }, names: names, cells: cells)));
        }

        [Test]
        public void Bitmaps()
        {
            Assert.That(FirstWith(p => Copy(p, solid: new byte[] { 63, 0 })), Is.EqualTo("WCMAP_BITMAP_LENGTH"));
            Assert.That(FirstWith(p => Copy(p, walk: new byte[0])), Is.EqualTo("WCMAP_BITMAP_LENGTH"));
            Assert.That(FirstWith(p => Copy(p, solid: new byte[] { 127 })), Is.EqualTo("WCMAP_BITMAP_LENGTH"), "spare bit in the solid mask");
            Assert.That(FirstWith(p => Copy(p, walk: new byte[] { 255 })), Is.EqualTo("WCMAP_BITMAP_LENGTH"), "spare bit in the walkable mask");
            Assert.That(FirstWith(p => Copy(p, solid: new byte[] { 62 })), Is.EqualTo("WCMAP_PLATE_NAV_MISMATCH"), "walkable on a cell that is not solid");
            Assert.That(FirstWith(p => Copy(p, walk: new byte[] { 62 })), Is.EqualTo("WCMAP_PLATE_NAV_MISMATCH"), "walkable bit off where it should be on");
        }

        // The walkable mask is recomputed: a wall standing on the plate makes its cell not walkable.
        [Test]
        public void WalkableMaskIsRecomputed()
        {
            MapDesc d = Ml4Maps.Make(0, 6, 4, null, new[] { Ml4Maps.Flat(1, 1, 1, 3, 2, 3 * M) }, walls: new[] { Ml4Maps.Wall(1, 2, 1, 1, 1, M, 3 * M) });
            WcMap m = Ml4Maps.Valid(Ml4Maps.Bake(d));
            Assert.That(m.Plates[0].IsWalkable(1, 0), Is.False);
            Assert.That(m.Plates[0].IsWalkable(0, 0), Is.True);
            Assert.That(Code(With(m, new[] { Copy(m.Plates[0], walk: new byte[] { 63 }) })), Is.EqualTo("WCMAP_PLATE_NAV_MISMATCH"));
        }

        // The ground bitmap includes the headroom rule: a plate 1 m up blocks the ground under it.
        [Test]
        public void GroundBitmapIncludesHeadroom()
        {
            WcMap m = Ml4Maps.Valid(Ml4Maps.Bake(Ml4Maps.Make(6, 4, Ml4Maps.Flat(1, 1, 1, 3, 2, M))));
            Assert.That(m.IsPassable(1, 1), Is.False);
            Assert.That(m.IsPassable(0, 0), Is.True);
            byte[] open = m.Passability.ToArray();
            open[(1 * 6 + 1) >> 3] |= (byte)(1 << ((1 * 6 + 1) & 7));
            Assert.That(Code(With(m, m.Plates.ToArray(), passability: open)), Is.EqualTo("WCMAP_NAV_WALL_MISMATCH"));
        }

        // 4097 plates are CAPACITY; so are more solid cells than map cells.
        [Test]
        public void Capacity()
        {
            WcMap m = Base();
            WcMapPlate one = Copy(m.Plates[0], w: 1, d: 1, solid: new byte[] { 1 }, walk: new byte[] { 1 });
            WcMapPlate[] many = Enumerable.Range(1, 4097).Select(i => Copy(one, id: (ulong)i)).ToArray();
            Assert.That(Code(With(m, many)), Is.EqualTo("WCMAP_CAPACITY"));
            WcMapPlate full(ulong id, int y) => new WcMapPlate(id, 0, 0, 6, 4, PlateAxis.Flat, y, y, 32768, WcMapPlate.NoSurface, new byte[] { 255, 255, 255 }, new byte[] { 255, 255, 255 });
            Assert.That(Code(With(m, new[] { full(1, (int)(3 * M)), Copy(m.Plates[1], id: 2, w: 1, d: 1, solid: new byte[] { 1 }, walk: new byte[] { 1 }) })),
                Is.EqualTo("WCMAP_CAPACITY"), "25 solid cells on a 24-cell map");
        }

        // ---- decode-time checks and the version (file bytes) ----

        private static byte[] BaseBytes() => WcMapWriter.Write(Base());

        private static byte[][] Payloads(byte[] bytes)
        {
            var result = new byte[13][];
            using var reader = new BinaryReader(new MemoryStream(bytes));
            reader.BaseStream.Position = 8;
            for (int i = 1; i <= 12; i++)
            {
                Assert.That(reader.ReadUInt16(), Is.EqualTo(i));
                result[i] = reader.ReadBytes((int)reader.ReadUInt32());
                reader.BaseStream.Position += 32;
            }
            return result;
        }

        // The envelope with valid digests around the given payloads; version and declared section count as given.
        private static byte[] Pack(byte[][] payloads, ushort version = 3, ushort declared = 12)
        {
            Func<SHA256> factory = SHA256.Create;
            using SHA256 sha = factory();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(WcMapWriter.Magic); writer.Write(version); writer.Write(declared);
            for (ushort id = 1; id <= 12; id++)
            {
                int start = (int)stream.Position;
                writer.Write(id); writer.Write((uint)payloads[id].Length); writer.Write(payloads[id]);
                writer.Write(sha.ComputeHash(stream.ToArray(), start, payloads[id].Length + 6));
            }
            writer.Write(sha.ComputeHash(stream.ToArray()));
            return stream.ToArray();
        }

        private static string ReadError(byte[] bytes) => Assert.Throws<InvalidDataException>(() => WcMapReader.Read(bytes)).Message;

        // Section 12 with its first plate's fields patched (offsets inside the payload: count 0, id 4, x 12, z 14, w 16, d 18).
        private static byte[] Patched(Action<BinaryWriter> write)
        {
            byte[][] p = Payloads(BaseBytes());
            using (var stream = new MemoryStream(p[12], true))
            using (var w = new BinaryWriter(stream)) write(w);
            return Pack(p);
        }

        [Test]
        public void PackedBaseReadsBack()
        {
            Assert.That(Pack(Payloads(BaseBytes())), Is.EqualTo(BaseBytes()), "the test envelope is the writer's");
        }

        [Test]
        public void VersionThreeWithoutPlatesIsAVersionError()
        {
            byte[][] p = Payloads(BaseBytes());
            p[12] = new byte[4];
            Assert.That(ReadError(Pack(p)), Is.EqualTo("WCMAP_VERSION"));
        }

        [Test]
        public void SectionCountFollowsTheVersion()
        {
            Assert.That(ReadError(Pack(Payloads(BaseBytes()), 3, 11)), Is.EqualTo("WCMAP_SECTION_COUNT"));
            Assert.That(ReadError(Pack(Payloads(BaseBytes()), 2, 12)), Is.EqualTo("WCMAP_SECTION_COUNT"));
            Assert.That(ReadError(Pack(Payloads(BaseBytes()), 4, 12)), Is.EqualTo("WCMAP_VERSION"));
        }

        [Test]
        public void DecodeChecksCountAndRectangleBeforeTheMasks()
        {
            Assert.That(ReadError(Patched(w => w.Write(4097u))), Is.EqualTo("WCMAP_CAPACITY"));
            Assert.That(ReadError(Patched(w => { w.Seek(16, SeekOrigin.Begin); w.Write((ushort)0); })), Is.EqualTo("WCMAP_PLATE_GEOMETRY"));
            Assert.That(ReadError(Patched(w => { w.Seek(18, SeekOrigin.Begin); w.Write((ushort)0); })), Is.EqualTo("WCMAP_PLATE_GEOMETRY"));
            Assert.That(ReadError(Patched(w => { w.Seek(12, SeekOrigin.Begin); w.Write((ushort)4); })), Is.EqualTo("WCMAP_PLATE_GEOMETRY"), "x + w > width");
            Assert.That(ReadError(Patched(w => { w.Seek(14, SeekOrigin.Begin); w.Write((ushort)3); })), Is.EqualTo("WCMAP_PLATE_GEOMETRY"), "z + d > depth");
            Assert.That(ReadError(Patched(w => { w.Seek(16, SeekOrigin.Begin); w.Write((ushort)65535); })), Is.EqualTo("WCMAP_PLATE_GEOMETRY"), "no 8 KB mask read");
            Assert.That(ReadError(Patched(w => { w.Seek(20, SeekOrigin.Begin); w.Write((byte)3); })), Is.EqualTo("WCMAP_PLATE_GEOMETRY"), "axis, from Validate");
        }

        [Test]
        public void MasksAreReadToTheEndOfTheSection()
        {
            byte[][] p = Payloads(BaseBytes());
            byte[][] shortP = (byte[][])p.Clone();
            shortP[12] = p[12].Take(p[12].Length - 1).ToArray();
            Assert.That(ReadError(Pack(shortP)), Is.EqualTo("WCMAP_TRUNCATED"));
            byte[][] longP = (byte[][])p.Clone();
            longP[12] = p[12].Concat(new byte[] { 0 }).ToArray();
            Assert.That(ReadError(Pack(longP)), Is.EqualTo("WCMAP_PAYLOAD_LENGTH"));
        }

        // ---- check order: a file breaking two rules reports the earlier one (adjacent steps) ----

        [Test]
        public void FieldsBeforeTheHeightTable()
        {
            MapDesc d = Ml4Maps.Make(0, 6, 4, Ml4Maps.Ground(6, 4, 0), new[] { Ml4Maps.Flat(1, 1, 1, 3, 2, 3 * M) });
            WcMap m = Ml4Maps.Valid(Ml4Maps.Bake(d));
            int[] steep = m.HeightPoints.ToArray();
            steep[0] = (int)(5 * M);
            Assert.That(Code(With(m, m.Plates.ToArray(), heights: steep)), Is.EqualTo("WCMAP_HEIGHT_SLOPE"));
            Assert.That(Code(With(m, new[] { Copy(m.Plates[0], thick: 1) }, heights: steep)), Is.EqualTo("WCMAP_PLATE_GEOMETRY"));
        }

        [Test]
        public void HeightTableBeforePlateSlope()
        {
            MapDesc d = Ml4Maps.Make(0, 6, 4, Ml4Maps.Ground(6, 4, 0), new[] { Ml4Maps.Flat(1, 1, 1, 3, 2, 3 * M) });
            WcMap m = Ml4Maps.Valid(Ml4Maps.Bake(d));
            int[] steep = m.HeightPoints.ToArray();
            steep[0] = (int)(5 * M);
            WcMapPlate tooSteep = Copy(m.Plates[0], axis: PlateAxis.X, from: 0, to: (int)(7 * M));
            Assert.That(Code(With(m, new[] { tooSteep })), Is.EqualTo("WCMAP_PLATE_SLOPE"));
            Assert.That(Code(With(m, new[] { tooSteep }, heights: steep)), Is.EqualTo("WCMAP_HEIGHT_SLOPE"));
        }

        [Test]
        public void PlateSlopeBeforeTheGroundBitmap()
        {
            WcMap m = Base();
            byte[] closed = new byte[m.Passability.Count];
            WcMapPlate tooSteep = Copy(m.Plates[1], axis: PlateAxis.X, from: (int)(3 * M), to: (int)(10 * M));
            Assert.That(Code(With(m, new[] { m.Plates[0], m.Plates[1] }, passability: closed)), Is.EqualTo("WCMAP_NAV_WALL_MISMATCH"));
            Assert.That(Code(With(m, new[] { m.Plates[0], tooSteep }, passability: closed)), Is.EqualTo("WCMAP_PLATE_SLOPE"));
        }

        [Test]
        public void GroundBitmapBeforeThePlateRules()
        {
            WcMap m = Base();
            byte[] closed = new byte[m.Passability.Count];
            WcMapPlate low = Copy(m.Plates[1], from: (int)(3 * M + M / 2), to: (int)(3 * M + M / 2));
            Assert.That(Code(With(m, new[] { m.Plates[0], low })), Is.EqualTo("WCMAP_PLATE_STACK"));
            Assert.That(Code(With(m, new[] { m.Plates[0], low }, passability: closed)), Is.EqualTo("WCMAP_NAV_WALL_MISMATCH"));
        }

        private static MapDesc OnHill(MapPlate[] plates, params MapWall[] walls) => Ml4Maps.Make(0, 6, 4, Ml4Maps.Ground(6, 4, M), plates, walls: walls);

        [Test]
        public void GroundBitmapBeforeWallTerrainBeforeBuried()
        {
            WcMap both = Ml4Maps.Bake(OnHill(new[] { Ml4Maps.Flat(1, 1, 1, 3, 2, M - 1) }, Ml4Maps.Wall(1, 0, 0, 1, 1, 2 * M - 2)));
            Assert.That(Code(both), Is.EqualTo("WCMAP_WALL_TERRAIN"));
            Assert.That(Code(Ml4Maps.Bake(OnHill(new[] { Ml4Maps.Flat(1, 1, 1, 3, 2, M - 1) }))), Is.EqualTo("WCMAP_PLATE_BURIED"));
            Assert.That(Code(With(both, both.Plates.ToArray(), passability: new byte[both.Passability.Count])), Is.EqualTo("WCMAP_NAV_WALL_MISMATCH"));
        }

        [Test]
        public void BuriedBeforeStack()
        {
            Assert.That(Code(Ml4Maps.Bake(OnHill(new[] { Ml4Maps.Flat(1, 1, 1, 3, 2, M - 1), Ml4Maps.Flat(2, 2, 1, 3, 2, 2 * M) }))), Is.EqualTo("WCMAP_PLATE_BURIED"));
            Assert.That(Code(Ml4Maps.Bake(OnHill(new[] { Ml4Maps.Flat(1, 1, 1, 3, 2, M), Ml4Maps.Flat(2, 2, 1, 3, 2, 2 * M) }))), Is.EqualTo("WCMAP_PLATE_STACK"));
        }

        [Test]
        public void StackBeforeWallThrough()
        {
            MapWall tall = Ml4Maps.Wall(1, 1, 2, 1, 1, 7 * M);
            Assert.That(Code(Ml4Maps.Bake(OnHill(new[] { Ml4Maps.Flat(1, 1, 1, 3, 2, 4 * M), Ml4Maps.Flat(2, 2, 1, 3, 2, 5 * M) }, tall))), Is.EqualTo("WCMAP_PLATE_STACK"));
            Assert.That(Code(Ml4Maps.Bake(OnHill(new[] { Ml4Maps.Flat(1, 1, 1, 3, 2, 4 * M), Ml4Maps.Flat(2, 2, 1, 3, 2, 7 * M) }, tall))), Is.EqualTo("WCMAP_WALL_PLATE"));
        }

        [Test]
        public void WallThroughBeforeWalkable()
        {
            WcMap through = Ml4Maps.Bake(OnHill(new[] { Ml4Maps.Flat(1, 1, 1, 3, 2, 4 * M) }, Ml4Maps.Wall(1, 1, 2, 1, 1, 7 * M)));
            Assert.That(Code(With(through, new[] { Copy(through.Plates[0], walk: new byte[] { 0 }) })), Is.EqualTo("WCMAP_WALL_PLATE"));
            WcMap ok = Ml4Maps.Valid(Ml4Maps.Bake(OnHill(new[] { Ml4Maps.Flat(1, 1, 1, 3, 2, 4 * M) })));
            Assert.That(Code(With(ok, new[] { Copy(ok.Plates[0], walk: new byte[] { 0 }) })), Is.EqualTo("WCMAP_PLATE_NAV_MISMATCH"));
        }

        [Test]
        public void WalkableBeforeTheSurfaceTable()
        {
            WcMap m = Base();
            var names = new[] { "road", "grass" };
            Assert.That(Code(With(m, m.Plates.ToArray(), names: names, cells: new byte[24])), Is.EqualTo("WCMAP_SURFACE_ORDER"));
            Assert.That(Code(With(m, new[] { m.Plates[0], Copy(m.Plates[1], walk: new byte[] { 0 }) }, names: names, cells: new byte[24])), Is.EqualTo("WCMAP_PLATE_NAV_MISMATCH"));
        }
    }
}
