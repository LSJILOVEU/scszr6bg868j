using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using WarCore.Tests.Motion;

namespace WarCore.Tests
{
    // RC1-K3: the pack of a world turns its switches on. Facing, standing orders and inertia each have a field. The block AI
    // has none of its own: a shield that gives the AI a chance, in a world whose units face a direction, is the switch. A
    // world built from the field is, byte for byte, the world a recipe built by hand gets from its own switch.
    public sealed class PackSwitchTests
    {
        private const string Id = "k3-test", Parent = "k3-parent";
        private const string Facing = "\"combat\": { \"facing\": true }";
        private const string Standing = "\"ai\": { \"standingOrders\": true }";
        private const long Accel = 9000, Decel = 18000;

        private static string Inertia(string rates) => "\"movement\": { \"inertia\": " + rates + " }";
        private static string Rates(long accel, long decel) => "{ \"accelMmPerS2\": " + accel + ", \"decelMmPerS2\": " + decel + " }";

        // The rwr-m6 layer as it is, or with one more section; parent: a layer of that section between the two.
        private static ContentPack Pack(string section = null, string parent = null)
        {
            var layers = new Dictionary<string, string>
            {
                [Id] = "{ \"id\": \"" + Id + "\", \"extends\": \"" + (parent == null ? "rwr-m6" : Parent) + "\"" + (section == null ? "" : ", " + section) + " }"
            };
            if (parent != null) layers[Parent] = "{ \"id\": \"" + Parent + "\", \"extends\": \"rwr-m6\", " + parent + " }";
            return ContentLoader.Load(new MemoryContentSource(layers, FileContentSource.Rwr()), Id);
        }

        private static bool Carries(WorldRecipe recipe, string optionId)
        {
            foreach (IWorldOption option in recipe.Options)
                if (option.OptionId == optionId) return true;
            return false;
        }

        // The two worlds have one hash now and after each of `steps` steps.
        private static void SameAtEveryTick(SimSession a, SimSession b, int steps)
        {
            Assert.That(b.StateHash, Is.EqualTo(a.StateHash), "tick " + a.Tick);
            for (int i = 0; i < steps; i++)
            {
                a.StepOnce(); b.StepOnce();
                Assert.That(b.StateHash, Is.EqualTo(a.StateHash), "tick " + a.Tick);
            }
        }

        // One unit walks a line toward +X. Under a pack that says facing it carries the facing component and turns the way
        // it walks, exactly as under the same pack without the field and with the recipe's switch.
        [Test]
        public void FacingFromThePackIsTheRecipeSwitch()
        {
            WorldRecipe line = InertiaWorlds.Line(25000, 12000);
            WorldRecipe byPack = line.WithPack(Pack(Facing)), bySwitch = line.WithPack(Pack()).WithFacing(), without = line.WithPack(Pack());
            Assert.That((byPack.Facing, bySwitch.Facing, without.Facing), Is.EqualTo((true, true, false)));
            Assert.That(Carries(byPack, "world.facing"), Is.False, "the pack says it; the recipe carries no switch");
            Assert.That(line.WithPack(Pack("\"combat\": { \"facing\": false }")).Facing, Is.False, "false is off");

            SimSession a = InertiaWorlds.Build(byPack, InertiaWorlds.FlatMap()), b = InertiaWorlds.Build(bySwitch, InertiaWorlds.FlatMap());
            Assert.That(InertiaWorlds.Build(without, InertiaWorlds.FlatMap()).StateHash, Is.Not.EqualTo(a.StateHash), "a world without facing is another world");
            Assert.That(M3TestWorld.Component<UnitFacing>(a), Is.EqualTo(UnitFacing.Default));
            SameAtEveryTick(a, b, 60);
            UnitFacing facing = M3TestWorld.Component<UnitFacing>(a);
            Assert.That((facing.X.Raw > 0, facing.Z.Raw), Is.EqualTo((true, 0L)), "it faces +X, the way it walked");
        }

