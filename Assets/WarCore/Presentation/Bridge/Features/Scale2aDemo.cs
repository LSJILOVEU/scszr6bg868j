using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // SCALE-2a: two small worlds for the drawn sizes, both on the "rwr-stance" body (1.75 m, crouched 1.0, prone 0.5) through an
    // in-memory layer ("scale-2a", as StanceDemo's: the "damage" resolver and 40 health). Neither has a match or a player.
    // Lineup: a flat 16 x 10 m field, three men of team 1 side by side 1.5 m apart (x 5, 6.5 and 8 m, z 5.5 m), each in his own
    // squad. Squad 2 crouches at tick 1, squad 3 goes prone at tick 2, squad 1 stands. Behind them (z 6..7 m) a doorway (x 9..12,
    // the opening x 10..11, 2.0 m clear = WallSpan.ClearanceRaw) and a window (x 12..15, the opening x 13..14, sill 0.9 m, opening
    // top 2.0 m); the walls are 3 m high. Nobody moves and nobody fires.
    // CrouchFire: a flat 20 x 10 m field, one man of each team 10 m apart (x 5 and 15 m, z 5 m); squad 2 crouches at tick 1 and
    // both keep shooting each other.
    public static class Scale2aDemo
    {
        public const string Layer = "rwr-stance", DemoLayer = "scale-2a";
        private const string DemoJson = "{ \"id\": \"" + DemoLayer + "\", \"extends\": \"" + Layer + "\", \"combat\": { \"maxHealth\": 40, \"resolver\": \"damage\" } }";
        public const long CrouchTick = 1, ProneTick = 2;
        public const long WallTopRaw = 3 * SimFix.Scale, SillRaw = 58982, OpeningTopRaw = WallSpan.ClearanceRaw;
        private const long Speed = 6000;
        private const int LineupWidth = 16, LineupDepth = 10, FireWidth = 20, FireDepth = 10;

        public static WorldRecipe Recipe()
        {
            ContentPack pack = Pack();
            int weapon = pack.WeaponOfUnitType(WarCore.Content.Rwr.RwrPack.SoldierTeam1);
            var units = new[]
            {
                new UnitSpawn(1, 0, 1, 5000, 5500, Speed, 1, weapon),
                new UnitSpawn(2, 0, 2, 6500, 5500, Speed, 1, weapon),
                new UnitSpawn(3, 0, 3, 8000, 5500, Speed, 1, weapon),
            };
            var script = new[] { BridgeCommand.Stance(CrouchTick, 2, 1), BridgeCommand.Stance(ProneTick, 3, 2) };
            return new WorldRecipe(41, MoverKind.InterimCrowd, units, script).WithCombat(pack.BuildCombat());
        }

        public static WorldRecipe CrouchFire()
        {
            ContentPack pack = Pack();
            var units = new[]
            {
                new UnitSpawn(1, 0, 1, 5000, 5000, Speed, 1, pack.WeaponOfUnitType(WarCore.Content.Rwr.RwrPack.SoldierTeam1)),
                new UnitSpawn(2, 0, 2, 15000, 5000, Speed, 2, pack.WeaponOfUnitType(WarCore.Content.Rwr.RwrPack.SoldierTeam2)),
            };
            var script = new[] { BridgeCommand.Stance(CrouchTick, 2, 1) };
            return new WorldRecipe(41, MoverKind.InterimCrowd, units, script).WithCombat(pack.BuildCombat());
        }

        private static ContentPack Pack() => ContentLoader.Load(new DemoSource(), DemoLayer);

        private sealed class DemoSource : IContentSource
        {
            private readonly IContentSource files = FileContentSource.Rwr();
            public bool TryRead(string layerId, out string json, out string location)
            {
                if (layerId != DemoLayer) return files.TryRead(layerId, out json, out location);
                json = DemoJson; location = "demo:" + DemoLayer; return true;
            }
        }

        // The lineup as a showcase stage (no player, no script): the palette of "rwr-stance".
        public static ShowcaseStage Stage() => new ShowcaseStage("scale-lineup", "Scale lineup", 6f, Layer,
            new[] { "standing, crouched and prone side by side", "a doorway 2.0 m clear and a window sill 0.9 m high" },
            MapBytes, Recipe, () => new (long AtTick, PlayerInput Input)[0]);

        // The three men's midpoint: where the lineup's camera looks.
        public const float LookX = 6.5f, LookZ = 5.5f;

        public static byte[] MapBytes()
        {
            const long top = WallTopRaw, opening = OpeningTopRaw;
            WcMapWall[] walls =
            {
                Wall(1, 9, 10, 0, top), Wall(2, 10, 11, opening, top), Wall(3, 11, 12, 0, top),        // the doorway
                Wall(4, 12, 13, 0, top), Wall(5, 13, 14, 0, SillRaw), Wall(6, 13, 14, opening, top), Wall(7, 14, 15, 0, top), // the window
            };
            var bits = new byte[(LineupWidth * LineupDepth + 7) / 8];
            for (int z = 0; z < LineupDepth; z++)
                for (int x = 0; x < LineupWidth; x++)
                {
                    bool blocked = z == 6 && x >= 9 && x < 15 && x != 10;
                    if (!blocked) { int k = z * LineupWidth + x; bits[k / 8] |= (byte)(1 << (k % 8)); }
                }
            var map = new WcMap("scale-lineup", "none", default, new SimVec3(SimFix.FromInt(LineupWidth), SimFix.FromInt(6), SimFix.FromInt(LineupDepth)),
                SimFix.FromInt(LineupWidth), default, LineupWidth, LineupDepth, new[] { new WcMapLayer("walls", 1) }, walls, bits);
            return WcMapWriter.Write(map);
        }

        // A one-cell-deep wall on row z 6..7 from x0 to x1 (metres), bottom to top (raw, both even).
        private static WcMapWall Wall(ulong placement, int x0, int x1, long bottomRaw, long topRaw) =>
            new WcMapWall(0, placement, "wall", "walls",
                new SimVec3(SimFix.Ratio(x0 + x1, 2), SimFix.FromRaw((bottomRaw + topRaw) / 2), SimFix.Ratio(13, 2)),
                SimFix.Ratio(x1 - x0, 2), SimFix.FromRaw(topRaw - bottomRaw), SimFix.Ratio(1, 2));

        public static byte[] FireMapBytes()
        {
            var bits = new byte[(FireWidth * FireDepth + 7) / 8];
            for (int k = 0; k < FireWidth * FireDepth; k++) bits[k / 8] |= (byte)(1 << (k % 8));
            var map = new WcMap("scale-crouch-fire", "none", default, new SimVec3(SimFix.FromInt(FireWidth), SimFix.FromInt(6), SimFix.FromInt(FireDepth)),
                SimFix.FromInt(FireWidth), default, FireWidth, FireDepth, new[] { new WcMapLayer("walls", 1) }, new WcMapWall[0], bits);
            return WcMapWriter.Write(map);
        }
    }
}
