using System.Collections.Generic;

namespace WarCore.Presentation
{
    public enum ShotKind { Tick, FirstFire, FireAfter3s, FirstFlip, End }

    public readonly struct ShotAnchor
    {
        public ShotKind Kind { get; }
        public long Tick { get; }
        // Where the event happened (a shot's midpoint, a flipped point's centre), so the picture can show it. Else follow the soldier.
        public bool HasFocus { get; }
        public float FocusX { get; }
        public float FocusZ { get; }
        public ShotAnchor(ShotKind kind, long tick) { Kind = kind; Tick = tick; HasFocus = false; FocusX = 0; FocusZ = 0; }
        public ShotAnchor(ShotKind kind, long tick, float focusX, float focusZ) { Kind = kind; Tick = tick; HasFocus = true; FocusX = focusX; FocusZ = focusZ; }

        public string FileName(string prefix)
        {
            switch (Kind)
            {
                case ShotKind.FirstFire: return $"{prefix}-first-fire-t{Tick:0000}.png";
                case ShotKind.FireAfter3s: return $"{prefix}-fire3s-t{Tick:0000}.png";
                case ShotKind.FirstFlip: return $"{prefix}-first-flip-t{Tick:0000}.png";
                case ShotKind.End: return $"{prefix}-end-t{Tick:0000}.png";
                default: return $"{prefix}-t{Tick:0000}.png";
            }
        }
    }

    // When to take a picture: fixed ticks plus event anchors. Call Observe once per tick with that tick's frame.
    // A flip is any change of a point's owner, neutral to a team included. An anchor the match never reaches never fires.
    public sealed class ShotPlan
    {
        public static readonly long[] FixedTicks = { 0, 150, 300, 600 };
        public const long AfterFireTicks = 3 * SimSession.TickRate;

        private FrameView previous;
        private long firstFireTick = -1, lastTick = -1;
        private bool flipped, ended;

        public void Observe(FrameView frame, List<ShotAnchor> anchors)
        {
            anchors.Clear();
            long tick = frame.Tick;
            if (tick <= lastTick) // each tick counts once, however often it is offered
                return;
            lastTick = tick;
            if (System.Array.IndexOf(FixedTicks, tick) >= 0)
                anchors.Add(new ShotAnchor(ShotKind.Tick, tick));
            if (firstFireTick < 0 && frame.Fires.Length > 0)
            {
                firstFireTick = tick;
                anchors.Add(OnShot(ShotKind.FirstFire, frame));
            }
            else if (firstFireTick >= 0 && tick == firstFireTick + AfterFireTicks)
                anchors.Add(OnShot(ShotKind.FireAfter3s, frame));
            if (!flipped && previous != null && TryFlippedPoint(previous, frame, out ViewPoint point))
            {
                flipped = true;
                anchors.Add(new ShotAnchor(ShotKind.FirstFlip, tick, point.X, point.Z));
            }
            if (!ended && frame.Match.IsFinished)
            {
                ended = true;
                anchors.Add(new ShotAnchor(ShotKind.End, tick));
            }
            previous = frame;
        }

        // The +3 s frame may have no shot at that very tick: then the picture follows the soldier.
        private static ShotAnchor OnShot(ShotKind kind, FrameView frame)
        {
            if (frame.Fires.Length == 0)
                return new ShotAnchor(kind, frame.Tick);
            ViewFire fire = frame.Fires[0];
            return new ShotAnchor(kind, frame.Tick, (fire.X0 + fire.X1) / 2f, (fire.Z0 + fire.Z1) / 2f);
        }

        private static bool TryFlippedPoint(FrameView before, FrameView after, out ViewPoint point)
        {
            var a = before.Points; var b = after.Points;
            for (int i = 0; i < a.Length && i < b.Length; i++)
                if (a[i].Owner != b[i].Owner)
                {
                    point = b[i];
                    return true;
                }
            point = default;
            return false;
        }
    }
}
