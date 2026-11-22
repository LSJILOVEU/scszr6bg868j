using System;
using WarCore.Content;

namespace WarCore.Presentation
{
    // AIM-2: how far the mouse reaches round a body. The pick cylinder is the body's radius times RadiusPermille / 1000 wide and
    // its height at the stance level now times HeightPermille / 1000 tall (BodySizes gives both), so it follows the content
    // pack's body. A content pack sets it with its "pick" section; without one the hosts pass null and the pick is the old one.
    public sealed class PickReach
    {
        public int RadiusPermille { get; }
        public int HeightPermille { get; }

        public PickReach(int radiusPermille, int heightPermille)
        {
            if (radiusPermille < PickRules.MinRadiusPermille || radiusPermille > PickRules.MaxRadiusPermille)
                throw new ArgumentOutOfRangeException(nameof(radiusPermille), "The pick radius is " + PickRules.MinRadiusPermille + " to " + PickRules.MaxRadiusPermille + " permille.");
            if (heightPermille < PickRules.MinHeightPermille || heightPermille > PickRules.MaxHeightPermille)
                throw new ArgumentOutOfRangeException(nameof(heightPermille), "The pick height is " + PickRules.MinHeightPermille + " to " + PickRules.MaxHeightPermille + " permille.");
            RadiusPermille = radiusPermille;
            HeightPermille = heightPermille;
        }

        // The reach a content pack asks for, or null when it has no pick section.
        public static PickReach Of(ContentPack pack)
        {
            if (pack == null) throw new ArgumentNullException(nameof(pack));
            PickRules rules = pack.SectionOrNull<PickRules>();
            return rules == null ? null : new PickReach(rules.RadiusPermille, rules.HeightPermille);
        }

        // The cylinder's radius and height for a body of this radius and height (metres): float, multiplied before divided.
        public float Radius(float bodyRadius) => bodyRadius * RadiusPermille / 1000f;
        public float Height(float bodyTop) => bodyTop * HeightPermille / 1000f;
    }
}
