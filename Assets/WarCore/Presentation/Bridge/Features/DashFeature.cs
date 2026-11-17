using System;
using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // The per-unit dash. On only when a recipe unit has a dash (from its unit type's dash fields or UnitSpawn.WithDash), so every
    // other world keeps its exact snapshots and hashes. Each such unit carries its spec and its dash state; the locomotion takes the
    // steps. Nothing asks for a dash yet: the trigger is InterimLocomotion.TryDash.
    internal sealed class DashFeature : WorldFeature
    {
        internal static readonly DashFeature Instance = new DashFeature();

        public override string OptionId => "world.dash";

        public override bool IsActive(WorldAssembly a)
        {
            foreach (UnitSpawn spawn in a.Recipe.Units)
                if (spawn.HasDash) return true;
            return false;
        }

        public override void Validate(WorldAssembly a)
        {
            if (a.Recipe.Mover != MoverKind.InterimCrowd) throw new ArgumentException("A unit with a dash needs the crowd mover.");
        }

        public override void RegisterComponents(WorldAssembly a)
        {
            a.Sim.Entities.Register<DashSpec>(DashSpec.ComponentId, new DashSpecSerializer());
            a.Sim.Entities.Register<DashState>(DashState.ComponentId, new DashStateSerializer());
        }

        public override void PlaceUnit(WorldAssembly a, int index, EntityId unit)
        {
            UnitSpawn spawn = a.Recipe.Units[index];
            if (!spawn.HasDash) return;
            EntityStore entities = a.Sim.Entities;
            entities.Set(entities.Resolve<DashSpec>(), unit,
                new DashSpec(SimFix.Ratio(spawn.DashDistanceMm, 1000), spawn.DashTicks, spawn.DashCooldownTicks));
            entities.Set(entities.Resolve<DashState>(), unit, DashState.Initial);
        }

        public override void RegisterSystems(WorldAssembly a) =>
            a.Locomotion.BindDash(a.Sim.Entities.Resolve<DashSpec>(), a.Sim.Entities.Resolve<DashState>(), a.Sim.Clock);
    }
}
