using System;

namespace WarCore.Presentation
{
    // One drawn part of a soldier: its centre and size in metres in the body's own frame (x to the right, y up from the feet,
    // z forward, the way the unit faces).
    public readonly struct DrawnPart
    {
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float SizeX { get; }
        public float SizeY { get; }
        public float SizeZ { get; }
        public float Bottom => Y - SizeY / 2;
        public float TopY => Y + SizeY / 2;

        public DrawnPart(float x, float y, float z, float sizeX, float sizeY, float sizeZ)
        {
            X = x; Y = y; Z = z; SizeX = sizeX; SizeY = sizeY; SizeZ = sizeZ;
        }
    }

    // A whole soldier as drawn. Lying: the body lies on its front along z (the torso's long side is z, not y); upright otherwise.
    // Highest: the top of the highest of torso, head, pack and tool (the marks float above and do not count).
    public readonly struct DrawnBody
    {
        public DrawnPart Torso { get; }
        public DrawnPart Head { get; }
        public DrawnPart Gun { get; }
        public DrawnPart Blade { get; }
        public DrawnPart Shield { get; }
        public DrawnPart Pack { get; }
        public DrawnPart NerveMark { get; }
        public DrawnPart LeaderMark { get; }
        public float RingRadius { get; }
        public bool Lying { get; }
        // Degrees the blade is tilted up (upright only; a lying blade is level).
        public float BladeTilt { get; }

        internal DrawnBody(DrawnPart torso, DrawnPart head, DrawnPart gun, DrawnPart blade, DrawnPart shield, DrawnPart pack,
            DrawnPart nerveMark, DrawnPart leaderMark, float ringRadius, bool lying, float bladeTilt)
        {
            Torso = torso; Head = head; Gun = gun; Blade = blade; Shield = shield; Pack = pack; NerveMark = nerveMark;
            LeaderMark = leaderMark; RingRadius = ringRadius; Lying = lying; BladeTilt = bladeTilt;
        }

        public float Highest => Math.Max(Math.Max(Torso.TopY, Head.TopY), Math.Max(Pack.TopY, Math.Max(Gun.TopY, Blade.TopY)));
        // From the back of the torso to the front of the head along z (lying), or from the feet to the top (upright).
        public float Length => Lying ? Head.Z + Head.SizeZ / 2 - (Torso.Z - Torso.SizeZ / 2) : Math.Max(Torso.TopY, Head.TopY);
    }

    // SCALE-2a: the table that sizes a drawn soldier from its body. Every part's size is a share of the standing height H
    // (BodySizes.StandingHeight), except the torso's width, which is the body's diameter 2r; positions are anchored on the
    // simulation's own numbers: the head's top is the stance's Top, the gun and the blade are at its Muzzle. Crouched: the same
    // parts, lower. Prone: lying on the front, H long, nothing higher than the stance's Top. Drawn only: no Sim number reads it.
    // PLACEHOLDER shares (the dispatch's starting values); a content pack does not set them yet.
    public static class DrawnParts
    {
        public const float TorsoDepth = 0.16f, Head = 0.14f, Rifle = 0.48f, BladeLength = 0.5f;
        public const float ShieldHigh = 0.5f, ShieldWide = 0.35f, ShieldDrop = 0.25f;
        public const float PackHigh = 0.3f, PackWide = 0.2f, PackDeep = 0.12f;
        public const float NerveMarkSize = 0.16f, LeaderMarkSize = 0.17f, NerveLift = 0.2f, LeaderLift = 0.46f;
        public const float Ring = 0.4f, Chips = 0.63f;
        // The thin sides of the tools, and where they sit, as shares of H or of the radius r.
        private const float ToolThick = 0.046f, ToolHigh = 0.057f, BladeThick = 0.03f, GunOut = 0.8f, BladeOut = 1.12f;
        private const float ShieldOut = -0.4f, ShieldAhead = 0.24f, HeadAhead = 0.2f, GunAhead = 0.3f;
        public const float UprightBladeTilt = -35f;

        // A stance whose top is below this share of the standing height is drawn lying (prone); higher ones upright (standing,
        // crouched). The packs' prone top is 0.5 of 1.75 (0.29), their crouch 1.0 (0.57).
        public const float LyingBelow = 0.4f;

        public static DrawnBody For(float standingHeight, float radius, float top, float muzzle) =>
            top < LyingBelow * standingHeight ? Lying(standingHeight, radius, top, muzzle) : Upright(standingHeight, radius, top, muzzle);

