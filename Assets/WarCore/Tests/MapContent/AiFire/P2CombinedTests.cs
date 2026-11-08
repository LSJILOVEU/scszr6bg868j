using System.Numerics;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 12: the sense interval with the target memory (A3) and the search (A3a) in one world: K = 40, R = 15,
    // memory 300, search 120. B walks in along z = 10, is picked on 40 and shot on 81 and 111 (A samples on 66, 106); from
    // 82 B walks north-west to cell (12, 16) and the wall hides it from 138 = g. A keeps B and fires on x = 141 (ready from
    // the shot on 111) at B's current position Q, not at the memory's frozen point P (where B was last seen on A's thinking
    // tick 136); the sample tick ns = 146 clears the entry and the search orders A to P on A's first thinking tick after g.
    public sealed class P2CombinedTests
    {
        private const int K = 40;
        private const long G = 138, X = 141, Ns = 146, Seen = 136, OrderTick = 146, ThinkAfterNs = 156;
        private const int AmmoG = 1; private const long DueG = 141000;
        internal const string Systems = "bridge.dead-cleanup|0|5;battle.reinforce|0|8;interim.orders|0|10;battle.ai|0|12;"
            + "battle.ai-contacts|0|13;battle.ai-fire|0|14;battle.ai-search|0|17;"
            + "interim.locomotion|1|20;combat.system|3|30;battle.capture|3|40;battle.match|3|50";
        private static readonly P1.Move[] Script = { new P1.Move(20, 2, A2.Cell(8, 10)), new P1.Move(82, 2, A2.Cell(12, 16)) };
        private static string Ai => P2.AiAll(300, 120, 15, K);

        // The sine of the angle between two horizontal vectors is |cross| / (|u| |v|); a heading rounded to the fixed-point
        // scale is off by at most about 1 / Scale. "Along" allows sine <= 2 / Scale, "Away" demands a hundred times more;
        // both compare cross^2 * Scale^2 with 4 * |u|^2 * |v|^2 cross-multiplied in BigInteger, nothing is truncated.
        // The cross product itself is a long: every raw coordinate here is below 2^21, so a product stays below 2^42.
        private static bool Along(SimVec3 u, SimVec3 v) => CrossSquaredScaled(u, v) <= Bound(u, v);
        private static bool Away(SimVec3 u, SimVec3 v) => CrossSquaredScaled(u, v) > Bound(u, v) * 10000;
        private static BigInteger CrossSquaredScaled(SimVec3 u, SimVec3 v)
        {
            BigInteger cross = new BigInteger(P2.Cross(u, v));
            return cross * cross * SimFix.Scale * SimFix.Scale;
        }
        private static BigInteger Bound(SimVec3 u, SimVec3 v)
        {
            BigInteger lu = new BigInteger(u.X.Raw) * u.X.Raw + new BigInteger(u.Z.Raw) * u.Z.Raw;
            BigInteger lv = new BigInteger(v.X.Raw) * v.X.Raw + new BigInteger(v.Z.Raw) * v.Z.Raw;
            return 4 * lu * lv;
        }
        private static long Dot(SimVec3 u, SimVec3 v) => u.X.Raw * v.X.Raw + u.Z.Raw * v.Z.Raw;

        [Test]
        public void ThreeSystems_RunTogether_BothOrders()
        {
            P2.Trace forward = null, reverse = null;
            Assert.DoesNotThrow(() => forward = P2.Run(P2.Cast(), Script, Ai, 600));
            Assert.DoesNotThrow(() => reverse = P2.Run(P2.Cast(), Script, Ai, 600, true));
            Assert.That(P2.Systems(forward.W), Is.EqualTo(Systems));
            for (int t = 0; t <= 600; t++) Assert.That(reverse.Hashes[t], Is.EqualTo(forward.Hashes[t]), "tick " + t);
        }

        [Test]
        public void KeptTarget_IsShotAtItsCurrentPosition_NotTheFrozenPoint()
        {
            P2.Trace r = P2.Run(P2.Cast(), Script, Ai, 200);
            ulong a = r.Id(P2.KeyA), b = r.Id(P2.KeyB);
            Assert.That(r.A.FirstSelect(b), Is.EqualTo(P1.T));
            for (long t = P1.T; t < G; t++) Assert.That(r.A.Sel[(int)t], Is.EqualTo(b), "B picked, tick " + t);
            Assert.That(r.A.Sel[(int)G], Is.EqualTo(0UL), "B hidden on g");
            Assert.That(P2.FirstSample(a, P1.T, K), Is.EqualTo(66L));
            Assert.That(r.A.FireTicks(G), Is.EqualTo(new long[] { 81, 111 }), "the shots before g");
            Assert.That(P2.Samples(a, G, K), Is.False, "g is not a sample tick");
            long ns = P2.FirstSample(a, G + 1, K);
            Assert.That(ns, Is.EqualTo(Ns));
            Assert.That(r.A.Ammo[(int)G], Is.EqualTo(AmmoG));
            Assert.That(r.A.Due[(int)G], Is.EqualTo(DueG));
            long x = (DueG + 999) / 1000;
            Assert.That(x, Is.EqualTo(X));
            Assert.That(x, Is.GreaterThan(G).And.LessThan(ns));
            Assert.That(r.A.Fire[(int)x], Is.True, "the window shot");
            Assert.That(r.A.Sel[(int)x], Is.EqualTo(0UL));
            Assert.That(r.A.Target[(int)x], Is.EqualTo(0UL), "the shot is stopped by the wall");
            Assert.That(r.Counts[(int)x], Is.EqualTo(1));
            Assert.That(r.Counts[(int)ns - 1], Is.EqualTo(1));
            Assert.That(r.Counts[(int)ns], Is.EqualTo(0), "cleared on the sample tick");

            // The geometry on x, from a second run of the same world (the memory and the search are read along the way).
            M6World w = P2.World(P2.Cast(), Ai);
            Assert.That(A3.FirstThink(w, P2.KeyA), Is.EqualTo(6L), "A thinks on 6, 16, 26, ...");
            SimVec3 p = default, q = default, at = default, shot = default; long seen = -1, ordered = -1; SimVec3 orderTo = default;
            FireOutcome outcome = FireOutcome.Hit;
            for (long tick = 1; tick <= ThinkAfterNs; tick++)
            {
                w.Sim.Step(P2.Drive(w, tick, Script));
                if (ordered < 0 && A3a.Order(w, out SimVec3 to)) { ordered = tick; orderTo = to; }
                if (tick == x)
                {
                    Assert.That(A3.Recall(w, P2.KeyA, tick, out StableId remembered, out p, out seen), Is.True, "the memory holds B");
                    Assert.That(remembered.Value, Is.EqualTo(b));
                    q = w.Read<Position>(P2.KeyB).Value; at = w.Read<Position>(P2.KeyA).Value;
                    Assert.That(w.Combat.Fires.Length, Is.EqualTo(1));
                    FireRecord f = w.Combat.Fires[0];
                    Assert.That(f.Shooter.Value, Is.EqualTo(a));
                    shot = P2.Flat(f.End - f.Origin); outcome = f.Outcome;
                }
                if (tick == ThinkAfterNs)
                {
                    Assert.That(A3a.Order(w, out SimVec3 still), Is.True, "A is on its way to P");
                    Assert.That((still.X.Raw, still.Z.Raw), Is.EqualTo((p.X.Raw, p.Z.Raw)));
                }
            }
            Assert.That(seen, Is.EqualTo(Seen), "P is where B stood on A's last thinking tick with B in sight");
            Assert.That((p.X.Raw, p.Z.Raw), Is.Not.EqualTo((q.X.Raw, q.Z.Raw)), "P differs from Q");
            Assert.That(at, Is.EqualTo(A3.AAt), "A has not moved yet");
            SimVec3 toP = P2.Flat(p - at), toQ = P2.Flat(q - at);
            Assert.That(P2.Cross(toP, toQ), Is.Not.EqualTo(0L), "P and Q are not on one line from A");
            Assert.That(Away(toP, toQ), Is.True);
            // The heading is checked before the outcome: a shot at another point may miss the wall and still be wrong.
            Assert.That(Dot(shot, toQ), Is.GreaterThan(0L), "the shot goes towards B, not away from it");
            Assert.That(Along(shot, toQ), Is.True, "the shot heads for B's current position");
            Assert.That(Away(shot, toP), Is.True, "not for the frozen point");
            Assert.That(outcome, Is.EqualTo(FireOutcome.WallBlocked), "and the wall stops it");
            Assert.That(ordered, Is.EqualTo(OrderTick), "the search orders A on its first thinking tick after g");
            Assert.That((orderTo.X.Raw, orderTo.Z.Raw), Is.EqualTo((p.X.Raw, p.Z.Raw)), "to P");
        }
    }
}
