using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using WarCore.Tests.Dash;
using static WarCore.Tests.DashT.DashTWorlds;

namespace WarCore.Tests.DashT
{
    // DASH-T acceptance 3 and 4: a dash on a terrain world, by hand.
    //   The dash: 4000 mm = 262144 raw in 6 ticks. Along +X the total is 262144; tick k moves floor(262144k/6) - floor(262144(k-1)/6):
    //   43690, 43691, 43691, 43690, 43691, 43691. Along -X (total -262144, floor toward negative infinity): -43691, -43691, -43690,
    //   -43691, -43691, -43690.
    //   Pieces (D2): m = ceil(43690 / 16384) = ceil(43691 / 16384) = 3. 43690 splits into 14563, 14563, 14564 (floor(43690/3) =
    //   14563, floor(87380/3) = 29126); 43691 into 14563, 14564, 14564 (floor(87382/3) = 29127). -43691 splits into -14564, -14564,
    //   -14563 (floor(-43691/3) = -14564, floor(-87382/3) = -29128); -43690 into -14564, -14563, -14563 (floor(-87380/3) = -29127).
    //   Mud scales each piece by 500/1000 toward zero: 14563 -> 7281, 14564 -> 7282, -14563 -> -7281, -14564 -> -7282.
    public sealed class DashTTerrainTests
    {
        private static readonly long[] Plus = { 43690, 43691, 43691, 43690, 43691, 43691 };
        private static readonly long[] Minus = { -43691, -43691, -43690, -43691, -43691, -43690 };

        private static List<SimVec3> DashAndTrack(SimSession s, SimVec3 direction, int ticks)
        {
            Assert.That(DashWorlds.Dash(s, direction), Is.True);
            return DashWorlds.Track(s, ticks);
        }

        // Grass, flat field at 0: every tick moves the whole step (the pieces add up to it), y stays on the ground, z is untouched.
        [Test]
        public void Grass_MovesTheWholeStepEachTick()
        {
            SimSession plus = World(Map(), new[] { Dasher(1, 10500, 4500) });
            List<SimVec3> track = DashAndTrack(plus, DashWorlds.PlusX, 7);
            Assert.That(Deltas(track, p => p.X.Raw), Is.EqualTo(new long[] { 43690, 43691, 43691, 43690, 43691, 43691, 0 }));
            foreach (SimVec3 p in track) Assert.That((p.Y.Raw, p.Z.Raw), Is.EqualTo((0L, track[0].Z.Raw)));
            Assert.That(DashWorlds.State(plus).Ticks, Is.EqualTo(6), "not ended early");

            SimSession minus = World(Map(), new[] { Dasher(1, 10500, 4500) });
            Assert.That(Deltas(DashAndTrack(minus, DashWorlds.MinusX, 6), p => p.X.Raw), Is.EqualTo(Minus));
        }

        // Mud: tick +43690 -> 7281 + 7281 + 7282 = 21844, tick +43691 -> 7281 + 7282 + 7282 = 21845; 131068 in all (one whole
        // step a tick would give 21845 every tick, 131070). -X: tick -43691 -> -7282 - 7282 - 7281 = -21845, tick -43690 ->
        // -7282 - 7281 - 7281 = -21844 (floor toward negative infinity would make every -14563 a -7282: -21846 and -21845).
        [Test]
        public void Mud_ScalesEachPieceTowardZero()
        {
            SimSession plus = World(Map(surface: (x, z) => Mud), new[] { Dasher(1, 10500, 4500) });
            long[] p = Deltas(DashAndTrack(plus, DashWorlds.PlusX, 6), q => q.X.Raw);
            Assert.That(p, Is.EqualTo(new long[] { 21844, 21845, 21845, 21844, 21845, 21845 }));
            SimSession minus = World(Map(surface: (x, z) => Mud), new[] { Dasher(1, 10500, 4500) });
            long[] m = Deltas(DashAndTrack(minus, DashWorlds.MinusX, 6), q => q.X.Raw);
            Assert.That(m, Is.EqualTo(new long[] { -21845, -21845, -21844, -21845, -21845, -21844 }));
            long total = 0;
            foreach (long d in p) total += d;
            Assert.That(total, Is.EqualTo(131068));
        }

        // The surface where each piece starts decides its scale. From (5.5, 4.5) along +X on grass up to x = 6 and mud beyond:
        // tick 1 (43690): 14563 on grass to 375011, 14563 on grass to 389574 (cell 5 still), then 14564 from 389574 on grass to
        // 404138 (x 6.17, mud). Tick 2 (43691) starts on mud: 7281 + 7282 + 7282 = 21845.
        [Test]
        public void EachPieceTakesTheSurfaceWhereItStarts()
        {
            SimSession s = World(Map(surface: (x, z) => x >= 6 ? Mud : Grass), new[] { Dasher(1, 5500, 4500) });
            List<SimVec3> track = DashAndTrack(s, DashWorlds.PlusX, 2);
            Assert.That(track[1].X.Raw, Is.EqualTo(360448 + 43690));
            Assert.That(track[2].X.Raw - track[1].X.Raw, Is.EqualTo(21845));
        }

        // A sloped field (0.25 m a cell along x, within 3/4): y follows the ground at every tick, x as on flat grass.
        [Test]
        public void Slope_KeepsTheUnitOnTheGround()
        {
            SimSession s = World(Map(height: (i, j) => i * (int)(S / 4)), new[] { Dasher(1, 5500, 4500) });
            List<SimVec3> track = DashAndTrack(s, DashWorlds.PlusX, 6);
            Assert.That(Deltas(track, p => p.X.Raw), Is.EqualTo(Plus));
            foreach (SimVec3 p in track) Assert.That(p.Y.Raw, Is.EqualTo(p.X.Raw / 4), "y on the ground at x " + p.X.Raw);
        }

