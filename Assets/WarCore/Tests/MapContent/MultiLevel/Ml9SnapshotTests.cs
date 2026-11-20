using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml7Worlds;
using static WarCore.Tests.RespawnWorld;

namespace WarCore.Tests
{
    // ML-9 acceptance 9 (invariant 3): a plate match with both sides upstairs and downstairs, the AI on, resumes to the same
    // hashes; a map that changes only the plate's thickness (the navigation and its key unchanged) refuses the snapshot through
    // the plates.field key; a world without plates has no such key.
    public sealed class Ml9SnapshotTests
    {
        // 80 x 40: plate 1 flat on cells x 40..43, z 30..33, top 3 m, cutout (42, 31); thickness as given.
        private static byte[] Arena(long thickRaw) => Ml7Worlds.Bytes(Ml4Maps.Make(0, 80, 40, null,
            new[] { Ml4Maps.Flat(1, 40, 30, 4, 4, 3 * M, thickRaw) }, new[] { Ml4Maps.Cut(1, 1, 42, 31, 1, 1) }));

        private static UnitSpawn Gun(ulong key, int team, long xMm, long zMm, int layer) =>
            new UnitSpawn(key, 0, team, xMm, zMm, 1, team, 0, false, layer: layer);

        // Each team has two on the first floor and two on the ground (one under the floor, one outside it).
        private static readonly UnitSpawn[] Sides =
        {
            Gun(1, 1, 40500, 30500, 1), Gun(2, 1, 43500, 30500, 1), Gun(3, 1, 41500, 32500, 0), Gun(4, 1, 36000, 25000, 0),
            Gun(5, 2, 40500, 33500, 1), Gun(6, 2, 43500, 33500, 1), Gun(7, 2, 43500, 31500, 0), Gun(8, 2, 48000, 36000, 0),
        };

        private static M6World World(long thickRaw) => new M6World(
            Recipe(Sides, Pack(Points(), Bases(new SpawnAnchor(1, P(5, 30))), 0, interval: 30), false).WithPlatesPreview(), Arena(thickRaw));

        // The bridge hands the map's plates to combat: on the first floor and straight under it nobody sees the other.
        [Test]
        public void TheBridgeHandsThePlatesToCombat()
        {
            M6World w = World(M / 2);
            Assert.That(w.Combat.HasPlates, Is.True);
            Assert.That((w.Combat.Visible(w.Unit(3), w.Unit(1)), w.Combat.Visible(w.Unit(1), w.Unit(3))), Is.EqualTo((false, false)), "through the floor");
            Assert.That(w.Combat.Visible(w.Unit(1), w.Unit(5)), Is.True, "on the same floor");
        }

        [Test]
        public void APlateBattleResumesToTheSameHashes()
        {
            M6World a = World(M / 2);
            int fires = 0;
            for (int i = 0; i < 150; i++) { a.Step(); fires += a.Combat.Fires.Length; }
            byte[] snapshot = a.Sim.SaveRetainedSnapshot();
            var expected = new List<ulong>();
            for (int i = 0; i < 150; i++) { a.Step(); fires += a.Combat.Fires.Length; expected.Add(a.Hash); }
            Assert.That(fires, Is.GreaterThan(0), "the battle fires");
            M6World b = World(M / 2);
            b.Sim.RestoreRetainedSnapshot(snapshot);
            var resumed = new List<ulong>();
            for (int i = 0; i < 150; i++) { b.Step(); resumed.Add(b.Hash); }
            Assert.That(resumed, Is.EqualTo(expected));
        }

        // The review's case: 0.5 m and 0.8 m thick leave 2.5 m and 2.2 m under the floor, both walkable, so the navigation and
        // the terrain tables are the same; only plates.field tells the maps apart.
        [Test]
        public void OnlyTheThicknessChangedRefusesTheSnapshot()
        {
            WcMap half = WcMapReader.Read(Arena(M / 2)), thick = WcMapReader.Read(Arena(4 * M / 5));
            Assert.That(WcMapLayeredNav.Build(thick, null, thick.ContentId).Digest, Is.EqualTo(WcMapLayeredNav.Build(half, null, half.ContentId).Digest));
            Assert.That(TerrainDerivation.Derive(thick, null, thick.ContentId).Digest, Is.EqualTo(TerrainDerivation.Derive(half, null, half.ContentId).Digest));
            Assert.That(WcMapPlateField.Build(thick).Digest(), Is.Not.EqualTo(WcMapPlateField.Build(half).Digest()));
            M6World a = World(M / 2);
            a.Step(10);
            byte[] snapshot = a.Sim.SaveRetainedSnapshot();
            Assert.Catch<SimSnapshotException>(() => World(4 * M / 5).Sim.RestoreRetainedSnapshot(snapshot));
            Assert.DoesNotThrow(() => World(M / 2).Sim.RestoreRetainedSnapshot(snapshot));
        }

        [Test]
        public void AWorldWithoutPlatesHasNoFieldKey()
        {
            M6World w = new M6World(Recipe(new[] { Gun(1, 1, 30000, 20000, 0), Gun(2, 2, 50000, 20000, 0) },
                Pack(Points(), Bases(new SpawnAnchor(1, P(5, 30))), 0), false).WithPlatesPreview(), Ml7Worlds.Arena(false));
            Assert.Throws<SimBoundaryException>(() => w.Sim.StableContent.Key("plates.field"));
            Assert.That(w.Sim.StableContent.Digest, Is.EqualTo(new M6World(Recipe(new[] { Gun(1, 1, 30000, 20000, 0), Gun(2, 2, 50000, 20000, 0) },
                Pack(Points(), Bases(new SpawnAnchor(1, P(5, 30))), 0), false), Ml7Worlds.Arena(false)).Sim.StableContent.Digest), "the same as without the preview");
        }
    }
}
