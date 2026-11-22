using System;
using WarCore.Sim;

namespace WarCore.Combat
{
    // BLAST-1: one kind of explosion. Units whose body centre lies closer than OuterMm to the blast point take damage: the full
    // Damage within InnerMm, then falling linearly to 0 at OuterMm. Friendly: the source's own team (the source included) is hit
    // too. Walls: a wall, the ground or a floor plate between the blast and the body centre stops it. Damage is in health units
    // (the unit of the pack's maxHealth). The Id names the kind for commands and content; it is not part of any digest.
    public readonly struct BlastSpec
    {
        public const int MaxOuterMm = 100000, MaxDamage = 32767;

        public readonly string Id;
        public readonly int InnerMm, OuterMm, Damage;
        public readonly bool Friendly, Walls;
        public readonly long InnerRaw, OuterRaw, DamageRaw;
        // DOWN-1: ticks a unit this blast hurts without downing it stays stunned (0..600, 0 = none).
        public readonly int StunTicks;
        public const int MaxStunTicks = 600;

        public BlastSpec(string id, int innerMm, int outerMm, int damage, bool friendly, bool walls, int stunTicks = 0)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A blast needs an id.", nameof(id));
            if (outerMm < 1 || outerMm > MaxOuterMm) throw new ArgumentOutOfRangeException(nameof(outerMm));
            if (innerMm < 0 || innerMm >= outerMm) throw new ArgumentOutOfRangeException(nameof(innerMm));
            if (damage < 1 || damage > MaxDamage) throw new ArgumentOutOfRangeException(nameof(damage));
            if (stunTicks < 0 || stunTicks > MaxStunTicks) throw new ArgumentOutOfRangeException(nameof(stunTicks));
            StunTicks = stunTicks;
            Id = id; InnerMm = innerMm; OuterMm = outerMm; Damage = damage; Friendly = friendly; Walls = walls;
            InnerRaw = SimFix.Ratio(innerMm, 1000).Raw;
            OuterRaw = SimFix.Ratio(outerMm, 1000).Raw;
            DamageRaw = (long)damage * SimFix.Scale;
        }

        // Damage at distance d (raw) from the blast point: 0 at OuterRaw and beyond, DamageRaw within InnerRaw, otherwise
        // DamageRaw * (OuterRaw - d) / (OuterRaw - InnerRaw) rounded down. Bits: DamageRaw < 2^31 and OuterRaw - d < 2^23, so
        // the product stays below 2^54.
        public long DamageAt(long distanceRaw)
        {
            if (distanceRaw >= OuterRaw) return 0;
            if (distanceRaw <= InnerRaw) return DamageRaw;
            return DamageRaw * (OuterRaw - distanceRaw) / (OuterRaw - InnerRaw);
        }

        internal void WriteState(StateHash hash)
        {
            hash.Add(InnerMm); hash.Add(OuterMm); hash.Add(Damage); hash.Add(Friendly); hash.Add(Walls);
            // Only when set, so a blast that does not stun keeps the digest it had before DOWN-1.
            if (StunTicks > 0) { hash.Add("stun"); hash.Add(StunTicks); }
        }
    }

    // BLAST-1: the explosions a combat pack knows (1 to 32 kinds), how many go off in one tick at most (PerTick, 1 to 64) and how
    // many may wait (Queue, 1 to 4096). A pack without this section has no blast system and keeps its digest and limits.
    public sealed class BlastRules
    {
        public const int MaxKinds = 32, MaxPerTick = 64, MaxQueue = 4096, DefaultPerTick = 4, DefaultQueue = 64;

        private readonly BlastSpec[] kinds;
        public ReadOnlySpan<BlastSpec> Kinds => kinds;
        public int PerTick { get; }
        public int Queue { get; }
        // DOWN-1: some kind stuns (StunTicks > 0).
        public bool Stuns { get; }

        public BlastRules(BlastSpec[] kinds, int perTick = DefaultPerTick, int queue = DefaultQueue)
        {
            if (kinds == null) throw new ArgumentNullException(nameof(kinds));
            if (kinds.Length < 1 || kinds.Length > MaxKinds) throw new ArgumentOutOfRangeException(nameof(kinds), "A pack has 1 to 32 blast kinds.");
            if (perTick < 1 || perTick > MaxPerTick) throw new ArgumentOutOfRangeException(nameof(perTick));
            if (queue < 1 || queue > MaxQueue) throw new ArgumentOutOfRangeException(nameof(queue));
            this.kinds = (BlastSpec[])kinds.Clone();
            for (int i = 0; i < this.kinds.Length; i++)
            {
                if (this.kinds[i].Id == null) throw new ArgumentException("Blast " + i + " has no id.", nameof(kinds));
                for (int j = 0; j < i; j++)
                    if (this.kinds[j].Id == this.kinds[i].Id) throw new ArgumentException("Blast id '" + this.kinds[i].Id + "' is repeated.", nameof(kinds));
            }
            PerTick = perTick; Queue = queue;
            foreach (BlastSpec spec in this.kinds) Stuns |= spec.StunTicks > 0;
        }

        // The index of a kind by id, or -1.
        public int IndexOf(string id)
        {
            for (int i = 0; i < kinds.Length; i++)
                if (kinds[i].Id == id) return i;
            return -1;
        }

        internal void WriteState(StateHash hash)
        {
            hash.Add("combat.pack.blast.v1"); hash.Add(kinds.Length);
            foreach (BlastSpec spec in kinds) spec.WriteState(hash);
            hash.Add(PerTick); hash.Add(Queue);
        }
    }
}
