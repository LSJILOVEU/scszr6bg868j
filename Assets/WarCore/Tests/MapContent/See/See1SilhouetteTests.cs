using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.See1Field;

namespace WarCore.Tests
{
    // SEE-1 rule 2 (who is drawn as a silhouette) on ground T (building A on x 10..30, z 10..14, 6 m high). From a head (1 m up)
    // the line back meets A up to 5 m above it, which is 0.7071 * 5 = 3.54 m behind A's back face: a head is covered when
    // z <= 17.54 (between x 10 and 30).
    public sealed class See1SilhouetteTests
    {
        private static readonly ViewEntity Far = At(60, 40);

        private static HashSet<ulong> Run(ViewMap map, ViewEntity? player, ViewEntity[] units, (float X, float Y, float Z)? camera = null,
            LayerCutResult cut = null)
        {
            var forward = camera ?? Forward;
            cut = cut ?? LayerCut.Run(map.Plates, map, player, forward);
            return Silhouettes.Run(map.Plates, map, cut, player, units, forward);
        }

        // A7, player far away (A is Shown): a team mate covered by A is in; one 3.5 m behind A is in (3.5 < 3.54), one at 3.6 m
        // is not (z 17.6 > 17.54); the same place for an enemy, a dead team mate, a unit of team 0 and the player: not.
        [Test]
        public void OnlyCoveredLivingTeamMatesAreDrawn()
        {
            ViewMap t = T();
            var units = new[]
            {
                Far,
                At(25, 16, key: 2), At(25, 17.5f, key: 3), At(25, 17.6f, key: 4),
                At(25, 16, key: 5, team: 2), At(25, 16, key: 6, alive: false), At(25, 16, key: 7, team: 0),
                // Heads near the ends of a wall: the line from (31, 16.5) enters A at x 28.5 (A ends at x 30), from (42, 12.5) the
                // pillar (x 40..41, z 10..11) at x 40.5, from (43.5, 13.5) at x 41 (z 11).
                At(31, 16.5f, key: 8), At(42, 12.5f, key: 9), At(43.5f, 13.5f, key: 10),
            };
            Assert.That(Run(t, Far, units), Is.EquivalentTo(new ulong[] { 2, 3, 8, 9, 10 }));
        }

        // The player is not his own silhouette even when the cut he is given does not fade what covers him.
        [Test]
        public void NotThePlayerHimself()
        {
            ViewMap t = T();
            ViewEntity me = At(25, 16);
            var units = new[] { me, At(25, 16, key: 2) };
            LayerCutResult nothing = LayerCut.None(t.Plates, t);
            Assert.That(Run(t, me, units, cut: nothing), Is.EquivalentTo(new ulong[] { 2 }));
        }

        // The player in A's shadow (20, 17): A is Faded, so it covers nobody: the team mate at (25, 16) is seen through it.
        [Test]
        public void AFadedWallCoversNobody()
        {
            ViewMap t = T();
            ViewEntity me = At(20, 17);
            LayerCutResult cut = LayerCut.Run(t.Plates, t, me, Forward);
            Assert.That(cut.Wall(0), Is.EqualTo(CutState.Faded));
            Assert.That(Run(t, me, new[] { me, At(25, 16, key: 2) }), Is.Empty);
        }

        [Test]
        public void NoPlayerNoSilhouettes()
        {
            ViewMap t = T();
            var units = new[] { At(25, 16, key: 2) };
            Assert.That(Run(t, null, units), Is.Empty);
            Assert.That(Run(t, At(60, 40, alive: false), units), Is.Empty);
        }

        // A9, yaw 135 and pitch 60: the head line goes -x, +z by 0.4082 m a metre up; A covers a head when z >= 10 - 0.4082 * 5 =
        // 7.96. (25, 8.1) is covered, (25, 7.9) is not.
        [Test]
        public void ARotatedAndTiltedCamera()
        {
            ViewMap t = T();
            var camera = new IsoCamera(12f, 0f, 135f, 60f).Forward;
            var units = new[] { Far, At(25, 8.1f, key: 2), At(25, 7.9f, key: 3) };
            Assert.That(Run(t, Far, units, camera), Is.EquivalentTo(new ulong[] { 2 }));
        }

