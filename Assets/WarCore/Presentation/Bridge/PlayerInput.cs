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
        public bool Block { get; }
        public bool Throw { get; }
        /// <summary>0 = no skill; otherwise the held skill slot.</summary>
        public int SkillSlot { get; }
        /// <summary>Height of the aim point in millimetres; null = no height (the map's ground height is used).</summary>
        public long? AimYMm { get; }

        // Parameters may only be appended at the end.
        public PlayerInput(int moveXMilli, int moveZMilli, long aimXMm = 0, long aimZMm = 0, bool primary = false,
            bool block = false, bool @throw = false, int skillSlot = 0, long? aimYMm = null)
        {
            if (moveXMilli < -1000 || moveXMilli > 1000)
                throw new ArgumentOutOfRangeException(nameof(moveXMilli));
            if (moveZMilli < -1000 || moveZMilli > 1000)
                throw new ArgumentOutOfRangeException(nameof(moveZMilli));
            if (skillSlot < 0)
                throw new ArgumentOutOfRangeException(nameof(skillSlot));
            SimSession.Target(aimXMm, aimZMm);
            if (aimYMm.HasValue) SimSession.CheckAimY(aimYMm.Value);
            MoveXMilli = moveXMilli; MoveZMilli = moveZMilli;
            AimXMm = aimXMm; AimZMm = aimZMm; Primary = primary;
            Block = block; Throw = @throw; SkillSlot = skillSlot; AimYMm = aimYMm;
        }

        public bool Equals(PlayerInput other) => MoveXMilli == other.MoveXMilli && MoveZMilli == other.MoveZMilli
            && AimXMm == other.AimXMm && AimZMm == other.AimZMm && Primary == other.Primary
            && Block == other.Block && Throw == other.Throw && SkillSlot == other.SkillSlot && AimYMm == other.AimYMm;
        public override bool Equals(object obj) => obj is PlayerInput other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = MoveXMilli;
                hash = hash * 397 ^ MoveZMilli;
                hash = hash * 397 ^ AimXMm.GetHashCode();
                hash = hash * 397 ^ AimZMm.GetHashCode();
                hash = hash * 397 ^ Primary.GetHashCode();
                hash = hash * 397 ^ Block.GetHashCode();
                hash = hash * 397 ^ Throw.GetHashCode();
                hash = hash * 397 ^ SkillSlot;
                return hash * 397 ^ AimYMm.GetHashCode();
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
