namespace WarCore.Presentation
{
    public readonly struct ViewEntity
    {
        public ulong Key { get; }
        public int Archetype { get; }
        public int Squad { get; }
        public int Team { get; }
        public bool Alive { get; }
        // How hard the unit is pressed: 0 calm, 1 pinned, 2 broken (always 0 in a battle without the pressure system).
        public int Nerve { get; }
        // The AI squad the unit is in: the spawn key of its leader (its own key for a leader); 0 for a unit in no squad, a
        // dead unit and a battle without squads.
        public ulong Band { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        // ML-10: the layer the unit stands on (0 = the ground, 1..N = a floor plate); always 0 on a map without plates.
        public int Layer { get; }
        // STANCE-1: the unit's stance level now (0 = standing; always 0 in a world without stances).
        public int Stance { get; }

        public ViewEntity(ulong key, int archetype, float x, float y, float z, int squad = 0, int team = 0, bool alive = true, int nerve = 0,
            int layer = 0, ulong band = 0, int stance = 0)
        {
            Stance = stance;
            Layer = layer;
            Key = key;
            Squad = squad;
            Team = team;
            Alive = alive;
            Nerve = nerve;
            Band = band;
            Archetype = archetype;
            X = x;
            Y = y;
            Z = z;
        }
    }
}
