using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml7Worlds;

namespace WarCore.Tests
{
    // ML-8 fixtures. The demo house (PlateDemoMap): ground 24 x 16 = nodes 0..383; stair (layer 1) 384..387 on x 10..13, z 9,
    // its top 0 -> 3 m along x (0.75 m a cell); first floor (layer 2) 388..407 (z 6: 388..393 for x 9..14, z 7: 394..399,
    // z 8: 400..405, z 9: x 9 = 406, x 14 = 407; the cutout over the stair has no nodes). Seams: ground (9, 9) = 225 <-> 384,
    // stair top 387 <-> first floor (14, 9) 407.
    internal static class Ml8Worlds
    {
        internal static readonly byte[] Demo = PlateDemoMap.Bytes();

        internal static string Show(SimVec3 p) => "(" + p.X.Raw + ", " + p.Y.Raw + ", " + p.Z.Raw + ")";

        internal static SimVec3 P(long xMm, long yRaw, long zMm) => new SimVec3(SimFix.Ratio(xMm, 1000), SimFix.FromRaw(yRaw), SimFix.Ratio(zMm, 1000));

        // The stair's top under x (raw): 0.75 m a cell from x = 10 m.
        internal static long StairTop(long xRaw) => (xRaw - 10 * M) * 3 / 4;

        internal static SimSession Walk(byte[] map, UnitSpawn[] units, BridgeCommand[] script = null, MoverKind mover = MoverKind.InterimCrowd) =>
            SimSession.Create(Recipe(units, script, mover), map);

        // The floor of a layer under (x, z) as the map gives it: the flat height on the ground (no field), else the plate's top.
        internal static long Floor(WcMap map, int layer, SimVec3 p)
        {
            if (layer == 0) return map.FlatHeight.Raw;
            Assert(map.Plates[layer - 1].TopField(map.Min.X, map.Min.Z).TryHeightAt(p.X, p.Z, out SimFix y), "a floor under the unit");
            return y.Raw;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        internal static object Call(object owner, string name, params object[] args) =>
            owner.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Invoke(owner, args);

        // TryEnter(layer, fromKey, toKey, out newLayer) on a locomotion; keys are z * width + x.
        internal static (bool ok, int layer) Enter(InterimLocomotion l, int width, int layer, (int x, int z) from, (int x, int z) to)
        {
            var args = new object[] { layer, from.z * width + from.x, to.z * width + to.x, 0 };
            bool ok = (bool)Call(l, "TryEnter", args);
            return (ok, (int)args[3]);
        }

        // The terrain branch's whole check of one path-mover step: Settle (entry, floor, end footprint) and the swept footprint.
        internal static (bool ok, SimVec3 next) PathClear(InterimLocomotion l, int layer, SimVec3 from, SimVec3 next)
        {
            var args = new object[] { layer, from, next };
            bool ok = (bool)Call(l, "PathClear", args);
            return (ok, (SimVec3)args[2]);
        }

        internal static bool Open(InterimLocomotion l, int width, int layer, (int x, int z) own, int x, int z) =>
            (bool)Call(l, "Open", layer, own.z * width + own.x, x, z);

        // Rebinds a locomotion to a hand-built layered graph of the same size (the grid, terrain and vertical component stay).
        internal static void Rebind(SimSession s, LayeredNav nav, params PlateSlab[] plates)
        {
            InterimLocomotion l = M3TestWorld.Locomotion(s);
            var tops = plates.Select(p => Top(p)).ToArray();
            l.BindPlates(M3TestWorld.Sim(s).Entities.Resolve<VerticalState>(), nav, tops, default, 0);
        }

        internal static HeightField Top(PlateSlab p)
        {
            var points = new int[(p.Width + 1) * (p.Depth + 1)];
            for (int j = 0; j <= p.Depth; j++)
                for (int i = 0; i <= p.Width; i++) points[j * (p.Width + 1) + i] = (int)p.TopRaw(i, j);
            return new HeightField(p.Width, p.Depth, SimFix.FromRaw(p.X * M), SimFix.FromRaw(p.Z * M), points);
        }

        // Runs a session and records each unit's layer changes (a new entry only when the layer differs from the last).
        internal static Dictionary<ulong, List<int>> Layers(SimSession s, int ticks, params ulong[] keys)
        {
            var seen = keys.ToDictionary(k => k, k => new List<int> { Vertical(s, k).Layer });
            for (int t = 0; t < ticks; t++)
            {
                s.StepOnce();
                foreach (ulong k in keys)
                    if (seen[k][seen[k].Count - 1] != Vertical(s, k).Layer) seen[k].Add(Vertical(s, k).Layer);
            }
            return seen;
        }
    }
}
