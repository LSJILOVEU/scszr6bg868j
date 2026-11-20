using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Anc2ThrustTests
    {
        // One spearman (key 1) faces a swordsman straight ahead (key 2) and one 1.2 m to the side (key 3). Both are inside the
        // 60 degree sector; only the first is inside the thrust line. The spear may hit two targets here (an in-memory edit of
        // maxTargets), so the number of targets shows the hit shape.
        private static int TargetsOfTheFirstSpearSwing(bool thrust)
        {
            Func<string, string, string> widen = Anc2.Replace("ancient-field", "\"maxTargets\": 1", "\"maxTargets\": 2");
            Func<string, string, string> edit = thrust ? widen : (id, text) => Anc2.Replace("ancient-field", ", \"lineHalfWidth\": 0.3", "")(id, widen(id, text));
            ContentPack pack = Anc2.Load(edit);
            var units = new[]
            {
                UnitSpawn.OfType(1, pack, "ancient.spearman", 1, 1, 10000, 10000),
                UnitSpawn.OfType(2, pack, "ancient.swordsman", 2, 2, 12000, 10000),
                UnitSpawn.OfType(3, pack, "ancient.swordsman", 2, 2, 12000, 11200),
            };
            var points = new[] { new ControlPointDef(1, SimVec3.Metres(48, 0, 32), SimFix.FromInt(4)) };
            var anchors = new[] { new SpawnAnchor(1, SimVec3.Metres(4, 0, 32)), new SpawnAnchor(2, SimVec3.Metres(92, 0, 32)) };
            M6World w = Anc2.Build(pack, units, points, anchors);
            StableId spearman = w.Sim.Entities.StableOf(w.Unit(1));
            for (int i = 0; i < 40; i++)
            {
                w.Step();
                int landed = 0;
                foreach (StrikeRecord s in w.Combat.Strikes)
                    if (s.Kind == StrikeKind.Landed && s.Striker.Equals(spearman)) landed++;
                if (landed > 0) return landed;
            }
            return 0;
        }

        [Test]
        public void ATrustHitsTheLineAndASectorHitsBoth()
        {
            Assert.That(TargetsOfTheFirstSpearSwing(true), Is.EqualTo(1));
            Assert.That(TargetsOfTheFirstSpearSwing(false), Is.EqualTo(2));
        }
    }
}