        // Acceptance 4. From x 2.5 (163840) along +X toward a cell that stops the unit at x = 6: the unit may stand while its
        // centre is at least its radius less one raw (16383) from the cell, x <= 376833. Ticks 1..3 move 43690, 43691, 43691 to
        // 294912. Tick 4 (pieces 14563, 14563, 14564) reaches 338602. Tick 5 (14563, 14564, 14564): 353165, 367729, then 382293 is
        // refused: the dash ends after step 4 with the unit at 367729 (part of tick 5 taken). One whole step a tick would stop at
        // 338602; two pieces a tick (21845, 21846) at 360447. The cooldown runs as for a whole dash: refused at 59, accepted at 60.
        [TestCase("wall")]
        [TestCase("steep")]
        [TestCase("rock")]
        public void Blocked_StopsAtTheFirstPieceThatCannotMove(string by)
        {
            WcMap map = by == "wall" ? Map(walls: (6, 0, 1, 8))
                : by == "steep" ? Map(height: (i, j) => i >= 7 ? 2 * (int)S : 0)
                : Map(surface: (x, z) => x == 6 ? Rock : Grass);
            SimSession s = World(map, new[] { Dasher(1, 2500, 4500) });
            List<SimVec3> track = DashAndTrack(s, DashWorlds.PlusX, 6);
            long[] x = new long[track.Count];
            for (int i = 0; i < x.Length; i++) x[i] = track[i].X.Raw;
            Assert.That(x, Is.EqualTo(new long[] { 163840, 207530, 251221, 294912, 338602, 367729, 367729 }));
            Assert.That(track[6].Y.Raw, Is.Zero);
            Assert.That(DashWorlds.State(s).Ticks, Is.EqualTo(4), "ended after step 4");
            Assert.That(DashWorlds.Loco(s).IsDashing(M3TestWorld.Unit(s)), Is.False);
            while (DashWorlds.Tick(s) < 59) s.StepOnce();
            Assert.That(DashWorlds.Dash(s, DashWorlds.MinusX), Is.False, "tick 59, cooling down");
            s.StepOnce();
            Assert.That(DashWorlds.Dash(s, DashWorlds.MinusX), Is.True, "tick 60");
        }

        // A piece scaled to nothing is blocked; a tick whose step is nothing is not. 1 mm = 65 raw in 300 ticks: ticks 1..4 move
        // floor(65k/300) - floor(65(k-1)/300) = 0, tick 5 moves 1 raw. On mud that raw scales to 0: the dash ends after step 4,
        // the unit has not moved. On grass, and on a world without terrain, the raw is taken and the dash goes on.
        [Test]
        public void APieceScaledToNothingIsBlocked()
        {
            SimSession mud = World(Map(surface: (x, z) => Mud), new[] { Dasher(1, 10500, 4500, distanceMm: 1, ticks: 300, cooldown: 300) });
            SimSession grass = World(Map(), new[] { Dasher(1, 10500, 4500, distanceMm: 1, ticks: 300, cooldown: 300) });
            SimSession flat = DashWorlds.World(new[] { Dasher(1, 10500, 4500, distanceMm: 1, ticks: 300, cooldown: 300) }, DashWorlds.Map());
            List<SimVec3> m = DashAndTrack(mud, DashWorlds.PlusX, 5), g = DashAndTrack(grass, DashWorlds.PlusX, 5);
            List<SimVec3> f = DashAndTrack(flat, DashWorlds.PlusX, 5);
            Assert.That(DashWorlds.State(mud).TotalX.Raw, Is.EqualTo(65));
            for (int t = 1; t <= 4; t++) Assert.That(m[t], Is.EqualTo(m[0]), "tick " + t);
            Assert.That(DashWorlds.State(grass).Ticks, Is.EqualTo(300), "grass: the zero ticks do not end it");
            Assert.That(g[5].X.Raw - g[0].X.Raw, Is.EqualTo(1));
            Assert.That(DashWorlds.State(flat).Ticks, Is.EqualTo(300), "no terrain: the zero ticks do not end it");
            Assert.That(f[5].X.Raw - f[0].X.Raw, Is.EqualTo(1));
            Assert.That(m[5], Is.EqualTo(m[0]));
            Assert.That(DashWorlds.State(mud).Ticks, Is.EqualTo(4), "mud: tick 5's raw scales to nothing");
        }

        // A dash still needs the crowd mover (the feature refuses other movers first; the locomotion itself refuses too).
        [Test]
        public void BindDash_NeedsACrowd()
        {
            SimSession path = WorldBuilder.Build(new WorldRecipe(41, MoverKind.InterimPath, new[] { DashWorlds.Plain(1, 10500, 4500) },
                System.Array.Empty<BridgeCommand>()), DashWorlds.Map(), false);
            Assert.That(() => DashWorlds.Loco(path).BindDash(default, default, M3TestWorld.Sim(path).Clock),
                Throws.InvalidOperationException.With.Message.Contains("crowd"));
        }

        // A fast surface (x 2) doubles each piece: 0.67 m a tick becomes 1.33 m, still taken in pieces of at most 0.5 m.
        [Test]
        public void Fast_DoublesEachPiece()
        {
            SimSession s = World(Map(surface: (x, z) => Fast), new[] { Dasher(1, 2500, 4500) });
            Assert.That(Deltas(DashAndTrack(s, DashWorlds.PlusX, 6), p => p.X.Raw),
                Is.EqualTo(new long[] { 87380, 87382, 87382, 87380, 87382, 87382 }));
        }
    }
}
