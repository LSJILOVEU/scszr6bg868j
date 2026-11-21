using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // BODY-1 B3, B5, B6: whole matches on the shipped body layers (the pack of a shipped map swapped in memory), snapshots
    // across the body, and the wall-height report on the shipped maps.
    public sealed class Body1MatchTests
    {
        // BEHAVIOUR BASELINE, first recorded by BODY-1: the state hash at tick 600 of the three-lane map (seed 41) with the pack
        // "rwr-body". A change of play rules may re-record it. Two platforms must agree on it.
        internal const ulong LanesBodyHashAt600 = 0x3684B19A3921F37EUL;

        private static byte[] LanesBytes(string layer) => Body1Maps.WithPack(Body1Maps.Lanes, "rwr-m6", layer);

        private static M6World LanesWorld(string layer)
        {
            MapBakeResult bake = MapBake.Run(LanesBytes(layer));
            Assert.That(bake.PackId, Is.EqualTo(layer));
            return new M6World(MapRecipe.From(bake, RwrPack.Load(bake.PackId)), bake.WcMapBytes);
        }

        // B3: the swapped map passes the map gate with no problem on the body layer.
        [Test]
        public void Lanes_PassTheGateWithTheBodyLayer() =>
            Assert.That(MapGate.Check(LanesBytes("rwr-body"), RwrPack.Load("rwr-body")), Is.Empty);

        // B3: 600 ticks, twice, equal at every tick; the world differs from the one with no body at tick 600; the hash at 600 is the baseline.
        [Test]
        public void Lanes_RunTwiceGiveTheSameHashAtEveryTick_AndNotTheNoBodyOne()
        {
            M6World a = LanesWorld("rwr-body"), b = LanesWorld("rwr-body"), plain = new M6World(
                MapRecipe.From(MapBake.Run(Body1Maps.WithPack(Body1Maps.Lanes, "rwr-m6", "rwr-m6")), RwrPack.Load("rwr-m6")),
                MapBake.Run(Body1Maps.WithPack(Body1Maps.Lanes, "rwr-m6", "rwr-m6")).WcMapBytes);
            for (int tick = 1; tick <= 600; tick++)
            {
                a.Step(); b.Step(); plain.Step();
                Assert.That(b.Hash, Is.EqualTo(a.Hash), "tick " + tick);
            }
            TestContext.Out.WriteLine($"BODY1 lanes tick=600 body={a.Hash:X16} plain={plain.Hash:X16}");
            Assert.That(a.Hash, Is.Not.EqualTo(plain.Hash));
            Assert.That(a.Hash, Is.EqualTo(LanesBodyHashAt600));
        }

        // B3: a snapshot at tick 300 resumes to the same hashes up to 600, and the no-body world refuses it.
        [Test]
        public void Lanes_SnapshotAtTick300ResumesAndTheNoBodyWorldRefusesIt()
        {
            M6World a = LanesWorld("rwr-body");
            for (int i = 0; i < 300; i++) a.Step();
            byte[] snapshot = a.Sim.SaveRetainedSnapshot();
            var expected = new List<ulong>();
            for (int i = 0; i < 300; i++) { a.Step(); expected.Add(a.Hash); }
            M6World b = LanesWorld("rwr-body");
            b.Sim.RestoreRetainedSnapshot(snapshot);
            var resumed = new List<ulong>();
            for (int i = 0; i < 300; i++) { b.Step(); resumed.Add(b.Hash); }
            Assert.That(resumed, Is.EqualTo(expected));

            MapBakeResult plainBake = MapBake.Run(LanesBytes("rwr-m6"));
            var plain = new M6World(MapRecipe.From(plainBake, RwrPack.Load("rwr-m6")), plainBake.WcMapBytes);
            Assert.That(() => plain.Sim.RestoreRetainedSnapshot(snapshot), Throws.Exception);
            // and the other way round
            var plainAt300 = new M6World(MapRecipe.From(plainBake, RwrPack.Load("rwr-m6")), plainBake.WcMapBytes);
            for (int i = 0; i < 300; i++) plainAt300.Step();
            byte[] plainSnapshot = plainAt300.Sim.SaveRetainedSnapshot();
            Assert.That(() => LanesWorld("rwr-body").Sim.RestoreRetainedSnapshot(plainSnapshot), Throws.Exception);
        }

        // ---- B5: a floor plate ----

        // The 80 x 40 plate arena of the ML-9 tests: plate 1 flat on cells x 40..43, z 30..33, top 3 m (cutout at 42, 31). Each team
        // has two on the floor and two on the ground. The body layer's combat pack replaces the recipe's.
        private static UnitSpawn Gun(ulong key, int team, long xMm, long zMm, int layer) =>
            new UnitSpawn(key, 0, team, xMm, zMm, 1, team, 0, false, layer: layer);
        private static readonly UnitSpawn[] Sides =
        {
            Gun(1, 1, 40500, 30500, 1), Gun(2, 1, 43500, 30500, 1), Gun(3, 1, 41500, 32500, 0), Gun(4, 1, 36000, 25000, 0),
            Gun(5, 2, 40500, 33500, 1), Gun(6, 2, 43500, 33500, 1), Gun(7, 2, 43500, 31500, 0), Gun(8, 2, 48000, 36000, 0),
        };
        // The shipped combat pack hurts for real, so a team can be wiped out in a few ticks and the match would end; the endless rule set
        // keeps the world stepping for the whole window.
        private static M6World PlateWorld(string layer) => new M6World(M6World.Endless(
            RespawnWorld.Recipe(Sides, RespawnWorld.Pack(RespawnWorld.Points(), RespawnWorld.Bases(new SpawnAnchor(1, RespawnWorld.P(5, 30))), 0, interval: 30), false)
                .WithCombat(RwrPack.Load(layer).BuildCombat()).WithPlatesPreview()), Ml7Worlds.Arena());

        [TestCase("rwr-body")]
        [TestCase("rwr-field")]
        public void PlateWorld_BuildsRunsThreeHundredTicksAndRepeats(string layer)
        {
            M6World a = PlateWorld(layer), b = PlateWorld(layer);
            Assert.That(a.Combat.HasPlates, Is.True);
            Assert.That(a.Session, Is.Not.Null);
            // The floor still separates the layers with a body: on the floor and straight under it nobody sees the other; on one floor they do.
            Assert.That((a.Combat.Visible(a.Unit(3), a.Unit(1)), a.Combat.Visible(a.Unit(1), a.Unit(3))), Is.EqualTo((false, false)), "through the floor");
            Assert.That(a.Combat.Visible(a.Unit(1), a.Unit(5)), Is.True, "on the same floor");
            int fires = 0;
            for (int tick = 1; tick <= 300; tick++)
            {
                Assert.DoesNotThrow(() => { a.Step(); b.Step(); }, "tick " + tick);
                fires += a.Combat.Fires.Length;
                Assert.That(b.Hash, Is.EqualTo(a.Hash), "tick " + tick);
            }
            Assert.That(fires, Is.GreaterThan(0), "the battle fires");
        }

        // ---- B6: the wall-height report on the shipped maps ----

        // The four flat maps (three-lane, M6, M7, door): every wall that stands on the ground is 2 m or 3 m high, so no wall is
        // low enough to see over or to show a body above it. The report is empty.
        [TestCase(Body1Maps.Lanes)]
        [TestCase(Body1Maps.M6)]
        [TestCase(Body1Maps.M7)]
        [TestCase(Body1Maps.Door)]
        public void FlatMaps_HaveNoWallLowerThanTheBody(string path)
        {
            CombatBody body = RwrPack.Load("rwr-body").Body.Value;
            Assert.That(MapGate.WallHeights(File.ReadAllBytes(path), body), Is.Empty, path);
        }

        // Not asserted: what the report and the map gate say about the two terrain maps and the plate map with the body layer's
        // pack swapped in, for the comparison note.
        [TestCase(Body1Maps.Terrain, "rwr-terrain-demo", "rwr-field")]
        [TestCase(Body1Maps.DoorTerrain, "rwr-terrain-demo", "rwr-field")]
        [TestCase(Body1Maps.Plate, "rwr-m6", "rwr-body")]
        public void TerrainAndPlateMaps_ReportIsWrittenOut(string path, string from, string layer)
        {
            CombatBody body = RwrPack.Load(layer).Body.Value;
            byte[] swapped = Body1Maps.WithPack(path, from, layer);
            var lines = MapGate.WallHeights(swapped, body);
            var problems = MapGate.Check(swapped, RwrPack.Load(layer));
            TestContext.Out.WriteLine("BODY1 report " + path + " rows=" + lines.Count + " "
                + string.Join(" ", lines.Select(l => "(" + l.CellX + "," + l.CellZ + " top=" + l.TopAboveGroundRaw + " " + l.Band + ")"))
                + " gateProblems=" + problems.Count);
            Assert.That(lines, Is.Not.Null);
        }
    }
}
