using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Aim1.Aim1Worlds;

namespace WarCore.Tests.Aim1
{
    // AIM-1 A6 (TryReload), A7 (the action gate) and A10 (the script command). rwr.weapon-1: magazine 30, fire interval
    // 3390, reload 60000 thousandths of a tick. TryReload is asked between ticks; "now" is the tick about to run.
    public sealed class Aim1ReloadTests
    {
        private const long Interval = 3390, Reload = 60000;

        // Unit 1 (team 1, squad 1) alone, or with an unarmed enemy 30 m away when it should shoot.
        private static SimSession Lone(string layer = "aim1-harmless", bool target = false, BridgeCommand[] script = null) => World(Load(layer),
            target ? new[] { Unit(1, 1, 1, 50000, 10000), Unit(2, 2, 2, 50000, 40000, weapon: -1) } : new[] { Unit(1, 1, 1, 50000, 10000) }, script);

        private static Combatant With(Combatant c, int ammo, long due) => new Combatant(c.Team, c.Weapon, ammo, due, c.SpreadStored, c.SpreadLastMilli);

        [Test]
        public void A6_AFullMagazineIsRefusedAndNothingIsWritten()
        {
            SimSession s = Lone();
            for (int i = 0; i < 3; i++) s.StepOnce();
            Combatant before = State(s, 1);
            Assert.That(before.Ammo, Is.EqualTo(30));
            ulong hash = s.StateHash;
            Assert.That(CombatOf(s).TryReload(Id(s, 1)), Is.False);
            Assert.That(State(s, 1), Is.EqualTo(before));
            Assert.That(s.StateHash, Is.EqualTo(hash));
        }

        [Test]
        public void A6_AfterTheFireIntervalTheReloadStartsNow()
        {
            SimSession s = Lone();
            for (int i = 0; i < 9; i++) s.StepOnce();
            Combatant fired = new Combatant(1, 0, 29, 5000, 1340, 1000);
            SetState(s, 1, fired);
            Assert.That(CombatOf(s).TryReload(Id(s, 1)), Is.True);
            Combatant after = State(s, 1);
            Assert.That(after.Ammo, Is.EqualTo(30));
            Assert.That(after.DueMilli, Is.EqualTo(10 * 1000 + Reload), "asked before tick 10: 10 * 1000 + ReloadMilli");
            Assert.That((after.Team, after.Weapon, after.SpreadStored, after.SpreadLastMilli), Is.EqualTo((1, 0, 1340, 1000L)), "the other four fields stay");
            Assert.That(CombatOf(s).TryReload(Id(s, 1)), Is.False, "a reload in progress counts as full");
            Assert.That(State(s, 1), Is.EqualTo(after));
        }

        [Test]
        public void A6_ReloadRightAfterAShotWaitsForTheShotFirst()
        {
            SimSession s = Lone();
            for (int i = 0; i < 9; i++) s.StepOnce();
            SetState(s, 1, new Combatant(1, 0, 29, 9000 + Interval, 0, 0));
            Assert.That(CombatOf(s).TryReload(Id(s, 1)), Is.True);
            Assert.That(State(s, 1).DueMilli, Is.EqualTo(9000 + Interval + Reload), "the start is the shot's due time, not now");
        }

        [Test]
        public void A6_TheStartRuleIsTheAutomaticReloadsOne()
        {
            // Due inside the previous tick (9001..10000 for tick 10): the start is the due time; at or before 9000: now (10000).
            foreach ((long due, long start) in new[] { (9001L, 9001L), (10000L, 10000L), (9000L, 10000L), (0L, 10000L) })
            {
                SimSession s = Lone();
                for (int i = 0; i < 9; i++) s.StepOnce();
                SetState(s, 1, new Combatant(1, 0, 5, due, 0, 0));
                Assert.That(CombatOf(s).TryReload(Id(s, 1)), Is.True, "due " + due);
                Assert.That(State(s, 1).DueMilli, Is.EqualTo(start + Reload), "due " + due);
            }
        }

        [Test]
        public void A6_TheUnitCannotFireBeforeTheReloadEndsAndFiresOnItsTick()
        {
            SimSession s = Lone(target: true);
            SetState(s, 1, With(State(s, 1), 29, 0));
            Assert.That(CombatOf(s).TryReload(Id(s, 1)), Is.True);
            Assert.That(State(s, 1).DueMilli, Is.EqualTo(1000 + Reload), "asked before tick 1");
            long first = 0;
            for (int tick = 1; tick <= 70 && first == 0; tick++)
            {
                s.StepOnce();
                if (CombatOf(s).Fires.Length > 0) first = tick;
            }
            Assert.That(first, Is.EqualTo(61), "due 61000 = tick 61");
            Assert.That(State(s, 1).Ammo, Is.EqualTo(29));
        }

