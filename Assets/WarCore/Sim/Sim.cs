using System;
using System.Collections.Generic;

namespace WarCore.Sim
{
    // Extensions must encode both configuration and mutable fields that affect subsequent ticks.
    public interface ISimSystem : IStateHash
    {
        int Order
        {
            get;
        }

        void Step(SimContext context);
    }

    public interface IMatchMode : IStateHash
    {
        void Start(SimContext context);
        void Step(SimContext context);
        bool IsFinished
        {
            get;
        }
    }

    public sealed class SingleMatchMode : IMatchMode, IStableStateSerializer
    {
        public bool IsFinished => false;
        public string StateTypeId => "warcore.single-match";
        public int StateVersion => 1;
        public void SaveState(SimStateWriter writer)
        {
        }

        public void LoadState(SimStateReader reader)
        {
        }
        public void Start(SimContext context)
        {
        }

        public void Step(SimContext context)
        {
        }

        public void WriteState(StateHash hash)
        {
            hash.Add("single-match-v1");
        }
    }

    public sealed class SimClock : IStateHash
    {
        public const int TickRate = 30;
        public long Tick
        {
            get;
            private set;
        }

        internal void Advance()
        {
            if (Tick == long.MaxValue)
                throw new SimBoundaryException("Simulation clock exhausted.");
            Tick++;
        }

        internal void Restore(long tick)
        {
            SimStateReader.Require(tick >= 0, "Invalid simulation clock.");
            Tick = tick;
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(TickRate);
            hash.Add(Tick);
        }
    }

    public sealed partial class SimContext
    {
        private readonly SimEventBuffer events;
        private bool active;
        internal Sim Owner { get; set; }
        public ISpatialQueryModes SpatialQuery { get; internal set; }
        internal bool BufferMovement(EntityId entity, SimVec3 position) => Owner != null && Owner.BufferMovement(entity, position);
        public void SubmitMovement(EntityId entity, SimVec3 position)
        {
            if (!BufferMovement(entity, position))
                throw new InvalidOperationException("Movement output requires the movement advance phase.");
        }
        public SimClock Clock
        {
            get;
        }

        public EntityStore Entities
        {
            get;
        }

        public SimRandom Rng
        {
            get;
        }

        public SimScheduler Scheduler
        {
            get;
        }

        public InputFrame Input
        {
            get;
            private set;
        }

        public FactionRelations Factions
        {
            get;
            internal set;
        }

        internal SimContext(SimClock clock, EntityStore entities, SimRandom random, SimScheduler scheduler, SimEventBuffer events)
        {
            Clock = clock;
            Entities = entities;
            Rng = random;
            Scheduler = scheduler;
            this.events = events;
        }

        internal void Begin(InputFrame input)
        {
            Input = input;
            active = true;
        }

        internal void End()
        {
            active = false;
        }

        internal void RestoreInput(InputFrame input)
        {
            Input = input;
            active = false;
        }

        public void Emit(SimEvent item)
        {
            RequireActive();
            if (Entities.IdentityMode == IdentityMode.StableWorld)
                events.PublishStable(new StableSimEvent(item,default,default));
            else
                events.Publish(item);
        }

        public void Emit(StableSimEvent item)
        {
            RequireActive();
            Entities.ValidateLink(item.Source,false);
            Entities.ValidateLink(item.Target,false);
            events.PublishStable(item);
        }

        internal void RequireActive()
        {
            if (!active)
                throw new InvalidOperationException("This operation requires an active simulation tick.");
        }
    }

    public sealed partial class Sim : IStateHash
    {
        private sealed class Registration
        {
            public readonly string Id;
            public readonly int Order;
            public readonly ISimSystem System;
            public readonly SimSystemPhase Phase;
            public readonly string ManifestId;
            public Registration(string id, ISimSystem system, SimSystemPhase phase)
            {
                Id = id;
                Order = system.Order;
                Phase = phase;
                ManifestId = phase == SimSystemPhase.Requests ? id : id + "/phase/" + (int)phase;
                System = system;
            }
        }

        private sealed class RegistrationComparer : IComparer<Registration>
        {
            public int Compare(Registration left, Registration right) => left.Order.CompareTo(right.Order);
        }

        private readonly List<Registration> systems = new List<Registration>();
        private readonly SimEventBuffer events;
        private readonly IMatchMode mode;
        private readonly string modeId;
        private readonly StateHash hashSink = new StateHash();
        private bool hashing;
        private bool warmed;
        private bool started;
        private bool stepping;
        private bool faulted;
        public SimClock Clock
        {
            get;
        } = new SimClock();
        public EntityStore Entities
        {
            get;
        }
        public SimRandom Rng
        {
            get;
        }

        public SimScheduler Scheduler
        {
            get;
        }
        public SimContext Context
        {
            get;
        }