        private const string FacingLine = "\"combat\": { \"facing\": true },";

        // The block AI has no field of its own. It is on when the units face a direction and a shield of the combat pack gives
        // the AI a chance above zero; the ancient field under its layer alone is the field the recipe's two switches made.
        [Test]
        public void AShieldWithAChanceAndFacingIsTheBlockAi()
        {
            ContentPack field = Anc2.Load();
            ContentPack unfaced = Anc2.Load(Anc2.Replace("ancient-field", FacingLine, ""));
            ContentPack unraised = Anc2.Load(Anc2.Replace("ancient", "\"aiRaisePermille\": 500", "\"aiRaisePermille\": 0"));
            var bare = new WorldRecipe(41, MoverKind.InterimCrowd, new[] { UnitSpawn.OfType(1, field, "ancient.guard", 1, 1, 30000, 22000) },
                Array.Empty<BridgeCommand>());
            WorldRecipe Of(ContentPack pack) => bare.WithCombat(pack.BuildCombat()).WithPack(pack);
            Assert.That((Of(field).BlockAi, Of(unfaced).BlockAi, Of(unraised).BlockAi), Is.EqualTo((true, false, false)),
                "facing and a chance; no facing; a chance of zero");
            Assert.That(Of(field).BlockAiAsked, Is.False, "nobody asked: the numbers are the switch");
            Assert.That(Of(unfaced).WithFacing().BlockAi, Is.True, "the recipe's facing switch counts as the pack's field");
            Assert.That(bare.WithPack(field).BlockAi, Is.False, "the chance is read from the combat pack: without one there is no shield");
            Assert.That(Of(unraised).WithBlockAi().WithPlayer(1).BlockAi, Is.True, "a recipe built by hand may still ask, and a later option keeps the ask");

            M6World byPack = Anc2.World(field, switches: false), bySwitch = Anc2.World(unfaced);
            Assert.That(byPack.System<BlockAiSystem>(), Is.Not.Null);
            SameAtEveryTick(byPack.Session, bySwitch.Session, 240);
            Assert.That(Anc2.World(unfaced, switches: false).System<BlockAiSystem>(), Is.Null, "a block goes up toward where the unit faces");
            Assert.That(Anc2.World(unraised, switches: false).System<BlockAiSystem>(), Is.Null);
        }

        // Squad 1 walks and is told to hold at tick 20. Under a pack that says standing orders it stops and stays, exactly as
        // under the same pack without the field and with the recipe's switch.
        [Test]
        public void StandingOrdersFromThePackAreTheRecipeSwitch()
        {
            var units = new[] { M6World.U(1, 1, 10, 10, speed: 6000), M6World.U(2, 1, 12, 10, speed: 6000), M6World.U(3, 2, 70, 35, speed: 6000) };
            WorldRecipe squad = M6World.RecipeOf(units, M6World.Pack());
            WorldRecipe byPack = squad.WithPack(Pack(Standing)), bySwitch = squad.WithPack(Pack()).WithStandingOrders(), without = squad.WithPack(Pack());
            Assert.That((byPack.Standing, bySwitch.Standing, without.Standing), Is.EqualTo((true, true, false)));
            Assert.That(Carries(byPack, "world.standing"), Is.False, "the pack says it; the recipe carries no switch");
            Assert.That(squad.WithPack(Pack("\"ai\": { \"standingOrders\": false }")).Standing, Is.False, "false is off");

            M6World a = new M6World(byPack), b = new M6World(bySwitch);
            Assert.That(new M6World(without).Hash, Is.Not.EqualTo(a.Hash), "a world without standing orders is another world");
            foreach (M6World w in new[] { a, b })
            {
                w.Session.Enqueue(BridgeCommand.MoveTo(1, 1, 40000, 10000));
                w.Session.Enqueue(BridgeCommand.Hold(20, 1));
            }
            SameAtEveryTick(a.Session, b.Session, 19);
            Assert.That(a.Read<MoveIntent>(1).Active, Is.True, "tick 19: the squad is under way");
            SameAtEveryTick(a.Session, b.Session, 41);
            Assert.That(a.Read<MoveIntent>(1).Active, Is.False, "tick 60: it holds");
            Assert.That(a.Read<SquadStanding>(1).Kind, Is.EqualTo(b.Read<SquadStanding>(1).Kind));
        }

