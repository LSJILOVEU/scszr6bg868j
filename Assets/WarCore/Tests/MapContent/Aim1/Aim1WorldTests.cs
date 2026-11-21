using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Aim1.Aim1Worlds;

namespace WarCore.Tests.Aim1
{
    // AIM-1 A5 (standing equals off), A8 (snapshots and determinism) and A9 (the aim-run fixture).
    public sealed class Aim1WorldTests
    {
        // Four against four, 30 m apart, nobody ordered to move.
        private static SimSession Standoff(string layer) => World(Load(layer), new[]
        {
            Unit(1, 1, 1, 35000, 10000), Unit(2, 2, 1, 45000, 10000), Unit(3, 3, 1, 55000, 10000), Unit(4, 4, 1, 65000, 10000),
            Unit(5, 5, 2, 35000, 40000), Unit(6, 6, 2, 45000, 40000), Unit(7, 7, 2, 55000, 40000), Unit(8, 8, 2, 65000, 40000),
        });

        private static string Record(in FireRecord f) =>
            f.Shooter.Value + ">" + f.Target.Value + " " + f.Origin.X.Raw + "," + f.Origin.Y.Raw + "," + f.Origin.Z.Raw + " -> "
            + f.End.X.Raw + "," + f.End.Y.Raw + "," + f.End.Z.Raw + " d" + f.Distance.Raw + " dmg" + f.DamageRaw + " " + f.Outcome;

        private static List<string> Fires(SimSession s, int ticks)
        {
            var list = new List<string>();
            for (int tick = 1; tick <= ticks; tick++)
            {
                s.StepOnce();
                foreach (FireRecord f in CombatOf(s).Fires) list.Add(tick + ": " + Record(f));
            }
            return list;
        }

        // A5: with nobody moving, every shot of rwr-aim is the shot of rwr-field (hashes differ: the pack digest differs).
        [TestCase("rwr-field", "rwr-aim")]
        [TestCase("aim1-harmless-field", "aim1-harmless")]
        public void A5_StandingStillIsLikeTheSwitchOff(string off, string on)
        {
            List<string> a = Fires(Standoff(off), 300), b = Fires(Standoff(on), 300);
            Assert.That(a.Count, Is.GreaterThan(8));
            Assert.That(b, Is.EqualTo(a));
        }

        // A8: a snapshot taken while units walk and shoot resumes to the same hash at every tick.
        private static SimSession Skirmish(string layer = "aim1-harmless") => World(Load(layer), new[]
        {
            Unit(1, 1, 1, 30000, 10000), Unit(2, 2, 1, 40000, 10000), Unit(3, 3, 2, 20000, 40000, Walk), Unit(4, 4, 2, 25000, 45000),
        }, new[] { BridgeCommand.MoveTo(1, 3, 90000, 40000), BridgeCommand.MoveTo(2, 4, 90000, 45000) });

        private static List<ulong> Hashes(SimSession s, int ticks)
        {
            var list = new List<ulong>();
            for (int i = 0; i < ticks; i++) { s.StepOnce(); list.Add(s.StateHash); }
            return list;
        }

        [Test]
        public void A8_ASnapshotWhileWalkingAndShootingResumes()
        {
            SimSession a = Skirmish();
            for (int i = 0; i < 40; i++) a.StepOnce();
            Assert.That(M3TestWorld.Position(a, 3).X.Raw, Is.GreaterThan(22 * S), "unit 3 is walking");
            Assert.That(CombatOf(a).ShotAngle(Id(a, 4), State(a, 4), 3000, a.Tick), Is.EqualTo(9000), "unit 4 is running");
            byte[] snapshot = SimOf(a).SaveRetainedSnapshot();
            List<ulong> expected = Hashes(a, 120);
            SimSession b = Skirmish();
            SimOf(b).RestoreRetainedSnapshot(snapshot);
            Assert.That(Hashes(b, 120), Is.EqualTo(expected));
        }

        [Test]
        public void A8_ASnapshotDuringAManualReloadResumes()
        {
            SimSession a = Skirmish();
            for (int i = 0; i < 10; i++) a.StepOnce();
            SetState(a, 1, new Combatant(1, 0, 12, 0, 0, 0));
            Assert.That(CombatOf(a).TryReload(Id(a, 1)), Is.True);
            for (int i = 0; i < 20; i++) a.StepOnce();
            Assert.That(State(a, 1).DueMilli, Is.EqualTo(11 * 1000 + 60000), "still reloading");
            byte[] snapshot = SimOf(a).SaveRetainedSnapshot();
            List<ulong> expected = Hashes(a, 60);
            SimSession b = Skirmish();
            SimOf(b).RestoreRetainedSnapshot(snapshot);
            Assert.That(State(b, 1).DueMilli, Is.EqualTo(11 * 1000 + 60000));
            Assert.That(Hashes(b, 60), Is.EqualTo(expected));
        }

