using System;
using System.Collections.Generic;
using System.Linq;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml11aWorld;

namespace WarCore.Tests
{
    // MAP-E0 fixture: the assault demo map (PlateAssaultMap) with its flat ground swapped for a height table and the house
    // lifted by one rise F. Nothing else of the description changes: cells, cutouts, points, bases, units, seed.
    //   level 0: the map as it is (no height table).
    //   level 1: a height table that is all 0, F = 0.
    //   level 2: heights 400 * x + 2048 * ((x + 2 * z) % 5) at point (x, z), F = the highest point under the house (rounded up to even).
    // Heights[z][x]: row z, column x. 41 x 21 points, raw units (1 m = 65536).
    internal sealed class MapE0Level
    {
        internal const long M = SimFix.Scale;
        internal const int Width = 40, Depth = 20;
        // The house stands on cells x 14..25, z 5..14, so on points x 14..26, z 5..15.
        internal const int HouseX0 = 14, HouseX1 = 26, HouseZ0 = 5, HouseZ1 = 15;
        internal static readonly ContentPack Pack = RwrPack.Load("rwr-m6");

        internal readonly int Level;
        internal readonly long Rise;
        internal readonly MapDesc Desc;
        internal readonly byte[] DescBytes;
        internal readonly MapBakeResult Baked;
        // Extra reference: level 0 run under terrain rules as well (not part of the acceptance).
        internal readonly bool WithRules;

        private MapE0Level(int level, bool withRules)
        {
            Level = level; WithRules = withRules;
            Desc = BuildDesc(level, out Rise);
            DescBytes = MapDescJson.Write(Desc);
            Baked = MapBake.Run(DescBytes);
        }

        private static readonly Dictionary<string, MapE0Level> Cache = new Dictionary<string, MapE0Level>();
        internal static MapE0Level Of(int level, bool withRules = false)
        {
            string key = level + (withRules ? "r" : "");
            lock (Cache)
            {
                if (!Cache.TryGetValue(key, out MapE0Level made)) Cache[key] = made = new MapE0Level(level, withRules);
                return made;
            }
        }

        internal static int[][] Waves() =>
            Enumerable.Range(0, Depth + 1).Select(z => Enumerable.Range(0, Width + 1).Select(x => 400 * x + 2048 * ((x + 2 * z) % 5)).ToArray()).ToArray();

        internal static int[][] Zeros() =>
            Enumerable.Range(0, Depth + 1).Select(_ => new int[Width + 1]).ToArray();

        // The highest ground under the house, rounded up to an even number (wall heights must be even).
        internal static long RiseOf(int[][] heights)
        {
            long top = 0;
            for (int z = HouseZ0; z <= HouseZ1; z++)
                for (int x = HouseX0; x <= HouseX1; x++) top = Math.Max(top, heights[z][x]);
            return top % 2 == 0 ? top : top + 1;
        }

        internal static MapDesc BuildDesc(int level, out long rise)
        {
            MapDesc source = PlateAssaultMap.Desc();
            rise = 0;
            if (level == 0) return source;
            int[][] heights = level == 1 ? Zeros() : Waves();
            rise = RiseOf(heights);
            return Lift(source, heights, rise);
        }

        // The same description with a height table, every wall standing on the ground higher by `rise`, every wall with its own
        // bottom lifted by `rise`, and both plates lifted by `rise`. Rebuilt field by field with the constructors.
        internal static MapDesc Lift(MapDesc d, int[][] heights, long rise)
        {
            MapWall[] walls = d.Walls.Select(w => w.BaseRaw.HasValue
                ? new MapWall(w.Id, w.Kind, w.Layer, w.Cell, w.Size, w.HeightRaw, w.Owner, w.Src, w.BaseRaw.Value + rise)
                : new MapWall(w.Id, w.Kind, w.Layer, w.Cell, w.Size, w.HeightRaw + rise, w.Owner, w.Src)).ToArray();
            MapPlate[] plates = d.Plates.Select(p => new MapPlate(p.Id, p.Cell, p.Size,
                p.YRaw.HasValue ? p.YRaw.Value + rise : (long?)null,
                p.Slope != null ? new MapSlope(p.Slope.Axis, p.Slope.FromRaw + rise, p.Slope.ToRaw + rise) : null,
                p.ThickRaw, p.Surface, p.Owner, p.Src)).ToArray();
            return new MapDesc(d.Id, d.Content, d.Pack, d.Size, d.Origin, d.FloorRaw, d.CeilingRaw, d.Layers.ToArray(), heights, d.Surfaces,
                plates, d.Cutouts.ToArray(), walls, d.Points.ToArray(), d.Bases.ToArray(), d.Units.ToArray(), d.Run,
                d.Owners?.ToArray(), d.HeightOwner?.ToArray(), d.SurfaceOwner?.ToArray(), d.Stamps.ToArray(), d.Removed.ToArray(), d.Next);
        }

        // The recipe the runtime builds from a map file, with the plate switch the tests use.
        internal WorldRecipe Recipe()
        {
            WorldRecipe recipe = MapRecipe.From(Baked, Pack).WithPlatesPreview();
            return WithRules && recipe.TerrainPack == null ? recipe.WithTerrainRules(Pack) : recipe;
        }

