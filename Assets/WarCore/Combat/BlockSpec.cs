using System;
using WarCore.Sim;

namespace WarCore.Combat
{
    // Blocking data: a unit holding a block takes reduced damage from a melee blow that comes from inside its front arc. The arc is
    // centred on the held direction; CosHalfAnglePermille is the cosine of its half angle in thousandths (500 = 60 degrees, 0 = the
    // whole front half-plane). ReductionPermille is the share of the damage that is stopped (1000 = all of it, 0 = none).
    // Only melee is affected; shots never read this.
    public readonly struct BlockSpec : IStateHash
    {
        public readonly int CosHalfAnglePermille, ReductionPermille;

        public BlockSpec(int cosHalfAnglePermille, int reductionPermille)
        {
            if (cosHalfAnglePermille < 0 || cosHalfAnglePermille > 1000) throw new ArgumentOutOfRangeException(nameof(cosHalfAnglePermille));
            if (reductionPermille < 0 || reductionPermille > 1000) throw new ArgumentOutOfRangeException(nameof(reductionPermille));
            CosHalfAnglePermille = cosHalfAnglePermille; ReductionPermille = reductionPermille;
        }

        // Damage that gets through, in raw fixed-point; the stopped share rounds down.
        public long Through(long damageRaw) => damageRaw - damageRaw * ReductionPermille / 1000;

        public void WriteState(StateHash hash) { hash.Add(CosHalfAnglePermille); hash.Add(ReductionPermille); }
    }

    // Per-unit block state. The component exists only in worlds whose CombatPack has block specs. Spec is -1 for a unit that cannot
    // block. While Holding, (DirX, DirZ) is the unit direction (raw fixed-point) the arc is centred on; both are zero otherwise,
    // so the saved bytes of a released unit are canonical.
    public readonly struct BlockState : IStateHash
    {
        public const string ComponentId = "combat.block-state";
        public readonly int Spec;
        public readonly bool Holding;
        public readonly long DirX, DirZ;

        public BlockState(int spec, bool holding = false, long dirX = 0, long dirZ = 0)
        { Spec = spec; Holding = holding; DirX = dirX; DirZ = dirZ; }

        public void WriteState(StateHash hash) { hash.Add(Spec); hash.Add(Holding); hash.Add(DirX); hash.Add(DirZ); }
    }

    public sealed class BlockStateSerializer : IStableComponentSerializer<BlockState>
    {
        public int TypeId => 3003;
        public int Version => 1;
        public void Write(SimStateWriter writer, in BlockState value)
        {
            writer.Int32(value.Spec); writer.Bool(value.Holding); writer.Int64(value.DirX); writer.Int64(value.DirZ);
        }
        public BlockState Read(SimStateReader reader)
        {
            int spec = reader.Int32(); bool holding = reader.Bool();
            return new BlockState(spec, holding, reader.Int64(), reader.Int64());
        }
    }
}
