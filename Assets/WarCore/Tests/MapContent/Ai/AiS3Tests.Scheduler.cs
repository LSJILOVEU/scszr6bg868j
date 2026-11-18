using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // AiS3Tests: the scheduler (falling back, standing) and whole battles on the control layout.
    public sealed partial class AiS3Tests
    {
        // ---- The scheduler ----

        // Keys 7..13 without 10: the attackers within 3 m of key 10 while they stand abreast.
        private static readonly ulong[] Pressed = { 7, 8, 9, 11, 12, 13 };
        // Every attacker but key 10: who a fall of key 10 presses under a radius that covers the field. Once the attackers
        // walk they are no longer abreast, so the scheduler tests use that radius.
        private static readonly ulong[] Rest = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 };
        private const int Wide = 100000;

        // A broken unit that sees nobody goes to its team's point behind it, stands there, and goes forward again once
        // it has recovered. Key 10, which nothing presses, walks on.
        [Test]
        public void Broken_FallsBackToTheNearestOwnPointAndReturns()
        {
            // 4 thousandths a tick: 10000 is above 1000 for more than 570 ticks.
            M6World w = Field(Inject(1000, BoundAi, Wide, 4));
            Pressure p = w.System<Pressure>();
            Assert.That(p, Is.Not.Null);
            w.Step(5);
            p.Advance(new[] { Fall(w, 10) });
            foreach (ulong key in Rest) Assert.That(p.Level(Id(w, key)), Is.EqualTo(AiNerve.Broken), "key " + key);
            Assert.That(p.Level(Id(w, 10)), Is.EqualTo(AiNerve.Calm));
            w.Step(Think);
            for (ulong key = 1; key <= Attackers; key++)
            {
                MoveIntent order = w.Read<MoveIntent>(key);
                Assert.That(order.Active, Is.True, "key " + key);
                Assert.That(Horizontal2(order.Target, key == 10 ? Theirs : Own), Is.LessThanOrEqualTo(Metres2(6)), "key " + key);
            }
            // 20 m at 6 m/s is 100 ticks, and nineteen units need a little longer to sort out their slots.
            w.Step(270);
            foreach (ulong key in Rest)
            {
                Assert.That(p.Level(Id(w, key)), Is.EqualTo(AiNerve.Broken), "key " + key);
                // A unit stops within the arrival distance of its slot, so one at the rim may stand just outside the circle.
                Assert.That(Horizontal2(At(w, key), Own), Is.LessThanOrEqualTo(Metres2(7)), "key " + key);
                Assert.That(w.Read<MoveIntent>(key).Active, Is.False, "key " + key);
            }
            SimVec3 stood = At(w, 8);
            w.Step(20);
            Assert.That(At(w, 8), Is.EqualTo(stood));
            // Recovered (after tick 580 or so): forward again.
            w.Step(300 + 2 * Think);
            foreach (ulong key in Rest)
            {
                Assert.That(p.Level(Id(w, key)), Is.EqualTo(AiNerve.Calm), "key " + key);
                MoveIntent order = w.Read<MoveIntent>(key);
                Assert.That(order.Active, Is.True, "key " + key);
                Assert.That(Horizontal2(order.Target, Theirs), Is.LessThanOrEqualTo(Metres2(6)), "key " + key);
            }
        }

        // Without a point of its own a broken unit goes to its base; without a base either it stands.
        [TestCase(true)]
        [TestCase(false)]
        public void Broken_WithoutAPointGoesToTheBaseOrStands(bool ownBase)
        {
            M6World w = Field(Inject(1000, BoundAi, Wide), ownPoint: false, ownBase: ownBase);
            Pressure p = w.System<Pressure>();
            w.Step(Think + 5);
            foreach (ulong key in Rest) Assert.That(w.Read<MoveIntent>(key).Active, Is.True, "walking to the enemy point first, key " + key);
            p.Advance(new[] { Fall(w, 10) });
            w.Step(Think);
            SimVec3 home = SimVec3.Metres(6, 0, 20);
            foreach (ulong key in Rest)
            {
                MoveIntent order = w.Read<MoveIntent>(key);
                if (!ownBase) { Assert.That(order.Active, Is.False, "key " + key); continue; }
                Assert.That(order.Active, Is.True, "key " + key);
                Assert.That((order.Target.X.Raw, order.Target.Z.Raw), Is.EqualTo((home.X.Raw, home.Z.Raw)), "key " + key);
            }
            Assert.That(w.Read<MoveIntent>(10).Active, Is.True);
            Assert.That(Horizontal2(w.Read<MoveIntent>(10).Target, Theirs), Is.LessThanOrEqualTo(Metres2(6)));
        }

        // A pinned unit that sees nobody stops where it is until the pressure is off; it does not fall back.
        [Test]
        public void Pinned_StandsWhileItLasts()
        {
            M6World w = Field(Inject(0, BoundAi, Wide));
            Pressure p = w.System<Pressure>();
            w.Step(Think + 5);
            p.Advance(new[] { Fall(w, 10) });
            foreach (ulong key in Rest) Assert.That(p.Level(Id(w, key)), Is.EqualTo(AiNerve.Pinned), "key " + key);
            w.Step(Think);
            var stood = new Dictionary<ulong, SimVec3>();
            foreach (ulong key in Rest)
            {
                Assert.That(w.Read<MoveIntent>(key).Active, Is.False, "key " + key);
                stood[key] = At(w, key);
            }
            Assert.That(w.Read<MoveIntent>(10).Active, Is.True);
            // 10000 falls below 2000 after 134 ticks; 36 ticks are gone.
            w.Step(60);
            foreach (ulong key in Rest)
            {
                Assert.That(p.Level(Id(w, key)), Is.EqualTo(AiNerve.Pinned), "key " + key);
                Assert.That(At(w, key), Is.EqualTo(stood[key]), "key " + key);
            }
            // Calm from tick 135 after the fall; every unit thinks once in the 30 ticks after that.
            w.Step(50 + Think);
            foreach (ulong key in Rest)
            {
                Assert.That(p.Level(Id(w, key)), Is.EqualTo(AiNerve.Calm), "key " + key);
                Assert.That(w.Read<MoveIntent>(key).Active, Is.True, "key " + key);
                Assert.That(Horizontal2(w.Read<MoveIntent>(key).Target, Theirs), Is.LessThanOrEqualTo(Metres2(6)), "key " + key);
            }
        }

        // In sight of the enemy every unit advances in its move window (share 1000); a pinned one gets no order at all.
        [Test]
        public void Pinned_InSightDoesNotAdvance()
        {
            string all = "\"engage\": \"bound\", \"boundMoveTicks\": 60, \"boundFireTicks\": 30, \"boundSharePermille\": 1000, \"boundMinRangeMm\": 12000";
            // A fall anywhere presses the whole team here, so key 10 walking off does not let its neighbours go.
            M6World w = Field(Inject(0, all, 100000), attackersX: 80), calm = Field(Inject(0, all, 100000), attackersX: 80);
            Pressure p = w.System<Pressure>();
            int moved = 0, calmMoved = 0;
            for (int t = 0; t < 180; t++)
            {
                // Keep keys 7..13 pinned: a fall beside them every tick.
                p.Advance(new[] { Fall(w, 10) });
                w.Step(); calm.Step();
                foreach (ulong key in Pressed)
                {
                    Assert.That(p.Level(Id(w, key)), Is.EqualTo(AiNerve.Pinned), "key " + key);
                    if (w.Read<MoveIntent>(key).Active) moved++;
                    if (calm.Read<MoveIntent>(key).Active) calmMoved++;
                }
            }
            Assert.That(moved, Is.EqualTo(0));
            Assert.That(calmMoved, Is.GreaterThan(0), "the same units advance when nothing presses them");
            Assert.That(At(w, 8), Is.EqualTo(At(Field(null, attackersX: 80), 8)), "a pinned unit never left its spawn point");
        }

        // In sight of the enemy a broken unit turns round and goes to its own point, and while it is pressed it stays
        // there. It remembers where the enemy was, but a unit falling back neither suppresses nor searches: with every
        // unit on the field broken, both tables stay empty.
        [Test]
        public void Broken_InSightFallsBackAndStays()
        {
            string all = "\"targetMemoryTicks\": 300, \"searchTicks\": 90, \"suppressTicks\": 90, "
                + "\"engage\": \"bound\", \"boundMoveTicks\": 60, \"boundFireTicks\": 30, \"boundSharePermille\": 1000, \"boundMinRangeMm\": 12000";
            M6World w = Field(Inject(1000, all, Wide), attackersX: 80);
            Pressure p = w.System<Pressure>();
            TargetMemory memory = w.System<TargetMemory>(); TargetSearch search = w.System<TargetSearch>(); TargetSuppress suppress = w.System<TargetSuppress>();
            Assert.That(memory, Is.Not.Null); Assert.That(search, Is.Not.Null); Assert.That(suppress, Is.Not.Null);
            // Two fall on each side every tick, so each unit has a fallen teammate that is not itself.
            void Press()
            {
                p.Advance(new[] { Fall(w, 9), Fall(w, 10), Fall(w, Attackers + 1, shooterTeam: 1), Fall(w, Attackers + 2, shooterTeam: 1) });
                w.Step();
                Assert.That((Count(search), Count(suppress)), Is.EqualTo((0, 0)), "tick " + w.Sim.Clock.Tick);
            }
            for (int t = 0; t < 2 * Think; t++) Press();
            Assert.That(Count(memory), Is.GreaterThanOrEqualTo(Attackers), "every attacker saw an enemy and remembers it");
            for (ulong key = 1; key <= Attackers + Defenders; key++) Assert.That(p.Level(Id(w, key)), Is.EqualTo(AiNerve.Broken), "key " + key);
            for (ulong key = 1; key <= Attackers; key++)
            {
                MoveIntent order = w.Read<MoveIntent>(key);
                Assert.That(order.Active, Is.True, "key " + key);
                Assert.That(Horizontal2(order.Target, Own), Is.LessThanOrEqualTo(Metres2(6)), "key " + key);
            }
            // 40 m at 6 m/s is 200 ticks, plus the time twenty units need to sort out their slots.
            for (int t = 0; t < 400; t++) Press();
            var stood = new Dictionary<ulong, SimVec3>();
            for (ulong key = 1; key <= Attackers; key++)
            {
                Assert.That(Horizontal2(At(w, key), Own), Is.LessThanOrEqualTo(Metres2(7)), "key " + key);
                Assert.That(w.Read<MoveIntent>(key).Active, Is.False, "key " + key);
                stood[key] = At(w, key);
            }
            for (int t = 0; t < 2 * Think; t++) Press();
            for (ulong key = 1; key <= Attackers; key++) Assert.That(At(w, key), Is.EqualTo(stood[key]), "key " + key);
        }

        // A defender in its own point is already where a broken unit goes: it takes a slot there and does not leave.
        [Test]
        public void Broken_DefenderStaysInItsPoint()
        {
            M6World w = Field(Inject(1000, BoundAi, 100000), ownPoint: false, defenderSpeed: 6000);
            Pressure p = w.System<Pressure>();
            w.Step(Think);
            var stood = new Dictionary<ulong, SimVec3>();
            for (int t = 0; t < 150; t++)
            {
                p.Advance(new[] { Fall(w, Attackers + 1, shooterTeam: 1), Fall(w, Attackers + 6, shooterTeam: 1) });
                w.Step();
                for (ulong key = Attackers + 1; key <= Attackers + Defenders; key++)
                {
                    Assert.That(p.Level(Id(w, key)), Is.EqualTo(AiNerve.Broken), "key " + key);
                    Assert.That(Horizontal2(At(w, key), Theirs), Is.LessThanOrEqualTo(Metres2(6)), "key " + key + " tick " + t);
                    // The first 60 ticks may go into reaching a slot; after that nobody moves.
                    if (t == 60) stood[key] = At(w, key);
                    if (t > 60) Assert.That(At(w, key), Is.EqualTo(stood[key]), "key " + key + " tick " + t);
                }
            }
        }

        // ---- Battles on the control layout ----

        // "hold" never looks at the nerve: with the pressure system on, every unit is where it is without it, tick by tick.
        [Test]
        public void Battle_HoldWithPressureMovesNobodyElsewhere()
        {
            M6World on = Battle(41, null, Json(Values)), off = new M6World(FairEFixture.Recipe("control", 41, null), FairEFixture.Map("control"));
            Pressure p = on.System<Pressure>();
            Assert.That(p, Is.Not.Null);
            Assert.That(off.System<Pressure>(), Is.Null);
            int pressed = 0;
            for (int tick = 0; tick < 900; tick++)
            {
                on.Step(); off.Step();
                for (ulong key = 1; key <= 300; key++)
                {
                    Assert.That(on.Dead(key), Is.EqualTo(off.Dead(key)), "key " + key + " tick " + tick);
                    if (on.Has<Position>(key)) Assert.That(At(on, key), Is.EqualTo(At(off, key)), "key " + key + " tick " + tick);
                }
                pressed = Math.Max(pressed, Entries(p));
            }
            Assert.That(pressed, Is.GreaterThan(0), "the battle must have pressed somebody");
        }

        [Test]
        public void Battle_PressureChangesTheBoundBattle()
        {
            M6World on = Battle(41, null, S3), off = Battle(41, null, "{ " + BoundAi + " }");
            on.Step(1200); off.Step(1200);
            bool differs = false;
            for (ulong key = 1; key <= 300 && !differs; key++)
                differs = on.Dead(key) != off.Dead(key) || (on.Has<Position>(key) && !At(on, key).Equals(At(off, key)));
            Assert.That(differs, Is.True);
        }

        private const string Combo1 = "{ " + BoundAi + ", " + PressureAi + ", \"holdSharePermille\": 300, \"attackSpreadPoints\": 3 }";
        private const string Combo2 = "{ " + BoundAi + ", " + PressureAi + ", \"slotSpacingMm\": 2000, \"slotRingMm\": 6000, \"placement\": \"scatter\", \"scatterMm\": 6000 }";
        private static int BrokenUnits(M6World w, Pressure p)
        {
            int count = 0;
            for (ulong key = 1; key <= 300; key++) if (w.Has<Position>(key) && !w.Dead(key) && p.Level(Id(w, key)) == AiNerve.Broken) count++;
            return count;
        }

        // Same seed twice, tick by tick; a snapshot taken while units are broken restores to the same battle.
        [TestCase(null, S3)]
        [TestCase(false, S3)]
        [TestCase(true, S3)]
        [TestCase(null, Combo1)]
        [TestCase(true, Combo1)]
        [TestCase(null, Combo2)]
        [TestCase(true, Combo2)]
        public void Battle_RepeatAndRestoreWhileBroken(bool? fair, string ai)
        {
            M6World first = Battle(41, fair, ai), repeat = Battle(41, fair, ai);
            Pressure p = first.System<Pressure>();
            Assert.That(repeat.Hash, Is.EqualTo(first.Hash));
            int stepped = 0;
            while (BrokenUnits(first, p) < 2)
            {
                Assert.That(stepped, Is.LessThan(3000), "nobody broke");
                first.Step(); repeat.Step(); stepped++;
                Assert.That(repeat.Hash, Is.EqualTo(first.Hash), "tick " + first.Sim.Clock.Tick);
            }
            byte[] snapshot = first.Sim.SaveRetainedSnapshot();
            M6World restored = Battle(41, fair, ai);
            restored.Sim.RestoreRetainedSnapshot(snapshot);
            Assert.That(restored.Hash, Is.EqualTo(first.Hash));
            Assert.That(BrokenUnits(restored, restored.System<Pressure>()), Is.EqualTo(BrokenUnits(first, p)));
            for (int tick = 0; tick < 600 && !first.Sim.IsFinished; tick++)
            {
                first.Step(); restored.Step();
                Assert.That(restored.Hash, Is.EqualTo(first.Hash), "tick " + first.Sim.Clock.Tick);
            }
        }
    }
}
