using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace WarCore.Tests
{
    internal static class StableIdentitySourceRules
    {
        internal static List<string> Violations(string source, string path)
        {
            string text = SourceRules.StripComments(source);
            var result = new List<string>();
            if (Regex.IsMatch(text,@"\.\s*Create\s*\(\s*(?:default(?:\s*\([^)]*\))?)?\s*\)"))
                result.Add("Default entity creation is forbidden in product call sites.");
            if (!path.Replace('\\','/').EndsWith("Sim/Sim.cs"))
                foreach (Match constructor in Regex.Matches(text,@"new\s+(?:EntityStore|Sim|Simulation)\s*\([^;]+"))
                    if (!constructor.Value.Contains("IdentityMode.StableWorld"))
                        result.Add("Content must explicitly select StableWorld.");
            return result;
        }
    }
}
