using System;
using NUnit.Framework;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using static WarCore.Tests.See1Field;

namespace WarCore.Tests
{
    // AIM-2 S1: the pick without a reach answers as it did before AIM-2. 10 000 rays from a fixed SplitMix64 seed over three
    // grounds (flat, the A4 ridge height field, the demo house's plates), each with and without body sizes (the "rwr-stance"
    // pack, three stance levels). The digest was recorded before AIM-2 changed AimPick. It hashes only what no runtime's float
    // rounding can move (Unity's Mono and .NET round float sums differently, see Scale2aGoldenTests): the kind, the snapped
    // unit's key, the layer, whether there is a height, and a snapped unit's height (from its exact float, in double). The
    // rays are built in double and rounded to float once. Points on the ground or a plate are covered by A4PickTests and
    // Ml10PickTests.
    public sealed class Aim2GoldenTests
    {
        private const ulong Digest = 6297845250137508449UL;
        private const int Rays = 10000;

        internal sealed class SplitMix64
        {
            private ulong state;
            public SplitMix64(ulong seed) { state = seed; }
            public ulong Next()
            {
                ulong z = state += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
            // A double in [0, 1) from the top 24 bits (exact).
            public double Unit() => (Next() >> 40) / 16777216.0;
            public double Range(double lo, double hi) => lo + (hi - lo) * Unit();
            public int Below(int n) => (int)(Next() % (ulong)n);
        }

        internal sealed class World
        {
            public ViewGround Ground;
            public ViewPlates Plates;
            public FrameView Shown;
            public float Width, Depth;
        }

        // The ridge of A4.Ridge in metres: 0 outside x 6..18, rising 0.5 per metre to 3 m at x 12.
        private static double RidgeY(double x) => x >= 6 && x <= 18 ? (6 - Math.Abs(x - 12)) / 2 : 0;

        // The player (key 1, team 1) and 23 more: mostly team 2, some team 1, one dead enemy and one without a team, so every
        // filter of the snap is crossed. Heights on the ground (or the floor plate of layer 2 at 3 m in the house).
        internal static FrameView Units(SplitMix64 rng, int ground, float width, float depth)
        {
            var units = new ViewEntity[24];
            for (int i = 0; i < units.Length; i++)
            {
                ulong key = (ulong)(i + 1);
                int team = i == 0 ? 1 : i % 6 == 1 ? 1 : i == 7 ? 0 : 2;
                bool alive = i != 9;
                int stance = rng.Below(3), layer = 0;
                double x, z, y = 0;
                if (ground == 2 && i % 2 == 0)
                {
                    x = rng.Range(9.5, 14.5); z = rng.Range(6.5, 9.5); y = 3; layer = 2;
                }
                else
                {
                    x = rng.Range(1, width - 1); z = rng.Range(1, depth - 1);
                    if (ground == 1) y = RidgeY(x);
                }
                units[i] = new ViewEntity(key, 0, (float)x, (float)y, (float)z, team: team, alive: alive, layer: layer, stance: stance);
            }
            return new FrameView(1, units);
        }

        internal static World[] Worlds(SplitMix64 rng)
        {
            var house = House();
            var worlds = new World[3];
            worlds[0] = new World { Ground = null, Plates = null, Width = 24f, Depth = 16f };
            worlds[1] = new World { Ground = A4.RidgeGround(), Plates = null, Width = 24f, Depth = 8f };
            worlds[2] = new World { Ground = ViewGround.For(house.Map), Plates = house.Plates, Width = 24f, Depth = 16f };
            for (int w = 0; w < 3; w++) worlds[w].Shown = Units(rng, w, worlds[w].Width, worlds[w].Depth);
            return worlds;
        }

        // One ray: a target point (on an enemy's body or near it, on the floor plate, or anywhere over the ground), a downward
        // direction, and the origin 25 m back along it.
        internal static (float ox, float oy, float oz, float fx, float fy, float fz) Ray(SplitMix64 rng, World world, int ground)
        {
            double tx, ty, tz;
            int kind = rng.Below(3);
            if (kind == 0)
            {
                ViewEntity e = world.Shown.Entities[1 + rng.Below(world.Shown.Entities.Length - 1)];
                tx = e.X + rng.Range(-0.7, 0.7); tz = e.Z + rng.Range(-0.7, 0.7); ty = e.Y + rng.Range(0, 2.2);
            }
            else if (ground == 2)
            {
                tx = rng.Range(9, 15); tz = rng.Range(6, 10); ty = 3;
            }
            else
            {
                tx = rng.Range(0, world.Width); tz = rng.Range(0, world.Depth); ty = ground == 1 ? RidgeY(tx) : 0;
            }
            double fx = rng.Range(-1, 1), fz = rng.Range(-1, 1), fy = -rng.Range(0.5, 1.0);
            double length = Math.Sqrt(fx * fx + fy * fy + fz * fz);
            fx /= length; fy /= length; fz /= length;
            return ((float)(tx - 25 * fx), (float)(ty - 25 * fy), (float)(tz - 25 * fz), (float)fx, (float)fy, (float)fz);
        }

        internal static ulong Run(Func<int, World, int, (float, float, float, float, float, float), BodySizes, AimTarget> pick,
            out int units, out int plates, out int grounds, out int refused)
        {
            var rng = new SplitMix64(0xA1A2UL);
            World[] worlds = Worlds(rng);
            BodySizes sizes = BodySizes.For(RwrPack.Load("rwr-stance").BuildCombat());
            var digest = new Scale2aGoldenTests.Fnv();
            units = plates = grounds = refused = 0;
            for (int r = 0; r < Rays; r++)
            {
                int w = r % 3;
                BodySizes s = (r / 3) % 2 == 0 ? null : sizes;
                var ray = Ray(rng, worlds[w], w);
                AimTarget a;
                try { a = pick(w, worlds[w], r, ray, s); }
                catch (ArgumentOutOfRangeException) { digest.Add(long.MaxValue); refused++; continue; }
                digest.Add((long)a.Kind); digest.Add((long)a.UnitKey); digest.Add(a.Layer); digest.Add(a.YMm.HasValue ? 1 : 0);
                if (a.Kind == AimKind.Unit) digest.Add(a.YMm ?? long.MinValue);
                if (a.Kind == AimKind.Unit) units++;
                else if (a.Kind == AimKind.Plate) plates++;
                else grounds++;
            }
            return digest.Value;
        }

        // The pick as the hosts call it before AIM-2: no reach.
        internal static AimTarget OldPick(int w, World world, int r, (float ox, float oy, float oz, float fx, float fy, float fz) ray, BodySizes sizes) =>
            AimPick.PickRay(ray.ox, ray.oy, ray.oz, ray.fx, ray.fy, ray.fz, 0f, world.Ground, world.Shown, 1, world.Plates, null, sizes);

        [Test]
        public void S1_ThePickWithoutAReachMatchesTheRecordedBits()
        {
            ulong got = 0;
            int units = 0, plates = 0, grounds = 0, refused = 0;
            Assert.DoesNotThrow(() => got = Run(OldPick, out units, out plates, out grounds, out refused));
            TestContext.WriteLine("digest " + got + " units " + units + " plates " + plates + " grounds " + grounds + " refused " + refused);
            Assert.That(units, Is.GreaterThanOrEqualTo(1000), "unit results");
            Assert.That(plates, Is.GreaterThanOrEqualTo(1000), "plate results");
            Assert.That(grounds, Is.GreaterThanOrEqualTo(1000), "ground and plane results");
            Assert.That(got, Is.EqualTo(Digest), "digest " + got);
        }
    }
}