        [Test]
        public void A6_RefusedWithoutTheSwitchWithoutAWeaponOrWhenDead()
        {
            SimSession off = Lone("aim1-no-reload");
            SetState(off, 1, With(State(off, 1), 29, 0));
            Assert.That(CombatOf(off).TryReload(Id(off, 1)), Is.False, "the pack has no manual reload");
            Assert.That(State(off, 1).Ammo, Is.EqualTo(29));
            SimSession field = Lone("aim1-harmless-field");
            SetState(field, 1, With(State(field, 1), 29, 0));
            Assert.That(CombatOf(field).TryReload(Id(field, 1)), Is.False, "rwr-field has no manual reload");

            SimSession unarmed = World(Load("aim1-harmless"), new[] { Unit(1, 1, 1, 50000, 10000, weapon: -1) });
            bool reloaded = true;
            Assert.DoesNotThrow(() => reloaded = CombatOf(unarmed).TryReload(Id(unarmed, 1)));
            Assert.That(reloaded, Is.False, "no weapon");

            // Unit 2 (unarmed) is shot dead by unit 1 with the shipped rwr-aim layer (rwr.hit is lethal at 30 m half the time).
            SimSession lethal = World(Load("rwr-aim"), new[] { Unit(1, 1, 1, 50000, 10000), Unit(2, 2, 2, 50000, 40000, weapon: -1) });
            for (int t = 0; t < 200 && !CombatOf(lethal).IsDead(Id(lethal, 2)); t++) lethal.StepOnce();
            Assert.That(CombatOf(lethal).IsDead(Id(lethal, 2)), Is.True);
            SetState(lethal, 2, new Combatant(2, 0, 3, 0, 0, 0));
            Assert.That(CombatOf(lethal).TryReload(Id(lethal, 2)), Is.False, "dead");
            Assert.That(State(lethal, 2).Ammo, Is.EqualTo(3));
        }

        [Test]
        public void A6_AnEntityWithoutCombatStateIsRefused()
        {
            SimSession s = Lone();
            Assert.That(CombatOf(s).TryReload(new EntityId(0, 1)), Is.False);
        }

        [Test]
        public void A7_TheActionGateCanForbidReloading()
        {
            SimSession s = Lone();
            SetState(s, 1, With(State(s, 1), 29, 0));
            var gate = new ActionGate();
            gate.Add(new Deny(UnitAction.Fire));
            CombatOf(s).UseActionGate(gate);
            var denyReload = new ActionGate();
            denyReload.Add(new Deny(UnitAction.Reload | UnitAction.Move));
            CombatOf(s).UseActionGate(denyReload);
            Assert.That(CombatOf(s).TryReload(Id(s, 1)), Is.False, "Reload denied");
            Assert.That(State(s, 1).Ammo, Is.EqualTo(29));
            CombatOf(s).UseActionGate(gate);
            Assert.That(CombatOf(s).TryReload(Id(s, 1)), Is.True, "only Fire denied");
            SetState(s, 1, With(State(s, 1), 29, 0));
            CombatOf(s).UseActionGate(null);
            Assert.That(CombatOf(s).TryReload(Id(s, 1)), Is.True, "no gate: allowed");
        }

        // A10: squad 1 = units 1 (29 rounds) and 2 (full); squad 2 = unit 3 (29 rounds). The reload asked for squad 1 at tick 5
        // refills unit 1 only; unit 2 (full) and unit 3 (another squad) are untouched; the frame of tick 5 has no squad order.
        private static SimSession Squads(BridgeCommand[] script)
        {
            SimSession s = World(Load("aim1-harmless"), new[] { Unit(1, 1, 1, 10000, 10000), Unit(2, 1, 1, 12000, 10000), Unit(3, 2, 1, 10000, 20000) }, script);
            SetState(s, 1, With(State(s, 1), 29, 0));
            SetState(s, 3, With(State(s, 3), 29, 0));
            return s;
        }

        private static readonly BridgeCommand[] Walk = { BridgeCommand.MoveTo(1, 1, 80000, 10000), BridgeCommand.MoveTo(2, 2, 80000, 20000) };

