using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // F5u (1)(2): the default key table and what the mapper makes of it.
    public sealed class F5uKeyTableTests
    {
        private static readonly (string Key, PlayerKeyAction Action, int Slot)[] Expected =
        {
            ("Mouse1", PlayerKeyAction.Block, 0),
            ("Space", PlayerKeyAction.Block, 0),
            ("G", PlayerKeyAction.Throw, 0),
            ("Alpha1", PlayerKeyAction.Skill, 1),
            ("Alpha2", PlayerKeyAction.Skill, 2),
            ("Alpha3", PlayerKeyAction.Skill, 3),
            ("Alpha4", PlayerKeyAction.Skill, 4),
        };

        // Move, primary attack and camera keys (MpHost.cs, IsoCameraInput.cs).
        private static readonly string[] Taken = { "W", "A", "S", "D", "Q", "E", "R", "F", "Mouse0", "Mouse2" };

        [Test]
        public void F5uDefault_IsTheSevenBindingsInOrder()
        {
            IReadOnlyList<PlayerKeyBinding> table = PlayerKeys.Default;
            Assert.That(table.Count, Is.EqualTo(7));
            for (int i = 0; i < Expected.Length; i++)
            {
                Assert.That(table[i].Key, Is.EqualTo(Expected[i].Key), $"key {i}");
                Assert.That(table[i].Action, Is.EqualTo(Expected[i].Action), $"action {i}");
                Assert.That(table[i].SkillSlot, Is.EqualTo(Expected[i].Slot), $"slot {i}");
            }
        }

        [Test]
        public void F5uDefault_KeysAreDistinctAndFree()
        {
            var seen = new HashSet<string>();
            foreach (PlayerKeyBinding binding in PlayerKeys.Default)
            {
                Assert.That(seen.Add(binding.Key), Is.True, binding.Key);
                Assert.That(Taken, Does.Not.Contain(binding.Key));
            }
            Assert.That(seen.Count, Is.EqualTo(7));
        }

        [Test]
        public void F5uDefault_IsANewTableOnEveryCall()
        {
            IReadOnlyList<PlayerKeyBinding> first = PlayerKeys.Default;
            Assert.That(PlayerKeys.Default, Is.Not.SameAs(first));
            var edited = (PlayerKeyBinding[])first;
            edited[0] = new PlayerKeyBinding("X", PlayerKeyAction.Throw);
            IReadOnlyList<PlayerKeyBinding> next = PlayerKeys.Default;
            Assert.That(next[0].Key, Is.EqualTo("Mouse1"));
            Assert.That(next[0].Action, Is.EqualTo(PlayerKeyAction.Block));
        }

        private static PlayerInput Press(params string[] keys)
        {
            var down = new HashSet<string>(keys);
            return new PlayerInputMapper(PlayerKeys.Default).Map(new IsoCamera(10), 0, 0, 50, 50, 100, 100, true, false, k => down.Contains(k));
        }

        private static void Only(PlayerInput input, bool block, bool @throw, int skill)
        {
            Assert.That(input.Block, Is.EqualTo(block), "Block");
            Assert.That(input.Throw, Is.EqualTo(@throw), "Throw");
            Assert.That(input.SkillSlot, Is.EqualTo(skill), "SkillSlot");
        }

        [Test]
        public void F5uMap_EachDefaultKeySetsOnlyItsAction()
        {
            Only(Press("Mouse1"), true, false, 0);
            Only(Press("Space"), true, false, 0);
            Only(Press("G"), false, true, 0);
            Only(Press("Alpha1"), false, false, 1);
            Only(Press("Alpha2"), false, false, 2);
            Only(Press("Alpha3"), false, false, 3);
            Only(Press("Alpha4"), false, false, 4);
            Only(Press(), false, false, 0);
        }

        [Test]
        public void F5uMap_TwoSkillKeys_TableOrderWins()
        {
            Only(Press("Alpha3", "Alpha1"), false, false, 1);
        }

        [Test]
        public void F5uMap_BlockThrowAndSkillTogether()
        {
            Only(Press("Mouse1", "G", "Alpha2"), true, true, 2);
        }
    }
}
