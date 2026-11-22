using System;
using WarCore.Sim;

namespace WarCore.Combat
{
    // VSPREAD-1: where aimed and automatic lines at a unit end. Eye: the unit's eye, as before. Center: the middle of the
    // unit's body (half its stance's height), back to the eye when a wall cuts the line to the middle.
    public enum VerticalAim : byte { Eye = 0, Center = 1 }

    // VSPREAD-1 combat.verticalSpread: after the horizontal turn every shot is tilted up or down by up to its shot angle times
    // the shooter's stance permille (capped at MaxVerticalMilli), and optionally aimed at the middle of the body. Permille is
    // the standing level's value (level 0); StancePermille[n - 1] is level n's, resolved (a stance the layer did not name
    // takes the standing value). SnapMm: how close to an enemy's foot a controlled player's ground point must be to aim at
    // that enemy (Center only). Immutable; the pack checks that it has a body and one value per stance.
    public sealed class VerticalSpread
    {
        public const int MaxVerticalMilli = 45000;
        public const int DefaultSnapMm = 600, MinSnapMm = 1, MaxSnapMm = 5000;

        private readonly int[] stancePermille;
        public int Permille { get; }
        public ReadOnlySpan<int> StancePermille => stancePermille;
        public VerticalAim Aim { get; }
        public int SnapMm { get; }
        // The snap radius in raw units, truncated (600 mm = 39321 raw).
        public long SnapRaw => (long)SnapMm * SimFix.Scale / 1000;

        public VerticalSpread(int permille, int[] stancePermille = null, VerticalAim aim = VerticalAim.Eye, int snapMm = DefaultSnapMm)
        {
            Check(permille, nameof(permille));
            this.stancePermille = stancePermille == null ? Array.Empty<int>() : (int[])stancePermille.Clone();
            foreach (int value in this.stancePermille) Check(value, nameof(stancePermille));
            if (aim != VerticalAim.Eye && aim != VerticalAim.Center) throw new ArgumentOutOfRangeException(nameof(aim));
            if (snapMm < MinSnapMm || snapMm > MaxSnapMm)
                throw new ArgumentOutOfRangeException(nameof(snapMm), "The snap distance is " + MinSnapMm + " to " + MaxSnapMm + " mm.");
            Permille = permille; Aim = aim; SnapMm = snapMm;
        }

        private static void Check(int permille, string name)
        {
            if (permille < StatLine.MinPermille || permille > StatLine.MaxPermille)
                throw new ArgumentOutOfRangeException(name, "A vertical permille is " + StatLine.MinPermille + " to " + StatLine.MaxPermille + ".");
        }

        // The permille of a level (0 = standing).
        public int PermilleOf(int level) => level == 0 ? Permille : stancePermille[level - 1];

        // The vertical angle of a shot: shotAngle * permille / 1000, truncated, capped at MaxVerticalMilli.
        public static int Angle(int shotAngle, int permille) => (int)Math.Min(MaxVerticalMilli, (long)shotAngle * permille / 1000);

        // Tilts a direction up (positive offset) or down by a uniform offset in [-angle, +angle] (thousandths of a degree)
        // chosen by roll, in the vertical plane through the direction; the horizontal heading is kept. The direction is first
        // normalized; one with no horizontal part comes back untouched. A steep line tilted past the vertical is not clamped.
        public static SimVec3 Tilt(SimVec3 direction, int angleMilli, int roll)
        {
            long offset = angleMilli * (2L * roll - 65535) / 65535;
            SimFix turns = SimFix.Ratio(offset, 360000);
            SimFix sin = SimMath.Sin(turns), cos = SimMath.Cos(turns);
            SimVec3 d = direction.Normalized;
            SimFix h = new SimVec3(d.X, default, d.Z).Length;
            if (h.Raw == 0) return direction;
            SimFix across = h * cos - d.Y * sin, up = h * sin + d.Y * cos;
            return new SimVec3(SimFix.FromRaw(d.X.Raw * across.Raw / h.Raw), up, SimFix.FromRaw(d.Z.Raw * across.Raw / h.Raw));
        }

        internal void WriteState(StateHash hash)
        {
            hash.Add("combat.pack.vspread.v1"); hash.Add(Permille); hash.Add(stancePermille.Length);
            foreach (int value in stancePermille) hash.Add(value);
            hash.Add((int)Aim); hash.Add(SnapMm);
        }
    }
}
