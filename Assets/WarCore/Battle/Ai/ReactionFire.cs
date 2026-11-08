using System;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Battle
{
    // AI reaction time R: an automatic shooter does not fire at a target before R ticks have passed since it first could
    // select that target. Per shooter one entry (target T, First = the tick the clock for T started, Last = the latest tick
    // Sense selected T, i.e. the latest tick the shooter took part in automatic fire). Selecting T on consecutive ticks keeps
    // the clock; any other tick (another target, no target, or no Sense call because of an intent, the player or death)
    // starts it again. Cooldown and reaction run side by side: the first shot comes at max(First + R, weapon ready).
    //
    // One object is both the fire decision (installed with CombatSystem.UseAutoFire) and the state system battle.ai-fire
    // (Order 14, before combat). It wraps only the combat system's built-in decision, which uses the same selector and range,
    // so the target Sense records is the one the built-in TryAim aims at. Sense and TryAim never allocate: the entries live in
    // arrays indexed by entity slot. Saves and hashes list them in stable-identity order (the combat stage order), only for
    // entities that exist and whose identity matches the entry, so a destroyed or reused slot is never written.
    public sealed class ReactionFire : IAutoFire, IRetainedGameplaySystem
    {
        // Same bound as the content field (Content/Data/Sections/AiSection.cs MaxReactionTicks); Battle cannot see Content.
        internal const int MaxTicks = 1000000;

        private readonly CombatSystem combat;
        private readonly IAutoFire inner;
        private readonly int ticks;
        private readonly EntityStage stage;
        private readonly bool[] has;
        private readonly StableId[] shooters, targets;
        private readonly long[] firsts, lasts;

        public ReactionFire(CombatSystem combat, IAutoFire inner, int ticks)
        {
            if (ticks < 0 || ticks > MaxTicks) throw new ArgumentOutOfRangeException(nameof(ticks));
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.ticks = ticks;
            stage = new EntityStage(combat.Entities);
            int capacity = combat.Entities.Capacity;
            has = new bool[capacity];
            shooters = new StableId[capacity]; targets = new StableId[capacity];
            firsts = new long[capacity]; lasts = new long[capacity];
        }

        public int Order => 14;
        public string StateTypeId => "battle.ai-fire";
        public int StateVersion => 1;

        // The number of entries a save or hash would write now.
        internal int Count
        {
            get
            {
                int count = 0;
                foreach (EntityId entity in stage.Capture()) if (Exported(entity)) count++;
                return count;
            }
        }

        public void Sense(CombatSystem combat, EntityId shooter, in Combatant unit, in Position position, long tick)
        {
            inner.Sense(combat, shooter, in unit, in position, tick);
            int slot = shooter.Id;
            if ((uint)slot >= (uint)has.Length) return;
            if (unit.Weapon < 0
                || !combat.Pack.Selector.TrySelect(combat, shooter, combat.Pack.Weapons[unit.Weapon].FalloffEnd, out EntityId target))
            {
                has[slot] = false;
                return;
            }
            StableId self = combat.Entities.StableOf(shooter), selected = combat.Entities.StableOf(target);
            if (has[slot] && shooters[slot].Equals(self) && targets[slot].Equals(selected) && lasts[slot] == tick - 1)
            {
                lasts[slot] = tick;
                return;
            }
            has[slot] = true; shooters[slot] = self; targets[slot] = selected; firsts[slot] = tick; lasts[slot] = tick;
        }

        public AutoFireAction TryAim(CombatSystem combat, EntityId shooter, in Combatant unit, in Position position,
            in WeaponDef weapon, long tick, out SimVec3 direction)
        {
            int slot = shooter.Id;
            if ((uint)slot >= (uint)has.Length || !has[slot] || !shooters[slot].Equals(combat.Entities.StableOf(shooter))
                || lasts[slot] != tick || tick - firsts[slot] < ticks)
            {
                direction = default;
                return AutoFireAction.Pass;
            }
            return inner.TryAim(combat, shooter, in unit, in position, in weapon, tick, out direction);
        }

        // Clears, one tick late, the entries Sense did not touch on the previous tick.
        public void Step(SimContext c)
        {
            long tick = c.Clock.Tick;
            for (int slot = 0; slot < has.Length; slot++)
                if (has[slot] && lasts[slot] < tick - 1) has[slot] = false;
        }

        private bool Exported(EntityId entity) =>
            (uint)entity.Id < (uint)has.Length && has[entity.Id] && shooters[entity.Id].Equals(combat.Entities.StableOf(entity));

        public void SaveState(SimStateWriter writer)
        {
            writer.Int32(ticks);
            writer.Int32(Count);
            foreach (EntityId entity in stage.Capture())
            {
                if (!Exported(entity)) continue;
                int slot = entity.Id;
                writer.UInt64(shooters[slot].Value); writer.UInt64(targets[slot].Value); writer.Int64(firsts[slot]); writer.Int64(lasts[slot]);
            }
        }

        public void LoadState(SimStateReader reader)
        {
            Array.Clear(has, 0, has.Length);
            SimStateReader.Require(reader.Int32() == ticks, "AI reaction time differs.");
            int count = reader.Count(combat.Entities.Capacity);
            ulong previous = 0;
            for (int i = 0; i < count; i++)
            {
                ulong key = reader.UInt64();
                SimStateReader.Require(i == 0 || key > previous, "AI reaction shooters are not in strictly increasing order.");
                previous = key;
                var shooter = new StableId(key);
                var target = new StableId(reader.UInt64());
                long first = reader.Int64(), last = reader.Int64();
                SimStateReader.Require(first >= 0, "AI reaction tick is negative.");
                SimStateReader.Require(last >= first, "AI reaction last tick precedes its first tick.");
                SimStateReader.Require(combat.Entities.TryResolve(shooter, out EntityRef found), "AI reaction shooter does not exist.");
                int slot = ((EntityId)found).Id;
                SimStateReader.Require((uint)slot < (uint)has.Length, "AI reaction shooter is outside the entity capacity.");
                has[slot] = true; shooters[slot] = shooter; targets[slot] = target; firsts[slot] = first; lasts[slot] = last;
            }
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(StateTypeId); hash.Add(StateVersion); hash.Add(ticks); hash.Add(Count);
            foreach (EntityId entity in stage.Capture())
            {
                if (!Exported(entity)) continue;
                int slot = entity.Id;
                hash.Add(shooters[slot].Value); hash.Add(targets[slot].Value); hash.Add(firsts[slot]); hash.Add(lasts[slot]);
            }
        }
    }
}
