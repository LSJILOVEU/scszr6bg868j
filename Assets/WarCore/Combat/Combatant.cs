using WarCore.Sim;

namespace WarCore.Combat
{
    public readonly struct Combatant : IStateHash
    {
        public const string ComponentId = "combat.combatant";
        public readonly int Team, Weapon, Ammo;
        public readonly long DueMilli;
        // Spread state left by the last shot (see SpreadSpec): stored angle in thousandths of a degree, and the milli-tick it was left at.
        public readonly int SpreadStored;
        public readonly long SpreadLastMilli;

        public Combatant(int team, int weapon, int ammo, long dueMilli, int spreadStored = 0, long spreadLastMilli = 0)
        { Team = team; Weapon = weapon; Ammo = ammo; DueMilli = dueMilli; SpreadStored = spreadStored; SpreadLastMilli = spreadLastMilli; }

        public void WriteState(StateHash hash)
        { hash.Add(Team); hash.Add(Weapon); hash.Add(Ammo); hash.Add(DueMilli); hash.Add(SpreadStored); hash.Add(SpreadLastMilli); }
    }

    public sealed class CombatantSerializer : IStableComponentSerializer<Combatant>
    {
        public int TypeId => 3001;
        public int Version => 2;
        public void Write(SimStateWriter writer, in Combatant value)
        { writer.Int32(value.Team); writer.Int32(value.Weapon); writer.Int32(value.Ammo); writer.Int64(value.DueMilli);
            writer.Int32(value.SpreadStored); writer.Int64(value.SpreadLastMilli); }
        public Combatant Read(SimStateReader reader) =>
            new Combatant(reader.Int32(), reader.Int32(), reader.Int32(), reader.Int64(), reader.Int32(), reader.Int64());
    }
}