        [Test]
        public void A10_TheCommandReloadsTheSquadBeforeItsTick()
        {
            var script = new List<BridgeCommand>(Walk) { BridgeCommand.Reload(5, 1) };
            SimSession s = Squads(script.ToArray()), plain = Squads(Walk);
            for (int tick = 1; tick <= 4; tick++) { s.StepOnce(); plain.StepOnce(); }
            Assert.That(State(s, 1).Ammo, Is.EqualTo(29));
            s.StepOnce(); plain.StepOnce();
            Assert.That(State(s, 1).Ammo, Is.EqualTo(30));
            Assert.That(State(s, 1).DueMilli, Is.EqualTo(5 * 1000 + Reload), "asked before tick 5");
            Assert.That(State(s, 2), Is.EqualTo(State(plain, 2)), "full magazine: untouched");
            Assert.That(State(s, 3).Ammo, Is.EqualTo(29), "another squad");
            Assert.That(s.CommandLog.Count, Is.EqualTo(3));
            Assert.That(s.CommandLog[2].Kind, Is.EqualTo(BridgeCommandKind.Reload));
            Assert.That((s.CommandLog[2].AtTick, s.CommandLog[2].Squad), Is.EqualTo((5L, 1)));
            // No squad order rode on tick 5: the squads keep walking exactly as without the command.
            for (int tick = 6; tick <= 40; tick++)
            {
                s.StepOnce(); plain.StepOnce();
                for (ulong key = 1; key <= 3; key++)
                    Assert.That(M3TestWorld.Position(s, key), Is.EqualTo(M3TestWorld.Position(plain, key)), "tick " + tick + " unit " + key);
            }
            Assert.That(M3TestWorld.Position(s, 1).X.Raw, Is.GreaterThan(15 * S), "still walking");
        }

        [Test]
        public void A10_TheLoggedCommandsReplayTheSameWorld()
        {
            var script = new List<BridgeCommand>(Walk) { BridgeCommand.Reload(5, 1), BridgeCommand.Reload(9, 2) };
            SimSession a = Squads(script.ToArray());
            var hashes = new List<ulong>();
            for (int i = 0; i < 30; i++) { a.StepOnce(); hashes.Add(a.StateHash); }
            var logged = new BridgeCommand[a.CommandLog.Count];
            for (int i = 0; i < logged.Length; i++) logged[i] = a.CommandLog[i];
            SimSession b = Squads(logged);
            for (int i = 0; i < 30; i++) { b.StepOnce(); Assert.That(b.StateHash, Is.EqualTo(hashes[i]), "tick " + (i + 1)); }
            Assert.That(State(b, 3).Ammo, Is.EqualTo(30));
            Assert.That(State(b, 3).DueMilli, Is.EqualTo(9 * 1000 + Reload));
        }

        [Test]
        public void A10_DeadAndControlledUnitsAreSkipped()
        {
            SimSession s = WorldBuilder.Build(new WorldRecipe(41, MoverKind.InterimCrowd, new[] { Unit(1, 1, 1, 10000, 10000), Unit(2, 1, 1, 12000, 10000), Unit(3, 3, 2, 90000, 50000, weapon: -1) },
                new[] { BridgeCommand.Reload(2, 1) }).WithCombat(Load("aim1-harmless").BuildCombat()).WithMatch(Aim1Match()).WithPlayer(1), Map(), false);
            SetState(s, 1, With(State(s, 1), 29, 0));
            SetState(s, 2, With(State(s, 2), 29, 0));
            s.StepOnce(); s.StepOnce();
            Assert.That(s.IsFinished, Is.False);
            Assert.That(s.CommandLog.Count, Is.EqualTo(1));
            Assert.That(State(s, 1).Ammo, Is.EqualTo(29), "the player's unit is not reloaded by a squad command");
            Assert.That(State(s, 2).Ammo, Is.EqualTo(30));
        }

        [Test]
        public void A10_AWorldWithoutCombatIgnoresTheCommand()
        {
            SimSession s = WorldBuilder.Build(new WorldRecipe(41, MoverKind.InterimCrowd, new[] { new UnitSpawn(1, 0, 1, 10000, 10000, Run) },
                new[] { BridgeCommand.Reload(2, 1), BridgeCommand.MoveTo(3, 1, 30000, 10000) }), Map(), false);
            Assert.DoesNotThrow(() => { for (int i = 0; i < 10; i++) s.StepOnce(); });
            Assert.That(s.CommandLog.Count, Is.EqualTo(2));
            Assert.That(M3TestWorld.Position(s, 1).X.Raw, Is.GreaterThan(10 * S), "the next command still runs");
        }

        [Test]
        public void A10_TheCommandIsCheckedLikeTheOthers()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => BridgeCommand.Reload(0, 1));
            BridgeCommand c = BridgeCommand.Reload(7, 3);
            Assert.That((c.Kind, c.AtTick, c.Squad, c.XMm, c.ZMm, c.TargetKey, c.Layer), Is.EqualTo((BridgeCommandKind.Reload, 7L, 3, 0L, 0L, 0UL, 0)));
        }

        private static WarCore.Battle.MatchPack Aim1Match() =>
            Load("aim1-harmless").BuildMatch(new[] { new WarCore.Battle.ControlPointDef(1, SimVec3.Metres(50, 0, 30), SimFix.FromInt(5)) },
                new[] { new WarCore.Battle.SpawnAnchor(1, SimVec3.Metres(10, 0, 10)), new WarCore.Battle.SpawnAnchor(2, SimVec3.Metres(90, 0, 50)) });
    }
}
