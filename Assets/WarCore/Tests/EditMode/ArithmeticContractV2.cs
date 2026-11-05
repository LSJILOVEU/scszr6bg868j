using System;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Frozen v1 inputs and numeric answers remain authoritative; only two error types changed in G25.
    internal static class ArithmeticContractV2
    {
        internal const int Version = 2;

        internal static Type Expected(Type legacy)
        {
            if (legacy == typeof(OverflowException))
                return typeof(SimOverflowException);
            if (legacy == typeof(DivideByZeroException))
                return typeof(SimDivideByZeroException);
            return legacy;
        }

        internal static Type Expected(string frozenName, bool product = true)
        {
            Type legacy;
            switch (frozenName)
            {
                case "OverflowException":
                    legacy = typeof(OverflowException);
                    break;
                case "DivideByZeroException":
                    legacy = typeof(DivideByZeroException);
                    break;
                case "ArgumentOutOfRangeException":
                    legacy = typeof(ArgumentOutOfRangeException);
                    break;
                default:
                    throw new InvalidOperationException("Unknown frozen v1 arithmetic error: " + frozenName);
            }
            return product ? Expected(legacy) : legacy;
        }
    }
}
