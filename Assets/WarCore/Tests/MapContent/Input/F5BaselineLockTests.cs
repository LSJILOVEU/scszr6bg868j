using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // Recorded on the unmodified base before any F5 production change: a script that only uses the
    // five original PlayerInput fields must keep every tick hash and the consumed PlayerLog.
    public sealed class F5BaselineLockTests
    {
        private const int Ticks = 300;
        private const long ClickAt = 150; // LivePlayerInput.Apply(pressed) before stepping tick 151.

        private static readonly (long AtTick, PlayerInput Input)[] Script = {
            (1, new PlayerInput(1000, 0, 20000, 15000, false)),
            (20, new PlayerInput(0, 1000, 20000, 15000, true)),
            (40, new PlayerInput(0, 1000, 25000, 12000, true)),
            (60, new PlayerInput(-500, 500, 25000, 12000, false)),
            (90, new PlayerInput(-500, 500, 30000, 18000, true)),
            (120, new PlayerInput(0, 0, 30000, 18000, false)),
            (200, new PlayerInput(300, 0, 18000, 22000, false)),
            (240, new PlayerInput(300, 0, 18000, 22000, true)),
            (270, default)
        };

        private static readonly PlayerInput Clicked = new PlayerInput(700, -700, 12000, 9000, false);

        internal static ulong[] Run(out SimSession session)
        {
            var w = F5World.World();
            session = w.Session;
            foreach (var item in Script) session.SetPlayerInput(item.AtTick, item.Input);
            var live = new LivePlayerInput();
            var hashes = new ulong[Ticks];
            for (int tick = 1; tick <= Ticks; tick++)
            {
                if (session.Tick == ClickAt) live.Apply(session, Clicked, true);
                session.StepOnce();
                hashes[tick - 1] = session.StateHash;
            }
            return hashes;
        }

        [Test]
        public void OldFieldsOnlyScript_KeepsEveryTickHash_AndPlayerLog()
        {
            ulong[] hashes = Run(out SimSession session);
            Assert.That(hashes, Has.Length.EqualTo(F5BaselineData.Hashes.Length));
            Assert.That(session.Tick, Is.EqualTo(Ticks));
            for (int i = 0; i < Ticks; i++)
                Assert.That(hashes[i], Is.EqualTo(F5BaselineData.Hashes[i]), "tick=" + (i + 1));
            var log = session.PlayerLog;
            Assert.That(log.Count, Is.EqualTo(F5BaselineData.Log.Length));
            for (int i = 0; i < log.Count; i++)
            {
                var e = F5BaselineData.Log[i];
                var r = log[i];
                Assert.That((r.AtTick, r.Input.MoveXMilli, r.Input.MoveZMilli, r.Input.AimXMm, r.Input.AimZMm, r.Input.Primary),
                    Is.EqualTo((e.AtTick, e.MoveX, e.MoveZ, e.AimX, e.AimZ, e.Primary)), "log " + i);
            }
        }
    }
}
