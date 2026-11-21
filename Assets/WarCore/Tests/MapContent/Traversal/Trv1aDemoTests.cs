using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml11aWorld;
using static WarCore.Tests.Trv1aMaps;

namespace WarCore.Tests
{
    // TRV-1a acceptance 5 and 6: the house on the slope (Trv1aDemo).
    public sealed class Trv1aDemoTests
    {
        // The ramp the demo describes: x 10..11, z 15..16, 65540 -> 124526 along x, 58986 thick (124526 - h(10)).
        [Test]
        public void TheRampIsTwoCellsDownTheSlope()
        {
            MapPlate p = Plate(Trv1aDemo.RampPlan(Trv1aDemo.House()));
            Assert.That((p.Cell.X, p.Cell.Z, p.Size.X, p.Size.Z, p.Slope.Axis, p.Slope.FromRaw, p.Slope.ToRaw, p.ThickRaw),
                Is.EqualTo((10, 15, 2, 2, "x", 65540L, 124526L, 58986L)));
        }

        // Acceptance 5: the gate passes with no hint; without the ramp Check finds the point on the floor out of reach and the
        // hints name the doorway's two cells and nothing else; from
        // the ground at (9, 15) to the point's cell (15, 15): the ramp's (10, 15), (11, 15), the floor's (12, 15) .. (15, 15),
        // six orthogonal steps of cost 1, 6000.
        [Test]
        public void TheRampOpensTheDoorway()
        {
            ContentPack pack = RwrPack.Load("rwr-m6");
            Assert.That(MapGate.Check(Trv1aDemo.DescBytes(), pack), Is.Empty);
            Assert.That(MapGate.Hints(Trv1aDemo.DescBytes(), pack), Is.Empty);
            byte[] house = MapDescJson.Write(Trv1aDemo.House());
            Assert.That(MapGate.Check(house, pack).Select(p => p.Code + " " + p.Id), Is.EqualTo(new[] { "GATE_UNREACHABLE base 1 point 2", "GATE_UNREACHABLE base 2 point 2" }));
            Assert.That(MapGate.Hints(house, pack).Select(h => (h.Plate, h.CellX, h.CellZ, h.Side, h.GapRaw)), Is.EqualTo(new[]
            {
                (1ul, 12, 15, MapSide.MinusX, Trv1aDemo.FloorTop - Trv1aDemo.Height(12)),
                (1ul, 12, 16, MapSide.MinusX, Trv1aDemo.FloorTop - Trv1aDemo.Height(12)),
            }));
            LayeredNav nav = Nav(Trv1aDemo.Desc());
            Assert.That(Cost(nav, Node(nav, 0, 9, 15), Node(nav, 1, 15, 15)), Is.EqualTo(6000));
            LayeredNav closed = Nav(Trv1aDemo.House());
            Assert.That(Cost(closed, Node(closed, 0, 9, 15), Node(closed, 1, 15, 15)), Is.EqualTo(-1));
        }

        // Acceptance 6, one team at a time (the other team left out, a match that never ends): the six soldiers on the field
        // walk round the house, up the ramp and take the point on the floor; every one of them stands on the floor at some
        // tick, no walk fails, and nobody stands still at the ramp for more than 90 ticks.
        [TestCase(1)]
        [TestCase(2)]
        public void EachTeamAloneWalksUpTheRamp(int team)
        {
            MapBakeResult baked = MapBake.Run(Trv1aDemo.DescBytes());
            WorldRecipe full = MapRecipe.From(baked, RwrPack.Load("rwr-m6"));
            UnitSpawn[] squad = full.Units.ToArray().Where(u => u.Team == team && !u.Reserve).ToArray();
            WorldRecipe recipe = M6World.Endless(new WorldRecipe(full.Seed, MoverKind.InterimCrowd, squad, Array.Empty<BridgeCommand>())
                .WithCombat(full.Combat).WithMatch(full.Match).WithCrowdPass(CrowdPassKind.TeamIdle).WithTerrainRules(full.TerrainPack).WithPlatesPreview());
            var w = new M6World(recipe, baked.WcMapBytes);
            var up = new HashSet<ulong>();
            var still = new Dictionary<ulong, (SimVec3 At, int Since)>();
            int taken = -1, worstStill = 0;
            for (int t = 1; t <= 900; t++)
            {
                w.Step();
                if (taken < 0 && w.Capture.Points[1].Owner == team) taken = t;
                foreach (UnitSpawn unit in squad)
                {
                    Assert.That(Failed(w, unit.Key), Is.False, "unit " + unit.Key + " tick " + t);
                    if (Layer(w, unit.Key) == 1) up.Add(unit.Key);
                    worstStill = Math.Max(worstStill, StillAtTheRamp(w, unit.Key, t, still));
                }
            }
            TestContext.WriteLine("team " + team + " takes the point at tick " + taken + ", longest still at the ramp " + worstStill);
            Assert.That(squad.Length, Is.EqualTo(6));
            Assert.That(up.Count, Is.EqualTo(6), "every soldier has stood on the floor");
            Assert.That(taken, Is.GreaterThan(0), "the point on the floor is taken");
            Assert.That(worstStill, Is.LessThanOrEqualTo(90));
        }

