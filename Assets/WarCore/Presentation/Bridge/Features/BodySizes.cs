using System;
using System.Runtime.CompilerServices;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // SCALE-2a: the one place the picture reads how big a unit is. Heights come from the content pack's body (CombatPack.Body,
    // level 0) and its stances (level n is Stances[n - 1]), in metres (raw / 65536), every one measured up from the feet. The
    // drawn body, the pick cylinder, the silhouette, the layer cut and the see-through ring all read it, so what is drawn and
    // what is picked stay the same height. A world without combat or without a body has none (Of returns null): such a world
    // keeps the old 1 m picture through TopOf and EyeOf.
    // A change of stance is read from ViewEntity.Stance: the old level until the change is due, the new one from then on.
    public sealed class BodySizes
    {
        // The default unit collider radius; a pack's own comes from its body (HC-1, BodyRadius).
        internal const long RadiusRaw = BodyRadius.DefaultRaw;
        private static readonly ConditionalWeakTable<CombatPack, BodySizes> Made = new ConditionalWeakTable<CombatPack, BodySizes>();
        private readonly float[] top, eye, muzzle;
        private readonly float radius;

        private BodySizes(CombatPack pack)
        {
            CombatBody body = pack.Body.Value;
            radius = body.Radius.Raw / 65536f;
            int levels = pack.Stances.Length + 1;
            top = new float[levels];
            eye = new float[levels];
            muzzle = new float[levels];
            for (int level = 0; level < levels; level++)
            {
                CombatBody b = level == 0 ? body : pack.Stances[level - 1].Body;
                top[level] = b.Height.Raw / 65536f;
                eye[level] = b.Eye.Raw / 65536f;
                muzzle[level] = b.Muzzle.Raw / 65536f;
            }
        }

        // The sizes of the session's units, or null when it has no combat or its pack has no body. One instance per pack.
        public static BodySizes Of(SimSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            return For(session.CombatPackForView);
        }

        internal static BodySizes For(CombatPack pack) =>
            pack == null || !pack.Body.HasValue ? null : Made.GetValue(pack, p => new BodySizes(p));

        // How many stance levels there are (1 without stances).
        public int Levels => top.Length;
        // The standing height (level 0): every drawn part is sized by it, whatever the unit's stance.
        public float StandingHeight => top[0];
        // The body's radius: the drawn body is 2 * Radius wide.
        public float Radius => radius;

        // The top of the head, the eye and the muzzle over the feet at the unit's stance level now.
        public float Top(ViewEntity e) => top[Level(e.Stance)];
        public float Eye(ViewEntity e) => eye[Level(e.Stance)];
        public float Muzzle(ViewEntity e) => muzzle[Level(e.Stance)];

        private int Level(int level)
        {
            if (level < 0 || level >= top.Length) throw new ArgumentOutOfRangeException(nameof(level), "No stance level " + level + ".");
            return level;
        }

        // The only fall-back to the old picture: without sizes the body is AimPick.BodyHeight tall and the eye is at its top. Both
        // return float, and callers add them to the feet in float before widening to double, as the old code did.
        public static float TopOf(BodySizes sizes, ViewEntity e) => sizes == null ? AimPick.BodyHeight : sizes.Top(e);
        public static float EyeOf(BodySizes sizes, ViewEntity e) => sizes == null ? AimPick.BodyHeight : sizes.Eye(e);
    }
}
