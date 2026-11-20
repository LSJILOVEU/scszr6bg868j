using System;
using WarCore.Sim;

namespace WarCore.Interim
{
    // Floor plates (worlds whose map has plates, always on the terrain branch): every unit has a VerticalState. A two-argument
    // order is an order on the ground and clears the target layer; an order onto a plate records the target layer. Routes run
    // over the layered graph from the unit's own layer; each step that enters a new cell takes its layer from TryEnter, the
    // one place that changes a unit's layer, and its height from that layer's floor.
    public sealed partial class InterimLocomotion
    {
        private ComponentType<VerticalState> vertical;
        private bool layered;
        private LayeredNav layers;
        private LayeredPathfinder layeredFinder;
        private HeightField[] tops;
        private SimFix flatFloor;
        private uint footAnchoredLayers;
        // Scratch, not state: the layer of the last candidate Settle placed, and the open-cell rule of the last clearance test.
        private int candidateLayer;
        private OpenCells openCells;

        public void BindPlates(ComponentType<VerticalState> component, LayeredNav nav, HeightField[] plateTops, SimFix flatGround,
            uint footAnchored)
        {
            if (terrain == null) throw new InvalidOperationException("Floor plates need the terrain branch.");
            if (nav == null) throw new ArgumentNullException(nameof(nav));
            if (plateTops == null || plateTops.Length != nav.LayerCount - 1) throw new ArgumentException("One top field per plate.", nameof(plateTops));
            if (nav.Width != grid.Width || nav.Depth != grid.Depth) throw new ArgumentException("The layered graph must cover the terrain grid.", nameof(nav));
            vertical = component;
            layers = nav;
            layeredFinder = new LayeredPathfinder(nav);
            tops = (HeightField[])plateTops.Clone();
            flatFloor = flatGround;
            footAnchoredLayers = footAnchored;
            openCells = new OpenCells(this);
            layered = true;
        }

        public void Order(EntityId unit, SimVec3 target, int layer)
        {
            VerticalState.Checked(layer, nameof(layer));
            if (layer == 0) { Order(unit, target); return; }
            if (!layered) throw new NotSupportedException("Layer orders need a world with floor plates.");
            MoveIntent intent = RequireIntent(unit);
            SetTargetLayer(unit, layer);
            Route(unit, intent, Standable(layer, OnLayer(layer, target)));
        }

        // How far (in cells) a plate order's point may move to find a place to stand.
        internal const int SnapCells = 8;

        // A plate order's point, as a squad slot may give it (off the plate, on a cutout or a wall, or with the footprint over
        // an edge), moves to where a unit can stand on that layer: kept when its cell is walkable and its footprint stands on
        // cells open for that cell; else that cell's centre when walkable; else the centre of the nearest walkable cell of the
        // layer (rings of growing Chebyshev distance up to SnapCells, each ring in z then x order). Unchanged when none is found.
        private SimVec3 Standable(int layer, SimVec3 p)
        {
            int cx = (int)((p.X.Raw - grid.OriginX.Raw) >> 16), cz = (int)((p.Z.Raw - grid.OriginZ.Raw) >> 16);
            if (layers.TryNode(layer, cx, cz, out int own) && layers.IsPassable(own))
            {
                int key = cz * grid.Width + cx;
                openCells.Set(layer, key, layer, key);
                return grid.SegmentClear(p, p, UnitFootprint, openCells) ? p : NodeCentre(own);
            }
            for (int r = 1; r <= SnapCells; r++)
                for (int z = cz - r; z <= cz + r; z++)
                    for (int x = cx - r; x <= cx + r; x++)
                    {
                        if (z != cz - r && z != cz + r && x != cx - r && x != cx + r) continue;
                        if (layers.TryNode(layer, x, z, out int node) && layers.IsPassable(node)) return NodeCentre(node);
                    }
            return p;
        }

        private void SetTargetLayer(EntityId unit, int layer) =>
            entities.Set(vertical, unit, entities.Read(vertical, unit).WithTarget(layer));

        private int LayerOf(EntityId unit) => entities.Read(vertical, unit).Layer;

        // The accepted candidate's layer becomes the unit's layer (before any re-plan from the new point).
        private void CommitLayer(EntityId unit)
        {
            VerticalState state = entities.Read(vertical, unit);
            if (state.Layer != candidateLayer) entities.Set(vertical, unit, VerticalState.Raw((ushort)candidateLayer, state.TargetLayer));
        }

        // The floor of a layer under (x, z): the ground (its height field, or the flat floor) or a plate's top field.
        private bool Floor(int layer, SimFix x, SimFix z, out SimFix y)
        {
            if (layer > 0) return tops[layer - 1].TryHeightAt(x, z, out y);
            if (ground != null) return ground.TryHeightAt(x, z, out y);
            y = flatFloor;
            return true;
        }

