using System;
using System.Collections.Generic;
using WarCore.Content.MapTools;

namespace WarCore.Presentation
{
    // A list of grid edits for MapDocument.WithEdits, kept in the order they were added. Only plain values cross this class,
    // so the Unity side can fill it without seeing Content.
    public sealed class MapEditList
    {
        private readonly List<Action<MapEdit>> steps = new List<Action<MapEdit>>();

        public int Count => steps.Count;

        // Height point (i, j) gets raw (same rules as MapDocument.WithHeight).
        public MapEditList Height(int i, int j, int raw)
        {
            steps.Add(e => e.SetHeight(i, j, raw));
            return this;
        }

        // Surface cell (x, z) gets the named surface (same rules as MapDocument.WithSurface).
        public MapEditList Surface(int x, int z, string name)
        {
            steps.Add(e => e.SetSurface(x, z, name));
            return this;
        }

        internal void ApplyTo(MapEdit edit)
        {
            foreach (Action<MapEdit> step in steps) step(edit);
        }
    }
}
