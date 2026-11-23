using System;
using System.Collections.Generic;
using WarCore.Combat;
using WarCore.Content.MapTools;
using WarCore.Sim;

namespace WarCore.Content
{
    // SCALE-2b: the standard parts of a content pack (section "scale"), in whole millimetres as written and in raw
    // (1 m = 65536). Millimetres become raw with SimFix.Ratio (truncated); the five values used as wall or storey heights
    // (storey, the three wall classes, railing) then grow 1 raw when odd, because wall heights must be even and padding
    // only goes up (as the window prefab pads its sill). Plate, window sill, window top (the prefab pads those itself) and
    // the street widths are not padded. Every relation is checked on the padded values.
    // These are recommended sizes, not legal ones: the editor gate never reads them, so a hand-made wall of another height
    // stays legal. The checks use the same constants as the gate and the prefabs, so a part they accept builds.
    public sealed class ScaleRules
    {
        public const int MaxMm = 100000;
        // The stair direction needs n + 4 cells inside a map side of at most HeightField.MaxSide cells.
        public const int MaxStairCells = HeightField.MaxSide - 4;

        // The values a pack without the section gets (checked against no body).
        public static readonly ScaleRules Default = new ScaleRules(3000, 500, 1000, 1400, 2500, 900, 2000, 1000, 175, 280, 3250, 2000, 3000);

        public int StoreyMm { get; }
        public int PlateMm { get; }
        public int LowWallMm { get; }
        public int ChestWallMm { get; }
        public int HighWallMm { get; }
        public int WindowSillMm { get; }
        public int WindowTopMm { get; }
        public int RailingMm { get; }
        public int StairRiseMm { get; }
        public int StairRunMm { get; }
        public int LaneMm { get; }
        public int SidewalkMm { get; }
        public int AlleyMm { get; }

        public long StoreyRaw { get; }
        public long PlateRaw { get; }
        public long LowWallRaw { get; }
        public long ChestWallRaw { get; }
        public long HighWallRaw { get; }
        public long WindowSillRaw { get; }
        public long WindowTopRaw { get; }
        public long RailingRaw { get; }
        public long LaneRaw { get; }
        public long SidewalkRaw { get; }
        public long AlleyRaw { get; }

        // Each value 1 to MaxMm (the section reader enforces it).
        internal ScaleRules(int storeyMm, int plateMm, int lowWallMm, int chestWallMm, int highWallMm, int windowSillMm, int windowTopMm,
            int railingMm, int stairRiseMm, int stairRunMm, int laneMm, int sidewalkMm, int alleyMm)
        {
            StoreyMm = storeyMm; PlateMm = plateMm; LowWallMm = lowWallMm; ChestWallMm = chestWallMm; HighWallMm = highWallMm;
            WindowSillMm = windowSillMm; WindowTopMm = windowTopMm; RailingMm = railingMm; StairRiseMm = stairRiseMm; StairRunMm = stairRunMm;
            LaneMm = laneMm; SidewalkMm = sidewalkMm; AlleyMm = alleyMm;
            StoreyRaw = Even(Raw(storeyMm)); PlateRaw = Raw(plateMm);
            LowWallRaw = Even(Raw(lowWallMm)); ChestWallRaw = Even(Raw(chestWallMm)); HighWallRaw = Even(Raw(highWallMm));
            WindowSillRaw = Raw(windowSillMm); WindowTopRaw = Raw(windowTopMm); RailingRaw = Even(Raw(railingMm));
            LaneRaw = Raw(laneMm); SidewalkRaw = Raw(sidewalkMm); AlleyRaw = Raw(alleyMm);
        }

        internal static long Raw(int mm) => SimFix.Ratio(mm, 1000).Raw;

        private static long Even(long raw) => (raw & 1) != 0 ? raw + 1 : raw;

        // The pack's section, or Default when no layer wrote one.
        public static ScaleRules Of(ContentPack pack)
        {
            if (pack == null) throw new ArgumentNullException(nameof(pack));
            return pack.SectionOrNull<ScaleRules>() ?? Default;
        }

        // Stair cells of a standard building with this many floors: the fewest cells whose rise per cell, ceil(storey / n),
        // is at most k = floor(stairRise * 1 m / stairRun), and with three floors or more also at most storey - plate - 2 m
        // (stairs stand over each other). One floor has no stair plate and gets 1 (a building needs a stair of one cell).
        // ArgumentException when floors is outside 1 to MapBuilding.MaxFloors, when k is not positive, or when more than
        // MaxStairCells cells are needed. The section's checks keep stairRise / stairRun within the layer's slope and 5/3,
        // so MapBuildings accepts the result.
        public int StairCells(int floors)
        {
            if (floors < 1 || floors > MapBuilding.MaxFloors) throw new ArgumentException("floors must be 1 to " + MapBuilding.MaxFloors, nameof(floors));
            if (floors == 1) return 1;
            long k = (long)StairRiseMm * SimFix.Scale / StairRunMm;
            if (floors >= 3) k = Math.Min(k, StoreyRaw - PlateRaw - WallSpan.ClearanceRaw);
            if (k <= 0) throw new ArgumentException("no stair fits: the rise per cell would be 0 raw", nameof(floors));
            long n = (StoreyRaw + k - 1) / k;
            if (n > MaxStairCells) throw new ArgumentException("the stair needs " + n + " cells, more than " + MaxStairCells, nameof(floors));
            return (int)n;
        }

