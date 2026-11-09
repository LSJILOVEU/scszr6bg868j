using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // A4 acceptance 4(a), completed: the flat M2b world of A4FlatLockTests with five different aim points held in turn, a
    // fire fold that also takes DamageRaw and ShooterTeam, and ticks where the player is held by its teammate counted on
    // their own (the crowd blocks the player; it never pushes it). Old interfaces only: the values were recorded on the base
    // commit with this file and A4Fixture.cs alone, and must not change.
    public sealed class A4FlatAimTests
    {
        internal const int Ticks = 600, Phase = 120;

        internal static PlayerInput Script(int phase) => phase switch
        {
            0 => new PlayerInput(1000, 0, 30000, 10000, true),   // +x into teammate 2; the line to (30, 10) crosses the long wall
            1 => new PlayerInput(0, 1000, 2000, 31000, true),    // +z; open map
            2 => new PlayerInput(-1000, 0, 20000, 28000, true),  // -x; enemy 4
            3 => new PlayerInput(0, -1000, 5000, 2000, true),    // -z; open map, the other side
            _ => new PlayerInput(1000, 0, 40000, 4000, true),    // +x; past the long wall's end
        };

        internal struct Stats { internal int Fires, Hit, Wall, Open, Friend, TeammateHeldTicks, OtherBlockedTicks; internal long TravelRaw; }

        internal static (ulong hash, ulong fires, Stats stats, int aims) Run(M6World w, Func<long, PlayerInput?> script)
        {
            ulong fold = A4.Fnv(14695981039346656037UL, w.Session.StateHash);
            ulong fires = 14695981039346656037UL;
            var stats = new Stats();
            var aims = new HashSet<(long, long)>();
            ulong key = w.Session.ControlledKey.Value;
            SimVec3 last = w.Read<Position>(key).Value;
            int moveX = 0, moveZ = 0;
            for (int i = 0; i < Ticks && !w.Session.IsFinished; i++)
            {
                long next = w.Session.Tick + 1;
                PlayerInput? input = script(next);
                if (input.HasValue)
                {
                    w.Session.SetPlayerInput(next, input.Value);
                    moveX = input.Value.MoveXMilli; moveZ = input.Value.MoveZMilli;
                    aims.Add((input.Value.AimXMm, input.Value.AimZMm));
                }
                w.Session.StepOnce();
                long tick = w.Session.Tick;
                foreach (FireRecord f in w.Combat.Fires)
                {
                    stats.Fires++;
                    if (f.Outcome == FireOutcome.Hit) stats.Hit++;
                    if (f.Outcome == FireOutcome.WallBlocked) stats.Wall++;
                    if (f.Outcome == FireOutcome.Open) stats.Open++;
                    if (f.Outcome == FireOutcome.FriendBlocked) stats.Friend++;
                    fires = A4.Fnv(fires, (ulong)tick); fires = A4.Fnv(fires, f.Shooter.Value); fires = A4.Fnv(fires, f.Target.Value);
                    fires = A4.Fnv(fires, (ulong)f.Origin.X.Raw); fires = A4.Fnv(fires, (ulong)f.Origin.Y.Raw); fires = A4.Fnv(fires, (ulong)f.Origin.Z.Raw);
                    fires = A4.Fnv(fires, (ulong)f.End.X.Raw); fires = A4.Fnv(fires, (ulong)f.End.Y.Raw); fires = A4.Fnv(fires, (ulong)f.End.Z.Raw);
                    fires = A4.Fnv(fires, (ulong)f.Distance.Raw); fires = A4.Fnv(fires, (ulong)f.DamageRaw);
                    fires = A4.Fnv(fires, (ulong)(long)f.ShooterTeam); fires = A4.Fnv(fires, (ulong)f.Outcome);
                }
                fold = A4.Fnv(fold, w.Session.StateHash);
                Assert.That(w.Session.Current.TryGet(key, out ViewEntity me) && me.Alive, Is.True, "player alive at tick " + tick);
                SimVec3 p = w.Read<Position>(key).Value;
                Assert.That(p.Y.Raw, Is.EqualTo(0), "player Y at tick " + tick);
                long dx = p.X.Raw - last.X.Raw, dz = p.Z.Raw - last.Z.Raw;
                stats.TravelRaw += Math.Abs(dx) + Math.Abs(dz);
                if ((moveX != 0 || moveZ != 0) && dx == 0 && dz == 0)
                {
                    // Held: a move input and no movement. Counted as held by the teammate when a living teammate's centre is
                    // within 1.2 m and ahead along the input.
                    bool teammate = false;
                    foreach (ViewEntity e in w.Session.Current.Entities)
                    {
                        if (e.Key == key || !e.Alive || e.Team != me.Team) continue;
                        double ex = e.X - me.X, ez = e.Z - me.Z;
                        if (ex * ex + ez * ez <= 1.2 * 1.2 && ex * moveX + ez * moveZ > 0) teammate = true;
                    }
                    if (teammate) stats.TeammateHeldTicks++; else stats.OtherBlockedTicks++;
                }
                last = p;
            }
            return (fold, fires, stats, aims.Count);
        }

        internal static PlayerInput? AtPhaseStart(long tick) => (tick - 1) % Phase == 0 ? Script((int)((tick - 1) / Phase)) : (PlayerInput?)null;

        [Test]
        public void A4FlatAim_FiveAimPointsAndTheTeammateHold_AreUnchanged()
        {
            var w = new M6World(A4.FlatRecipe(), DemoRecipes.M2bMapBytes());
            var (hash, fires, s, aims) = Run(w, AtPhaseStart);
            TestContext.Progress.WriteLine($"A4FlatAim hash=0x{hash:X16} fires=0x{fires:X16} aims={aims} fires={s.Fires} hit={s.Hit} wall={s.Wall} open={s.Open} friend={s.Friend} held={s.TeammateHeldTicks} other={s.OtherBlockedTicks} travel={s.TravelRaw}");
            Assert.That(w.Session.Tick, Is.EqualTo(Ticks), "the match runs all ticks");
            Assert.That(aims, Is.EqualTo(5));
            Assert.That(s.Fires, Is.GreaterThanOrEqualTo(10));
            Assert.That(s.Hit, Is.GreaterThanOrEqualTo(1));
            Assert.That(s.Wall, Is.GreaterThanOrEqualTo(1));
            Assert.That(s.TravelRaw, Is.GreaterThanOrEqualTo(5L * 65536));
            Assert.That(s.TeammateHeldTicks, Is.EqualTo(108));
            Assert.That(hash, Is.EqualTo(0xA6BC240E7D9C8D28UL));
            Assert.That(fires, Is.EqualTo(0x04C25D728D732FC4UL));
        }
    }
}
