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

        public ViewFire(float x0, float z0, float x1, float z1, int team, bool lethal)
        {
            X0 = x0; Z0 = z0; X1 = x1; Z1 = z1; Team = team; Lethal = lethal;
        }
    }
}
