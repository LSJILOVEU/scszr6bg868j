using System;
using WarCore.Sim;

namespace WarCore.Interim
{
    // Layers (worlds with floor plates): every unit has a VerticalState. A two-argument order is an order on the ground and
    // clears the target layer; an order onto a plate records the target layer and stops the unit, which does not walk onto
    // plates yet.
    public sealed partial class InterimLocomotion
    {
        private ComponentType<VerticalState> vertical;
        private bool layered;

        public void BindVertical(ComponentType<VerticalState> component) { vertical = component; layered = true; }

        public void Order(EntityId unit, SimVec3 target, int layer)
        {
            VerticalState.Checked(layer, nameof(layer));
            if (layer == 0) { Order(unit, target); return; }
            if (!layered) throw new NotSupportedException("Layer orders need a world with floor plates.");
            RequireIntent(unit);
            SetTargetLayer(unit, layer);
            Stop(unit);
        }

        private void SetTargetLayer(EntityId unit, int layer) =>
            entities.Set(vertical, unit, entities.Read(vertical, unit).WithTarget(layer));
    }
}
