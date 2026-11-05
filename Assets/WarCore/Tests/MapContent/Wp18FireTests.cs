using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Control;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Wp18TestWorld;

namespace WarCore.Tests
{
    public sealed class Wp18FireTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void HeldM16A4_ThirtyFirstShotAt160_HitAndMiss(bool hit)
        {
            var w = new M6World(Recipe());
            w.Session.SetPlayerInput(1, Aim(z: hit ? 10000 : 20000));
            var ticks = new List<long>();
            SimVec3 target = w.Read<Position>(2).Value;
            for (int tick = 1; tick <= 160; tick++)
            {
                w.Session.StepOnce();
                Assert.That(w.Session.Tick, Is.EqualTo(tick));
                Assert.That(w.Read<Position>(2).Value, Is.EqualTo(target), "stationary target");
                Assert.That(w.Session.Winner, Is.Zero);
                foreach (var fire in w.Combat.Fires)
                {
                    Assert.That(fire.Shooter, Is.EqualTo(Id(1)));
                    Assert.That(fire.Target, Is.EqualTo(hit ? Id(2) : default(StableId)));
                    Assert.That(fire.Origin, Is.EqualTo(w.Read<Position>(1).Value));
                    Assert.That(fire.DamageRaw, Is.Zero);
                    ticks.Add(tick);
                    Assert.That(w.Read<Combatant>(1).Ammo, Is.EqualTo(30 - ticks.Count % 30));
                }
            }
            Assert.That(ticks.Count, Is.EqualTo(31));
            for (int n = 1; n <= 30; n++)
                Assert.That(ticks[n - 1], Is.EqualTo(1 + ((n - 1) * 3390 + 999) / 1000), "shot=" + n);
            Assert.That(ticks[0], Is.EqualTo(1));
            Assert.That(ticks[29], Is.EqualTo(100));
            Assert.That(ticks[30], Is.EqualTo(160));
        }

        [Test]
        public void RealRwrDamage_Seed41_At52Metres_KillsOnFifthShotTick15()
        {
            // WP-17: spread is switched off here; this test locks the real damage curve and seed 41 hit rolls, not aim.
            var w = new M6World(Recipe(M5TestWorld.NoSpread(RwrPack.Create()), new[] {
                Wp16TestWorld.U(1, weapon: 0, speed: 1),
                Wp16TestWorld.U(2, 62000, team: 2, squad: 2, speed: 1)
            }));
            // Seed 41 combat.hit rolls: 16454, 44835, 53790, 15946, 6962.
            // At 52 m, falloff=15/33; the lethal threshold is 14894 raw.
            w.Session.SetPlayerInput(1, Aim(x: 62000));
            int shots = 0;
            for (int tick = 1; tick <= 15; tick++)
            {
                w.Session.StepOnce();
                shots += Shots(w);
                Assert.That(w.Dead(2), Is.EqualTo(tick == 15), "tick=" + tick);
                foreach (var fire in w.Combat.Fires)
                {
                    Assert.That(fire.Target, Is.EqualTo(Id(2)));
                    Assert.That(fire.DamageRaw, Is.EqualTo(tick == 15 ? 100 * SimFix.Scale : 0));
                }
            }
            Assert.That(shots, Is.EqualTo(5));
            Assert.That(w.Sim.GameplayLife(Id(2)).EnteredTick, Is.EqualTo(15));
            Assert.That(Status(w, 2).Alive, Is.False);
        }

