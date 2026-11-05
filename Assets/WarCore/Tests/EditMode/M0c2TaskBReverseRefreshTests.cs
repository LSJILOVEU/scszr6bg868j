using System;
using System.Security.Cryptography;
using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2TaskBStatus"),Category("M0c2TaskBReverseRefresh")]
    public sealed class M0c2TaskBReverseRefreshTests
    {
        [Test] public void DistinctRowsReverseRefreshMatchesIndependentV2WorldAndSnapshot()
        {
            var sim=M0c2TaskBReverseRefreshFixture.Create();
            sim.Step(default);Assert.That(sim.Events.Read(1).Length,Is.EqualTo(2));
            Assert.That(sim.Events.Read(1)[0].Radius.Raw,Is.EqualTo(7));
            Assert.That(sim.Events.Read(1)[1].Radius.Raw,Is.EqualTo(9));
            byte[] first=sim.SaveRetainedSnapshot();ulong firstHash=sim.StateHashRetained();
            sim.Step(default);var events=sim.Events.Read(2);
            Assert.That(events.Length,Is.EqualTo(2),"Exactly one refresh per distinct row");
            Assert.That(events[0].Radius.Raw,Is.EqualTo(9),"Current action 1 precedes action 2 despite the old row tokens");
            Assert.That(events[1].Radius.Raw,Is.EqualTo(7));
            Assert.That(events[0].Tag,Is.EqualTo(2));Assert.That(events[1].Tag,Is.EqualTo(2));
            Assert.That(sim.GameplayStatusCount,Is.EqualTo(2));
            for(int i=0;i<2;i++)
            {
                var status=sim.GameplayStatusAt(i);
                Assert.That(status.Key.Token,Is.EqualTo((ulong)i));Assert.That(status.StartTick,Is.EqualTo(1));
                Assert.That(status.Stacks,Is.EqualTo(1));Assert.That(status.SourceSnapshot.CapturedTick,Is.EqualTo(2));
                Assert.That(status.Attribution.ActionOrdinal,Is.EqualTo((uint)(2-i)));
            }
            var vector=VectorJson.Load("m0c2-s4-reverse-refresh-v2.json");var cases=vector["cases"];
            var schema=M0c2TaskBReverseRefreshFixture.Schema();
            Assert.That(schema.RulesDigest.ToString("X16"),Is.EqualTo(vector["rules"].Text));
            Assert.That(schema.CapacityDigest.ToString("X16"),Is.EqualTo(vector["capacity"].Text));
            Check(first,firstHash,cases[0]);Check(sim.SaveRetainedSnapshot(),sim.StateHashRetained(),cases[1]);
            var restored=M0c2TaskBReverseRefreshFixture.Create();restored.RestoreRetainedSnapshot(sim.SaveRetainedSnapshot(),true);
            Check(restored.SaveRetainedSnapshot(),restored.StateHashRetained(),cases[1]);
            Assert.That(cases[1]["world_hash"].Text,Is.Not.EqualTo(vector["legacy_tick2"]["world_hash"].Text));
            Assert.That(cases[1]["snapshot_sha256"].Text,Is.Not.EqualTo(vector["legacy_tick2"]["snapshot_sha256"].Text));
            Assert.That(cases[1]["gameplay_sha256"].Text,Is.EqualTo(vector["legacy_tick2"]["gameplay_sha256"].Text));
        }
        private static void Check(byte[] snapshot,ulong hash,VectorJson row)
        {
            Assert.That(hash.ToString("X16"),Is.EqualTo(row["world_hash"].Text),"Independent retained world hash at tick "+row["tick"].Integer);
            using(var sha=SHA256.Create())Assert.That(BitConverter.ToString(sha.ComputeHash(snapshot)).Replace("-",""),Is.EqualTo(row["snapshot_sha256"].Text),"Independent full snapshot SHA256");
            Assert.That(BitConverter.ToString(snapshot).Replace("-","").ToLowerInvariant(),Is.EqualTo(row["snapshot"].Text),"Independent full snapshot bytes");
        }
    }
}