        // The section's relations against one body (raw). r = the body's radius (HC-1); E, M, H = the standing body's eye, muzzle, height;
        // L = the highest eye or muzzle of any stance, and without stances L + r is 2r; the slope is the layer's walkable
        // slope (its terrain rules, else the height field's 2/1). One text per failing row, in the documented order
        // (docs/world-scale.md, section "Content section scale"); empty when every relation holds.
        internal static List<string> Problems(ScaleRules s, CombatBody body, IReadOnlyList<StanceDef> stances, long slopeNum, long slopeDen,
            TraversalRules traversal)
        {
            var problems = new List<string>();
            long r = body.Radius.Raw, eye = body.Eye.Raw, muzzle = body.Muzzle.Raw, height = body.Height.Raw;
            long low = Math.Min(eye, muzzle), high = Math.Max(eye, muzzle);
            long lowest = r;
            string lowestText = "2r";
            if (stances != null && stances.Count > 0)
            {
                lowest = 0;
                lowestText = "highest stance eye or muzzle + r";
                foreach (StanceDef stance in stances) lowest = Math.Max(lowest, Math.Max(stance.Body.Eye.Raw, stance.Body.Muzzle.Raw));
            }

            if (s.LowWallRaw < lowest + r || s.LowWallRaw > low - r)
                problems.Add(Item("lowWallMm", s.LowWallRaw, s.LowWallMm, "must be within [" + (lowest + r) + ", " + (low - r)
                    + "] raw (" + lowestText + ", min(eye, muzzle) - r)"));
            if (s.ChestWallRaw <= s.LowWallRaw || s.ChestWallRaw >= low)
                problems.Add(Item("chestWallMm", s.ChestWallRaw, s.ChestWallMm, "must be above lowWall " + s.LowWallRaw + " raw and below min(eye, muzzle) " + low + " raw"));
            if (s.HighWallRaw < height)
                problems.Add(Item("highWallMm", s.HighWallRaw, s.HighWallMm, "must be at least the body height " + height + " raw"));
            if (s.WindowSillRaw < 2 * r || s.WindowSillRaw + 1 > low - r)
                problems.Add(Item("windowSillMm", s.WindowSillRaw, s.WindowSillMm, "must be within [" + 2 * r + ", " + (low - r - 1)
                    + "] raw (2r, min(eye, muzzle) - r - 1 for the prefab's odd-height padding)"));
            if (s.WindowTopRaw < high + r || s.WindowTopRaw > s.StoreyRaw - s.PlateRaw)
                problems.Add(Item("windowTopMm", s.WindowTopRaw, s.WindowTopMm, "must be within [" + (high + r) + ", " + (s.StoreyRaw - s.PlateRaw)
                    + "] raw (max(eye, muzzle) + r, storey - plate)"));
            if (s.PlateRaw < WcMapPlate.MinThickRaw)
                problems.Add(Item("plateMm", s.PlateRaw, s.PlateMm, "must be at least " + WcMapPlate.MinThickRaw + " raw"));
            if (s.StoreyRaw - s.PlateRaw < WallSpan.ClearanceRaw)
                problems.Add(Item("storeyMm", s.StoreyRaw, s.StoreyMm, "minus plate " + s.PlateRaw + " raw must be at least the headroom " + WallSpan.ClearanceRaw + " raw"));
            if ((long)s.StairRiseMm * slopeDen > slopeNum * s.StairRunMm
                || (long)s.StairRiseMm * MapGate.PlateSlopeDen > (long)MapGate.PlateSlopeNum * s.StairRunMm)
                problems.Add("stairRiseMm/stairRunMm " + s.StairRiseMm + "/" + s.StairRunMm + " mm must not be steeper than the layer's slope "
                    + slopeNum + "/" + slopeDen + " nor " + MapGate.PlateSlopeNum + "/" + MapGate.PlateSlopeDen);
            if (s.RailingRaw < 2 * r || s.RailingRaw > low - r)
                problems.Add(Item("railingMm", s.RailingRaw, s.RailingMm, "must be within [" + 2 * r + ", " + (low - r) + "] raw (2r, min(eye, muzzle) - r)"));
            Street(problems, "laneMm", s.LaneMm);
            Street(problems, "sidewalkMm", s.SidewalkMm);
            Street(problems, "alleyMm", s.AlleyMm);

            if (traversal != null && HasOver(traversal))
            {
                if (traversal.FirstMatch(TraversalShape.Over, s.LowWallRaw, s.LowWallRaw, LinkDerive.NoGap) < 0)
                    problems.Add(Item("lowWallMm", s.LowWallRaw, s.LowWallMm, "standing alone on flat ground matches no over traversal kind"));
                int k = traversal.FirstMatch(TraversalShape.Over, s.HighWallRaw, s.HighWallRaw, LinkDerive.NoGap);
                if (k >= 0)
                    problems.Add(Item("highWallMm", s.HighWallRaw, s.HighWallMm, "standing alone on flat ground matches over traversal kind '" + traversal[k].Id + "'"));
            }
            return problems;
        }

        private static bool HasOver(TraversalRules traversal)
        {
            for (int i = 0; i < traversal.Count; i++)
                if (traversal[i].Shape == TraversalShape.Over) return true;
            return false;
        }

        // At least one cell wide.
        private static void Street(List<string> problems, string field, int mm)
        {
            if (mm < 1000) problems.Add(field + " " + mm + " mm must be at least 1000 mm (one cell)");
        }

        private static string Item(string field, long raw, int mm, string rule) => field + " " + raw + " raw (" + mm + " mm) " + rule;
    }
}
