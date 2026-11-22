using System;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.VSpread1.VSpread1Worlds;

namespace WarCore.Tests.VSpread1
{
    // VSPREAD-1 V11 (a prone target is much harder to hit) and V15 (the "vspread-range" replay: three lanes, 1000 shots each,
    // standing > crouched > prone). Shots count from VspreadDemo.SettleTick on, when every target has reached its stance.
    // With VSPREAD1_SIDE_OUT set the test writes every counted shot (lane, height where its line crosses the target plane,
    // outcome) as JSON for the side view page.
    public sealed class VSpread1RangeTests
    {
        private const int PerLane = 1000;
        // First recorded by VSPREAD-1 (a new fixture; the card's estimates are about 187, 102 and 45).
        internal static readonly int[] RecordedHits = { 193, 114, 55 };

        private sealed class Lanes
        {
            internal readonly int[] Shots = new int[VspreadDemo.Lanes], Hits = new int[VspreadDemo.Lanes];
            internal readonly StringBuilder Side = new StringBuilder();
            internal long LastTick;
        }

        private static Lanes Run()
        {
            SimSession s = Range();
            CombatSystem combat = CombatOf(s);
            var lanes = new Lanes();
            var shooters = new StableId[VspreadDemo.Lanes];
            for (int l = 0; l < shooters.Length; l++) shooters[l] = SimOf(s).Entities.StableOf(Unit(s, (ulong)l + 1));
            double plane = VspreadDemo.TargetXMm / 1000.0;
            while (s.Tick < 3000 && Array.Exists(lanes.Shots, n => n < PerLane))
            {
                s.StepOnce();
                if (s.Tick < VspreadDemo.SettleTick) continue;
                foreach (FireRecord f in combat.Fires)
                {
                    int lane = Array.IndexOf(shooters, f.Shooter);
                    if (lane < 0 || lanes.Shots[lane] == PerLane) continue;
                    lanes.Shots[lane]++;
                    if (f.Outcome == FireOutcome.Hit) lanes.Hits[lane]++;
                    double ox = f.Origin.X.Raw / 65536.0, oy = f.Origin.Y.Raw / 65536.0, ex = f.End.X.Raw / 65536.0, ey = f.End.Y.Raw / 65536.0;
                    bool stopped = f.Outcome == FireOutcome.WallBlocked || ex < plane - 0.5;
                    double y = stopped || ex == ox ? ey : oy + (ey - oy) * (plane - ox) / (ex - ox);
                    if (lanes.Side.Length > 0) lanes.Side.Append(",\n");
                    lanes.Side.Append(string.Format(CultureInfo.InvariantCulture, "{{\"lane\":{0},\"y\":{1:F4},\"z\":{2:F4},\"outcome\":\"{3}\",\"short\":{4}}}",
                        lane, y, f.End.Z.Raw / 65536.0 - VspreadDemo.LaneZMm(lane) / 1000.0, f.Outcome, stopped ? "true" : "false"));
                }
            }
            lanes.LastTick = s.Tick;
            return lanes;
        }

        [Test]
        public void V11_V15_StandingCrouchedProne()
        {
            Lanes lanes = Run();
            string path = Environment.GetEnvironmentVariable("VSPREAD1_SIDE_OUT");
            if (!string.IsNullOrEmpty(path))
                File.WriteAllText(path, "{\"plane\":" + (VspreadDemo.TargetXMm / 1000.0).ToString(CultureInfo.InvariantCulture)
                    + ",\"hits\":[" + string.Join(",", lanes.Hits) + "],\"shots\":[\n" + lanes.Side + "\n]}\n");
            for (int l = 0; l < VspreadDemo.Lanes; l++) Assert.That(lanes.Shots[l], Is.EqualTo(PerLane), "lane " + l);
            Assert.That(lanes.Hits[0], Is.EqualTo(RecordedHits[0]), "standing");
            Assert.That(lanes.Hits[1], Is.EqualTo(RecordedHits[1]), "crouched");
            Assert.That(lanes.Hits[2], Is.EqualTo(RecordedHits[2]), "prone");
            // V11: the recorded numbers lie in the card's ranges and standing is hit more than twice as often as prone.
            Assert.That(RecordedHits[0], Is.InRange(140, 260));
            Assert.That(RecordedHits[2], Is.InRange(20, 90));
            Assert.That(RecordedHits[0], Is.GreaterThan(2 * RecordedHits[2]));
            // V15: strictly fewer hits per lower stance.
            Assert.That(RecordedHits[0], Is.GreaterThan(RecordedHits[1]));
            Assert.That(RecordedHits[1], Is.GreaterThan(RecordedHits[2]));
        }
    }
}