        [Test]
        public void A8_TheSameSeedGivesTheSameWorldTwice() =>
            Assert.That(Hashes(Skirmish(), 150), Is.EqualTo(Hashes(Skirmish(), 150)));

        [Test]
        public void A8_OnlyAMoveSpreadPackRegistersTheMoveMark()
        {
            List<(string Id, int Order)> on = Systems(Skirmish()), off = Systems(Skirmish("aim1-harmless-field")),
                reloadOnly = Systems(Skirmish("aim1-reload-only"));
            Assert.That(on, Does.Contain(("combat.move-mark", 19)));
            Assert.That(on.FindIndex(e => e.Id == "combat.move-mark"), Is.LessThan(on.FindIndex(e => e.Id == "interim.locomotion")), "before the locomotion");
            Assert.That(off.Exists(e => e.Order == 19), Is.False);
            Assert.That(reloadOnly, Is.EqualTo(off), "manualReload alone adds no system");
            Assert.That(on.Count, Is.EqualTo(off.Count + 1));
        }

        [Test]
        public void A8_ASnapshotOfOneSideIsRefusedByTheOther()
        {
            SimSession on = Skirmish(), off = Skirmish("aim1-harmless-field");
            for (int i = 0; i < 20; i++) { on.StepOnce(); off.StepOnce(); }
            byte[] onBytes = SimOf(on).SaveRetainedSnapshot(), offBytes = SimOf(off).SaveRetainedSnapshot();
            Assert.That(() => SimOf(Skirmish("aim1-harmless-field")).RestoreRetainedSnapshot(onBytes), Throws.Exception);
            Assert.That(() => SimOf(Skirmish()).RestoreRetainedSnapshot(offBytes), Throws.Exception);
        }

        [Test]
        public void A8_TheMoveMarkKeepsNothing()
        {
            SimSession s = Skirmish();
            MoveMark mark = CombatOf(s).MoveMark;
            // A new table claims no tick: not even tick 0 for an id whose generation the empty table also holds (0).
            Assert.That(mark.TryBefore(new EntityId(Id(s, 3).Id, 0), 0, out _, out _), Is.False, "an empty table claims tick 0");
            for (int i = 0; i < 5; i++) s.StepOnce();
            Assert.That((mark.Order, mark.Phase, mark.StateTypeId, mark.StateVersion), Is.EqualTo((19, SimSystemPhase.Movement, "combat.move-mark", 1)));
            var writer = new SimStateWriter();
            mark.SaveState(writer);
            Assert.That(writer.ToArray(), Is.Empty);
            Assert.That(mark.TryBefore(Id(s, 3), s.Tick, out _, out _), Is.True);
            mark.LoadState(new SimStateReader(Array.Empty<byte>()));
            Assert.That(mark.TryBefore(Id(s, 3), s.Tick, out long x, out long z), Is.False, "a restore forgets the table");
            Assert.That((x, z), Is.EqualTo((0L, 0L)));
            Assert.That(mark.TryBefore(new EntityId(Id(s, 3).Id, Id(s, 3).Generation + 1), s.Tick - 1, out _, out _), Is.False);
            Assert.That(mark.TryBefore(new EntityId(-1, 0), s.Tick, out _, out _), Is.False, "outside the table");
            s.StepOnce();
            Assert.That(mark.TryBefore(new EntityId(Id(s, 3).Id, Id(s, 3).Generation + 1), s.Tick, out _, out _), Is.False, "another entity in the slot");
            Assert.That(mark.TryBefore(Id(s, 3), s.Tick, out long x3, out long z3), Is.True);
            SimVec3 now = M3TestWorld.Position(s, 3);
            long dx = now.X.Raw - x3, dz = now.Z.Raw - z3;
            Assert.That(dx * dx + dz * dz, Is.InRange(5000L * 5000L, 6554L * 6554L), "the walker's step this tick, at most 6553 raw");
            Assert.That(dx, Is.GreaterThan(4000L), "along +x");
        }

