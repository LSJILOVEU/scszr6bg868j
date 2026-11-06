using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Wp16TestWorld;

namespace WarCore.Tests
{
    // C11: hold, follow and move-to are carried out after the one-tick command is gone, and the match AI leaves those units alone.
    public sealed class SquadStandingTests
    {
        // Squad 1 (keys 1..4) stands at x 10..13 m, squad 2 (key 5, the leader) at x 14 m, all z 10 m. One enemy far away.
        private static M6World World(bool standing = true, int think = 1000000, params BridgeCommand[] script)
        {
            var units = new List<UnitSpawn>();
            for (ulong key = 1; key <= 4; key++) units.Add(U(key, 9000 + (long)key * 1000, 10000, squad: 1));
            units.Add(U(5, 14000, 10000, squad: 2));
            units.Add(U(6, 70000, 35000, team: 2, squad: 3));
            WorldRecipe recipe = M6World.RecipeOf(units.ToArray(), Match(think: think));
            if (standing) recipe = recipe.WithStandingOrders();
            var w = new M6World(recipe);
            foreach (BridgeCommand c in script) w.Session.Enqueue(c);
            return w;
        }

        private static void Run(M6World w, int ticks) { for (int i = 0; i < ticks; i++) w.Session.StepOnce(); }

        private static bool TryStanding(M6World w, ulong key, out SquadStanding value) =>
            w.Sim.Entities.TryGet(w.Sim.Entities.Resolve<SquadStanding>(), w.Unit(key), out value);

        private static bool Moved(M6World w, ulong key, SimVec3 from) => !Point(w, key).Equals(from);

        [Test]
        public void Hold_StopsTheSquadWhereItStands_AndKeepsItThere()
        {
            var w = World(true, 1000000, BridgeCommand.MoveTo(1, 1, 40000, 10000), BridgeCommand.Hold(20, 1));
            Run(w, 19);
            Assert.That(w.Read<MoveIntent>(1).Active, Is.True, "the squad is under way");
            Run(w, 2);
            SimVec3[] at = { Point(w, 1), Point(w, 2), Point(w, 3), Point(w, 4) };
            for (ulong key = 1; key <= 4; key++)
            {
                Assert.That(w.Read<MoveIntent>(key).Active, Is.False, "unit " + key);
                Assert.That(TryStanding(w, key, out SquadStanding s) && s.Kind == StandingKind.Hold, Is.True, "unit " + key);
            }
            Run(w, 200);
            for (ulong key = 1; key <= 4; key++) Assert.That(Point(w, key), Is.EqualTo(at[key - 1]), "unit " + key);
            Assert.That(TryStanding(w, 5, out _), Is.False, "another squad is untouched");
        }

        [Test]
        public void WithoutTheRecipeFlag_HoldAndFollowChangeNothing()
        {
            var plain = World(false, 1000000, BridgeCommand.MoveTo(1, 1, 40000, 10000), BridgeCommand.Hold(20, 1), BridgeCommand.Follow(21, 2, 1));
            var control = World(false, 1000000, BridgeCommand.MoveTo(1, 1, 40000, 10000));
            for (int i = 0; i < 120; i++)
            {
                plain.Session.StepOnce(); control.Session.StepOnce();
                for (ulong key = 1; key <= 5; key++) Assert.That(Point(plain, key), Is.EqualTo(Point(control, key)), "tick " + i + " unit " + key);
            }
            Assert.That(Moved(plain, 1, SimVec3.Metres(10, 0, 10)), Is.True);
        }

        [Test]
        public void Follow_KeepsTheSquadWithinStopDistanceOfTheMovingTarget()
        {
            var w = World(true, 1000000, BridgeCommand.MoveTo(1, 2, 50000, 10000), BridgeCommand.Follow(2, 1, 5));
            Run(w, 60);
            SimVec3 aim = w.Read<MoveIntent>(1).Target - Point(w, 5);
            Assert.That(new SimVec3(aim.X, default, aim.Z).LengthSquared <= new SimVec3(SimFix.FromInt(2), default, default).LengthSquared, Is.True,
                "while the leader walks, the followers' order tracks it");
            Assert.That(TryStanding(w, 1, out SquadStanding s), Is.True);
            Assert.That((s.Kind, s.Target), Is.EqualTo((StandingKind.Follow, w.Sim.Entities.StableOf(w.Unit(5)).Value)));
            Assert.That(TryStanding(w, 5, out _), Is.True, "the leader carries its own move, not the follow");
            Assert.That(TryStanding(w, 5, out SquadStanding leader) && leader.Kind == StandingKind.Move, Is.True);
            Run(w, 800);
            Assert.That(w.Read<MoveIntent>(5).Active, Is.False, "the leader has arrived");
            Assert.That(Point(w, 5).X.Raw, Is.EqualTo(SimFix.FromInt(50).Raw).Within(SimFix.Scale));
            for (ulong key = 1; key <= 4; key++)
            {
                SimVec3 d = Point(w, key) - Point(w, 5);
                SimWide gap = new SimVec3(d.X, default, d.Z).LengthSquared;
                Assert.That(gap <= new SimVec3(SimFix.Ratio(31, 10), default, default).LengthSquared, Is.True, "unit " + key + " is close to the leader");
                Assert.That(gap >= new SimVec3(SimFix.Ratio(5, 2), default, default).LengthSquared, Is.True, "unit " + key + " stopped at the stop distance, not on the leader");
                Assert.That(Point(w, key).X.Raw, Is.GreaterThan(SimFix.FromInt(40).Raw), "unit " + key + " came along");
                Assert.That(w.Read<MoveIntent>(key).Active, Is.False, "unit " + key + " has stopped near the leader");
            }
        }

