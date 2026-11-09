using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // A4 acceptance 3 (input contract and replay) and 7 (mapper with the aim overload, appendix K3).
    public sealed class A4InputTests
    {
        private const long Limit = 16384L * 1000;

        [Test]
        public void A4Input_HeightIsOptionalAndPartOfEquality()
        {
            Assert.That(new PlayerInput(0, 0, 1, 2).AimYMm, Is.Null);
            var none = new PlayerInput(0, 0, 1, 2, true);
            var zero = new PlayerInput(0, 0, 1, 2, true, aimYMm: 0);
            var one = new PlayerInput(0, 0, 1, 2, true, aimYMm: 1);
            Assert.That(none.Equals(zero), Is.False); Assert.That(zero.Equals(one), Is.False); Assert.That(none.Equals(one), Is.False);
            var zero2 = new PlayerInput(0, 0, 1, 2, true, aimYMm: 0);
            Assert.That(zero.Equals(zero2), Is.True);
            Assert.That(zero.GetHashCode(), Is.EqualTo(zero2.GetHashCode()));
            Assert.That(none.Equals(new PlayerInput(0, 0, 1, 2, true)), Is.True);
        }

        [TestCase(Limit + 1)]
        [TestCase(-Limit - 1)]
        public void A4Input_HeightOutsideTheWorld_Throws(long yMm)
        {
            var e = Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerInput(0, 0, 0, 0, aimYMm: yMm));
            Assert.That(e.ParamName, Is.EqualTo("aimYMm"));
        }

        [TestCase(Limit)]
        [TestCase(-Limit)]
        public void A4Input_HeightAtTheWorldLimit_IsAccepted(long yMm) =>
            Assert.That(new PlayerInput(0, 0, 0, 0, aimYMm: yMm).AimYMm, Is.EqualTo(yMm));

        // ZeroResolver (Wp18TestWorld's default pack): nobody dies, so the match never ends during the run.
        private static M6World PlayerWorld() =>
            new M6World(Wp18TestWorld.Recipe(null, new[] {
                Wp16TestWorld.U(1, 20000, 2000, weapon: 0), Wp16TestWorld.U(2, 22000, 2000, team: 2, squad: 2) }), DemoRecipes.M2bMapBytes());

        // The synthetic click copies the height: both the pressed tick and the release tick carry it (PlayerLog only records an
        // input when it is consumed, SimSession.cs:339-341, so the session is stepped past both).
        [Test]
        public void A4Input_LiveClickCopiesTheHeight()
        {
            var w = PlayerWorld();
            var live = new LivePlayerInput();
            live.Apply(w.Session, new PlayerInput(0, 0, 22000, 2000, false, aimYMm: 200), true);
            w.Session.StepOnce(); w.Session.StepOnce();
            var log = w.Session.PlayerLog;
            Assert.That(log.Count, Is.EqualTo(2));
            Assert.That(log[0].Input.Primary, Is.True); Assert.That(log[0].Input.AimYMm, Is.EqualTo(200));
            Assert.That(log[1].Input.Primary, Is.False); Assert.That(log[1].Input.AimYMm, Is.EqualTo(200));
        }

        // Seven frames; the ones at ticks 20, 60, 85 and 140 change only the height.
        private static readonly (long tick, long x, long? y)[] Frames =
        {
            (1, 22000, null), (20, 22000, -1500), (41, 23000, -1500), (60, 23000, 0), (85, 23000, 200), (110, 21000, 200), (140, 21000, null),
        };

        private static PlayerInput FrameInput(int i) => new PlayerInput(0, 0, Frames[i].x, 2000, true, aimYMm: Frames[i].y);

        private static List<ulong> Run(M6World w, int ticks)
        {
            var folds = new List<ulong>();
            for (int t = 0; t < ticks; t++)
            {
                w.Session.StepOnce();
                ulong fold = A4.Fnv(14695981039346656037UL, w.Session.StateHash);
                foreach (FireRecord f in w.Combat.Fires)
                {
                    fold = A4.Fnv(fold, (ulong)f.End.X.Raw); fold = A4.Fnv(fold, (ulong)f.End.Y.Raw); fold = A4.Fnv(fold, (ulong)f.End.Z.Raw);
                    fold = A4.Fnv(fold, (ulong)f.Outcome);
                }
                folds.Add(fold);
            }
            return folds;
        }

        [Test]
        public void A4Input_ReplayOfThePlayerLog_IsTickForTickEqual()
        {
            int last = (int)Frames[Frames.Length - 1].tick + 120;
            var original = PlayerWorld();
            for (int i = 0; i < Frames.Length; i++) original.Session.SetPlayerInput(Frames[i].tick, FrameInput(i));
            List<ulong> a = Run(original, last);
            var log = original.Session.PlayerLog;
            Assert.That(log.Count, Is.EqualTo(Frames.Length), "every frame differs from the one before");
            bool negative = false;
            foreach (var r in log) if (r.Input.AimYMm < 0) negative = true;
            Assert.That(negative, Is.True);

            var replay = PlayerWorld();
            foreach (var r in log) replay.Session.SetPlayerInput(r.AtTick, r.Input);
            Assert.That(Run(replay, last), Is.EqualTo(a));

            // One frame's height one millimetre off: that frame's shot ends elsewhere.
            var changed = PlayerWorld();
            foreach (var r in log)
            {
                PlayerInput p = r.Input;
                if (r.AtTick == 85) p = new PlayerInput(p.MoveXMilli, p.MoveZMilli, p.AimXMm, p.AimZMm, p.Primary, p.Block, p.Throw, p.SkillSlot, 201);
                changed.Session.SetPlayerInput(r.AtTick, p);
            }
            List<ulong> c = Run(changed, last);
            for (int t = 0; t < 84; t++) Assert.That(c[t], Is.EqualTo(a[t]), "before the changed frame, tick " + (t + 1));
            bool differs = false;
            for (int t = 84; t < last; t++) if (c[t] != a[t]) differs = true;
            Assert.That(differs, Is.True, "a 1 mm change of the aim height shows from that frame on");
        }

        // ---- Mapper (acceptance 7) ----

        private enum Entry { Old, NewNull }

        private static PlayerInput Call(PlayerInputMapper m, Entry e, IsoCamera camera, float ix, float iy, float x, float y,
            float w, float h, bool inside, bool primary) =>
            e == Entry.Old ? m.Map(camera, ix, iy, x, y, w, h, inside, primary, null)
                : m.Map(camera, ix, iy, x, y, w, h, inside, primary, null, null);

        // The call sequences of F5MapperTests.cs:94-139, copied as literals.
        private static IEnumerable<Func<PlayerInputMapper, Entry, PlayerInput[]>> Sequences()
        {
            foreach (var c in new[] { (0f, 0f), (90f, 0f), (180f, 0f), (270f, 0f) })
            {
                float yaw = c.Item1;
                yield return (m, e) => {
                    var camera = new IsoCamera(10, groundY: 7, yaw: yaw, pitch: 45);
                    camera.Follow(2.25f, 26.75f);
                    return new[] { Call(m, e, camera, 0, 1, 750, 500, 1000, 500, true, true) };
                };
            }
            yield return (m, e) => {
                var camera = new IsoCamera(10); camera.Follow(1.2346f, -2.3456f);
                return new[] { Call(m, e, camera, 0, 1, 500, 250, 1000, 500, true, false), Call(m, e, camera, 0, -1, 500, 250, 1000, 500, true, false) };
            };
            foreach (var v in new[] { (0.1239f, -0.9879f), (-0.0009f, 0.0009f), (100f, 0f), (-100f, 0f), (0f, 100f), (3f, 4f), (1f, 1f) })
            {
                var axes = v;
                yield return (m, e) => new[] { Call(m, e, new IsoCamera(10, yaw: 0), axes.Item1, axes.Item2, 0, 0, 100, 100, false, false) };
            }
            yield return (m, e) => {
                var camera = new IsoCamera(10); var list = new List<PlayerInput>();
                foreach (float rotation in new[] { 0f, 90f, 37f, 180f }) { camera.Rotate(rotation); list.Add(Call(m, e, camera, 0, 1, 50, 50, 100, 100, true, false)); }
                return list.ToArray();
            };
            foreach (var p in new[] { (false, 800f, 600f), (true, 0f, 600f), (true, 800f, 0f), (true, -1f, 600f), (true, 800f, -1f) })
            {
                var pick = p;
                yield return (m, e) => {
                    var camera = new IsoCamera(10);
                    var first = Call(m, e, camera, 0, 0, 520, 380, 800, 600, true, false);
                    camera.Follow(30, 40); camera.Rotate(90);
                    return new[] { first, Call(m, e, camera, 0, 1, -900, 999, pick.Item2, pick.Item3, pick.Item1, true) };
                };
            }
            yield return (m, e) => {
                var camera = new IsoCamera(10, groundY: 30); camera.Follow(1.2346f, -2.3456f);
                var outside = Call(m, e, camera, 0, 0, 0, 0, 800, 600, false, false);
                camera.Follow(-12, 34);
                return new[] { outside, Call(m, e, camera, 0, 0, 0, 0, 0, 0, true, false) };
            };
        }

        [Test]
        public void A4Input_MapperAimNull_IsTheOldOverloadOnEveryF5Sequence()
        {
            int n = 0;
            foreach (var sequence in Sequences())
            {
                PlayerInput[] old = sequence(new PlayerInputMapper(), Entry.Old);
                PlayerInput[] fresh = sequence(new PlayerInputMapper(), Entry.NewNull);
                Assert.That(fresh, Is.EqualTo(old), "sequence " + n);
                foreach (PlayerInput p in fresh) Assert.That(p.AimYMm, Is.Null);
                n++;
            }
            Assert.That(n, Is.EqualTo(19));
        }

        private static AimTarget Ground(float x, float z, long? yMm) => new AimTarget(AimKind.Ground, x, 0.2f, z, yMm, 0);

        // Appendix K3: the height state frame by frame, new and old entries, inside and outside the window kept apart.
        [Test]
        public void A4Input_MapperHeightState_FollowsTheTable()
        {
            var camera = new IsoCamera(10);
            var m = new PlayerInputMapper();
            AimTarget g = Ground(3.5f, 4.25f, 200);
            PlayerInput New(bool inside, AimTarget? aim) => m.Map(camera, 0, 0, 400, 300, 800, 600, inside, true, null, aim);
            PlayerInput Old(bool inside) => m.Map(camera, 0, 0, 400, 300, 800, 600, inside, true, null);

            PlayerInput p = New(true, g);                        // 1
            Assert.That(p.AimXMm, Is.EqualTo(3500)); Assert.That(p.AimZMm, Is.EqualTo(4250)); Assert.That(p.AimYMm, Is.EqualTo(200));
            p = New(false, null);                                // 2
            Assert.That(p.AimXMm, Is.EqualTo(3500)); Assert.That(p.AimYMm, Is.EqualTo(200));
            p = Old(true);                                       // 3
            Assert.That(p.AimYMm, Is.Null);
            Assert.That(New(false, null).AimYMm, Is.Null, "4: the old entry inside the window cleared the height");
            Assert.That(New(true, g).AimYMm, Is.EqualTo(200), "5");
            p = Old(false);                                      // 6
            Assert.That(p.AimYMm, Is.Null); Assert.That(p.AimXMm, Is.EqualTo(3500));
            Assert.That(New(false, null).AimYMm, Is.EqualTo(200), "7: the old entry outside the window keeps the height");
            var plane = new AimTarget(AimKind.Plane, 6f, 0f, 7f, null, 0);
            p = New(true, plane);                                // 8
            Assert.That(p.AimXMm, Is.EqualTo(6000)); Assert.That(p.AimYMm, Is.Null);
            Assert.That(New(false, null).AimYMm, Is.Null, "9: a plane pick inside the window clears the height");
            Assert.That(New(true, g).AimYMm, Is.EqualTo(200));
            p = New(true, null);                                 // 10: aim null inside = the flat pick, no height
            Assert.That(p.AimYMm, Is.Null);
            Assert.That(p.AimXMm, Is.EqualTo(camera.ScreenToGround(400, 300, 800, 600).XMm));
            Assert.That(New(false, null).AimYMm, Is.Null, "11");
        }

        [Test]
        public void A4Input_MapperBeforeTheFirstPick_UsesTheFocusWithNoHeight()
        {
            var camera = new IsoCamera(10); camera.Follow(5, 6);
            PlayerInput p = new PlayerInputMapper().Map(camera, 0, 0, 0, 0, 800, 600, false, true, null, Ground(1, 1, 300));
            Assert.That(p.AimXMm, Is.EqualTo(5000)); Assert.That(p.AimZMm, Is.EqualTo(6000)); Assert.That(p.AimYMm, Is.Null);
        }

        // The walk script of acceptance 4(a) with every input carrying an explicit null height gives the same two folds as the
        // frozen lock (the mapper's new entry with aim = null outputs exactly that, see the F5 sequences above).
        [Test]
        public void A4Input_FlatWalkWithExplicitNullHeight_MatchesTheLock()
        {
            var w = new M6World(A4.FlatRecipe(), DemoRecipes.M2bMapBytes());
            var (hash, fires, _) = A4.RunPlayer(w, A4.FlatTicks, tick =>
            {
                if ((tick - 1) % 150 != 0) return null;
                PlayerInput s = A4.FlatScript((int)((tick - 1) / 150));
                return new PlayerInput(s.MoveXMilli, s.MoveZMilli, s.AimXMm, s.AimZMm, s.Primary, s.Block, s.Throw, s.SkillSlot, null);
            }, 0);
            Assert.That(hash, Is.EqualTo(0x270B175D7AA026D7UL));
            Assert.That(fires, Is.EqualTo(0x005F344E51360E1DUL));
        }
    }
}
