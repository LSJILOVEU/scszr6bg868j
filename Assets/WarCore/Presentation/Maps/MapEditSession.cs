using System;
using System.Collections.Generic;

namespace WarCore.Presentation
{
    // Undo and redo over immutable documents: each Do keeps the previous document, so undo is handing it back. At most limit
    // documents are kept for undo; the oldest is dropped beyond that.
    public sealed class MapEditSession
    {
        private readonly int limit;
        private readonly LinkedList<MapDocument> undo = new LinkedList<MapDocument>();
        private readonly Stack<MapDocument> redo = new Stack<MapDocument>();

        public MapEditSession(MapDocument start, int limit = 50)
        {
            if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));
            Current = start ?? throw new ArgumentNullException(nameof(start));
            this.limit = limit;
        }

        public MapDocument Current { get; private set; }
        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;
        public int UndoCount => undo.Count;

        // edit gets Current and returns the next document. When it throws, the session is unchanged and the exception goes
        // on; when it returns null, InvalidOperationException and the session is unchanged. Otherwise the redo list is cleared.
        public void Do(Func<MapDocument, MapDocument> edit)
        {
            if (edit == null) throw new ArgumentNullException(nameof(edit));
            MapDocument next = edit(Current);
            if (next == null) throw new InvalidOperationException("the edit returned no document");
            undo.AddLast(Current);
            if (undo.Count > limit) undo.RemoveFirst();
            redo.Clear();
            Current = next;
        }

        public bool Undo()
        {
            if (undo.Count == 0) return false;
            redo.Push(Current);
            Current = undo.Last.Value;
            undo.RemoveLast();
            return true;
        }

        public bool Redo()
        {
            if (redo.Count == 0) return false;
            undo.AddLast(Current);
            if (undo.Count > limit) undo.RemoveFirst();
            Current = redo.Pop();
            return true;
        }
    }
}
