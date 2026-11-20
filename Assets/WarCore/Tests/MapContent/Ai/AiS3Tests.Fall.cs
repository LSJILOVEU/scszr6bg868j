using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // AiS3Tests: what counts as a teammate brought down (PressureRules.FallOnKill, content field pressureFall). "hit" (the
    // default) counts any enemy fire record with damage, which is right only for a resolver that kills or does nothing;
    // "kill" counts a unit only when this tick's damage (fire records plus landed strikes) reaches its health.
    public sealed partial class AiS3Tests
    {
        private const string KillField = "\"pressureFall\": \"kill\"";

        private static PressureRules KillRules(int deathRadius = 3000, int nearShock = 0, int decay = 12, int spread = 0) =>
            new PressureRules(deathRadius, 2000, 3000, nearShock, decay, 7000, 1000, 2000, spread, 1000, fallOnKill: true);
        // An enemy shot on the victim with this much damage.
        private static FireRecord Hit(M6World w, ulong victim, long damage, int shooterTeam = 2) =>
            new FireRecord(Id(w, shooterTeam == 1 ? 1UL : Attackers + 1UL), Id(w, victim), At(w, victim), At(w, victim), default, damage, shooterTeam);

        // A shot with any damage is a fall under the default rule, whatever the unit's health.
        [Test]
        public void Fall_HitRuleCountsAnyDamage()
        {
            Assert.That(Rules().FallOnKill, Is.False);
            M6World w = Field(null);
            var p = new Pressure(w.Combat, Rules(nearShock: 0));
            p.Advance(new[] { Hit(w, 10, 1) });
            Assert.That(p.Value(Id(w, 11)), Is.EqualTo(2000));
        }

        [Test]
        public void Fall_KillRuleCountsOnlyDamageThatReachesHealth()
        {
            M6World w = Field(null);
            long health = w.Combat.HealthRaw(Id(w, 10));
            Assert.That(health, Is.EqualTo(M6World.Guns(new ZeroResolver()).MaxHealthRaw), "an unhurt unit has full health");
            Assert.That(health, Is.GreaterThan(1));
            // One short of the health: no fall. Exactly the health: a fall.
            var below = new Pressure(w.Combat, KillRules());
            below.Advance(new[] { Hit(w, 10, health - 1) });
            for (ulong key = 1; key <= Attackers + Defenders; key++) Assert.That(below.Value(Id(w, key)), Is.EqualTo(0), "key " + key);
            var exact = new Pressure(w.Combat, KillRules());
            exact.Advance(new[] { Hit(w, 10, health) });
            for (ulong key = 1; key <= Attackers; key++)
                Assert.That(exact.Value(Id(w, key)), Is.EqualTo(key >= 7 && key <= 13 && key != 10 ? 2000 : 0), "key " + key);
            // Two shots on one unit add up and count as one fall; a wound beside a kill adds nothing.
            var sum = new Pressure(w.Combat, KillRules());
            sum.Advance(new[] { Hit(w, 10, health - 1), Hit(w, 12, health - 1), Hit(w, 10, 1) });
            Assert.That(sum.Value(Id(w, 11)), Is.EqualTo(2000));
            Assert.That(sum.Value(Id(w, 14)), Is.EqualTo(0), "key 14 is 2 m from the wounded key 12 and 4 m from key 10");
            Assert.That(sum.Value(Id(w, 12)), Is.EqualTo(2000), "the wounded unit feels the fall beside it");
            // A record without damage adds nothing to the sum.
            var zero = new Pressure(w.Combat, KillRules());
            zero.Advance(new[] { Hit(w, 10, health - 1), Hit(w, 10, 0) });
            Assert.That(zero.Value(Id(w, 11)), Is.EqualTo(0));
            // The near shock does not depend on the rule: a wounding shot still lands near.
            var near = new Pressure(w.Combat, KillRules(nearShock: 1000));
            near.Advance(new[] { Hit(w, 10, health - 1) });
            Assert.That(near.Value(Id(w, 11)), Is.EqualTo(1000));
            // The health is read from the unit: half of it does not bring an unhurt unit down.
            var half = new Pressure(w.Combat, KillRules());
            half.Advance(new[] { Hit(w, 10, health / 2) });
            Assert.That(half.Value(Id(w, 11)), Is.EqualTo(0));
        }

        [Test]
        public void Fall_KillsAddsShotsAndLandedStrikesOfThatUnitOnly()
        {
            var me = new StableId(7); var other = new StableId(8); var foe = new StableId(30);
            SimVec3 at = SimVec3.Metres(1, 0, 1);
            FireRecord[] fires =
            {
                new FireRecord(foe, me, at, at, default, 30, 2), new FireRecord(foe, me, at, at, default, 0, 2, FireOutcome.Open),
                new FireRecord(foe, other, at, at, default, 100, 2),
            };
            StrikeRecord[] strikes =
            {
                new StrikeRecord(StrikeKind.Landed, foe, me, 5, 40, at), new StrikeRecord(StrikeKind.Started, foe, me, 5, 999, at),
                new StrikeRecord(StrikeKind.Blocked, foe, me, 5, 999, at), new StrikeRecord(StrikeKind.Whiffed, foe, me, 5, 999, at),
                new StrikeRecord(StrikeKind.Landed, foe, other, 5, 100, at),
            };
            Assert.That(Pressure.Kills(me, fires, strikes, 70), Is.True);
            Assert.That(Pressure.Kills(me, fires, strikes, 71), Is.False);
            Assert.That(Pressure.Kills(me, fires, default, 30), Is.True);
            Assert.That(Pressure.Kills(me, fires, default, 31), Is.False);
            Assert.That(Pressure.Kills(me, default, strikes, 40), Is.True);
            Assert.That(Pressure.Kills(me, default, strikes, 41), Is.False);
            Assert.That(Pressure.Kills(other, fires, strikes, 200), Is.True);
            Assert.That(Pressure.Kills(other, fires, strikes, 201), Is.False);
            // A record with negative damage takes nothing away.
            Assert.That(Pressure.Kills(me, new[] { fires[0], new FireRecord(foe, me, at, at, default, -30, 2) }, default, 30), Is.True);
        }

        // Under the RWR resolver a hit kills or does nothing, so the two rules press every unit the same, tick by tick.
        [Test]
        public void Fall_KillRuleEqualsHitRuleUnderTheRwrResolver()
        {
            M6World hit = Battle(41, null, Json(Values, BoundAi)), kill = Battle(41, null, Json(Values, BoundAi + ", " + KillField));
            Assert.That(kill.System<Pressure>().StateVersion, Is.EqualTo(hit.System<Pressure>().StateVersion + 2));
            int pressed = 0;
            for (int tick = 1; tick <= 1200; tick++)
            {
                hit.Step(); kill.Step();
                for (ulong key = 1; key <= 300; key++)
                {
                    int a = hit.System<Pressure>().Value(Id(hit, key)), b = kill.System<Pressure>().Value(Id(kill, key));
                    Assert.That(b, Is.EqualTo(a), "tick " + tick + " key " + key);
                    if (a > 0) pressed++;
                }
            }
            Assert.That(pressed, Is.GreaterThan(0), "somebody was pressed");
            int dead = 0;
            for (ulong key = 1; key <= 300; key++) if (hit.Dead(key)) { dead++; Assert.That(kill.Dead(key), Is.True, "key " + key); }
            Assert.That(dead, Is.GreaterThan(0), "somebody fell, so the death shock was in play");
        }

        // Shots that take 60 % of full health: the first wounds, the second kills.
        private sealed class WoundResolver : IHitResolver
        {
            public ulong PolicyKey => 0x5445535400000060;
            public long DamageRaw(in WeaponDef weapon, long falloffRaw, int rollRaw, long maxHealthRaw) => maxHealthRaw * 6 / 10;
        }

        // The S3 field with wounding shots and the attackers 25 m from the defenders, in sight. Only falls press (no near
        // shock) and every fall reaches the whole team (100 m).
        private static M6World WoundField(bool killRule)
        {
            var spawns = new List<UnitSpawn>();
            for (int i = 0; i < Attackers; i++) spawns.Add(new UnitSpawn((ulong)i + 1, 0, 1, 85000, 10500 + 1000 * i, 6000, 1, 0, false));
            for (int i = 0; i < Defenders; i++) spawns.Add(M6World.U((ulong)(Attackers + i + 1), 2, i < 5 ? 108 : 112, 16 + i % 5 * 2, weapon: 1, speed: 1));
            var points = new[] { new ControlPointDef(1, Theirs, SimFix.FromInt(6), 2), new ControlPointDef(2, Own, SimFix.FromInt(6), 1) };
            var bases = new[] { new SpawnAnchor(1, SimVec3.Metres(6, 0, 20)), new SpawnAnchor(2, SimVec3.Metres(150, 0, 20)) };
            string ai = Json(new[] { 100000, 2000, 3000, 0, 12, 7000, 1000, 2000, 0, 300 }, BoundAi + (killRule ? ", " + KillField : ""));
            WorldRecipe recipe = M6World.RecipeOf(spawns.ToArray(), M6World.Pack(points, ticks: 1000000, think: Think, bases: bases), M6World.Guns(new WoundResolver()))
                .WithRuleSet(MatchMode.TestNeverEndsId).WithAiRules(Pack(ai));
            return new M6World(recipe, Flat());
        }

        // Counts the ticks on which a unit's pressure rose, split by whether a teammate of it died on that tick, and the
        // living units that did not rise on a tick a teammate died (every fall reaches the whole team).
        private static (int withDeath, int withoutDeath, int wounds, int missed) Rises(bool killRule, int ticks)
        {
            M6World w = WoundField(killRule);
            const int units = Attackers + Defenders;
            var value = new int[units + 1]; var dead = new bool[units + 1];
            int withDeath = 0, withoutDeath = 0, wounds = 0, missed = 0;
            for (int tick = 1; tick <= ticks; tick++)
            {
                w.Step();
                var died = new bool[3];
                for (ulong key = 1; key <= units; key++)
                {
                    bool now = w.Dead(key);
                    if (now && !dead[key]) died[key <= Attackers ? 1 : 2] = true;
                    dead[key] = now;
                }
                foreach (FireRecord fire in w.Combat.Fires.ToArray())
                    if (fire.DamageRaw > 0 && w.Sim.Entities.TryResolve(fire.Target, out EntityRef target) && !w.Combat.IsDead((EntityId)target)) wounds++;
                for (ulong key = 1; key <= units; key++)
                {
                    int now = dead[key] ? 0 : w.System<Pressure>().Value(Id(w, key));
                    bool rose = now > Math.Max(0, Decayed(value[key], 12)), teamDied = died[key <= Attackers ? 1 : 2];
                    if (!dead[key] && rose) { if (teamDied) withDeath++; else withoutDeath++; }
                    if (!dead[key] && teamDied && !rose) missed++;
                    value[key] = now;
                }
            }
            return (withDeath, withoutDeath, wounds, missed);
        }

        // In a real battle with wounding shots, kill presses only on ticks a teammate died; hit also presses on wounds.
        [Test]
        public void Fall_KillRuleIgnoresWoundsInABattle()
        {
            var (killDeath, killWound, killWounds, killMissed) = Rises(true, 900);
            Assert.That(killWounds, Is.GreaterThan(0), "some shots only wounded");
            Assert.That(killDeath, Is.GreaterThan(0), "a fall pressed somebody");
            Assert.That(killWound, Is.EqualTo(0), "no rise without a teammate dying");
            Assert.That(killMissed, Is.EqualTo(0), "a wounded unit's last shot is a fall too: health is read, not assumed full");
            var (_, hitWound, _, _) = Rises(false, 900);
            Assert.That(hitWound, Is.GreaterThan(0), "under the hit rule a wound presses too");
        }

        [Test]
        public void Fall_ContentFieldPicksTheRule()
        {
            Assert.That(Pack(Json(Values)).Section<AiRules>().Pressure.Value.FallOnKill, Is.False);
            Assert.That(Pack(Json(Values, "\"pressureFall\": \"hit\"")).Section<AiRules>().Pressure.Value.FallOnKill, Is.False);
            PressureRules kill = Pack(Json(Values, KillField)).Section<AiRules>().Pressure.Value;
            Assert.That(kill.FallOnKill, Is.True);
            Assert.That(new[] { kill.DeathRadiusMm, kill.DeathShock, kill.NearRadiusMm, kill.NearShock, kill.DecayPermille, kill.BrokenAt, kill.RecoverAt, kill.PinnedAt, kill.SpreadPermille, kill.BreakSharePermille },
                Is.EqualTo(Values));
            var bad = Assert.Throws<ContentException>(() => Pack(Json(Values, "\"pressureFall\": \"wound\"")));
            Assert.That(bad.Message, Is.EqualTo("content pack 's3-test' layer 's3-test' ai field 'pressureFall': unknown value 'wound', allowed: hit, kill"));
            var number = Assert.Throws<ContentException>(() => Pack(Json(Values, "\"pressureFall\": 1")));
            Assert.That(number.Message, Is.EqualTo("content pack 's3-test' layer 's3-test' ai field 'pressureFall': must be a string"));
            var alone = Assert.Throws<ContentException>(() => Pack("{ " + KillField + " }"));
            Assert.That(alone.Message, Is.EqualTo("content pack 's3-test' layer 's3-test' ai field 'pressureFall': needs the pressure fields (pressureDeathRadiusMm ...)"));
            // The battle builds the system with the rule read.
            Assert.That(Field(Json(Values, BoundAi + ", " + KillField)).System<Pressure>().StateVersion, Is.EqualTo(3));
            Assert.That(Field(Json(Values, BoundAi)).System<Pressure>().StateVersion, Is.EqualTo(1));
        }

        // The rule is part of the system's version, its saved rules and its hash; one rule refuses the other's state.
        [Test]
        public void Fall_RuleIsSavedHashedAndChecked()
        {
            M6World w = Field(null);
            PressureRules hitRules = Rules(), killRules = new PressureRules(3000, 2000, 3000, 1000, 12, 7000, 1000, 2000, 0, 1000, fallOnKill: true);
            Assert.That(new Pressure(w.Combat, hitRules).StateVersion, Is.EqualTo(1));
            Assert.That(new Pressure(w.Combat, killRules).StateVersion, Is.EqualTo(3));
            Assert.That(new Pressure(w.Combat, hitRules, true).StateVersion, Is.EqualTo(2));
            Assert.That(new Pressure(w.Combat, killRules, true).StateVersion, Is.EqualTo(4));
            // Same entries, the rule alone differs: the hashes differ.
            var hit = new Pressure(w.Combat, hitRules); var kill = new Pressure(w.Combat, killRules);
            hit.Advance(new[] { Fall(w, 10) }); kill.Advance(new[] { Hit(w, 10, w.Combat.HealthRaw(Id(w, 10))) });
            Assert.That(kill.Value(Id(w, 11)), Is.EqualTo(hit.Value(Id(w, 11))));
            var hitHash = new StateHash(); hit.WriteState(hitHash); var killHash = new StateHash(); kill.WriteState(killHash);
            Assert.That(killHash.Value, Is.Not.EqualTo(hitHash.Value));
            // Each loads its own state; neither loads the other's.
            byte[] hitSaved = Save(hit), killSaved = Save(kill);
            var copy = new Pressure(w.Combat, killRules);
            Assert.DoesNotThrow(() => copy.LoadState(new SimStateReader(killSaved)));
            var copyHash = new StateHash(); copy.WriteState(copyHash);
            Assert.That(copyHash.Value, Is.EqualTo(killHash.Value));
            Assert.That(Save(copy), Is.EqualTo(killSaved));
            Assert.Catch(() => new Pressure(w.Combat, killRules).LoadState(new SimStateReader(hitSaved)));
            Assert.Catch(() => new Pressure(w.Combat, hitRules).LoadState(new SimStateReader(killSaved)));
        }

        // A kill-rule battle saved at tick 150 and restored runs on to tick 300 with the same hash.
        [Test]
        public void Fall_KillRuleBattleRestoresMidway()
        {
            string ai = Json(Values, BoundAi + ", " + KillField);
            M6World straight = Battle(41, null, ai), resumed = Battle(41, null, ai);
            straight.Step(150);
            byte[] saved = straight.Sim.SaveRetainedSnapshot();
            straight.Step(150);
            Assert.DoesNotThrow(() => resumed.Sim.RestoreRetainedSnapshot(saved));
            resumed.Step(150);
            Assert.That(resumed.Hash, Is.EqualTo(straight.Hash));
            // The hit-rule battle refuses the kill-rule snapshot.
            M6World other = Battle(41, null, Json(Values, BoundAi));
            other.Step();
            Assert.Catch(() => other.Sim.RestoreRetainedSnapshot(saved));
        }

        private static byte[] Save(Pressure p) { var writer = new SimStateWriter(); p.SaveState(writer); return writer.ToArray(); }
    }
}
