using System;

namespace WarCore.Sim
{
    public sealed partial class Sim
    {
        public const int StableSnapshotVersion = 6;
        public const int StableHashVersion = 3;
        public StableSpawnService Spawns { get; private set; }
        public StableContentManifest StableContent { get; private set; }
        private StateHash stableHashSink;
        private SimStateWriter stableWriter;
        private void InitializeStableWorld(StableContentManifest content)
        {
            if (Entities.IdentityMode != IdentityMode.StableWorld)
                return;
            Scheduler.StableEntities = Entities;
            RequestDiagnostics = new RequestDiagnostics(Config.RequestDiagnosticCapacity,Entities);
            Entities.RegisterRetirementObserver(this);
            stableMotion = new StableMotionCommand[Config.MotionRequestCapacity];
            motionKeys = new BoundedLookup<StableMotionCommand>(Config.MotionRequestCapacity);
            Spawns = new StableSpawnService(Entities,Config.SpawnRequestCapacity,Config.WorldCursorCapacity);
            StableContent = content ?? new StableContentManifest(0);
            Activations = new StableActivationTimeline(Config.ActivationCapacity,StableContent.Digest,Entities);
            stableHashSink = new StateHash();
            stableWriter = new SimStateWriter(Entities,hash:stableHashSink);
        }
        private void RequireLegacyProtocol()
        {
            if (Entities.IdentityMode != IdentityMode.Legacy)
                throw new SimBoundaryException("StableWorld requires explicit v3 hash and v5 snapshot entry points.");
        }

