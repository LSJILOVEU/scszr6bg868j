using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Presentation;
using static WarCore.Tests.See1Field;

namespace WarCore.Tests
{
    // SCALE-2a S3(e), S3(f), S4: the old path (no body sizes) is bit for bit what it was before SCALE-2a. Every digest below was
    // recorded on the code before the change (public main 89dfb1a) and is an FNV-1a hash of the exact bits of every result
    // (floats by SingleToInt32Bits), so the old path is compared with the old code, not with itself.
    public sealed class Scale2aGoldenTests
    {
        // Heights where float y + 1f and double y + 1.0 differ, so a change of the order of the addition shows.
        private static readonly float[] Ys = { 0.1f, 1.1f, 0.37f, 5.3f };

        // S3(e): [height index, group] with group 0 the side rays (leaving the cylinder at its top edge), 1 the cap rays (landing
        // on the top near the far edge), 2 the straight down rays (at the top and at the rim).
        private static readonly ulong[,] PickDigest =
        {
            { 13550308178592979205UL, 12897167355683376280UL, 16993100118699191507UL },
            { 14282959161814475523UL, 3036073935472983806UL, 7993992914543038737UL },
            { 15286087353135506899UL, 1750000245121862921UL, 2655799430188989038UL },
            { 13277586483481029284UL, 15181393506858016523UL, 13708917454612860865UL },
        };

        private const ulong TerrainInputDigest = 2454987396881980250UL, HouseInputDigest = 9975092720828191415UL;
        private const ulong SilhouetteDigest = 9504576269491795843UL, CutDigest = 11946379032752552359UL, SeeDigest = 10214496898857319079UL;

        internal sealed class Fnv
        {
            public ulong Value = 14695981039346656037UL;
            public int Count;
            public void Add(long v) { for (int i = 0; i < 8; i++) { Value ^= (byte)(v >> (8 * i)); Value *= 1099511628211UL; } }
            public void Add(float v) => Add(BitConverter.SingleToInt32Bits(v));
            public void Add(AimTarget a)
            {
                Add((long)a.Kind); Add(a.X); Add(a.Y); Add(a.Z); Add(a.YMm ?? long.MinValue); Add((long)a.UnitKey); Add(a.Layer);
                Count++;
            }
        }

        private static float Step(float v, int ulps) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(v) + ulps);

        private static FrameView Pair(float y) => new FrameView(1, new[]
        {
            new ViewEntity(1, 0, 0f, y, 0f, team: 1),
            new ViewEntity(2, 0, 9f, y, 0f, team: 2),
        });

        // The rays of one group for the enemy at (9, y, 0), radius 0.5; pick(ox, oy, oz, fx, fy, fz) gives the result.
        internal static void Sweep(float y, int group, Action<float, float, float, float, float, float> pick)
        {
            float top = y + 1f;
            if (group == 0)
            {
                // From x 7 level through the axis: the ray enters at t 1.5 (x 8.5) and leaves at t 2.5 (x 9.5).
                float oy = y + 1.125f, fy0 = (top - oy) / 2.5f;
                for (int k = -40; k <= 40; k++) pick(7f, oy, 0f, 1f, Step(fy0, k), 0f);
            }
            else if (group == 1)
            {
                // Steep: down 2 for 1 across, from 1 m over the top, landing about x 9.5.
                for (int i = -8; i <= 8; i++)
                    for (int j = -4; j <= 4; j++) pick(Step(9f, i), Step(top + 1f, j), 0f, 1f, -2f, 0f);
            }
            else
            {
                foreach (float x in new[] { 9.2f, 9.5f })
                    for (int i = -3; i <= 3; i++)
                        for (int j = -3; j <= 3; j++) pick(Step(x, i), Step(top, j), 0f, 0f, -1f, 0f);
                pick(9.2f, top + 2f, 0f, 0f, -1f, 0f);
            }
        }

        // The old path, called as the code before SCALE-2a called it.
        internal static AimTarget OldPick(FrameView shown, float ox, float oy, float oz, float fx, float fy, float fz) =>
            AimPick.PickRay(ox, oy, oz, fx, fy, fz, 0f, null, shown, 1);

