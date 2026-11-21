namespace WarCore.Presentation
{
    // One projectile in flight as the picture needs it: where it is at the committed tick and where it was a tick earlier
    // (its launch point on the tick it was fired). Floats and a team, no Sim types.
    public readonly struct ViewProjectile
    {
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float FromX { get; }
        public float FromY { get; }
        public float FromZ { get; }
        public int Team { get; }
        // Fired on the committed tick: the picture may show a muzzle flash at (FromX, FromY, FromZ).
        public bool Fresh { get; }

        public ViewProjectile(float x, float y, float z, float fromX, float fromY, float fromZ, int team, bool fresh)
        {
            X = x; Y = y; Z = z; FromX = fromX; FromY = fromY; FromZ = fromZ; Team = team; Fresh = fresh;
        }
    }

    // One projectile that ended on the committed tick: where it stopped, whose it was, and whether it stopped on a body.
    public readonly struct ViewImpact
    {
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public int Team { get; }
        public bool Struck { get; }
        public bool Damaged { get; }

        public ViewImpact(float x, float y, float z, int team, bool struck, bool damaged)
        {
            X = x; Y = y; Z = z; Team = team; Struck = struck; Damaged = damaged;
        }
    }
}
