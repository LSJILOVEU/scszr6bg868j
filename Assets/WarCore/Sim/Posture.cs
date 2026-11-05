using System;

namespace WarCore.Sim
{
    public readonly struct PostureDef : IStateHash
    {
        public int Id { get; }
        public SimFix Height { get; }
        public SimFix DetectionDistanceFactor { get; }
        public SimFix HitModifier { get; }
        public SimFix SpeedFactor { get; }

        // Height is absolute metres, not a multiplier of the baseline collider.
        public PostureDef(int id, SimFix height, SimFix detectionDistanceFactor, SimFix hitModifier, SimFix speedFactor)
        {
            if (id <= 0 || height.Raw < 0 || height.Raw > 32768 * SimFix.Scale
                || detectionDistanceFactor.Raw < 0 || speedFactor.Raw < 0)
                throw new ArgumentOutOfRangeException(nameof(id), "Invalid posture definition.");
            Id = id;
            Height = height;
            DetectionDistanceFactor = detectionDistanceFactor;
            HitModifier = hitModifier;
            SpeedFactor = speedFactor;
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(Id);
            Height.WriteState(hash);
            DetectionDistanceFactor.WriteState(hash);
            HitModifier.WriteState(hash);
            SpeedFactor.WriteState(hash);
        }

        internal void Save(SimStateWriter writer)
        {
            writer.Int32(Id);
            writer.Int64(Height.Raw);
            writer.Int64(DetectionDistanceFactor.Raw);
            writer.Int64(HitModifier.Raw);
            writer.Int64(SpeedFactor.Raw);
        }

        internal static PostureDef Read(SimStateReader reader) => new PostureDef(reader.Int32(),
            SimFix.FromRaw(reader.Int64()), SimFix.FromRaw(reader.Int64()),
            SimFix.FromRaw(reader.Int64()), SimFix.FromRaw(reader.Int64()));
    }

    public readonly struct Posture : IStateHash
    {
        // Id zero means the unmodified baseline, including during a first transition.
        public int CurrentId { get; }
        public int TargetId { get; }
        public long DueTick { get; }
        public long TaskId { get; }
        public bool UsesStableTask { get; }
        public TaskKey StableTask { get; }
        public bool IsTransitioning => TaskId != 0 || UsesStableTask;

        public Posture(int currentId, int targetId = 0, long dueTick = 0, long taskId = 0) : this()
        {
            if (currentId < 0 || targetId < 0 || dueTick < 0 || taskId < 0
                || (taskId == 0 && (targetId != 0 || dueTick != 0))
                || (taskId != 0 && (dueTick == 0 || currentId == targetId)))
                throw new ArgumentOutOfRangeException(nameof(taskId), "Invalid posture transition state.");
            CurrentId = currentId;
            TargetId = targetId;
            DueTick = dueTick;
            TaskId = taskId;
        }

        public Posture(int currentId, int targetId, TaskKey task) : this()
        {
            if (currentId < 0 || targetId < 0 || currentId == targetId || task.DueTick <= 0)
                throw new SimBoundaryException("Invalid stable posture task.");
            CurrentId = currentId;
            TargetId = targetId;
            DueTick = task.DueTick;
            StableTask = task;
            UsesStableTask = true;
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(CurrentId);
            hash.Add(TargetId);
            hash.Add(DueTick);
            hash.Add(TaskId);
        }
    }

    public sealed class PostureSerializer : IStableComponentSerializer<Posture>
    {
        public int TypeId => 6;
        public int Version => 1;
        public void Write(SimStateWriter writer, in Posture value)
        {
            if (writer.IsStable)
            {
                if (value.TaskId != 0)
                    throw new SimBoundaryException("Stable posture cannot encode a legacy task id.");
                writer.Int32(value.CurrentId);
                writer.Bool(value.UsesStableTask);
                if (value.UsesStableTask)
                {
                    writer.Int32(value.TargetId);
                    writer.TaskKeyValue(value.StableTask);
                }
                return;
            }
            writer.Int32(value.CurrentId);
            writer.Int32(value.TargetId);
            writer.Int64(value.DueTick);
            writer.Int64(value.TaskId);
        }

        public Posture Read(SimStateReader reader)
        {
            if (!reader.IsStable)
                return new Posture(reader.Int32(),reader.Int32(),reader.Int64(),reader.Int64());
            int current = reader.Int32();
            return reader.Bool() ? new Posture(current,reader.Int32(),reader.ReadTaskKey()) : new Posture(current);
        }
    }

    public sealed class PostureCatalog : IStateHash, IStableStateSerializer
    {
        private PostureDef[] definitions;
        public string StateTypeId => "warcore.posture-data";
        public int StateVersion => 1;
        public PostureCatalog(params PostureDef[] definitions)
        {
            if (definitions == null)
                throw new ArgumentNullException(nameof(definitions));
            this.definitions = (PostureDef[])definitions.Clone();
            Validate(this.definitions);
        }

        private static void Validate(PostureDef[] values)
        {
            int previous = 0;
            foreach (PostureDef value in values)
            {
                if (value.Id <= previous)
                    throw new ArgumentException("Posture ids must be positive, unique and sorted.");
                previous = value.Id;
            }
        }

        public PostureDef Get(int id)
        {
            int low = 0;
            int high = definitions.Length - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                if (definitions[middle].Id == id)
                    return definitions[middle];
                if (definitions[middle].Id < id)
                    low = middle + 1;
                else
                    high = middle - 1;
            }

            throw new InvalidOperationException("Missing posture definition: " + id);
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(definitions.Length);
            foreach (PostureDef definition in definitions)
                definition.WriteState(hash);
        }

        public void SaveState(SimStateWriter writer)
        {
            writer.Int32(definitions.Length);
            foreach (PostureDef definition in definitions)
                definition.Save(writer);
        }

        public void LoadState(SimStateReader reader)
        {
            var restored = new PostureDef[reader.Count(reader.Remaining / 36)];
            for (int i = 0; i < restored.Length; i++)
                restored[i] = PostureDef.Read(reader);
            Validate(restored);
            definitions = restored;
        }
    }
}
