using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // MAP-BL0 W5 (points, bases, soldiers, the recipes) and W6 (both recipes build a world; the numbers are printed).
    public sealed class WardStartTests
    {
        [Test]
        public void PointsAndBasesAreTheCardsTable()
        {
            MapDocument doc = WardBuild.Document(41);
            var points = doc.Points();
            Assert.That(points.Select(p => (p.Id, p.XMm, p.ZMm, p.RadiusMm, p.InitialOwner, p.HasSpawn, p.SpawnXMm, p.SpawnZMm, p.HasPlate, p.HasSpawnPlate)),
                Is.EqualTo(new[]
                {
                    (1UL, 80000L, 80000L, 6000, 0, true, 80000L, 80000L, false, false),
                    (2UL, 80000L, 123000L, 6000, 0, true, 80000L, 123000L, false, false),
                }));
            Assert.That(doc.Bases().Select(b => (b.Team, b.XMm, b.ZMm, b.HasPlate)),
                Is.EqualTo(new[] { (1, 4000L, 80000L, false), (2, 156000L, 80000L, false) }));
        }

        // The 1 m square formation worked out here: n soldiers in ceil(sqrt(n)) columns, centred.
        private static (long X, long Z) Slot(int rank, int n, long cx, long cz)
        {
            int cols = (int)Math.Ceiling(Math.Sqrt(n)), rows = (n + cols - 1) / cols;
            return (cx + (2 * (rank % cols) - (cols - 1)) * 500, cz + (2 * (rank / cols) - (rows - 1)) * 500);
        }

        [Test]
        public void ThirtyTwoASideOnTheFieldAndSixteenInReserve()
        {
            var units = WardBuild.Document(41).Units();
            Assert.That(units.Count, Is.EqualTo(96));
            (ulong From, string Type, int Team, bool Reserve, int Count, long X, long Z)[] groups =
            {
                (1, "rwr.soldier-1", 1, false, 32, 20000, 80000), (33, "rwr.soldier-1", 1, true, 16, 4000, 80000),
                (49, "rwr.soldier-2", 2, false, 32, 140000, 80000), (81, "rwr.soldier-2", 2, true, 16, 156000, 80000),
            };
            int i = 0;
            foreach (var g in groups)
                for (int rank = 0; rank < g.Count; rank++, i++)
                {
                    MapUnitInfo u = units[i];
                    (long x, long z) = Slot(rank, g.Count, g.X, g.Z);
                    Assert.That((u.Key, u.Type, u.Team, u.Squad, u.Reserve, u.XMm, u.ZMm, u.HasPlate),
                        Is.EqualTo((g.From + (ulong)rank, g.Type, g.Team, g.Team, g.Reserve, x, z, false)), "unit " + (i + 1));
                }
        }

        [Test]
        public void TheRecipeIsTheOpenBattlesWithKeyOneAsThePlayer()
        {
            WorldRecipe ai = WardDemo.AiRecipe(WardWorld.Baked), played = WardDemo.Recipe(WardWorld.Baked);
            Assert.That(ai.PlayerKey, Is.Null, "the AI recipe has no player");
            Assert.That(played.PlayerKey, Is.EqualTo(1UL));
            foreach (WorldRecipe r in new[] { ai, played })
            {
                Assert.That(r.AiPack, Is.Not.Null);
                Assert.That(r.AiPack.Id, Is.EqualTo("rwr-showcase"));
                Assert.That(r.Facing, Is.True);
                Assert.That(r.Seed, Is.EqualTo(41UL));
                Assert.That(r.CrowdPass, Is.EqualTo(CrowdPassKind.TeamIdle));
                Assert.That(r.Units.Length, Is.EqualTo(96));
            }
            UnitSpawn player = played.Units.ToArray().Single(u => u.Key == 1);
            int guard = WardWorld.Pack.SpawnOfUnitType("rwr.soldier-1").Block;
            Assert.That(guard, Is.Not.EqualTo(-1), "rwr.soldier-1 carries rwr.guard");
            Assert.That((player.Team, player.Reserve, player.Block), Is.EqualTo((1, false, guard)), "the player soldier carries the guard");
            Assert.That(played.Units.ToArray().Where(u => u.Team == 2).All(u => u.Block == -1), Is.True, "team 2 has no block");
            ControlPointDef[] points = played.Match.Points.ToArray();
            Assert.That(points.Select(p => (p.InitialOwner, p.HasSpawn)), Is.EqualTo(new[] { (0, true), (0, true) }));
            Assert.That(played.Match.Bases.ToArray().Select(b => (b.Team, b.Point.X.Raw, b.Point.Z.Raw)),
                Is.EqualTo(new[] { (1, 4L * 65536, 80L * 65536), (2, 156L * 65536, 80L * 65536) }));
        }

        // W6: both recipes build a world with every soldier (none taken out); the sizes are printed for the report.
        [Test]
        public void BothRecipesBuildAWorld()
        {
            foreach (bool withPlayer in new[] { false, true })
            {
                WorldRecipe recipe = withPlayer ? WardDemo.Recipe(WardWorld.Baked) : WardDemo.AiRecipe(WardWorld.Baked);
                M6World w = null;
                Assert.DoesNotThrow(() => w = WardWorld.World(recipe));
                var index = w.Sim.SpatialIndex;
                int walls = MapDocument.Open(WardWorld.File).Walls().Count;
                TestContext.WriteLine("MAPBL0 W6 player=" + withPlayer + " walls=" + walls + " soldiers=" + recipe.Units.Length + " entities=" + w.Sim.Entities.Capacity
                    + " cellTable=" + index.CellCapacity + " staticLimit=" + Math.Min(index.CellCapacity, 16384) / 2 + " occupiedCells=" + index.OccupiedCells
                    + " largeBodies=" + index.LargeBodyCount);
                Assert.That(index.OccupiedCells, Is.LessThan(index.CellCapacity), "far from the table size");
            }
        }
    }
}
