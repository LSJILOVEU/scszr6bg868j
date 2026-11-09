using System;
using System.Collections.Generic;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // A4 fixtures that need the interfaces A4 adds (appendix A). Everything that shapes behaviour is written here.
    internal static partial class A4
    {
        internal static readonly string[] Names = { "dirt", "grass" };
        private static readonly (int, int, int, int)[] NoWalls = Array.Empty<(int, int, int, int)>();

        // Same content literal as L9 (no "extends"); used only through WithTerrainRules.
        internal const string PackJson = "{ \"id\": \"l9-test\", "
            + "\"combat\": { \"maxHealth\": 100, \"resolver\": \"rwr.hit\", \"selector\": \"nearest-visible\", \"friendlyFire\": false, \"friendlyFireLocked\": false, \"allyCheckMissRate\": 0.25, \"allyHoldMilli\": 30000 }, "
            + "\"surfaces\": [ { \"id\": \"dirt\", \"passable\": true, \"speedMilli\": 1000, \"cost\": 1, \"concealMilli\": 1000 }, { \"id\": \"grass\", \"passable\": true, \"speedMilli\": 800, \"cost\": 1, \"concealMilli\": 1000 } ], "
            + "\"slopeRule\": { \"maxWalkableNum\": 2, \"maxWalkableDen\": 1 }, "
            + "\"ai\": { \"failedSlotTicks\": 0, \"targetMemoryTicks\": 0, \"searchTicks\": 0, \"reactionTicks\": 0, \"senseTicks\": 1 } }";
        internal static ContentPack TerrainPack() =>
            ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string> { ["l9-test"] = PackJson }), "l9-test");

        internal static WeaponDef Rifle(int spread) => new WeaponDef(30000, 30, 60000, SimFix.FromInt(10), SimFix.FromInt(30), 6554,
            spread == 0 ? default : new SpreadSpec(spread, 0, 0, spread));
        internal static CombatPack Combat(int spread) =>
            new CombatPack(new[] { Rifle(spread) }, new ZeroResolver(), new NearestVisibleEnemy(), 100 * S);
        internal static UnitSpawn Unit(ulong key, int team, long xMm, int weapon) => new UnitSpawn(key, 0, team, xMm, 4500, 3000, team, weapon, false);

        // The ridge (same as L9): grid points 6..18 rise to 3 m at x = 12, slope 0.5. The flat twin is all zero.
        internal static int Ridge(int i, int j) => i >= 6 && i <= 18 ? (int)((6 - Math.Abs(i - 12)) * S / 2) : 0;
        // The plateau: 0 up to grid point 16, 1 m at 17, 2 m from 18 (slope 1, within slopeRule 2/1).
        internal static int Plateau(int i, int j) => i <= 16 ? 0 : i >= 18 ? (int)(2 * S) : (int)S;
        internal static byte[] MapOf(Func<int, int, int> height) =>
            WcMapWriter.Write(Wp23.Make(24, 8, 0, 6 * S, NoWalls, 2 * S, height, Names, (x, z) => 0));
        internal static byte[] RidgeMap() => MapOf(Ridge);
        internal static byte[] FlatTwin() => MapOf((i, j) => 0);
        internal static byte[] PlateauMap() => MapOf(Plateau);

        // A world with no match and no orders; key 1 submits an aimed (or suppress) intent at Point every tick.
        internal static (M6World w, Aim aim) IntentWorld(byte[] map, UnitSpawn[] units, int spread = 0, bool suppress = false)
        {
            var aim = new Aim(1, suppress);
            var r = new WorldRecipe(41, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>()).WithCombat(Combat(spread))
                .WithCrowdPass(CrowdPassKind.TeamIdle).WithTerrainRules(TerrainPack()).With(aim);
            return (new M6World(r, map), aim);
        }

        // Steps until n fires of key 1 are logged (at most maxTicks).
        internal static List<(long tick, FireRecord fire)> Shots(M6World w, int n, int maxTicks = 400)
        {
            var log = new List<(long, FireRecord)>();
            for (int t = 0; t < maxTicks && log.Count < n; t++)
            {
                w.Step();
                foreach (FireRecord f in w.Combat.Fires) if (f.Shooter.Equals(Wp18TestWorld.Id(1))) log.Add((w.Sim.Clock.Tick, f));
            }
            return log;
        }

        internal static SimVec3 Raw(long x, long y, long z) => new SimVec3(SimFix.FromRaw(x), SimFix.FromRaw(y), SimFix.FromRaw(z));

        internal static ulong Rng(M6World w)
        {
            var hash = new StateHash();
            w.Sim.Rng.WriteState(hash);
            return hash.Value;
        }

        internal sealed class Aim : WorldFeature
        {
            private readonly ulong key; private readonly bool suppress;
            internal SimVec3 Point;
            internal Aim(ulong key, bool suppress) { this.key = key; this.suppress = suppress; }
            public override string OptionId => "test.a4-aim";
            public override void RegisterSystems(WorldAssembly a) =>
                a.Sim.RegisterGameplay("test.a4-aim", new Submitter(this, a.Sim, a.Combat), SimSystemPhase.Requests);
            private sealed class Submitter : IRetainedGameplaySystem
            {
                private readonly Aim owner; private readonly Simulation sim; private readonly CombatSystem combat;
                internal Submitter(Aim owner, Simulation sim, CombatSystem combat) { this.owner = owner; this.sim = sim; this.combat = combat; }
                public int Order => 25;
                public string StateTypeId => "test.a4-aim";
                public int StateVersion => 1;
                public void Step(SimContext c)
                {
                    StableId id = StableEntityKey.Placed(1, owner.key).Id;
                    combat.Submit(c, owner.suppress ? FireIntent.Suppress(id, owner.Point) : FireIntent.Aimed(id, owner.Point));
                }
                public void SaveState(SimStateWriter writer) { }
                public void LoadState(SimStateReader reader) { }
                public void WriteState(StateHash hash) { hash.Add(StateTypeId); hash.Add(StateVersion); }
            }
        }

        // ---- Picking (appendix K1, L5, L6) ----

        internal static ViewGround RidgeGround() => ViewGround.For(ViewMap.Parse(RidgeMap()));

        // Independent oracle: every triangle of the ridge field (split as L9's ClosedHeight: u >= v takes (00, 10, 11), else
        // (00, 11, 01)), double ray-triangle intersection on the closed triangle, the smallest t >= 0. NaN = no hit.
        // A ray that enters the closed rectangle through a side below the ground hits at the entry point, as long as it enters
        // no lower than 1 m under the lowest grid point (K1's height band; lower down it has met the flat plane outside first).
        internal static double OracleT(double ox, double oy, double oz, double fx, double fy, double fz, Func<int, int, int> height,
            int width = 24, int depth = 8, double lowest = 0)
        {
            double tIn = 0, tOut = double.PositiveInfinity;
            if (In(ox, fx, 0, width, ref tIn, ref tOut) && In(oz, fz, 0, depth, ref tIn, ref tOut))
            {
                double y = oy + tIn * fy;
                if (y < lowest - 1) return double.NaN;
                if (y < HeightD(ox + tIn * fx, oz + tIn * fz, height, width, depth)) return tIn;
            }
            double best = double.NaN;
            for (int j = 0; j < depth; j++)
                for (int i = 0; i < width; i++)
                {
                    double h00 = height(i, j) / (double)S, h10 = height(i + 1, j) / (double)S;
                    double h01 = height(i, j + 1) / (double)S, h11 = height(i + 1, j + 1) / (double)S;
                    Tri(ref best, ox, oy, oz, fx, fy, fz, i, h00, j, i + 1, h10, j, i + 1, h11, j + 1);
                    Tri(ref best, ox, oy, oz, fx, fy, fz, i, h00, j, i + 1, h11, j + 1, i, h01, j + 1);
                }
            return best;
        }

        private static bool In(double o, double d, double lo, double hi, ref double ta, ref double tb)
        {
            if (d == 0) return o >= lo && o <= hi;
            double t0 = (lo - o) / d, t1 = (hi - o) / d;
            if (t0 > t1) { double x = t0; t0 = t1; t1 = x; }
            ta = Math.Max(ta, t0); tb = Math.Min(tb, t1);
            return ta <= tb;
        }

        // Ground height (metres) on the closed field, same triangles as L9's ClosedHeight, in double.
        internal static double HeightD(double x, double z, Func<int, int, int> height, int width = 24, int depth = 8)
        {
            int i = Math.Min((int)Math.Floor(x), width - 1), j = Math.Min((int)Math.Floor(z), depth - 1);
            double u = x - i, v = z - j;
            double h00 = height(i, j) / (double)S, h10 = height(i + 1, j) / (double)S;
            double h01 = height(i, j + 1) / (double)S, h11 = height(i + 1, j + 1) / (double)S;
            return u >= v ? h00 + (h10 - h00) * u + (h11 - h10) * v : h00 + (h11 - h01) * u + (h01 - h00) * v;
        }

        private static void Tri(ref double best, double ox, double oy, double oz, double fx, double fy, double fz,
            double ax, double ay, double az, double bx, double by, double bz, double cx, double cy, double cz)
        {
            const double eps = 1e-12;
            double e1x = bx - ax, e1y = by - ay, e1z = bz - az, e2x = cx - ax, e2y = cy - ay, e2z = cz - az;
            double px = fy * e2z - fz * e2y, py = fz * e2x - fx * e2z, pz = fx * e2y - fy * e2x;
            double det = e1x * px + e1y * py + e1z * pz;
            if (Math.Abs(det) < eps) return;
            double inv = 1 / det, tx = ox - ax, ty = oy - ay, tz = oz - az;
            double u = (tx * px + ty * py + tz * pz) * inv;
            if (u < -1e-9 || u > 1 + 1e-9) return;
            double qx = ty * e1z - tz * e1y, qy = tz * e1x - tx * e1z, qz = tx * e1y - ty * e1x;
            double v = (fx * qx + fy * qy + fz * qz) * inv;
            if (v < -1e-9 || u + v > 1 + 1e-9) return;
            double t = (e2x * qx + e2y * qy + e2z * qz) * inv;
            if (t >= 0 && (double.IsNaN(best) || t < best)) best = t;
        }

        // A ray through point p (metres) with direction f: o = p - 50 f.
        internal static (float ox, float oy, float oz) Through(double px, double py, double pz, double fx, double fy, double fz) =>
            ((float)(px - 50 * fx), (float)(py - 50 * fy), (float)(pz - 50 * fz));

        // Orthographic projection of a world point to screen pixels (inverse of IsoCamera.RayOrigin's offsets).
        internal static (float sx, float sy) Project(IsoCamera camera, double x, double y, double z, float w, float h)
        {
            var p = camera.Position; var r = camera.Right; var u = camera.Up;
            double dx = x - p.X, dy = y - p.Y, dz = z - p.Z;
            double nx = (dx * r.X + dy * r.Y + dz * r.Z) / (camera.Zoom * (w / h));
            double ny = (dx * u.X + dy * u.Y + dz * u.Z) / camera.Zoom;
            return ((float)((nx + 1) / 2 * w), (float)((ny + 1) / 2 * h));
        }
    }
}
