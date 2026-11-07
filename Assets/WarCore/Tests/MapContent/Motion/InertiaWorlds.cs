using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests.Motion
{
    // Fixtures of the M6 inertia tests. Every parameter that changes behaviour is written here: seed 41, a flat 40 x 24 m map
    // without walls, one AI unit (key 1, squad 1) at (5 m, 12 m) with 6000 mm/s (13107 raw per tick), ordered at tick 1.
    internal static class InertiaWorlds
    {
        internal const long S = SimFix.Scale;
        internal const long Speed = 6000;
        internal const long StartXMm = 5000, StartZMm = 12000;

        internal static byte[] FlatMap() => WcMapWriter.Write(Wp23.Make(40, 24, 0, 6 * S, null, 4 * S));

        internal static WorldRecipe Line(long toXMm, long toZMm, MoverKind mover = MoverKind.Interim) =>
            new WorldRecipe(41, mover, new[] { new UnitSpawn(1, 0, 1, StartXMm, StartZMm, Speed) },
                new[] { BridgeCommand.MoveTo(1, 1, toXMm, toZMm) });

        internal static WorldRecipe Open(WorldRecipe recipe, long accel, long decel) => recipe.WithMotion().With(new InertiaOption(accel, decel));

        internal static MotionState State(SimSession s, ulong key = 1) => M3TestWorld.Component<MotionState>(s, key);

        // Acceptance 5: walls at x = 10 (z 0..16) and x = 20 (z 8..24) make the path from (5.5, 4.5) to (30.5, 4.5) turn
        // several times; the unit walks 6000 mm/s on the InterimPath mover.
        internal static SimSession Zigzag(long accel, long decel)
        {
            byte[] map = WcMapWriter.Write(Wp23.Make(40, 24, 0, 6 * S, new[] { (10, 0, 1, 16), (20, 8, 1, 16) }, 4 * S));
            var recipe = new WorldRecipe(41, MoverKind.InterimPath, new[] { new UnitSpawn(1, 0, 1, 5500, 4500, Speed) },
                new[] { BridgeCommand.MoveTo(1, 1, 30500, 4500) });
            return Build(Open(recipe, accel, decel), map);
        }

        // Acceptance 6: the five movers of MotionModelTests (flat and hill terrain), unit 1 ordered from (20.5, 12.5) to
        // (30.5, 12.5), unit 2 standing idle at (3.5, 3.5). The hill rises 1/16 m per metre along X.
        internal static readonly (MoverKind Mover, bool Hill)[] Movers =
        {
            (MoverKind.Interim, false), (MoverKind.InterimPath, false), (MoverKind.InterimCrowd, false),
            (MoverKind.InterimPath, true), (MoverKind.InterimCrowd, true),
        };

        internal static SimSession Mover(MoverKind mover, bool hill, long accel, long decel)
        {
            var units = new[] { new UnitSpawn(1, 0, 1, 20500, 12500, Speed), new UnitSpawn(2, 0, 2, 3500, 3500, Speed) };
            var recipe = Open(new WorldRecipe(41, mover, units, new[] { BridgeCommand.MoveTo(1, 1, 30500, 12500) }), accel, decel);
            WcMap map = hill ? Wp23.Make(40, 24, 0, 6 * S, null, 4 * S, (i, j) => i * (int)(S / 16)) : Wp23.Make(40, 24, 0, 6 * S, null, 4 * S);
            if (hill) recipe = recipe.WithTerrainRules(Wp23.Pack());
            return Build(recipe, WcMapWriter.Write(map));
        }

        // Acceptance 7, walls: a 1 m wall as in ControlledSweepTests, here cells x = 60, z 0..30, on an 80 x 40 m map; unit 1 on
        // the Interim mover starts at (3.5, 10.5) and is ordered to (75.5, 10.5), through the wall. The 60 m run-up lets the
        // fastest unit (240000 mm/s, 8 m per tick) reach its full speed before the wall.
        internal const long WallLeftRaw = 60 * S, Radius = 16384;

        internal static SimSession Wall(long speedMm, long accel, long decel)
        {
            var recipe = new WorldRecipe(41, MoverKind.Interim, new[] { new UnitSpawn(1, 0, 1, 3500, 10500, speedMm) },
                new[] { BridgeCommand.MoveTo(1, 1, 75500, 10500) });
            return Build(Open(recipe, accel, decel), WcMapWriter.Write(M3TestWorld.Boxes(80, 40, (60, 0, 1, 30))));
        }

        // Acceptance 7, crowd: unit 1 (team 1) on the crowd mover from (3.5, 10.5) toward (60.5, 10.5), along cell centres;
        // unit 3 of team 2 stands at (45.5, 10.5 + offset) in the way. No pass rule is set, and the bystander is on the other
        // team either way.
        internal static SimSession Crowd(long speedMm, long offsetZMm, long accel, long decel)
        {
            var units = new[] { new UnitSpawn(1, 0, 1, 3500, 10500, speedMm, team: 1), new UnitSpawn(3, 0, 3, 45500, 10500 + offsetZMm, Speed, team: 2) };
            var recipe = new WorldRecipe(41, MoverKind.InterimCrowd, units, new[] { BridgeCommand.MoveTo(1, 1, 60500, 10500) });
            return Build(Open(recipe, accel, decel), WcMapWriter.Write(Wp23.Make(80, 24, 0, 6 * S, null, 4 * S)));
        }

        // A world with a player (key 1, crowd mover with match, walking +X by input) and an AI unit (key 3) ordered along +X.
        internal static SimSession Player(long accel, long decel)
        {
            var units = new[]
            {
                new UnitSpawn(1, 0, 1, 8000, 10000, Speed, team: 1), new UnitSpawn(2, 0, 2, 70000, 35000, Speed, team: 2),
                new UnitSpawn(3, 0, 3, 8000, 14000, Speed, team: 1),
            };
            WorldRecipe recipe = Open(Wp16TestWorld.Recipe(units, 1, Wp16TestWorld.Match()), accel, decel);
            SimSession s = Build(recipe, WcMapWriter.Write(M3TestWorld.Boxes(80, 40)));
            s.Enqueue(BridgeCommand.MoveTo(1, 3, 20000, 14000));
            s.SetPlayerInput(1, new PlayerInput(1000, 0));
            return s;
        }

        internal static SimSession Build(WorldRecipe recipe, byte[] map, bool reverse = false) => WorldBuilder.Build(recipe, map, reverse);

        internal static string Sha(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }

        internal static string SnapshotSha(SimSession s) => Sha(M3TestWorld.Sim(s).SaveRetainedSnapshot());
    }
    // A stand-in model that records every call with the tick and returns the wanted speed unchanged.
    internal sealed class RecordingMotion : IMotionModel
    {
        internal readonly List<(long Tick, EntityId Unit)> Calls = new List<(long, EntityId)>();
        private readonly SimClock clock;
        internal RecordingMotion(SimClock clock) { this.clock = clock; }
        public SimFix StepSpeed(EntityId unit, SimFix wanted) { Calls.Add((clock.Tick, unit)); return wanted; }
    }
}
