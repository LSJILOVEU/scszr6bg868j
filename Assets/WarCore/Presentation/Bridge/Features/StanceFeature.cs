using WarCore.Combat;
using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // STANCE-1: stand, crouch, prone. On only when the recipe's combat pack has stances, so every other world keeps its exact
    // components, systems, snapshots and hashes. It registers the posture component (the spatial index takes its geometry, so a
    // unit's capsule follows its stance), makes the world's action gate and the stance system right after combat, and last of
    // all puts the speed line in front of the locomotion's motion model and the player's step, with the stance as its gear.
    internal sealed class StanceFeature : WorldStageFeature
    {
        internal static readonly StanceFeature Instance = new StanceFeature();

        public override string OptionId => "world.stance";

        public override bool IsActive(WorldAssembly a) => a.Recipe.Combat != null && a.Recipe.Combat.HasStances;

        public override void RegisterComponents(WorldAssembly a) => a.Postures = StanceSystem.RegisterPostures(a.Sim.Entities, a.Recipe.Combat);

        public override void Connect(WorldAssembly a)
        {
            a.Gate = new ActionGate();
            a.Stances = new StanceSystem(a.Sim, a.Postures, a.Positions, a.Colliders, a.Combat, a.Gate);
            a.Sim.RegisterGameplay("combat.stance", a.Stances, SimSystemPhase.Requests);
        }

        public override void RegisterRules(WorldAssembly a)
        {
            var line = new SpeedLine(a.Locomotion.Motion);
            line.AddGear(new StanceGear(a.Stances));
            a.Locomotion.BindMotion(line);
            a.PlayerStep?.BindSpeed(line);
            if (DashFeature.Instance.IsActive(a)) a.Gate.Add(new DashingLimit(a.Locomotion));
        }

        // The stance's speed factor as a gear of the speed line.
        private sealed class StanceGear : ISpeedGear
        {
            private readonly StanceSystem stances;
            internal StanceGear(StanceSystem stances) { this.stances = stances; }
            public int Permille(EntityId unit) => stances.SpeedPermille(unit);
        }
    }

    // STANCE-1: a stop-gap entry of the action gate for the dash until the dash registers its own: a dashing unit may not start
    // another dash, change stance, traverse, throw or interact. It reads the dash state through the locomotion (saved state).
    internal sealed class DashingLimit : IActionLimit
    {
        internal const UnitAction WhileDashing = UnitAction.Dash | UnitAction.Stance | UnitAction.Traverse | UnitAction.Throw | UnitAction.Interact;
        private readonly InterimLocomotion locomotion;
        internal DashingLimit(InterimLocomotion locomotion) { this.locomotion = locomotion; }
        public UnitAction Denied(EntityId unit) => locomotion.IsDashing(unit) ? WhileDashing : UnitAction.None;
    }
}
