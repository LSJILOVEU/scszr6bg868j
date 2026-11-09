using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 1(b), recorded on the code before P3 (the merge of the P2 branch and main) with the fast rifle and no
    // burst field: (b1) the reaction time 15, (b2) the sense interval 10, (b3) nothing registered. Recorded: the 300-tick
    // run (64-bit FNV-1a over the per-tick hashes), the state hash after tick 300, A's fire ticks (also checked against
    // the fixture's rhythm model with B = 0), the retained snapshot at 300 (length, digest) and the system table.
    public sealed class P3BaselineTests
    {
        internal const string Systems = P2BaselineTests.Systems;

        // (b1) { "reactionTicks": 15 }: the first shot on 40 + 15, then every 5 ticks, a 60-tick reload after 10 rounds.
        internal static readonly long[] Fires15 =
        {
            55, 60, 65, 70, 75, 80, 85, 90, 95, 100, 160, 165, 170, 175, 180, 185, 190, 195, 200, 205,
            265, 270, 275, 280, 285, 290, 295, 300,
        };
        internal const ulong Run15 = 1223441943531193618UL, End15 = 296936124191856758UL, Snapshot15Digest = 6865121131794261665UL;
        internal const int Snapshot15Length = 6751;

        // (b2) { "reactionTicks": 0, "senseTicks": 10 }: the first shot on A's first sample tick 46 (phase 4).
        internal static readonly long[] Fires10 =
        {
            46, 51, 56, 61, 66, 71, 76, 81, 86, 91, 151, 156, 161, 166, 171, 176, 181, 186, 191, 196,
            256, 261, 266, 271, 276, 281, 286, 291, 296,
        };
        internal const ulong Run10 = 16196317600479795563UL, End10 = 11899379392356105329UL, Snapshot10Digest = 15795551298508101251UL;
        internal const int Snapshot10Length = 6539;

        // (b3) { "reactionTicks": 0 }: nothing registered, the built-in decision fires from 40 on.
        internal static readonly long[] Fires0 =
        {
            40, 45, 50, 55, 60, 65, 70, 75, 80, 85, 145, 150, 155, 160, 165, 170, 175, 180, 185, 190,
            250, 255, 260, 265, 270, 275, 280, 285, 290, 295,
        };
        internal const ulong Run0 = 15325805454858870110UL, End0 = 18341873402848274040UL, Snapshot0Digest = 16193512772568994956UL;
        internal const int Snapshot0Length = 6347;

        internal static void Baseline(string ai, long first, long[] fires, ulong run, ulong end, int length, ulong digest, string systems, int? version)
        {
            P3.Trace r = null;
            Assert.DoesNotThrow(() => r = P3.Run(P2.Cast(), P1.Walk, ai, 300));
            Assert.That(P3.Systems(r.W), Is.EqualTo(systems));
            ReactionFire fire = r.W.System<ReactionFire>();
            if (version == null) Assert.That(fire, Is.Null);
            else Assert.That(fire.StateVersion, Is.EqualTo(version.Value));
            Assert.That(r.A.FirstSelect(r.Id(P3.KeyB)), Is.EqualTo(P1.T), "A first picks B on t");
            Assert.That(r.A.Due[(int)P1.T - 1], Is.EqualTo(0L), "A is ready on t");
            Assert.That(r.A.Ammo[(int)P1.T - 1], Is.EqualTo(P3.Magazine), "a full magazine on t");
            List<long> shots = r.A.FireTicks(300);
            Assert.That(shots, Is.EqualTo(fires), "A's shots");
            Assert.That(shots, Is.EqualTo(P3.Expect(first, 0, 0, 300)), "A's shots against the model with B = 0");
            Assert.That(P2.Fold(r.Hashes), Is.EqualTo(run), "the 300-tick run");
            Assert.That(r.Hashes[300], Is.EqualTo(end), "the hash after 300");
            byte[] snapshot = r.W.Sim.SaveRetainedSnapshot();
            Assert.That(snapshot.Length, Is.EqualTo(length), "snapshot length, tick 300");
            Assert.That(P3.Digest(snapshot), Is.EqualTo(digest), "snapshot digest, tick 300");
        }

        [Test]
        public void ReactionOnly_FastRifle_Unchanged() =>
            Baseline(P3.Ai(15), P1.T + 15, Fires15, Run15, End15, Snapshot15Length, Snapshot15Digest, Systems, 1);

        [Test]
        public void IntervalOnly_FastRifle_Unchanged() =>
            Baseline(P3.Ai(0, 10), P2TimingTests.TsA, Fires10, Run10, End10, Snapshot10Length, Snapshot10Digest, Systems, 2);

        [Test]
        public void Nothing_FastRifle_Unchanged() =>
            Baseline(P3.Ai(0), P1.T, Fires0, Run0, End0, Snapshot0Length, Snapshot0Digest, P1BaselineTests.Systems, null);
    }
}
