using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml11aWorld;
using static WarCore.Tests.Ml11b1World;

namespace WarCore.Tests
{
    // ML-11b-1 acceptance 12 and 13: whole games. The search and the chase on the house are the same for the same seed and
    // resume from a snapshot; the assault demo map (PlateAssaultMap) is played under the soldier's AI rules as they are.
    public sealed class Ml11b1BattleTests
    {
        private const string Search = "{ \"targetMemoryTicks\": 300, \"searchTicks\": 40 }";
        private const string Chase = "{ \"meleeChaseRange\": 12 }";

        // The assault demo map baked for layer rwr-house (the layer of the legacy house stage): the m6 layer's units and
        // match with the soldier's AI rules. The world reads that one layer.
        internal static readonly MapBakeResult Baked = MapBake.Run(PlateAssaultMap.DescBytes(), Showcase.LegacyHouseLayer);

        internal static WorldRecipe Playtest() => MapRecipe.From(Baked, RwrPack.Load(Baked.PackId)).WithPlatesPreview();

        internal static M6World Assault() => new M6World(Playtest(), Baked.WcMapBytes);

        // A soldier on the ground that remembers an enemy on the floor (over a wall on the ground) and is sent to its point.
        private static M6World SearchGame()
        {
            M6World w = World(new[] { Gunner(A, 33500, 31500), Still(B, FarX, FarZ) }, Search, Guns(8), map: Pillar());
            Plant(w, A, B, Up(41500, 32500), 1);
            Send(w, A, 0);
            return w;
        }

        // A soldier with a blade on the ground and an enemy at the head of the stair.
        private static M6World ChaseGame() =>
            World(new[] { Blade(A, 33500, 31500), Still(B, 40500, 31500, 1) }, Chase,
                Guns(8, MeleeWorld.Spec(radius: 3, maxTargets: 1, engage: 1.8)));

        private static void SameAndResumes(System.Func<M6World> game, int end, params int[] saves)
        {
            M6World a = game(), b = game();
            var resumed = new List<M6World>();
            for (int t = 1; t <= end; t++)
            {
                a.Step(); b.Step();
                Assert.That(b.Hash, Is.EqualTo(a.Hash), "tick " + t);
                foreach (M6World c in resumed) { c.Step(); Assert.That(c.Hash, Is.EqualTo(a.Hash), "resumed, tick " + t); }
                if (!saves.Contains(t)) continue;
                M6World next = game();
                next.Sim.RestoreRetainedSnapshot(a.Sim.SaveRetainedSnapshot());
                Assert.That(next.Hash, Is.EqualTo(a.Hash), "restored at tick " + t);
                resumed.Add(next);
            }
            Assert.That(resumed.Count, Is.EqualTo(saves.Length));
        }

        // 12. The search: the same seed gives the same hash at every tick; a snapshot taken on the way up (tick 40) and one
        // taken while the soldier stands at the place (tick 100) both resume to the same hashes past the end of the search.
        [Test]
        public void TheSearchIsDeterministicAndResumes()
        {
            SameAndResumes(SearchGame, 160, 40, 100);
            M6World w = SearchGame();
            w.Step(40);
            Assert.That((Layer(w, A), GoesTo(w, A, Up(41500, 32500), 1)), Is.EqualTo((2, true)), "tick 40: on the stair, searching");
            w.Step(60);
            Assert.That((Layer(w, A), Ordered(w, A)), Is.EqualTo((1, false)), "tick 100: standing at the place");
            Assert.That(w.System<TargetMemory>().StateVersion, Is.EqualTo(2));
        }

        // 12. The chase: the same seed gives the same hash at every tick; a snapshot taken on the stair resumes.
        [Test]
        public void TheChaseIsDeterministicAndResumes()
        {
            SameAndResumes(ChaseGame, 120, 30, 50);
            M6World w = ChaseGame();
            w.Step(50);
            Assert.That((Layer(w, A), GoesTo(w, A, At(w, B), 1)), Is.EqualTo((2, true)), "tick 50: on the stair, chasing");
        }

