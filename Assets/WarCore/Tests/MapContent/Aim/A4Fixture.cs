using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // A4 fixtures that use only the interfaces that existed before A4 (this file is frozen with the first commit).
    // Helpers that need the new interfaces live in A4Fixture.New.cs.
    internal static partial class A4
    {
        internal const long S = SimFix.Scale;
        internal const int FlatTicks = 600;
        internal const long LiftedGroundRaw = 98304; // 1.5 m

        // World (a)/(c) unit table on the M2b map (64 x 32 m, walls WarCore.Content.M2DemoMap): the player (1) walks into an idle
        // teammate (2) and is held there by the crowd (enemy 3 stands behind it); enemy 4 is the target past the end of the long
        // wall at x 24..26, z 0..24.
        internal static UnitSpawn[] FlatUnits() => new[]
        {
            new UnitSpawn(1, 0, 1, 10000, 16000, 6000, 1, 0, false),
            new UnitSpawn(2, 0, 1, 13000, 16000, 6000, 1, -1, false),
            new UnitSpawn(3, 0, 2, 16000, 16000, 6000, 2, -1, false),
            new UnitSpawn(4, 0, 2, 20000, 28000, 6000, 2, -1, false),
        };

        internal static WorldRecipe FlatRecipe() =>
            Wp18TestWorld.Recipe(M5TestWorld.NoSpread(WarCore.Content.Rwr.RwrPack.Create()), FlatUnits());

        // The M2b map with every field copied and Min.Y = FlatHeight = 1.5 m (WcMapValidation needs FlatHeight == Min.Y < Max.Y).
        internal static byte[] LiftedM2bBytes()
        {
            WcMap map = M2DemoMap.Build();
            var min = new SimVec3(map.Min.X, SimFix.FromRaw(LiftedGroundRaw), map.Min.Z);
            var lifted = new WcMap(map.MapId, map.ContentId, min, map.Max, map.ChunkSize, SimFix.FromRaw(LiftedGroundRaw),
                map.Width, map.Depth, ToArray(map.Layers), ToArray(map.Walls), ToArray(map.Passability));
            return WcMapWriter.Write(lifted);
        }

        private static T[] ToArray<T>(IReadOnlyList<T> list)
        {
            var array = new T[list.Count];
            for (int i = 0; i < array.Length; i++) array[i] = list[i];
            return array;
        }

        // Four phases of 150 ticks: walk and aim at the wall, the open map, the target; primary held throughout.
        internal static PlayerInput FlatScript(int phase) => phase switch
        {
            0 => new PlayerInput(1000, 0, 30000, 10000, true),    // +x into 2 and 3; the line to (30, 10) crosses the long wall
            1 => new PlayerInput(0, 1000, 2000, 31000, true),     // +z; open map
            2 => new PlayerInput(-1000, 0, 20000, 28000, true),   // -x; enemy 4
            _ => new PlayerInput(0, -1000, 20000, 28000, true),   // -z; enemy 4
        };

        internal struct FlatStats { internal int Fires, Hit, Wall, Open, Deaths, Respawns, PushedTicks, BlockedTicks; internal long TravelRaw; }

        // Runs the session tick by tick through StepOnce, feeding script(tick) (null keeps the held input). Folds every state
        // hash and every fire; on every tick the controlled unit is alive, asserts its Position.Y == groundRaw.
        internal static (ulong hash, ulong fires, FlatStats stats) RunPlayer(M6World w, int ticks, Func<long, PlayerInput?> script,
            long groundRaw)
        {
            ulong fold = Fnv(Fnv(14695981039346656037UL, w.Session.StateHash), 0);
            ulong fires = 14695981039346656037UL;
            var stats = new FlatStats();
            bool wasAlive = true;
            ulong? lastKey = w.Session.ControlledKey;
            SimVec3 last = w.Read<Position>(lastKey.Value).Value;
            int moveX = 0, moveZ = 0;
            for (int i = 0; i < ticks && !w.Session.IsFinished; i++)
            {
                long next = w.Session.Tick + 1;
                PlayerInput? input = script(next);
                if (input.HasValue)
                {
                    w.Session.SetPlayerInput(next, input.Value);
                    moveX = input.Value.MoveXMilli; moveZ = input.Value.MoveZMilli;
                }
                w.Session.StepOnce();
                long tick = w.Session.Tick;
                foreach (FireRecord f in w.Combat.Fires)
                {
                    stats.Fires++;
                    if (f.Outcome == FireOutcome.Hit) stats.Hit++;
                    if (f.Outcome == FireOutcome.WallBlocked) stats.Wall++;
                    if (f.Outcome == FireOutcome.Open) stats.Open++;
                    fires = Fnv(fires, (ulong)tick); fires = Fnv(fires, f.Shooter.Value); fires = Fnv(fires, f.Target.Value);
                    fires = Fnv(fires, (ulong)f.Origin.X.Raw); fires = Fnv(fires, (ulong)f.Origin.Y.Raw); fires = Fnv(fires, (ulong)f.Origin.Z.Raw);
                    fires = Fnv(fires, (ulong)f.End.X.Raw); fires = Fnv(fires, (ulong)f.End.Y.Raw); fires = Fnv(fires, (ulong)f.End.Z.Raw);
                    fires = Fnv(fires, (ulong)f.Distance.Raw); fires = Fnv(fires, (ulong)f.Outcome);
                }
                fold = Fnv(fold, w.Session.StateHash);
                ulong? key = w.Session.ControlledKey;
                bool alive = key.HasValue && w.Session.Current.TryGet(key.Value, out ViewEntity e) && e.Alive;
                if (wasAlive && !alive) stats.Deaths++;
                if (!wasAlive && alive) stats.Respawns++;
                if (alive)
                {
                    SimVec3 p = w.Read<Position>(key.Value).Value;
                    Assert.That(p.Y.Raw, Is.EqualTo(groundRaw), "player Y at tick " + tick);
                    if (key == lastKey && wasAlive)
                    {
                        long dx = p.X.Raw - last.X.Raw, dz = p.Z.Raw - last.Z.Raw;
                        stats.TravelRaw += Math.Abs(dx) + Math.Abs(dz);
                        // Pushed: the unit moved across its own input direction (or moved with no input).
                        bool across = (moveX == 0 && dx != 0) || (moveZ == 0 && dz != 0);
                        if (across) stats.PushedTicks++;
                        // Blocked: the unit had a move input and did not move (a teammate or enemy in the way, or a wall).
                        if ((moveX != 0 || moveZ != 0) && dx == 0 && dz == 0) stats.BlockedTicks++;
                    }
                    last = p;
                }
                lastKey = key; wasAlive = alive;
            }
            return (fold, fires, stats);
        }

        internal static ulong Fnv(ulong fold, ulong value) => (fold ^ value) * 1099511628211UL;

        // Fingerprint of DemoRecipes.MpRespawnDemo that world (b) depends on; a change to the recipe fails here, not silently.
        internal static void AssertRespawnRecipe(WorldRecipe recipe)
        {
            Assert.That(recipe.Units.Length, Is.EqualTo(300));
            Assert.That(recipe.PlayerKey, Is.EqualTo(1UL));
            Assert.That(recipe.Match.RespawnTicks, Is.EqualTo(300));
            Assert.That(recipe.Match.Bases.Length, Is.EqualTo(2));
            Assert.That(recipe.Match.Bases[0].Point, Is.EqualTo(SimVec3.Metres(6, 0, 50)));
            Assert.That(recipe.Match.Bases[1].Point, Is.EqualTo(SimVec3.Metres(154, 0, 50)));
        }
    }
}