        // One unit walks a line. Under a pack with inertia it speeds up and brakes by the pack's two rates, exactly as under
        // the same pack without the field and with the option a recipe built by hand adds. A world takes its inertia from one
        // of the two.
        [Test]
        public void InertiaFromThePackIsTheRecipeOption()
        {
            WorldRecipe line = InertiaWorlds.Line(25000, 12000);
            WorldRecipe byPack = line.WithPack(Pack(Inertia(Rates(Accel, Decel)))), without = line.WithPack(Pack());
            WorldRecipe byOption = InertiaWorlds.Open(without, Accel, Decel);
            Assert.That((byPack.Motion, byOption.Motion, without.Motion), Is.EqualTo((true, true, false)));
            Assert.That((Carries(byPack, "world.motion"), Carries(byPack, "world.inertia")), Is.EqualTo((false, false)),
                "the pack says it; the recipe carries neither the switch nor the option");

            ArgumentException both = Assert.Throws<ArgumentException>(() => InertiaWorlds.Build(InertiaWorlds.Open(byPack, Accel, Decel), InertiaWorlds.FlatMap()));
            Assert.That(both.Message, Does.StartWith("The recipe adds the inertia option and its content pack '" + Id + "' has movement.inertia. "));
            Assert.That(both.ParamName, Is.EqualTo("recipe"));

            SimSession a = InertiaWorlds.Build(byPack, InertiaWorlds.FlatMap()), b = InertiaWorlds.Build(byOption, InertiaWorlds.FlatMap());
            SimSession c = InertiaWorlds.Build(without, InertiaWorlds.FlatMap());
            Assert.That(c.StateHash, Is.Not.EqualTo(a.StateHash), "a world without inertia is another world");
            SameAtEveryTick(a, b, 6);
            for (int i = 0; i < 6; i++) c.StepOnce();
            Assert.That(M3TestWorld.Position(a).X.Raw, Is.LessThan(M3TestWorld.Position(c).X.Raw), "tick 6: still speeding up, behind the unit without inertia");
            SameAtEveryTick(a, b, 144);
            for (int i = 0; i < 144; i++) c.StepOnce();
            Assert.That(M3TestWorld.Position(a), Is.EqualTo(M3TestWorld.Position(c)), "tick 150: both stand at the end of the line");
        }