        private void RequireStableBoundary()
        {
            Entities.RequireStableWorld();
            RequireSnapshotBoundary(true);
        }
        private void WriteStableConfiguration(SimStateWriter writer)
        {
            writer.UInt64(StableEntityKey.ProtocolVersion);
            writer.Int32(gameplay != null ? GameplayHashVersion : Placed != null ? PlacedHashVersion : Entities.UsesRetainedProtocol ? RetainedHashVersion : StableHashVersion);
            StableContent.Write(writer);
            writer.Int64(Entities.RetiredRetentionTicks);
            SaveManifest(writer);
            if (Entities.UsesRetainedProtocol)
                SaveRetainedConfiguration(writer);
        }
        private void CheckStableConfiguration(SimStateReader reader)
        {
            SimStateReader.Require(reader.UInt64() == StableEntityKey.ProtocolVersion && reader.Int32() == (gameplay != null ? GameplayHashVersion : Placed != null ? PlacedHashVersion : Entities.UsesRetainedProtocol ? RetainedHashVersion : StableHashVersion)
                && reader.UInt64() == StableContent.WorldId && reader.UInt64() == StableContent.Digest
                && reader.Int64() == Entities.RetiredRetentionTicks,"Stable protocol, world or content manifest mismatch.");
            CheckManifest(reader);
            if (Entities.UsesRetainedProtocol)
                CheckRetainedConfiguration(reader);
        }
        internal ulong StateHashStable()
        {
            RequireRetainedProtocol(false);
            return ComputeStableHash();
        }
        private ulong ComputeStableHash()
        {
            RequireStableBoundary();
            if (hashing)
                throw new SimBoundaryException("Stable hashing cannot reenter the simulation.");
            hashing = true;
            try
            {
                stableWriter.ResetStable();
                stableWriter.String(gameplay != null ? "warcore-state-retained-v6" : Placed != null ? "warcore-state-retained-v5" : Entities.UsesRetainedProtocol ? "warcore-state-retained-v4" : "warcore-state-stable-v3");
                WriteStableConfiguration(stableWriter);
                stableWriter.Int64(Clock.Tick);
                stableInput.Save(stableWriter);
                stableWriter.Bool(started);
                stableWriter.Bool(faulted);
                stableWriter.String(modeId);
                stableWriter.Section(Serializer(mode,modeId));
                stableWriter.Bool(mode.IsFinished);
                Entities.WriteEntitySet(stableWriter);
                Spawns.Save(stableWriter);
                Activations.Save(stableWriter);
                Rng.SaveRandom(stableWriter);
                Scheduler.WriteStableScheduler(stableWriter);
                WriteStableSystems(stableWriter);
                stableWriter.Bool(Factions != null && Factions.Count != 0);
                if (Factions != null && Factions.Count != 0)
                    Factions.SaveRelations(stableWriter);
                SaveStaticRequests(stableWriter);
                SaveStableMotion(stableWriter);
                events.SaveStableEvents(stableWriter);
                if (Entities.UsesRetainedProtocol)
                    RequestDiagnostics.Save(stableWriter);
                Placed?.WriteHash(stableWriter);
                gameplay?.WriteHash(stableWriter);
                return StableEntityKey.Mix(stableHashSink.Value);
            }
            finally
            {
                hashing = false;
            }
        }
        private void WriteStableSystems(SimStateWriter writer)
        {
            writer.Int32(systems.Count);
            foreach (Registration entry in systems)
            {
                writer.String(entry.Id);
                writer.Int32(entry.Order);
                writer.Int32((int)entry.Phase);
                ISimStateSerializer serializer = Serializer(entry.System,entry.Id);
                SaveContract(writer,serializer);
                writer.Section(serializer);
            }
        }
        private void ReadStableSystems(SimStateReader reader)
        {
            SimStateReader.Require(reader.Int32() == systems.Count,"Stable system count mismatch.");
            foreach (Registration entry in systems)
            {
                SimStateReader.Require(reader.String() == entry.Id && reader.Int32() == entry.Order
                    && reader.Int32() == (int)entry.Phase,"Stable system order mismatch.");
                ISimStateSerializer serializer = Serializer(entry.System,entry.Id);
                CheckContract(reader,serializer,entry.Id);
                ReadStableSection(reader,serializer);
            }
        }
        private static void ReadStableSection(SimStateReader reader, ISimStateSerializer serializer)
        {
            SimStateReader section = reader.Section();
            serializer.LoadState(section);
            section.End();
        }
        internal byte[] SaveStableSnapshot()
        {
            RequireRetainedProtocol(false);
            return SaveStableSnapshotCore();
        }
        private byte[] SaveStableSnapshotCore()
        {
            RequireStableBoundary();
            CompleteSpatialBoundary();
            var writer = new SimStateWriter(Entities,materialize:true);
            writer.Int32(SnapshotMagic);
            writer.Int32(gameplay != null ? GameplaySnapshotVersion : Placed != null ? PlacedSnapshotVersion : Entities.UsesRetainedProtocol ? RetainedSnapshotVersion : StableSnapshotVersion);
            WriteStableConfiguration(writer);
            writer.Bool(warmed);
            writer.Bool(started);
            writer.Int64(Clock.Tick);
            Entities.SaveStableEntities(writer);
            Spawns.Save(writer);
            Activations.Save(writer);
            stableInput.Save(writer);
            Rng.SaveRandom(writer);
            Scheduler.SaveStableScheduler(writer);
            events.SaveStableEvents(writer);
            writer.Bool(Factions != null && Factions.Count != 0);
            if (Factions != null && Factions.Count != 0)
                Factions.SaveRelations(writer);
            writer.Section(Serializer(mode,modeId));
            WriteStableSystems(writer);
            SaveStaticRequests(writer);
            SaveStableMotion(writer);
            if (Entities.UsesRetainedProtocol)
                RequestDiagnostics.Save(writer);
            Placed?.Save(writer);
            gameplay?.Save(writer);
            return writer.ToArray();
        }
        internal void RestoreStableSnapshot(byte[] snapshot, bool reverseAllocation = false, IStableRestoreObserver observer = null)
        {
            RequireRetainedProtocol(false);
            RestoreStableSnapshotCore(snapshot,reverseAllocation,observer);
        }
        private void RestoreStableSnapshotCore(byte[] snapshot, bool reverseAllocation, IStableRestoreObserver observer)
        {
            Entities.RequireStableWorld();
            RequireSnapshotBoundary(false);
            observer?.Reached(StableRestorePhase.Begin);
            var reader = new SimStateReader(snapshot,Entities);
            SimStateReader.Require(reader.Int32() == SnapshotMagic && reader.Int32() == (gameplay != null ? GameplaySnapshotVersion : Placed != null ? PlacedSnapshotVersion : Entities.UsesRetainedProtocol ? RetainedSnapshotVersion : StableSnapshotVersion),Placed != null ? "M0C_SNAPSHOT_VERSION: Expected matching retained snapshot version. No automatic migration." : "Stable snapshot version mismatch.");
            CheckStableConfiguration(reader);
            faulted = true;
            bool savedWarmed = reader.Bool();
            bool savedStarted = reader.Bool();
            long tick = reader.Int64();
            SimStateReader.Require(tick >= 0 && savedStarted == (tick > 0) && (!savedStarted || savedWarmed),"Invalid stable snapshot lifecycle.");
            Entities.LoadStableEntities(reader,reverseAllocation,observer);
            Spawns.Load(reader);
            Activations.Load(reader,tick);
            stableInput = StableInputFrame.Read(reader);
            Context.RestoreInput(stableInput.Bind(Entities));
            Rng.LoadRandom(reader);
            Scheduler.LoadStableScheduler(reader,tick);
            events.CompleteWarmup();
            events.LoadStableEvents(reader,tick);
            if (reader.Bool())
                Context.Factions = FactionRelations.LoadRelations(reader,Context.Factions);
            else
                Context.Factions?.Clear();
            Clock.Restore(tick);
            Entities.IdentityTick = tick;
            ReadStableSection(reader,Serializer(mode,modeId));
            ReadStableSystems(reader);
            LoadStaticRequests(reader);
            LoadStableMotion(reader);
            if (Entities.UsesRetainedProtocol)
                RequestDiagnostics.Load(reader);
            else
                RequestDiagnostics.Reset();
            Placed?.Load(reader);
            gameplay?.Load(reader,tick);
            Entities.ValidatePlacedRestore();
            reader.End();
            foreach (Registration entry in systems)
                if (entry.System is ISimRestoreValidator validator)
                    validator.ValidateRestoredState(Context);
            observer?.Reached(StableRestorePhase.ReferencesBound);
            RebuildSpatialState();
            observer?.Reached(StableRestorePhase.SpatialRebuilt);
            warmed = false;
            started = savedStarted;
            faulted = false;
            if (savedWarmed)
                CompleteWarmup();
            observer?.Reached(StableRestorePhase.Complete);
        }
    }
}