        // Ticks a unit has stood still on the ramp or the two ground cells at its foot (0 elsewhere or when it moved).
        private static int StillAtTheRamp(M6World w, ulong key, int t, Dictionary<ulong, (SimVec3 At, int Since)> still)
        {
            SimVec3 at = At(w, key);
            int layer = Layer(w, key), cx = (int)(at.X.Raw / Trv1aDemo.M), cz = (int)(at.Z.Raw / Trv1aDemo.M);
            bool mouth = (layer == 2 || (layer == 0 && cx == 9)) && (cz == 15 || cz == 16);
            if (!mouth || !still.TryGetValue(key, out var s) || !s.At.Equals(at)) { still[key] = (at, t); return 0; }
            return t - s.Since;
        }

        // Acceptance 6: the AI plays the whole battle (seed 41, m6 rules, 6 soldiers and 18 reserves a side). Soldiers walk
        // up the ramp onto the floor, the point changes owner, no walk fails, nobody alive stands still on the ramp or the two
        // ground cells at its foot for more than 90 ticks, and the same seed gives the same hashes.
        [Test]
        public void BothTeamsWalkUpTheRamp()
        {
            MapBakeResult baked = MapBake.Run(Trv1aDemo.DescBytes());
            WorldRecipe recipe = MapRecipe.From(baked, RwrPack.Load("rwr-m6")).WithPlatesPreview();
            M6World w = new M6World(recipe, baked.WcMapBytes), again = new M6World(recipe, baked.WcMapBytes);
            UnitSpawn[] units = recipe.Units.ToArray();
            var onFloor = new HashSet<int>();
            var still = new Dictionary<ulong, (SimVec3 At, int Since)>();
            int flips = 0, owner = w.Capture.Points[1].Owner, worstStill = 0, t = 0;
            for (; t < 3000 && w.Match.Winner == 0; t++)
            {
                w.Step(); again.Step();
                Assert.That(again.Hash, Is.EqualTo(w.Hash), "tick " + t);
                if (w.Capture.Points[1].Owner != owner) { flips++; owner = w.Capture.Points[1].Owner; }
                foreach (UnitSpawn unit in units)
                {
                    if (!w.Has<Position>(unit.Key) || w.Dead(unit.Key)) { still.Remove(unit.Key); continue; }
                    Assert.That(Failed(w, unit.Key), Is.False, "unit " + unit.Key + " tick " + t);
                    if (Layer(w, unit.Key) == 1) onFloor.Add(unit.Team);
                    worstStill = Math.Max(worstStill, StillAtTheRamp(w, unit.Key, t, still));
                }
            }
            TestContext.WriteLine("ticks " + t + " winner " + w.Match.Winner + " flips " + flips + " longest still at the ramp " + worstStill);
            // This package's baseline: team 2 wins the fight at the ramp, holds the floor and wins at tick 784; team 1 never
            // gets up in this battle (each team alone does, above).
            Assert.That(onFloor, Is.EquivalentTo(new[] { 2 }), "team 2 reaches the floor");
            Assert.That((flips, w.Match.Winner, t), Is.EqualTo((1, 2, 784)), "the point changes owner once; team 2 wins");
            Assert.That(worstStill, Is.LessThanOrEqualTo(90), "nobody stands still at the ramp for more than 90 ticks");
        }
    }
}
