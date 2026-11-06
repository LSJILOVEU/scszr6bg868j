using System;
using WarCore.Sim;

namespace WarCore.Combat
{
    // Blocking (C3). A holding unit stops part or all of a melee blow that lands from inside its front arc; shots, blows from the side
    // or behind, and blows from a unit standing on it are not touched. The hold is plain state set by whoever controls the unit.
    public sealed partial class CombatSystem
    {
        private ComponentType<BlockState> blockStates;

        private void InitBlock()
        {
            if (pack.HasBlock) blockStates = Entities.Register<BlockState>(BlockState.ComponentId, new BlockStateSerializer());
        }

        // True when the unit carries a block spec.
        public bool CanBlock(EntityId unit) =>
            pack.HasBlock && Entities.TryGet(blockStates, unit, out BlockState state) && state.Spec >= 0;

        public bool IsHolding(EntityId unit) =>
            pack.HasBlock && Entities.TryGet(blockStates, unit, out BlockState state) && state.Holding;

        // Gives a unit its block spec (an index into the pack's specs), not holding.
        public void SetBlock(EntityId unit, int spec)
        {
            if (!pack.HasBlock || (uint)spec >= (uint)pack.Block.Length)
                throw new ArgumentException("The block spec index does not name a block spec of the combat pack.", nameof(spec));
            Entities.Set(blockStates, unit, new BlockState(spec));
        }

        // Starts or keeps a hold toward a horizontal direction (any length but zero; Y is ignored).
        public void Hold(EntityId unit, SimVec3 direction)
        {
            if (!CanBlock(unit)) throw new InvalidOperationException("The unit cannot block.");
            SimVec3 flat = Horizontal(direction);
            if (flat.LengthSquared.Sign == 0) throw new ArgumentException("A hold needs a direction.", nameof(direction));
            flat = flat.Normalized;
            Entities.Set(blockStates, unit, new BlockState(Entities.Get(blockStates, unit).Spec, true, flat.X.Raw, flat.Z.Raw));
        }

        public void Release(EntityId unit)
        {
            if (!CanBlock(unit)) return;
            Entities.Set(blockStates, unit, new BlockState(Entities.Get(blockStates, unit).Spec));
        }

        // Does a blow that started at `from` run into a block held by `victim`? A blow from the victim's own spot has no side and is not blocked.
        private bool TryBlock(EntityId victim, SimVec3 from, out BlockSpec spec)
        {
            spec = default;
            if (!Entities.TryGet(blockStates, victim, out BlockState state) || !state.Holding || state.Spec < 0
                || !Entities.TryGet(positions, victim, out Position at)) return false;
            SimVec3 toward = Horizontal(from - at.Value);
            if (toward.LengthSquared.Sign == 0) return false;
            spec = pack.Block[state.Spec];
            var held = new SimVec3(SimFix.FromRaw(state.DirX), default, SimFix.FromRaw(state.DirZ));
            return SimVec3.DotFix(held, toward.Normalized).Raw * 1000 >= (long)spec.CosHalfAnglePermille * SimFix.Scale;
        }
    }
}
