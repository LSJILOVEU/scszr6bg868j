using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests.Dash
{
    // The optional unit-type fields dashDistance (mm), dashTicks and dashCooldown (ticks), how they reach a world, the worlds a
    // dash is refused in, and (DASH-T) a terrain world that takes one.
    public sealed class DashContentTests
    {
        internal static ContentPack Load(string fields) =>
            ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
            {
                ["m7a-pack"] = "{\"id\":\"m7a-pack\",\"extends\":\"rwr-melee\",\"unitTypes\":["
                    + "{\"id\":\"runner\",\"weapon\":\"rwr.weapon-2\",\"melee\":\"rwr.sword-1\"," + fields + "}]}"
            }, FileContentSource.Rwr()), "m7a-pack");

        [Test]
        public void Fields_ReachTheUnitAndTheWorld()
        {
            ContentPack pack = Load("\"speed\":6000,\"dashDistance\":20000,\"dashTicks\":10,\"dashCooldown\":60");
            Assert.That(pack.Section<UnitDashes>().Of("runner"), Is.EqualTo(new UnitDash(20000, 10, 60)));
            UnitSpawn spawn = UnitSpawn.OfType(1, pack, "runner", 1, 1, 5500, 12500);
            Assert.That((spawn.HasDash, spawn.DashDistanceMm, spawn.DashTicks, spawn.DashCooldownTicks), Is.EqualTo((true, 20000L, 10, 60)));
            SimSession s = DashWorlds.World(new[] { spawn }, DashWorlds.Map());
            Assert.That(M3TestWorld.Component<DashSpec>(s), Is.EqualTo(new DashSpec(SimFix.FromInt(20), 10, 60)));
            Assert.That(DashWorlds.State(s), Is.EqualTo(DashState.Initial));
            Assert.That(DashWorlds.Dash(s, DashWorlds.PlusX), Is.True);
        }

        [TestCase("\"dashDistance\":20000", "dashDistance", "written together")]
        [TestCase("\"dashTicks\":10,\"dashCooldown\":60", "dashTicks", "written together")]
        [TestCase("\"dashDistance\":0,\"dashTicks\":10,\"dashCooldown\":60", "dashDistance", "out of range [1, 100000]")]
        [TestCase("\"dashDistance\":20001,\"dashTicks\":2,\"dashCooldown\":60", "dashDistance", "out of range [1, 20000]")]
        [TestCase("\"dashDistance\":100001,\"dashTicks\":300,\"dashCooldown\":300", "dashDistance", "out of range [1, 100000]")]
        [TestCase("\"dashDistance\":2000,\"dashTicks\":0,\"dashCooldown\":60", "dashTicks", "out of range [1, 300]")]
        [TestCase("\"dashDistance\":2000,\"dashTicks\":301,\"dashCooldown\":400", "dashTicks", "out of range [1, 300]")]
        [TestCase("\"dashDistance\":2000,\"dashTicks\":10,\"dashCooldown\":9", "dashCooldown", "out of range [10, 18000]")]
        [TestCase("\"dashDistance\":2000,\"dashTicks\":10,\"dashCooldown\":18001", "dashCooldown", "out of range [10, 18000]")]
        public void BadFields_NamePackEntryAndField(string fields, string field, string why)
        {
            var error = Assert.Throws<ContentException>(() => Load("\"speed\":6000," + fields));
            Assert.That(error.Message, Does.Contain("m7a-pack"));
            Assert.That(error.Message, Does.Contain("runner"));
            Assert.That(error.Message, Does.Contain("field '" + field + "'"));
            Assert.That(error.Message, Does.Contain(why));
        }

        [Test]
        public void Bounds_AreAccepted()
        {
            Assert.That(Load("\"speed\":6000,\"dashDistance\":100000,\"dashTicks\":10,\"dashCooldown\":18000").Section<UnitDashes>().Of("runner"),
                Is.EqualTo(new UnitDash(100000, 10, 18000)));
            Assert.That(Load("\"speed\":6000,\"dashDistance\":1,\"dashTicks\":300,\"dashCooldown\":300").Section<UnitDashes>().Of("runner"),
                Is.EqualTo(new UnitDash(1, 300, 300)));
            Assert.That(new DashSpec(SimFix.FromInt(100), 10, 18000).Distance, Is.EqualTo(SimFix.FromInt(100)));
            Assert.That(() => new DashSpec(SimFix.FromInt(100) + SimFix.FromRaw(1), 10, 18000), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new DashSpec(SimFix.FromInt(20) + SimFix.FromRaw(1), 2, 60), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new UnitSpawn(1, 0, 1, 0, 0, 6000).WithDash(20001, 2, 60), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new UnitSpawn(1, 0, 1, 0, 0, 6000).WithDash(2000, 10, 9), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void OtherMovers_AreRefused()
        {
            var units = new[] { DashWorlds.Dasher(1, 5500, 12500) };
            foreach (MoverKind mover in new[] { MoverKind.Interim, MoverKind.InterimPath })
                Assert.That(() => WorldBuilder.Build(new WorldRecipe(41, mover, units, Array.Empty<BridgeCommand>()), DashWorlds.Map(), false),
                    Throws.ArgumentException.With.Message.Contains("crowd mover"), mover.ToString());
        }

        // DASH-T replaces M7a's refusal: the same hill (1/16 along x) builds with a dasher, and the standard dash (20 m in 10 ticks)
        // covers its 20 m along +X on the ground, y = x / 16 at every tick (the field's points are i * 4096 raw).
        [Test]
        public void TerrainWorld_IsBuiltAndDashes()
        {
            byte[] hill = WcMapWriter.Write(Wp23.Make(80, 24, 0, 6 * DashWorlds.S, null, 4 * DashWorlds.S, (i, j) => i * (int)(DashWorlds.S / 16)));
            var recipe = new WorldRecipe(41, MoverKind.InterimCrowd, new[] { DashWorlds.Dasher(1, 5500, 12500) }, Array.Empty<BridgeCommand>())
                .WithPack(Wp23.Pack());
            SimSession s = null;
            Assert.DoesNotThrow(() => s = WorldBuilder.Build(recipe, hill, false));
            Assert.That(DashWorlds.Dash(s, DashWorlds.PlusX), Is.True);
            List<SimVec3> track = DashWorlds.Track(s, DashWorlds.Ticks);
            for (int k = 1; k <= DashWorlds.Ticks; k++)
            {
                Assert.That(track[k].X.Raw - track[k - 1].X.Raw, Is.EqualTo(2 * DashWorlds.S), "step " + k);
                Assert.That(track[k].Y.Raw, Is.EqualTo(track[k].X.Raw / 16), "step " + k + " on the ground");
            }
            Assert.That(DashWorlds.State(s).Ticks, Is.EqualTo(DashWorlds.Ticks));
        }
    }
}
