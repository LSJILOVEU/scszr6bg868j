using System;

namespace WarCore.Presentation
{
    /// <summary>Integer world-axis input. Sim normalizes movement; aim is a world point in millimetres.</summary>
    public readonly struct PlayerInput : IEquatable<PlayerInput>
    {
        public int MoveXMilli { get; }
        public int MoveZMilli { get; }
        public long AimXMm { get; }
        public long AimZMm { get; }
        public bool Primary { get; }

        public PlayerInput(int moveXMilli, int moveZMilli, long aimXMm = 0, long aimZMm = 0, bool primary = false)
        {
            if (moveXMilli < -1000 || moveXMilli > 1000)
                throw new ArgumentOutOfRangeException(nameof(moveXMilli));
            if (moveZMilli < -1000 || moveZMilli > 1000)
                throw new ArgumentOutOfRangeException(nameof(moveZMilli));
            SimSession.Target(aimXMm, aimZMm);
            MoveXMilli = moveXMilli; MoveZMilli = moveZMilli;
            AimXMm = aimXMm; AimZMm = aimZMm; Primary = primary;
        }

        public bool Equals(PlayerInput other) => MoveXMilli == other.MoveXMilli && MoveZMilli == other.MoveZMilli
            && AimXMm == other.AimXMm && AimZMm == other.AimZMm && Primary == other.Primary;
        public override bool Equals(object obj) => obj is PlayerInput other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = MoveXMilli;
                hash = hash * 397 ^ MoveZMilli;
                hash = hash * 397 ^ AimXMm.GetHashCode();
                hash = hash * 397 ^ AimZMm.GetHashCode();
                return hash * 397 ^ Primary.GetHashCode();
            }
        }
    }

    public readonly struct PlayerInputRecord
    {
        public long AtTick { get; }
        public PlayerInput Input { get; }

        internal PlayerInputRecord(long atTick, PlayerInput input)
        { AtTick = atTick; Input = input; }
    }
}