        [Test]
        public void TheHeightsMakeTheOrderOfTheAdditionShow()
        {
            int differ = 0;
            foreach (float y in Ys) if ((double)(y + 1f) != (double)y + 1.0) differ++;
            Assert.That(differ, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void OldPickRaysMatchTheRecordedBits()
        {
            var got = new ulong[Ys.Length, 3];
            for (int h = 0; h < Ys.Length; h++)
                for (int g = 0; g < 3; g++)
                {
                    FrameView shown = Pair(Ys[h]);
                    var digest = new Fnv();
                    int units = 0;
                    Sweep(Ys[h], g, (ox, oy, oz, fx, fy, fz) =>
                    {
                        AimTarget a = OldPick(shown, ox, oy, oz, fx, fy, fz);
                        if (a.Kind == AimKind.Unit) units++;
                        digest.Add(a);
                    });
                    Assert.That(units, Is.GreaterThan(0).And.LessThan(digest.Count), "the sweep straddles the edge: " + h + "/" + g);
                    got[h, g] = digest.Value;
                }
            Assert.That(Text(got), Is.EqualTo(Text(PickDigest)));
        }

        private static string Text(ulong[,] d)
        {
            var parts = new List<string>();
            for (int h = 0; h < d.GetLength(0); h++) parts.Add("{ " + d[h, 0] + "UL, " + d[h, 1] + "UL, " + d[h, 2] + "UL },");
            return string.Join(" ", parts);
        }

        private static ShowcaseStage Stage(string id)
        {
            foreach (ShowcaseStage stage in Showcase.Stages) if (stage.Id == id) return stage;
            throw new ArgumentException(id);
        }

        // S3(f): the house and terrain stages (no body) at tick 100, a 20 x 10 grid of screen points on 1280 x 720, row by row:
        // the cut, the pick and one mapper (it keeps the last pick), as the showcase host does every frame.
        internal static ulong InputDigest(string id, Func<SimSession, IsoCamera, float, float, LayerCutResult, AimTarget?> pick,
            Func<SimSession, ViewEntity?, (float X, float Y, float Z), LayerCutResult> cutOf)
        {
            SimSession session = Stage(id).Start();
            while (session.Tick < 100) session.StepOnce();
            ViewMap map = session.Map;
            var camera = new IsoCamera(Stage(id).Zoom, map.FloorY);
            Assert.That(session.Current.TryGet(session.ControlledKey.Value, out ViewEntity me), Is.True);
            camera.Follow(me.X, me.Z);
            var mapper = new PlayerInputMapper();
            var digest = new Fnv();
            for (int j = 0; j < 10; j++)
                for (int i = 0; i < 20; i++)
                {
                    float x = (i + 0.5f) * 64f, y = (j + 0.5f) * 72f;
                    LayerCutResult cut = cutOf(session, me, camera.Forward);
                    AimTarget? aim;
                    try { aim = pick(session, camera, x, y, cut); }
                    catch (ArgumentOutOfRangeException) { aim = null; }
                    PlayerInput input = mapper.Map(camera, 0, 0, x, y, 1280, 720, true, false, null, aim);
                    digest.Add(input.AimXMm); digest.Add(input.AimZMm); digest.Add(input.AimYMm ?? long.MinValue); digest.Add(input.AimLayer);
                }
            return digest.Value;
        }

        internal static AimTarget? OldScreenPick(SimSession s, IsoCamera camera, float x, float y, LayerCutResult cut) =>
            AimPick.Pick(camera, x, y, 1280, 720, ViewGround.For(s.Map), s.Current, s.ControlledKey, s.Map.Plates, cut);

        internal static LayerCutResult OldCut(SimSession s, ViewEntity? me, (float X, float Y, float Z) forward) =>
            LayerCut.Run(s.Map.Plates, s.Map, me, forward, LayerCut.SeeRadius);

        [TestCase("terrain")]
        [TestCase("house")]
        public void OldInputOfAStageWithoutABodyMatchesTheRecordedBits(string id)
        {
            ulong got = InputDigest(id, OldScreenPick, OldCut);
            Assert.That(got, Is.EqualTo(id == "terrain" ? TerrainInputDigest : HouseInputDigest), "digest " + got);
        }

        // S4 grounds: building A (ground T) and the demo house; units and the player at heights that are not whole metres.
        internal static readonly float[] UnitYs = { 0.37f, 1.1f };

        internal static ViewEntity[] Mates(float y)
        {
            var units = new List<ViewEntity> { At(60, 40, y: y) };
            ulong key = 2;
            foreach (float x in new[] { 25f, 31f, 42f })
                for (float z = 15f; z <= 18.5f; z += 0.25f) units.Add(At(x, z, key++, y: y));
            return units.ToArray();
        }

        internal static ulong SilhouetteRun(Func<ViewMap, LayerCutResult, ViewEntity, ViewEntity[], HashSet<ulong>> run)
        {
            ViewMap t = T();
            var digest = new Fnv();
            foreach (float y in UnitYs)
            {
                ViewEntity[] units = Mates(y);
                LayerCutResult cut = LayerCut.Run(t.Plates, t, units[0], Forward);
                var set = new List<ulong>(run(t, cut, units[0], units));
                set.Sort();
                digest.Add(set.Count);
                foreach (ulong k in set) digest.Add((long)k);
            }
            return digest.Value;
        }

        internal static HashSet<ulong> OldSilhouettes(ViewMap t, LayerCutResult cut, ViewEntity me, ViewEntity[] units) =>
            Silhouettes.Run(t.Plates, t, cut, me, units, Forward);

        internal static void AddCut(Fnv digest, LayerCutResult cut)
        {
            digest.Add(cut.Indoors ? 1 : 0);
            for (int k = 0; k < cut.PlateCount; k++) digest.Add((long)cut.Plate(k));
            for (int k = 0; k < cut.WallCount; k++) digest.Add((long)cut.Wall(k));
        }

        // The cut of the demo house over a grid of the player's places (on the ground and on the first floor plate).
        internal static ulong CutRun(Func<ViewPlates, ViewMap, ViewEntity, LayerCutResult> run)
        {
            var house = House();
            ViewMap m = house.Map;
            var digest = new Fnv();
            foreach (float y in new[] { 0.37f, 3.3f })
                for (int i = 0; i <= 12; i++)
                    for (int j = 0; j <= 12; j++)
                    {
                        float x = m.MinX + (i + 0.5f) * m.Width / 13f, z = m.MinZ + (j + 0.5f) * m.Depth / 13f;
                        AddCut(digest, run(house.Plates, m, At(x, z, y: y, layer: y > 1f ? 1 : 0)));
                    }
            return digest.Value;
        }

        internal static LayerCutResult OldCutAt(ViewPlates plates, ViewMap m, ViewEntity me) => LayerCut.Run(plates, m, me, Forward);

        // The see-through ring (radius 5) on ground T over a grid of the player's places.
        internal static ulong SeeRun(Func<ViewMap, ViewEntity, LayerCutResult> run)
        {
            ViewMap t = T();
            var digest = new Fnv();
            foreach (float y in UnitYs)
                for (float x = 5f; x <= 48f; x += 1.5f)
                    for (float z = 14.5f; z <= 26f; z += 0.5f) AddCut(digest, run(t, At(x, z, y: y)));
            return digest.Value;
        }

        internal static LayerCutResult OldSee(ViewMap t, ViewEntity me) => LayerCut.Run(t.Plates, t, me, Forward, 5f);

        [Test]
        public void OldSilhouettesCutAndRingMatchTheRecordedBits()
        {
            ulong s = SilhouetteRun(OldSilhouettes), c = CutRun(OldCutAt), z = SeeRun(OldSee);
            Assert.That((s, c, z), Is.EqualTo((SilhouetteDigest, CutDigest, SeeDigest)), s + " " + c + " " + z);
        }
    }
}
