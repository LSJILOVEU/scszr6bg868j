using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt003TargetSuppressStateTests
    {
        [Test]
        public void Load_ZeroSeen_RoundTrips()
        {
            M6World w = A3b.World(A3b.Ai(300, 100));
            TargetSuppress s = w.System<TargetSuppress>();
            var writer = new SimStateWriter();
            writer.Int32(100); writer.Int32(1);
            writer.UInt64(A3b.IdA); writer.Int64(0); writer.Int64(1);
            byte[] bytes = writer.ToArray();

            Assert.DoesNotThrow(() => s.LoadState(new SimStateReader(bytes)));
            var saved = new SimStateWriter();
            s.SaveState(saved);
            Assert.That(saved.ToArray(), Is.EqualTo(bytes));
        }
    }
}
