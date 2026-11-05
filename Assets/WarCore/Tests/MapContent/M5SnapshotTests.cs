using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class M5SnapshotTests
    {
        [Test]
        public void C0_PLACEHOLDER_DemoEndsBy1800_ReplayAndReverseCreationMatchEveryTick()
        {
            var recipe = DemoRecipes.M5(); var map = DemoRecipes.M2bMapBytes();
            var a = SimSession.Create(recipe, map); var b = SimSession.Create(DemoRecipes.M5(), map);
            var reverse = new M5TestWorld(recipe.Units.ToArray(), recipe.Combat, reverse: true, map: M2DemoMap.Build());
            Assert.That(M3TestWorld.Unit(a), Is.Not.EqualTo(reverse.Unit(0)), "Must reverse physical creation, not input sorting.");
            Assert.That(reverse.Sim.StateHashRetained(), Is.EqualTo(a.StateHash));
            int end = 0, firstDeath = 0, alive1 = 10, alive2 = 10;
            for (int tick = 1; tick <= 1800; tick++)
            {
                a.StepOnce(); b.StepOnce();
                reverse.Sim.Step(tick == 1 ? M4TestWorld.Command(1, 44000, 22000) : tick == 2 ? M4TestWorld.Command(2, 56000, 22000) : default);
                Assert.That(b.StateHash, Is.EqualTo(a.StateHash), "repeat tick=" + tick);
                Assert.That(reverse.Sim.StateHashRetained(), Is.EqualTo(a.StateHash), "reverse tick=" + tick);
                Assert.That(reverse.Combat.Fires.ToArray().Select(f => SimSession.ToView(f)), Is.EqualTo(a.Current.Fires.ToArray()));
                alive1 = alive2 = 0;
                foreach (var unit in a.Current.Entities) if (unit.Alive) { if (unit.Team == 1) alive1++; else if (unit.Team == 2) alive2++; }
                if (firstDeath == 0 && alive1 + alive2 < 20) firstDeath = tick;
                if (end == 0 && (alive1 == 0 || alive2 == 0))
                { end = tick; TestContext.Out.WriteLine($"C0 end={end} winner={(alive1 == alive2 ? "draw" : alive1 > 0 ? "Team1" : "Team2")} survivors={alive1}:{alive2} firstDeath={firstDeath}"); }
            }
            Assert.That(end, Is.InRange(1, 1800));
        }

        private static void EqualStates(M5TestWorld a, M5TestWorld b)
        {
            Assert.That(b.Sim.StateHashRetained(), Is.EqualTo(a.Sim.StateHashRetained()), "tick=" + a.Sim.Clock.Tick);
            for (int i = 0; i < a.Count; i++)
            {
                // Unit resolves stable identity each time, including after restore.
                Assert.That(b.State(i).Ammo, Is.EqualTo(a.State(i).Ammo));
                Assert.That(b.State(i).DueMilli, Is.EqualTo(a.State(i).DueMilli));
            }
        }
        [TestCase(20)]
        [TestCase(100)]
        public void C0_C1_PLACEHOLDER_FullSnapshot_MagazineAndReload_ReverseWorld(int saveTick)
        {
            var units = M5TestWorld.Pair(); var pack = M5TestWorld.Pack(new ZeroResolver());
            var a = new M5TestWorld(units, pack); var b = new M5TestWorld(units, pack, reverse: true);
            for (int i = 0; i < saveTick; i++) a.Step();
            if (saveTick == 20) Assert.That(a.State(0).Ammo, Is.InRange(1, 29));
            else { Assert.That(a.State(0).Ammo, Is.EqualTo(30)); Assert.That(a.State(0).DueMilli, Is.GreaterThan((saveTick + 50) * 1000)); }
            var snapshot = a.Sim.SaveRetainedSnapshot(); b.Sim.RestoreRetainedSnapshot(snapshot);
            Assert.That(b.Sim.SaveRetainedSnapshot(), Is.EqualTo(snapshot)); EqualStates(a, b);
            Assert.That(b.Combat.Fires.Length, Is.Zero, "Fire records are not restored state.");
            for (int tick = saveTick + 1; tick <= 450; tick++)
            { a.Step(); b.Step(); EqualStates(a, b); Assert.That(b.Combat.Fires.ToArray(), Is.EqualTo(a.Combat.Fires.ToArray())); }
        }
        [Test]
        public void C0_C6_PLACEHOLDER_FullSnapshot_AfterDeath_ReverseWorld()
        {
            var recipe = DemoRecipes.M5(); var units = recipe.Units.ToArray();
            var a = new M5TestWorld(units, recipe.Combat, map: M2DemoMap.Build());
            int saved = 0;
            for (int tick = 1; tick <= 1800 && saved == 0; tick++)
            {
                a.Sim.Step(tick == 1 ? M4TestWorld.Command(1, 44000, 22000) : tick == 2 ? M4TestWorld.Command(2, 56000, 22000) : default);
                for (int i = 0; i < 20; i++) if (a.Dead(i)) saved = tick;
            }
            Assert.That(saved, Is.GreaterThan(2));
            var b = new M5TestWorld(units, RwrPack.Create(), reverse: true, map: M2DemoMap.Build());
            byte[] snapshot = a.Sim.SaveRetainedSnapshot(); b.Sim.RestoreRetainedSnapshot(snapshot);
            Assert.That(b.Sim.SaveRetainedSnapshot(), Is.EqualTo(snapshot)); EqualStates(a, b);
            for (int tick = saved + 1; tick <= 1800; tick++)
            { a.Step(); b.Step(); EqualStates(a, b); Assert.That(b.Combat.Fires.ToArray(), Is.EqualTo(a.Combat.Fires.ToArray())); }
            TestContext.Out.WriteLine("C0 death snapshot tick=" + saved);
        }
        [Test]
        public void C0_PLACEHOLDER_ChangedWeaponPolicyRejectsSnapshot_AndChangesHash()
        {
            var pack = RwrPack.Create(); var weapons = pack.Weapons.ToArray(); var w = weapons[0];
            weapons[0] = new WeaponDef(w.FireIntervalMilli + 1, w.Magazine, w.ReloadMilli, w.FalloffStart, w.FalloffEnd, w.EffectRaw);
            var changed = new CombatPack(weapons, pack.Resolver, pack.Selector, pack.MaxHealthRaw);
            var a = new M5TestWorld(M5TestWorld.Pair(), pack); var b = new M5TestWorld(M5TestWorld.Pair(), changed);
            Assert.That(b.Sim.StateHashRetained(), Is.Not.EqualTo(a.Sim.StateHashRetained()));
            a.Step(); Assert.Catch<InvalidOperationException>(() => b.Sim.RestoreRetainedSnapshot(a.Sim.SaveRetainedSnapshot()));
        }
        [Test]
        public void C0_C1_PLACEHOLDER_CombatantFieldsAffectStateHash()
        {
            var a = new M5TestWorld(M5TestWorld.Pair()); ulong before = a.Sim.StateHashRetained(); var u = a.State(0);
            a.Sim.Entities.Set(a.Combatants, a.Unit(0), new Combatant(u.Team, u.Weapon, u.Ammo - 1, 3390));
            Assert.That(a.Sim.StateHashRetained(), Is.Not.EqualTo(before));
        }
        [Test]
        public void C0_PLACEHOLDER_ViewCopiesShotsAndPreservesTeamAlive()
        {
            var session = SimSession.Create(new WorldRecipe(41, MoverKind.Interim, M5TestWorld.Pair(2), Array.Empty<BridgeCommand>()).WithCombat(RwrPack.Create()));
            session.StepOnce(); FrameView frame = session.Current;
            var interpolated = FrameView.Interpolate(session.Previous, frame, 0.5f);
            Assert.That(frame.Fires.Length, Is.EqualTo(1)); Assert.That(interpolated.Fires.ToArray(), Is.EqualTo(frame.Fires.ToArray()));
            Assert.That(interpolated.Entities[1].Alive, Is.False); Assert.That(interpolated.Entities[1].Team, Is.EqualTo(2));
            session.StepOnce(); Assert.That(session.Current.Fires.Length, Is.Zero); Assert.That(frame.Fires.Length, Is.EqualTo(1));
            var source = frame.Fires.ToArray(); var copy = new FrameView(1, frame.Entities.ToArray(), source); source[0] = default;
            Assert.That(copy.Fires[0], Is.EqualTo(frame.Fires[0]));
            Assert.That(new ViewEntity(1, 0, 0, 0, 0).Alive, Is.True); Assert.That(new ViewEntity(1, 0, 0, 0, 0).Team, Is.Zero);
        }
        [Test]
        public void C0_LegacyM1M2bM3M4_Fixed600Hashes()
        {
            var recipes = new[] { new WorldRecipe(41, MoverKind.Interim, new[] { new UnitSpawn(1, 0, 1, 0, 0, 3000) },
                new[] { BridgeCommand.MoveTo(1, 1, 10000, 0), BridgeCommand.MoveTo(150, 1, 10000, 10000) }), DemoRecipes.M2b(), DemoRecipes.M3(), DemoRecipes.M4() };
            ulong[] expected = { 0x62306703E44CF8EB, 0xCEDECB8218C40918, 0x53DA7E1D51CE24F1, 0x605D078885B2B633 };
            for (int i = 0; i < 4; i++)
            {
                var session = i == 0 ? SimSession.Create(recipes[i]) : SimSession.Create(recipes[i], DemoRecipes.M2bMapBytes());
                for (int tick = 0; tick < 600; tick++) session.StepOnce();
                Assert.That(session.StateHash, Is.EqualTo(expected[i])); Assert.That(session.Current.Fires.Length, Is.Zero);
            }
        }
    }
}
