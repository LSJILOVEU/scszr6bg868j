namespace WarCore.Presentation
{
    // BLAST-1: one blast as the picture needs it: the point, the inner (full harm) and outer (no harm beyond) radii in
    // metres and the team that set it off (0 = none). HC-4: the burst is drawn from the two radii (BlastLook).
    public readonly struct ViewBlast
    {
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float Inner { get; }
        public float Radius { get; }
        public int Team { get; }

        public ViewBlast(float x, float y, float z, float inner, float radius, int team)
        {
            X = x; Y = y; Z = z; Inner = inner; Radius = radius; Team = team;
        }
    }
}
