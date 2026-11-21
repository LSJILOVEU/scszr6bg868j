namespace WarCore.Presentation
{
    // What MapDocument.WithTerrain gives back: the new document and how many height points and surface cells the stamp left
    // alone because they are hand edits.
    public sealed class MapTerrainResult
    {
        public MapDocument Document { get; }
        public int HeightsSkipped { get; }
        public int SurfacesSkipped { get; }

        internal MapTerrainResult(MapDocument document, int heightsSkipped, int surfacesSkipped)
        {
            Document = document;
            HeightsSkipped = heightsSkipped;
            SurfacesSkipped = surfacesSkipped;
        }
    }
}
