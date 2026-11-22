using System;
using System.Collections.Generic;

namespace WarCore.Presentation
{
    // MAP-BL0: the stage sets the showcase host can play. "" is the main showcase (Showcase.Stages, the same object); "ward"
    // is the walled ward alone. Names are compared exactly (ordinal, case counts); anything else throws ArgumentException.
    public static class ShowcaseSets
    {
        public static IReadOnlyList<ShowcaseStage> Stages(string set)
        {
            if (string.Equals(set, "", StringComparison.Ordinal)) return Showcase.Stages;
            if (string.Equals(set, WardDemo.SetId, StringComparison.Ordinal)) return WardDemo.Stages;
            throw Unknown(set);
        }

        // The file name prefix of a set's pictures. Only the named sets have one; the main showcase keeps the host's own.
        public static string ShotPrefix(string set)
        {
            if (string.Equals(set, WardDemo.SetId, StringComparison.Ordinal)) return WardDemo.ShotPrefix;
            throw Unknown(set);
        }

        private static ArgumentException Unknown(string set) =>
            new ArgumentException("unknown stage set '" + (set ?? "null") + "'", nameof(set));
    }
}
