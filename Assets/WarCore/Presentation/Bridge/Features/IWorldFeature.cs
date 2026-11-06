using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // One installer of the world assembly. The builder runs the hooks in a fixed sequence and each hook visits the features of
    // its table in table order (see FeatureTable); a feature does only the part of the world that belongs to it.
    //   Validate            before the map is read: rules about the recipe alone
    //   Prepare             after the map is read, before the simulation exists: facts derived from the map
    //   RegisterComponents  the simulation exists, no entity does
    //   PlaceWall           one call per map wall, right after its entity was created
    //   PlaceUnit           one call per unit, right after its entity was created
    //   RegisterSystems     everything the systems depend on is built (player control, combat, crowd pass rule)
    internal interface IWorldFeature : IWorldOption
    {
        bool IsActive(WorldAssembly assembly);
        void Validate(WorldAssembly assembly);
        void Prepare(WorldAssembly assembly);
        void RegisterComponents(WorldAssembly assembly);
        void PlaceWall(WorldAssembly assembly, WcMapWall wall, EntityId entity);
        void PlaceUnit(WorldAssembly assembly, int index, EntityId unit);
        void RegisterSystems(WorldAssembly assembly);
    }

    // Every hook does nothing and the feature is on; a feature overrides what it needs.
    internal abstract class WorldFeature : IWorldFeature
    {
        public abstract string OptionId { get; }
        public virtual bool IsActive(WorldAssembly assembly) => true;
        public virtual void Validate(WorldAssembly assembly) { }
        public virtual void Prepare(WorldAssembly assembly) { }
        public virtual void RegisterComponents(WorldAssembly assembly) { }
        public virtual void PlaceWall(WorldAssembly assembly, WcMapWall wall, EntityId entity) { }
        public virtual void PlaceUnit(WorldAssembly assembly, int index, EntityId unit) { }
        public virtual void RegisterSystems(WorldAssembly assembly) { }
    }
}
