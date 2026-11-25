using WarCore.Sim;

namespace WarCore.Interim
{
    /// <summary>Optional crowd rule: may a moving unit pass through a stationary one instead of being blocked by it?
    /// The movement layer knows nothing about teams or combat; the caller decides.
    /// Contract: read-only, deterministic and allocation-free. It must not draw random numbers, write components,
    /// depend on how often it is called, or use the physical entity index as input. The answer is directional:
    /// MayPass(a, b) says whether a may pass b. Digest identifies the algorithm and its static configuration and
    /// must stay fixed for the life of a world.</summary>
    public interface ICrowdPass
    {
        ulong Digest { get; }
        bool MayPass(EntityId mover, EntityId idle);
        // RC2-1: the same question when the other unit is walking. A rule that says nothing keeps the old answer: no.
        bool MayPassMoving(EntityId mover, EntityId other) => false;
    }
}
