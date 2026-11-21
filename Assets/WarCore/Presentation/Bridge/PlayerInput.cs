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
        /// <summary>Layer of the aim point: 0 = the ground, 1..N = a floor plate of the map (worlds with plates only).</summary>
        public int AimLayer { get; }
        /// <summary>The dash key is held. The bridge asks for a dash on the tick it goes down (held -> no repeat).</summary>
        public bool Dash { get; }

        // Parameters may only be appended at the end.
        public PlayerInput(int moveXMilli, int moveZMilli, long aimXMm = 0, long aimZMm = 0, bool primary = false,
            bool block = false, bool @throw = false, int skillSlot = 0, long? aimYMm = null, int aimLayer = 0,
            bool dash = false)
        {
            if (moveXMilli < -1000 || moveXMilli > 1000)
                throw new ArgumentOutOfRangeException(nameof(moveXMilli));
            if (moveZMilli < -1000 || moveZMilli > 1000)
                throw new ArgumentOutOfRangeException(nameof(moveZMilli));
            if (skillSlot < 0)
                throw new ArgumentOutOfRangeException(nameof(skillSlot));
            if (aimLayer < 0 || aimLayer > UnitSpawn.MaxLayer)
                throw new ArgumentOutOfRangeException(nameof(aimLayer), "A layer is 0 to 4096.");
            AimLayer = aimLayer;
            SimSession.Target(aimXMm, aimZMm);
            if (aimYMm.HasValue) SimSession.CheckAimY(aimYMm.Value);
            MoveXMilli = moveXMilli; MoveZMilli = moveZMilli;
            AimXMm = aimXMm; AimZMm = aimZMm; Primary = primary;
            Block = block; Throw = @throw; SkillSlot = skillSlot; AimYMm = aimYMm; Dash = dash;
        }

        public bool Equals(PlayerInput other) => MoveXMilli == other.MoveXMilli && MoveZMilli == other.MoveZMilli
            && AimXMm == other.AimXMm && AimZMm == other.AimZMm && Primary == other.Primary
            && Block == other.Block && Throw == other.Throw && SkillSlot == other.SkillSlot && AimYMm == other.AimYMm
            && AimLayer == other.AimLayer && Dash == other.Dash;
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
                hash = hash * 397 ^ AimYMm.GetHashCode();
                // Layer 0 keeps the hash code of an input without layers.
                if (AimLayer != 0) hash = hash * 397 ^ AimLayer;
                // No dash keeps the hash code of an input without a dash.
                return Dash ? hash * 397 ^ 1 : hash;
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
