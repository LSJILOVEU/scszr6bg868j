using System;

namespace WarCore.Sim
{
    public class SimBoundaryException : InvalidOperationException
    {
        public SimBoundaryException(string message) : base(message)
        {
        }
    }

    public sealed class SimConfig
    {
        public const int DefaultEventHardLimit = 16384;
        public int EventRetainedTicks { get; }
        public int EventInitialCapacity { get; }
        public int EventHardLimit { get; }
        public int EntityCapacity { get; }
        public int TimerCapacity { get; }
        public int SpatialMembershipCapacity { get; }
        public int SpatialCellCapacity { get; }
        public int SpatialMaxCellsPerBody { get; }
        public int StaticSpatialMembershipCapacity { get; }
        public int StaticSpatialCellCapacity { get; }
        public int SpawnRequestCapacity { get; }
        public int WorldCursorCapacity { get; }
        public int MotionRequestCapacity { get; }
        public int ActivationCapacity { get; }
        public int TaskSystemCapacity { get; }
        public int RequestDiagnosticCapacity { get; }

        public SimConfig(int eventRetainedTicks = 8, int eventInitialCapacity = 4096,
            int eventHardLimit = DefaultEventHardLimit, int entityCapacity = 4, int timerCapacity = 1024,
            int spatialMembershipCapacity = 32768, int spatialCellCapacity = 0, int spatialMaxCellsPerBody = 64,
            int staticSpatialMembershipCapacity = 0, int staticSpatialCellCapacity = 0,
            int spawnRequestCapacity = 1024, int worldCursorCapacity = 256, int motionRequestCapacity = 1024,
            int activationCapacity = 1024, int taskSystemCapacity = 256, int requestDiagnosticCapacity = 4096)
        {
            if (eventRetainedTicks < 1 || eventInitialCapacity < 1 || eventHardLimit < eventInitialCapacity
                || eventHardLimit == int.MaxValue || entityCapacity < 1 || timerCapacity < 1)
                throw new ArgumentOutOfRangeException(nameof(eventHardLimit), "Capacities must be positive; initial events must fit the hard limit and its summary slot.");
            EventRetainedTicks = eventRetainedTicks;
            EventInitialCapacity = eventInitialCapacity;
            EventHardLimit = eventHardLimit;
            EntityCapacity = entityCapacity;
            TimerCapacity = timerCapacity;
            ValidateRequestCapacity(spawnRequestCapacity);
            ValidateRequestCapacity(worldCursorCapacity);
            ValidateRequestCapacity(motionRequestCapacity);
            ValidateRequestCapacity(activationCapacity);
            ValidateRequestCapacity(taskSystemCapacity);
            ValidateRequestCapacity(requestDiagnosticCapacity);
            SpawnRequestCapacity = spawnRequestCapacity;
            WorldCursorCapacity = worldCursorCapacity;
            MotionRequestCapacity = motionRequestCapacity;
            ActivationCapacity = activationCapacity;
            TaskSystemCapacity = taskSystemCapacity;
            RequestDiagnosticCapacity = requestDiagnosticCapacity;
            if (spatialMembershipCapacity < 1 || spatialMembershipCapacity > int.MaxValue / 5 || spatialMaxCellsPerBody < 1)
                throw new SimBoundaryException("Spatial membership capacities must be positive and fit five levels.");
            SpatialMembershipCapacity = spatialMembershipCapacity;
            SpatialCellCapacity = spatialCellCapacity == 0 ? 0 : GridSpatialIndex.ResolveCellCapacity(entityCapacity, spatialCellCapacity);
            SpatialMaxCellsPerBody = spatialMaxCellsPerBody;
            if (staticSpatialMembershipCapacity < 0 || staticSpatialMembershipCapacity > int.MaxValue / 5)
                throw new SimBoundaryException("Static spatial membership capacity must fit five levels.");
            StaticSpatialMembershipCapacity = staticSpatialMembershipCapacity == 0 ? (spatialMembershipCapacity < 16384 ? spatialMembershipCapacity : 16384) : staticSpatialMembershipCapacity;
            StaticSpatialCellCapacity = staticSpatialCellCapacity == 0 ? (SpatialCellCapacity < 16384 ? SpatialCellCapacity : 16384)
                : GridSpatialIndex.ResolveCellCapacity(entityCapacity, staticSpatialCellCapacity);
            EstimateEventPayloadBytes(true);
        }
        private static void ValidateRequestCapacity(int capacity)
        {
            if (capacity < 1 || capacity > (1 << 27))
                throw new SimBoundaryException("Request capacities must be within 1..134217728.");
        }

        // Per Sim: retainedTicks * (capacity + 1) * 128 event payload bytes. The +1
        // reserves the drop summary. Capacity is initial before warmup, hard limit after.
        // Add retainedTicks * (8 + 4) for tick/count arrays, array headers/references,
        // entityCapacity * (1 + 8 + 4) for alive/generation/free slots, component arrays,
        // and timerCapacity * (2 * 136 + 4) plus the scheduler id dictionary.
        // Runtime object headers/dictionary overhead are excluded, so this is not RSS.
        public long EstimateEventPayloadBytes(bool warmed)
        {
            long slots = (long)EventRetainedTicks * ((warmed ? EventHardLimit : EventInitialCapacity) + 1L);
            if (slots > long.MaxValue / 128)
                throw new SimBoundaryException("Event payload size exceeds the supported byte count.");
            return slots * 128;
        }
    }

    internal static class SimChecked
    {
        internal static long Add(long left, long right)
        {
            if ((right > 0 && left > long.MaxValue - right) || (right < 0 && left < long.MinValue - right))
                throw new SimBoundaryException("Signed addition exceeds the simulation range.");
            return left + right;
        }
    }
}
