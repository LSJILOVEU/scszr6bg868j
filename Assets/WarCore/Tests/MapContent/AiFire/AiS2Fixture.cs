using System;
using System.Collections.Generic;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // AI-S2 fixtures, built on P1's, P2's and P3's (their files are not touched).
    //
    // The line: A2's map without walls and without grass, P3's combat pack (5-tick rifle, 10 rounds, 60-tick reload,
    // effect 0, so nobody dies), 20 armed team-1 shooters that barely move (1 mm/s) on the column x = 22, z = 1..20 (keys
    // 101..120), and the unarmed target B (squad 2, held from tick 1) standing at (8, 10). Every shooter has B within the
    // 20 m sight from the start (the farthest, z = 1 and z = 20, at 16.9 m) and no wall or ally on its line, so all twenty
    // select B on tick 1.
    //
    // Hand values: d = Permille(id, salt) * (2s + 1) / 1000 - s, value = v * (1000 + d) / 1000 rounded down, a burst at
    // least 1. The ids are StableEntityKey.Placed(1, key).Id; the expected numbers were worked out from these definitions
    // and AiParts.Permille outside the game code.
    internal static class S2
    {
        internal const int Shooters = 20;
        internal const ulong FirstKey = 101, KeyB = P1.KeyB;
        internal static readonly SimVec3 BAt = A2.Cell(8, 10);
        internal const int R = 30, B = 3, Q = 20, Spread = 300;
        internal static string Ai(int? spread) => Ai(R, B, Q, spread);
        internal static string Ai(int r, int b, int q, int? spread, string more = "") =>
            "{ \"reactionTicks\": " + r + ", \"burstShots\": " + b + ", \"burstPauseTicks\": " + q
            + (spread == null ? "" : ", \"traitSpreadPermille\": " + spread.Value) + more + " }";

        // Per line shooter (keys 101..120) at s = 300: reaction (team 30), burst (team 3), pause (team 20).
        internal static readonly int[] LineR = { 26, 37, 32, 34, 22, 35, 35, 27, 36, 21, 35, 29, 24, 27, 26, 34, 30, 34, 29, 22 };
        internal static readonly int[] LineB = { 3, 2, 2, 2, 3, 3, 3, 2, 3, 3, 3, 3, 2, 3, 3, 3, 3, 3, 3, 2 };
        internal static readonly int[] LineQ = { 17, 23, 14, 23, 18, 22, 18, 16, 17, 17, 15, 18, 15, 25, 23, 19, 20, 18, 16, 15 };

        internal static StableId Placed(ulong key) => StableEntityKey.Placed(1, key).Id;

        internal static UnitSpawn[] Line()
        {
            var units = new List<UnitSpawn>();
            for (int i = 0; i < Shooters; i++) units.Add(A2.Soldier(FirstKey + (ulong)i, 1, A2.Cell(22, 1 + i), 0, 1));
            units.Add(P1.Target(KeyB, 2, BAt));
            return units.ToArray();
        }

        internal static M6World LineWorld(string ai, bool reverse = false) =>
            new M6World(P3.Recipe(Line(), ai), WcMapWriter.Write(A2.Map(Array.Empty<(int, int, int, int)>(), (x, z) => false)), reverse);

        internal sealed class LineRun
        {
            internal M6World W;
            internal readonly long[] Select = new long[Shooters];
            internal readonly List<long>[] Fires = new List<long>[Shooters];
            internal readonly List<ulong> Hashes = new List<ulong>();
        }

        // Steps the line to the given tick: per shooter the first tick it selected B (P1.Selected after the step, -1 for
        // never) and every tick it fired; the hash after every step (index = tick, index 0 = the start).
        internal static LineRun RunLine(string ai, long last, bool reverse = false)
        {
            var r = new LineRun { W = LineWorld(ai, reverse) };
            for (int i = 0; i < Shooters; i++) { r.Select[i] = -1; r.Fires[i] = new List<long>(); }
            ulong b = P1.Id(r.W, KeyB).Value;
            r.Hashes.Add(r.W.Hash);
            for (long tick = 1; tick <= last; tick++)
            {
                r.W.Sim.Step(P2.Drive(r.W, tick, Array.Empty<P1.Move>()));
                for (int i = 0; i < Shooters; i++)
                {
                    ulong key = FirstKey + (ulong)i;
                    if (r.Select[i] < 0 && P1.Selected(r.W, key) == b) r.Select[i] = tick;
                    if (P1.Fired(r.W, key)) r.Fires[i].Add(tick);
                }
                r.Hashes.Add(r.W.Hash);
            }
            return r;
        }

        // A ReactionFire made on a fresh world's combat system (not registered), for the per-shooter values.
        internal static ReactionFire Fire(int r, int b, int q, int spread)
        {
            CombatSystem combat = P3.World(P2.Cast(), null).Combat;
            return new ReactionFire(combat, combat.BuiltInAutoFire, r, 1, b, q, null, spread);
        }
    }
}