        // 13. The playtest AI rules build on the assault map and every part they turn on is there.
        [Test]
        public void ThePlaytestRulesBuildOnTheAssaultMap()
        {
            WorldRecipe recipe = Playtest();
            Assert.That(recipe.Pack?.Id, Is.EqualTo(Showcase.LegacyHouseLayer), "the AI rules are read from the world's one pack");
            Assert.That(recipe.Pack.Terrain, Is.Null, "which has no terrain rules: the plates stand under the default rules");
            AiRules ai = recipe.Pack.Section<AiRules>();
            Assert.That((ai.FailedSlotTicks, ai.TargetMemoryTicks, ai.SearchTicks, ai.SuppressTicks, ai.MeleeChaseRange, ai.ReactionTicks, ai.SenseTicks,
                ai.BurstShots, ai.BurstPauseTicks), Is.EqualTo((90, 300, 90, 90, 12, 15, 6, 3, 30)));
            M6World w = Assault();
            Assert.That(w.System<TargetMemory>(), Is.Not.Null);
            Assert.That(w.System<TargetMemory>().StateVersion, Is.EqualTo(2));
            Assert.That(w.System<TargetSearch>(), Is.Not.Null);
            Assert.That(w.System<TargetSuppress>(), Is.Not.Null);
            Assert.That(w.System<FailedSlotMemory>(), Is.Not.Null);
            Assert.That(w.System<ReactionFire>(), Is.Not.Null);
            Assert.That(Part<SimFix>(w, "meleeChaseRange"), Is.EqualTo(SimFix.FromInt(12)));
        }

        // 13. The whole battle (seed 41): at every tick every living soldier stands on a walkable cell of its layer, the same
        // seed gives the same hashes, and the battle ends. The numbers are this package's baseline: the winner, the end tick,
        // the tick the point upstairs first changes hands, how many attackers have been on the first floor, and how many times
        // a walk failed (a soldier's order newly marked failed).
        [Test]
        public void ThePlaytestBattleKeepsEveryoneOnWalkableCells()
        {
            WcMap map = WcMapReader.Read(Baked.WcMapBytes);
            LayeredNav nav = WcMapLayeredNav.Build(map, null, map.ContentId);
            M6World w = Assault(), again = Assault();
            UnitSpawn[] units = w.Recipe.Units.ToArray();
            var upstairs = new HashSet<ulong>();
            var failing = new HashSet<ulong>();
            int t = 0, failed = 0, taken = -1;
            for (; t < 3000 && w.Match.Winner == 0; t++)
            {
                w.Step(); again.Step();
                Assert.That(again.Hash, Is.EqualTo(w.Hash), "tick " + t);
                if (taken < 0 && w.Capture.Points[PlateAssaultMap.UpstairsPoint].Owner == 1) taken = t + 1;
                foreach (UnitSpawn unit in units)
                {
                    if (w.Dead(unit.Key)) { failing.Remove(unit.Key); continue; }
                    if (!Failed(w, unit.Key)) failing.Remove(unit.Key);
                    else if (failing.Add(unit.Key)) failed++;
                    SimVec3 at = At(w, unit.Key);
                    int layer = Layer(w, unit.Key);
                    Assert.That(nav.TryNode(layer, (int)(at.X.Raw / M), (int)(at.Z.Raw / M), out int node) && nav.IsPassable(node), Is.True,
                        "unit " + unit.Key + " tick " + t + " layer " + layer);
                    if (unit.Team == 1 && layer == 2) upstairs.Add(unit.Key);
                }
            }
            // Team 2 has no reserves and is shot out in 196 ticks; the point upstairs is never taken (ML-11a's rules: team 2
            // wins at tick 521).
            Assert.That(w.Match.EndReason, Is.EqualTo(MatchEndReason.Exhausted));
            Assert.That((w.Match.Winner, t, taken, upstairs.Count, failed), Is.EqualTo((1, 196, -1, 1, 0)));
        }
    }
}
