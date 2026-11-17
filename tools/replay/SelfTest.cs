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
