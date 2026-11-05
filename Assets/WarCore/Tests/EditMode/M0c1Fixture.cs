using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // The same lifecycle program consumes both content packages. No genre branch in Sim.
    internal readonly struct M0c1Package
    {
        internal readonly string Name;
        internal readonly string[] Roles;
        internal readonly ulong World;
        internal readonly ulong Kind;
        internal M0c1Package(string name, string[] roles, ulong world, ulong kind)
        { Name=name; Roles=roles; World=world; Kind=kind; }
        internal static M0c1Package Ranged => new M0c1Package("ranged",new[]{"rifle","grenade","armor","healing"},701,9);
        internal static M0c1Package Melee => new M0c1Package("melee",new[]{"chakra","cooldown","dash","status"},702,19);
        internal StableContentManifest Manifest() => new StableContentManifest(World,
            new StableContentKey(Roles[0],Kind),new StableContentKey(Roles[1],Kind+1),
            new StableContentKey(Roles[2],Kind+2),new StableContentKey(Roles[3],Kind+3));
    }
    internal sealed class M0c1Lifecycle : IRetainedGameplaySystem
    {
        internal readonly StableEntityKey Parent;
        internal readonly ulong Kind;
        internal readonly int Period;
        internal readonly int DestroyOffset;
        internal bool Auto;
        internal bool Restore;
        internal bool Destroy;
        internal bool Recreate;
        internal bool Spawn;
        internal bool Task;
        internal M0c1Lifecycle(StableEntityKey parent, ulong kind = 9, int period = 40, int destroyOffset = 3)
        { Parent=parent; Kind=kind; Period=period; DestroyOffset=destroyOffset; }
        public int Order => 10;
        public string StateTypeId => "test.m0c1.lifecycle";
        public int StateVersion => 1;
        public void Step(SimContext context)
        {
            long phase = context.Clock.Tick % Period;
            bool restore = Restore || (Auto && phase == 0);
            bool create = Recreate || (Auto && phase == 0);
            if (Destroy)
                context.Entities.Destroy(context.Entities.Resolve(new EntityLink(Parent.Id)));
            if (restore)
                context.RestorePlaced(Parent);
            if (create)
                context.Entities.Create(Parent);
            if (Auto && phase == DestroyOffset)
                context.Entities.Destroy(context.Entities.Resolve(new EntityLink(Parent.Id)));
            if (Spawn || (Auto && phase == 1))
            {
                context.Owner.Spawns.Enqueue(new EntityLink(Parent.Id),new StableRequestKey(1,0,Parent.Id,0,0,Kind));
                context.Scheduler.ScheduleFor(context.Clock.Tick+2000,0,Parent.Id,Kind,new StableSimEvent(new EventTypeId(100)));
            }
            if (Task)
                context.Scheduler.ScheduleFor(context.Clock.Tick+2000,0,Parent.Id,Kind,new StableSimEvent(new EventTypeId(100)));
            Destroy=false; Restore=false; Recreate=false; Spawn=false; Task=false;
        }
        public void SaveState(SimStateWriter writer)
        {
            writer.UInt64(Parent.First); writer.UInt64(Parent.Second); writer.UInt64(Kind);
            writer.Int32(Period); writer.Int32(DestroyOffset);
            writer.Bool(Destroy); writer.Bool(Auto); writer.Bool(Restore); writer.Bool(Recreate); writer.Bool(Spawn); writer.Bool(Task);
        }
        public void LoadState(SimStateReader reader)
        {
            SimStateReader.Require(reader.UInt64()==Parent.First && reader.UInt64()==Parent.Second && reader.UInt64()==Kind
                && reader.Int32()==Period && reader.Int32()==DestroyOffset,"Lifecycle configuration mismatch.");
            Destroy=reader.Bool(); Auto=reader.Bool(); Restore=reader.Bool(); Recreate=reader.Bool(); Spawn=reader.Bool(); Task=reader.Bool();
        }
        public void WriteState(StateHash hash) { }
    }
    internal static class M0c1Fixture
    {
        internal static StableEntityKey Parent => StableEntityKey.Placed(7,11);
        internal static Simulation Create(out M0c1Lifecycle system, long retention = 1, byte policy = 1,
            InitialPlacedState initial = null, M0c1Package? package = null, int count = 1)
        {
            var data = package ?? M0c1Package.Ranged;
            var content = data.Manifest();
            var entries = new PlacedCatalogEntry[count];
            for (int i=0;i<count;i++) entries[i]=new PlacedCatalogEntry(7,(ulong)i+11,policy);
            var catalog = new PlacedCatalog(content,entries);
            var sim = Simulation.CreateRetainedWorld(41,catalog,new SimConfig(entityCapacity:64,timerCapacity:64),
                new RetainedWorldOptions(retention,128),content,initial);
            system = new M0c1Lifecycle(Parent,data.Kind);
            sim.RegisterGameplay("lifecycle",system);
            return sim;
        }
    }
}
