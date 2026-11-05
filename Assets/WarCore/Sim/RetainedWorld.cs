namespace WarCore.Sim
{
    public sealed class RetainedWorldOptions
    {
        public const long DefaultRetentionTicks = 32;
        public long RetentionTicks { get; }
        public int IdentityCapacity { get; }
        public RetainedWorldOptions(long retentionTicks = DefaultRetentionTicks, int identityCapacity = 0)
        {
            if (retentionTicks < 0 || retentionTicks == long.MaxValue || identityCapacity < 0 || identityCapacity > (1 << 27))
                throw new SimBoundaryException("Retained worlds require a finite nonnegative retention and a bounded identity capacity."+EntityStore.IdentityCapacityPlanningRule);
            RetentionTicks = retentionTicks;
            IdentityCapacity = identityCapacity;
        }
    }

    public sealed partial class Sim
    {
        public const int RetainedHashVersion = 4;
        public const int RetainedSnapshotVersion = 6;
        internal static Sim CreateRetainedWorld(ulong seed, SimConfig config = null, RetainedWorldOptions options = null,
            StableContentManifest content = null)
            => new Sim(seed,config:config,identityMode:IdentityMode.StableWorld,stableContent:content,
                retainedWorld:options ?? new RetainedWorldOptions());
        private void RequireRetainedProtocol(bool expected)
        {
            Entities.RequireStableWorld();
            if (Entities.UsesRetainedProtocol != expected)
                throw new SimBoundaryException("Select the matching stable v3/v5 or retained v4/v6 entry point.");
        }
        public ulong StateHashRetained()
        {
            RequireRetainedProtocol(true);
            return ComputeStableHash();
        }
        public byte[] SaveRetainedSnapshot()
        {
            RequireRetainedProtocol(true);
            return SaveStableSnapshotCore();
        }
        public void RestoreRetainedSnapshot(byte[] snapshot, bool reverseAllocation = false, IStableRestoreObserver observer = null)
        {
            RequireRetainedProtocol(true);
            RestoreStableSnapshotCore(snapshot,reverseAllocation,observer);
        }
        private void SaveRetainedConfiguration(SimStateWriter writer)
        {
            writer.Int32(Entities.IdentityCapacity);
            writer.Int32(Config.SpawnRequestCapacity);
            writer.Int32(Config.WorldCursorCapacity);
            writer.Int32(Config.MotionRequestCapacity);
            writer.Int32(Config.ActivationCapacity);
            writer.Int32(Config.TaskSystemCapacity);
            writer.Int32(Config.RequestDiagnosticCapacity);
        }
        private void CheckRetainedConfiguration(SimStateReader reader)
        {
            SimStateReader.Require(reader.Int32() == Entities.IdentityCapacity && reader.Int32() == Config.SpawnRequestCapacity
                && reader.Int32() == Config.WorldCursorCapacity && reader.Int32() == Config.MotionRequestCapacity
                && reader.Int32() == Config.ActivationCapacity && reader.Int32() == Config.TaskSystemCapacity
                && reader.Int32() == Config.RequestDiagnosticCapacity,"Retained world capacity configuration mismatch.");
        }
    }
}
