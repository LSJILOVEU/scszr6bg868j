using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt171TargetMemoryHashTests
    {
        [Test]
        public void WriteState_EmptyMemoryIncludesSchemaVersion()
        {
            M6World w = A3.World(new[] { A3.Observer() }, 60);
            var expected = new StateHash();
            expected.Add("battle.ai-contacts"); expected.Add(1); expected.Add(60); expected.Add(0);
            var actual = new StateHash();
            w.System<TargetMemory>().WriteState(actual);
            Assert.That(actual.Value, Is.EqualTo(expected.Value));
        }

        [Test]
        public void WriteState_RememberedPositionAndSightingTickMatchFieldOrder()
        {
            M6World w = A3.World(new[] { A3.Observer(), A3.Target(A3.KeyB, 2, A2.Cell(21, 18)) }, 60);
            TargetMemory memory = w.System<TargetMemory>();
            ulong observer = A3.Id(w, A3.KeyA).Value, target = A3.Id(w, A3.KeyB).Value;
            var writer = new SimStateWriter();
            writer.Int32(60); writer.Int32(1);
            writer.UInt64(observer); writer.UInt64(target);
            writer.Int64(11); writer.Int64(22); writer.Int64(33); writer.Int64(7);
            memory.LoadState(new SimStateReader(writer.ToArray()));

            var expected = new StateHash();
            expected.Add("battle.ai-contacts"); expected.Add(1); expected.Add(60); expected.Add(1);
            expected.Add(observer); expected.Add(target);
            expected.Add(11L); expected.Add(22L); expected.Add(33L); expected.Add(7L);
            var actual = new StateHash();
            memory.WriteState(actual);
            Assert.That(actual.Value, Is.EqualTo(expected.Value));
        }
    }
}
