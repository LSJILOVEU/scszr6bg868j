using System;
using System.Collections;
using System.Reflection;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    internal static class M3TestWorld
    {
        internal static SimVec3 Point(long xMm, long zMm) => new SimVec3(SimFix.Ratio(xMm, 1000), default, SimFix.Ratio(zMm, 1000));
        internal static Simulation Sim(SimSession session) => M3LegacyRegressionTests.SimulationOf(session);
        internal static EntityId Unit(SimSession session, ulong key = 1) =>
            Sim(session).Entities.Resolve(new EntityLink(StableEntityKey.Placed(1, key).Id));
        internal static T Component<T>(SimSession session, ulong key = 1) where T : unmanaged, IStateHash =>
            Sim(session).Entities.Get<T>(Unit(session, key));
        internal static SimVec3 Position(SimSession session, ulong key = 1) => Component<Position>(session, key).Value;
        internal static InterimLocomotion Locomotion(SimSession session)
        {
            var systems = (IEnumerable)typeof(Simulation).GetField("systems", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Sim(session));
            foreach (object entry in systems)
            {
                object system = entry.GetType().GetField("System", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(entry);
                if (system is InterimLocomotion locomotion) return locomotion;
            }
            throw new InvalidOperationException("No locomotion system.");
        }

        internal static SimSession Single(byte[] bytes, long xMm = 500, long zMm = 500, long speed = 6000) =>
            SimSession.Create(new WorldRecipe(41, MoverKind.InterimPath,
                new[] { new UnitSpawn(1, 0, 1, xMm, zMm, speed) }, Array.Empty<BridgeCommand>()), bytes);

        internal static WcMap Boxes(int width, int depth, params (int x, int z, int w, int d)[] boxes)
        {
            var walls = new WcMapWall[boxes.Length];
            var bits = new byte[(width * depth + 7) / 8];
            for (int i = 0; i < boxes.Length; i++)
            {
                var b = boxes[i];
                walls[i] = new WcMapWall(0, (ulong)(i + 1), "wall", "walls",
                    new SimVec3(SimFix.FromInt(b.x) + SimFix.Ratio(b.w, 2), SimFix.FromInt(1), SimFix.FromInt(b.z) + SimFix.Ratio(b.d, 2)),
                    SimFix.Ratio(b.w, 2), SimFix.FromInt(2), SimFix.Ratio(b.d, 2));
            }
            for (int z = 0; z < depth; z++)
                for (int x = 0; x < width; x++)
                {
                    bool blocked = false;
                    foreach (var b in boxes) blocked |= x >= b.x && x < b.x + b.w && z >= b.z && z < b.z + b.d;
                    if (!blocked) { int i = z * width + x; bits[i / 8] |= (byte)(1 << (i % 8)); }
                }
            return new WcMap("m3-test", "none", default, SimVec3.Metres(width, 4, depth), SimFix.FromInt(Math.Max(width, depth)),
                default, (uint)width, (uint)depth, new[] { new WcMapLayer("walls", 1) }, walls, bits);
        }

        internal static WcMap UMap() => Boxes(16, 16, (4, 2, 1, 8), (9, 2, 1, 8), (4, 2, 6, 1));

        // Independent continuous segment/rectangle distance: intersection plus endpoint/edge minima.
        internal static double WallDistance(SimVec3 a, SimVec3 b, WcMap map, WcMapWall wall)
        {
            double ax = a.X.Raw / 65536.0, az = a.Z.Raw / 65536.0;
            double bx = b.X.Raw / 65536.0, bz = b.Z.Raw / 65536.0;
            double left = (map.Min.X.Raw + wall.LocalCenter.X.Raw - wall.HalfX.Raw) / 65536.0;
            double right = left + 2 * wall.HalfX.Raw / 65536.0;
            double bottom = (map.Min.Z.Raw + wall.LocalCenter.Z.Raw - wall.HalfZ.Raw) / 65536.0;
            double top = bottom + 2 * wall.HalfZ.Raw / 65536.0;
            double lo = 0, hi = 1;
            bool Slice(double origin, double delta, double min, double max)
            {
                if (delta == 0) return origin >= min && origin <= max;
                double u = (min - origin) / delta, v = (max - origin) / delta;
                lo = Math.Max(lo, Math.Min(u, v)); hi = Math.Min(hi, Math.Max(u, v));
                return lo <= hi;
            }
            if (Slice(ax, bx - ax, left, right) && Slice(az, bz - az, bottom, top)) return 0;
            double PointBox(double x, double z)
            {
                double dx = Math.Max(0, Math.Max(left - x, x - right));
                double dz = Math.Max(0, Math.Max(bottom - z, z - top));
                return dx * dx + dz * dz;
            }
            double CornerSegment(double x, double z)
            {
                double dx = bx - ax, dz = bz - az, length = dx * dx + dz * dz;
                double t = length == 0 ? 0 : Math.Max(0, Math.Min(1, ((x - ax) * dx + (z - az) * dz) / length));
                double ex = x - ax - t * dx, ez = z - az - t * dz;
                return ex * ex + ez * ez;
            }
            double squared = Math.Min(PointBox(ax, az), PointBox(bx, bz));
            squared = Math.Min(squared, CornerSegment(left, bottom));
            squared = Math.Min(squared, CornerSegment(left, top));
            squared = Math.Min(squared, CornerSegment(right, bottom));
            squared = Math.Min(squared, CornerSegment(right, top));
            return Math.Sqrt(squared);
        }
    }
}
