using System;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Battle
{
    public sealed class BurstCadence : IRetainedGameplaySystem
    {
        private readonly CombatSystem combat;
        private readonly EntityStage stage;
        private readonly StableId[] shooters;
        private readonly long[] deadlines;

        public BurstCadence(CombatSystem combat)
        {
            this.combat = combat;
            stage = new EntityStage(combat.Entities);
            shooters = new StableId[combat.Entities.Capacity];
            deadlines = new long[combat.Entities.Capacity];
        }

        public int Order => 15;
        public string StateTypeId => "battle.burst-cadence";
        public int StateVersion => 1;

        internal bool Paused(EntityId shooter, long tick) =>
            shooters[shooter.Id].Equals(combat.Entities.StableOf(shooter)) && tick < deadlines[shooter.Id];

        internal void Pause(EntityId shooter, long deadline)
        {
            shooters[shooter.Id] = combat.Entities.StableOf(shooter);
            deadlines[shooter.Id] = deadline;
        }

        public void Step(SimContext c) { }

        private bool Exported(EntityId entity) => deadlines[entity.Id] > 0
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
            writer.Int32(Count);
            foreach (EntityId entity in stage.Capture())
            {
                if (!Exported(entity)) continue;
                writer.UInt64(shooters[entity.Id].Value); writer.Int64(deadlines[entity.Id]);
            }
        }

        public void LoadState(SimStateReader reader)
        {
            Array.Clear(deadlines, 0, deadlines.Length);
            int count = reader.Count(combat.Entities.Capacity);
            ulong previous = 0;
            for (int i = 0; i < count; i++)
            {
                ulong key = reader.UInt64();
                SimStateReader.Require(i == 0 || key > previous, "AI burst shooters are not in strictly increasing order.");
                previous = key;
                long deadline = reader.Int64();
                SimStateReader.Require(deadline > 0, "AI burst deadline is not positive.");
                SimStateReader.Require(combat.Entities.TryResolve(new StableId(key), out EntityRef found), "AI burst shooter does not exist.");
                int slot = ((EntityId)found).Id;
                shooters[slot] = new StableId(key); deadlines[slot] = deadline;
            }
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(StateTypeId); hash.Add(StateVersion); hash.Add(Count);
            foreach (EntityId entity in stage.Capture())
            {
                if (!Exported(entity)) continue;
                hash.Add(shooters[entity.Id].Value); hash.Add(deadlines[entity.Id]);
            }
        }
    }
}
