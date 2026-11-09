using NUnit.Framework;
using WarCore.Combat;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // A4 acceptance 1: an aimed intent flies along the 3D line to its point (CombatSystem intent branch); the zero rule looks at the
    // horizontal part only. Tick 1, first shot, no spread unless stated. Values are raw (65536 = 1 m).
    public sealed class A4IntentTests
    {
        private static FireRecord First(byte[] map, UnitSpawn[] units, System.Func<M6World, SimVec3> point, int spread = 0)
        {
            var (w, aim) = A4.IntentWorld(map, units, spread);
            aim.Point = point(w);
            var shots = A4.Shots(w, 1);
            Assert.That(shots.Count, Is.EqualTo(1), "one shot");
            Assert.That(shots[0].tick, Is.EqualTo(1));
            return shots[0].fire;
        }

        private static SimVec3 Pos(M6World w, ulong key) => w.Read<Position>(key).Value;
        private static void End(FireRecord f, long x, long y, long z)
        {
            Assert.That(f.End.X.Raw, Is.EqualTo(x), "End.X"); Assert.That(f.End.Y.Raw, Is.EqualTo(y), "End.Y"); Assert.That(f.End.Z.Raw, Is.EqualTo(z), "End.Z");
        }

        [Test]
        public void A4Intent_T1_UphillAtTargetCentre_Hits()
        {
            FireRecord f = First(A4.RidgeMap(), new[] { A4.Unit(1, 1, 3500, 0), A4.Unit(2, 2, 9500, -1) }, w => Pos(w, 2));
            Assert.That(f.Outcome, Is.EqualTo(FireOutcome.Hit));
            Assert.That(f.Target, Is.EqualTo(Wp18TestWorld.Id(2)));
            End(f, 606204, 109908, 294912);
            Assert.That(f.Distance.Raw, Is.EqualTo(393216));
        }

        [Test]
        public void A4Intent_T1g_PointOnGroundZero_IsBlockedByTheSlope()
        {
            FireRecord f = First(A4.RidgeMap(), new[] { A4.Unit(1, 1, 3500, 0), A4.Unit(2, 2, 9500, -1) },
                w => new SimVec3(Pos(w, 2).X, default, Pos(w, 2).Z));
            Assert.That(f.Outcome, Is.EqualTo(FireOutcome.WallBlocked));
            End(f, 491520, 0, 294912);
        }

        [Test]
        public void A4Intent_T2_DownhillAtTargetCentre_Hits()
        {
            FireRecord f = First(A4.RidgeMap(), new[] { A4.Unit(1, 1, 9500, 0), A4.Unit(2, 2, 3500, -1) }, w => Pos(w, 2));
            Assert.That(f.Outcome, Is.EqualTo(FireOutcome.Hit));
            Assert.That(f.Target, Is.EqualTo(Wp18TestWorld.Id(2)));
            End(f, 245764, 4780, 294912);
        }

        [Test]
        public void A4Intent_T1_Spread_SixShotsAllHitAboveGround()
        {
            var (w, aim) = A4.IntentWorld(A4.RidgeMap(), new[] { A4.Unit(1, 1, 3500, 0), A4.Unit(2, 2, 9500, -1) }, 2000);
            aim.Point = Pos(w, 2);
            var shots = A4.Shots(w, 6);
            Assert.That(shots.Count, Is.EqualTo(6));
            long[] ticks = { 1, 31, 61, 91, 121, 151 };
            bool offLine = false;
            for (int i = 0; i < 6; i++)
            {
                FireRecord f = shots[i].fire;
                Assert.That(shots[i].tick, Is.EqualTo(ticks[i]));
                Assert.That(f.Outcome, Is.EqualTo(FireOutcome.Hit), "shot " + i);
                Assert.That(f.End.Y.Raw, Is.GreaterThan(0), "shot " + i);
                if (f.End.Z.Raw != 294912) offLine = true;
            }
            Assert.That(offLine, Is.True, "spread turned at least one shot off the z line");
        }

        [Test]
        public void A4Intent_T4_OverATeammateOnLowGround_Hits()
        {
            FireRecord f = First(A4.PlateauMap(), new[] { A4.Unit(1, 1, 3500, 0), A4.Unit(3, 1, 11500, -1), A4.Unit(2, 2, 20500, -1) },
                w => Pos(w, 2));
            Assert.That(f.Outcome, Is.EqualTo(FireOutcome.Hit));
            Assert.That(f.Target, Is.EqualTo(Wp18TestWorld.Id(2)));
            End(f, 1327101, 129139, 294912);
            Assert.That(f.Distance.Raw, Is.EqualTo(1114112));
        }

        [Test]
        public void A4Intent_F1_FlatSameHeight_IsTheHorizontalShot()
        {
            FireRecord f = First(A4.FlatTwin(), new[] { A4.Unit(1, 1, 3500, 0), A4.Unit(2, 2, 9500, -1) }, w => Pos(w, 2));
            Assert.That(f.Outcome, Is.EqualTo(FireOutcome.Hit));
            End(f, 606208, 0, 294912);
            Assert.That(f.Distance.Raw, Is.EqualTo(393216));
        }

        [Test]
        public void A4Intent_F2_SlightlyHigh_StillHits()
        {
            FireRecord f = First(A4.FlatTwin(), new[] { A4.Unit(1, 1, 3500, 0), A4.Unit(2, 2, 9500, -1) },
                w => Pos(w, 2) + new SimVec3(default, SimFix.Ratio(1, 5), default));
            Assert.That(f.Outcome, Is.EqualTo(FireOutcome.Hit));
            End(f, 606204, 12559, 294912);
        }

        // Locks the opposite of the old "the point's Y is ignored" (Wp13AimedTests before A4).
        [Test]
        public void A4Intent_F3_TooHigh_PassesOver()
        {
            FireRecord f = First(A4.FlatTwin(), new[] { A4.Unit(1, 1, 3500, 0), A4.Unit(2, 2, 9500, -1) },
                w => Pos(w, 2) + new SimVec3(default, SimFix.FromInt(3), default));
            Assert.That(f.Outcome, Is.EqualTo(FireOutcome.Open));
            Assert.That(f.Target, Is.EqualTo(default(StableId)));
            End(f, 1987886, 879240, 294912);
            Assert.That(f.Distance.Raw, Is.EqualTo(1758510));
        }

        [Test]
        public void A4Intent_F4_StraightAbove_DoesNotFire()
        {
            var (w, aim) = A4.IntentWorld(A4.FlatTwin(), new[] { A4.Unit(1, 1, 3500, 0), A4.Unit(2, 2, 9500, -1) });
            aim.Point = Pos(w, 1) + new SimVec3(default, SimFix.FromInt(50), default);
            Combatant before = w.Read<Combatant>(1);
            ulong rng = A4.Rng(w);
            for (int t = 0; t < 60; t++)
            {
                w.Step();
                Assert.That(w.Combat.Fires.Length, Is.EqualTo(0), "tick " + w.Sim.Clock.Tick);
            }
            Combatant after = w.Read<Combatant>(1);
            Assert.That(after.Ammo, Is.EqualTo(before.Ammo));
            Assert.That(after.DueMilli, Is.EqualTo(before.DueMilli));
            Assert.That(after.SpreadStored, Is.EqualTo(before.SpreadStored));
            Assert.That(after.SpreadLastMilli, Is.EqualTo(before.SpreadLastMilli));
            Assert.That(A4.Rng(w), Is.EqualTo(rng));
        }
    }
}
