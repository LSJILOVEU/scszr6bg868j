using System.Collections.Generic;

namespace WarCore.Content
{
    // AIM-2: what the optional section "pick" publishes. Only the picture and the mouse pick read it (Presentation's
    // PickReach); it is not part of the combat pack, of any policy digest or of the state hash, so a layer that adds it
    // simulates exactly as before. Without the section nothing is published and ContentPack.SectionOrNull<PickRules>() is null.
    public sealed class PickRules
    {
        // The pick cylinder's radius as permille of the body's radius, and its height as permille of the body's height at its
        // stance level now. The radius floor keeps the cylinder no thinner than the old fixed 0.5 m for a 0.25 m body.
        public const int MinRadiusPermille = 2000, MaxRadiusPermille = 6000, MinHeightPermille = 1000, MaxHeightPermille = 2000;

        public int RadiusPermille { get; }
        public int HeightPermille { get; }

        internal PickRules(int radiusPermille, int heightPermille) { RadiusPermille = radiusPermille; HeightPermille = heightPermille; }
    }

    // "pick": { "radiusPermille": 2000..6000, "heightPermille": 1000..2000 }: both fields required after the layers merge
    // (field by field, so a child layer may write one field and inherit the other), integers, no other field. It needs the
    // combat body (combat.body), which sizes the cylinder; a pack with pick and no body is refused.
    internal sealed class PickSection : ObjectSection
    {
        internal const string SectionKey = "pick", RadiusField = "radiusPermille", HeightField = "heightPermille";

        internal PickSection() : base(SectionKey) { }

        public override void Build(SectionBuild b)
        {
            ContentEntry entry = b.Single(Key);
            if (entry == null) return;
            var r = new EntryReader(b.Pack, entry);
            int radius = r.Int(RadiusField, PickRules.MinRadiusPermille, PickRules.MaxRadiusPermille);
            int height = r.Int(HeightField, PickRules.MinHeightPermille, PickRules.MaxHeightPermille);
            r.Done();
            if (!b.Body.HasValue)
                throw new ContentException("content pack '" + b.Pack + "' layer '" + entry.FirstLayer + "' pick needs combat.body (the pick is sized by the body)");
            b.Publish(new PickRules(radius, height));
        }
    }
}
