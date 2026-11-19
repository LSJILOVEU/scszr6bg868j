using System;
using System.Collections.Generic;

namespace WarCore.Replay
{
    // Checks the metric formulas on hand-built layouts and that an export is byte-identical when repeated.
    public static class SelfTest
    {
        private static int failures;

        public static int Run()
        {
            failures = 0;
            var lattice = new List<(double x, double z)>();
            for (int r = 0; r < 10; r++) for (int c = 0; c < 10; c++) lattice.Add((c + (r % 2) * 0.5, r * Math.Sqrt(0.75)));
            Expect("lattice nnSpread is 0", Metrics.NearestNeighbourSpread(lattice) < 1e-9);
            var uneven = new List<(double x, double z)>();
            for (int i = 0; i < 12; i++) uneven.Add((i * i, 0));
            Expect("uneven nnSpread above 0.5", Metrics.NearestNeighbourSpread(uneven) > 0.3);
            Expect("nnSpread undefined below MinUnits", double.IsNaN(Metrics.NearestNeighbourSpread(lattice.GetRange(0, Metrics.MinUnits - 1))));

            var points = new List<(double x, double z, double r)> { (0, 0, 6), (100, 0, 6) };
            var twelve = new List<(double x, double z)>();
            for (int i = 0; i < 9; i++) twelve.Add((i * 0.5 - 2, 1));
            for (int i = 0; i < 2; i++) twelve.Add((100, i));
            twelve.Add((50, 50));
            Expect("share 9 of 12", Math.Abs(Metrics.MaxPointShare(twelve, points) - 0.75) < 1e-12);
            Expect("share undefined below MinUnits", double.IsNaN(Metrics.MaxPointShare(twelve.GetRange(0, Metrics.MinUnits - 1), points)));

            var owners = new List<int> { 2, 0 };
            var oneSide = new List<(double x, double z)>();
            for (int i = 0; i < 8; i++) oneSide.Add((-15, -1 + i * 0.25));
            Expect("attack from one side near 0", Metrics.AttackBearingSpread(oneSide, 1, points, owners) < 0.01);
            var ring = new List<(double x, double z)>();
            for (int i = 0; i < 8; i++) ring.Add((15 * Math.Cos(i * Math.PI / 4), 15 * Math.Sin(i * Math.PI / 4)));
            Expect("attack from all sides near 1", Metrics.AttackBearingSpread(ring, 1, points, owners) > 0.99);
            Expect("own point is not attacked", double.IsNaN(Metrics.AttackBearingSpread(ring, 2, points, owners)));
            Expect("too few attackers undefined", double.IsNaN(Metrics.AttackBearingSpread(ring.GetRange(0, 4), 1, points, owners)));
            var far = new List<(double x, double z)>();
            for (int i = 0; i < 8; i++) far.Add((-40, i));
            Expect("beyond attack range undefined", double.IsNaN(Metrics.AttackBearingSpread(far, 1, points, owners)));

            // Close share: 12 units, pairs 1 m apart (close) and singles 5 m from everyone.
            var pairs = new List<(double x, double z)>();
            for (int i = 0; i < 4; i++) { pairs.Add((i * 10, 0)); pairs.Add((i * 10 + 1, 0)); }
            for (int i = 0; i < 4; i++) pairs.Add((i * 10 + 5, 20));
            Expect("closeShare 8 of 12", Math.Abs(Metrics.CloseShare(pairs) - 8.0 / 12) < 1e-12);
            var apart = new List<(double x, double z)>();
            for (int i = 0; i < 6; i++) { apart.Add((i * 10, 0)); apart.Add((i * 10, 1.2)); }
            Expect("closeShare at exactly 1.2 m is not close", Metrics.CloseShare(apart) == 0);
            Expect("closeShare undefined below MinUnits", double.IsNaN(Metrics.CloseShare(pairs.GetRange(0, Metrics.MinUnits - 1))));

            // Near points: only units within 2 x radius (12 m) of a point count; nearest teammate from the whole team.
            var near = new List<(double x, double z)>();
            for (int i = 0; i < 10; i++) near.Add((i * 0.5 - 2.25, 11));  // within 12 m of (0, 0), 0.5 m apart: all close
            for (int i = 0; i < 10; i++)                                  // 11 m around (100, 0), 6.8 m apart: none close
                near.Add((100 + 11 * Math.Cos(i * Math.PI / 5), 11 * Math.Sin(i * Math.PI / 5)));
            near.Add((50, 50)); near.Add((50.5, 50));                   // far from both points, close to each other
            Expect("closeShareNearPoints 10 of 20", Math.Abs(Metrics.CloseShareNearPoints(near, points) - 0.5) < 1e-12);
            Expect("closeShareNearPoints undefined below MinUnits", double.IsNaN(Metrics.CloseShareNearPoints(near.GetRange(11, 11), points)));

            // Idle outside: idle and beyond radius + ring of every point.
            var idleUnits = new List<(double x, double z)>(); var idle = new List<bool>();
            for (int i = 0; i < 10; i++) { idleUnits.Add((0, i < 5 ? 3 : 9)); idle.Add(true); }   // 5 inside 6 m, 5 at 9 m
            idleUnits.Add((50, 50)); idle.Add(false);                                              // outside but walking
            idleUnits.Add((50, 50)); idle.Add(true);                                               // outside and idle
            Expect("idleOutside 6 of 12 without a ring", Math.Abs(Metrics.IdleOutside(idleUnits, idle, points, 0) - 0.5) < 1e-12);
            Expect("idleOutside 1 of 12 with a 6 m ring", Math.Abs(Metrics.IdleOutside(idleUnits, idle, points, 6) - 1.0 / 12) < 1e-12);

            // Enemy within 20 m (inclusive) and idle with no enemy within 45 m (inclusive).
            var foes = new List<(double x, double z)> { (0, 20), (200, 0) };
            var watch = new List<(double x, double z)>();
            for (int i = 0; i < 12; i++) watch.Add((0, i < 3 ? 0 : -1 - i));   // 3 at exactly 20 m, the rest 22 m and further
            Expect("enemyWithin20 3 of 12", Math.Abs(Metrics.EnemyWithin(watch, foes) - 3.0 / 12) < 1e-12);
            Expect("enemyWithin20 undefined below MinUnits", double.IsNaN(Metrics.EnemyWithin(watch.GetRange(0, Metrics.MinUnits - 1), foes)));
            var lone = new List<(double x, double z)> { (50, 95.01) };   // just beyond 45 m of (50, 50)
            var near45 = new List<(double x, double z)> { (50, 95) };       // exactly 45 m from (50, 50)
            Expect("idleNoEnemy 1 of 12 with nobody near", Math.Abs(Metrics.IdleNoEnemy(idleUnits, idle, lone, points, 6) - 1.0 / 12) < 1e-12);
            Expect("idleNoEnemy 0 with an enemy at 45 m", Metrics.IdleNoEnemy(idleUnits, idle, near45, points, 6) == 0);
            Expect("idleNoEnemy 6 of 12 without a ring and no enemy", Math.Abs(Metrics.IdleNoEnemy(idleUnits, idle, new List<(double x, double z)>(), points, 0) - 0.5) < 1e-12);

            // Cover: a wall 10 m wide and 1 m deep centred on (0, 10); enemies at (0, 20) and (30, 20).
            var wall = new List<(double x, double z, double sx, double sz)> { (0, 10, 10, 1) };
            Expect("a line through the wall is blocked", Metrics.Blocked((0, 20), (0, 0), wall[0]));
            Expect("a line past the wall's end is not", !Metrics.Blocked((0, 20), (12, 0), wall[0]));
            Expect("a line touching the wall's corner is blocked", Metrics.Blocked((0, 20), (10, 0), wall[0]));
            Expect("a line that stops short of the wall is not", !Metrics.Blocked((0, 20), (0, 11), wall[0]));
            var pinnedFive = new List<(double x, double z)> { (0, 0), (1, 0), (-1, 0), (12, 0), (-12, 0) };
            var one = new List<(double x, double z)> { (0, 20) };
            Expect("pinnedHidden 3 of 5 behind the wall", Math.Abs(Metrics.PinnedHidden(pinnedFive, one, wall) - 0.6) < 1e-12);
            Expect("pinnedHidden 0 when a second enemy sees round the wall", Metrics.PinnedHidden(pinnedFive, new List<(double x, double z)> { (0, 20), (30, 20) }, wall) == 0);
            Expect("pinnedHidden 1 when the enemy is out of range", Metrics.PinnedHidden(pinnedFive, one, wall, 15) == 1);
            Expect("pinnedHidden counts an enemy at exactly the range", Metrics.PinnedHidden(new List<(double x, double z)> { (12, 4), (12, 4), (12, 4), (12, 4), (12, 4) }, one, wall, 20) == 0);
            Expect("pinnedHidden undefined below MinPinned", double.IsNaN(Metrics.PinnedHidden(pinnedFive.GetRange(0, Metrics.MinPinned - 1), one, wall)));
            var atWall = new List<(double x, double z)> { (0, 10), (0, 13), (7.5, 10), (7.6, 10), (6.5, 12.5) };   // inside, 2.5 m off the face, 2.5 m and 2.6 m off the end, 2.5 m off the corner
            Expect("pinnedAtWall 4 of 5", Math.Abs(Metrics.PinnedAtWall(atWall, wall) - 0.8) < 1e-12);
            Expect("pinnedAtWall undefined below MinPinned", double.IsNaN(Metrics.PinnedAtWall(atWall.GetRange(0, Metrics.MinPinned - 1), wall)));
            Expect("pinnedMoving 2 of 5", Math.Abs(Metrics.PinnedMoving(2, 5) - 0.4) < 1e-12);
            Expect("pinnedMoving undefined below MinPinned", double.IsNaN(Metrics.PinnedMoving(2, Metrics.MinPinned - 1)));

            string a = Program.Export("base", 300, 3, out _), b = Program.Export("base", 300, 3, out _);
            Expect("export repeats byte for byte", a == b);

            Console.WriteLine(failures == 0 ? "selftest: all passed" : $"selftest: {failures} failed");
            return failures == 0 ? 0 : 1;
        }

        private static void Expect(string name, bool ok)
        {
            Console.WriteLine((ok ? "ok   " : "FAIL ") + name);
            if (!ok) failures++;
        }
    }
}
