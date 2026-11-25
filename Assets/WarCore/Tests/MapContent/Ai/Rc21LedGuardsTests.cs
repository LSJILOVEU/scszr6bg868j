using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.LedGame;

namespace WarCore.Tests
{
    // RC2-1, what the led squad must leave alone: a stage where no led order is given, the units of the old standing
    // orders, the walk order that was refused, and the heap.
    public sealed class Rc21LedGuardsTests
    {
        // The stage's content with the led squad's two numbers taken out of whichever file writes them.
        private sealed class WithoutLed : IContentSource
        {
            private readonly IContentSource files;
            public WithoutLed(IContentSource files) { this.files = files; }
            public bool TryRead(string layerId, out string text, out string location)
            {
                if (!files.TryRead(layerId, out text, out location)) return false;
                text = Regex.Replace(text, "\"ledSize\"\\s*:\\s*\\d+\\s*,\\s*\"ledRecruitRadiusMm\"\\s*:\\s*\\d+", "");
                return true;
            }
        }

        private static string Line(ViewEntity e) => string.Format(CultureInfo.InvariantCulture, "{0} team {1} alive {2} at {3:R} {4:R} {5:R} layer {6} band {7} stance {8}",
            e.Key, e.Team, e.Alive, e.X, e.Y, e.Z, e.Layer, e.Band, e.Stance);

        // Acceptance 2. The stage under its own layer and under the same layer without the two numbers, both played by the
        // stage's own script and commands: every unit is in the same place and state at every fiftieth tick.
        [TestCase("battle")]
        [TestCase("bullets")]
        [TestCase("house")]
        [TestCase("terrain")]
        [TestCase("melee")]
        [TestCase("hamlet")]
        public void AStageWhereNoLedOrderIsGivenPlaysAsItDoesWithoutTheRules(string id)
        {
            ShowcaseStage stage = Stage(id);
            ContentCatalog catalog = ContentCatalog.Find();
            ContentPack plain = ContentLoader.Load(new WithoutLed(new FileContentSource(catalog.DirectoryOf(catalog.CartridgeOf(stage.Layer)))), stage.Layer);
            Assert.That(stage.Recipe().Pack.Section<AiRules>().Led.HasValue, Is.True);
            Assert.That(plain.Section<AiRules>().Led.HasValue, Is.False);
            // The stage's own way to a world (ShowcaseStage.World), under the plain pack.
            WorldRecipe recipe = MapRecipe.From(MapBake.Run(File.ReadAllBytes(Showcase.ProjectFile(stage.Map)), stage.Layer), plain, useUnitTypes: true);
            SimSession with = stage.Start(), without = SimSession.Create(recipe.WithPlayer(Showcase.FirstDeployed(recipe.Units)), stage.MapBytes());
            Assert.That(with.LedOrders, Is.True);
            Assert.That(without.LedOrders, Is.False);
            foreach (SimSession s in new[] { with, without })
            {
                foreach ((long AtTick, PlayerInput Input) step in stage.Script()) s.SetPlayerInput(step.AtTick, step.Input);
                foreach (BridgeCommand command in stage.Commands()) s.Enqueue(command);
            }
            for (int t = 1; t <= 600; t++)
            {
                with.StepOnce();
                without.StepOnce();
                if (t % 50 != 0) continue;
                ViewEntity[] a = with.Current.Entities.ToArray(), b = without.Current.Entities.ToArray();
                Assert.That(a.Length, Is.EqualTo(b.Length));
                for (int i = 0; i < a.Length; i++) Assert.That(Line(a[i]), Is.EqualTo(Line(b[i])), "tick " + t);
            }
            Assert.That(with.LedCount, Is.EqualTo(0));
            Assert.That(with.IsFinished, Is.EqualTo(without.IsFinished));
        }

        // The battle stage with a stand-in between the squad and the world's move orders that listens to one soldier: the
        // first member who has to walk more than a metre to its place when the squad is called (a twin world tells which).
        private static LedGame Listening(out LedOrders orders)
        {
            var twin = new LedGame("battle");
            twin.Order(SquadCommandKind.Follow);
            ulong walker = 0;
            foreach (ulong key in twin.Members())
                if (walker == 0 && twin.Led.TryPlace(twin.Id(key), out SimVec3 place) && Metres(twin.At(key), place) > 1) walker = key;
            Assert.That(walker, Is.Not.EqualTo(0UL), "somebody has to walk to its place");
            var g = new LedGame("battle");
            orders = new LedOrders(g.Moves) { Unit = g.Id(walker).Value };
            g.Moves = orders;
            return g;
        }

