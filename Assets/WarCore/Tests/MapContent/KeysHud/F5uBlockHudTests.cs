using System;
using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // F5u (3)(4): the HUD's Blocking line follows the Sim's held block, and reading it changes nothing.
    public sealed class F5uBlockHudTests
    {
        private const ulong Player = 1, Mate = 2;
        // Unit 1 spawns at (6, 41) m. Aiming 5 m ahead on the ground keeps PlayerFire on its Hold branch (M7 has no facing).
        private static PlayerInput Hold(bool block) => new PlayerInput(0, 0, 11000, 41000, false, block);

        private static bool HasBlockingLine(string text) => Array.IndexOf(text.Split('\n'), "Blocking") >= 0;

        private static string Hud(SimSession session, ulong key, UnitStatus status) =>
            HudLines.Build(session.Current, session.StateHash, key, status, new GroundPick(1f, 2f));

        [Test]
        public void F5uBlocking_FollowsTheHeldBlockNotTheKey()
        {
            SimSession session = SimSession.Create(DemoRecipes.MpBlockDemo(), DemoRecipes.M7MapBytes());
            Assert.That(session.ControlledKey, Is.EqualTo(Player));
            session.SetPlayerInput(1, Hold(true));
            session.SetPlayerInput(10, Hold(false));
            int held = 0;
            while (session.Tick < 30)
            {
                session.StepOnce();
                bool expected = session.Tick < 10;
                Assert.That(session.TryUnitStatus(Player, out UnitStatus status), Is.True);
                Assert.That(status.Blocking, Is.EqualTo(expected), $"player tick {session.Tick}");
                Assert.That(HasBlockingLine(Hud(session, Player, status)), Is.EqualTo(expected), $"player HUD tick {session.Tick}");
                Assert.That(Hud(session, Player, status), expected ? Does.Contain("Blocking") : Does.Not.Contain("Blocking"));
                if (status.Blocking) held++;

                Assert.That(session.TryUnitStatus(Mate, out UnitStatus mate), Is.True);
                Assert.That(mate.Blocking, Is.False, $"mate tick {session.Tick}");
                Assert.That(Hud(session, Mate, mate), Does.Not.Contain("Blocking"), $"mate HUD tick {session.Tick}");
            }
            Assert.That(held, Is.EqualTo(9));
        }

        // The Blocking line is a line of its own right after the Follow line; nothing else in the text moves.
        [Test]
        public void F5uBlockingLine_IsInsertedAfterFollowAsItsOwnLine()
        {
            var status = new UnitStatus(100f, 100f, 30, 0, false, true, true, true);
            var frame = new FrameView(600, System.Array.Empty<ViewEntity>());
            Assert.That(HudLines.Build(frame, 0x8D7460197CEA0D74UL, 1, status, new GroundPick(12.34f, 56.78f)),
                Is.EqualTo("Tick 600\nStateHash 8D7460197CEA0D74\nFollow #1  HP 100/100  Ammo 30\nBlocking\nPoints\nGround (12.3, 56.8)"));
            var notBlocking = new UnitStatus(100f, 100f, 30, 0, false, true, true, false);
            Assert.That(HudLines.Build(frame, 0x8D7460197CEA0D74UL, 1, notBlocking, null),
                Is.EqualTo("Tick 600\nStateHash 8D7460197CEA0D74\nFollow #1  HP 100/100  Ammo 30\nPoints\nGround -"));
            Assert.That(HudLines.Build(frame, 0x8D7460197CEA0D74UL, null, status, null),
                Is.EqualTo("Tick 600\nStateHash 8D7460197CEA0D74\nFollow -\nPoints\nGround -"));
        }

        [Test]
        public void F5uReadingStatusAndHudEveryTick_LeavesHashesUntouched()
        {
            SimSession plain = SimSession.Create(DemoRecipes.MpBlockDemo(), DemoRecipes.M7MapBytes());
            SimSession read = SimSession.Create(DemoRecipes.MpBlockDemo(), DemoRecipes.M7MapBytes());
            plain.SetPlayerInput(1, Hold(true));
            read.SetPlayerInput(1, Hold(true));
            int blocking = 0;
            for (int tick = 0; tick < 300; tick++)
            {
                plain.StepOnce(); read.StepOnce();
                Assert.That(read.TryUnitStatus(Player, out UnitStatus status), Is.True);
                HudLines.Build(read.Current, read.StateHash, Player, status, new GroundPick(1f, 2f));
                if (status.Blocking) blocking++;
                Assert.That(read.StateHash, Is.EqualTo(plain.StateHash), $"tick {tick + 1}");
            }
            Assert.That(blocking, Is.EqualTo(300));
            Assert.That(read.CommandLog, Is.Empty);
        }
    }
}
