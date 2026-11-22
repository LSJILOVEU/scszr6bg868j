using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using static WarCore.Tests.Stance.Stance1Worlds;

namespace WarCore.Tests.Stance
{
    // STANCE-1 S12: the "stance-wall" replay. Once squad 2 is down behind the 1 m wall no shot hits anybody (ticks 80..240);
    // before it crouches (tick 60) and after it stands again (tick 260 on) shots hit. The demo layer's hits wound (StanceDemo).
    public sealed class Stance1ReplayTests
    {
        [Test]
        public void S12_StanceWall_CrouchedBehindTheWallNobodyIsHit()
        {
            SimSession s = WorldBuilder.Build(StanceDemo.Recipe(), StanceDemo.MapBytes(), false);
            CombatSystem combat = CombatOf(s);
            var hits = new List<long>();
            for (int i = 0; i < 400; i++)
            {
                s.StepOnce();
                foreach (FireRecord f in combat.Fires)
                    if (f.Outcome == FireOutcome.Hit) hits.Add(s.Tick);
            }
            Assert.That(hits.FindAll(t => t >= 80 && t <= 240), Is.Empty);
            Assert.That(hits.FindAll(t => t < StanceDemo.CrouchTick), Is.Not.Empty, "hits before the crouch");
            Assert.That(hits.FindAll(t => t >= 260), Is.Not.Empty, "hits after standing up");
            Assert.That(s.StanceOf(5).Level, Is.EqualTo(0));
            // First recorded by STANCE-1 (a new fixture): the hit count and the state hash at tick 400.
            Assert.That(hits.Count, Is.EqualTo(128));
            Assert.That(s.StateHash, Is.EqualTo(7773801032435214032UL));
        }
    }
}