        // Acceptance 7, second half. One member's walk orders are refused: it is asked once, then left alone until the anchor
        // moves and the places are worked out again. Saving and loading does not make the squad ask again.
        [Test]
        public void AWalkOrderThatWasRefusedIsNotGivenAgainUntilThePlacesChange()
        {
            LedGame g = Listening(out LedOrders orders);
            orders.Refuse = true;
            var walker = new StableId(orders.Unit);
            g.Order(SquadCommandKind.Follow);
            Assert.That(g.Led.IsMember(walker), Is.True);
            Assert.That(orders.Orders, Is.EqualTo(1), "asked once");
            SimVec3 anchor = g.Led.Anchor;
            g.Wait(30);
            g.W.Sim.RestoreRetainedSnapshot(g.W.Sim.SaveRetainedSnapshot());
            g.Wait(30);
            Assert.That(orders.Orders, Is.EqualTo(1), "and not again while the places stay, loaded or not");
            Assert.That((g.Led.Anchor.X.Raw, g.Led.Anchor.Z.Raw), Is.EqualTo((anchor.X.Raw, anchor.Z.Raw)));
            // The leader walks: the member is asked again only on a tick the anchor moved (the places were worked out again),
            // and then once.
            int moved = 0;
            for (int t = 0; t < 40; t++)
            {
                SimVec3 before = g.Led.Anchor;
                int asked = orders.Orders;
                g.Step(Walk(1000, 0));
                bool shifted = g.Led.Anchor.X.Raw != before.X.Raw || g.Led.Anchor.Z.Raw != before.Z.Raw;
                if (shifted) moved++;
                Assert.That(orders.Orders - asked, Is.InRange(0, shifted ? 1 : 0), "step " + t);
            }
            Assert.That(moved, Is.GreaterThan(1), "the anchor moved more than once");
            Assert.That(orders.Orders, Is.InRange(3, 1 + moved), "asked again when the places changed");
            // The leader stands: the last order was refused, so the member is still left alone.
            int stood = orders.Orders;
            ComponentType<Position> positions = g.W.Sim.Entities.Resolve<Position>();
            var entity = (EntityId)g.W.Sim.Entities.Resolve(new EntityLink(walker));
            SimVec3 from = g.W.Sim.Entities.Read(positions, entity).Value;
            g.Wait(30);
            Assert.That(orders.Orders, Is.EqualTo(stood));
            Assert.That(Metres(g.W.Sim.Entities.Read(positions, entity).Value, from), Is.LessThan(0.01), "it has not moved");
            // Orders are taken again: with the next change of the places the member gets one and walks.
            orders.Refuse = false;
            g.Step(Walk(1000, 0), 30);
            Assert.That(orders.Orders, Is.GreaterThan(stood));
            Assert.That(Metres(g.W.Sim.Entities.Read(positions, entity).Value, from), Is.GreaterThan(1), "it walks");
        }

        // A member on its way is told once where to go, not on every tick, and a new place means a new order at once.
        [Test]
        public void AMemberIsToldOnceWhereToGo()
        {
            LedGame g = Listening(out LedOrders orders);
            g.Order(SquadCommandKind.Follow);
            Assert.That(orders.Orders, Is.EqualTo(1));
            g.Wait(3);
            Assert.That(g.W.Sim.Entities.Read(g.W.Sim.Entities.Resolve<MoveIntent>(), (EntityId)g.W.Sim.Entities.Resolve(new EntityLink(new StableId(orders.Unit)))).Active, Is.True, "it is on its way");
            Assert.That(orders.Orders, Is.EqualTo(1), "one order for one place");
            SimVec3 here = g.At(g.Player);
            g.Order(SquadCommandKind.MoveTo, new SimVec3(here.X + SimFix.FromInt(20), here.Y, here.Z));
            Assert.That(orders.Orders, Is.EqualTo(2), "the place changed: told at once");
            g.Wait(5);
            Assert.That(orders.Orders, Is.EqualTo(2));
            // The first point lay due east of the old places, the next lies due south of the new ones.
            SimVec3 east = g.Led.Anchor;
            g.Order(SquadCommandKind.MoveTo, new SimVec3(east.X, east.Y, east.Z - SimFix.FromInt(10)));
            Assert.That((g.Led.Anchor.X.Raw, g.Led.Anchor.Z.Raw), Is.EqualTo((east.X.Raw, east.Z.Raw - SimFix.FromInt(10).Raw)));
            Assert.That(orders.Orders, Is.EqualTo(3));
            g.Wait(5);
            Assert.That(orders.Orders, Is.EqualTo(3));
            // Dismissed on its way, the member is stopped.
            int stops = orders.Stops;
            g.Order(SquadCommandKind.Dismiss);
            Assert.That(orders.Stops, Is.EqualTo(stops + 1));
        }

