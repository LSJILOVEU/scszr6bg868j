using System;

namespace WarCore.Presentation
{
    // MAP-BL0: builds the walled ward through the MapDocument facade from a WardLayout. Document(41) is what
    // Maps/walled-ward-160.map.json holds, byte for byte. One function per kind of piece, called in the order that fixes the
    // wall ids: ward wall, shops (each: door part, window part, back, two sides), solid houses, stalls, carts, wells, trees.
    // A later package that swaps one kind of piece (thin walls, posts) changes that function only.
    public static class WardBuild
    {
        public const string Layer = "walls";

        public static MapDocument Document(ulong seed) => Document(seed, WardLayout.Default);

        public static MapDocument Document(ulong seed, WardLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            MapDocument doc = New(seed);
            doc = WardWalls(doc, layout);
            doc = Shops(doc, layout);
            doc = Houses(doc, layout);
            doc = Stalls(doc, layout);
            doc = Carts(doc, layout);
            doc = Wells(doc, layout);
            doc = Trees(doc, layout);
            doc = PointsAndBases(doc);
            return Soldiers(doc);
        }

        public static MapDocument New(ulong seed) => MapDocument.New(new MapNewSettings
        {
            Id = WardLayout.MapId, Content = WardLayout.Content, Pack = WardLayout.Pack, Width = WardLayout.Side, Depth = WardLayout.Side,
            FloorRaw = WardLayout.FloorRaw, CeilingRaw = WardLayout.CeilingRaw, Seed = seed, CrowdPass = WardLayout.CrowdPass,
            Heights = false, Surface = null,
        });

        public static MapDocument WardWalls(MapDocument doc, WardLayout layout)
        {
            foreach (WardBox b in WardLayout.WardWalls) doc = Add(doc, WardLayout.WardWallKind, b, layout.WardWallRaw);
            return doc;
        }

        // Door part (frame, door, frame) and window part (frame, window, frame) on the street face, then the back wall and
        // the two sides. The door and window parts are the facade's prefabs (kind "wall" on the first layer).
        public static MapDocument Shops(MapDocument doc, WardLayout layout)
        {
            foreach (WardShop s in layout.Shops)
            {
                bool alongX = !s.AlongZ;
                int x = s.AlongZ ? s.Front : s.Start, z = s.AlongZ ? s.Start : s.Front;
                doc = doc.WithDoorWall(x, z, alongX, 3, 1, 1, layout.ShopTopRaw);
                doc = s.AlongZ ? doc.WithWindow(x, z + 3, alongX, 3, 1, 1, layout.ShopTopRaw)
                    : doc.WithWindow(x + 3, z, alongX, 3, 1, 1, layout.ShopTopRaw);
                int low = Math.Min(s.Front, s.Back);
                if (s.AlongZ)
                {
                    doc = Add(doc, WardLayout.ShopKind, new WardBox(s.Back, s.Start, 1, WardLayout.ShopLength), layout.ShopTopRaw);
                    doc = Add(doc, WardLayout.ShopKind, new WardBox(low + 1, s.Start, 3, 1), layout.ShopTopRaw);
                    doc = Add(doc, WardLayout.ShopKind, new WardBox(low + 1, s.Start + WardLayout.ShopLength - 1, 3, 1), layout.ShopTopRaw);
                }
                else
                {
                    doc = Add(doc, WardLayout.ShopKind, new WardBox(s.Start, s.Back, WardLayout.ShopLength, 1), layout.ShopTopRaw);
                    doc = Add(doc, WardLayout.ShopKind, new WardBox(s.Start, low + 1, 1, 3), layout.ShopTopRaw);
                    doc = Add(doc, WardLayout.ShopKind, new WardBox(s.Start + WardLayout.ShopLength - 1, low + 1, 1, 3), layout.ShopTopRaw);
                }
            }
            return doc;
        }

        public static MapDocument Houses(MapDocument doc, WardLayout layout)
        {
            for (int i = 0; i < layout.Houses.Count; i++) doc = Add(doc, WardLayout.HouseKind, layout.Houses[i], layout.HouseRaw);
            return doc;
        }

        public static MapDocument Stalls(MapDocument doc, WardLayout layout)
        {
            for (int i = 0; i < layout.Stalls.Count; i++) doc = Add(doc, WardLayout.StallKind, layout.Stalls[i], layout.StallRaw);
            return doc;
        }

        // Hand carts, then ox carts.
        public static MapDocument Carts(MapDocument doc, WardLayout layout)
        {
            for (int i = 0; i < layout.Handcarts.Count; i++) doc = Add(doc, WardLayout.HandcartKind, layout.Handcarts[i], layout.HandcartRaw);
            for (int i = 0; i < layout.Oxcarts.Count; i++) doc = Add(doc, WardLayout.OxcartKind, layout.Oxcarts[i], layout.OxcartRaw);
            return doc;
        }

        public static MapDocument Wells(MapDocument doc, WardLayout layout)
        {
            for (int i = 0; i < layout.Wells.Count; i++) doc = Add(doc, WardLayout.WellKind, layout.Wells[i], layout.WellRaw);
            return doc;
        }

        public static MapDocument Trees(MapDocument doc, WardLayout layout)
        {
            for (int i = 0; i < layout.Trees.Count; i++) doc = Add(doc, WardLayout.TreeKind, layout.Trees[i], layout.TreeRaw);
            return doc;
        }

        // Both points neutral, each with its spawn at its centre; one base a team.
        public static MapDocument PointsAndBases(MapDocument doc)
        {
            foreach ((long x, long z) in WardLayout.Points)
                doc = doc.WithPointAdded(new MapPointInfo(x, z, WardLayout.PointRadiusMm, 0, true, x, z, false, 0, false, 0));
            for (int team = 1; team <= WardLayout.Bases.Count; team++)
                doc = doc.WithBaseAdded(new MapBaseInfo(team, WardLayout.Bases[team - 1].XMm, WardLayout.Bases[team - 1].ZMm, false, 0));
            return doc;
        }

        // Per team: the deployed squad at its formation inside the gate, then the reserves at its base. Squad = team.
        public static MapDocument Soldiers(MapDocument doc)
        {
            for (int team = 1; team <= 2; team++)
            {
                (long fx, long fz) = WardLayout.Formations[team - 1];
                (long bx, long bz) = WardLayout.Bases[team - 1];
                doc = doc.WithSquad(WardLayout.Soldier(team), team, team, fx, fz, WardLayout.OnField, false);
                doc = doc.WithSquad(WardLayout.Soldier(team), team, team, bx, bz, WardLayout.Reserve, true);
            }
            return doc;
        }

        private static MapDocument Add(MapDocument doc, string kind, WardBox b, long heightRaw) =>
            doc.WithWallAdded(new MapWallInfo(kind, Layer, b.X, b.Z, b.W, b.D, heightRaw, false, 0));
    }
}
