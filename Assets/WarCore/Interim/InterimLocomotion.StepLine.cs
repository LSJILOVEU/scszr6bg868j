using System;
using WarCore.Sim;

namespace WarCore.Interim
{
    // SLOPE-1: the foot step ("B" of the speed line), after inertia. One product: surface x slope x registered drags, the
    // drags' product clamped by the step line's floor and ceiling, divided once (StatLine.Apply). The slope is the rise of the
    // ground 0.5 m ahead along the step's own heading, looked up in the content's SlopeTable.
    // Off by default: without a slope table, clamps or registered drags no step enters this file, so the old two-line
    // formulas run as they were. A blocked cell (surface 0) never reaches the stat line either. Nothing here is saved: the
    // configuration is content (the world's content key names it) and the drags read saved components.
    public sealed partial class InterimLocomotion
    {
        // Surface and slope take two of the stat line's seven factors.
        public const int MaxStepDrags = StatLine.MaxFactors - 2;
        public const int MaxStepFloorPermille = 1000, MinStepCeilPermille = 1000;

        private SlopeTable slopeTable;
        private int stepFloorPermille, stepCeilPermille;
        private readonly IStepDrag[] stepDrags = new IStepDrag[MaxStepDrags];
        private int stepDragCount;
        // Scratch for the factors of one step, never state.
        private readonly int[] stepFactors = new int[StatLine.MaxFactors];

        // The slope table (null: no slope) and the clamps of the drags' product, permille, 0 = none. The floor is at most 1000
        // and the ceiling at least 1000, so a unit whose every factor is 1000 is never clamped, and a step stays at most twice
        // its base.
        public void SetStepLine(SlopeTable slope, int dragFloorPermille, int dragCeilPermille)
        {
            if (dragFloorPermille != 0 && (dragFloorPermille < StatLine.MinPermille || dragFloorPermille > MaxStepFloorPermille))
                throw new ArgumentOutOfRangeException(nameof(dragFloorPermille));
            if (dragCeilPermille != 0 && (dragCeilPermille < MinStepCeilPermille || dragCeilPermille > StatLine.MaxPermille))
                throw new ArgumentOutOfRangeException(nameof(dragCeilPermille));
            slopeTable = slope;
            stepFloorPermille = dragFloorPermille;
            stepCeilPermille = dragCeilPermille;
        }

        public SlopeTable StepSlope => slopeTable;
        public int StepFloorPermille => stepFloorPermille;
        public int StepCeilPermille => stepCeilPermille;
        public int StepDragCount => stepDragCount;

        // Registers one drag of the foot step, in order. Also on a flat world (no terrain): there only the drags and the clamps
        // act. At most MaxStepDrags.
        public void AddStepDrag(IStepDrag drag)
        {
            if (drag == null) throw new ArgumentNullException(nameof(drag));
            if (stepDragCount >= MaxStepDrags)
                throw new InvalidOperationException("The foot step takes at most " + MaxStepDrags + " registered drags.");
            stepDrags[stepDragCount++] = drag;
        }

        private bool StepLineOn => slopeTable != null || stepFloorPermille != 0 || stepCeilPermille != 0 || stepDragCount != 0;

        // The read-only receipt of a step of `unit` from where it stands, heading along `heading` with base `baseStep`: the same
        // factors and the same stat line as the real step, no random draw, no write. An AI unit passes its step after inertia
        // (the result leaves out the one-raw minimum); the player passes one axis's absolute displacement per call.
        public StepReceipt ExplainStep(EntityId unit, SimVec3 heading, SimFix baseStep)
        {
            SimVec3 point = entities.Get(positions, unit).Value;
            int layer = layered ? LayerOf(unit) : 0;
            int milli = SurfaceMilliAt(layer, point);
            if (milli == 0) return new StepReceipt(true, null);
            int count = StepFactors(unit, point, layer, heading, milli);
            return new StepReceipt(false, StatLine.Explain(baseStep.Raw, ReadOnlySpan<int>.Empty,
                new ReadOnlySpan<int>(stepFactors, 0, count), stepFloorPermille, stepCeilPermille));
        }

        // An AI unit's terrain step through the line (StepOf, line on, surface not blocked): at least one raw, at most 0.5 m.
        private SimFix StepLineOf(EntityId unit, SimVec3 point, SimFix baseStep, int layer, SimVec3 heading, long milli)
        {
            int count = StepFactors(unit, point, layer, heading, (int)milli);
            long length = StatLine.Apply(baseStep.Raw, ReadOnlySpan<int>.Empty, new ReadOnlySpan<int>(stepFactors, 0, count),
                stepFloorPermille, stepCeilPermille);
            if (length < 1) length = 1;
            if (length > MaxEffectiveStepRaw) throw new InvalidOperationException("A terrain step through the step line exceeds 0.5 m.");
            return SimFix.FromRaw(length);
        }