        // ---- A9: aim-run. Team 1 (keys 1-4) stands on z = 10 m; team 2 (keys 5-8) on z = 40 m, 30 m in front, either stands
        // or runs along +x at 6000 mm/s (the top band, 3000) for the whole run. Nobody takes damage. ----

        internal static SimSession AimRun(bool moving) => World(Load("aim1-harmless"), AimRunUnits(), moving ? AimRunScript() : null);

        internal static UnitSpawn[] AimRunUnits() => new[]
        {
            Unit(1, 1, 1, 35000, 10000), Unit(2, 2, 1, 45000, 10000), Unit(3, 3, 1, 55000, 10000), Unit(4, 4, 1, 65000, 10000),
            Unit(5, 5, 2, 5000, 40000), Unit(6, 6, 2, 13000, 40000), Unit(7, 7, 2, 21000, 40000), Unit(8, 8, 2, 29000, 40000),
        };

        internal static BridgeCommand[] AimRunScript() => new[]
        {
            BridgeCommand.MoveTo(1, 5, 71000, 40000), BridgeCommand.MoveTo(2, 6, 79000, 40000),
            BridgeCommand.MoveTo(3, 7, 87000, 40000), BridgeCommand.MoveTo(4, 8, 95000, 40000),
        };

        // How far a shot (origin to end) passes beside the nearest team-1 unit ahead of it, as 10000 * tan(angle), in integers
        // (|cross| * 10000 / dot of the shot and the direction to the unit; units behind the shot do not count).
        private static long Miss(SimSession s, in FireRecord f)
        {
            long sx = f.End.X.Raw - f.Origin.X.Raw, sz = f.End.Z.Raw - f.Origin.Z.Raw, best = long.MaxValue;
            for (ulong key = 1; key <= 4; key++)
            {
                SimVec3 p = M3TestWorld.Position(s, key);
                long tx = p.X.Raw - f.Origin.X.Raw, tz = p.Z.Raw - f.Origin.Z.Raw, dot = sx * tx + sz * tz;
                if (dot > 0) best = Math.Min(best, Math.Abs(sx * tz - sz * tx) * 10000 / dot);
            }
            return best;
        }

        private sealed class RunResult
        {
            internal long MissSum;
            internal int Team2Shots;
            internal readonly List<string> Team1 = new List<string>();
        }

        private static RunResult Run(bool moving)
        {
            SimSession s = AimRun(moving);
            var r = new RunResult();
            for (int tick = 1; tick <= 300; tick++)
            {
                s.StepOnce();
                foreach (FireRecord f in CombatOf(s).Fires)
                {
                    if (f.ShooterTeam == 2) { r.MissSum += Miss(s, f); r.Team2Shots++; }
                }
                for (ulong key = 1; key <= 4; key++)
                {
                    Combatant c = State(s, key);
                    Assert.That(CombatOf(s).ShotAngle(Id(s, key), c, 3000, s.Tick), Is.EqualTo(3000), "team 1 stands: tick " + tick);
                    r.Team1.Add(tick + ":" + key + ":" + c.Ammo + ":" + c.SpreadStored + ":" + c.SpreadLastMilli);
                }
                if (moving && tick > 10 && tick < 250)
                    for (ulong key = 5; key <= 8; key++)
                        Assert.That(CombatOf(s).ShotAngle(Id(s, key), State(s, key), 3000, s.Tick), Is.EqualTo(9000), "team 2 runs: tick " + tick + " unit " + key);
            }
            return r;
        }

        [Test]
        public void A9_RunningShootersScatterTheirShots()
        {
            RunResult moving = Run(true), still = Run(false);
            TestContext.Out.WriteLine($"AIM1 A9 moving: shots={moving.Team2Shots} missSum={moving.MissSum}; still: shots={still.Team2Shots} missSum={still.MissSum}");
            Assert.That(moving.Team2Shots, Is.EqualTo(still.Team2Shots), "the same shots, only scattered differently");
            Assert.That(moving.MissSum, Is.GreaterThan(still.MissSum * 2));
            // Recorded by AIM-1 (new fixture, new behaviour): 240 shots each, about three times the scatter when running.
            Assert.That(moving.MissSum, Is.EqualTo(155071L));
            Assert.That(still.MissSum, Is.EqualTo(52226L));
            Assert.That(moving.Team1, Is.EqualTo(still.Team1), "team 1's shots and spread state depend on team 1 only");
        }
    }
}
