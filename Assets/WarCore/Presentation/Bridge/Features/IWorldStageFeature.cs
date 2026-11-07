using WarCore.Sim;

namespace WarCore.Presentation
{
    // Two hooks on top of IWorldFeature, for features whose systems hang on objects other features build. IWorldFeature itself
    // does not change: a feature or extension that implements only it is never asked about these hooks and gets exactly the six
    // it always got. The builder runs each of these after the built-in table of the hook, then the extensions that implement
    // this interface, in the order they were added.
    //   CompleteComponents  right after RegisterComponents of every feature and extension (the combat component)
    //   CompleteUnit        right after PlaceUnit of every feature and extension, per unit (the combat binding and state, which
    //                       so replace whatever an extension wrote there)
    //   Connect        every entity exists, before RegisterSystems: the objects the systems are made from (player control,
    //                  then combat, then the crowd pass rule)
    //   RegisterRules  after RegisterSystems, so the locomotion exists: the systems built on movement (match, block AI, player)
    internal interface IWorldStageFeature : IWorldFeature
    {
        void CompleteComponents(WorldAssembly assembly);
        void CompleteUnit(WorldAssembly assembly, int index, EntityId unit);
        void Connect(WorldAssembly assembly);
        void RegisterRules(WorldAssembly assembly);
    }

    // Every hook does nothing and the feature is on; a feature overrides what it needs.
    internal abstract class WorldStageFeature : WorldFeature, IWorldStageFeature
    {
        public virtual void CompleteComponents(WorldAssembly assembly) { }
        public virtual void CompleteUnit(WorldAssembly assembly, int index, EntityId unit) { }
        public virtual void Connect(WorldAssembly assembly) { }
        public virtual void RegisterRules(WorldAssembly assembly) { }
    }
}
