using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // WP-20: dual-wield, intents, the controlled unit, snapshots, data, and the no-melee guarantees.
    public sealed class Wp20MeleeRulesTests
    {
        private const long Ox = 20000, Oz = 20000;
        private static WeaponDef Gun() => new WeaponDef(3390, 30, 60000, SimFix.FromInt(34), SimFix.FromInt(67), 32768);
        private static StableId Shooter(MeleeWorld w) => new StableId(w.Id(1));
        private static void Move(MeleeWorld w, ulong key, long xMm)
        {
            w.W.Set(key, new Position(new SimVec3(SimFix.Ratio(xMm, 1000), w.W.Read<Position>(key).Value.Y, SimFix.Ratio(Oz, 1000))));
        }

        [Test]
        public void DualWield_GunBeyondN_SwordWithinN_AndTheGunKeepsItsOwnState()
        {
            // A gun with real spread. The enemy is far (gun), then at exactly N (sword), and so on: gun, sword, gun, sword, gun.
            var w = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec() }, new[] { SpreadGun() }),
                new[] { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0, weapon: 0), MeleeWorld.Unit(2, 2, Ox + 5000, Oz) });
            int expectedAmmo = 30;
            for (int round = 0; round < 3; round++)
            {
                Move(w, 2, Ox + 5000);
                int guard = 0;
                do { w.Step(); guard++; } while (w.W.Combat.Fires.Length == 0 && guard < 30);
                Assert.That(w.W.Combat.Fires.Length, Is.EqualTo(1), "beyond N the gun is used, round " + round);
                Assert.That(w.Count(StrikeKind.Started), Is.Zero);
                Combatant held = w.W.Read<Combatant>(1);
                Assert.That(held.Ammo, Is.EqualTo(--expectedAmmo), "the magazine carries on across swings, nothing is refilled");
                if (round == 0) Assert.That(held.SpreadStored, Is.Not.Zero, "the shot left spread behind");
                if (round == 2) break;
                Move(w, 2, Ox + 1800); // exactly N: the boundary counts as inside
                w.Step();
                Assert.That(w.Count(StrikeKind.Started), Is.EqualTo(1)); Assert.That(w.W.Combat.Fires.Length, Is.Zero);
                for (int busy = 1; busy < 17; busy++)
                {
                    w.Step();
                    Assert.That(w.W.Combat.Fires.Length, Is.Zero, "no shot while the swing is busy");
                    Assert.That(w.W.Read<Combatant>(1), Is.EqualTo(held), "the whole gun state (ammo, due time, spread) is untouched");
                }
            }
            Move(w, 2, Ox + 1801); // just outside N, once the last shot is on its cooldown the gun is used again
            int before = w.W.Read<Combatant>(1).Ammo;
            w.Step(8);
            Assert.That(w.Count(StrikeKind.Started), Is.Zero);
            Assert.That(w.W.Read<Combatant>(1).Ammo, Is.LessThan(before), "and the gun fired");
        }

        private static WeaponDef SpreadGun() => new WeaponDef(3390, 30, 60000, SimFix.FromInt(34), SimFix.FromInt(67), 32768,
            new SpreadSpec(2000, 1000, 5000, 8000));

        // Components are registered by the combat system itself: a world assembled by hand (no bridge) can carry melee too.
        [Test]
        public void ACombatSystemAssembledByHandRegistersAndRunsMelee()
        {
            var w = new M5TestWorld(M5TestWorld.Pair(weapon: -1, distanceMm: 1000), MeleeWorld.Pack(new[] { MeleeWorld.Spec() }));
            w.Combat.SetMelee(w.Unit(0), 0);
            for (int i = 0; i < 7; i++) w.Step();
            Assert.That(w.Sim.GameplayResource(w.Id(1), 1), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)));
            Assert.Throws<ArgumentException>(() => w.Combat.SetMelee(w.Unit(1), 1), "no such spec");
        }

        [Test]
        public void AUnitWithoutMeleeKeepsFiringItsGunInAMeleePack()
        {
            var w = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec() }, new[] { Gun() }),
                new[] { MeleeWorld.Unit(1, 1, Ox, Oz, weapon: 0), MeleeWorld.Unit(2, 2, Ox + 1000, Oz) });
            w.Step(3);
            Assert.That(w.W.Read<Combatant>(1).Ammo, Is.EqualTo(29));
            Assert.That(w.Count(StrikeKind.Started), Is.Zero);
            Assert.That(w.W.Combat.CanMelee(w.W.Unit(1)), Is.False);
        }

        [Test]
        public void IntentRoutes_AimedAtTargetAndNothing()
        {
            // Aimed swings at any point, even with nobody inside the engage range; here the enemy sits 1.9 m away, between N and the radius.
            var aimed = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec() }),
                new[] { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), MeleeWorld.Unit(2, 2, Ox + 1900, Oz) });
            aimed.Step(); Assert.That(aimed.Count(StrikeKind.Started), Is.Zero, "no intent and nobody within N: idle");
            aimed.Intent = c => aimed.W.Combat.Submit(c, FireIntent.Aimed(Shooter(aimed), SimVec3.Metres(30, 0, 20)));
            aimed.Step(); Assert.That(aimed.Count(StrikeKind.Started), Is.EqualTo(1));
            aimed.Step(6);
            Assert.That(aimed.Health(2), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)));
            // Aiming at the shooter's own position gives no heading: nothing starts.
            var zero = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec() }), new[] { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0) });
            zero.Intent = c => zero.W.Combat.Submit(c, FireIntent.Aimed(Shooter(zero), SimVec3.Metres(20, 0, 20)));
            zero.Step(3); Assert.That(zero.Count(StrikeKind.Started), Is.Zero);

            // AtTarget: a gunless unit swings when the target is within the radius, and never fires a gun.
            var at = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec() }),
                new[] { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), MeleeWorld.Unit(2, 2, Ox + 1900, Oz), MeleeWorld.Unit(3, 2, Ox, Oz + 3000) });
            at.Intent = c => at.W.Combat.Submit(c, FireIntent.AtTarget(Shooter(at), new StableId(at.Id(3))));
            at.Step(); Assert.That(at.Count(StrikeKind.Started), Is.Zero, "target 3 m away is out of the 2 m radius");
            at.Intent = c => at.W.Combat.Submit(c, FireIntent.AtTarget(Shooter(at), new StableId(at.Id(2))));
            at.Step(); Assert.That(at.Count(StrikeKind.Started), Is.EqualTo(1));
            Assert.That(at.W.Combat.Fires.Length, Is.Zero);

            // A dual unit uses N: a target at 1.9 m is beyond it, so the AtTarget intent goes to the gun, not the sword.
            var dual = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec() }, new[] { Gun() }),
                new[] { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0, weapon: 0), MeleeWorld.Unit(2, 2, Ox + 1900, Oz) });
            dual.Intent = c => dual.W.Combat.Submit(c, FireIntent.AtTarget(Shooter(dual), new StableId(dual.Id(2))));
            dual.Step();
            Assert.That(dual.Count(StrikeKind.Started), Is.Zero); Assert.That(dual.W.Combat.Fires.Length, Is.EqualTo(1));

            // A dual unit aimed exactly N away swings; one millimetre further the aim goes to the gun.
            foreach (long mm in new[] { 1800L, 1801L })
            {
                var edge = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec() }, new[] { Gun() }),
                    new[] { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0, weapon: 0) });
                var point = new SimVec3(SimFix.Ratio(Ox + mm, 1000), default, SimFix.Ratio(Oz, 1000));
                edge.Intent = c => edge.W.Combat.Submit(c, FireIntent.Aimed(Shooter(edge), point));
                edge.Step();
                Assert.That(edge.Count(StrikeKind.Started), Is.EqualTo(mm == 1800 ? 1 : 0), "aimed at " + mm + " mm");
                Assert.That(edge.W.Combat.Fires.Length, Is.EqualTo(mm == 1800 ? 0 : 1), "aimed at " + mm + " mm");
            }
        }

        [Test]
        public void ControlledUnit_NoInputNoSwing_InputStartsOne_ReleasingDoesNotCancelIt()
        {
            var w = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec() }),
                new[] { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), MeleeWorld.Unit(2, 2, Ox + 1000, Oz) }, player: 1, match: M6World.Pack());
            w.Step(3);
            Assert.That(w.Count(StrikeKind.Started), Is.Zero, "an enemy within N does not make the controlled unit swing by itself");
            w.Step(new InputFrame(default, SimVec3.Metres(30, 0, 20), primaryAttack: true));
            Assert.That(w.Count(StrikeKind.Started), Is.EqualTo(1));
            w.Step(6); // input released: the accepted swing still lands on its tick (4 + 6 = 10)
            Assert.That(w.Tick, Is.EqualTo(10));
            Assert.That(w.Health(2), Is.EqualTo(MeleeWorld.Full - MeleeWorld.Cost(10)));
            Assert.That(w.W.Combat.CanMelee(w.W.Unit(1)), Is.True);
        }

        [Test]
        public void SnapshotsInsideTheSwing_RestoreIntoAReversedWorldAndContinueIdentically()
        {
            UnitSpawn[] Units() => new[] { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), MeleeWorld.Unit(2, 2, Ox + 1000, Oz), MeleeWorld.Unit(3, 2, Ox + 1400, Oz + 400) };
            CombatPack Pack() => MeleeWorld.Pack(new[] { MeleeWorld.Spec() });
            var run = new MeleeWorld(Pack(), Units());
            var hashes = new ulong[31]; var states = new MeleeState[31]; var snapshots = new Dictionary<int, byte[]>();
            foreach (int at in new[] { 3, 7, 12 }) snapshots[at] = null;
            for (int tick = 1; tick <= 30; tick++)
            {
                run.Step(); hashes[tick] = run.W.Hash; states[tick] = run.W.Read<MeleeState>(1);
                if (snapshots.ContainsKey(tick)) snapshots[tick] = run.W.Sim.SaveRetainedSnapshot();
            }
            foreach (var pair in snapshots)
            {
                var twin = new MeleeWorld(Pack(), Units(), reverse: true);
                twin.W.Sim.RestoreRetainedSnapshot(pair.Value, reverseAllocation: true);
                Assert.That(twin.W.Hash, Is.EqualTo(hashes[pair.Key]), "restored at " + pair.Key);
                Assert.That(twin.W.Read<MeleeState>(1), Is.EqualTo(states[pair.Key]), "the pending swing comes back as it was");
                for (int tick = pair.Key + 1; tick <= 30; tick++)
                {
                    twin.Step();
                    Assert.That(twin.W.Hash, Is.EqualTo(hashes[tick]), "restored at " + pair.Key + ", tick " + tick);
                }
            }
            var other = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec(damage: 11) }), Units());
            Assert.Catch(() => other.W.Sim.RestoreRetainedSnapshot(snapshots[3]), "a different melee configuration refuses the snapshot");
        }

        [Test]
        public void ThePendingSwingIsPartOfTheSavedState()
        {
            var w = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec() }), new[] { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), MeleeWorld.Unit(2, 2, Ox + 1000, Oz) });
            w.Step(3);
            MeleeState pending = w.W.Read<MeleeState>(1);
            Assert.That(pending.StartTick, Is.EqualTo(1)); Assert.That(pending.NextStartTick, Is.EqualTo(18));
            Assert.That(pending.DirX, Is.GreaterThan(0)); Assert.That(pending.DirZ, Is.Zero);
            Assert.That(pending.Origin.X.Raw, Is.EqualTo(SimFix.Ratio(Ox, 1000).Raw));
            w.Step(5); // tick 8: landed
            MeleeState spent = w.W.Read<MeleeState>(1);
            Assert.That(spent.StartTick, Is.EqualTo(-1)); Assert.That(spent.NextStartTick, Is.EqualTo(18));
            Assert.That(spent.Origin, Is.EqualTo(default(SimVec3))); Assert.That(spent.DirX, Is.Zero);
        }

        // ---- worlds without melee ----

        [Test]
        public void WorldsWithoutMeleeCarryNothingExtra()
        {
            var world = new M6World(M6World.RecipeOf(new[] { M6World.U(1, 1, weapon: 0), M6World.U(2, 2, 24, weapon: 0) }));
            Assert.Throws<InvalidOperationException>(() => world.Sim.Entities.Resolve<MeleeState>());
            Assert.That(world.Combat.Strikes.Length, Is.Zero);
            var plain = RwrPack.Create();
            var same = new CombatPack(plain.Weapons.ToArray(), plain.Resolver, plain.Selector, plain.MaxHealthRaw, plain.FriendlyFire,
                plain.FriendlyFireLocked, plain.AllyCheckMissRaw, plain.AllyHoldMilli, new MeleeSpec[0]);
            Assert.That(same.PolicyDigest, Is.EqualTo(plain.PolicyDigest), "an empty melee table hashes like none");
            Assert.That(plain.PolicyDigest, Is.EqualTo(0x8BE7D7DFA58533CDUL));
            Assert.That(RwrPack.CreateM6().PolicyDigest, Is.EqualTo(0x934F8562D85447D2UL));
        }

        [Test]
        public void EveryMeleeFieldChangesThePolicyDigest()
        {
            var baseline = MeleeWorld.Pack(new[] { MeleeWorld.Spec() }).PolicyDigest;
            var variants = new[]
            {
                MeleeWorld.Spec(windup: 7), MeleeWorld.Spec(active: 3), MeleeWorld.Spec(recovery: 10), MeleeWorld.Spec(radius: 2.5),
                MeleeWorld.Spec(cos: 501), MeleeWorld.Spec(halfHeight: 1.5), MeleeWorld.Spec(damage: 11), MeleeWorld.Spec(maxTargets: 4), MeleeWorld.Spec(engage: 1.7),
            };
            var seen = new HashSet<ulong> { baseline };
            foreach (var spec in variants) Assert.That(seen.Add(MeleeWorld.Pack(new[] { spec }).PolicyDigest), Is.True);
            Assert.That(MeleeWorld.Pack(null).PolicyDigest, Is.Not.EqualTo(baseline));
        }

        // ---- data ----

        [Test]
        public void TheSampleLayerLoads_AndTheOldLayersHaveNoMelee()
        {
            ContentPack pack = RwrPack.Load("rwr-melee");
            Assert.That(pack.MeleeCount, Is.EqualTo(1)); Assert.That(pack.MeleeId(0), Is.EqualTo("rwr.sword-1"));
            Assert.That(pack.WeaponOfUnitType("rwr.swordsman"), Is.EqualTo(-1)); Assert.That(pack.MeleeOfUnitType("rwr.swordsman"), Is.EqualTo(0));
            Assert.That(pack.WeaponOfUnitType("rwr.rifle-sword"), Is.EqualTo(0)); Assert.That(pack.MeleeOfUnitType("rwr.rifle-sword"), Is.EqualTo(0));
            Assert.That(pack.MeleeOfUnitType(RwrPack.SoldierTeam1), Is.EqualTo(-1));
            MeleeSpec sword = pack.BuildCombat().Melee[0];
            Assert.That((sword.WindupTicks, sword.ActiveTicks, sword.RecoveryTicks, sword.CosHalfAnglePermille, sword.MaxTargets), Is.EqualTo((6, 2, 9, 500, 3)));
            Assert.That((sword.Radius, sword.HalfHeight, sword.Damage, sword.EngageRange),
                Is.EqualTo((SimFix.FromInt(2), SimFix.FromInt(1), SimFix.FromInt(34), SimFix.Ratio(18, 10))));
            foreach (string layer in new[] { RwrPack.M5Layer, RwrPack.M6Layer })
            {
                ContentPack old = RwrPack.Load(layer);
                Assert.That(old.MeleeCount, Is.Zero); Assert.That(old.BuildCombat().HasMelee, Is.False);
            }
            Assert.That(pack.BuildCombat().Weapons.Length, Is.EqualTo(RwrPack.Load(RwrPack.M6Layer).BuildCombat().Weapons.Length), "no gun was added");
        }

        private static string Melee(string tweak = "") =>
            "{ \"id\": \"t\", \"extends\": \"rwr\", \"melee\": [ { \"id\": \"m\", \"windupTicks\": 6, \"activeTicks\": 2, \"recoveryTicks\": 9, \"radius\": 2, \"cosHalfAnglePermille\": 500,"
            + " \"halfHeight\": 1, \"damage\": 34, \"maxTargets\": 3, \"engageRange\": 1.8" + tweak + " } ],"
            + " \"unitTypes\": [ { \"id\": \"u\", \"weapon\": \"\", \"melee\": \"m\" } ] }";
        private static ContentPack Load(string json) =>
            ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string> { ["t"] = json }, FileContentSource.Rwr()), "t");

        [Test]
        public void LoaderRejectsBadMeleeData()
        {
            Assert.DoesNotThrow(() => Load(Melee()));
            Assert.That(Assert.Throws<ContentException>(() => Load(Melee(", \"extra\": 1"))).Message, Does.Contain("extra"));
            Assert.That(Assert.Throws<ContentException>(() => Load(Melee().Replace("\"windupTicks\": 6, ", ""))).Message, Does.Contain("windupTicks"));
            Assert.That(Assert.Throws<ContentException>(() => Load(Melee().Replace("\"windupTicks\": 6", "\"windupTicks\": 0"))).Message, Does.Contain("windupTicks"));
            Assert.That(Assert.Throws<ContentException>(() => Load(Melee().Replace("\"engageRange\": 1.8", "\"engageRange\": 2.5"))).Message, Does.Contain("engageRange"));
            Assert.That(Assert.Throws<ContentException>(() => Load(Melee().Replace("\"maxTargets\": 3", "\"maxTargets\": 17"))).Message, Does.Contain("maxTargets"));
            Assert.That(Assert.Throws<ContentException>(() => Load(Melee().Replace("\"melee\": \"m\"", "\"melee\": \"nope\""))).Message, Does.Contain("nope"));
            Assert.That(Assert.Throws<ContentException>(() => Load(Melee().Replace(", \"melee\": \"m\"", ""))).Message, Does.Contain("needs a melee"));
            Assert.That(Assert.Throws<ContentException>(() => Load(Melee().Replace("\"weapon\": \"\"", "\"weapon\": \"nope\""))).Message, Does.Contain("nope"));
            // The same melee id in a child layer overrides field by field, like any other entry.
            var child = new Dictionary<string, string>
            {
                ["t"] = Melee(),
                ["c"] = "{ \"id\": \"c\", \"extends\": \"t\", \"melee\": [ { \"id\": \"m\", \"damage\": 50 } ] }",
            };
            ContentPack merged = ContentLoader.Load(new MemoryContentSource(child, FileContentSource.Rwr()), "c");
            Assert.That(merged.BuildCombat().Melee[0].Damage, Is.EqualTo(SimFix.FromInt(50)));
            Assert.That(merged.BuildCombat().Melee[0].WindupTicks, Is.EqualTo(6));
        }

        // ---- the demo ----

        [Test]
        public void TheDuelIsDeterministicAndEndsWhenBothFall()
        {
            // Anchors: first swing starts (tick 1), first blow lands (tick 7), both fall (tick 41). Hashes are taken on those ticks.
            ulong[] Run(bool reverse, out long firstStart, out long firstHit, out long end, out bool bothDead, out bool randomUntouched)
            {
                var w = new M6World(DemoRecipes.MeleeDuel(), reverse: reverse);
                ulong Random() { var h = new StateHash(); w.Sim.Rng.WriteState(h); return h.Value; }
                ulong randomAtStart = Random();
                var anchors = new ulong[3];
                firstStart = firstHit = end = 0;
                for (int tick = 1; tick <= 60 && end == 0; tick++)
                {
                    w.Step();
                    foreach (var s in w.Combat.Strikes)
                    {
                        if (s.Kind == StrikeKind.Started && firstStart == 0) firstStart = s.Tick;
                        if (s.Kind == StrikeKind.Landed && firstHit == 0) firstHit = s.Tick;
                    }
                    if (tick == 1) anchors[0] = w.Hash;
                    if (tick == 7) anchors[1] = w.Hash;
                    if (w.Dead(1) || w.Dead(2)) { end = tick; anchors[2] = w.Hash; }
                }
                bothDead = w.Dead(1) && w.Dead(2);
                randomUntouched = Random() == randomAtStart;
                return anchors;
            }
            ulong[] a = Run(false, out long start, out long hit, out long end, out bool bothDead, out bool randomUntouched);
            ulong[] b = Run(true, out _, out _, out long endReverse, out bool bothDeadReverse, out _);
            Assert.That(start, Is.EqualTo(1)); Assert.That(hit, Is.EqualTo(7)); Assert.That(end, Is.EqualTo(41), "three blows of 34 each, one swing every 17 ticks");
            Assert.That(bothDead, Is.True, "the blows are simultaneous: both fall on the same tick");
            Assert.That(endReverse, Is.EqualTo(end)); Assert.That(bothDeadReverse, Is.True);
            Assert.That(randomUntouched, Is.True, "melee draws nothing from any random stream");
            Assert.That(b, Is.EqualTo(a), "creation order does not matter");
            Assert.That(string.Join(",", Array.ConvertAll(a, x => x.ToString("X16"))), Is.EqualTo("0B5FD7A23E20E47E,71E273D81BF1B37C,12555D85A27C8D18"),
                "state hashes at the first swing (tick 1), the first blow (tick 7) and the fall (tick 41)");
        }
    }
}
