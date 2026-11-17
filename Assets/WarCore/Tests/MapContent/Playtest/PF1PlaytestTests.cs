using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // PF1: the playtest recipe is the MP respawn demo plus the AI rules of content layer rwr-m7-playtest.
    public sealed class PF1PlaytestTests
    {
        private const int Ticks = 600, Restore = 300;

        private static M6World World(WorldRecipe recipe) => new M6World(recipe, DemoRecipes.M7MapBytes());

        [Test]
        public void PF1Recipe_AiPackCarriesTheTenLayerValues()
        {
            Assert.That(DemoRecipes.MpRespawnDemo().AiPack, Is.Null);
            WorldRecipe recipe = DemoRecipes.MpPlaytest();
            Assert.That(recipe.AiPack, Is.Not.Null);
            AiRules r = recipe.AiPack.Section<AiRules>();
            Assert.That(r.FailedSlotTicks, Is.EqualTo(90));
            Assert.That(r.TargetMemoryTicks, Is.EqualTo(300));
            Assert.That(r.SearchTicks, Is.EqualTo(90));
            Assert.That(r.ReactionTicks, Is.EqualTo(15));
            Assert.That(r.SenseTicks, Is.EqualTo(6));
            Assert.That(r.BurstShots, Is.EqualTo(3));
            Assert.That(r.BurstPauseTicks, Is.EqualTo(30));
            Assert.That(r.SuppressTicks, Is.EqualTo(90));
            Assert.That(r.MeleeChaseRange, Is.EqualTo(12));
            Assert.That(r.HoldSharePermille, Is.EqualTo(300));
            Assert.That(r.FairDecisions, Is.False);
            Assert.That(r.BurstPauseFollowsShooter, Is.False);
            // The nine values other than the split are the ones of rwr-ai-playtest.
            AiRules p = RwrPack.Load("rwr-ai-playtest").Section<AiRules>();
            Assert.That((r.FailedSlotTicks, r.TargetMemoryTicks, r.SearchTicks, r.ReactionTicks, r.SenseTicks, r.BurstShots,
                    r.BurstPauseTicks, r.SuppressTicks, r.MeleeChaseRange),
                Is.EqualTo((p.FailedSlotTicks, p.TargetMemoryTicks, p.SearchTicks, p.ReactionTicks, p.SenseTicks, p.BurstShots,
                    p.BurstPauseTicks, p.SuppressTicks, p.MeleeChaseRange)));
        }

        // Everything but the AI pack is the respawn demo's.
        [Test]
        public void PF1Recipe_RestIsTheRespawnDemo()
        {
            WorldRecipe demo = DemoRecipes.MpRespawnDemo(), play = DemoRecipes.MpPlaytest();
            Assert.That(play.Units.ToArray(), Is.EqualTo(demo.Units.ToArray()));
            Assert.That(play.PlayerKey, Is.EqualTo(demo.PlayerKey));
            Assert.That(play.Seed, Is.EqualTo(demo.Seed));
            Assert.That(play.Mover, Is.EqualTo(demo.Mover));
            Assert.That(play.CrowdPass, Is.EqualTo(demo.CrowdPass));
            Assert.That(play.RuleSetId, Is.EqualTo(demo.RuleSetId));
            Assert.That(play.Match.PolicyDigest, Is.EqualTo(demo.Match.PolicyDigest));
            Assert.That(play.Combat.PolicyDigest, Is.EqualTo(demo.Combat.PolicyDigest));
            Assert.That(play.TerrainPack, Is.Null);
        }

        [Test]
        public void PF1World_ValuesReachTheInstalledSystems()
        {
            M6World w = World(DemoRecipes.MpPlaytest());
            SimpleAiSystem ai = w.System<SimpleAiSystem>();
            Assert.That(AiPlaytest.Field(ai, "holdSharePermille"), Is.EqualTo(300));
            Assert.That(AiPlaytest.Field(ai, "fairDecisions"), Is.EqualTo(false));
            Assert.That(AiPlaytest.Field(w.System<FailedSlotMemory>(), "ticks"), Is.EqualTo(90));
            Assert.That(AiPlaytest.Field(w.System<TargetMemory>(), "ticks"), Is.EqualTo(300));
            Assert.That(AiPlaytest.Field(w.System<TargetSearch>(), "ticks"), Is.EqualTo(90));
            Assert.That(AiPlaytest.Field(w.System<TargetSuppress>(), "ticks"), Is.EqualTo(90));
            ReactionFire fire = w.System<ReactionFire>();
            Assert.That(AiPlaytest.Field(fire, "ticks"), Is.EqualTo(15));
            Assert.That(AiPlaytest.Field(fire, "senseTicks"), Is.EqualTo(6));
            Assert.That(AiPlaytest.Field(fire, "burstShots"), Is.EqualTo(3));
            Assert.That(AiPlaytest.Field(fire, "burstPauseTicks"), Is.EqualTo(30));
            Assert.That(AiPlaytest.Field(w.Combat, "autoFire"), Is.SameAs(fire));
            Assert.That(World(DemoRecipes.MpRespawnDemo()).System<ReactionFire>(), Is.Null);
        }

        [Test]
        public void PF1World_RepeatsEveryTickAndRestoresAt300()
        {
            M6World first = World(DemoRecipes.MpPlaytest()), repeat = World(DemoRecipes.MpPlaytest());
            Assert.That(repeat.Hash, Is.EqualTo(first.Hash));
            var hashes = new ulong[Ticks + 1];
            byte[] snapshot = null;
            for (int tick = 1; tick <= Ticks; tick++)
            {
                first.Step(); repeat.Step();
                Assert.That(repeat.Hash, Is.EqualTo(first.Hash), "tick " + first.Sim.Clock.Tick);
                hashes[tick] = first.Hash;
                if (tick == Restore) snapshot = first.Sim.SaveRetainedSnapshot();
            }
            Assert.That(first.Sim.IsFinished, Is.False);
            M6World restored = World(DemoRecipes.MpPlaytest());
            restored.Sim.RestoreRetainedSnapshot(snapshot);
            Assert.That(restored.Hash, Is.EqualTo(hashes[Restore]));
            for (int tick = Restore + 1; tick <= Ticks; tick++)
            {
                restored.Step();
                Assert.That(restored.Hash, Is.EqualTo(hashes[tick]), "tick " + tick);
            }
            TestContext.Progress.WriteLine("PF1 hash t300=" + hashes[Restore].ToString("x16") + " t600=" + hashes[Ticks].ToString("x16"));
        }

        // Per team, the distinct point indexes that alive AI units are ordered into or stand in (inside a point's radius).
        internal static int[] DistinctTargets(M6World w, out string detail)
        {
            MatchPack match = w.Recipe.Match;
            var seen = new[] { new SortedSet<int>(), new SortedSet<int>(), new SortedSet<int>() };
            var counts = new Dictionary<(int team, int point), int>();
            ComponentType<Combatant> combatants = w.Sim.Entities.Resolve<Combatant>();
            ComponentType<MoveIntent> intents = w.Sim.Entities.Resolve<MoveIntent>();
            ComponentType<Position> positions = w.Sim.Entities.Resolve<Position>();
            ulong? controlled = w.Session.ControlledKey;
            foreach (UnitSpawn spawn in w.Recipe.Units)
            {
                if (spawn.Key == controlled) continue;
                EntityId e = w.Unit(spawn.Key);
                if (!w.Sim.Entities.TryGet(combatants, e, out Combatant unit) || w.Combat.IsDead(e)) continue;
                // A moving unit counts for the point it is ordered into, a stopped one for the point it stands in.
                SimVec3 at;
                if (w.Sim.Entities.TryGet(intents, e, out MoveIntent intent) && intent.Active) at = intent.Target;
                else if (w.Sim.Entities.TryGet(positions, e, out Position position)) at = position.Value;
                else continue;
                for (int i = 0; i < match.Points.Length; i++)
                {
                    ControlPointDef p = match.Points[i];
                    if (CombatSystem.Horizontal(at - p.Center).LengthSquared > new SimVec3(p.Radius, default, default).LengthSquared) continue;
                    seen[unit.Team].Add(i);
                    counts.TryGetValue((unit.Team, i), out int n);
                    counts[(unit.Team, i)] = n + 1;
                    break;
                }
            }
            var text = new System.Text.StringBuilder();
            foreach (var pair in counts) text.Append(" team" + pair.Key.team + "/point" + pair.Key.point + "=" + pair.Value);
            detail = text.ToString();
            return new[] { 0, seen[1].Count, seen[2].Count };
        }

        // Acceptance 4: with the split each team sends AI units to at least two points; the respawn demo's number is recorded.
        // Counted at tick 600, not 300: the first flips land on tick 300 itself (points 0 and 4), so on that tick no unit has
        // thought again and both recipes still show one point per team; from tick 330 on the split is visible.
        [Test]
        public void PF1Split_EachTeamTargetsAtLeastTwoPoints()
        {
            M6World play = World(DemoRecipes.MpPlaytest()), demo = World(DemoRecipes.MpRespawnDemo());
            play.Step(Ticks); demo.Step(Ticks);
            int[] p = DistinctTargets(play, out string pd), d = DistinctTargets(demo, out string dd);
            TestContext.Progress.WriteLine("PF1 tick 600 distinct target points: MpPlaytest team1=" + p[1] + " team2=" + p[2] + " (" + pd.Trim()
                + "); MpRespawnDemo team1=" + d[1] + " team2=" + d[2] + " (" + dd.Trim() + ")");
            Assert.That(p[1], Is.GreaterThanOrEqualTo(2), pd);
            Assert.That(p[2], Is.GreaterThanOrEqualTo(2), pd);
        }
    }
}
