using System;
using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // MAP-BL0: the stage sets the showcase host can play, and the ward's one stage.
    public sealed class WardSetsTests
    {
        [Test]
        public void TheEmptySetIsTheMainShowcaseItself()
        {
            Assert.That(ShowcaseSets.Stages(""), Is.SameAs(Showcase.Stages));
        }

        [Test]
        public void TheWardSetIsTheWardStageAlone()
        {
            var stages = ShowcaseSets.Stages("ward");
            Assert.That(stages, Is.SameAs(WardDemo.Stages));
            Assert.That(stages.Count, Is.EqualTo(1));
            Assert.That((stages[0].Id, stages[0].Title), Is.EqualTo(("ward", "Walled ward")));
            Assert.That(ShowcaseSets.ShotPrefix("ward"), Is.EqualTo("WARD"));
            Assert.That(WardDemo.SetId, Is.EqualTo("ward"));
        }

        [TestCase("x")]
        [TestCase("Ward")]
        [TestCase("WARD")]
        [TestCase(" ward")]
        [TestCase(null)]
        public void AnyOtherSetIsRefusedByName(string set)
        {
            var e = Assert.Throws<ArgumentException>(() => ShowcaseSets.Stages(set));
            Assert.That(e.Message, Does.Contain("'" + (set ?? "null") + "'"));
            var p = Assert.Throws<ArgumentException>(() => ShowcaseSets.ShotPrefix(set));
            Assert.That(p.Message, Does.Contain("'" + (set ?? "null") + "'"));
        }

        [Test]
        public void TheMainShowcaseHasNoSetPrefix()
        {
            Assert.Throws<ArgumentException>(() => ShowcaseSets.ShotPrefix(""));
        }

        [Test]
        public void TheWardStageStartsOnTheCommittedMapWithKeyOneAsThePlayer()
        {
            ShowcaseStage stage = WardDemo.Stages[0];
            Assert.DoesNotThrow(() =>
            {
                Assert.That(stage.MapBytes(), Is.EqualTo(WardWorld.Baked.WcMapBytes));
                Assert.That(stage.Recipe().PlayerKey, Is.EqualTo(1UL));
                Assert.That(stage.Shows.Count, Is.GreaterThan(0));
                SimSession session = stage.Start();
                Assert.That(session.ControlledKey, Is.EqualTo(1UL));
                for (int t = 0; t < 30; t++) session.StepOnce();
                Assert.That(session.Tick, Is.EqualTo(30));
                Assert.That(stage.Colors(session.Map).Team.Count, Is.GreaterThanOrEqualTo(2));
            });
        }

        // The script walks east towards the crossroads and uses fire, dash and block.
        [Test]
        public void TheScriptWalksToTheCrossroadsAndFiresDashesAndBlocks()
        {
            var script = WardDemo.Script();
            Assert.That(script, Is.Not.SameAs(WardDemo.Script()), "a new array each call");
            bool fire = false, dash = false, block = false;
            foreach (var item in script)
            {
                Assert.That((item.Input.AimXMm, item.Input.AimZMm), Is.EqualTo((80000L, 80000L)), "aims at the crossroads");
                fire |= item.Input.Primary; dash |= item.Input.Dash; block |= item.Input.Block;
            }
            Assert.That((fire, dash, block), Is.EqualTo((true, true, true)));
            Assert.That(script[0].Input.MoveXMilli, Is.EqualTo(1000), "east first");
        }
    }
}
