namespace WarCore.Presentation
{
    // BLAST-1: one blast as the picture needs it: the point, the outer radius in metres and the team that set it off (0 = none).
    public readonly struct ViewBlast
    {
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float Radius { get; }
        public int Team { get; }

        public ViewBlast(float x, float y, float z, float radius, int team)
        {
            X = x; Y = y; Z = z; Radius = radius; Team = team;
        }
    }
}
