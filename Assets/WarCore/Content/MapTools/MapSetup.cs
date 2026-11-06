using System.Collections.Generic;
using System.Collections.ObjectModel;
using WarCore.Battle;

namespace WarCore.Content.MapTools
{
    // A unit of the description as plain values; the unit type id is not resolved here.
    public sealed class MapSoldier
    {
        public ulong Key { get; }
        public string Type { get; }
        public int Team { get; }
        public int Squad { get; }
        public long XMm { get; }
        public long ZMm { get; }
        public bool Reserve { get; }
        public MapSoldier(ulong key, string type, int team, int squad, long xMm, long zMm, bool reserve)
        {
            Key = key; Type = type; Team = team; Squad = squad; XMm = xMm; ZMm = zMm; Reserve = reserve;
        }
    }

    // Match and run data baked from a description: control points and base anchors (Y on the ground), soldiers, run settings.
    public sealed class MapSetup
    {
        private readonly ControlPointDef[] points;
        private readonly SpawnAnchor[] bases;
        public ControlPointDef[] ControlPoints => (ControlPointDef[])points.Clone();
        public SpawnAnchor[] Bases => (SpawnAnchor[])bases.Clone();
        public IReadOnlyList<MapSoldier> Soldiers { get; }
        public ulong Seed { get; }
        public string CrowdPass { get; }

        internal MapSetup(ControlPointDef[] points, SpawnAnchor[] bases, MapSoldier[] soldiers, ulong seed, string crowdPass)
        {
            this.points = points; this.bases = bases;
            Soldiers = new ReadOnlyCollection<MapSoldier>(soldiers);
            Seed = seed; CrowdPass = crowdPass;
        }
    }
}
