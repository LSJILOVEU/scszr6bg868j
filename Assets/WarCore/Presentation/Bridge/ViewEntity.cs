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
        // The temper of the unit's AI squad: 0 none (no squad, a dead unit, a battle without tempers), 1 normal, 2 rash,
        // 3 steady.
        public int Mood { get; }
        // The regroup mark of the unit's AI squad: 0 none (no squad, a dead unit, a battle without regrouping, a squad that
        // is not regrouping), 1 regrouping, 2 short of members and not regrouping.
        public int Regroup { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        // ML-10: the layer the unit stands on (0 = the ground, 1..N = a floor plate); always 0 on a map without plates.
        public int Layer { get; }
        // STANCE-1: the unit's stance level now (0 = standing; always 0 in a world without stances).
        public int Stance { get; }

        public ViewEntity(ulong key, int archetype, float x, float y, float z, int squad = 0, int team = 0, bool alive = true, int nerve = 0,
            int layer = 0, ulong band = 0, int stance = 0, int mood = 0, int regroup = 0)
        {
            Stance = stance;
            Mood = mood;
            Regroup = regroup;
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