        // The demo house with a 12 m wall on cell (10, 4), south of the house: a head on the first floor at (14.5, 3 + 1, 8.5)
        // looks back along x - z = 6, through (10.5, 4.5) at 5.66 m up (under 12 m): covered. The player's own lines (x - z = 4)
        // miss that wall.
        private static (ViewPlates Plates, ViewMap Map) WithTallWall() =>
            House(new[] { new MapWall(20, "wall", "walls", new MapXZ(10, 4), new MapXZ(1, 1), 12 * SimFix.Scale) });

        [Test]
        public void ATeamMateOnAHiddenFloorIsNotDrawn()
        {
            var (plates, map) = WithTallWall();
            int extra = Enumerable.Range(0, map.Walls.Length).Single(w => map.Walls[w].CenterX == 10.5f && map.Walls[w].CenterZ == 4.5f);
            ViewEntity mate = At(14.5f, 8.5f, key: 2, y: 3f, layer: 2);

            // Player on the floor: the floor is drawn, the mate on it is covered by the 12 m wall.
            ViewEntity onFloor = At(11.5f, 7.5f, y: 3f, layer: 2);
            LayerCutResult up = LayerCut.Run(plates, map, onFloor, Forward);
            Assert.That((up.Plate(1), up.Wall(extra)), Is.EqualTo((CutState.Shown, CutState.Shown)));
            Assert.That(Silhouettes.Run(plates, map, up, onFloor, new[] { onFloor, mate }, Forward), Is.EquivalentTo(new ulong[] { 2 }));

            // Player under the floor: the floor is hidden and so is the mate on it.
            ViewEntity under = At(11.5f, 7.5f);
            LayerCutResult down = LayerCut.Run(plates, map, under, Forward);
            Assert.That((down.Plate(1), down.Wall(extra)), Is.EqualTo((CutState.Hidden, CutState.Shown)));
            Assert.That(down.IsHidden(mate), Is.True);
            Assert.That(Silhouettes.Run(plates, map, down, under, new[] { under, mate }, Forward), Is.Empty);
        }

        // A floor covers too: the player outdoors at (4.5, 8.5), a team mate on the ground under the first floor at (11.5, 7.5):
        // its head line (1 m up) enters the floor (2.5 to 3 m) at 1.5 m further up, over cell (10, 6), and the 3 m south wall
        // is passed over (3.12 m at the wall's near face).
        [Test]
        public void AFloorCoversATeamMateUnderIt()
        {
            var (plates, map) = House();
            ViewEntity me = At(4.5f, 8.5f), mate = At(11.5f, 7.5f, key: 2);
            LayerCutResult cut = LayerCut.Run(plates, map, me, Forward);
            Assert.That((cut.Indoors, cut.Plate(1)), Is.EqualTo((false, CutState.Shown)));
            Assert.That(Silhouettes.Run(plates, map, cut, me, new[] { me, mate }, Forward), Is.EquivalentTo(new ulong[] { 2 }));
        }

        // A faded floor covers nobody: the player halfway up the stair (12, 1.5, 9.5) has the first floor between him and the
        // camera (Faded, ML-10), so the team mate under it at (11.5, 7.5) is seen through it.
        [Test]
        public void AFadedFloorCoversNobody()
        {
            var (plates, map) = House();
            ViewEntity me = At(12f, 9.5f, y: 1.5f, layer: 1), mate = At(11.5f, 7.5f, key: 2);
            LayerCutResult cut = LayerCut.Run(plates, map, me, Forward);
            Assert.That(cut.Plate(1), Is.EqualTo(CutState.Faded));
            Assert.That(Silhouettes.Run(plates, map, cut, me, new[] { me, mate }, Forward), Is.Empty);
        }

        // Without a team (team 0) there are no team mates: a player of team 0 draws none of the covered units of team 0.
        [Test]
        public void UnitsWithoutATeamAreNeverDrawn()
        {
            ViewMap t = T();
            ViewEntity me = At(60, 40, team: 0);
            Assert.That(Run(t, me, new[] { me, At(25, 16, key: 2, team: 0) }), Is.Empty);
        }
    }
}
