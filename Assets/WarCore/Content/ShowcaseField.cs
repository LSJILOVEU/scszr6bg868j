using System;
using WarCore.Battle;
using WarCore.Sim;

namespace WarCore.Content
{
    // SHOW-1: the flat open field of the showcase's melee stage, built in plain C#: 96 x 64 m, no wall, one surface (named
    // by the caller, so the field itself knows no cartridge), three control points down the middle (x = 48; z = 12, 32, 52;
    // 4 m circles) and one base a side on the middle line (x = 4 and x = 92).
    public static class ShowcaseField
    {
        public const int Width = 96, Depth = 64;
        public const string MapId = "showcase-field";

        public static byte[] MapBytes(string surface)
        {
            if (string.IsNullOrEmpty(surface)) throw new ArgumentException("A surface id is required.", nameof(surface));
            var bits = new byte[Width * Depth / 8];
            for (int i = 0; i < bits.Length; i++) bits[i] = 255;
            var max = new SimVec3(SimFix.FromInt(Width), SimFix.FromInt(4), SimFix.FromInt(Depth));
            return WcMapWriter.Write(new WcMap(MapId, "none", default, max, SimFix.FromInt(Width), default,
                Width, Depth, new[] { new WcMapLayer("walls", 1) }, Array.Empty<WcMapWall>(), bits,
                new int[(Width + 1) * (Depth + 1)], new[] { surface }, new byte[Width * Depth]));
        }

        // The match on the field under the pack's own match rules.
        public static MatchPack BuildMatch(ContentPack pack)
        {
            if (pack == null) throw new ArgumentNullException(nameof(pack));
            var points = new ControlPointDef[3];
            for (int n = 0; n < points.Length; n++)
                points[n] = new ControlPointDef(n + 1, SimVec3.Metres(Width / 2, 0, 12 + 20 * n), SimFix.FromInt(4));
            return pack.BuildMatch(points, new[] { new SpawnAnchor(1, SimVec3.Metres(4, 0, Depth / 2)), new SpawnAnchor(2, SimVec3.Metres(Width - 4, 0, Depth / 2)) });
        }
    }
}
