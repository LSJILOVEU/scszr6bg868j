namespace WarCore.Sim
{
    internal sealed partial class TieredTimeline
    {
        private bool Recorded(EntityId entity) => entity.Id >= 0 && entity.Id < entries.Length
            && entries[entity.Id].HasIdentity && entries[entity.Id].Identity.Equals(entities.StableAt(entity.Id));
        private void SaveStable(SimStateWriter writer)
        {
            int count = 0;
            for (int i = 0; i < entities.RegisteredIdentityCount; i++)
                if (entities.LogicalLifeAt(i) != IdentityLife.Retired && Recorded(entities.LogicalEntityAt(i)))
                    count++;
            writer.Int32(count);
            for (int i = 0; i < entities.RegisteredIdentityCount; i++)
            {
                if (entities.LogicalLifeAt(i) == IdentityLife.Retired)
                    continue;
                EntityId entity = entities.LogicalEntityAt(i);
                if (!Recorded(entity))
                    continue;
                writer.Link(new EntityLink(entities.StableAt(entity.Id)));
                writer.Int64(entries[entity.Id].LastSimulatedTick);
                writer.Int32((int)entries[entity.Id].PreviousLevel);
            }
        }
        private void LoadStable(SimStateReader reader)
        {
            entries = new Entry[entities.Capacity];
            int count = reader.Count(entities.Capacity);
            ulong previous = 0;
            for (int i = 0; i < count; i++)
            {
                EntityLink link = reader.Link();
                EntityId entity = entities.ResolveLogical(link);
                long tick = reader.Int64();
                var level = (SimTierLevel)reader.Int32();
                SimStateReader.Require(link.HasValue && (i == 0 || link.Id.Value > previous) && tick >= 0
                    && level >= SimTierLevel.Full && level <= SimTierLevel.Dormant,"Invalid stable timeline.");
                entries[entity.Id] = new Entry { Generation=entity.Generation, LastSimulatedTick=tick, PreviousLevel=level, Identity=link.Id, HasIdentity=true };
                previous = link.Id.Value;
            }
        }
    }
}
