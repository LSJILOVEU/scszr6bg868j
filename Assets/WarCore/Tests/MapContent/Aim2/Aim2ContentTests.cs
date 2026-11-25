using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // AIM-2 S5-S7b: the content section "pick" (radiusPermille 2000..6000, heightPermille 1000..2000, both required after the
    // merge, needs combat.body), kept out of Sim, and the showcase stages' reach.
    public sealed class Aim2ContentTests
    {
        private static ContentPack Load(string id, params (string Id, string Json)[] layers)
        {
            var map = new Dictionary<string, string>();
            foreach (var l in layers) map[l.Id] = l.Json;
            return ContentLoader.Load(new MemoryContentSource(map, FileContentSource.Rwr()), id);
        }

        private static string Layer(string id, string pick, string extends = "rwr-body") =>
            "{ \"id\": \"" + id + "\", \"extends\": \"" + extends + "\"" + (pick == null ? "" : ", \"pick\": " + pick) + " }";

        private static string Pick(string radius, string height) => "{ \"radiusPermille\": " + radius + ", \"heightPermille\": " + height + " }";

        private static PickRules Rules(string pick) => Load("aim2-x", ("aim2-x", Layer("aim2-x", pick))).SectionOrNull<PickRules>();

        private static string Error(string pick, string extends = "rwr-body") =>
            Assert.Throws<ContentException>(() => Load("aim2-x", ("aim2-x", Layer("aim2-x", pick, extends)))).Message;

        [Test]
        public void S5_TheSectionIsReadAndALayerWithoutItHasNone()
        {
            PickRules r = Rules(Pick("3000", "1150"));
            Assert.That((r.RadiusPermille, r.HeightPermille), Is.EqualTo((3000, 1150)));
            Assert.That(Rules(null), Is.Null);
            Assert.That(PickReach.Of(Load("aim2-x", ("aim2-x", Layer("aim2-x", null)))), Is.Null);
            PickReach reach = PickReach.Of(Load("aim2-x", ("aim2-x", Layer("aim2-x", Pick("2500", "1200")))));
            Assert.That((reach.RadiusPermille, reach.HeightPermille), Is.EqualTo((2500, 1200)));
        }

        [TestCase("2000", "1000")]
        [TestCase("6000", "2000")]
        [TestCase("2000", "2000")]
        [TestCase("6000", "1000")]
        public void S5_TheBoundsAreAccepted(string radius, string height)
        {
            PickRules r = null;
            Assert.DoesNotThrow(() => r = Rules(Pick(radius, height)));
            Assert.That((r.RadiusPermille, r.HeightPermille), Is.EqualTo((int.Parse(radius), int.Parse(height))));
        }

        [TestCase("1999", "1150", "radiusPermille")]
        [TestCase("999", "1150", "radiusPermille")]
        [TestCase("6001", "1150", "radiusPermille")]
        [TestCase("3000", "999", "heightPermille")]
        [TestCase("3000", "2001", "heightPermille")]
        [TestCase("3000.5", "1150", "radiusPermille")]
        [TestCase("3000", "1.5", "heightPermille")]
        [TestCase("\"3000\"", "1150", "radiusPermille")]
        public void S5_OutOfRangeOrNotWholeIsRejected(string radius, string height, string field)
        {
            Assert.That(Error(Pick(radius, height)), Does.Contain("field '" + field + "'"));
        }

        [Test]
        public void S5_UnknownAndMissingFieldsAreRejected()
        {
            Assert.That(Error("{ \"radiusPermille\": 3000, \"heightPermille\": 1150, \"radius\": 1 }"), Does.Contain("field 'radius': unknown field name"));
            Assert.That(Error("{ \"radiusPermille\": 3000 }"), Does.Contain("heightPermille"));
            Assert.That(Error("{ \"heightPermille\": 1150 }"), Does.Contain("radiusPermille"));
        }

        // A child that writes one key inherits the other from its parent; a child that writes both overrides both.
        [Test]
        public void S5_AChildInheritsAndOverridesFieldByField()
        {
            var parent = ("aim2-parent", Layer("aim2-parent", Pick("3000", "1150")));
            PickRules one = Load("aim2-child", parent, ("aim2-child", Layer("aim2-child", "{ \"radiusPermille\": 4000 }", "aim2-parent")))
                .SectionOrNull<PickRules>();
            Assert.That((one.RadiusPermille, one.HeightPermille), Is.EqualTo((4000, 1150)));
            PickRules other = Load("aim2-child", parent, ("aim2-child", Layer("aim2-child", "{ \"heightPermille\": 1500 }", "aim2-parent")))
                .SectionOrNull<PickRules>();
            Assert.That((other.RadiusPermille, other.HeightPermille), Is.EqualTo((3000, 1500)));
            PickRules both = Load("aim2-child", parent, ("aim2-child", Layer("aim2-child", Pick("5000", "1000"), "aim2-parent")))
                .SectionOrNull<PickRules>();
            Assert.That((both.RadiusPermille, both.HeightPermille), Is.EqualTo((5000, 1000)));
            PickRules heir = Load("aim2-child", parent, ("aim2-child", Layer("aim2-child", null, "aim2-parent"))).SectionOrNull<PickRules>();
            Assert.That((heir.RadiusPermille, heir.HeightPermille), Is.EqualTo((3000, 1150)));
        }

        // The pick is sized by the body: without combat.body it is refused, naming both.
        [Test]
        public void S5_APickWithoutABodyIsRejected()
        {
            string message = Error(Pick("3000", "1150"), "rwr-m6");
            Assert.That(message, Does.Contain("pick"));
            Assert.That(message, Does.Contain("combat.body"));
        }

        // S6: two cartridges with the same layer id, one with a pick and one without, play the house map with the same script:
        // every tick's state hash is the same, so the pick reaches neither Sim nor the pack's part of the hash.
        [Test]
        public void S6_ThePickNeverReachesTheSimulation()
        {
            const string id = "aim2-s6";
            ulong[] Run(string pick)
            {
                using (var dirs = new Ed0Dirs())
                {
                    string dir = dirs.Dir("Data");
                    Ed0Dirs.CopyRwr(dir);
                    File.WriteAllText(Path.Combine(dir, id + ".json"), Layer(id, pick));
                    Ed0Dirs.UseList(dir);
                    ShowcaseStage stage = Showcase.HouseStage(id);
                    Assert.That(stage.Reach() != null, Is.EqualTo(pick != null), "the cartridge is the one in use");
                    SimSession s = stage.Start();
                    foreach (var item in stage.Script()) s.SetPlayerInput(item.AtTick, item.Input);
                    var hashes = new ulong[601];
                    hashes[0] = s.StateHash;
                    for (int t = 1; t <= 600; t++)
                    {
                        s.StepOnce();
                        hashes[t] = s.StateHash;
                    }
                    return hashes;
                }
            }
            ulong[] with = Run(Pick("3000", "1150")), without = Run(null);
            Assert.That(with, Is.EqualTo(without));
            Assert.That(with[600], Is.Not.EqualTo(with[0]));
        }

        private static ShowcaseStage Stage(string id)
        {
            foreach (ShowcaseStage stage in Showcase.Stages) if (stage.Id == id) return stage;
            throw new ArgumentException(id);
        }

        // S7b: the listed house stage fights with a body and reads the shipped pick from its layer; the legacy house has no
        // body; the stages whose layer has no pick (terrain, melee) get no reach.
        [Test]
        public void S7b_TheShowcaseStagesReadTheirReachFromTheirLayer()
        {
            ShowcaseStage house = Stage("house");
            Assert.That(house.Layer, Is.EqualTo(Showcase.HouseLayer));
            Assert.That(BodySizes.Of(house.Start()), Is.Not.Null);
            Assert.That(BodySizes.Of(Showcase.LegacyHouse.Start()), Is.Null);
            Assert.That(Showcase.LegacyHouse.Reach(), Is.Null);
            foreach (string id in new[] { "battle", "bullets", "house", "hamlet" })
            {
                PickReach reach = Stage(id).Reach();
                Assert.That(reach, Is.Not.Null, id);
                Assert.That((reach.RadiusPermille, reach.HeightPermille), Is.EqualTo((3000, 1150)), id);
                Assert.That(BodySizes.Of(Stage(id).Start()), Is.Not.Null, id + ": a reach needs a body");
            }
            Assert.That(Stage("terrain").Reach(), Is.Null);
            Assert.That(Stage("melee").Reach(), Is.Null);
        }
    }
}