        // The controlled unit's terrain displacement through the line (ControlledTerrainCandidate, line on, surface not
        // blocked): probed once along the whole displacement, the same factors on both axes, each axis's sign and magnitude
        // apart (the old product's rounding toward zero). Each axis is at most 0.25 m before the product, so the result is at
        // most 0.5 m per axis; today only the stance gear (at most 1000) acts before this, the check guards later gears.
        private SimVec3 ControlledStepLine(int i, SimVec3 displacement, long milli)
        {
            long ax = displacement.X.Raw < 0 ? -displacement.X.Raw : displacement.X.Raw;
            long az = displacement.Z.Raw < 0 ? -displacement.Z.Raw : displacement.Z.Raw;
            if (ax > MaxTerrainStepRaw || az > MaxTerrainStepRaw)
                throw new InvalidOperationException("A controlled step through the step line is at most 0.25 m per axis.");
            EntityId unit = provisional[i].Unit;
            int count = StepFactors(unit, provisional[i].Point, layered ? LayerOf(unit) : 0, displacement, (int)milli);
            return AxesThroughLine(displacement, count);
        }

        // A flat world's AI step through the registered drags and the clamps (a positive step stays at least one raw);
        // unchanged when no drag is registered.
        private SimFix FlatStep(EntityId unit, SimFix speed)
        {
            if (stepDragCount == 0) return speed;
            int count = StepFactors(unit, default, 0, default, 1000);
            long length = StatLine.Apply(speed.Raw, ReadOnlySpan<int>.Empty, new ReadOnlySpan<int>(stepFactors, 0, count),
                stepFloorPermille, stepCeilPermille);
            return SimFix.FromRaw(speed.Raw > 0 && length < 1 ? 1 : length);
        }

        // A flat world's controlled displacement through the registered drags and the clamps, axis by axis; unchanged on a
        // terrain world (its own step line runs later, once) or when no drag is registered.
        private SimVec3 FlatControlled(int i, SimVec3 displacement)
        {
            if (terrain != null || stepDragCount == 0) return displacement;
            return AxesThroughLine(displacement, StepFactors(provisional[i].Unit, default, 0, default, 1000));
        }

        private SimVec3 AxesThroughLine(SimVec3 displacement, int count)
        {
            var drags = new ReadOnlySpan<int>(stepFactors, 0, count);
            long x = StatLine.Apply(displacement.X.Raw, ReadOnlySpan<int>.Empty, drags, stepFloorPermille, stepCeilPermille);
            long z = StatLine.Apply(displacement.Z.Raw, ReadOnlySpan<int>.Empty, drags, stepFloorPermille, stepCeilPermille);
            return new SimVec3(SimFix.FromRaw(x), default, SimFix.FromRaw(z));
        }

        // The fixed drags of one step into stepFactors: [surface, slope, registered drags in order]. Returns their count.
        private int StepFactors(EntityId unit, SimVec3 point, int layer, SimVec3 heading, int surfaceMilli)
        {
            stepFactors[0] = surfaceMilli;
            stepFactors[1] = SlopeFactor(point, layer, heading);
            for (int k = 0; k < stepDragCount; k++)
            {
                int value = stepDrags[k].Permille(unit);
                if (value < StatLine.MinPermille || value > 1000)
                    throw new InvalidOperationException("A registered step drag must be 1 to 1000 permille.");
                stepFactors[2 + k] = value;
            }
            return 2 + stepDragCount;
        }

        // 1000 without a slope table, without a heading, or where either ground height cannot be read (outside the height
        // field, off this plate, no height field). The probe is the point plus the horizontal heading made unit and halved
        // per axis toward zero (raw * 32768 / 65536); walls and units are not looked at.
        private int SlopeFactor(SimVec3 point, int layer, SimVec3 heading)
        {
            if (slopeTable == null || (heading.X.Raw == 0 && heading.Z.Raw == 0)) return 1000;
            SimVec3 along = new SimVec3(heading.X, default, heading.Z).Normalized;
            SimFix px = SimFix.FromRaw(point.X.Raw + along.X.Raw * SlopeTable.ProbeRaw / SimFix.Scale);
            SimFix pz = SimFix.FromRaw(point.Z.Raw + along.Z.Raw * SlopeTable.ProbeRaw / SimFix.Scale);
            if (!GroundAt(layer, point.X, point.Z, out SimFix here) || !GroundAt(layer, px, pz, out SimFix ahead)) return 1000;
            long rise = ahead.Raw - here.Raw;
            if (rise == 0) return 1000;
            return slopeTable.SpeedPermille(SlopeTable.SlopePermille(rise), rise > 0);
        }

        // One height function for both points: the layer's floor on a plate world, else the height field.
        private bool GroundAt(int layer, SimFix x, SimFix z, out SimFix y)
        {
            if (layered) return Floor(layer, x, z, out y);
            if (ground != null) return ground.TryHeightAt(x, z, out y);
            y = default;
            return false;
        }

        // The surface factor under a point, as the step reads it: 1000 on a flat world or off the grid.
        private int SurfaceMilliAt(int layer, SimVec3 point)
        {
            if (terrain == null) return 1000;
            if (layered) return PlateSpeedMilli(layer, point);
            return grid.TryCell(point, out int cell) ? terrain.SpeedMilliAt(cell) : 1000;
        }
    }
}