        [Test]
        public void Follow_TheTargetItselfIsNeverMarked_AndAnUnknownOrDeadTargetIsIgnored()
        {
            var w = World(true, 1000000, BridgeCommand.Follow(2, 1, 1));
            Run(w, 5);
            Assert.That(TryStanding(w, 1, out _), Is.False, "a unit does not follow itself");
            for (ulong key = 2; key <= 4; key++) Assert.That(TryStanding(w, key, out SquadStanding s) && s.Kind == StandingKind.Follow, Is.True, "unit " + key);

            var dead = World(true, 1000000, BridgeCommand.Follow(30, 1, 5));
            KillAt(dead, 10, 5);
            Run(dead, 40);
            Assert.That(TryStanding(dead, 1, out _), Is.False, "nothing to follow: the order is ignored");
        }

        [Test]
        public void Follow_EndsWhenTheTargetDies()
        {
            var w = World(true, 1000000, BridgeCommand.MoveTo(1, 2, 50000, 10000), BridgeCommand.Follow(2, 1, 5));
            KillAt(w, 100, 5);
            Run(w, 90);
            Assert.That(TryStanding(w, 1, out _), Is.True);
            Run(w, 30);
            for (ulong key = 1; key <= 4; key++)
            {
                Assert.That(TryStanding(w, key, out _), Is.False, "unit " + key + " is free again");
                Assert.That(w.Read<MoveIntent>(key).Active, Is.False, "unit " + key + " stopped");
            }
        }

        [Test]
        public void MoveTo_StandsUntilArrival_ThenTheOrderIsReleased()
        {
            var w = World(true, 1000000, BridgeCommand.MoveTo(1, 1, 30000, 10000));
            Run(w, 10);
            for (ulong key = 1; key <= 4; key++) Assert.That(TryStanding(w, key, out SquadStanding s) && s.Kind == StandingKind.Move, Is.True, "unit " + key);
            Run(w, 800);
            for (ulong key = 1; key <= 4; key++) Assert.That(TryStanding(w, key, out _), Is.False, "unit " + key + " arrived");
        }

        [Test]
        public void ALaterCommandReplacesTheStandingOrder()
        {
            var w = World(true, 1000000, BridgeCommand.Hold(1, 1), BridgeCommand.Follow(5, 1, 5), BridgeCommand.MoveTo(9, 1, 40000, 25000), BridgeCommand.Hold(14, 1));
            Run(w, 3); Assert.That(TryStanding(w, 2, out SquadStanding a) && a.Kind == StandingKind.Hold, Is.True);
            Run(w, 3); Assert.That(TryStanding(w, 2, out SquadStanding b) && b.Kind == StandingKind.Follow, Is.True);
            Run(w, 4); Assert.That(TryStanding(w, 2, out SquadStanding c) && c.Kind == StandingKind.Move, Is.True);
            Run(w, 5); Assert.That(TryStanding(w, 2, out SquadStanding d) && d.Kind == StandingKind.Hold, Is.True);
        }

        [Test]
        public void TheAiLeavesACommandedUnitAlone_AndResumesAfterTheOrderEnds()
        {
            // think = 1: the AI looks at every unit every tick and sends idle team-1 units toward the nearest unowned point.
            var free = World(true, 1, BridgeCommand.Hold(100, 2));
            Run(free, 40);
            Assert.That(free.Read<MoveIntent>(5).Active, Is.True, "an uncommanded unit is steered by the AI");
            var held = World(true, 1, BridgeCommand.Hold(1, 2));
            SimVec3 at = Point(held, 5);
            Run(held, 40);
            Assert.That(Point(held, 5), Is.EqualTo(at), "a held unit is not");
            Assert.That(held.Read<MoveIntent>(5).Active, Is.False);

            var moved = World(true, 1, BridgeCommand.MoveTo(1, 2, 14000, 14000));
            int guard = 0;
            while (TryStanding(moved, 5, out _) && guard++ < 400) moved.Session.StepOnce();
            Assert.That(TryStanding(moved, 5, out _), Is.False, "the move is over");
            SimVec3 after = Point(moved, 5);
            Run(moved, 40);
            Assert.That(Moved(moved, 5, after), Is.True, "the AI has taken the unit back");
        }

