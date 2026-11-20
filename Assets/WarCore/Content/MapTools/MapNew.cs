using System;
using System.IO;

namespace WarCore.Content.MapTools
{
    // What a new, empty description is made of. HeightRaw null: flat map; Surface null: no surface table.
    public sealed class MapNewSpec
    {
        public string Id { get; set; }
        public string Content { get; set; } = "none";
        public string Pack { get; set; }
        public int Width { get; set; }
        public int Depth { get; set; }
        public long FloorRaw { get; set; }
        public long CeilingRaw { get; set; }
        public ulong Seed { get; set; }
        public string CrowdPass { get; set; } = "none";
        public int? HeightRaw { get; set; }
        public string Surface { get; set; }
    }

    public static class MapNew
    {
        // The editor's terrain stamp instance: registered on every new map as owner 1, so it always gets a grid owner number
        // (design map-editor-v1 3.4).
        public const string TerrainInstance = "terrain";

        // An empty description: origin [0, 0], one layer "walls" (bit 1), no entries, owners ["hand", "stamp:terrain"], next
        // all 1; then heights and surfaces as the spec asks. The result goes through the reader (write, read back), so a spec
        // the reader refuses (a side outside [1, MaxSide], for one) throws its InvalidDataException; a HeightRaw outside
        // [FloorRaw, CeilingRaw] throws ArgumentOutOfRangeException from EnableHeights. The id and the floor/ceiling order are
        // the bake's checks (MapBake.Run), not the reader's, so they are not checked here.
        public static MapDesc Desc(MapNewSpec spec)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            var empty = new MapDesc(spec.Id ?? "", spec.Content ?? "", spec.Pack ?? "", new MapXZ(spec.Width, spec.Depth), new MapXZ(0, 0),
                spec.FloorRaw, spec.CeilingRaw, new[] { new MapLayer("walls", 1) }, null, null, new MapWall[0], new MapPoint[0],
                new MapBase[0], new MapUnit[0], new MapRun(spec.Seed, spec.CrowdPass ?? ""), new[] { MapEdit.Hand, "stamp:" + TerrainInstance },
                null, null, new MapStamp[0], new MapSrc[0], new MapNext(1, 1, 1, 1));
            MapDesc checkedEmpty = MapDescJson.Read(MapDescJson.Write(empty));
            var edit = new MapEdit(checkedEmpty);
            if (spec.HeightRaw.HasValue) edit.EnableHeights(spec.HeightRaw.Value);
            if (spec.Surface != null) edit.EnableSurfaces(spec.Surface);
            return MapDescJson.Read(MapDescJson.Write(edit.Current));
        }
    }
}
