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

        private FireIntent(StableId shooter, FireIntentKind kind, SimVec3 point, StableId target)
        { Shooter = shooter; Kind = kind; Point = point; Target = target; }

        public static FireIntent Aimed(StableId shooter, SimVec3 point) => new FireIntent(shooter, FireIntentKind.Aimed, point, default);
        public static FireIntent AtTarget(StableId shooter, StableId target) => new FireIntent(shooter, FireIntentKind.AtTarget, default, target);
    }
}