        [Test]
        public void ThePlayerUnitIsNeverGivenAStandingOrder()
        {
            var units = new List<UnitSpawn> { U(1, 10000, 10000, squad: 1), U(2, 11000, 10000, squad: 1), U(6, 70000, 35000, team: 2, squad: 3) };
            var w = new M6World(M6World.RecipeOf(units.ToArray(), Match()).WithStandingOrders().WithPlayer(1));
            w.Session.Enqueue(BridgeCommand.Hold(1, 1));
            Run(w, 3);
            Assert.That(TryStanding(w, 1, out _), Is.False);
            Assert.That(TryStanding(w, 2, out SquadStanding s) && s.Kind == StandingKind.Hold, Is.True);
        }

        [Test]
        public void SameScript_SameHashes_AndASnapshotMidFollowResumesIdentically()
        {
            BridgeCommand[] script = { BridgeCommand.MoveTo(1, 2, 50000, 10000), BridgeCommand.Follow(2, 1, 5), BridgeCommand.Hold(150, 2) };
            var a = World(true, 1000000, script); var whole = World(true, 1000000, script);
            for (int i = 0; i < 70; i++) { a.Session.StepOnce(); whole.Session.StepOnce(); Assert.That(a.Session.StateHash, Is.EqualTo(whole.Session.StateHash), "tick " + i); }
            var b = World(true, 1000000, script[2]); // only the command still to come
            b.Sim.RestoreRetainedSnapshot(a.Sim.SaveRetainedSnapshot());
            Assert.That(b.Session.StateHash, Is.EqualTo(whole.Session.StateHash));
            for (int i = 0; i < 250; i++)
            {
                whole.Session.StepOnce(); b.Session.StepOnce();
                Assert.That(b.Session.StateHash, Is.EqualTo(whole.Session.StateHash), "step " + (71 + i));
            }
            Assert.That(TryStanding(b, 5, out SquadStanding s) && s.Kind == StandingKind.Hold, Is.True);
        }

        // The other movers go through the same orders system; the world has no match, so nothing but the commands steers.
        [Test]
        public void EveryMover_CarriesOutHoldFollowAndMove([Values(MoverKind.Interim, MoverKind.InterimPath, MoverKind.InterimCrowd)] MoverKind mover)
        {
            var units = new[] { Wp23.Unit(1, 1, 10500, 12500, 4000), Wp23.Unit(2, 2, 12500, 12500, 4000) };
            var script = new[] { BridgeCommand.MoveTo(1, 1, 18500, 12500), BridgeCommand.Hold(40, 1), BridgeCommand.Follow(60, 1, 2), BridgeCommand.MoveTo(61, 2, 30500, 12500) };
            SimSession s = SimSession.Create(new WorldRecipe(41, mover, units, script).WithStandingOrders(), WcMapWriter.Write(Wp23.Make(40, 24, 0, 6 * Wp23.S, null, 4 * Wp23.S)));
            ComponentType<SquadStanding> type = M3TestWorld.Sim(s).Entities.Resolve<SquadStanding>();
            bool Has(ulong key, out SquadStanding v) => M3TestWorld.Sim(s).Entities.TryGet(type, M3TestWorld.Unit(s, key), out v);
            for (int i = 0; i < 10; i++) s.StepOnce();
            Assert.That(Has(1, out SquadStanding move) && move.Kind == StandingKind.Move, Is.True);
            for (int i = 0; i < 31; i++) s.StepOnce();
            SimVec3 held = Wp23.At(s, 1);
            Assert.That(Has(1, out SquadStanding hold) && hold.Kind == StandingKind.Hold, Is.True);
            Assert.That(M3TestWorld.Component<MoveIntent>(s, 1).Active, Is.False);
            for (int i = 0; i < 18; i++) s.StepOnce();
            Assert.That(Wp23.At(s, 1), Is.EqualTo(held));
            for (int i = 0; i < 400; i++) s.StepOnce();
            Assert.That(Has(1, out SquadStanding follow) && follow.Kind == StandingKind.Follow, Is.True);
            Assert.That(Wp23.At(s, 1).X.Raw, Is.GreaterThan(SimFix.FromInt(26).Raw), "unit 1 followed unit 2 to its destination");
            Assert.That(Has(2, out _), Is.False, "the leader arrived");
        }

        [Test]
        public void TheComponentSerializesBothWays()
        {
            var write = new SimStateWriter();
            new SquadStandingSerializer().Write(write, new SquadStanding(StandingKind.Follow, 0x1234567890ABCDEF));
            var read = new SquadStandingSerializer().Read(new SimStateReader(write.ToArray()));
            Assert.That((read.Kind, read.Target), Is.EqualTo((StandingKind.Follow, 0x1234567890ABCDEFUL)));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new SquadStanding((StandingKind)3));
            var h1 = new StateHash(); new SquadStanding(StandingKind.Hold).WriteState(h1);
            var h2 = new StateHash(); new SquadStanding(StandingKind.Follow).WriteState(h2);
            var h3 = new StateHash(); new SquadStanding(StandingKind.Follow, 7).WriteState(h3);
            Assert.That(h1.Value, Is.Not.EqualTo(h2.Value)); Assert.That(h2.Value, Is.Not.EqualTo(h3.Value));
        }
    }
}
