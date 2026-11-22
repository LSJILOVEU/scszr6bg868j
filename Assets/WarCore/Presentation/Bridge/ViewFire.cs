namespace WarCore.Presentation
{
    // One shot as the picture needs it: floats and a team, no Sim types.
    public readonly struct ViewFire
    {
        public float X0 { get; }
        public float Z0 { get; }
        public float X1 { get; }
        public float Z1 { get; }
        public int Team { get; }
        public bool Lethal { get; }
        // SCALE-2a: the heights the shot starts and ends at in the simulation (metres; 0 from the six-argument constructor).
        // In memory only: replays and snapshots do not carry them.
        public float Y0 { get; }
        public float Y1 { get; }

        public ViewFire(float x0, float z0, float x1, float z1, int team, bool lethal)
            : this(x0, z0, x1, z1, team, lethal, 0f, 0f)
        {
        }

        public ViewFire(float x0, float z0, float x1, float z1, int team, bool lethal, float y0, float y1)
        {
            X0 = x0; Z0 = z0; X1 = x1; Z1 = z1; Team = team; Lethal = lethal; Y0 = y0; Y1 = y1;
        }
    }
}
