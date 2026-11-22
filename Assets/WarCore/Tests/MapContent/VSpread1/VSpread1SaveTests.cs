using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.VSpread1.VSpread1Worlds;

namespace WarCore.Tests.VSpread1
{
    // VSPREAD-1 V14: saves in the middle of a burst and of a stance change resume the same run; two runs are the same; a save
    // with the section does not load into a world without it; the section adds no component or system.
    public sealed class VSpread1SaveTests
    {
        private static List<ulong> Hashes(SimSession s, int n)
        {
            var list = new List<ulong>();
            for (int i = 0; i < n; i++) { s.StepOnce(); list.Add(s.StateHash); }
            return list;
        }

        private static void StepTo(SimSession s, long tick) { while (s.Tick < tick) s.StepOnce(); }

        // Tick 10: every shooter is firing (one shot a tick) and the prone target is still lying down (due at tick 23).
        [TestCase(10)]
        [TestCase(200)]
        public void V14_ASaveResumesTheSameRun(long at)
        {
            SimSession a = Range();
            StepTo(a, at);
            if (at == 10) Assert.That(a.Stances.StanceOf(Unit(a, 6)).IsChanging, Is.True, "mid change");
            byte[] save = SimOf(a).SaveRetainedSnapshot();
            List<ulong> expected = Hashes(a, 60);
            SimSession b = Range();
            SimOf(b).RestoreRetainedSnapshot(save);
            b.RefreshView();
            Assert.That(Hashes(b, 60), Is.EqualTo(expected));
            Assert.That(SimOf(b).SaveRetainedSnapshot(), Is.EqualTo(SimOf(a).SaveRetainedSnapshot()));
        }

        [Test]
        public void V14_TwoRunsAreTheSame() => Assert.That(Hashes(Range(), 120), Is.EqualTo(Hashes(Range(), 120)));

        // The range world with the same pack but no vertical spread.
        private static SimSession Plain()
        {
            CombatPack p = VspreadDemo.Content().BuildCombat();
            var plain = new CombatPack(p.Weapons.ToArray(), p.Resolver, p.Selector, p.MaxHealthRaw, p.FriendlyFire, p.FriendlyFireLocked,
                p.AllyCheckMissRaw, p.AllyHoldMilli, body: p.Body, stances: p.Stances.ToArray());
            return WorldBuilder.Build(VspreadDemo.Recipe().WithCombat(plain), VspreadDemo.MapBytes(), false);
        }

        [Test]
        public void V14_SavesDoNotCrossTheSwitch()
        {
            SimSession on = Range(), off = Plain();
            StepTo(on, 5); StepTo(off, 5);
            byte[] onSave = SimOf(on).SaveRetainedSnapshot(), offSave = SimOf(off).SaveRetainedSnapshot();
            Assert.That(() => SimOf(Plain()).RestoreRetainedSnapshot(onSave), Throws.Exception);
            Assert.That(() => SimOf(Range()).RestoreRetainedSnapshot(offSave), Throws.Exception);
            // Same components and systems: before the first step (no shots, no events yet) the two snapshots differ only in the
            // digest bytes, so they are as long.
            Assert.That(SimOf(Range()).SaveRetainedSnapshot().Length, Is.EqualTo(SimOf(Plain()).SaveRetainedSnapshot().Length));
            Assert.That(CombatOf(on).Pack.PolicyDigest, Is.Not.EqualTo(CombatOf(off).Pack.PolicyDigest));
        }
    }
}
