using System;
using System.IO;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class V1AncientMatchTests
    {
        private const string DataDirectory = "Assets/WarCore/Content/Ancient/Data";

        // PLACEHOLDER: seed, formation, terrain bounds, control point and anchors are fixture values.
        private static M6World World()
        {
            ContentPack pack = ContentLoader.Load(new FileContentSource(DataDirectory), "ancient");
            Assert.That(File.ReadAllText(Path.Combine(DataDirectory, "ancient.json")), Does.Not.Contain("rwr."));
            Assert.That(pack.WeaponCount, Is.Zero);
            Assert.That(pack.MeleeCount, Is.EqualTo(1));
            var units = new UnitSpawn[40];
            for (int i = 0; i < 20; i++)
            {
                string type = i % 2 == 0 ? "ancient.swordsman" : "ancient.guard";
                units[i] = UnitSpawn.OfType((ulong)i + 1, pack, type, 1, 1, 4000 + 1500 * (i % 4), 4000 + 2000 * (i / 4));
                units[i + 20] = UnitSpawn.OfType((ulong)i + 21, pack, type, 2, 2, 28000 - 1500 * (i % 4), 4000 + 2000 * (i / 4));
            }
            var points = new[] { new ControlPointDef(1, SimVec3.Metres(16, 0, 8), SimFix.FromInt(4)) };
            var anchors = new[] { new SpawnAnchor(1, SimVec3.Metres(2, 0, 8)), new SpawnAnchor(2, SimVec3.Metres(30, 0, 8)) };
            var recipe = new WorldRecipe(41, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>())
                .WithCombat(pack.BuildCombat()).WithMatch(pack.BuildMatch(points, anchors))
                .WithCrowdPass(CrowdPassKind.TeamIdle).WithTerrainRules(pack).WithFacing().WithBlockAi();
            byte[] map = WcMapWriter.Write(Wp23.Make(32, 16, 0, 4 * SimFix.Scale, Array.Empty<(int, int, int, int)>(),
                2 * SimFix.Scale, (x, z) => 0, new[] { "ancient.ground" }, (x, z) => 0));
            return new M6World(recipe, map);
        }

        private static M6World RunToEnd()
        {
            M6World w = World();
            int hits = 0, blocks = 0;
            while (!w.Mode.IsFinished && w.Sim.Clock.Tick < w.Recipe.Match.MaxTicks)
            {
                w.Step();
                foreach (StrikeRecord strike in w.Combat.Strikes)
                {
                    if (strike.Kind == StrikeKind.Landed && strike.DamageRaw == 34 * SimFix.Scale) hits++;
                    if (strike.Kind == StrikeKind.Landed && strike.DamageRaw == SimFix.Ratio(17, 2).Raw) blocks++;
                }
            }
            Assert.That(w.Mode.IsFinished, Is.True);
            Assert.That(w.Mode.EndReason, Is.EqualTo(MatchEndReason.Exhausted));
            Assert.That(hits, Is.GreaterThan(0));
            Assert.That(blocks, Is.GreaterThan(0));
            return w;
        }

        private static int Deaths(M6World w, int team)
        {
            int count = 0;
            foreach (UnitSpawn unit in w.Recipe.Units)
                if (unit.Team == team && w.Dead(unit.Key)) count++;
            return count;
        }

        [Test]
        public void V1_AncientMatch_RunsToEnd()
        {
            M6World w = RunToEnd();
            Assert.That(w.Mode.Winner, Is.EqualTo(2));
            Assert.That(w.Mode.EndTick, Is.EqualTo(177));
            Assert.That(new[] { Deaths(w, 1), Deaths(w, 2) }, Is.EqualTo(new[] { 20, 16 }));
        }

        [Test]
        public void V1_AncientMatch_SameSeedSameHash()
        {
            M6World first = RunToEnd(), second = RunToEnd();
            Assert.That(second.Hash, Is.EqualTo(first.Hash));
        }
    }
}
