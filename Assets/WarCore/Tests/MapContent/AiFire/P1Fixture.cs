using System;
using System.Collections.Generic;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // P1 fixtures, built on A3's memory fixture through its helpers (A3Fixture.cs; A3's files are not touched): A2's map with
    // its wall, grass concealing to a half (Lc = 10 m, L = 20 m), the rifle with effect 0 (30-tick interval, 3 rounds, 60-tick
    // reload), thinking interval 10, seed 41. Observer A (key 1, team 1, armed) stands on the only control point, which team 1
    // owns, so its AI never moves it. Targets (team 2, unarmed) move only on squad commands. Here the targets start west of A,
    // beyond the 20 m sight, and walk in along z = 10; nothing else moves.
    internal static class P1
    {
        internal const ulong KeyA = A3.KeyA, KeyB = A3.KeyB, KeyC = A3.KeyC, KeyA2 = 4;
        internal static readonly SimFix Range = SimFix.FromInt(30);   // the rifle's FalloffEnd (A2Fixture.cs Rifle)

        // The content layer: A3's (grass 500) with the ai object when given, else without the ai key.
        internal static string PackJson(string ai) => ai == null ? A3.PackJson(A3.Grass500, (int?)null) : A3.PackJson(A3.Grass500, ai);
        internal static string Ai(int reaction) => "{ \"reactionTicks\": " + reaction + " }";

        internal static UnitSpawn Target(ulong key, int squad, SimVec3 at, long speed = 3000) =>
            new UnitSpawn(key, 0, squad, at.X.Raw * 1000 / A2.S, at.Z.Raw * 1000 / A2.S, speed, 2, -1, false);
        internal static UnitSpawn Shooter(ulong key, SimVec3 at) => A2.Soldier(key, 1, at, 0, 3000);

        internal static WorldRecipe Recipe(UnitSpawn[] units, string ai, params IWorldOption[] extensions)
        {
            WorldRecipe r = A3.Recipe(units, A3.Load(PackJson(ai)));
            foreach (IWorldOption e in extensions) r = r.With(e);
            return r;
        }
        internal static M6World World(UnitSpawn[] units, string ai, bool reverse = false, params IWorldOption[] extensions) =>
            new M6World(Recipe(units, ai, extensions), WcMapWriter.Write(A2.Map(A2.DWalls)), reverse);

        internal static InputFrame Command(SquadCommandKind kind, int squad, SimVec3 at = default) =>
            new InputFrame(default, default, squad: new SquadCommand(kind, squad, at, default));

        // A script: (tick, squad, kind, destination). Ticks 1 and 2 hold squads 2 and 3; afterwards a squad whose move just
        // ended is held again (InterimOrders ends a move standing on arrival), so every target stays under squad command.
        internal readonly struct Move
        {
            internal readonly long Tick; internal readonly int Squad; internal readonly SimVec3 To;
            internal Move(long tick, int squad, SimVec3 to) { Tick = tick; Squad = squad; To = to; }
        }
        internal static InputFrame Drive(M6World w, long tick, Move[] script)
        {
            if (tick == 1) return Command(SquadCommandKind.Hold, 2);
            if (tick == 2) return Command(SquadCommandKind.Hold, 3);
            foreach (Move m in script) if (m.Tick == tick) return Command(SquadCommandKind.MoveTo, m.Squad, m.To);
            if (Ended(w, KeyB)) return Command(SquadCommandKind.Hold, 2);
            if (Ended(w, KeyC)) return Command(SquadCommandKind.Hold, 3);
            return default;
        }
        private static bool Ended(M6World w, ulong key) => w.Has<SquadStanding>(key)
            && w.Read<SquadStanding>(key).Kind == StandingKind.Move && !w.Read<MoveIntent>(key).Active;

        internal static StableId Id(M6World w, ulong key) => w.Sim.Entities.StableOf(w.Unit(key));

        // What the shooter's selector picks now (outside the combat step; Sight.cs takes the conservative path there).
        internal static ulong Selected(M6World w, ulong shooter)
        {
            if (!w.Combat.Pack.Selector.TrySelect(w.Combat, w.Unit(shooter), Range, out EntityId target)) return 0;
            return w.Sim.Entities.StableOf(target).Value;
        }

        // The ticks of the shooter's fire records this tick.
        internal static bool Fired(M6World w, ulong shooter)
        {
            StableId id = Id(w, shooter);
            foreach (FireRecord f in w.Combat.Fires) if (f.Shooter.Equals(id)) return true;
            return false;
        }

        // 64-bit FNV-1a over the bytes.
        internal static ulong Digest(byte[] bytes) => A3.Digest(bytes);
        internal static string Systems(M6World w) => L4.Systems(w);
    }
}
