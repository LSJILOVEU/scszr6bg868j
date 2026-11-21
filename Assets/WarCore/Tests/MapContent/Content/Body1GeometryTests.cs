using System.Linq;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Sim;

namespace WarCore.Tests
{
    // BODY-1 B4 and B7: the shipped layers' body really changes what a shot sees and hits (a fixture world built straight from
    // the shipped combat pack, like Ml3Fixture), and the window stamp accepts the shipped body at its default sill and opening.
    public sealed class Body1GeometryTests
    {
        private const long M = Ml3World.M;
        // Two armed enemies 10 m apart on y = 0, a 1 m thick wall across x = 5 m (z -2..2 m). Ground-to-top walls, raw metres:
        //   1.0 m = 65536   1.4 m = 91750 (91750.4)   1.6 m = 104858 (104857.6)   2.0 m = 131072
        // Sight line height: eye 1.5 m = 98304 raw with a body, 0.5 m = 32768 without one. A wall under the eye is seen over,
        // a wall above it hides the target. "Hurt" = a fire record that hit and carried damage.
        private static int Hurt(string layer, long wallTopRaw, ulong seed = 41)
        {
            CombatPack pack = RwrPack.Load(layer).BuildCombat();
            var walls = wallTopRaw == 0 ? null : new[] { new Ml3World.Wall(9 * M / 2, -2 * M, 11 * M / 2, 2 * M, 0, wallTopRaw) };
            var w = new Ml3World(pack, new[] { new Ml3World.Unit(0, 0, 0, 1), new Ml3World.Unit(10 * M, 0, 0, 2) }, walls, seed: seed);
            int hurt = 0;
            for (int tick = 0; tick < 90; tick++)
                foreach (FireRecord f in w.StepFires())
                    if (f.Outcome == FireOutcome.Hit && f.DamageRaw > 0) hurt++;
            return hurt;
        }

        // Control: no wall, both layers shoot and hurt (so a zero below means the wall, not a dead test).
        [TestCase("rwr-m6")]
        [TestCase("rwr-body")]
        [TestCase("rwr-field")]
        public void NoWall_SomeoneIsHurt(string layer) => Assert.That(Hurt(layer, 0), Is.GreaterThan(0), layer);

        // Without a body (the old layers) a 1.0 m wall hides the target: nobody is hurt. 1.4 m too.
        [TestCase("rwr-m6", 65536L)]
        [TestCase("rwr-m6", 91750L)]
        [TestCase("rwr-surfaces", 65536L)]
        public void OldLayers_AOneMetreWallStillHidesTheTarget(string layer, long top) => Assert.That(Hurt(layer, top), Is.EqualTo(0), layer + " " + top);

        // With the shipped body (eye 1.5 m): walls of 1.0 and 1.4 m are seen over, 1.6 and 2.0 m hide the target.
        [TestCase("rwr-body", 65536L, true)]
        [TestCase("rwr-body", 91750L, true)]
        [TestCase("rwr-body", 104858L, false)]
        [TestCase("rwr-body", 131072L, false)]
        [TestCase("rwr-field", 65536L, true)]
        [TestCase("rwr-field", 91750L, true)]
        [TestCase("rwr-field", 104858L, false)]
        [TestCase("rwr-field", 131072L, false)]
        public void Body_WallsUnderTheEyeAreSeenOver(string layer, long top, bool hurt) =>
            Assert.That(Hurt(layer, top) > 0, Is.EqualTo(hurt), layer + " wall top raw " + top);

        // The shot leaves the muzzle: 1.5 m = 98304 raw above the foot with a body, at the foot (0) without one.
        [Test]
        public void ShotOriginIsTheMuzzle()
        {
            Ml3World With(string layer) => new Ml3World(RwrPack.Load(layer).BuildCombat(), new[] { new Ml3World.Unit(0, 0, 0, 1), new Ml3World.Unit(10 * M, 0, 0, 2) });
            FireRecord body = With("rwr-body").StepFires().First(), old = With("rwr-m6").StepFires().First();
            Assert.That((body.Origin.Y.Raw, body.End.Y.Raw), Is.EqualTo((98304L, 98304L)));
            Assert.That(old.Origin.Y.Raw, Is.EqualTo(0L));
        }

        // B7: the window stamp with the shipped body at the default sill (0.9 m) and opening (2.0 m): accepted, and the map that
        // carries it (the three-lane map with the pack swapped, plus the stamp) passes the map gate with no problem.
        [Test]
        public void Window_ShippedBodyAcceptsTheDefaults()
        {
            foreach (string layer in new[] { "rwr-body", "rwr-field" })
            {
                CombatBody body = RwrPack.Load(layer).Body.Value;
                MapDesc start = MapDescJson.Read(Body1Maps.WithPack(Body1Maps.Lanes, "rwr-m6", layer));
                MapStampPlan plan = null;
                Assert.DoesNotThrow(() => plan = MapPrefabs.Window(start, "body1-win", new MapXZ(2, 2), true, 10, 4, 2, 4 * M, "walls", "wall", body), layer);
                Assert.That(plan.Walls.Select(e => e.Template.HeightRaw), Is.EqualTo(new[] { 4 * M, 58982L, 2 * M, 4 * M }), layer);
                var edit = new MapEdit(start);
                edit.Stamp(plan);
                Assert.That(edit.Current.Walls.Count, Is.EqualTo(start.Walls.Count + 4), layer);
                Assert.That(MapGate.Check(MapDescJson.Write(edit.Current), RwrPack.Load(layer)), Is.Empty, layer);
                // Limits of the shipped body: sill up to 1.25 m (81920 raw), opening top from 1.75 m (114688 raw).
                Assert.DoesNotThrow(() => MapPrefabs.Window(start, "w", new MapXZ(2, 2), true, 10, 4, 2, 4 * M, "walls", "wall", body, 81920, 114688), layer);
                Assert.Throws<System.ArgumentException>(() => MapPrefabs.Window(start, "w", new MapXZ(2, 2), true, 10, 4, 2, 4 * M, "walls", "wall", body, 81922), layer);
                Assert.Throws<System.ArgumentException>(() => MapPrefabs.Window(start, "w", new MapXZ(2, 2), true, 10, 4, 2, 4 * M, "walls", "wall", body, openingRaw: 114686), layer);
            }
        }
    }
}
