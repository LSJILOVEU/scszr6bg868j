using System;

namespace WarCore.Sim
{
    // The action bits of InputFrame.Actions. Bits 0..3 ask for a stance: 0 asks for nothing, n (1..8) asks for stance level n - 1.
    // Bit 7 (0x80) is free and bits 8..11 (0xF00) are kept for picking a slot; neither is valid yet.
    public static class InputActions
    {
        public const uint StanceMask = 0xF;
        public const uint Reload = 0x10, Interact = 0x20, GiveUp = 0x40;
        public const uint ValidMask = 0x7F;
        public const int MaxStanceLevel = 7;

        public static bool IsValid(uint actions) => (actions & ~ValidMask) == 0 && (actions & StanceMask) <= MaxStanceLevel + 1;

        // -1 when no stance is asked for, else the level 0..7.
        public static int StanceRequest(uint actions) => (int)(actions & StanceMask) - 1;

        public static uint WithStance(uint actions, int level)
        {
            if (level < 0 || level > MaxStanceLevel)
                throw new ArgumentOutOfRangeException(nameof(level));
            return (actions & ~StanceMask) | (uint)(level + 1);
        }
    }
}
