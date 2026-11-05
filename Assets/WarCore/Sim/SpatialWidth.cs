using System.Diagnostics;

namespace WarCore.Sim
{
    internal static class SpatialWidth
    {
        // Every spatial factory reaches this guard before arithmetic; a larger world needs a new width proof.
        [Conditional("DEBUG")]
        internal static void AssertWorldLimit(int metres)
        {
            if (metres != 16384 || SimFix.Scale != 65536)
                throw new SimBoundaryException("Spatial arithmetic proof requires a 16384 metre world and 65536 raw units per metre.");
        }
    }
}
