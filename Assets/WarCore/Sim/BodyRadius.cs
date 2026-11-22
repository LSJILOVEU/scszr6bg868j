namespace WarCore.Sim
{
    // HC-1: the one place a unit body's radius is defined. The content pack's combat.body.radius picks a value inside
    // [MinRaw, MaxRaw]; a pack that says nothing gets DefaultRaw. Every consumer (collider, walking clearance, deployment,
    // map gate, window gate, picture) reads the pack's value; none keeps a copy of 0.25 m.
    public static class BodyRadius
    {
        // 0.25 m: the radius of every unit before the pack could set one.
        public const long DefaultRaw = SimFix.Scale / 4;
        // 0.05 m: the smallest body a pack may ask for.
        public const long MinRaw = SimFix.Scale / 20;
        // The proof in NavGrid.SegmentClear bounds the clearance radius, so no body may be fatter than it allows.
        public const long MaxRaw = NavGrid.MaxClearRadiusRaw;

        public static SimFix Default => SimFix.FromRaw(DefaultRaw);

        // Is `radius` a body radius a pack may ask for?
        public static bool IsValid(SimFix radius) => radius.Raw >= MinRaw && radius.Raw <= MaxRaw;
    }
}