        // What the loader makes of the three fields. A rate is 0 (that half stays off) or within the limits the option has.
        [Test]
        public void TheLoaderPublishesTheThreeFields()
        {
            ContentPack plain = Pack();
            Assert.That(plain.SectionOrNull<BodyFacing>(), Is.Null);
            Assert.That(plain.Section<AiRules>().StandingOrders, Is.False);
            Assert.That(plain.SectionOrNull<MovementRules>(), Is.Null);
            Assert.That(Pack(Facing).SectionOrNull<BodyFacing>(), Is.Not.Null);
            Assert.That(Pack("\"combat\": { \"facing\": false }").SectionOrNull<BodyFacing>(), Is.Null);
            Assert.That(Pack("\"combat\": { \"facing\": false }", Facing).SectionOrNull<BodyFacing>(), Is.Null, "a layer above turns it off");
            Assert.That(Pack(Standing).Section<AiRules>().StandingOrders, Is.True);
            Assert.That(Pack("\"ai\": { \"standingOrders\": false }", Standing).Section<AiRules>().StandingOrders, Is.False, "a layer above turns it off");

            MovementRules movement = Pack(Inertia(Rates(Accel, Decel))).Section<MovementRules>();
            Assert.That((movement.Inertia.AccelMmPerS2, movement.Inertia.DecelMmPerS2, movement.Slope), Is.EqualTo((Accel, Decel, (SlopeTable)null)));
            foreach ((long accel, long decel) in new[] { (0L, InertiaRates.MinMmPerS2), (InertiaRates.MaxMmPerS2, 0L), (InertiaRates.MinMmPerS2, InertiaRates.MaxMmPerS2) })
            {
                InertiaRates rates = Pack(Inertia(Rates(accel, decel))).Section<MovementRules>().Inertia;
                Assert.That((rates.AccelMmPerS2, rates.DecelMmPerS2), Is.EqualTo((accel, decel)));
                Assert.DoesNotThrow(() => new InertiaOption(accel, decel));
            }
            Assert.That((InertiaRates.MinMmPerS2, InertiaRates.MaxMmPerS2), Is.EqualTo((100L, 1000000L)));
            InertiaRates whole = Pack(Inertia(Rates(InertiaRates.MinMmPerS2, 0)), Inertia(Rates(Accel, Decel))).Section<MovementRules>().Inertia;
            Assert.That((whole.AccelMmPerS2, whole.DecelMmPerS2), Is.EqualTo((InertiaRates.MinMmPerS2, 0L)), "a layer above replaces both rates");
        }

        [TestCase("\"combat\": { \"facing\": 1 }", "combat field 'facing': must be true or false")]
        [TestCase("\"ai\": { \"standingOrders\": \"yes\" }", "ai field 'standingOrders': must be true or false")]
        [TestCase("\"movement\": { \"inertia\": 5 }", "movement field 'inertia': must be an object")]
        [TestCase("\"movement\": { \"inertia\": { \"accelMmPerS2\": 0, \"decelMmPerS2\": 0 } }", "movement.inertia: 'accelMmPerS2' and 'decelMmPerS2' are both 0; leave 'inertia' out for no inertia")]
        [TestCase("\"movement\": { \"inertia\": { \"accelMmPerS2\": 99, \"decelMmPerS2\": 0 } }", "movement.inertia field 'accelMmPerS2': is 99, allowed 0 or 100 to 1000000")]
        [TestCase("\"movement\": { \"inertia\": { \"accelMmPerS2\": 0, \"decelMmPerS2\": 1 } }", "movement.inertia field 'decelMmPerS2': is 1, allowed 0 or 100 to 1000000")]
        [TestCase("\"movement\": { \"inertia\": { \"accelMmPerS2\": 1000001, \"decelMmPerS2\": 0 } }", "movement.inertia field 'accelMmPerS2': out of range [0, 1000000]")]
        [TestCase("\"movement\": { \"inertia\": { \"accelMmPerS2\": 0, \"decelMmPerS2\": -100 } }", "movement.inertia field 'decelMmPerS2': out of range [0, 1000000]")]
        [TestCase("\"movement\": { \"inertia\": { \"accelMmPerS2\": 9000 } }", "movement.inertia is missing field 'decelMmPerS2'")]
        [TestCase("\"movement\": { \"inertia\": { \"decelMmPerS2\": 9000 } }", "movement.inertia is missing field 'accelMmPerS2'")]
        [TestCase("\"movement\": { \"inertia\": { \"accelMmPerS2\": 9000, \"decelMmPerS2\": 0, \"turn\": 1 } }", "movement.inertia field 'turn': unknown field name (misspelled?)")]
        [TestCase("\"movement\": { \"inertia\": { \"accelMmPerS2\": 9000.5, \"decelMmPerS2\": 0 } }", "movement.inertia field 'accelMmPerS2': must be an integer")]
        public void TheLoaderRefusesAFieldItCannotRead(string section, string why)
        {
            ContentException refused = Assert.Throws<ContentException>(() => Pack(section));
            Assert.That(refused.Message, Is.EqualTo("content pack '" + Id + "' layer '" + Id + "' " + why));
        }
    }
}
