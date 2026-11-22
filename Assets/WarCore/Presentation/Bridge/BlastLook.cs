using System;

namespace WarCore.Presentation
{
    // HC-4: the sizes of a drawn blast, read from the blast's own radii (metres) so the picture follows the content pack:
    // the scorch mark and the dust ring mark the inner radius (full harm), a ring on the ground marks the outer radius (no
    // harm beyond) and fades out; flash and smoke grow with the outer radius. Also the drawn grenade's reach and flight time,
    // read from the throw's millimetres and ticks. Pure numbers, no engine: ShowcaseFx draws them.
    public readonly struct BlastLook
    {
        // Drawn-only feel (not simulation values).
        public const float FlashPerOuter = 0.55f, SmokePerOuter = 0.75f, RingFadeSeconds = 0.8f, RingWidth = 0.12f, RingLift = 0.04f;

        public float Inner { get; }
        public float Outer { get; }

        private BlastLook(float inner, float outer) { Inner = inner; Outer = outer; }

        // inner may be 0 (no full-harm core); outer must be larger than inner. NaN is refused.
        public static BlastLook Of(float inner, float outer)
        {
            if (!(inner >= 0f && outer > inner)) throw new ArgumentOutOfRangeException(nameof(inner), "A drawn blast needs 0 <= inner < outer.");
            return new BlastLook(inner, outer);
        }

        public bool HasCore => Inner > 0f;
        public float ScorchDiameter => 2f * Inner;
        public float DustRadius => Inner;
        public float RingRadius => Outer;
        public float FlashSize => Outer * FlashPerOuter;
        public float SmokeSize => Outer * SmokePerOuter;

        // How much of the outer ring is left age seconds after the burst: 1 at the burst, 0 from RingFadeSeconds on.
        public static float RingAlpha(float age)
        {
            if (!(age > 0f)) return 1f;
            float left = 1f - age / RingFadeSeconds;
            return left > 0f ? left : 0f;
        }

        // The drawn grenade's reach in metres and its flight in seconds (the flight lasts the fuse).
        public static float ReachMetres(long reachMm) => reachMm / 1000f;
        public static float FlightSeconds(long fuseTicks) => fuseTicks / (float)SimSession.TickRate;
    }
}
