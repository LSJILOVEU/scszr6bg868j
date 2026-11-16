using NUnit.Framework;
using WarCore.Battle;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt170TargetMemoryTests
    {
        [Test]
        public void Step_DeletesDeadObserverMemory()
        {
            M6World w = A3.World(new[] { A3.Observer(), A3.Target(A3.KeyB, 2, A2.Cell(21, 18)) }, 60);
            long first = A3.FirstThink(w, A3.KeyA), death = first + 1;
            Wp16TestWorld.KillAt(w, death, A3.KeyA);
            for (long t = 1; t <= death; t++)
                w.Sim.Step(t == 1 ? A3.Command(SquadCommandKind.Hold, 2) : default);

            Assert.That(w.Dead(A3.KeyA), Is.True);
            Assert.That(w.Dead(A3.KeyB), Is.False);
            Assert.That(A3.Recall(w, A3.KeyA, death, out StableId target, out _, out long seen), Is.True);
            Assert.That(target, Is.EqualTo(A3.Id(w, A3.KeyB)));
            Assert.That(seen, Is.EqualTo(first));

            // Death settles after memory cleanup; run cleanup before the AI can forget the observer.
            w.System<TargetMemory>().Step(w.Sim.Context);

            Assert.That(A3.Recall(w, A3.KeyA, death, out target, out SimVec3 at, out seen), Is.False);
            Assert.That(target, Is.EqualTo(default(StableId)));
            Assert.That(at, Is.EqualTo(default(SimVec3)));
            Assert.That(seen, Is.EqualTo(0L));
        }
    }
}
