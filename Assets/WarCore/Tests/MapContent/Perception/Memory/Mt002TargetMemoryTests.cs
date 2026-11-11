using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt002TargetMemoryTests
    {
        [Test]
        public void ObserverDestroyed_DeletesUnexpiredEntry()
        {
            M6World w = A3.World(new[] { A3.Observer(), A3.Target(A3.KeyB, 2, A2.Cell(21, 18)) }, 60);
            w.Sim.Step(A3.Command(SquadCommandKind.Hold, 2));
            w.Step((int)A3.FirstThink(w, A3.KeyA) - 1);
            Assert.That(A3.Count(w), Is.EqualTo(1));
            Assert.That(A3.Recall(w, A3.KeyA, w.Sim.Clock.Tick, out _, out _, out long seen), Is.True);
            Assert.That(seen, Is.EqualTo(w.Sim.Clock.Tick));

            w.Sim.Entities.Destroy(w.Unit(A3.KeyA));
            w.System<TargetMemory>().Step(w.Sim.Context);

            Assert.That(A3.Count(w), Is.EqualTo(0));
        }
    }
}
