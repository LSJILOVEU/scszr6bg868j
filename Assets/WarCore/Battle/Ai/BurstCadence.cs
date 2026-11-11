using System;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Battle
{
    // Target loss cannot clear this state; only the shooter's identity and actual rounds affect its cadence.
    public sealed class BurstCadence : IRetainedGameplaySystem
    {
        private readonly CombatSystem combat;
        private readonly EntityStage stage;
        private readonly int burstShots, pauseTicks;
        private readonly StableId[] shooters;
        private readonly int[] shots;
        private readonly long[] lastShots;

        public BurstCadence(CombatSystem combat, int burstShots, int pauseTicks)
        {
            this.combat = combat;
            this.burstShots = burstShots;
            this.pauseTicks = pauseTicks;
            stage = new EntityStage(combat.Entities);
            int capacity = combat.Entities.Capacity;
            shooters = new StableId[capacity];
            shots = new int[capacity]; lastShots = new long[capacity];
        }

        public int Order => 15;
        public string StateTypeId => "battle.ai-burst-cadence";
        public int StateVersion => 1;

        internal bool Ready(EntityId shooter, long tick)
        {
            int slot = shooter.Id;
            StableId self = combat.Entities.StableOf(shooter);
            if (!shooters[slot].Equals(self))
            {
                shooters[slot] = self;
                Forget(slot);
            }
            if (shots[slot] == burstShots)
            {
                if (tick - lastShots[slot] < pauseTicks) return false;
                shots[slot] = 0;
            }
            return true;
        }

        internal void Fired(EntityId shooter, in Combatant unit, long tick)
        {
            int slot = shooter.Id;
            lastShots[slot] = tick;
            shots[slot] = unit.Ammo == 1 ? 0 : shots[slot] + 1;
        }

        internal void Forget(int slot)
        {
            shots[slot] = 0; lastShots[slot] = 0;
        }

        public void Step(SimContext c) { }

        private bool Exported(EntityId entity) =>
            shooters[entity.Id].Equals(combat.Entities.StableOf(entity)) && !combat.IsDead(entity);

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
            writer.Int32(burstShots); writer.Int32(pauseTicks); writer.Int32(Count);
            foreach (EntityId entity in stage.Capture())
            {
                if (!Exported(entity)) continue;
                int slot = entity.Id;
                writer.UInt64(shooters[slot].Value); writer.Int32(shots[slot]); writer.Int64(lastShots[slot]);
            }
        }

        public void LoadState(SimStateReader reader)
        {
            Array.Clear(shooters, 0, shooters.Length);
            SimStateReader.Require(reader.Int32() == burstShots, "AI cadence burst length differs.");
            SimStateReader.Require(reader.Int32() == pauseTicks, "AI cadence pause differs.");
            int count = reader.Count(combat.Entities.Capacity);
            ulong previous = 0;
            for (int i = 0; i < count; i++)
            {
                ulong key = reader.UInt64();
                SimStateReader.Require(i == 0 || key > previous, "AI cadence shooters are not in strictly increasing order.");
                previous = key;
                int fired = reader.Int32();
                long last = reader.Int64();
                SimStateReader.Require(fired >= 0 && fired <= burstShots, "AI cadence shot count is out of range.");
                SimStateReader.Require(last >= 0 && (fired == 0 || last > 0), "AI cadence last shot is invalid.");
                var shooter = new StableId(key);
                SimStateReader.Require(combat.Entities.TryResolve(shooter, out EntityRef found), "AI cadence shooter does not exist.");
                int slot = ((EntityId)found).Id;
                shooters[slot] = shooter; shots[slot] = fired; lastShots[slot] = last;
            }
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(StateTypeId); hash.Add(StateVersion); hash.Add(burstShots); hash.Add(pauseTicks); hash.Add(Count);
            foreach (EntityId entity in stage.Capture())
            {
                if (!Exported(entity)) continue;
                int slot = entity.Id;
                hash.Add(shooters[slot].Value); hash.Add(shots[slot]); hash.Add(lastShots[slot]);
            }
        }
    }
}
