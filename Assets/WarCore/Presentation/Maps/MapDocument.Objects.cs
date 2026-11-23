using System;
using WarCore.Content.MapTools;

namespace WarCore.Presentation
{
    // ED-6a write side: entries, door walls, windows and squads. Each member is one MapEdit.Batch and returns a new document,
    // leaving this one as it was; every check is MapEdit's (off the map, sizes, a cutout's plate), with its exception type.
    // Ids: an added entry takes NextId of its kind (read it first); Replace and the moves keep the id and src and make the
    // owner hand (MapEdit.Replace). kind is "wall", "point", "base", "unit", "plate" or "cutout".
    public sealed partial class MapDocument
    {
        public MapDocument WithWallAdded(MapWallInfo wall) => Batched(MapEntrySteps.AddWall(wall));
        public MapDocument WithPointAdded(MapPointInfo point) => Batched(MapEntrySteps.AddPoint(point));
        public MapDocument WithBaseAdded(MapBaseInfo anchor) => Batched(MapEntrySteps.AddBase(anchor));
        public MapDocument WithUnitAdded(MapUnitInfo unit) => Batched(MapEntrySteps.AddUnit(unit));
        public MapDocument WithPlateAdded(MapPlateInfo plate) => Batched(MapEntrySteps.AddPlate(plate));
        public MapDocument WithCutoutAdded(MapCutoutInfo cutout) => Batched(MapEntrySteps.AddCutout(cutout));

        public MapDocument WithWallReplaced(MapWallInfo wall) => Batched(MapEntrySteps.ReplaceWall(wall));
        public MapDocument WithPointReplaced(MapPointInfo point) => Batched(MapEntrySteps.ReplacePoint(point));
        public MapDocument WithBaseReplaced(MapBaseInfo anchor) => Batched(MapEntrySteps.ReplaceBase(anchor));
        public MapDocument WithUnitReplaced(MapUnitInfo unit) => Batched(MapEntrySteps.ReplaceUnit(unit));
        public MapDocument WithPlateReplaced(MapPlateInfo plate) => Batched(MapEntrySteps.ReplacePlate(plate));
        public MapDocument WithCutoutReplaced(MapCutoutInfo cutout) => Batched(MapEntrySteps.ReplaceCutout(cutout));

        // A wall, plate or cutout moved by whole cells; another kind throws ArgumentException.
        public MapDocument WithCellsMoved(string kind, ulong id, int dx, int dz) => Batched(MapEntrySteps.MoveCells(kind, id, dx, dz));

        // A point (with its spawn), base or unit moved by millimetres; another kind throws ArgumentException.
        public MapDocument WithMmMoved(string kind, ulong id, long dxMm, long dzMm) => Batched(MapEntrySteps.MoveMm(kind, id, dxMm, dzMm));

        // A copy moved by dx, dz: cells for walls, plates and cutouts, millimetres for points, bases and units. The copy takes
        // NextId, is owned by hand and has no src; a copied cutout cuts the same plate.
        public MapDocument WithCopy(string kind, ulong id, long dx, long dz) => Batched(MapEntrySteps.Copy(kind, id, dx, dz));

        // A one-cell-thick wall with a doorway (MapPrefabs.Door) on the first layer, as stamp "door-<n>" (n the smallest free).
        public MapDocument WithDoorWall(int startX, int startZ, bool alongX, int length, int doorStart, int doorWidth, long topRaw) =>
            Batched(MapEntrySteps.DoorWall(startX, startZ, alongX, length, doorStart, doorWidth, topRaw));

        // A wall with a window (MapPrefabs.Window, the layer's scale, defaults without one) as stamp "window-<n>". The window's gate needs
        // the combat body of this description's layer: a layer without one throws InvalidOperationException.
        public MapDocument WithWindow(int startX, int startZ, bool alongX, int length, int windowStart, int windowWidth, long topRaw) =>
            Batched(MapEntrySteps.Window(startX, startZ, alongX, length, windowStart, windowWidth, topRaw));

        // Removes every entry stamped by the instance, hand-edited ones included, each leaving a tombstone; ArgumentException
        // when there is none. Heights, surfaces, owners and the stamps table do not change.
        public MapDocument WithoutStamp(string instance) => Batched(MapEntrySteps.RemoveStamp(instance));

        // count (1 to 100) units of one type, team and squad in the 1 m formation around the centre (millimetres), keys from
        // NextId("unit"). The type is not checked against the layer (the gate's GATE_REF does that).
        public MapDocument WithSquad(string type, int team, int squad, long centerXMm, long centerZMm, int count, bool reserve) =>
            Batched(MapEntrySteps.Squad(type, team, squad, centerXMm, centerZMm, count, reserve));

        // Every write of the list in order as one batch: the bytes of the same writes done one by one, or, when one is
        // refused, that write's exception and no new document.
        public MapDocument WithEntryEdits(MapEntryEditList edits)
        {
            if (edits == null) throw new ArgumentNullException(nameof(edits));
            return Edited(e => e.Batch(b => edits.ApplyTo(b)));
        }

        private MapDocument Batched(Action<MapEdit> step) => Edited(e => e.Batch(step));
    }
}
