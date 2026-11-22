using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.See1Field;
using static WarCore.Tests.Stance.Stance1Worlds;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // SCALE-2a S1-S4, S6-S8, S10, S12 (the headless half; the Unity half is Scale2aViewsTests): one source of a unit's size
    // (BodySizes), read by the pick, the silhouettes, the layer cut and the see-through ring, the drawn-parts table, the
    // model fit, the shot heights and the lineup world. Heights are compared with raw / 65536f of the pack's own values.
    public sealed class Scale2aSizesTests
    {
        private const float Eps = 1e-5f;

        private static CombatPack StancePack() => RwrPack.Load("rwr-stance").BuildCombat();

        private static float M(long raw) => raw / 65536f;

        private static ViewEntity Unit(ulong key, float x, float z, int team, int stance = 0, float y = 0f) =>
            new ViewEntity(key, 0, x, y, z, team: team, stance: stance);

        // S1: the "rwr-stance" body by level: top 1.75/1.0/0.5, eye and muzzle 1.5/0.75/0.25, standing height 1.75 at every
        // level, radius 0.25 (the content constant and a placed unit's collider).
        [Test]
        public void S1_TheStancePacksSizesByLevel()
        {
            CombatPack pack = StancePack();
            BodySizes s = null;
            Assert.DoesNotThrow(() => s = BodySizes.For(pack));
            Assert.That(s, Is.Not.Null);
            Assert.That(s.Levels, Is.EqualTo(3));
            var bodies = new[] { pack.Body.Value, pack.Stances[0].Body, pack.Stances[1].Body };
            var tops = new[] { 1.75f, 1.0f, 0.5f };
            var eyes = new[] { 1.5f, 0.75f, 0.25f };
            for (int level = 0; level < 3; level++)
            {
                ViewEntity e = Unit(9, 0f, 0f, 1, level);
                Assert.That(s.Top(e), Is.EqualTo(M(bodies[level].Height.Raw)), "top " + level);
                Assert.That(s.Eye(e), Is.EqualTo(M(bodies[level].Eye.Raw)), "eye " + level);
                Assert.That(s.Muzzle(e), Is.EqualTo(M(bodies[level].Muzzle.Raw)), "muzzle " + level);
                Assert.That(s.Top(e), Is.EqualTo(tops[level]).Within(1e-4f));
                Assert.That(s.Eye(e), Is.EqualTo(eyes[level]).Within(1e-4f));
                Assert.That(s.Muzzle(e), Is.EqualTo(eyes[level]).Within(1e-4f));
                Assert.That(s.StandingHeight, Is.EqualTo(M(pack.Body.Value.Height.Raw)));
            }
            Assert.That(s.Radius, Is.EqualTo(M(CombatSection.UnitRadiusRaw)));
            Assert.That(s.Radius, Is.EqualTo(0.25f));
            SimSession lineup = SimSession.Create(Scale2aDemo.Recipe(), Scale2aDemo.MapBytes());
            Simulation sim = M3TestWorld.Sim(lineup);
            Assert.That(sim.Entities.Read(sim.Entities.Resolve<Collider>(), M3TestWorld.Unit(lineup, 1)).Radius.Raw, Is.EqualTo(BodySizes.RadiusRaw),
                "a placed unit's collider");
            Assert.That(BodySizes.Of(lineup), Is.SameAs(BodySizes.Of(lineup)), "one instance per pack");
            Assert.That(BodySizes.Of(lineup).Top(Unit(1, 0, 0, 1, 2)), Is.EqualTo(M(pack.Stances[1].Body.Height.Raw)));
        }

        // S1: a pack made in code whose eye and muzzle differ (1.8/1.65/1.45, crouched 1.1/0.9/0.8; none a whole number of raw).
        [Test]
        public void S1_EyeAndMuzzleAreReadApart()
        {
            CombatPack basis = StancePack();
            CombatBody Body(double h, double eye, double muzzle) =>
                new CombatBody(SimFix.FromRaw((long)Math.Round(h * 65536)), SimFix.FromRaw((long)Math.Round(eye * 65536)), SimFix.FromRaw((long)Math.Round(muzzle * 65536)));
            CombatBody stand = Body(1.8, 1.65, 1.45), crouch = Body(1.1, 0.9, 0.8);
            var pack = new CombatPack(basis.Weapons.ToArray(), basis.Resolver, basis.Selector, basis.MaxHealthRaw, body: stand,
                stances: new[] { new StanceDef("crouch", crouch, 500, 700, 6) });
            BodySizes s = BodySizes.For(pack);
            Assert.That(s.Top(Unit(1, 0, 0, 1)), Is.EqualTo(M(stand.Height.Raw)));
            Assert.That(s.Eye(Unit(1, 0, 0, 1)), Is.EqualTo(M(stand.Eye.Raw)));
            Assert.That(s.Muzzle(Unit(1, 0, 0, 1)), Is.EqualTo(M(stand.Muzzle.Raw)));
            Assert.That(s.Top(Unit(1, 0, 0, 1, 1)), Is.EqualTo(M(crouch.Height.Raw)));
            Assert.That(s.Eye(Unit(1, 0, 0, 1, 1)), Is.EqualTo(M(crouch.Eye.Raw)));
            Assert.That(s.Muzzle(Unit(1, 0, 0, 1, 1)), Is.EqualTo(M(crouch.Muzzle.Raw)));
            Assert.That(s.StandingHeight, Is.EqualTo(M(stand.Height.Raw)));
            Assert.That(M(stand.Eye.Raw), Is.Not.EqualTo(M(stand.Muzzle.Raw)));
        }

        // S1: level -1 and the level count throw, the last level does not.
        [Test]
        public void S1_LevelsOutsideThePackThrow()
        {
            BodySizes s = BodySizes.For(StancePack());
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Top(Unit(1, 0, 0, 1, -1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Eye(Unit(1, 0, 0, 1, 3)));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Muzzle(Unit(1, 0, 0, 1, 3)));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Top(Unit(1, 0, 0, 1, 3)));
            Assert.DoesNotThrow(() => s.Top(Unit(1, 0, 0, 1, 2)));
            Assert.DoesNotThrow(() => s.Top(Unit(1, 0, 0, 1, 0)));
            BodySizes standing = BodySizes.For(RwrPack.Load("rwr-field").BuildCombat());
            Assert.That(standing.Levels, Is.EqualTo(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => standing.Top(Unit(1, 0, 0, 1, 1)));
        }

        // S1: no body (rwr-m6, ancient-field, rwr-terrain-demo, the showcase's house, terrain and melee stages): null.
        [Test]
        public void S1_PacksWithoutABodyHaveNoSizes()
        {
            BodySizes Read(Func<BodySizes> read)
            {
                BodySizes got = null;
                Assert.DoesNotThrow(() => got = read());
                return got;
            }
            Assert.That(Read(() => BodySizes.For(RwrPack.Load("rwr-m6").BuildCombat())), Is.Null);
            Assert.That(Read(() => BodySizes.For(RwrPack.Load("rwr-terrain-demo").BuildCombat())), Is.Null);
            Assert.That(Read(() => BodySizes.For(ContentLoader.Load(new FileContentSource("Assets/WarCore/Content/Ancient/Data"), "ancient-field").BuildCombat())), Is.Null);
            foreach (ShowcaseStage stage in Showcase.Stages.Where(s => s.Id != "house").Append(Showcase.LegacyHouse)) // AIM-2: the old house
            {
                SimSession session = stage.Start();
                if (stage.Id == "house" || stage.Id == "terrain" || stage.Id == "melee")
                    Assert.That(Read(() => BodySizes.Of(session)), Is.Null, stage.Id);
                else Assert.That(Read(() => BodySizes.Of(session)), Is.Not.Null, stage.Id);
            }
        }

        // S1: a world without combat has none either.
        [Test]
        public void S1_AWorldWithoutCombatHasNoSizes()
        {
            WorldRecipe r = Scale2aDemo.Recipe();
            var bare = new WorldRecipe(r.Seed, r.Mover, r.Units.ToArray().Select(u => new UnitSpawn(u.Key, u.Archetype, u.Squad, u.XMm, u.ZMm, u.SpeedMmPerSec)).ToArray(),
                new BridgeCommand[0]);
            SimSession s = null;
            Assert.DoesNotThrow(() => s = SimSession.Create(bare, Scale2aDemo.MapBytes()));
            BodySizes none = BodySizes.For(StancePack());
            Assert.DoesNotThrow(() => none = BodySizes.Of(s));
            Assert.That(none, Is.Null);
            Assert.Throws<ArgumentNullException>(() => BodySizes.Of(null));
        }

        // S2: the stance-wall replay, ticks 55..75: every unit's Top and Eye are its level's body; squad 2's crouch is picked at
        // 1.75 m until the tick it is due and at 1.0 m from then on.
        [Test]
        public void S2_SizesFollowTheStanceTickByTick()
        {
            SimSession s = WorldBuilder.Build(StanceDemo.Recipe(), StanceDemo.MapBytes(), false);
            CombatPack pack = StancePack();
            var bodies = new[] { pack.Body.Value, pack.Stances[0].Body, pack.Stances[1].Body };
            BodySizes sizes = BodySizes.Of(s);
            long due = -1;
            var seen = new List<(long Tick, bool Low, bool High)>();
            while (s.Tick < 75)
            {
                s.StepOnce();
                if (due < 0 && s.StanceOf(5).Target == 1) due = s.StanceOf(5).Due;
                if (s.Tick < 55) continue;
                FrameView frame = s.Current;
                foreach (ViewEntity e in frame.Entities)
                {
                    int level = s.StanceOf(e.Key).Level;
                    Assert.That(e.Stance, Is.EqualTo(level), "unit " + e.Key + " at " + s.Tick);
                    Assert.That(sizes.Top(e), Is.EqualTo(M(bodies[level].Height.Raw)), "unit " + e.Key + " at " + s.Tick);
                    Assert.That(sizes.Eye(e), Is.EqualTo(M(bodies[level].Eye.Raw)), "unit " + e.Key + " at " + s.Tick);
                }
                Assert.That(frame.TryGet(5, out ViewEntity crouching), Is.True);
                float top = sizes.Top(crouching);
                AimTarget Ray(float h) => AimPick.PickRay(crouching.X - 1.5f, h + 0.05f, crouching.Z, 1f, -0.05f, 0f, 0f, null, frame, 1, null, null, sizes);
                seen.Add((s.Tick, Ray(top - 0.01f).UnitKey == 5, Ray(top + 0.06f).Kind == AimKind.Unit));
                Assert.That(Ray(top - 0.01f).UnitKey, Is.EqualTo(5UL), "just under the top at " + s.Tick);
                Assert.That(Ray(top + 0.06f).Kind, Is.Not.EqualTo(AimKind.Unit), "over the top at " + s.Tick);
                if (s.Tick == due - 1) Assert.That(top, Is.EqualTo(M(bodies[0].Height.Raw)), "the last tick before it is due");
                if (s.Tick == due) Assert.That(top, Is.EqualTo(M(bodies[1].Height.Raw)), "the tick it is due");
            }
            Assert.That(due, Is.InRange(56L, 75L), "the crouch falls due inside the window");
            Assert.That(seen.Count, Is.EqualTo(21));
        }

        private static FrameView Shown(params ViewEntity[] units) => new FrameView(1, units);

        // S3(b): from (7, 3.25, 0) along (1, -1, 0) the ray enters the cylinder of a standing enemy at (9, 0, 0) at t 1.5 and
        // y 1.75, exactly its top: picked.
        [Test]
        public void S3b_ARayEnteringAtTheTopIsPicked()
        {
            BodySizes sizes = BodySizes.For(StancePack());
            FrameView shown = Shown(Unit(1, 0, -20, 1), Unit(2, 9, 0, 2));
            AimTarget a = AimPick.PickRay(7f, 3.25f, 0f, 1f, -1f, 0f, 0f, null, shown, 1, null, null, sizes);
            Assert.That(a.Kind, Is.EqualTo(AimKind.Unit));
            Assert.That(a.UnitKey, Is.EqualTo(2UL));
        }

        // S3(d): a camera turned 30 degrees and tilted 60; its ray through a point h up the enemy's axis. Standing (key 2) at
        // h 1.9: picked with sizes (through the top cap), not without; crouched (key 3) at h 1.9: not, at 1.4: picked.
        [Test]
        public void S3d_ARotatedTiltedCameraPicksByTheStanceHeight()
        {
            BodySizes sizes = BodySizes.For(StancePack());
            var f = new IsoCamera(10f, 0f, 30f, 60f).Forward;
            FrameView shown = Shown(Unit(1, 0, -20, 1), Unit(2, 10, 0, 2), Unit(3, 10, 10, 2, stance: 1));
            AimTarget Through(float x, float z, float h, BodySizes s) =>
                AimPick.PickRay(x - 30 * f.X, h - 30 * f.Y, z - 30 * f.Z, f.X, f.Y, f.Z, 0f, null, shown, 1, null, null, s);
            AimTarget standing = Through(10, 0, 1.9f, sizes);
            Assert.That((standing.Kind, standing.UnitKey), Is.EqualTo((AimKind.Unit, 2UL)));
            Assert.That(Through(10, 0, 1.9f, null).Kind, Is.EqualTo(AimKind.Plane), "the old 1 m body is missed");
            Assert.That(Through(10, 10, 1.9f, sizes).Kind, Is.EqualTo(AimKind.Plane), "over the crouched head");
            AimTarget crouched = Through(10, 10, 1.4f, sizes);
            Assert.That((crouched.Kind, crouched.UnitKey), Is.EqualTo((AimKind.Unit, 3UL)));
        }

        // S3(g): straight down from 5 m where the circles of a standing (key 5) and a crouched (key 3) enemy overlap (0.4 m
        // apart): with sizes the standing one's higher top is met first; the old equal tops tie and the lower key wins.
        [Test]
        public void S3g_StraightDownMeetsTheHigherTopFirst()
        {
            BodySizes sizes = BodySizes.For(StancePack());
            FrameView shown = Shown(Unit(1, 0, -20, 1), Unit(5, 9f, 0, 2), Unit(3, 9.4f, 0, 2, stance: 1));
            Assert.That(AimPick.PickRay(9.2f, 5f, 0f, 0f, -1f, 0f, 0f, null, shown, 1, null, null, sizes).UnitKey, Is.EqualTo(5UL));
            Assert.That(AimPick.PickRay(9.2f, 5f, 0f, 0f, -1f, 0f, 0f, null, shown, 1).UnitKey, Is.EqualTo(3UL));
        }

        // S3(e): the old path is the recorded bits whether sizes are left out or passed as null.
        [Test]
        public void S3e_NullSizesAreTheOldPickBitForBit()
        {
            var forNull = new Scale2aGoldenTests.Fnv();
            var forNone = new Scale2aGoldenTests.Fnv();
            foreach (float y in new[] { 0.1f, 1.1f, 0.37f, 5.3f })
                for (int g = 0; g < 3; g++)
                {
                    FrameView shown = Shown(Unit(1, 0f, 0f, 1, y: y), Unit(2, 9f, 0f, 2, y: y));
                    Scale2aGoldenTests.Sweep(y, g, (ox, oy, oz, fx, fy, fz) =>
                    {
                        AimTarget a = AimPick.PickRay(ox, oy, oz, fx, fy, fz, 0f, null, shown, 1, null, null, null);
                        AimTarget b = Scale2aGoldenTests.OldPick(shown, ox, oy, oz, fx, fy, fz);
                        forNull.Add(a);
                        forNone.Add(b);
                        Assert.That((a.Kind, a.UnitKey, a.X, a.Y, a.Z, a.YMm), Is.EqualTo((b.Kind, b.UnitKey, b.X, b.Y, b.Z, b.YMm)));
                    });
                }
            Assert.That(forNull.Value, Is.EqualTo(forNone.Value));
            Assert.That(forNull.Count, Is.GreaterThan(1000));
        }

        // S3(f): the showcase's house and terrain stages have no body, so the hosts' BodySizes.Of(session) is null and their
        // input is the recorded sequence.
        [TestCase("terrain", 2454987396881980250UL)]
        [TestCase("house", 9975092720828191415UL)]
        public void S3f_InputOfAStageWithoutABodyIsTheRecordedSequence(string id, ulong recorded)
        {
            ulong got = Scale2aGoldenTests.InputDigest(id,
                (s, camera, x, y, cut) => AimPick.Pick(camera, x, y, 1280, 720, ViewGround.For(s.Map), s.Current, s.ControlledKey, s.Map.Plates, cut, BodySizes.Of(s)),
                (s, me, forward) => LayerCut.Run(s.Map.Plates, s.Map, me, forward, LayerCut.SeeRadius, BodySizes.Of(s)));
            Assert.That(got, Is.EqualTo(recorded));
        }

        // S4: a team mate right behind a 1.6 m wall, the camera low (pitch 20, looking +z): its head line rises 0.09 m over
        // the 0.25 m to the wall. Without sizes (head 1 m) and crouched (1.0) covered; standing (1.75) not.
        [Test]
        public void S4_SilhouettesTakeTheHeadFromTheStance()
        {
            BodySizes sizes = BodySizes.For(StancePack());
            ViewMap map = Map((10, 10, 20, 1, 1.6));
            float p = 20f * MathF.PI / 180f;
            (float X, float Y, float Z) forward = (0f, -MathF.Sin(p), MathF.Cos(p));
            ViewEntity me = At(60, 40);
            HashSet<ulong> Covered(int stance, BodySizes s)
            {
                var units = new[] { me, Unit(2, 20f, 11.25f, 1, stance) };
                return Silhouettes.Run(map.Plates, map, LayerCut.Run(map.Plates, map, me, forward, 0f, s), me, units, forward, s);
            }
            Assert.That(Covered(0, null), Is.EquivalentTo(new ulong[] { 2 }), "the old 1 m head");
            Assert.That(Covered(0, sizes), Is.Empty, "standing, 1.75 m");
            Assert.That(Covered(1, sizes), Is.EquivalentTo(new ulong[] { 2 }), "crouched, 1.0 m");
            float eyeLine = 1.5f + 0.25f * MathF.Tan(p);
            Assert.That(eyeLine, Is.LessThan(1.6f), "an eye-height line would still be covered: the head is what counts");
        }

        // S4: a raised wall (3.1 to 4.1 m) 1 m behind the player along the view (pitch 45, looking +z): only the line back from
        // 1.75 m crosses it (at 2.75..3.75 m), not the one from 1.0 m (2.0..3.0) or the feet. Faded with sizes, not without.
        [Test]
        public void S4_TheCutDrawsItsHeadLineFromTheStance()
        {
            BodySizes sizes = BodySizes.For(StancePack());
            ViewMap map = Raised(10, 10, 20, 1, 3.1, 1.0);
            (float X, float Y, float Z) forward = (0f, -MathF.Sqrt(0.5f), MathF.Sqrt(0.5f));
            ViewEntity me = At(20.5f, 12f);
            Assert.That(LayerCut.Run(map.Plates, map, me, forward, 0f, sizes).Wall(0), Is.EqualTo(CutState.Faded));
            Assert.That(LayerCut.Run(map.Plates, map, me, forward).Wall(0), Is.EqualTo(CutState.Shown));
            Assert.That(LayerCut.Run(map.Plates, map, me, forward, 0f, null).Wall(0), Is.EqualTo(CutState.Shown));
            Assert.That(LayerCut.Run(map.Plates, map, At(20.5f, 12f), forward, 0f, sizes).Wall(0), Is.EqualTo(CutState.Faded));
            ViewEntity crouched = new ViewEntity(1, 0, 20.5f, 0f, 12f, team: 1, stance: 1);
            Assert.That(LayerCut.Run(map.Plates, map, crouched, forward, 0f, sizes).Wall(0), Is.EqualTo(CutState.Shown), "crouched, 1.0 m");
        }

        // S4: the see-through ring. Looking +z (pitch 45), the outer sample 5 m toward -z lies behind a 0.9 m wall (z 26..27);
        // from that sample alone the line back crosses a 3 m wall (z 24..25). The eye at 1.5 (standing) and 1.0 (no sizes)
        // sees the sample over the low wall, so the 3 m wall fades; the crouched eye at 0.75 does not, so it stays. Over a 1.2 m
        // low wall only the standing eye sees.
        [Test]
        public void S4_TheRingLooksFromTheStancesEye()
        {
            BodySizes sizes = BodySizes.For(StancePack());
            ViewMap map = Map((20, 26, 1, 1, 0.9), (20, 24, 1, 1, 3.0));
            (float X, float Y, float Z) forward = (0f, -MathF.Sqrt(0.5f), MathF.Sqrt(0.5f));
            ViewEntity Me(int stance) => new ViewEntity(1, 0, 20.5f, 0f, 30.5f, team: 1, stance: stance);
            Assert.That(LayerCut.Run(map.Plates, map, Me(0), forward, 0f, sizes).Wall(1), Is.EqualTo(CutState.Shown), "not by the player's own lines");
            Assert.That(LayerCut.Run(map.Plates, map, Me(0), forward, 5f, sizes).Wall(1), Is.EqualTo(CutState.Faded), "standing eye 1.5");
            Assert.That(LayerCut.Run(map.Plates, map, Me(1), forward, 5f, sizes).Wall(1), Is.EqualTo(CutState.Shown), "crouched eye 0.75");
            Assert.That(LayerCut.Run(map.Plates, map, Me(0), forward, 5f).Wall(1), Is.EqualTo(CutState.Faded), "no sizes: eye 1.0");
            Assert.That(LayerCut.Run(map.Plates, map, Me(1), forward, 5f, null).Wall(1), Is.EqualTo(CutState.Faded), "no sizes: eye 1.0");
            // A 1.2 m low wall instead: the standing eye (1.5) still sees over it, the old eye (1.0) does not.
            ViewMap higher = Map((20, 26, 1, 1, 1.2), (20, 24, 1, 1, 3.0));
            Assert.That(LayerCut.Run(higher.Plates, higher, Me(0), forward, 5f, sizes).Wall(1), Is.EqualTo(CutState.Faded), "standing eye 1.5 over 1.2");
            Assert.That(LayerCut.Run(higher.Plates, higher, Me(0), forward, 5f).Wall(1), Is.EqualTo(CutState.Shown), "no sizes: eye 1.0 under 1.2");
        }

        // S4: with sizes passed as null the silhouettes, the cut and the ring are the recorded bits.
        [Test]
        public void S4_NullSizesAreTheOldBitsForBit()
        {
            ulong s = Scale2aGoldenTests.SilhouetteRun((t, cut, me, units) => Silhouettes.Run(t.Plates, t, cut, me, units, Forward, null));
            ulong c = Scale2aGoldenTests.CutRun((plates, m, me) => LayerCut.Run(plates, m, me, Forward, 0f, null));
            ulong z = Scale2aGoldenTests.SeeRun((t, me) => LayerCut.Run(t.Plates, t, me, Forward, 5f, null));
            Assert.That((s, c, z), Is.EqualTo((9504576269491795843UL, 11946379032752552359UL, 10214496898857319079UL)));
        }

        // S6: the drawn-parts table (the final shares are the dispatch's starting values).
        [Test]
        public void S6_UprightPartsFollowTheTable()
        {
            float h = 1.75f, r = 0.25f;
            DrawnBody b = default;
            Assert.DoesNotThrow(() => b = DrawnParts.Upright(h, r, 1.75f, 1.5f));
            Assert.That(b.Lying, Is.False);
            Assert.That(b.Head.TopY, Is.EqualTo(1.75f).Within(Eps), "head top = Top");
            Assert.That(b.Gun.Y, Is.EqualTo(1.5f).Within(Eps), "gun at Muzzle");
            Assert.That(b.Blade.Y, Is.EqualTo(1.5f).Within(Eps), "blade at Muzzle");
            Assert.That(b.Highest, Is.EqualTo(1.75f).Within(Eps));
            Assert.That(b.Torso.SizeX, Is.EqualTo(0.5f).Within(Eps), "torso 2r wide");
            Assert.That(b.Torso.SizeZ, Is.EqualTo(0.28f).Within(Eps));
            Assert.That(b.Torso.Bottom, Is.EqualTo(0f).Within(Eps), "torso from the feet");
            Assert.That(b.Torso.TopY, Is.EqualTo(b.Head.Bottom).Within(Eps), "to the head's bottom");
            Assert.That(b.Head.SizeY, Is.EqualTo(0.245f).Within(Eps));
            Assert.That(b.Gun.SizeZ, Is.EqualTo(0.84f).Within(Eps));
            Assert.That(b.Blade.SizeZ, Is.EqualTo(0.875f).Within(Eps));
            Assert.That(b.Shield.SizeY, Is.EqualTo(0.875f).Within(Eps));
            Assert.That(b.Shield.SizeX, Is.EqualTo(0.6125f).Within(Eps));
            Assert.That(b.Shield.Y, Is.EqualTo(1.5f - 0.4375f).Within(Eps), "a quarter H under the muzzle");
            Assert.That(b.Pack.SizeY, Is.EqualTo(0.525f).Within(Eps));
            Assert.That(b.Pack.SizeX, Is.EqualTo(0.35f).Within(Eps));
            Assert.That(b.Pack.SizeZ, Is.EqualTo(0.21f).Within(Eps));
            Assert.That(b.Pack.TopY, Is.EqualTo(b.Head.Bottom).Within(Eps), "pack top under the shoulder");
            Assert.That(b.Pack.Z + b.Pack.SizeZ / 2, Is.EqualTo(-b.Torso.SizeZ / 2).Within(Eps), "on the back");
            Assert.That(b.NerveMark.SizeY, Is.EqualTo(0.28f).Within(Eps));
            Assert.That(b.NerveMark.Y, Is.EqualTo(1.75f + 0.35f).Within(Eps));
            Assert.That(b.LeaderMark.SizeY, Is.EqualTo(0.2975f).Within(Eps));
            Assert.That(b.LeaderMark.Y, Is.EqualTo(1.75f + 0.805f).Within(Eps));
            Assert.That(b.RingRadius, Is.EqualTo(0.7f).Within(Eps));
            Assert.That(DrawnParts.ChipsHeight(1.75f), Is.EqualTo(1.1025f).Within(Eps));
            Assert.That(DrawnParts.GunLength(1.75f), Is.EqualTo(0.84f).Within(Eps));
        }

        // S6: H x 1.1 multiplies every part's size by 1.1 but the torso's width (2r); positions stay on Top and Muzzle.
        [Test]
        public void S6_PartsScaleWithTheStandingHeightButTheTorsoWidth()
        {
            DrawnBody a = DrawnParts.Upright(1.75f, 0.25f, 1.75f, 1.5f), b = DrawnParts.Upright(1.75f * 1.1f, 0.25f, 1.75f, 1.5f);
            Func<DrawnBody, DrawnPart>[] parts = { d => d.Head, d => d.Gun, d => d.Blade, d => d.Shield, d => d.Pack, d => d.NerveMark, d => d.LeaderMark };
            foreach (var part in parts)
            {
                Assert.That(part(b).SizeX, Is.EqualTo(part(a).SizeX * 1.1f).Within(Eps));
                Assert.That(part(b).SizeY, Is.EqualTo(part(a).SizeY * 1.1f).Within(Eps));
                Assert.That(part(b).SizeZ, Is.EqualTo(part(a).SizeZ * 1.1f).Within(Eps));
            }
            Assert.That(b.Torso.SizeZ, Is.EqualTo(a.Torso.SizeZ * 1.1f).Within(Eps));
            Assert.That(b.Torso.SizeX, Is.EqualTo(a.Torso.SizeX).Within(Eps), "2r, not H");
            Assert.That(b.RingRadius, Is.EqualTo(a.RingRadius * 1.1f).Within(Eps));
            Assert.That(b.Head.TopY, Is.EqualTo(1.75f).Within(Eps));
            Assert.That(b.Gun.Y, Is.EqualTo(1.5f).Within(Eps));
        }

        // S6: crouched: the same part sizes as standing (sized by H), the head's top at 1.0, the gun at 0.75.
        [Test]
        public void S6_CrouchedIsTheSamePartsLower()
        {
            BodySizes s = BodySizes.For(StancePack());
            ViewEntity standing = Unit(1, 0, 0, 1), crouched = Unit(1, 0, 0, 1, 1);
            DrawnBody up = DrawnParts.For(s.StandingHeight, s.Radius, s.Top(standing), s.Muzzle(standing));
            DrawnBody low = DrawnParts.For(s.StandingHeight, s.Radius, s.Top(crouched), s.Muzzle(crouched));
            Assert.That(low.Lying, Is.False);
            Assert.That(low.Head.TopY, Is.EqualTo(1.0f).Within(1e-4f));
            Assert.That(low.Gun.Y, Is.EqualTo(0.75f).Within(1e-4f));
            Assert.That(low.Highest, Is.EqualTo(1.0f).Within(1e-4f));
            foreach (var part in new Func<DrawnBody, DrawnPart>[] { d => d.Head, d => d.Gun, d => d.Blade, d => d.Pack, d => d.Shield })
                Assert.That((part(low).SizeX, part(low).SizeY, part(low).SizeZ), Is.EqualTo((part(up).SizeX, part(up).SizeY, part(up).SizeZ)));
        }

        // S6: prone: lying along z, 1.75 long, nothing (pack included) higher than the stance's top 0.5.
        [Test]
        public void S6_ProneLiesFlatUnderItsTop()
        {
            BodySizes s = BodySizes.For(StancePack());
            ViewEntity prone = Unit(1, 0, 0, 1, 2);
            DrawnBody d = DrawnParts.For(s.StandingHeight, s.Radius, s.Top(prone), s.Muzzle(prone));
            Assert.That(d.Lying, Is.True);
            Assert.That(d.Highest, Is.LessThanOrEqualTo(s.Top(prone)));
            Assert.That(d.Pack.TopY, Is.LessThanOrEqualTo(0.5f));
            Assert.That(d.Length, Is.EqualTo(1.75f).Within(1e-4f));
            Assert.That(d.Head.Z, Is.GreaterThan(0f), "the head ahead of the feet's point");
            Assert.That(d.Torso.Z - d.Torso.SizeZ / 2, Is.LessThan(0f).And.GreaterThan(-1.75f));
            DrawnBody low = DrawnParts.Lying(1.75f, 0.25f, 0.3f, 0.2f);
            Assert.That(low.Highest, Is.LessThanOrEqualTo(0.3f), "a lower prone top squeezes the pose");
            Assert.That(low.Length, Is.EqualTo(1.75f).Within(1e-4f));
        }

        // S6: what the table refuses.
        [Test]
        public void S6_TheTableRefusesBadBodies()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => DrawnParts.Upright(0f, 0.25f, 1f, 0.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => DrawnParts.Upright(1.75f, 0f, 1f, 0.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => DrawnParts.Upright(1.75f, 0.25f, 2f, 0.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => DrawnParts.Lying(1.75f, 0.25f, 0.5f, 0.6f));
            Assert.Throws<ArgumentOutOfRangeException>(() => DrawnParts.Upright(float.NaN, 0.25f, 1f, 0.5f));
        }

        // S7: a model 2.0 tall fits a 1.75 m body at 0.875; equal heights at 1; 0, negative, NaN and infinity are refused.
        [Test]
        public void S7_ModelFit()
        {
            float fit = 0f;
            Assert.DoesNotThrow(() => fit = DrawnParts.ModelFit(2f, 1.75f));
            Assert.That(fit, Is.EqualTo(0.875f).Within(1e-6f));
            Assert.That(DrawnParts.ModelFit(1.75f, 1.75f), Is.EqualTo(1f));
            foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => DrawnParts.ModelFit(bad, 1.75f), "model " + bad);
                Assert.Throws<ArgumentOutOfRangeException>(() => DrawnParts.ModelFit(2f, bad), "body " + bad);
            }
        }

        // S8: the crouch-fire world. Every shot's view heights are its record's start and end; a crouched shooter fires from
        // 0.75, a standing one at a crouched target from 1.5, and some shot changes height on the way.
        [Test]
        public void S8_ShotHeightsAreTheSimulationsStartAndEnd()
        {
            SimSession s = WorldBuilder.Build(Scale2aDemo.CrouchFire(), Scale2aDemo.FireMapBytes(), false);
            CombatSystem combat = CombatOf(s);
            int shots = 0;
            bool crouchedShooter = false, standingAtCrouched = false, slanted = false;
            for (int i = 0; i < 300 && !s.IsFinished; i++)
            {
                s.StepOnce();
                ReadOnlySpan<ViewFire> view = s.Current.Fires;
                Assert.That(view.Length, Is.EqualTo(combat.Fires.Length));
                for (int k = 0; k < view.Length; k++)
                {
                    FireRecord f = combat.Fires[k];
                    ViewFire v = SimSession.ToView(f);
                    Assert.That((v.Y0, v.Y1), Is.EqualTo(((float)f.Origin.Y.Raw / SimFix.Scale, (float)f.End.Y.Raw / SimFix.Scale)));
                    Assert.That((view[k].Y0, view[k].Y1, view[k].X0, view[k].Z1), Is.EqualTo((v.Y0, v.Y1, v.X0, v.Z1)));
                    shots++;
                    if (s.Tick > 10 && v.Team == 2 && v.Y0 == 0.75f) crouchedShooter = true;
                    if (s.Tick > 10 && v.Team == 1 && v.Y0 == 1.5f) standingAtCrouched = true;
                    if (v.Y0 != v.Y1) slanted = true;
                }
            }
            Assert.That(shots, Is.GreaterThan(4));
            Assert.That(crouchedShooter, Is.True, "a crouched shooter fires from 0.75 m");
            Assert.That(standingAtCrouched, Is.True, "a standing shooter fires from 1.5 m");
            Assert.That(slanted, Is.True, "a shot from 1.5 m at a crouched eye (0.75 m) slants");
            ViewFire six = new ViewFire(1f, 2f, 3f, 4f, 1, true);
            Assert.That((six.Y0, six.Y1), Is.EqualTo((0f, 0f)), "the six-argument constructor");
        }

        // S8: a drawn shot never ends below the ground (the flat floor here).
        [Test]
        public void S8_ADrawnShotEndsNoLowerThanTheGround()
        {
            Assert.That(DrawnParts.ShotEndY(-0.3f, null, 0f, 5f, 5f), Is.EqualTo(0f));
            Assert.That(DrawnParts.ShotEndY(-0.3f, null, 2.5f, 5f, 5f), Is.EqualTo(2.5f));
            Assert.That(DrawnParts.ShotEndY(0.4f, null, 0f, 5f, 5f), Is.EqualTo(0.4f));
            Assert.That(DrawnParts.ShotEndY(3f, null, 2.5f, 5f, 5f), Is.EqualTo(3f));
        }

        // S10: one source. Outside AimPick and BodySizes nothing in Presentation or Unity names AimPick.BodyHeight (comments
        // stripped); the showcase files no longer hold the sizes moved to ShowcaseLegacySizes and DrawnParts.
        [Test]
        public void S10_TheOldHeightLivesInOnePlace()
        {
            string root = "Assets/WarCore";
            var files = Directory.GetFiles(Path.Combine(root, "Presentation"), "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(Path.Combine(root, "Unity"), "*.cs", SearchOption.AllDirectories))
                .Where(f => !f.Replace('\\', '/').Contains("/Tests/"));
            var named = files.Where(f => SourceRules.StripComments(File.ReadAllText(f)).Contains("AimPick.BodyHeight"))
                .Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.That(named, Is.EqualTo(new[] { "BodySizes.cs" }), "AimPick.cs names its own constant without the class");
            var moved = new Dictionary<string, string[]>
            {
                ["ShowcaseUnits.cs"] = new[] { "1.7f", "0.62f", "1.25f", "1.3f", "1.05f", "0.12f", "0.28f", "BodyHeight + 0.8f", "BodyHeight + 0.35f", "RingRadius = 0.7f" },
                ["ShowcaseShots.cs"] = new[] { "1.4f", "1.1f", "0.9f" },
                ["ShowcaseScene.cs"] = new[] { "1.5f", "1.1f" },
            };
            foreach (var pair in moved)
            {
                string code = SourceRules.StripComments(File.ReadAllText(Path.Combine(root, "Unity", pair.Key)));
                foreach (string literal in pair.Value) Assert.That(code, Does.Not.Contain(literal), pair.Key);
            }
        }

        // S12: the lineup world: at tick 0 all three stand (1.75); from tick 150 on standing, crouched, prone (1.75, 1.0, 0.5).
        [Test]
        public void S12_TheLineupStandsCrouchesAndLies()
        {
            SimSession s = null;
            Assert.DoesNotThrow(() => s = SimSession.Create(Scale2aDemo.Recipe(), Scale2aDemo.MapBytes()));
            BodySizes sizes = BodySizes.Of(s);
            void Check(long tick, int[] levels, float[] tops)
            {
                while (s.Tick < tick) s.StepOnce();
                for (int k = 0; k < 3; k++)
                {
                    Assert.That(s.Current.TryGet((ulong)k + 1, out ViewEntity e), Is.True);
                    Assert.That(e.Stance, Is.EqualTo(levels[k]), "unit " + (k + 1) + " at " + tick);
                    Assert.That(sizes.Top(e), Is.EqualTo(tops[k]).Within(1e-4f), "unit " + (k + 1) + " at " + tick);
                    Assert.That(e.Alive, Is.True);
                }
            }
            Check(0, new[] { 0, 0, 0 }, new[] { 1.75f, 1.75f, 1.75f });
            foreach (long tick in new long[] { 150, 300, 600 }) Check(tick, new[] { 0, 1, 2 }, new[] { 1.75f, 1.0f, 0.5f });
            Assert.That(s.Current.Fires.Length, Is.EqualTo(0), "nobody fires");
            ViewMap map = s.Map;
            Assert.That(map.Walls.Length, Is.EqualTo(7));
            Assert.That(map.Walls.ToArray().Min(w => w.CenterY - w.SizeY / 2 > 0.5f ? w.CenterY - w.SizeY / 2 : 99f), Is.EqualTo(2f).Within(1e-4f), "2.0 m clear");
            Assert.That(map.Walls.ToArray().Count(w => Math.Abs(w.CenterY + w.SizeY / 2 - 58982 / 65536f) < 1e-4f), Is.EqualTo(1), "a 0.9 m sill");
            Assert.That(Scale2aDemo.Stage().Start().Tick, Is.EqualTo(0));
        }
    }
}