        public ISimEventReader Events => events;
        public StableSimEvent ReadStableEvent(long tick, int index) => events.ReadStable(tick,index);
        public SimConfig Config { get; }
        public FactionRelations Factions => Context.Factions;
        public bool IsFinished => mode.IsFinished;
        internal Sim(ulong seed, IMatchMode mode = null, string modeId = "single-match-v1", SimEventBuffer eventBuffer = null,
            SimConfig config = null, FactionRelations factions = null, IdentityMode identityMode = IdentityMode.Legacy,
            int identityCapacity = 0, long retiredRetentionTicks = EntityStore.DefaultRetiredRetentionTicks, StableContentManifest stableContent = null,
            RetainedWorldOptions retainedWorld = null, PlacedCatalog placedCatalog = null, InitialPlacedState initialPlaced = null)
        {
            if (string.IsNullOrEmpty(modeId))
                throw new ArgumentException("A mode id is required.", nameof(modeId));
            if (mode is IRetainedGameplayMode && (retainedWorld == null || placedCatalog == null || identityMode != IdentityMode.StableWorld))
                throw new SimBoundaryException(RetainedRequired);
            this.mode = mode ?? new SingleMatchMode();
            this.modeId = modeId;
            if (eventBuffer != null && config != null)
                throw new ArgumentException("Supply either SimConfig or a legacy event buffer, not both.");
            Config = config ?? (eventBuffer == null ? new SimConfig() : new SimConfig(eventBuffer.RetainedTicks, eventBuffer.InitialCapacity, eventBuffer.HardLimit));
            events = eventBuffer ?? new SimEventBuffer(Config.EventRetainedTicks, Config.EventInitialCapacity, Config.EventHardLimit);
            if (retainedWorld != null && identityMode != IdentityMode.StableWorld)
                throw new SimBoundaryException("Retained lifecycle requires StableWorld.");
            Entities = new EntityStore(Config.EntityCapacity, identityMode,
                retainedWorld == null ? identityCapacity : retainedWorld.IdentityCapacity,
                retainedWorld == null ? retiredRetentionTicks : retainedWorld.RetentionTicks,retainedWorld != null);
            if (placedCatalog != null)
                Entities.Placed = new PlacedDestructionState(placedCatalog,initialPlaced);
            Scheduler = new SimScheduler(Config.TimerCapacity, identityMode,Config.TaskSystemCapacity);
            if (identityMode == IdentityMode.StableWorld)
                events.EnableStableEvents();
            Rng = new SimRandom(seed);
            Context = new SimContext(Clock, Entities, Rng, Scheduler, events);
            Context.Owner = this;
            InitializeStableWorld(stableContent);
            // Content tables can be shared; each Sim owns the mutable restoration target.
            Context.Factions = factions == null || factions.Count == 0 ? null : factions.Copy();
        }

        internal void Register(string stableId, ISimSystem system, SimSystemPhase phase = SimSystemPhase.Automatic)
        {
            if (system is IRetainedGameplaySystem)
                RequireGameplayProtocol();
            if (started)
                throw new InvalidOperationException("Systems must be registered before the first tick.");
            if (system == null)
                throw new ArgumentNullException(nameof(system));
            if (string.IsNullOrEmpty(stableId))
                throw new ArgumentException("A system id is required.", nameof(stableId));
            foreach (Registration entry in systems)
                if (entry.Order == system.Order || entry.Id == stableId)
                    throw new InvalidOperationException("Duplicate system order or id: " + stableId);
            if (phase == SimSystemPhase.Automatic)
                phase = system is ISimPhaseSystem staged ? staged.Phase : SimSystemPhase.Requests;
            if (phase < SimSystemPhase.Requests || phase > SimSystemPhase.Queries)
                throw new ArgumentOutOfRangeException(nameof(phase));
            systems.Add(new Registration(stableId, system, phase));
            systems.Sort(new RegistrationComparer());
        }

        public void CompleteWarmup()
        {
            if (warmed)
                return;
            CompleteSpatialBoundary();
            foreach (Registration entry in systems)
                if (entry.System is ISimWarmupSystem warmupSystem)
                    warmupSystem.PrepareWarmup();
            events.CompleteWarmup();
            Entities.CompleteWarmup();
            Scheduler.CompleteWarmup();
            warmed = true;
        }

        public void Step(InputFrame input)
        {
            RequireActivationBoundary();
            if (Entities.IdentityMode == IdentityMode.StableWorld)
                stableInput = new StableInputFrame(input);
            StepCore(input);
        }