        private SimVec3 OnLayer(int layer, SimVec3 p) => Floor(layer, p.X, p.Z, out SimFix y) ? new SimVec3(p.X, y, p.Z) : p;

        private bool Walkable(int layer, int x, int z) => layers.TryNode(layer, x, z, out int node) && layers.IsPassable(node);

        // A seam from (layer, a) into cell b: the other side's layer, the lowest when several (plates in one cell are kept apart
        // by the clearance rules, so there is at most one).
        private bool SeamInto(int layer, int ax, int az, int bx, int bz, out int other)
        {
            other = -1;
            if (!layers.TryNode(layer, ax, az, out int from)) return false;
            for (int k = 0; k < layers.SeamCount(from); k++)
            {
                int node = layers.Seam(from, k);
                if (layers.XOf(node) != bx || layers.ZOf(node) != bz) continue;
                int l = layers.LayerOf(node);
                if (other < 0 || l < other) other = l;
            }
            return other >= 0;
        }

        private bool Into(int layer, int ax, int az, int bx, int bz, out int next)
        {
            next = layer;
            return Walkable(layer, bx, bz) || SeamInto(layer, ax, az, bx, bz, out next);
        }

        // The only place that changes a unit's layer (K12). Same cell: stay. The target cell walkable on this layer: stay. An
        // orthogonal neighbour: cross the seam into it. A diagonal one: two orthogonal steps, X first, then Z first. Anything
        // else fails (a step is at most MaxEffectiveStepRaw, half a cell, so it never spans more than one cell).
        private bool TryEnter(int layer, int fromKey, int toKey, out int newLayer)
        {
            newLayer = layer;
            if (fromKey == toKey) return true;
            int fx = fromKey % grid.Width, fz = fromKey / grid.Width, tx = toKey % grid.Width, tz = toKey / grid.Width;
            int dx = tx - fx, dz = tz - fz;
            if (dx < -1 || dx > 1 || dz < -1 || dz > 1) return false;
            if (dx == 0 || dz == 0)
            {
                if (Into(layer, fx, fz, tx, tz, out newLayer)) return true;
                newLayer = layer;
                return false;
            }
            if (Walkable(layer, tx, tz)) return true;
            if (Into(layer, fx, fz, tx, fz, out int mid) && Into(mid, tx, fz, tx, tz, out newLayer)) return true;
            if (Into(layer, fx, fz, fx, tz, out mid) && Into(mid, fx, tz, tx, tz, out newLayer)) return true;
            newLayer = layer;
            return false;
        }

        // Places a candidate step on its layer: the layer from TryEnter, Y from that layer's floor. False: the step is not taken.
        private bool Settle(int layer, SimVec3 from, ref SimVec3 next)
        {
            candidateLayer = layer;
            if (!grid.TryCell(from, out int a) || !grid.TryCell(next, out int b)) return false;
            if (!TryEnter(layer, a, b, out int entered) || !Floor(entered, next.X, next.Z, out SimFix y)) return false;
            candidateLayer = entered;
            next = new SimVec3(next.X, y, next.Z);
            // Where the unit stops, its footprint stands on cells open for its new layer alone; the union of both ends' rules
            // (below) is only for the way there. Without this a step off a floor could end with the footprint over a floor
            // cell that the stair's own rule closes, and no later step could leave it.
            openCells.Set(entered, b, entered, b);
            if (!grid.SegmentClear(next, next, UnitFootprint, openCells)) return false;
            openCells.Set(layer, a, entered, b);
            return true;
        }

        // A cell is open for (layer, own cell) when it is walkable on that layer or a seam from (layer, own) lands on it (K12).
        private bool Open(int layer, int ownKey, int x, int z) =>
            Walkable(layer, x, z) || SeamInto(layer, ownKey % grid.Width, ownKey / grid.Width, x, z, out _);

        private sealed class OpenCells : ICellPassability
        {
            private readonly InterimLocomotion owner;
            private int layerA, ownA, layerB, ownB;
            internal OpenCells(InterimLocomotion owner) { this.owner = owner; }
            internal void Set(int la, int oa, int lb, int ob) { layerA = la; ownA = oa; layerB = lb; ownB = ob; }
            // Open for the start of the step or for its end.
            public bool IsPassable(int x, int z) => owner.Open(layerA, ownA, x, z) || owner.Open(layerB, ownB, x, z);
        }

        // Body ranges on the vertical axis, as the spatial index builds them: half height h = WorldHalfExtents.Y (floor(H/2));
        // centre = foot + h for a foot-anchored (ML-3) body, else the foot.
        private SimFix BodyLift(Collider collider) =>
            (collider.Layer & footAnchoredLayers) != 0 ? collider.WorldHalfExtents.Y : default;

