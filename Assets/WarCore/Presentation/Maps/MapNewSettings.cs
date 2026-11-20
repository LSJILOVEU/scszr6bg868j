namespace WarCore.Presentation
{
    // What MapDocument.New makes: an empty map of Width x Depth cells on the layer Pack. Heights false: a flat map (HeightRaw
    // is then ignored); Surface null: no surface table.
    public sealed class MapNewSettings
    {
        public string Id { get; set; }
        public string Content { get; set; } = "none";
        public string Pack { get; set; }
        public int Width { get; set; }
        public int Depth { get; set; }
        public long FloorRaw { get; set; }
        public long CeilingRaw { get; set; }
        public ulong Seed { get; set; }
        public string CrowdPass { get; set; } = "none";
        public bool Heights { get; set; }
        public int HeightRaw { get; set; }
        public string Surface { get; set; }
    }
}
