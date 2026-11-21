using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // ML-10 acceptance 1 (nothing old changes), 2 (the plates for the picture against Sim, PlateCheck) and 3 (layers in the
    // view): the plate demo (PlateDemo) and a map without plates.
    public sealed class Ml10ViewTests
    {
        private const long M = SimFix.Scale;

        internal static SimSession Demo() => SimSession.Create(PlateDemo.Recipe(), PlateDemo.MapBytes());

        // Every cell of each plate's rectangle, at its centre and one point in each triangle of its split: the view's top
        // (solid cells) is Sim's floor and the view's walkable bit is Sim's.
        [Test]
        public void ThePlatesForThePictureAreSims()
        {
            SimSession s = Demo();
            ViewPlates view = s.Map.Plates;
            PlateWorld sim = s.PlateWorldForCheck;
            Assert.That((view.Count, sim.LayerCount), Is.EqualTo((2, 3)));
            Assert.That((view[0].Layer, view[0].Id, view[0].X, view[0].Z, view[0].Width, view[0].Depth), Is.EqualTo((1, 1UL, 10, 9, 4, 1)), "the stair");
            Assert.That((view[1].Layer, view[1].Id, view[1].X, view[1].Z, view[1].Width, view[1].Depth), Is.EqualTo((2, 2UL, 9, 6, 6, 4)), "the floor");
            Assert.That((view[1].Top(0, 0), view[1].Bottom(0, 0), view[1].Thick), Is.EqualTo((3f, 2.5f, 0.5f)));
            int solid = 0, walkable = 0;
            for (int k = 0; k < view.Count; k++)
            {
                ViewPlate p = view[k];
                for (int j = 0; j < p.Depth; j++)
                    for (int i = 0; i < p.Width; i++)
                    {
                        foreach ((long u, long v) in new[] { (M / 2, M / 2), (3 * M / 4, M / 4), (M / 4, 3 * M / 4) })
                        {
                            long x = (p.X + i) * M + u, z = (p.Z + j) * M + v;
                            bool mine = view.TryTopRaw(p.Layer, x, z, out long a);
                            bool sims = sim.TryFloor(p.Layer, SimFix.FromRaw(x), SimFix.FromRaw(z), false, out SimFix b);
                            Assert.That((mine, mine ? a : 0), Is.EqualTo((sims, sims ? b.Raw : 0)), $"layer {p.Layer} cell {p.X + i},{p.Z + j}");
                            Assert.That(mine, Is.EqualTo(p.IsSolid(i, j)));
                        }
                        bool walk = sim.TryFloor(p.Layer, SimFix.FromRaw((p.X + i) * M + M / 2), SimFix.FromRaw((p.Z + j) * M + M / 2), true, out _);
                        Assert.That(p.IsWalkable(i, j), Is.EqualTo(walk), $"walkable, layer {p.Layer} cell {p.X + i},{p.Z + j}");
                        if (p.IsSolid(i, j)) solid++;
                        if (walk) walkable++;
                    }
            }
            Assert.That((solid, walkable), Is.EqualTo((4 + 20, 4 + 20)), "hand count: stair 4, floor 24 minus the 4 cut over the stair");
            Assert.That(PlateCheck.Run(view, s).IsClean, Is.True, PlateCheck.Run(view, s).ToString());
            Assert.That(PlateCheck.Run(view, s).ToString(), Is.EqualTo("shape ok\nhole 0\ntop 0\nwalk 0\n"));
            Assert.That(TerrainCheck.Run(s.Map, s).IsClean, Is.True, "the terrain rule of the self-check is clean on the demo");
            Assert.That(view.TryTopRaw(2, 9 * M, 6 * M, out long corner) && corner == 3 * M, Is.True, "the minimum edges are inside");
            Assert.That(view.TryTopRaw(2, 15 * M, 7 * M, out _), Is.False, "the maximum edge is not");
            Assert.That(view.TryTopRaw(0, 11 * M, 7 * M, out _) || view.TryTopRaw(3, 11 * M, 7 * M, out _), Is.False, "layers 1..N only");
        }

        // Each rule of PlateCheck: a top changed at one corner (one cell; at a corner off the split's diagonal too), a hole dug, a cutout filled, a walkable bit
        // flipped, and a view of another map.
        [Test]
        public void PlateCheckReportsEachRule()
        {
            SimSession s = Demo();
            ViewPlates Fresh() => ViewPlates.From(WcMapReader.Read(PlateDemo.MapBytes()));

            ViewPlates top = Fresh();
            SetTop(top[1], 0, 0, 3 * M + M / 4);
            Assert.That(PlateCheck.Run(top, s).ToString(), Is.EqualTo("shape ok\nhole 0\ntop 1 2:9,6\nwalk 0\n"));
            // The plate's south-east corner belongs to one cell, (14, 6), and only to the triangle of its split away from the diagonal.
            ViewPlates side = Fresh();
            SetTop(side[1], 6, 0, 3 * M + M / 4);
            Assert.That(PlateCheck.Run(side, s).ToString(), Is.EqualTo("shape ok\nhole 0\ntop 1 2:14,6\nwalk 0\n"));

            ViewPlates dug = Fresh();
            Flags(dug[1], "solid")[1 * 6 + 2] = false;
            Assert.That(PlateCheck.Run(dug, s).ToString(), Is.EqualTo("shape ok\nhole 1 2:11,7\ntop 0\nwalk 0\n"));

            ViewPlates filled = Fresh();
            Flags(filled[1], "solid")[3 * 6 + 1] = true;
            Assert.That(PlateCheck.Run(filled, s).ToString(), Is.EqualTo("shape ok\nhole 1 2:10,9\ntop 0\nwalk 0\n"));

            ViewPlates walk = Fresh();
            Flags(walk[0], "walkable")[3] = false;
            Assert.That(PlateCheck.Run(walk, s).ToString(), Is.EqualTo("shape ok\nhole 0\ntop 0\nwalk 1 1:13,9\n"));

            Assert.That(PlateCheck.Run(null, s).ShapeMismatch, Is.True, "no plates drawn on a map with plates");
            byte[] other = Ml7Worlds.Plates;
            Assert.That(PlateCheck.Run(ViewPlates.From(WcMapReader.Read(other)), s).ShapeMismatch, Is.True, "another map's plates");
            SimSession flat = SimSession.Create(Ml7Worlds.Recipe(new[] { Ml7Worlds.Unit(1, 1, 500, 500) }, preview: false), Ml7Worlds.NoPlates);
            Assert.That((flat.Map.Plates, PlateCheck.Run(null, flat).IsClean), Is.EqualTo(((ViewPlates)null, true)), "no plates on either side");
        }

        // 3: while the player walks the demo script (in, up the stair, onto the floor), the view's layer is the vertical
        // state's at every tick, and interpolation keeps it.
        [Test]
        public void TheViewCarriesEachUnitsLayer()
        {
            SimSession s = Demo();
            foreach (var item in PlateDemo.Script()) s.SetPlayerInput(item.AtTick, item.Input);
            var layers = new List<int> { 0 };
            for (int tick = 1; tick <= 420; tick++)
            {
                s.StepOnce();
                foreach (ViewEntity e in s.Current.Entities)
                    Assert.That(e.Layer, Is.EqualTo((int)Ml7Worlds.Vertical(s, e.Key).Layer), $"unit {e.Key} at tick {tick}");
                s.Current.TryGet(PlateDemo.PlayerKey, out ViewEntity me);
                FrameView.Interpolate(s.Previous, s.Current, 0.5f).TryGet(PlateDemo.PlayerKey, out ViewEntity half);
                Assert.That(half.Layer, Is.EqualTo(me.Layer), "interpolated, tick " + tick);
                if (layers[layers.Count - 1] != me.Layer) layers.Add(me.Layer);
                if (tick == 150) Assert.That((me.Layer, me.X > 9 && me.X < 15 && me.Z > 6 && me.Z < 10), Is.EqualTo((0, true)), "under the floor at 150");
                if (tick == 300) Assert.That((me.Layer, me.Y), Is.EqualTo((2, 3f)), "on the floor at 300");
            }
            Assert.That(layers, Is.EqualTo(new[] { 0, 1, 2 }), "ground, stair, floor");
            s.Current.TryGet(2, out ViewEntity mate);
            Assert.That((mate.Layer, mate.Y), Is.EqualTo((2, 3f)), "a team mate placed on the floor");
        }

        // 1: without plates the session has none, every unit is on layer 0, the cut leaves everything and picks carry layer 0.
        [Test]
        public void WithoutPlatesNothingChanges()
        {
            SimSession s = SimSession.Create(Ml7Worlds.Recipe(new[] { Ml7Worlds.Unit(1, 1, 500, 500), Ml7Worlds.Unit(2, 2, 3500, 2500) },
                new[] { BridgeCommand.MoveTo(1, 1, 9500, 4500) }, preview: false), Ml7Worlds.NoPlates);
            Assert.That(s.Map.Plates, Is.Null);
            for (int tick = 0; tick < 60; tick++)
            {
                Assert.That(s.Current.Entities.ToArray().All(e => e.Layer == 0), Is.True);
                s.StepOnce();
            }
            s.Current.TryGet(1, out ViewEntity me);
            LayerCutResult cut = LayerCut.Run(null, s.Map, me, new IsoCamera(12f).Forward);
            Assert.That((cut.Indoors, cut.PlateCount), Is.EqualTo((false, 0)));
            var camera = new IsoCamera(12f);
            camera.Follow(me.X, me.Z);
            for (int i = 0; i < 9; i++)
            {
                AimTarget a = AimPick.Pick(camera, 100 + 200 * i, 80 + 100 * i, 1920, 1080, ViewGround.For(s.Map), s.Current, 1, null, null);
                AimTarget b = AimPick.Pick(camera, 100 + 200 * i, 80 + 100 * i, 1920, 1080, ViewGround.For(s.Map), s.Current, 1);
                Assert.That((a.Kind, a.X, a.Y, a.Z, a.XMm, a.ZMm, a.YMm, a.UnitKey, a.Layer), Is.EqualTo((b.Kind, b.X, b.Y, b.Z, b.XMm, b.ZMm, b.YMm, b.UnitKey, 0)));
                Assert.That(a.Kind, Is.Not.EqualTo(AimKind.Plate));
            }
            var unit = new ViewEntity(7, 0, 1, 2, 3, 4, 5, true, 1);
            Assert.That((unit.Layer, unit.Nerve), Is.EqualTo((0, 1)), "the old constructor gives layer 0");
        }

        // A top point changed in both the corner table and the sampled field.
        private static void SetTop(ViewPlate p, int i, int j, long raw)
        {
            int[] table = (int[])typeof(ViewPlate).GetField("topRaw", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(p);
            table[j * (p.Width + 1) + i] = (int)raw;
            var field = new HeightField(p.Width, p.Depth, SimFix.FromRaw(p.X * M), SimFix.FromRaw(p.Z * M), table);
            typeof(ViewPlate).GetField("<Field>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(p, field);
        }

        private static bool[] Flags(ViewPlate p, string name) =>
            (bool[])typeof(ViewPlate).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(p);
    }
}
