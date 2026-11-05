using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace WarCore.Tests
{
    internal static class RetainedEntrypointSourceRules
    {
        internal static List<string> Violations(string source, string path)
        {
            string relative = path.Replace('\\','/').TrimStart('/');
            int root = relative.IndexOf("Assets/WarCore/",StringComparison.Ordinal);
            if (root >= 0)
                relative = relative.Substring(root+"Assets/WarCore/".Length);
            var result = new List<string>();
            if (relative.StartsWith("Tests/",StringComparison.Ordinal) || relative.StartsWith("Verification/",StringComparison.Ordinal))
                return result;
            string text = SourceRules.StripComments(source);
            // Keep compatibility declarations, not their bodies or other calls in the same file.
            if (relative == "Sim/StableSimState.cs" || relative == "Sim/StableSchedulerState.cs")
                text = Regex.Replace(text,@"\b(?:public|internal)\s+ulong\s+StateHashStable\s*\(\s*\)","");
            if (relative == "Sim/StableSimState.cs")
            {
                text = Regex.Replace(text,@"\b(?:public|internal)\s+byte\[\]\s+SaveStableSnapshot\s*\(\s*\)","");
                text = Regex.Replace(text,@"\b(?:public|internal)\s+void\s+RestoreStableSnapshot\s*\(\s*byte\[\]\s+snapshot\s*,\s*bool\s+reverseAllocation\s*=\s*false\s*,\s*IStableRestoreObserver\s+observer\s*=\s*null\s*\)","");
            }
            foreach (Match call in Regex.Matches(text,@"\b(?:StateHashStable|SaveStableSnapshot|RestoreStableSnapshot)\s*\("))
                result.Add("Product code must use retained entrypoints: "+call.Value);
            string names = "Sim|Simulation";
            foreach (Match alias in Regex.Matches(text,@"\busing\s+(\w+)\s*=\s*(?:global\s*::\s*)?WarCore\s*\.\s*Sim\s*\.\s*Sim\s*;"))
                names += "|"+Regex.Escape(alias.Groups[1].Value);
            // Balanced parentheses include nested SimConfig calls and multiline named arguments.
            string pattern = @"\bnew\s+(?:global\s*::\s*)?(?:\w+\s*\.\s*)*(?:"+names+@")\s*\((?>[^()]|(?<depth>\()|(?<-depth>\)))*(?(depth)(?!))\)";
            foreach (Match constructor in Regex.Matches(text,pattern))
            {
                // The one retained factory must still pass a non-null RetainedWorldOptions.
                string compact = Regex.Replace(constructor.Value,@"\s+","");
                if (relative == "Sim/RetainedWorld.cs" && compact ==
                    "newSim(seed,config:config,identityMode:IdentityMode.StableWorld,stableContent:content,retainedWorld:options??newRetainedWorldOptions())")
                    continue;
                if (relative == "Sim/PlacedWorld.cs" && compact ==
                    "newSim(seed,mode:mode,modeId:modeId,config:config,identityMode:IdentityMode.StableWorld,stableContent:content,retainedWorld:options??newRetainedWorldOptions(),placedCatalog:catalog,initialPlaced:initial)")
                    continue;
                result.Add("Product construction must use Sim.CreateRetainedWorld.");
            }
            if (Regex.IsMatch(text,@"\b(?:"+names+@")\s+\w+\s*=\s*new\s*\(")
                || Regex.IsMatch(text,@"\b(?:"+names+@")\s+\w+\s*\([^;{}]*\)\s*=>\s*new\s*\("))
                result.Add("Target typed construction must use the retained factory.");
            string entityText = text;
            if (relative == "Sim/Sim.cs")
                entityText = entityText.Replace("new EntityStore(Config.EntityCapacity, identityMode,\n                retainedWorld == null ? identityCapacity : retainedWorld.IdentityCapacity,\n                retainedWorld == null ? retiredRetentionTicks : retainedWorld.RetentionTicks,retainedWorld != null)", "");
            if (Regex.IsMatch(entityText,@"\bnew\s+(?:[\w]+\s*\.\s*)*EntityStore\s*\("))
                result.Add("Product code cannot construct EntityStore directly.");
            if (!relative.StartsWith("Sim/",StringComparison.Ordinal) && Regex.IsMatch(text,@"\bclass\s+\w+[^{}]*:\s*[^{}]*\b(?:ISimSystem|IMatchMode)\b")
                && !Regex.IsMatch(text,@"\bIRetainedGameplay(?:System|Mode)\b"))
                result.Add("Gameplay must declare its retained contract.");
            return result;
        }
    }
}
