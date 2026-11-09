using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // DemoRecipes.TerrainMatch: the shipped demo terrain match. Its layout must be the L6 fixture's; its content values come
    // from the shipped files and are not locked here (only that the match is decided).
    public sealed class L6DemoRecipeTests
    {
        private static string Edit(string json, params (string From, string To)[] edits)
        {
            foreach (var (from, to) in edits)
            {
                Assert.That(json, Does.Contain(from), "test text drifted");
                json = json.Replace(from, to);
            }
            return json;
        }

        private static ContentPack ContentA() => L6.Load("l6-content", Edit(L6.PackJson,
            ("\"id\": \"l6-test\"", "\"id\": \"l6-content\""),
            ("{ \"id\": \"rwr.soldier-1\", \"weapon\": \"rwr.weapon-1\" }, { \"id\": \"rwr.soldier-2\", \"weapon\": \"rwr.weapon-2\" }",
             "{ \"id\": \"rwr.soldier-1\", \"weapon\": \"rwr.weapon-2\" }, { \"id\": \"rwr.soldier-2\", \"weapon\": \"rwr.weapon-1\" }"),
            ("\"fireIntervalMilli\": 3390", "\"fireIntervalMilli\": 3391"),
            ("\"captureTicks\": 300", "\"captureTicks\": 301"),
            ("\"thinkInterval\": 30,", "\"thinkInterval\": 31,")));

        // The weapon interval differs too: the combat digest covers only weapons, the combat section and the sight.
        private static ContentPack TerrainB() => L6.Load("l6-terrain", Edit(L6.PackJson,
            ("\"id\": \"l6-test\"", "\"id\": \"l6-terrain\""),
            ("\"fireIntervalMilli\": 3390", "\"fireIntervalMilli\": 3392"),
            ("\"captureTicks\": 300", "\"captureTicks\": 302"),
            ("\"speedMilli\": 500,", "\"speedMilli\": 501,"),
            ("\"failedSlotTicks\": 0", "\"failedSlotTicks\": 7")));

        [Test]
        public void L6Demo_LayoutIsTheFixture()
        {
            WorldRecipe demo = DemoRecipes.TerrainMatch(), fixture = L6.Recipe();
            Assert.That((demo.Seed, demo.Mover, demo.CrowdPass, demo.RuleSetId), Is.EqualTo((fixture.Seed, fixture.Mover, fixture.CrowdPass, fixture.RuleSetId)));
            Assert.That(demo.RuleSetId, Is.EqualTo(MatchMode.RwrRuleSetId));
            Assert.That(demo.PlayerKey.HasValue, Is.False);
            Assert.That(demo.Units.Length, Is.EqualTo(fixture.Units.Length));
            for (int i = 0; i < fixture.Units.Length; i++)
            {
                UnitSpawn d = demo.Units[i], f = fixture.Units[i];
                Assert.That((d.Key, d.Archetype, d.Squad, d.XMm, d.ZMm, d.SpeedMmPerSec, d.Team, d.Weapon, d.Reserve, d.Melee, d.Block),
                    Is.EqualTo((f.Key, f.Archetype, f.Squad, f.XMm, f.ZMm, f.SpeedMmPerSec, f.Team, f.Weapon, f.Reserve, f.Melee, f.Block)), "unit " + i);
            }
            Assert.That(demo.Match.Points.Length, Is.EqualTo(fixture.Match.Points.Length));
            for (int i = 0; i < fixture.Match.Points.Length; i++)
            {
                ControlPointDef d = demo.Match.Points[i], f = fixture.Match.Points[i];
                Assert.That((d.Id, d.Center, d.Radius, d.InitialOwner, d.HasSpawn, d.Spawn),
                    Is.EqualTo((f.Id, f.Center, f.Radius, f.InitialOwner, f.HasSpawn, f.Spawn)), "point " + i);
            }
            Assert.That(demo.Match.Bases.Length, Is.EqualTo(fixture.Match.Bases.Length));
            for (int i = 0; i < fixture.Match.Bases.Length; i++)
                Assert.That((demo.Match.Bases[i].Team, demo.Match.Bases[i].Point), Is.EqualTo((fixture.Match.Bases[i].Team, fixture.Match.Bases[i].Point)), "base " + i);
            Assert.That(demo.TerrainPack.Id, Is.EqualTo(TerrainDemoMap.PackId));
        }

        [Test]
        public void L6Demo_CombatAndMatchFromContent_TerrainFromTerrain()
        {
            ContentPack a = ContentA(), b = TerrainB(), shipped = RwrPack.Load(RwrPack.M6Layer);
            ulong ca = a.BuildCombat().PolicyDigest, cb = b.BuildCombat().PolicyDigest, cs = shipped.BuildCombat().PolicyDigest;
            ulong ma = TerrainDemoMap.BuildMatch(a).PolicyDigest, mb = TerrainDemoMap.BuildMatch(b).PolicyDigest, ms = TerrainDemoMap.BuildMatch(shipped).PolicyDigest;
            Assert.That(new[] { ca, cb, cs }, Is.Unique, "the three combat digests must differ");
            Assert.That(new[] { ma, mb, ms }, Is.Unique, "the three match digests must differ");

            WorldRecipe r = DemoRecipes.TerrainMatch(a, b);
            foreach (UnitSpawn u in r.Units)
                Assert.That(u.Weapon, Is.EqualTo(u.Team == 1 ? 1 : 0), "unit " + u.Key + " takes its weapon from the content pack");
            Assert.That(r.Combat.PolicyDigest, Is.EqualTo(ca));
            Assert.That(r.Match.PolicyDigest, Is.EqualTo(ma));
            Assert.That(r.TerrainPack, Is.SameAs(b));
        }

        [Test]
        public void L6Demo_ThePublicSessionPlaysTheMatchToAResult()
        {
            SimSession session = SimSession.Create(DemoRecipes.TerrainMatch(), TerrainDemo.MapBytes());
            while (!session.IsFinished && session.Tick < L6.StepLimit) session.StepOnce();
            Assert.That(session.IsFinished, Is.True);
            Assert.That(session.Winner, Is.EqualTo(1).Or.EqualTo(2));
            Assert.That(session.EndTick, Is.LessThanOrEqualTo(9000));
            Assert.That(session.EndReason, Is.EqualTo((int)MatchEndReason.AllPoints).Or.EqualTo((int)MatchEndReason.Exhausted).Or.EqualTo((int)MatchEndReason.Time));
        }

        [Test]
        public void L6Demo_EventAnchorsExist()
        {
            var w = new M6World(DemoRecipes.TerrainMatch(), TerrainDemo.MapBytes());
            L6Trace trace = L6.RunToEnd(w);
            TestContext.Progress.WriteLine("L6 demo record: " + L6.Line(w, trace));
            Assert.That(trace.FirstFire, Is.GreaterThanOrEqualTo(1), "first shot");
            Assert.That(trace.FirstFire + 90, Is.LessThan(w.Match.EndTick), "three seconds after the first shot");
            Assert.That(trace.FirstFlip, Is.GreaterThanOrEqualTo(1), "first change of owner");
            Assert.That(trace.FirstFlip, Is.LessThan(w.Match.EndTick));
        }
    }
}