        private static bool BodiesOverlap(long centreA, long halfA, long centreB, long halfB)
        {
            long d = centreA - centreB;
            if (d < 0) d = -d;
            return d < halfA + halfB;
        }

        // The node centre a route heads for, on its own layer's floor.
        private SimVec3 NodeCentre(int node)
        {
            SimVec3 centre = grid.Center(layers.ZOf(node) * grid.Width + layers.XOf(node), default);
            if (!Floor(layers.LayerOf(node), centre.X, centre.Z, out SimFix y)) throw new InvalidOperationException("A node centre has no floor.");
            return new SimVec3(centre.X, y, centre.Z);
        }

        private bool LayeredSearch(EntityId unit, SimVec3 point, SimVec3 target)
        {
            if (!grid.TryCell(point, out int a) || !grid.TryCell(target, out int b)) return false;
            VerticalState state = entities.Read(vertical, unit);
            if (!layers.TryNode(state.Layer, a % grid.Width, a / grid.Width, out int start)
                || !layers.TryNode(state.TargetLayer, b % grid.Width, b / grid.Width, out int end)) return false;
            return layeredFinder.Find(start, end) == GridPathStatus.Found;
        }

        private bool LayeredCrowdWaypoint(EntityId unit, SimVec3 point, SimVec3 target, out SimVec3 waypoint)
        {
            waypoint = default;
            if (!LayeredSearch(unit, point, target)) return false;
            ReadOnlySpan<int> route = layeredFinder.Path;
            waypoint = route.Length == 1 ? target : NodeCentre(route[1]);
            NoteLayeredTail(route, route.Length == 1 ? 0 : 1);
            return true;
        }

        // The path mover's waypoint over the layered graph: as TryWaypoint, but a run of same-direction steps also ends where
        // the layer changes, so a seam is always a waypoint.
        private bool LayeredWaypoint(EntityId unit, SimVec3 point, SimVec3 target, out SimVec3 waypoint)
        {
            waypoint = default;
            if (!LayeredSearch(unit, point, target)) return false;
            ReadOnlySpan<int> route = layeredFinder.Path;
            SimVec3 center = NodeCentre(route[0]);
            if (!point.Equals(center)) { waypoint = center; NoteLayeredTail(route, 0); return true; }
            if (route.Length == 1) { waypoint = target; NoteLayeredTail(route, 0); return true; }
            int vertex = 1;
            int dx = layers.XOf(route[1]) - layers.XOf(route[0]), dz = layers.ZOf(route[1]) - layers.ZOf(route[0]);
            bool flat = layers.LayerOf(route[1]) == layers.LayerOf(route[0]);
            while (flat && vertex + 1 < route.Length
                && layers.LayerOf(route[vertex + 1]) == layers.LayerOf(route[vertex])
                && layers.XOf(route[vertex + 1]) - layers.XOf(route[vertex]) == dx
                && layers.ZOf(route[vertex + 1]) - layers.ZOf(route[vertex]) == dz) vertex++;
            waypoint = NodeCentre(route[vertex]);
            NoteLayeredTail(route, vertex);
            return true;
        }

        // NoteTail over node coordinates: a diagonal step changes both X and Z; a seam is an orthogonal step.
        private void NoteLayeredTail(ReadOnlySpan<int> route, int from)
        {
            long length = 0;
            for (int i = from + 1; i < route.Length; i++)
            {
                bool diagonal = layers.XOf(route[i]) != layers.XOf(route[i - 1]) && layers.ZOf(route[i]) != layers.ZOf(route[i - 1]);
                length += diagonal ? 92682 : SimFix.Scale;
            }
            routeTail = length;
            tailFresh = true;
        }

        // Read-only route cost on a plate world: both ends on the ground (layered route costs are ML-11's).
        private bool LayeredRouteCost(SimVec3 from, SimVec3 to, long limitCost, out long cost)
        {
            cost = 0;
            if (!grid.TryCell(from, out int a) || !grid.TryCell(to, out int b)
                || !layers.TryNode(0, a % grid.Width, a / grid.Width, out int start) || !layers.TryNode(0, b % grid.Width, b / grid.Width, out int end)
                || layeredFinder.FindWithin(start, end, limitCost) != GridPathStatus.Found) return false;
            cost = layeredFinder.TotalCostWide;
            return true;
        }

        private ushort PlateSpeedMilli(int layer, SimVec3 point)
        {
            if (!grid.TryCell(point, out int cell) || !layers.TryNode(layer, cell % grid.Width, cell / grid.Width, out int node)) return 1000;
            return layers.SpeedMilliAt(node);
        }
    }
}
