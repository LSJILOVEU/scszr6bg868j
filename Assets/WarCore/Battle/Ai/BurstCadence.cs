using System;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Battle
{
    // Deadlines follow stable shooters; ReactionFire keeps target-local burst counts.
    public sealed class BurstCadence : IRetainedGameplaySystem
    {
        private readonly CombatSystem combat;
        private readonly EntityStage stage;
        private readonly int pauseTicks;
        private readonly bool[] has;
        private readonly StableId[] shooters;
        private readonly long[] pauseUntil;

        public BurstCadence(CombatSystem combat, int pauseTicks)
        {
            this.combat = combat;
            this.pauseTicks = pauseTicks;
            stage = new EntityStage(combat.Entities);
            int capacity = combat.Entities.Capacity;
            has = new bool[capacity]; shooters = new StableId[capacity];
            pauseUntil = new long[capacity];
        }

        public int Order => 15;
        public string StateTypeId => "battle.ai-burst-cadence";
        public int StateVersion => 1;

        internal bool Ready(EntityId shooter, long tick)
        {
            int slot = shooter.Id;
            StableId self = combat.Entities.StableOf(shooter);
            if (!has[slot] || !shooters[slot].Equals(self))
            {
                has[slot] = true; shooters[slot] = self; pauseUntil[slot] = 0;
            }
            return tick >= pauseUntil[slot];
        }

        internal void Pause(EntityId shooter, long tick) => pauseUntil[shooter.Id] = tick + pauseTicks;

        public void Step(SimContext c) { }

        private bool Exported(EntityId entity) => has[entity.Id]
            && shooters[entity.Id].Equals(combat.Entities.StableOf(entity)) && !combat.IsDead(entity);

        private int Count
        {
            get
            {
                int count = 0;
                foreach (EntityId entity in stage.Capture()) if (Exported(entity)) count++;
                return count;
            }
        }

        public void SaveState(SimStateWriter writer)
        {
            writer.Int32(pauseTicks); writer.Int32(Count);
            foreach (EntityId entity in stage.Capture())
            {
                if (!Exported(entity)) continue;
                int slot = entity.Id;
                writer.UInt64(shooters[slot].Value); writer.Int64(pauseUntil[slot]);
            }
        }

        public void LoadState(SimStateReader reader)
        {
            SimStateReader.Require(reader.Int32() == pauseTicks, "AI burst pause differs.");
            int count = reader.Count(combat.Entities.Capacity);
            Array.Clear(has, 0, has.Length);
            ulong previous = 0;
            for (int i = 0; i < count; i++)
            {
                ulong key = reader.UInt64();
                SimStateReader.Require(i == 0 || key > previous, "AI burst shooters are not in strictly increasing order.");
                previous = key;
                var shooter = new StableId(key);
                long until = reader.Int64();
                SimStateReader.Require(until >= 0, "AI burst deadline is negative.");
                SimStateReader.Require(combat.Entities.TryResolve(shooter, out EntityRef found), "AI burst shooter does not exist.");
                int slot = ((EntityId)found).Id;
                has[slot] = true; shooters[slot] = shooter; pauseUntil[slot] = until;
            }
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(StateTypeId); hash.Add(StateVersion); hash.Add(pauseTicks); hash.Add(Count);
            foreach (EntityId entity in stage.Capture())
            {
                if (!Exported(entity)) continue;
                int slot = entity.Id;
                hash.Add(shooters[slot].Value); hash.Add(pauseUntil[slot]);
            }
        }
    }
}
