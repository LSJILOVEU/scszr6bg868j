using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // C4: an AI-driven unit with a block spec raises it against an incoming swing with the spec's chance, toward where it faces.
    // Unit 1 (team 1) swings at unit 2 (team 2), 1 m ahead on +X, and lands on tick 7 for a full health bar (damage 100).
    public sealed class BlockAiTests
    {
        private const long Ox = 20000, Oz = 20000;

        private static CombatPack Pack(int permille, int reduction = 1000) =>
            new CombatPack(Array.Empty<WeaponDef>(), new FixedHit(), new NearestVisibleEnemy(), 100 * SimFix.Scale, false, false, 0, 30000,
                new[] { MeleeWorld.Spec(damage: 100, maxTargets: 4) }, new[] { new BlockSpec(500, reduction, permille) });

        private sealed class FixedHit : IHitResolver
        {
            public ulong PolicyKey => 0x424C4F434B000002;
            public long DamageRaw(in WeaponDef weapon, long falloffRaw, int rollRaw, long maxHealthRaw) => maxHealthRaw / 20;
        }

        private static UnitSpawn Blocker(ulong key, long xMm, long zMm, int team = 2) => new UnitSpawn(key, 0, 2, xMm, zMm, 3000, team, -1, false, -1, 0);

        // facing: the defender's facing as (x, z) in thousandths; (-1000, 0) faces the attacker, (1000, 0) has its back to it.
        private static MeleeWorld Duel(int permille, long facingX, long facingZ, bool ai = true, int reduction = 1000)
        {
            var w = new MeleeWorld(Pack(permille, reduction), new[] { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), Blocker(2, Ox + 1000, Oz) }, blockAi: ai,
                map: null);
            if (!ai) return w;
            Face(w, 2, facingX, facingZ);
            return w;
        }

        private static void Face(MeleeWorld w, ulong key, long x, long z) =>
            w.W.Sim.Entities.Set(w.W.Sim.Entities.Resolve<UnitFacing>(), w.W.Unit(key), new UnitFacing(SimFix.Ratio(x, 1000), SimFix.Ratio(z, 1000)));

        private static bool Dead(MeleeWorld w, ulong key) => w.W.Combat.IsDead(w.W.Unit(key));

        [Test]
        public void AFacingDefenderWhoAlwaysRaises_SurvivesTheBlow()
        {
            var w = Duel(1000, -1000, 0);
            w.Step(7);
            Assert.That(w.Health(2), Is.EqualTo(MeleeWorld.Full));
            Assert.That(Dead(w, 2), Is.False);
            Assert.That(w.Count(StrikeKind.Blocked), Is.EqualTo(1));
        }

        [Test]
        public void AnAttackerFromBehindIsNotBlocked_AndKills()
        {
            var w = Duel(1000, 1000, 0);
            w.Step(7);
            Assert.That(w.Health(2), Is.EqualTo(0));
            Assert.That(Dead(w, 2), Is.True);
            Assert.That(w.Count(StrikeKind.Blocked), Is.Zero);
        }

        [Test]
        public void ABlowFromTheSideIsNotBlocked()
        {
            var w = Duel(1000, 0, 1000);
            w.Step(7);
            Assert.That(Dead(w, 2), Is.True);
        }

        [Test]
        public void AChanceOfZero_NeverRaises()
        {
            var w = Duel(0, -1000, 0);
            w.Step(7);
            Assert.That(Dead(w, 2), Is.True);
            Assert.That(w.Count(StrikeKind.Blocked), Is.Zero);
        }

        [Test]
        public void WithoutTheRecipeFlag_NothingRaises()
        {
            var w = Duel(1000, -1000, 0, ai: false);
            w.Step(7);
            Assert.That(Dead(w, 2), Is.True);
        }

        [Test]
        public void TheBlockIsUpDuringTheWindupAndDownAfterTheSwing()
        {
            var w = Duel(1000, -1000, 0);
            var defender = w.W.Unit(2);
            Assert.That(w.W.Combat.IsHolding(defender), Is.False, "nothing is coming yet");
            w.Step(2);
            Assert.That(w.W.Combat.IsHolding(defender), Is.True, "the swing started on tick 1; the shield is up on tick 2");
            w.Step(5);
            Assert.That(w.Count(StrikeKind.Blocked), Is.EqualTo(1));
            w.Step(1);
            Assert.That(w.W.Combat.IsHolding(defender), Is.False, "the swing is spent");
        }

        [Test]
        public void AGunlessDefenderWithoutABlockSpecIsLeftAlone()
        {
            var w = new MeleeWorld(Pack(1000), new[] { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), MeleeWorld.Unit(2, 2, Ox + 1000, Oz) }, blockAi: true);
            Assert.DoesNotThrow(() => w.Step(10));
            Assert.That(w.W.Combat.IsHolding(w.W.Unit(2)), Is.False);
        }

        [Test]
        public void AUnitOutsideTheSwingsReachDoesNotRaise()
        {
            // Unit 3 stands 2.8 m behind the attacker: inside the search range, outside the swing's 2 m radius.
            var w = new MeleeWorld(Pack(1000), new[] { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), Blocker(2, Ox + 1000, Oz), Blocker(3, Ox - 2800, Oz) }, blockAi: true);
            Face(w, 2, -1000, 0); Face(w, 3, 1000, 0);
            bool raised = false; int blocked = 0;
            for (int i = 0; i < 8; i++) { w.Step(); raised |= w.W.Combat.IsHolding(w.W.Unit(3)); blocked += w.Count(StrikeKind.Blocked); }
            Assert.That(raised, Is.False);
            Assert.That(blocked, Is.EqualTo(1), "the swing was aimed at unit 2");
        }

        [Test]
        public void ATeammatesSwingDoesNotRaise()
        {
            // Unit 3 is on unit 2's team and swings at unit 4 (team 1), 1 m from it; unit 2 stands 1.5 m from unit 3, inside the swing's radius.
            var w = new MeleeWorld(Pack(1000), new[] { Blocker(2, Ox, Oz), new UnitSpawn(3, 0, 2, Ox + 1500, Oz, 3000, 2, -1, false, 0),
                MeleeWorld.Unit(4, 1, Ox + 2500, Oz) }, blockAi: true);
            Face(w, 2, 1000, 0);
            bool raised = false;
            for (int i = 0; i < 8; i++) { w.Step(); raised |= w.W.Combat.IsHolding(w.W.Unit(2)); }
            Assert.That(raised, Is.False);
        }

        [Test]
        public void AnIdleNeighbourNearTheWorldOriginIsNoThreat()
        {
            // An attacker that is not swinging carries no swing origin; it must not read as one at the world origin (0, 0).
            var w = new MeleeWorld(Pack(1000), new[] { MeleeWorld.Unit(1, 1, 3000, 1000, melee: 0), Blocker(2, 500, 1000) }, blockAi: true);
            Face(w, 2, 1000, 0);
            for (int i = 0; i < 10; i++) { w.Step(); Assert.That(w.W.Combat.IsHolding(w.W.Unit(2)), Is.False, "tick " + i); }
        }

        [Test]
        public void TheShieldStaysWhereItWasRaised_WhenTheUnitTurnsDuringTheSwing()
        {
            var w = Duel(1000, -1000, 0);
            w.Step(2);
            Face(w, 2, 1000, 0);
            w.Step(5);
            Assert.That(w.Count(StrikeKind.Blocked), Is.EqualTo(1));
            Assert.That(Dead(w, 2), Is.False);
        }

        [Test]
        public void AUnitWithAZeroChanceKeepsAHoldSomeoneElseSet()
        {
            var w = Duel(0, -1000, 0);
            w.W.Combat.Hold(w.W.Unit(2), new SimVec3(SimFix.FromInt(-1), default, default));
            w.Step(3);
            Assert.That(w.W.Combat.IsHolding(w.W.Unit(2)), Is.True);
            w.Step(4);
            Assert.That(w.Count(StrikeKind.Blocked), Is.EqualTo(1));
        }

        [Test]
        public void ThePartialReductionStillApplies()
        {
            var w = Duel(1000, -1000, 0, reduction: 600);
            w.Step(7);
            Assert.That(w.Health(2), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(40)));
        }

        // --- the chance: structure only -------------------------------------------------------------------------------------------

        private static StableId Id(ulong v) => new StableId(v);

        [Test]
        public void TheChanceRolls_AtZeroNever_AtOneThousandAlways_AndAreMonotone()
        {
            for (ulong a = 1; a <= 40; a++)
                for (ulong v = 100; v < 110; v++)
                    for (long t = 1; t <= 10; t++)
                    {
                        Assert.That(BlockAiSystem.Raises(Id(a), Id(v), t, 0), Is.False);
                        Assert.That(BlockAiSystem.Raises(Id(a), Id(v), t, 1000), Is.True);
                        bool previous = false;
                        for (int p = 0; p <= 1000; p += 50)
                        {
                            bool now = BlockAiSystem.Raises(Id(a), Id(v), t, p);
                            Assert.That(!previous || now, Is.True, "a higher chance never drops a swing");
                            previous = now;
                        }
                        Assert.That(BlockAiSystem.Raises(Id(a), Id(v), t, 317), Is.EqualTo(BlockAiSystem.Raises(Id(a), Id(v), t, 317)));
                    }
        }

        [Test]
        public void TheRollDependsOnAttackerVictimAndStartTick_AndSpreadsAcrossThem()
        {
            int hits = 0, total = 0;
            var seen = new System.Collections.Generic.HashSet<bool>();
            for (ulong a = 1; a <= 20; a++)
                for (long t = 1; t <= 100; t++)
                {
                    bool r = BlockAiSystem.Raises(Id(a), Id(77), t, 500);
                    seen.Add(r); total++; if (r) hits++;
                }
            Assert.That(seen.Count, Is.EqualTo(2));
            var overTime = new System.Collections.Generic.HashSet<bool>();
            for (long t = 1; t <= 100; t++) overTime.Add(BlockAiSystem.Raises(Id(3), Id(77), t, 500));
            Assert.That(overTime.Count, Is.EqualTo(2), "one attacker's successive swings are rolled apart");
            Assert.That(hits, Is.InRange(total * 4 / 10, total * 6 / 10), "about half at 500");
        }

        [Test]
        public void AMiddleChanceBlocksSomeSwingsAndNotOthers_TheSameWayEveryRun()
        {
            // Twenty duels with different attackers' identities (keys 1..20 swing at their own defender 100 + key).
            string Run()
            {
                var sb = new System.Text.StringBuilder();
                for (ulong k = 1; k <= 20; k++)
                {
                    var w = new MeleeWorld(Pack(500), new[] { MeleeWorld.Unit(k, 1, Ox, Oz, melee: 0), Blocker(100 + k, Ox + 1000, Oz) }, blockAi: true);
                    Face(w, 100 + k, -1000, 0);
                    w.Step(8);
                    sb.Append(Dead(w, 100 + k) ? 'x' : 'o');
                }
                return sb.ToString();
            }
            string first = Run();
            Assert.That(Run(), Is.EqualTo(first));
            Assert.That(first, Does.Contain("x").And.Contain("o"));
        }

        [Test]
        public void ReplayAndSnapshotMidWindupGiveTheSameHashes()
        {
            var whole = Duel(1000, -1000, 0); var a = Duel(1000, -1000, 0);
            whole.Step(3); a.Step(3);
            var b = Duel(1000, -1000, 0);
            b.W.Sim.RestoreRetainedSnapshot(a.W.Sim.SaveRetainedSnapshot());
            Assert.That(b.W.Combat.IsHolding(b.W.Unit(2)), Is.True, "the hold is saved with the unit");
            for (int i = 0; i < 12; i++)
            {
                whole.Step(); b.Step();
                Assert.That(b.W.Session.StateHash, Is.EqualTo(whole.W.Session.StateHash), "step " + (4 + i));
            }
        }

        [Test]
        public void TheBlockAiNeedsBlockSpecsAndFacing()
        {
            var noBlock = MeleeWorld.Pack(new[] { MeleeWorld.Spec() });
            var units = new[] { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), MeleeWorld.Unit(2, 2, Ox + 1000, Oz) };
            Assert.That(Refused(() => new MeleeWorld(noBlock, units, blockAi: true)), Is.InstanceOf<ArgumentException>());
            var recipe = new WorldRecipe(41, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>()).WithCombat(Pack(1000)).WithBlockAi();
            Assert.That(Refused(() => new M6World(recipe)), Is.InstanceOf<ArgumentException>());
        }

        // The world builder is called by reflection, so the refusal arrives wrapped.
        private static Exception Refused(Action build)
        {
            Exception e = Assert.Catch(() => build());
            return e is System.Reflection.TargetInvocationException wrapped ? wrapped.InnerException : e;
        }

        [Test]
        public void TheChanceIsPartOfThePackDigestOnlyWhenSet()
        {
            Assert.That(Pack(0).PolicyDigest, Is.EqualTo(new CombatPack(Array.Empty<WeaponDef>(), new FixedHit(), new NearestVisibleEnemy(), 100 * SimFix.Scale, false, false, 0, 30000,
                new[] { MeleeWorld.Spec(damage: 100, maxTargets: 4) }, new[] { new BlockSpec(500, 1000) }).PolicyDigest));
            var plain = new StateHash(); new BlockSpec(500, 1000).WriteState(plain);
            var bytes = new StateHash(); bytes.Add(500); bytes.Add(1000);
            Assert.That(plain.Value, Is.EqualTo(bytes.Value), "a spec without a chance hashes as it did before the field existed");
            Assert.That(Pack(300).PolicyDigest, Is.Not.EqualTo(Pack(0).PolicyDigest));
            Assert.That(Pack(300).PolicyDigest, Is.Not.EqualTo(Pack(301).PolicyDigest));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BlockSpec(500, 1000, 1001));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BlockSpec(500, 1000, -1));
        }
    }
}
