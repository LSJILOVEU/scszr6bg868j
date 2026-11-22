using UnityEngine;

namespace WarCore.Unity
{
    // SHOW-1's fixed soldier sizes, kept only for a world without body sizes (BodySizes.Of is null: the house, terrain and
    // melee stages). A world with a body draws every part from DrawnParts instead (SCALE-2a). PLACEHOLDER art.
    public static class ShowcaseLegacySizes
    {
        public const float BodyHeight = 1.7f, BodyWidth = 0.5f, DuckScale = 0.62f, RingRadius = 0.7f;
        public static readonly Vector3 HeadAt = new Vector3(0f, BodyHeight - 0.12f, 0f), HeadScale = new Vector3(0.3f, 0.3f, 0.3f);
        public static readonly Vector3 GunAt = new Vector3(0.2f, 1.25f, 0.45f), GunScale = new Vector3(0.08f, 0.1f, 0.9f);
        public static readonly Vector3 BladeAt = new Vector3(0.28f, 1.3f, 0.5f), BladeScale = new Vector3(0.05f, 0.1f, 1.1f);
        public static readonly Vector3 ShieldAt = new Vector3(-0.1f, 1.05f, 0.42f), ShieldScale = new Vector3(0.7f, 0.9f, 0.08f);
        public static readonly Vector3 NerveAt = new Vector3(0f, BodyHeight + 0.35f, 0f), NerveScale = new Vector3(0.28f, 0.28f, 0.28f);
        public static readonly Vector3 LeaderAt = new Vector3(0f, BodyHeight + 0.8f, 0f), LeaderScale = new Vector3(0.3f, 0.3f, 0.3f);
        public const float BladeTilt = -35f;
        // Gunfire and effects: a shot leaves 1.4 m over the shooter's feet and ends 1.1 m over the ground on a height field; the
        // flash sits 0.9 m ahead; the grenade leaves the hand 1.5 m up; chips fly 1.1 m up from a hit unit.
        public const float MuzzleHeight = 1.4f, ShotEndLift = 1.1f, FlashReach = 0.9f, ThrowHeight = 1.5f, ChipsHeight = 1.1f;
    }
}