        // The old standing orders (ai.standingOrders) find their units by the static squad number. No unit carries the led
        // squad's number, so a led command changes nothing in a world that has only the old orders.
        [Test]
        public void ALedCommandMovesNoUnitOfTheOldStandingOrders()
        {
            Assert.That(BridgeCommand.Led, Is.EqualTo(SquadCommand.Led), "the bridge hands squad numbers on as they are");
            Assert.That(() => new UnitSpawn(1, 0, BridgeCommand.Led, 0, 0, 3000), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new UnitSpawn(1, 0, 0, 0, 0, 3000), Throws.Nothing, "squad 0 is a squad number");
            Assert.That(new UnitSpawn(1, 0, 0, 0, 0, 3000).Squad, Is.EqualTo(0));
            var units = new[] { M6World.U(1, 1, 10, 10, speed: 6000), M6World.U(2, 1, 12, 10, speed: 6000), M6World.U(3, 2, 70, 35, speed: 6000) };
            WorldRecipe old = M6World.RecipeOf(units, M6World.Pack()).WithStandingOrders();
            M6World a = new M6World(old), b = new M6World(old);
            Assert.That(a.System<LedSquad>(), Is.Null);
            foreach (M6World w in new[] { a, b }) w.Session.Enqueue(BridgeCommand.MoveTo(1, 1, 40000, 10000));
            a.Session.Enqueue(BridgeCommand.Hold(5, BridgeCommand.Led));
            a.Session.Enqueue(BridgeCommand.MoveTo(6, BridgeCommand.Led, 20000, 30000));
            a.Session.Enqueue(BridgeCommand.Follow(7, BridgeCommand.Led, 0));
            a.Session.Enqueue(BridgeCommand.Dismiss(8));
            for (int t = 0; t < 60; t++)
            {
                a.Session.StepOnce();
                b.Session.StepOnce();
                long tick = a.Session.Tick;
                // The hash of a tick holds that tick's input, so on the four ticks of the led commands the units are compared.
                if (tick < 5 || tick > 8) Assert.That(a.Session.StateHash, Is.EqualTo(b.Session.StateHash), "tick " + tick);
                else
                    foreach (ulong key in new ulong[] { 1, 2, 3 })
                    {
                        SimVec3 here = a.Read<Position>(key).Value, there = b.Read<Position>(key).Value;
                        Assert.That(here.X.Raw, Is.EqualTo(there.X.Raw), "tick " + tick + " unit " + key);
                        Assert.That(here.Z.Raw, Is.EqualTo(there.Z.Raw), "tick " + tick + " unit " + key);
                        Assert.That(a.Read<MoveIntent>(key).Active, Is.EqualTo(b.Read<MoveIntent>(key).Active), "tick " + tick + " unit " + key);
                        Assert.That(a.Has<SquadStanding>(key), Is.EqualTo(b.Has<SquadStanding>(key)), "tick " + tick + " unit " + key);
                        if (a.Has<SquadStanding>(key))
                            Assert.That(a.Read<SquadStanding>(key).Kind, Is.EqualTo(b.Read<SquadStanding>(key).Kind), "tick " + tick + " unit " + key);
                    }
            }
            Assert.That(a.Session.CommandLog.Count, Is.EqualTo(5));
            Assert.That(a.Read<MoveIntent>(1).Active, Is.True, "squad 1 is still on its way");
        }

#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
        // Acceptance 10. A leader walks with its squad, tells it to hold, sends it to a point, calls it back and dismisses
        // it: no step allocates. The first run warms the code up; the second is measured, from tick 22 on (the stage itself
        // allocates once, 72 bytes at tick 15, with or without a squad).
        [Test]
        public void LeadingASquadAllocatesNothing()
        {
            ShowcaseStage stage = Stage("battle");
            long total = 0;
            int led = 0;
            for (int run = 0; run < 2; run++)
            {
                var w = new M6World(stage.Recipe(), stage.MapBytes());
                LedSquad squad = w.System<LedSquad>();
                SimVec3 start = w.Read<Position>(w.Session.ControlledKey.Value).Value;
                InputFrame there = Command(SquadCommandKind.MoveTo, new SimVec3(start.X + SimFix.FromInt(12), start.Y, start.Z));
                w.Sim.Step(Command(SquadCommandKind.Follow));
                for (int i = 0; i < 20; i++) w.Sim.Step(Walk(1000, 0));
                AllocationMeter.Stabilize();
                for (int i = 0; i < 90; i++)
                {
                    InputFrame frame = i == 20 ? Command(SquadCommandKind.Hold) : i == 35 ? there : i == 50 ? Command(SquadCommandKind.Follow)
                        : i == 80 ? Command(SquadCommandKind.Dismiss) : i < 70 ? Walk(1000, 0) : default;
                    if (i == 79) led = squad.MemberCount;
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    w.Sim.Step(frame);
                    long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                    if (run == 1) total += bytes;
                }
                Assert.That(squad.MemberCount, Is.EqualTo(0), "dismissed");
            }
            Assert.That(led, Is.EqualTo(8), "the squad was full while it was measured");
            Assert.That(total, Is.EqualTo(0L));
            long mark = GC.GetAllocatedBytesForCurrentThread();
            var control = new byte[64];
            long positive = GC.GetAllocatedBytesForCurrentThread() - mark;
            GC.KeepAlive(control);
            Assert.That(positive, Is.GreaterThanOrEqualTo(64), "the counter counts");
        }
#endif
    }
}
