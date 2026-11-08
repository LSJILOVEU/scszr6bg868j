using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 1(b), recorded on the code before P1 (base 95d4fc9): the P1 fixture with B and C walking into A's sight.
    // With "reactionTicks" left out the system table, the 600-tick run (64-bit FNV-1a over the per-tick hashes), the
    // state hash at the end of tick 600 and the retained snapshots at ticks 300 and 600 (length and digest) are as recorded.
    public sealed class P1BaselineTests
    {
        internal const string Systems = "bridge.dead-cleanup|0|5;battle.reinforce|0|8;interim.orders|0|10;battle.ai|0|12;"
            + "interim.locomotion|1|20;combat.system|3|30;battle.capture|3|40;battle.match|3|50";
        internal const ulong Run600 = 7845115808961037120UL, End600 = 15733245162959224229UL;
        internal const int Snapshot300Length = 6236, Snapshot600Length = 6236;
        internal const ulong Snapshot300Digest = 7137994958871546723UL, Snapshot600Digest = 15177781327830261052UL;

        internal static UnitSpawn[] Units() => new[]
        {
            P1.Shooter(P1.KeyA, A3.AAt), P1.Target(P1.KeyB, 2, A2.Cell(0, 10)), P1.Target(P1.KeyC, 3, A2.Cell(0, 4)),
        };
        internal static readonly P1.Move[] Script =
        {
            new P1.Move(20, 2, A2.Cell(8, 10)), new P1.Move(120, 3, A2.Cell(10, 4)), new P1.Move(250, 2, A2.Cell(0, 10)),
        };

        internal static M6World Run(string ai, System.Action<M6World, long> each)
        {
            M6World w = null;
            Assert.DoesNotThrow(() => w = P1.World(Units(), ai));
            each(w, 0);
            for (long tick = 1; tick <= 600; tick++)
            {
                w.Sim.Step(P1.Drive(w, tick, Script));
                each(w, tick);
            }
            return w;
        }

        // 1(b): left out and written as 0 side by side: equal system tables, equal hashes on every tick.
        [Test]
        public void LeftOutAndZero_StepTogether()
        {
            M6World off = null, zero = null;
            Assert.DoesNotThrow(() => off = P1.World(Units(), null));
            Assert.DoesNotThrow(() => zero = P1.World(Units(), P1.Ai(0)));
            Assert.That(P1.Systems(zero), Is.EqualTo(Systems));
            Assert.That(P1.Systems(off), Is.EqualTo(Systems));
            Assert.That(zero.Hash, Is.EqualTo(off.Hash), "tick 0");
            for (long tick = 1; tick <= 600; tick++)
            {
                off.Sim.Step(P1.Drive(off, tick, Script));
                zero.Sim.Step(P1.Drive(zero, tick, Script));
                Assert.That(zero.Hash, Is.EqualTo(off.Hash), "tick " + tick);
            }
            Assert.That(zero.Hash, Is.EqualTo(End600));
        }

        // 1(c): a flat map (no surface table, no height field) with a content layer that sets a positive reaction time: not
        // a terrain world, so nothing is registered and the run equals the same map with the field written as 0.
        [Test]
        public void FlatMapWithPack_DoesNotRegister()
        {
            byte[] map = WcMapWriter.Write(Wp23.Make(A2.Width, A2.Depth, 0, 4 * A2.S, A2.DWalls, 2 * A2.S));
            M6World on = null, zero = null;
            Assert.DoesNotThrow(() => on = new M6World(P1.Recipe(Units(), P1.Ai(15)), map));
            Assert.DoesNotThrow(() => zero = new M6World(P1.Recipe(Units(), P1.Ai(0)), map));
            Assert.That(P1.Systems(on), Is.EqualTo(P1.Systems(zero)));
            Assert.That(P1.Systems(on), Does.Not.Contain("battle.ai-fire"));
            Assert.That(on.System<ReactionFire>(), Is.Null);
            int fires = 0;
            for (long tick = 1; tick <= 600; tick++)
            {
                on.Sim.Step(P1.Drive(on, tick, Script));
                zero.Sim.Step(P1.Drive(zero, tick, Script));
                fires += on.Combat.Fires.Length;
            }
            Assert.That(fires, Is.GreaterThan(0), "the run reaches the automatic fire branch");
            Assert.That(on.Hash, Is.EqualTo(zero.Hash));
        }

        [Test]
        public void LeftOut_Unchanged()
        {
            ulong fold = 14695981039346656037UL;
            int fires = 0;
            M6World w = Run(null, (x, tick) =>
            {
                fold = (fold ^ x.Hash) * 1099511628211UL;
                fires += tick > 0 ? x.Combat.Fires.Length : 0;
                if (tick == 300 || tick == 600)
                {
                    byte[] snapshot = x.Sim.SaveRetainedSnapshot();
                    Assert.That(snapshot.Length, Is.EqualTo(tick == 300 ? Snapshot300Length : Snapshot600Length), "length, tick " + tick);
                    Assert.That(P1.Digest(snapshot), Is.EqualTo(tick == 300 ? Snapshot300Digest : Snapshot600Digest), "digest, tick " + tick);
                }
            });
            Assert.That(P1.Systems(w), Is.EqualTo(Systems));
            Assert.That(fires, Is.GreaterThan(0), "the run reaches the automatic fire branch");
            Assert.That(fold, Is.EqualTo(Run600));
            Assert.That(w.Hash, Is.EqualTo(End600));
        }
    }
}
