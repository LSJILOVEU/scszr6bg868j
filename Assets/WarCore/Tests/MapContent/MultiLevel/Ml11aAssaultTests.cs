using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml11aWorld;

namespace WarCore.Tests
{
    // ML-11a acceptance 14: the assault demo map (PlateAssaultMap), played by the AI alone under the m6 layer.
    public sealed class Ml11aAssaultTests
    {
        private static readonly MapBakeResult Baked = MapBake.Run(PlateAssaultMap.DescBytes());
        private static WorldRecipe Recipe() => MapRecipe.From(Baked, RwrPack.Load("rwr-m6")).WithPlatesPreview();
        private static M6World Battle() => new M6World(Recipe(), Baked.WcMapBytes);

        // The description passes the gate; the first floor is joined to the ground by the stair's two ends only, so the point
        // upstairs is walked to by the stair or not at all.
        [Test]
        public void TheHouseIsEnteredByTheStairOnly()
        {
            Assert.That(MapGate.Check(PlateAssaultMap.DescBytes(), RwrPack.Load("rwr-m6")), Is.Empty);
            WcMap map = WcMapReader.Read(Baked.WcMapBytes);
            LayeredNav nav = WcMapLayeredNav.Build(map, null, map.ContentId);
            var seams = new List<(int Layer, int X, int Z, int ToLayer, int ToX, int ToZ)>();
            for (int n = 0; n < nav.Count; n++)
                for (int k = 0; k < nav.SeamCount(n); k++)
                {
                    int other = nav.Seam(n, k);
                    if (n < other) seams.Add((nav.LayerOf(n), nav.XOf(n), nav.ZOf(n), nav.LayerOf(other), nav.XOf(other), nav.ZOf(other)));
                }
            Assert.That(seams, Is.EquivalentTo(new[]
            {
                (0, 16, 11, 1, 17, 11), (0, 16, 12, 1, 17, 12), (1, 20, 11, 2, 21, 11), (1, 20, 12, 2, 21, 12),
            }));
            WorldRecipe recipe = Recipe();
            ControlPointDef upstairs = recipe.Match.Points[PlateAssaultMap.UpstairsPoint];
            Assert.That((upstairs.Layer, upstairs.InitialOwner), Is.EqualTo((2, 2)));
            Assert.That(recipe.Units.ToArray().Count(u => u.Team == 2 && u.Layer == 2), Is.EqualTo(3), "defenders start upstairs");
            Assert.That(recipe.Units.ToArray().Count(u => u.Team == 1), Is.EqualTo(8));
        }

        // 14a. Without team 2 on the map (and a match that never ends), team 1's eight soldiers walk in by the west door, up the
        // stair and take the point upstairs, then go down and take the east point. The ticks are this package's baseline.
        [Test]
        public void TheSquadTakesThePointUpstairs()
        {
            WorldRecipe full = Recipe();
            UnitSpawn[] squad = full.Units.ToArray().Where(u => u.Team == 1).ToArray();
            WorldRecipe recipe = M6World.Endless(new WorldRecipe(full.Seed, MoverKind.InterimCrowd, squad, Array.Empty<BridgeCommand>())
                .WithCombat(full.Combat).WithMatch(full.Match).WithCrowdPass(CrowdPassKind.TeamIdle).WithPlatesPreview());
            var w = new M6World(recipe, Baked.WcMapBytes);
            var upstairs = new HashSet<ulong>();
            int taken = -1, east = -1;
            for (int t = 1; t <= 900; t++)
            {
                w.Step();
                if (taken < 0 && w.Capture.Points[PlateAssaultMap.UpstairsPoint].Owner == 1) taken = t;
                if (east < 0 && w.Capture.Points[2].Owner == 1) east = t;
                foreach (UnitSpawn unit in squad)
                {
                    Assert.That(Failed(w, unit.Key), Is.False, "unit " + unit.Key + " tick " + t);
                    if (Layer(w, unit.Key) == 2) upstairs.Add(unit.Key);
                }
            }
            Assert.That(upstairs.Count, Is.EqualTo(8), "every soldier has been on the first floor");
            Assert.That((taken, east), Is.EqualTo((393, 810)));
        }

        // 14b. The whole battle (seed 41): at every tick every living soldier stands on a walkable cell of its layer, no walk
        // fails, the battle ends, and the same seed gives the same hashes.
        [Test]
        public void TheBattleKeepsEveryoneOnWalkableCells()
        {
            WcMap map = WcMapReader.Read(Baked.WcMapBytes);
            LayeredNav nav = WcMapLayeredNav.Build(map, null, map.ContentId);
            M6World w = Battle(), again = Battle();
            UnitSpawn[] units = w.Recipe.Units.ToArray();
            var upstairs = new HashSet<ulong>();
            int t = 0;
            for (; t < 3000 && w.Match.Winner == 0; t++)
            {
                w.Step(); again.Step();
                Assert.That(again.Hash, Is.EqualTo(w.Hash), "tick " + t);
                foreach (UnitSpawn unit in units)
                {
                    if (w.Dead(unit.Key)) continue;
                    Assert.That(Failed(w, unit.Key), Is.False, "unit " + unit.Key + " tick " + t);
                    SimVec3 at = At(w, unit.Key);
                    int layer = Layer(w, unit.Key);
                    Assert.That(nav.TryNode(layer, (int)(at.X.Raw / M), (int)(at.Z.Raw / M), out int node) && nav.IsPassable(node), Is.True,
                        "unit " + unit.Key + " tick " + t + " layer " + layer);
                    if (unit.Team == 1 && layer == 2) upstairs.Add(unit.Key);
                }
            }
            Assert.That(w.Match.Winner, Is.Not.EqualTo(0), "the battle ends");
            Assert.That(upstairs, Is.Not.Empty, "an attacker reaches the first floor");
        }
    }
}
