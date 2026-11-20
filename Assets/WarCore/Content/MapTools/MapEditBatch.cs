using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;

namespace WarCore.Content.MapTools
{
    // ED-1: batches, the surface name table, and turning on heights or surfaces.
    public sealed partial class MapEdit
    {
        // Runs body with one shared draft and builds once at the end. Each step inside sees the steps before it (Current, the
        // owner queries and every check read the draft), so a batch that succeeds ends with the same canonical bytes as the
        // same steps done one by one. The batch is all or nothing: when body throws, Current is what it was before the batch
        // and the same exception object is rethrown. A step that throws fails the batch even when body catches it (part of
        // the step may already be in the draft): the batch then ends by rethrowing that step's exception (the first one),
        // with Current unchanged. A Batch inside a Batch throws InvalidOperationException ("MAPEDIT_BATCH ...") and fails the
        // outer one the same way.
        public void Batch(Action<MapEdit> body)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (batch != null)
            {
                var nested = new InvalidOperationException("MAPEDIT_BATCH a batch inside a batch");
                if (batchError == null) batchError = ExceptionDispatchInfo.Capture(nested);
                throw nested;
            }
            batch = new MapEditDraft(current);
            batchError = null;
            try
            {
                body(this);
            }
            catch
            {
                batch = null;
                batchError = null;
                throw;
            }
            MapEditDraft done = batch;
            ExceptionDispatchInfo failed = batchError;
            batch = null;
            batchError = null;
            failed?.Throw();
            current = done.Build();
        }

        // Adds a name to the ordinal-ascending surface table and renumbers every cell so each keeps its name; owners do not
        // change. A name already in the table changes nothing. The table holds at most 64 names (one base64 character).
        public void AddSurfaceName(string name)
        {
            Step(draft =>
            {
                CheckSurfaceName(name);
                if (draft.SurfaceNames == null) throw new ArgumentException("the description has no surfaces");
                int at = draft.SurfaceNames.BinarySearch(name, StringComparer.Ordinal);
                if (at >= 0) return;
                if (draft.SurfaceNames.Count >= MapEditDraft.MaxOwners) throw new InvalidOperationException("MAPDESC_GRID the surface table is full");
                int insert = ~at;
                draft.SurfaceNames.Insert(insert, name);
                foreach (char[] row in draft.SurfaceRows)
                    for (int x = 0; x < row.Length; x++)
                    {
                        int index = MapEditDraft.OwnerIndex(row[x]);
                        if (index >= insert) row[x] = MapEditDraft.OwnerChar(index + 1);
                    }
            });
        }

        // Gives a description without heights a (D + 1) x (W + 1) height table, every point raw, every point unowned. raw must
        // be in [floorRaw, ceilingRaw] and in the reader's height range, so the result can be read back.
        public void EnableHeights(int raw)
        {
            Step(draft =>
            {
                if (raw < current.FloorRaw || raw > current.CeilingRaw || raw < -MapDescReader.HeightLimit || raw > MapDescReader.HeightLimit)
                    throw new ArgumentOutOfRangeException(nameof(raw));
                if (draft.Heights != null) throw new ArgumentException("the description already has heights");
                draft.Heights = new int[draft.Depth + 1][];
                for (int j = 0; j <= draft.Depth; j++)
                {
                    draft.Heights[j] = new int[draft.Width + 1];
                    for (int i = 0; i <= draft.Width; i++) draft.Heights[j][i] = raw;
                }
                draft.HeightOwner = MapEditDraft.Grid(null, draft.Depth + 1, draft.Width + 1);
            });
        }

        // Gives a description without surfaces a table holding only name, every cell that surface, every cell unowned.
        public void EnableSurfaces(string name)
        {
            Step(draft =>
            {
                CheckSurfaceName(name);
                if (draft.SurfaceRows != null) throw new ArgumentException("the description already has surfaces");
                draft.SurfaceNames = new List<string> { name };
                draft.SurfaceRows = new char[draft.Depth][];
                for (int z = 0; z < draft.Depth; z++) draft.SurfaceRows[z] = new string(MapEditDraft.OwnerChar(0), draft.Width).ToCharArray();
                draft.SurfaceOwner = MapEditDraft.Grid(null, draft.Depth, draft.Width);
            });
        }

        // A surface name the bake accepts: 1 to 255 UTF-8 bytes, no NUL, no lone surrogate.
        private static void CheckSurfaceName(string name)
        {
            try { WcMapValidation.TextBytes(name); }
            catch (InvalidDataException) { throw new ArgumentException("bad surface name", nameof(name)); }
        }
    }
}
