using System;
using System.Collections.Generic;
using System.IO;
using WarCore.Content;
using WarCore.Content.MapTools;

namespace WarCore.Presentation
{
    // A map description (bytes) + its content pack + a palette + the message table -> the data of a flat picture of the map
    // and of its gate problems. For command-line tools (it takes a ContentPack); Unity gets its preview data from the
    // editor facade. No state: the same inputs give the same data.
    public static partial class MapPreview
    {
        public const string GroundFallbackKey = "MAPPREVIEW_GROUND_FALLBACK";

        // MapGate.Check's ArgumentException when the pack is not the description's pack passes through. A description that cannot
        // be read gives the one read problem and nothing else: no grid, no entries, no pins.
        public static MapPreviewData Build(byte[] descBytes, ContentPack pack, MapPalette palette, MapMessages messages)
        {
            if (descBytes == null) throw new ArgumentNullException(nameof(descBytes));
            if (pack == null) throw new ArgumentNullException(nameof(pack));
            if (palette == null) throw new ArgumentNullException(nameof(palette));
            if (messages == null) throw new ArgumentNullException(nameof(messages));
            IReadOnlyList<MapProblem> gate = MapGate.Check(descBytes, pack);
            MapDesc desc = null;
            try { desc = MapDescJson.Read(descBytes); }
            catch (InvalidDataException) { }

            var data = new MapPreviewData();
            var problems = new List<MapPreviewProblem>();
            var pins = new List<MapPreviewPin>();
            foreach (MapProblem p in gate)
            {
                string text = messages.Describe(p);
                problems.Add(new MapPreviewProblem { Code = p.Code, Id = p.Id ?? "", Text = text });
                if (!TryPlace(desc, p, out long x, out long z)) continue;
                pins.Add(new MapPreviewPin { ProblemIndex = problems.Count - 1, Code = p.Code, Id = p.Id ?? "", Text = text, XMm = x, ZMm = z });
            }
            data.Problems = problems;
            data.Pins = pins;
            if (desc == null) return data;

            data.Id = desc.Id;
            data.Pack = desc.Pack;
            data.Width = desc.Size.X;
            data.Depth = desc.Size.Z;
            data.OriginX = desc.Origin.X;
            data.OriginZ = desc.Origin.Z;
            Grid(desc, pack, palette, data);
            Entries(desc, palette, data);
            data.Notes = Notes(desc, palette, messages);
            return data;
        }

        // The place of a problem as the gate gave it: its millimetres, else the centre of its cell (cells count from the origin).
        private static bool TryPlace(MapDesc desc, MapProblem p, out long xMm, out long zMm)
        {
            xMm = zMm = 0;
            if (p.HasMm) { xMm = p.XMm; zMm = p.ZMm; return true; }
            if (!p.HasCell || desc == null) return false;
            xMm = ((long)desc.Origin.X + p.CellX) * 1000 + 500;
            zMm = ((long)desc.Origin.Z + p.CellZ) * 1000 + 500;
            return true;
        }

        private static void Grid(MapDesc desc, ContentPack pack, MapPalette palette, MapPreviewData data)
        {
            int w = desc.Size.X, d = desc.Size.Z;
            var ground = new int[w * d];
            for (int z = 0; z < d; z++)
                for (int x = 0; x < w; x++)
                    ground[z * w + x] = desc.Surfaces == null ? palette.NoGround
                        : palette.GroundColor(desc.Surfaces.Names[SurfaceIndex(desc.Surfaces.Rows[z][x])], out _);
            data.Shade = MapTerrainView.Shade(desc);
            data.Steep = MapTerrainView.Steep(desc, pack);
            data.Ground = ground;
        }

        // The index of a base64 character, the way the description's surface rows are written.
        private static int SurfaceIndex(char c) =>
            c >= 'A' && c <= 'Z' ? c - 'A' : c >= 'a' && c <= 'z' ? c - 'a' + 26 : c >= '0' && c <= '9' ? c - '0' + 52 : c == '+' ? 62 : 63;

        private static void Entries(MapDesc desc, MapPalette palette, MapPreviewData data)
        {
            var walls = new List<MapPreviewWall>();
            foreach (MapWall w in desc.Walls)
                walls.Add(new MapPreviewWall
                {
                    Id = w.Id, CellX = w.Cell.X, CellZ = w.Cell.Z, SizeX = w.Size.X, SizeZ = w.Size.Z, HeightRaw = w.HeightRaw,
                    Raised = w.BaseRaw.HasValue, BaseRaw = w.BaseRaw ?? 0, SideColor = palette.StructSide, TopColor = palette.StructTop,
                });
            var plates = new List<MapPreviewPlate>();
            foreach (MapPlate p in desc.Plates)
                plates.Add(new MapPreviewPlate
                {
                    Id = p.Id, CellX = p.Cell.X, CellZ = p.Cell.Z, SizeX = p.Size.X, SizeZ = p.Size.Z, ThickRaw = p.ThickRaw,
                    Axis = p.Slope == null ? "" : p.Slope.Axis, FromRaw = p.Slope?.FromRaw ?? p.YRaw.Value, ToRaw = p.Slope?.ToRaw ?? p.YRaw.Value,
                });
            var points = new List<MapPreviewPoint>();
            foreach (MapPoint p in desc.Points)
                points.Add(new MapPreviewPoint
                {
                    Id = p.Id, XMm = p.At.X, ZMm = p.At.Z, RadiusMm = p.RadiusMm, Owner = p.InitialOwner, Color = OwnerColor(palette, p.InitialOwner),
                });
            var bases = new List<MapPreviewBase>();
            foreach (MapBase b in desc.Bases)
                bases.Add(new MapPreviewBase { Id = b.Id, Team = b.Team, XMm = b.At.X, ZMm = b.At.Z, Color = OwnerColor(palette, b.Team) });
            var units = new List<MapPreviewUnit>();
            foreach (MapUnit u in desc.Units)
                units.Add(new MapPreviewUnit
                {
                    Key = u.Key, Type = u.Type, Team = u.Team, Squad = u.Squad, XMm = u.At.X, ZMm = u.At.Z, Reserve = u.Reserve,
                    Color = OwnerColor(palette, u.Team),
                });
            data.Walls = walls;
            data.Plates = plates;
            data.Points = points;
            data.Bases = bases;
            data.Units = units;
        }

        // 0 = nobody: the neutral colour; n: the nth team colour. The reader only lets owners and teams be 0 to 2 and a palette has
        // at least two team colours, so the index is always inside the list.
        private static int OwnerColor(MapPalette palette, int owner) => owner == 0 ? palette.Neutral : palette.Team[owner - 1];

        // One note per surface name of the description's table that the palette does not list.
        private static List<MapPreviewNote> Notes(MapDesc desc, MapPalette palette, MapMessages messages)
        {
            var notes = new List<MapPreviewNote>();
            if (desc.Surfaces == null) return notes;
            foreach (string name in desc.Surfaces.Names)
            {
                palette.GroundColor(name, out bool fallback);
                if (fallback) notes.Add(new MapPreviewNote { Name = name, Key = GroundFallbackKey, Text = messages.Text(GroundFallbackKey) });
            }
            return notes;
        }
    }
}