        [TestCase(0, true)]
        [TestCase(200, true)]
        [TestCase(300, false)]
        public void RealMap_CapsuleCentreRay_LateralOffset(long lateralMm, bool hit)
        {
            var w = new M6World(Recipe(units: new[] {
                Wp16TestWorld.U(1, 20000, 2000, weapon: 0),
                Wp16TestWorld.U(2, 22000, 2000 + lateralMm, team: 2, squad: 2)
            }), DemoRecipes.M2bMapBytes());
            w.Session.SetPlayerInput(1, Aim(x: 22000, z: 2000));
            w.Session.StepOnce();
            Assert.That(Shots(w), Is.EqualTo(1));
            var fire = w.Combat.Fires[0];
            Assert.That(fire.Target, Is.EqualTo(hit ? Id(2) : default(StableId)));
            Assert.That(fire.Origin, Is.EqualTo(w.Read<Position>(1).Value));
            Assert.That(fire.End.Y, Is.EqualTo(fire.Origin.Y));
            Assert.That(fire.End.Z, Is.EqualTo(fire.Origin.Z));
            if (!hit) Assert.That(fire.End.X, Is.EqualTo(SimFix.FromInt(24)), "real wall stops the miss");
            Assert.That(w.Read<Combatant>(1).Ammo, Is.EqualTo(29));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PrimaryFalse_BesideEnemy_600Ticks_NoPlayerShots(bool armedEnemy)
        {
            var w = new M6World(Recipe(units: new[] {
                Wp16TestWorld.U(1, weapon: 0),
                Wp16TestWorld.U(2, 13000, team: 2, squad: 2, weapon: armedEnemy ? 0 : -1)
            }));
            w.Session.SetPlayerInput(1, Aim(false, 13000));
            int enemyShots = 0;
            for (int tick = 1; tick <= 600; tick++)
            {
                w.Session.StepOnce();
                Assert.That(w.Session.Tick, Is.EqualTo(tick));
                Assert.That(Shots(w), Is.Zero);
                enemyShots += Shots(w, 2);
            }
            Assert.That(w.Read<Combatant>(1).Ammo, Is.EqualTo(30));
            Assert.That(enemyShots, armedEnemy ? Is.GreaterThan(0) : Is.EqualTo(0));
        }

        [Test]
        public void ZeroAimDirection_DoesNotSpendAmmoOrRandomness()
        {
            var w = new M6World(Recipe());
            ulong rng = Wp13TestWorld.Hash(w.Sim.Rng);
            Combatant before = w.Read<Combatant>(1);
            w.Session.SetPlayerInput(1, Aim(x: 10000));
            for (int tick = 0; tick < 20; tick++)
            {
                w.Session.StepOnce(); Assert.That(Shots(w), Is.Zero);
                Assert.That(w.Read<Combatant>(1), Is.EqualTo(before));
                Assert.That(Wp13TestWorld.Hash(w.Sim.Rng), Is.EqualTo(rng));
            }
        }

        [Test]
        public void DeadPlayer_NoSubmissionEvenWhenCurrentStillNamesCorpse()
        {
            var w = new M6World(Recipe());
            Wp16TestWorld.KillAt(w, 1, 1);
            w.Session.SetPlayerInput(1, Aim(false)); w.Session.StepOnce();
            Assert.That(w.Dead(1), Is.True);
            // Tick 2 still runs: Match sees gameplay deaths on the following tick.
            w.Session.SetPlayerInput(2, Aim());
            Assert.DoesNotThrow(() => w.Session.StepOnce(), "Submit rejects dead shooters");
            Assert.That(w.Session.Tick, Is.EqualTo(2));
            Assert.That(w.Session.ControlledKey, Is.EqualTo(1UL));
            Assert.That(Shots(w), Is.Zero);
            Assert.That(Status(w).Alive, Is.False);
        }

        [Test]
        public void Fallback_RereadsNewControlledEntity_AndFiresForSuccessor()
        {
            var w = new M6World(Recipe(units: new[] {
                Wp16TestWorld.U(1, weapon: 0), Wp16TestWorld.U(3, 10000, 20000, weapon: 0),
                Wp16TestWorld.U(2, 20000, team: 2, squad: 2)
            }));
            Wp16TestWorld.KillAt(w, 1, 1);
            w.Session.SetPlayerInput(1, Aim(false)); w.Session.StepOnce();
            // The successor may have fired as AI at tick 1; wait until its weapon is ready.
            w.Session.SetPlayerInput(2, Aim());
            int successorShots = 0;
            for (int tick = 2; tick <= 10; tick++)
            {
                w.Session.StepOnce();
                Assert.That(w.Session.ControlledKey, Is.EqualTo(3UL));
                Assert.That(Shots(w), Is.Zero);
                foreach (var fire in w.Combat.Fires)
                {
                    Assert.That(fire.Shooter, Is.EqualTo(Id(3)));
                    Assert.That(fire.Origin, Is.EqualTo(w.Read<Position>(3).Value));
                    successorShots++;
                }
            }
            Assert.That(successorShots, Is.GreaterThan(0));
        }

        [Test]
        public void IsolatedAi_FireRecordsUnchangedAgainstNoPlayerRecipe()
        {
            var plain = M6World.RecipeOf(new[] {
                Wp16TestWorld.U(1, 2000, 2000, weapon: 0, speed: 1),
                Wp16TestWorld.U(2, 70000, 30000, weapon: 0, speed: 1),
                Wp16TestWorld.U(3, 70000, 35000, team: 2, squad: 2, weapon: 0, speed: 1)
            }, Wp16TestWorld.Match(think: 1), M5TestWorld.Pack(new ZeroResolver()));
            var baseline = new M6World(plain); var player = new M6World(plain.WithPlayer(1));
            Assert.That(baseline.System<PlayerFire>(), Is.Null);
            Assert.That(player.System<PlayerFire>().Order, Is.EqualTo(29));
            player.Session.SetPlayerInput(1, Aim(false));
            int aiShots = 0;
            for (int tick = 1; tick <= 600; tick++)
            {
                baseline.Session.StepOnce(); player.Session.StepOnce();
                var expected = AiFires(baseline); var actual = AiFires(player);
                Assert.That(actual, Is.EqualTo(expected), "all FireRecord fields, tick=" + tick);
                aiShots += actual.Length;
                Assert.That(Shots(player), Is.Zero);
                Assert.That(Shots(baseline), Is.Zero, "player remains outside AI weapon range");
            }
            Assert.That(aiShots, Is.GreaterThan(0));
        }

        private static FireRecord[] AiFires(M6World w)
        {
            var result = new List<FireRecord>();
            foreach (var fire in w.Combat.Fires) if (!fire.Shooter.Equals(Id(1))) result.Add(fire);
            return result.ToArray();
        }
    }
}
