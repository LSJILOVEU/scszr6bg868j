using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using WarCore.Presentation;

namespace WarCore.Replay
{
    // Runs one named setup without Unity and writes a compact replay: unit positions every N ticks,
    // point owners, shots, and the four layout metrics (see README.md).
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--list")
            {
                foreach (var pair in Recipes.All) Console.WriteLine($"{pair.Key}\t{pair.Value.Label}");
                return 0;
            }
            if (args.Length == 1 && args[0] == "--selftest") { UseProjectRoot(); return SelfTest.Run(); }
            if (args.Length == 5 && args[0] == "--map") return RunMap(args[1], args[2], args[3], args[4]);
            if ((args.Length != 4 && args.Length != 5) || !Recipes.All.ContainsKey(args[0]))
            {
                Console.Error.WriteLine("usage: WarCore.Replay <setup> <maxTicks> <every> <output.json> [lanesSeed] | --map <map.json> <maxTicks> <every> <output.json> | --list | --selftest");
                return 2;
            }
            // A fifth argument reseeds the three-lane setups (the other setups ignore it), to see one change over several matches.
            if (args.Length == 5) Recipes.LanesSeed = int.Parse(args[4], CultureInfo.InvariantCulture);
            string output = Path.GetFullPath(args[3]);
            UseProjectRoot();
            string json = Export(args[0], int.Parse(args[1], CultureInfo.InvariantCulture), int.Parse(args[2], CultureInfo.InvariantCulture), out string summary);
            File.WriteAllText(output, json);
            Console.WriteLine(summary);
            return 0;
        }

        // Runs a map description file as it stands (its own seed, the content layer it names, found in the cartridges).
        private static int RunMap(string mapFile, string maxTicks, string every, string outputFile)
        {
            string input = Path.GetFullPath(mapFile), output = Path.GetFullPath(outputFile);
            UseProjectRoot();
            MapRunSetup setup = MapRunSetup.From(File.ReadAllBytes(input));
            string name = System.Text.RegularExpressions.Regex.Replace(Path.GetFileName(input), "[^A-Za-z0-9._-]", "_");
            var entry = new Recipes.Entry("Map file " + name, () => setup.Recipe, () => setup.Bake.WcMapBytes);
            string json = Export("map:" + name, entry, int.Parse(maxTicks, CultureInfo.InvariantCulture), int.Parse(every, CultureInfo.InvariantCulture), out string summary);
            File.WriteAllText(output, json);
            Console.WriteLine(summary);
            return 0;
        }

        // Content packs are read relative to the Unity project root, as in the editor.
        private static void UseProjectRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "Assets", "WarCore"))) directory = directory.Parent;
            if (directory == null) throw new DirectoryNotFoundException("Cannot locate the Unity project root above the binary.");
            Directory.SetCurrentDirectory(directory.FullName);
        }

        public static string Export(string setup, int maxTicks, int every, out string summary) => Export(setup, Recipes.All[setup], maxTicks, every, out summary);

        private static string Export(string setup, Recipes.Entry entry, int maxTicks, int every, out string summary)
        {
            if (maxTicks < 0 || every < 1) throw new ArgumentOutOfRangeException(nameof(every));
            WorldRecipe recipe = entry.Recipe();
            SimSession session = SimSession.Create(recipe, entry.Map());
            var keys = new List<ulong>(); var teams = new List<int>();
            foreach (UnitSpawn unit in recipe.Units) { keys.Add(unit.Key); teams.Add(unit.Team); }
            // Squads: unit index by key; per frame each unit's leader index (-1 = in no squad), each team's men in squads and
            // its members' distances to their leaders.
            var indexOf = new Dictionary<ulong, int>();
            for (int i = 0; i < keys.Count; i++) indexOf[keys[i]] = i;
            var bands = new int[keys.Count]; var inSquads = new int[2];
            var leash = new[] { new List<double>(), new List<double>() };

            var points = new List<(double x, double z, double r)>();
            foreach (ViewPoint point in session.Current.Points) points.Add((point.X, point.Z, point.Radius));
            const int metricCount = 16;
            var stats = new Metrics.Series[2, metricCount];
            for (int t = 0; t < 2; t++) for (int m = 0; m < metricCount; m++) stats[t, m] = new Metrics.Series();
            var side = new[] { new List<(double x, double z)>(), new List<(double x, double z)>() };
            var idleSide = new[] { new List<bool>(), new List<bool>() };
            // Per unit: last seen position and the tick since which it has not changed (idleOutside).
            var lastX = new float[keys.Count]; var lastZ = new float[keys.Count]; var since = new long[keys.Count];
            for (int i = 0; i < keys.Count; i++) since[i] = -1;
            double ring = entry.RingMm / 1000.0;
            var owners = new List<int>();
            var pinned = new List<int>(); var broken = new List<int>(); var pressed = new int[2, 2];
            // Cover numbers: where each team's pinned units stand, how many of them moved since the previous recorded frame
            // (frameX/frameZ: a unit's position in that frame, NaN when it was not on the field), and the walls.
            var pinnedAt = new[] { new List<(double x, double z)>(), new List<(double x, double z)>() };
            var pinnedMoved = new int[2];
            var frameX = new float[keys.Count]; var frameZ = new float[keys.Count];
            for (int i = 0; i < keys.Count; i++) frameX[i] = float.NaN;
            var walls = new List<(double x, double z, double sx, double sz)>();
            foreach (ViewWall wall in session.Map.Walls) walls.Add((wall.CenterX, wall.CenterZ, wall.SizeX, wall.SizeZ));

            var sb = new StringBuilder(4 << 20);
            ViewMap map = session.Map;
            sb.Append("{\"config\":\"").Append(setup).Append("\",\"label\":\"").Append(entry.Label).Append("\",\"tickRate\":").Append(SimSession.TickRate).Append(",\"every\":").Append(every);
            sb.Append(",\"map\":{\"minX\":").Append(F(map.MinX)).Append(",\"minZ\":").Append(F(map.MinZ)).Append(",\"w\":").Append(map.Width).Append(",\"d\":").Append(map.Depth).Append(",\"walls\":[");
            bool first = true;
            foreach (ViewWall wall in map.Walls)
            { if (!first) sb.Append(','); first = false; sb.Append('[').Append(F(wall.CenterX)).Append(',').Append(F(wall.CenterZ)).Append(',').Append(F(wall.SizeX)).Append(',').Append(F(wall.SizeZ)).Append(']'); }
            sb.Append("]},\"teams\":[").Append(string.Join(",", teams)).Append("],\"points\":[");
            first = true;
            foreach (var p in points) { if (!first) sb.Append(','); first = false; sb.Append('[').Append(F((float)p.x)).Append(',').Append(F((float)p.z)).Append(',').Append(F((float)p.r)).Append(']'); }
            sb.Append("],\"frames\":[");
            var shots = new List<int>(); var blasts = new List<int>(); first = true;
            for (int tick = 0; tick <= maxTicks; tick++)
            {
                if (tick > 0) { if (session.IsFinished) break; session.StepOnce(); }
                FrameView frame = session.Current;
                for (int i = 0; i < keys.Count; i++)
                {
                    if (!frame.TryGet(keys[i], out ViewEntity seen)) { since[i] = -1; continue; }
                    if (since[i] < 0 || seen.X != lastX[i] || seen.Z != lastZ[i]) { lastX[i] = seen.X; lastZ[i] = seen.Z; since[i] = frame.Tick; }
                }
                foreach (ViewFire fire in frame.Fires)
                { shots.Add(D(fire.X0)); shots.Add(D(fire.Z0)); shots.Add(D(fire.X1)); shots.Add(D(fire.Z1)); shots.Add(fire.Team); shots.Add(fire.Lethal ? 1 : 0); }
                foreach (ViewBlast blast in frame.Blasts)
                { blasts.Add(D(blast.X)); blasts.Add(D(blast.Z)); blasts.Add(D(blast.Radius)); blasts.Add(blast.Team); }
                if (tick % every != 0 && !session.IsFinished) continue;
                if (!first) sb.Append(','); first = false;
                sb.Append("{\"t\":").Append(frame.Tick).Append(",\"o\":[");
                owners.Clear();
                bool f2 = true;
                foreach (ViewPoint point in frame.Points) { if (!f2) sb.Append(','); f2 = false; sb.Append(point.Owner); owners.Add(point.Owner); }
                sb.Append("],\"u\":[");
                side[0].Clear(); side[1].Clear(); idleSide[0].Clear(); idleSide[1].Clear();
                pinned.Clear(); broken.Clear(); Array.Clear(pressed, 0, pressed.Length);
                pinnedAt[0].Clear(); pinnedAt[1].Clear(); pinnedMoved[0] = pinnedMoved[1] = 0;
                bool banded = false; inSquads[0] = inSquads[1] = 0; leash[0].Clear(); leash[1].Clear();
                // Per unit: x, z in decimetres, state (0 = not on the field, 1 = alive, 2 = dead).
                for (int i = 0; i < keys.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    bands[i] = -1;
                    if (frame.TryGet(keys[i], out ViewEntity e))
                    {
                        if (e.Band != 0 && indexOf.TryGetValue(e.Band, out int lead))
                        {
                            bands[i] = lead; banded = true;
                            if (teams[i] >= 1 && teams[i] <= 2)
                            {
                                inSquads[teams[i] - 1]++;
                                if (lead != i && frame.TryGet(e.Band, out ViewEntity leader) && leader.Alive)
                                    leash[teams[i] - 1].Add(Math.Sqrt((e.X - leader.X) * (double)(e.X - leader.X) + (e.Z - leader.Z) * (double)(e.Z - leader.Z)));
                            }
                        }
                        sb.Append(D(e.X)).Append(',').Append(D(e.Z)).Append(',').Append(e.Alive ? 1 : 2);
                        if (e.Alive && teams[i] >= 1 && teams[i] <= 2)
                        {
                            side[teams[i] - 1].Add((e.X, e.Z)); idleSide[teams[i] - 1].Add(frame.Tick - since[i] >= Metrics.IdleTicks);
                            if (e.Nerve == 1)
                            {
                                pinned.Add(i); pressed[teams[i] - 1, 0]++; pinnedAt[teams[i] - 1].Add((e.X, e.Z));
                                if (!float.IsNaN(frameX[i]) && (e.X != frameX[i] || e.Z != frameZ[i])) pinnedMoved[teams[i] - 1]++;
                            }
                            else if (e.Nerve == 2) { broken.Add(i); pressed[teams[i] - 1, 1]++; }
                        }
                        frameX[i] = e.Alive ? e.X : float.NaN; frameZ[i] = e.Z;
                    }
                    else { sb.Append("0,0,0"); frameX[i] = float.NaN; }
                }
                // Per frame metrics: [share1, share2, nnSpread1, nnSpread2, spread1, spread2, close1, close2, idle1, idle2, closeNear1, closeNear2,
                // enemy20_1, enemy20_2, idleNoEnemy1, idleNoEnemy2, pinned1, pinned2, broken1, broken2, pinnedHidden1, pinnedHidden2,
                // pinnedMoving1, pinnedMoving2, pinnedAtWall1, pinnedAtWall2, squadShare1, squadShare2, squadLeash1, squadLeash2,
                // squadLeashP90_1, squadLeashP90_2]; null when undefined.
                var values = new double[2 * metricCount];
                for (int t = 0; t < 2; t++)
                {
                    values[t] = Metrics.MaxPointShare(side[t], points);
                    values[2 + t] = Metrics.NearestNeighbourSpread(side[t]);
                    values[4 + t] = Metrics.AttackBearingSpread(side[t], t + 1, points, owners);
                    values[6 + t] = Metrics.CloseShare(side[t]);
                    values[8 + t] = Metrics.IdleOutside(side[t], idleSide[t], points, ring);
                    values[10 + t] = Metrics.CloseShareNearPoints(side[t], points);
                    values[12 + t] = Metrics.EnemyWithin(side[t], side[1 - t]);
                    values[14 + t] = Metrics.IdleNoEnemy(side[t], idleSide[t], side[1 - t], points, ring);
                    values[16 + t] = Metrics.Share(pressed[t, 0], side[t].Count);
                    values[18 + t] = Metrics.Share(pressed[t, 1], side[t].Count);
                    values[20 + t] = Metrics.PinnedHidden(pinnedAt[t], side[1 - t], walls);
                    values[22 + t] = Metrics.PinnedMoving(pinnedMoved[t], pinnedAt[t].Count);
                    values[24 + t] = Metrics.PinnedAtWall(pinnedAt[t], walls);
                    values[26 + t] = banded ? Metrics.Share(inSquads[t], side[t].Count) : double.NaN;
                    values[28 + t] = Metrics.Leash(leash[t], 0.5);
                    values[30 + t] = Metrics.Leash(leash[t], 0.9);
                    for (int m = 0; m < metricCount; m++) stats[t, m].Add(values[m * 2 + t]);
                }
                sb.Append("],\"m\":[");
                for (int v = 0; v < values.Length; v++) { if (v > 0) sb.Append(','); sb.Append(N(values[v])); }
                // Unit indices that are pinned (n) and broken (b); written only when there are any, so a setup without the pressure system keeps its frames.
                if (pinned.Count > 0) sb.Append("],\"n\":[").Append(string.Join(",", pinned));
                if (broken.Count > 0) sb.Append("],\"b\":[").Append(string.Join(",", broken));
                // Each unit's squad leader by unit index (-1 = in no squad; a leader names itself); written only when somebody is in a squad.
                if (banded) sb.Append("],\"q\":[").Append(string.Join(",", bands));
                // Blasts since the previous recorded frame (x, z, outer radius in decimetres, team); written only when there are any.
                if (blasts.Count > 0) sb.Append("],\"x\":[").Append(string.Join(",", blasts));
                sb.Append("],\"s\":[").Append(string.Join(",", shots)).Append("],\"a\":[").Append(frame.Match.AliveOne).Append(',').Append(frame.Match.AliveTwo)
                  .Append(',').Append(frame.Match.ReserveOne).Append(',').Append(frame.Match.ReserveTwo).Append("],\"h\":\"").Append(session.StateHash.ToString("X16")).Append("\"}");
                shots.Clear(); blasts.Clear();
            }
            bool decided = session.IsFinished && session.Winner != 0;
            sb.Append("],\"metrics\":{\"attackRange\":").Append(N(Metrics.AttackRange)).Append(",\"slotRing\":").Append(N(ring)).Append(",\"decidedTick\":").Append(decided ? session.EndTick.ToString(CultureInfo.InvariantCulture) : "null");
            string[] names = { "maxPointShare", "nnSpread", "attackSpread", "closeShare", "idleOutside", "closeShareNearPoints", "enemyWithin20", "idleNoEnemy", "pinnedShare", "brokenShare", "pinnedHidden", "pinnedMoving", "pinnedAtWall", "squadShare", "squadLeash", "squadLeashP90" };
            var line = new StringBuilder();
            for (int t = 0; t < 2; t++)
            {
                sb.Append(",\"team").Append(t + 1).Append("\":{");
                for (int m = 0; m < metricCount; m++)
                {
                    if (m > 0) sb.Append(',');
                    Metrics.Series s = stats[t, m];
                    sb.Append('"').Append(names[m]).Append("\":{\"mean\":").Append(N(s.Mean)).Append(",\"peak\":").Append(N(s.Peak)).Append(",\"frames\":").Append(s.Count).Append('}');
                    line.Append(' ').Append(names[m]).Append(t + 1).Append('=').Append(N(s.Mean)).Append('/').Append(N(s.Peak));
                }
                sb.Append('}');
            }
            sb.Append("},\"winner\":").Append(session.Winner).Append(",\"endTick\":").Append(session.EndTick).Append(",\"hash\":\"").Append(session.StateHash.ToString("X16")).Append("\"}");
            summary = $"{setup}: ticks={session.Tick} winner={session.Winner} decidedTick={(decided ? session.EndTick.ToString(CultureInfo.InvariantCulture) : "none")} hash={session.StateHash:X16} bytes={sb.Length} mean/peak:{line}";
            return sb.ToString();
        }

        private static string F(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        private static string N(double value) => double.IsNaN(value) ? "null" : value.ToString("0.###", CultureInfo.InvariantCulture);
        private static int D(float metres) => (int)Math.Round(metres * 10f);
    }
}
