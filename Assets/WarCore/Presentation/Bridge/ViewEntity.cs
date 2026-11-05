namespace WarCore.Presentation
{
    public readonly struct ViewEntity
    {
        public ulong Key { get; }
        public int Archetype { get; }
        public int Squad { get; }
        public int Team { get; }
        public bool Alive { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public ViewEntity(ulong key, int archetype, float x, float y, float z, int squad = 0, int team = 0, bool alive = true)
        {
            Key = key;
            Squad = squad;
            Team = team;
            Alive = alive;
            Archetype = archetype;
            X = x;
            Y = y;
            Z = z;
        }
    }
}
