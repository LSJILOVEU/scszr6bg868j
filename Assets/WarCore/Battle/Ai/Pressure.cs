using System;
using System.Collections.Generic;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Battle
{
    // The numbers of the pressure system. Pressure runs from 0 to Pressure.Max; the shocks and the three lines are in that
    // unit. Radii are horizontal, in millimetres. DecayPermille is the share of the current pressure lost every tick, in
    // thousandths.
    public readonly struct PressureRules
    {
        public readonly int DeathRadiusMm, DeathShock, NearRadiusMm, NearShock, DecayPermille, BrokenAt, RecoverAt, PinnedAt, SpreadPermille, BreakSharePermille;
        public PressureRules(int deathRadiusMm, int deathShock, int nearRadiusMm, int nearShock, int decayPermille,
            int brokenAt, int recoverAt, int pinnedAt, int spreadPermille, int breakSharePermille)
        {
            if (deathRadiusMm < 0) throw new ArgumentOutOfRangeException(nameof(deathRadiusMm));
            if (deathShock < 0 || deathShock > Pressure.Max) throw new ArgumentOutOfRangeException(nameof(deathShock));
            if (nearRadiusMm < 0) throw new ArgumentOutOfRangeException(nameof(nearRadiusMm));
            if (nearShock < 0 || nearShock > Pressure.Max) throw new ArgumentOutOfRangeException(nameof(nearShock));
            if (decayPermille < 1 || decayPermille > 1000) throw new ArgumentOutOfRangeException(nameof(decayPermille));
            if (brokenAt < 1 || brokenAt > Pressure.Max) throw new ArgumentOutOfRangeException(nameof(brokenAt));
            if (recoverAt < 0 || recoverAt >= brokenAt) throw new ArgumentOutOfRangeException(nameof(recoverAt));
            if (pinnedAt < 1 || pinnedAt > Pressure.Max) throw new ArgumentOutOfRangeException(nameof(pinnedAt));
            if (spreadPermille < 0 || spreadPermille > 1000) throw new ArgumentOutOfRangeException(nameof(spreadPermille));
            if (breakSharePermille < 0 || breakSharePermille > 1000) throw new ArgumentOutOfRangeException(nameof(breakSharePermille));
            DeathRadiusMm = deathRadiusMm; DeathShock = deathShock; NearRadiusMm = nearRadiusMm; NearShock = nearShock;
            DecayPermille = decayPermille; BrokenAt = brokenAt; RecoverAt = recoverAt; PinnedAt = pinnedAt; SpreadPermille = spreadPermille;
            BreakSharePermille = breakSharePermille;
        }
    }

    // How hard each unit is pressed, as one number per unit. Every tick, after combat:
    // 1. the number loses DecayPermille thousandths of itself (at least 1), so under steady fire it settles at a level
    //    that grows with how much fire there is, and falls by half in a fixed time once the fire stops;
    // 2. it rises by NearShock when an enemy shot of this tick ended within NearRadiusMm of the unit (once a tick, however
    //    many shots), and by DeathShock for every teammate brought down this tick within DeathRadiusMm (a fire record
    //    with damage; damage lands after the combat step, so the record is all there is to read, and the RWR resolver
    //    either kills or does nothing). The rise is scaled per unit: 1000 + (Permille(key) - 500) * SpreadPermille / 500
    //    thousandths, so some units shake more than others;
    // 3. the unit is Broken from BrokenAt until the number is back at RecoverAt, and Pinned at or above PinnedAt. Only
    //    BreakSharePermille thousandths of the units (picked by key) can break; the rest stop at Pinned, so a whole team
    //    never runs.
    // The AI scheduler hands the level to the engage part (AiUnit.Nerve); nothing else reads it.
    //
    // With sources on (a battle with cover rules) every entry also keeps where the fire that pressed the unit came from:
    // the origin of the near shot that counted (the first such record of the tick), or, in a tick without one, of the
    // first record that brought down a teammate who counted. A rise replaces the source only while the number stands
    // below PinnedAt, so a unit keeps one source from the rise that pinned it until it is calm again: it takes cover
    // once and stays, rather than moving for every new shooter. The scheduler reads it through TryPressedFrom; it is
    // saved and hashed with the entry (state version 2). Without sources nothing of it exists and the state is as it was.
    //
    // Order 32 in the Queries phase: after combat (30), whose fire records are not saved and so must be read on the tick
    // they are made. Only units with a number above zero have an entry.
    public sealed class Pressure : IRetainedGameplaySystem
    {
        public const int Max = 10000;
        private const ulong CourageSalt = 0x510e527fade682d1UL, BreakSalt = 0x9b05688c2b3e6c1fUL;
        private struct Entry { internal int Value; internal bool Broken; internal SimVec3 From; }
        private struct Fallen { internal ulong Key; internal int Team; internal SimVec3 At, From; }

        private readonly CombatSystem combat;
        private readonly PressureRules rules;
        private readonly bool sources;
        private readonly EntityStage stage;
        private readonly ComponentType<Position> positions;
        private readonly ComponentType<Combatant> combatants;
        private readonly SimWide nearSquared, deathSquared;
        private readonly SortedList<ulong, Entry> entries;
        private readonly IList<ulong> keys;
        private readonly IList<Entry> values;
        private readonly List<ulong> drop;
        private readonly Fallen[] fallen;

        public Pressure(CombatSystem combat, PressureRules rules, bool sources = false)
        {
            this.sources = sources;
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            if (rules.BrokenAt == 0) throw new ArgumentException("Pressure rules are not set.", nameof(rules));
            this.rules = rules;
            stage = new EntityStage(combat.Entities);
            positions = combat.Entities.Resolve<Position>(); combatants = combat.Entities.Resolve<Combatant>();
            nearSquared = new SimVec3(SimFix.Ratio(rules.NearRadiusMm, 1000), default, default).LengthSquared;
            deathSquared = new SimVec3(SimFix.Ratio(rules.DeathRadiusMm, 1000), default, default).LengthSquared;
            int capacity = combat.Entities.Capacity;
            entries = new SortedList<ulong, Entry>(capacity);
            keys = entries.Keys; values = entries.Values;
            drop = new List<ulong>(capacity);
            fallen = new Fallen[capacity];
        }

        public int Order => 32;
        public string StateTypeId => "battle.ai-pressure";
        public int StateVersion => sources ? 2 : 1;
        internal int Count => entries.Count;
        internal bool Sources => sources;

        // The unit's pressure, 0 when it has none.
        public int Value(StableId unit) => entries.TryGetValue(unit.Value, out Entry e) ? e.Value : 0;
        public AiNerve Level(StableId unit)
        {
            if (!entries.TryGetValue(unit.Value, out Entry e)) return AiNerve.Calm;
            return e.Broken ? AiNerve.Broken : e.Value >= rules.PinnedAt ? AiNerve.Pinned : AiNerve.Calm;
        }
        // Where the fire that pressed the unit came from. False without sources or without an entry.
        public bool TryPressedFrom(StableId unit, out SimVec3 from)
        {
            bool found = entries.TryGetValue(unit.Value, out Entry e) && sources;
            from = found ? e.From : default;
            return found;
        }
        // Whether this unit can break at all: Permille(key) < BreakSharePermille. The others stop at Pinned.
        public static bool Breaks(ulong key, int breakSharePermille) => AiParts.Permille(key, BreakSalt) < breakSharePermille;
        // Thousandths by which this unit's shocks are scaled.
        public static int Courage(ulong key, int spreadPermille) => 1000 + (AiParts.Permille(key, CourageSalt) - 500) * spreadPermille / 500;

        public void Step(SimContext c) => Advance(combat.Fires);

        // One tick of pressure from these fire records. Step calls it with the records combat made this tick.
        public void Advance(ReadOnlySpan<FireRecord> fires)
        {
            EntityStore store = combat.Entities;
            // 1. Decay. A unit that is gone or dead loses its entry.
            drop.Clear();
            for (int i = 0; i < keys.Count; i++)
            {
                ulong key = keys[i];
                if (!store.TryResolve(new StableId(key), out EntityRef unit) || combat.IsDead((EntityId)unit)) { drop.Add(key); continue; }
                Entry e = values[i];
                e.Value -= Math.Max(1, (int)((long)e.Value * rules.DecayPermille / 1000));
                entries[key] = e;
            }
            foreach (ulong key in drop) entries.Remove(key);

            // 2. This tick's shocks.
            int fallenCount = 0;
            foreach (ref readonly FireRecord fire in fires)
            {
                if (fire.DamageRaw <= 0 || !store.TryResolve(fire.Target, out EntityRef target)
                    || !store.TryGet(combatants, (EntityId)target, out Combatant victim) || !store.TryGet(positions, (EntityId)target, out Position at)) continue;
                bool counted = false;
                for (int i = 0; i < fallenCount; i++) if (fallen[i].Key == fire.Target.Value) { counted = true; break; }
                if (!counted) fallen[fallenCount++] = new Fallen { Key = fire.Target.Value, Team = victim.Team, At = at.Value, From = fire.Origin };
            }
            if (fires.Length > 0)
            {
                foreach (EntityId entity in stage.Capture())
                {
                    if (!store.TryGet(combatants, entity, out Combatant unit) || unit.Team < 1 || unit.Team > 2
                        || !store.TryGet(positions, entity, out Position position) || combat.IsDead(entity)) continue;
                    ulong key = store.StableOf(entity).Value;
                    SimVec3 here = position.Value;
                    int shock = 0;
                    SimVec3 from = default; bool sourced = false;
                    foreach (ref readonly FireRecord fire in fires)
                    {
                        if (fire.ShooterTeam == unit.Team || CombatSystem.Horizontal(fire.End - here).LengthSquared > nearSquared) continue;
                        shock = rules.NearShock; from = fire.Origin; sourced = true; break;
                    }
                    for (int i = 0; i < fallenCount; i++)
                    {
                        if (fallen[i].Team != unit.Team || fallen[i].Key == key
                            || CombatSystem.Horizontal(fallen[i].At - here).LengthSquared > deathSquared) continue;
                        shock += rules.DeathShock;
                        if (!sourced) { from = fallen[i].From; sourced = true; }
                    }
                    if (shock == 0) continue;
                    long rise = (long)shock * Courage(key, rules.SpreadPermille) / 1000;
                    if (rise <= 0) continue;
                    entries.TryGetValue(key, out Entry e);
                    // The source is replaced only while the number (after this tick's decay) is below the pinned line.
                    if (sources && e.Value < rules.PinnedAt) e.From = from;
                    e.Value = (int)Math.Min(Max, e.Value + rise);
                    entries[key] = e;
                }
            }

            // 3. The level, once a tick on the number after decay and shocks. An entry at zero goes.
            drop.Clear();
            for (int i = 0; i < keys.Count; i++)
            {
                Entry e = values[i];
                if (e.Value == 0) { drop.Add(keys[i]); continue; }
                bool broken = e.Broken ? e.Value > rules.RecoverAt : e.Value >= rules.BrokenAt && Breaks(keys[i], rules.BreakSharePermille);
                if (broken != e.Broken) { e.Broken = broken; entries[keys[i]] = e; }
            }
            foreach (ulong key in drop) entries.Remove(key);
        }

        private void WriteRules(SimStateWriter writer)
        {
            writer.Int32(rules.DeathRadiusMm); writer.Int32(rules.DeathShock); writer.Int32(rules.NearRadiusMm); writer.Int32(rules.NearShock);
            writer.Int32(rules.DecayPermille); writer.Int32(rules.BrokenAt); writer.Int32(rules.RecoverAt); writer.Int32(rules.PinnedAt);
            writer.Int32(rules.SpreadPermille); writer.Int32(rules.BreakSharePermille);
        }

        public void SaveState(SimStateWriter writer)
        {
            WriteRules(writer);
            writer.Int32(entries.Count);
            foreach (KeyValuePair<ulong, Entry> pair in entries)
            {
                writer.UInt64(pair.Key); writer.Int32(pair.Value.Value); writer.Bool(pair.Value.Broken);
                if (sources) { writer.Int64(pair.Value.From.X.Raw); writer.Int64(pair.Value.From.Y.Raw); writer.Int64(pair.Value.From.Z.Raw); }
            }
        }

        public void LoadState(SimStateReader reader)
        {
            bool same = reader.Int32() == rules.DeathRadiusMm; same &= reader.Int32() == rules.DeathShock;
            same &= reader.Int32() == rules.NearRadiusMm; same &= reader.Int32() == rules.NearShock;
            same &= reader.Int32() == rules.DecayPermille; same &= reader.Int32() == rules.BrokenAt;
            same &= reader.Int32() == rules.RecoverAt; same &= reader.Int32() == rules.PinnedAt;
            same &= reader.Int32() == rules.SpreadPermille; same &= reader.Int32() == rules.BreakSharePermille;
            SimStateReader.Require(same, "AI pressure rules differ.");
            int count = reader.Count(combat.Entities.Capacity);
            entries.Clear();
            ulong previous = 0;
            for (int i = 0; i < count; i++)
            {
                ulong key = reader.UInt64();
                SimStateReader.Require(i == 0 || key > previous, "AI pressure units are not in strictly increasing order.");
                previous = key;
                int value = reader.Int32(); bool broken = reader.Bool();
                SimStateReader.Require(value >= 1 && value <= Max, "AI pressure is out of range.");
                // Broken is entered at BrokenAt and left at RecoverAt, so it cannot stand at or below the recover line.
                SimStateReader.Require(!broken || value > rules.RecoverAt, "AI pressure is broken at or below the recover line.");
                // At the broken line every unit that can break is broken (the level is settled at the end of every step),
                // and a unit that cannot break never is.
                SimStateReader.Require(broken ? Breaks(key, rules.BreakSharePermille) : value < rules.BrokenAt || !Breaks(key, rules.BreakSharePermille),
                    "AI pressure and its broken flag disagree.");
                SimVec3 from = sources ? new SimVec3(SimFix.FromRaw(reader.Int64()), SimFix.FromRaw(reader.Int64()), SimFix.FromRaw(reader.Int64())) : default;
                entries[key] = new Entry { Value = value, Broken = broken, From = from };
            }
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(StateTypeId); hash.Add(StateVersion);
            hash.Add(rules.DeathRadiusMm); hash.Add(rules.DeathShock); hash.Add(rules.NearRadiusMm); hash.Add(rules.NearShock);
            hash.Add(rules.DecayPermille); hash.Add(rules.BrokenAt); hash.Add(rules.RecoverAt); hash.Add(rules.PinnedAt); hash.Add(rules.SpreadPermille); hash.Add(rules.BreakSharePermille);
            hash.Add(entries.Count);
            foreach (KeyValuePair<ulong, Entry> pair in entries)
            {
                hash.Add(pair.Key); hash.Add(pair.Value.Value); hash.Add(pair.Value.Broken);
                if (sources) { hash.Add(pair.Value.From.X.Raw); hash.Add(pair.Value.From.Y.Raw); hash.Add(pair.Value.From.Z.Raw); }
            }
        }
    }
}
