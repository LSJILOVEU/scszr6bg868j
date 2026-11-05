using System;

namespace WarCore.Presentation
{
    // Immutable snapshots; consumers refresh their references after advancing the session.
    public sealed class FrameView
    {
        private readonly ViewEntity[] entities;
        private readonly ViewFire[] fires;
        private readonly ViewPoint[] points;
        public ReadOnlySpan<ViewPoint> Points => points;
        public ViewMatch Match { get; }
        public ReadOnlySpan<ViewFire> Fires => fires;

        public long Tick { get; }
        public ReadOnlySpan<ViewEntity> Entities => entities;

        public FrameView(long tick, ViewEntity[] entities, ReadOnlySpan<ViewFire> fires = default, ReadOnlySpan<ViewPoint> points = default, ViewMatch match = default)
        {
            if (tick < 0)
                throw new ArgumentOutOfRangeException(nameof(tick));
            if (entities == null)
                throw new ArgumentNullException(nameof(entities));
            Tick = tick;
            this.fires = fires.ToArray();
            this.points = points.ToArray(); Match = match;
            this.entities = (ViewEntity[])entities.Clone();
            Array.Sort(this.entities, (a, b) => a.Key.CompareTo(b.Key));
            for (int i = 1; i < this.entities.Length; i++)
                if (this.entities[i - 1].Key == this.entities[i].Key)
                    throw new ArgumentException("View keys must be unique.", nameof(entities));
        }

        public bool TryGet(ulong key, out ViewEntity entity)
        {
            int low = 0;
            int high = entities.Length - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                int order = entities[middle].Key.CompareTo(key);
                if (order == 0)
                {
                    entity = entities[middle];
                    return true;
                }
                if (order < 0)
                    low = middle + 1;
                else
                    high = middle - 1;
            }
            entity = default;
            return false;
        }

        public static FrameView Interpolate(FrameView previous, FrameView current, float alpha)
        {
            if (previous == null || current == null)
                throw new ArgumentNullException(previous == null ? nameof(previous) : nameof(current));
            if (!(alpha >= 0 && alpha < 1))
                throw new ArgumentOutOfRangeException(nameof(alpha));
            var result = new ViewEntity[current.entities.Length];
            for (int i = 0; i < result.Length; i++)
            {
                ViewEntity next = current.entities[i];
                result[i] = next;
                if (!previous.TryGet(next.Key, out ViewEntity old))
                    continue;
                float dx = next.X - old.X;
                float dy = next.Y - old.Y;
                float dz = next.Z - old.Z;
                if (dx * dx + dy * dy + dz * dz > 4)
                    continue;
                result[i] = new ViewEntity(next.Key, next.Archetype,
                    old.X + dx * alpha, old.Y + dy * alpha, old.Z + dz * alpha, next.Squad, next.Team, next.Alive);
            }
            return new FrameView(current.Tick, result, current.fires, current.points, current.Match);
        }
    }
}
