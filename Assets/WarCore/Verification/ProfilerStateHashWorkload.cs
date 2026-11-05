using System;
using System.IO;
using WarCore.Sim;
using WarCore.Verification.Fixtures;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Verification
{
    // Partition probes retain a fresh-sink control; the public API reuses its Sim-owned sink.
    public sealed class ProfilerStateHashWorkload
    {
        private readonly string name;
        private readonly Simulation sim;
        private readonly LoadSystem load;
        private readonly SingleMatchMode mode = new SingleMatchMode();
        private readonly SimEventBuffer events;
        private readonly SimScheduler populatedScheduler;
        private readonly SimEventBuffer populatedEvents;
        private readonly SimRandom emptyRandom = new SimRandom(123);
        private readonly SimRandom manyRandom = new SimRandom(123);
        private readonly FactionRelations factions;
        private readonly MovementSystem movement;
        public ulong Expected { get; }
        public ulong SceneHash { get; }

        public ProfilerStateHashWorkload(string name)
        {
            this.name = name;
            events = new SimEventBuffer(8, 64, 64);
            sim = new Simulation(123, mode, eventBuffer: events);
            load = new LoadSystem(sim, 2000);
            sim.Register("load", load);
            // Match the original state-hash scene exactly: tick 0, one RNG stream, no factions.
            SceneHash = sim.StateHash();
            var composed = new StateHash();
            Header(composed);
            sim.Entities.WriteState(composed);
            sim.Rng.WriteState(composed);
            sim.Scheduler.WriteState(composed);
            Systems(composed);
            events.WriteState(composed);
            HashScenarios.Require(composed.Value == SceneHash, "State hash partition differs from public API.");
            HashScenarios.Require(SceneHash == 0x2D5D2DAB85CD412EUL, "Original hash-only scene vector changed.");
            populatedScheduler = new SimScheduler(64);
            populatedEvents = new SimEventBuffer(8, 64, 64);
            for (int tick = 1; tick <= 8; tick++)
            {
                populatedEvents.BeginTick(tick);
                populatedScheduler.Advance(tick, populatedEvents);
                for (int i = 0; i < 4; i++)
                    populatedScheduler.Schedule(tick + 2, new SimEvent(new EventTypeId(30), tag: i + 1));
                populatedEvents.Publish(new SimEvent(new EventTypeId(31), tag: tick));
                populatedEvents.EndTick();
            }
            populatedScheduler.CompleteWarmup();
            populatedEvents.CompleteWarmup();
            manyRandom.NextUInt64(manyRandom.Stream("zeta"));
            manyRandom.NextUInt64(manyRandom.Stream("alpha"));
            manyRandom.NextUInt64(manyRandom.Stream("middle"));
            factions = new FactionRelations(2, new[] { FactionRelation.Friendly, FactionRelation.Hostile,
                FactionRelation.Neutral, FactionRelation.Friendly });
            var store = new EntityStore();
            var positions = store.Register<Position>("position");
            var movements = store.Register<Movement>("movement");
            var navigation = new NavProviderRegistry();
            navigation.Register(MoveMode.Ground, new FlatGroundNavProvider());
            movement = new MovementSystem(store, positions, movements, navigation, 1);
            Expected = Sample();
        }

        private void Header(StateHash hash)
        {
            hash.Add("warcore-state-m0b1-v1");
            sim.Clock.WriteState(hash);
            sim.Context.Input.WriteState(hash);
            hash.Add(false); // started
            hash.Add(false); // faulted
            hash.Add("single-match-v1");
            mode.WriteState(hash);
            hash.Add(mode.IsFinished);
        }

        private void Systems(StateHash hash)
        {
            hash.Add(1);
            hash.Add("load");
            hash.Add(load.Order);
            load.WriteState(hash);
        }

        public ulong Sample()
        {
            if (name == "state-hash") return sim.StateHash();
            var hash = new StateHash();
            switch (name)
            {
                case "state-hash-new": break;
                case "state-hash-strings":
                    // Eight string writes in the original public hash, in traversal order.
                    hash.Add("warcore-state-m0b1-v1"); hash.Add("single-match-v1"); hash.Add("single-match-v1");
                    hash.Add("collider"); hash.Add("health"); hash.Add("position"); hash.Add("load"); hash.Add("load");
                    break;
                case "state-hash-header": Header(hash); break;
                case "state-hash-entities": sim.Entities.WriteState(hash); break;
                case "state-hash-scheduler": sim.Scheduler.WriteState(hash); break;
                case "state-hash-events": events.WriteState(hash); break;
                case "state-hash-scheduler-populated": populatedScheduler.WriteState(hash); break;
                case "state-hash-events-populated": populatedEvents.WriteState(hash); break;
                case "state-hash-rng": sim.Rng.WriteState(hash); break;
                case "state-hash-rng-empty": emptyRandom.WriteState(hash); break;
                case "state-hash-rng-many": manyRandom.WriteState(hash); break;
                case "state-hash-systems": Systems(hash); break;
                case "state-hash-factions": hash.Add("warcore.factions-v1"); factions.WriteState(hash); break;
                case "state-hash-movement": movement.WriteState(hash); break;
                default: throw new ArgumentException("Unknown state hash probe: " + name);
            }
            return hash.Value;
        }

        public void Describe(TextWriter output)
        {
            string sink = name == "state-hash" ? "sim-owned-reused" : "fresh-StateHash-per-call";
            string allocation = name == "state-hash" ? "sim-delta-zero" : "fresh-sink-positive";
            output.WriteLine("hash_probe|name={0};sink={3};control=state-hash-new;scene_tick=0;scene_hash=0x{1:X16};expected=0x{2:X16};partitions_verified=true;expected_allocation={4}", name, SceneHash, Expected, sink, allocation);
            output.WriteLine("hash_probe.scope|header=clock-input-lifecycle-mode;systems=registered-LoadSystem-plus-id-order;strings=8-original-ASCII-writes;rng=one-stream;populated=8-ticks-with-pending-and-due-tasks;factions/movement=separate-extension-fixtures;SimConfig/SaveState=not-on-hash-path");
        }
    }
}
