using System;
using System.Text;

namespace WarCore.Presentation
{
    // What the followed unit shows on the HUD: health in display units (raw / SimFix.Scale) and the magazine count.
    public readonly struct UnitStatus
    {
        public float Health { get; }
        public float MaxHealth { get; }
        public int Ammo { get; }
        // Sampled at the session's committed tick. Includes ordinary fire intervals and reloads.
        // Zero is only a timer value: also check Alive and HasWeapon before showing readiness.
        public long WaitTicksLeft { get; }
        public bool Reloading { get; }
        public bool Alive { get; }
        public bool HasWeapon { get; }
        // The unit is holding a block in the Sim at the committed tick (not whether a block key is down).
        public bool Blocking { get; }
        // Parameters may only be appended at the end.
        public UnitStatus(float health, float maxHealth, int ammo, long waitTicksLeft = 0,
            bool reloading = false, bool alive = true, bool hasWeapon = true, bool blocking = false)
        {
            Health = health; MaxHealth = maxHealth; Ammo = ammo; WaitTicksLeft = waitTicksLeft;
            Reloading = reloading; Alive = alive; HasWeapon = hasWeapon; Blocking = blocking;
        }
    }

    public static class HudLines
    {
        // status / pick are null when nothing is followed or the mouse is not over the ground.
        public static string Build(FrameView frame, ulong stateHash, ulong? followKey, UnitStatus? status, GroundPick? pick)
        {
            var text = new StringBuilder();
            text.Append(FormattableString.Invariant($"Tick {frame.Tick}\nStateHash {stateHash:X16}\n"));
            if (followKey.HasValue && status.HasValue)
            {
                text.Append(FormattableString.Invariant($"Follow #{followKey.Value}  HP {status.Value.Health:F0}/{status.Value.MaxHealth:F0}  Ammo {status.Value.Ammo}\n"));
                if (status.Value.Blocking)
                    text.Append("Blocking\n");
            }
            else
                text.Append("Follow -\n");
            text.Append("Points");
            foreach (ViewPoint p in frame.Points)
                text.Append(FormattableString.Invariant($"  P{p.Id} {(p.Owner == 0 ? "-" : "T" + p.Owner)}"));
            text.Append('\n');
            text.Append(pick.HasValue ? FormattableString.Invariant($"Ground ({pick.Value.X:F1}, {pick.Value.Z:F1})") : "Ground -");
            return text.ToString();
        }
    }
}
