using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // RC2-6a: which of its two questions the walking code asks the pass rule about the controlled unit. The controlled unit
    // walks by its input and never has a walk order, so before this card the code always asked the question for a standing
    // unit. Now a tick in which the input gives the controlled unit a step counts as walking, whether or not the step can be
    // taken; a tick of its dash and a tick in which it is held count as standing, as before.
    // The field: the player stands right under a wall (x 8..12, z 11..12); a team mate 0.6 m east of it is ordered west, so
    // its step comes within reach of the player and the rule is asked about the two. The stand-in rule writes down which
    // question it got and answers no. The two are units 1 and 2, and every test runs with the player as either: the walking
    // code steps the units in that order, so with the player as unit 2 the team mate asks before the player's own turn.
    public sealed class Rc26aWalkingLeaderTests
    {
        private sealed class Asked : ICrowdPass
        {
            internal readonly List<(bool Walking, EntityId Mover, EntityId Other)> Log = new List<(bool, EntityId, EntityId)>();
            public ulong Digest => 1;
            public bool MayPass(EntityId mover, EntityId idle) { Log.Add((false, mover, idle)); return false; }
            public bool MayPassMoving(EntityId mover, EntityId other) { Log.Add((true, mover, other)); return false; }
        }

        // A rule that tells the two questions apart in what happens next: a standing unit is passed, a walking one is not.
        private sealed class PassTheStanding : ICrowdPass
        {
            public ulong Digest => 2;
            public bool MayPass(EntityId mover, EntityId idle) => true;
            public bool MayPassMoving(EntityId mover, EntityId other) => false;
        }

        private sealed class Holding : IUnitHold
        {
            private readonly EntityId unit;
            internal Holding(EntityId unit) { this.unit = unit; }
            public bool Holds(EntityId other) => other.Equals(unit);
        }

        private static readonly InputFrame South = LedGame.Walk(0, -1000), North = LedGame.Walk(0, 1000);
        // A walk step of the player is 0.2 m; a step of its dash here is about 0.01 m (60 mm in 6 ticks).
        private static readonly long WalkStepRaw = SimFix.Ratio(200, 1000).Raw;

        private static M6World Field(ulong player, out InterimLocomotion walking, out Asked asked)
        {
            var units = new UnitSpawn[2];
            units[player - 1] = new UnitSpawn(player, 0, 1, 10000, 10750, 6000, 1).WithDash(60, 6, 90);
            units[2 - player] = new UnitSpawn(3 - player, 0, 1, 10600, 10750, 6000, 1);
            var w = new M6World(M6World.Endless(M6World.RecipeOf(units)).WithPlayer(player), WcMapWriter.Write(M3TestWorld.Boxes(80, 40, (8, 11, 4, 1))));
            walking = w.System<InterimLocomotion>();
            asked = new Asked();
            typeof(InterimLocomotion).GetField("pass", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(walking, asked);
            walking.Order(w.Unit(3 - player), new SimVec3(SimFix.Ratio(8500, 1000), default, SimFix.Ratio(10500, 1000)));
            return w;
        }

        // One tick with that input. Returns how far south the player went (raw) and, through questions, what the team mate's
        // step asked about the player: true for the question about a walking unit, false for the one about a standing unit.
        private static long Tick(M6World w, ulong player, Asked asked, InputFrame input, out List<bool> questions)
        {
            long before = w.Read<Position>(player).Value.Z.Raw, x = w.Read<Position>(player).Value.X.Raw;
            asked.Log.Clear();
            w.Sim.Step(input);
            questions = new List<bool>();
            foreach ((bool Walking, EntityId Mover, EntityId Other) q in asked.Log)
                if (q.Mover.Equals(w.Unit(3 - player)) && q.Other.Equals(w.Unit(player))) questions.Add(q.Walking);
            Assert.That(questions.Count, Is.GreaterThan(0), "the team mate's step asked about the player");
            Assert.That(w.Read<Position>(player).Value.X.Raw, Is.EqualTo(x));
            return before - w.Read<Position>(player).Value.Z.Raw;
        }

        [TestCase(1UL)]
        [TestCase(2UL)]
        public void WithoutInputTheControlledUnitStands(ulong player)
        {
            M6World w = Field(player, out _, out Asked asked);
            Assert.That(Tick(w, player, asked, default, out List<bool> questions), Is.EqualTo(0L));
            Assert.That(questions, Is.All.EqualTo(false));
        }

        [TestCase(1UL)]
        [TestCase(2UL)]
        public void SteppingTheControlledUnitWalks(ulong player)
        {
            M6World w = Field(player, out _, out Asked asked);
            Assert.That(Tick(w, player, asked, South, out List<bool> questions), Is.EqualTo(WalkStepRaw));
            Assert.That(questions, Is.All.EqualTo(true));
            Assert.That(Tick(w, player, asked, default, out questions), Is.EqualTo(0L), "and stands again on the next tick without input");
            Assert.That(questions, Is.All.EqualTo(false));
        }

        [TestCase(1UL)]
        [TestCase(2UL)]
        public void PushingAgainstAWallTheControlledUnitWalks(ulong player)
        {
            M6World w = Field(player, out _, out Asked asked);
            Assert.That(Tick(w, player, asked, North, out List<bool> questions), Is.EqualTo(0L), "the wall stops it");
            Assert.That(questions, Is.All.EqualTo(true));
        }

        [TestCase(1UL)]
        [TestCase(2UL)]
        public void OnATickOfItsDashTheControlledUnitStands(ulong player)
        {
            M6World w = Field(player, out InterimLocomotion walking, out Asked asked);
            Assert.That(walking.TryDash(w.Unit(player), SimVec3.Metres(0, 0, -1)), Is.True);
            long south = Tick(w, player, asked, South, out List<bool> questions);
            Assert.That(south, Is.GreaterThan(0L), "the dash moved it");
            Assert.That(south * 10, Is.LessThan(WalkStepRaw), "by the dash's step, not by the input's");
            Assert.That(questions, Is.All.EqualTo(false));
        }

        // The mark is made anew on every tick from that tick's input and is not saved: a world saved on a tick in which the
        // player walks (it pushes against the wall, so the team mate has to go round it) and loaded into another world plays
        // on tick for tick as the world that never saved, through the tick on which the player stands again.
        [TestCase(1UL)]
        [TestCase(2UL)]
        public void SavedWhileTheControlledUnitWalksAndLoadedTheWorldPlaysOnTheSame(ulong player)
        {
            M6World Made()
            {
                M6World w = Field(player, out InterimLocomotion walking, out _);
                typeof(InterimLocomotion).GetField("pass", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(walking, new PassTheStanding());
                return w;
            }
            M6World whole = Made(), early = Made(), resumed = Made();
            ulong mate = 3 - player;
            for (int t = 0; t < 5; t++) { whole.Sim.Step(North); early.Sim.Step(North); resumed.Sim.Step(default); }
            Assert.That(resumed.Read<Position>(mate).Value.Z.Raw, Is.Not.EqualTo(whole.Read<Position>(mate).Value.Z.Raw),
                "past the walking player the team mate goes another way than through the standing one");
            Assert.That(resumed.Hash, Is.Not.EqualTo(whole.Hash));
            resumed.Sim.RestoreRetainedSnapshot(early.Sim.SaveRetainedSnapshot());
            Assert.That(resumed.Hash, Is.EqualTo(whole.Hash), "the loaded world is the saved one");
            for (int t = 0; t < 40; t++)
            {
                // Three more ticks of pushing, then the player stands.
                InputFrame input = t < 3 ? North : default;
                whole.Sim.Step(input); resumed.Sim.Step(input);
                Assert.That(resumed.Hash, Is.EqualTo(whole.Hash), "step " + t);
            }
            Assert.That(whole.Read<Position>(mate).Value.X.Raw, Is.LessThan(whole.Read<Position>(player).Value.X.Raw), "the team mate went on");
        }

        [TestCase(1UL)]
        [TestCase(2UL)]
        public void HeldTheControlledUnitStands(ulong player)
        {
            M6World w = Field(player, out InterimLocomotion walking, out Asked asked);
            walking.BindHold(new Holding(w.Unit(player)));
            Assert.That(Tick(w, player, asked, South, out List<bool> questions), Is.EqualTo(0L));
            Assert.That(questions, Is.All.EqualTo(false));
        }
    }
}
