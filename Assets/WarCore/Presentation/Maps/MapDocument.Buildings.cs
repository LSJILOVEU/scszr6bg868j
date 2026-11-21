using System;
using System.Collections.Generic;
using WarCore.Content.MapTools;

namespace WarCore.Presentation
{
    // ML-12a: buildings and upper-floor doors and windows. A building is the stamp "building-<n>" (MapBuildings: walls, door,
    // floor plates, stairs, then the cutouts, one MapEdit.Batch). Its parameters live in the map's .build.json text
    // (MapBuildSpec), which these members take and give back; they read and write no file. Like the other With* members
    // they return a new document and leave this one as it was; on any failure nothing changes.
    public sealed partial class MapDocument
    {
        // Adds a building under a new stamp name. buildJson: the map's current .build.json text, or null when it has none
        // (a new one is started). ArgumentException for bad parameters or a file written for another map.
        public MapBuildResult WithBuilding(string buildJson, MapBuildingInfo building)
        {
            if (building == null) throw new ArgumentNullException(nameof(building));
            MapBuildSpec spec = Spec(buildJson, true);
            MapBuilding b = ToBuilding(building, null);
            string instance = MapEntrySteps.FreeInstance(desc, "building");
            MapDocument doc = Edited(e => e.Batch(x => Run(x, instance, b)));
            return new MapBuildResult(doc, spec.With(instance, b).Write(), instance);
        }

        // Changes a building's floor count. Up: the building is stamped again with more floors (hand-edited entries stay as
        // they are). Down: the entries of the removed floors and the stairs up to them are removed, hand-edited ones too,
        // each leaving a tombstone, and each removed floor gets the next generation so adding it again later works.
        // InvalidOperationException without a .build.json (the building's parameters are lost); ArgumentException for an
        // unknown instance, the same count, or a count outside 1 to 32.
        public MapBuildResult WithFloors(string buildJson, string instance, int floors)
        {
            if (buildJson == null)
                throw new InvalidOperationException("the map has no .build.json, so the building's parameters are unknown and floors cannot change");
            MapBuildSpec spec = Spec(buildJson, false);
            MapBuilding b = spec.Find(instance) ?? throw new ArgumentException("no building " + instance + " in the .build.json", nameof(instance));
            if (floors < 1 || floors > MapBuilding.MaxFloors) throw new ArgumentException("floors must be 1 to " + MapBuilding.MaxFloors, nameof(floors));
            if (floors == b.Floors) throw new ArgumentException("the building already has " + floors + " floors", nameof(floors));
            MapBuilding next;
            MapDocument doc;
            if (floors > b.Floors)
            {
                next = b.WithFloors(floors);
                doc = Edited(e => e.Batch(x => Run(x, instance, next)));
            }
            else
            {
                next = MapBuildings.Lowered(b, floors);
                doc = Edited(e => e.Batch(x =>
                {
                    foreach (KeyValuePair<string, ulong> entry in MapBuildings.Removal(x.Current, instance, floors)) x.Remove(entry.Key, entry.Value);
                }));
            }
            return new MapBuildResult(doc, spec.With(instance, next).Write(), instance);
        }

        // Removes a building: every entry of its stamp (as WithoutStamp) and its .build.json entry.
        public MapBuildResult WithoutBuilding(string buildJson, string instance)
        {
            MapBuildSpec spec = Spec(buildJson, true);
            MapDocument doc = WithoutStamp(instance);
            return new MapBuildResult(doc, spec.Without(instance).Write(), instance);
        }

        // The buildings of a .build.json text, in file order (empty for null).
        public IReadOnlyList<MapBuildingInfo> Buildings(string buildJson)
        {
            if (buildJson == null) return Array.AsReadOnly(new MapBuildingInfo[0]);
            MapBuildSpec spec = Spec(buildJson, false);
            var list = new MapBuildingInfo[spec.Buildings.Count];
            for (int k = 0; k < list.Length; k++) list[k] = ToInfo(spec.Instances[k], spec.Buildings[k]);
            return Array.AsReadOnly(list);
        }

        // MapPrefabs.DoorOn on a floor top (absolute raw Y) as stamp "door-<n>", on the description's first layer, kind "wall".
        public MapDocument WithDoorOn(int startX, int startZ, bool alongX, int length, int doorStart, int doorWidth, long floorTopRaw, long topRaw) =>
            Edited(e => e.Batch(x => x.Stamp(MapPrefabs.DoorOn(x.Current, MapEntrySteps.FreeInstance(x.Current, "door"), new MapXZ(startX, startZ),
                alongX, length, doorStart, doorWidth, floorTopRaw, topRaw, FirstLayer(x.Current), "wall"))));

        // MapPrefabs.WindowOn (default sill and opening) as stamp "window-<n>". Needs the combat body of this description's
        // layer: a layer without one throws InvalidOperationException.
        public MapDocument WithWindowOn(int startX, int startZ, bool alongX, int length, int windowStart, int windowWidth, long floorTopRaw, long topRaw) =>
            Edited(e => e.Batch(x => x.Stamp(MapPrefabs.WindowOn(x.Current, MapEntrySteps.FreeInstance(x.Current, "window"), new MapXZ(startX, startZ),
                alongX, length, windowStart, windowWidth, floorTopRaw, topRaw, FirstLayer(x.Current), "wall", MapEntrySteps.BodyOf(x.Current.Pack)))));

        private static void Run(MapEdit edit, string instance, MapBuilding b)
        {
            edit.Stamp(MapBuildings.Structure(edit.Current, instance, b));
            edit.Stamp(MapBuildings.Cutouts(edit.Current, instance, b));
        }

        private MapBuildSpec Spec(string buildJson, bool allowNull)
        {
            MapBuildSpec spec = buildJson == null && allowNull ? new MapBuildSpec(desc.Size.X, desc.Size.Z, desc.Pack) : MapBuildSpec.Read(buildJson);
            spec.CheckFor(desc);
            return spec;
        }

        private static string FirstLayer(MapDesc d)
        {
            if (d.Layers.Count == 0) throw new InvalidOperationException("the description has no layer");
            return d.Layers[0].Name;
        }

        private static MapBuilding ToBuilding(MapBuildingInfo i, int[] generations)
        {
            if (i.DoorSide.Length != 1) throw new ArgumentException("the door side must be n, e, s or w", nameof(i));
            return new MapBuilding(new MapXZ(i.CellX, i.CellZ), new MapXZ(i.SizeX, i.SizeZ), i.Floors, i.StoreyRaw, i.ThickRaw, i.DoorSide[0],
                i.DoorAt, i.DoorWidth, i.StairAlongX, new MapXZ(i.StairX, i.StairZ), i.StairLength, generations);
        }

        private static MapBuildingInfo ToInfo(string instance, MapBuilding b) =>
            new MapBuildingInfo(instance, b.Cell.X, b.Cell.Z, b.Size.X, b.Size.Z, b.Floors, b.StoreyRaw, b.ThickRaw, b.DoorSide.ToString(),
                b.DoorAt, b.DoorWidth, b.StairAlongX, b.StairCell.X, b.StairCell.Z, b.StairLength);
    }
}