        internal M6World Battle() => new M6World(Recipe(), Baked.WcMapBytes);

        // Team 1 alone on a match that never ends. Rebuilt from the full recipe; a terrain recipe keeps its terrain rules.
        internal M6World Squad(out UnitSpawn[] squad)
        {
            WorldRecipe full = Recipe();
            squad = full.Units.ToArray().Where(u => u.Team == 1).ToArray();
            WorldRecipe recipe = new WorldRecipe(full.Seed, MoverKind.InterimCrowd, squad, Array.Empty<BridgeCommand>())
                .WithCombat(full.Combat).WithMatch(full.Match).WithCrowdPass(CrowdPassKind.TeamIdle).WithPlatesPreview();
            if (full.TerrainPack != null) recipe = recipe.WithTerrainRules(full.TerrainPack);
            return new M6World(M6World.Endless(recipe), Baked.WcMapBytes);
        }

        internal LayeredNav Nav()
        {
            WcMap map = WcMapReader.Read(Baked.WcMapBytes);
            return WcMapLayeredNav.Build(map, Pack);
        }

        internal List<(int Layer, int X, int Z, int ToLayer, int ToX, int ToZ)> Seams()
        {
            LayeredNav nav = Nav();
            var seams = new List<(int Layer, int X, int Z, int ToLayer, int ToX, int ToZ)>();
            for (int n = 0; n < nav.Count; n++)
                for (int k = 0; k < nav.SeamCount(n); k++)
                {
                    int other = nav.Seam(n, k);
                    if (n < other) seams.Add((nav.LayerOf(n), nav.XOf(n), nav.ZOf(n), nav.LayerOf(other), nav.XOf(other), nav.ZOf(other)));
                }
            return seams;
        }

        // 900 ticks of team 1 alone: the tick the point upstairs and the east point are taken, each soldier's first tick on the
        // first floor, and the first failed walk.
        internal sealed class SquadRun
        {
            internal int Taken = -1, East = -1;
            internal readonly SortedDictionary<ulong, int> FirstUp = new SortedDictionary<ulong, int>();
            // Height (raw Y) of each soldier at tick 1, and at the tick it first stands on the first floor.
            internal readonly SortedDictionary<ulong, long> StartY = new SortedDictionary<ulong, long>();
            internal readonly SortedDictionary<ulong, long> UpY = new SortedDictionary<ulong, long>();
            internal string FirstFailure;
        }

        internal SquadRun RunSquad(int ticks = 900)
        {
            M6World w = Squad(out UnitSpawn[] squad);
            var run = new SquadRun();
            for (int t = 1; t <= ticks; t++)
            {
                w.Step();
                if (run.Taken < 0 && w.Capture.Points[PlateAssaultMap.UpstairsPoint].Owner == 1) run.Taken = t;
                if (run.East < 0 && w.Capture.Points[2].Owner == 1) run.East = t;
                foreach (UnitSpawn unit in squad)
                {
                    if (run.FirstFailure == null && Failed(w, unit.Key)) run.FirstFailure = "unit " + unit.Key + " tick " + t;
                    if (t == 1) run.StartY[unit.Key] = At(w, unit.Key).Y.Raw;
                    if (Layer(w, unit.Key) == 2 && !run.FirstUp.ContainsKey(unit.Key))
                    {
                        run.FirstUp[unit.Key] = t;
                        run.UpY[unit.Key] = At(w, unit.Key).Y.Raw;
                    }
                }
            }
            return run;
        }

        // The whole battle (seed 41), the same world twice: the end tick, the last hash, the winner, who of team 1 reached the
        // first floor, the first failed walk, the first soldier off the walkable cells, and the first hash that differed.
        internal sealed class BattleRun
        {
            internal int Ticks;
            internal ulong Hash;
            internal int Winner;
            internal readonly HashSet<ulong> Upstairs = new HashSet<ulong>();
            internal string FirstFailure, FirstOffCell, FirstHashDiff;
        }

        internal BattleRun RunBattle(int limit = 3000)
        {
            LayeredNav nav = Nav();
            M6World w = Battle(), again = Battle();
            UnitSpawn[] units = w.Recipe.Units.ToArray();
            var run = new BattleRun();
            int t = 0;
            for (; t < limit && w.Match.Winner == 0; t++)
            {
                w.Step(); again.Step();
                if (run.FirstHashDiff == null && again.Hash != w.Hash) run.FirstHashDiff = "tick " + t;
                foreach (UnitSpawn unit in units)
                {
                    if (w.Dead(unit.Key)) continue;
                    if (run.FirstFailure == null && Failed(w, unit.Key)) run.FirstFailure = "unit " + unit.Key + " tick " + t;
                    SimVec3 at = At(w, unit.Key);
                    int layer = Layer(w, unit.Key);
                    if (run.FirstOffCell == null && !(nav.TryNode(layer, (int)(at.X.Raw / M), (int)(at.Z.Raw / M), out int node) && nav.IsPassable(node)))
                        run.FirstOffCell = "unit " + unit.Key + " tick " + t + " layer " + layer;
                    if (unit.Team == 1 && layer == 2) run.Upstairs.Add(unit.Key);
                }
            }
            run.Ticks = t; run.Hash = w.Hash; run.Winner = w.Match.Winner;
            return run;
        }
    }
}
