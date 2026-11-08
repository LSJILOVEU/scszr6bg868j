using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.RespawnWorld;

namespace WarCore.Tests
{
    // C2 (1) respawn off shows nothing; (9) the MP respawn demo only changes the respawn delay of the match rules.
    public sealed class C2RecipeAndOffTests
    {
        private static void NoneFor(M6World w, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                w.Session.StepOnce();
                Assert.That(w.Session.Current.Respawn.State, Is.EqualTo(RespawnPhase.None), "tick " + w.Session.Tick);
                Assert.That(HudLines.Respawn(w.Session.Current), Is.Null, "tick " + w.Session.Tick);
            }
        }

        [Test]
        public void C2RespawnOff_PlayerDies_NoRespawnState()
        {
            var w = World(Units(U(3, 0, 0, reserve: true)), Pack(Points(Spawn(1, 30, 20)), Bases(new SpawnAnchor(1, P(5, 38))), 0), kills: new long[] { 1 });
            NoneFor(w, 60);
            Assert.That(w.Dead(Player), Is.True);
        }

        [Test]
        public void C2NoPlayer_NoRespawnState()
        {
            var pack = Pack(Points(Spawn(1, 30, 20)), Bases(new SpawnAnchor(1, P(5, 38))), 300);
            NoneFor(new M6World(Recipe(Units(U(3, 0, 0, reserve: true)), pack, player: false)), 60);
        }

        // No match rules (so no player either: WorldBuilder refuses a player without them).
        [Test]
        public void C2NoMatch_NoRespawnState()
        {
            SimSession session = SimSession.Create(new WorldRecipe(Seed, MoverKind.Interim, Units().ToArray(), Array.Empty<BridgeCommand>()));
            for (int i = 0; i < 30; i++)
            {
                session.StepOnce();
                Assert.That(session.Current.Respawn.State, Is.EqualTo(RespawnPhase.None));
                Assert.That(HudLines.Respawn(session.Current), Is.Null);
            }
            Assert.That(session.Tick, Is.EqualTo(30));
        }

        private static void SameExceptDelay(MatchPack a, MatchPack b)
        {
            Assert.That(b.Points.ToArray(), Is.EqualTo(a.Points.ToArray()));
            Assert.That(b.Bases.ToArray(), Is.EqualTo(a.Bases.ToArray()));
            Assert.That(new object[] { b.Num, b.Den, b.Strict, b.CaptureTicks, b.Interrupt, b.DecayPerTick, b.IntervalTicks, b.ThinkInterval,
                    b.MaxTicks, b.Sight, b.GridColumns, b.GridRows, b.GridSpacing },
                Is.EqualTo(new object[] { a.Num, a.Den, a.Strict, a.CaptureTicks, a.Interrupt, a.DecayPerTick, a.IntervalTicks, a.ThinkInterval,
                    a.MaxTicks, a.Sight, a.GridColumns, a.GridRows, a.GridSpacing }));
        }

        [Test]
        public void C2MpRespawnDemo_IsTheBlockDemoWithA300TickDelay()
        {
            WorldRecipe block = DemoRecipes.MpBlockDemo(), respawn = DemoRecipes.MpRespawnDemo();
            Assert.That(block.Match.RespawnTicks, Is.Zero);
            Assert.That(respawn.Match.RespawnTicks, Is.EqualTo(300));
            SameExceptDelay(block.Match, respawn.Match);
            Assert.That(respawn.Units.ToArray(), Is.EqualTo(block.Units.ToArray()));
            Assert.That(respawn.PlayerKey, Is.EqualTo(block.PlayerKey));
            Assert.That(respawn.Combat.PolicyDigest, Is.EqualTo(block.Combat.PolicyDigest));
            Assert.That(respawn.Seed, Is.EqualTo(block.Seed));
            Assert.That(respawn.Mover, Is.EqualTo(block.Mover));
            Assert.That(respawn.Match.PolicyDigest, Is.Not.EqualTo(block.Match.PolicyDigest));
        }

        [Test]
        public void C2WithRespawn_CopiesEveryMatchField()
        {
            var points = new[]
            {
                new ControlPointDef(4, SimVec3.Metres(30, 0, 20), SimFix.FromInt(3), 1, true, SimVec3.Metres(31, 0, 21)),
                new ControlPointDef(7, SimVec3.Metres(60, 0, 10), SimFix.FromInt(5), 2),
            };
            var bases = new[] { new SpawnAnchor(1, SimVec3.Metres(5, 0, 38)), new SpawnAnchor(2, SimVec3.Metres(75, 0, 2)) };
            var odd = new MatchPack(points, bases, 3, 2, true, 450, InterruptMode.Decay, 3, 900, 17, 12345, SimFix.FromInt(41),
                3, 2, SimFix.FromInt(3), 7);
            var recipe = new WorldRecipe(Seed, MoverKind.InterimCrowd, Units().ToArray(), Array.Empty<BridgeCommand>()).WithMatch(odd);
            MatchPack copied = DemoRecipes.WithRespawn(recipe, 90).Match;
            SameExceptDelay(odd, copied);
            Assert.That(copied.RespawnTicks, Is.EqualTo(90));
            Assert.That(odd.RespawnTicks, Is.EqualTo(7));
        }

        [Test]
        public void C2WithRespawnZero_OnANoRespawnRecipe_KeepsTheDigest()
        {
            WorldRecipe block = DemoRecipes.MpBlockDemo();
            Assert.That(block.Match.RespawnTicks, Is.Zero);
            Assert.That(DemoRecipes.WithRespawn(block, 0).Match.PolicyDigest, Is.EqualTo(block.Match.PolicyDigest));
        }

        [Test]
        public void C2WithRespawn_WithoutMatchRules_Throws()
        {
            var recipe = new WorldRecipe(Seed, MoverKind.InterimCrowd, Units().ToArray(), Array.Empty<BridgeCommand>());
            Assert.Throws<InvalidOperationException>(() => DemoRecipes.WithRespawn(recipe, 300));
        }

        // (9e) The M7 MP respawn demo runs 600 ticks; whenever the frame shows a respawn state, the controlled unit is dead.
        [Test]
        public void C2MpRespawnDemo_Runs600Ticks_StateOnlyWhileDead()
        {
            SimSession session = SimSession.Create(DemoRecipes.MpRespawnDemo(), DemoRecipes.M7MapBytes());
            for (int i = 0; i < 600 && !session.IsFinished; i++)
            {
                session.StepOnce();
                FrameView frame = session.Current;
                if (frame.Respawn.State == RespawnPhase.None) continue;
                Assert.That(frame.TryGet(session.ControlledKey.Value, out ViewEntity unit), Is.True, "tick " + frame.Tick);
                Assert.That(unit.Alive, Is.False, "tick " + frame.Tick);
            }
            Assert.That(session.Tick, Is.EqualTo(600));
        }
    }
}
