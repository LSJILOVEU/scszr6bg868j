using System;
using System.Collections.Generic;
using WarCore.Content.MapTools;

namespace WarCore.Presentation
{
    // A list of entry writes for MapDocument.WithEntryEdits, kept in the order they were added (a multi-selection edit).
    // Each method takes the arguments of the MapDocument member of the same meaning and follows its rules; the checks run
    // when the batch runs, against the state the writes before it left.
    public sealed class MapEntryEditList
    {
        private readonly List<Action<MapEdit>> steps = new List<Action<MapEdit>>();

        public int Count => steps.Count;

        public MapEntryEditList AddWall(MapWallInfo wall) => Add(MapEntrySteps.AddWall(wall));
        public MapEntryEditList AddPoint(MapPointInfo point) => Add(MapEntrySteps.AddPoint(point));
        public MapEntryEditList AddBase(MapBaseInfo anchor) => Add(MapEntrySteps.AddBase(anchor));
        public MapEntryEditList AddUnit(MapUnitInfo unit) => Add(MapEntrySteps.AddUnit(unit));
        public MapEntryEditList AddPlate(MapPlateInfo plate) => Add(MapEntrySteps.AddPlate(plate));
        public MapEntryEditList AddCutout(MapCutoutInfo cutout) => Add(MapEntrySteps.AddCutout(cutout));

        public MapEntryEditList ReplaceWall(MapWallInfo wall) => Add(MapEntrySteps.ReplaceWall(wall));
        public MapEntryEditList ReplacePoint(MapPointInfo point) => Add(MapEntrySteps.ReplacePoint(point));
        public MapEntryEditList ReplaceBase(MapBaseInfo anchor) => Add(MapEntrySteps.ReplaceBase(anchor));
        public MapEntryEditList ReplaceUnit(MapUnitInfo unit) => Add(MapEntrySteps.ReplaceUnit(unit));
        public MapEntryEditList ReplacePlate(MapPlateInfo plate) => Add(MapEntrySteps.ReplacePlate(plate));
        public MapEntryEditList ReplaceCutout(MapCutoutInfo cutout) => Add(MapEntrySteps.ReplaceCutout(cutout));

        // MapDocument.WithCellsMoved, WithMmMoved, WithCopy, WithoutEntry.
        public MapEntryEditList MoveCells(string kind, ulong id, int dx, int dz) => Add(MapEntrySteps.MoveCells(kind, id, dx, dz));
        public MapEntryEditList MoveMm(string kind, ulong id, long dxMm, long dzMm) => Add(MapEntrySteps.MoveMm(kind, id, dxMm, dzMm));
        public MapEntryEditList Copy(string kind, ulong id, long dx, long dz) => Add(MapEntrySteps.Copy(kind, id, dx, dz));
        public MapEntryEditList Remove(string kind, ulong id) => Add(MapEntrySteps.Remove(kind, id));

        // MapDocument.WithDoorWall, WithWindow, WithoutStamp, WithSquad.
        public MapEntryEditList DoorWall(int startX, int startZ, bool alongX, int length, int doorStart, int doorWidth, long topRaw) =>
            Add(MapEntrySteps.DoorWall(startX, startZ, alongX, length, doorStart, doorWidth, topRaw));

        public MapEntryEditList Window(int startX, int startZ, bool alongX, int length, int windowStart, int windowWidth, long topRaw) =>
            Add(MapEntrySteps.Window(startX, startZ, alongX, length, windowStart, windowWidth, topRaw));

        public MapEntryEditList RemoveStamp(string instance) => Add(MapEntrySteps.RemoveStamp(instance));

        public MapEntryEditList Squad(string type, int team, int squad, long centerXMm, long centerZMm, int count, bool reserve) =>
            Add(MapEntrySteps.Squad(type, team, squad, centerXMm, centerZMm, count, reserve));

        internal void ApplyTo(MapEdit edit)
        {
            foreach (Action<MapEdit> step in steps) step(edit);
        }

        private MapEntryEditList Add(Action<MapEdit> step)
        {
            steps.Add(step);
            return this;
        }
    }
}