        private void StepCore(InputFrame input)
        {
            if (stepping || faulted || IsFinished)
                throw new InvalidOperationException("Simulation cannot step in its current state.");
            CompleteWarmup();
            stepping = true;
            try
            {
                Clock.Advance();
                gameplay?.CaptureInput(Clock.Tick);
                gameplay?.CaptureAbilityInput(input,Clock.Tick);
                Entities.IdentityTick = Clock.Tick;
                Activations?.Advance(Clock.Tick);
                Entities.SynchronizeStableEntities();
                events.BeginTick(Clock.Tick);
                Context.Begin(input);
                gameplay?.Cleanup();
                Scheduler.Advance(Clock.Tick, events);
                gameplay?.BeginResources();
                gameplay?.BeginAbilities();
                placedRestorePhase = true;
                if (!started)
                {
                    started = true;
                    mode.Start(Context);
                }

                mode.Step(Context);
                RunPhase(SimSystemPhase.Requests);
                placedRestorePhase = false;
                Spawns?.Commit();
                Entities.SynchronizeStableEntities();
                if (Entities.IdentityMode == IdentityMode.StableWorld)
                    CommitStableMotion();
                SettlePositionRequests();
                BeginMovement();
                RunPhase(SimSystemPhase.Movement);
                CommitMovement();
                RunPhase(SimSystemPhase.Attachment);
                FollowAttachments();
                PublishSpatialState();
                gameplayQueries = true;
                gameplay?.RunAbilityQ0();
                RunPhase(SimSystemPhase.Queries);
                gameplayQueries = false;
                gameplay?.Cleanup();
                gameplay?.ValidateBindings();
                gameplay?.Settle();
                Entities.ValidateSpatialWrites();
                spatialIndex?.Synchronize();
                Entities.RecycleDestroyed();
                Entities.SynchronizeStableEntities();
                Entities.RecycleRetiredIdentities();
                Scheduler.ValidateDueAtTickEnd();
                RequestDiagnostics?.Flush(events);
                events.EndTick();
            }
            catch
            {
                faulted = true;
                throw;
            }
            finally
            {
                Entities.OpenSpatialWrites();
                Context.End();
                placedRestorePhase = false;
                gameplayQueries = false;
                stepping = false;
            }
        }

        internal ulong StateHash() => ComputeStateHash(false);

        // Protocol selection is explicit so existing peers and stored v1 vectors cannot silently change.
        internal ulong StateHashV2() => ComputeStateHash(true);

        private ulong ComputeStateHash(bool incremental)
        {
            RequireLegacyProtocol();
            // A sink belongs to one Sim; sharing or recursively resetting it would corrupt the byte stream.
            if (hashing)
                throw new InvalidOperationException("State hashing cannot reenter the same simulation.");
            hashing = true;
            try
            {
                hashSink.Reset();
                WriteAuthoritativeState(hashSink, incremental);
                events.WriteState(hashSink);
                return hashSink.Value;
            }
            finally
            {
                hashing = false;
            }
        }

        void IStateHash.WriteState(StateHash hash) => WriteState(hash);

        internal void WriteState(StateHash hash)
        {
            WriteAuthoritativeState(hash);
            events.WriteState(hash);
        }

        internal void WriteStateV2(StateHash hash)
        {
            WriteAuthoritativeStateV2(hash);
            events.WriteState(hash);
        }

        // Output transport is excluded so overflow tests compare the state that drives future ticks.
        internal void WriteAuthoritativeState(StateHash hash) => WriteAuthoritativeState(hash, false);
        internal void WriteAuthoritativeStateV2(StateHash hash) => WriteAuthoritativeState(hash, true);

        private void WriteAuthoritativeState(StateHash hash, bool incremental)
        {
            RequireLegacyProtocol();
            if (stepping)
                throw new InvalidOperationException("Hash only at tick boundaries.");
            hash.Add(incremental ? "warcore-state-v2" : "warcore-state-m0b1-v1");
            Clock.WriteState(hash);
            Context.Input.WriteState(hash);
            hash.Add(started);
            hash.Add(faulted);
            hash.Add(modeId);
            mode.WriteState(hash);
            hash.Add(mode.IsFinished);
            Entities.WriteState(hash);
            Rng.WriteState(hash);
            if (incremental)
                Scheduler.WriteStateV2(hash);
            else
                Scheduler.WriteState(hash);
            hash.Add(systems.Count);
            foreach (Registration entry in systems)
            {
                hash.Add(entry.Id);
                hash.Add(entry.Order);
                entry.System.WriteState(hash);
                if (entry.Phase != SimSystemPhase.Requests)
                {
                    hash.Add("warcore.system-phase-v1");
                    hash.Add((int)entry.Phase);
                }
            }

            // Optional extensions have tagged tails; legacy scenes keep their original hash bytes.
            if (Factions != null && Factions.Count != 0)
            {
                hash.Add("warcore.factions-v1");
                Factions.WriteState(hash);
            }
            WriteStaticRequests(hash);
        }
    }

    // Integer source units preserve fractions across frames without floating point drift or dropped ticks.
    public sealed class TickAccumulator
    {
        private readonly long unitsPerSecond;
        private long remainder;
        public TickAccumulator(long unitsPerSecond)
        {
            if (unitsPerSecond <= 0)
                throw new ArgumentOutOfRangeException(nameof(unitsPerSecond));
            this.unitsPerSecond = unitsPerSecond;
        }

        public long AddElapsed(long units)
        {
            if (units < 0)
                throw new ArgumentOutOfRangeException(nameof(units));
            long total = SimInteger.Add(remainder, SimInteger.Multiply(units, SimClock.TickRate));
            long ticks = SimInteger.Divide(total, unitsPerSecond);
            remainder = SimInteger.Remainder(total, unitsPerSecond);
            return ticks;
        }
    }
}
