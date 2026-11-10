using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class F6UnitTypeSpawnTests
    {
        private const string ShieldFields = "\"cosHalfAnglePermille\":500,\"reductionPermille\":750,\"aiRaisePermille\":1000";
        private const string UnitFields = "\"speed\":4200,\"block\":\"shield\",\"archetype\":7";

        private static ContentPack Load(string fields, string shield = ShieldFields) =>
            ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
            {
                ["f6-pack"] = "{\"id\":\"f6-pack\",\"extends\":\"rwr-melee\",\"block\":["
                    + "{\"id\":\"unused\",\"cosHalfAnglePermille\":0,\"reductionPermille\":0,\"aiRaisePermille\":0},"
                    + "{\"id\":\"shield\"," + shield + "}],\"unitTypes\":["
                    + "{\"id\":\"guard\",\"weapon\":\"rwr.weapon-2\",\"melee\":\"rwr.sword-1\""
                    + (fields.Length == 0 ? "" : "," + fields) + "}]}"
            }, FileContentSource.Rwr()), "f6-pack");

        [Test]
        public void F6_OfType_MatchesManualSpawn()
        {
            ContentPack pack = Load(UnitFields);
            UnitSpawn actual = UnitSpawn.OfType(17, pack, "guard", 3, 1, 30000, 20000);
            var manual = new UnitSpawn(17, 7, 3, 30000, 20000, 4200, 1, 1, false, 0, 1);
            Assert.That((actual.Archetype, actual.Weapon, actual.Melee, actual.Block, actual.SpeedMmPerSec), Is.EqualTo((7, 1, 0, 1, 4200L)));
            Assert.That((actual.Key, actual.Squad, actual.Team, actual.XMm, actual.ZMm, actual.Reserve), Is.EqualTo((17UL, 3, 1, 30000L, 20000L, false)));
            var opponent = new UnitSpawn(18, 0, 4, 31000, 20000, 3000, 2, -1, false, 0);
            var typed = new MeleeWorld(pack.BuildCombat(), new[] { actual, opponent });
            var direct = new MeleeWorld(pack.BuildCombat(), new[] { manual, opponent });
            Assert.That(typed.Tick, Is.Zero);
            Assert.That(typed.W.Hash, Is.EqualTo(direct.W.Hash), "tick 0");
            typed.W.Combat.Hold(typed.W.Unit(17), SimVec3.Metres(1, 0, 0));
            direct.W.Combat.Hold(direct.W.Unit(17), SimVec3.Metres(1, 0, 0));
            Assert.That(typed.W.Combat.IsHolding(typed.W.Unit(17)), Is.True);
            typed.Step(300); direct.Step(300);
            Assert.That(typed.Tick, Is.EqualTo(300));
            Assert.That(typed.W.Hash, Is.EqualTo(direct.W.Hash), "tick 300");
        }

        [Test]
        public void F6_OfType_PreservesReserveAndCallerFields()
        {
            UnitSpawn spawn = UnitSpawn.OfType(91, Load(UnitFields), "guard", 8, 2, 1234, 5678, true);
            Assert.That((spawn.Key, spawn.Squad, spawn.Team, spawn.XMm, spawn.ZMm, spawn.Reserve), Is.EqualTo((91UL, 8, 2, 1234L, 5678L, true)));
            Assert.That((spawn.Archetype, spawn.Weapon, spawn.Melee, spawn.Block, spawn.SpeedMmPerSec), Is.EqualTo((7, 1, 0, 1, 4200L)));
        }

        [Test]
        public void F6_OfType_DefaultArchetypeAndBlock()
        {
            UnitSpawn spawn = UnitSpawn.OfType(1, Load("\"speed\":3000"), "guard", 1, 1, 0, 0);
            Assert.That((spawn.Archetype, spawn.Block), Is.EqualTo((0, -1)));
            Assert.That((spawn.Weapon, spawn.Melee, spawn.SpeedMmPerSec), Is.EqualTo((1, 0, 3000L)));
        }

        [Test]
        public void F6_OfType_UnknownUnitNamesPackEntryAndField()
        {
            ContentPack pack = Load(UnitFields);
            var error = Assert.Throws<ContentException>(() => UnitSpawn.OfType(1, pack, "missing-unit", 1, 1, 0, 0));
            Assert.That(error.Message, Does.Contain("f6-pack"));
            Assert.That(error.Message, Does.Contain("missing-unit"));
            Assert.That(error.Message, Does.Contain("unitTypeId"));
        }

        [Test]
        public void F6_UnknownBlockNamesPackEntryAndField()
        {
            var error = Assert.Throws<ContentException>(() => Load("\"speed\":3000,\"block\":\"missing-block\""));
            Assert.That(error.Message, Does.Contain("f6-pack"));
            Assert.That(error.Message, Does.Contain("guard"));
            Assert.That(error.Message, Does.Contain("field 'block'"));
            Assert.That(error.Message, Does.Contain("missing-block"));
        }

        [Test]
        public void F6_OfType_MissingSpeedNamesPackEntryAndField()
        {
            ContentPack pack = Load("");
            var error = Assert.Throws<ContentException>(() => UnitSpawn.OfType(1, pack, "guard", 1, 1, 0, 0));
            Assert.That(error.Message, Does.Contain("f6-pack"));
            Assert.That(error.Message, Does.Contain("guard"));
            Assert.That(error.Message, Does.Contain("field 'speed'"));
        }

        [TestCase(0L)]
        [TestCase(UnitSpawn.MaxSpeedMmPerSec + 1)]
        public void F6_SpeedOutsideSpawnRangeIsRejected(long speed)
        {
            var error = Assert.Throws<ContentException>(() => Load("\"speed\":" + speed));
            Assert.That(error.Message, Does.Contain("f6-pack"));
            Assert.That(error.Message, Does.Contain("guard"));
            Assert.That(error.Message, Does.Contain("field 'speed'"));
        }

        [TestCase(1L)]
        [TestCase(UnitSpawn.MaxSpeedMmPerSec)]
        public void F6_SpeedRangeEndsLoad(long speed)
        {
            UnitSpawn spawn = UnitSpawn.OfType(1, Load("\"speed\":" + speed), "guard", 1, 1, 0, 0);
            Assert.That(spawn.SpeedMmPerSec, Is.EqualTo(speed));
        }

        [TestCase("1.5")]
        [TestCase("\"3000\"")]
        public void F6_SpeedMustBeAnInteger(string speed)
        {
            var error = Assert.Throws<ContentException>(() => Load("\"speed\":" + speed));
            Assert.That(error.Message, Does.Contain("field 'speed'"));
        }

        [Test]
        public void F6_BlockLoadsAllThreeFieldsInEntryOrder()
        {
            CombatPack combat = Load(UnitFields).BuildCombat();
            Assert.That(combat.Block.Length, Is.EqualTo(2));
            BlockSpec first = combat.Block[0], second = combat.Block[1];
            Assert.That((first.CosHalfAnglePermille, first.ReductionPermille, first.AiRaisePermille), Is.EqualTo((0, 0, 0)));
            Assert.That((second.CosHalfAnglePermille, second.ReductionPermille, second.AiRaisePermille), Is.EqualTo((500, 750, 1000)));
        }

        [TestCase("cosHalfAnglePermille", -1)]
        [TestCase("cosHalfAnglePermille", 1001)]
        [TestCase("reductionPermille", -1)]
        [TestCase("reductionPermille", 1001)]
        [TestCase("aiRaisePermille", -1)]
        [TestCase("aiRaisePermille", 1001)]
        public void F6_BlockRangeErrorsNamePackEntryAndField(string field, int value)
        {
            string shield = "\"cosHalfAnglePermille\":" + (field == "cosHalfAnglePermille" ? value : 500)
                + ",\"reductionPermille\":" + (field == "reductionPermille" ? value : 750)
                + ",\"aiRaisePermille\":" + (field == "aiRaisePermille" ? value : 1000);
            var error = Assert.Throws<ContentException>(() => Load(UnitFields, shield));
            Assert.That(error.Message, Does.Contain("f6-pack"));
            Assert.That(error.Message, Does.Contain("shield"));
            Assert.That(error.Message, Does.Contain("field '" + field + "'"));
        }
    }
}
