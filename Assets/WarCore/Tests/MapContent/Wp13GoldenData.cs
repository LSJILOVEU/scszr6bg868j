using System;
using System.Globalization;
using System.Security.Cryptography;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // Shared observation only: recording and comparison use exactly the same inputs and tick bounds.
    internal static class Wp13GoldenData
    {
        internal const string BaselinePath = "Assets/WarCore/Tests/MapContent/Golden/wp13-baseline.txt";
        internal static readonly string[] Header = {
            "# WP13 baseline v1; UTF-8; tab-separated; invariant numbers; uppercase hexadecimal hashes.",
            "# tick columns: scenario, tick, StateHash, Fires, RngState, Events",
            "# snapshot columns: snap:<scenario>:<tick>, SHA256(SaveRetainedSnapshot bytes)",
            "# M5: DemoRecipes.M5 + M2bMapBytes; SimSession.StepOnce with recipe script; ticks 0..1800 or Winner, inclusive.",
            "# M6/M6-reverse: DemoRecipes.M6 seed 41 + M6MapBytes; M6World forward/reverse; ticks 0..Winner or 9000, inclusive.",
            "# M7-real: M7BehaviorLockTests.Real(41); M6World.Step through the end tick 747 (Sim refuses any later step).",
            "# M6-narrow: M7BehaviorLockTests.Demo(41) + NarrowMap; forward; ticks 0..Winner or 9000, inclusive.",
            "# Fires: StateHash starts with record count, then each record in original order (never sorted).",
            "# FireRecord fields: Shooter.Value, Target.Value, Origin XYZ raw, End XYZ raw, Distance raw, DamageRaw, ShooterTeam.",
            "# M5 Fires uses session.Current.Fires: X0,Z0,X1,Z1 (IEEE-754 bits as Int32), Team, Lethal.",
            "# M5 public ViewFire omits Shooter/Target, Y, Distance and exact DamageRaw; these are NOT covered by its Fires column.",
            "# RngState: Sim.Rng.WriteState digest of seed and all named streams, including combat.hit; no isolated combat.hit position API is used.",
            "# Events: StateHash of count, stable event Payload.WriteState then Source/Target links, in sequence order; then NextSequence and dropped counters.",
            "# M5 Sim is accessed via existing M3TestWorld.Sim for snapshot/RNG/events only; no new reflection or gameplay systems.",
            "# Snapshots: M5 ticks 20,100; M6, M6-reverse and M6-narrow tick 300. Hashes only, not snapshot payloads."
        };
        private static readonly string[] TickColumns = { "scenario", "tick", "StateHash", "Fires", "RngState", "Events" };
        private static readonly string[] SnapshotColumns = { "snapshot key", "SHA256" };

        internal sealed class Row
        {
            internal readonly string Scenario;
            internal readonly long Tick;
            internal readonly string[] Columns, Values;
            internal Row(string scenario, long tick, string[] columns, params string[] values)
            { Scenario = scenario; Tick = tick; Columns = columns; Values = values; }
            internal string Line => string.Join("\t", Values);
        }

        // End tick of M7BehaviorLockTests.Real(41), taken from the WP-19 A0 baseline; the code never derives it.
        internal const long RealEndTick = 747;

        internal static void Capture(Action<Row> emit)
        {
            CaptureM5(emit);
            CaptureMatch("M6", new M6World(DemoRecipes.M6(), DemoRecipes.M6MapBytes()), emit);
            CaptureMatch("M6-reverse", new M6World(DemoRecipes.M6(), DemoRecipes.M6MapBytes(), reverse: true), emit);
            var real = M7BehaviorLockTests.Real(41);
            EmitWorld("M7-real", real, emit);
            while (real.Sim.Clock.Tick < RealEndTick)
            {
                real.Step();
                EmitWorld("M7-real", real, emit);
            }
            CaptureMatch("M6-narrow", new M6World(M7BehaviorLockTests.Demo(41), M7BehaviorLockTests.NarrowMap()), emit);
        }

        private static void CaptureM5(Action<Row> emit)
        {
            var session = SimSession.Create(DemoRecipes.M5(), DemoRecipes.M2bMapBytes());
            var sim = M3TestWorld.Sim(session);
            EmitTick("M5", sim, session.StateHash, ViewFires(session.Current.Fires), emit);
            while (session.Tick < 1800 && session.Winner == 0)
            {
                long previous = session.Tick;
                session.StepOnce();
                if (session.Tick != previous + 1)
                    throw new InvalidOperationException("M5 StepOnce did not advance exactly one tick at " + Number(previous));
                EmitTick("M5", sim, session.StateHash, ViewFires(session.Current.Fires), emit);
                if (session.Tick == 20 || session.Tick == 100) EmitSnapshot("M5", sim, emit);
            }
            if (session.Tick < 100)
                throw new InvalidOperationException("M5 ended before required snapshot tick 100.");
        }

        private static void CaptureMatch(string scenario, M6World world, Action<Row> emit)
        {
            EmitWorld(scenario, world, emit);
            while (world.Match.Winner == 0 && world.Sim.Clock.Tick < 9000)
            {
                world.Step();
                EmitWorld(scenario, world, emit);
                if (world.Sim.Clock.Tick == 300) EmitSnapshot(scenario, world.Sim, emit);
            }
            if (world.Sim.Clock.Tick < 300)
                throw new InvalidOperationException(scenario + " ended before required snapshot tick 300.");
        }

        private static void EmitWorld(string scenario, M6World world, Action<Row> emit)
        {
            EmitTick(scenario, world.Sim, world.Hash, Fires(world.Combat.Fires), emit);
        }

        private static void EmitTick(string scenario, Simulation sim, ulong stateHash, string fires, Action<Row> emit)
        {
            var rng = new StateHash();
            sim.Rng.WriteState(rng); // Read only: do not register or draw from any stream.
            emit(new Row(scenario, sim.Clock.Tick, TickColumns,
                scenario, Number(sim.Clock.Tick), Hex(stateHash), fires, Hex(rng.Value), Events(sim)));
        }

        private static string Fires(ReadOnlySpan<FireRecord> fires)
        {
            var hash = new StateHash();
            hash.Add(fires.Length);
            foreach (var fire in fires)
            {
                hash.Add(fire.Shooter.Value);
                hash.Add(fire.Target.Value);
                fire.Origin.WriteState(hash);
                fire.End.WriteState(hash);
                fire.Distance.WriteState(hash);
                hash.Add(fire.DamageRaw);
                hash.Add(fire.ShooterTeam);
            }
            return Hex(hash.Value);
        }

        private static string ViewFires(ReadOnlySpan<ViewFire> fires)
        {
            var hash = new StateHash();
            hash.Add(fires.Length);
            foreach (var fire in fires)
            {
                // Bit reinterpretation, not float formatting or runtime GetHashCode. Works on older Unity APIs.
                hash.Add(BitConverter.ToInt32(BitConverter.GetBytes(fire.X0), 0));
                hash.Add(BitConverter.ToInt32(BitConverter.GetBytes(fire.Z0), 0));
                hash.Add(BitConverter.ToInt32(BitConverter.GetBytes(fire.X1), 0));
                hash.Add(BitConverter.ToInt32(BitConverter.GetBytes(fire.Z1), 0));
                hash.Add(fire.Team);
                hash.Add(fire.Lethal);
            }
            return Hex(hash.Value);
        }

        private static string Events(Simulation sim)
        {
            var hash = new StateHash();
            int count = sim.Events.Read(sim.Clock.Tick).Length;
            hash.Add(count);
            for (int i = 0; i < count; i++)
            {
                var item = sim.ReadStableEvent(sim.Clock.Tick, i);
                item.Payload.WriteState(hash);
                item.Source.WriteState(hash);
                item.Target.WriteState(hash);
            }
            hash.Add(sim.Events.NextSequence);
            hash.Add(sim.Events.EventsDroppedThisTick);
            hash.Add(sim.Events.EventsDroppedTotal);
            return Hex(hash.Value);
        }

        private static void EmitSnapshot(string scenario, Simulation sim, Action<Row> emit)
        {
            string digest;
            using (var sha = SHA256.Create())
                digest = BitConverter.ToString(sha.ComputeHash(sim.SaveRetainedSnapshot())).Replace("-", "");
            emit(new Row(scenario, sim.Clock.Tick, SnapshotColumns,
                "snap:" + scenario + ":" + Number(sim.Clock.Tick), digest));
        }

        private static string Hex(ulong value) => value.ToString("X16", CultureInfo.InvariantCulture);
        private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