        // The upright (standing or crouched) soldier: standingHeight H, radius r, the stance's top and muzzle over the feet.
        public static DrawnBody Upright(float standingHeight, float radius, float top, float muzzle)
        {
            Check(standingHeight, radius, top, muzzle);
            float h = standingHeight, head = Head * h, shoulder = top - head;
            float gun = Rifle * h, blade = BladeLength * h, depth = TorsoDepth * h;
            var torso = new DrawnPart(0f, shoulder / 2, 0f, 2 * radius, shoulder, depth);
            var headPart = new DrawnPart(0f, top - head / 2, 0f, head, head, head);
            var gunPart = new DrawnPart(GunOut * radius, muzzle, gun / 2, ToolThick * h, ToolHigh * h, gun);
            var bladePart = new DrawnPart(BladeOut * radius, muzzle, 0.45f * blade, BladeThick * h, ToolHigh * h, blade);
            var shield = new DrawnPart(ShieldOut * radius, muzzle - ShieldDrop * h, ShieldAhead * h, ShieldWide * h, ShieldHigh * h, ToolThick * h);
            float packHigh = PackHigh * h, packDeep = PackDeep * h;
            var pack = new DrawnPart(0f, shoulder - packHigh / 2, -(depth + packDeep) / 2, PackWide * h, packHigh, packDeep);
            return new DrawnBody(torso, headPart, gunPart, bladePart, shield, pack, Marks(h, top, NerveMarkSize, NerveLift),
                Marks(h, top, LeaderMarkSize, LeaderLift), Ring * h, false, UprightBladeTilt);
        }

        // Prone: on the front along z, the head forward of the feet's point, H from the back of the torso to the front of the
        // head. Torso, head, pack and tool keep their sizes, but when torso and pack together would stand higher than top, every
        // height of the pose is squeezed by the same factor so that nothing tops it.
        public static DrawnBody Lying(float standingHeight, float radius, float top, float muzzle)
        {
            Check(standingHeight, radius, top, muzzle);
            float h = standingHeight, head = Head * h, depth = TorsoDepth * h, packDeep = PackDeep * h;
            float k = Math.Min(1f, top / (depth + packDeep));
            float headZ = HeadAhead * h, back = headZ - head / 2, length = h - head;
            var torso = new DrawnPart(0f, k * depth / 2, back - length / 2, 2 * radius, k * depth, length);
            var headPart = new DrawnPart(0f, k * depth / 2, headZ, head, k * head, head);
            float gun = Rifle * h, blade = BladeLength * h, toolY = Math.Min(muzzle, top - k * ToolHigh * h / 2);
            var gunPart = new DrawnPart(GunOut * radius, toolY, GunAhead * h, ToolThick * h, k * ToolHigh * h, gun);
            var bladePart = new DrawnPart(BladeOut * radius, toolY, GunAhead * h, BladeThick * h, k * ToolHigh * h, blade);
            var shield = new DrawnPart(ShieldOut * radius, k * ToolThick * h / 2, headZ + ShieldAhead * h, ShieldWide * h, k * ToolThick * h, ShieldHigh * h);
            float packLong = PackHigh * h;
            var pack = new DrawnPart(0f, k * (depth + packDeep / 2), back - packLong / 2, PackWide * h, k * packDeep, packLong);
            return new DrawnBody(torso, headPart, gunPart, bladePart, shield, pack, Marks(h, top, NerveMarkSize, NerveLift),
                Marks(h, top, LeaderMarkSize, LeaderLift), Ring * h, true, 0f);
        }

        private static DrawnPart Marks(float h, float top, float size, float lift) => new DrawnPart(0f, top + lift * h, 0f, size * h, size * h, size * h);

        private static void Check(float standingHeight, float radius, float top, float muzzle)
        {
            if (!(standingHeight > 0f) || float.IsInfinity(standingHeight)) throw new ArgumentOutOfRangeException(nameof(standingHeight));
            if (!(radius > 0f) || float.IsInfinity(radius)) throw new ArgumentOutOfRangeException(nameof(radius));
            if (!(top > 0f) || top > standingHeight) throw new ArgumentOutOfRangeException(nameof(top));
            if (!(muzzle >= 0f) || muzzle > top) throw new ArgumentOutOfRangeException(nameof(muzzle));
        }

        // Where chips fly off a hit unit, over its feet.
        public static float ChipsHeight(float top) => Chips * top;

        // How far the muzzle flash sits in front of the shot's start: the drawn gun's length.
        public static float GunLength(float standingHeight) => Rifle * standingHeight;

        // The scale that makes a model modelHeight tall stand simHeight tall. Both must be finite and above 0.
        public static float ModelFit(float modelHeight, float simHeight)
        {
            if (!(modelHeight > 0f) || float.IsInfinity(modelHeight)) throw new ArgumentOutOfRangeException(nameof(modelHeight));
            if (!(simHeight > 0f) || float.IsInfinity(simHeight)) throw new ArgumentOutOfRangeException(nameof(simHeight));
            return simHeight / modelHeight;
        }

        // A shot's drawn end height: never below the ground there (the height field's, or the flat floor's). A shot spread
        // downward can end under a flat map's floor in the simulation; only the picture is lifted.
        public static float ShotEndY(float y1, ViewGround ground, float floorY, float x, float z) =>
            Math.Max(y1, ground == null ? floorY : ground.HeightAt(x, z, floorY));
    }
}
