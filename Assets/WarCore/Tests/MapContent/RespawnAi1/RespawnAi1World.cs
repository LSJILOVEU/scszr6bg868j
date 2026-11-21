using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // RESPAWN-AI-1 fixture: flat 80 x 40 m map, two teams, no player. Team 1: two on the field (keys 1, 2) and six reserves
    // (11..16), base B1 at (6, 20). Team 2: two on the field (3, 4) and six reserves (21..26), base B2 at (74, 20). Point P
    // (id 5, index 0) at (40, 20), radius 4 m, spawn on its centre, owned by team 1; a neutral point with no spawn far away.
    // Reinforcement every 30 ticks, a 2 x 2 grid 2 m apart. Capture takes 8000 ticks and the AI thinks every 1000000, so
    // nobody moves and P keeps its owner inside every test window. Nobody carries a weapon; hits deal no damage.
    // Team 1's entry list is [B1, P]. Only the pre-RESPAWN-AI-1 API is used here, so the lock can be recorded on the old code.
    internal static class RespawnAi1World
    {
        internal const int Interval = 30;
        internal const ulong Contester = 30;
        internal static readonly SimVec3 B1 = SimVec3.Metres(6, 0, 20), B2 = SimVec3.Metres(74, 0, 20), PCentre = SimVec3.Metres(40, 0, 20);

        internal static UnitSpawn U(ulong key, long xMm, long zMm, int team, bool reserve = false, int layer = 0) =>
            new UnitSpawn(key, 0, team, xMm, zMm, 1, team, -1, reserve, layer: layer);

        // extra: units added to the base set (for example an enemy standing in P's circle).
        internal static List<UnitSpawn> Units(params UnitSpawn[] extra)
        {
            var units = new List<UnitSpawn>
            {
                U(1, 10000, 36000, 1), U(2, 14000, 36000, 1), U(3, 70000, 36000, 2), U(4, 66000, 36000, 2),
            };
            for (ulong k = 11; k <= 16; k++) units.Add(U(k, 2000, 2000, 1, reserve: true));
            for (ulong k = 21; k <= 26; k++) units.Add(U(k, 78000, 2000, 2, reserve: true));
            units.AddRange(extra);
            return units;
        }

        internal static ControlPointDef P(int owner = 1, bool spawn = true) =>
            new ControlPointDef(5, PCentre, SimFix.FromInt(4), owner, spawn, spawn ? PCentre : default);
        internal static ControlPointDef Far() => new ControlPointDef(9, SimVec3.Metres(60, 0, 4), SimFix.FromInt(2));

        internal static SpawnAnchor[] Bases(bool teamOneBase = true) => teamOneBase
            ? new[] { new SpawnAnchor(1, B1), new SpawnAnchor(2, B2) }
            : new[] { new SpawnAnchor(2, B2) };

        // The pack through the constructor the old code has (no RESPAWN-AI-1 field).
        internal static MatchPack OldPack(ControlPointDef point, bool teamOneBase = true) =>
            new MatchPack(new[] { point, Far() }, Bases(teamOneBase), 2, 1, false, 8000, InterruptMode.Reset, 1, Interval, 1000000, 100000,
                SimFix.FromInt(35), 2, 2, SimFix.FromInt(2));

        internal static WorldRecipe Recipe(IEnumerable<UnitSpawn> units, MatchPack pack) =>
            M6World.Endless(RespawnWorld.Recipe(units, pack, player: false));

        internal static M6World World(IEnumerable<UnitSpawn> units, MatchPack pack, byte[] map = null)
        {
            M6World w = null;
            Assert.DoesNotThrow(() => w = new M6World(Recipe(units, pack), map));
            return w;
        }

        // Every deployment record of team `team` up to tick `ticks`, as (tick, unit key, point).
        internal static List<(long tick, ulong key, SimVec3 point)> Deploys(M6World w, int team, int ticks, Action<M6World> each = null)
        {
            var list = new List<(long, ulong, SimVec3)>();
            for (int t = 0; t < ticks; t++)
            {
                Assert.DoesNotThrow(() => w.Step());
                foreach (DeployRecord r in w.Reinforce.Deployed)
                    if (r.Team == team) list.Add((r.Tick, KeyOf(w, r.Unit), r.Point));
                each?.Invoke(w);
            }
            return list;
        }

        internal static ulong KeyOf(M6World w, StableId unit)
        {
            foreach (UnitSpawn spawn in w.Recipe.Units)
                if (w.Sim.Entities.StableOf(w.Unit(spawn.Key)).Equals(unit)) return spawn.Key;
            return 0;
        }

        internal static bool Near(SimVec3 at, SimVec3 anchor) =>
            Math.Abs(at.X.Raw - anchor.X.Raw) <= SimFix.Scale && Math.Abs(at.Z.Raw - anchor.Z.Raw) <= SimFix.Scale;

        internal static byte[] Saved(ReinforceSystem system)
        {
            var writer = new SimStateWriter();
            system.SaveState(writer);
            return writer.ToArray();
        }

        internal static ulong HashOf(ReinforceSystem system)
        {
            var hash = new StateHash();
            system.WriteState(hash);
            return hash.Value;
        }

        // 64-bit FNV-1a over the per-tick retained hashes.
        internal static ulong Run(M6World w, int ticks)
        {
            ulong h = 14695981039346656037UL;
            for (int t = 0; t < ticks; t++)
            {
                Assert.DoesNotThrow(() => w.Step());
                ulong v = w.Hash;
                for (int b = 0; b < 8; b++) { h ^= (byte)(v >> (8 * b)); h *= 1099511628211UL; }
            }
            return h;
        }
    }
}
