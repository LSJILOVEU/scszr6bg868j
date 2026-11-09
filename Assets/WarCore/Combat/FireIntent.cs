using WarCore.Sim;

namespace WarCore.Combat
{
    public enum FireIntentKind : byte { Aimed = 1, AtTarget = 2 }

    // Transient input. Each submission is valid only for its simulation tick.
    public readonly struct FireIntent
    {
        public readonly StableId Shooter;
        public readonly FireIntentKind Kind;
        public readonly SimVec3 Point;
        public readonly StableId Target;
        // Aimed only: decline the shot (no ammo, no cooldown change) when a living teammate is the first thing on the line.
        public readonly bool AvoidAllies;

        private FireIntent(StableId shooter, FireIntentKind kind, SimVec3 point, StableId target, bool avoidAllies = false)
        { Shooter = shooter; Kind = kind; Point = point; Target = target; AvoidAllies = avoidAllies; }

        public static FireIntent Aimed(StableId shooter, SimVec3 point) => new FireIntent(shooter, FireIntentKind.Aimed, point, default);
        public static FireIntent AtTarget(StableId shooter, StableId target) => new FireIntent(shooter, FireIntentKind.AtTarget, default, target);
        public static FireIntent Suppress(StableId shooter, SimVec3 point) => new FireIntent(shooter, FireIntentKind.Aimed, point, default, true);
    }
}
