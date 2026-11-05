using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Wp18TestWorld;

namespace WarCore.Tests
{
    public sealed class Wp18StatusTests
    {
        [Test]
        public void CommittedTick_InitialIntervalReloadAndReloadEnd()
        {
            var w = new M6World(Recipe());
            Check(w, 30, 0, false);
            Assert.That(Status(w).Health, Is.EqualTo(100));
            Assert.That(Status(w).MaxHealth, Is.EqualTo(100));
            Assert.That(Status(w).Alive, Is.True); Assert.That(Status(w).HasWeapon, Is.True);
            w.Session.SetPlayerInput(1, Aim());
            w.Session.SetPlayerInput(101, Aim(false));
            for (int tick = 1; tick <= 160; tick++)
            {
                w.Session.StepOnce();
                ulong before = w.Hash;
                switch (tick)
                {
                    case 1: Check(w, 29, 4, false); break; // due=4390, next ready tick=5
                    case 4: Check(w, 29, 1, false); break;
                    case 5: Check(w, 28, 3, false); break;
                    case 99: Check(w, 1, 1, false); break;
                    case 100: Check(w, 30, 60, true); break; // due=159310
                    case 158: Check(w, 30, 2, true); break;
                    case 159: Check(w, 30, 1, true); break; // shorter than a fire interval, still reloading
                    case 160: Check(w, 30, 0, false); break;
                }
                Assert.That(w.Hash, Is.EqualTo(before), "HUD reads are read-only");
            }
            w.Session.SetPlayerInput(161, Aim()); w.Session.StepOnce();
            Check(w, 29, 4, false);
        }

        [Test]
        public void SingleRoundMagazine_UsesUnitWeaponIndexAndSuppliedCombatPack()
        {
            var pack = M5TestWorld.Pack(new ZeroResolver(), weapons: new[] {
                new WeaponDef(5000, 7, 9000, default, SimFix.FromInt(67), 0),
                new WeaponDef(1000, 1, 1500, default, SimFix.FromInt(67), 0)
            });
            var w = new M6World(Recipe(pack, new[] {
                Wp16TestWorld.U(1, weapon: 1), Wp16TestWorld.U(2, 20000, team: 2, squad: 2)
            }));
            Check(w, 1, 0, false);
            w.Session.SetPlayerInput(1, Aim()); w.Session.SetPlayerInput(2, Aim(false));
            w.Session.StepOnce(); Check(w, 1, 2, true); // full magazine immediately, due=2500
            w.Session.StepOnce(); Check(w, 1, 1, true);
            w.Session.StepOnce(); Check(w, 1, 0, false);
        }

        [Test]
        public void DeathAndNoWeapon_AreSeparateFromTimerReadiness()
        {
            var w = new M6World(Recipe());
            UnitStatus unarmed = Status(w, 2);
            Assert.That(unarmed.HasWeapon, Is.False); Assert.That(unarmed.Alive, Is.True);
            Assert.That(unarmed.WaitTicksLeft, Is.Zero); Assert.That(unarmed.Reloading, Is.False);
            Wp16TestWorld.KillAt(w, 1, 1);
            w.Session.StepOnce();
            UnitStatus dead = Status(w);
            Assert.That(dead.Alive, Is.False); Assert.That(dead.HasWeapon, Is.True);
            Assert.That(dead.Health, Is.Zero); Assert.That(dead.WaitTicksLeft, Is.Zero);
            Assert.That(dead.Reloading, Is.False);
            Assert.That(w.Session.TryUnitStatus(9999, out _), Is.False);
        }

        [Test]
        public void UnarmedControlledUnit_HeldPrimaryHasNoEffect()
        {
            var w = new M6World(Recipe(units: new[] {
                Wp16TestWorld.U(1), Wp16TestWorld.U(2, 20000, team: 2, squad: 2)
            }));
            w.Session.SetPlayerInput(1, Aim());
            for (int tick = 0; tick < 10; tick++)
            {
                w.Session.StepOnce(); Assert.That(Shots(w), Is.Zero);
                Assert.That(Status(w).HasWeapon, Is.False); Check(w, 0, 0, false);
            }
        }

        private static void Check(M6World w, int ammo, long wait, bool reloading)
        {
            UnitStatus status = Status(w);
            Assert.That(status.Ammo, Is.EqualTo(ammo), "ammo tick=" + w.Session.Tick);
            Assert.That(status.WaitTicksLeft, Is.EqualTo(wait), "wait tick=" + w.Session.Tick);
            Assert.That(status.Reloading, Is.EqualTo(reloading), "reload tick=" + w.Session.Tick);
        }
    }
}
