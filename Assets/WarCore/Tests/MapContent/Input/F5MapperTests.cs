using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // F5 (6): key -> action table on PlayerInputMapper.
    public sealed class F5MapperTests
    {
        private static List<PlayerKeyBinding> Table() => new List<PlayerKeyBinding> {
            new PlayerKeyBinding("Q", PlayerKeyAction.Block),
            new PlayerKeyBinding("E", PlayerKeyAction.Block),
            new PlayerKeyBinding("G", PlayerKeyAction.Throw),
            new PlayerKeyBinding("1", PlayerKeyAction.Skill, 1),
            new PlayerKeyBinding("2", PlayerKeyAction.Skill, 2)
        };

        private static PlayerInput Press(PlayerInputMapper mapper, params string[] keys)
        {
            var down = new HashSet<string>(keys);
            return mapper.Map(new IsoCamera(10), 0, 0, 50, 50, 100, 100, true, false, k => down.Contains(k));
        }

        private static void Only(PlayerInput input, bool block, bool @throw, int skill)
        {
            Assert.That(input.Block, Is.EqualTo(block), "Block");
            Assert.That(input.Throw, Is.EqualTo(@throw), "Throw");
            Assert.That(input.SkillSlot, Is.EqualTo(skill), "SkillSlot");
        }

        [Test]
        public void EachBoundKey_SetsOnlyItsAction()
        {
            var mapper = new PlayerInputMapper(Table());
            Only(Press(mapper, "Q"), true, false, 0);
            Only(Press(mapper, "E"), true, false, 0);
            Only(Press(mapper, "G"), false, true, 0);
            Only(Press(mapper, "2"), false, false, 2);
            Only(Press(mapper), false, false, 0);
            Only(Press(mapper, "X"), false, false, 0);
        }

        [Test]
        public void TwoSkillKeys_FirstInTableOrderWins()
        {
            Only(Press(new PlayerInputMapper(Table()), "1", "2"), false, false, 1);
            var swapped = Table();
            (swapped[3], swapped[4]) = (swapped[4], swapped[3]);
            Only(Press(new PlayerInputMapper(swapped), "1", "2"), false, false, 2);
        }

        [Test]
        public void Table_IsCopiedAtConstruction()
        {
            var table = Table();
            var mapper = new PlayerInputMapper(table);
            table.Clear();
            Only(Press(mapper, "Q"), true, false, 0);
        }

        [Test]
        public void InvalidBindings_Throw()
        {
            Assert.Throws<ArgumentException>(() => new PlayerKeyBinding("", PlayerKeyAction.Block));
            Assert.Throws<ArgumentException>(() => new PlayerKeyBinding("1", PlayerKeyAction.Skill, 0));
            Assert.Throws<ArgumentException>(() => new PlayerKeyBinding("Q", PlayerKeyAction.Block, 1));
        }

        // The same call sequences as Wp18MapperTests: the 9-arg Map, the new overload with a null
        // key reader, and a non-empty table with a null key reader all agree on every field.
        private static IEnumerable<Func<PlayerInputMapper, bool, PlayerInput[]>> Sequences()
        {
            foreach (var c in new[] { (0f, 0f), (90f, 0f), (180f, 0f), (270f, 0f) })
            {
                float yaw = c.Item1;
                yield return (m, nine) => {
                    var camera = new IsoCamera(10, groundY: 7, yaw: yaw, pitch: 45);
                    camera.Follow(2.25f, 26.75f);
                    return new[] { Call(m, nine, camera, 0, 1, 750, 500, 1000, 500, true, true) };
                };
            }
            yield return (m, nine) => {
                var camera = new IsoCamera(10); camera.Follow(1.2346f, -2.3456f);
                return new[] { Call(m, nine, camera, 0, 1, 500, 250, 1000, 500, true, false), Call(m, nine, camera, 0, -1, 500, 250, 1000, 500, true, false) };
            };
            foreach (var v in new[] { (0.1239f, -0.9879f), (-0.0009f, 0.0009f), (100f, 0f), (-100f, 0f), (0f, 100f), (3f, 4f), (1f, 1f) })
            {
                var axes = v;
                yield return (m, nine) => new[] { Call(m, nine, new IsoCamera(10, yaw: 0), axes.Item1, axes.Item2, 0, 0, 100, 100, false, false) };
            }
            yield return (m, nine) => {
                var camera = new IsoCamera(10); var list = new List<PlayerInput>();
                foreach (float rotation in new[] { 0f, 90f, 37f, 180f }) { camera.Rotate(rotation); list.Add(Call(m, nine, camera, 0, 1, 50, 50, 100, 100, true, false)); }
                return list.ToArray();
            };
            foreach (var p in new[] { (false, 800f, 600f), (true, 0f, 600f), (true, 800f, 0f), (true, -1f, 600f), (true, 800f, -1f) })
            {
                var pick = p;
                yield return (m, nine) => {
                    var camera = new IsoCamera(10);
                    var first = Call(m, nine, camera, 0, 0, 520, 380, 800, 600, true, false);
                    camera.Follow(30, 40); camera.Rotate(90);
                    return new[] { first, Call(m, nine, camera, 0, 1, -900, 999, pick.Item2, pick.Item3, pick.Item1, true) };
                };
            }
            yield return (m, nine) => {
                var camera = new IsoCamera(10, groundY: 30); camera.Follow(1.2346f, -2.3456f);
                var outside = Call(m, nine, camera, 0, 0, 0, 0, 800, 600, false, false);
                camera.Follow(-12, 34);
                return new[] { outside, Call(m, nine, camera, 0, 0, 0, 0, 0, 0, true, false) };
            };
        }

        private static PlayerInput Call(PlayerInputMapper m, bool nine, IsoCamera camera, float ix, float iy, float x, float y,
            float w, float h, bool inside, bool primary) =>
            nine ? m.Map(camera, ix, iy, x, y, w, h, inside, primary) : m.Map(camera, ix, iy, x, y, w, h, inside, primary, null);

        [Test]
        public void NullKeyReaderAndEmptyTable_MatchTheNineArgumentMap()
        {
            int compared = 0;
            foreach (var sequence in Sequences())
            {
                PlayerInput[] expected = sequence(new PlayerInputMapper(), true);
                PlayerInput[] viaNull = sequence(new PlayerInputMapper(), false);
                PlayerInput[] viaTable = sequence(new PlayerInputMapper(Table()), false);
                Assert.That(viaNull.Length, Is.EqualTo(expected.Length));
                Assert.That(viaTable.Length, Is.EqualTo(expected.Length));
                for (int i = 0; i < expected.Length; i++)
                {
                    foreach (var actual in new[] { viaNull[i], viaTable[i] })
                    {
                        Assert.That((actual.MoveXMilli, actual.MoveZMilli, actual.AimXMm, actual.AimZMm, actual.Primary),
                            Is.EqualTo((expected[i].MoveXMilli, expected[i].MoveZMilli, expected[i].AimXMm, expected[i].AimZMm, expected[i].Primary)));
                        Assert.That((actual.Block, actual.Throw, actual.SkillSlot), Is.EqualTo((false, false, 0)));
                        Assert.That(actual, Is.EqualTo(expected[i]));
                    }
                    compared++;
                }
            }
            Assert.That(compared, Is.EqualTo(29));
        }

        [Test]
        public void KeysDoNotChangeMovementAimOrPrimary()
        {
            var camera = new IsoCamera(10); camera.Follow(1.2346f, -2.3456f);
            var plain = new PlayerInputMapper().Map(camera, 0, 1, 500, 250, 1000, 500, true, true);
            var keyed = new PlayerInputMapper(Table()).Map(camera, 0, 1, 500, 250, 1000, 500, true, true, k => k == "E" || k == "G" || k == "2");
            Assert.That((keyed.MoveXMilli, keyed.MoveZMilli, keyed.AimXMm, keyed.AimZMm, keyed.Primary),
                Is.EqualTo((plain.MoveXMilli, plain.MoveZMilli, plain.AimXMm, plain.AimZMm, plain.Primary)));
            Only(keyed, true, true, 2);
        }
    }
}
