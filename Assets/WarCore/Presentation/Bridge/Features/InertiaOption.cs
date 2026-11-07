using WarCore.Content;
using System;
using WarCore.Interim;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Presentation
{
    // Acceleration and braking for AI units, in mm/s^2 (each 0, which leaves that part off, or 100 to 1 000 000). The recipe
    // must also open WithMotion(). With both rates at zero the world is the same as without the option. Otherwise every unit
    // carries a motion state, its motion limits take the two rates, and the locomotion asks the inertia model for each step.
    // The option is an extension of the world assembly: its hooks run after the built-in ones.
    public sealed class InertiaOption : IWorldFeature
    {
        private const long MinRateMmPerS2 = 100, MaxRateMmPerS2 = 1000000;
        // mm/s^2 to metres per tick squared: 1000 mm per metre, 30 ticks per second.
        private const long PerTickSquared = 1000L * SimClock.TickRate * SimClock.TickRate;

        private readonly long accelMmPerS2, decelMmPerS2;

        public InertiaOption(long accelMmPerS2, long decelMmPerS2)
        {
            Check(accelMmPerS2, nameof(accelMmPerS2));
            Check(decelMmPerS2, nameof(decelMmPerS2));
            this.accelMmPerS2 = accelMmPerS2;
            this.decelMmPerS2 = decelMmPerS2;
        }

        private static void Check(long value, string name)
        {
            if (value != 0 && (value < MinRateMmPerS2 || value > MaxRateMmPerS2))
                throw new ArgumentOutOfRangeException(name, "A rate is 0 or between 100 and 1000000 mm/s^2.");
        }

        public string OptionId => "world.inertia";

        private bool Off => accelMmPerS2 == 0 && decelMmPerS2 == 0;
        private static SimFix PerTick(long mmPerS2) => SimFix.Ratio(mmPerS2, PerTickSquared);

        bool IWorldFeature.IsActive(WorldAssembly a) => true;

        void IWorldFeature.Validate(WorldAssembly a)
        {
            if (!a.Recipe.Motion) throw new ArgumentException("The inertia option needs the motion component (WithMotion).");
        }

        void IWorldFeature.Prepare(WorldAssembly a) { }

        void IWorldFeature.RegisterComponents(WorldAssembly a)
        {
            if (Off) return;
            a.Sim.Entities.Register<MotionState>(MotionState.ComponentId, new MotionStateSerializer());
        }

        void IWorldFeature.PlaceWall(WorldAssembly a, WcMapWall wall, EntityId entity) { }

        // The built-in motion row wrote (speed, 0, 0, 0) first; this one adds the two rates.
        void IWorldFeature.PlaceUnit(WorldAssembly a, int index, EntityId unit)
        {
            if (Off) return;
            EntityStore entities = a.Sim.Entities;
            entities.Set(a.MotionLimits, unit, new MotionLimits(WorldAssembly.SpawnSpeed(a.Recipe.Units[index]),
                PerTick(accelMmPerS2), PerTick(decelMmPerS2), default));
            entities.Set(entities.Resolve<MotionState>(), unit, MotionState.Initial);
        }

        void IWorldFeature.RegisterSystems(WorldAssembly a)
        {
            if (Off) return;
            Simulation sim = a.Sim;
            a.Locomotion.BindMotion(new InertiaMotion(sim.Entities, a.Positions, a.Intents, a.MotionLimits,
                sim.Entities.Resolve<MotionState>(), sim.Clock));
        }
    }
}
