using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml11aWorld;
using static WarCore.Tests.RespawnWorld;

namespace WarCore.Tests
{
    // ML-11a acceptance 15: on a map with floor plates the AI rules that still measure on the plane are refused when the world
    // is built, in a fixed order; the same rules build a world without plates, and the values that turn a rule off pass.
    // ML-11b-1 lifted the search, the suppressive fire and the melee chase: they read layers now and build on plates.
    public sealed class Ml11aRulesTests
    {
        private const string Pressure = "\"pressureDeathRadiusMm\": 3000, \"pressureDeathShock\": 2000, \"pressureNearRadiusMm\": 3000, \"pressureNearShock\": 1000, "
            + "\"pressureDecayPermille\": 12, \"pressureBrokenAt\": 7000, \"pressureRecoverAt\": 1000, \"pressurePinnedAt\": 2000, "
            + "\"pressureSpreadPermille\": 500, \"pressureBreakSharePermille\": 300";
        private const string Bound = "\"engage\": \"bound\", \"boundMoveTicks\": 60, \"boundFireTicks\": 90, \"boundSharePermille\": 700, \"boundMinRangeMm\": 12000";
        private const string Scatter = "\"placement\": \"scatter\", \"scatterMm\": 6000";
        private const string Roles = "\"placement\": \"assault-roles\", \"frontPermille\": 425, \"flankPermille\": 375, \"rearPermille\": 200, \"frontArcMm\": 14000, "
            + "\"flankSideMm\": 35000, \"rearOutMm\": 12000, \"flankMinMm\": 60000, \"anchorMm\": 10000, \"arriveMm\": 3000";

        private static M6World Build(string ai, bool plates = true)
        {
            var units = new[] { Walker(1, 30500, 31500), Walker(2, 60500, 31500, 0, 2) };
            ControlPointDef[] points = { plates ? PointUp(1, 41500, 32500) : PointDown(1, 41500, 32500) };
            if (plates) return World(units, points, ai: Rules("{ " + ai + " }"));
            MatchPack pack = M6World.Pack(points, interval: 1000000, think: 10, bases: Bases(new SpawnAnchor(1, P(5, 30))));
            return new M6World(M6World.Endless(Recipe(units, pack, false)).WithAiRules(Rules("{ " + ai + " }")), Ml9bMatchTests.Arena(false));
        }

        [TestCase(Pressure, "pressureNearRadiusMm")]
        [TestCase(Bound, "engage")]
        [TestCase(Scatter, "placement")]
        [TestCase(Roles, "placement")]
        // Two at once: the first in the fixed order is the one named.
        [TestCase(Pressure + ", " + Bound, "pressureNearRadiusMm")]
        [TestCase(Bound + ", " + Scatter, "engage")]
        [TestCase(Pressure + ", " + Scatter, "pressureNearRadiusMm")]
        public void ARuleThatMeasuresOnThePlaneIsRefusedOnPlates(string ai, string field)
        {
            // The fixture builds the world by reflection, which wraps what the build throws.
            Exception thrown = Assert.Throws<System.Reflection.TargetInvocationException>(() => Build(ai)).InnerException;
            Assert.That(thrown, Is.InstanceOf<ContentException>());
            string message = thrown.Message;
            Assert.That(message, Does.Contain(field).And.Contain("is not supported on a map with floor plates"));
            foreach (string other in new[] { "pressureNearRadiusMm", "engage", "placement" })
                if (other != field) Assert.That(message, Does.Not.Contain(other), other);
            Assert.DoesNotThrow(() => Build(ai, false), "the same rules build a world without plates");
        }

        // The values that leave a rule off, and every rule the layered AI supports, written out.
        [TestCase("\"searchTicks\": 0, \"suppressTicks\": 0, \"meleeChaseRange\": 0, \"engage\": \"hold\", \"placement\": \"nearest-slot\"")]
        [TestCase("\"failedSlotTicks\": 60, \"targetMemoryTicks\": 90, \"reactionTicks\": 6, \"senseTicks\": 3, \"burstShots\": 3, \"burstPauseTicks\": 20, "
            + "\"slotSpacingMm\": 1500, \"slotRingMm\": 1000, \"holdSharePermille\": 300, \"attackSpreadPoints\": 2, \"dispatch\": \"nearest\"")]
        [TestCase("\"targetMemoryTicks\": 90, \"searchTicks\": 60, \"suppressTicks\": 30, \"meleeChaseRange\": 12")]
        public void TheOtherRulesBuildOnPlates(string ai)
        {
            M6World w = null;
            Assert.DoesNotThrow(() => w = Build(ai));
            w.Step(30);
            Assert.That(Ordered(w, 1), Is.True);
        }

        // Rules built in code have no place in a pack to point at: the refusal still names the field.
        [Test]
        public void RulesBuiltInCodeAreRefusedByName()
        {
            var rules = new AiRules(0, engage: ContentPolicies.BoundEngage, boundMoveTicks: 60, boundFireTicks: 90, boundSharePermille: 700, boundMinRangeMm: 12000);
            Assert.That(rules.FieldError, Is.Null);
            Assert.That(Assert.Throws<NotSupportedException>(() => ContentPolicies.Parts(rules, plates: true)).Message,
                Is.EqualTo("ai.engage is not supported on a map with floor plates."));
            Assert.DoesNotThrow(() => ContentPolicies.Parts(rules, plates: false));
            Assert.DoesNotThrow(() => ContentPolicies.Parts(rules));
            Assert.DoesNotThrow(() => ContentPolicies.Parts(null, plates: true));
        }
    }
}
