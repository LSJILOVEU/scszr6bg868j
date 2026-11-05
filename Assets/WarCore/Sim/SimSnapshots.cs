using System;

namespace WarCore.Sim
{
    public interface ISimRestoreValidator
    {
        void ValidateRestoredState(SimContext context);
    }

    public sealed partial class Sim
    {
        public const int SnapshotVersion = 4;
        private const int SnapshotMagic = 0x57435331;

        private void RequireSnapshotBoundary(bool saving)
        {
            if (stepping || events.IsActive || (saving && faulted))
                throw new SimSnapshotException("Snapshots require a completed tick boundary.");
            if (saving && (events.CurrentTick != Clock.Tick || Scheduler.AdvancedThrough != Clock.Tick))
                throw new SimSnapshotException("Clock, scheduler and event window must share the tick boundary.");
        }

        private static ISimStateSerializer Serializer(object value, string id)
        {
            if (!(value is ISimStateSerializer serializer) || string.IsNullOrEmpty(serializer.StateTypeId) || serializer.StateVersion <= 0)
                throw new SimSnapshotException("Missing ISimStateSerializer or invalid state contract: " + id);
            return serializer;
        }

        private static void SaveContract(SimStateWriter writer, ISimStateSerializer serializer)
        {
            writer.String(serializer.StateTypeId);
            writer.Int32(serializer.StateVersion);
        }

        private static void CheckContract(SimStateReader reader, ISimStateSerializer serializer, string id)
        {
            string type = reader.String();
            int version = reader.Int32();
            SimStateReader.Require(type == serializer.StateTypeId && version == serializer.StateVersion,
                "State registration manifest mismatch: " + id + "; snapshot=" + type + " v" + version
                + "; registered=" + serializer.StateTypeId + " v" + serializer.StateVersion);
        }

        private void SaveManifest(SimStateWriter writer)
        {
            writer.Int32(Config.EventRetainedTicks);
            writer.Int32(Config.EventInitialCapacity);
            writer.Int32(Config.EventHardLimit);
            writer.Int32(Config.EntityCapacity);
            writer.Int32(Config.TimerCapacity);
            Entities.SaveManifest(writer);
            writer.String(modeId);
            SaveContract(writer, Serializer(mode, modeId));
            writer.Int32(systems.Count);
            foreach (Registration entry in systems)
            {
                writer.String(entry.ManifestId);
                writer.Int32(entry.Order);
                SaveContract(writer, Serializer(entry.System, entry.Id));
            }
        }

        private void CheckManifest(SimStateReader reader)
        {
            SimStateReader.Require(reader.Int32() == Config.EventRetainedTicks && reader.Int32() == Config.EventInitialCapacity
                && reader.Int32() == Config.EventHardLimit && reader.Int32() == Config.EntityCapacity
                && reader.Int32() == Config.TimerCapacity, "SimConfig snapshot mismatch.");
            Entities.CheckManifest(reader);
            SimStateReader.Require(reader.String() == modeId, "Mode registration manifest mismatch.");
            CheckContract(reader, Serializer(mode, modeId), modeId);
            SimStateReader.Require(reader.Int32() == systems.Count, "System registration manifest count mismatch.");
            foreach (Registration entry in systems)
            {
                string id = reader.String();
                int order = reader.Int32();
                SimStateReader.Require(id == entry.ManifestId && order == entry.Order,
                    "System registration manifest mismatch: snapshot=" + id + " order=" + order
                    + "; registered=" + entry.Id + " order=" + entry.Order);
                CheckContract(reader, Serializer(entry.System, entry.Id), entry.Id);
            }
        }

        private static void SaveExtension(SimStateWriter writer, ISimStateSerializer serializer)
        {
            var section = new SimStateWriter();
            serializer.SaveState(section);
            writer.Bytes(section.ToArray());
        }

        private static void LoadExtension(SimStateReader reader, ISimStateSerializer serializer)
        {
            var section = new SimStateReader(reader.Bytes());
            serializer.LoadState(section);
            section.End();
        }

        internal byte[] SaveSnapshot()
        {
            RequireLegacyProtocol();
            RequireSnapshotBoundary(true);
            CompleteSpatialBoundary();
            var writer = new SimStateWriter();
            writer.Int32(SnapshotMagic);
            writer.Int32(SnapshotVersion);
            writer.UInt64(0);
            SaveManifest(writer);
            writer.Bool(warmed);
            writer.Bool(started);
            writer.Int64(Clock.Tick);
            SnapshotValues.Input(writer, Context.Input);
            Entities.SaveEntities(writer);
            Rng.SaveRandom(writer);
            Scheduler.SaveScheduler(writer);
            events.SaveEvents(writer);
            writer.Bool(Factions != null && Factions.Count != 0);
            if (Factions != null && Factions.Count != 0)
                Factions.SaveRelations(writer);
            SaveExtension(writer, Serializer(mode, modeId));
            foreach (Registration entry in systems)
                SaveExtension(writer, Serializer(entry.System, entry.Id));
            SaveStaticRequests(writer);
            return writer.ToArray();
        }

        internal void RestoreSnapshot(byte[] snapshot)
        {
            RequireLegacyProtocol();
            RequireSnapshotBoundary(false);
            var reader = new SimStateReader(snapshot);
            SimStateReader.Require(reader.Int32() == SnapshotMagic, "Invalid WarCore snapshot magic.");
            int version = reader.Int32();
            SimStateReader.Require(version == SnapshotVersion,
                "Snapshot version mismatch: expected " + SnapshotVersion + ", received " + version + ".");
            SimStateReader.Require(reader.UInt64() == 0, "Unsupported snapshot content manifest hash.");
            // Compatibility failures precede all state writes. A malformed payload faults the Sim
            // so a partially decoded state cannot silently continue; a valid snapshot can recover it.
            CheckManifest(reader);
            faulted = true;
            bool savedWarmed = reader.Bool();
            bool savedStarted = reader.Bool();
            long tick = reader.Int64();
            SimStateReader.Require(tick >= 0 && savedStarted == (tick > 0) && (!savedStarted || savedWarmed), "Invalid snapshot lifecycle.");
            InputFrame input = SnapshotValues.Input(reader);
            Entities.LoadEntities(reader);
            Rng.LoadRandom(reader);
            Scheduler.LoadScheduler(reader, tick);
            events.LoadEvents(reader, tick);
            if (reader.Bool())
                Context.Factions = FactionRelations.LoadRelations(reader, Context.Factions);
            else
                Context.Factions?.Clear();
            Clock.Restore(tick);
            Context.RestoreInput(input);
            LoadExtension(reader, Serializer(mode, modeId));
            foreach (Registration entry in systems)
                LoadExtension(reader, Serializer(entry.System, entry.Id));
            LoadStaticRequests(reader);
            reader.End();
            foreach (Registration entry in systems)
                if (entry.System is ISimRestoreValidator validator)
                    validator.ValidateRestoredState(Context);
            RebuildSpatialState();
            if (savedWarmed)
                foreach (Registration entry in systems)
                    if (entry.System is ISimWarmupSystem warmupSystem)
                        warmupSystem.PrepareWarmup();
            warmed = savedWarmed;
            started = savedStarted;
            faulted = false;
        }
    }
}
