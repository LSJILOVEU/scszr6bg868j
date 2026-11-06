using System;
using System.Text;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // C3: a unit holding a block takes reduced melee damage from blows that come from inside its front arc. Unit 1 (team 1) swings
    // at unit 2 (team 2), 1 m ahead on +X, and lands on tick 7. Unit 2 may carry block spec 0 (cosine 500 = a 60 degree half arc).
    public sealed class BlockTests
    {
        private const long Ox = 20000, Oz = 20000;

        private static CombatPack Pack(int reduction = 1000, int cos = 500, WeaponDef[] guns = null, bool melee = true) =>
            new CombatPack(guns ?? Array.Empty<WeaponDef>(), new FixedHit(), new NearestVisibleEnemy(), 100 * SimFix.Scale, false, false, 0, 30000,
                melee ? new[] { MeleeWorld.Spec(maxTargets: 4) } : null, new[] { new BlockSpec(cos, reduction) });

        private sealed class FixedHit : IHitResolver
        {
            public ulong PolicyKey => 0x424C4F434B000001;
            public long DamageRaw(in WeaponDef weapon, long falloffRaw, int rollRaw, long maxHealthRaw) => maxHealthRaw / 20;
        }

        private static UnitSpawn Blocker(ulong key, long xMm, long zMm) => new UnitSpawn(key, 0, 2, xMm, zMm, 3000, 2, -1, false, -1, 0);

        private static MeleeWorld Duel(CombatPack pack, params UnitSpawn[] extra)
        {
            var units = new System.Collections.Generic.List<UnitSpawn> { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), Blocker(2, Ox + 1000, Oz) };
            units.AddRange(extra);
            return new MeleeWorld(pack, units.ToArray());
        }

        private static void Hold(MeleeWorld w, ulong key, long dirX, long dirZ) =>
            w.W.Combat.Hold(w.W.Unit(key), new SimVec3(SimFix.Ratio(dirX, 1000), default, SimFix.Ratio(dirZ, 1000)));

        // Direction the hold is aimed along, as (x, z) in thousandths, against a blow coming from -X.
        [TestCase(-1000, 0, true, TestName = "facing the attacker")]
        [TestCase(1000, 0, false, TestName = "back to the attacker")]
        [TestCase(0, 1000, false, TestName = "side, 90 degrees")]
        [TestCase(0, -1000, false, TestName = "other side, 90 degrees")]
        [TestCase(-707, 707, true, TestName = "45 degrees off")]
        [TestCase(-515, 857, true, TestName = "59 degrees off, inside the 60 degree edge")]
        [TestCase(-485, 875, false, TestName = "61 degrees off, outside the edge")]
        [TestCase(1000, 1000, false, TestName = "135 degrees off, behind")]
        [TestCase(-2575, 4285, true, TestName = "long vector, 59 degrees off")]
        [TestCase(-2425, 4375, false, TestName = "long vector, 61 degrees off")]
        public void OnlyABlowFromInsideTheArcIsStopped(long dirX, long dirZ, bool stopped)
        {
            var w = Duel(Pack());
            Hold(w, 2, dirX, dirZ);
            w.Step(7);
            Assert.That(w.Health(2), Is.EqualTo(stopped ? MeleeWorld.Full : MeleeWorld.Full - MeleeWorld.Cost(10)));
            Assert.That(w.Count(StrikeKind.Blocked), Is.EqualTo(stopped ? 1 : 0));
            Assert.That(w.Count(StrikeKind.Landed), Is.EqualTo(stopped ? 0 : 1));
            Assert.That(w.Count(StrikeKind.Whiffed), Is.Zero, "a blocked blow is not a whiff");
        }

        [Test]
        public void ARunningHoldIsNotNeeded_TheUnitMustBeHoldingWhenTheBlowLands()
        {
            var notYet = Duel(Pack()); notYet.Step(6); Hold(notYet, 2, -1000, 0); notYet.Step();
            Assert.That(notYet.Health(2), Is.EqualTo(MeleeWorld.Full), "a hold taken during the windup counts at the landing tick");
            var tooLate = Duel(Pack()); Hold(tooLate, 2, -1000, 0); tooLate.Step(6); tooLate.W.Combat.Release(tooLate.W.Unit(2)); tooLate.Step();
            Assert.That(tooLate.Health(2), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)), "a hold let go before the landing tick does not");
            var never = Duel(Pack()); never.Step(7);
            Assert.That(never.Health(2), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)), "having a spec is not holding");
        }

        [Test]
        public void ReductionIsTheStoppedShare_AndTheRestLandsAsOneBlow()
        {
            var w = Duel(Pack(reduction: 600));
            Hold(w, 2, -1000, 0);
            w.Step(7);
            Assert.That(w.Health(2), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(4)), "40% of 10 gets through");
            Assert.That(w.Count(StrikeKind.Landed), Is.EqualTo(1)); Assert.That(w.Count(StrikeKind.Blocked), Is.Zero);
            foreach (var s in w.W.Combat.Strikes) if (s.Kind == StrikeKind.Landed) Assert.That(s.DamageRaw, Is.EqualTo(MeleeWorld.Cost(4)));
            Assert.That(new BlockSpec(500, 333).Through(10 * SimFix.Scale), Is.EqualTo(10 * SimFix.Scale - 10 * SimFix.Scale * 333 / 1000));
            Assert.That(new BlockSpec(500, 0).Through(12345), Is.EqualTo(12345));
        }

        [Test]
        public void EachTargetIsJudgedAlone_TheOtherTargetInTheSwingStillTakesTheBlow()
        {
            // Both stand in the sector; only the one facing the attacker holds a block.
            var w = Duel(Pack(), Blocker(3, Ox + 1400, Oz + 300));
            Hold(w, 2, -1000, 0); Hold(w, 3, 1000, 0);
            w.Step(7);
            Assert.That(w.Health(2), Is.EqualTo(MeleeWorld.Full));
            Assert.That(w.Health(3), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)));
            Assert.That(w.Count(StrikeKind.Blocked), Is.EqualTo(1)); Assert.That(w.Count(StrikeKind.Landed), Is.EqualTo(1));
        }

        [Test]
        public void ABlowFromTheSideOrBehindStillHurts_EvenWhenTheUnitHoldsTowardSomeoneElse()
        {
            // The attacker's blow comes from -X; the hold faces +Z. Facing is the defender's, not the attacker's.
            var w = Duel(Pack()); Hold(w, 2, 0, 1000); w.Step(7);
            Assert.That(w.Health(2), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)));
        }

        [Test]
        public void ShotsAreNotBlocked()
        {
            WeaponDef gun = new WeaponDef(3390, 30, 60000, SimFix.FromInt(34), SimFix.FromInt(67), 32768);
            long Run(bool hold)
            {
                var w = new MeleeWorld(Pack(guns: new[] { gun }, melee: false), new[] { new UnitSpawn(1, 0, 1, Ox, Oz, 3000, 1, 0), Blocker(2, Ox + 1000, Oz) });
                if (hold) Hold(w, 2, -1000, 0);
                w.Step(40);
                return w.Health(2);
            }
            long held = Run(true), open = Run(false);
            Assert.That(open, Is.LessThan(MeleeWorld.Full), "the gun hurts");
            Assert.That(held, Is.EqualTo(open));
        }

        [Test]
        public void AWorldWithoutBlockData_HasNoBlockComponentAndNoBlockedRecords()
        {
            var w = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec() }), new[] { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), MeleeWorld.Unit(2, 2, Ox + 1000, Oz) });
            Assert.That(w.W.Combat.CanBlock(w.W.Unit(2)), Is.False);
            Assert.Throws<InvalidOperationException>(() => w.W.Sim.Entities.Resolve<BlockState>());
            Assert.That(Encoding.ASCII.GetString(w.W.Sim.SaveRetainedSnapshot()), Does.Not.Contain(BlockState.ComponentId));
            w.Step(7); Assert.That(w.Count(StrikeKind.Blocked), Is.Zero);
            Assert.That(MeleeWorld.Pack(new[] { MeleeWorld.Spec() }).HasBlock, Is.False);
            var bare = new CombatPack(Array.Empty<WeaponDef>(), new FixedHit(), new NearestVisibleEnemy(), 100 * SimFix.Scale, false, false, 0, 30000, new[] { MeleeWorld.Spec(maxTargets: 4) });
            Assert.That(Pack().PolicyDigest, Is.Not.EqualTo(bare.PolicyDigest), "block data is part of the pack digest");
            Assert.That(Pack(reduction: 500).PolicyDigest, Is.Not.EqualTo(Pack().PolicyDigest));
            Assert.That(Pack(cos: 400).PolicyDigest, Is.Not.EqualTo(Pack().PolicyDigest));
        }

        [Test]
        public void TheHoldIsStateThatSurvivesASnapshotAndRestore()
        {
            UnitSpawn[] Units() => new[] { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), Blocker(2, Ox + 1000, Oz) };
            var run = new MeleeWorld(Pack(), Units()); Hold(run, 2, -1000, 0); run.Step(3);
            byte[] saved = run.W.Sim.SaveRetainedSnapshot();
            var twin = new MeleeWorld(Pack(), Units(), reverse: true);
            twin.W.Sim.RestoreRetainedSnapshot(saved, reverseAllocation: true);
            Assert.That(twin.W.Combat.IsHolding(twin.W.Unit(2)), Is.True);
            Assert.That(twin.W.Hash, Is.EqualTo(run.W.Hash));
            run.Step(4); twin.Step(4);
            Assert.That(twin.W.Hash, Is.EqualTo(run.W.Hash)); Assert.That(twin.Health(2), Is.EqualTo(MeleeWorld.Full));
            run.W.Combat.Release(run.W.Unit(2)); Assert.That(run.W.Read<BlockState>(2), Is.EqualTo(new BlockState(0)), "a released unit saves canonical bytes");
        }

        [Test]
        public void BadInputIsRefused()
        {
            var w = Duel(Pack());
            Assert.Throws<ArgumentException>(() => w.W.Combat.SetBlock(w.W.Unit(2), 1));
            Assert.Throws<ArgumentException>(() => w.W.Combat.Hold(w.W.Unit(2), default));
            Assert.Throws<InvalidOperationException>(() => w.W.Combat.Hold(w.W.Unit(1), SimVec3.Metres(1, 0, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BlockSpec(1001, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BlockSpec(0, 1001));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BlockSpec(-1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BlockSpec(0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new UnitSpawn(9, 0, 1, 0, 0, 1000, block: -2));
        }

        [Test]
        public void TheControlledUnit_HoldsWhileTheBlockInputIsDown_TowardTheAim()
        {
            var w = new MeleeWorld(Pack(), new[] { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), Blocker(2, Ox + 1000, Oz) }, player: 2, match: M6World.Pack());
            Assert.That(w.W.Combat.IsHolding(w.W.Unit(2)), Is.False);
            w.Step(new InputFrame(default, SimVec3.Metres(0, 0, 20), block: true)); // aim -X of the unit at (21, 20): the attacker's side
            Assert.That(w.W.Combat.IsHolding(w.W.Unit(2)), Is.True);
            BlockState held = w.W.Read<BlockState>(2);
            Assert.That((held.DirX, held.DirZ), Is.EqualTo((-SimFix.Scale, 0L)));
            for (int i = 0; i < 6; i++) w.Step(new InputFrame(default, SimVec3.Metres(0, 0, 20), block: true));
            Assert.That(w.Tick, Is.GreaterThanOrEqualTo(7));
            Assert.That(w.Health(2), Is.EqualTo(MeleeWorld.Full), "the blow was stopped");
            w.Step(new InputFrame(default, SimVec3.Metres(0, 0, 20)));
            Assert.That(w.W.Combat.IsHolding(w.W.Unit(2)), Is.False, "letting go releases");
        }
    }
}
